using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace PulseArena
{
    public class PulseApp:MonoBehaviour
    {
        public static PulseApp Instance;
        public NetworkManager Network;
        public GameObject MatchPrefab,PlayerPrefab;
        public Camera View;
        public PulseInput Controls {get;private set;}
        public PulseUI UI {get;private set;}
        public string PlayerName="PILOT",RoomCode="",Status="",Notice="";
        public bool Busy,Paused,Practice;
        public float Recoil,DamageFlash,HitMarker;
        /// Camera shake multiplier (0 = off). Exposed for the settings screen.
        public static float ShakeScale=1f;
        /// True only while the round is live (phase 1).
        public bool IsPlaying=>ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==1;
        /// True during the pre-round countdown (phase 3) and while playing (phase 1): camera/HUD are active.
        public bool InRound{get{if(!ArenaMatch.Instance)return false;int phase=ArenaMatch.Instance.Phase.Value;return phase==1||phase==3;}}

        // First-person weapons: one model per WeaponId, only the shown one is active.
        Transform[] guns,muzzles;
        int shownWeapon;
        float lowerBlend,sprintBlend,bobAmount,bobTime;
        // Camera shake state.
        float trauma,hitRoll,hitPitch;
        const float GunDepth=.55f;
        const float GunViewportX=.70f,GunViewportY=.17f;
        static readonly float[] GunScale={.5f,.5f};
        static readonly float[] RecoilKick={1f,2.2f};

        // Gun-feel springs (visual only): camera punch, view-model kick, FOV punch, look sway.
        float punchPitch,punchPitchV,punchYaw,punchYawV,gunKick,gunKickV,fovPunch,fovPunchV,baseFov=76f;
        float swayX,swayY,lastYaw,lastPitch,hitstopUntil;
        bool haveLook;

        float noticeUntil;
        bool leaving;
        void Awake()
        {
            Instance=this;Application.targetFrameRate=60;
            PlayerName=PlayerPrefs.GetString("pilot","PILOT");
            Controls=gameObject.AddComponent<PulseInput>();gameObject.AddComponent<PulseAudio>();gameObject.AddComponent<PulseEffects>();
            ArenaArt.BuildArena(new GameObject("Reactor 07 | Arena").transform);
            BuildGuns();
            UI=gameObject.AddComponent<PulseUI>();
            Network.OnServerStarted+=ServerStarted;Network.OnClientDisconnectCallback+=Disconnected;
            Network.NetworkConfig.ConnectionApproval=true;Network.ConnectionApprovalCallback+=Approve;
        }
        void BuildGuns()
        {
            int count=WeaponDef.All.Length;
            guns=new Transform[count];muzzles=new Transform[count];
            for(int i=0;i<count;i++)
            {
                var gun=ArenaArt.Weapon(View.transform,(WeaponId)i);
                gun.localScale=Vector3.one*GunScale[Mathf.Min(i,GunScale.Length-1)];
                gun.gameObject.SetActive(false);
                guns[i]=gun;
                muzzles[i]=gun.Find("Muzzle");
                if(!muzzles[i])
                    foreach(var t in gun.GetComponentsInChildren<Transform>(true))if(t.name=="Muzzle"){muzzles[i]=t;break;}
                if(!muzzles[i])muzzles[i]=gun;
            }
        }
        /// The first-person muzzle transform of a weapon (for flashes and tracers).
        public Transform MuzzleOf(WeaponId id)=>muzzles!=null&&(int)id>=0&&(int)id<muzzles.Length?muzzles[(int)id]:null;
        void OnEnable()
        {
            PulseEvents.Shot+=OnShotShake;PulseEvents.Damaged+=OnDamagedShake;PulseEvents.Eliminated+=OnEliminatedShake;
        }
        void OnDisable()
        {
            PulseEvents.Shot-=OnShotShake;PulseEvents.Damaged-=OnDamagedShake;PulseEvents.Eliminated-=OnEliminatedShake;
        }
        void AddTrauma(float amount){trauma=Mathf.Min(1,trauma+amount);}
        void OnShotShake(ArenaPlayer shooter,WeaponId weapon,Vector3 from,Vector3 to,bool hit)
        {
            if(!shooter||shooter!=ArenaPlayer.Local)return;
            bool scatter=weapon==WeaponId.Scatter;
            AddTrauma(scatter?.14f:.035f);
            // Impulses into the springs: gun kicks back, camera pitches up with a little random yaw, FOV pops.
            gunKickV+=scatter?26f:12f;
            punchPitchV+=scatter?40f:7f;
            punchYawV+=UnityEngine.Random.Range(-1f,1f)*(scatter?8f:3f);
            fovPunchV+=scatter?18f:6f;
        }
        static void Spring(ref float x,ref float v,float dt,float k,float c)
        {
            v+=(-k*x-c*v)*dt;x+=v*dt;
        }
        void OnDamagedShake(ArenaPlayer victim,ArenaPlayer attacker,int amount,Vector3 from)
        {
            if(!victim||victim!=ArenaPlayer.Local)return;
            AddTrauma(.25f+Mathf.Clamp01(amount/60f)*.3f);
            // Bias the kick away from the attacker: roll and pitch the view the other way.
            Vector3 toAttacker=from-View.transform.position;
            if(toAttacker.sqrMagnitude>.01f)
            {
                toAttacker.Normalize();
                hitRoll=-Vector3.Dot(View.transform.right,toAttacker)*4f;
                hitPitch=-2f*Mathf.Clamp01(.5f+Vector3.Dot(View.transform.forward,toAttacker)*.5f);
            }
        }
        void OnEliminatedShake(ArenaPlayer victim,ArenaPlayer attacker,WeaponId weapon)
        {
            var local=ArenaPlayer.Local;if(!victim||!local)return;
            if(victim==local){AddTrauma(.5f);return;}
            if(attacker==local)
            {
                // Hitstop feel (never touches Time.timeScale): freeze the view model/springs for a few frames, punch the FOV in.
                hitstopUntil=Time.unscaledTime+.07f;fovPunchV-=30f;AddTrauma(.08f);
            }
            float distance=Vector3.Distance(victim.transform.position,local.transform.position);
            if(distance<14f)AddTrauma(.3f*(1-distance/14f));
        }
        void Start(){UI.Show("menu");ApplyQuality(PlayerPrefs.GetInt("quality",1));gameObject.AddComponent<PulseSmoke>();}
        void OnDestroy()
        {
            if(Network){Network.OnServerStarted-=ServerStarted;Network.OnClientDisconnectCallback-=Disconnected;Network.ConnectionApprovalCallback-=Approve;}
            if(Instance==this)Instance=null;
        }
        void Approve(NetworkManager.ConnectionApprovalRequest request,NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved=Network.ConnectedClientsIds.Count<4;response.CreatePlayerObject=true;response.Pending=false;
            if(!response.Approved)response.Reason="This room is full (4 players).";
        }
        void ServerStarted()
        {
            var match=Instantiate(MatchPrefab).GetComponent<NetworkObject>();match.Spawn();ArenaPickup.SpawnAll(Network);
            if(Practice)
            {
                for(int i=0;i<3;i++){var go=Instantiate(PlayerPrefab);var bot=go.GetComponent<ArenaPlayer>();bot.InitializeAsBot=true;bot.BotIndex=i;go.GetComponent<NetworkObject>().Spawn();}
                Invoke(nameof(StartPractice),.3f);
            }
        }
        void StartPractice(){if(ArenaMatch.Instance&&Practice)ArenaMatch.Instance.StartRound();}
        public void AttachCamera(ArenaPlayer player){Controls.Yaw=player.transform.eulerAngles.y;Controls.Pitch=0;}
        public void OnPhase(int phase)
        {
            bool round=phase==1||phase==3;
            Paused=false;if(UI)UI.Show(phase==0?"lobby":round?"game":"results");
            LockCursor(round);if(phase==1)Announce("SYSTEMS ONLINE  /  FIRST TO 15");
        }
        public void Announce(string text){Notice=text;noticeUntil=Time.unscaledTime+2.2f;}
        void LateUpdate()
        {
            Recoil=Mathf.MoveTowards(Recoil,0,Time.deltaTime*7);DamageFlash=Mathf.MoveTowards(DamageFlash,0,Time.deltaTime*2);HitMarker=Mathf.MoveTowards(HitMarker,0,Time.deltaTime*5);
            if(Time.unscaledTime>noticeUntil)Notice="";
            var p=ArenaPlayer.Local;bool inGame=InRound&&p;
            float dt=Time.deltaTime;
            trauma=Mathf.MoveTowards(trauma,0,dt*1.8f);
            hitRoll=Mathf.MoveTowards(hitRoll,0,dt*14f);hitPitch=Mathf.MoveTowards(hitPitch,0,dt*10f);
            if(inGame)
            {
                int wanted=(int)p.CurrentWeapon;
                // Lower the old weapon, switch the model, then raise the new one.
                if(wanted!=shownWeapon)
                {
                    lowerBlend=Mathf.MoveTowards(lowerBlend,1,dt/.16f);
                    if(lowerBlend>=1)shownWeapon=wanted;
                }
                else lowerBlend=Mathf.MoveTowards(lowerBlend,0,dt/.16f);
                for(int i=0;i<guns.Length;i++)
                {
                    bool visible=p.Alive&&i==shownWeapon;
                    if(guns[i].gameObject.activeSelf!=visible)guns[i].gameObject.SetActive(visible);
                }

                float shake=trauma*trauma*ShakeScale;float time=Time.time;
                float shakeYaw=(Mathf.PerlinNoise(time*23f,.3f)-.5f)*2f*2.2f*shake;
                float shakePitch=(Mathf.PerlinNoise(time*21f,7.7f)-.5f)*2f*2.2f*shake+hitPitch*ShakeScale;
                float shakeRoll=(Mathf.PerlinNoise(time*19f,13.1f)-.5f)*2f*2.5f*shake+hitRoll*ShakeScale;
                // Springs freeze briefly on a kill (hitstop feel); the camera still follows the player's look.
                float vdt=Time.unscaledTime<hitstopUntil?0f:Mathf.Min(dt,.033f);
                Spring(ref punchPitch,ref punchPitchV,vdt,140f,16f);
                Spring(ref punchYaw,ref punchYawV,vdt,140f,16f);
                Spring(ref gunKick,ref gunKickV,vdt,160f,15f);
                Spring(ref fovPunch,ref fovPunchV,vdt,120f,14f);
                punchPitch=Mathf.Clamp(punchPitch,-1f,4.5f);punchYaw=Mathf.Clamp(punchYaw,-2f,2f);
                gunKick=Mathf.Clamp(gunKick,-.3f,2.6f);fovPunch=Mathf.Clamp(fovPunch,-5f,3f);

                // View-model sway follows the look speed (the gun lags behind the camera).
                float dYaw=haveLook?Mathf.DeltaAngle(lastYaw,Controls.Yaw):0f,dPitch=haveLook?Controls.Pitch-lastPitch:0f;
                lastYaw=Controls.Yaw;lastPitch=Controls.Pitch;haveLook=true;
                float invDt=1f/Mathf.Max(dt,.001f);
                float swayTargetX=Mathf.Clamp(-dYaw*invDt*.0004f,-.04f,.04f),swayTargetY=Mathf.Clamp(dPitch*invDt*.0004f,-.04f,.04f);
                float swayLerp=1-Mathf.Exp(-dt*12f);
                swayX=Mathf.Lerp(swayX,swayTargetX,swayLerp);swayY=Mathf.Lerp(swayY,swayTargetY,swayLerp);

                View.transform.position=p.transform.position+Vector3.up*1.65f;
                View.transform.rotation=Quaternion.Euler(Controls.Pitch-punchPitch*ShakeScale+shakePitch,Controls.Yaw+punchYaw*ShakeScale+shakeYaw,shakeRoll);

                float walk=Controls.Move.magnitude;bool sprinting=Controls.Sprint&&walk>.1f;
                bobAmount=Mathf.MoveTowards(bobAmount,walk,dt*6);
                bobTime+=dt*(sprinting?14f:10f)*bobAmount;
                float bobX=Mathf.Sin(bobTime*.5f)*bobAmount*(sprinting?.02f:.012f),bobY=Mathf.Abs(Mathf.Sin(bobTime*.5f))*bobAmount*(sprinting?.022f:.014f);
                float breathe=Mathf.Sin(Time.time*1.6f)*.003f*(1-bobAmount);
                sprintBlend=Mathf.MoveTowards(sprintBlend,sprinting?1:0,dt*6);
                var def=p.CurrentDef;
                float reload=p.Reloading?Mathf.Sin(Mathf.Clamp01(1-(float)(p.ReloadUntil.Value-p.Now)/def.ReloadSeconds)*Mathf.PI):0;
                float lower=lowerBlend*lowerBlend*(3-2*lowerBlend);
                float kick=gunKick*(shownWeapon==(int)WeaponId.Scatter?.9f:1f);

                // Park the gun low at the right edge, clear of the on-screen FIRE button, at any aspect ratio.
                float halfHeight=Mathf.Tan(38*Mathf.Deg2Rad)*GunDepth,halfWidth=halfHeight*Mathf.Max(View.aspect,1f);
                var gun=guns[shownWeapon];
                gun.localPosition=new Vector3((GunViewportX*2-1)*halfWidth+bobX+swayX-sprintBlend*.06f,(GunViewportY*2-1)*halfHeight+bobY+breathe+swayY-reload*.2f-lower*.45f-sprintBlend*.04f,GunDepth-kick*.06f);
                gun.localRotation=Quaternion.Euler(-kick*5f+reload*30+lower*35f+sprintBlend*8f+swayY*60f,reload*-20-4f-sprintBlend*16f+swayX*60f,reload*-30-sprintBlend*14f-swayX*50f);
                baseFov=Mathf.Lerp(baseFov,Controls.Sprint?83:76,dt*6);
                View.fieldOfView=baseFov+fovPunch*ShakeScale;
            }
            else
            {
                if(guns!=null)for(int i=0;i<guns.Length;i++)if(guns[i].gameObject.activeSelf)guns[i].gameObject.SetActive(false);
                lowerBlend=0;haveLook=false;baseFov=76f;punchPitch=punchPitchV=punchYaw=punchYawV=gunKick=gunKickV=fovPunch=fovPunchV=0f;
                float angle=Time.unscaledTime*.035f;View.transform.position=new Vector3(Mathf.Sin(angle)*16,9,Mathf.Cos(angle)*16);View.transform.LookAt(new Vector3(0,1.5f,0));View.fieldOfView=62;
            }
        }
        public void SaveName(string value)
        {PlayerName=System.Text.RegularExpressions.Regex.Replace(value??"PILOT","[^a-zA-Z0-9 _-]","");if(string.IsNullOrWhiteSpace(PlayerName))PlayerName="PILOT";PlayerName=PlayerName.Substring(0,Math.Min(16,PlayerName.Length));PlayerPrefs.SetString("pilot",PlayerName);PlayerPrefs.Save();}
        public async void HostRoom(){await Connect(true,"");}
        public async void JoinRoom(string code){await Connect(false,code);}
        public async Task Connect(bool host,string code)
        {
            if(Busy||Network.IsListening)return;
            code=(code??"").Trim().ToUpperInvariant();
            if(!host&&(code.Length<4||code.Length>12)){Status="Enter the room code shared by your host.";return;}
            Busy=true;Status=host?"Creating your room...":"Connecting to room...";Practice=false;
            try
            {
                var rooms=PulseOnline.Current;
                RoomCode=host?await rooms.HostAsync(Network):await rooms.JoinAsync(Network,code);
                Status="";UI.Show("lobby");
                // A late joiner may already have received the active match state.
                if(ArenaMatch.Instance)OnPhase(ArenaMatch.Instance.Phase.Value);
                Debug.Log("PULSE_ROOM_READY "+RoomCode);
            }
            catch(Exception e)
            {
                Debug.LogWarning("PULSE_CONNECT_FAILED "+e.Message);
                try{await PulseOnline.Current.LeaveAsync();}catch(Exception){}
                Network.Shutdown();RoomCode="";Status="Could not connect. Check your internet and room code, then try again.\n"+e.Message;UI.Show("menu");
            }
            finally{Busy=false;}
        }
        public void StartOffline()
        {
            if(Busy||Network.IsListening)return;Practice=true;Status="";RoomCode="PRACTICE";
            Network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",7779,"127.0.0.1");
            if(!Network.StartHost()){Practice=false;Status="Could not start practice. Please try again.";}
        }
        public void StartDirect(bool host)
        {
            Practice=false;RoomCode="LOCAL TEST";Network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",7781,"127.0.0.1");
            if(host)Network.StartHost();else Network.StartClient();UI.Show("lobby");
        }
        public async void Leave()
        {
            if(leaving||Busy)return;leaving=true;Busy=true;Paused=false;LockCursor(false);CancelInvoke(nameof(StartPractice));
            try{if(!Practice)await PulseOnline.Current.LeaveAsync();}catch(Exception e){Debug.LogWarning(e.Message);}
            finally{Network.Shutdown();RoomCode="";Practice=false;Busy=false;leaving=false;UI.Show("menu");}
        }
        void Disconnected(ulong id)
        {
            if(leaving||Busy||id!=Network.LocalClientId)return;
            Status="The room connection ended. The host may have left. You can create or join another room.";Leave();
        }
        public void TogglePause(){if(!IsPlaying)return;Paused=!Paused;UI.Show(Paused?"pause":"game");LockCursor(!Paused);}
        public void OpenSettings(){UI.Show("settings");LockCursor(false);}
        public void CloseSettings(){UI.Show(IsPlaying?"pause":"menu");}
        public void StartMatch(){if(Network.IsServer&&ArenaMatch.Instance)ArenaMatch.Instance.StartRound();}
        static void LockCursor(bool active){Cursor.lockState=active&&!Application.isMobilePlatform?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!active||Application.isMobilePlatform;}
        public void ApplyQuality(int level)=>PulseDisplay.ApplyQuality(level);
        void OnApplicationFocus(bool focused){if(!focused&&IsPlaying&&!Paused&&!PulseSmoke.Testing)TogglePause();}
    }
}
