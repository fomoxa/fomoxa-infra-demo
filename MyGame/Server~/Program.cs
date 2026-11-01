using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Standalone;
using Fomoxa.Rpc.Directory;
using Generated;
using MyGame.Network;

namespace MyGame.Server
{
    /// <summary>
    /// Game server dạng console: thay cho MyGame.exe -pod, chạy được trong container
    /// mà không cần Unity. Bước 1 của P5 chỉ làm phần này chạy local; Agones thêm sau.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            PodConfig config;
            try
            {
                config = PodConfig.Read(Environment.GetCommandLineArgs());
            }
            catch (PodConfigException problem)
            {
                Console.Error.WriteLine($"server: {problem.Message}");
                return 1;
            }

            var registry = new FomoxaRegistry();
            FomoxaAdapters.RegisterAll(registry);

            NetworkRuntime runtime = null!;
            var prefabs = new StandalonePrefabs();
            prefabs.Register(
                FomoxaPrefabs.PlayerId,
                FomoxaPrefabs.PlayerFingerprint,
                () => new PlayerEntity(() => runtime.TimeManager.TickDelta));

            var clock = Stopwatch.StartNew();
            var transport = new PodTransport(registry) { BehindGateway = config.BehindGateway };

            using var agones = await AgonesLifecycle.StartAsync(config.Agones);

            runtime = StandaloneRuntime.Create(
                registry,
                new NetworkSettings
                {
                    TickRate = 30,
                    MaxTicksPerFrame = 3,
                    InputBuffer = 2,
                    LogSendDropped = false,
                    LogReceiveDropped = false,
                },
                transport,
                prefabs,
                new StandaloneBehaviours(),
                new SceneFileDirectory(Path.Combine(AppContext.BaseDirectory, "Scenes")),
                () => clock.Elapsed,
                NetworkLog.Console);

