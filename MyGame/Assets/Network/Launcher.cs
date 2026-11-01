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

        [Header("Client")]
        public RoomJoiner joiner;

        private DirectoryPod pod;
        private int slot;

        // Lobby: đăng nhập (tên + mật khẩu) rồi chọn phòng từ danh sách backend trả về.
        // "auto" = Nakama tự cấp phòng trống từ pool arena:1..4 khi đủ 2 người.
        private string playerName = "ana";
        private string lobbyRoom = "";
        private string password = "";

        private void Start()
        {
            manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState += OnClientConnectionState;

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

        // --- API cho LobbyScreen (UI Toolkit) ---

        public bool ClientRunning => manager != null && manager.ClientManager.State != ConnectionState.Stopped;

        public bool ServerRunning => manager != null && manager.ServerManager.State != ServerState.Stopped;

        /// <summary>Phòng người chơi vừa chọn trong sảnh (rỗng nếu chưa chọn).</summary>
        public string CurrentRoom => lobbyRoom;

        public void SelectRoom(string room) => lobbyRoom = room;

        public void BeginHost() => StartHost();

        public void BeginPod() => StartPod(Environment.GetCommandLineArgs());

        /// <summary>Thoát phòng / dừng server; <see cref="OnClientConnectionState"/> đưa người chơi về sảnh.</summary>
        public void LeaveRoom()
        {
            manager.ClientManager.StopConnection();
            manager.ServerManager.StopConnection();
        }

        private void OnGUI()
        {
            // Đã có LobbyScreen (UI Toolkit) thì không vẽ IMGUI cũ nữa.
            if (LobbyScreen.Active)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10f, 10f, 460f, 460f));
            DrawControls();
            GUILayout.EndArea();
        }

        private void DrawControls()
        {
            bool serverRunning = manager.ServerManager.State != ServerState.Stopped;
            bool clientRunning = manager.ClientManager.State != ConnectionState.Stopped;

            if (serverRunning || clientRunning)
            {
                if (joiner != null && clientRunning)
                {
                    GUILayout.Label($"Người chơi: {playerName}   phòng: {lobbyRoom}");
                }

                GUILayout.Label($"server: {manager.ServerManager.State}  peers: {manager.ServerManager.PeerCount}  client: {manager.ClientManager.State}");
                if (pod != null)
                {
                    GUILayout.Label($"pod: {podName} id={pod.PodId} rooms={pod.Rooms.Count}");
                }

                GUILayout.Label("move: WASD / arrow keys");
                // Với client đã đăng nhập, dừng kết nối = thoát phòng; OnClientConnectionState đưa về sảnh.
                if (GUILayout.Button(joiner != null && joiner.LoggedIn && clientRunning && !serverRunning ? "Thoát phòng (về sảnh)" : "Stop"))
                {
                    manager.ClientManager.StopConnection();
                    manager.ServerManager.StopConnection();
                    if (joiner != null && !joiner.LoggedIn)
                    {
                        joiner.ClearStatus();
                    }
                }

                return;
            }

            if (GUILayout.Button("Host (local server + player)"))
            {
                StartHost();
            }

            DrawLobby();

            GUILayout.Space(8f);
            GUILayout.Label("Công cụ dev:");
            if (GUILayout.Button("Start pod (register with directory)"))
            {
                StartPod(Environment.GetCommandLineArgs());
            }
        }

        private void DrawLobby()
        {
            GUILayout.Space(8f);
            GUILayout.Label("=== Lobby ===");

            bool busy = joiner != null && joiner.Busy;
            GUI.enabled = !busy;

            if (joiner == null)
            {
                GUILayout.Label("Chưa gán RoomJoiner");
            }
            else if (!joiner.LoggedIn)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Tên:", GUILayout.Width(70f));
                playerName = GUILayout.TextField(playerName ?? "", 24);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mật khẩu:", GUILayout.Width(70f));
                password = GUILayout.PasswordField(password ?? "", '*', 64);
                GUILayout.EndHorizontal();

                GUILayout.Label("(Tên mới sẽ được tạo tài khoản với mật khẩu này. Tối thiểu 8 ký tự.)");
                if (GUILayout.Button("Đăng nhập / Đăng ký"))
                {
                    _ = joiner.Login(playerName, password);
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Xin chào, {joiner.LoggedInAs}");
                if (GUILayout.Button("Làm mới", GUILayout.Width(80f)))
                {
                    _ = joiner.RefreshRooms();
                }

                if (GUILayout.Button("Đăng xuất", GUILayout.Width(80f)))
                {
                    password = "";
                    joiner.Logout();
                }

                GUILayout.EndHorizontal();

                GUILayout.Label("Phòng (mỗi phòng cần 2 người để ghép trận):");
                foreach (RoomInfo room in joiner.Rooms)
                {
                    string name = room.RoomKey == "auto" ? "Tự động ghép trận" : room.RoomKey;
                    string stateText = room.State == "full" ? "đầy" : room.State == "waiting" ? "có người chờ" : "trống";
                    if (GUILayout.Button($"{name}   [{room.Players}/{room.Capacity}] {stateText}"))
                    {
                        lobbyRoom = room.RoomKey;
                        _ = joiner.JoinRoom(room.RoomKey);
                    }
                }

                if (joiner.Rooms.Count == 0)
                {
                    GUILayout.Label("(Chưa có dữ liệu phòng - bấm Làm mới)");
                }
            }

            GUI.enabled = true;

            if (joiner != null && !string.IsNullOrEmpty(joiner.Status))
            {
                Color previous = GUI.color;
                GUI.color = joiner.IsError ? new Color(1f, 0.4f, 0.4f) : Color.white;
                GUILayout.Label(joiner.Status);
                GUI.color = previous;
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

            if (args.State == ConnectionState.Started && joiner != null && joiner.LoggedIn)
            {
                joiner.SetNotice($"Đã vào phòng {lobbyRoom}");
            }

            // Rời phòng (hoặc bị ngắt): quay về sảnh và tải lại danh sách phòng.
            if (args.State == ConnectionState.Stopped && joiner != null && joiner.LoggedIn)
            {
                joiner.LeftRoom();
                joiner.SetNotice(args.Reason == StopReason.None || args.Reason == StopReason.LocalStop ? "Đã rời phòng" : $"Mất kết nối phòng: {args.Reason}");
                lobbyRoom = "";
                _ = joiner.RefreshRooms();
            }
        }
    }
}
