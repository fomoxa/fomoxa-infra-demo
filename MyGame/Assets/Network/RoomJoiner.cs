using System;
using System.Threading.Tasks;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Fomoxa.Unity;
using Generated;
using MyGame.Backend;
using MyGame.Backend.Models;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class RoomJoiner : MonoBehaviour
    {
        public NetworkManager manager;
        public NetworkTransport backendTransport;
        public string backendAddress = "127.0.0.1";
        public ushort backendPort = 9600;

        private ITransportConnector connector;
        private TaskCompletionSource<ITransport> connecting;
        private RpcConnection connection;

        /// <summary>Trạng thái hiện tại để lobby (Launcher) hiển thị.</summary>
        public string Status { get; private set; } = "";
        public bool IsError { get; private set; }
        public bool Busy { get; private set; }

        /// <summary>Đặt thông báo thường (không lỗi) cho lobby, ví dụ khi rời phòng.</summary>
        public void SetNotice(string text) => SetStatus(text);

        public void ClearStatus()
        {
            if (!Busy)
            {
                SetStatus("");
            }
        }

        private void SetStatus(string text, bool error = false)
        {
            Status = text;
            IsError = error;
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-autojoin") >= 0)
            {
                string room = "arena:1";
                foreach (string arg in args)
                {
                    if (arg.StartsWith("-room:", StringComparison.Ordinal))
                    {
                        room = arg.Substring("-room:".Length);
                    }
                }

                _ = Join(PlayerName(args), room);
            }
        }

        private static string PlayerName(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-name:", StringComparison.Ordinal))
                {
                    return arg.Substring("-name:".Length);
                }
            }

            return "player";
        }

        private static string DeviceId(string[] args, string player)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-device:", StringComparison.Ordinal))
                {
                    return arg.Substring("-device:".Length);
                }
            }

            return $"fomoxa-device-{player}";
        }

        /// <summary>Đã đăng nhập backend (có thể xem phòng và vào phòng).</summary>
        public bool LoggedIn => connection != null && loggedInAs != null;

        public string LoggedInAs => loggedInAs ?? "";

        /// <summary>Danh sách phòng từ lần RefreshRooms gần nhất.</summary>
        public System.Collections.Generic.IReadOnlyList<RoomInfo> Rooms => rooms;

        /// <summary>Tăng mỗi khi danh sách phòng đổi, để UI biết cần dựng lại.</summary>
        public int RoomsVersion { get; private set; }

        /// <summary>Người đang online (từ Lobby.Presence, làm mới mỗi vài giây).</summary>
        public System.Collections.Generic.IReadOnlyList<OnlinePlayer> Online => online;

        public int OnlineVersion { get; private set; }

        private readonly System.Collections.Generic.List<OnlinePlayer> online = new System.Collections.Generic.List<OnlinePlayer>();
        private string currentRoom = "";
        private float nextPoll;
        private bool polling;
        private string roomsSignature = "";
        private string onlineSignature = "";
        private const float PollSeconds = 5f;

        /// <summary>Client đã rời phòng (Launcher gọi) - quay về sảnh.</summary>
        public void LeftRoom() => currentRoom = "";

        private string loggedInAs;
        private readonly System.Collections.Generic.List<RoomInfo> rooms = new System.Collections.Generic.List<RoomInfo>();

        /// <summary>Đăng nhập (tên + mật khẩu). Lần đầu dùng một tên thì tài khoản được tạo.</summary>
        public async Task Login(string player, string password)
        {
            if (Busy)
            {
                return;
            }

            Busy = true;
            try
            {
                await LoginCore(player, password);
                await RefreshRoomsCore();
                await PresenceCore();
                nextPoll = Time.unscaledTime + PollSeconds;
            }
            catch (Exception error)
            {
                Fail(error);
            }
            finally
            {
                Busy = false;
            }
        }

        public void Logout()
        {
            if (Busy)
            {
                return;
            }

            connection?.Dispose();
            connection = null;
            loggedInAs = null;
            currentRoom = "";
            rooms.Clear();
            online.Clear();
            roomsSignature = "";
            onlineSignature = "";
            RoomsVersion++;
            OnlineVersion++;
            SetStatus("Đã đăng xuất");
        }

        public async Task RefreshRooms()
        {
            if (Busy)
            {
                return;
            }

            Busy = true;
            try
            {
                await RefreshRoomsCore();
            }
            catch (Exception error)
            {
                Fail(error);
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Vào phòng khi đã đăng nhập (dùng bởi lobby).</summary>
        public async Task JoinRoom(string roomKey)
        {
            if (Busy)
            {
                return;
            }

            if (!LoggedIn)
            {
                SetStatus("Hãy đăng nhập trước", true);
                return;
            }

            Busy = true;
            try
            {
                await JoinCore(roomKey);
            }
            catch (Exception error)
            {
                Fail(error);
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Đăng nhập (không mật khẩu, qua DeviceId) rồi vào phòng. Dùng cho -autojoin và các script test.</summary>
        public async Task Join(string player, string roomKey)
        {
            if (Busy)
            {
                return;
            }

            player = (player ?? "").Trim();
            if (player.Length == 0)
            {
                SetStatus("Hãy nhập tên người chơi", true);
                return;
            }

            Busy = true;
            try
            {
                await LoginCore(player, "");
                await JoinCore(roomKey);
            }
            catch (Exception error)
            {
                Fail(error);
            }
            finally
            {
                Busy = false;
            }
        }

        private async Task LoginCore(string player, string password)
        {
            player = (player ?? "").Trim();
            if (player.Length == 0)
            {
                throw new InvalidOperationException("Hãy nhập tên người chơi");
            }

            // Bỏ kết nối cũ (đổi tài khoản hoặc đăng nhập lại).
            connection?.Dispose();
            connection = null;
            loggedInAs = null;

            connecting = new TaskCompletionSource<ITransport>();
            SetStatus($"Đang kết nối backend {backendAddress}:{backendPort}...");
            Debug.Log($"[Join] connecting to {backendAddress}:{backendPort}");
            connector = backendTransport.CreateFactory().CreateConnector(backendAddress, backendPort);
            ITransport transport = await connecting.Task;

            string[] args = Environment.GetCommandLineArgs();
            var login = new PlayerLogin { Player = player, DeviceId = DeviceId(args, player) };
            if (!string.IsNullOrEmpty(password))
            {
                login.Secret = System.Text.Encoding.UTF8.GetBytes(password);
            }

            var options = new ConnectOptions
            {
                Credential = Credential.Of(PlayersMethods.Login, login),
            };

            SetStatus($"Đang đăng nhập '{player}'...");
            connection = RpcConnection.Driven(transport, NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options);
            await connection.Opened;
            loggedInAs = player;
            SetStatus($"Đã đăng nhập: {player}");
            Debug.Log($"[Join] logged in as {player}");
        }

        private async Task RefreshRoomsCore()
        {
            RoomList list = await new LobbyClient(connection).ListRoomsAsync(new ListRoomsRequest(), CallOptions.Within(TimeSpan.FromSeconds(10)));
            string signature = string.Join(";", System.Linq.Enumerable.Select(list.Rooms, r => $"{r.RoomKey}|{r.Players}|{r.Capacity}|{r.State}"));
            if (signature != roomsSignature)
            {
                roomsSignature = signature;
                rooms.Clear();
                rooms.AddRange(list.Rooms);
                RoomsVersion++;
                Debug.Log($"[Lobby] {rooms.Count} phòng");
            }
        }

        private async Task PresenceCore()
        {
            OnlineList list = await new LobbyClient(connection).PresenceAsync(
                new PresenceRequest { Room = currentRoom }, CallOptions.Within(TimeSpan.FromSeconds(10)));
            string signature = string.Join(";", System.Linq.Enumerable.Select(list.Players, p => $"{p.Name}|{p.Room}"));
            if (signature != onlineSignature)
            {
                onlineSignature = signature;
                online.Clear();
                online.AddRange(list.Players);
                OnlineVersion++;
            }
        }

        /// <summary>Mỗi vài giây: báo "còn online" + lấy danh sách online; ở sảnh thì làm mới cả danh sách phòng.</summary>
        private async Task PollOnce()
        {
            polling = true;
            try
            {
                await PresenceCore();
                if (!Busy && currentRoom.Length == 0)
                {
                    await RefreshRoomsCore();
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Lobby] poll loi: {error.Message}");
                if (connection == null || !connection.IsOpen)
                {
                    loggedInAs = null;
                    SetStatus("Mất kết nối backend, hãy đăng nhập lại", true);
                }
            }
            finally
            {
                polling = false;
            }
        }

        private async Task JoinCore(string roomKey)
        {
            SetStatus(roomKey == "auto"
                ? "Đang ghép trận (chờ người chơi khác, tối đa 30 giây)..."
                : $"Đang vào phòng {roomKey} (cần 2 người cùng phòng)...");

            RoomTicket ticket = await JoinWithRetry(new LobbyClient(connection), new JoinRoomRequest
            {
                RoomKey = roomKey,
                Version = Application.version,
            });
            currentRoom = roomKey;
            Debug.Log($"[Join] room {roomKey} -> {ticket.Address}:{ticket.Port}, grant {ticket.Connection.Length} bytes");
            SetStatus($"Đã nhận ticket phòng {roomKey}. Đang kết nối game server {ticket.Address}:{ticket.Port}...");
            manager.ClientManager.StartConnection(ticket.Address, ticket.Port, ticket.Connection);
        }

        private void Fail(Exception error)
        {
            if (error is RpcException failed)
            {
                if (failed.Status.Details is NoRoomForVersion missing)
                {
                    Debug.LogWarning($"[Join] no pod runs version {missing.Version}");
                    SetStatus($"Không có game server chạy phiên bản {missing.Version}", true);
                }
                else
                {
                    Debug.LogError($"[Join] failed: {failed.Status}");
                    SetStatus(failed.Status.Code == StatusCode.Unauthenticated
                        ? $"Đăng nhập thất bại: {failed.Status.Message}"
                        : $"Thất bại: {failed.Status.Message}", true);
                    if (failed.Status.Code == StatusCode.Unauthenticated)
                    {
                        loggedInAs = null;
                    }
                }
            }
            else
            {
                Debug.LogError($"[Join] error: {error.Message}");
                SetStatus($"Lỗi: {error.Message}", true);
                // Lỗi kết nối: coi như chưa đăng nhập để người dùng đăng nhập lại.
                if (connection == null)
                {
                    loggedInAs = null;
                }
            }
        }

        private static readonly TimeSpan MatchBudget = TimeSpan.FromSeconds(30);

        private static async Task<RoomTicket> JoinWithRetry(LobbyClient lobby, JoinRoomRequest request)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await lobby.JoinRoomAsync(request, CallOptions.Within(MatchBudget));
                }
                catch (RpcException failed) when (failed.Status.Code == StatusCode.Unavailable && attempt < 3)
                {
                    Debug.Log($"[Join] {failed.Status.Message} (thử lại lần {attempt})");
                }
            }
        }

        private void Update()
        {
            if (connector != null)
            {
                ConnectStatus status = connector.Poll(out ITransport transport);
                if (status == ConnectStatus.Connected)
                {
                    connector.Dispose();
                    connector = null;
                    connecting.SetResult(transport);
                }
                else if (status != ConnectStatus.Pending)
                {
                    connector.Dispose();
                    connector = null;
                    connecting.SetException(new InvalidOperationException($"cannot reach the backend: {status}"));
                }
            }

            connection?.Tick(MonotonicClock.Now);

            if (LoggedIn && !polling && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollSeconds;
                _ = PollOnce();
            }
        }

        private void OnDestroy()
        {
            connector?.Dispose();
            connection?.Dispose();
        }
    }
}
