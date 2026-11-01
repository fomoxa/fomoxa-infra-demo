using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using MyGame.Backend;
using MyGame.Backend.Models;

namespace MockCluster
{
    /// <summary>
    /// Đăng nhập giả: tên hợp lệ là vào được. Nếu client gửi mật khẩu (Secret) thì lần đầu dùng tên sẽ tạo tài khoản,
    /// các lần sau phải đúng mật khẩu (giữ trong bộ nhớ, mất khi tắt mock). Không mật khẩu (DeviceId) vẫn vào được.
    /// </summary>
    internal sealed class MockPlayers : IAuthenticator<PlayerLogin, PlayerWelcome>
    {
        private static readonly Regex ValidName = new Regex("^[A-Za-z0-9_-]{3,24}$");
        private readonly ConcurrentDictionary<string, string> accounts = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public UnaryMethod<PlayerLogin, PlayerWelcome> Method => PlayersMethods.Login;

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
            string name = (request.Player ?? "").Trim();
            if (!ValidName.IsMatch(name))
            {
                throw new RpcException(Status.InvalidArgument("ten chi gom chu, so, '_' hoac '-', dai 3-24 ky tu"));
            }

            if (request.Secret != null && request.Secret.Length > 0)
            {
                string password = Encoding.UTF8.GetString(request.Secret);
                if (password.Length < 8)
                {
                    throw new RpcException(Status.InvalidArgument("mat khau phai co it nhat 8 ky tu"));
                }

                string stored = accounts.GetOrAdd(name, password);
                if (stored != password)
                {
                    Console.WriteLine($"[Backend] {name}: sai mat khau");
                    throw new RpcException(Status.Unauthenticated("sai tên hoặc mật khẩu"));
                }

                Console.WriteLine($"[Backend] {name} dang nhap (mat khau)");
            }
            else
            {
                Console.WriteLine($"[Backend] {name} dang nhap (khong mat khau)");
            }

            return (new Identity(name, new[] { Roles.Player }), new PlayerWelcome { Player = name });
        }
    }

    /// <summary>Lobby giả: không Nakama, ghép trận tức thì; số người trong phòng lấy từ báo cáo của pod thật.</summary>
    internal sealed class MockLobby : ILobby
    {
        private readonly MockDirectory directory;
        private readonly TimeSpan matchDelay;
        private int autoCursor;

        public MockLobby(MockDirectory directory, TimeSpan matchDelay)
        {
            this.directory = directory;
            this.matchDelay = matchDelay;
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
