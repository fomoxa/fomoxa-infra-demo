using System;
using System.IO;
using System.Threading.Tasks;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Rpc.Directory;
using Fomoxa.Unity;
using MyGame.Backend.Models;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class Launcher : MonoBehaviour
    {
        public NetworkManager manager;
        public NetworkObject playerPrefab;
        public ushort port = 7777;

        [Header("Pod")]
        public EncryptedUdpNetworkTransport encryptedTransport;
        public ushort podPort = 7778;
        public string directoryAddress = "127.0.0.1";
        public ushort directoryPort = 9500;
        public string podAddress = "127.0.0.1";
        public string podName = "pod-1";
        public string region = "local";
        public int maxPlayers = 16;
        public string roomKey = "arena:1";
        public bool behindGateway = true;

        private DirectoryPod pod;
        private int slot;

        private void Start()
        {
            manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState += OnClientConnectionState;

            // Vào từ sảnh: sảnh đã lấy ticket, scene này chỉ việc nối vào pod.
            if (MatchSession.TryTake(out RoomTicket ticket))
            {
                Debug.Log($"[Match] {MatchSession.Room} -> {ticket.Address}:{ticket.Port}");
                manager.ClientManager.StartConnection(ticket.Address, ticket.Port, ticket.Connection);
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-pod") >= 0)
            {
                StartPod(args);
                return;
            }

            if (Array.IndexOf(args, "-host") >= 0)
            {
                StartHost();
            }

            if (Array.IndexOf(args, "-autojoin") < 0 && Array.IndexOf(args, "-client") >= 0)
            {
                Invoke(nameof(ConnectClient), 1f);
            }
        }

        private void OnDestroy()
        {
            if (manager == null || manager.ServerManager == null)
            {
                return;
            }

            manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            pod?.Dispose();
        }

        // --- API cho MatchHudScreen (UI Toolkit) ---

        public bool ClientRunning => manager != null && manager.ClientManager.State != ConnectionState.Stopped;

        public bool ServerRunning => manager != null && manager.ServerManager.State != ServerState.Stopped;

        /// <summary>Phòng đang chơi (do sảnh chọn); rỗng nếu chạy Host / pod trực tiếp.</summary>
        public string CurrentRoom => MatchSession.Room;

        public bool PodRunning => pod != null;

        public string PodSummary => pod == null ? "" : $"pod: {podName} id={pod.PodId} rooms={pod.Rooms.Count}";

        public string ConnectionSummary =>
            $"server: {manager.ServerManager.State}  peers: {manager.ServerManager.PeerCount}  client: {manager.ClientManager.State}";

        public void BeginHost() => StartHost();

        public void BeginPod() => StartPod(Environment.GetCommandLineArgs());

        /// <summary>Thoát phòng / dừng server. Vào từ sảnh thì <see cref="OnClientConnectionState"/> đưa về scene sảnh.</summary>
        public void LeaveRoom()
        {
            manager.ClientManager.StopConnection();
            manager.ServerManager.StopConnection();
            if (!MatchSession.FromLobby)
            {
                return;
            }

            // Nếu client chưa kịp chạy (Stopped ngay) thì không có sự kiện Stopped -> tự về sảnh.
            if (!ClientRunning)
            {
                MatchSession.ReturnToLobby("Đã rời phòng");
            }
        }

        private void ConnectClient()
        {
            manager.ClientManager.StartConnection("127.0.0.1", port);
        }

        private void StartHost()
        {
            if (encryptedTransport != null)
            {
                encryptedTransport.BehindGateway = false;
            }

            if (!EnsureTokens("host"))
            {
                return;
            }

            manager.ServerManager.StartConnection(port);
            manager.ClientManager.StartConnection("127.0.0.1", manager.ServerManager.Port);
            Debug.Log($"host listening on port {manager.ServerManager.Port}");
        }

        private bool EnsureTokens(string target)
        {
            if (encryptedTransport == null)
            {
                return true;
            }

            if (encryptedTransport.Tokens == null)
            {
                var key = new byte[ConnectTokens.KeyLength];
                new System.Random().NextBytes(key);
                encryptedTransport.Tokens = new ConnectTokens(FomoxaRegistry.Default, key);
                encryptedTransport.Target = target;
            }

            return true;
        }

        private void StartPod(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("-podport:", StringComparison.Ordinal))
                {
                    podPort = ushort.Parse(arg.Substring("-podport:".Length));
                }
                else if (arg.StartsWith("-podname:", StringComparison.Ordinal))
                {
                    podName = arg.Substring("-podname:".Length);
                }
                else if (arg.StartsWith("-room:", StringComparison.Ordinal))
                {
                    roomKey = arg.Substring("-room:".Length);
                }
                else if (arg.StartsWith("-directory:", StringComparison.Ordinal))
                {
                    string[] parts = arg.Substring("-directory:".Length).Split(':');
                    directoryAddress = parts[0];
                    directoryPort = ushort.Parse(parts[1]);
                }
            }

            string keyFile = Environment.GetEnvironmentVariable("POD_KEY_FILE");
            if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile))
            {
                Debug.LogError("pod: POD_KEY_FILE is not set or does not exist");
                return;
            }

            if (encryptedTransport != null)
            {
                encryptedTransport.BehindGateway = behindGateway;
            }

            pod = new DirectoryPod(manager.ServerManager, manager.TimeManager, new DirectoryPodSettings
            {
                DirectoryAddress = directoryAddress,
                DirectoryPort = directoryPort,
                Key = File.ReadAllBytes(keyFile),
                Address = podAddress,
                Port = podPort,
                Target = podName,
                Version = Application.version,
                Region = region,
                MaxPlayers = (uint)maxPlayers,
            });

            pod.OnRegistered += OnPodRegistered;
            pod.OnError += error => Debug.LogError("[Pod] " + error);
            pod.OnDrain += deadline => Debug.Log($"[Pod] draining until {deadline:O}");
            pod.OnDrainDeadline += () => Debug.Log("[Pod] drain deadline reached");
            pod.Start();

            Debug.Log($"[Pod] registering {podName} ({podAddress}:{podPort}, version {Application.version}) at {directoryAddress}:{directoryPort}");
        }

        private void OnPodRegistered(DirectoryRegistration registration)
        {
            Debug.Log($"[Pod] registered id={registration.PodId} changed={registration.ChangedPodId}");

            encryptedTransport.Tokens = new ConnectTokens(FomoxaRegistry.Default, registration.TicketKey);
            encryptedTransport.Target = podName;
            manager.ServerManager.StartConnection(podPort);
            _ = AcquireRoom();
        }

        private async Task AcquireRoom()
        {
            try
            {
                await pod.AcquireRoom(roomKey);
                Debug.Log($"[Pod] holds {roomKey}");
            }
            catch (Exception error)
            {
                Debug.LogError($"[Pod] acquire {roomKey}: {error.Message}");
            }
        }

        private void OnRemoteConnectionState(ConnectionStateArgs args)
        {
            Debug.Log($"peer {args.PeerId}: {args.State} {args.Reason}");
            if (args.State != ConnectionState.Started)
            {
                return;
            }

            Vector3 position = new Vector3(0f, 1f, 0f) + SpawnOffset();
            NetworkObject player = Instantiate(playerPrefab, position, Quaternion.identity);
            manager.ServerManager.Spawn(player, args.PeerId);
        }

        private Vector3 SpawnOffset()
        {
            slot = (slot + 1) % 8;
            float angle = slot * Mathf.PI * 2f / 8f;
            return new Vector3(Mathf.Sin(angle) * 3f, 0f, Mathf.Cos(angle) * 3f);
        }

        private void OnClientConnectionState(ConnectionStateArgs args)
        {
            Debug.Log($"client: {args.State} {args.Reason}");

            RoomJoiner joiner = RoomJoiner.Instance;
            if (!MatchSession.FromLobby || joiner == null)
            {
                return;
            }

            if (args.State == ConnectionState.Started)
            {
                joiner.SetNotice($"Đã vào phòng {MatchSession.Room}");
                return;
            }

            // Rời phòng hoặc bị ngắt: về scene sảnh (RoomJoiner giữ nguyên phiên đăng nhập).
            if (args.State == ConnectionState.Stopped)
            {
                MatchSession.ReturnToLobby(
                    args.Reason == StopReason.None || args.Reason == StopReason.LocalStop
                        ? "Đã rời phòng"
                        : $"Mất kết nối phòng: {args.Reason}");
            }
        }
    }
}
