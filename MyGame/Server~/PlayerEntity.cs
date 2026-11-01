using System;
using System.Numerics;
using Fomoxa.Networking;
using Fomoxa.Networking.Standalone;
using Generated;
using MyGame.Network.Models;

namespace MyGame.Network
{
    /// <summary>
    /// Bản console của prefab Player. Phải khớp Player.prefab:
    /// 2 behaviour đúng thứ tự (NetworkTransform rồi Player), cùng state/input model.
    /// </summary>
    public sealed class PlayerEntity : StandaloneEntity
    {
        public PlayerEntity(Func<double> tickDelta)
            : this(new PlayerTransform(), new Player(tickDelta))
        {
        }

        private PlayerEntity(PlayerTransform transform, Player player)
            : base(FomoxaPrefabs.PlayerId, new EntityBehaviour[] { transform, player })
        {
            player.Entity = this;
        }
    }

    /// <summary>
    /// Thay cho NetworkTransform: cùng cờ sync và ngưỡng như Player.prefab
    /// (syncPosition, syncRotation, syncScale=false, 0.01 / 0.1 / 0.01).
    /// Server không nhận pose nên phần Receive để trống.
    /// </summary>
    public sealed class PlayerTransform : EntityBehaviour, ITransformReceiver
    {
        public PlayerTransform()
        {
            SyncTransform(
                new TransformSync(new TransformSyncSettings(true, true, false, 0.01f, 0.1f, 0.01f)),
                this);
        }

        public void ResetReceive()
        {
        }

        public void Receive(in TransformSample sample)
        {
        }
    }

    /// <summary>
    /// Bản console của Assets/Network/Player.cs. Cùng hằng số (speed 6, bounds 23),
    /// cùng model MoveInput; khác là dùng System.Numerics thay UnityEngine.
    /// </summary>
    public sealed class Player : EntityBehaviour
    {
        private const float Speed = 6f;
        private const float Bounds = 23f;

        private readonly Func<double> tickDelta;

        public Player(Func<double> tickDelta)
        {
            this.tickDelta = tickDelta ?? (() => 1.0 / 30.0);
        }

        public StandaloneEntity Entity { get; set; }

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputNetAdapter.Instance, Gather, Apply);
        }

        private static void Gather(MoveInput input)
        {
            input.X = 0f;
            input.Z = 0f;
        }

        private void Apply(MoveInput input, InputContext context)
        {
            if (Entity == null)
            {
                return;
            }

            var direction = new Vector3(input.X, 0f, input.Z);
            float length = direction.Length();
            if (length > 1f)
            {
                direction /= length;
            }

            if (direction.LengthSquared() > 0.0001f)
            {
                float yaw = MathF.Atan2(direction.X, direction.Z);
                Entity.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            }

            Vector3 next = Entity.Position + direction * (Speed * (float)tickDelta());
            next.X = Math.Clamp(next.X, -Bounds, Bounds);
            next.Z = Math.Clamp(next.Z, -Bounds, Bounds);
            next.Y = 1f;
            Entity.Position = next;
        }
    }
}
