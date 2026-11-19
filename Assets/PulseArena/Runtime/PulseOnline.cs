using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace PulseArena
{
    // How internet / local rooms are created and joined. PulseApp only talks to
    // IRoomService, so the backend (Epic Online Services, same-Wi-Fi LAN) can be
    // swapped without touching gameplay code. No Unity-billed service is used.
    public interface IRoomService
    {
        string Label { get; }
        /// Starts hosting (NetworkManager must be running as host when this returns). Returns the room code to share.
        Task<string> HostAsync(NetworkManager network);
        /// Joins a room by code (NetworkManager must be running as client when this returns). Returns the code.
        Task<string> JoinAsync(NetworkManager network, string code);
        Task LeaveAsync();
    }

    public enum OnlineMode { Internet, LocalWifi }

    public struct NearbyRoom
    {
        public string Code;
        public string HostName;
        public int Players;     // current players (max is 4)
        public float LastSeen;  // Time.realtimeSinceStartup of the last packet
    }

    public static class PulseOnline
    {
        const string ModeKey = "onlineMode";
        static OnlineMode? mode;
        static readonly LanRoomService lan = new LanRoomService();
#if PULSE_EOS
        static readonly EosRoomService eos = new EosRoomService();
#endif

        /// Which backend is used. Persisted. Defaults to LocalWifi unless the internet backend is configured.
        public static OnlineMode Mode
        {
            get
            {
                if (mode.HasValue) return mode.Value;
                int v = PlayerPrefs.GetInt(ModeKey, -1);
                if (v == 0 || v == 1) mode = (OnlineMode)v;
                else mode = InternetAvailable ? OnlineMode.Internet : OnlineMode.LocalWifi;
                return mode.Value;
            }
            set
            {
                mode = value;
                PlayerPrefs.SetInt(ModeKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// True when an internet backend is configured and usable.
        public static bool InternetAvailable
        {
            get
            {
#if PULSE_EOS
                return EosRoomService.Usable;
#else
                return false;
#endif
            }
        }

        public static string InternetUnavailableReason
        {
            get
            {
#if PULSE_EOS
                return EosRoomService.UnavailableReason;
#else
                return "Internet matches are coming in a future update. Play with friends on the same Wi-Fi now!";
#endif
            }
        }

        /// The service for the current Mode. Never null; an unavailable internet mode throws a readable error from HostAsync/JoinAsync.
        public static IRoomService Current
        {
            get
            {
                if (Mode == OnlineMode.LocalWifi) return lan;
#if PULSE_EOS
                if (InternetAvailable) return eos;
#endif
                return new UnavailableRoomService(InternetUnavailableReason);
            }
        }

        public static IReadOnlyList<NearbyRoom> NearbyRooms => LanDiscovery.Rooms;
        public static void StartDiscovery() => LanDiscovery.StartListening();
        public static void StopDiscovery() => LanDiscovery.StopListening();
    }

    sealed class UnavailableRoomService : IRoomService
    {
        readonly string reason;
        public UnavailableRoomService(string reason) { this.reason = reason; }
        public string Label => "Internet (unavailable)";
        public Task<string> HostAsync(NetworkManager network) => Task.FromException<string>(new InvalidOperationException(reason));
        public Task<string> JoinAsync(NetworkManager network, string code) => Task.FromException<string>(new InvalidOperationException(reason));
        public Task LeaveAsync() => Task.CompletedTask;
    }
}
