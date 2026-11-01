using System;
using Fomoxa;

namespace MyGame.Backend.Models
{
    [Network]
    [Codec("rpc")]
    public class PlayerLogin
    {
        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string DeviceId { get; set; } = "";

        [Network("bytes")]
        [Codec("rpc")]
        public byte[] Secret { get; set; } = Array.Empty<byte>();

        /// <summary>"login" | "register" | "recover"; rỗng = kiểu cũ (DeviceId / chỉ có tên).</summary>
        [Network("string")]
        [Codec("rpc")]
        public string Mode { get; set; } = "";

        /// <summary>Mã khôi phục (chỉ dùng khi Mode = "recover"; khi đó Secret là mật khẩu MỚI).</summary>
        [Network("bytes")]
        [Codec("rpc")]
        public byte[] Recovery { get; set; } = Array.Empty<byte>();
    }

    [Network]
    [Codec("rpc")]
    public class PlayerWelcome
    {
        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";

        /// <summary>Mã khôi phục mới - chỉ có khi vừa đăng ký hoặc vừa khôi phục; hiện đúng một lần.</summary>
        [Network("string")]
        [Codec("rpc")]
        public string RecoveryCode { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class JoinRoomRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string RoomKey { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Version { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class RoomTicket
    {
        [Network("string")]
        [Codec("rpc")]
        public string Address { get; set; } = "";

        [Network("u16")]
        [Codec("rpc")]
        public ushort Port { get; set; }

        [Network("bytes")]
        [Codec("rpc")]
        public byte[] Connection { get; set; } = Array.Empty<byte>();

        [Network("string")]
        [Codec("rpc")]
        public string WebSocketUrl { get; set; } = "";
    }

    [Network]
    [Codec("rpc")]
    public class NoRoomForVersion
    {
        [Network("string")]
        [Codec("rpc")]
        public string Version { get; set; } = "";
    }
}
