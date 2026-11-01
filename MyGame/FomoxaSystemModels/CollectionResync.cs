// Copied by com.fomoxa.networking from its system models. Edit the package, not this file.
using Fomoxa;

namespace Fomoxa.Networking.Messaging
{
    [Network]
    [Codec("net")]
    [NetworkChannel("net", Channel.ReliableOrdered)]
    public class CollectionResync
    {
        [Network("u8")]
        [Codec("net")]
        public byte Collection { get; set; }
    }
}
