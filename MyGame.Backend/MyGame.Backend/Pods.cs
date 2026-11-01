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

    public sealed class PodGate : IPodGate
    {
        private readonly DirectoryLink directory;

        public PodGate(DirectoryLink directory)
        {
            this.directory = directory;
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
