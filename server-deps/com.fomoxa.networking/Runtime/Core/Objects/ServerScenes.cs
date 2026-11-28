using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    public sealed class ServerScenes
    {
        private readonly ServerObjects objects;
        private readonly ServerSession session;
        private readonly SceneProtocol protocol;
        private readonly Dictionary<uint, Instance> instances = new Dictionary<uint, Instance>();
        private readonly List<NetworkScene> global = new List<NetworkScene>();
        private readonly Dictionary<ulong, HashSet<uint>> loadedByPeer = new Dictionary<ulong, HashSet<uint>>();
        private readonly Dictionary<ulong, List<uint>> outgoing = new Dictionary<ulong, List<uint>>();
        private readonly List<ulong> startedPeers = new List<ulong>();
        private readonly List<ulong> changedPeers = new List<ulong>();
        private readonly SceneLoad load = new SceneLoad();
        private readonly SceneUnload unload = new SceneUnload();
        private SceneLoaded loaded = new SceneLoaded();
        private uint nextId = 1;

        internal ServerScenes(ServerObjects objects, ServerSession session, SceneProtocol protocol)
        {
            this.objects = objects;
            this.session = session;
            this.protocol = protocol;
            session.Dispatcher.Register(protocol.LoadedCodec.MessageId, OnLoaded);
            session.OnServerConnectionState += OnServerState;
            session.OnRemoteConnectionState += OnPeerState;
        }

        public event Action<NetworkScene> OnSceneAdded;

        public event Action<NetworkScene> OnSceneRemoved;

        public event Action<ulong, NetworkScene> OnPeerLoaded;

        public IReadOnlyList<NetworkScene> Global => global;

        public bool Contains(NetworkScene scene) => instances.ContainsKey(scene.Id);

        public bool IsGlobal(NetworkScene scene) => instances.TryGetValue(scene.Id, out Instance instance) && instance.Global;

        public bool HasLoaded(ulong peerId, NetworkScene scene) =>
            loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes) && scenes.Contains(scene.Id);

        public bool IsAssigned(ulong peerId, NetworkScene scene) =>
            instances.TryGetValue(scene.Id, out Instance instance) && (instance.Global || instance.Peers.Contains(peerId));

        public uint SceneIdOf(NetworkScene scene) => instances.TryGetValue(scene.Id, out Instance instance) ? instance.SceneId : 0;

        internal ScenePhysics PhysicsOf(NetworkScene scene) => instances.TryGetValue(scene.Id, out Instance instance) ? instance.Physics : ScenePhysics.Shared;

        public IReadOnlyCollection<ulong> PeersOf(NetworkScene scene)
        {
            if (!instances.TryGetValue(scene.Id, out Instance instance))
            {
                return new List<ulong>();
            }

            if (!instance.Global)
            {
                return new List<ulong>(instance.Peers);
            }

            var started = new List<ulong>();
            session.CopyStartedPeerIds(started);
            return started;
        }

        public IReadOnlyList<NetworkScene> ScenesOf(ulong peerId)
        {
            var assigned = new List<NetworkScene>(global);
            foreach (KeyValuePair<uint, Instance> entry in instances)
            {
                if (!entry.Value.Global && entry.Value.Peers.Contains(peerId))
                {
                    assigned.Add(new NetworkScene(entry.Key));
                }
            }

            return assigned;
        }

        public IReadOnlyList<NetworkScene> InstancesOf(uint sceneId)
        {
            var found = new List<NetworkScene>();
            foreach (KeyValuePair<uint, Instance> entry in instances)
            {
                if (entry.Value.SceneId == sceneId)
                {
                    found.Add(new NetworkScene(entry.Key));
                }
            }

            return found;
        }

        public IReadOnlyList<NetworkScene> LoadGlobal(IReadOnlyList<uint> sceneIds, bool replace = false, ScenePhysics physics = ScenePhysics.Shared)
        {
            EnsureStarted();
            EnsureIds(sceneIds);
            if (replace && global.Count > 0)
            {
                UnloadGlobal(new List<NetworkScene>(global));
            }

            foreach (uint sceneId in sceneIds)
            {
                if (GlobalOf(sceneId) == default)
                {
                    EnsurePhysics(sceneId, physics);
                }
            }

            CollectStartedPeers();
            var scenes = new List<NetworkScene>(sceneIds.Count);
            foreach (uint sceneId in sceneIds)
            {
                NetworkScene scene = GlobalOf(sceneId);
                if (scene == default)
                {
                    scene = Add(sceneId, true, physics);
                    global.Add(scene);
                    OnSceneAdded?.Invoke(scene);
                    foreach (ulong peerId in startedPeers)
                    {
                        Queue(peerId, scene.Id);
                    }
                }

                scenes.Add(scene);
            }

            SendLoads();
            return scenes;
        }

        public void UnloadGlobal(IReadOnlyList<NetworkScene> scenes)
        {
            EnsureStarted();
            if (scenes == null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            foreach (NetworkScene scene in scenes)
            {
                if (!IsGlobal(scene))
                {
                    throw new ArgumentException($"{scene} is not a global scene", nameof(scenes));
                }
            }

            CollectStartedPeers();
            foreach (NetworkScene scene in scenes)
            {
                if (!global.Remove(scene))
                {
                    continue;
                }

                instances.Remove(scene.Id);
                objects.DespawnScene(scene);
                foreach (ulong peerId in startedPeers)
                {
                    Forget(peerId, scene.Id);
                    Queue(peerId, scene.Id);
                }

                OnSceneRemoved?.Invoke(scene);
            }

            SendUnloads();
        }

        public NetworkScene Load(uint sceneId, IReadOnlyList<ulong> peerIds, ScenePhysics physics = ScenePhysics.Shared)
        {
            EnsureStarted();
            EnsureId(sceneId);
            if (peerIds == null)
            {
                throw new ArgumentNullException(nameof(peerIds));
            }

            EnsurePhysics(sceneId, physics);
            NetworkScene scene = Add(sceneId, false, physics);
            OnSceneAdded?.Invoke(scene);
            AddStartedPeers(instances[scene.Id], scene, peerIds);
            SendLoads();
            return scene;
        }

        public int AddPeers(NetworkScene scene, IReadOnlyList<ulong> peerIds)
        {
            EnsureStarted();
            Instance instance = PerPeerInstance(scene);
            if (peerIds == null)
            {
                throw new ArgumentNullException(nameof(peerIds));
            }

            int added = AddStartedPeers(instance, scene, peerIds);
            SendLoads();
            return added;
        }

        public int RemovePeers(NetworkScene scene, IReadOnlyList<ulong> peerIds)
        {
            EnsureStarted();
            Instance instance = PerPeerInstance(scene);
            if (peerIds == null)
            {
                throw new ArgumentNullException(nameof(peerIds));
            }

            changedPeers.Clear();
            foreach (ulong peerId in peerIds)
            {
                if (instance.Peers.Remove(peerId))
                {
                    Forget(peerId, scene.Id);
                    changedPeers.Add(peerId);
                }
            }

            foreach (ulong peerId in changedPeers)
            {
                objects.RebuildObserversOfPeer(peerId);
                Queue(peerId, scene.Id);
            }

            SendUnloads();
            return changedPeers.Count;
        }

        public void Unload(NetworkScene scene)
        {
            EnsureStarted();
            Instance instance = PerPeerInstance(scene);
            objects.DespawnScene(scene);
            instances.Remove(scene.Id);
            foreach (ulong peerId in instance.Peers)
            {
                Forget(peerId, scene.Id);
                Queue(peerId, scene.Id);
            }

            SendUnloads();
            OnSceneRemoved?.Invoke(scene);
        }

        internal void StartPeer(ulong peerId)
        {
            loadedByPeer[peerId] = new HashSet<uint>();
            load.Full = true;
            load.Scenes.Clear();
            foreach (NetworkScene scene in global)
            {
                load.Scenes.Add(RefOf(scene.Id));
            }

            session.EnqueueObjectMessage(peerId, protocol.LoadCodec.MessageId, protocol.LoadCodec.Encode(load).Span);
        }

        private int AddStartedPeers(Instance instance, NetworkScene scene, IReadOnlyList<ulong> peerIds)
        {
            int added = 0;
            foreach (ulong peerId in peerIds)
            {
                if (session.PeerState(peerId) == ConnectionState.Started && instance.Peers.Add(peerId))
                {
                    Queue(peerId, scene.Id);
                    added++;
                }
            }

            return added;
        }

        private NetworkScene Add(uint sceneId, bool isGlobal, ScenePhysics physics)
        {
            if (nextId == 0)
            {
                throw new InvalidOperationException("the server ran out of scene instance ids");
            }

            var scene = new NetworkScene(nextId);
            nextId++;
            instances.Add(scene.Id, new Instance(sceneId, isGlobal, physics));
            return scene;
        }

        private NetworkScene GlobalOf(uint sceneId)
        {
            foreach (NetworkScene scene in global)
            {
                if (instances[scene.Id].SceneId == sceneId)
                {
                    return scene;
                }
            }

            return default;
        }

        private void EnsurePhysics(uint sceneId, ScenePhysics physics)
        {
            if (physics != ScenePhysics.Shared)
            {
                return;
            }

            foreach (Instance instance in instances.Values)
            {
                if (instance.SceneId == sceneId)
                {
                    throw new ArgumentException($"scene 0x{sceneId:X8} already has an instance, so another one needs ScenePhysics.Own", nameof(physics));
                }
            }
        }

        private void OnLoaded(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            protocol.LoadedCodec.Decode(payload, ref loaded);
            if (!loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes))
            {
                return;
            }

            var added = new List<NetworkScene>();
            foreach (uint id in loaded.Scenes)
            {
                var scene = new NetworkScene(id);
                if (IsAssigned(peerId, scene) && scenes.Add(id))
                {
                    added.Add(scene);
                }
            }

            if (added.Count == 0)
            {
                return;
            }

            objects.RebuildObserversOfPeer(peerId);
            foreach (NetworkScene scene in added)
            {
                OnPeerLoaded?.Invoke(peerId, scene);
            }
        }

        private Instance PerPeerInstance(NetworkScene scene)
        {
            if (!instances.TryGetValue(scene.Id, out Instance instance))
            {
                throw new ArgumentException($"{scene} is not loaded", nameof(scene));
            }

            if (instance.Global)
            {
                throw new ArgumentException($"{scene} is a global scene", nameof(scene));
            }

            return instance;
        }

        private SceneRef RefOf(uint id)
        {
            Instance instance = instances[id];
            return new SceneRef { Scene = id, SceneId = instance.SceneId, Physics = (byte)instance.Physics };
        }

        private void Forget(ulong peerId, uint id)
        {
            if (loadedByPeer.TryGetValue(peerId, out HashSet<uint> scenes))
            {
                scenes.Remove(id);
            }
        }

        private void Queue(ulong peerId, uint id)
        {
            if (!outgoing.TryGetValue(peerId, out List<uint> scenes))
            {
                scenes = new List<uint>();
                outgoing.Add(peerId, scenes);
            }

            scenes.Add(id);
        }

        private void SendLoads()
        {
            load.Full = false;
            foreach (KeyValuePair<ulong, List<uint>> pending in outgoing)
            {
                if (pending.Value.Count == 0)
                {
                    continue;
                }

                load.Scenes.Clear();
                foreach (uint id in pending.Value)
                {
                    load.Scenes.Add(RefOf(id));
                }

                session.EnqueueObjectMessage(pending.Key, protocol.LoadCodec.MessageId, protocol.LoadCodec.Encode(load).Span);
                pending.Value.Clear();
            }
        }

        private void SendUnloads()
        {
            foreach (KeyValuePair<ulong, List<uint>> pending in outgoing)
            {
                if (pending.Value.Count == 0)
                {
                    continue;
                }

                unload.Scenes.Clear();
                unload.Scenes.AddRange(pending.Value);
                session.EnqueueObjectMessage(pending.Key, protocol.UnloadCodec.MessageId, protocol.UnloadCodec.Encode(unload).Span);
                pending.Value.Clear();
            }
        }

        private void CollectStartedPeers()
        {
            startedPeers.Clear();
            session.CopyStartedPeerIds(startedPeers);
        }

        private void EnsureStarted()
        {
            objects.EnsureNotDeciding();
            if (session.State != ServerState.Started)
            {
                throw new InvalidOperationException("the server session is not started");
            }
        }

        private static void EnsureIds(IReadOnlyList<uint> sceneIds)
        {
            if (sceneIds == null)
            {
                throw new ArgumentNullException(nameof(sceneIds));
            }

            foreach (uint sceneId in sceneIds)
            {
                EnsureId(sceneId);
            }
        }

        private static void EnsureId(uint sceneId)
        {
            if (sceneId == 0)
            {
                throw new ArgumentException("scene id 0 means no network scene", nameof(sceneId));
            }
        }

        private void OnPeerState(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped)
            {
                return;
            }

            loadedByPeer.Remove(args.PeerId);
            outgoing.Remove(args.PeerId);
            foreach (Instance instance in instances.Values)
            {
                instance.Peers.Remove(args.PeerId);
            }
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.State == ServerState.Stopped)
            {
                instances.Clear();
                global.Clear();
                loadedByPeer.Clear();
                outgoing.Clear();
                nextId = 1;
            }
        }

        private sealed class Instance
        {
            public Instance(uint sceneId, bool isGlobal, ScenePhysics physics)
            {
                SceneId = sceneId;
                Global = isGlobal;
                Physics = physics;
            }

            public uint SceneId { get; }

            public bool Global { get; }

            public ScenePhysics Physics { get; }

            public HashSet<ulong> Peers { get; } = new HashSet<ulong>();
        }
    }
}
