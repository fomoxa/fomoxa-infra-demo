using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Objects
{
    public sealed class ClientObjects
    {
        private readonly ClientSession session;
        private readonly ObjectProtocol protocol;
        private readonly IObjectSpawner spawner;
        private readonly ObjectTable table = new ObjectTable();
        private readonly HashSet<(NetworkScene, ulong)> sceneObjectIds = new HashSet<(NetworkScene, ulong)>();
        private readonly List<uint> clearing = new List<uint>();
        private LocalPeer localPeer = new LocalPeer();
        private ObjectSpawn spawn = new ObjectSpawn();
        private ObjectSceneSpawn sceneSpawn = new ObjectSceneSpawn();
        private ObjectDespawn despawn = new ObjectDespawn();
        private ObjectOwnerChange ownerChange = new ObjectOwnerChange();
        private ObjectSceneMove sceneMove = new ObjectSceneMove();
        private readonly SceneProtocol sceneProtocol;
        private readonly ISceneHost sceneHost;
        private readonly Dictionary<NetworkScene, SceneRef> loadedScenes = new Dictionary<NetworkScene, SceneRef>();
        private readonly List<SceneStep> sceneSteps = new List<SceneStep>();
        private readonly SceneLoaded sceneLoaded = new SceneLoaded();
        private SceneLoad sceneLoad = new SceneLoad();
        private SceneUnload sceneUnload = new SceneUnload();
        private bool sceneBusy;
        private SceneStep sceneRunning;

        public ClientObjects(ClientSession session, ObjectProtocol protocol, IObjectSpawner spawner)
            : this(session, protocol, spawner, null, null)
        {
        }

        public ClientObjects(ClientSession session, ObjectProtocol protocol, IObjectSpawner spawner, SceneProtocol sceneProtocol, ISceneHost sceneHost)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            if ((sceneProtocol == null) != (sceneHost == null))
            {
                throw new ArgumentException("a scene protocol needs a scene host, and a scene host needs a scene protocol", nameof(sceneHost));
            }

            this.sceneProtocol = sceneProtocol;
            this.sceneHost = sceneHost;
            MessageDispatcher dispatcher = session.Dispatcher;
            dispatcher.Register(protocol.LocalPeerCodec.MessageId, OnLocalPeer);
            session.SetAcceptance(protocol.LocalPeerCodec.MessageId);
            dispatcher.Register(protocol.SpawnCodec.MessageId, OnSpawn);
            dispatcher.Register(protocol.SceneSpawnCodec.MessageId, OnSceneSpawn);
            dispatcher.Register(protocol.DespawnCodec.MessageId, OnDespawn);
            dispatcher.Register(protocol.OwnerChangeCodec.MessageId, OnOwnerChange);
            dispatcher.Register(protocol.SceneMoveCodec.MessageId, OnSceneMove);
            if (sceneProtocol != null)
            {
                dispatcher.Register(sceneProtocol.LoadCodec.MessageId, OnSceneLoad);
                dispatcher.Register(sceneProtocol.UnloadCodec.MessageId, OnSceneUnload);
            }

            session.SetStoppedHandler(Clear);
        }

        public event Action<ObjectMismatchArgs> OnObjectMismatch;

        public event Action<ObjectOwnerChangedArgs> OnOwnerChanged;

        public event Action<ObjectSceneMovedArgs> OnSceneMoved;

        public event Action<ulong> OnLocalPeerAssigned;

        public event Action<NetworkScene, uint> OnSceneLoadStarted;

        public event Action<NetworkScene, float> OnSceneLoadProgress;

        public event Action<NetworkScene> OnSceneLoaded;

        public event Action<NetworkScene> OnSceneUnloaded;

        public ulong LocalPeerId { get; private set; }

        public ushort ServerTickRate { get; private set; }

        public PhysicsBackend PhysicsBackend { get; set; } = PhysicsBackend.Rigidbody;

        public int Count => table.Count;

        public bool TryGet(uint objectId, out ObjectRow row) => table.TryGet(objectId, out row);

        public IReadOnlyCollection<NetworkScene> LoadedScenes => loadedScenes.Keys;

        public uint SceneIdOf(NetworkScene scene)
        {
            if (loadedScenes.TryGetValue(scene, out SceneRef loaded))
            {
                return loaded.SceneId;
            }

            return sceneBusy && sceneRunning.Load && sceneRunning.Scene == scene ? sceneRunning.SceneId : 0;
        }

        public bool IsOwner(uint objectId) =>
            LocalPeerId != 0 && table.TryGet(objectId, out ObjectRow row) && row.OwnerId == LocalPeerId;

        public void ForgetLoadedScenes()
        {
            loadedScenes.Clear();
        }

        private void OnLocalPeer(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.LocalPeerCodec, payload, ref localPeer))
            {
                return;
            }

            if (localPeer.TickRate == 0 || !Enum.IsDefined(typeof(PhysicsBackend), localPeer.PhysicsBackend))
            {
                Mismatch(ObjectMismatchKind.InvalidMessage, 0, 0, 0);
                return;
            }

            if ((PhysicsBackend)localPeer.PhysicsBackend != PhysicsBackend)
            {
                session.Abort(StopReason.PhysicsBackendMismatch);
                return;
            }

            LocalPeerId = localPeer.PeerId;
            ServerTickRate = localPeer.TickRate;
            OnLocalPeerAssigned?.Invoke(LocalPeerId);
        }

        private void OnSpawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.SpawnCodec, payload, ref spawn))
            {
                return;
            }

            if (spawn.PrefabId == 0)
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, spawn.ObjectId, 0, 0);
                return;
            }

            var scene = new NetworkScene(spawn.Scene);
            if (scene != default && !loadedScenes.ContainsKey(scene))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, spawn.ObjectId, spawn.PrefabId, 0);
                return;
            }

            Spawn(spawn.ObjectId, spawn.PrefabId, 0, spawn.PrefabFingerprint, spawn.OwnerId, spawn.Mask, spawn.Values, spawn.States, spawn.Collections, scene);
        }

        private void OnSceneSpawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.SceneSpawnCodec, payload, ref sceneSpawn))
            {
                return;
            }

            if (sceneSpawn.SceneObjectId == 0)
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, sceneSpawn.ObjectId, 0, 0);
                return;
            }

            var scene = new NetworkScene(sceneSpawn.Scene);
            if (scene != default && (!loadedScenes.TryGetValue(scene, out SceneRef loaded) || (uint)(sceneSpawn.SceneObjectId >> 32) != loaded.SceneId))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, sceneSpawn.ObjectId, 0, sceneSpawn.SceneObjectId);
                return;
            }

            Spawn(sceneSpawn.ObjectId, 0, sceneSpawn.SceneObjectId, sceneSpawn.Fingerprint, sceneSpawn.OwnerId, sceneSpawn.Mask, sceneSpawn.Values, sceneSpawn.States, sceneSpawn.Collections, scene);
        }

        private void Spawn(uint objectId, uint prefabId, ulong sceneObjectId, uint fingerprint, ulong ownerId, byte mask, List<float> values, List<ReadOnlyMemory<byte>> states, List<ReadOnlyMemory<byte>> collections, NetworkScene scene)
        {
            if (objectId == 0 || !SpawnTransform.TryUnpack(mask, values, out Vector3 position, out Quaternion rotation, out Vector3 scale))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, objectId, prefabId, sceneObjectId);
                return;
            }

            if ((sceneObjectId != 0 && sceneObjectIds.Contains((scene, sceneObjectId))) || !table.TryAdd(new ObjectRow(objectId, prefabId, sceneObjectId, ownerId, scene)))
            {
                Mismatch(ObjectMismatchKind.DuplicateObject, objectId, prefabId, sceneObjectId);
                return;
            }

            if (sceneObjectId != 0)
            {
                sceneObjectIds.Add((scene, sceneObjectId));
            }

            SpawnResult result;
            try
            {
                result = spawner.Spawn(new SpawnedObject(objectId, prefabId, sceneObjectId, fingerprint, ownerId, position, rotation, scale, states, scene, collections));
            }
            catch
            {
                Forget(objectId, scene, sceneObjectId);
                throw;
            }

            if (result == SpawnResult.Spawned)
            {
                return;
            }

            Forget(objectId, scene, sceneObjectId);
            Mismatch(KindOf(result), objectId, prefabId, sceneObjectId);
        }

        private void OnDespawn(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.DespawnCodec, payload, ref despawn))
            {
                return;
            }

            uint objectId = despawn.ObjectId;
            if (!table.TryGet(objectId, out ObjectRow row))
            {
                Mismatch(ObjectMismatchKind.UnknownObject, objectId, 0, 0);
                return;
            }

            try
            {
                spawner.Despawn(objectId);
            }
            finally
            {
                Forget(objectId, row.Scene, row.SceneObjectId);
            }
        }

        private void OnOwnerChange(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.OwnerChangeCodec, payload, ref ownerChange))
            {
                return;
            }

            uint objectId = ownerChange.ObjectId;
            if (!table.SetOwner(objectId, ownerChange.OwnerId, out ulong previousOwnerId))
            {
                Mismatch(ObjectMismatchKind.UnknownObject, objectId, 0, 0);
                return;
            }

            OnOwnerChanged?.Invoke(new ObjectOwnerChangedArgs(objectId, previousOwnerId, ownerChange.OwnerId));
        }

        private void OnSceneMove(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(protocol.SceneMoveCodec, payload, ref sceneMove))
            {
                return;
            }

            uint objectId = sceneMove.ObjectId;
            if (!table.TryGet(objectId, out ObjectRow row))
            {
                Mismatch(ObjectMismatchKind.UnknownObject, objectId, 0, 0);
                return;
            }

            var scene = new NetworkScene(sceneMove.Scene);
            if (row.SceneObjectId != 0 || (scene != default && !loadedScenes.ContainsKey(scene)))
            {
                Mismatch(ObjectMismatchKind.InvalidSpawn, objectId, row.PrefabId, row.SceneObjectId);
                return;
            }

            if (row.Scene == scene)
            {
                return;
            }

            table.SetScene(objectId, scene, out NetworkScene previousScene);
            OnSceneMoved?.Invoke(new ObjectSceneMovedArgs(objectId, previousScene, scene, sceneMove.Tick));
        }

        private void OnSceneLoad(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(sceneProtocol.LoadCodec, payload, ref sceneLoad))
            {
                return;
            }

            if (!sceneLoad.Scenes.TrueForAll(IsValid))
            {
                Mismatch(ObjectMismatchKind.InvalidMessage, 0, 0, 0);
                return;
            }

            if (sceneLoad.Full)
            {
                foreach (KeyValuePair<NetworkScene, SceneRef> loaded in loadedScenes)
                {
                    if (!Lists(sceneLoad.Scenes, loaded.Value))
                    {
                        sceneSteps.Add(SceneStep.Unloading(loaded.Key));
                    }
                }

                if (sceneBusy && sceneRunning.Load && !Lists(sceneLoad.Scenes, sceneRunning.Ref))
                {
                    sceneSteps.Add(SceneStep.Unloading(sceneRunning.Scene));
                }
            }

            foreach (SceneRef sceneRef in sceneLoad.Scenes)
            {
                if (!IsLoadPending(new NetworkScene(sceneRef.Scene)))
                {
                    sceneSteps.Add(SceneStep.Loading(sceneRef));
                }
            }

            RunScenes();
        }

        private void OnSceneUnload(ulong peerId, ReadOnlyMemory<byte> payload)
        {
            if (!TryDecode(sceneProtocol.UnloadCodec, payload, ref sceneUnload))
            {
                return;
            }

            foreach (uint id in sceneUnload.Scenes)
            {
                var scene = new NetworkScene(id);
                int queued = sceneSteps.FindIndex(step => step.Load && step.Scene == scene);
                if (queued >= 0)
                {
                    sceneSteps.RemoveAt(queued);
                }
                else
                {
                    sceneSteps.Add(SceneStep.Unloading(scene));
                }
            }

            RunScenes();
        }

        private static bool IsValid(SceneRef sceneRef) =>
            sceneRef.Scene != 0 && sceneRef.SceneId != 0 && Enum.IsDefined(typeof(ScenePhysics), sceneRef.Physics);

        private static bool Lists(List<SceneRef> scenes, SceneRef wanted) =>
            scenes.Exists(listed => listed.Scene == wanted.Scene && listed.SceneId == wanted.SceneId && listed.Physics == wanted.Physics);

        private bool IsLoadPending(NetworkScene scene) =>
            (sceneBusy && sceneRunning.Load && sceneRunning.Scene == scene)
            || sceneSteps.Exists(step => step.Load && step.Scene == scene);

        private void RunScenes()
        {
            while (!sceneBusy && sceneSteps.Count > 0)
            {
                SceneStep step = sceneSteps[0];
                sceneSteps.RemoveAt(0);
                NetworkScene scene = step.Scene;
                if (step.Load)
                {
                    if (loadedScenes.ContainsKey(scene))
                    {
                        ReportLoaded(scene);
                        continue;
                    }

                    sceneBusy = true;
                    sceneRunning = step;
                    SceneRef sceneRef = step.Ref;
                    OnSceneLoadStarted?.Invoke(scene, sceneRef.SceneId);
                    if (!sceneHost.TryLoad(scene, sceneRef.SceneId, (ScenePhysics)sceneRef.Physics, progress => ReportProgress(scene, progress), () => FinishLoad(sceneRef), () => FailLoad(scene)))
                    {
                        FailLoad(scene);
                        return;
                    }
                }
                else if (loadedScenes.ContainsKey(scene))
                {
                    sceneBusy = true;
                    sceneRunning = step;
                    sceneHost.Unload(scene, () => FinishUnload(scene));
                }
            }
        }

        private void ReportProgress(NetworkScene scene, float progress)
        {
            if (!sceneBusy || !sceneRunning.Load || sceneRunning.Scene != scene)
            {
                return;
            }

            OnSceneLoadProgress?.Invoke(scene, float.IsNaN(progress) ? 0f : Math.Clamp(progress, 0f, 1f));
        }

        private void FinishLoad(SceneRef sceneRef)
        {
            var scene = new NetworkScene(sceneRef.Scene);
            sceneBusy = false;
            loadedScenes.Add(scene, sceneRef);
            OnSceneLoaded?.Invoke(scene);
            ReportLoaded(scene);
            RunScenes();
        }

        private void FailLoad(NetworkScene scene)
        {
            sceneBusy = false;
            sceneSteps.Clear();
            OnObjectMismatch?.Invoke(new ObjectMismatchArgs(ObjectMismatchKind.UnknownScene, 0, 0, 0, scene));
            session.Abort(StopReason.PrefabMismatch);
        }

        private void FinishUnload(NetworkScene scene)
        {
            sceneBusy = false;
            loadedScenes.Remove(scene);
            OnSceneUnloaded?.Invoke(scene);
            RunScenes();
        }

        private void ReportLoaded(NetworkScene scene)
        {
            if (session.State != ConnectionState.Started)
            {
                return;
            }

            sceneLoaded.Scenes.Clear();
            sceneLoaded.Scenes.Add(scene.Id);
            session.EnqueueObjectMessage(sceneProtocol.LoadedCodec.MessageId, sceneProtocol.LoadedCodec.Encode(sceneLoaded).Span);
        }

        private bool TryDecode<T>(IMessageCodec<T> codec, ReadOnlyMemory<byte> payload, ref T value)
        {
            try
            {
                codec.Decode(payload, ref value);
                return true;
            }
            catch (MessageDecodeException)
            {
                Mismatch(ObjectMismatchKind.InvalidMessage, 0, 0, 0);
                return false;
            }
        }

        private static ObjectMismatchKind KindOf(SpawnResult result)
        {
            switch (result)
            {
                case SpawnResult.IncompatiblePrefab:
                    return ObjectMismatchKind.IncompatiblePrefab;
                case SpawnResult.UnknownSceneObject:
                    return ObjectMismatchKind.UnknownSceneObject;
                case SpawnResult.IncompatibleSceneObject:
                    return ObjectMismatchKind.IncompatibleSceneObject;
                case SpawnResult.InvalidSpawn:
                    return ObjectMismatchKind.InvalidSpawn;
                default:
                    return ObjectMismatchKind.UnknownPrefab;
            }
        }

        private void Forget(uint objectId, NetworkScene scene, ulong sceneObjectId)
        {
            table.Remove(objectId);
            sceneObjectIds.Remove((scene, sceneObjectId));
        }

        private void Mismatch(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId)
        {
            OnObjectMismatch?.Invoke(new ObjectMismatchArgs(kind, objectId, prefabId, sceneObjectId));
            bool isBuildMismatch = kind == ObjectMismatchKind.UnknownPrefab
                || kind == ObjectMismatchKind.IncompatiblePrefab
                || kind == ObjectMismatchKind.UnknownSceneObject
                || kind == ObjectMismatchKind.IncompatibleSceneObject;
            session.Abort(isBuildMismatch ? StopReason.PrefabMismatch : StopReason.ObjectMismatch);
        }

        private void Clear()
        {
            clearing.Clear();
            table.CopyIdsTo(clearing);
            table.Clear();
            sceneObjectIds.Clear();
            sceneSteps.Clear();
            LocalPeerId = 0;
            ServerTickRate = 0;
            for (int index = clearing.Count - 1; index >= 0; index--)
            {
                spawner.Despawn(clearing[index]);
            }
        }

        private readonly struct SceneStep
        {
            private SceneStep(NetworkScene scene, SceneRef sceneRef, bool load)
            {
                Scene = scene;
                Ref = sceneRef;
                Load = load;
            }

            public NetworkScene Scene { get; }

            public SceneRef Ref { get; }

            public uint SceneId => Ref.SceneId;

            public bool Load { get; }

            public static SceneStep Loading(SceneRef sceneRef) =>
                new SceneStep(new NetworkScene(sceneRef.Scene), new SceneRef { Scene = sceneRef.Scene, SceneId = sceneRef.SceneId, Physics = sceneRef.Physics }, true);

            public static SceneStep Unloading(NetworkScene scene) => new SceneStep(scene, null, false);
        }
    }
}
