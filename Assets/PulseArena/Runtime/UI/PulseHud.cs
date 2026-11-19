using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PulseArena
{
    /// In-match HUD (Free-Fire style layout). Static frames (touch controls, tracks) live on the static canvas; every value that changes
    /// lives on a separate dynamic canvas. Text is only rewritten when the underlying value changes.
    /// Pieces: HudCrosshair, HitMarker, DamageNumbers, KillFeed (UI/Hud/).
    public class PulseHud : MonoBehaviour
    {
        static PulseApp App => PulseApp.Instance;
        const int Arcs = 6;
        const string LeftFireKey = "leftFireButton";
        static readonly string[] NumberText = BuildNumbers();
        static string[] BuildNumbers() { var a = new string[201]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }

        /// Second, smaller fire button on the left side (Free Fire "left fire"). Default on. Settings UI may bind to this.
        public static bool LeftFire
        {
            get => PlayerPrefs.GetInt(LeftFireKey, 1) == 1;
            set { PlayerPrefs.SetInt(LeftFireKey, value ? 1 : 0); PulseDisplay.Refresh(); }
        }

        static readonly Color FireColor = new Color(1f, .42f, .1f), FireRim = new Color(.4f, .1f, .02f, .95f);
        static readonly Color ReloadColor = new Color(.18f, .6f, 1f), ReloadRim = new Color(.03f, .15f, .4f, .95f);
        static readonly Color SprintColor = new Color(.2f, .82f, .38f), SprintRim = new Color(.03f, .3f, .1f, .95f);
        static readonly Color Gold = new Color(1f, .82f, .2f);

        Canvas dynCanvas;
        RectTransform dynFull, dynSafe;
        HudLayout layout;
        bool layoutValid;

        // static refs
        RectTransform fireRt, fireLeftRt, sprintRt, reloadRt, swapRt, pauseRt, padRt, thumbRt, timerBtnRt, clusterStatic;
        TouchPad moveZone, fireTouch, fireLeftTouch, sprintTouch;
        RectTransform zoneRt, lookRt;

        // dynamic refs
        RectTransform clusterDyn, ammoDyn, scoreRt, timerRt, scoreboardRt, shieldFill, reloadFill;
        TMP_Text score, timer, shieldValue, shieldLabel, weaponLabel, ammoText, altText, scoreboardText, countdown, streakText, banner, announce, toast, deathText;
        Image damageVignette, lowVignette, deathPanel, shieldFillImg, reloadFillImg;
        ScoreboardButton scoreboardButton;

        readonly HudCrosshair crosshair = new HudCrosshair();
        readonly HitMarker hitMarker = new HitMarker();
        readonly DamageNumbers numbers = new DamageNumbers();
        readonly KillFeed killFeed = new KillFeed();

        class Arc { public Image Image; public float Until; public Vector3 From; }
        readonly Arc[] arcs = new Arc[Arcs];
        int arcNext;

        // state
        float damageFlash, shieldShown = 100, countdownT, bannerT, toastT, scoreboardNext, lastKillTime, lastShotTime, lastHitSound;
        Vector3 lastShotTo;
        int streak, lastSeconds = -1, lastKills = -1, lastDeaths = -1, lastHealth = -1, lastMag = -1, lastReserve = -1, lastWeapon = -1, lastAltMag = -1, lastAltReserve = -1, lastRespawn = -1;
        bool firstBloodDone, lowActive, lastReloading, lastSwapping;
        string killer = "", killerWeapon = "";
        readonly StringBuilder sb = new StringBuilder(256);
        string lastScoreboard = "";

        // ---------------------------------------------------------------- build
        public void Build(RectTransform gameRoot, RectTransform gameSafe, RectTransform dynFullRect, RectTransform dynSafeRect, Canvas dynamicCanvas)
        {
            dynCanvas = dynamicCanvas; dynFull = dynFullRect; dynSafe = dynSafeRect;
            BuildStatic(gameRoot, gameSafe);
            BuildDynamic();
            for (int i = 0; i < Arcs; i++) arcs[i].Image.gameObject.SetActive(false);
            PulseDisplay.Changed += InvalidateLayout;
        }
        void OnDestroy() { PulseDisplay.Changed -= InvalidateLayout; }
        void InvalidateLayout() { layoutValid = false; }

        static void ClickSound() { if (PulseAudio.Instance) PulseAudio.Instance.PlayUi("ui_click", .8f); }

        TMP_Text Glyph(RectTransform parent, string glyph, string caption, float glyphPts, float capPts)
        {
            var g = UiKit.Text("Glyph", parent, glyph, new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 20), glyphPts, Color.white, TextAlignmentOptions.Center, true);
            HudKit.Frac(g, 0, .34f, 1, .92f);
            var c = UiKit.Text("Caption", parent, caption, new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 14), capPts, new Color(1, 1, 1, .92f), TextAlignmentOptions.Center, true);
            HudKit.Frac(c, 0, .1f, 1, .36f);
            return g;
        }

        void FireIcon(RectTransform parent)
        {
            var white = new Color(1, 1, 1, .95f);
            HudKit.Frac(UiKit.Img("Icon ring", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white, false, UiKit.Ring), .28f, .28f, .72f, .72f);
            HudKit.Frac(UiKit.Img("Icon dot", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white, false, UiKit.Circle), .45f, .45f, .55f, .55f);
            HudKit.Frac(UiKit.Img("Tick N", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white), .485f, .70f, .515f, .84f);
            HudKit.Frac(UiKit.Img("Tick S", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white), .485f, .16f, .515f, .30f);
            HudKit.Frac(UiKit.Img("Tick W", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white), .16f, .485f, .30f, .515f);
            HudKit.Frac(UiKit.Img("Tick E", parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, white), .70f, .485f, .84f, .515f);
        }

        void BuildStatic(RectTransform full, RectTransform safe)
        {
            // Touch surfaces are first so every button sits above them. Look = right 60%, floating move = left 40%.
            lookRt = UiKit.Img("Look area", safe, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear, true).rectTransform;
            lookRt.anchorMin = new Vector2(.4f, 0); lookRt.anchorMax = Vector2.one; lookRt.offsetMin = lookRt.offsetMax = Vector2.zero;
            lookRt.gameObject.AddComponent<TouchPad>().Mode = "look";

            zoneRt = UiKit.Img("Movement", safe, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear, true).rectTransform;
            zoneRt.anchorMin = Vector2.zero; zoneRt.anchorMax = new Vector2(.4f, 1); zoneRt.offsetMin = zoneRt.offsetMax = Vector2.zero;
            var pad = UiKit.Img("Pad", zoneRt, new Vector2(.5f, .5f), Vector2.zero, new Vector2(200, 200), new Color(1, 1, 1, .4f), false, UiKit.Ring);
            padRt = pad.rectTransform;
            HudKit.Frac(UiKit.Img("Fill", padRt, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(.05f, .1f, .2f, .35f), false, UiKit.Circle), 0, 0, 1, 1);
            var thumb = UiKit.Img("Thumb", padRt, new Vector2(.5f, .5f), Vector2.zero, new Vector2(70, 70), new Color(1, 1, 1, .8f), false, UiKit.Circle);
            thumbRt = thumb.rectTransform;
            UiKit.Img("Thumb rim", thumbRt, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(.2f, .6f, 1f, .9f), false, UiKit.Ring).rectTransform.anchorMax = Vector2.one;
            var tr = thumbRt.Find("Thumb rim").GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.offsetMin = tr.offsetMax = Vector2.zero;
            moveZone = zoneRt.gameObject.AddComponent<TouchPad>(); moveZone.Mode = "move"; moveZone.Pad = padRt; moveZone.Knob = thumbRt;

            // Top bar
            var tint = UiKit.Img("Top tint", full, new Vector2(.5f, 1), Vector2.zero, new Vector2(0, 110), new Color(.01f, .03f, .06f, .4f));
            tint.rectTransform.anchorMin = new Vector2(0, 1); tint.rectTransform.anchorMax = new Vector2(1, 1); tint.rectTransform.pivot = new Vector2(.5f, 1); tint.rectTransform.offsetMin = new Vector2(0, -110); tint.rectTransform.offsetMax = Vector2.zero;

            var timerBg = UiKit.Img("SCOREBOARD", safe, new Vector2(.5f, 1), Vector2.zero, new Vector2(300, 90), new Color(.05f, .1f, .2f, .75f), true);
            timerBtnRt = timerBg.rectTransform; timerBtnRt.pivot = new Vector2(.5f, 1);
            UiKit.Img("Accent", timerBtnRt, new Vector2(.5f, 0), Vector2.zero, new Vector2(300, 4), Gold);
            UiKit.Text("Goal", timerBtnRt, "FIRST TO " + ArenaMatch.Goal + "   /   TAP FOR SCORES", new Vector2(.5f, 0), new Vector2(0, 20), new Vector2(290, 26), 18, UiKit.Muted, TextAlignmentOptions.Center);
            scoreboardButton = timerBg.gameObject.AddComponent<ScoreboardButton>();
            timerBg.gameObject.AddComponent<PressFeedback>();

            var pause = UiKit.MakeButton(safe, "PAUSE", "PAUSE", new Vector2(1, 1), Vector2.zero, new Vector2(150, 64), () => App.TogglePause(), false, UiKit.Sfx.Click, 24);
            pauseRt = (RectTransform)pause.transform;

            // Fire buttons (touch pad mode "fire"); the big one right, the small optional one left
            var fire = HudKit.Round(safe, "FIRE", FireColor, FireRim); fireRt = fire.rectTransform;
            fireTouch = fire.gameObject.AddComponent<TouchPad>(); fireTouch.Mode = "fire"; fire.gameObject.AddComponent<PressFeedback>();
            FireIcon(fireRt);
            var fireL = HudKit.Round(safe, "FIRE LEFT", new Color(1f, .5f, .15f), FireRim); fireLeftRt = fireL.rectTransform;
            fireLeftTouch = fireL.gameObject.AddComponent<TouchPad>(); fireLeftTouch.Mode = "fire"; fireL.gameObject.AddComponent<PressFeedback>();
            FireIcon(fireLeftRt);

            var sprint = HudKit.Round(safe, "SPRINT", SprintColor, SprintRim); sprintRt = sprint.rectTransform;
            sprintTouch = sprint.gameObject.AddComponent<TouchPad>(); sprintTouch.Mode = "sprint"; sprint.gameObject.AddComponent<PressFeedback>();
            Glyph(sprintRt, ">>", "SPRINT", 38, 15);

            var reload = HudKit.Round(safe, "RELOAD", ReloadColor, ReloadRim); reloadRt = reload.rectTransform;
            var rb = reload.gameObject.AddComponent<Button>(); rb.targetGraphic = reload; rb.transition = Selectable.Transition.None;
            rb.onClick.AddListener(() => { ClickSound(); App.Controls.Reload(); });
            reload.gameObject.AddComponent<PressFeedback>();
            Glyph(reloadRt, "R", "RELOAD", 40, 15);

            // Weapon / ammo panel above FIRE: taps swap weapons
            var swap = UiKit.Img("SWAP", safe, new Vector2(1, 0), Vector2.zero, new Vector2(240, 80), new Color(.05f, .1f, .22f, .86f), true);
            swapRt = swap.rectTransform;
            var sbtn = swap.gameObject.AddComponent<Button>(); sbtn.targetGraphic = swap; sbtn.transition = Selectable.Transition.None;
            sbtn.onClick.AddListener(() => { ClickSound(); GameApi.SwapWeapon(); });
            swap.gameObject.AddComponent<PressFeedback>();
            var accent = UiKit.Img("Accent", swapRt, new Vector2(.5f, 1), Vector2.zero, Vector2.zero, Gold); HudKit.Frac(accent, 0, 0, 1, 0);
            accent.rectTransform.anchorMin = new Vector2(0, 1); accent.rectTransform.anchorMax = Vector2.one; accent.rectTransform.offsetMin = new Vector2(0, -4);
            var track = UiKit.Img("Reload track", swapRt, new Vector2(.5f, 0), Vector2.zero, Vector2.zero, new Color(0, 0, 0, .45f)); HudKit.Frac(track, 0, 0, 1, 0);
            track.rectTransform.offsetMax = new Vector2(0, 6);
            UiKit.Text("Hint", swapRt, "SWAP", new Vector2(1, 1), new Vector2(-8, -12), new Vector2(60, 12), 12, new Color(1, 1, 1, .55f), TextAlignmentOptions.TopRight, true);

            // Bottom-centre shield bar frame
            clusterStatic = UiKit.Rect("Cluster frame", safe, new Vector2(.5f, 0), Vector2.zero, new Vector2(420, 76));
            clusterStatic.pivot = new Vector2(.5f, 0);
            var back = UiKit.Img("Shield track", clusterStatic, new Vector2(.5f, 0), Vector2.zero, Vector2.zero, new Color(.02f, .05f, .12f, .8f));
            back.rectTransform.anchorMin = Vector2.zero; back.rectTransform.anchorMax = new Vector2(1, 0); back.rectTransform.pivot = new Vector2(.5f, 0); back.rectTransform.offsetMin = new Vector2(0, 0); back.rectTransform.offsetMax = new Vector2(0, 26);
        }

        void BuildDynamic()
        {
            // full-screen effects
            damageVignette = UiKit.Img("Damage vignette", dynFull, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear, false, UiKit.Vignette); HudKit.Stretch(damageVignette.rectTransform);
            lowVignette = UiKit.Img("Low shield vignette", dynFull, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear, false, UiKit.Vignette); HudKit.Stretch(lowVignette.rectTransform);
            for (int i = 0; i < Arcs; i++)
                arcs[i] = new Arc { Image = UiKit.Img("Damage arc", dynFull, new Vector2(.5f, .5f), Vector2.zero, new Vector2(420, 420), new Color(1, .2f, .12f, 0), false, UiKit.Arc) };

            crosshair.Build(dynFull);
            hitMarker.Build(dynFull);
            numbers.Build(dynFull, 12);

            countdown = UiKit.Text("Countdown", dynFull, "", new Vector2(.5f, .62f), Vector2.zero, new Vector2(500, 220), 170, UiKit.Cyan, TextAlignmentOptions.Center, true);
            banner = UiKit.Text("Elimination banner", dynFull, "", new Vector2(.5f, .73f), Vector2.zero, new Vector2(1000, 130), 66, Gold, TextAlignmentOptions.Center, true);
            streakText = UiKit.Text("Streak", dynFull, "", new Vector2(.5f, .64f), Vector2.zero, new Vector2(1000, 60), 44, new Color(1, .45f, .15f), TextAlignmentOptions.Center, true);
            announce = UiKit.Text("Announcement", dynFull, "", new Vector2(.5f, .85f), Vector2.zero, new Vector2(1000, 50), 30, UiKit.Cyan, TextAlignmentOptions.Center);
            toast = UiKit.Text("Pickup toast", dynFull, "", new Vector2(.5f, .30f), Vector2.zero, new Vector2(600, 50), 34, UiKit.Cyan, TextAlignmentOptions.Center, true);

            deathPanel = UiKit.Img("Respawn panel", dynFull, new Vector2(.5f, .5f), Vector2.zero, new Vector2(820, 230), new Color(.01f, .02f, .03f, .62f));
            deathText = UiKit.Text("Respawn", deathPanel.transform, "", new Vector2(.5f, .5f), Vector2.zero, new Vector2(780, 210), 36, UiKit.White, TextAlignmentOptions.Center);
            deathPanel.gameObject.SetActive(false);

            // safe-area items
            score = UiKit.Text("Score", dynSafe, "", new Vector2(0, 1), Vector2.zero, new Vector2(520, 36), 28, UiKit.White, TextAlignmentOptions.TopLeft, true); score.rectTransform.pivot = new Vector2(0, 1); scoreRt = score.rectTransform;
            timer = UiKit.Text("Clock", dynSafe, "03:00", new Vector2(.5f, 1), Vector2.zero, new Vector2(300, 56), 48, UiKit.White, TextAlignmentOptions.Center, true); timer.rectTransform.pivot = new Vector2(.5f, 1); timerRt = timer.rectTransform;

            var sbBack = UiKit.Img("Scoreboard panel", dynSafe, new Vector2(.5f, 1), Vector2.zero, new Vector2(620, 240), new Color(.02f, .05f, .08f, .9f));
            sbBack.rectTransform.pivot = new Vector2(.5f, 1); scoreboardRt = sbBack.rectTransform;
            UiKit.Img("Accent", scoreboardRt, new Vector2(.5f, 1), Vector2.zero, new Vector2(620, 3), Gold);
            scoreboardText = UiKit.Text("Scores", scoreboardRt, "", new Vector2(.5f, .5f), Vector2.zero, new Vector2(580, 210), 26, UiKit.White, TextAlignmentOptions.TopLeft);
            sbBack.gameObject.SetActive(false);
            scoreboardButton.Changed = open => { scoreboardRt.gameObject.SetActive(open); scoreboardNext = 0; };

            killFeed.Build(dynSafe);

            // Weapon / ammo panel values (mirrors the static SWAP panel rect)
            ammoDyn = UiKit.Rect("Ammo panel", dynSafe, new Vector2(1, 0), Vector2.zero, new Vector2(240, 80));
            weaponLabel = HudKit.Frac(UiKit.Text("Weapon label", ammoDyn, "PX-24", new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 20), 24, Gold, TextAlignmentOptions.MidlineLeft, true), .06f, .42f, .42f, .92f);
            ammoText = HudKit.Frac(UiKit.Text("Ammo", ammoDyn, "24 / 96", new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 20), 40, UiKit.White, TextAlignmentOptions.MidlineRight, true), .3f, .38f, .96f, .98f);
            altText = HudKit.Frac(UiKit.Text("Alt weapon", ammoDyn, "SX-8   6 / 18", new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 14), 19, UiKit.Muted, TextAlignmentOptions.MidlineLeft, true), .06f, .1f, .96f, .42f);
            reloadFillImg = UiKit.Img("Reload fill", ammoDyn, new Vector2(0, 0), Vector2.zero, Vector2.zero, UiKit.Amber);
            reloadFill = reloadFillImg.rectTransform; reloadFill.anchorMin = Vector2.zero; reloadFill.anchorMax = new Vector2(1, 0); reloadFill.pivot = new Vector2(0, .5f);
            reloadFill.offsetMin = Vector2.zero; reloadFill.offsetMax = new Vector2(0, 6); reloadFill.localScale = new Vector3(0, 1, 1);

            clusterDyn = UiKit.Rect("Cluster", dynSafe, new Vector2(.5f, 0), Vector2.zero, new Vector2(420, 76)); clusterDyn.pivot = new Vector2(.5f, 0);
            shieldLabel = HudKit.Frac(UiKit.Text("Shield label", clusterDyn, "SHIELD", new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 20), 24, UiKit.Muted, TextAlignmentOptions.BottomLeft, true), 0, .38f, .5f, 1);
            shieldValue = HudKit.Frac(UiKit.Text("Shield", clusterDyn, "100", new Vector2(.5f, .5f), Vector2.zero, new Vector2(80, 20), 44, UiKit.White, TextAlignmentOptions.BottomRight, true), .5f, .3f, 1, 1.05f);
            shieldFillImg = UiKit.Img("Shield fill", clusterDyn, new Vector2(0, 0), Vector2.zero, Vector2.zero, UiKit.Cyan);
            shieldFill = shieldFillImg.rectTransform; shieldFill.anchorMin = Vector2.zero; shieldFill.anchorMax = new Vector2(1, 0); shieldFill.pivot = new Vector2(0, .5f);
            shieldFill.offsetMin = new Vector2(4, 4); shieldFill.offsetMax = new Vector2(-4, 22);
        }

        // ---------------------------------------------------------------- layout
        void ApplyLayout()
        {
            layout = PulseDisplay.Current; layoutValid = true;
            var L = layout; float m = L.Margin, gap = 14, S = L.Small, W = L.SmallWide;
            var br = new Vector2(1, 0); var mid = new Vector2(.5f, .5f);

            float F = L.Fire * 1.1f, D = Mathf.Max(S * 1.05f, 64);
            float fcx = -(m + F * .5f), fcy = m + F * .5f + 8;
            HudKit.Place(fireRt, br, mid, new Vector2(fcx, fcy), new Vector2(F, F));
            float r = F * .5f + D * .5f + gap;
            HudKit.Place(reloadRt, br, mid, new Vector2(fcx + Mathf.Cos(160 * Mathf.Deg2Rad) * r, fcy + Mathf.Sin(160 * Mathf.Deg2Rad) * r), new Vector2(D, D));
            HudKit.Place(sprintRt, br, mid, new Vector2(fcx + Mathf.Cos(208 * Mathf.Deg2Rad) * r, fcy + Mathf.Sin(208 * Mathf.Deg2Rad) * r), new Vector2(D, D));
            float panelW = Mathf.Max(F * 1.5f, 230), panelH = Mathf.Max(S * 1.15f, 78);
            var panelPos = new Vector2(fcx - (panelW - F) * .5f, fcy + F * .5f + gap + panelH * .5f);
            HudKit.Place(swapRt, br, mid, panelPos, new Vector2(panelW, panelH));
            HudKit.Place(ammoDyn, br, mid, panelPos, new Vector2(panelW, panelH));

            bool left = LeftFire;
            fireLeftRt.gameObject.SetActive(left);
            float FL = F * .64f;
            HudKit.Place(fireLeftRt, Vector2.zero, mid, new Vector2(m + FL * .5f, m + L.Pad + 30 + FL * .5f), new Vector2(FL, FL));

            HudKit.Place(pauseRt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-m, -14), new Vector2(W, S * .9f));
            HudKit.Place(timerBtnRt, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -6), new Vector2(300, 90));
            HudKit.Place(timerRt, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -10), new Vector2(300, 56));
            HudKit.Place(scoreRt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(m, -14), new Vector2(520, 36));
            HudKit.Place(scoreboardRt, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -102), new Vector2(620, 240));
            HudKit.Place(killFeed.Root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-m, -(14 + S * .9f + 10)), Vector2.zero);
            killFeed.Layout();

            // Shield bar between the two thumb blocks
            float rightExt = m + F * .5f + Mathf.Abs(Mathf.Cos(160 * Mathf.Deg2Rad)) * r + D * .5f;
            float safeW = dynSafe.rect.width > 10 ? dynSafe.rect.width : L.CanvasSize.x;
            float cw = Mathf.Clamp(safeW - 2 * (Mathf.Max(L.LeftBlock, rightExt) + 16), 200, 420);
            HudKit.Place(clusterStatic, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, m * .5f), new Vector2(cw, 76));
            HudKit.Place(clusterDyn, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, m * .5f), new Vector2(cw, 76));

            padRt.sizeDelta = new Vector2(L.Pad, L.Pad); thumbRt.sizeDelta = Vector2.one * (L.Pad * .38f);
            moveZone.HomeMargin = m + 4; moveZone.GoHome();
        }

        // ---------------------------------------------------------------- events
        void OnEnable()
        {
            PulseEvents.Damaged += OnDamaged; PulseEvents.Eliminated += OnEliminated; PulseEvents.PickedUp += OnPickup; PulseEvents.Shot += OnShot;
            PulseEvents.PhaseChanged += OnPhase; PulseEvents.Countdown += OnCountdown; PulseEvents.Respawned += OnRespawned;
        }
        void OnDisable()
        {
            PulseEvents.Damaged -= OnDamaged; PulseEvents.Eliminated -= OnEliminated; PulseEvents.PickedUp -= OnPickup; PulseEvents.Shot -= OnShot;
            PulseEvents.PhaseChanged -= OnPhase; PulseEvents.Countdown -= OnCountdown; PulseEvents.Respawned -= OnRespawned;
        }

        void OnPhase(int phase)
        {
            if (phase == 0 || phase == 3) { streak = 0; firstBloodDone = false; killFeed.Clear(); numbers.Clear(); killer = ""; countdownT = 0; bannerT = 0; banner.text = ""; streakText.text = ""; }
        }
        void OnCountdown(int seconds)
        {
            countdown.text = seconds >= 3 ? "3" : seconds == 2 ? "2" : seconds == 1 ? "1" : "GO";
            countdown.color = seconds == 0 ? UiKit.Amber : UiKit.Cyan; countdownT = seconds == 0 ? .8f : 1f;
        }
        void OnRespawned(ArenaPlayer p) { if (p == ArenaPlayer.Local) { killer = ""; shieldShown = 0; } }
        void OnPickup(ArenaPlayer p, PickupKind kind, Vector3 at)
        {
            if (p != ArenaPlayer.Local) return;
            toast.text = kind == PickupKind.Shield ? "+50 SHIELD" : "+AMMO"; toast.color = kind == PickupKind.Shield ? UiKit.Cyan : UiKit.Amber; toastT = 1.4f;
        }

        void OnShot(ArenaPlayer shooter, WeaponId weapon, Vector3 from, Vector3 to, bool hit)
        {
            if (shooter != ArenaPlayer.Local) return;
            crosshair.Kick(WeaponDef.Get((int)weapon).Spread > 0 ? 12f : 6f);
            lastShotTo = to; lastShotTime = Time.unscaledTime;
        }

        void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from)
        {
            var local = ArenaPlayer.Local; if (!local) return;
            if (victim == local)
            {
                damageFlash = 1;
                if (attacker && attacker != victim) { var a = arcs[arcNext]; arcNext = (arcNext + 1) % Arcs; a.From = from; a.Until = Time.unscaledTime + 1f; a.Image.gameObject.SetActive(true); }
            }
            else if (attacker == local && victim)
            {
                hitMarker.Show(false);
                Vector3 world = victim.transform.position + Vector3.up * 1.5f;
                if (Time.unscaledTime - lastShotTime < .35f && (lastShotTo - world).sqrMagnitude < 6f) world = lastShotTo;   // hit position of the shot
                numbers.Add(victim, amount, world);
                float now = Time.unscaledTime;
                if (now - lastHitSound > .06f && PulseAudio.Instance) { lastHitSound = now; PulseAudio.Instance.PlayUi("hit_confirm", 1); }
            }
        }

        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId weapon)
        {
            var local = ArenaPlayer.Local; float now = Time.unscaledTime;
            string label = WeaponDef.Get((int)weapon).Label;
            string a = attacker ? UiKit.Clean(attacker.CallSign.Value.ToString()) : "THE ARENA";
            string v = victim ? UiKit.Clean(victim.CallSign.Value.ToString()) : "PILOT";
            bool mineA = attacker && attacker == local, mineV = victim && victim == local;
            killFeed.Add(a, label, v, mineA, mineV);

            if (mineV) { streak = 0; killer = a; killerWeapon = label; lastRespawn = -1; }
            if (mineA && attacker != victim)
            {
                hitMarker.Show(true); numbers.MarkKill(victim);
                if (PulseAudio.Instance) PulseAudio.Instance.PlayUi("kill_confirm", 1);
                if (now - lastKillTime > 8f) streak = 0;
                lastKillTime = now; streak++;
                banner.text = "<size=50%>ELIMINATED</size>\n" + v; banner.color = Gold; bannerT = 1.8f;
                string text = null;
                if (streak == 2) text = "DOUBLE KILL"; else if (streak == 3) text = "TRIPLE KILL"; else if (streak == 4) text = "QUAD KILL"; else if (streak >= 5) text = "RAMPAGE";
                if (!firstBloodDone) text = "FIRST BLOOD";
                streakText.text = text == null ? "" : streak >= 2 ? text + "  x" + streak : text; streakText.color = new Color(1, .45f, .15f);
            }
            firstBloodDone = true;
        }

        // ---------------------------------------------------------------- per frame
        void Update()
        {
            if (!dynCanvas || !dynCanvas.gameObject.activeSelf) return;
            if (!layoutValid) ApplyLayout();
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            var p = ArenaPlayer.Local; var match = ArenaMatch.Instance;
            UiKit.Set(announce, App ? App.Notice : "");

            // timers and animated overlays do not need the local pilot
            if (countdownT > 0)
            {
                countdownT -= dt; float k = Mathf.Clamp01(countdownT / .3f); var c = countdown.color; c.a = Mathf.Min(1, k * 2); countdown.color = c;
                float pop = 1 + Mathf.Clamp01((countdownT - .75f) / .25f) * .5f; countdown.rectTransform.localScale = Vector3.one * pop;
                if (countdownT <= 0) countdown.text = "";
            }
            if (bannerT > 0)
            {
                bannerT -= dt; float a = Mathf.Clamp01(bannerT / .4f);
                var c = banner.color; c.a = a; banner.color = c; var c2 = streakText.color; c2.a = a; streakText.color = c2;
                float pop = 1 + Mathf.Clamp01((bannerT - 1.5f) / .3f) * .45f;
                banner.rectTransform.localScale = Vector3.one * pop; streakText.rectTransform.localScale = Vector3.one * (1 + Mathf.Clamp01((bannerT - 1.55f) / .25f) * .3f);
                if (bannerT <= 0) { banner.text = ""; streakText.text = ""; }
            }
            if (toastT > 0)
            {
                toastT -= dt; var c = toast.color; c.a = Mathf.Clamp01(toastT / .4f); toast.color = c;
                toast.rectTransform.anchoredPosition = new Vector2(0, (1.4f - toastT) * 30);
                if (toastT <= 0) toast.text = "";
            }
            killFeed.Tick(now);
            hitMarker.Tick(dt);

            // pressed look for sticky holds is handled by HudPress; nothing to do per frame for buttons

            if (match)
            {
                int seconds = Mathf.CeilToInt((float)match.TimeLeft);
                if (seconds != lastSeconds) { lastSeconds = seconds; timer.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
            }
            var controls = App ? App.Controls : null;
            if (!p) { crosshair.Tick(dt, 6, 0, false, false, false); return; }

            if (p.Kills.Value != lastKills || p.Deaths.Value != lastDeaths)
            {
                lastKills = p.Kills.Value; lastDeaths = p.Deaths.Value;
                score.text = "ELIMS  " + lastKills.ToString("00") + "     DEATHS  " + lastDeaths.ToString("00");
            }

            // shield
            int health = p.Health.Value;
            shieldShown = Mathf.MoveTowards(shieldShown, health, 110f * dt * (health < shieldShown ? 1.6f : 1f));
            shieldFill.localScale = new Vector3(Mathf.Clamp01(shieldShown / 100f), 1, 1);
            if (health != lastHealth)
            {
                lastHealth = health; shieldValue.text = NumberText[Mathf.Clamp(health, 0, 200)];
                bool low = health < 30; shieldValue.color = low ? UiKit.Red : UiKit.White; shieldFillImg.color = low ? UiKit.Red : UiKit.Cyan;
            }
            bool lowNow = p.Alive && health < 30 && GameApi.InRound;
            if (lowNow || lowActive)
            {
                float a = lowNow ? .2f + .16f * (.5f + .5f * Mathf.Sin(now * 6f)) : 0;
                lowVignette.color = new Color(1, .08f, .05f, a); lowActive = lowNow;
            }

            // weapon and ammo
            var def = p.CurrentDef; int w = (int)def.Id, mag = p.MagazineOf(def.Id), reserve = p.CurrentReserve;
            var altId = (WeaponId)(1 - w); int altMag = p.MagazineOf(altId), altRes = p.ReserveOf(altId);
            if (w != lastWeapon || mag != lastMag || reserve != lastReserve || altMag != lastAltMag || altRes != lastAltReserve)
            {
                bool weaponChanged = w != lastWeapon;
                lastWeapon = w; lastMag = mag; lastReserve = reserve; lastAltMag = altMag; lastAltReserve = altRes;
                if (weaponChanged) weaponLabel.text = def.Label;
                ammoText.text = NumberText[Mathf.Clamp(mag, 0, 200)] + " <size=55%><color=#B8D0E0>/ " + NumberText[Mathf.Clamp(reserve, 0, 200)] + "</color></size>";
                bool low = mag <= Mathf.Max(1, def.MagSize / 4); ammoText.color = mag == 0 ? UiKit.Red : low ? UiKit.Amber : UiKit.White;
                altText.text = WeaponDef.Get((int)altId).Label + "   " + NumberText[Mathf.Clamp(altMag, 0, 200)] + " / " + NumberText[Mathf.Clamp(altRes, 0, 200)] + "   <size=70%>TAP TO SWAP</size>";
            }
            bool reloading = p.Reloading, swapping = p.Swapping;
            if (reloading || swapping)
            {
                float frac = reloading ? 1 - (float)(p.ReloadUntil.Value - p.Now) / def.ReloadSeconds : 1 - (float)(p.SwapUntil.Value - p.Now) / ArenaPlayer.SwapSeconds;
                reloadFill.localScale = new Vector3(Mathf.Clamp01(frac), 1, 1);
            }
            else if (lastReloading || lastSwapping) reloadFill.localScale = new Vector3(0, 1, 1);
            lastReloading = reloading; lastSwapping = swapping;

            // crosshair: spread from weapon, movement, recent fire; red on target (PulseAim contract)
            bool visible = p.Alive && GameApi.InRound;
            float move = controls ? controls.Move.magnitude : 0;
            crosshair.Tick(dt, 6f + def.Spread * 3.5f, move, controls && controls.Sprint, PulseAim.OnTarget, visible);

            // flashes
            damageFlash = Mathf.MoveTowards(damageFlash, 0, dt * 2.2f);
            if (damageFlash > 0 || damageVignette.color.a > 0) damageVignette.color = new Color(1, .1f, .05f, damageFlash * .5f);

            UpdateArcs(p, now, controls);
            var cam = App ? App.View : null;
            numbers.Tick(now, cam, Mathf.Max(dynCanvas.scaleFactor, .01f), new Vector2(Screen.width, Screen.height) * .5f);

            // respawn screen
            if (!p.Alive)
            {
                int left = Mathf.Max(1, Mathf.CeilToInt((float)(p.RespawnAt.Value - p.Now)));
                if (!deathPanel.gameObject.activeSelf) { deathPanel.gameObject.SetActive(true); lastRespawn = -1; }
                if (left != lastRespawn)
                {
                    lastRespawn = left; sb.Length = 0;
                    sb.Append("<size=44><b><color=#FF5A4A>SHIELD OFFLINE</color></b></size>\n");
                    if (killer.Length > 0) sb.Append("<color=#9DB8C8>ELIMINATED BY</color> <color=#FFA633>").Append(killer).Append("</color>  <color=#9DB8C8>WITH</color> <color=#33DBFF>").Append(killerWeapon).Append("</color>\n");
                    sb.Append("RESPAWN IN ").Append(left);
                    deathText.text = sb.ToString();
                }
            }
            else if (deathPanel.gameObject.activeSelf) deathPanel.gameObject.SetActive(false);

            if (scoreboardRt.gameObject.activeSelf && now >= scoreboardNext) { scoreboardNext = now + .5f; RefreshScoreboard(); }
        }

        void UpdateArcs(ArenaPlayer local, float now, PulseInput controls)
        {
            if (!controls) return;
            Vector3 pos = local.transform.position; float yaw = controls.Yaw;
            foreach (var a in arcs)
            {
                if (!a.Image.gameObject.activeSelf) continue;
                float left = a.Until - now;
                if (left <= 0) { a.Image.gameObject.SetActive(false); continue; }
                Vector3 d = a.From - pos; d.y = 0;
                float angle = d.sqrMagnitude < .01f ? 0 : Vector3.SignedAngle(Quaternion.Euler(0, yaw, 0) * Vector3.forward, d, Vector3.up);
                a.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, -angle);
                a.Image.color = new Color(1, .2f, .12f, Mathf.Clamp01(left) * .95f);
            }
        }

        void RefreshScoreboard()
        {
            var players = ArenaMatch.Players(); var local = ArenaPlayer.Local;
            sb.Length = 0;
            for (int i = 0; i < players.Length && i < 5; i++)
            {
                var pl = players[i]; bool me = pl == local;
                sb.Append(me ? "<color=#FFE070>" : "<color=#E8F7FF>").Append(i + 1).Append("   ").Append(UiKit.Clean(pl.CallSign.Value.ToString()))
                  .Append("</color>  <color=#9DB8C8>K</color> ").Append(pl.Kills.Value).Append("  <color=#9DB8C8>D</color> ").Append(pl.Deaths.Value).Append('\n');
            }
            string text = sb.ToString();
            if (text != lastScoreboard) { lastScoreboard = text; scoreboardText.text = text; }
        }

        /// Called by PulseUI when the game page is shown.
        public void OnShown() { layoutValid = false; scoreboardButton.Set(false); }
    }
}
