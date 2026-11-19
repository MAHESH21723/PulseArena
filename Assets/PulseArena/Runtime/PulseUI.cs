using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PulseArena
{
    /// Menus, lobby, settings and results. Everything is built in code. The in-match HUD lives in PulseHud.
    /// Two canvases: "Pulse UI" (pages + static HUD frames) and "Pulse HUD" (values that change every frame).
    /// Look: bright stylized mobile hero-shooter (sky gradient, chunky orange/white buttons, rounded paper cards, floating ribbons).
    public class PulseUI:MonoBehaviour
    {
        class PlayerRow{public GameObject Go;public Image Bg,Badge,TagBg;public TMP_Text Rank,Name,A,B,C,Tag;}

        readonly Dictionary<string,GameObject> screens=new Dictionary<string,GameObject>();
        readonly List<RectTransform> safeRects=new List<RectTransform>();
        readonly List<Button> connectButtons=new List<Button>();
        readonly List<CanvasGroup> fading=new List<CanvasGroup>();
        Canvas staticCanvas,dynCanvas;
        CanvasScaler staticScaler,dynScaler;
        PulseHud hud;
        TMP_Text status,room,notice,lobbyHint,matchLabel,resultsTitle,modeReason,nearbyEmpty,qualityDesc,nearbyHeader,internetHint;
        Button start,rematch,practice,createButton,modeInternet,modeWifi;
        readonly Button[] roomButtons=new Button[3];
        readonly string[] roomCodes=new string[3];
        readonly PlayerRow[] lobbyRows=new PlayerRow[4];
        readonly PlayerRow[] resultRows=new PlayerRow[4];
        TMP_InputField pilot,code;
        CanvasGroup fade;
        string current;
        Rect lastSafe;int lastW,lastH;Vector2Int lastSim;
        float nextSlow,slide,pop=1;
        bool discovering;
        Action refreshSettings;
        readonly StringBuilder sb=new StringBuilder(512);
        string lastRoster="",lastResults="";
        Image comingSoonBadge;Button joinButton;
        static readonly Color OrangeText=new Color(.86f,.38f,.02f);
        PulseApp App=>PulseApp.Instance;

        void Awake()
        {
            UiKit.LoadFonts();
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            staticCanvas=MakeCanvas("Pulse UI",0,true,out staticScaler);
            dynCanvas=MakeCanvas("Pulse HUD",1,false,out dynScaler);
            var staticRoot=(RectTransform)staticCanvas.transform;var dynRoot=(RectTransform)dynCanvas.transform;
            var staticSafe=SafeRect(staticRoot);var dynSafe=SafeRect(dynRoot);
            // "game" is a full-screen root with its own safe-area child so the crosshair stays at the true screen centre.
            var gameRoot=UiKit.Full("game",staticRoot);gameRoot.gameObject.AddComponent<CanvasGroup>();screens["game"]=gameRoot.gameObject;gameRoot.gameObject.SetActive(false);
            var gameSafe=SafeRect(gameRoot);
            Pages(staticSafe);
            hud=gameObject.AddComponent<PulseHud>();hud.Build(gameRoot,gameSafe,dynRoot,dynSafe,dynCanvas);
            dynCanvas.gameObject.SetActive(false);
            GameApi.ShakeScale=PlayerPrefs.GetInt("shake",1)==1?1f:0f;
            AdaptScreen();
        }
        void Pages(RectTransform safe)
        {
            BuildMenu(safe);BuildLobby(safe);BuildPause(safe);BuildSettings(safe);BuildResults(safe);BuildControls(safe);BuildAbout(safe);
        }
        Canvas MakeCanvas(string name,int order,bool raycast,out CanvasScaler scaler)
        {
            var go=new GameObject(name,typeof(Canvas),typeof(CanvasScaler));
            var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=order;
            if(raycast)go.AddComponent<GraphicRaycaster>();
            scaler=go.GetComponent<CanvasScaler>();UiKit.ConfigureScaler(scaler,.5f);
            return canvas;
        }
        RectTransform SafeRect(Transform parent)
        {var r=UiKit.Full("Safe area",parent);safeRects.Add(r);return r;}

        // ---------------------------------------------------------------- helpers
        GameObject Page(string name,Transform safe,bool backdrop=true)
        {
            var root=UiKit.Full(name,safe).gameObject;
            if(backdrop){var bg=UiKit.Full("Shade",root.transform).gameObject.AddComponent<Image>();bg.color=new Color(.08f,.13f,.36f,.62f);bg.raycastTarget=true;
                // extend the shade past the safe area so a cutout never shows a bright strip
                bg.rectTransform.offsetMin=new Vector2(-600,-600);bg.rectTransform.offsetMax=new Vector2(600,600);}
            root.AddComponent<CanvasGroup>();screens[name]=root;root.SetActive(false);return root;
        }
        /// Paper card with a floating blue ribbon title straddling its top edge. Content is positioned from the card's top edge (anchor .5,1).
        Transform Card(GameObject root,string title,string sub,float width,float height,out TMP_Text titleText)
        {
            var card=UiKit.Card("Card",root.transform,new Vector2(.5f,.5f),new Vector2(0,-14),new Vector2(width,height),UiKit.Paper,44,true,.5f);
            float rw=Mathf.Min(width-140,660);
            var ribbon=UiKit.Card("Ribbon",card,new Vector2(.5f,1),new Vector2(0,-4),new Vector2(rw,92),Color.white,34,true);
            UiKit.Gradient2(ribbon.Find("Body").GetComponent<Image>(),new Color(.42f,.74f,1),new Color(.2f,.42f,.96f));
            UiKit.Outline(ribbon,new Color(1,1,1,.55f),34);
            titleText=UiKit.Text("Title",ribbon,title,new Vector2(.5f,.5f),new Vector2(0,2),new Vector2(rw-40,80),46,Color.white,TextAlignmentOptions.Center,true);
            UiKit.Text("Subtitle",card,sub,new Vector2(.5f,1),new Vector2(0,-92),new Vector2(width-80,34),24,UiKit.Slate,TextAlignmentOptions.Center);
            return card;
        }
        Transform Card(GameObject root,string title,string sub,float width,float height){return Card(root,title,sub,width,height,out _);}
        Button Btn(Transform parent,string name,string label,Vector2 pos,Vector2 size,Action action,bool accent=false,UiKit.Sfx sfx=UiKit.Sfx.Click,float points=28,Vector2? anchor=null)
        {return UiKit.MakeButton(parent,name,label,anchor??new Vector2(.5f,1),pos,size,action,accent,accent&&sfx==UiKit.Sfx.Click?UiKit.Sfx.Confirm:sfx,points);}
        Button Btn(Transform parent,string name,string label,Vector2 pos,Vector2 size,Action action,UiKit.BtnStyle style,UiKit.Sfx sfx=UiKit.Sfx.Click,float points=28,Vector2? anchor=null)
        {return UiKit.MakeButton(parent,name,label,anchor??new Vector2(.5f,1),pos,size,action,style,sfx,points);}
        TMP_Text Tl(string name,Transform parent,string value,Vector2 pos,Vector2 size,float points,Color color,bool bold,Vector2? anchor=null,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {
            var a=anchor??new Vector2(0,.5f);var t=UiKit.Text(name,parent,value,a,pos,size,points,color,align,bold);
            if(a.x==0)t.rectTransform.pivot=new Vector2(0,.5f);return t;
        }
        TMP_InputField Input(Transform parent,string placeholder,string value,Vector2 pos,Vector2 size,int max)
        {
            var bg=UiKit.RImg(placeholder,parent,new Vector2(.5f,1),pos,size,Color.white,size.y*.32f,true);
            UiKit.Outline(bg.transform,new Color(.45f,.6f,.95f,.9f),size.y*.32f);
            var field=bg.gameObject.AddComponent<TMP_InputField>();field.characterLimit=max;field.lineType=TMP_InputField.LineType.SingleLine;field.transition=Selectable.Transition.None;
            var inner=size-new Vector2(36,6);
            var area=UiKit.Rect("Text area",bg.transform,new Vector2(.5f,.5f),Vector2.zero,inner);area.gameObject.AddComponent<RectMask2D>();
            field.textViewport=area;field.textComponent=(TextMeshProUGUI)UiKit.Text("Value",area,value,new Vector2(.5f,.5f),Vector2.zero,inner,30,UiKit.Navy,TextAlignmentOptions.MidlineLeft,true);
            field.placeholder=UiKit.Text("Placeholder",area,placeholder,new Vector2(.5f,.5f),Vector2.zero,inner,26,new Color(.55f,.64f,.82f),TextAlignmentOptions.MidlineLeft,true);
            field.fontAsset=UiKit.Bold;field.text=value;field.targetGraphic=bg;field.caretColor=UiKit.SkyDeep;field.customCaretColor=true;field.selectionColor=new Color(.4f,.6f,1,.35f);return field;
        }
        PlayerRow MakeRow(Transform parent,string name,float y,float w,float h,bool stats)
        {
            var r=new PlayerRow();var half=new Vector2(.5f,.5f);
            r.Bg=UiKit.RImg(name,parent,new Vector2(.5f,1),new Vector2(0,y),new Vector2(w,h),Color.white,h*.32f);r.Go=r.Bg.gameObject;
            UiKit.Gradient2(r.Bg,UiKit.PaperDeep,UiKit.PaperDeep);
            float b=h-14;
            r.Badge=UiKit.Img("Badge",r.Go.transform,half,new Vector2(-w*.5f+h*.5f+2,0),new Vector2(b,b),UiKit.Sky,false,UiKit.Circle);
            r.Rank=UiKit.Text("Rank",r.Badge.transform,"1",half,Vector2.zero,new Vector2(b,b),28,Color.white,TextAlignmentOptions.Center,true);
            float nameW=w*.5f;r.Name=UiKit.Text("Name",r.Go.transform,"",half,new Vector2(-w*.5f+h+24+nameW*.5f,0),new Vector2(nameW,h),30,UiKit.Navy,TextAlignmentOptions.MidlineLeft,true);
            if(stats)
            {
                r.A=UiKit.Text("Stat A",r.Go.transform,"",half,new Vector2(w*.5f-260,0),new Vector2(90,h),30,UiKit.Navy,TextAlignmentOptions.Center,true);
                r.B=UiKit.Text("Stat B",r.Go.transform,"",half,new Vector2(w*.5f-160,0),new Vector2(90,h),30,UiKit.Navy,TextAlignmentOptions.Center,true);
                r.C=UiKit.Text("Stat C",r.Go.transform,"",half,new Vector2(w*.5f-60,0),new Vector2(100,h),30,UiKit.Navy,TextAlignmentOptions.Center,true);
            }
            else
            {
                r.TagBg=UiKit.Pill("Tag",r.Go.transform,half,new Vector2(w*.5f-96,0),new Vector2(150,40),UiKit.SkyDeep);
                r.Tag=UiKit.Text("Tag label",r.TagBg.transform,"",half,Vector2.zero,new Vector2(150,40),22,Color.white,TextAlignmentOptions.Center,true);
            }
            return r;
        }
        static void RowColor(PlayerRow r,Color top,Color bottom){UiKit.Gradient2(r.Bg,top,bottom);}

        // ---------------------------------------------------------------- menu
        void BuildMenu(Transform safe)
        {
            var root=Page("menu",safe,false);var rt=root.transform;
            // sky backdrop with soft floating shapes (extends past the safe area)
            var sky=UiKit.Img("Backdrop",rt,Vector2.zero,Vector2.zero,Vector2.zero,Color.white,true);
            sky.rectTransform.anchorMax=Vector2.one;sky.rectTransform.offsetMin=new Vector2(-600,-600);sky.rectTransform.offsetMax=new Vector2(600,600);
            UiKit.Gradient2(sky,new Color(.34f,.72f,1,.95f),new Color(.52f,.38f,.98f,.95f));
            Decor(rt,new Vector2(.12f,.85f),620,0,new Color(1,1,1,.10f),true,0);
            Decor(rt,new Vector2(.78f,.1f),760,0,new Color(1,1,1,.09f),true,1.5f);
            Decor(rt,new Vector2(.5f,.55f),360,25,new Color(1,.9f,.4f,.16f),false,3);
            Decor(rt,new Vector2(.05f,.2f),220,-18,new Color(1,1,1,.10f),false,2);
            Decor(rt,new Vector2(.93f,.88f),180,30,new Color(1,1,1,.12f),false,4);

            // ---- left: logo, chips, big play button
            UiKit.Pill("Edition chip",rt,new Vector2(0,.5f),new Vector2(70+210,355),new Vector2(420,48),new Color(1,1,1,.92f));
            UiKit.Text("Edition",rt,"4-PLAYER ARENA BRAWL",new Vector2(0,.5f),new Vector2(70+210,355),new Vector2(400,48),24,UiKit.SkyDeep,TextAlignmentOptions.Center,true);
            var logo=UiKit.Rect("Logo",rt,new Vector2(0,.5f),new Vector2(0,0),new Vector2(10,10));logo.localRotation=Quaternion.Euler(0,0,2.5f);
            var bob=logo.gameObject.AddComponent<UiBob>();bob.Amplitude=new Vector2(0,7);bob.Speed=1.6f;bob.Rotate=1.2f;
            UiKit.ChunkyText("PULSE",logo,"PULSE",new Vector2(0,.5f),new Vector2(70,215),new Vector2(760,190),170,UiKit.Sun,new Color(.55f,.2f,.02f),12);
            UiKit.ChunkyText("ARENA",logo,"ARENA",new Vector2(0,.5f),new Vector2(78,60),new Vector2(760,190),170,Color.white,new Color(.13f,.2f,.6f),12);
            Tl("Tagline",rt,"CHARGE IN. STAND OUT.",new Vector2(76,-70),new Vector2(760,56),40,Color.white,true);
            string[] chips={"4 PLAYERS","3 MINUTES","15 KILLS"};float[] cw={200,220,190};float cx=76;
            for(int i=0;i<chips.Length;i++)
            {
                var chip=UiKit.Pill("Chip "+i,rt,new Vector2(0,.5f),new Vector2(cx+cw[i]*.5f,-135),new Vector2(cw[i],50),new Color(.10f,.16f,.42f,.55f));
                UiKit.Outline(chip.transform,new Color(1,1,1,.55f),25);
                UiKit.Text("Chip text",chip.transform,chips[i],new Vector2(.5f,.5f),Vector2.zero,new Vector2(cw[i],50),24,Color.white,TextAlignmentOptions.Center,true);cx+=cw[i]+14;
            }
            practice=Btn(rt,"PRACTICE WITH BOTS","PLAY VS BOTS",new Vector2(76+270,-265),new Vector2(540,140),()=>{App.SaveName(pilot.text);App.StartOffline();},UiKit.BtnStyle.Primary,UiKit.Sfx.Confirm,54,new Vector2(0,.5f));
            var pb=practice.gameObject.AddComponent<UiBob>();pb.Amplitude=new Vector2(0,5);pb.Speed=2.6f;
            Tl("Play hint",rt,"Solo match  /  3 bots  /  no waiting",new Vector2(80,-370),new Vector2(700,36),24,new Color(1,1,1,.92f),false);
            Tl("Version",rt,"v"+Application.version+"   /   Made by Maheshwaran   /   NO ADS",new Vector2(76,-420),new Vector2(700,30),22,new Color(1,1,1,.8f),false);

            // ---- top-right icon buttons
            UiKit.MakeIconButton(rt,"SETTINGS",UiSprites.Gear,null,new Vector2(1,1),new Vector2(-176,-62),new Vector2(88,88),App.OpenSettings,UiKit.BtnStyle.Secondary);
            UiKit.MakeIconButton(rt,"HOW TO PLAY",null,"?",new Vector2(1,1),new Vector2(-66,-62),new Vector2(88,88),()=>Show("controls"),UiKit.BtnStyle.Secondary);

            // ---- right: online card
            var card=UiKit.Card("Online card",rt,new Vector2(1,.5f),new Vector2(-50-300,-34),new Vector2(600,740),UiKit.Paper,44,true,.5f);
            UiKit.Text("Header",card,"PLAY ONLINE",new Vector2(.5f,1),new Vector2(0,-52),new Vector2(540,60),42,UiKit.Navy,TextAlignmentOptions.Center,true);
            UiKit.Text("Name label",card,"CALL SIGN",new Vector2(.5f,1),new Vector2(0,-104),new Vector2(540,28),22,UiKit.Slate,TextAlignmentOptions.MidlineLeft,true);
            pilot=Input(card,"PILOT",App.PlayerName,new Vector2(0,-150),new Vector2(540,64),16);pilot.onEndEdit.AddListener(App.SaveName);
            UiKit.Pill("Mode track",card,new Vector2(.5f,1),new Vector2(0,-236),new Vector2(540,72),UiKit.PaperDeep);
            modeInternet=Btn(card,"INTERNET","INTERNET",new Vector2(-133,-236),new Vector2(262,58),()=>SetMode(OnlineMode.Internet),UiKit.BtnStyle.Segment,UiKit.Sfx.Click,24);
            comingSoonBadge=UiKit.Pill("Coming soon badge",card,new Vector2(.5f,1),new Vector2(-133,-206),new Vector2(150,26),new Color(1f,.76f,.12f));
            UiKit.Text("Badge text",comingSoonBadge.transform,"COMING SOON",new Vector2(.5f,.5f),Vector2.zero,new Vector2(150,26),15,UiKit.Navy,TextAlignmentOptions.Center,true);
            modeWifi=Btn(card,"SAME WI-FI","SAME WI-FI",new Vector2(133,-236),new Vector2(262,58),()=>SetMode(OnlineMode.LocalWifi),UiKit.BtnStyle.Segment,UiKit.Sfx.Click,24);
            createButton=Btn(card,"CREATE INTERNET ROOM","CREATE INTERNET ROOM",new Vector2(0,-330),new Vector2(540,88),()=>{App.SaveName(pilot.text);App.HostRoom();},UiKit.BtnStyle.Success,UiKit.Sfx.Confirm,30);connectButtons.Add(createButton);
            code=Input(card,"ROOM CODE","",new Vector2(-105,-430),new Vector2(330,64),12);code.characterValidation=TMP_InputField.CharacterValidation.Alphanumeric;
            joinButton=Btn(card,"JOIN","JOIN",new Vector2(175,-430),new Vector2(190,64),()=>{App.SaveName(pilot.text);App.JoinRoom(code.text);},UiKit.BtnStyle.Blue,UiKit.Sfx.Confirm,28);connectButtons.Add(joinButton);
            nearbyHeader=UiKit.Text("Nearby header",card,"NEARBY ROOMS",new Vector2(.5f,1),new Vector2(0,-500),new Vector2(540,28),22,UiKit.Slate,TextAlignmentOptions.MidlineLeft,true);
            modeReason=UiKit.Text("Internet reason",card,"",new Vector2(.5f,1),new Vector2(0,-555),new Vector2(540,160),28,OrangeText,TextAlignmentOptions.Top,true);modeReason.rectTransform.pivot=new Vector2(.5f,1);modeReason.rectTransform.anchoredPosition=new Vector2(0,-340);
            internetHint=UiKit.Text("Internet hint",card,"Create a room and share its code with friends, or type a friend's code and tap JOIN.",new Vector2(.5f,1),new Vector2(0,-520),new Vector2(540,100),23,UiKit.Slate,TextAlignmentOptions.TopLeft);internetHint.rectTransform.pivot=new Vector2(.5f,1);
            nearbyEmpty=UiKit.Text("Nearby empty",card,"Searching for rooms on this Wi-Fi...",new Vector2(.5f,1),new Vector2(0,-520),new Vector2(540,60),23,UiKit.Slate,TextAlignmentOptions.TopLeft);nearbyEmpty.rectTransform.pivot=new Vector2(.5f,1);
            for(int i=0;i<roomButtons.Length;i++)
            {
                int index=i;
                roomButtons[i]=Btn(card,"Room "+i,"",new Vector2(0,-548-i*58),new Vector2(540,52),()=>{if(roomCodes[index]!=null){App.SaveName(pilot.text);App.JoinRoom(roomCodes[index]);}},UiKit.BtnStyle.Secondary,UiKit.Sfx.Click,24);
                roomButtons[i].gameObject.SetActive(false);connectButtons.Add(roomButtons[i]);
            }
            status=UiKit.Text("Status",card,"",new Vector2(.5f,1),new Vector2(0,-704),new Vector2(540,40),22,OrangeText,TextAlignmentOptions.Center,true);
            RefreshMode();
        }
        void Decor(Transform parent,Vector2 anchor,float size,float rotation,Color color,bool circle,float phase)
        {
            var im=circle?UiKit.Img("Decor",parent,anchor,Vector2.zero,new Vector2(size,size),color,false,UiKit.Circle):UiKit.RImg("Decor",parent,anchor,Vector2.zero,new Vector2(size,size),color,size*.22f);
            im.rectTransform.localRotation=Quaternion.Euler(0,0,rotation);
            var b=im.gameObject.AddComponent<UiBob>();b.Amplitude=new Vector2(10,16);b.Speed=.5f;b.Phase=phase;b.Rotate=rotation!=0?4:0;
        }
        void SetMode(OnlineMode mode){GameApi.Mode=mode;RefreshMode();UpdateDiscovery();}
        void RefreshMode()
        {
            bool wifi=GameApi.Mode==OnlineMode.LocalWifi;
            UiKit.SetSelected(modeInternet,!wifi);UiKit.SetSelected(modeWifi,wifi);
            UiKit.SetLabel(createButton,wifi?"CREATE WI-FI ROOM":"CREATE INTERNET ROOM");
            bool blocked=!wifi&&!GameApi.InternetAvailable;
            if(comingSoonBadge)comingSoonBadge.gameObject.SetActive(!GameApi.InternetAvailable);
            modeReason.gameObject.SetActive(blocked);if(blocked)UiKit.Set(modeReason,"Internet matches are coming in a future update.\nPlay with friends on the same Wi-Fi now!");
            // while internet play is not available, hide the controls that cannot work
            createButton.gameObject.SetActive(!blocked);
            if(code)code.gameObject.SetActive(!blocked);
            if(joinButton)joinButton.gameObject.SetActive(!blocked);
            nearbyHeader.gameObject.SetActive(!blocked);
            internetHint.gameObject.SetActive(!wifi&&!blocked);
            nearbyEmpty.gameObject.SetActive(wifi);
            if(!wifi)foreach(var b in roomButtons)b.gameObject.SetActive(false);
            UiKit.Set(nearbyHeader,wifi?"NEARBY ROOMS":blocked?"INTERNET":"HOW IT WORKS");
        }
        void UpdateDiscovery()
        {
            bool want=current=="menu"&&GameApi.Mode==OnlineMode.LocalWifi;
            if(want==discovering)return;discovering=want;
            try{if(want)GameApi.StartDiscovery();else GameApi.StopDiscovery();}catch(Exception e){Debug.LogWarning("Discovery: "+e.Message);}
        }
        void RefreshNearby()
        {
            if(GameApi.Mode!=OnlineMode.LocalWifi)return;
            var list=GameApi.NearbyRooms;int shown=0;float now=Time.realtimeSinceStartup;
            for(int i=0;list!=null&&i<list.Count&&shown<roomButtons.Length;i++)
            {
                var r=list[i];if(now-r.LastSeen>8f)continue;
                roomCodes[shown]=r.Code;
                UiKit.SetLabel(roomButtons[shown],UiKit.Clean(r.HostName)+"   "+r.Code+"   "+r.Players+"/4");
                roomButtons[shown].gameObject.SetActive(true);shown++;
            }
            for(int i=shown;i<roomButtons.Length;i++){roomCodes[i]=null;roomButtons[i].gameObject.SetActive(false);}
            nearbyEmpty.gameObject.SetActive(shown==0);
        }

        // ---------------------------------------------------------------- lobby, pause, results, controls
        void BuildLobby(Transform safe)
        {
            var root=Page("lobby",safe);var card=Card(root,"ROOM CONTROL","Share the code with up to three other pilots.",920,760);
            UiKit.Text("Code label",card,"ROOM CODE",new Vector2(.5f,1),new Vector2(-150,-146),new Vector2(480,26),22,UiKit.Slate,TextAlignmentOptions.Center,true);
            var panel=UiKit.RImg("Code panel",card,new Vector2(.5f,1),new Vector2(-150,-208),new Vector2(480,96),UiKit.PaperDeep,30);UiKit.Outline(panel.transform,new Color(.45f,.6f,.95f,.8f),30);
            room=UiKit.Text("Code",panel.transform,"------",new Vector2(.5f,.5f),Vector2.zero,new Vector2(460,90),68,UiKit.SkyDeep,TextAlignmentOptions.Center,true);
            Btn(card,"COPY CODE","COPY CODE",new Vector2(250,-208),new Vector2(240,80),()=>{GUIUtility.systemCopyBuffer=App.RoomCode;App.Announce("ROOM CODE COPIED");},UiKit.BtnStyle.Blue,UiKit.Sfx.Click,26);
            for(int i=0;i<lobbyRows.Length;i++)
            {
                lobbyRows[i]=MakeRow(card,"Slot "+i,-296-i*70,820,62,false);
                lobbyRows[i].Badge.color=UiKit.PlayerColors[i%4];UiKit.Set(lobbyRows[i].Rank,(i+1).ToString());
            }
            lobbyHint=UiKit.Text("Hint",card,"",new Vector2(.5f,1),new Vector2(0,-590),new Vector2(820,40),24,UiKit.Slate,TextAlignmentOptions.Center,true);
            start=Btn(card,"START MATCH","START MATCH",new Vector2(160,-650),new Vector2(460,80),App.StartMatch,true,UiKit.Sfx.Confirm,32);
            Btn(card,"LEAVE","LEAVE",new Vector2(-260,-650),new Vector2(260,80),App.Leave,UiKit.BtnStyle.Secondary,UiKit.Sfx.Back,28);
            UiKit.Text("Match info",card,"FREE-FOR-ALL  /  180 SECONDS  /  HOST LEAVING ENDS THE ROOM",new Vector2(.5f,1),new Vector2(0,-722),new Vector2(860,30),22,UiKit.Slate,TextAlignmentOptions.Center);
        }
        void BuildPause(Transform safe)
        {
            var root=Page("pause",safe);var card=Card(root,"TACTICAL BREAK","The match keeps running while this menu is open.",720,610);
            Btn(card,"RESUME","RESUME",new Vector2(0,-205),new Vector2(580,92),App.TogglePause,true,UiKit.Sfx.Back,36);
            Btn(card,"SETTINGS","SETTINGS",new Vector2(0,-310),new Vector2(580,76),App.OpenSettings,UiKit.BtnStyle.Secondary,UiKit.Sfx.Click,30);
            Btn(card,"LEAVE MATCH","LEAVE MATCH",new Vector2(0,-398),new Vector2(580,76),App.Leave,UiKit.BtnStyle.Danger,UiKit.Sfx.Back,30);
            UiKit.Text("Keys",card,"WASD move  /  Mouse aim, fire  /  R reload\nQ, 1, 2, wheel swap  /  Shift sprint  /  Esc pause",new Vector2(.5f,1),new Vector2(0,-512),new Vector2(660,80),22,UiKit.Slate,TextAlignmentOptions.Center);
        }
        void BuildResults(Transform safe)
        {
            var root=Page("results",safe);var card=Card(root,"MATCH COMPLETE","REACTOR 07   /   FINAL STANDINGS",1000,770,out resultsTitle);
            float w=880;
            UiKit.Text("Header",card,"PILOT",new Vector2(.5f,1),new Vector2(-w*.5f+130+120,-152),new Vector2(300,30),22,UiKit.Slate,TextAlignmentOptions.MidlineLeft,true).rectTransform.anchoredPosition=new Vector2(-w*.5f+110,-152);
            string[] heads={"K","D","K/D"};float[] hx={w*.5f-260,w*.5f-160,w*.5f-60};
            for(int i=0;i<3;i++)UiKit.Text("Header "+heads[i],card,heads[i],new Vector2(.5f,1),new Vector2(hx[i],-152),new Vector2(100,30),22,UiKit.Slate,TextAlignmentOptions.Center,true);
            for(int i=0;i<resultRows.Length;i++)resultRows[i]=MakeRow(card,"Place "+(i+1),-225-i*84,w,72,true);
            rematch=Btn(card,"PLAY AGAIN","PLAY AGAIN",new Vector2(170,-630),new Vector2(500,86),App.StartMatch,true,UiKit.Sfx.Confirm,34);
            Btn(card,"LEAVE","LEAVE",new Vector2(-290,-630),new Vector2(280,86),App.Leave,UiKit.BtnStyle.Secondary,UiKit.Sfx.Back,30);
            matchLabel=UiKit.Text("Host hint",card,"",new Vector2(.5f,1),new Vector2(0,-714),new Vector2(860,34),22,UiKit.Slate,TextAlignmentOptions.Center);
        }
        void BuildControls(Transform safe)
        {
            var root=Page("controls",safe);var card=Card(root,"HOW TO PLAY","A short, sharp free-for-all. Every pilot is a rival.",1100,800);
            const string h="<color=#2F5BE0>";
            var t=UiKit.Text("Instructions",card,h+"WIN THE ROUND</color>\nReach 15 eliminations, or lead when the 3-minute timer ends.\n\n"+h+"ON YOUR PHONE</color>\nTouch the left side to move (the pad appears under your thumb). Drag the right side to aim. Hold FIRE to shoot. RELOAD swaps cells, SWAP changes weapon, hold SPRINT to run. Tap the clock for live scores.\n\n"+h+"ON YOUR COMPUTER</color>\nWASD moves, mouse aims, left-click fires. R reloads, Q / 1 / 2 / wheel swaps weapons, Shift sprints, click the clock for scores, Esc pauses.\n\nShields return on respawn. You are protected briefly after spawning.",new Vector2(.5f,1),new Vector2(0,-140),new Vector2(1000,560),25,UiKit.Navy,TextAlignmentOptions.TopLeft);
            t.rectTransform.pivot=new Vector2(.5f,1);t.enableAutoSizing=true;t.fontSizeMax=25;t.fontSizeMin=22;
            Btn(card,"GOT IT","GOT IT",new Vector2(-170,-738),new Vector2(340,84),()=>Show("menu"),true,UiKit.Sfx.Back,34);
            Btn(card,"ABOUT","ABOUT / CREDITS",new Vector2(190,-738),new Vector2(420,84),()=>Show("about"),UiKit.BtnStyle.Secondary,UiKit.Sfx.Click,28);
        }
        const string ContactEmail="kmaheshwaran9965@gmail.com";
        void BuildAbout(Transform safe)
        {
            var root=Page("about",safe);var card=Card(root,"ABOUT","Pulse Arena  /  v"+Application.version,1100,800);
            const string h="<color=#2F5BE0>";
            var t=UiKit.Text("Credits",card,h+"GAME DESIGN & DEVELOPMENT</color>\nMaheshwaran\n\n"+h+"ART</color>\nKenney & Quaternius (CC0)\n\n"+h+"SOUND</color>\nCC0 sources (Kenney, OpenGameArt: bart, SpringySpringo, BMacZero). See ThirdParty/Audio/CREDITS.md\n\n"+h+"FONT</color>\nPlay (SIL Open Font License)\n\n"+h+"CONTACT</color>",new Vector2(.5f,1),new Vector2(0,-140),new Vector2(1000,430),25,UiKit.Navy,TextAlignmentOptions.TopLeft);
            t.rectTransform.pivot=new Vector2(.5f,1);t.enableAutoSizing=true;t.fontSizeMax=25;t.fontSizeMin=20;
            Btn(card,"EMAIL",ContactEmail,new Vector2(0,-610),new Vector2(700,70),()=>Application.OpenURL("mailto:"+ContactEmail),UiKit.BtnStyle.Blue,UiKit.Sfx.Click,28);
            Btn(card,"BACK","BACK",new Vector2(0,-728),new Vector2(560,84),()=>Show("controls"),true,UiKit.Sfx.Back,34);
        }

        // ---------------------------------------------------------------- settings
        void BuildSettings(Transform safe)
        {
            var root=Page("settings",safe);var card=Card(root,"PILOT SETTINGS","Changes are saved on this device.",1200,790);
            float L=-290,R=290;
            var left=UiKit.RImg("Audio card",card,new Vector2(.5f,1),new Vector2(L,-148+(-250)),new Vector2(550,500),UiKit.PaperDeep,32);
            var right=UiKit.RImg("Play card",card,new Vector2(.5f,1),new Vector2(R,-148+(-250)),new Vector2(550,500),UiKit.PaperDeep,32);
            left.color=new Color(.9f,.94f,1);right.color=new Color(.9f,.94f,1);
            UiKit.Outline(left.transform,new Color(.55f,.68f,.95f,.6f),32);UiKit.Outline(right.transform,new Color(.55f,.68f,.95f,.6f),32);
            SliderRow(card,"MASTER VOLUME",L,-196,0,1,PulseAudio.MasterVolume,v=>(int)(v*100)+"%",v=>PulseAudio.MasterVolume=v);
            SliderRow(card,"EFFECTS VOLUME",L,-300,0,1,PulseAudio.SfxVolume,v=>(int)(v*100)+"%",v=>PulseAudio.SfxVolume=v);
            SliderRow(card,"LOOK SENSITIVITY",L,-404,.3f,2.5f,PlayerPrefs.GetFloat("sensitivity",1),v=>v.ToString("0.0")+"x",v=>{App.Controls.Sensitivity=v;PlayerPrefs.SetFloat("sensitivity",v);});
            SliderRow(card,"HUD SCALE",L,-508,.8f,1.3f,PulseDisplay.HudScale,v=>(int)(v*100)+"%",v=>PulseDisplay.HudScale=v);

            Label(card,"QUALITY",R,-188);
            var q=SegmentRow(card,R,-246,new[]{"BATTERY","BALANCED","SMOOTH"},()=>PulseDisplay.Quality,i=>{App.ApplyQuality(i);UpdateQualityText();});
            qualityDesc=UiKit.Text("Quality note",card,"",new Vector2(.5f,1),new Vector2(R,-300),new Vector2(490,30),22,UiKit.Slate,TextAlignmentOptions.Center);UpdateQualityText();
            Label(card,"BOT DIFFICULTY",R,-352);
            var d=SegmentRow(card,R,-410,new[]{"EASY","NORMAL","HARD"},()=>(int)PulseSettings.Difficulty,i=>PulseSettings.Difficulty=(BotDifficulty)i);
            var vib=ToggleRow(card,"VIBRATION",R,-468,()=>PlayerPrefs.GetInt(PulseSettings.Haptics,1)==1,on=>{PlayerPrefs.SetInt(PulseSettings.Haptics,on?1:0);});
            var shake=ToggleRow(card,"SCREEN SHAKE",R,-528,()=>PlayerPrefs.GetInt("shake",1)==1,on=>{PlayerPrefs.SetInt("shake",on?1:0);GameApi.ShakeScale=on?1f:0f;});
            Btn(card,"DONE","DONE",new Vector2(0,-716),new Vector2(560,80),()=>{PlayerPrefs.Save();App.CloseSettings();},true,UiKit.Sfx.Back,34);
            var assist=ToggleRow(card,"AIM ASSIST",L,-598,()=>PlayerPrefs.GetFloat("aimAssist",1)>.5f,on=>PlayerPrefs.SetFloat("aimAssist",on?1f:0f));
            var autoFire=ToggleRow(card,"AUTO FIRE",R,-588,()=>PlayerPrefs.GetInt("autoFire",0)==1,on=>PlayerPrefs.SetInt("autoFire",on?1:0));
            refreshSettings=()=>{q();d();vib();shake();assist();autoFire();UpdateQualityText();};
        }
        void UpdateQualityText(){if(qualityDesc)UiKit.Set(qualityDesc,PulseDisplay.Quality==0?"30 FPS  /  720p  /  no shadows":PulseDisplay.Quality==1?"60 FPS  /  900p  /  shadows":"60 FPS  /  1080p  /  shadows + MSAA");}
        void Label(Transform card,string text,float x,float y)
        {UiKit.Text(text,card,text,new Vector2(.5f,1),new Vector2(x,y),new Vector2(490,34),26,UiKit.Navy,TextAlignmentOptions.MidlineLeft,true);}
        void SliderRow(Transform card,string label,float x,float y,float low,float high,float value,Func<float,string> format,Action<float> changed)
        {
            UiKit.Text(label,card,label,new Vector2(.5f,1),new Vector2(x-80,y),new Vector2(320,34),26,UiKit.Navy,TextAlignmentOptions.MidlineLeft,true);
            var readout=UiKit.Text("Value",card,format(value),new Vector2(.5f,1),new Vector2(x+185,y),new Vector2(110,34),26,UiKit.SkyDeep,TextAlignmentOptions.MidlineRight,true);
            const float rowH=64,knob=44;
            var hit=UiKit.Img(label+" slider",card,new Vector2(.5f,1),new Vector2(x,y-54),new Vector2(490,rowH),Color.clear,true);
            var track=UiKit.Pill("Track",hit.transform,new Vector2(.5f,.5f),Vector2.zero,Vector2.zero,new Color(.74f,.82f,.96f));
            track.rectTransform.anchorMin=new Vector2(0,.5f);track.rectTransform.anchorMax=new Vector2(1,.5f);track.rectTransform.offsetMin=new Vector2(6,-9);track.rectTransform.offsetMax=new Vector2(-6,9);
            track.pixelsPerUnitMultiplier=UiSprites.Corner/9f;
            var fillArea=UiKit.Rect("Fill Area",hit.transform,new Vector2(0,.5f),Vector2.zero,Vector2.zero);fillArea.anchorMin=new Vector2(0,.5f);fillArea.anchorMax=new Vector2(1,.5f);fillArea.offsetMin=new Vector2(knob*.5f,-9);fillArea.offsetMax=new Vector2(-knob*.5f,9);
            var fill=UiKit.RImg("Fill",fillArea,Vector2.zero,Vector2.zero,Vector2.zero,Color.white,9);fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
            UiKit.Gradient2(fill,UiKit.Sun,UiKit.Orange);
            var area=UiKit.Rect("Handle Slide Area",hit.transform,Vector2.zero,Vector2.zero,Vector2.zero);area.anchorMin=Vector2.zero;area.anchorMax=Vector2.one;area.offsetMin=new Vector2(knob*.5f,0);area.offsetMax=new Vector2(-knob*.5f,0);
            // Slider stretches the handle to the full row height (anchors y 0..1), so sizeDelta.y is relative: rowH-knob is removed => knob tall.
            var handle=UiKit.Img("Handle",area,new Vector2(.5f,.5f),Vector2.zero,new Vector2(knob,knob-rowH),UiKit.Orange,true,UiKit.Circle);
            var core=UiKit.Img("Core",handle.transform,Vector2.zero,Vector2.zero,Vector2.zero,Color.white,false,UiKit.Circle);core.rectTransform.anchorMax=Vector2.one;core.rectTransform.offsetMin=new Vector2(7,7);core.rectTransform.offsetMax=new Vector2(-7,-7);
            var slider=hit.gameObject.AddComponent<Slider>();slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.transition=Selectable.Transition.None;slider.direction=Slider.Direction.LeftToRight;
            slider.minValue=low;slider.maxValue=high;slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v=>{changed(v);UiKit.Set(readout,format(v));});
        }
        Action SegmentRow(Transform card,float x,float y,string[] labels,Func<int> get,Action<int> set)
        {
            var buttons=new Button[labels.Length];float inner=478,gap=6,w=(inner-gap*(labels.Length-1))/labels.Length;
            UiKit.Pill("Segments",card,new Vector2(.5f,1),new Vector2(x,y),new Vector2(490,70),new Color(.8f,.87f,.98f));
            Action refresh=()=>{for(int i=0;i<buttons.Length;i++)UiKit.SetSelected(buttons[i],i==get());};
            for(int i=0;i<labels.Length;i++)
            {
                int index=i;float cx=x-inner*.5f+w*.5f+i*(w+gap);
                buttons[i]=Btn(card,labels[i],labels[i],new Vector2(cx,y),new Vector2(w,56),()=>{set(index);refresh();},UiKit.BtnStyle.Segment,UiKit.Sfx.Click,22);
            }
            refresh();return refresh;
        }
        Action ToggleRow(Transform card,string label,float x,float y,Func<bool> get,Action<bool> set)
        {
            UiKit.Text(label,card,label,new Vector2(.5f,1),new Vector2(x-100,y),new Vector2(290,40),26,UiKit.Navy,TextAlignmentOptions.MidlineLeft,true);
            Button button=null;
            Action refresh=()=>UiKit.SetSwitch(button,get(),false);
            button=UiKit.MakeSwitch(card,label+" toggle",new Vector2(.5f,1),new Vector2(x+170,y),new Vector2(150,56),()=>{set(!get());refresh();});
            UiKit.SetSwitch(button,get(),true);return refresh;
        }

        // ---------------------------------------------------------------- pages and transitions
        public void Show(string page)
        {
            if(!screens.ContainsKey(page))return;
            string previous=current;current=page;
            foreach(var entry in screens)
            {
                var go=entry.Value;var group=go.GetComponent<CanvasGroup>();
                if(entry.Key==page)
                {
                    fading.Remove(group);go.SetActive(true);group.alpha=0;group.blocksRaycasts=true;group.interactable=true;fade=group;slide=page=="game"?0:24;
                    pop=page=="game"?1:0;go.transform.localScale=page=="game"?Vector3.one:Vector3.one*.9f;
                    ((RectTransform)go.transform).anchoredPosition=new Vector2(0,-slide);
                }
                else if(entry.Key==previous&&go.activeSelf&&previous!="game"&&page!="game")
                {
                    group.blocksRaycasts=false;group.interactable=false;if(!fading.Contains(group))fading.Add(group);
                }
                else {fading.Remove(group);go.SetActive(false);}
            }
            dynCanvas.gameObject.SetActive(page=="game");
            if(page=="game")hud.OnShown();
            if(page=="settings"&&refreshSettings!=null)refreshSettings();
            if(page=="menu")RefreshMode();
            PulseDisplay.SetMatchActive(GameApi.InRound);
            UpdateDiscovery();
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
        }

        void AdaptScreen()
        {
            lastW=Screen.width;lastH=Screen.height;lastSim=PulseDisplay.ScreenSize;
            // ScreenSize honours PulseDisplay.SimulateResolution, so QA captures get the same match as the real device.
            float match=PulseDisplay.MatchFor((float)lastSim.x/Mathf.Max(lastSim.y,1));
            staticScaler.matchWidthOrHeight=match;dynScaler.matchWidthOrHeight=match;
            lastSafe=Screen.safeArea;
            var min=lastSafe.position/new Vector2(lastW,lastH);var max=(lastSafe.position+lastSafe.size)/new Vector2(lastW,lastH);
            foreach(var r in safeRects){r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;}
            PulseDisplay.ApplyQuality(PulseDisplay.Quality);   // re-derive render scale from the new height
            PulseDisplay.Refresh();
        }

        static float EaseOutBack(float t){t=Mathf.Clamp01(t)-1;return 1+2.2f*t*t*t+1.2f*t*t;}

        void Update()
        {
            if(Screen.width!=lastW||Screen.height!=lastH||Screen.safeArea!=lastSafe||PulseDisplay.ScreenSize!=lastSim)AdaptScreen();
            float dt=Time.unscaledDeltaTime;
            if(fade)
            {
                fade.alpha=Mathf.MoveTowards(fade.alpha,1,dt*7);slide=Mathf.MoveTowards(slide,0,dt*160);
                if(current!="game")
                {
                    var frt=(RectTransform)fade.transform;frt.anchoredPosition=new Vector2(0,-slide);
                    if(pop<1){pop=Mathf.MoveTowards(pop,1,dt*4.5f);frt.localScale=Vector3.one*Mathf.LerpUnclamped(.9f,1,EaseOutBack(pop));}
                }
            }
            for(int i=fading.Count-1;i>=0;i--)
            {
                var g=fading[i];g.alpha=Mathf.MoveTowards(g.alpha,0,dt*10);
                if(g.alpha<=0){g.gameObject.SetActive(false);g.alpha=1;g.blocksRaycasts=true;g.interactable=true;((RectTransform)g.transform).anchoredPosition=Vector2.zero;g.transform.localScale=Vector3.one;fading.RemoveAt(i);}
            }
            if(current==null)return;
            if(current=="menu")
            {
                UiKit.Set(status,App.Status);
                bool wifi=GameApi.Mode==OnlineMode.LocalWifi,blocked=!wifi&&!GameApi.InternetAvailable;
                foreach(var b in connectButtons)b.interactable=!App.Busy&&!(blocked&&(b==createButton||b.name=="JOIN"));
                practice.interactable=!App.Busy;
            }
            if(Time.unscaledTime<nextSlow)return;
            nextSlow=Time.unscaledTime+.25f;
            if(current=="menu")RefreshNearby();
            if(current=="lobby")
            {
                room.text=App.RoomCode;var players=ArenaMatch.Players();
                sb.Length=0;for(int i=0;i<players.Length;i++){var p=players[i];sb.Append(p.CallSign.Value.ToString()).Append(p.Bot.Value?"B":p.OwnerClientId==0?"H":"C").Append('|');}
                string text=sb.ToString();
                if(text!=lastRoster)
                {
                    lastRoster=text;
                    for(int i=0;i<lobbyRows.Length;i++)
                    {
                        var row=lobbyRows[i];
                        if(i<players.Length)
                        {
                            var p=players[i];bool bot=p.Bot.Value,host=p.OwnerClientId==0&&!bot;
                            row.Name.text=UiKit.Clean(p.CallSign.Value.ToString());row.Name.color=UiKit.Navy;row.Badge.color=UiKit.PlayerColors[i%4];
                            row.TagBg.gameObject.SetActive(true);row.Tag.text=bot?"BOT":host?"HOST":"PILOT";row.TagBg.color=bot?UiKit.Slate:host?UiKit.Orange:UiKit.SkyDeep;
                            RowColor(row,Color.white,UiKit.PaperDeep);
                        }
                        else
                        {
                            row.Name.text="Waiting for pilot...";row.Name.color=new Color(.6f,.68f,.84f);row.Badge.color=new Color(.76f,.82f,.94f);row.TagBg.gameObject.SetActive(false);
                            RowColor(row,new Color(.93f,.96f,1),new Color(.93f,.96f,1));
                        }
                    }
                }
                bool amHost=App.Network.IsServer;start.interactable=amHost&&players.Length>=2;
                UiKit.Set(lobbyHint,App.Notice!=""?App.Notice:amHost?(players.Length<2?"Waiting for another pilot to join...":"All pilots connected. Start when you are ready."):"Waiting for the host to start the match...");
            }
            if(current=="results")
            {
                var players=ArenaMatch.Players();var local=ArenaPlayer.Local;
                sb.Length=0;
                for(int i=0;i<players.Length;i++){var p=players[i];sb.Append(i).Append(p.CallSign.Value.ToString()).Append(p.Kills.Value).Append('/').Append(p.Deaths.Value).Append(p==local?"*":"").Append('|');}
                string text=sb.ToString();
                if(text!=lastResults)
                {
                    lastResults=text;
                    for(int i=0;i<resultRows.Length;i++)
                    {
                        var row=resultRows[i];
                        if(i>=players.Length){row.Go.SetActive(false);continue;}
                        row.Go.SetActive(true);
                        var p=players[i];bool winner=i==0,me=p==local;int k=p.Kills.Value,d=p.Deaths.Value;
                        row.Name.text=UiKit.Clean(p.CallSign.Value.ToString())+(winner?"   <color=#D9480F>WINNER</color>":me?"   <color=#2F5BE0>YOU</color>":"");
                        row.Rank.text=(i+1).ToString();row.A.text=k.ToString();row.B.text=d.ToString();row.C.text=(k/(float)Mathf.Max(1,d)).ToString("0.0");
                        row.Badge.color=winner?UiKit.Orange:UiKit.PlayerColors[i%4];
                        if(winner)RowColor(row,new Color(1,.93f,.52f),new Color(1,.8f,.3f));
                        else if(me)RowColor(row,new Color(.8f,.9f,1),new Color(.66f,.8f,1));
                        else RowColor(row,Color.white,UiKit.PaperDeep);
                    }
                }
                UiKit.Set(resultsTitle,players.Length>0&&players[0]==local?"VICTORY!":"MATCH COMPLETE");
                rematch.interactable=App.Network.IsServer;UiKit.Set(matchLabel,App.Network.IsServer?"Start another round with the same pilots.":"Waiting for the host to start another round.");
            }
        }
    }
}
