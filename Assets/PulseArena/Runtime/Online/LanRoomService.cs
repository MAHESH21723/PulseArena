using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PulseArena
{
    // Same-Wi-Fi rooms over direct UnityTransport connections. Free forever, no servers.
    //
    // Room code = 6 characters (Crockford base32, 30 bits):
    //   [12-bit random salt][8-bit 3rd octet][8-bit 4th octet][2-bit port offset]
    // The joiner supplies the first two octets from its own Wi-Fi address, so the code only
    // decodes correctly when both phones are on the same /16 network (true for normal home
    // routers: 192.168.x.x). Discovery by UDP broadcast is tried first and does not rely on that.
    public sealed class LanRoomService : IRoomService
    {
        public const int BasePort = 7790;
        const int PortSlots = 4;
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public string Label => "Local Wi-Fi";

        public Task<string> HostAsync(NetworkManager network)
        {
            var info = LanNet.Find();
            if (!info.HasValue) throw new InvalidOperationException("No Wi-Fi network found. Connect to Wi-Fi (or turn on your phone hotspot) and try again.");
            int slot = -1;
            for (int i = 0; i < PortSlots; i++) if (PortFree(BasePort + i)) { slot = i; break; }
            if (slot < 0) throw new InvalidOperationException("Local ports " + BasePort + "-" + (BasePort + PortSlots - 1) + " are busy.");
            int port = BasePort + slot;
            var ab = info.Value.Address.GetAddressBytes();
            string code = Encode(ab[2], ab[3], slot);

            network.GetComponent<UnityTransport>().SetConnectionData(info.Value.Address.ToString(), (ushort)port, "0.0.0.0");
            if (!network.StartHost()) throw new InvalidOperationException("Could not start the room on this network.");
            LanDiscovery.StartAdvertising(code, port,
                () => network.IsListening ? network.ConnectedClientsIds.Count : 1,
                () => PlayerPrefs.GetString("pilot", "PILOT"));
            Debug.Log("PULSE_LAN hosting " + info.Value.Address + ":" + port + " code " + code);
            return Task.FromResult(code);
        }

        public async Task<string> JoinAsync(NetworkManager network, string code)
        {
            code = Normalize(code);
            if (code.Length != 6) throw new InvalidOperationException("A Wi-Fi room code has 6 characters.");

            string ip = null; int port = 0;
            LanDiscovery.StartListening();
            try
            {
                // 1) discovered rooms (wait briefly for a beacon)
                float end = Time.realtimeSinceStartup + 1.6f;
                while (!LanDiscovery.TryResolve(code, out ip, out port) && Time.realtimeSinceStartup < end) await Task.Delay(100);
            }
            finally { LanDiscovery.StopListening(); }

            // 2) decode the code using our own network prefix
            if (ip == null)
            {
                var info = LanNet.Find();
                if (!info.HasValue) throw new InvalidOperationException("No Wi-Fi network found. Connect to the same Wi-Fi as your host.");
                int o3, o4, slot;
                if (!TryDecode(code, out o3, out o4, out slot)) throw new InvalidOperationException("That room code is not valid.");
                var ab = info.Value.Address.GetAddressBytes();
                ip = ab[0] + "." + ab[1] + "." + o3 + "." + o4;
                port = BasePort + slot;
            }

            network.GetComponent<UnityTransport>().SetConnectionData(ip, (ushort)port);
            if (!network.StartClient()) throw new InvalidOperationException("Could not start the connection.");
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!network.IsConnectedClient)
            {
                if (!network.IsListening) throw new InvalidOperationException("The host refused or could not be reached.");
                if (Time.realtimeSinceStartup > timeout) throw new InvalidOperationException("No room found for that code on this Wi-Fi.");
                await Task.Delay(100);
            }
            return code;
        }

        public Task LeaveAsync()
        {
            LanDiscovery.StopAdvertising();
            return Task.CompletedTask;
        }

        // ---------- code helpers ----------
        public static string Normalize(string code)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (code ?? "").ToUpperInvariant())
            {
                char c = ch;
                if (c == 'O') c = '0'; else if (c == 'I' || c == 'L') c = '1'; else if (c == 'U') c = 'V';
                if (Alphabet.IndexOf(c) >= 0) sb.Append(c);
            }
            return sb.ToString();
        }

        static string Encode(int o3, int o4, int slot)
        {
            uint salt = (uint)UnityEngine.Random.Range(0, 4096);
            uint v = (salt << 18) | ((uint)o3 << 10) | ((uint)o4 << 2) | (uint)slot;
            var c = new char[6];
            for (int i = 5; i >= 0; i--) { c[i] = Alphabet[(int)(v & 31)]; v >>= 5; }
            return new string(c);
        }

        static bool TryDecode(string code, out int o3, out int o4, out int slot)
        {
            o3 = o4 = slot = 0;
            uint v = 0;
            foreach (char ch in code) { int i = Alphabet.IndexOf(ch); if (i < 0) return false; v = (v << 5) | (uint)i; }
            slot = (int)(v & 3); o4 = (int)((v >> 2) & 255); o3 = (int)((v >> 10) & 255);
            return true;
        }

        static bool PortFree(int port)
        {
            try { using (var u = new UdpClient(new IPEndPoint(IPAddress.Any, port))) return true; }
            catch (Exception) { return false; }
        }
    }
}
