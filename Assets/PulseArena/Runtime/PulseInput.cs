using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
            bool alive=app&&!app.Paused&&player&&player.Alive;
            bool canLook=alive&&GameApi.InRound;     // phase 1 or 3 (countdown)
            bool canAct=alive&&GameApi.IsPlaying;    // phase 1 only
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame&&app&&app.IsPlaying)app.TogglePause();
            if(!canLook){Move=Vector2.zero;Fire=Sprint=false;touchLook=Vector2.zero;PulseAim.Clear();return;}
            Move=Vector2.zero;Fire=Sprint=false;
            if(canAct){Move=TouchMove;Fire=TouchFire;Sprint=TouchSprint;}
            Vector2 delta=Vector2.zero;
            if(Keyboard.current!=null)
            {
                var k=Keyboard.current;
                if(canAct)
                {
                    Move+=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
                    Sprint|=k.leftShiftKey.isPressed;if(k.rKey.wasPressedThisFrame)Reload();
                    if(k.qKey.wasPressedThisFrame)Swap();
                    if(k.digit1Key.wasPressedThisFrame)GameApi.SwapWeapon(WeaponId.Carbine);
                    if(k.digit2Key.wasPressedThisFrame)GameApi.SwapWeapon(WeaponId.Scatter);
                }
            }
            // Touch look: light exponential smoothing plus mild speed-based acceleration.
            float dt=Mathf.Max(Time.unscaledDeltaTime,.0001f);
            float take=1-Mathf.Exp(-dt*35f);
            Vector2 step=touchLook*take;touchLook-=step;
            float speed=step.magnitude/dt/Mathf.Max(Screen.height,1);   // screen heights per second
            float gain=1+Mathf.Clamp01(speed/1.5f)*.35f;
            Vector2 touchDelta=step*gain*.12f*(1080f/Mathf.Max(Screen.height,1));
            bool dragging=touchDelta.sqrMagnitude>.0004f;
            delta+=touchDelta;
            if(Mouse.current!=null&&Cursor.lockState==CursorLockMode.Locked)
            {
                delta+=Mouse.current.delta.ReadValue()*.075f;
                if(canAct){Fire|=Mouse.current.leftButton.isPressed;if(Mathf.Abs(Mouse.current.scroll.ReadValue().y)>.1f&&Time.unscaledTime-lastWheel>.25f){lastWheel=Time.unscaledTime;Swap();}}
            }
            Move=Vector2.ClampMagnitude(Move,1);
            // Aim assist (local human only): friction on touch drag near a target, gentle pull while moving/firing, optional auto-fire.
            PulseAim.Tick(player,Yaw,Pitch);
            if(canAct&&!Sprint&&PulseAim.AutoFireWanted(player.CurrentDef))Fire=true;
            delta-=touchDelta*(1-PulseAim.LookScale);
            Vector2 pull=canAct?PulseAim.Pull(dt,Move.sqrMagnitude>.04f,Fire,dragging):Vector2.zero;
            Yaw=Mathf.Repeat(Yaw+delta.x*Sensitivity+pull.x,360);Pitch=Mathf.Clamp(Pitch-delta.y*Sensitivity+pull.y,-75,75);
        }
        float lastWheel;
        public void Reload(){if(ArenaPlayer.Local&&ArenaPlayer.Local.Alive)ArenaPlayer.Local.ReloadServerRpc();}
        public void Swap(){GameApi.SwapWeapon();}
    }

    /// Touch surface. Modes: look, move (floating pad), fire, sprint.
    public class TouchPad:MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler
    {
        public string Mode;
        public RectTransform Knob;
        /// Move mode: the visible pad ring (child of this zone). It follows the thumb and returns to its home corner when idle.
        public RectTransform Pad;
        public float HomeMargin=36;
        int finger=int.MinValue;
        /// Fire buttons currently held (right + optional left); TouchFire stays on until all are released.
        static int fireHolds;
        Vector2 origin,lastSize;
        Image padImage;
        public bool Active=>finger!=int.MinValue;
        float Radius=>Pad?Pad.rect.width*.4f:75;

        void OnEnable(){if(Pad)padImage=Pad.GetComponent<Image>();GoHome();}
        void LateUpdate()
        {
            if(Mode!="move"||!Pad||Active)return;
            var size=((RectTransform)transform).rect.size;
            if(size!=lastSize||Pad.sizeDelta.x!=lastPad){lastSize=size;lastPad=Pad.sizeDelta.x;GoHome();}
        }
        float lastPad;
        public void GoHome()
        {
            if(Mode!="move"||!Pad)return;
            var r=((RectTransform)transform).rect;float half=Pad.sizeDelta.x*.5f;
            Pad.anchoredPosition=new Vector2(r.xMin+HomeMargin+half,r.yMin+HomeMargin+half);
            if(Knob)Knob.anchoredPosition=Vector2.zero;
            if(padImage){var c=padImage.color;c.a=.4f;padImage.color=c;}
        }
        public void OnPointerDown(PointerEventData e)
        {
            if(finger!=int.MinValue)return;finger=e.pointerId;origin=e.position;
            if(Mode=="fire"){fireHolds++;PulseApp.Instance.Controls.TouchFire=true;}
            if(Mode=="sprint")PulseApp.Instance.Controls.TouchSprint=true;
            if(Mode=="move")
            {
                var zone=(RectTransform)transform;RectTransformUtility.ScreenPointToLocalPointInRectangle(zone,e.position,e.pressEventCamera,out var local);
                var r=zone.rect;float rad=Radius+8;
                local.x=Mathf.Clamp(local.x,r.xMin+rad,r.xMax-rad);local.y=Mathf.Clamp(local.y,r.yMin+rad,r.yMax-rad);
                if(Pad)Pad.anchoredPosition=local;
                if(padImage){var c=padImage.color;c.a=.75f;padImage.color=c;}
                OnDrag(e);
            }
        }
        public void OnDrag(PointerEventData e)
        {
            if(e.pointerId!=finger)return;
            // Fire buttons also steer the view, so one thumb can shoot and aim (mobile-shooter style).
            if(Mode=="look"||Mode=="fire")PulseApp.Instance.Controls.Look(e.delta);
            if(Mode=="move"&&Pad)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(Pad,e.position,e.pressEventCamera,out var point);
                float rad=Radius;Vector2 v=Vector2.ClampMagnitude(point/rad,1);
                if(v.magnitude<.08f)v=Vector2.zero;
                PulseApp.Instance.Controls.TouchMove=v;if(Knob)Knob.anchoredPosition=v*rad;
            }
        }
        public void OnPointerUp(PointerEventData e){if(e.pointerId==finger)ResetPad();}
        void OnDisable(){ResetPad();}
        void ResetPad()
        {
            bool wasDown=finger!=int.MinValue;finger=int.MinValue;
            if(Mode=="fire"&&wasDown)fireHolds=Mathf.Max(0,fireHolds-1);
            if(!PulseApp.Instance)return;var input=PulseApp.Instance.Controls;if(!input)return;
            if(Mode=="fire")input.TouchFire=fireHolds>0;if(Mode=="sprint")input.TouchSprint=false;
            if(Mode=="move"){input.TouchMove=Vector2.zero;GoHome();}
        }
    }
}
