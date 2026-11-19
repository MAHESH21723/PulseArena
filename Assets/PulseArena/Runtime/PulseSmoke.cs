using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace PulseArena
{
    // Development-player integration checks. Excluded from release behavior.
    public class PulseSmoke:MonoBehaviour
    {
        public static string Argument(string key)
        {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:"";}
        public static bool Testing=>(Debug.isDebugBuild||Application.isEditor)&&!string.IsNullOrEmpty(Argument("-pulse-test"));
        public static bool SuppressPlayerInput=>Testing&&Argument("-pulse-test")!="touch";
        IEnumerator Start()
        {
            if(!Debug.isDebugBuild&&!Application.isEditor)yield break;
            string mode=Argument("-pulse-test");if(string.IsNullOrEmpty(mode))yield break;
            Application.runInBackground=true;
            yield return new WaitForSeconds(4);
            var app=PulseApp.Instance;
            if(mode=="touch"){gameObject.AddComponent<PulseTouchCheck>();yield break;}
            if(mode=="menu")
            {yield return Capture("menu");app.UI.Show("settings");yield return new WaitForSeconds(.5f);yield return Capture("settings");app.UI.Show("controls");yield return new WaitForSeconds(.5f);yield return Capture("controls");Application.Quit();yield break;}
            if(mode=="screens"){yield return Screens(app);yield break;}
            if(mode=="showcase"){gameObject.AddComponent<PulseShowcase>().Begin(app);yield break;}
            int countdownTicks=0;Action<int> onTick=n=>countdownTicks|=1<<n;
            if(mode=="practice"){PulseEvents.Countdown+=onTick;app.StartOffline();}
            else if(mode=="host")app.StartDirect(true);
            else if(mode=="client")app.StartDirect(false);
            else if(mode=="online-host")app.HostRoom();
            else if(mode=="online-client")app.JoinRoom(Argument("-room"));
            else if(mode=="lan-host"){PulseOnline.Mode=OnlineMode.LocalWifi;app.HostRoom();}
            else if(mode=="lan-client"){PulseOnline.Mode=OnlineMode.LocalWifi;app.JoinRoom(Argument("-room"));}
            float deadline=Time.realtimeSinceStartup+45;
            while(!ArenaPlayer.Local&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!ArenaPlayer.Local){Report("FAIL: player did not spawn. "+app.Status);Application.Quit(2);yield break;}
            if(mode=="online-host"||mode=="lan-host")
            {
                while(app.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
                File.WriteAllText(Path.Combine(Argument("-test-output"),"room.txt"),app.RoomCode);
            }
            if(mode.EndsWith("host")||mode=="host")
            {
                while(app.Network.ConnectedClientsIds.Count<2&&Time.realtimeSinceStartup<deadline)yield return null;
                if(app.Network.ConnectedClientsIds.Count<2){Report("FAIL: second client did not connect");Application.Quit(2);yield break;}
                app.StartMatch();
            }
            if(mode=="practice")
            {
                // Countdown (phase 3) must precede play (phase 1).
                bool sawCountdown=false;float limit=Time.realtimeSinceStartup+15;
                while(!app.IsPlaying&&Time.realtimeSinceStartup<limit)
                {if(ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==3)sawCountdown=true;yield return null;}
                yield return null;PulseEvents.Countdown-=onTick;
                // 3 and GO(0) must arrive; 2/1 may be skipped by a long frame hitch (first-run shader compiles).
                bool countdownOk=sawCountdown&&app.IsPlaying&&(countdownTicks&9)==9;
                var results=new System.Collections.Generic.List<string>();
                bool allPass=true;
                void Add(bool ok,string text){allPass&=ok;results.Add((ok?"PASS: ":"FAIL: ")+text);Debug.Log("PULSE_TEST "+(ok?"PASS: ":"FAIL: ")+text);}
                Add(countdownOk,"countdown to playing transition (saw phase 3="+sawCountdown+", ticks="+countdownTicks+")");
                yield return new WaitForSeconds(1.5f);
                var me=ArenaPlayer.Local;
                Add(ArenaMatch.Players().Length==4&&app.IsPlaying,"practice spawned player + 3 bots, active match");

                // Weapon swap.
                bool startedCarbine=me.CurrentWeapon==WeaponId.Carbine&&me.Ammo.Value==me.MagazineOf(WeaponId.Carbine);
                me.RequestSwap(WeaponId.Scatter);yield return new WaitForSeconds(.7f);
                Add(startedCarbine&&me.CurrentWeapon==WeaponId.Scatter&&!me.Swapping&&me.Ammo.Value==me.MagazineOf(WeaponId.Scatter),"weapon swap to scatter works");

                // Scatter at point-blank range damages a stationary bot.
                ArenaPlayer target=null;foreach(var q in ArenaMatch.Players())if(q.Bot.Value){target=q;break;}
                bool scatterHit=false;
                if(target)
                {
                    target.enabled=false;
                    // Spawn pad 0 is open floor on every layout; face the target 2.5 m ahead.
                    var pad=ArenaArt.Spawn(0);var ahead=ArenaArt.SpawnRotation(0)*Vector3.forward;ahead.y=0;ahead.Normalize();
                    float aim=Quaternion.LookRotation(ahead).eulerAngles.y;
                    Place(me,pad,aim);Place(target,pad+ahead*2.5f,aim+180);
                    app.Controls.Yaw=aim;app.Controls.Pitch=0;me.TestInput(Vector2.zero,aim,0,false);
                    yield return new WaitForSeconds(1.8f);// let spawn immunity of the target pass
                    int before=target.Health.Value,magBefore=me.MagazineOf(WeaponId.Scatter);
                    float stop=Time.realtimeSinceStartup+2;
                    while(Time.realtimeSinceStartup<stop&&target.Health.Value==before){me.TestInput(Vector2.zero,aim,0,true);yield return new WaitForSeconds(.04f);}
                    me.TestInput(Vector2.zero,aim,0,false);yield return new WaitForSeconds(.3f);
                    scatterHit=target.Health.Value<before&&me.MagazineOf(WeaponId.Scatter)<magBefore;
                }
                Add(scatterHit,"scatter shot damages a bot at close range");
                me.RequestSwap(WeaponId.Carbine);yield return new WaitForSeconds(.5f);
                yield return Capture("practice");
                Report((allPass?"PASS: ":"FAIL: ")+string.Join("; ",results));
                yield return new WaitForSeconds(2);app.Leave();yield return new WaitForSeconds(1);Application.Quit();yield break;
            }
            if(mode=="client"||mode=="online-client"||mode=="lan-client")
            {
                while(!app.IsPlaying&&Time.realtimeSinceStartup<deadline)yield return null;
                yield return new WaitForSeconds(1);var before=ArenaPlayer.Local.transform.position;
                float until=Time.realtimeSinceStartup+.6f;while(Time.realtimeSinceStartup<until){ArenaPlayer.Local.TestInput(Vector2.up,0,0,false);yield return new WaitForSeconds(.04f);}
                ArenaPlayer.Local.TestInput(Vector2.zero,0,0,false);yield return new WaitForSeconds(.5f);
                bool moved=Vector3.Distance(before,ArenaPlayer.Local.transform.position)>.5f;
                bool sawDeath=false,sawRespawn=false;
                while(ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value!=2&&Time.realtimeSinceStartup<deadline)
                {if(ArenaPlayer.Local.Health.Value==0)sawDeath=true;if(sawDeath&&ArenaPlayer.Local.Health.Value==100)sawRespawn=true;yield return null;}
                yield return Capture("client-results");Report(moved&&sawDeath&&sawRespawn&&ArenaMatch.Instance&&ArenaMatch.Instance.Phase.Value==2?"PASS: remote movement, damage, death, respawn and results replicated":"FAIL: client moved="+moved+" death="+sawDeath+" respawn="+sawRespawn);
                yield return new WaitForSeconds(2);app.Leave();yield return new WaitForSeconds(1);Application.Quit();yield break;
            }
            yield return new WaitForSeconds(4);
            var host=ArenaPlayer.Local;ArenaPlayer rival=null;foreach(var p in ArenaMatch.Players())if(p!=host)rival=p;
            Place(host,new Vector3(-4,.12f,-18),90);Place(rival,new Vector3(4,.12f,-18),270);
            app.Controls.Yaw=90;app.Controls.Pitch=0;host.TestInput(Vector2.zero,90,0,false);
            yield return new WaitForSeconds(2);yield return Capture("combat");
            float finish=Time.realtimeSinceStartup+2;
            while(Time.realtimeSinceStartup<finish&&rival.Health.Value>0){host.TestInput(Vector2.zero,90,0,true);yield return new WaitForSeconds(.04f);}
            host.TestInput(Vector2.zero,90,0,false);
            bool killed=rival.Health.Value==0&&host.Kills.Value==1&&rival.Deaths.Value==1;
            host.ReloadServerRpc();yield return new WaitForSeconds(1.7f);bool reloaded=host.Ammo.Value==24;
            yield return new WaitForSeconds(2);bool respawned=rival.Health.Value==100;
            ArenaMatch.Instance.EndsAt.Value=host.Now+.25;yield return new WaitForSeconds(1);
            yield return Capture("host-results");
            Report(killed&&reloaded&&respawned&&ArenaMatch.Instance.Phase.Value==2?"PASS: network connection, server damage, score, reload, respawn and timed results":"FAIL: killed="+killed+" reload="+reloaded+" respawn="+respawned);
            yield return new WaitForSeconds(3);app.Leave();yield return new WaitForSeconds(1);Application.Quit();
        }
        static void Place(ArenaPlayer p,Vector3 point,float yaw)
        {var c=p.GetComponent<CharacterController>();c.enabled=false;p.transform.position=point;c.enabled=true;p.GetComponent<NetworkTransform>().Teleport(point,Quaternion.Euler(0,yaw,0),Vector3.one);}
        // Captures menu, settings, in-match HUD and results at several phone/tablet resolutions.
        static readonly (int w,int h,float dpi,string name)[] Devices={(1024,768,264,"tablet-4x3"),(1600,900,320,"16x9"),(2340,1080,400,"19.5x9"),(2400,1080,405,"20x9"),(3200,1440,515,"20x9-qhd"),(2560,1080,420,"21x9")};
        IEnumerator Screens(PulseApp app)
        {
            foreach(var d in Devices){PulseDisplay.SimulateResolution(d.w,d.h,d.dpi);yield return new WaitForSecondsRealtime(.6f);app.UI.Show("menu");yield return new WaitForSecondsRealtime(.8f);yield return Capture(d.name+"-menu");app.UI.Show("settings");yield return new WaitForSecondsRealtime(.8f);yield return Capture(d.name+"-settings");}
            PulseDisplay.SimulateResolution(1600,900,320);app.UI.Show("menu");yield return new WaitForSecondsRealtime(.5f);
            app.StartOffline();float limit=Time.realtimeSinceStartup+20;while(!app.IsPlaying&&Time.realtimeSinceStartup<limit)yield return null;
            yield return new WaitForSeconds(6);
            foreach(var d in Devices){PulseDisplay.SimulateResolution(d.w,d.h,d.dpi);yield return new WaitForSecondsRealtime(.8f);yield return Capture(d.name+"-game");}
            PulseDisplay.SimulateResolution(1600,900,320);
            bool ok=app.IsPlaying&&ArenaMatch.Players().Length==4;
            if(ok&&ArenaMatch.Instance){ArenaMatch.Instance.Phase.Value=2;yield return new WaitForSecondsRealtime(1.2f);yield return Capture("16x9-results");}
            Report(ok?"PASS: screens captured for "+Devices.Length+" devices":"FAIL: practice did not start for screens");
            yield return new WaitForSeconds(1);app.Leave();yield return new WaitForSeconds(1);Application.Quit();
        }
        IEnumerator Capture(string name)
        {
            string dir=Argument("-test-output");if(string.IsNullOrEmpty(dir))yield break;Directory.CreateDirectory(dir);
            // Hidden player windows do not present their swap chain. Render to an explicit
            // target so automated visual checks contain the scene and Canvas, not a black window.
            // Every root overlay canvas (pages + dynamic HUD) is rendered through the camera, top canvases nearest.
            var camera=PulseApp.Instance.View;
            var canvases=System.Array.FindAll(FindObjectsByType<Canvas>(FindObjectsInactive.Exclude),c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay);
            System.Array.Sort(canvases,(a,b)=>a.sortingOrder.CompareTo(b.sortingOrder));
            var previousModes=new RenderMode[canvases.Length];
            for(int i=0;i<canvases.Length;i++){previousModes[i]=canvases[i].renderMode;canvases[i].renderMode=RenderMode.ScreenSpaceCamera;canvases[i].worldCamera=camera;canvases[i].planeDistance=.09f-i*.004f;}
            int width=PulseDisplay.ScreenSize.x,height=PulseDisplay.ScreenSize.y;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.antiAliasing=2;target.Create();camera.targetTexture=target;
            Canvas.ForceUpdateCanvases();
            // Headless (-batchmode) editors never reach end-of-frame; the explicit Render() below doesn't need it.
            if(Application.isBatchMode)yield return null;else yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),texture.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=previousModes[i];canvases[i].worldCamera=null;}target.Release();Destroy(target);Destroy(texture);yield return null;
        }
        void Report(string value)
        {Debug.Log("PULSE_TEST "+value);string dir=Argument("-test-output");if(!string.IsNullOrEmpty(dir)){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,Argument("-pulse-test")+".txt"),value);}}
    }
}

