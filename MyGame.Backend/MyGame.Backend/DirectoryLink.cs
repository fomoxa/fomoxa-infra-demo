using System;
using System.Text;
using System.Threading.Tasks;
using MyGame.Backend.Models;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Directory;
using Fomoxa.Rpc.Net;

namespace MyGame.Backend
{
    public sealed class DirectoryLink : IDisposable
    {
        private readonly string address;
        private readonly ConnectOptions options;
        private readonly Registry registry = DirectoryClient.Declare(DirectoryAuthenticationClient.Registry()).Build();
        private readonly object gate = new object();
        private Task<RpcConnection>? opening;
        private bool disposed;

        public DirectoryLink(string address, byte[] backendKey)
        {
            this.address = address;
            options = new ConnectOptions
            {
                Credential = Credential.Of(DirectoryAuthenticationMethods.Authenticate, new DirectoryAuth { Role = DirectoryRoles.Backend, Key = backendKey }),
            };
        }

        public async Task<RoomTicket> Ticket(string player, string roomKey, string version)
        {
            var request = new AllocateRequest
            {
                RoomKey = roomKey,
                Version = version,
                ApplicationData = Encoding.UTF8.GetBytes(player),
                Tickets = 1,
            };
            AllocateResponse allocation;
            try
            {
                allocation = await Allocate(request);
            }
            catch (RpcException problem) when (problem.Status.Details is NoPodAvailable missing)
            {
                throw new RpcException(Status.FailedPrecondition(problem.Status.Message, new NoRoomForVersion { Version = missing.Version }));
            }
            catch (RpcException problem)
            {
                throw new RpcException(new Status(problem.Status.Code, problem.Status.Message));
            }
            catch (ConnectException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, $"the directory is not reachable: {problem.Message}"));
            }
            catch (System.Net.Sockets.SocketException problem)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, $"the directory is not reachable: {problem.Message}"));
            }

            return new RoomTicket
            {
                Address = allocation.Address,
                Port = allocation.Port,
                Connection = allocation.Connections.Count > 0 ? allocation.Connections[0] : Array.Empty<byte>(),
                WebSocketUrl = allocation.WebSocketUrl,
            };
        }

        private async Task<AllocateResponse> Allocate(AllocateRequest request)
        {
            RpcConnection connection = await Open();
            try
            {
                return await new DirectoryClient(connection).AllocateAsync(request);
            }
            catch (RpcException problem) when (problem.Status.Code == StatusCode.Unavailable && !connection.IsOpen)
            {
                return await new DirectoryClient(await Open()).AllocateAsync(request);
            }
        }

        public void Dispose()
        {
            Task<RpcConnection>? last;
            lock (gate)
            {
                disposed = true;
                last = opening;
                opening = null;
            }

            if (last != null && last.IsCompletedSuccessfully)
            {
                last.Result.Dispose();
            }
        }

        private Task<RpcConnection> Open()
        {
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(DirectoryLink));
                }

                if (opening == null || opening.IsFaulted || opening.IsCanceled || (opening.IsCompletedSuccessfully && !opening.Result.IsOpen))
                {
                    if (opening != null && opening.IsCompletedSuccessfully)
                    {
                        opening.Result.Dispose();
                    }

                    opening = RpcConnection.Connect(address, FomoxaDirectoryCodecs.NetSchema.Build(), registry, options);
                }

                return opening;
            }
        }
    }
}
