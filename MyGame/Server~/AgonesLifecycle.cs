using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agones;
using Grpc.Core;

namespace MyGame.Server
{
    /// <summary>
    /// Quản lý vòng đời pod với Agones sidecar trong Kubernetes (gRPC port 9357).
    /// Nếu chạy ngoài cluster (môi trường dev local), toàn bộ phương thức là no-op.
    /// </summary>
    internal sealed class AgonesLifecycle : IDisposable
    {
        private readonly AgonesSDK agones;
        private readonly HashSet<ulong> activePlayers = new HashSet<ulong>();
        private bool allocated;
        private bool shuttingDown;

        public bool Enabled => agones != null;

        private AgonesLifecycle(AgonesSDK agones)
        {
            this.agones = agones;
        }

        public static async Task<AgonesLifecycle> StartAsync(bool enabled)
        {
            if (!enabled)
            {
                Console.WriteLine("[Agones] disabled (local dev no-op)");
                return new AgonesLifecycle(null);
            }

            Console.WriteLine("[Agones] connecting to sidecar and sending Ready...");
            var sdk = new AgonesSDK();
            bool ready = false;

            // Sidecar Agones khởi động chậm hơn container game server vài trăm ms.
            // Thử lại ReadyAsync tối đa 60 lần x 500 ms = 30 giây.
            for (int attempt = 1; attempt <= 60; attempt++)
            {
                try
                {
                    Status status = await sdk.ReadyAsync();
                    if (status.StatusCode == StatusCode.OK)
                    {
                        ready = true;
                        Console.WriteLine("[Agones] Ready");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt % 10 == 0 || attempt == 1)
                    {
                        Console.WriteLine($"[Agones] ReadyAsync attempt {attempt}/60: {ex.Message}");
                    }
                }

                await Task.Delay(500);
            }

            if (!ready)
            {
                Console.Error.WriteLine("[Agones] warning: khong the Ready voi Agones sidecar sau 30s. Chuyen sang che do no-op.");
                return new AgonesLifecycle(null);
            }

            return new AgonesLifecycle(sdk);
        }

        public void Health()
        {
            if (agones == null || shuttingDown)
            {
                return;
            }

            _ = SendHealthAsync();
        }

        private async Task SendHealthAsync()
        {
            try
            {
                await agones.HealthAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agones] HealthAsync error: {ex.Message}");
            }
        }

        public async Task OnRoomAcquiredAsync(string room)
        {
            if (agones == null || allocated || shuttingDown)
            {
                return;
            }

            allocated = true;
            Console.WriteLine($"[Agones] Allocate (room {room})");
            try
            {
                await agones.AllocateAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agones] AllocateAsync error: {ex.Message}");
            }
        }

        public void OnPlayerConnected(ulong peerId)
        {
            lock (activePlayers)
            {
                activePlayers.Add(peerId);
                Console.WriteLine($"[Agones] player {peerId} connected (active: {activePlayers.Count})");
            }
        }

        public void OnPlayerDisconnected(ulong peerId)
        {
            int remaining;
            lock (activePlayers)
            {
                activePlayers.Remove(peerId);
                remaining = activePlayers.Count;
                Console.WriteLine($"[Agones] player {peerId} disconnected (remaining: {remaining})");
            }

            // Khi hết người chơi trong phòng đã Allocate, đưa GameServer về Ready để tái sử dụng
            if (remaining == 0 && allocated && !shuttingDown && agones != null)
            {
                allocated = false;
                Console.WriteLine("[Agones] all players left, returning to Ready");
                _ = ReturnToReadyAsync();
            }
        }

        private async Task ReturnToReadyAsync()
        {
            try
            {
                await agones.ReadyAsync();
                Console.WriteLine("[Agones] Ready again");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agones] ReadyAsync error: {ex.Message}");
            }
        }

        public async Task ShutdownAsync()
        {
            if (agones == null || shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            Console.WriteLine("[Agones] Shutting down...");
            try
            {
                await agones.ShutDownAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agones] ShutDownAsync error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (agones != null && !shuttingDown)
            {
                shuttingDown = true;
                try
                {
                    agones.ShutDownAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }
            }
        }
    }
}
