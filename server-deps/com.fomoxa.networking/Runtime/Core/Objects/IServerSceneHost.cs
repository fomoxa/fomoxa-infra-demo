using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Objects
{
    public interface IServerSceneHost
    {
        bool TryLoad(NetworkScene scene, uint sceneId, ScenePhysics physics, Func<bool> accept, Action loaded, Action<Exception> failed);

        void Unload(NetworkScene scene, Action unloaded);

        void SceneObjectsOf(NetworkScene scene, List<INetworkEntity> found);

        bool Holds(NetworkScene scene, INetworkEntity entity);

        bool Knows(uint sceneId);

        void PresentSceneObjects(List<INetworkEntity> found);
    }
}
