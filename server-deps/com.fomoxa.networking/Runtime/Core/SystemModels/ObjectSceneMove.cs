using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class ObjectSceneMove
    {
        [Network("u32")]
        [Codec("net")]
        public uint ObjectId { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Scene { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint Tick { get; set; }
    }
}
