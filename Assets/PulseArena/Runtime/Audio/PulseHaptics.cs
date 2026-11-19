using UnityEngine;

namespace PulseArena
{
    /// Android vibration driven by PulseEvents. No-op in the editor and on other platforms.
    public class PulseHaptics : MonoBehaviour
    {
        public static bool Enabled => PlayerPrefs.GetInt(PulseSettings.Haptics, 1) == 1;
        float lastTime;
        int shots;

        void OnEnable()
        {
            PulseEvents.Shot += OnShot; PulseEvents.Damaged += OnDamaged; PulseEvents.Eliminated += OnEliminated; PulseEvents.PickedUp += OnPickup;
        }
        void OnDisable()
        {
            PulseEvents.Shot -= OnShot; PulseEvents.Damaged -= OnDamaged; PulseEvents.Eliminated -= OnEliminated; PulseEvents.PickedUp -= OnPickup;
        }
        void OnShot(ArenaPlayer shooter, WeaponId w, Vector3 a, Vector3 b, bool hit)
        {
            if (shooter != ArenaPlayer.Local) return;
            if (w == WeaponId.Carbine) { if ((++shots & 1) == 0) return; Pulse(Kind.Tick); }
            else Pulse(Kind.Thump);
        }
        void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from) { if (victim == ArenaPlayer.Local) Pulse(Kind.Medium); }
        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId w)
        {
            if (victim == ArenaPlayer.Local) Pulse(Kind.Double, true);
            else if (attacker == ArenaPlayer.Local) Pulse(Kind.Medium);
        }
        void OnPickup(ArenaPlayer p, PickupKind kind, Vector3 at) { if (p == ArenaPlayer.Local) Pulse(Kind.Tick); }

        public enum Kind { Tick, Thump, Medium, Double }

        void Pulse(Kind kind, bool force = false)
        {
            if (!Enabled) return;
            float now = Time.unscaledTime;
            if (!force && now - lastTime < .045f) return;
            lastTime = now;
            Vibrate(kind);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator, tick, thump, medium, dbl;
        static AndroidJavaClass effectClass;
        static bool inited, ok;

        static void Init()
        {
            inited = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdk = version.GetStatic<int>("SDK_INT");
                    if (sdk >= 31)
                    {
                        using (var mgr = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                            vibrator = mgr.Call<AndroidJavaObject>("getDefaultVibrator");
                    }
                    else vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;
                effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                bool amp = vibrator.Call<bool>("hasAmplitudeControl");
                int Level(int v) => amp ? v : -1; // -1 = DEFAULT_AMPLITUDE
                tick = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 12L, Level(40));
                thump = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 20L, Level(90));
                medium = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 35L, Level(150));
                dbl = effectClass.CallStatic<AndroidJavaObject>("createWaveform", new long[] { 0, 70, 60, 90 }, new int[] { 0, Level(230), 0, Level(255) }, -1);
                ok = true;
            }
            catch (System.Exception e) { Debug.LogWarning("Haptics unavailable: " + e.Message); }
            // Referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the manifest.
            if (Application.platform == RuntimePlatform.WindowsEditor) Handheld.Vibrate();
        }

        static void Vibrate(Kind kind)
        {
            if (!inited) Init();
            if (!ok) return;
            try
            {
                var effect = kind == Kind.Tick ? tick : kind == Kind.Thump ? thump : kind == Kind.Medium ? medium : dbl;
                vibrator.Call("vibrate", effect);
            }
            catch (System.Exception) { ok = false; }
        }
#else
        static void Vibrate(Kind kind) { }
#endif
    }
}
