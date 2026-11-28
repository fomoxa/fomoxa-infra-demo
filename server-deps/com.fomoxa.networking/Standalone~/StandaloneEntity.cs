using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Standalone
{
    public class StandaloneEntity : INetworkEntity, IPoseSignals, IChildBodyPoses, IEntityPoseTarget
    {
        private static readonly Dictionary<Type, bool> PoseOverrides = new Dictionary<Type, bool>();
        private readonly EntityBehaviour[] behaviours;
        private readonly List<ChildBodyDesc> childDescs = new List<ChildBodyDesc>();
        private readonly List<ChildBodyDesc2D> childDescs2D = new List<ChildBodyDesc2D>();
        private readonly List<ChildBodyDesc> fileChildren = new List<ChildBodyDesc>();
        private readonly List<ChildBodyDesc2D> fileChildren2D = new List<ChildBodyDesc2D>();
        private readonly List<PhysicsBody> childBodies = new List<PhysicsBody>();
        private readonly List<PhysicsBody2D> childBodies2D = new List<PhysicsBody2D>();
        private readonly List<PhysicsBody> bodies = new List<PhysicsBody>();
        private readonly List<PhysicsBody2D> bodies2D = new List<PhysicsBody2D>();
        private readonly List<(BodyState State, BodyKind Kind)> movedStates = new List<(BodyState State, BodyKind Kind)>();
        private readonly List<(BodyState2D State, BodyKind Kind)> movedStates2D = new List<(BodyState2D State, BodyKind Kind)>();
        private bool childrenRead;
        private Vector3 position;
        private Quaternion rotation = Quaternion.Identity;
        private Vector3 scale = Vector3.One;
        private BodyDesc? fileBody;
        private BodyDesc2D? fileBody2D;
        private float? fileAngle;
        private bool attached;
        private readonly bool signalsPose;

        public StandaloneEntity(uint prefabId, IReadOnlyList<EntityBehaviour> behaviours)
            : this(prefabId, 0, 0, behaviours)
        {
        }

        internal StandaloneEntity(uint prefabId, ulong sceneObjectId, uint sceneFingerprint, IReadOnlyList<EntityBehaviour> behaviours)
        {
            if (behaviours == null)
            {
                throw new ArgumentNullException(nameof(behaviours));
            }

            PrefabId = prefabId;
            SceneObjectId = sceneObjectId;
            SceneFingerprint = sceneFingerprint;
            this.behaviours = new EntityBehaviour[behaviours.Count];
            for (int index = 0; index < behaviours.Count; index++)
            {
                this.behaviours[index] = behaviours[index];
            }

            signalsPose = !OverridesPose(GetType());
            EntityBehaviour.Attach(this);
        }

        public uint PrefabId { get; }

        public ulong SceneObjectId { get; }

        public NetworkScene Scene { get; set; }

        public bool DespawnWithOwner { get; set; } = true;

        public NetworkVisibility Visibility { get; set; } = NetworkVisibility.Rule;

        public IReadOnlyList<EntityBehaviour> EntityBehaviours => behaviours;

        public EntityRecord Record { get; private set; }

        public PhysicsBody Body { get; private set; }

        public PhysicsBody2D Body2D { get; private set; }

        public IReadOnlyList<PhysicsBody> Bodies => bodies;

        public IReadOnlyList<PhysicsBody2D> Bodies2D => bodies2D;

        public virtual Vector3 Position
        {
            get
            {
                if (Body.IsValid)
                {
                    return Body.Position;
                }

                if (Body2D.IsValid)
                {
                    Vector2 planar = Body2D.Position;
                    return new Vector3(planar.X, planar.Y, position.Z);
                }

                return position;
            }

            set
            {
                position = value;
                if (Body.IsValid)
                {
                    PhysicsBody body = Body;
                    body.Position = value;
                }
                else if (Body2D.IsValid)
                {
                    PhysicsBody2D body = Body2D;
                    body.Position = new Vector2(value.X, value.Y);
                }

                SignalPose();
            }
        }

        public virtual Quaternion Rotation
        {
            get
            {
                if (Body.IsValid)
                {
                    return Body.Rotation;
                }

                if (Body2D.IsValid)
                {
                    return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Body2D.Rotation);
                }

                return rotation;
            }

            set
            {
                rotation = value;
                fileAngle = null;
                if (Body.IsValid)
                {
                    PhysicsBody body = Body;
                    body.Rotation = value;
                }
                else if (Body2D.IsValid)
                {
                    PhysicsBody2D body = Body2D;
                    body.Rotation = AngleOf(value);
                }

                SignalPose();
            }
        }

        public virtual Vector3 Scale
        {
            get => scale;
            set
            {
                scale = value;
                SignalPose();
            }
        }

        internal uint SceneFingerprint { get; }

        bool IPoseSignals.SignalsPose => signalsPose;

        bool IPoseSignals.HasBody => Body.IsValid || Body2D.IsValid;

        public virtual bool TryGetBody(out BodyDesc body)
        {
            body = fileBody.GetValueOrDefault();
            return fileBody.HasValue;
        }

        public virtual bool TryGetBody2D(out BodyDesc2D body)
        {
            body = fileBody2D.GetValueOrDefault();
            return fileBody2D.HasValue;
        }

        public virtual void GetChildBodies(List<ChildBodyDesc> into)
        {
            into.AddRange(fileChildren);
        }

        public virtual void GetChildBodies2D(List<ChildBodyDesc2D> into)
        {
            into.AddRange(fileChildren2D);
        }

        internal void SetFileBodies(BodyDesc? body, BodyDesc2D? body2D, List<ChildBodyDesc> children, List<ChildBodyDesc2D> children2D)
        {
            fileBody = body;
            fileBody2D = body2D;
            fileAngle = body2D?.Rotation;
            fileChildren.Clear();
            fileChildren.AddRange(children);
            fileChildren2D.Clear();
            fileChildren2D.AddRange(children2D);
        }

        internal void AttachBodies(IPhysicsScenes physics)
        {
            Vector3 at = Position;
            Quaternion facing = Rotation;
            float angle = fileAngle ?? AngleOf(facing);
            ReadChildren();
            if (TryGetBody(out BodyDesc body))
            {
                Body = physics.AddBody(this, Scene, new BodyDesc(body.Kind, body.Colliders, at, facing, body.Mass, body.Motion));
                bodies.Add(Body);
            }

            if (TryGetBody2D(out BodyDesc2D body2D))
            {
                Body2D = physics.AddBody2D(this, Scene, new BodyDesc2D(body2D.Kind, body2D.Colliders, new Vector2(at.X, at.Y), angle, body2D.Mass, body2D.Motion));
                bodies2D.Add(Body2D);
            }

            foreach (ChildBodyDesc child in childDescs)
            {
                BodyDesc described = child.Body;
                Vector3 childPosition = at + Vector3.Transform(child.LocalPosition, facing);
                Quaternion childRotation = Quaternion.Normalize(facing * child.LocalRotation);
                PhysicsBody created = physics.AddBody(this, Scene, new BodyDesc(described.Kind, described.Colliders, childPosition, childRotation, described.Mass, described.Motion));
                childBodies.Add(created);
                bodies.Add(created);
            }

            foreach (ChildBodyDesc2D child in childDescs2D)
            {
                BodyDesc2D described = child.Body;
                Vector2 childPosition = new Vector2(at.X, at.Y) + Rotate(child.LocalPosition, angle);
                PhysicsBody2D created = physics.AddBody2D(this, Scene, new BodyDesc2D(described.Kind, described.Colliders, childPosition, angle + child.LocalRotation, described.Mass, described.Motion));
                childBodies2D.Add(created);
                bodies2D.Add(created);
            }

            attached = true;
        }

        internal void DetachBodies(IPhysicsScenes physics)
        {
            if (!attached)
            {
                return;
            }

            position = Position;
            rotation = Rotation;
            physics.RemoveBodies(this);
            Body = default;
            Body2D = default;
            childBodies.Clear();
            childBodies2D.Clear();
            bodies.Clear();
            bodies2D.Clear();
            attached = false;
        }

        internal void MoveTo(NetworkScene scene, IPhysicsScenes physics)
        {
            if (physics == null || !attached)
            {
                Scene = scene;
                return;
            }

            movedStates.Clear();
            movedStates2D.Clear();
            foreach (PhysicsBody body in bodies)
            {
                movedStates.Add((body.State, body.Kind));
            }

            foreach (PhysicsBody2D body in bodies2D)
            {
                movedStates2D.Add((body.State, body.Kind));
            }

            DetachBodies(physics);
            Scene = scene;
            AttachBodies(physics);
            for (int index = 0; index < bodies.Count && index < movedStates.Count; index++)
            {
                PhysicsBody body = bodies[index];
                if (body.Kind != movedStates[index].Kind)
                {
                    body.Kind = movedStates[index].Kind;
                }

                body.State = movedStates[index].State;
            }

            for (int index = 0; index < bodies2D.Count && index < movedStates2D.Count; index++)
            {
                PhysicsBody2D body = bodies2D[index];
                if (body.Kind != movedStates2D[index].Kind)
                {
                    body.Kind = movedStates2D[index].Kind;
                }

                body.State = movedStates2D[index].State;
            }

            movedStates.Clear();
            movedStates2D.Clear();
        }

        internal void PlaceProxies(IPhysicsScenes physics)
        {
            if (Body.IsValid)
            {
                physics.PlaceProxy(this, Body);
            }

            if (Body2D.IsValid)
            {
                physics.PlaceProxy(this, Body2D);
            }

            for (int index = 0; index < childBodies.Count; index++)
            {
                if (Synchronizes(ChildBody.Of(index)))
                {
                    physics.PlaceProxy(this, childBodies[index]);
                }
            }

            for (int index = 0; index < childBodies2D.Count; index++)
            {
                if (Synchronizes(ChildBody.Of2D(index)))
                {
                    physics.PlaceProxy(this, childBodies2D[index]);
                }
            }
        }

        void IChildBodyPoses.ReadChildPose(ChildBody body, out Vector3 localPosition, out Quaternion localRotation)
        {
            if (!childrenRead)
            {
                ReadChildren();
            }

            if (body.Is2D)
            {
                if (body.Index >= childDescs2D.Count)
                {
                    throw new InvalidOperationException($"{body} is not among the {childDescs2D.Count} 2D child bodies of {GetType().FullName}");
                }

                if (body.Index < childBodies2D.Count && childBodies2D[body.Index].IsValid)
                {
                    PhysicsBody2D child = childBodies2D[body.Index];
                    Vector2 planar = child.Position;
                    ToLocal(new Vector3(planar.X, planar.Y, Position.Z), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, child.Rotation), out localPosition, out localRotation);
                    return;
                }

                ChildBodyDesc2D declared = childDescs2D[body.Index];
                localPosition = new Vector3(declared.LocalPosition.X, declared.LocalPosition.Y, 0f);
                localRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, declared.LocalRotation);
                return;
            }

            if (body.Index >= childDescs.Count)
            {
                throw new InvalidOperationException($"{body} is not among the {childDescs.Count} child bodies of {GetType().FullName}");
            }

            if (body.Index < childBodies.Count && childBodies[body.Index].IsValid)
            {
                PhysicsBody child = childBodies[body.Index];
                ToLocal(child.Position, child.Rotation, out localPosition, out localRotation);
                return;
            }

            localPosition = childDescs[body.Index].LocalPosition;
            localRotation = childDescs[body.Index].LocalRotation;
        }

        void IEntityPoseTarget.WriteRootPose(in Vector3 worldPosition, in Quaternion worldRotation, in Vector3 localScale)
        {
            Position = worldPosition;
            Rotation = worldRotation;
            Scale = localScale;
        }

        void IEntityPoseTarget.WriteChildPose(ChildBody body, in Vector3 localPosition, in Quaternion localRotation)
        {
            Quaternion entityRotation = Rotation;
            Vector3 worldPosition = Position + Vector3.Transform(localPosition, entityRotation);
            Quaternion worldRotation = Quaternion.Normalize(entityRotation * localRotation);
            if (body.Is2D)
            {
                PhysicsBody2D child = ChildBody2DAt(body);
                if (child.IsValid)
                {
                    child.Position = new Vector2(worldPosition.X, worldPosition.Y);
                    child.Rotation = AngleOf(worldRotation);
                }
            }
            else
            {
                PhysicsBody child = ChildBodyAt(body);
                if (child.IsValid)
                {
                    child.Position = worldPosition;
                    child.Rotation = worldRotation;
                }
            }

            SignalPose();
        }

        void IEntityPoseTarget.CollectRootBodies(List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D)
        {
            if (Body.IsValid)
            {
                bodies.Add(Body);
            }

            if (Body2D.IsValid)
            {
                bodies2D.Add(Body2D);
            }
        }

        void IEntityPoseTarget.CollectChildBody(ChildBody body, List<PhysicsBody> bodies, List<PhysicsBody2D> bodies2D)
        {
            if (body.Is2D)
            {
                PhysicsBody2D child = ChildBody2DAt(body);
                if (child.IsValid)
                {
                    bodies2D.Add(child);
                }
            }
            else
            {
                PhysicsBody child = ChildBodyAt(body);
                if (child.IsValid)
                {
                    bodies.Add(child);
                }
            }
        }

        private PhysicsBody ChildBodyAt(ChildBody body) =>
            body.Index < childBodies.Count ? childBodies[body.Index] : default;

        private PhysicsBody2D ChildBody2DAt(ChildBody body) =>
            body.Index < childBodies2D.Count ? childBodies2D[body.Index] : default;

        private void ReadChildren()
        {
            childDescs.Clear();
            childDescs2D.Clear();
            GetChildBodies(childDescs);
            GetChildBodies2D(childDescs2D);
            childrenRead = true;
        }

        private bool Synchronizes(ChildBody body)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                if (behaviour.TransformSlot?.Source is ChildBodySource source && source.Body.Equals(body))
                {
                    return true;
                }
            }

            return false;
        }

        private void ToLocal(Vector3 worldPosition, Quaternion worldRotation, out Vector3 localPosition, out Quaternion localRotation)
        {
            Quaternion inverse = Quaternion.Inverse(Rotation);
            localPosition = Vector3.Transform(worldPosition - Position, inverse);
            localRotation = Quaternion.Normalize(inverse * worldRotation);
        }

        private static Vector2 Rotate(Vector2 value, float angle)
        {
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            return new Vector2((value.X * cos) - (value.Y * sin), (value.X * sin) + (value.Y * cos));
        }

        private void SignalPose()
        {
            EntityRecord record = Record;
            record?.Server?.SignalTransform(record);
        }

        private static bool OverridesPose(Type type)
        {
            lock (PoseOverrides)
            {
                if (!PoseOverrides.TryGetValue(type, out bool overrides))
                {
                    overrides = OverridesGetter(type, nameof(Position)) || OverridesGetter(type, nameof(Rotation)) || OverridesGetter(type, nameof(Scale));
                    PoseOverrides.Add(type, overrides);
                }

                return overrides;
            }
        }

        private static bool OverridesGetter(Type type, string property)
        {
            for (Type current = type; current != typeof(StandaloneEntity); current = current.BaseType)
            {
                MethodInfo getter = current.GetProperty(property, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetMethod;
                if (getter != null && getter.GetBaseDefinition().DeclaringType == typeof(StandaloneEntity))
                {
                    return true;
                }
            }

            return false;
        }

        private static float AngleOf(Quaternion rotation) => 2f * MathF.Atan2(rotation.Z, rotation.W);

        Vector3 INetworkEntity.ReadWorldPosition() => Position;

        void INetworkEntity.ReadRootPose(out Vector3 worldPosition, out Quaternion worldRotation, out Vector3 localScale)
        {
            worldPosition = Position;
            worldRotation = Rotation;
            localScale = Scale;
        }

        void INetworkEntity.Bind(EntityRecord record) => Record = record;

        void INetworkEntity.Unbind(EntityRecord record)
        {
            if (Record == record)
            {
                Record = null;
            }
        }
    }
}
