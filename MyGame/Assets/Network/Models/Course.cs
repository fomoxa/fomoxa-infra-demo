using System;

namespace MyGame.Network.Models
{
    /// <summary>
    /// Đường leo "bục nhảy": các bục xếp xoắn ốc, mỗi bục cao hơn bục trước <see cref="StepHeight"/>.
    /// Dùng chung cho client Unity và server console nên chỉ chứa float và System - không phụ thuộc Unity.
    /// Bục là "một chiều": nhảy xuyên từ dưới lên được, đáp xuống từ trên thì đứng được, nhưng đi ngang từ bên cạnh thì bị chặn.
    /// Điểm của người chơi = số bậc (chỉ số bục) cao nhất đã đứng lên.
    /// </summary>
    public static class Course
    {
        public const int Count = 18;
        public const float StepHeight = 1.4f;
        public const float PlatformHalf = 1.75f;
        public const float Thickness = 0.4f;
        public const float Radius = 7f;
        public const float AngleStepDegrees = 40f;

        public const float Speed = 6f;
        public const float Bounds = 23f;
        public const float Gravity = 25f;
        public const float JumpSpeed = 9.5f;
        public const float PlayerRadius = 0.5f;
        public const float PlayerHeight = 2f;

        /// <summary>Tâm capsule cao hơn chân đúng một nửa chiều cao.</summary>
        public const float HalfHeight = PlayerHeight / 2f;

        private const float Epsilon = 0.05f;

        private static readonly float[] CenterX = new float[Count];
        private static readonly float[] CenterZ = new float[Count];

        static Course()
        {
            for (int i = 0; i < Count; i++)
            {
                double angle = i * AngleStepDegrees * Math.PI / 180.0;
                CenterX[i] = (float)(Math.Sin(angle) * Radius);
                CenterZ[i] = (float)(Math.Cos(angle) * Radius);
            }
        }

        /// <summary>Bục thứ <paramref name="step"/> (1..Count): tâm X, Z và độ cao mặt trên.</summary>
        public static void Platform(int step, out float x, out float z, out float top)
        {
            x = CenterX[step - 1];
            z = CenterZ[step - 1];
            top = step * StepHeight;
        }

        /// <summary>Độ cao mặt đất dưới chân: mặt trên cao nhất của bục mà chân đang ở trên hoặc ngang; không có thì 0 (sàn).</summary>
        public static float GroundHeight(float x, float z, float feetY)
        {
            float ground = 0f;
            for (int i = 0; i < Count; i++)
            {
                float top = (i + 1) * StepHeight;
                if (top <= feetY + Epsilon && top > ground && Inside(i, x, z, 0f))
                {
                    ground = top;
                }
            }

            return ground;
        }

        /// <summary>Chỉ số bục (1..Count) mà người chơi đang đứng trên đó; 0 nếu đang ở sàn.</summary>
        public static int StepAt(float x, float z, float feetY)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                float top = (i + 1) * StepHeight;
                if (Math.Abs(top - feetY) <= Epsilon && Inside(i, x, z, 0f))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        /// <summary>Đi tới (x, z) ở độ cao chân này có đâm ngang vào thân một bục không.</summary>
        public static bool Blocked(float x, float z, float feetY)
        {
            for (int i = 0; i < Count; i++)
            {
                float top = (i + 1) * StepHeight;
                float bottom = top - Thickness;
                bool overlapsVertically = feetY < top - Epsilon && feetY + PlayerHeight > bottom;
                if (overlapsVertically && Inside(i, x, z, PlayerRadius))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Một tick di chuyển + nhảy: dùng chung cho Unity (host) và server console để hai bên luôn cho cùng kết quả.
        /// (dirX, dirZ) đã được giới hạn độ dài ≤ 1.
        /// </summary>
        public static void Simulate(ref float x, ref float feet, ref float z, ref float vy, float dirX, float dirZ, bool jump, float dt)
        {
            float nx = Math.Min(Bounds, Math.Max(-Bounds, x + dirX * Speed * dt));
            float nz = Math.Min(Bounds, Math.Max(-Bounds, z + dirZ * Speed * dt));
            if (Blocked(nx, nz, feet))
            {
                // Trượt dọc theo cạnh bục nếu một trục còn đi được.
                if (!Blocked(nx, z, feet))
                {
                    nz = z;
                }
                else if (!Blocked(x, nz, feet))
                {
                    nx = x;
                }
                else
                {
                    nx = x;
                    nz = z;
                }
            }

            float ground = GroundHeight(nx, nz, feet);
            bool grounded = vy <= 0f && feet <= ground + 0.001f;
            if (jump && grounded)
            {
                vy = JumpSpeed;
                grounded = false;
            }

            if (grounded)
            {
                feet = ground;
                vy = 0f;
            }
            else
            {
                vy -= Gravity * dt;
                float next = feet + vy * dt;
                if (vy <= 0f && next <= ground)
                {
                    next = ground;
                    vy = 0f;
                }

                feet = next;
            }

            x = nx;
            z = nz;
        }

        private static bool Inside(int index, float x, float z, float margin) =>
            Math.Abs(x - CenterX[index]) <= PlatformHalf + margin && Math.Abs(z - CenterZ[index]) <= PlatformHalf + margin;
    }
}
