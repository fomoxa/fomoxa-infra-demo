using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    internal sealed class StandaloneServerEntityBackend : IServerEntityBackend
    {
        private readonly StandalonePrefabs prefabs;

        private readonly IPhysicsScenes physics;

        public StandaloneServerEntityBackend(StandalonePrefabs prefabs, IPhysicsScenes physics)
        {
            this.physics = physics;
            this.prefabs = prefabs;
        }

        public string NameOf(INetworkEntity entity) =>
            entity.SceneObjectId != 0 ? $"scene object 0x{entity.SceneObjectId:X16}" : $"prefab 0x{entity.PrefabId:X8}";

        public void ValidateSpawn(INetworkEntity entity)
        {
            if (!(entity is StandaloneEntity))
            {
                throw new ArgumentException($"{entity.GetType().Name} is not a StandaloneEntity; the standalone backend spawns StandaloneEntity and its subclasses", nameof(entity));
            }
        }

        public uint FingerprintOf(INetworkEntity entity, bool isSceneObject)
        {
            if (isSceneObject)
            {
                return ((StandaloneEntity)entity).SceneFingerprint;
            }

            if (prefabs.TryGet(entity.PrefabId, out uint fingerprint, out Func<StandaloneEntity> _))
            {
                return fingerprint;
            }

            throw new ArgumentException($"prefab id 0x{entity.PrefabId:X8} is not registered with StandalonePrefabs", nameof(entity));
        }

        public NetworkScene SceneOf(INetworkEntity entity) => ((StandaloneEntity)entity).Scene;

        public Func<NetworkScene, bool> HasScene { get; set; }

        public void MoveToScene(INetworkEntity entity, NetworkScene scene)
        {
            if (scene != default && (HasScene == null || !HasScene(scene)))
            {
                throw new InvalidOperationException($"{scene} is still loading; move objects into it once it has loaded");
            }

            ((StandaloneEntity)entity).MoveTo(scene, physics);
        }

        public void PrepareSpawn(INetworkEntity entity)
        {
            var standalone = (StandaloneEntity)entity;
            if (physics != null)
            {
                standalone.AttachBodies(physics);
            }
        }

        public void Activate(INetworkEntity entity)
        {
        }

        public void End(INetworkEntity entity, bool isSceneObject)
        {
            if (physics != null)
            {
                ((StandaloneEntity)entity).DetachBodies(physics);
            }
        }
    }

    internal sealed class StandaloneServerSceneHost : IServerSceneHost
    {
        private readonly FomoxaRegistry registry;
        private readonly ISceneFiles files;
        private readonly StandaloneBehaviours behaviours;
        private readonly Dictionary<NetworkScene, List<StandaloneEntity>> scenes = new Dictionary<NetworkScene, List<StandaloneEntity>>();
        private readonly List<SceneFile> bootFiles = new List<SceneFile>();
        private readonly List<StandaloneEntity> bootObjects = new List<StandaloneEntity>();

        private readonly IPhysicsScenes physics;

        public StandaloneServerSceneHost(FomoxaRegistry registry, ISceneFiles files, StandaloneBehaviours behaviours, IPhysicsScenes physics, IReadOnlyList<SceneFile> bootScenes)
        {
            this.physics = physics;
            this.registry = registry;
            this.files = files;
            this.behaviours = behaviours;
            bootFiles.AddRange(bootScenes);
        }

        public bool Knows(uint sceneId) => files.Knows(sceneId);

        public void PresentSceneObjects(List<INetworkEntity> found)
        {
            bootObjects.Clear();
            foreach (SceneFile file in bootFiles)
            {
                List<StandaloneEntity> sceneObjects = StandaloneSceneObjects.Build(file, behaviours);
                bootObjects.AddRange(sceneObjects);
                found.AddRange(sceneObjects);
            }
        }

        public bool Has(NetworkScene scene) => scenes.ContainsKey(scene);

        public bool TryLoad(NetworkScene scene, uint sceneId, ScenePhysics scenePhysics, Func<bool> accept, Action loaded, Action<Exception> failed)
        {
            if (!files.Knows(sceneId))
            {
                return false;
            }

            SceneFile file;
            List<StandaloneEntity> sceneObjects;
            try
            {
                file = StandaloneSceneObjects.Read(registry, files, sceneId);
                sceneObjects = StandaloneSceneObjects.Build(file, behaviours);
            }
            catch (Exception exception)
            {
                failed(exception);
                return true;
            }

            foreach (StandaloneEntity sceneObject in sceneObjects)
            {
                sceneObject.Scene = scene;
            }

            if (!accept())
            {
                return true;
            }

            scenes.Add(scene, sceneObjects);
            physics?.LoadScene(scene, file);
            loaded();
            return true;
        }

        public void Unload(NetworkScene scene, Action unloaded)
        {
            if (scenes.Remove(scene))
            {
                physics?.UnloadScene(scene);
            }

            unloaded();
        }

        public void SceneObjectsOf(NetworkScene scene, List<INetworkEntity> found)
        {
            if (scenes.TryGetValue(scene, out List<StandaloneEntity> sceneObjects))
            {
                found.AddRange(sceneObjects);
            }
        }

        public bool Holds(NetworkScene scene, INetworkEntity entity) =>
            entity is StandaloneEntity standalone
            && scenes.TryGetValue(scene, out List<StandaloneEntity> sceneObjects)
            && sceneObjects.Contains(standalone);
    }
}
