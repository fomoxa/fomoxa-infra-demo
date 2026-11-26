using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking
{
    internal sealed class EntityPoseSource : ITransformSource, ITransformWriter, IOwnerBodies
    {
        private readonly EntityBehaviour behaviour;

        public EntityPoseSource(EntityBehaviour behaviour)
        {
            this.behaviour = behaviour;
        }

        internal bool Writable => behaviour.TransformSlot.Record?.Representation is IEntityPoseTarget;

        public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale) =>
            behaviour.TransformSlot.Record.Representation.ReadRootPose(out localPosition, out localRotation, out localScale);

        public void WriteLocal(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale) =>
            Target().WriteRootPose(localPosition, localRotation, localScale);

        public void CollectBodies(List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D) =>
            Target().CollectRootBodies(bodies, bodies2D);

        private IEntityPoseTarget Target() =>
            behaviour.TransformSlot.Record.Representation as IEntityPoseTarget
                ?? throw new InvalidOperationException($"{behaviour.GetType().FullName} synchronizes the pose of an entity that cannot be written");
    }
}
