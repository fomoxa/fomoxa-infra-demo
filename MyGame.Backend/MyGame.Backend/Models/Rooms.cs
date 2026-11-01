using System.Collections.Generic;
using Fomoxa;

namespace MyGame.Backend.Models
{
    [Network]
    [Codec("rpc")]
    public class ListRoomsRequest
    {
    }

    [Network]
    [Codec("rpc")]
    public class RoomInfo
    {
        [Network("string")]
        [Codec("rpc")]
        public string RoomKey { get; set; } = "";

        /// <summary>Số người đã vào hàng chờ hoặc đã được ghép vào phòng này.</summary>
        [Network("u32")]
        [Codec("rpc")]
        public uint Players { get; set; }

        [Network("u32")]
        [Codec("rpc")]
        public uint Capacity { get; set; }

        /// <summary>"open" (trống), "waiting" (có người chờ ghép), "full" (đã ghép đủ cặp).</summary>
        [Network("string")]
        [Codec("rpc")]
        public string State { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class RoomList
    {
        [Network("Array<RoomInfo>")]
        [Codec("rpc")]
        public List<RoomInfo> Rooms { get; set; } = new List<RoomInfo>();
    }

    /// <summary>Pod báo lên backend: số người đang kết nối thật và sức chứa của một room nó giữ.</summary>
    [Network]
    [Codec("rpc")]
    public class RoomReport
    {
        [Network("string")]
        [Codec("rpc")]
        public string RoomKey { get; set; } = "";

        [Network("u32")]
        [Codec("rpc")]
        public uint Players { get; set; }

        [Network("u32")]
        [Codec("rpc")]
        public uint Capacity { get; set; }
    }

    /// <summary>Client báo "tôi đang online" (kèm phòng đang ở, rỗng nếu đang ở sảnh) và xin danh sách người online.</summary>
    [Network]
    [Codec("rpc")]
    public class PresenceRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class OnlinePlayer
    {
        [Network("string")]
        [Codec("rpc")]
        public string Name { get; set; } = "";

        /// <summary>Phòng đang chơi; rỗng = đang ở sảnh.</summary>
        [Network("string")]
        [Codec("rpc")]
        public string Room { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class OnlineList
    {
        [Network("Array<OnlinePlayer>")]
        [Codec("rpc")]
        public List<OnlinePlayer> Players { get; set; } = new List<OnlinePlayer>();
    }
}
