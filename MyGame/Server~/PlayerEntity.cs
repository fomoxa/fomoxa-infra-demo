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
            Behaviour = player;
            transform.OnPose = position => player.Replicated = position;
        }

        /// <summary>Hành vi Player để đọc điểm số.</summary>
        public Player Behaviour { get; }
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

        /// <summary>Chỉ client giả lập (bot test) dùng: nhận vị trí do server gửi về.</summary>
        public Action<Vector3> OnPose { get; set; }

        public void Receive(in TransformSample sample)
        {
            OnPose?.Invoke(sample.LocalPosition);
        }
    }

    /// <summary>
    /// Bản console của Assets/Network/Player.cs. Dùng chung <see cref="Course.Simulate"/> nên di chuyển / nhảy /
    /// đáp bục khớp bản Unity; khác là dùng System.Numerics thay UnityEngine.
    /// </summary>
    public sealed class Player : EntityBehaviour
    {
        /// <summary>Chỉ dùng cho client giả lập (bot test): nếu đặt, thay cho đầu vào rỗng của Gather.</summary>
        public static Action<Player, MoveInput> BotGather { get; set; }

        /// <summary>Vị trí do server gửi về (chỉ có ở client giả lập).</summary>
        public Vector3? Replicated { get; set; }

        private readonly Func<double> tickDelta;
        private float verticalSpeed;

        public Player(Func<double> tickDelta)
        {
            this.tickDelta = tickDelta ?? (() => 1.0 / 30.0);
        }

        public StandaloneEntity Entity { get; set; }

        /// <summary>Số bậc cao nhất đã đứng lên (0 = chưa lên bục nào).</summary>
        public int Steps { get; private set; }

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputNetAdapter.Instance, Gather, Apply);
        }

        private void Gather(MoveInput input)
        {
            input.X = 0f;
            input.Z = 0f;
            input.Jump = false;
            BotGather?.Invoke(this, input);
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

            Vector3 position = Entity.Position;
            float x = position.X;
            float z = position.Z;
            float feet = position.Y - Course.HalfHeight;
            Course.Simulate(ref x, ref feet, ref z, ref verticalSpeed, direction.X, direction.Z, input.Jump, (float)tickDelta());
            Entity.Position = new Vector3(x, feet + Course.HalfHeight, z);

            if (verticalSpeed == 0f)
            {
                int step = Course.StepAt(x, z, feet);
                if (step > Steps)
                {
                    Steps = step;
                }
            }
        }
    }
}
