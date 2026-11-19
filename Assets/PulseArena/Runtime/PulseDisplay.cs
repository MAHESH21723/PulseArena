using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PulseArena
{
    /// Sizes of touch controls and the canvas for a given screen, in canvas units (reference 1600x900).
    public struct HudLayout
    {
        public Vector2 CanvasSize;   // canvas size in units
        public float ScaleFactor;    // canvas units -> pixels
        public float Match;          // CanvasScaler.matchWidthOrHeight
        public float Fire, Pad, Small, SmallWide, Margin, Hud;
        public float ClusterScale;   // bottom-centre shield/weapon panel scale
        public float LeftBlock, RightBlock;
    }

    // Device / resolution / performance policy for phones.
    public static class PulseDisplay
    {
        public static readonly Vector2 Reference = new Vector2(1600, 900);
        public static event Action Changed;

        static Vector2Int? simSize;
        static float simDpi;

        public static float HudScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(PulseSettings.HudScale, 1f), .8f, 1.3f);
            set { PlayerPrefs.SetFloat(PulseSettings.HudScale, Mathf.Clamp(value, .8f, 1.3f)); Changed?.Invoke(); }
        }
        public static float Dpi
        {
            get
            {
                float d = simDpi > 0 ? simDpi : Screen.dpi;
                return d <= 0 ? 160 : Mathf.Clamp(d, 100, 700);
            }
        }
        public static Vector2Int ScreenSize => simSize ?? new Vector2Int(Screen.width, Screen.height);

        /// Dev/test only: pretend the screen has this size/DPI. On desktop the window is actually resized too.
        public static void SimulateResolution(int width, int height, float dpi = 0)
        {
            simSize = new Vector2Int(width, height); simDpi = dpi;
            if (!Application.isMobilePlatform) Screen.SetResolution(width, height, FullScreenMode.Windowed);
            Changed?.Invoke();
        }
        /// Raise Changed (used by PulseUI when the real screen size or safe area changes).
        public static void Refresh() => Changed?.Invoke();
        public static void ClearSimulation() { simSize = null; simDpi = 0; Changed?.Invoke(); }

        /// Height-match on wide phones, width-match on 4:3 tablets, blended between.
        public static float MatchFor(float aspect) => Mathf.Clamp01((aspect - 4f / 3f) / (16f / 9f - 4f / 3f));

        public static HudLayout Compute(int w, int h, float dpi, float hudScale)
        {
            w = Mathf.Max(w, 1); h = Mathf.Max(h, 1);
            var l = new HudLayout { Hud = hudScale };
            l.Match = MatchFor((float)w / h);
            float log = Mathf.Lerp(Mathf.Log(w / Reference.x, 2), Mathf.Log(h / Reference.y, 2), l.Match);
            l.ScaleFactor = Mathf.Pow(2, log);
            l.CanvasSize = new Vector2(w / l.ScaleFactor, h / l.ScaleFactor);
            float unitsPerMm = dpi / 25.4f / l.ScaleFactor;
            l.Fire = Mathf.Clamp(15f * unitsPerMm, 120, 190) * hudScale;
            l.Pad = Mathf.Clamp(24f * unitsPerMm, 170, 280) * hudScale;
            l.Small = Mathf.Clamp(Mathf.Max(10f * unitsPerMm, 9.5f * unitsPerMm), 64, 100) * Mathf.Min(hudScale, 1.2f);
            l.SmallWide = l.Small * 1.8f;
            l.Margin = 36;
            float gap = 16;
            l.LeftBlock = l.Margin + l.Pad;
            l.RightBlock = l.Margin + l.Fire + gap + l.SmallWide;
            float half = l.CanvasSize.x * .5f - Mathf.Max(l.LeftBlock, l.RightBlock) - 20;
            l.ClusterScale = Mathf.Clamp(2 * half / 620f, .6f, Mathf.Min(1.1f, hudScale));
            return l;
        }
        public static HudLayout Current => Compute(ScreenSize.x, ScreenSize.y, Dpi, HudScale);

        // ---- quality
        // 0 Battery: 30 fps, ~720p, no shadows.  1 Balanced: 60 fps, ~900p, hard shadows.  2 Smooth: 60 fps, ~1080p, hard shadows, MSAA 2x.
        public static void ApplyQuality(int level)
        {
            level = Mathf.Clamp(level, 0, 2);
            PlayerPrefs.SetInt(PulseSettings.Quality, level);
            QualitySettings.shadows = level == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.HardOnly;
            QualitySettings.shadowDistance = level == 0 ? 0 : 45;
            Application.targetFrameRate = level == 0 ? 30 : 60;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp)
            {
                int target = level == 0 ? 720 : level == 1 ? 900 : 1080;
                urp.renderScale = Mathf.Clamp(target / (float)Mathf.Max(Screen.height, 1), .5f, 1f);
                urp.msaaSampleCount = level == 2 ? 2 : 1;
                urp.shadowDistance = level == 0 ? 0 : 45;
            }
        }
        public static int Quality => Mathf.Clamp(PlayerPrefs.GetInt(PulseSettings.Quality, 1), 0, 2);

        /// NeverSleep only while a match is on screen.
        public static void SetMatchActive(bool active) =>
            Screen.sleepTimeout = active ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
    }
}
