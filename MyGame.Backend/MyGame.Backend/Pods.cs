using System.Security.Cryptography;
using System.Threading.Tasks;
using MyGame.Backend.Models;
using Fomoxa.Rpc;

namespace MyGame.Backend
{
    public sealed class PodKeys : IAuthenticator<PodLogin, FRpcVoid>
    {
        private readonly byte[] key;

        public PodKeys(byte[] key)
        {
            this.key = key;
        }

        public UnaryMethod<PodLogin, FRpcVoid> Method => PodsMethods.Login;

        public (Identity Identity, FRpcVoid Response) Authenticate(CallContext context, PodLogin request)
        {
            if (!CryptographicOperations.FixedTimeEquals(request.Key, key))
            {
                throw new RpcException(Status.Unauthenticated("the pod key does not match"));
            }

            return (new Identity(Roles.Pod, new[] { Roles.Pod }), new FRpcVoid());
        }
    }

    /// <summary>Số người thật trong từng room do các pod báo lên; báo cáo cũ hơn <see cref="MaxAge"/> bị bỏ qua.</summary>
    public sealed class RoomStats
    {
        public static readonly System.TimeSpan MaxAge = System.TimeSpan.FromSeconds(10);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (uint Players, uint Capacity, System.DateTime Seen)> rooms =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (uint, uint, System.DateTime)>();

        public void Report(string room, uint players, uint capacity) => rooms[room] = (players, capacity, System.DateTime.UtcNow);

        public bool TryGet(string room, out uint players, out uint capacity)
        {
            if (rooms.TryGetValue(room, out var entry) && System.DateTime.UtcNow - entry.Seen <= MaxAge)
            {
                players = entry.Players;
                capacity = entry.Capacity;
                return true;
            }

            players = 0;
            capacity = 0;
            return false;
        }

        public System.Collections.Generic.IReadOnlyList<(string Room, uint Players, uint Capacity)> Current()
        {
            var list = new System.Collections.Generic.List<(string, uint, uint)>();
            foreach (var pair in rooms)
            {
                if (System.DateTime.UtcNow - pair.Value.Seen <= MaxAge)
                {
                    list.Add((pair.Key, pair.Value.Players, pair.Value.Capacity));
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list;
        }
    }

    public sealed class PodGate : IPodGate
    {
        private readonly DirectoryLink directory;
        private readonly RoomStats stats;

        public PodGate(DirectoryLink directory, RoomStats stats)
        {
            this.directory = directory;
            this.stats = stats;
        }

        public void ReportRoom(CallContext context, RoomReport request)
        {
            if (string.IsNullOrEmpty(request.RoomKey))
            {
                throw new RpcException(Status.InvalidArgument("ReportRoom needs a room key"));
            }

            stats.Report(request.RoomKey, request.Players, request.Capacity);
        }

        public Task<RoomTicket> TransferPlayer(CallContext context, TransferPlayerRequest request)
        {
            if (string.IsNullOrEmpty(request.Player) || string.IsNullOrEmpty(request.RoomKey))
            {
                throw new RpcException(Status.InvalidArgument("TransferPlayer needs a player and a room key"));
            }

            return directory.Ticket(request.Player, request.RoomKey, request.Version);
        }
    }
}
