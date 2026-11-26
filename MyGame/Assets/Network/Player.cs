using System;
using Fomoxa.Networking;
using Fomoxa.Unity;
using MyGame.Network.Models;
using UnityEngine;

namespace MyGame.Network
{
    [NetworkInputModel("MoveInput")]
    public sealed partial class Player : NetworkBehaviour
    {
        private float verticalSpeed;

        [NetworkInput]
        private float x;

        [NetworkInput]
        private float z;

        [NetworkInput]
        private bool jump;

        /// <summary>Số bậc cao nhất đã đứng lên (0 = chưa lên bục nào).</summary>
        public int Steps { get; private set; }
        private bool bot;
        private bool trace;
        private float nextTrace;

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            bot = Array.IndexOf(args, "-bot") >= 0;
            trace = Array.IndexOf(args, "-trace") >= 0;
        }

        public override void OnStartServer()
        {
            Debug.Log($"[Player] spawn on server id={NetworkObject.ObjectId} owner={NetworkObject.OwnerId}");
        }

        public override void OnStartClient()
        {
            Debug.Log($"[Player] spawn on client id={NetworkObject.ObjectId} owner={NetworkObject.OwnerId} mine={NetworkObject.IsOwner} pos={transform.position}");
        }

        private void Update()
        {
            if (!trace || Time.time < nextTrace)
            {
                return;
            }

            nextTrace = Time.time + 2f;
            Debug.Log($"[Pos] id={NetworkObject.ObjectId} owner={NetworkObject.OwnerId} pos={transform.position}");
        }

        protected override void OnGatherInput()
        {
            if (bot)
            {
                float time = Time.time;
                x = Mathf.Cos(time);
                z = Mathf.Sin(time);
                jump = Mathf.Repeat(time, 1.5f) < 0.1f;
                return;
            }

            // Di chuyển theo hướng camera: W = tiến về phía camera đang nhìn. Server vẫn nhận hướng theo trục thế giới.
            Vector3 move = Quaternion.Euler(0f, PlayerCamera.Yaw, 0f) * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            x = move.x;
            z = move.z;
            jump = Input.GetKey(KeyCode.Space);
        }

        protected override void OnApplyInput(InputContext context)
        {
            Vector3 direction = Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            Vector3 position = transform.position;
            float positionX = position.x;
            float positionZ = position.z;
            float feet = position.y - Course.HalfHeight;
            Course.Simulate(ref positionX, ref feet, ref positionZ, ref verticalSpeed, direction.x, direction.z, jump, (float)NetworkManager.TimeManager.TickDelta);
            transform.position = new Vector3(positionX, feet + Course.HalfHeight, positionZ);

            if (verticalSpeed == 0f)
            {
                int step = Course.StepAt(positionX, positionZ, feet);
                if (step > Steps)
                {
                    Steps = step;
                }
            }
        }
    }
}
