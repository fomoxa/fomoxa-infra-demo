using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Objects
{
    public sealed class ServerObjects
    {
        private readonly ServerSession session;
        private readonly ObjectProtocol protocol;
        private readonly SpawnSource source;
        private readonly ObjectTable table = new ObjectTable();
        private readonly HashSet<(NetworkScene, ulong)> sceneObjectIds = new HashSet<(NetworkScene, ulong)>();
        private readonly Dictionary<uint, ObserverSet> observers = new Dictionary<uint, ObserverSet>();
        private readonly List<uint> ownedByLeavingPeer = new List<uint>();
        private readonly List<ulong> startedPeers = new List<ulong>();
        private readonly List<ulong> entering = new List<ulong>();
        private readonly List<ulong> leaving = new List<ulong>();
        private readonly List<ulong> staying = new List<ulong>();
        private readonly List<ObserverAddedArgs> added = new List<ObserverAddedArgs>();
        private readonly List<FailedEntry> failedEntries = new List<FailedEntry>();
        private readonly List<uint> roundObjectIds = new List<uint>();
        private readonly List<uint> everyObjectIds = new List<uint>();
        private readonly HashSet<ulong> candidateSet = new HashSet<ulong>();
        private readonly List<ulong> candidatePeers = new List<ulong>();
        private readonly LocalPeer localPeer = new LocalPeer();
        private ushort tickRate = 30;
        private readonly ObjectSpawn spawn = new ObjectSpawn();
        private readonly ObjectSceneSpawn sceneSpawn = new ObjectSceneSpawn();
        private readonly ObjectDespawn despawn = new ObjectDespawn();
        private readonly ObjectOwnerChange ownerChange = new ObjectOwnerChange();
        private readonly ObjectSceneMove sceneMove = new ObjectSceneMove();
        private int observerInterval = 15;
        private int roundTick;
        private int roundCursor;
        private int roundShare;
        private bool deciding;

        public ServerObjects(ServerSession session, ObjectProtocol protocol, SpawnSource source)
            : this(session, protocol, source, null)
        {
        }

        public ServerObjects(ServerSession session, ObjectProtocol protocol, SpawnSource source, SceneProtocol sceneProtocol)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            session.SetPeerStartHandler(StartPeer);
            session.OnServerConnectionState += OnServerState;
            session.OnRemoteConnectionState += OnPeerState;
            if (sceneProtocol != null)
            {
                Scenes = new ServerScenes(this, session, sceneProtocol);
            }
        }

        public event Action<ObjectOwnerChangedArgs> OnOwnerChanged;

        public event Action<ObserverAddedArgs> OnObserverAdded;

        public event Action<uint> OnDespawned;

        public Func<uint, bool> DespawnWithOwner { get; set; }

        public Action<uint> Despawner { get; set; }

        public Func<uint, ulong, bool> Observes { get; set; }

        public Func<uint, HashSet<ulong>, bool> CollectCandidates { get; set; }

        public ServerScenes Scenes { get; }

        public ushort TickRate
        {
            get => tickRate;
            set => tickRate = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the tick rate is at least one tick per second");
        }

        public PhysicsBackend PhysicsBackend { get; set; } = PhysicsBackend.Rigidbody;

        public int ObserverInterval
        {
            get => observerInterval;
            set => observerInterval = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "the observer interval is at least one tick");
        }

        public int Count => table.Count;

        internal uint NextObjectId { get; set; } = 1;

        public bool TryGet(uint objectId, out ObjectRow row) => table.TryGet(objectId, out row);

        public bool IsObserver(uint objectId, ulong peerId) =>
            observers.TryGetValue(objectId, out ObserverSet peerIds) && peerIds.Contains(peerId);

        public IReadOnlyList<ulong> ObserversOf(uint objectId) =>
            observers.TryGetValue(objectId, out ObserverSet peerIds) ? peerIds : (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public int SendToObservers(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body) =>
            observers.TryGetValue(objectId, out ObserverSet peerIds) ? session.BroadcastToObject(peerIds, messageId, objectId, behaviourIndex, body) : 0;

        internal int SendToObservers(uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body, ulong exceptPeerId) =>
            observers.TryGetValue(objectId, out ObserverSet peerIds) ? session.BroadcastToObject(peerIds, exceptPeerId, messageId, objectId, behaviourIndex, body) : 0;

        public SendResult SendToObserver(ulong peerId, uint messageId, uint objectId, byte behaviourIndex, ReadOnlySpan<byte> body)
        {
            if (session.PeerState(peerId) == ConnectionState.Stopped)
            {
                return SendResult.NotConnected;
            }

            if (!IsObserver(objectId, peerId))
            {
                return SendResult.NotObserver;
            }

            return session.SendToObject(peerId, messageId, objectId, behaviourIndex, body);
        }

        public uint Spawn(uint prefabId, ulong ownerId) => Spawn(prefabId, ownerId, default);

        public uint Spawn(uint prefabId, ulong ownerId, NetworkScene scene)
        {
            EnsureNotDeciding();
            EnsureStarted();
            if (prefabId == 0)
            {
                throw new ArgumentException("prefab id 0 means no prefab", nameof(prefabId));
            }

            EnsureValidOwner(ownerId);
            EnsureNetworkScene(scene);
            uint objectId = AllocateObjectId();
            ObserverSet peerIds = DecideObservers(objectId, ownerId, scene);
            WriteSpawn(objectId, prefabId, ownerId, scene);
            ReadOnlySpan<byte> payload = protocol.SpawnCodec.Encode(spawn).Span;
            foreach (ulong peerId in peerIds)
            {
                session.EnqueueObjectMessage(peerId, protocol.SpawnCodec.MessageId, payload);
            }

            table.Add(new ObjectRow(objectId, prefabId, 0, ownerId, scene));
            observers.Add(objectId, peerIds);
            return objectId;
        }

        public uint SpawnScene(ulong sceneObjectId, ulong ownerId) => SpawnScene(sceneObjectId, ownerId, default);

        public uint SpawnScene(ulong sceneObjectId, ulong ownerId, NetworkScene scene)
        {
            EnsureNotDeciding();
            EnsureStarted();
            if (sceneObjectId == 0)
            {
                throw new ArgumentException("scene object id 0 means no scene object", nameof(sceneObjectId));
            }

            if (sceneObjectIds.Contains((scene, sceneObjectId)))
            {
                throw new ArgumentException($"scene object 0x{sceneObjectId:X16} is already spawned in {scene}", nameof(sceneObjectId));
            }

            EnsureValidOwner(ownerId);
            EnsureNetworkScene(scene);
            uint objectId = AllocateObjectId();
            ObserverSet peerIds = DecideObservers(objectId, ownerId, scene);
            WriteSceneSpawn(objectId, scene, sceneObjectId, ownerId);
            ReadOnlySpan<byte> payload = protocol.SceneSpawnCodec.Encode(sceneSpawn).Span;
            foreach (ulong peerId in peerIds)
            {
                session.EnqueueObjectMessage(peerId, protocol.SceneSpawnCodec.MessageId, payload);
            }

            table.Add(new ObjectRow(objectId, 0, sceneObjectId, ownerId, scene));
            sceneObjectIds.Add((scene, sceneObjectId));
            observers.Add(objectId, peerIds);
            return objectId;
        }

        public bool Despawn(uint objectId)
        {
            EnsureNotDeciding();
            if (!table.TryGet(objectId, out ObjectRow row))
            {
                return false;
            }

            table.Remove(objectId);
            sceneObjectIds.Remove((row.Scene, row.SceneObjectId));
            observers.Remove(objectId, out ObserverSet peerIds);
            despawn.ObjectId = objectId;
            ReadOnlySpan<byte> payload = protocol.DespawnCodec.Encode(despawn).Span;
            foreach (ulong peerId in peerIds)
            {
                session.EnqueueObjectMessage(peerId, protocol.DespawnCodec.MessageId, payload);
            }

            OnDespawned?.Invoke(objectId);
            return true;
        }

        public bool ChangeOwner(uint objectId, ulong ownerId)
        {
            EnsureNotDeciding();
            if (!IsValidOwner(ownerId) || !table.SetOwner(objectId, ownerId, out ulong previousOwnerId))
            {
                return false;
            }

            if (previousOwnerId == ownerId)
            {
                return true;
            }

            ObserverSet peerIds = observers[objectId];
            table.TryGet(objectId, out ObjectRow changed);
            bool previousLeaves;
            try
            {
                previousLeaves = previousOwnerId != 0 && peerIds.Contains(previousOwnerId) && (!InScene(changed.Scene, previousOwnerId) || !Decide(objectId, previousOwnerId));
            }
            catch
            {
                table.SetOwner(objectId, previousOwnerId, out _);
                throw;
            }

            bool ownerEnters = ownerId != 0 && !peerIds.Contains(ownerId) && InScene(changed.Scene, ownerId);
            ownerChange.ObjectId = objectId;
            ownerChange.OwnerId = ownerId;
            ReadOnlySpan<byte> payload = protocol.OwnerChangeCodec.Encode(ownerChange).Span;
            foreach (ulong peerId in peerIds)
            {
                if (!previousLeaves || peerId != previousOwnerId)
                {
                    session.EnqueueObjectMessage(peerId, protocol.OwnerChangeCodec.MessageId, payload);
                }
            }

            if (previousLeaves)
            {
                peerIds.Remove(previousOwnerId);
                SendDespawn(objectId, previousOwnerId);
            }

            if (ownerEnters)
            {
                entering.Clear();
                entering.Add(ownerId);
                Enter(changed, peerIds, entering);
            }

            OnOwnerChanged?.Invoke(new ObjectOwnerChangedArgs(objectId, previousOwnerId, ownerId));
            FinishEntries();
            return true;
        }

        public bool CanMoveToScene(uint objectId, NetworkScene scene, out NetworkScene previousScene)
        {
            if (!table.TryGet(objectId, out ObjectRow row))
            {
                previousScene = default;
                return false;
            }

            if (row.SceneObjectId != 0)
            {
                throw new ArgumentException($"scene object 0x{row.SceneObjectId:X16} stays in the scene it belongs to; only prefab objects move between scenes", nameof(objectId));
            }

            EnsureNetworkScene(scene);
            previousScene = row.Scene;
            return true;
        }

        public bool MoveToScene(uint objectId, NetworkScene scene, uint tick)
        {
            EnsureNotDeciding();
            if (!CanMoveToScene(objectId, scene, out NetworkScene previousScene))
            {
                return false;
            }

            if (previousScene == scene)
            {
                return true;
            }

            table.SetScene(objectId, scene, out _);
            table.TryGet(objectId, out ObjectRow moved);
            ObserverSet peerIds = observers[objectId];
            entering.Clear();
            leaving.Clear();
            staying.Clear();
            if (session.State == ServerState.Started)
            {
                CollectStartedPeers();
                try
                {
                    foreach (ulong peerId in CandidatesOf(moved, peerIds))
                    {
                        bool observes = InScene(scene, peerId) && (peerId == moved.OwnerId || Decide(objectId, peerId));
                        bool present = peerIds.Contains(peerId);
                        if (observes && present)
                        {
                            staying.Add(peerId);
                        }
                        else if (observes)
                        {
                            entering.Add(peerId);
                        }
                        else if (present)
                        {
                            leaving.Add(peerId);
                        }
                    }
                }
                catch
                {
                    table.SetScene(objectId, previousScene, out _);
                    throw;
                }
            }

            sceneMove.ObjectId = objectId;
            sceneMove.Scene = scene.Id;
            sceneMove.Tick = tick;
            ReadOnlySpan<byte> payload = protocol.SceneMoveCodec.Encode(sceneMove).Span;
            foreach (ulong peerId in staying)
            {
                session.EnqueueObjectMessage(peerId, protocol.SceneMoveCodec.MessageId, payload);
            }

            foreach (ulong peerId in leaving)
            {
                peerIds.Remove(peerId);
                SendDespawn(objectId, peerId);
            }

            try
            {
                Enter(moved, peerIds, entering);
            }
            finally
            {
                staying.Clear();
                FinishEntries();
            }

            return true;
        }

        public bool RebuildObservers(uint objectId)
        {
            EnsureNotDeciding();
            if (session.State != ServerState.Started || !table.TryGet(objectId, out ObjectRow row))
            {
                return false;
            }

            CollectStartedPeers();
            try
            {
                Rebuild(row, true);
            }
            finally
            {
                FinishEntries();
            }

            return true;
        }

        internal bool PeerStarted(ulong peerId) =>
            session.State == ServerState.Started && session.PeerState(peerId) == ConnectionState.Started;

        internal void RebuildPairs(List<uint> objectIds, ulong peerId)
        {
            EnsureNotDeciding();
            startedPeers.Clear();
            startedPeers.Add(peerId);
            try
            {
                foreach (uint objectId in objectIds)
                {
                    if (table.TryGet(objectId, out ObjectRow row))
                    {
                        Rebuild(row, false);
                    }
                }
            }
            finally
            {
                FinishEntries();
            }
        }

        public void RebuildObserversOfPeer(ulong peerId)
        {
            EnsureNotDeciding();
            if (session.State != ServerState.Started || session.PeerState(peerId) != ConnectionState.Started)
            {
                return;
            }

            startedPeers.Clear();
            startedPeers.Add(peerId);
            RebuildEvery(false);
        }

        public void RebuildObservers()
        {
            EnsureNotDeciding();
            if (session.State != ServerState.Started)
            {
                return;
            }

            CollectStartedPeers();
            RebuildEvery(true);
        }

        public void RebuildObserversRound()
        {
            EnsureNotDeciding();
            if (session.State != ServerState.Started)
            {
                return;
            }

            if (roundTick >= observerInterval)
            {
                roundTick = 0;
            }

            if (roundTick == 0)
            {
                roundObjectIds.Clear();
                table.CopyIdsTo(roundObjectIds);
                roundCursor = 0;
                roundShare = (roundObjectIds.Count + observerInterval - 1) / observerInterval;
            }

            roundTick++;
            if (roundCursor >= roundObjectIds.Count)
            {
                return;
            }

            CollectStartedPeers();
            int end = Math.Min(roundCursor + roundShare, roundObjectIds.Count);
            try
            {
                while (roundCursor < end)
                {
                    uint objectId = roundObjectIds[roundCursor++];
                    if (table.TryGet(objectId, out ObjectRow row))
                    {
                        Rebuild(row, true);
                    }
                }
            }
            finally
            {
                FinishEntries();
            }
        }

        internal void DespawnScene(NetworkScene scene)
        {
            var inScene = new List<uint>();
            foreach (ObjectRow row in table)
            {
                if (row.Scene == scene)
                {
                    inScene.Add(row.ObjectId);
                }
            }

            for (int index = inScene.Count - 1; index >= 0; index--)
            {
                uint objectId = inScene[index];
                if (!table.Contains(objectId))
                {
                    continue;
                }

                if (Despawner != null)
                {
                    Despawner(objectId);
                }
                else
                {
                    Despawn(objectId);
                }
            }
        }

        private void RebuildEvery(bool everyPeer)
        {
            everyObjectIds.Clear();
            table.CopyIdsTo(everyObjectIds);
            try
            {
                foreach (uint objectId in everyObjectIds)
                {
                    if (table.TryGet(objectId, out ObjectRow row))
                    {
                        Rebuild(row, everyPeer);
                    }
                }
            }
            finally
            {
                everyObjectIds.Clear();
                FinishEntries();
            }
        }

        private void Rebuild(ObjectRow row, bool everyPeer)
        {
            ObserverSet peerIds = observers[row.ObjectId];
            entering.Clear();
            leaving.Clear();
            foreach (ulong peerId in everyPeer ? CandidatesOf(row, peerIds) : startedPeers)
            {
                bool observes = InScene(row.Scene, peerId) && (peerId == row.OwnerId || Decide(row.ObjectId, peerId));
                bool present = peerIds.Contains(peerId);
                if (observes && !present)
                {
                    entering.Add(peerId);
                }
                else if (!observes && present)
                {
                    leaving.Add(peerId);
                }
            }

            foreach (ulong peerId in leaving)
            {
                peerIds.Remove(peerId);
                SendDespawn(row.ObjectId, peerId);
            }

            Enter(row, peerIds, entering);
        }

        private List<ulong> CandidatesOf(ObjectRow row, ObserverSet peerIds)
        {
            candidateSet.Clear();
            if (CollectCandidates == null || !CollectCandidates(row.ObjectId, candidateSet))
            {
                return startedPeers;
            }

            candidatePeers.Clear();
            foreach (ulong peerId in candidateSet)
            {
                if (session.PeerState(peerId) == ConnectionState.Started)
                {
                    candidatePeers.Add(peerId);
                }
            }

            if (row.OwnerId != 0 && !candidateSet.Contains(row.OwnerId) && session.PeerState(row.OwnerId) == ConnectionState.Started)
            {
                candidatePeers.Add(row.OwnerId);
            }

            foreach (ulong peerId in peerIds)
            {
                if (!candidateSet.Contains(peerId) && peerId != row.OwnerId)
                {
                    candidatePeers.Add(peerId);
                }
            }

            return candidatePeers;
        }

        private void Enter(ObjectRow row, ObserverSet peerIds, List<ulong> peers)
        {
            if (peers.Count == 0)
            {
                return;
            }

            bool isScene = row.SceneObjectId != 0;
            uint messageId = isScene ? protocol.SceneSpawnCodec.MessageId : protocol.SpawnCodec.MessageId;
            ReadOnlySpan<byte> payload;
            try
            {
                payload = EncodeSpawn(row);
            }
            catch (Exception exception)
            {
                foreach (ulong peerId in peers)
                {
                    failedEntries.Add(new FailedEntry(peerId, messageId, exception));
                }

                return;
            }

            foreach (ulong peerId in peers)
            {
                session.EnqueueObjectMessage(peerId, messageId, payload);
                peerIds.Add(peerId);
                added.Add(new ObserverAddedArgs(row.ObjectId, peerId));
            }
        }

        private void FinishEntries()
        {
            if (added.Count > 0 && OnObserverAdded != null)
            {
                foreach (ObserverAddedArgs args in added)
                {
                    OnObserverAdded(args);
                }
            }

            added.Clear();
            if (failedEntries.Count == 0)
            {
                return;
            }

            FailedEntry[] failures = failedEntries.ToArray();
            failedEntries.Clear();
            foreach (FailedEntry failure in failures)
            {
                session.EndPeerByHandler(failure.PeerId, failure.MessageId, failure.Exception);
            }
        }

        private ReadOnlySpan<byte> EncodeSpawn(ObjectRow row)
        {
            if (row.SceneObjectId != 0)
            {
                WriteSceneSpawn(row.ObjectId, row.Scene, row.SceneObjectId, row.OwnerId);
                return protocol.SceneSpawnCodec.Encode(sceneSpawn).Span;
            }

            WriteSpawn(row.ObjectId, row.PrefabId, row.OwnerId, row.Scene);
            return protocol.SpawnCodec.Encode(spawn).Span;
        }

        private void SendDespawn(uint objectId, ulong peerId)
        {
            despawn.ObjectId = objectId;
            session.EnqueueObjectMessage(peerId, protocol.DespawnCodec.MessageId, protocol.DespawnCodec.Encode(despawn).Span);
        }

        private ObserverSet DecideObservers(uint objectId, ulong ownerId, NetworkScene scene)
        {
            CollectStartedPeers();
            var peerIds = new ObserverSet(startedPeers.Count);
            foreach (ulong peerId in startedPeers)
            {
                if (InScene(scene, peerId) && (peerId == ownerId || Decide(objectId, peerId)))
                {
                    peerIds.Add(peerId);
                }
            }

            return peerIds;
        }

        private bool InScene(NetworkScene scene, ulong peerId) =>
            scene == default || (Scenes != null && Scenes.HasLoaded(peerId, scene));

        private void EnsureNetworkScene(NetworkScene scene)
        {
            if (scene != default && (Scenes == null || !Scenes.Contains(scene)))
            {
                throw new ArgumentException($"{scene} is not a network scene", nameof(scene));
            }
        }

        private bool Decide(uint objectId, ulong peerId)
        {
            Func<uint, ulong, bool> observes = Observes;
            if (observes == null)
            {
                return true;
            }

            deciding = true;
            try
            {
                return observes(objectId, peerId);
            }
            finally
            {
                deciding = false;
            }
        }

        private void CollectStartedPeers()
        {
            startedPeers.Clear();
            session.CopyStartedPeerIds(startedPeers);
        }

        private bool StartPeer(ulong peerId, out uint failedMessageId, out Exception exception)
        {
            localPeer.PeerId = peerId;
            localPeer.TickRate = tickRate;
            localPeer.PhysicsBackend = (byte)PhysicsBackend;
            session.EnqueueObjectMessage(peerId, protocol.LocalPeerCodec.MessageId, protocol.LocalPeerCodec.Encode(localPeer).Span);
            Scenes?.StartPeer(peerId);
            added.Clear();
            foreach (ObjectRow row in table)
            {
                bool observes;
                try
                {
                    observes = InScene(row.Scene, peerId) && (row.OwnerId == peerId || Decide(row.ObjectId, peerId));
                }
                catch
                {
                    added.Clear();
                    RemoveFromEverySet(peerId);
                    throw;
                }

                if (!observes)
                {
                    continue;
                }

                bool isScene = row.SceneObjectId != 0;
                ReadOnlySpan<byte> payload;
                try
                {
                    payload = EncodeSpawn(row);
                }
                catch (Exception thrown)
                {
                    added.Clear();
                    failedMessageId = isScene ? protocol.SceneSpawnCodec.MessageId : protocol.SpawnCodec.MessageId;
                    exception = thrown;
                    return false;
                }

                session.EnqueueObjectMessage(peerId, isScene ? protocol.SceneSpawnCodec.MessageId : protocol.SpawnCodec.MessageId, payload);
                observers[row.ObjectId].Add(peerId);
                added.Add(new ObserverAddedArgs(row.ObjectId, peerId));
            }

            FinishEntries();
            failedMessageId = 0;
            exception = null;
            return true;
        }

        private void WriteSpawn(uint objectId, uint prefabId, ulong ownerId, NetworkScene scene)
        {
            SpawnData data = source(objectId);
            spawn.ObjectId = objectId;
            spawn.PrefabId = prefabId;
            spawn.PrefabFingerprint = data.PrefabFingerprint;
            spawn.OwnerId = ownerId;
            spawn.Scene = scene.Id;
            SpawnTransform.Pack(data, spawn);
        }

        private void WriteSceneSpawn(uint objectId, NetworkScene scene, ulong sceneObjectId, ulong ownerId)
        {
            SpawnData data = source(objectId);
            sceneSpawn.ObjectId = objectId;
            sceneSpawn.Scene = scene.Id;
            sceneSpawn.SceneObjectId = sceneObjectId;
            sceneSpawn.Fingerprint = data.PrefabFingerprint;
            sceneSpawn.OwnerId = ownerId;
            SpawnTransform.Pack(data, sceneSpawn);
        }

        private bool IsValidOwner(ulong ownerId) =>
            ownerId == 0 || session.PeerState(ownerId) == ConnectionState.Started;

        private void EnsureValidOwner(ulong ownerId)
        {
            if (!IsValidOwner(ownerId))
            {
                throw new ArgumentException($"owner {ownerId} is not a started peer", nameof(ownerId));
            }
        }

        internal void EnsureNotDeciding()
        {
            if (deciding)
            {
                throw new InvalidOperationException("the observer rule is running; it cannot spawn, despawn, change owners or rebuild observers");
            }
        }

        private void OnPeerState(ConnectionStateArgs args)
        {
            if (args.State != ConnectionState.Stopped || session.State != ServerState.Started)
            {
                return;
            }

            RemoveFromEverySet(args.PeerId);
            ownedByLeavingPeer.Clear();
            foreach (ObjectRow row in table)
            {
                if (row.OwnerId == args.PeerId)
                {
                    ownedByLeavingPeer.Add(row.ObjectId);
                }
            }

            for (int index = ownedByLeavingPeer.Count - 1; index >= 0; index--)
            {
                uint objectId = ownedByLeavingPeer[index];
                if (!table.TryGet(objectId, out ObjectRow row) || row.OwnerId != args.PeerId)
                {
                    continue;
                }

                if (DespawnWithOwner?.Invoke(objectId) ?? true)
                {
                    if (Despawner != null)
                    {
                        Despawner(objectId);
                    }
                    else
                    {
                        Despawn(objectId);
                    }
                }
                else
                {
                    ChangeOwner(objectId, 0);
                }
            }
        }

        private void RemoveFromEverySet(ulong peerId)
        {
            foreach (ObserverSet peerIds in observers.Values)
            {
                peerIds.Remove(peerId);
            }
        }

        private void EnsureStarted()
        {
            if (session.State != ServerState.Started)
            {
                throw new InvalidOperationException("the server session is not started");
            }
        }

        private uint AllocateObjectId()
        {
            for (uint tried = 0; tried < uint.MaxValue; tried++)
            {
                uint candidate = NextObjectId;
                NextObjectId = candidate == uint.MaxValue ? 1 : candidate + 1;
                if (!table.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("every object id is in use");
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.State == ServerState.Stopped)
            {
                table.Clear();
                sceneObjectIds.Clear();
                observers.Clear();
                roundObjectIds.Clear();
                roundTick = 0;
                roundCursor = 0;
                NextObjectId = 1;
            }
        }

        private readonly struct FailedEntry
        {
            public FailedEntry(ulong peerId, uint messageId, Exception exception)
            {
                PeerId = peerId;
                MessageId = messageId;
                Exception = exception;
            }

            public ulong PeerId { get; }

            public uint MessageId { get; }

            public Exception Exception { get; }
        }
    }
}
