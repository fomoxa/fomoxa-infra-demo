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
using Fomoxa.Networking;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using Fomoxa.Rpc.Net;
using Generated;
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

            var playersRegistry = new LobbyServer(new MockLobby(directory, TimeSpan.FromSeconds(delay))).Registry().Authenticate(new MockPlayers()).Build();
            using RpcServer playersServer = RpcServer.Bind(playersAddress, NetSchema.Build(), playersRegistry).Serve();

            Console.WriteLine($"[Mock] directory giả  : {directoryServer.LocalEndPoint}");
            Console.WriteLine($"[Mock] backend giả    : {playersServer.LocalEndPoint}  (client Unity: 127.0.0.1:{playersServer.LocalEndPoint.Port})");

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
                        Console.Error.WriteLine($"[Mock] khong thay MyGame.Server.dll ('{server}'). Truyen --server=<duong dan> hoac dung --no-spawn.");
                        return 1;
                    }

                    string keyFile = Path.Combine(Path.GetTempPath(), "fomoxa-mock-pod.key");
                    File.WriteAllBytes(keyFile, RandomNumberGenerator.GetBytes(32));
                    string rooms = string.Join(",", Enumerable.Range(1, podCount).Select(i => $"arena:{i}"));
                    string directoryTarget = "127.0.0.1:" + directoryServer.LocalEndPoint.Port;
                    for (int i = 0; i < podCount; i++)
                    {
                        children.Add(StartPod(server, i, basePort + i, directoryTarget, rooms, keyFile, args.Get("pod-backend", ""), args.Get("pod-backend-key", "")));
                    }
                }

                if (!await WaitForPods(directory, spawn ? podCount : 0, stopping))
                {
                    Console.Error.WriteLine("[Mock] pod chua dang ky du sau 30 giay");
                    if (args.Has("selftest"))
                    {
                        return 2;
                    }
                }
                else if (spawn)
                {
                    Console.WriteLine($"[Mock] {podCount} pod da dang ky. Cum gia san sang. Ctrl+C de tat.");
                }

                if (args.Has("selftest"))
                {
                    return await SelfTest(playersServer.LocalEndPoint.Port) ? 0 : 3;
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
            Console.WriteLine("[probe] online: " + string.Join(", ", online.Players.Select(p => $"{p.Name}@{(p.Room.Length == 0 ? "sanh" : p.Room)}")));
            return 0;
        }

        /// <summary>Đóng vai client: đăng nhập, xem phòng, vào phòng, kiểm tra ticket. Không kiểm UDP.</summary>
        private static async Task<bool> SelfTest(int port)
        {
            bool ok = true;
            void Check(string name, bool pass, string detail = "")
            {
                Console.WriteLine($"[selftest] {(pass ? "OK  " : "FAIL")} {name} {detail}");
                ok &= pass;
            }

            RpcConnection Open(PlayerLogin login)
            {
                var options = new ConnectOptions { Credential = Credential.Of(PlayersMethods.Login, login) };
                return RpcConnection.Connect($"127.0.0.1:{port}", NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options).GetAwaiter().GetResult();
            }

            using (RpcConnection connection = Open(new PlayerLogin { Player = "selftest", DeviceId = "selftest-device", Secret = Encoding.UTF8.GetBytes("password123") }))
            {
                var lobby = new LobbyClient(connection);
                RoomList list = await lobby.ListRoomsAsync(new ListRoomsRequest(), CallOptions.Within(TimeSpan.FromSeconds(5)));
                Check("ListRooms", list.Rooms.Count > 1, $"({string.Join(", ", list.Rooms.Select(r => $"{r.RoomKey}[{r.Players}/{r.Capacity}] {r.State}"))})");

                RoomTicket direct = await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "arena:1", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(5)));
                Check("JoinRoom arena:1", direct.Port != 0 && direct.Connection.Length > 0, $"-> {direct.Address}:{direct.Port}, grant {direct.Connection.Length} bytes");

                RoomTicket auto = await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "auto", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(5)));
                Check("JoinRoom auto", auto.Port != 0 && auto.Connection.Length > 0, $"-> {auto.Address}:{auto.Port}");

                OnlineList online = await lobby.PresenceAsync(new PresenceRequest { Room = "arena:1" }, CallOptions.Within(TimeSpan.FromSeconds(5)));
                Check("Presence (danh sach online)", online.Players.Any(p => p.Name == "selftest" && p.Room == "arena:1"),
                    $"({string.Join(", ", online.Players.Select(p => $"{p.Name}@{(p.Room.Length == 0 ? "sanh" : p.Room)}"))})");

                try
                {
                    await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "arena:99", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(5)));
                    Check("JoinRoom phong khong ton tai bi tu choi", false);
                }
                catch (RpcException)
                {
                    Check("JoinRoom phong khong ton tai bi tu choi", true);
                }
            }

            try
            {
                using RpcConnection wrong = Open(new PlayerLogin { Player = "selftest", DeviceId = "x", Secret = Encoding.UTF8.GetBytes("wrongpassword") });
                Check("Sai mat khau bi tu choi", false);
            }
            catch (Exception)
            {
                Check("Sai mat khau bi tu choi", true);
            }

            Console.WriteLine(ok ? "[selftest] TAT CA DAT" : "[selftest] CO LOI");
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
