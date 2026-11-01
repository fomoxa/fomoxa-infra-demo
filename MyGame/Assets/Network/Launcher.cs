using System;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class Launcher : MonoBehaviour
    {
        public NetworkManager manager;
        public NetworkObject playerPrefab;
        public ushort port = 7777;

        private string address = "127.0.0.1";
        private int slot;

        private void Start()
        {
            manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            manager.ClientManager.OnClientConnectionState += OnClientConnectionState;

            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-host") >= 0)
            {
                StartServer();
                manager.ClientManager.StartConnection(address, manager.ServerManager.Port);
                return;
            }

            if (Array.IndexOf(args, "-server") >= 0)
            {
                StartServer();
            }

            if (Array.IndexOf(args, "-client") >= 0)
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
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 10f, 360f, 170f));
            DrawControls();
            GUILayout.EndArea();
        }

        private void DrawControls()
        {
            bool serverRunning = manager.ServerManager.State != ServerState.Stopped;
            bool clientRunning = manager.ClientManager.State != ConnectionState.Stopped;

            if (serverRunning || clientRunning)
            {
                GUILayout.Label($"server: {manager.ServerManager.State}  peers: {manager.ServerManager.PeerCount}  client: {manager.ClientManager.State}");
                GUILayout.Label($"move: arrow keys / WASD");
                if (GUILayout.Button("Stop"))
                {
                    manager.ClientManager.StopConnection();
                    manager.ServerManager.StopConnection();
                }

                return;
            }

            address = GUILayout.TextField(address);
            if (GUILayout.Button("Host (server + local player)"))
            {
                StartServer();
                manager.ClientManager.StartConnection(address, manager.ServerManager.Port);
            }

            if (GUILayout.Button("Server only"))
            {
                StartServer();
            }

            if (GUILayout.Button("Client"))
            {
                ConnectClient();
            }
        }

        private void ConnectClient()
        {
            manager.ClientManager.StartConnection(address, port);
        }

        private void StartServer()
        {
            manager.ServerManager.StartConnection(port);
            Debug.Log($"server listening on port {manager.ServerManager.Port}");
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
        }
    }
}
