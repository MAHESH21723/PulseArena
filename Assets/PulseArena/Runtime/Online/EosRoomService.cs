#if PULSE_EOS
// Epic Online Services internet backend (SKELETON).
//
// Done here: config loading, Device ID login (no Epic account), private lobby with a short join code
// (attribute search), max 4 members.
// MISSING: an NGO transport over EOS P2P. As of 2026-11 no maintained one exists for NGO 2.x + Android
// (see ONLINE_SETUP.md). Until one is written/installed, set TransportReady below and wire
// ConfigureTransport(). Written against the EOS C# SDK API as bundled by com.playeveryware.eos 6.x;
// it has NOT been compiled against the SDK in this repo, so expect small signature fixes.
using System;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using Unity.Netcode;
using UnityEngine;

namespace PulseArena
{
    public sealed class EosRoomService : IRoomService
    {
        const string CodeAttr = "PULSECODE";
        const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        /// Flip to true once an NGO EOS P2P transport is installed and ConfigureTransport() is implemented.
        public static bool TransportReady = false;

        public static bool Usable { get { return UnavailableReason == null; } }
        public static string UnavailableReason
        {
            get
            {
                if (!TransportReady) return "Internet play needs the EOS peer-to-peer transport, which is not installed yet. Use Local Wi-Fi.";
                if (EOSManager.Instance == null) return "Internet play is not configured (EOS plugin not initialised; check credentials in .env).";
                return null;
            }
        }

        public string Label => "Internet (Epic Online Services)";

        string lobbyId;
        ProductUserId user;

        LobbyInterface Lobby { get { return EOSManager.Instance.GetEOSLobbyInterface(); } }

        async Task LoginAsync()
        {
            if (user != null && user.IsValid()) return;
            var connect = EOSManager.Instance.GetEOSConnectInterface();
            // 1) create the device id once per install (ignore "already exists")
            var tcs = new TaskCompletionSource<Result>();
            connect.CreateDeviceId(new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel }, null, (ref CreateDeviceIdCallbackInfo i) => tcs.TrySetResult(i.ResultCode));
            var r = await tcs.Task;
            if (r != Result.Success && r != Result.DuplicateNotAllowed) throw new InvalidOperationException("Device ID failed: " + r);
            // 2) log in with it
            var login = new TaskCompletionSource<LoginCallbackInfo>();
            connect.Login(new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = PlayerPrefs.GetString("pilot", "PILOT") }
            }, null, (ref LoginCallbackInfo i) => login.TrySetResult(i));
            var li = await login.Task;
            if (li.ResultCode == Result.InvalidUser) throw new InvalidOperationException("First-time EOS user creation not implemented (CreateUser).");
            if (li.ResultCode != Result.Success) throw new InvalidOperationException("EOS login failed: " + li.ResultCode);
            user = li.LocalUserId;
        }

        static string NewCode()
        {
            var c = new char[6];
            for (int i = 0; i < 6; i++) c[i] = Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)];
            return new string(c);
        }

        public async Task<string> HostAsync(NetworkManager network)
        {
            if (!TransportReady) throw new NotSupportedException(UnavailableReason);
            await LoginAsync();
            string code = NewCode();
            var create = new TaskCompletionSource<CreateLobbyCallbackInfo>();
            Lobby.CreateLobby(new CreateLobbyOptions
            {
                LocalUserId = user, MaxLobbyMembers = 4, PermissionLevel = LobbyPermissionLevel.Joinviapresence,
                BucketId = "pulse", PresenceEnabled = false, AllowInvites = false
            }, null, (ref CreateLobbyCallbackInfo i) => create.TrySetResult(i));
            var ci = await create.Task;
            if (ci.ResultCode != Result.Success) throw new InvalidOperationException("Lobby create failed: " + ci.ResultCode);
            lobbyId = ci.LobbyId;

            var mod = new UpdateLobbyModificationOptions { LobbyId = lobbyId, LocalUserId = user };
            Lobby.UpdateLobbyModification(ref mod, out var handle);
            var attr = new AttributeData { Key = CodeAttr, Value = code };
            var add = new LobbyModificationAddAttributeOptions { Attribute = attr, Visibility = LobbyAttributeVisibility.Public };
            handle.AddAttribute(ref add);
            var up = new TaskCompletionSource<Result>();
            Lobby.UpdateLobby(new UpdateLobbyOptions { LobbyModificationHandle = handle }, null, (ref UpdateLobbyCallbackInfo i) => up.TrySetResult(i.ResultCode));
            if (await up.Task != Result.Success) throw new InvalidOperationException("Lobby update failed.");

            ConfigureTransport(network, true, user, null);
            if (!network.StartHost()) throw new InvalidOperationException("Could not start host.");
            return code;
        }

        public async Task<string> JoinAsync(NetworkManager network, string code)
        {
            if (!TransportReady) throw new NotSupportedException(UnavailableReason);
            await LoginAsync();
            // TODO: LobbySearch by attribute PULSECODE == code -> JoinLobby -> read owner ProductUserId -> ConfigureTransport(client) -> StartClient.
            throw new NotImplementedException("EOS lobby search/join not finished; see ONLINE_SETUP.md.");
        }

        public Task LeaveAsync()
        {
            var id = lobbyId; lobbyId = null;
            if (id == null || user == null) return Task.CompletedTask;
            var tcs = new TaskCompletionSource<bool>();
            Lobby.LeaveLobby(new LeaveLobbyOptions { LobbyId = id, LocalUserId = user }, null, (ref LeaveLobbyCallbackInfo i) => tcs.TrySetResult(true));
            return tcs.Task;
        }

        // Point the NGO transport (EOS P2P) at our local user / the host's ProductUserId.
        static void ConfigureTransport(NetworkManager network, bool host, ProductUserId local, ProductUserId remoteHost)
        {
            throw new NotImplementedException("No NGO EOS P2P transport installed.");
        }
    }
}
#endif
