using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
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
        private readonly NakamaLink nakama;

        private Backend(DirectoryLink directory, NakamaLink nakama)
        {
            this.directory = directory;
            this.nakama = nakama;
        }

        public IPEndPoint? PlayersTcp { get; private set; }

        public IPEndPoint? PlayersWebSocket { get; private set; }

        public IPEndPoint? PodsTcp { get; private set; }

        public static Backend Start(BackendSettings settings)
        {
            NakamaLink nakama = string.IsNullOrEmpty(settings.NakamaUrl)
                ? null!
                : new NakamaLink(settings.NakamaUrl, settings.NakamaServerKey, settings.NakamaHttpKey);
            var backend = new Backend(new DirectoryLink(settings.DirectoryAddress, settings.DirectoryKey), nakama);
            if (nakama != null)
            {
                Console.WriteLine($"[Nakama] cấu hình {settings.NakamaUrl}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        NakamaSession probe = await nakama.AuthenticateDevice("fomoxa-selftest-00001");
                        Console.WriteLine($"[Nakama] self test ok {probe.UserId}");
                    }
                    catch (Exception problem)
                    {
                        Console.WriteLine($"[Nakama] self test lỗi: {problem.GetType().Name} {problem.Message}");
                    }
                });
            }
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
            nakama?.Dispose();
        }

        private void Listen(BackendSettings settings)
        {
            var schema = Generated.NetSchema.Build();
            var sessions = new PlayerSessions();
            var stats = new RoomStats();
            Registry players = new LobbyServer(new Lobby(directory, nakama, sessions, stats)).Registry().Authenticate(new Players(nakama, sessions)).Build();
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
                Registry pods = new PodGateServer(new PodGate(directory, stats)).Registry().Authenticate(new PodKeys(settings.PodKey)).Build();
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
