using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

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
        public bool IsPlaying=>ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==1;
        ISession session;
        Transform gun;
        float noticeUntil;
        bool leaving;
        void Awake()
        {
            Instance=this;Application.targetFrameRate=60;Screen.sleepTimeout=SleepTimeout.NeverSleep;
            PlayerName=PlayerPrefs.GetString("pilot","PILOT");
            Controls=gameObject.AddComponent<PulseInput>();gameObject.AddComponent<PulseAudio>();
            ArenaArt.BuildArena(new GameObject("Reactor 07 | Arena").transform);
            gun=ArenaArt.Weapon(View.transform);gun.localScale=Vector3.one*.65f;gun.localPosition=new Vector3(.28f,-.27f,.57f);gun.gameObject.SetActive(false);
            UI=gameObject.AddComponent<PulseUI>();
            Network.OnServerStarted+=ServerStarted;Network.OnClientDisconnectCallback+=Disconnected;
            Network.NetworkConfig.ConnectionApproval=true;Network.ConnectionApprovalCallback+=Approve;
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
            var match=Instantiate(MatchPrefab).GetComponent<NetworkObject>();match.Spawn();
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
            Paused=false;if(UI)UI.Show(phase==0?"lobby":phase==1?"game":"results");
            LockCursor(phase==1);if(phase==1)Announce("SYSTEMS ONLINE  /  FIRST TO 15");
            if(phase==2)PulseAudio.Instance.Play("kill");
        }
        public void Announce(string text){Notice=text;noticeUntil=Time.unscaledTime+2.2f;}
        void LateUpdate()
        {
            Recoil=Mathf.MoveTowards(Recoil,0,Time.deltaTime*7);DamageFlash=Mathf.MoveTowards(DamageFlash,0,Time.deltaTime*2);HitMarker=Mathf.MoveTowards(HitMarker,0,Time.deltaTime*5);
            if(Time.unscaledTime>noticeUntil)Notice="";
            var p=ArenaPlayer.Local;bool inGame=IsPlaying&&p;
            gun.gameObject.SetActive(inGame&&p.Alive);
            if(inGame)
            {
                View.transform.position=p.transform.position+Vector3.up*1.65f;
                View.transform.rotation=Quaternion.Euler(Controls.Pitch-Recoil*1.4f,Controls.Yaw,0);
                float walk=Controls.Move.magnitude;float bob=Mathf.Sin(Time.time*10)*walk*.012f;
                float reload=p.Reloading?Mathf.Sin(Mathf.Clamp01((float)(p.ReloadUntil.Value-p.Now)/1.45f)*Mathf.PI):0;
                gun.localPosition=new Vector3(.28f+bob,-.27f-reload*.22f+Mathf.Abs(bob),.57f-Recoil*.07f);
                gun.localRotation=Quaternion.Euler(Recoil*-7+reload*30,reload*-20,reload*-30+(Controls.Sprint?-14:0));
                View.fieldOfView=Mathf.Lerp(View.fieldOfView,Controls.Sprint?83:76,Time.deltaTime*6);
            }
            else
            {
                float angle=Time.unscaledTime*.035f;View.transform.position=new Vector3(Mathf.Sin(angle)*16,9,Mathf.Cos(angle)*16);View.transform.LookAt(new Vector3(0,1.5f,0));View.fieldOfView=62;
            }
        }
        public void SaveName(string value)
        {PlayerName=System.Text.RegularExpressions.Regex.Replace(value??"PILOT","[^a-zA-Z0-9 _-]","");if(string.IsNullOrWhiteSpace(PlayerName))PlayerName="PILOT";PlayerName=PlayerName.Substring(0,Math.Min(16,PlayerName.Length));PlayerPrefs.SetString("pilot",PlayerName);PlayerPrefs.Save();}
        async Task InitializeServices()
        {
            if(string.IsNullOrEmpty(Application.cloudProjectId))throw new InvalidOperationException("Online setup is missing. Link this project in Unity Project Settings > Services.");
            if(UnityServices.State!=ServicesInitializationState.Initialized)
            {
                var options=new InitializationOptions();
                string profile=PulseSmoke.Argument("-profile");if(!string.IsNullOrEmpty(profile))options.SetProfile(profile);
                await UnityServices.InitializeAsync(options);
            }
            if(!AuthenticationService.Instance.IsSignedIn)await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
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
                await InitializeServices();
                session=host?await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions{Name="Pulse Arena",MaxPlayers=4,IsPrivate=true}.WithRelayNetwork())
                    :await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                RoomCode=session.Code;Status="";UI.Show("lobby");
                // A late joiner may already have received the active match state.
                if(ArenaMatch.Instance)OnPhase(ArenaMatch.Instance.Phase.Value);
                Debug.Log("PULSE_ROOM_READY "+RoomCode);
            }
            catch(Exception e)
            {
                Debug.LogWarning("PULSE_CONNECT_FAILED "+e.Message);
                if(session!=null){try{await session.LeaveAsync();}catch(Exception){}session=null;}
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
            try{if(session!=null)await session.LeaveAsync();}catch(Exception e){Debug.LogWarning(e.Message);}
            finally{session=null;Network.Shutdown();RoomCode="";Practice=false;Busy=false;leaving=false;UI.Show("menu");}
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
        public void ApplyQuality(int level){PlayerPrefs.SetInt("quality",level);QualitySettings.shadows=level==0?ShadowQuality.Disable:ShadowQuality.HardOnly;QualitySettings.shadowDistance=level==0?0:45;Application.targetFrameRate=level==0?30:60;}
        void OnApplicationFocus(bool focused){if(!focused&&IsPlaying&&!Paused&&!PulseSmoke.Testing)TogglePause();}
    }
}
