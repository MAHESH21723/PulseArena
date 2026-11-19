using System.Collections.Generic;
using UnityEngine;

namespace PulseArena
{
    /// Event-driven audio.
    /// * Your own gun and all UI/feedback sounds are 2D and always full quality.
    /// * Other pilots' guns, impacts, steps and pickups are 3D, low-passed by distance (air absorption).
    /// * Every key has several recorded variations, random pitch/volume, a per-key voice cap, a cooldown and a priority
    ///   (important sounds steal voices from unimportant ones when the pool is full).
    /// * Your own shots duck the world pool for a moment so your gun stays punchy.
    ///
    /// Helpers for other systems: PlayUi, Play2D, Play3D, PlayHitConfirm, PlayCrit, PlayShieldBreak, PlayEmptyClick,
    /// PlayLowAmmo, PlayMatchResult and the null-safe statics (PulseAudio.HitConfirm(...), PulseAudio.Ui(...)).
    public class PulseAudio : MonoBehaviour
    {
        public const string SfxKey = "sfxVolume";
        public static PulseAudio Instance;
        /// Raised whenever a clip actually starts playing (clip, final source volume). Used by the showcase recorder.
        public static event System.Action<AudioClip, float> Played;

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(PulseSettings.Volume, .7f);
            set { PlayerPrefs.SetFloat(PulseSettings.Volume, value); AudioListener.volume = value; }
        }
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set { PlayerPrefs.SetFloat(SfxKey, value); if (Instance) Instance.sfx = value; }
        }

        class Voice { public AudioSource Source; public AudioLowPassFilter Lpf; public string Key; public float Started, Base; public int Priority; public bool Spatial; }
        class Sound
        {
            public AudioClip[] Clips; public float Volume = 1; public int MaxVoices = 4; public bool Loop; public int Last = -1;
            public float Pitch = .04f, VolVar = .08f, Cooldown, LastPlayed = -99; public int Priority;
        }

        const int WorldVoices = 12, FlatVoices = 8;
        const float HearRange = 45f, LowPassNear = 3f, LowPassFar = 42f, OpenCutoff = 22000f, FarCutoff = 1400f;
        readonly Dictionary<string, Sound> sounds = new Dictionary<string, Sound>();
        Voice[] world, flat;
        AudioSource ambience, warning;
        float sfx = 1;
        Dictionary<string, AudioClip> fallback;

        // gameplay-state trackers (local pilot only)
        float duck = 1, duckUntil;
        int lastMag = -1; WeaponId lastWeapon; float lastHealth = -1, nextEmptyAt;
        bool pendingHit, pendingCrit, pendingKill; Camera cam;

        void Awake()
        {
            Instance = this;
            AudioListener.volume = MasterVolume; sfx = SfxVolume;
            var bank = Resources.Load<PulseSoundBank>("Audio/PulseSoundBank");
            if (bank)
            {
                foreach (var e in bank.Entries)
                {
                    if (e.Clips == null || e.Clips.Length == 0) continue;
                    sounds[e.Key] = new Sound
                    {
                        Clips = e.Clips, Volume = e.Volume, MaxVoices = Mathf.Max(1, e.MaxVoices), Loop = e.Loop,
                        Pitch = e.PitchVariation, VolVar = e.VolumeVariation, Cooldown = e.Cooldown, Priority = e.Priority
                    };
                }
            }
            else Debug.LogWarning("PulseSoundBank missing; using synthesized fallback sounds. Run PulseAssetsBuilder.BuildSoundBank.");
            world = MakePool(WorldVoices, true); flat = MakePool(FlatVoices, false);
            ambience = MakeLoop(Ambient()); ambience.volume = .12f; ambience.Play();
            warning = MakeLoop(sounds.TryGetValue("low_shield", out var low) ? low.Clips[0] : Synth("tone", .25f, 880, 880, 0)); warning.volume = 0;
            gameObject.AddComponent<PulseHaptics>();
        }

        Voice[] MakePool(int count, bool spatial)
        {
            var pool = new Voice[count];
            for (int i = 0; i < count; i++)
            {
                // One child object per voice so each source can carry its own low-pass filter.
                var go = new GameObject(spatial ? "WorldVoice" : "FlatVoice"); go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = spatial ? 1 : 0;
                AudioLowPassFilter lpf = null;
                if (spatial)
                {
                    s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 3; s.maxDistance = 50; s.dopplerLevel = 0; s.spread = 40;
                    lpf = go.AddComponent<AudioLowPassFilter>(); lpf.cutoffFrequency = OpenCutoff;
                }
                pool[i] = new Voice { Source = s, Lpf = lpf, Spatial = spatial };
            }
            return pool;
        }
        AudioSource MakeLoop(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.spatialBlend = 0; s.playOnAwake = false; return s;
        }

        void OnEnable()
        {
            PulseEvents.Shot += OnShot; PulseEvents.Damaged += OnDamaged; PulseEvents.Eliminated += OnEliminated; PulseEvents.Respawned += OnRespawned;
            PulseEvents.WeaponSwapped += OnSwap; PulseEvents.ReloadStarted += OnReload; PulseEvents.PickedUp += OnPickup; PulseEvents.Footstep += OnFootstep;
            PulseEvents.PhaseChanged += OnPhase; PulseEvents.Countdown += OnCountdown;
        }
        void OnDisable()
        {
            PulseEvents.Shot -= OnShot; PulseEvents.Damaged -= OnDamaged; PulseEvents.Eliminated -= OnEliminated; PulseEvents.Respawned -= OnRespawned;
            PulseEvents.WeaponSwapped -= OnSwap; PulseEvents.ReloadStarted -= OnReload; PulseEvents.PickedUp -= OnPickup; PulseEvents.Footstep -= OnFootstep;
            PulseEvents.PhaseChanged -= OnPhase; PulseEvents.Countdown -= OnCountdown;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---- event handlers
        static bool Mine(ArenaPlayer p) => p && p == ArenaPlayer.Local;
        void At(string key, ArenaPlayer who, Vector3 at, float volume = 1)
        { if (Mine(who)) Play2D(key, volume); else Play3D(key, at, volume); }

        void OnShot(ArenaPlayer shooter, WeaponId w, Vector3 muzzle, Vector3 end, bool hit)
        {
            string key = w == WeaponId.Scatter ? "shot_secondary" : "shot_carbine";
            if (Mine(shooter)) { Play2D(key, .9f); duckUntil = Time.unscaledTime + (w == WeaponId.Scatter ? .22f : .1f); }
            else Play3D(key, muzzle, 1f);
            // Impacts are quieter than the shot; the pilot-hit impact is skipped for your own hits (the hit-confirm tick replaces it).
            if (hit && Mine(shooter)) return;
            Play3D(hit ? "impact_shield" : "impact_wall", end, .8f);
        }
        void OnDamaged(ArenaPlayer victim, ArenaPlayer attacker, int amount, Vector3 from)
        {
            if (Mine(victim)) Play2D("hit_taken", 1f);
            else if (Mine(attacker) && victim != attacker)
            {
                // Resolved in LateUpdate so a kill plays only the kill confirm, not tick + kill.
                pendingHit = true;
                if (attacker.CurrentWeapon == WeaponId.Carbine && amount >= GameApi.Def(attacker).Damage * 1.5f) pendingCrit = true;
            }
        }
        void OnEliminated(ArenaPlayer victim, ArenaPlayer attacker, WeaponId w)
        {
            if (victim)
            {
                if (Mine(victim)) Play2D("death", 1f);
                else Play3D("explosion", victim.transform.position + Vector3.up, 1f);
            }
            if (Mine(attacker) && attacker != victim) pendingKill = true;
        }
        void OnRespawned(ArenaPlayer p) { if (p) At("respawn", p, p.transform.position); }
        void OnSwap(ArenaPlayer p, WeaponId w) { if (p) At("swap", p, p.transform.position, .8f); }
        void OnReload(ArenaPlayer p) { if (p) At("reload", p, p.transform.position, .9f); }
        void OnPickup(ArenaPlayer p, PickupKind k, Vector3 at)
        {
            string key = k == PickupKind.Shield ? "pickup_shield" : "pickup_ammo";
            if (Mine(p)) Play2D(key); else Play3D(key, at, .8f);
        }
        void OnFootstep(ArenaPlayer p, Vector3 at) { if (Mine(p)) Play2D("footstep", .55f); else Play3D("footstep", at, .8f); }
        void OnPhase(int phase)
        {
            if (phase != 2) return;
            var me = ArenaPlayer.Local;
            if (!me) { Play2D("match_end"); return; }
            int best = 0;
            foreach (var p in FindObjectsByType<ArenaPlayer>(FindObjectsSortMode.None)) if (p != me) best = Mathf.Max(best, p.Kills.Value);
            PlayMatchResult(me.Kills.Value > 0 && me.Kills.Value >= best);
        }
        void OnCountdown(int seconds) => Play2D(seconds > 0 ? "countdown" : "countdown_go");

        // ---- public API
        /// 2D UI / feedback sound. Keys: ui_click, ui_confirm, ui_back, ui_hover, ui_error, ui_select (plus any gameplay key).
        public void PlayUi(string key, float volume = 1) => Play2D(key, volume);
        public void Play2D(string key, float volume = 1) => StartVoice(flat, key, volume, false, default);
        public void Play3D(string key, Vector3 position, float volume = 1)
        {
            var listener = Listener();
            if (listener && (position - listener.position).sqrMagnitude > HearRange * HearRange) return;
            StartVoice(world, key, volume, true, position, listener ? Vector3.Distance(position, listener.position) : 0);
        }

        /// Bright tick when you hit an enemy; pass kill = true for the rewarding kill sound instead.
        public void PlayHitConfirm(bool kill = false) => Play2D(kill ? "kill_confirm" : "hit_confirm", 1f);
        /// Higher, shimmering tick for a critical / headshot hit.
        public void PlayCrit() => Play2D("hit_crit", 1f);
        public void PlayShieldBreak() => Play2D("shield_break", 1f);
        /// Dry-fire click (magazine empty).
        public void PlayEmptyClick() => Play2D("empty", 1f);
        /// Two-beep low-magazine warning.
        public void PlayLowAmmo() => Play2D("low_ammo", 1f);
        public void PlayMatchResult(bool win) => Play2D(win ? "match_win" : "match_lose", 1f);
        public bool Has(string key) => sounds.ContainsKey(key);

        // Null-safe statics: PulseAudio.HitConfirm(false), PulseAudio.Ui("ui_click")
        public static void Ui(string key, float volume = 1) { if (Instance) Instance.Play2D(key, volume); }
        public static void HitConfirm(bool kill = false) { if (Instance) Instance.PlayHitConfirm(kill); }

        /// Backwards compatible: shot, hit, reload, click, kill, respawn.
        public void Play(string kind, float volume = 1)
        {
            string key = kind == "shot" ? "shot_carbine" : kind == "hit" ? "hit_taken" : kind == "click" ? "ui_click" : kind;
            Play2D(key, volume);
        }

        Transform Listener()
        {
            if (!cam) cam = Camera.main;
            return cam ? cam.transform : null;
        }

        void StartVoice(Voice[] pool, string key, float volume, bool spatial, Vector3 at, float distance = 0)
        {
            AudioClip clip; float baseVolume = 1, pitchVar = .04f, volVar = .08f; Sound sound; int priority = 0;
            float now = Time.unscaledTime;
            if (sounds.TryGetValue(key, out sound))
            {
                if (sound.Cooldown > 0 && now - sound.LastPlayed < sound.Cooldown) return;
                int n = sound.Clips.Length, i = n == 1 ? 0 : Random.Range(0, n);
                if (n > 1 && i == sound.Last) i = (i + 1) % n;
                sound.Last = i; clip = sound.Clips[i]; baseVolume = sound.Volume; pitchVar = sound.Pitch; volVar = sound.VolVar; priority = sound.Priority;
            }
            else { clip = Fallback(key); if (!clip) return; }
            int max = sound != null ? sound.MaxVoices : 4, active = 0;
            Voice free = null, steal = null;
            foreach (var v in pool)
            {
                bool playing = v.Source.isPlaying;
                if (playing && v.Key == key) active++;
                if (!playing) { if (free == null) free = v; }
                else if (v.Priority <= priority && (steal == null || v.Priority < steal.Priority || (v.Priority == steal.Priority && v.Started < steal.Started))) steal = v;
            }
            if (active >= max) return;
            var voice = free ?? steal;
            if (voice == null) return;
            if (sound != null) sound.LastPlayed = now;

            var s = voice.Source;
            s.Stop(); s.clip = clip; s.loop = false; s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            float level = Mathf.Clamp01(volume * baseVolume * sfx * (1f - Random.Range(0f, volVar)));
            voice.Base = level; voice.Key = key; voice.Started = now; voice.Priority = priority;
            s.volume = spatial ? level * duck : level;
            if (spatial)
            {
                s.transform.position = at;
                // Air absorption: far sources lose their highs, so distant gunfire is a dull thud, not a crack.
                if (voice.Lpf)
                {
                    float d01 = Mathf.Clamp01((distance - LowPassNear) / (LowPassFar - LowPassNear));
                    voice.Lpf.cutoffFrequency = OpenCutoff * Mathf.Pow(FarCutoff / OpenCutoff, d01);
                }
            }
            s.Play();
            Played?.Invoke(clip, s.volume);
        }

        void LateUpdate()
        {
            if (pendingKill) PlayHitConfirm(true);
            else if (pendingCrit) PlayCrit();
            else if (pendingHit) PlayHitConfirm(false);
            pendingHit = pendingCrit = pendingKill = false;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            // duck world voices while your own gun fires
            float target = now < duckUntil ? .6f : 1f;
            duck = Mathf.MoveTowards(duck, target, Time.unscaledDeltaTime * (target < duck ? 14f : 3f));
            foreach (var v in world) if (v.Source.isPlaying) v.Source.volume = v.Base * duck;

            var p = ArenaPlayer.Local; bool inRound = p && p.Alive && GameApi.InRound;
            if (inRound)
            {
                // low shield: heartbeat loop; shield break cue when it drops under the threshold
                float health = p.Health.Value;
                bool low = health < 30;
                if (lastHealth >= 30 && health < 30 && health > 0) PlayShieldBreak();
                lastHealth = health;
                warning.volume = low ? .6f * sfx : 0;
                if (low && !warning.isPlaying) warning.Play(); else if (!low && warning.isPlaying) warning.Stop();

                // low ammo + dry fire
                int mag = GameApi.Magazine(p); var weapon = GameApi.Weapon(p);
                int threshold = Mathf.Max(1, Mathf.CeilToInt(GameApi.Def(p).MagSize * .25f));
                if (weapon == lastWeapon && lastMag > mag && mag == threshold) PlayLowAmmo();
                lastMag = mag; lastWeapon = weapon;
                var app = PulseApp.Instance;
                if (mag == 0 && app && app.Controls.Fire && !p.Reloading && !p.Swapping && now >= nextEmptyAt) { PlayEmptyClick(); nextEmptyAt = now + .32f; }
            }
            else
            {
                lastHealth = -1; lastMag = -1;
                if (warning.isPlaying) warning.Stop();
            }
            ambience.volume = .12f * sfx;
        }

        // ---- synthesized fallbacks (used only if the sound bank is not built)
        AudioClip Fallback(string key)
        {
            if (fallback == null) fallback = new Dictionary<string, AudioClip>();
            string group = key.StartsWith("shot") ? "shot" : key.StartsWith("ui") ? "click" : key.StartsWith("impact") || key.StartsWith("hit") ? "hit" : key == "reload" || key == "swap" ? "reload" : key == "footstep" ? "step" : "kill";
            if (!fallback.TryGetValue(group, out var clip))
            {
                clip = group == "shot" ? Synth(group, .16f, 180, 35, .65f) : group == "hit" ? Synth(group, .1f, 880, 440, .25f) : group == "reload" ? Synth(group, .3f, 120, 1000, .08f)
                    : group == "click" ? Synth(group, .065f, 760, 520, .03f) : group == "step" ? Synth(group, .05f, 90, 60, .3f) : Synth(group, .35f, 380, 1100, .12f);
                fallback[group] = clip;
            }
            return clip;
        }
        static AudioClip Synth(string name, float seconds, float from, float to, float noise)
        {
            const int rate = 22050; var data = new float[(int)(rate * seconds)]; var rand = new System.Random(name.Length); float phase = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length; phase += Mathf.Lerp(from, to, t) * 2 * Mathf.PI / rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(t * 15, 1)) * .1f + Mathf.Pow(1 - t, 2) * Mathf.Min(t * 70, 1);
                data[i] = (Mathf.Sin(phase) * .5f + Mathf.Sin(phase * .5f) * .15f + ((float)rand.NextDouble() * 2 - 1) * noise) * env * .65f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }
        static AudioClip Ambient()
        {
            const int rate = 22050; var data = new float[rate * 8];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                data[i] = (Mathf.Sin(t * 2 * Mathf.PI * 55) * .12f + Mathf.Sin(t * 2 * Mathf.PI * 82.5f) * .07f + Mathf.Sin(t * 2 * Mathf.PI * 110) * .03f) * (.7f + .3f * Mathf.Cos(t * Mathf.PI / 4));
            }
            var clip = AudioClip.Create("Reactor atmosphere", data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }
}
