using System;
using System.Collections.Generic;
using System.Net;
using Fomoxa.Net.WebSockets;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Fomoxa.Rpc.WebSockets;

namespace MyGame.Backend
{
    public sealed class Backend : IDisposable
    {
        private readonly List<RpcServer> servers = new List<RpcServer>();
        private readonly DirectoryLink directory;

        private Backend(DirectoryLink directory)
        {
            this.directory = directory;
        }

        public IPEndPoint? PlayersTcp { get; private set; }

        public IPEndPoint? PlayersWebSocket { get; private set; }

        public IPEndPoint? PodsTcp { get; private set; }

        public static Backend Start(BackendSettings settings)
        {
            var backend = new Backend(new DirectoryLink(settings.DirectoryAddress, settings.DirectoryKey));
            try
            {
                backend.Listen(settings);
                return backend;
            }
            catch
            {
                backend.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (RpcServer server in servers)
            {
                server.Dispose();
            }

            servers.Clear();
            directory.Dispose();
        }

        private void Listen(BackendSettings settings)
        {
            var schema = Generated.NetSchema.Build();
            Registry players = new LobbyServer(new Lobby(directory)).Registry().Authenticate(new Players()).Build();
            if (!string.IsNullOrEmpty(settings.PlayersTcp))
            {
                RpcServer server = Started(RpcServer.Bind(settings.PlayersTcp, schema, players));
                PlayersTcp = server.LocalEndPoint;
            }

            if (!string.IsNullOrEmpty(settings.PlayersWebSocket))
            {
                WebSocketReadiness.Register();
                var listener = new WebSocketListener(EndPoint(settings.PlayersWebSocket), new WebSocketSettings());
                PlayersWebSocket = listener.LocalEndPoint;
                Started(RpcServer.Listen(listener, schema, players));
            }

            if (!string.IsNullOrEmpty(settings.PodsTcp))
            {
                Registry pods = new PodGateServer(new PodGate(directory)).Registry().Authenticate(new PodKeys(settings.PodKey)).Build();
                RpcServer server = Started(RpcServer.Bind(settings.PodsTcp, schema, pods));
                PodsTcp = server.LocalEndPoint;
            }
        }

        private RpcServer Started(RpcServer server)
        {
            servers.Add(server);
            return server.Serve();
        }

        private static IPEndPoint EndPoint(string address)
        {
            int colon = address.LastIndexOf(':');
            if (colon <= 0 || !ushort.TryParse(address.Substring(colon + 1), out ushort port))
            {
                throw new BackendSettingsException($"'{address}' is not host:port");
            }

            return new IPEndPoint(IPAddress.Parse(address.Substring(0, colon).Trim('[', ']')), port);
        }
    }
}
