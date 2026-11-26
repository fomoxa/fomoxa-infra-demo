using System.Collections.Generic;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking
{
    internal interface IOwnerBodies
    {
        void CollectBodies(List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D);
    }
}
