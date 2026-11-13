using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyGame.Backend.Accounts;
using MyGame.Backend.Models;
using Fomoxa.Rpc;

namespace MyGame.Backend
{
    /// <summary>
    /// Session token của Nakama theo user id, để Lobby gọi RPC thay người chơi.
    /// </summary>
    public sealed class PlayerSessions
    {
        private readonly Dictionary<string, string> tokens = new Dictionary<string, string>();
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        private readonly Dictionary<string, string> userIds = new Dictionary<string, string>();

        /// <summary>Principal (tên tài khoản) -> user id trong Nakama, dùng khi xác minh ticket ghép trận.</summary>
        public void SetUser(string principal, string userId)
        {
            lock (tokens)
            {
                userIds[principal] = userId;
            }
        }

        public string UserIdOf(string principal)
        {
            lock (tokens)
            {
                return userIds.TryGetValue(principal, out string id) ? id : principal;
            }
        }

        public void SetName(string userId, string name)
        {
            lock (tokens)
            {
                names[userId] = name;
            }
        }

        public string NameOf(string userId)
        {
            lock (tokens)
            {
                return names.TryGetValue(userId, out string name) ? name : userId;
            }
        }

        public void Set(string userId, string token)
        {
            lock (tokens)
            {
                tokens[userId] = token;
            }
        }

        public bool TryGet(string userId, out string token)
        {
            lock (tokens)
            {
                return tokens.TryGetValue(userId, out token!);
            }
        }
    }

    public sealed class Players : IAuthenticator<PlayerLogin, PlayerWelcome>
    {
        private readonly NakamaLink nakama;
        private readonly PlayerSessions sessions;
        private readonly AccountAuth accounts;

        public Players(NakamaLink nakama, PlayerSessions sessions, AccountAuth accounts)
        {
            this.nakama = nakama;
            this.sessions = sessions;
            this.accounts = accounts;
        }

        public UnaryMethod<PlayerLogin, PlayerWelcome> Method => PlayersMethods.Login;

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
            // Có tài khoản (Mode / mật khẩu): kiểm tra qua AccountService; principal = tên chuẩn hoá.
            if (AccountAuth.UsesAccounts(request))
            {
                AuthOutcome outcome = accounts.Authenticate(request);
                if (nakama != null)
                {
                    LinkNakama(outcome);
                }

                sessions.SetName(outcome.Principal, outcome.DisplayName);
                Console.WriteLine($"[Account] {outcome.DisplayName} login ({(request.Mode.Length == 0 ? "login" : request.Mode)})");
                return (new Identity(outcome.Principal, new[] { Roles.Player }),
                    new PlayerWelcome { Player = outcome.DisplayName, RecoveryCode = outcome.RecoveryCode });
            }

            if (!string.IsNullOrEmpty(request.DeviceId))
            {
                return ThroughNakama(request);
            }

            if (string.IsNullOrEmpty(request.Player))
            {
                throw new RpcException(Status.Unauthenticated("a player name is required"));
            }

            if (nakama != null)
            {
                throw new RpcException(Status.Unauthenticated("Nakama đang bật: hãy đăng nhập bằng tài khoản"));
            }

            return (new Identity(request.Player, new[] { Roles.Player }), new PlayerWelcome { Player = request.Player });
        }

