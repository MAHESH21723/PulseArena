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
            if(mode=="practice")app.StartOffline();
            else if(mode=="host")app.StartDirect(true);
            else if(mode=="client")app.StartDirect(false);
            else if(mode=="online-host")app.HostRoom();
            else if(mode=="online-client")app.JoinRoom(Argument("-room"));
            float deadline=Time.realtimeSinceStartup+45;
            while(!ArenaPlayer.Local&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!ArenaPlayer.Local){Report("FAIL: player did not spawn. "+app.Status);Application.Quit(2);yield break;}
            if(mode=="online-host")
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
                yield return new WaitForSeconds(5);yield return Capture("practice");
                Report(ArenaMatch.Players().Length==4&&app.IsPlaying?"PASS: practice spawned player + 3 bots, active match":"FAIL: practice state");
                yield return new WaitForSeconds(4);app.Leave();yield return new WaitForSeconds(1);Application.Quit();yield break;
            }
            if(mode=="client"||mode=="online-client")
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
        IEnumerator Capture(string name)
        {
            string dir=Argument("-test-output");if(string.IsNullOrEmpty(dir))yield break;Directory.CreateDirectory(dir);
            // Hidden player windows do not present their swap chain. Render to an explicit
            // target so automated visual checks contain the scene and Canvas, not a black window.
            var camera=PulseApp.Instance.View;var canvas=FindFirstObjectByType<Canvas>();
            var previousMode=canvas.renderMode;var previousCamera=canvas.worldCamera;float previousDistance=canvas.planeDistance;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.08f;
            int width=Screen.width,height=Screen.height;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.antiAliasing=2;target.Create();camera.targetTexture=target;
            Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),texture.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;canvas.renderMode=previousMode;canvas.worldCamera=previousCamera;canvas.planeDistance=previousDistance;target.Release();Destroy(target);Destroy(texture);yield return null;
        }
        void Report(string value)
        {Debug.Log("PULSE_TEST "+value);string dir=Argument("-test-output");if(!string.IsNullOrEmpty(dir)){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,Argument("-pulse-test")+".txt"),value);}}
    }
}

