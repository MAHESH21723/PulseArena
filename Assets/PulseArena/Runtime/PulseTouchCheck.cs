using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PulseArena
{
    // Development-only integration check: touches enter the Input System and pass
    // through the real UI raycaster. No button callbacks or gameplay RPCs are called.
    public class PulseTouchCheck:MonoBehaviour
    {
        Touchscreen screen;
        readonly List<string> checks=new List<string>();
        IEnumerator Start()
        {
            if(!Debug.isDebugBuild&&!Application.isEditor)yield break;
            // The test player runs in a hidden window. Keep its synthetic touchscreen
            // active while unfocused; production uses the platform's normal policy.
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            screen=InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var app=PulseApp.Instance;
            Check(app.UI&&GameObject.Find("menu"),"startup creates visible menu");
            Check(app.View.transform.position.y>5,"menu camera is above arena");
            // These are all the shapes used by the arena, weapons and pilots.
            foreach(var type in new[]{PrimitiveType.Cube,PrimitiveType.Cylinder,PrimitiveType.Capsule,PrimitiveType.Sphere})
            {
                var shape=GameObject.CreatePrimitive(type);
                Check(shape.GetComponent<Renderer>()&&shape.GetComponent<Collider>(),type+" has renderer and collider");
                Destroy(shape);
            }
            yield return Tap("SETTINGS");Check(GameObject.Find("settings"),"settings opens by touch");
            yield return Tap("DONE");Check(GameObject.Find("menu"),"settings returns to menu by touch");
            yield return Tap("PRACTICE WITH BOTS");
            float deadline=Time.realtimeSinceStartup+15;
            while(!app.IsPlaying&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.IsPlaying&&ArenaPlayer.Local,"practice starts by touch");
            foreach(var bot in ArenaMatch.Players())if(bot.Bot.Value)bot.enabled=false;
            yield return new WaitForSeconds(.5f);
            Check(GameObject.Find("game")&&GameObject.Find("FIRE"),"touch HUD is visible");
            var player=ArenaPlayer.Local;var before=player.transform.position;int ammo=player.Ammo.Value;
            Vector2 move=Point("Movement"),fire=Point("FIRE");
            Touch(1,UnityEngine.InputSystem.TouchPhase.Began,move);
            Touch(2,UnityEngine.InputSystem.TouchPhase.Began,fire);
            yield return new WaitForSeconds(.1f);
            move+=Vector2.up*70*FindFirstObjectByType<Canvas>().scaleFactor;
            Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,move);
            yield return new WaitForSeconds(.65f);
            Check(Vector3.Distance(before,player.transform.position)>.5f,"joystick moves player through normal input");
            Check(player.Ammo.Value<ammo,"second simultaneous touch fires weapon");
            Touch(1,UnityEngine.InputSystem.TouchPhase.Ended,move);
            Touch(2,UnityEngine.InputSystem.TouchPhase.Ended,fire);
            yield return new WaitForSeconds(.15f);
            Check(app.Controls.Move.sqrMagnitude<.01f&&!app.Controls.Fire,"releasing touches stops movement and fire");
            float yaw=app.Controls.Yaw;Vector2 look=Point("Look area");
            Touch(3,UnityEngine.InputSystem.TouchPhase.Began,look);yield return new WaitForSeconds(.1f);
            Touch(3,UnityEngine.InputSystem.TouchPhase.Moved,look+new Vector2(110,20));yield return new WaitForSeconds(.15f);
            Touch(3,UnityEngine.InputSystem.TouchPhase.Ended,look+new Vector2(110,20));yield return new WaitForSeconds(.1f);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw,app.Controls.Yaw))>1,"right-side drag aims camera");
            yield return Tap("RELOAD");yield return new WaitForSeconds(1.7f);
            Check(player.Ammo.Value==24,"reload works by touch");
            yield return Tap("PAUSE");Check(app.Paused&&GameObject.Find("pause"),"pause opens by touch");
            yield return Tap("RESUME");Check(!app.Paused&&GameObject.Find("game"),"resume works by touch");
            yield return Tap("PAUSE");yield return Tap("LEAVE MATCH");yield return new WaitForSeconds(.5f);
            Check(!app.Network.IsListening&&GameObject.Find("menu"),"leave returns to menu by touch");
            Write("PASS ("+Screen.width+"x"+Screen.height+"): "+string.Join("; ",checks));
            InputSystem.RemoveDevice(screen);Application.Quit();
        }
        Vector2 Point(string name)
        {
            Canvas.ForceUpdateCanvases();
            var go=GameObject.Find(name);Check(go,"touch target exists: "+name);
            return RectTransformUtility.WorldToScreenPoint(null,((RectTransform)go.transform).TransformPoint(((RectTransform)go.transform).rect.center));
        }
        IEnumerator Tap(string name)
        {
            var point=Point(name);
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=point},hits);
            Debug.Log("PULSE_TOUCH_TARGET "+name+" at "+point+" enabled="+screen.enabled+" hits="+string.Join(",",hits.ConvertAll(h=>h.gameObject.name)));
            Touch(5,UnityEngine.InputSystem.TouchPhase.Began,point);
            yield return new WaitForSeconds(.12f);Touch(5,UnityEngine.InputSystem.TouchPhase.Ended,point);
            yield return new WaitForSeconds(.4f);
        }
        void Touch(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 position)
        {InputSystem.QueueStateEvent(screen,new TouchState{touchId=id,phase=phase,position=position,pressure=phase==UnityEngine.InputSystem.TouchPhase.Ended?0:1});}
        void Check(bool condition,string description)
        {
            if(condition){checks.Add(description);return;}
            Write("FAIL: "+description);Application.Quit(2);throw new InvalidOperationException(description);
        }
        void Write(string message)
        {
            Debug.Log("PULSE_TOUCH_TEST "+message);
            string dir=PulseSmoke.Argument("-test-output");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"touch.txt"),message);
        }
    }
}
