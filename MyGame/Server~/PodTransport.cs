using System;
using Fomoxa.Net.Transports;
using Fomoxa.Networking;
using Fomoxa.Networking.Transports;

namespace MyGame.Server
{
    /// <summary>
    /// Listener UDP mã hoá của pod, nhưng ticket key chỉ có sau khi directory register.
    /// Vì <see cref="EncryptedUdpTransportFactory"/> là bất biến, factory này giữ
    /// token hiện tại và dựng factory thật mỗi lần <see cref="CreateListener"/> -
    /// đúng cách <c>EncryptedUdpNetworkTransport</c> làm ở phía Unity.
    /// </summary>
    internal sealed class PodTransport : ITransportFactory
    {
        private readonly FomoxaRegistry registry;
        private readonly EncryptedUdpTransportFactory client;
        private readonly UdpTransportFactory plainUdp = new UdpTransportFactory();

        private ConnectTokens tokens;
        private string target = "";

        public PodTransport(FomoxaRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            client = new EncryptedUdpTransportFactory(registry);
        }

        public bool BehindGateway { get; set; } = true;

        public int FrameBudget => client.FrameBudget;

        public void Use(ConnectTokens connectTokens, string name)
        {
            tokens = connectTokens ?? throw new ArgumentNullException(nameof(connectTokens));
            target = name ?? throw new ArgumentNullException(nameof(name));
        }

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            if (tokens == null)
            {
                throw new InvalidOperationException("pod chua co ticket key tu directory");
            }

            var server = new EncryptedUdpTransportFactory(registry, tokens, target, BehindGateway);
            return server.CreateListener(port, out boundPort);
        }

        public ITransportConnector CreateConnector(string address, ushort port) =>
            plainUdp.CreateConnector(address, port);
    }
}
