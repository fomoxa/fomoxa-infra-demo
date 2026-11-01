using System;
using Fomoxa.Networking;
using Fomoxa.Unity;
using Generated;
using MyGame.Network.Models;
using UnityEngine;

namespace MyGame.Network
{
    public sealed class Player : NetworkBehaviour
    {
        private NetworkManager manager;
        private float verticalSpeed;

        /// <summary>Số bậc cao nhất đã đứng lên (0 = chưa lên bục nào).</summary>
        public int Steps { get; private set; }
        private bool bot;
        private bool trace;
        private float nextTrace;

        private void Awake()
        {
            manager = FindAnyObjectByType<NetworkManager>();
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

        protected override void OnRegisterInput(NetworkInput input)
        {
            input.Use(MoveInputNetAdapter.Instance, Gather, Apply);
        }

        private void Gather(MoveInput input)
        {
            if (bot)
            {
                float time = Time.time;
                input.X = Mathf.Cos(time);
                input.Z = Mathf.Sin(time);
                input.Jump = Mathf.Repeat(time, 1.5f) < 0.1f;
                return;
            }

            // Di chuyển theo hướng camera: W = tiến về phía camera đang nhìn. Server vẫn nhận hướng theo trục thế giới.
            Vector3 move = Quaternion.Euler(0f, PlayerCamera.Yaw, 0f) * new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input.X = move.x;
            input.Z = move.z;
            input.Jump = Input.GetKey(KeyCode.Space);
        }

        private void Apply(MoveInput input, InputContext context)
        {
            if (manager == null)
            {
                return;
            }

            Vector3 direction = Vector3.ClampMagnitude(new Vector3(input.X, 0f, input.Z), 1f);
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            Vector3 position = transform.position;
            float x = position.x;
            float z = position.z;
            float feet = position.y - Course.HalfHeight;
            Course.Simulate(ref x, ref feet, ref z, ref verticalSpeed, direction.x, direction.z, input.Jump, (float)manager.TimeManager.TickDelta);
            transform.position = new Vector3(x, feet + Course.HalfHeight, z);

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
