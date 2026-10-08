using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PulseArena
{
    public class PulseInput:MonoBehaviour
    {
        public Vector2 Move,TouchMove;
        public float Yaw,Pitch;
        public bool Fire,Sprint,TouchFire,TouchSprint;
        public float Sensitivity=1;
        Vector2 touchLook;
        public void Look(Vector2 delta){touchLook+=delta;}
        void Awake(){Sensitivity=PlayerPrefs.GetFloat("sensitivity",1);}
        void Update()
        {
            var app=PulseApp.Instance;var player=ArenaPlayer.Local;
            bool playing=app&&app.IsPlaying&&!app.Paused&&player&&player.Alive;
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame&&app&&app.IsPlaying)app.TogglePause();
            if(!playing){Move=Vector2.zero;Fire=Sprint=false;touchLook=Vector2.zero;TouchMove=Vector2.zero;TouchFire=TouchSprint=false;return;}
            Move=TouchMove;Fire=TouchFire;Sprint=TouchSprint;
            if(Keyboard.current!=null)
            {
                var k=Keyboard.current;Move+=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
                Sprint|=k.leftShiftKey.isPressed;if(k.rKey.wasPressedThisFrame)Reload();
            }
            var delta=touchLook*.12f*(1080f/Mathf.Max(Screen.height,1));touchLook=Vector2.zero;
            if(Mouse.current!=null&&Cursor.lockState==CursorLockMode.Locked){delta+=Mouse.current.delta.ReadValue()*.075f;Fire|=Mouse.current.leftButton.isPressed;}
            Yaw=Mathf.Repeat(Yaw+delta.x*Sensitivity,360);Pitch=Mathf.Clamp(Pitch-delta.y*Sensitivity,-75,75);Move=Vector2.ClampMagnitude(Move,1);
        }
        public void Reload(){if(ArenaPlayer.Local&&ArenaPlayer.Local.Alive){ArenaPlayer.Local.ReloadServerRpc();PulseAudio.Instance.Play("reload",.5f);}}
    }
    public class TouchPad:MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler
    {
        public string Mode;
        public RectTransform Knob;
        int finger=int.MinValue;
        Vector2 origin;
        public void OnPointerDown(PointerEventData e)
        {
            if(finger!=int.MinValue)return;finger=e.pointerId;origin=e.position;
            if(Mode=="fire")PulseApp.Instance.Controls.TouchFire=true;
            if(Mode=="sprint")PulseApp.Instance.Controls.TouchSprint=true;
            if(Mode=="move")OnDrag(e);
        }
        public void OnDrag(PointerEventData e)
        {
            if(e.pointerId!=finger)return;
            if(Mode=="look")PulseApp.Instance.Controls.Look(e.delta);
            if(Mode=="move")
            {
                var r=(RectTransform)transform;RectTransformUtility.ScreenPointToLocalPointInRectangle(r,e.position,e.pressEventCamera,out var point);
                Vector2 v=Vector2.ClampMagnitude(point/(r.rect.width*.35f),1);PulseApp.Instance.Controls.TouchMove=v;if(Knob)Knob.anchoredPosition=v*r.rect.width*.3f;
            }
        }
        public void OnPointerUp(PointerEventData e){if(e.pointerId==finger)ResetPad();}
        void OnDisable(){ResetPad();}
        void ResetPad()
        {finger=int.MinValue;if(!PulseApp.Instance)return;var input=PulseApp.Instance.Controls;if(Mode=="fire")input.TouchFire=false;if(Mode=="sprint")input.TouchSprint=false;if(Mode=="move"){input.TouchMove=Vector2.zero;if(Knob)Knob.anchoredPosition=Vector2.zero;}}
    }
}
