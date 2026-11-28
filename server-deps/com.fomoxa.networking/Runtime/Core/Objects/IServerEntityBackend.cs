namespace Fomoxa.Networking.Objects
{
    public interface IServerEntityBackend
    {
        string NameOf(INetworkEntity entity);

        void ValidateSpawn(INetworkEntity entity);

        uint FingerprintOf(INetworkEntity entity, bool isSceneObject);

        NetworkScene SceneOf(INetworkEntity entity);

        void MoveToScene(INetworkEntity entity, NetworkScene scene);

        void PrepareSpawn(INetworkEntity entity);

        void Activate(INetworkEntity entity);

        void End(INetworkEntity entity, bool isSceneObject);
    }
}
