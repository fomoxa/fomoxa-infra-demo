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
        [SerializeField] private float speed = 6f;
        [SerializeField] private float bounds = 23f;

        private NetworkManager manager;
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
                return;
            }

            input.X = Input.GetAxisRaw("Horizontal");
            input.Z = Input.GetAxisRaw("Vertical");
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

            Vector3 next = transform.position + direction * speed * (float)manager.TimeManager.TickDelta;
            next.x = Mathf.Clamp(next.x, -bounds, bounds);
            next.z = Mathf.Clamp(next.z, -bounds, bounds);
            next.y = 1f;
            transform.position = next;
        }
    }
}
