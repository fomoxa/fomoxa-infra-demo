using System.Collections.Generic;
using Fomoxa;

namespace MyGame.Backend.Models
{
    [Network]
    [Codec("rpc")]
    public class ActionResult
    {
        [Network("bool")]
        [Codec("rpc")]
        public bool Ok { get; set; }

        [Network("string")]
        [Codec("rpc")]
        public string Message { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class ChangePasswordRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string OldPassword { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string NewPassword { get; set; } = "";
    }

    /// <summary>Rỗng = hồ sơ của chính mình.</summary>
    /// <summary>Lấy mã khôi phục vừa được cấp (chỉ lấy được một lần, trong 10 phút sau khi đăng ký / khôi phục).</summary>
    [Network]
    [Codec("rpc")]
    public class RecoveryCodeRequest
    {
    }

    [Network]
    [Codec("rpc")]
    public class ProfileRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class PlayerProfile
    {
        [Network("string")]
        [Codec("rpc")]
        public string Name { get; set; } = "";

        [Network("u32")]
        [Codec("rpc")]
        public uint BestSteps { get; set; }

        [Network("u32")]
        [Codec("rpc")]
        public uint Matches { get; set; }

        [Network("i64")]
        [Codec("rpc")]
        public long CreatedUnix { get; set; }
    }

    [Network]
    [Codec("rpc")]
    public class FriendInfo
    {
        [Network("string")]
        [Codec("rpc")]
        public string Name { get; set; } = "";

        [Network("bool")]
        [Codec("rpc")]
        public bool Online { get; set; }

        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";

        [Network("u32")]
        [Codec("rpc")]
        public uint BestSteps { get; set; }
    }

    [Network]
    [Codec("rpc")]
    public class InviteInfo
    {
        [Network("string")]
        [Codec("rpc")]
        public string From { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class SocialRequest
    {
    }

    [Network]
    [Codec("rpc")]
    public class SocialState
    {
        [Network("Array<FriendInfo>")]
        [Codec("rpc")]
        public List<FriendInfo> Friends { get; set; } = new List<FriendInfo>();

        [Network("Array<string>")]
        [Codec("rpc")]
        public List<string> Incoming { get; set; } = new List<string>();

        [Network("Array<string>")]
        [Codec("rpc")]
        public List<string> Outgoing { get; set; } = new List<string>();

        [Network("Array<InviteInfo>")]
        [Codec("rpc")]
        public List<InviteInfo> Invites { get; set; } = new List<InviteInfo>();
    }

    /// <summary>Action: request | accept | decline | remove | dismiss-invite.</summary>
    [Network]
    [Codec("rpc")]
    public class FriendActionRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Action { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Target { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class InviteRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string To { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";
    }

    /// <summary>Một dòng bảng điểm: Steps = số bậc cao nhất trong trận này, Best = kỷ lục cá nhân.</summary>
    [Network]
    [Codec("rpc")]
    public class ScoreEntry
    {
        [Network("string")]
        [Codec("rpc")]
        public string Name { get; set; } = "";

        [Network("u32")]
        [Codec("rpc")]
        public uint Steps { get; set; }

        [Network("u32")]
        [Codec("rpc")]
        public uint Best { get; set; }
    }

    [Network]
    [Codec("rpc")]
    public class ScoreboardRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class ScoreboardState
    {
        [Network("Array<ScoreEntry>")]
        [Codec("rpc")]
        public List<ScoreEntry> Entries { get; set; } = new List<ScoreEntry>();
    }

    [Network]
    [Codec("rpc")]
    public class PodScore
    {
        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";

        [Network("u32")]
        [Codec("rpc")]
        public uint Steps { get; set; }
    }

    /// <summary>Pod báo bảng điểm của một phòng lên backend.</summary>
    [Network]
    [Codec("rpc")]
    public class ScoreReport
    {
        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";

        [Network("Array<PodScore>")]
        [Codec("rpc")]
        public List<PodScore> Entries { get; set; } = new List<PodScore>();
    }
}
