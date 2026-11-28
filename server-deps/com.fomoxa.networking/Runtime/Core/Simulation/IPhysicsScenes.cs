using System.Collections.Generic;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Simulation
{
    public interface IPhysicsScenes : IPhysicsWorlds
    {
        void LoadScene(NetworkScene scene, SceneFile file);

        void UnloadScene(NetworkScene scene);

        PhysicsBody AddBody(INetworkEntity entity, NetworkScene scene, in BodyDesc body);

        PhysicsBody2D AddBody2D(INetworkEntity entity, NetworkScene scene, in BodyDesc2D body);

        void RemoveBodies(INetworkEntity entity);

        StaticGroup AddStatic(NetworkScene scene, IReadOnlyList<ColliderDesc> colliders);

        StaticGroup AddStatic2D(NetworkScene scene, IReadOnlyList<ColliderDesc2D> colliders);

        void RemoveStatic(StaticGroup group);

        PhysicsBody AddBody(NetworkScene scene, in BodyDesc body);

        PhysicsBody2D AddBody2D(NetworkScene scene, in BodyDesc2D body);

        void RemoveBody(PhysicsBody body);

        void RemoveBody2D(PhysicsBody2D body);

        void WorldsOf(INetworkEntity entity, List<IPhysicsSimulation> worlds);

        PhysicsHistory HistoryOf(IPhysicsSimulation world, int capacity);

        void PlaceProxy(INetworkEntity entity, PhysicsBody body);

        void PlaceProxy(INetworkEntity entity, PhysicsBody2D body);

        void EndProxy(INetworkEntity entity);
    }
}
