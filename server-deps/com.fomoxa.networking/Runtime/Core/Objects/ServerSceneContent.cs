using System;
using System.Collections.Generic;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ServerSceneContent
    {
        private readonly ServerSession session;
        private readonly ServerScenes scenes;
        private readonly ServerEntities entities;
        private readonly IServerSceneHost host;
        private readonly NetworkLog log;
        private readonly Dictionary<NetworkScene, uint> assets = new Dictionary<NetworkScene, uint>();
        private readonly HashSet<NetworkScene> loaded = new HashSet<NetworkScene>();
        private readonly HashSet<NetworkScene> loading = new HashSet<NetworkScene>();
        private readonly HashSet<NetworkScene> removedWhileLoading = new HashSet<NetworkScene>();
        private readonly List<NetworkScene> waiting = new List<NetworkScene>();
        private readonly Dictionary<uint, int> unloadingByAsset = new Dictionary<uint, int>();
        private int epoch;

        public ServerSceneContent(ServerSession session, ServerScenes scenes, ServerEntities entities, IServerSceneHost host, NetworkLog log)
        {
            this.session = session;
            this.scenes = scenes;
            this.entities = entities;
            this.host = host;
            this.log = log;
            scenes.OnSceneAdded += Load;
            scenes.OnSceneRemoved += Remove;
            session.OnServerConnectionState += ReleaseAllWhenStopped;
        }

        public event Action<NetworkScene> OnLoaded;

        public event Action<NetworkScene, Exception> OnLoadFailed;

        public event Action<NetworkScene> OnUnloaded;

        public bool IsLoading(NetworkScene scene) => loading.Contains(scene);

        public bool IsLoaded(NetworkScene scene) => loaded.Contains(scene);

        private void Load(NetworkScene scene)
        {
            uint sceneId = scenes.SceneIdOf(scene);
            assets[scene] = sceneId;
            loading.Add(scene);
            if (scenes.PhysicsOf(scene) == ScenePhysics.Shared && unloadingByAsset.ContainsKey(sceneId))
            {
                waiting.Add(scene);
                return;
            }

            StartLoad(scene, sceneId, scenes.PhysicsOf(scene));
        }

        private void StartLoad(NetworkScene scene, uint sceneId, ScenePhysics physics)
        {
            int attempt = epoch;
            if (!host.TryLoad(scene, sceneId, physics, () => Accept(scene, attempt), () => Finish(scene), exception => Fail(scene, exception, attempt)))
            {
                loading.Remove(scene);
                assets.Remove(scene);
            }
        }

        private bool Accept(NetworkScene scene, int attempt)
        {
            if (attempt != epoch)
            {
                return false;
            }

            loading.Remove(scene);
            if (removedWhileLoading.Remove(scene) || session.State != ServerState.Started)
            {
                assets.Remove(scene);
                return false;
            }

            loaded.Add(scene);
            return true;
        }

        private void Finish(NetworkScene scene)
        {
            var found = new List<INetworkEntity>();
            host.SceneObjectsOf(scene, found);
            entities.SpawnSceneObjects(found);
            OnLoaded?.Invoke(scene);
        }

        private void Fail(NetworkScene scene, Exception exception, int attempt)
        {
            if (attempt != epoch)
            {
                return;
            }

            loading.Remove(scene);
            removedWhileLoading.Remove(scene);
            assets.Remove(scene);
            log.Exception(exception);
            OnLoadFailed?.Invoke(scene, exception);
        }

        private void Remove(NetworkScene scene)
        {
            if (waiting.Remove(scene))
            {
                loading.Remove(scene);
                assets.Remove(scene);
                return;
            }

            if (loading.Contains(scene))
            {
                removedWhileLoading.Add(scene);
                return;
            }

            if (loaded.Remove(scene))
            {
                Release(scene);
            }
        }

        private void Release(NetworkScene scene)
        {
            assets.Remove(scene, out uint sceneId);
            unloadingByAsset[sceneId] = unloadingByAsset.TryGetValue(sceneId, out int unloading) ? unloading + 1 : 1;
            entities.ForgetSceneObjects(entity => host.Holds(scene, entity));
            host.Unload(scene, () => FinishUnload(scene, sceneId));
        }

        private void FinishUnload(NetworkScene scene, uint sceneId)
        {
            if (unloadingByAsset[sceneId] > 1)
            {
                unloadingByAsset[sceneId]--;
            }
            else
            {
                unloadingByAsset.Remove(sceneId);
                StartWaiting(sceneId);
            }

            OnUnloaded?.Invoke(scene);
        }

        private void StartWaiting(uint sceneId)
        {
            List<NetworkScene> ready = waiting.FindAll(scene => assets[scene] == sceneId);
            waiting.RemoveAll(scene => assets[scene] == sceneId);
            foreach (NetworkScene scene in ready)
            {
                StartLoad(scene, sceneId, ScenePhysics.Shared);
            }
        }

        private void ReleaseAllWhenStopped(ServerConnectionStateArgs args)
        {
            if (args.State != ServerState.Stopped)
            {
                return;
            }

            epoch++;
            foreach (NetworkScene scene in loading)
            {
                if (!loaded.Contains(scene))
                {
                    assets.Remove(scene);
                }
            }

            loading.Clear();
            removedWhileLoading.Clear();
            waiting.Clear();
            var releasing = new List<NetworkScene>(loaded);
            loaded.Clear();
            foreach (NetworkScene scene in releasing)
            {
                Release(scene);
            }
        }
    }
}
