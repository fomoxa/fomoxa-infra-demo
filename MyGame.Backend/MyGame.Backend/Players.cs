using System.Threading.Tasks;
using MyGame.Backend.Models;
using Fomoxa.Rpc;

namespace MyGame.Backend
{
    public sealed class Players : IAuthenticator<PlayerLogin, PlayerWelcome>
    {
        public UnaryMethod<PlayerLogin, PlayerWelcome> Method => PlayersMethods.Login;

        public (Identity Identity, PlayerWelcome Response) Authenticate(CallContext context, PlayerLogin request)
        {
            if (string.IsNullOrEmpty(request.Player))
            {
                throw new RpcException(Status.Unauthenticated("a player name is required"));
            }

            return (new Identity(request.Player, new[] { Roles.Player }), new PlayerWelcome { Player = request.Player });
        }
    }

    public sealed class Lobby : ILobby
    {
        private readonly DirectoryLink directory;

        public Lobby(DirectoryLink directory)
        {
            this.directory = directory;
        }

        public Task<RoomTicket> JoinRoom(CallContext context, JoinRoomRequest request)
        {
            if (string.IsNullOrEmpty(request.RoomKey))
            {
                throw new RpcException(Status.InvalidArgument("JoinRoom needs a room key"));
            }

            return directory.Ticket(context.Session!.Principal, request.RoomKey, request.Version);
        }
    }
}
