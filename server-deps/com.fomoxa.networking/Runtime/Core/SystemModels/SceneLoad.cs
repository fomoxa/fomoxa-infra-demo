using System.Collections.Generic;
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class SceneLoad
    {
        [Network("bool")]
        [Codec("net")]
        public bool Full { get; set; }

        [Network("Array<SceneRef>")]
        [Codec("net")]
        public List<SceneRef> Scenes { get; set; } = new List<SceneRef>();
    }

    [Network]
    [Codec("net")]
    public class SceneRef
    {
        [Network("u32")]
        [Codec("net")]
        public uint Scene { get; set; }

        [Network("u32")]
        [Codec("net")]
        public uint SceneId { get; set; }

        [Network("u8")]
        [Codec("net")]
        public byte Physics { get; set; }
    }
}
