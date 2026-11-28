using System;
using System.Collections.Generic;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class NetworkScenes
    {
        private readonly ServerManager server;

        internal NetworkScenes(ServerManager server)
        {
            this.server = server;
        }

        public event Action<NetworkScene> OnLoaded;

        public event Action<NetworkScene, Exception> OnLoadFailed;

        public event Action<NetworkScene> OnUnloaded;

        public event Action<ulong, NetworkScene> OnPeerLoaded
        {
            add => Tier.OnPeerLoaded += value;
            remove => Tier.OnPeerLoaded -= value;
        }

        public IReadOnlyList<NetworkScene> Global => Tier.Global;

        internal ServerManager Server => server;

        private ServerScenes Tier => server.Objects.Scenes;

        public bool Contains(NetworkScene scene) => Tier.Contains(scene);

        public bool IsGlobal(NetworkScene scene) => Tier.IsGlobal(scene);

        public bool HasLoaded(ulong peerId, NetworkScene scene) => Tier.HasLoaded(peerId, scene);

        public IReadOnlyCollection<ulong> PeersOf(NetworkScene scene) => Tier.PeersOf(scene);

        public IReadOnlyList<NetworkScene> ScenesOf(ulong peerId) => Tier.ScenesOf(peerId);

        public IReadOnlyList<NetworkScene> InstancesOf(uint sceneId) => Tier.InstancesOf(sceneId);

        public uint SceneIdOf(NetworkScene scene) => Tier.SceneIdOf(scene);

        public IReadOnlyList<NetworkScene> LoadGlobal(IReadOnlyList<uint> sceneIds, bool replace = false, ScenePhysics physics = ScenePhysics.Shared)
        {
            EnsureRegistered(sceneIds);
            return Tier.LoadGlobal(sceneIds, replace, physics);
        }

        public void UnloadGlobal(IReadOnlyList<NetworkScene> scenes) => Tier.UnloadGlobal(scenes);

        public NetworkScene Load(uint sceneId, IReadOnlyList<ulong> peerIds, ScenePhysics physics = ScenePhysics.Shared)
        {
            EnsureRegistered(sceneId);
            return Tier.Load(sceneId, peerIds, physics);
        }

        public int AddPeers(NetworkScene scene, IReadOnlyList<ulong> peerIds) => Tier.AddPeers(scene, peerIds);

        public int RemovePeers(NetworkScene scene, IReadOnlyList<ulong> peerIds) => Tier.RemovePeers(scene, peerIds);

        public void Unload(NetworkScene scene) => Tier.Unload(scene);

        internal void RaiseLoaded(NetworkScene scene) => OnLoaded?.Invoke(scene);

        internal void RaiseLoadFailed(NetworkScene scene, Exception exception) => OnLoadFailed?.Invoke(scene, exception);

        internal void RaiseUnloaded(NetworkScene scene) => OnUnloaded?.Invoke(scene);

        private void EnsureRegistered(IReadOnlyList<uint> sceneIds)
        {
            if (sceneIds == null)
            {
                throw new ArgumentNullException(nameof(sceneIds));
            }

            foreach (uint sceneId in sceneIds)
            {
                EnsureRegistered(sceneId);
            }
        }

        private void EnsureRegistered(uint sceneId)
        {
            if (sceneId != 0 && !server.SceneHost.Knows(sceneId))
            {
                throw new ArgumentException($"scene 0x{sceneId:X8} is not in the scene registry", nameof(sceneId));
            }
        }
    }
}
