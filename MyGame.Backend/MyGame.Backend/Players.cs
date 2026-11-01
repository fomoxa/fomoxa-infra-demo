using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        public Players(NakamaLink nakama, PlayerSessions sessions)
        {
            this.nakama = nakama;
            this.sessions = sessions;
        }

        public UnaryMethod<PlayerLogin, PlayerWelcome> Method => PlayersMethods.Login;

        private static readonly System.Text.RegularExpressions.Regex ValidName =
            new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_-]{3,24}$");

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
            // Có mật khẩu (Secret) -> đăng nhập tài khoản qua Nakama.
            if (request.Secret != null && request.Secret.Length > 0)
            {
                return WithPassword(request);
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
                throw new RpcException(Status.Unauthenticated("Nakama đang bật: client phải gửi DeviceId"));
            }

            return (new Identity(request.Player, new[] { Roles.Player }), new PlayerWelcome { Player = request.Player });
        }

        private (Identity Identity, PlayerWelcome Response) WithPassword(PlayerLogin request)
        {
            if (nakama == null)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "backend chưa cấu hình Nakama"));
            }

            if (!ValidName.IsMatch(request.Player ?? ""))
            {
                throw new RpcException(Status.InvalidArgument("tên chỉ gồm chữ, số, '_' hoặc '-', dài 3-24 ký tự"));
            }

            string password = System.Text.Encoding.UTF8.GetString(request.Secret);
            if (password.Length < 8)
            {
                throw new RpcException(Status.InvalidArgument("mật khẩu phải có ít nhất 8 ký tự"));
            }

            Console.WriteLine($"[Nakama] password login for {request.Player}");
            Task<NakamaSession> auth = Task.Run(() => nakama.AuthenticatePassword(request.Player!, password));
            if (!auth.Wait(TimeSpan.FromSeconds(8)))
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "Nakama không trả lời"));
            }

            NakamaSession session;
            try
            {
                session = auth.GetAwaiter().GetResult();
            }
            catch (NakamaException problem) when (problem.Message.Contains("401") || problem.Message.Contains("403"))
            {
                // Sai mật khẩu hoặc tên đã có chủ. Không lộ chi tiết từ Nakama.
                Console.WriteLine($"[Nakama] password rejected for {request.Player}");
                throw new RpcException(Status.Unauthenticated("sai tên hoặc mật khẩu"));
            }
            catch (NakamaException problem)
            {
                Console.WriteLine($"[Nakama] password login failed: {problem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }

            Console.WriteLine($"[Nakama] authenticated {request.Player} as {session.UserId}");
            sessions.Set(session.UserId, session.Token);
            return (new Identity(session.UserId, new[] { Roles.Player }), new PlayerWelcome { Player = request.Player! });
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
                Console.WriteLine("[Nakama] auth timeout sau 8 giây");
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
                Console.WriteLine($"[Nakama] auth lỗi: {problem.GetType().Name} {problem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, "Nakama: " + problem.Message));
            }

            Console.WriteLine($"[Nakama] authenticated {request.DeviceId} as {session.UserId}");
            sessions.Set(session.UserId, session.Token);
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

        public Lobby(DirectoryLink directory, NakamaLink nakama, PlayerSessions sessions, RoomStats stats)
        {
            this.stats = stats;
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
                Console.WriteLine($"[Lobby] {principal} -> {ticket.Address}:{ticket.Port} (room: {matchedRoom})");
                return ticket;
            }
            catch (FormatException formatProblem)
            {
                Console.Error.WriteLine($"[Lobby] FormatException: {formatProblem.Message}");
                throw new RpcException(new Status(StatusCode.Unavailable, $"du lieu khong hop le: {formatProblem.Message}"));
            }
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
                state = await nakama.FindMatch(token, roomKey);
                DateTime deadline = DateTime.UtcNow + MatchTimeout;
                while (state.Status != "matched" && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(300);
                    state = await nakama.PollMatch(token, roomKey);
                }
            }
            catch (NakamaException problem)
            {
                Console.WriteLine($"[Lobby] ghép trận lỗi: {problem.Message}");
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
                granted = await nakama.VerifyTicket(state.Ticket, assignedRoom, principal);
            }
            catch (NakamaException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, problem.Message));
            }

            if (!granted.Ok)
            {
                Console.WriteLine($"[Lobby] ticket bị từ chối: {granted.Message}");
                throw new RpcException(Status.FailedPrecondition(granted.Message));
            }

            return assignedRoom;
        }
    }
}
