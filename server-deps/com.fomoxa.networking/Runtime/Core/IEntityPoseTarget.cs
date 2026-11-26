using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking
{
    internal interface IEntityPoseTarget
    {
        void WriteRootPose(in Vector3 worldPosition, in Quaternion worldRotation, in Vector3 localScale);

        void WriteChildPose(ChildBody body, in Vector3 localPosition, in Quaternion localRotation);

        void CollectRootBodies(List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D);

        void CollectChildBody(ChildBody body, List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D);
    }
}
