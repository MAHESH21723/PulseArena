using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PulseArena
{
    // Headless marketing capture (dev/test only; started by PulseSmoke for -pulse-test showcase).
    // Produces, under -test-output:
    //   logo_character.png / logo_character_alt.png   1024x1024 RGBA, transparent background (difference matting)
    //   frames/f_00000.jpg ...                        1920x1080 JPG q92, one per game frame at Time.captureFramerate = 30
    //   events.csv                                    frame,clip,volume (from PulseAudio.Played)
    //   screens/01..08 *.png                          portfolio screenshots
    //   showcase.txt                                  "PASS: ..." / "FAIL: ..." (written last)
    // Everything is rendered through the main camera into a RenderTexture, so it works with a hidden window.
    // Runs after every other script (execution order 1000) so it can override the camera and read final HUD state.
    [DefaultExecutionOrder(1000)]
    public class PulseShowcase : MonoBehaviour
    {
        const int W = 1920, H = 1080, Fps = 30;
        const int GameplayTicks = 540;   // countdown start -> end of first-person footage (3 s + ~15 s)
        const int CineTicks = 180;

        static readonly (string text, int start, int end)[] Captions =
        {
            ("Fast 4-player arena battles", 20, 115),
            ("Two weapons: PX-24 carbine + SX-8 scatter", 125, 220),
            ("Smart bots: Easy / Normal / Hard", 230, 325),
            ("Aim assist built for touch", 335, 430),
            ("Same Wi-Fi multiplayer", 440, 535),
        };

        PulseApp app;
        string dir, framesDir, screensDir;
        readonly List<string> warnings = new List<string>();

        // capture state
        Camera cam;
        RenderTexture rt;
        Texture2D tex;
        int width = W, height = H;
        readonly List<Canvas> canvases = new List<Canvas>();
        readonly List<RenderMode> canvasModes = new List<RenderMode>();
        Canvas captionCanvas;
        CanvasGroup captionGroup;
        TMP_Text captionText, captionShadow;
        bool active, recording, captureReady;
        int tick, frame;
        string pendingPng;
        readonly StringBuilder csv = new StringBuilder(64 * 1024);

        // script state
        bool autopilot, cine, hideHud;
        int capBase = -1, cineTick, weaponWanted = -1;
        bool wantReload;
        string armShot; bool armHit; bool armKill;
        int lastShotTick = -1, lastKillTick = -1; bool lastShotHit;
        Vector3 focus; bool haveFocus;

        // autopilot state
        ArenaPlayer target; bool targetVisible;
        int nextRetarget, nextStrafe, detourUntil, nextStuckCheck;
        float strafeDir = 1, detourDir = 1;
        Vector3 stuckPos;

        // restore state
        int prevCaptureRate, prevQuality;
        BotDifficulty prevDifficulty;
        float prevRenderScale; int prevMsaa; bool haveUrp;

        public void Begin(PulseApp a)
        {
            app = a;
            dir = PulseSmoke.Argument("-test-output");
            StartCoroutine(Main());
        }

        // ------------------------------------------------------------------ coroutine plumbing
        IEnumerator Main()
        {
            string failure = null;
            if (string.IsNullOrEmpty(dir)) { Debug.LogError("PULSE_TEST FAIL: showcase needs -test-output"); Application.Quit(2); yield break; }
            yield return Safe(Script(), e => failure = e);
            Restore();
            try { File.WriteAllText(Path.Combine(dir, "events.csv"), "frame,clip,volume\n" + csv); }
            catch (Exception e) { warnings.Add("events.csv: " + e.Message); }
            try { if (app && app.Network && app.Network.IsListening) app.Leave(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            yield return Wait(15);
            int files = 0;
            try { files = Directory.GetFiles(framesDir, "f_*.jpg").Length; } catch (Exception) { }
            string result;
            if (failure != null) result = "FAIL: " + OneLine(failure);
            else if (files < 800 || files != frame) result = "FAIL: expected ~900-1150 frames, wrote " + files + " (counted " + frame + ")";
            else result = "PASS: " + files + " frames at " + Fps + " fps = " + (files / (float)Fps).ToString("0.0", CultureInfo.InvariantCulture) + " s"
                + (warnings.Count > 0 ? "; WARN: " + string.Join(" | ", warnings) : "");
            Debug.Log("PULSE_TEST " + result);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "showcase.txt"), result);   // written LAST
        }

        static string OneLine(string s) { s = (s ?? "").Replace('\r', ' ').Replace('\n', ' '); return s.Length > 400 ? s.Substring(0, 400) : s; }

        // Runs nested IEnumerators with exceptions caught (a plain try/catch cannot wrap a yield).
        IEnumerator Safe(IEnumerator root, Action<string> onFail)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            while (stack.Count > 0)
            {
                bool more; object cur = null;
                try { more = stack.Peek().MoveNext(); if (more) cur = stack.Peek().Current; }
                catch (Exception e) { Debug.LogException(e); onFail(e.GetType().Name + ": " + e.Message); yield break; }
                if (!more) { stack.Pop(); continue; }
                if (cur is IEnumerator inner) { stack.Push(inner); continue; }
                yield return cur;
            }
        }

        static IEnumerator Wait(int ticks) { for (int i = 0; i < ticks; i++) yield return null; }
        static IEnumerator Until(Func<bool> cond, int timeout) { for (int i = 0; i < timeout && !cond(); i++) yield return null; }

        // ------------------------------------------------------------------ the script
        IEnumerator Script()
        {
            Directory.CreateDirectory(dir);
            framesDir = Path.Combine(dir, "frames"); screensDir = Path.Combine(dir, "screens");
            Directory.CreateDirectory(framesDir); Directory.CreateDirectory(screensDir);
            foreach (var f in Directory.GetFiles(framesDir, "f_*.jpg")) File.Delete(f);

            prevQuality = PulseDisplay.Quality; prevDifficulty = PulseSettings.Difficulty; prevCaptureRate = Time.captureFramerate;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp) { haveUrp = true; prevRenderScale = urp.renderScale; prevMsaa = urp.msaaSampleCount; }
            PulseSettings.Difficulty = BotDifficulty.Hard;          // aggressive bots
            PulseDisplay.ApplyQuality(2);                            // smooth: shadows + MSAA
            PulseDisplay.SimulateResolution(W, H, 420);
            yield return Wait(10);

            // ---- logo renders (no frames needed, fully synchronous)
            try { RenderLogo("PilotHuman", "logo_character.png", true); } catch (Exception e) { Debug.LogException(e); warnings.Add("logo_character: " + e.Message); }
            try { RenderLogo("PilotBot", "logo_character_alt.png", false); } catch (Exception e) { Debug.LogException(e); warnings.Add("logo_character_alt: " + e.Message); }
            yield return Wait(3);

            SetupCapture();
            yield return Wait(5);   // let the camera/canvases settle before frame 0

            // ---- a. main menu
            Time.captureFramerate = Fps;
            PulseAudio.Played += OnAudio; PulseEvents.Shot += OnShot; PulseEvents.Eliminated += OnEliminated;
            recording = true;
            yield return Wait(45);
            pendingPng = "01-menu.png";
            yield return Wait(40);
            TapPlay();

            // ---- b. countdown + first-person gameplay
            yield return Until(() => ArenaPlayer.Local && ArenaMatch.Instance && ArenaMatch.Instance.Phase.Value == 3, 900);
            if (!ArenaPlayer.Local) throw new Exception("practice match did not start: " + app.Status);
            capBase = tick; autopilot = true;
            yield return Until(() => ArenaMatch.Instance && ArenaMatch.Instance.Phase.Value == 1, 600);
            int g0 = tick;
            armKill = true; lastKillTick = -1;
            yield return Wait(45); weaponWanted = (int)WeaponId.Scatter;           // swap to the scatter while caption 2 is up
            yield return Wait(85); weaponWanted = (int)WeaponId.Carbine;
            yield return Until(() => tick >= g0 + 175, 400);
            yield return ShotWhenFiring("02-gameplay.png", true, 80);
            yield return Until(() => tick >= g0 + 235, 400);
            wantReload = true;                                                     // visible reload
            yield return Until(() => tick >= capBase + GameplayTicks, 1200);
            armKill = false;

            // ---- 08 phone aspect (recording paused: this is a hard cut before the cinematic anyway)
            recording = false;
            PulseDisplay.SimulateResolution(2400, 1080, 405);
            MakeTarget(2400, 1080);
            yield return Wait(30);
            yield return ShotWhenFiring("08-gameplay-phone.png", false, 150);
            yield return Wait(2);
            PulseDisplay.SimulateResolution(W, H, 420);
            MakeTarget(W, H);
            yield return Wait(25);

            // ---- c. cinematic third-person fly-by, HUD hidden
            hideHud = true; cine = true; cineTick = 0; haveFocus = false;
            var me = ArenaPlayer.Local;
            if (me && me.Model) me.Model.gameObject.SetActive(true);
            weaponWanted = -1;
            recording = true;
            yield return Wait(100);
            pendingPng = "04-arena.png";
            yield return Wait(CineTicks - 100);

            // ---- d. settings, then results
            hideHud = false;
            app.UI.Show("settings");
            yield return Wait(55);
            pendingPng = "05-settings.png";
            yield return Wait(35);

            autopilot = false; cine = false;
            if (me && me.Model) me.Model.gameObject.SetActive(false);
            if (ArenaMatch.Instance && ArenaMatch.Instance.Phase.Value != 2) ArenaMatch.Instance.Phase.Value = 2;
            yield return Wait(60);
            pendingPng = "06-results.png";
            yield return Wait(30);
            recording = false;

            // ---- extras (not in the video)
            app.UI.Show("lobby");
            yield return Wait(25);
            pendingPng = "07-lobby.png";
            yield return Wait(3);

            foreach (var n in new[] { "01-menu.png", "02-gameplay.png", "03-elimination.png", "04-arena.png", "05-settings.png", "06-results.png", "07-lobby.png", "08-gameplay-phone.png" })
                if (!File.Exists(Path.Combine(screensDir, n))) warnings.Add("missing screens/" + n);
            if (!File.Exists(Path.Combine(dir, "logo_character.png"))) warnings.Add("missing logo_character.png");
        }

        IEnumerator ShotWhenFiring(string name, bool needHit, int timeout)
        {
            armShot = name; armHit = needHit;
            for (int i = 0; i < timeout && armShot != null; i++) yield return null;
            if (armShot != null) { pendingPng = armShot; armShot = null; warnings.Add(name + " captured without a shot this frame"); }
            yield return Wait(2);
        }

        void TapPlay()
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude))
                if (b && b.gameObject.name == "PRACTICE WITH BOTS" && b.interactable) { b.onClick.Invoke(); return; }
            warnings.Add("play button not found, used StartOffline");
            app.StartOffline();
        }

        // ------------------------------------------------------------------ events
        void OnAudio(AudioClip clip, float volume)
        {
            if (!recording) return;
            csv.Append(frame).Append(',').Append(clip ? clip.name.Replace(',', '_') : "").Append(',').Append(volume.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }
        void OnShot(ArenaPlayer shooter, WeaponId w, Vector3 from, Vector3 to, bool hit)
        {
            if (shooter && shooter == ArenaPlayer.Local) { lastShotTick = tick; lastShotHit = hit; }
        }
        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId w)
        {
            if (attacker && attacker == ArenaPlayer.Local && victim != attacker && lastKillTick < 0) lastKillTick = tick;
        }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            if (active && autopilot) Pilot();
        }

        void LateUpdate()
        {
            if (!active) return;
            if (cam && cine) DriveCamera(Time.deltaTime);
            UpdateCaption();
            SetHud(!hideHud);
            if (armShot != null && lastShotTick == tick && (lastShotHit || !armHit)) { pendingPng = armShot; armShot = null; }
            if (armKill && lastKillTick >= 0 && tick >= lastKillTick + 3) { pendingPng = "03-elimination.png"; armKill = false; }
            if (captureReady && (recording || pendingPng != null))
            {
                try { CaptureFrame(); }
                catch (Exception e) { Debug.LogException(e); warnings.Add("capture: " + e.Message); recording = false; pendingPng = null; }
            }
            tick++;
        }

        void SetHud(bool show)
        {
            foreach (var c in canvases) if (c && c != captionCanvas && c.enabled != show) c.enabled = show;
        }

        void UpdateCaption()
        {
            if (!captionGroup) return;
            float alpha = 0; string text = null;
            if (capBase >= 0)
            {
                int t = tick - capBase;
                foreach (var c in Captions)
                    if (t >= c.start && t <= c.end) { alpha = Mathf.Clamp01(Mathf.Min((t - c.start) / 9f, (c.end - t) / 9f)); text = c.text; break; }
            }
            captionGroup.alpha = alpha;
            if (text != null) { UiKit.Set(captionText, text); UiKit.Set(captionShadow, text); }
        }

        void ForceQuality()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urp) return;
            if (!Mathf.Approximately(urp.renderScale, 1f)) urp.renderScale = 1f;   // PulseUI re-derives it on resolution changes
            if (urp.msaaSampleCount != 4) urp.msaaSampleCount = 4;
        }

        void CaptureFrame()
        {
            ForceQuality();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            if (recording)
            {
                File.WriteAllBytes(Path.Combine(framesDir, "f_" + frame.ToString("00000", CultureInfo.InvariantCulture) + ".jpg"), tex.EncodeToJPG(92));
                frame++;
            }
            if (pendingPng != null)
            {
                File.WriteAllBytes(Path.Combine(screensDir, pendingPng), tex.EncodeToPNG());
                pendingPng = null;
            }
        }

        // ------------------------------------------------------------------ capture setup
        void SetupCapture()
        {
            cam = app.View;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            BuildCaptions();
            canvases.Clear(); canvasModes.Clear();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c && c.transform.parent == null && c.renderMode == RenderMode.ScreenSpaceOverlay) { canvases.Add(c); canvasModes.Add(c.renderMode); }
            canvases.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
            // modes list is only used for restore (all were overlay)
            MakeTarget(W, H);
            for (int i = 0; i < canvases.Count; i++)
            {
                var c = canvases[i];
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = .09f - i * .004f;
            }
            cam.enabled = false;   // we render manually, once per tick
            active = true; captureReady = true;
        }

        void MakeTarget(int w, int h)
        {
            width = w; height = h;
            if (cam) cam.targetTexture = null;
            if (rt) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            if (cam) cam.targetTexture = rt;
            if (tex && (tex.width != w || tex.height != h)) tex.Reinitialize(w, h);
        }

        void BuildCaptions()
        {
            var go = new GameObject("Showcase captions", typeof(Canvas), typeof(CanvasScaler));
            captionCanvas = go.GetComponent<Canvas>(); captionCanvas.renderMode = RenderMode.ScreenSpaceOverlay; captionCanvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>(); UiKit.ConfigureScaler(scaler, 1f); scaler.matchWidthOrHeight = 1f;
            var root = UiKit.Full("Caption root", go.transform);
            captionGroup = root.gameObject.AddComponent<CanvasGroup>(); captionGroup.alpha = 0; captionGroup.blocksRaycasts = false;
            // bottom-centre, between the move pad and the fire cluster, above the shield bar, on a dark pill
            var anchor = new Vector2(.5f, 0); var size = new Vector2(880, 64); var pos = new Vector2(0, 140);
            UiKit.Pill("Caption pill", root, anchor, pos, size + new Vector2(40, 10), new Color(.04f, .07f, .2f, .72f));
            captionShadow = UiKit.Text("Caption shadow", root, "", anchor, pos + new Vector2(3, -3), size, 40, new Color(0, 0, 0, .85f), TextAlignmentOptions.Center, true);
            captionText = UiKit.Text("Caption", root, "", anchor, pos, size, 40, Color.white, TextAlignmentOptions.Center, true);
            captionShadow.overflowMode = captionText.overflowMode = TextOverflowModes.Overflow;
            captionShadow.textWrappingMode = captionText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        void Restore()
        {
            active = false; recording = false; captureReady = false; autopilot = false;
            PulseAudio.Played -= OnAudio; PulseEvents.Shot -= OnShot; PulseEvents.Eliminated -= OnEliminated;
            Time.captureFramerate = prevCaptureRate;
            try
            {
                for (int i = 0; i < canvases.Count; i++)
                    if (canvases[i]) { canvases[i].enabled = true; canvases[i].worldCamera = null; canvases[i].renderMode = canvasModes[i]; }
                if (cam) { cam.targetTexture = null; cam.enabled = true; }
                if (rt) { rt.Release(); Destroy(rt); rt = null; }
                if (tex) { Destroy(tex); tex = null; }
                if (captionCanvas) Destroy(captionCanvas.gameObject);
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (haveUrp && urp) { urp.renderScale = prevRenderScale; urp.msaaSampleCount = prevMsaa; }
                PulseSettings.Difficulty = prevDifficulty;
                PulseDisplay.ApplyQuality(prevQuality);
                PulseDisplay.ClearSimulation();
            }
            catch (Exception e) { Debug.LogWarning("Showcase restore: " + e.Message); }
        }

        // ------------------------------------------------------------------ cinematic camera
        void DriveCamera(float dt)
        {
            // hide the first-person view-model (children of the camera)
            for (int i = 0; i < cam.transform.childCount; i++)
            {
                var child = cam.transform.GetChild(i).gameObject;
                if (child.activeSelf) child.SetActive(false);
            }
            Vector3 reactor = new Vector3(0, 1.4f, 0), sum = Vector3.zero; int n = 0;
            foreach (var p in ArenaPlayer.All) if (p && p.Alive) { sum += p.transform.position; n++; }
            Vector3 want = n > 0 ? Vector3.Lerp(reactor, sum / n, .55f) : reactor;
            if (!haveFocus) { focus = want; haveFocus = true; }
            focus = Vector3.Lerp(focus, want, 1 - Mathf.Exp(-2.5f * dt));
            float t = cineTick / (float)Fps, k = Mathf.Clamp01(cineTick / (float)CineTicks);
            float angle = (35f + t * 34f) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(16f, 11f, k), height = Mathf.Lerp(7.5f, 3.4f, Mathf.SmoothStep(0, 1, k));
            Vector3 look = focus + Vector3.up * 1f;
            Vector3 pos = focus + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius + Vector3.up * height;
            if (Physics.Linecast(look, pos, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.GetComponentInParent<ArenaPlayer>())
                pos = hit.point + (look - pos).normalized * .6f;
            cam.transform.position = pos; cam.transform.LookAt(look);
            cam.fieldOfView = 58;
            cineTick++;
        }

        // ------------------------------------------------------------------ autopilot (drives the LOCAL pilot through ArenaPlayer.TestInput)
        void Pilot()
        {
            var me = ArenaPlayer.Local; var match = ArenaMatch.Instance;
            if (!me || !match || !me.IsSpawned) return;
            var ctl = app.Controls; float dt = Time.deltaTime;
            if (me.IsServer && me.Alive)
            {   // tougher player: regenerate shield, keep ammo topped up
                if (me.Health.Value < 55) me.Health.Value = 100;
                if (me.ReserveCarbine.Value < 48) me.ReserveCarbine.Value = ArenaPlayer.StartReserveCarbine;
                if (me.ReserveScatter.Value < 9) me.ReserveScatter.Value = ArenaPlayer.StartReserveScatter;
            }
            bool playing = match.Phase.Value == 1;
            float yaw = ctl.Yaw, pitch = ctl.Pitch;
            if (!me.Alive) { me.TestInput(Vector2.zero, yaw, pitch, false); return; }

            if (tick >= nextRetarget || !target || !target.Alive) { nextRetarget = tick + 6; target = PickTarget(me); }
            else targetVisible = HasLoS(me, target);

            var move = Vector2.zero; bool fire = false;
            var cur = me.CurrentWeapon; var def = me.CurrentDef; bool scatter = cur == WeaponId.Scatter;
            if (target)
            {
                Vector3 d = target.transform.position + Vector3.up * 1.25f - me.Eye;
                float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z), dist = d.magnitude;
                float wy = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, wp = -Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
                float rate = playing ? 320f : 140f;
                yaw = Mathf.MoveTowardsAngle(yaw, wy, rate * dt);
                pitch = Mathf.MoveTowards(pitch, wp, rate * .8f * dt);
                float err = Mathf.Sqrt(Mathf.Pow(Mathf.DeltaAngle(yaw, wy), 2) + Mathf.Pow(pitch - wp, 2));
                if (playing)
                {
                    float pref = scatter ? 5f : 11f, fwd = 0;
                    if (dist > pref + 3f) fwd = 1f; else if (dist < pref - 3f) fwd = -.7f;
                    if (!targetVisible) fwd = 1f;
                    if (tick >= nextStrafe) { strafeDir = -strafeDir; nextStrafe = tick + UnityEngine.Random.Range(25, 60); }
                    move = new Vector2(targetVisible ? strafeDir * .85f : 0f, fwd);
                    Vector3 wd = Quaternion.Euler(0, yaw, 0) * new Vector3(move.x, 0, move.y);
                    bool blocked = wd.sqrMagnitude > .01f && Physics.Raycast(me.transform.position + Vector3.up * .5f, wd.normalized, 1.1f, ~0, QueryTriggerInteraction.Ignore);
                    if (tick >= nextStuckCheck)
                    {
                        nextStuckCheck = tick + 40;
                        if ((me.transform.position - stuckPos).sqrMagnitude < .6f * .6f && move.sqrMagnitude > .1f) { detourUntil = tick + 50; detourDir = -detourDir; }
                        stuckPos = me.transform.position;
                    }
                    if (blocked && !targetVisible) { detourUntil = Mathf.Max(detourUntil, tick + 30); }
                    if (tick < detourUntil) move = new Vector2(detourDir, targetVisible ? 0f : .2f);
                    else if (blocked) { strafeDir = -strafeDir; move = new Vector2(strafeDir, 0); }

                    float range = scatter ? 11f : Mathf.Min(def.Range * .7f, 45f);
                    fire = targetVisible && err < 3.5f && dist < range && me.MagazineOf(cur) > 0 && !me.Reloading && !me.Swapping;
                }
            }
            if (playing)
            {
                if (weaponWanted >= 0 && (int)cur != weaponWanted && !me.Swapping) me.RequestSwap((WeaponId)weaponWanted);
                int mag = me.MagazineOf(cur);
                if ((wantReload && mag < def.MagSize) || (mag <= 4 && me.ReserveOf(cur) > 0))
                {
                    if (!me.Reloading && !me.Swapping) { me.ReloadServerRpc(); wantReload = false; }
                }
                else wantReload = false;
            }
            yaw = Mathf.Repeat(yaw, 360f);
            me.TestInput(move, yaw, pitch, fire);
            ctl.Yaw = yaw; ctl.Pitch = pitch;
        }

        ArenaPlayer PickTarget(ArenaPlayer me)
        {
            ArenaPlayer best = null; float bestScore = float.MaxValue; bool bestVisible = false;
            foreach (var p in ArenaPlayer.All)
            {
                if (!p || p == me || !p.Alive) continue;
                float d = Vector3.Distance(p.transform.position, me.transform.position);
                bool vis = HasLoS(me, p);
                float score = d * (vis ? 1f : 2.5f);
                if (score < bestScore) { bestScore = score; best = p; bestVisible = vis; }
            }
            targetVisible = bestVisible;
            return best;
        }

        static bool HasLoS(ArenaPlayer me, ArenaPlayer p)
        {
            Vector3 eye = me.Eye, chest = p.transform.position + Vector3.up * 1.2f;
            if (!Physics.Linecast(eye, chest, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            var hp = hit.collider.GetComponentInParent<ArenaPlayer>();
            return hp == p || hp == me;
        }

        // ------------------------------------------------------------------ logo renders
        // Transparent background via difference matting: render the same pose on black and on white and solve
        // alpha = 1 - (white - black), colour = black / alpha. Independent of URP's alpha handling / post-processing.
        void RenderLogo(string prefabName, string file, bool hero)
        {
            var prefab = ArenaArt.WorldPrefab(prefabName);
            if (!prefab) throw new Exception("prefab " + prefabName + " missing in Resources/World");
            const int S = 1024;
            var stage = new GameObject("Showcase stage").transform; stage.position = new Vector3(0, 900, 0);
            var model = Instantiate(prefab, stage, false).transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0, hero ? 148f : -115f, 0);   // camera sits at -Z looking +Z
            Transform hand = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "HandAttach") { hand = t; break; }
            if (hand) ArenaArt.WeaponThirdPerson(hand, WeaponId.Carbine);
            var anim = model.GetComponentInChildren<Animator>(true);
            if (anim)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.applyRootMotion = false;
                if (anim.runtimeAnimatorController)
                {
                    anim.Rebind();
                    foreach (var prm in anim.parameters)
                    {
                        if (prm.name == "Alive" && prm.type == AnimatorControllerParameterType.Bool) anim.SetBool("Alive", true);
                        if (prm.name == "Speed" && prm.type == AnimatorControllerParameterType.Float) anim.SetFloat("Speed", 0f);
                    }
                    anim.Update(.8f);
                }
            }
            Bounds b = default; bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            float height = any && b.size.y > .5f ? b.size.y : 1.8f, top = any ? b.max.y : stage.position.y + 1.8f;
            // frame head to knees with margin so the ears and the hand-held gun are both in shot
            Vector3 aim = new Vector3(stage.position.x, top - .40f * height, stage.position.z);
            const float fov = 26f;
            float dist = .52f * height / Mathf.Tan(fov * .5f * Mathf.Deg2Rad);

            var camGo = new GameObject("Showcase logo camera");
            var lc = camGo.AddComponent<Camera>();
            lc.fieldOfView = fov; lc.nearClipPlane = .05f; lc.farClipPlane = 60f; lc.clearFlags = CameraClearFlags.SolidColor; lc.allowHDR = false; lc.allowMSAA = true;
            var data = lc.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            camGo.transform.position = aim + Quaternion.Euler(-7f, hero ? 0f : 0f, 0) * (Vector3.back * dist);   // slight low angle
            camGo.transform.LookAt(aim);

            // lighting: warm key (the only directional), cyan rim from behind, flat bluish fill
            var disabled = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l && l.enabled) { l.enabled = false; disabled.Add(l); }
            var ambMode = RenderSettings.ambientMode; var ambColor = RenderSettings.ambientLight; bool fog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.30f, .33f, .45f); RenderSettings.fog = false;
            var key = new GameObject("Showcase key").AddComponent<Light>();
            key.type = LightType.Directional; key.color = new Color(1f, .72f, .45f); key.intensity = 2.4f; key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(30, 35, 0);
            var rim = new GameObject("Showcase rim").AddComponent<Light>();
            rim.type = LightType.Spot; rim.color = new Color(.2f, .9f, 1f); rim.spotAngle = 80; rim.range = height * 8f; rim.intensity = 28f; rim.shadows = LightShadows.None;
            rim.transform.position = aim + new Vector3(hero ? 1.0f : -1.0f, .5f, 1.2f) * height;
            rim.transform.rotation = Quaternion.LookRotation(aim - rim.transform.position);
            var rim2 = new GameObject("Showcase rim2").AddComponent<Light>();
            rim2.type = LightType.Spot; rim2.color = new Color(.35f, .6f, 1f); rim2.spotAngle = 80; rim2.range = height * 8f; rim2.intensity = 14f; rim2.shadows = LightShadows.None;
            rim2.transform.position = aim + new Vector3(hero ? -1.2f : 1.2f, .2f, 1.0f) * height;
            rim2.transform.rotation = Quaternion.LookRotation(aim - rim2.transform.position);

            var target = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; target.Create();
            var read = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset; float scale = urp ? urp.renderScale : 1; int msaa = urp ? urp.msaaSampleCount : 1;
            try
            {
                if (urp) { urp.renderScale = 1f; urp.msaaSampleCount = 4; }
                lc.targetTexture = target;
                lc.backgroundColor = Color.black; lc.Render(); var black = ReadBack(read, target, S);
                lc.backgroundColor = Color.white; lc.Render(); var white = ReadBack(read, target, S);
                var outPx = new Color32[black.Length];
                for (int i = 0; i < outPx.Length; i++)
                {
                    Color32 bl = black[i], wh = white[i];
                    float diff = ((wh.r - bl.r) + (wh.g - bl.g) + (wh.b - bl.b)) / (3f * 255f);
                    float a = Mathf.Clamp01(1f - diff);
                    if (a > .985f) a = 1f;
                    if (a < .004f) { outPx[i] = new Color32(0, 0, 0, 0); continue; }
                    outPx[i] = new Color32((byte)Mathf.Min(255, Mathf.RoundToInt(bl.r / a)), (byte)Mathf.Min(255, Mathf.RoundToInt(bl.g / a)), (byte)Mathf.Min(255, Mathf.RoundToInt(bl.b / a)), (byte)Mathf.RoundToInt(a * 255f));
                }
                read.SetPixels32(outPx);
                File.WriteAllBytes(Path.Combine(dir, file), read.EncodeToPNG());
            }
            finally
            {
                if (urp) { urp.renderScale = scale; urp.msaaSampleCount = msaa; }
                lc.targetTexture = null; target.Release(); Destroy(target); Destroy(read);
                foreach (var l in disabled) if (l) l.enabled = true;
                RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambColor; RenderSettings.fog = fog;
                Destroy(key.gameObject); Destroy(rim.gameObject); Destroy(rim2.gameObject); Destroy(camGo); Destroy(stage.gameObject);
            }
        }

        static Color32[] ReadBack(Texture2D read, RenderTexture target, int size)
        {
            var previous = RenderTexture.active; RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0); RenderTexture.active = previous;
            return read.GetPixels32();
        }
    }
}
