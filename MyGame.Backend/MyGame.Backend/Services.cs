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

        [Unary(Requires = Roles.Player)]
        Task<RoomList> ListRooms(CallContext context, ListRoomsRequest request);

        [Unary(Requires = Roles.Player)]
        OnlineList Presence(CallContext context, PresenceRequest request);

        [Unary(Requires = Roles.Player)]
        Task<ActionResult> ChangePassword(CallContext context, ChangePasswordRequest request);

        [Unary(Requires = Roles.Player)]
        ActionResult TakeRecoveryCode(CallContext context, RecoveryCodeRequest request);

        [Unary(Requires = Roles.Player)]
        Task<PlayerProfile> GetProfile(CallContext context, ProfileRequest request);

        [Unary(Requires = Roles.Player)]
        Task<SocialState> Social(CallContext context, SocialRequest request);

        [Unary(Requires = Roles.Player)]
        Task<ActionResult> FriendAction(CallContext context, FriendActionRequest request);

        [Unary(Requires = Roles.Player)]
        Task<ActionResult> Invite(CallContext context, InviteRequest request);

        [Unary(Requires = Roles.Player)]
        ScoreboardState Scoreboard(CallContext context, ScoreboardRequest request);
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

        [Unary(Requires = Roles.Pod)]
        void ReportRoom(CallContext context, RoomReport request);

        [Unary(Requires = Roles.Pod)]
        void ReportScores(CallContext context, ScoreReport request);
    }
}
