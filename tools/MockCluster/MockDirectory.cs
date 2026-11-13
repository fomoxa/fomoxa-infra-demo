using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Fomoxa.Networking;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using Generated;

namespace MockCluster
{
    internal sealed class MockPod
    {
        public uint Id;
        public string Session = "";
        public string Address = "";
        public ushort Port;
        public string Target = "";
        public string Version = "";
        public uint MaxPlayers;
        public uint Players;
        public bool Draining;
        public DateTime LastSeen = DateTime.UtcNow;
        public byte[] TicketKey = Array.Empty<byte>();
        public ConnectTokens Tokens;
        public ItemStream<DrainOrder> DrainStream;
        public readonly HashSet<string> Rooms = new HashSet<string>();
    }

    internal sealed class RoomView
    {
        public string RoomKey = "";
        public uint Players;
        public uint Capacity;
    }

    /// <summary>Directory giả: nhận pod đăng ký, giữ room, cấp ticket (ConnectTokens) cho backend.</summary>
    internal sealed class MockDirectory : IDirectory
    {
        private static readonly TimeSpan PodTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DrainWindow = TimeSpan.FromSeconds(60);

        private readonly object gate = new object();
        private readonly FomoxaRegistry registry;
        private readonly bool strictVersion;
        private readonly List<MockPod> pods = new List<MockPod>();
        private uint nextId = 1;

        public MockDirectory(FomoxaRegistry registry, bool strictVersion)
        {
            this.registry = registry;
            this.strictVersion = strictVersion;
        }

        public int PodCount
        {
            get
            {
                lock (gate)
                {
                    Expire();
                    return pods.Count;
                }
            }
        }

        public IReadOnlyList<RoomView> Rooms()
        {
            lock (gate)
            {
                Expire();
                var rooms = new List<RoomView>();
                foreach (MockPod pod in pods.Where(p => !p.Draining))
                {
                    foreach (string room in pod.Rooms)
                    {
                        // Pod báo tổng người chơi của cả pod; với claim-one mỗi pod một room nên chính xác.
                        rooms.Add(new RoomView { RoomKey = room, Players = pod.Players, Capacity = pod.MaxPlayers });
                    }
                }

                return rooms.OrderBy(r => r.RoomKey, StringComparer.Ordinal).ToList();
            }
        }

        public AllocateResponse Allocate(CallContext context, AllocateRequest request) => Issue(request.RoomKey, request.Version, request.ApplicationData, request.Tickets);

        public AllocateResponse Issue(string roomKey, string version, byte[] data, uint tickets)
        {
            lock (gate)
            {
                Expire();
                MockPod pod = pods.FirstOrDefault(p => p.Rooms.Contains(roomKey) && !p.Draining);
                if (pod == null)
                {
                    throw new RpcException(Status.FailedPrecondition($"khong co pod nao giu room {roomKey}", new NoPodAvailable { Version = version ?? "" }));
                }

                if (strictVersion && !string.IsNullOrEmpty(version) && version != pod.Version)
                {
                    throw new RpcException(Status.FailedPrecondition($"pod chay phien ban {pod.Version}, client {version}", new NoPodAvailable { Version = version }));
                }

                var response = new AllocateResponse { Address = pod.Address, Port = pod.Port, WebSocketUrl = "" };
                uint count = Math.Max(1, tickets);
                for (uint i = 0; i < count; i++)
                {
                    response.Connections.Add(pod.Tokens.Issue(pod.Target, data ?? Array.Empty<byte>()));
                }

                return response;
            }
        }

        public AllocateResponse Relocate(CallContext context, RelocateRoom request) =>
            throw new RpcException(Status.FailedPrecondition("mock directory khong ho tro Relocate", new NoPodAvailable { Version = "" }));

