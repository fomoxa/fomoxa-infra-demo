using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fomoxa.Networking.Objects
{
    public enum SpawnResult
    {
        Spawned,
        UnknownPrefab,
        IncompatiblePrefab,
        UnknownSceneObject,
        IncompatibleSceneObject,
        InvalidSpawn,
    }

    public interface ISceneHost
    {
        bool TryLoad(NetworkScene scene, uint sceneId, ScenePhysics physics, Action<float> progress, Action loaded, Action failed);

        void Unload(NetworkScene scene, Action unloaded);
    }

    public enum ObjectMismatchKind
    {
        UnknownPrefab,
        IncompatiblePrefab,
        UnknownSceneObject,
        IncompatibleSceneObject,
        DuplicateObject,
        UnknownObject,
        InvalidSpawn,
        InvalidMessage,
        UnknownScene,
    }

    public readonly struct ServerRpcRejectedArgs
    {
        public ServerRpcRejectedArgs(ulong peerId, uint objectId, byte behaviourIndex, uint messageId)
        {
            PeerId = peerId;
            ObjectId = objectId;
            BehaviourIndex = behaviourIndex;
            MessageId = messageId;
        }

        public ulong PeerId { get; }

        public uint ObjectId { get; }

        public byte BehaviourIndex { get; }

        public uint MessageId { get; }
    }

    public readonly struct SpawnedObject
    {
        public SpawnedObject(uint objectId, uint prefabId, ulong sceneObjectId, uint prefabFingerprint, ulong ownerId, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states)
            : this(objectId, prefabId, sceneObjectId, prefabFingerprint, ownerId, position, rotation, scale, states, default)
        {
        }

        public SpawnedObject(uint objectId, uint prefabId, ulong sceneObjectId, uint prefabFingerprint, ulong ownerId, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states, NetworkScene scene)
            : this(objectId, prefabId, sceneObjectId, prefabFingerprint, ownerId, position, rotation, scale, states, scene, null)
        {
        }

        public SpawnedObject(uint objectId, uint prefabId, ulong sceneObjectId, uint prefabFingerprint, ulong ownerId, Vector3 position, Quaternion rotation, Vector3 scale, IReadOnlyList<ReadOnlyMemory<byte>> states, NetworkScene scene, IReadOnlyList<ReadOnlyMemory<byte>> collections)
        {
            Collections = collections ?? Array.Empty<ReadOnlyMemory<byte>>();
            Scene = scene;
            ObjectId = objectId;
            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            PrefabFingerprint = prefabFingerprint;
            OwnerId = ownerId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            States = states;
        }

        public uint ObjectId { get; }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public uint PrefabFingerprint { get; }

        public ulong OwnerId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public IReadOnlyList<ReadOnlyMemory<byte>> States { get; }

        public IReadOnlyList<ReadOnlyMemory<byte>> Collections { get; }

        public NetworkScene Scene { get; }
    }

    public interface IObjectSpawner
    {
        SpawnResult Spawn(in SpawnedObject spawned);

        void Despawn(uint objectId);
    }

    public readonly struct ObjectOwnerChangedArgs
    {
        public ObjectOwnerChangedArgs(uint objectId, ulong previousOwnerId, ulong ownerId)
        {
            ObjectId = objectId;
            PreviousOwnerId = previousOwnerId;
            OwnerId = ownerId;
        }

        public uint ObjectId { get; }

        public ulong PreviousOwnerId { get; }

        public ulong OwnerId { get; }
    }

    public readonly struct ObjectSceneMovedArgs
    {
        public ObjectSceneMovedArgs(uint objectId, NetworkScene previousScene, NetworkScene scene, uint tick)
        {
            ObjectId = objectId;
            PreviousScene = previousScene;
            Scene = scene;
            Tick = tick;
        }

        public uint ObjectId { get; }

        public NetworkScene PreviousScene { get; }

        public NetworkScene Scene { get; }

        public uint Tick { get; }
    }

    public readonly struct ObserverAddedArgs
    {
        public ObserverAddedArgs(uint objectId, ulong peerId)
        {
            ObjectId = objectId;
            PeerId = peerId;
        }

        public uint ObjectId { get; }

        public ulong PeerId { get; }
    }

    public readonly struct ObjectMismatchArgs
    {
        public ObjectMismatchArgs(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId)
            : this(kind, objectId, prefabId, sceneObjectId, default)
        {
        }

        public ObjectMismatchArgs(ObjectMismatchKind kind, uint objectId, uint prefabId, ulong sceneObjectId, NetworkScene scene)
        {
            Kind = kind;
            ObjectId = objectId;
            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            Scene = scene;
        }

        public ObjectMismatchKind Kind { get; }

        public uint ObjectId { get; }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public NetworkScene Scene { get; }
    }
}
