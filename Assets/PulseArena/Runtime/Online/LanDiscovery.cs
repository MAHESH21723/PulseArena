using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace PulseArena
{
    // UDP broadcast discovery for same-Wi-Fi rooms.
    // Packet (UTF-8 text): PULSEARENA|1|CODE|HOSTNAME|PLAYERS|PORT   sent once a second to UDP 47777.
    static class LanDiscovery
    {
        public const int DiscoveryPort = 47777;
        const string GameId = "PULSEARENA";
        const string Protocol = "1";
        const float ExpireSeconds = 4f;

        sealed class Entry { public NearbyRoom Room; public string Ip; public int Port; }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static readonly List<NearbyRoom> view = new List<NearbyRoom>();
        static readonly ConcurrentQueue<KeyValuePair<byte[], IPAddress>> inbox = new ConcurrentQueue<KeyValuePair<byte[], IPAddress>>();

        static int listenRefs;
        static UdpClient listener;
        static object multicastLock;
        static Pump pump;

        // Advertising state (host)
        static bool advertising;
        static string advCode, advPort;
        static Func<int> advPlayers;
        static Func<string> advName;
        static UdpClient sender;
        static float nextSend;

        public static IReadOnlyList<NearbyRoom> Rooms { get { Prune(); return view; } }

        // ---------- listening ----------
        public static void StartListening()
        {
            listenRefs++;
            if (listener != null) return;
            EnsurePump();
            AcquireMulticastLock();
            try
            {
                var c = new UdpClient(AddressFamily.InterNetwork);
                c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                c.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                listener = c;
                ReceiveLoop(c);
            }
            catch (Exception e) { Debug.LogWarning("PULSE_LAN discovery listen failed: " + e.Message); listener = null; }
        }

        public static void StopListening()
        {
            if (listenRefs > 0) listenRefs--;
            if (listenRefs > 0) return;
            try { if (listener != null) listener.Close(); } catch (Exception) { }
            listener = null;
            ReleaseMulticastLock();
            entries.Clear(); view.Clear();
        }

        static async void ReceiveLoop(UdpClient c)
        {
            while (true)
            {
                try
                {
                    var r = await c.ReceiveAsync();
                    inbox.Enqueue(new KeyValuePair<byte[], IPAddress>(r.Buffer, r.RemoteEndPoint.Address));
                }
                catch (Exception) { break; } // socket closed
            }
        }

        public static bool TryResolve(string code, out string ip, out int port)
        {
            Drain();
            Entry e;
            if (entries.TryGetValue(code, out e)) { ip = e.Ip; port = e.Port; return true; }
            ip = null; port = 0; return false;
        }

        // ---------- advertising ----------
        public static void StartAdvertising(string code, int port, Func<int> players, Func<string> hostName)
        {
            advCode = code; advPort = port.ToString(); advPlayers = players; advName = hostName;
            EnsurePump();
            try
            {
                if (sender != null) sender.Close();
                sender = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            }
            catch (Exception e) { Debug.LogWarning("PULSE_LAN advertise socket failed: " + e.Message); sender = null; }
            advertising = true; nextSend = 0;
        }

        public static void StopAdvertising()
        {
            advertising = false;
            try { if (sender != null) sender.Close(); } catch (Exception) { }
            sender = null;
        }

        static void SendBeacon()
        {
            if (sender == null) return;
            string name = (advName != null ? advName() : "PILOT").Replace("|", "");
            int count = advPlayers != null ? advPlayers() : 1;
            string msg = GameId + "|" + Protocol + "|" + advCode + "|" + name + "|" + Mathf.Clamp(count, 0, 4) + "|" + advPort;
            var bytes = Encoding.UTF8.GetBytes(msg);
            var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
            var info = LanNet.Find();
            if (info.HasValue) targets.Add(info.Value.Broadcast);
            foreach (var t in targets)
            {
                try { sender.Send(bytes, bytes.Length, new IPEndPoint(t, DiscoveryPort)); }
                catch (Exception) { }
            }
        }

        // ---------- main-thread pump ----------
        static void Drain()
        {
            KeyValuePair<byte[], IPAddress> p;
            while (inbox.TryDequeue(out p))
            {
                string s; try { s = Encoding.UTF8.GetString(p.Key); } catch (Exception) { continue; }
                var f = s.Split('|');
                if (f.Length < 6 || f[0] != GameId || f[1] != Protocol) continue;
                if (advertising && f[2] == advCode) continue; // our own beacon
                int players, port;
                if (!int.TryParse(f[4], out players) || !int.TryParse(f[5], out port)) continue;
                entries[f[2]] = new Entry
                {
                    Ip = p.Value.ToString(), Port = port,
                    Room = new NearbyRoom { Code = f[2], HostName = f[3], Players = players, LastSeen = Time.realtimeSinceStartup }
                };
            }
            Rebuild();
        }

        static void Prune()
        {
            Drain();
            List<string> dead = null;
            foreach (var kv in entries)
                if (Time.realtimeSinceStartup - kv.Value.Room.LastSeen > ExpireSeconds)
                {
                    if (dead == null) dead = new List<string>();
                    dead.Add(kv.Key);
                }
            if (dead != null) { foreach (var k in dead) entries.Remove(k); Rebuild(); }
        }

        static void Rebuild()
        {
            view.Clear();
            foreach (var kv in entries) view.Add(kv.Value.Room);
            view.Sort((a, b) => string.CompareOrdinal(a.Code, b.Code));
        }

        static void EnsurePump()
        {
            if (pump) return;
            var go = new GameObject("PulseLanDiscovery") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            pump = go.AddComponent<Pump>();
        }

        sealed class Pump : MonoBehaviour
        {
            void Update()
            {
                if (listener != null) Prune();
                if (advertising && Time.realtimeSinceStartup >= nextSend) { nextSend = Time.realtimeSinceStartup + 1f; SendBeacon(); }
            }
            void OnDestroy()
            {
                StopAdvertising();
                try { if (listener != null) listener.Close(); } catch (Exception) { }
                listener = null; ReleaseMulticastLock(); listenRefs = 0;
            }
        }

        // ---------- Android multicast lock (needed to receive broadcasts on many phones) ----------
        static void AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (multicastLock != null) return;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var ctx = act.Call<AndroidJavaObject>("getApplicationContext"))
                using (var wifi = ctx.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    var l = wifi.Call<AndroidJavaObject>("createMulticastLock", "pulsearena-lan");
                    l.Call("setReferenceCounted", false);
                    l.Call("acquire");
                    multicastLock = l;
                }
            }
            catch (Exception e) { Debug.LogWarning("PULSE_LAN multicast lock failed: " + e.Message); }
