using System;
using System.Collections.Generic;
using System.Threading;
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
        private readonly object stateLock = new object();
        private static readonly TimeSpan EmptyGrace = TimeSpan.FromSeconds(30);
        private CancellationTokenSource emptyTimer;
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

        /// <summary>
        /// Pod giữ được room từ directory. Chưa có người chơi nên vẫn ở trạng thái Ready;
        /// chỉ chuyển Allocated khi người chơi đầu tiên kết nối.
        /// </summary>
        public Task OnRoomAcquiredAsync(string room)
        {
            Console.WriteLine($"[Agones] holding room {room} (state stays Ready until first player)");
            return Task.CompletedTask;
        }

        public void OnPlayerConnected(ulong peerId)
        {
            bool needAllocate = false;
            lock (stateLock)
            {
                activePlayers.Add(peerId);
                Console.WriteLine($"[Agones] player {peerId} connected (active: {activePlayers.Count})");

                // Có người quay lại trong thời gian chờ -> hủy đếm ngược tắt pod.
                emptyTimer?.Cancel();
                emptyTimer = null;

                if (agones != null && !allocated && !shuttingDown)
                {
                    allocated = true;
                    needAllocate = true;
                }
            }

            if (needAllocate)
            {
                _ = AllocateAsync();
            }
        }

        private async Task AllocateAsync()
        {
            Console.WriteLine("[Agones] first player joined -> Allocate");
            try
            {
                await agones.AllocateAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Agones] AllocateAsync error: {ex.Message}");
                lock (stateLock)
                {
                    // Cho phép thử lại ở lần kết nối kế tiếp.
                    allocated = false;
                }
            }
        }

        public void OnPlayerDisconnected(ulong peerId)
        {
            CancellationTokenSource timer = null;
            lock (stateLock)
            {
                activePlayers.Remove(peerId);
                int remaining = activePlayers.Count;
                Console.WriteLine($"[Agones] player {peerId} disconnected (remaining: {remaining})");

                if (remaining == 0 && allocated && !shuttingDown && agones != null)
                {
                    emptyTimer?.Cancel();
                    timer = emptyTimer = new CancellationTokenSource();
                }
            }

            if (timer != null)
            {
                _ = ShutdownWhenStillEmptyAsync(timer);
            }
        }

        /// <summary>
        /// Phòng đã trống: đợi một khoảng ngắn (phòng trường hợp mất kết nối thoáng qua)
        /// rồi Shutdown. Fleet tự tạo GameServer mới ở trạng thái Ready thay thế, nên pod không
        /// bao giờ ở trạng thái Ready mà vẫn giữ room của một phiên cũ.
        /// </summary>
        private async Task ShutdownWhenStillEmptyAsync(CancellationTokenSource timer)
        {
            Console.WriteLine($"[Agones] room empty, shutting down in {EmptyGrace.TotalSeconds:0}s unless a player returns");
            try
            {
                await Task.Delay(EmptyGrace, timer.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Agones] player returned, shutdown cancelled");
                return;
            }

            lock (stateLock)
            {
                if (timer.IsCancellationRequested || activePlayers.Count > 0)
                {
                    return;
                }
            }

            await ShutdownAsync();
            Environment.Exit(0);
        }

        public async Task ShutdownAsync()
        {
            lock (stateLock)
            {
                if (agones == null || shuttingDown)
                {
                    return;
                }

                shuttingDown = true;
                emptyTimer?.Cancel();
            }

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
