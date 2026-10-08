using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Collections;

namespace PulseArena
{
    [RequireComponent(typeof(CharacterController),typeof(NetworkObject))]
    public class ArenaPlayer : NetworkBehaviour
    {
        public static ArenaPlayer Local;
        public readonly NetworkVariable<int> Health=new NetworkVariable<int>(100);
        public readonly NetworkVariable<int> Ammo=new NetworkVariable<int>(24);
        public readonly NetworkVariable<int> Kills=new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Deaths=new NetworkVariable<int>(0);
        public readonly NetworkVariable<FixedString32Bytes> CallSign=new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<bool> Bot=new NetworkVariable<bool>(false);
        public readonly NetworkVariable<double> ReloadUntil=new NetworkVariable<double>(0);
        public readonly NetworkVariable<double> RespawnAt=new NetworkVariable<double>(0);
        public bool InitializeAsBot;
        public int BotIndex;
        public Transform Model {get;private set;}
        CharacterController controller;
        Transform leftLeg,rightLeg;
        Vector2 move;
        float yaw,pitch,vertical,sendAt,nextShot,lastInput,botTurn,walkPhase;
        bool fire,sprint;
        double immuneUntil;
        Vector3 previous,botGoal;
        int spawnCount;
        public double Now => NetworkManager ? NetworkManager.ServerTime.Time : Time.time;
        public bool Alive => Health.Value>0;
        public bool Reloading => ReloadUntil.Value>Now;

