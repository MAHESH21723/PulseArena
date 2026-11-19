using System;
using UnityEngine;

namespace PulseArena
{
    // Shared contracts between gameplay, bots, world, UI, audio and online code.
    // Keep this file small and stable: other systems depend on these names.

    public enum WeaponId { Carbine = 0, Scatter = 1 }

    public sealed class WeaponDef
    {
        public readonly WeaponId Id;
        public readonly string Name, Label, SoundKey;
        public readonly int Damage, Pellets, MagSize;
        public readonly float Interval, Spread, Range, ReloadSeconds;

        WeaponDef(WeaponId id, string name, string label, string sound, int damage, int pellets, int mag, float interval, float spread, float range, float reload)
        { Id = id; Name = name; Label = label; SoundKey = sound; Damage = damage; Pellets = pellets; MagSize = mag; Interval = interval; Spread = spread; Range = range; ReloadSeconds = reload; }

        // Damage is per pellet. Shields are 100.
        public static readonly WeaponDef[] All =
        {
            new WeaponDef(WeaponId.Carbine, "PX-24  /  PULSE CARBINE", "PX-24", "shot_carbine", 25, 1, 24, .14f, 0f, 90f, 1.45f),
            new WeaponDef(WeaponId.Scatter, "SX-8  /  SCATTER CANNON", "SX-8", "shot_secondary", 12, 8, 6, .8f, 5.5f, 28f, 1.9f),
        };
        public static WeaponDef Get(int id) => All[Mathf.Clamp(id, 0, All.Length - 1)];
    }

    public enum PickupKind { Shield = 0, Ammo = 1 }

    /// A place on the map where a pickup appears. Implemented by the world/pickup code;
    /// read by bots (to seek shields/ammo) and HUD. Valid on server and clients.
    public interface IPickupSite
    {
        PickupKind Kind { get; }
        bool Available { get; }
        Vector3 Position { get; }
    }

    public static class PickupRegistry
    {
        public static readonly System.Collections.Generic.List<IPickupSite> Sites = new System.Collections.Generic.List<IPickupSite>();
    }

    public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }

    // What a bot wants to do this frame. Applied by ArenaPlayer on the server.
    public struct BotCommand
    {
        public Vector2 Move;
        public float Yaw, Pitch;
        public bool Fire, Sprint, Reload;
        public int Weapon; // -1 = keep current
    }

    public static class PulseSettings
    {
        public const string Volume = "volume", Sensitivity = "sensitivity", Quality = "quality", Pilot = "pilot";
        public const string BotLevel = "botDifficulty", Haptics = "haptics", HudScale = "hudScale";
        public static BotDifficulty Difficulty
        {
            get => (BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(BotLevel, 1), 0, 2);
            set { PlayerPrefs.SetInt(BotLevel, (int)value); PlayerPrefs.Save(); }
        }
    }

    // Client-side presentation events. Raised on every peer (from ClientRpcs or local
    // observation) so audio, HUD, effects and haptics never reach into each other.
    // Gameplay authority stays on the server; never change game state from a listener.
    public static class PulseEvents
    {
        /// shooter, weapon, muzzle position, end point of the main ray, whether a pilot was hit
        public static event Action<ArenaPlayer, WeaponId, Vector3, Vector3, bool> Shot;
        /// victim, attacker (may be null), damage amount, attacker position
        public static event Action<ArenaPlayer, ArenaPlayer, int, Vector3> Damaged;
        /// victim, attacker (may be null), weapon used
        public static event Action<ArenaPlayer, ArenaPlayer, WeaponId> Eliminated;
        public static event Action<ArenaPlayer> Respawned;
        public static event Action<ArenaPlayer, WeaponId> WeaponSwapped;
        public static event Action<ArenaPlayer> ReloadStarted;
        public static event Action<ArenaPlayer, PickupKind, Vector3> PickedUp;
        /// pilot, world position of the step
        public static event Action<ArenaPlayer, Vector3> Footstep;
        /// 0 lobby, 1 playing, 2 results, 3 countdown
        public static event Action<int> PhaseChanged;
        /// whole seconds left before the round starts (3, 2, 1, 0 = go)
        public static event Action<int> Countdown;

        public static void RaiseShot(ArenaPlayer p, WeaponId w, Vector3 from, Vector3 to, bool hit) => Shot?.Invoke(p, w, from, to, hit);
        public static void RaiseDamaged(ArenaPlayer v, ArenaPlayer a, int amount, Vector3 from) => Damaged?.Invoke(v, a, amount, from);
        public static void RaiseEliminated(ArenaPlayer v, ArenaPlayer a, WeaponId w) => Eliminated?.Invoke(v, a, w);
        public static void RaiseRespawned(ArenaPlayer p) => Respawned?.Invoke(p);
        public static void RaiseWeaponSwapped(ArenaPlayer p, WeaponId w) => WeaponSwapped?.Invoke(p, w);
        public static void RaiseReloadStarted(ArenaPlayer p) => ReloadStarted?.Invoke(p);
        public static void RaisePickedUp(ArenaPlayer p, PickupKind k, Vector3 at) => PickedUp?.Invoke(p, k, at);
        public static void RaiseFootstep(ArenaPlayer p, Vector3 at) => Footstep?.Invoke(p, at);
        public static void RaisePhaseChanged(int phase) => PhaseChanged?.Invoke(phase);
        public static void RaiseCountdown(int seconds) => Countdown?.Invoke(seconds);
    }
}
