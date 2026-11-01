using System;
using Fomoxa;

namespace MyGame.Backend.Models
{
    [Network]
    [Codec("rpc")]
    public class PodLogin
    {
        [Network("bytes")]
        [Codec("rpc")]
        public byte[] Key { get; set; } = Array.Empty<byte>();
    }

    [Network]
    [Codec("rpc")]
    public class TransferPlayerRequest
    {
        [Network("string")]
        [Codec("rpc")]
        public string Player { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string RoomKey { get; set; } = "";

        [Network("string")]
        [Codec("rpc")]
        public string Version { get; set; } = "";
    }
}