        void Awake(){controller=GetComponent<CharacterController>();}
        public override void OnNetworkSpawn()
        {
            if(IsServer)
            {
                Bot.Value=InitializeAsBot;
                CallSign.Value=new FixedString32Bytes(InitializeAsBot?new[]{"VECTOR","ION","NOVA"}[BotIndex%3]:"PILOT "+OwnerClientId);
                ResetForRound();
            }
            Model=ArenaArt.Robot(transform,Bot.Value?"Amber":"Cyan");
            leftLeg=Model.Find("LeftLeg");rightLeg=Model.Find("RightLeg");previous=transform.position;
            if(IsOwner&&!Bot.Value)
            {
                Local=this;Model.gameObject.SetActive(false);
                SetNameServerRpc(PulseApp.Instance.PlayerName);
                PulseApp.Instance.AttachCamera(this);
            }
            Health.OnValueChanged+=OnHealth;
        }
        public override void OnNetworkDespawn(){Health.OnValueChanged-=OnHealth;if(Local==this)Local=null;}
        void OnHealth(int before,int after)
        {
            if(Local!=this)return;
            if(after<before){PulseApp.Instance.DamageFlash=1;PulseAudio.Instance.Play("hit");}
            else if(before==0&&after>0)PulseAudio.Instance.Play("respawn");
        }
        [ServerRpc]
        void SetNameServerRpc(string name)
        {
            if(string.IsNullOrWhiteSpace(name))name="PILOT";
            var clean=System.Text.RegularExpressions.Regex.Replace(name,"[^a-zA-Z0-9 _-]","");
            CallSign.Value=new FixedString32Bytes(clean.Substring(0,Mathf.Min(clean.Length,16)));
        }
        void Update()
        {
            if(!IsSpawned)return;
            bool active=ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==1;
            if(controller.enabled!=Alive)controller.enabled=Alive;
            if(Model&&Local!=this)Model.gameObject.SetActive(Alive);
            if(IsOwner&&!Bot.Value&&!PulseSmoke.SuppressPlayerInput&&Time.unscaledTime>=sendAt)
            {
                sendAt=Time.unscaledTime+.033f;
                var input=PulseApp.Instance.Controls;
                bool enabled=active&&Alive&&!PulseApp.Instance.Paused;
                InputServerRpc(enabled?input.Move:Vector2.zero,input.Yaw,input.Pitch,enabled&&input.Fire,enabled&&input.Sprint);
            }
            if(IsServer)
            {
                if(!active)return;
                if(!Alive){if(Now>=RespawnAt.Value)Respawn();return;}
                if(ReloadUntil.Value>0&&Now>=ReloadUntil.Value){Ammo.Value=24;ReloadUntil.Value=0;}
                if(Bot.Value)ThinkBot();else if(Time.unscaledTime-lastInput>.35f){move=Vector2.zero;fire=false;}
                transform.rotation=Quaternion.Euler(0,yaw,0);
                Vector3 direction=transform.right*move.x+transform.forward*move.y;
                vertical=controller.isGrounded?-2:Mathf.Max(vertical-22*Time.deltaTime,-30);
                controller.Move((direction*(sprint?7.8f:5.4f)+Vector3.up*vertical)*Time.deltaTime);
                if(fire&&!sprint)TryShoot();
                if(Ammo.Value==0&&!Reloading)BeginReload();
            }
            if(Model&&Local!=this)
            {
                float speed=(transform.position-previous).magnitude/Mathf.Max(Time.deltaTime,.001f);
                walkPhase+=Time.deltaTime*Mathf.Min(speed,8)*2.2f;
                float angle=Mathf.Sin(walkPhase)*Mathf.Min(speed*6,30);
                leftLeg.localRotation=Quaternion.Euler(angle,0,0);rightLeg.localRotation=Quaternion.Euler(-angle,0,0);
                Model.localPosition=Vector3.up*(Mathf.Abs(Mathf.Sin(walkPhase))*.035f);
                previous=transform.position;
            }
        }
        [ServerRpc(Delivery=RpcDelivery.Unreliable)]
        void InputServerRpc(Vector2 value,float y,float p,bool shooting,bool running)
        {
            if(Bot.Value||!Finite(value.x)||!Finite(value.y)||!Finite(y)||!Finite(p))return;
            move=Vector2.ClampMagnitude(value,1);yaw=Mathf.Repeat(y,360);pitch=Mathf.Clamp(p,-75,75);fire=shooting;sprint=running;lastInput=Time.unscaledTime;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public void TestInput(Vector2 v,float y,float p,bool f){if(Debug.isDebugBuild||Application.isEditor)InputServerRpc(v,y,p,f,false);}
        [ServerRpc] public void ReloadServerRpc(){if(Alive&&ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==1)BeginReload();}
        void BeginReload(){if(Reloading||Ammo.Value==24)return;ReloadUntil.Value=Now+1.45;}
        void TryShoot()
        {
            if(Reloading||Ammo.Value<=0||Time.time<nextShot)return;
            nextShot=Time.time+.14f;Ammo.Value--;
            Vector3 origin=transform.position+Vector3.up*1.65f;
            Vector3 direction=Quaternion.Euler(pitch,yaw,0)*Vector3.forward;
            var end=origin+direction*90;bool hitPlayer=false,eliminated=false;
            if(Physics.Raycast(origin,direction,out var hit,90,~0,QueryTriggerInteraction.Ignore))
            {
                end=hit.point;var target=hit.collider.GetComponentInParent<ArenaPlayer>();
                if(target&&target!=this&&target.Alive&&Now>target.immuneUntil)
                {hitPlayer=true;eliminated=target.ReceiveDamage(25,this);}
            }
            ShotClientRpc(origin+direction*.65f,end,hitPlayer,eliminated);
        }
        [ClientRpc]
        void ShotClientRpc(Vector3 from,Vector3 to,bool hit,bool eliminated)
        {
            PulseEffect.Trace(from,to);PulseEffect.Impact(to,hit);
            if(Local==this)
            {
                PulseApp.Instance.Recoil=1;PulseAudio.Instance.Play("shot",.6f);
                if(hit)PulseApp.Instance.HitMarker=1;
                if(eliminated){PulseAudio.Instance.Play("kill");PulseApp.Instance.Announce("ELIMINATION  +100");}
            }
            else if(Camera.main)PulseAudio.Instance.Play("shot",Mathf.Clamp01(1-Vector3.Distance(Camera.main.transform.position,from)/45)*.22f);
        }
        public bool ReceiveDamage(int amount,ArenaPlayer attacker)
        {
            if(!IsServer||!Alive||Now<=immuneUntil)return false;
            Health.Value=Mathf.Max(0,Health.Value-amount);
            if(Health.Value>0)return false;
            Deaths.Value++;attacker.Kills.Value++;RespawnAt.Value=Now+3;move=Vector2.zero;fire=false;ReloadUntil.Value=0;
            ArenaMatch.Instance.CheckWinner();return true;
        }
        public void ResetForRound()
        {if(!IsServer)return;Kills.Value=0;Deaths.Value=0;Respawn();}
        void Respawn()
        {
            int slot=Bot.Value?BotIndex+1:(int)OwnerClientId;
            Vector3 spot=ArenaArt.Spawn(slot+spawnCount++);
            controller.enabled=false;transform.position=spot;transform.rotation=Quaternion.LookRotation(new Vector3(-spot.x,0,-spot.z));controller.enabled=true;
            yaw=transform.eulerAngles.y;pitch=0;vertical=0;move=Vector2.zero;fire=false;
            Health.Value=100;Ammo.Value=24;ReloadUntil.Value=0;RespawnAt.Value=0;immuneUntil=Now+1.5;
            GetComponent<NetworkTransform>().Teleport(spot,transform.rotation,Vector3.one);
            RespawnClientRpc(yaw);
        }
        [ClientRpc]void RespawnClientRpc(float facing){if(IsOwner&&!Bot.Value&&PulseApp.Instance){PulseApp.Instance.Controls.Yaw=facing;PulseApp.Instance.Controls.Pitch=0;}}
        void ThinkBot()
        {
            ArenaPlayer target=null;float nearest=900;
            foreach(var p in FindObjectsByType<ArenaPlayer>(FindObjectsSortMode.None))
            {
                if(p==this||!p.Alive)continue;var delta=p.transform.position-transform.position;
                if(delta.sqrMagnitude<nearest&&Physics.Raycast(transform.position+Vector3.up*1.65f,delta.normalized,out var hit,30)&&hit.collider.GetComponentInParent<ArenaPlayer>()==p){nearest=delta.sqrMagnitude;target=p;}
            }
            if(target)
            {
                var d=target.transform.position-transform.position;yaw=Mathf.MoveTowardsAngle(yaw,Quaternion.LookRotation(d).eulerAngles.y,160*Time.deltaTime);pitch=Mathf.Sin(Time.time*2+BotIndex)*2;
                move=new Vector2(Mathf.Sin(Time.time+BotIndex)*.6f,nearest>100?.7f:0);fire=Mathf.Abs(Mathf.DeltaAngle(yaw,Quaternion.LookRotation(d).eulerAngles.y))<5&&Mathf.Sin(Time.time*1.7f+BotIndex)>.1f;sprint=false;
            }
            else
            {
                fire=false;pitch=0;
                if(Time.time>botTurn||Vector3.Distance(transform.position,botGoal)<2)
                {botTurn=Time.time+4;botGoal=new Vector3(Random.Range(-20f,20f),.1f,Random.Range(-20f,20f));}
                var delta=botGoal-transform.position;delta.y=0;if(delta.sqrMagnitude>.1f)yaw=Mathf.MoveTowardsAngle(yaw,Quaternion.LookRotation(delta).eulerAngles.y,180*Time.deltaTime);
                move=Vector2.up*.7f;
                if(Physics.Raycast(transform.position+Vector3.up,transform.forward,1.4f)){yaw+=90*Time.deltaTime*2;move=new Vector2(.6f,.3f);botTurn=0;}
            }
        }
    }
}
