using System;
using System.Threading;
using Fomoxa.Net.Transports;
using Fomoxa.Net.WebSockets;
using Fomoxa.Rpc.Net;

namespace Fomoxa.Rpc.WebSockets
{
    public static class WebSocketReadiness
    {
        private static int registered;

        public static void Register()
        {
            if (Interlocked.Exchange(ref registered, 1) == 1)
            {
                return;
            }
            ShellReadiness.RegisterTransport(transport => For(transport as WebSocketTransport));
            ShellReadiness.RegisterListener(listener => listener is WebSocketListener webSocket ? new ListenerReadiness(webSocket) : null);
        }

        public static IReadiness? For(WebSocketTransport? transport) =>
            transport?.Socket is { } socket ? new TransportReadiness(transport, new SocketReadiness(socket)) : null;

        public static IListenerReadiness For(WebSocketListener listener) => new ListenerReadiness(listener);

        private sealed class TransportReadiness : IReadiness
        {
            private readonly WebSocketTransport transport;
            private readonly SocketReadiness socket;

            public TransportReadiness(WebSocketTransport transport, SocketReadiness socket)
            {
                this.transport = transport;
                this.socket = socket;
            }

            public bool ReportsWritable => true;

            public bool WaitReady(bool writable, TimeSpan timeout, Waker waker) =>
                transport.HasPendingInput || socket.WaitReady(writable || transport.HasPendingOutput, timeout, waker);
        }

        private sealed class ListenerReadiness : IListenerReadiness
        {
            private readonly WebSocketListener listener;

            public ListenerReadiness(WebSocketListener listener)
            {
                this.listener = listener;
            }

            public bool WaitReady(TimeSpan timeout, Waker waker) => listener.WaitReady(timeout, waker.Socket);
        }
    }
}
