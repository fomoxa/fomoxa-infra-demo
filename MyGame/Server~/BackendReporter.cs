using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Generated;
using MyGame.Backend;
using MyGame.Backend.Models;

namespace MyGame.Server
{
    /// <summary>
    /// Báo số người chơi thật của từng room về backend (PodGate.ReportRoom, cổng pod 9602) để lobby hiển thị chính xác.
    /// Best-effort: backend chưa lên hay mất kết nối chỉ ghi log, pod vẫn chạy bình thường.
    /// </summary>
    internal sealed class BackendReporter : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        private readonly string address;
        private readonly byte[] key;
        private readonly uint capacity;
        private readonly Func<IReadOnlyCollection<string>> rooms;
        private readonly Func<int> players;
        private readonly Func<IReadOnlyList<(string Player, uint Steps)>> scores;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Task loop;

        private BackendReporter(string address, byte[] key, uint capacity, Func<IReadOnlyCollection<string>> rooms, Func<int> players, Func<IReadOnlyList<(string Player, uint Steps)>> scores)
        {
            this.scores = scores;
            this.address = address;
            this.key = key;
            this.capacity = capacity;
            this.rooms = rooms;
            this.players = players;
            loop = Task.Run(Run);
        }

        public static BackendReporter StartIfConfigured(PodConfig config, Func<IReadOnlyCollection<string>> rooms, Func<int> players, Func<IReadOnlyList<(string Player, uint Steps)>> scores)
        {
            if (string.IsNullOrEmpty(config.BackendAddress))
            {
                return null;
            }

            Console.WriteLine($"[Backend] bao so nguoi choi ve {config.BackendAddress}");
            return new BackendReporter(config.BackendAddress, File.ReadAllBytes(config.BackendKeyFile), config.MaxPlayers, rooms, players, scores);
        }

        private async Task Run()
        {
            RpcConnection connection = null;
            bool warned = false;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (connection == null || !connection.IsOpen)
                    {
                        connection?.Dispose();
                        connection = await Connect();
                        Console.WriteLine("[Backend] da ket noi");
                        warned = false;
                    }

                    var gate = new PodGateClient(connection);
                    uint count = (uint)Math.Max(0, players());
                    foreach (string room in rooms())
                    {
                        await gate.ReportRoomAsync(
                            new RoomReport { RoomKey = room, Players = count, Capacity = capacity },
                            CallOptions.Within(TimeSpan.FromSeconds(3)));

                        // Bảng điểm: pod claim-one giữ đúng một room nên mọi người chơi trên pod thuộc room đó.
                        var report = new ScoreReport { Room = room };
                        foreach (var entry in scores())
                        {
                            if (entry.Player.Length > 0)
                            {
                                report.Entries.Add(new PodScore { Player = entry.Player, Steps = entry.Steps });
                            }
                        }

                        await gate.ReportScoresAsync(report, CallOptions.Within(TimeSpan.FromSeconds(3)));
                    }
                }
                catch (Exception error)
                {
                    connection?.Dispose();
                    connection = null;
                    if (!warned)
                    {
                        warned = true;
                        Console.Error.WriteLine($"[Backend] bao cao loi: {error.Message} (se thu lai)");
                    }
                }

                try
                {
                    await Task.Delay(Interval, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            connection?.Dispose();
        }

        private Task<RpcConnection> Connect()
        {
            var options = new ConnectOptions
            {
                Credential = Credential.Of(PodsMethods.Login, new PodLogin { Key = key }),
            };
            return RpcConnection.Connect(address, NetSchema.Build(), PodGateClient.Declare(PodsClient.Registry()).Build(), options);
        }

        public void Dispose()
        {
            stop.Cancel();
            try
            {
                loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            stop.Dispose();
        }
    }
}