#endif
        }

        static void ReleaseMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var l = multicastLock as AndroidJavaObject;
                if (l != null)
                {
                    if (l.Call<bool>("isHeld")) l.Call("release");
                    l.Dispose();
                }
            }
            catch (Exception) { }
            multicastLock = null;
#endif
        }
    }

    struct LanInterface { public IPAddress Address; public IPAddress Broadcast; }

    static class LanNet
    {
        static bool IsPrivate(IPAddress a)
        {
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        // Prefer Wi-Fi / Ethernet / hotspot interfaces with a private IPv4 address.
        public static LanInterface? Find()
        {
            LanInterface? best = null; int bestScore = -1;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string n = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    if (n.Contains("virtual") || n.Contains("vmware") || n.Contains("vbox") || n.Contains("docker") || n.Contains("rmnet") ||
                        n.Contains("tun") || n.Contains("ppp") || n.Contains("dummy") || n.Contains("bluetooth") || n.Contains("loopback")) continue;
                    int score = 1;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || n.Contains("wlan") || n.Contains("wi-fi") || n.Contains("wifi")) score = 3;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.Contains("eth")) score = 2;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ua.Address)) continue;
                        if (score <= bestScore) continue;
                        IPAddress mask; try { mask = ua.IPv4Mask; } catch (Exception) { mask = null; }
                        if (mask == null || mask.Equals(IPAddress.Any)) mask = IPAddress.Parse("255.255.255.0");
                        var ab = ua.Address.GetAddressBytes(); var mb = mask.GetAddressBytes(); var bb = new byte[4];
                        for (int i = 0; i < 4; i++) bb[i] = (byte)(ab[i] | ~mb[i]);
                        best = new LanInterface { Address = ua.Address, Broadcast = new IPAddress(bb) };
                        bestScore = score;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("PULSE_LAN interface scan failed: " + e.Message); }
            return best;
        }
    }
}
