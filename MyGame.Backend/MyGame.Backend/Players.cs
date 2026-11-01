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

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
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

        public Lobby(DirectoryLink directory, NakamaLink nakama, PlayerSessions sessions)
        {
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
