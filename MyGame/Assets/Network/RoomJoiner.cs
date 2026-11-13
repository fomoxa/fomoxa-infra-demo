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
        /// <summary>Không còn dùng (NetworkManager nằm trong scene Game); giữ để scene cũ không mất tham chiếu.</summary>
        [HideInInspector] public NetworkManager manager;
        public NetworkTransport backendTransport;
        public string backendAddress = "127.0.0.1";
        public ushort backendPort = 9600;

        [Header("TLS (mã hoá đường client - backend)")]
        [Tooltip("Bật khi backend đứng sau proxy TLS (cổng 9643). Mật khẩu đi qua đường này nên bật khi ra khỏi mạng nội bộ.")]
        public bool useTls;
        [Tooltip("Vân tay SHA-256 của chứng chỉ (hex). Dùng cho chứng chỉ tự ký; để trống nếu chứng chỉ do CA công cộng cấp.")]
        public string tlsPinSha256 = "";

        /// <summary>
        /// RoomJoiner sống xuyên suốt các scene (sảnh ↔ trận): giữ phiên đăng nhập và tiếp tục báo online khi đang chơi.
        /// </summary>
        public static RoomJoiner Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Quay về scene sảnh sẽ nạp lại bản RoomJoiner của scene: bỏ bản trùng, dùng bản đang sống.
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

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

        private void ApplyCommandLine(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-backend:", StringComparison.Ordinal))
                {
                    string value = arg.Substring("-backend:".Length);
                    int colon = value.LastIndexOf(':');
                    if (colon > 0 && ushort.TryParse(value.Substring(colon + 1), out ushort port))
                    {
                        backendAddress = value.Substring(0, colon);
                        backendPort = port;
                    }
                }
                else if (arg == "-tls")
                {
                    useTls = true;
                }
                else if (arg.StartsWith("-tlspin:", StringComparison.Ordinal))
                {
                    useTls = true;
                    tlsPinSha256 = arg.Substring("-tlspin:".Length);
                }
            }
        }

        private ITransportFactory BackendFactory()
        {
            if (!useTls)
            {
                return backendTransport.CreateFactory();
            }

            var settings = new TlsSettings();
            if (!string.IsNullOrWhiteSpace(tlsPinSha256))
            {
                if (!PinnedCertificateCheck.IsValidPin(tlsPinSha256))
                {
                    throw new InvalidOperationException("tlsPinSha256 không phải SHA-256 dạng hex (64 ký tự hex, có thể có dấu ':')");
                }

                settings.CertificateCheck = new PinnedCertificateCheck(tlsPinSha256);
            }

            return new TcpTransportFactory(settings);
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
        public void LeftRoom()
        {
            currentRoom = "";
            board.Clear();
            boardSignature = "";
            BoardVersion++;
        }

        /// <summary>Hồ sơ của chính mình (kỷ lục số bậc, số trận).</summary>
        public PlayerProfile Profile { get; private set; }

        public int ProfileVersion { get; private set; }

        /// <summary>Bạn bè, lời mời kết bạn, lời mời vào phòng.</summary>
        public SocialState Social { get; private set; }

        public int SocialVersion { get; private set; }

        /// <summary>Bảng điểm của phòng đang chơi.</summary>
        public System.Collections.Generic.IReadOnlyList<ScoreEntry> Board => board;

        public int BoardVersion { get; private set; }

        public string CurrentRoom => currentRoom;

        private readonly System.Collections.Generic.List<ScoreEntry> board = new System.Collections.Generic.List<ScoreEntry>();
        private string socialSignature = "";
        private string profileSignature = "";
        private string boardSignature = "";
        private float nextBoard;
        private bool boardPolling;
        private string loggedInAs;
        private readonly System.Collections.Generic.List<RoomInfo> rooms = new System.Collections.Generic.List<RoomInfo>();

        /// <summary>Mã khôi phục vừa được cấp (sau khi đăng ký / khôi phục). Chỉ hiện một lần: gọi <see cref="AcknowledgeRecoveryCode"/> khi người chơi đã lưu.</summary>
        public string NewRecoveryCode { get; private set; } = "";

        public void AcknowledgeRecoveryCode() => NewRecoveryCode = "";

        public Task Login(string player, string password) => Authenticate(player, password, "login", "", "");

        /// <summary>Tạo tài khoản mới (tên chưa ai dùng). Thành công thì <see cref="NewRecoveryCode"/> có mã khôi phục.</summary>
        public Task Register(string player, string password) => Authenticate(player, password, "register", "", "");

        /// <summary>Đặt lại mật khẩu bằng mã khôi phục; thành công thì có mã khôi phục MỚI.</summary>
        public Task Recover(string player, string recoveryCode, string newPassword) => Authenticate(player, newPassword, "recover", recoveryCode, "");

        private async Task Authenticate(string player, string password, string mode, string recovery, string _)
        {
            if (Busy)
            {
                return;
            }

            Busy = true;
            try
            {
                await LoginCore(player, password, mode, recovery);
                if (mode != "login")
                {
                    ActionResult taken = await new LobbyClient(connection).TakeRecoveryCodeAsync(new RecoveryCodeRequest(), CallOptions.Within(TimeSpan.FromSeconds(10)));
                    NewRecoveryCode = taken.Ok ? taken.Message : "";
                }

                await RefreshRoomsCore();
                await PresenceCore();
                await SocialCore();
                await ProfileCore();
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
            socialSignature = "";
            profileSignature = "";
            boardSignature = "";
            Social = null;
            Profile = null;
            board.Clear();
            NewRecoveryCode = "";
            RoomsVersion++;
            OnlineVersion++;
            SocialVersion++;
            ProfileVersion++;
            BoardVersion++;
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
                await LoginCore(player, "", "", "");
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

        private async Task LoginCore(string player, string password, string mode, string recovery)
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
            ApplyCommandLine(Environment.GetCommandLineArgs());
            connector = BackendFactory().CreateConnector(backendAddress, backendPort);
            ITransport transport = await connecting.Task;

            string[] args = Environment.GetCommandLineArgs();
            var login = new PlayerLogin { Player = player, DeviceId = DeviceId(args, player), Mode = mode };
            if (!string.IsNullOrEmpty(password))
            {
                login.Secret = System.Text.Encoding.UTF8.GetBytes(password);
            }

            if (!string.IsNullOrEmpty(recovery))
            {
                login.Recovery = System.Text.Encoding.UTF8.GetBytes(recovery);
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
                Debug.Log($"[Lobby] {rooms.Count} rooms");
            }
        }

        private async Task SocialCore()
        {
            SocialState state = await new LobbyClient(connection).SocialAsync(new SocialRequest(), CallOptions.Within(TimeSpan.FromSeconds(10)));
            string signature = string.Join(";", System.Linq.Enumerable.Select(state.Friends, f => $"{f.Name}|{f.Online}|{f.Room}|{f.BestSteps}"))
                + "#" + string.Join(",", state.Incoming) + "#" + string.Join(",", state.Outgoing)
                + "#" + string.Join(";", System.Linq.Enumerable.Select(state.Invites, i => $"{i.From}|{i.Room}"));
            if (signature != socialSignature)
            {
                socialSignature = signature;
                Social = state;
                SocialVersion++;
            }
        }

        private async Task ProfileCore()
        {
            PlayerProfile profile = await new LobbyClient(connection).GetProfileAsync(new ProfileRequest(), CallOptions.Within(TimeSpan.FromSeconds(10)));
            string signature = $"{profile.Name}|{profile.BestSteps}|{profile.Matches}";
            if (signature != profileSignature)
            {
                profileSignature = signature;
                Profile = profile;
                ProfileVersion++;
            }
        }

        private async Task BoardCore()
        {
            ScoreboardState state = await new LobbyClient(connection).ScoreboardAsync(
                new ScoreboardRequest { Room = currentRoom }, CallOptions.Within(TimeSpan.FromSeconds(5)));
            string signature = string.Join(";", System.Linq.Enumerable.Select(state.Entries, e => $"{e.Name}|{e.Steps}|{e.Best}"));
            if (signature != boardSignature)
            {
                boardSignature = signature;
                board.Clear();
                board.AddRange(state.Entries);
                BoardVersion++;
            }
        }

        private async Task PollBoard()
        {
            boardPolling = true;
            try
            {
                await BoardCore();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Board] {error.Message}");
            }
            finally
            {
                boardPolling = false;
            }
        }

        // ---------------------------------------------------------------- hành động tài khoản / bạn bè

        public async Task ChangePassword(string oldPassword, string newPassword) =>
            await Act(async () =>
            {
                ActionResult result = await new LobbyClient(connection).ChangePasswordAsync(
                    new ChangePasswordRequest { OldPassword = oldPassword, NewPassword = newPassword }, CallOptions.Within(TimeSpan.FromSeconds(15)));
                SetStatus(result.Ok ? "Đã đổi mật khẩu" : result.Message, !result.Ok);
            });

        /// <summary>action: request | accept | decline | remove | dismiss-invite.</summary>
        public async Task FriendAction(string action, string target) =>
            await Act(async () =>
            {
                ActionResult result = await new LobbyClient(connection).FriendActionAsync(
                    new FriendActionRequest { Action = action, Target = target }, CallOptions.Within(TimeSpan.FromSeconds(10)));
                if (!string.IsNullOrEmpty(result.Message))
                {
                    SetStatus(result.Message, !result.Ok);
                }

                await SocialCore();
            });

        public async Task Invite(string friend) =>
            await Act(async () =>
            {
                ActionResult result = await new LobbyClient(connection).InviteAsync(
                    new InviteRequest { To = friend, Room = currentRoom }, CallOptions.Within(TimeSpan.FromSeconds(10)));
                SetStatus(result.Ok ? $"Đã mời {friend} vào {currentRoom}" : result.Message, !result.Ok);
            });

        private async Task Act(Func<Task> action)
        {
            if (!LoggedIn)
            {
                return;
            }

            try
            {
                await action();
            }
            catch (Exception error)
            {
                Fail(error);
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
                await SocialCore();
                if (!Busy && currentRoom.Length == 0)
                {
                    await RefreshRoomsCore();
                    await ProfileCore();
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Lobby] poll error: {error.Message}");
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
            SetStatus($"Đã nhận ticket phòng {roomKey}. Đang tải màn chơi...");
            // Scene trận đấu chỉ được tải sau khi đã có ticket; Launcher trong scene đó sẽ nối vào pod.
            MatchSession.Begin(roomKey, ticket);
        }

        private void Fail(Exception error)
        {
            // Đăng nhập bị từ chối: lấy thông điệp thật từ backend (sai mật khẩu, tên đã có người dùng, mã khôi phục sai...).
            if (error is ConnectException rejected && rejected.Status != null)
            {
                Debug.LogWarning($"[Join] rejected: {rejected.Status}");
                SetStatus(rejected.Status.Message, true);
                loggedInAs = null;
                return;
            }

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
                    Debug.Log($"[Join] {failed.Status.Message} (retry {attempt})");
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
                    string detail = status == ConnectStatus.TlsRejected && connector is ITlsRejection rejection
                        ? $" ({rejection.TlsRejection})"
                        : "";
                    connector.Dispose();
                    connector = null;
                    connecting.SetException(new InvalidOperationException($"không kết nối được backend: {status}{detail}"));
                }
            }

            connection?.Tick(MonotonicClock.Now);

            if (LoggedIn && currentRoom.Length > 0 && !boardPolling && Time.unscaledTime >= nextBoard)
            {
                nextBoard = Time.unscaledTime + 1.5f;
                _ = PollBoard();
            }

            if (LoggedIn && !polling && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollSeconds;
                _ = PollOnce();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            else
            {
                return;
            }

            connector?.Dispose();
            connection?.Dispose();
        }
    }
}
