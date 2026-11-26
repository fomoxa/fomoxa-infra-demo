// Copied by com.fomoxa.networking from its system models. Edit the package, not this file.
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class TransformAuthority
    {
        [Network("u8")]
        [Codec("net")]
        public byte Generation { get; set; }

        [Network("bool")]
        [Codec("net")]
        public bool Enabled { get; set; }
    }
}
