using System.Collections.Generic;
using UnityEngine;

namespace PulseArena
{
    // The ONLY place where UI/audio/haptics code touches APIs owned by other agents.
    // If a name differs, fix it here and nothing else needs to change.
    public static class GameApi
    {
        static PulseApp App => PulseApp.Instance;

        // ---- PulseApp (combat agent)
        public static bool IsPlaying => App && App.IsPlaying;
        public static bool InRound => App && App.InRound;
        public static float ShakeScale { get => PulseApp.ShakeScale; set => PulseApp.ShakeScale = value; }

        // ---- ArenaPlayer (combat agent)
        public static int Magazine(ArenaPlayer p) => p.MagazineOf(p.CurrentWeapon);
        public static int Reserve(ArenaPlayer p) => p.CurrentReserve;
        public static WeaponDef Def(ArenaPlayer p) => p.CurrentDef;
        public static WeaponId Weapon(ArenaPlayer p) => p.CurrentWeapon;
        public static void SwapWeapon() { var p = ArenaPlayer.Local; if (p && p.Alive) p.RequestSwap(); }
        public static void SwapWeapon(WeaponId id) { var p = ArenaPlayer.Local; if (p && p.Alive) p.RequestSwap(id); }

        // ---- ArenaMatch (combat agent)
        public static double TimeLeft => ArenaMatch.Instance ? ArenaMatch.Instance.TimeLeft : 0;

        // ---- PulseOnline (online agent)
        public static OnlineMode Mode { get => PulseOnline.Mode; set => PulseOnline.Mode = value; }
        public static IReadOnlyList<NearbyRoom> NearbyRooms => PulseOnline.NearbyRooms;
        public static void StartDiscovery() => PulseOnline.StartDiscovery();
        public static void StopDiscovery() => PulseOnline.StopDiscovery();
        public static bool InternetAvailable => PulseOnline.InternetAvailable;
        public static string InternetUnavailableReason => PulseOnline.InternetUnavailableReason;
    }
}