        public PodRegistered RegisterPod(CallContext context, RegisterPod request)
        {
            lock (gate)
            {
                string session = context.Session.Principal;
                MockPod pod = request.PodId != 0 ? pods.FirstOrDefault(p => p.Id == request.PodId) : null;
                if (pod == null)
                {
                    pod = new MockPod { Id = nextId++, TicketKey = RandomNumberGenerator.GetBytes(ConnectTokens.KeyLength) };
                    pod.Tokens = new ConnectTokens(registry, pod.TicketKey);
                    pods.Add(pod);
                }

                pod.Session = session;
                pod.Address = request.Address;
                pod.Port = request.Port;
                pod.Target = request.Target;
                pod.Version = request.Version;
                pod.MaxPlayers = request.MaxPlayers;
                pod.LastSeen = DateTime.UtcNow;
                foreach (string room in request.Rooms)
                {
                    pod.Rooms.Add(room);
                }

                Console.WriteLine($"[Directory] pod #{pod.Id} '{pod.Target}' {pod.Address}:{pod.Port} v{pod.Version} registered");
                return new PodRegistered { PodId = pod.Id, ReportIntervalMs = 2000, TicketKey = pod.TicketKey };
            }
        }

        public void ReportLoad(CallContext context, PodLoad request)
        {
            lock (gate)
            {
                MockPod pod = BySession(context);
                if (pod != null)
                {
                    pod.Players = request.Players;
                    pod.LastSeen = DateTime.UtcNow;
                }
            }
        }

        public void AcquireRoom(CallContext context, AcquireRoom request)
        {
            lock (gate)
            {
                MockPod pod = BySession(context) ?? throw new RpcException(Status.FailedPrecondition("pod chua dang ky"));
                MockPod owner = pods.FirstOrDefault(p => p.Rooms.Contains(request.RoomKey));
                if (owner != null && owner != pod)
                {
                    throw new RpcException(Status.FailedPrecondition($"room {request.RoomKey} da co pod #{owner.Id} giu"));
                }

                pod.Rooms.Add(request.RoomKey);
                pod.LastSeen = DateTime.UtcNow;
                Console.WriteLine($"[Directory] pod #{pod.Id} holds room {request.RoomKey}");
            }
        }

        public void ReleaseRoom(CallContext context, ReleaseRoom request)
        {
            lock (gate)
            {
                BySession(context)?.Rooms.Remove(request.RoomKey);
            }
        }

        public void WatchDrain(ItemStream<DrainOrder> stream, DrainWatch request)
        {
            lock (gate)
            {
                MockPod pod = BySession(stream.Context);
                if (pod != null)
                {
                    pod.DrainStream = stream;
                    if (pod.Draining)
                    {
                        stream.Send(new DrainOrder { Deadline = DateTimeOffset.UtcNow.Add(DrainWindow).ToUnixTimeMilliseconds() });
                    }
                }
            }
        }

        public void RequestDrain(CallContext context, DrainSelf request)
        {
            lock (gate)
            {
                MockPod pod = BySession(context);
                if (pod == null)
                {
                    return;
                }

                pod.Draining = true;
                long deadline = DateTimeOffset.UtcNow.Add(DrainWindow).ToUnixTimeMilliseconds();
                pod.DrainStream?.Send(new DrainOrder { Deadline = deadline });
                Console.WriteLine($"[Directory] pod #{pod.Id} requested drain (window {DrainWindow.TotalSeconds:0}s)");
            }
        }

        public void WatchPods(ItemStream<PodChange> stream, PodWatch request)
        {
            // Không có gateway trong cụm giả: giữ stream mở, không gửi gì.
        }

        private MockPod BySession(CallContext context) => pods.FirstOrDefault(p => p.Session == context.Session.Principal);

        private void Expire()
        {
            DateTime now = DateTime.UtcNow;
            foreach (MockPod gone in pods.Where(p => now - p.LastSeen > PodTimeout).ToList())
            {
                pods.Remove(gone);
                Console.WriteLine($"[Directory] pod #{gone.Id} '{gone.Target}' lost heartbeat, removed from cluster");
            }
        }
    }

    /// <summary>Directory giả chấp nhận mọi key (chỉ dùng local); role lấy từ request.</summary>
    internal sealed class MockDirectoryAuth : IAuthenticator<DirectoryAuth, FRpcVoid>
    {
        public UnaryMethod<DirectoryAuth, FRpcVoid> Method => DirectoryAuthenticationMethods.Authenticate;

        public (Identity Identity, FRpcVoid Response) Authenticate(CallContext context, DirectoryAuth request)
        {
            string role = request.Role ?? "";
            string name = role + ":" + Guid.NewGuid().ToString("N");
            return (new Identity(name, new[] { role }), new FRpcVoid());
        }
    }
}
