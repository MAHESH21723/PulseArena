using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace PulseArena
{
    public class PulseUI:MonoBehaviour
    {
        readonly Dictionary<string,GameObject> screens=new Dictionary<string,GameObject>();
        readonly List<UnityEngine.UI.Button> connectButtons=new List<UnityEngine.UI.Button>();
        RectTransform safe;
        TMP_FontAsset font;
        TMP_Text status,room,roster,health,ammo,timer,score,notice,death,results,lobbyHint,matchLabel;
        UnityEngine.UI.Image damage,hit,healthFill;
        UnityEngine.UI.Button start,rematch;
        TMP_InputField pilot,code;
        CanvasGroup fade;
        string current;
        Rect lastSafe;
        readonly Color white=new Color(.91f,.97f,1);
        readonly Color muted=new Color(.54f,.66f,.74f);
        readonly Color ink=new Color(.028f,.055f,.084f,.97f);
        PulseApp App=>PulseApp.Instance;

        void Awake()
        {
            font=Resources.Load<TMP_FontAsset>("Fonts/PulseFont");
            var canvas=new GameObject("Pulse UI",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            safe=Rect("Safe area",canvas.transform,Vector2.zero,Vector2.zero,Vector2.zero);safe.anchorMax=Vector2.one;safe.offsetMin=safe.offsetMax=Vector2.zero;
            BuildMenu();BuildLobby();BuildGame();BuildPause();BuildSettings();BuildResults();BuildControls();
        }
        RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pos,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));var rt=go.GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=anchor;rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=pos;rt.sizeDelta=size;return rt;
        }
        RectTransform Full(string name,Transform parent)
        {var r=Rect(name,parent,Vector2.zero,Vector2.zero,Vector2.zero);r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
        UnityEngine.UI.Image Panel(string name,Transform parent,Vector2 anchor,Vector2 pos,Vector2 size,Color color,bool ray=false)
        {var r=Rect(name,parent,anchor,pos,size);var im=r.gameObject.AddComponent<UnityEngine.UI.Image>();im.color=color;im.raycastTarget=ray;return im;}
        TMP_Text Text(string name,Transform parent,string value,Vector2 anchor,Vector2 pos,Vector2 size,float points,Color color,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {var r=Rect(name,parent,anchor,pos,size);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=points;t.color=color;t.alignment=align;t.raycastTarget=false;t.richText=false;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;return t;}
        UnityEngine.UI.Button Button(Transform parent,string value,Vector2 pos,Vector2 size,Action action,bool accent=false)
        {
            var im=Panel(value,parent,new Vector2(.5f,.5f),pos,size,accent?ArenaArt.Cyan:new Color(.09f,.15f,.2f),true);
            var button=im.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=im;var colors=button.colors;colors.highlightedColor=new Color(.8f,.95f,1);colors.pressedColor=new Color(.48f,.7f,.76f);colors.disabledColor=new Color(.35f,.4f,.45f);button.colors=colors;
            Text("Label",im.transform,value,new Vector2(.5f,.5f),Vector2.zero,size-new Vector2(24,4),21,accent?ink:white,TextAlignmentOptions.Center);
            button.onClick.AddListener(()=>{PulseAudio.Instance.Play("click",.6f);action();});return button;
        }
        TMP_InputField Input(Transform parent,string placeholder,string value,Vector2 pos,Vector2 size,int max)
        {
            var bg=Panel(placeholder,parent,new Vector2(.5f,.5f),pos,size,new Color(.025f,.045f,.068f),true);
            var field=bg.gameObject.AddComponent<TMP_InputField>();field.characterLimit=max;field.lineType=TMP_InputField.LineType.SingleLine;
            var area=Rect("Text area",bg.transform,new Vector2(.5f,.5f),Vector2.zero,size-new Vector2(32,4));area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            field.textViewport=area;field.textComponent=(TextMeshProUGUI)Text("Value",area,value,new Vector2(.5f,.5f),Vector2.zero,size-new Vector2(32,4),23,white);
            field.placeholder=Text("Placeholder",area,placeholder,new Vector2(.5f,.5f),Vector2.zero,size-new Vector2(32,4),21,muted);field.fontAsset=font;field.text=value;field.targetGraphic=bg;
            field.caretColor=ArenaArt.Cyan;field.customCaretColor=true;return field;
        }
        GameObject Page(string name,bool backdrop=true)
        {
            var root=Full(name,safe).gameObject;
            if(backdrop){var bg=Full("Shade",root.transform).gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.012f,.025f,.047f,.65f);bg.raycastTarget=true;}
            root.AddComponent<CanvasGroup>();screens[name]=root;root.SetActive(false);return root;
        }
        Transform Card(GameObject root,string title,string sub,float width=700,float height=640)
        {
            var card=Panel("Card",root.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(width,height),ink).transform;
            Panel("Accent",card,new Vector2(.5f,1),new Vector2(0,-2),new Vector2(width,4),ArenaArt.Cyan);
            Text("Title",card,title,new Vector2(.5f,1),new Vector2(0,-62),new Vector2(width-88,55),38,white);
            Text("Subtitle",card,sub,new Vector2(.5f,1),new Vector2(0,-111),new Vector2(width-88,40),18,muted);
            return card;
        }
        void BuildMenu()
        {
            var root=Page("menu",false);
            var shade=Full("Shade",root.transform).gameObject.AddComponent<UnityEngine.UI.Image>();shade.color=new Color(.015f,.03f,.06f,.36f);shade.raycastTarget=false;
            Text("Edition",root.transform,"P / A     •     MULTIPLAYER ARENA",new Vector2(0,1),new Vector2(320,-74),new Vector2(500,40),19,ArenaArt.Cyan);
            Text("Title",root.transform,"PULSE\nARENA",new Vector2(0,.5f),new Vector2(365,88),new Vector2(590,260),112,white).fontStyle=FontStyles.Bold;
            Panel("Rule",root.transform,new Vector2(0,.5f),new Vector2(118,-88),new Vector2(90,4),ArenaArt.Cyan);
            Text("Tagline",root.transform,"CHARGE IN. STAND OUT.",new Vector2(0,.5f),new Vector2(348,-135),new Vector2(550,55),29,white);
            Text("Description",root.transform,"One reactor. Four pilots.\nMake every pulse count.",new Vector2(0,.5f),new Vector2(345,-207),new Vector2(545,85),24,new Color(.7f,.8f,.86f));
            Text("Rules",root.transform,"04 PLAYERS    /    03 MINUTES    /    15 ELIMINATIONS",new Vector2(0,0),new Vector2(390,64),new Vector2(635,42),18,ArenaArt.Cyan);
            var card=Panel("Deploy card",root.transform,new Vector2(1,.5f),new Vector2(-304,0),new Vector2(470,690),ink).transform;
            Panel("Accent",card,new Vector2(.5f,1),new Vector2(0,-2),new Vector2(470,4),ArenaArt.Cyan);
            Text("Ready",card,"READY TO DEPLOY",new Vector2(.5f,.5f),new Vector2(0,280),new Vector2(390,50),30,white);
            Text("Name label",card,"CALL SIGN",new Vector2(.5f,.5f),new Vector2(0,224),new Vector2(390,24),15,muted);
            pilot=Input(card,"PILOT",App.PlayerName,new Vector2(0,180),new Vector2(390,58),16);pilot.onEndEdit.AddListener(App.SaveName);
            connectButtons.Add(Button(card,"CREATE INTERNET ROOM",new Vector2(0,91),new Vector2(390,65),()=>{App.SaveName(pilot.text);App.HostRoom();},true));
            code=Input(card,"ROOM CODE","",new Vector2(-66,13),new Vector2(258,56),12);code.characterValidation=TMP_InputField.CharacterValidation.Alphanumeric;
            connectButtons.Add(Button(card,"JOIN",new Vector2(137,13),new Vector2(115,56),()=>{App.SaveName(pilot.text);App.JoinRoom(code.text);}));
            connectButtons.Add(Button(card,"PRACTICE WITH BOTS",new Vector2(0,-64),new Vector2(390,61),()=>{App.SaveName(pilot.text);App.StartOffline();}));
            Button(card,"SETTINGS",new Vector2(-101,-139),new Vector2(188,50),App.OpenSettings);
            Button(card,"HOW TO PLAY",new Vector2(101,-139),new Vector2(188,50),()=>Show("controls"));
            status=Text("Status",card,"",new Vector2(.5f,.5f),new Vector2(0,-221),new Vector2(390,96),17,ArenaArt.Amber);
            Text("Version",card,"REACTOR 07   /   v"+Application.version+"   •   NO ADS",new Vector2(.5f,.5f),new Vector2(0,-307),new Vector2(390,25),14,muted);
        }
        void BuildLobby()
        {
            var root=Page("lobby");var card=Card(root,"ROOM CONTROL","Share the code with up to three other pilots.",800,710);
            room=Text("Code",card,"------",new Vector2(.5f,.5f),new Vector2(-66,154),new Vector2(500,70),52,ArenaArt.Cyan);
            Button(card,"COPY CODE",new Vector2(227,154),new Vector2(220,54),()=>{GUIUtility.systemCopyBuffer=App.RoomCode;App.Announce("ROOM CODE COPIED");});
            Panel("Divider",card,new Vector2(.5f,.5f),new Vector2(0,93),new Vector2(704,1),new Color(.2f,.3f,.35f));
            roster=Text("Roster",card,"",new Vector2(.5f,.5f),new Vector2(0,-14),new Vector2(704,188),25,white);
            lobbyHint=Text("Hint",card,"",new Vector2(.5f,.5f),new Vector2(0,-148),new Vector2(704,60),19,muted);
            start=Button(card,"START MATCH",new Vector2(139,-230),new Vector2(426,64),App.StartMatch,true);
            Button(card,"LEAVE",new Vector2(-229,-230),new Vector2(248,64),App.Leave);
            Text("Match info",card,"FREE-FOR-ALL  /  180 SECONDS  /  HOST LEAVING ENDS THE ROOM",new Vector2(.5f,.5f),new Vector2(0,-310),new Vector2(704,34),14,muted);
        }
        void BuildGame()
        {
            var root=Page("game",false);var tr=root.transform;
            // The look surface is behind every button, so a fire/reload tap never rotates the view.
            var look=Panel("Look area",tr,new Vector2(.75f,.5f),Vector2.zero,new Vector2(800,900),Color.clear,true);look.gameObject.AddComponent<TouchPad>().Mode="look";
            Panel("Top tint",tr,new Vector2(.5f,1),new Vector2(0,-58),new Vector2(1600,116),new Color(.01f,.03f,.045f,.68f));
            Text("Arena",tr,"REACTOR 07",new Vector2(0,1),new Vector2(180,-38),new Vector2(280,36),24,ArenaArt.Cyan);
            score=Text("Score",tr,"",new Vector2(0,1),new Vector2(190,-78),new Vector2(300,32),20,white);
            timer=Text("Clock",tr,"03:00",new Vector2(.5f,1),new Vector2(0,-42),new Vector2(260,48),36,white,TextAlignmentOptions.Center);
            Text("Goal",tr,"FIRST TO 15",new Vector2(.5f,1),new Vector2(0,-84),new Vector2(260,26),15,muted,TextAlignmentOptions.Center);
            var pause=Button(tr,"PAUSE",Vector2.zero,new Vector2(138,52),App.TogglePause);Anchor(pause.transform,new Vector2(1,1),new Vector2(-110,-54));
            Panel("Crosshair horizontal",tr,new Vector2(.5f,.5f),Vector2.zero,new Vector2(21,2),white);
            Panel("Crosshair vertical",tr,new Vector2(.5f,.5f),Vector2.zero,new Vector2(2,21),white);
            hit=Panel("Hit confirmation",tr,new Vector2(.5f,.5f),Vector2.zero,new Vector2(34,3),ArenaArt.Amber);hit.transform.localRotation=Quaternion.Euler(0,0,45);
            notice=Text("Announcement",tr,"",new Vector2(.5f,.7f),Vector2.zero,new Vector2(800,50),27,ArenaArt.Cyan,TextAlignmentOptions.Center);
            death=Text("Respawn",tr,"",new Vector2(.5f,.5f),new Vector2(0,75),new Vector2(730,115),38,white,TextAlignmentOptions.Center);
            var joy=Panel("Movement",tr,new Vector2(0,0),new Vector2(163,164),new Vector2(188,188),new Color(.07f,.15f,.2f,.6f),true);
            Panel("Guide X",joy.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(130,1),new Color(.5f,.8f,.85f,.3f));
            Panel("Guide Y",joy.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(1,130),new Color(.5f,.8f,.85f,.3f));
            var knob=Panel("Thumb",joy.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(58,58),new Color(.35f,.92f,1,.65f));
            var pad=joy.gameObject.AddComponent<TouchPad>();pad.Mode="move";pad.Knob=knob.rectTransform;
            Text("Move label",joy.transform,"MOVE",new Vector2(.5f,0),new Vector2(0,-25),new Vector2(150,30),16,muted,TextAlignmentOptions.Center);
            Hold(tr,"FIRE",new Vector2(-137,184),new Vector2(146,146),"fire",new Color(.08f,.49f,.57f,.85f));
            Hold(tr,"SPRINT",new Vector2(-310,113),new Vector2(126,66),"sprint",new Color(.07f,.15f,.2f,.7f));
            var reload=Button(tr,"RELOAD",Vector2.zero,new Vector2(146,58),()=>App.Controls.Reload());Anchor(reload.transform,new Vector2(1,0),new Vector2(-137,67));
            health=Text("Shield",tr,"100",new Vector2(.5f,0),new Vector2(-150,72),new Vector2(170,52),36,white);
            Text("Shield label",tr,"SHIELD",new Vector2(.5f,0),new Vector2(-151,112),new Vector2(170,27),14,muted);
            Panel("Health backing",tr,new Vector2(.5f,0),new Vector2(-150,37),new Vector2(170,5),new Color(.2f,.3f,.35f));
            healthFill=Panel("Health fill",tr,new Vector2(.5f,0),new Vector2(-235,37),new Vector2(170,5),ArenaArt.Cyan);healthFill.rectTransform.pivot=new Vector2(0,.5f);
            ammo=Text("Ammo",tr,"24 / INF",new Vector2(.5f,0),new Vector2(104,70),new Vector2(280,52),34,white);
            Text("Weapon label",tr,"PX-24  /  PULSE CARBINE",new Vector2(.5f,0),new Vector2(115,112),new Vector2(300,27),14,muted);
            damage=Full("Damage vignette",tr).gameObject.AddComponent<UnityEngine.UI.Image>();damage.raycastTarget=false;damage.color=Color.clear;
        }
        void Hold(Transform parent,string label,Vector2 pos,Vector2 size,string mode,Color color)
        {var im=Panel(label,parent,new Vector2(1,0),pos,size,color,true);im.gameObject.AddComponent<TouchPad>().Mode=mode;Text("Label",im.transform,label,new Vector2(.5f,.5f),Vector2.zero,size,22,white,TextAlignmentOptions.Center);}
        void Anchor(Transform item,Vector2 anchor,Vector2 pos){var r=(RectTransform)item;r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=pos;}
        void BuildPause()
        {
            var root=Page("pause");var card=Card(root,"TACTICAL BREAK","The match keeps running while this menu is open.",680,590);
            Button(card,"RESUME",new Vector2(0,74),new Vector2(580,66),App.TogglePause,true);
            Button(card,"SETTINGS",new Vector2(0,-10),new Vector2(580,60),App.OpenSettings);
            Button(card,"LEAVE MATCH",new Vector2(0,-92),new Vector2(580,60),App.Leave);
            Text("Keys",card,"WASD move  •  Mouse aim / fire  •  R reload\nShift sprint  •  Escape pause",new Vector2(.5f,.5f),new Vector2(0,-201),new Vector2(580,66),19,muted,TextAlignmentOptions.Center);
        }
        void Slider(Transform card,string label,float y,float low,float high,float value,Action<float> changed)
        {
            Text(label,card,label,new Vector2(.5f,.5f),new Vector2(0,y+35),new Vector2(580,35),22,white);
            var bg=Panel(label+" slider",card,new Vector2(.5f,.5f),new Vector2(0,y),new Vector2(580,18),new Color(.14f,.23f,.29f),true);
            var slider=bg.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=low;slider.maxValue=high;
            var handle=Panel("Handle",bg.transform,Vector2.zero,Vector2.zero,new Vector2(28,36),ArenaArt.Cyan,true);
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;slider.value=value;slider.onValueChanged.AddListener(v=>changed(v));
        }
        void BuildSettings()
        {
            var root=Page("settings");var card=Card(root,"PILOT SETTINGS","Changes are saved on this device.",700,690);
            Slider(card,"MASTER VOLUME",110,0,1,PlayerPrefs.GetFloat("volume",.7f),v=>{AudioListener.volume=v;PlayerPrefs.SetFloat("volume",v);});
            Slider(card,"LOOK SENSITIVITY",0,.3f,2.5f,PlayerPrefs.GetFloat("sensitivity",1),v=>{App.Controls.Sensitivity=v;PlayerPrefs.SetFloat("sensitivity",v);});
            Text("Performance",card,"PERFORMANCE",new Vector2(.5f,.5f),new Vector2(0,-78),new Vector2(580,36),20,white);
            Button(card,"BATTERY  /  30 FPS",new Vector2(-148,-130),new Vector2(284,58),()=>App.ApplyQuality(0));
            Button(card,"SMOOTH  /  60 FPS",new Vector2(148,-130),new Vector2(284,58),()=>App.ApplyQuality(1));
            Button(card,"DONE",new Vector2(0,-249),new Vector2(580,64),()=>{PlayerPrefs.Save();App.CloseSettings();},true);
        }
        void BuildResults()
        {
            var root=Page("results");var card=Card(root,"MATCH COMPLETE","REACTOR 07   /   FINAL STANDINGS",840,710);
            results=Text("Standings",card,"",new Vector2(.5f,.5f),new Vector2(0,10),new Vector2(736,300),29,white);
            rematch=Button(card,"PLAY AGAIN",new Vector2(144,-238),new Vector2(432,65),App.StartMatch,true);
            Button(card,"LEAVE",new Vector2(-241,-238),new Vector2(239,65),App.Leave);
            matchLabel=Text("Host hint",card,"",new Vector2(.5f,.5f),new Vector2(0,-307),new Vector2(736,36),17,muted,TextAlignmentOptions.Center);
        }
        void BuildControls()
        {
            var root=Page("controls");var card=Card(root,"HOW TO PLAY","A short, sharp free-for-all. Every pilot is a rival.",850,740);
            Text("Instructions",card,"WIN THE ROUND\nReach 15 eliminations, or lead when the 3-minute timer ends.\n\nON YOUR PHONE\nLeft pad moves. Drag the right side to aim. Hold FIRE to shoot.\nTap RELOAD to swap cells. Hold SPRINT to move faster.\n\nON YOUR COMPUTER\nWASD moves. Mouse aims. Left-click fires. R reloads.\nHold Shift to sprint. Escape opens the pause menu.\n\nShields return on respawn. You are protected briefly after spawning.",new Vector2(.5f,.5f),new Vector2(0,-9),new Vector2(744,407),21,white);
            Button(card,"GOT IT",new Vector2(0,-285),new Vector2(744,62),()=>Show("menu"),true);
        }
        public void Show(string page)
        {current=page;foreach(var entry in screens)entry.Value.SetActive(entry.Key==page);fade=screens[page].GetComponent<CanvasGroup>();fade.alpha=0;EventSystem.current.SetSelectedGameObject(null);}
        void Update()
        {
            if(Screen.safeArea!=lastSafe){lastSafe=Screen.safeArea;safe.anchorMin=lastSafe.position/new Vector2(Screen.width,Screen.height);safe.anchorMax=(lastSafe.position+lastSafe.size)/new Vector2(Screen.width,Screen.height);safe.offsetMin=safe.offsetMax=Vector2.zero;}
            if(fade)fade.alpha=Mathf.MoveTowards(fade.alpha,1,Time.unscaledDeltaTime*6);
            status.text=App.Status;foreach(var button in connectButtons)button.interactable=!App.Busy;
            if(current=="lobby")
            {
                room.text=App.RoomCode;var players=ArenaMatch.Players();
                roster.text=string.Join("\n",players.Select((p,i)=>(i+1).ToString("00")+"    "+p.CallSign.Value+(p.Bot.Value?"  [BOT]":p.OwnerClientId==0?"  [HOST]":"  [CONNECTED]")));
                bool host=App.Network.IsServer;start.interactable=host&&players.Length>=2;
                lobbyHint.text=App.Notice!=""?App.Notice:host?(players.Length<2?"Waiting for another pilot to join...":"All pilots connected. Start when you are ready."):"Waiting for the host to start the match...";
            }
            if(current=="game")
            {
                var p=ArenaPlayer.Local;var match=ArenaMatch.Instance;if(!p||!match)return;
                int seconds=Mathf.Max(0,(int)Math.Ceiling(match.EndsAt.Value-p.Now));timer.text=(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
                health.text=p.Health.Value.ToString("000");healthFill.rectTransform.sizeDelta=new Vector2(170*p.Health.Value/100f,5);
                ammo.text=p.Reloading?"RECHARGING...":p.Ammo.Value.ToString("00")+" / INF";ammo.fontSize=p.Reloading?22:34;
                score.text="ELIMS  "+p.Kills.Value.ToString("00")+"     DEATHS  "+p.Deaths.Value.ToString("00");notice.text=App.Notice;
                death.text=p.Alive?"":"SHIELD OFFLINE\nRESPAWN IN "+Mathf.Max(1,(int)Math.Ceiling(p.RespawnAt.Value-p.Now));
                hit.color=new Color(1,.65f,.2f,App.HitMarker);damage.color=new Color(1,.12f,.06f,App.DamageFlash*.24f);
            }
            if(current=="results")
            {
                var players=ArenaMatch.Players();results.text=string.Join("\n\n",players.Select((p,i)=>(i==0?"WINNER  ":(i+1).ToString("00")+"        ")+p.CallSign.Value+"     "+p.Kills.Value+" K  /  "+p.Deaths.Value+" D"));
                rematch.interactable=App.Network.IsServer;matchLabel.text=App.Network.IsServer?"Start another round with the same pilots.":"Waiting for the host to start another round.";
            }
        }
    }
}