            int slot = 0;
            var authenticator = new PodAuthenticator();
            runtime.ServerManager.Authenticator = authenticator;
            var entities = new Dictionary<ulong, PlayerEntity>();
            var scoreGate = new object();
            IReadOnlyList<(string Player, uint Steps)> scoreSnapshot = Array.Empty<(string, uint)>();
            runtime.ServerManager.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Started)
                {
                    agones.OnPlayerConnected(args.PeerId);
                    var player = new PlayerEntity(() => runtime.TimeManager.TickDelta);
                    player.Position = new Vector3(0f, 1f, 0f) + SpawnOffset();
                    runtime.ServerManager.Spawn(player, args.PeerId);
                    lock (scoreGate)
                    {
                        entities[args.PeerId] = player;
                    }

                    Console.WriteLine($"[Pod] peer {args.PeerId}: Started -> spawn at {player.Position}");
                    return;
                }

                if (args.State == ConnectionState.Stopped)
                {
                    agones.OnPlayerDisconnected(args.PeerId);
                    lock (scoreGate)
                    {
                        entities.Remove(args.PeerId);
                    }

                    authenticator.Forget(args.PeerId);
                }

                Console.WriteLine($"[Pod] peer {args.PeerId}: {args.State} {args.Reason}");
            };

            Vector3 SpawnOffset()
            {
                slot = (slot + 1) % 8;
                float angle = slot * MathF.PI * 2f / 8f;
                return new Vector3(MathF.Sin(angle) * 3f, 0f, MathF.Cos(angle) * 3f);
            }

            using var stopping = new ManualResetEventSlim();

            using var pod = new DirectoryPod(runtime, new DirectoryPodSettings
            {
                DirectoryAddress = config.DirectoryHost,
                DirectoryPort = config.DirectoryPort,
                Key = File.ReadAllBytes(config.KeyFile),
                Address = config.PodAddress,
                Port = config.Port,
                Target = config.PodName,
                Version = config.Version,
                Region = config.Region,
                MaxPlayers = config.MaxPlayers,
            });

            using var backendReporter = BackendReporter.StartIfConfigured(
                config,
                () => pod.Rooms,
                () => runtime.ServerManager.PeerCount,
                () => scoreSnapshot);

            bool draining = false;

            // SIGTERM/SIGINT lần 1: xin directory drain (ngừng nhận room mới) rồi chờ hết người chơi.
            // Lần 2, hoặc khi chưa đăng ký directory, hoặc directory báo lỗi: thoát ngay.
            // Hết hạn drain_seconds thì OnDrainDeadline xử lý.
            bool drainRequested = false;
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

            pod.OnRegistered += registration =>
            {
                Console.WriteLine($"[Pod] registered id={registration.PodId} changed={registration.ChangedPodId}");

                if (registration.ChangedPodId && runtime.ServerManager.State != ServerState.Stopped)
                {
                    Console.WriteLine("[Pod] ticket key doi, dung listener de mo lai");
                    runtime.ServerManager.StopConnection();
                }

                transport.Use(new ConnectTokens(registry, registration.TicketKey), config.PodName);

                if (runtime.ServerManager.State == ServerState.Stopped)
                {
                    runtime.ServerManager.StartConnection(config.Port);
                    Console.WriteLine($"[Pod] listening on udp {runtime.ServerManager.Port}");
                }

                if (config.ClaimOne)
                {
                    _ = AcquireFirstAvailable(config.Rooms);
                }
                else
                {
                    foreach (string room in config.Rooms)
                    {
                        _ = AcquireRoom(room);
                    }
                }
            };

            pod.OnDrain += deadline =>
            {
                draining = true;
                Console.WriteLine($"[Pod] draining until {deadline:O}");
            };

            pod.OnDrainDeadline += () =>
            {
                Console.WriteLine("[Pod] drain deadline reached");
                _ = agones.ShutdownAsync();
                stopping.Set();
            };

            pod.OnError += error => Console.Error.WriteLine("[Pod] " + error);

            async Task AcquireFirstAvailable(IReadOnlyList<string> candidateRooms)
            {
                while (!draining && !stopping.IsSet)
                {
                    foreach (string candidate in candidateRooms)
                    {
                        if (draining || stopping.IsSet)
                        {
                            return;
                        }

                        try
                        {
                            await pod.AcquireRoom(candidate);
                            Console.WriteLine($"[Pod] claimed room from pool: {candidate}");
                            await agones.OnRoomAcquiredAsync(candidate);
                            return;
                        }
                        catch (Exception error)
                        {
                            Console.WriteLine($"[Pod] pool candidate {candidate} busy: {error.Message}");
                        }
                    }

                    // Tạm thời các room trong pool đều bận, thử lại sau 2 giây
                    try
                    {
                        await Task.Delay(2000);
                    }
                    catch
                    {
                        return;
                    }
                }
            }

            async Task AcquireRoom(string room)
            {
                if (draining)
                {
                    return;
                }

                try
                {
                    await pod.AcquireRoom(room);
                    Console.WriteLine($"[Pod] holds {room}");
                    await agones.OnRoomAcquiredAsync(room);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"[Pod] acquire {room}: {error.Message}");
                }
            }

            pod.Start();
            Console.WriteLine(
                $"[Pod] registering {config.PodName} ({config.PodAddress}:{config.Port}, version {config.Version}) at {config.DirectoryHost}:{config.DirectoryPort}");

            TimeSpan last = clock.Elapsed;
            double nextTrace = 0;
            double nextHealth = 0;
            double nextScores = 0;
            while (!stopping.IsSet)
            {
                TimeSpan now = clock.Elapsed;
                runtime.BeginFrame(now, (now - last).TotalSeconds);
                runtime.EndFrame();
                last = now;

                if (now.TotalSeconds >= nextScores)
                {
                    nextScores = now.TotalSeconds + 1;
                    var snapshot = new List<(string Player, uint Steps)>();
                    lock (scoreGate)
                    {
                        foreach (KeyValuePair<ulong, PlayerEntity> entry in entities)
                        {
                            snapshot.Add((authenticator.NameOf(entry.Key), (uint)entry.Value.Behaviour.Steps));
                        }
                    }

                    scoreSnapshot = snapshot;
                }

                if (now.TotalSeconds >= nextHealth)
                {
                    nextHealth = now.TotalSeconds + 2;
                    agones.Health();
                }

                if (config.Trace && now.TotalSeconds >= nextTrace)
                {
                    nextTrace = now.TotalSeconds + 2;
                    foreach (KeyValuePair<uint, INetworkEntity> entry in runtime.ServerManager.Spawned)
                    {
                        Vector3 position = entry.Value.ReadWorldPosition();
                        Console.WriteLine($"[Pos] id={entry.Key} pos=({position.X:F2}, {position.Y:F2}, {position.Z:F2})");
                    }
                }

                if (drainRequested && runtime.ServerManager.PeerCount == 0)
                {
                    Console.WriteLine("[Pod] drain: het nguoi choi, thoat");
                    stopping.Set();
                }

                Thread.Sleep(1);
            }

            runtime.ServerManager.StopConnection();
            await agones.ShutdownAsync();
            Console.WriteLine("[Pod] stopped");
            return 0;

            void Stop(PosixSignalContext context)
            {
                context.Cancel = true;

                if (drainRequested || !pod.IsRegistered)
                {
                    stopping.Set();
                    return;
                }

                drainRequested = true;
                draining = true;
                Console.WriteLine($"[Pod] {context.Signal}: xin drain, cho het nguoi choi (gui tin hieu lan nua de thoat ngay)");
                pod.RequestDrain().ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        Console.Error.WriteLine($"[Pod] RequestDrain loi: {task.Exception?.GetBaseException().Message}; thoat ngay");
                        stopping.Set();
                    }
                });
            }
        }
    }
}
