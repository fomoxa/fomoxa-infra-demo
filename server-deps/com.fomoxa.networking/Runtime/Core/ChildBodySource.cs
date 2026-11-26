using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking
{
    internal sealed class ChildBodySource : ITransformSource, ITransformWriter, IOwnerBodies
    {
        private readonly EntityBehaviour behaviour;
        private readonly ChildBody body;

        public ChildBodySource(EntityBehaviour behaviour, ChildBody body)
        {
            this.behaviour = behaviour;
            this.body = body;
        }

        public ChildBody Body => body;

        internal bool Writable => behaviour.TransformSlot.Record?.Representation is IEntityPoseTarget;

        public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
        {
            if (!(behaviour.TransformSlot.Record.Representation is IChildBodyPoses poses))
            {
                throw new InvalidOperationException($"{behaviour.GetType().FullName} synchronizes {body}, but its entity has no child bodies");
            }

            poses.ReadChildPose(body, out localPosition, out localRotation);
            localScale = Vector3.One;
        }

        public void WriteLocal(in Vector3 localPosition, in Quaternion localRotation, in Vector3 localScale) =>
            Target().WriteChildPose(body, localPosition, localRotation);

        public void CollectBodies(List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D) =>
            Target().CollectChildBody(body, bodies, bodies2D);

        private IEntityPoseTarget Target() =>
            behaviour.TransformSlot.Record.Representation as IEntityPoseTarget
                ?? throw new InvalidOperationException($"{behaviour.GetType().FullName} synchronizes {body}, but its entity cannot be written");
    }
}
