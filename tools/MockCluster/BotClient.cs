using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Standalone;
using Fomoxa.Networking.Transports;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Generated;
using MyGame.Backend;
using MyGame.Backend.Models;
using MyGame.Network;
using MyGame.Network.Models;

namespace MockCluster
{
    /// <summary>
    /// Bot chạy thật qua UDP mã hoá: đăng nhập -> xin ticket -> nối vào pod -> tự leo các bục theo vị trí server gửi về.
    /// Kiểm chứng đầu-cuối: ticket được pod chấp nhận, pod biết tên người chơi, nhảy/đáp bục, bảng điểm và kỷ lục.
    /// </summary>
    internal static class BotClient
    {
        public static async Task<bool> Run(int backendPort, int seconds)
        {
            bool ok = true;
            void Check(string name, bool pass, string detail = "")
            {
                Console.WriteLine($"[bot] {(pass ? "OK  " : "FAIL")} {name} {detail}");
                ok &= pass;
            }

            const string name = "botjumper";
            var options = new ConnectOptions
            {
                Credential = Credential.Of(PlayersMethods.Login, new PlayerLogin { Player = name, Mode = "register", Secret = Encoding.UTF8.GetBytes("password123") }),
            };
            using RpcConnection rpc = await RpcConnection.Connect($"127.0.0.1:{backendPort}", NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options);
            var lobby = new LobbyClient(rpc);
            RoomTicket ticket = await lobby.JoinRoomAsync(new JoinRoomRequest { RoomKey = "arena:1", Version = "1.0" }, CallOptions.Within(TimeSpan.FromSeconds(8)));
            Check("Co ticket vao arena:1", ticket.Connection.Length > 0, $"-> {ticket.Address}:{ticket.Port}");

            var registry = new FomoxaRegistry();
            FomoxaAdapters.RegisterAll(registry);
            NetworkRuntime runtime = null!;
            var prefabs = new StandalonePrefabs();
            prefabs.Register(FomoxaPrefabs.PlayerId, FomoxaPrefabs.PlayerFingerprint, () => new PlayerEntity(() => runtime.TimeManager.TickDelta));
            var clock = Stopwatch.StartNew();
            runtime = StandaloneRuntime.Create(
                registry,
                new NetworkSettings { TickRate = 30, MaxTicksPerFrame = 3, InputBuffer = 2, LogSendDropped = false, LogReceiveDropped = false },
                new EncryptedUdpTransportFactory(registry),
                prefabs,
                new StandaloneBehaviours(),
                new SceneFileDirectory(Path.Combine(AppContext.BaseDirectory, "Scenes")),
                () => clock.Elapsed,
                NetworkLog.Console);

            int connectedAt = -1;
            runtime.ClientManager.OnClientConnectionState += args =>
            {
                Console.WriteLine($"[bot] ket noi: {args.State} {args.Reason}");
                if (args.State == ConnectionState.Started)
                {
                    connectedAt = Environment.TickCount;
                }
            };

            Player.BotGather = Climb;
            runtime.ClientManager.StartConnection(ticket.Address, ticket.Port, ticket.Connection);

            TimeSpan last = clock.Elapsed;
            double end = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < end)
            {
                TimeSpan now = clock.Elapsed;
                runtime.BeginFrame(now, (now - last).TotalSeconds);
                runtime.EndFrame();
                last = now;
                await Task.Delay(1);
            }

            Check("Da vao pod (client Started)", connectedAt >= 0);
            runtime.ClientManager.StopConnection();

            ScoreboardState board = await lobby.ScoreboardAsync(new ScoreboardRequest { Room = "arena:1" }, CallOptions.Within(TimeSpan.FromSeconds(8)));
            ScoreEntry mine = board.Entries.FirstOrDefault(e => e.Name == name);
            Check("Bang diem co ten bot", mine != null, $"({string.Join(", ", board.Entries.Select(e => $"{e.Name}:{e.Steps}/{e.Best}"))})");
            Check("Bot leo duoc it nhat 3 bac", mine != null && mine.Steps >= 3, $"(steps={mine?.Steps})");
            PlayerProfile profile = await lobby.GetProfileAsync(new ProfileRequest(), CallOptions.Within(TimeSpan.FromSeconds(8)));
            Check("Ky luc ca nhan duoc luu", mine != null && profile.BestSteps >= mine.Steps && profile.BestSteps >= 3, $"(best={profile.BestSteps}, matches={profile.Matches})");

            Player.BotGather = null;
            Console.WriteLine(ok ? "[bot] TAT CA DAT" : "[bot] CO LOI");
            return ok;
        }

        private static int traceCounter;
        private static int highest;

        /// <summary>Đi tới bục kế tiếp, nhảy khi sát mép. Dựa vào vị trí server gửi về nên không cần biết điểm xuất phát.</summary>
        private static void Climb(Player player, MoveInput input)
        {
            if (player.Replicated is not Vector3 position)
            {
                return;
            }

            float x = position.X;
            float z = position.Z;
            float feet = position.Y - Course.HalfHeight;
            float ground = Course.GroundHeight(x, z, feet);
            bool grounded = Math.Abs(feet - ground) < 0.02f;

            // Đang bay thì StepAt = 0: nhớ bậc cao nhất đã đứng để vẫn hướng tới bục kế tiếp.
            int standing = Course.StepAt(x, z, feet);
            if (grounded && standing > highest)
            {
                highest = standing;
            }

            int current = highest;
            int next = Math.Min(Course.Count, current + 1);
            Course.Platform(next, out float cx, out float cz, out _);

            float dx = cx - x;
            float dz = cz - z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length > 0.01f)
            {
                input.X = dx / length;
                input.Z = dz / length;
            }

            float edge = MathF.Max(MathF.Abs(dx), MathF.Abs(dz)) - Course.PlatformHalf;
            input.Jump = grounded && edge < 2.0f && current < Course.Count;

            if (++traceCounter % 15 == 0 && Environment.GetEnvironmentVariable("BOT_TRACE") == "1")
            {
                Console.WriteLine($"[bot] pos=({x:F2},{feet:F2},{z:F2}) step={current}->{next} grounded={grounded} edge={edge:F2} jump={input.Jump}");
            }
        }
    }
}