        /// <summary>Lấy phiên Nakama (dùng cho ghép trận) gắn với tài khoản, bằng device id ổn định theo tên.</summary>
        private void LinkNakama(AuthOutcome outcome)
        {
            Task<NakamaSession> auth = Task.Run(() => nakama.AuthenticateDevice("fomoxa-acct-" + outcome.Principal));
            if (!auth.Wait(TimeSpan.FromSeconds(8)))
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "Nakama không trả lời"));
            }

            try
            {
                NakamaSession session = auth.GetAwaiter().GetResult();
                sessions.Set(outcome.Principal, session.Token);
                sessions.SetUser(outcome.Principal, session.UserId);
            }
            catch (NakamaException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }
        }

        private (Identity Identity, PlayerWelcome Response) ThroughNakama(PlayerLogin request)
        {
            if (nakama == null)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "backend chưa cấu hình Nakama"));
            }

            Console.WriteLine($"[Nakama] authenticating device {request.DeviceId}");
            Task<NakamaSession> auth = Task.Run(() => nakama.AuthenticateDevice(request.DeviceId));
            if (!auth.Wait(TimeSpan.FromSeconds(8)))
            {
                Console.WriteLine("[Nakama] auth timeout after 8 seconds");
                throw new RpcException(new Status(StatusCode.Unavailable, "Nakama không trả lời"));
            }

            NakamaSession session;
            try
            {
                session = auth.GetAwaiter().GetResult();
            }
            catch (NakamaException problem)
            {
                Console.WriteLine($"[Nakama] auth failed: {problem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }
            catch (Exception problem)
            {
                Console.WriteLine($"[Nakama] auth error: {problem.GetType().Name} {problem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, "Nakama: " + problem.Message));
            }

            Console.WriteLine($"[Nakama] authenticated {request.DeviceId} as {session.UserId}");
            sessions.Set(session.UserId, session.Token);
            sessions.SetName(session.UserId, string.IsNullOrEmpty(request.Player) ? session.UserId : request.Player);
            return (new Identity(session.UserId, new[] { Roles.Player }), new PlayerWelcome { Player = request.Player });
        }
    }

    public sealed class Lobby : ILobby
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan IdempotentWindow = TimeSpan.FromSeconds(5);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (RoomTicket Ticket, DateTime Expire)> recentTickets = new();

        private readonly DirectoryLink directory;
        private readonly NakamaLink nakama;
        private readonly PlayerSessions sessions;

        private readonly RoomStats stats;
        private readonly OnlinePlayers online;

        private readonly LobbyCore core;

        public Lobby(DirectoryLink directory, NakamaLink nakama, PlayerSessions sessions, RoomStats stats, OnlinePlayers online, LobbyCore core)
        {
            this.stats = stats;
            this.online = online;
            this.core = core;
            this.directory = directory;
            this.nakama = nakama;
            this.sessions = sessions;
        }

        public async Task<RoomTicket> JoinRoom(CallContext context, JoinRoomRequest request)
        {
            if (string.IsNullOrEmpty(request.RoomKey))
            {
                throw new RpcException(Status.InvalidArgument("JoinRoom needs a room key"));
            }

            string principal = context.Session!.Principal;
            string cacheKey = $"{principal}:{request.RoomKey}";

            // Idempotent: neu vua cap ticket cho cung user va room trong vong 5 giay thi tra ve ngay
            if (recentTickets.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.Expire)
            {
                Console.WriteLine($"[Lobby] {principal} asks for {request.RoomKey} (idempotent cache hit)");
                return cached.Ticket;
            }

            Console.WriteLine($"[Lobby] {principal} asks for {request.RoomKey}");
            try
            {
                string matchedRoom = await WaitForMatch(principal, request.RoomKey);
                RoomTicket ticket = await directory.Ticket(principal, matchedRoom, request.Version);
                recentTickets[cacheKey] = (ticket, DateTime.UtcNow + IdempotentWindow);
                core.Metrics?.Count("joins_total", "result=\"ok\"");
                Console.WriteLine($"[Lobby] {principal} -> {ticket.Address}:{ticket.Port} (room: {matchedRoom})");
                return ticket;
            }
            catch (FormatException formatProblem)
            {
                Console.Error.WriteLine($"[Lobby] FormatException: {formatProblem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, $"du lieu khong hop le: {formatProblem.Message}"));
            }
        }

        public Task<ActionResult> ChangePassword(CallContext context, ChangePasswordRequest request) =>
            core.ChangePassword(context.Session!.Principal, request);

        public ActionResult TakeRecoveryCode(CallContext context, RecoveryCodeRequest request) =>
            core.TakeRecoveryCode(context.Session!.Principal);

        public Task<PlayerProfile> GetProfile(CallContext context, ProfileRequest request) =>
            core.GetProfile(context.Session!.Principal, request);

        public Task<SocialState> Social(CallContext context, SocialRequest request) =>
            core.Social(context.Session!.Principal);

        public Task<ActionResult> FriendAction(CallContext context, FriendActionRequest request) =>
            core.FriendAction(context.Session!.Principal, request);

        public Task<ActionResult> Invite(CallContext context, InviteRequest request) =>
            core.Invite(context.Session!.Principal, request);

        public ScoreboardState Scoreboard(CallContext context, ScoreboardRequest request) =>
            core.Scoreboard(request);

        public OnlineList Presence(CallContext context, PresenceRequest request)
        {
            string principal = context.Session!.Principal;
            online.Touch(principal, sessions.NameOf(principal), request.Room);
            return online.Snapshot();
        }

        public async Task<RoomList> ListRooms(CallContext context, ListRoomsRequest request)
        {
            var list = new RoomList();
            if (nakama == null)
            {
                // Không có Nakama: ai cũng vào được room bất kỳ; liệt kê room do pod báo lên (hoặc pool mặc định).
                var reported = stats.Current();
                if (reported.Count == 0)
                {
                    foreach (string room in new[] { "arena:1", "arena:2", "arena:3", "arena:4" })
                    {
                        list.Rooms.Add(new RoomInfo { RoomKey = room, Players = 0, Capacity = 2, State = "open" });
                    }
                }

                foreach (var entry in reported)
                {
                    list.Rooms.Add(Describe(entry.Room, entry.Players, entry.Capacity == 0 ? 2u : entry.Capacity));
                }

                return list;
            }

            try
            {
                foreach (NakamaRoom room in await nakama.ListRooms())
                {
                    uint players = (uint)Math.Max(0, room.Players);
                    uint capacity = (uint)Math.Max(0, room.Capacity);

                    // Số người đang kết nối thật tại pod (nếu có) thắng số "đã ghép" của Nakama khi lớn hơn.
                    if (room.Room != "auto" && stats.TryGet(room.Room, out uint actual, out _))
                    {
                        players = Math.Max(players, actual);
                    }

                    list.Rooms.Add(Describe(room.Room, players, capacity));
                }

                // Room do pod giữ nhưng ngoài pool cố định của Nakama (khi có nhiều pod hơn) vẫn hiện trong danh sách.
                foreach (var extra in stats.Current())
                {
                    if (!list.Rooms.Any(r => r.RoomKey == extra.Room))
                    {
                        list.Rooms.Add(Describe(extra.Room, extra.Players, 2));
                    }
                }
            }
            catch (NakamaException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }

            return list;
        }

        private static RoomInfo Describe(string room, uint players, uint capacity) => new RoomInfo
        {
            RoomKey = room,
            Players = players,
            Capacity = capacity,
            State = players >= capacity ? "full" : players > 0 ? "waiting" : "open",
        };

        private async Task<string> WaitForMatch(string principal, string roomKey)
        {
            if (nakama == null)
            {
                return roomKey;
            }

            if (!sessions.TryGet(principal, out string token))
            {
                throw new RpcException(Status.Unauthenticated("phiên Nakama không còn, đăng nhập lại"));
            }

            MatchState state;
            try
            {
                // "auto": chỉ chọn trong các room thực sự có pod (do pod báo lên), để pod mới / ít pod vẫn khớp.
                var pool = roomKey == "auto"
                    ? stats.Current().Where(r => r.Players < Math.Max(r.Capacity, 1u)).Select(r => r.Room).ToList()
                    : null;
                state = await nakama.FindMatch(token, roomKey, pool);
                DateTime deadline = DateTime.UtcNow + MatchTimeout;
                while (state.Status != "matched" && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(300);
                    state = await nakama.PollMatch(token, roomKey);
                }
            }
            catch (NakamaException problem)
            {
                Console.WriteLine($"[Lobby] matchmaking error: {problem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }

            if (state.Status != "matched" || string.IsNullOrEmpty(state.Ticket))
            {
                throw new RpcException(Status.FailedPrecondition(
                    $"không ghép được trận cho {roomKey} trong {MatchTimeout.TotalSeconds:N0} giây"));
            }

            string assignedRoom = !string.IsNullOrEmpty(state.Room) ? state.Room : roomKey;

            NakamaTicket granted;
            try
            {
                granted = await nakama.VerifyTicket(state.Ticket, assignedRoom, sessions.UserIdOf(principal));
            }
            catch (NakamaException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }

            if (!granted.Ok)
            {
                Console.WriteLine($"[Lobby] ticket rejected: {granted.Message}");
                throw new RpcException(Status.FailedPrecondition(granted.Message));
            }

            return assignedRoom;
        }
    }
}
