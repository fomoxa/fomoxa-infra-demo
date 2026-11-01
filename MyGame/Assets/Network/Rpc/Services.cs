using System.Threading.Tasks;
using MyGame.Backend.Models;
using Fomoxa.Rpc;

namespace MyGame.Backend
{
    public static class Roles
    {
        public const string Player = "player";
        public const string Pod = "pod";
    }

    [Service("Players")]
    public partial interface IPlayers
    {
        [Unary]
        PlayerWelcome Login(PlayerLogin request);
    }

    [Service("Lobby")]
    public partial interface ILobby
    {
        [Unary(Requires = Roles.Player, Details = new[] { typeof(NoRoomForVersion) })]
        Task<RoomTicket> JoinRoom(CallContext context, JoinRoomRequest request);
    }

    [Service("Pods")]
    public partial interface IPods
    {
        [Unary]
        void Login(PodLogin request);
    }

    [Service("PodGate")]
    public partial interface IPodGate
    {
        [Unary(Requires = Roles.Pod, Details = new[] { typeof(NoRoomForVersion) })]
        Task<RoomTicket> TransferPlayer(CallContext context, TransferPlayerRequest request);
    }
}
