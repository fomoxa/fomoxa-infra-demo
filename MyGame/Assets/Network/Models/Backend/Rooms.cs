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
}
