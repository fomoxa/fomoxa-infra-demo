using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using MyGame.Network;
using Fomoxa.Networking;
using Fomoxa.Networking.Transports;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using Fomoxa.Rpc.Net;
using Generated;
using MyGame.Backend.Accounts;
using MyGame.Backend;
using MyGame.Backend.Models;

namespace MockCluster
{
    /// <summary>
    /// Cụm giả cho client Unity: backend giả :9600 (Login / ListRooms / JoinRoom), directory giả :9500,
    /// và khởi chạy sẵn các pod thật (MyGame.Server) nối vào directory giả.
    ///
    ///   dotnet run --project tools/MockCluster -- --server=MyGame/Server~/bin/Release/net8.0/MyGame.Server.dll
    ///
    /// Tuỳ chọn: --pods=4 --players=0.0.0.0:9600 --directory=0.0.0.0:9500 --base-port=7778
    ///           --strict-version --match-delay=0 --no-spawn --selftest
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] argv)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var args = Args.Parse(argv);

            // --probe=host:port: đóng vai client gọi Login + ListRooms vào một backend (thật hoặc giả) rồi thoát.
            if (args.Has("probe"))
            {
                return await Probe(args.Get("probe", ""));
            }

            string playersAddress = args.Get("players", "0.0.0.0:9600");
            string directoryAddress = args.Get("directory", "0.0.0.0:9500");
            int podCount = int.Parse(args.Get("pods", "4"));
            int basePort = int.Parse(args.Get("base-port", "7778"));
            double delay = double.Parse(args.Get("match-delay", "0"), System.Globalization.CultureInfo.InvariantCulture);
            bool spawn = !args.Has("no-spawn");

            var registry = new FomoxaRegistry();
            FomoxaAdapters.RegisterAll(registry);
            var directory = new MockDirectory(registry, args.Has("strict-version"));

            var directorySchema = FomoxaDirectoryCodecs.NetSchema.Build();
            Registry directoryRegistry = new DirectoryServer(directory).Registry().Authenticate(new MockDirectoryAuth()).Build();
            using RpcServer directoryServer = RpcServer.Bind(directoryAddress, directorySchema, directoryRegistry).Serve();

            string dataFile = args.Get("data", "");
            IKeyValueStore kv = dataFile.Length == 0 ? new MemoryKv() : new FileKv(dataFile);
            var accounts = new AccountService(kv);
            var online = new OnlinePlayers();
            var accountAuth = new AccountAuth(accounts);
            var core = new LobbyCore(accounts, online, accountAuth);

            var playersRegistry = new LobbyServer(new MockLobby(directory, TimeSpan.FromSeconds(delay), online, core)).Registry()
                .Authenticate(new MockPlayers(accountAuth)).Build();
            using RpcServer playersServer = RpcServer.Bind(playersAddress, NetSchema.Build(), playersRegistry).Serve();

            // Cổng pod giả: các pod thật báo bảng điểm lên đây.
            string podsAddress = args.Get("pods-listen", "0.0.0.0:9602");
            var podsRegistry = new PodGateServer(new MockPodGate(core)).Registry().Authenticate(new MockPodKeys()).Build();
            using RpcServer podsServer = RpcServer.Bind(podsAddress, NetSchema.Build(), podsRegistry).Serve();

            Console.WriteLine($"[Mock] mock directory : {directoryServer.LocalEndPoint}");
            Console.WriteLine($"[Mock] mock backend   : {playersServer.LocalEndPoint}  (Unity client: 127.0.0.1:{playersServer.LocalEndPoint.Port})");
            Console.WriteLine($"[Mock] mock pod port  : {podsServer.LocalEndPoint}");

            // TLS: giống proxy TLS đặt trước backend thật. Tắt bằng --no-tls.
            TlsTerminator tls = null;
            if (!args.Has("no-tls"))
            {
                tls = new TlsTerminator(args.Get("tls-listen", "0.0.0.0:9643"), "127.0.0.1", playersServer.LocalEndPoint.Port, args.Get("tls-cert", ".mock/mock-tls.pfx"));
                Console.WriteLine($"[Mock] backend TLS    : port {tls.Port}  pin SHA-256 = {tls.PinSha256}");
                Console.WriteLine($"[Mock]   Unity: -backend:127.0.0.1:{tls.Port} -tlspin:{tls.PinSha256}");
            }
            Console.WriteLine(dataFile.Length == 0 ? "[Mock] accounts: in memory (lost on exit; use --data=file.json to persist)" : $"[Mock] accounts stored at {dataFile}");

            using var stopping = new ManualResetEventSlim();
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stopping.Set(); });
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stopping.Set(); });

            var children = new List<Process>();
            try
            {
                if (spawn)
                {
                    string server = args.Get("server", "");
                    if (server.Length == 0 || !File.Exists(server))
                    {
                        Console.Error.WriteLine($"[Mock] MyGame.Server.dll not found ('{server}'). Pass --server=<path> or use --no-spawn.");
                        return 1;
                    }

                    string keyFile = Path.Combine(Path.GetTempPath(), "fomoxa-mock-pod.key");
                    File.WriteAllBytes(keyFile, RandomNumberGenerator.GetBytes(32));
                    string rooms = string.Join(",", Enumerable.Range(1, podCount).Select(i => $"arena:{i}"));
                    string directoryTarget = "127.0.0.1:" + directoryServer.LocalEndPoint.Port;
                    for (int i = 0; i < podCount; i++)
                    {
                        children.Add(StartPod(server, i, basePort + i, directoryTarget, rooms, keyFile, "127.0.0.1:" + podsServer.LocalEndPoint.Port, keyFile));
                    }
                }

                if (!await WaitForPods(directory, spawn ? podCount : 0, stopping))
                {
                    Console.Error.WriteLine("[Mock] pods did not register in time after 30 seconds");
                    if (args.Has("selftest"))
                    {
                        return 2;
                    }
                }
                else if (spawn)
                {
                    Console.WriteLine($"[Mock] {podCount} pods registered. Mock cluster ready. Ctrl+C to stop.");
                }

                if (args.Has("bot-test"))
                {
                    return await BotClient.Run(playersServer.LocalEndPoint.Port, int.Parse(args.Get("bot-seconds", "25"))) ? 0 : 4;
                }

                if (args.Has("selftest"))
                {
                    // --selftest-target=<cổng backend>,<cổng pod> kiểm một backend THẬT (đang chạy ngoài) thay cho backend giả.
                    int targetPort = playersServer.LocalEndPoint.Port;
                    int targetPodPort = podsServer.LocalEndPoint.Port;
                    bool real = args.Has("selftest-target");
                    if (real)
                    {
                        string[] ports = args.Get("selftest-target", "").Split(',');
                        targetPort = int.Parse(ports[0]);
                        targetPodPort = int.Parse(ports[1]);
                    }

                    bool passed = await SelfTest(targetPort, targetPodPort, real);
                    if (!real)
                    {
                        passed &= await TlsSelfTest(tls);
                    }

                    return passed ? 0 : 3;
                }

                stopping.Wait();
                return 0;
            }
            finally
            {
                foreach (Process child in children)
                {
                    try
                    {
                        if (!child.HasExited)
                        {
                            child.Kill(true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static Process StartPod(string serverDll, int index, int port, string directory, string rooms, string keyFile, string backend, string backendKey)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add(serverDll);
            info.ArgumentList.Add($"--directory={directory}");
            info.ArgumentList.Add($"--name=mock-pod-{index + 1}");
            info.ArgumentList.Add("--address=127.0.0.1");
            info.ArgumentList.Add($"--port={port}");
            info.ArgumentList.Add($"--room={rooms}");
            info.ArgumentList.Add("--claim-one=true");
            info.ArgumentList.Add("--behind-gateway=false");
            info.ArgumentList.Add($"--key={keyFile}");
            if (backend.Length > 0)
            {
                info.ArgumentList.Add($"--backend={backend}");
                info.ArgumentList.Add($"--backend-key={backendKey}");
            }

            Process process = Process.Start(info) ?? throw new InvalidOperationException("khong chay duoc dotnet");
            string tag = $"[pod-{index + 1}] ";
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(tag + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(tag + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static async Task<bool> WaitForPods(MockDirectory directory, int expected, ManualResetEventSlim stopping)
        {
            if (expected == 0)
            {
                return true;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && !stopping.IsSet)
            {
                if (directory.Rooms().Count >= expected)
                {
                    return true;
                }

                await Task.Delay(300);
            }

            return false;
        }

        private static async Task<int> Probe(string address)
        {
            var options = new ConnectOptions { Credential = Credential.Of(PlayersMethods.Login, new PlayerLogin { Player = "probe" }) };
            using RpcConnection connection = await RpcConnection.Connect(address, NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options);
            RoomList list = await new LobbyClient(connection).ListRoomsAsync(new ListRoomsRequest(), CallOptions.Within(TimeSpan.FromSeconds(5)));
            Console.WriteLine("[probe] " + string.Join(", ", list.Rooms.Select(r => $"{r.RoomKey}[{r.Players}/{r.Capacity}] {r.State}")));
            OnlineList online = await new LobbyClient(connection).PresenceAsync(new PresenceRequest { Room = "" }, CallOptions.Within(TimeSpan.FromSeconds(5)));
            Console.WriteLine("[probe] online: " + string.Join(", ", online.Players.Select(p => $"{p.Name}@{(p.Room.Length == 0 ? "lobby" : p.Room)}")));
            return 0;
        }

        /// <summary>Đóng vai client: tài khoản, phòng, online, bạn bè, lời mời, bảng điểm. Không kiểm UDP.</summary>
        private static async Task<bool> SelfTest(int port, int podPort, bool real)
        {
            bool ok = true;
            // Tên riêng cho mỗi lần chạy: backend thật (Nakama) giữ tài khoản lâu dài nên chạy lại không được trùng tên.
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            string me = "st" + suffix;
            string friend = "fr" + suffix;
            void Check(string name, bool pass, string detail = "")
            {
                Console.WriteLine($"[selftest] {(pass ? "OK  " : "FAIL")} {name} {detail}");
                ok &= pass;
            }

            RpcConnection Open(string player, string password, string mode = "", string recovery = "")
            {
                var login = new PlayerLogin
                {
                    Player = player,
                    Mode = mode,
                    Secret = Encoding.UTF8.GetBytes(password),
                    Recovery = Encoding.UTF8.GetBytes(recovery),
                };
                var options = new ConnectOptions { Credential = Credential.Of(PlayersMethods.Login, login) };
                return RpcConnection.Connect($"127.0.0.1:{port}", NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options).GetAwaiter().GetResult();
            }

            bool Rejected(Func<RpcConnection> open)
            {
                try
                {
                    using RpcConnection c = open();
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }

            CallOptions Within = CallOptions.Within(TimeSpan.FromSeconds(8));
            string recoveryCode;

            // --- tài khoản
            using (RpcConnection first = Open(me, "password123", "register"))
            {
                // Mã khôi phục nằm trong PlayerWelcome; thử qua đường khôi phục thật bên dưới.
            }

            Check("Duplicate name registration is rejected", Rejected(() => Open(me.ToUpperInvariant()[..1] + me[1..], "password123", "register")));
            Check("Weak password registration is rejected", Rejected(() => Open("weakpw", "123", "register")));
            Check("Wrong password is rejected", Rejected(() => Open(me, "wrongpassword", "login")));
            Check("Non-existent account is rejected", Rejected(() => Open("nobodyhere", "password123", "login")));
            using (RpcConnection again = Open(me.ToUpperInvariant(), "password123", "login"))
            {
                Check("Correct login (case-insensitive)", true);
            }

            // Recovery needs a real code: register a second account and read the code from PlayerWelcome over a direct connection.
            recoveryCode = RegisterAndGetCode(friend, "password123", port);
            Check("Registration returns a recovery code", recoveryCode.Length == 19, $"({recoveryCode})");
            Check("Recovery with a wrong code is rejected", Rejected(() => Open(friend, "newpassword1", "recover", "AAAA-AAAA-AAAA-AAAA")));
            using (RpcConnection recovered = Open(friend, "newpassword1", "recover", recoveryCode))
            {
                Check("Recovery with the correct code", true);
            }

            Check("Old recovery code is no longer valid", Rejected(() => Open(friend, "another12345", "recover", recoveryCode)));
            Check("Old password no longer works after recovery", Rejected(() => Open(friend, "password123", "login")));

            // --- sảnh + phòng + online
            using (RpcConnection connection = Open(me, "password123", "login"))
            {
                var lobby = new LobbyClient(connection);
                RoomList list = await lobby.ListRoomsAsync(new ListRoomsRequest(), Within);
                Check("ListRooms", list.Rooms.Count > 1, $"({string.Join(", ", list.Rooms.Select(r => $"{r.RoomKey}[{r.Players}/{r.Capacity}] {r.State}"))})");

                // Hai người cùng xin phòng "auto": cụm giả cấp ngay; Nakama thật ghép cặp rồi cấp cùng một phòng.
                using RpcConnection partner = Open(friend, "newpassword1", "login");
                Task<RoomTicket> first = lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "auto", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(25)));
                Task<RoomTicket> second = new LobbyClient(partner).JoinRoomAsync(new JoinRoomRequest { RoomKey = "auto", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(25)));
                RoomTicket auto = await first;
                RoomTicket pair = await second;
                Check("JoinRoom auto (2 players)", auto.Port != 0 && auto.Connection.Length > 0 && pair.Port != 0,
                    $"-> {auto.Address}:{auto.Port} / {pair.Address}:{pair.Port}{(real ? " (via real Nakama)" : "")}");
                if (real)
                {
                    Check("Both matched to the same pod", auto.Port == pair.Port);
                }
                else
                {
                    RoomTicket direct = await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "arena:1", Version = "1.0" }, Within);
                    Check("JoinRoom arena:1", direct.Port != 0 && direct.Connection.Length > 0, $"-> {direct.Address}:{direct.Port}, grant {direct.Connection.Length} bytes");
                }

                OnlineList online = await lobby.PresenceAsync(new PresenceRequest { Room = "arena:1" }, Within);
                Check("Presence", online.Players.Any(p => p.Name == me && p.Room == "arena:1"),
                    $"({string.Join(", ", online.Players.Select(p => $"{p.Name}@{(p.Room.Length == 0 ? "lobby" : p.Room)}"))})");

                try
                {
                    await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "arena:99", Version = "1.0" }, Within);
                    Check("JoinRoom non-existent room is rejected", false);
                }
                catch (RpcException)
                {
                    Check("JoinRoom non-existent room is rejected", true);
                }

                // --- đổi mật khẩu
                ActionResult changed = await lobby.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "password123", NewPassword = "password456" }, Within);
                Check("Change password", changed.Ok);
                bool wrongOld = false;
                try
                {
                    await lobby.ChangePasswordAsync(new ChangePasswordRequest { OldPassword = "khongdung123", NewPassword = "password789" }, Within);
                }
                catch (RpcException)
                {
                    wrongOld = true;
                }

                Check("Change password with a wrong old password is rejected", wrongOld);
            }

            Check("Login with the old password after change is rejected", Rejected(() => Open(me, "password123", "login")));

            // --- bảng điểm + kỷ lục do pod báo
            using (RpcConnection pod = await RpcConnection.Connect(
                $"127.0.0.1:{podPort}",
                NetSchema.Build(),
                PodGateClient.Declare(PodsClient.Registry()).Build(),
                new ConnectOptions { Credential = Credential.Of(PodsMethods.Login, new PodLogin { Key = new byte[32] }) }))
            using (RpcConnection meConnection = Open(me, "password456", "login"))
            {
                var gate = new PodGateClient(pod);
                var lobby = new LobbyClient(meConnection);

                async Task Report(uint steps) =>
                    await gate.ReportScoresAsync(new ScoreReport { Room = "arena:1", Entries = { new PodScore { Player = me, Steps = steps }, new PodScore { Player = "guestx", Steps = 1 } } }, Within);

                await Report(3);
                // Pod reports are processed in the background (they read and write storage), so poll for up to 10 seconds.
                ScoreboardState board = await lobby.ScoreboardAsync(new ScoreboardRequest { Room = "arena:1" }, Within);
                for (int attempt = 0; attempt < 40 && board.Entries.Count != 2; attempt++)
                {
                    await Task.Delay(250);
                    board = await lobby.ScoreboardAsync(new ScoreboardRequest { Room = "arena:1" }, Within);
                }

                Check("Scoreboard ranking", board.Entries.Count == 2 && board.Entries[0].Name == me && board.Entries[0].Steps == 3,
                    $"({string.Join(", ", board.Entries.Select(e => $"{e.Name}:{e.Steps}/{e.Best}"))})");

                await Report(2);
                await Report(5);
                PlayerProfile profile = await lobby.GetProfileAsync(new ProfileRequest(), Within);
                for (int attempt = 0; attempt < 40 && profile.BestSteps != 5; attempt++)
                {
                    await Task.Delay(250);
                    profile = await lobby.GetProfileAsync(new ProfileRequest(), Within);
                }

                Check("Best record stores the highest step count", profile.BestSteps == 5 && profile.Matches >= 1, $"(best={profile.BestSteps}, matches={profile.Matches})");

                // --- bạn bè + lời mời
                using RpcConnection other = Open(friend, "newpassword1", "login");
                var otherLobby = new LobbyClient(other);
                await lobby.FriendActionAsync(new FriendActionRequest { Action = "request", Target = friend }, Within);
                SocialState incoming = await otherLobby.SocialAsync(new SocialRequest(), Within);
                Check("Friend request reaches the recipient", incoming.Incoming.Contains(me));
                await otherLobby.FriendActionAsync(new FriendActionRequest { Action = "accept", Target = me }, Within);
                SocialState mine = await lobby.SocialAsync(new SocialRequest(), Within);
                Check("Friendship is mutual", mine.Friends.Any(f => f.Name == friend));

                await lobby.PresenceAsync(new PresenceRequest { Room = "arena:1" }, Within);
                await otherLobby.PresenceAsync(new PresenceRequest { Room = "" }, Within);
                ActionResult invited = await lobby.InviteAsync(new InviteRequest { To = friend, Room = "arena:1" }, Within);
                SocialState withInvite = await otherLobby.SocialAsync(new SocialRequest(), Within);
                Check("Invite a friend to a room", invited.Ok && withInvite.Invites.Any(i => i.From == me && i.Room == "arena:1"));
                Check("Friend shows as online and where they are", withInvite.Friends.Any(f => f.Name == me && f.Online && f.Room == "arena:1"));

                bool inviteStranger = false;
                try
                {
                    await lobby.InviteAsync(new InviteRequest { To = "guestx", Room = "arena:1" }, Within);
                }
                catch (RpcException)
                {
                    inviteStranger = true;
                }

                Check("Cannot invite a non-friend", inviteStranger);
            }

            Console.WriteLine(ok ? "[selftest] ALL PASSED" : "[selftest] FAILURES");
            return ok;
        }

        /// <summary>Đăng ký rồi lấy mã khôi phục bằng Lobby.TakeRecoveryCode.</summary>
        private static string RegisterAndGetCode(string player, string password, int port)
        {
            var login = new PlayerLogin { Player = player, Mode = "register", Secret = Encoding.UTF8.GetBytes(password) };
            var options = new ConnectOptions { Credential = Credential.Of(PlayersMethods.Login, login) };
            using RpcConnection connection = RpcConnection.Connect($"127.0.0.1:{port}", NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options).GetAwaiter().GetResult();
            ActionResult taken = new LobbyClient(connection).TakeRecoveryCodeAsync(new RecoveryCodeRequest(), CallOptions.Within(TimeSpan.FromSeconds(8))).GetAwaiter().GetResult();
            return taken.Ok ? taken.Message : "";
        }

        /// <summary>Kết nối qua TLS tới cổng đầu cuối: đúng pin thì vào được và gọi RPC; sai pin thì bị từ chối.</summary>
        private static async Task<bool> TlsSelfTest(TlsTerminator tls)
        {
            if (tls == null)
            {
                return true;
            }

            bool ok = true;
            void Check(string name, bool pass, string detail = "")
            {
                Console.WriteLine($"[selftest] {(pass ? "OK  " : "FAIL")} {name} {detail}");
                ok &= pass;
            }

            async Task<ITransport> ConnectTls(string pin)
            {
                var settings = new TlsSettings { CertificateCheck = new PinnedCertificateCheck(pin) };
                using ITransportConnector connector = new TcpTransportFactory(settings).CreateConnector("127.0.0.1", (ushort)tls.Port);
                DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline)
                {
                    ConnectStatus status = connector.Poll(out ITransport transport);
                    if (status == ConnectStatus.Connected)
                    {
                        return transport;
                    }

                    if (status != ConnectStatus.Pending)
                    {
                        throw new InvalidOperationException(status.ToString());
                    }

                    await Task.Delay(10);
                }

                throw new TimeoutException("TLS handshake timed out");
            }

            try
            {
                ITransport transport = await ConnectTls(tls.PinSha256);
                var login = new PlayerLogin { Player = "tlsguest" };
                var options = new ConnectOptions { Credential = Credential.Of(PlayersMethods.Login, login) };
                using RpcConnection connection = await RpcConnection.Open(transport, NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options);
                RoomList rooms = await new LobbyClient(connection).ListRoomsAsync(new ListRoomsRequest(), CallOptions.Within(TimeSpan.FromSeconds(8)));
                Check("Connect over TLS (correct pin) and call RPC", rooms.Rooms.Count > 0, $"({rooms.Rooms.Count} rooms)");
            }
            catch (Exception problem)
            {
                Check("Connect over TLS (correct pin) and call RPC", false, problem.Message);
            }

            try
            {
                await ConnectTls(new string('0', 64));
                Check("Wrong pin is rejected", false);
            }
            catch (InvalidOperationException problem)
            {
                Check("Wrong pin is rejected", problem.Message == "TlsRejected", $"({problem.Message})");
            }
            catch (Exception problem)
            {
                Check("Wrong pin is rejected", false, problem.Message);
            }

            Console.WriteLine(ok ? "[selftest] TLS PASSED" : "[selftest] TLS FAILURES");
            return ok;
        }

        private sealed class Args
        {
            private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

            public static Args Parse(string[] argv)
            {
                var args = new Args();
                foreach (string raw in argv)
                {
                    if (!raw.StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string body = raw.Substring(2);
                    int equals = body.IndexOf('=');
                    args.values[equals < 0 ? body : body.Substring(0, equals)] = equals < 0 ? "true" : body.Substring(equals + 1);
                }

                return args;
            }

            public bool Has(string name) => values.ContainsKey(name);

            public string Get(string name, string fallback) => values.TryGetValue(name, out string value) ? value : fallback;
        }
    }
}
