using System;
using System.Threading.Tasks;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Fomoxa.Unity;
using Generated;
using MyGame.Backend;
using MyGame.Backend.Models;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class RoomJoiner : MonoBehaviour
    {
        public NetworkManager manager;
        public NetworkTransport backendTransport;
        public string backendAddress = "127.0.0.1";
        public ushort backendPort = 9600;

        private ITransportConnector connector;
        private TaskCompletionSource<ITransport> connecting;
        private RpcConnection connection;

        /// <summary>Trạng thái hiện tại để lobby (Launcher) hiển thị.</summary>
        public string Status { get; private set; } = "";
        public bool IsError { get; private set; }
        public bool Busy { get; private set; }

        public void ClearStatus()
        {
            if (!Busy)
            {
                SetStatus("");
            }
        }

        private void SetStatus(string text, bool error = false)
        {
            Status = text;
            IsError = error;
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-autojoin") >= 0)
            {
                string room = "arena:1";
                foreach (string arg in args)
                {
                    if (arg.StartsWith("-room:", StringComparison.Ordinal))
                    {
                        room = arg.Substring("-room:".Length);
                    }
                }

                _ = Join(PlayerName(args), room);
            }
        }

        private static string PlayerName(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-name:", StringComparison.Ordinal))
                {
                    return arg.Substring("-name:".Length);
                }
            }

            return "player";
        }

        private static string DeviceId(string[] args, string player)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-device:", StringComparison.Ordinal))
                {
                    return arg.Substring("-device:".Length);
                }
            }

            return $"fomoxa-device-{player}";
        }

        public async Task Join(string player, string roomKey)
        {
            if (Busy)
            {
                return;
            }

            player = (player ?? "").Trim();
            if (player.Length == 0)
            {
                SetStatus("Hãy nhập tên người chơi", true);
                return;
            }

            Busy = true;
            try
            {
                // Bỏ kết nối cũ (nếu người chơi vào lại phòng khác).
                connection?.Dispose();
                connection = null;

                connecting = new TaskCompletionSource<ITransport>();
                SetStatus($"Đang kết nối backend {backendAddress}:{backendPort}...");
                Debug.Log($"[Join] connecting to {backendAddress}:{backendPort}");
                connector = backendTransport.CreateFactory().CreateConnector(backendAddress, backendPort);
                ITransport transport = await connecting.Task;

                string[] args = Environment.GetCommandLineArgs();
                var options = new ConnectOptions
                {
                    Credential = Credential.Of(PlayersMethods.Login, new PlayerLogin { Player = player, DeviceId = DeviceId(args, player) }),
                };

                SetStatus($"Đang đăng nhập '{player}'...");
                connection = RpcConnection.Driven(transport, NetSchema.Build(), LobbyClient.Declare(PlayersClient.Registry()).Build(), options);
                await connection.Opened;
                Debug.Log($"[Join] logged in as {player}");

                SetStatus(roomKey == "auto"
                    ? "Đã đăng nhập. Đang ghép trận (chờ người chơi khác, tối đa 30 giây)..."
                    : $"Đã đăng nhập. Đang vào phòng {roomKey} (cần 2 người cùng phòng)...");

                RoomTicket ticket = await JoinWithRetry(new LobbyClient(connection), new JoinRoomRequest
                {
                    RoomKey = roomKey,
                    Version = Application.version,
                });
                Debug.Log($"[Join] room {roomKey} -> {ticket.Address}:{ticket.Port}, grant {ticket.Connection.Length} bytes");
                SetStatus($"Đã nhận ticket phòng {roomKey}. Đang kết nối game server {ticket.Address}:{ticket.Port}...");
                manager.ClientManager.StartConnection(ticket.Address, ticket.Port, ticket.Connection);
            }
            catch (RpcException failed) when (failed.Status.Details is NoRoomForVersion missing)
            {
                Debug.LogWarning($"[Join] no pod runs version {missing.Version}");
                SetStatus($"Không có game server chạy phiên bản {missing.Version}", true);
            }
            catch (RpcException failed)
            {
                Debug.LogError($"[Join] failed: {failed.Status}");
                SetStatus($"Vào phòng thất bại: {failed.Status.Message}", true);
            }
            catch (Exception error)
            {
                Debug.LogError($"[Join] error: {error.Message}");
                SetStatus($"Lỗi: {error.Message}", true);
            }
            finally
            {
                Busy = false;
            }
        }

        private static readonly TimeSpan MatchBudget = TimeSpan.FromSeconds(30);

        private static async Task<RoomTicket> JoinWithRetry(LobbyClient lobby, JoinRoomRequest request)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await lobby.JoinRoomAsync(request, CallOptions.Within(MatchBudget));
                }
                catch (RpcException failed) when (failed.Status.Code == StatusCode.Unavailable && attempt < 3)
                {
                    Debug.Log($"[Join] {failed.Status.Message} (thử lại lần {attempt})");
                }
            }
        }

        private void Update()
        {
            if (connector != null)
            {
                ConnectStatus status = connector.Poll(out ITransport transport);
                if (status == ConnectStatus.Connected)
                {
                    connector.Dispose();
                    connector = null;
                    connecting.SetResult(transport);
                }
                else if (status != ConnectStatus.Pending)
                {
                    connector.Dispose();
                    connector = null;
                    connecting.SetException(new InvalidOperationException($"cannot reach the backend: {status}"));
                }
            }

            connection?.Tick(MonotonicClock.Now);
        }

        private void OnDestroy()
        {
            connector?.Dispose();
            connection?.Dispose();
        }
    }
}
