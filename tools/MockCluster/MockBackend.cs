using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using MyGame.Backend;
using MyGame.Backend.Accounts;
using MyGame.Backend.Models;

namespace MockCluster
{
    /// <summary>
    /// Đăng nhập giả nhưng dùng đúng AccountService như backend thật: đăng ký (nhận mã khôi phục), đăng nhập, khôi phục,
    /// đổi mật khẩu. Không mật khẩu (chỉ tên) vẫn vào được như khách. Dữ liệu trong bộ nhớ, hoặc file nếu có --data.
    /// </summary>
    internal sealed class MockPlayers : IAuthenticator<PlayerLogin, PlayerWelcome>
    {
        private readonly AccountAuth accounts;

        public MockPlayers(AccountAuth accounts)
        {
            this.accounts = accounts;
        }

        public UnaryMethod<PlayerLogin, PlayerWelcome> Method => PlayersMethods.Login;

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
            if (AccountAuth.UsesAccounts(request))
            {
                AuthOutcome outcome = accounts.Authenticate(request);
                Console.WriteLine($"[Backend] {outcome.DisplayName} login ({(request.Mode.Length == 0 ? "login" : request.Mode)})");
                return (new Identity(outcome.Principal, new[] { Roles.Player }),
                    new PlayerWelcome { Player = outcome.DisplayName, RecoveryCode = outcome.RecoveryCode });
            }

            string name = (request.Player ?? "").Trim();
            if (!AccountService.IsValidName(name))
            {
                throw new RpcException(Status.InvalidArgument("ten chi gom chu, so, '_' hoac '-', dai 3-24 ky tu"));
            }

            Console.WriteLine($"[Backend] {name} login (guest)");
            return (new Identity(name, new[] { Roles.Player }), new PlayerWelcome { Player = name });
        }
    }

    /// <summary>Cổng pod giả (9602): nhận bảng điểm từ các pod thật.</summary>
    internal sealed class MockPodGate : IPodGate
    {
        private readonly LobbyCore core;

        public MockPodGate(LobbyCore core)
        {
            this.core = core;
        }

        public Task<RoomTicket> TransferPlayer(CallContext context, TransferPlayerRequest request) =>
            throw new RpcException(Status.FailedPrecondition("mock khong ho tro TransferPlayer"));

        public void ReportRoom(CallContext context, RoomReport request)
        {
        }

        public void ReportScores(CallContext context, ScoreReport request) => core.ReportScores(request);
    }

    internal sealed class MockPodKeys : IAuthenticator<PodLogin, FRpcVoid>
    {
        public UnaryMethod<PodLogin, FRpcVoid> Method => PodsMethods.Login;

        public (Identity Identity, FRpcVoid Response) Authenticate(CallContext context, PodLogin request) =>
            (new Identity(Roles.Pod, new[] { Roles.Pod }), new FRpcVoid());
    }

    /// <summary>Lobby giả: không Nakama, ghép trận tức thì; số người trong phòng lấy từ báo cáo của pod thật.</summary>
    internal sealed class MockLobby : ILobby
    {
        private readonly MockDirectory directory;
        private readonly TimeSpan matchDelay;
        private readonly OnlinePlayers online;
        private readonly LobbyCore core;
        private int autoCursor;

        public MockLobby(MockDirectory directory, TimeSpan matchDelay, OnlinePlayers online, LobbyCore core)
        {
            this.directory = directory;
            this.matchDelay = matchDelay;
            this.online = online;
            this.core = core;
        }

        public Task<ActionResult> ChangePassword(CallContext context, ChangePasswordRequest request) => core.ChangePassword(context.Session!.Principal, request);

        public ActionResult TakeRecoveryCode(CallContext context, RecoveryCodeRequest request) => core.TakeRecoveryCode(context.Session!.Principal);

        public Task<PlayerProfile> GetProfile(CallContext context, ProfileRequest request) => core.GetProfile(context.Session!.Principal, request);

        public Task<SocialState> Social(CallContext context, SocialRequest request) => core.Social(context.Session!.Principal);

        public Task<ActionResult> FriendAction(CallContext context, FriendActionRequest request) => core.FriendAction(context.Session!.Principal, request);

        public Task<ActionResult> Invite(CallContext context, InviteRequest request) => core.Invite(context.Session!.Principal, request);

        public ScoreboardState Scoreboard(CallContext context, ScoreboardRequest request) => core.Scoreboard(request);

        public OnlineList Presence(CallContext context, PresenceRequest request)
        {
            string principal = context.Session!.Principal;
            online.Touch(principal, principal, request.Room);
            return online.Snapshot();
        }

        public Task<RoomList> ListRooms(CallContext context, ListRoomsRequest request)
        {
            var list = new RoomList();
            var rooms = directory.Rooms();
            bool anyOpen = rooms.Any(r => r.Players < r.Capacity);
            list.Rooms.Add(new RoomInfo { RoomKey = "auto", Players = 0, Capacity = 2, State = anyOpen ? "open" : "full" });
            foreach (RoomView room in rooms)
            {
                list.Rooms.Add(new RoomInfo
                {
                    RoomKey = room.RoomKey,
                    Players = room.Players,
                    Capacity = room.Capacity,
                    State = room.Players >= room.Capacity ? "full" : room.Players > 0 ? "waiting" : "open",
                });
            }

            return Task.FromResult(list);
        }

        public async Task<RoomTicket> JoinRoom(CallContext context, JoinRoomRequest request)
        {
            if (string.IsNullOrEmpty(request.RoomKey))
            {
                throw new RpcException(Status.InvalidArgument("JoinRoom needs a room key"));
            }

            string player = context.Session!.Principal;
            string room = request.RoomKey;
            if (room == "auto")
            {
                var open = directory.Rooms().Where(r => r.Players < r.Capacity).ToList();
                if (open.Count == 0)
                {
                    throw new RpcException(Status.FailedPrecondition("khong con phong trong"));
                }

                room = open[(autoCursor++ & int.MaxValue) % open.Count].RoomKey;
            }

            if (matchDelay > TimeSpan.Zero)
            {
                await Task.Delay(matchDelay);
            }

            AllocateResponse allocation;
            try
            {
                allocation = directory.Issue(room, request.Version, Encoding.UTF8.GetBytes(player), 1);
            }
            catch (RpcException problem) when (problem.Status.Details is NoPodAvailable missing)
            {
                throw new RpcException(Status.FailedPrecondition(problem.Status.Message, new NoRoomForVersion { Version = missing.Version }));
            }

            online.Touch(player, player, room);
            Console.WriteLine($"[Backend] {player} -> {room} ({allocation.Address}:{allocation.Port})");
            return new RoomTicket
            {
                Address = allocation.Address,
                Port = allocation.Port,
                Connection = allocation.Connections.Count > 0 ? allocation.Connections[0] : Array.Empty<byte>(),
                WebSocketUrl = "",
            };
        }
    }
}
