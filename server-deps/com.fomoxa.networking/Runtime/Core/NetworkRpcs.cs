using System;
using System.ComponentModel;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking
{
    public sealed class NetworkRpcs
    {
        private readonly EntityBehaviour behaviour;
        private readonly RpcMessageIds rpcIds;

        internal NetworkRpcs(EntityBehaviour behaviour, RpcMessageIds rpcIds)
        {
            this.behaviour = behaviour;
            this.rpcIds = rpcIds;
        }

        public void OnServer<T>(IMessageCodec<T> codec, Action<ulong, T> handler, bool requireOwnership = true)
            where T : new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            behaviour.AddServerRpc(codec.MessageId, new ServerRpc<T>(codec, handler, requireOwnership));
        }

        public void OnClient<T>(IMessageCodec<T> codec, Action<T> handler)
            where T : new()
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            behaviour.AddClientRpc(codec.MessageId, new ClientRpc<T>(codec, handler));
        }

        public void OnServer(string rpc, Action<ulong> handler, bool requireOwnership = true)
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            behaviour.AddServerRpc(MessageIdOf(rpc), new EmptyServerRpc(handler, requireOwnership));
        }

        public void OnClient(string rpc, Action handler)
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            behaviour.AddClientRpc(MessageIdOf(rpc), new EmptyClientRpc(handler));
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Pack<TValues, TModel>(string rpc, IMessageCodec<TModel> codec, Action<TValues, TModel> pack)
            where TModel : class, new()
        {
            if (rpc == null)
            {
                throw new ArgumentNullException(nameof(rpc));
            }

            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            behaviour.AddPacker(rpc, new RpcPacker<TValues, TModel>(codec, pack));
        }

        internal static string NotGeneratedValues(Type type, string rpc) =>
            $"{type.FullName}.{rpc} has no generated RPC model for its parameters; run Tools > Fomoxa > Generate";

        internal static string NotGenerated(Type type, string rpc) =>
            $"{type.FullName}.{rpc} has no generated parameterless RPC model; mark the method with [ServerRpc] or [ClientRpc] and run Tools > Fomoxa > Generate";

        private uint MessageIdOf(string rpc)
        {
            if (!rpcIds.TryGet(behaviour.DeclaringType, rpc, out uint messageId))
            {
                throw new HandlerRegistrationException(NotGenerated(behaviour.DeclaringType, rpc));
            }

            return messageId;
        }
    }

    internal abstract class RpcPacker
    {
        public abstract Type ValuesType { get; }
    }

    internal abstract class RpcPacker<TValues> : RpcPacker
    {
        public override Type ValuesType => typeof(TValues);

        public abstract uint MessageId { get; }

        public abstract ReadOnlyMemory<byte> Encode(TValues values);
    }

    internal sealed class RpcPacker<TValues, TModel> : RpcPacker<TValues>
        where TModel : class, new()
    {
        private readonly IMessageCodec<TModel> codec;
        private readonly Action<TValues, TModel> pack;
        private readonly TModel model = new TModel();

        public RpcPacker(IMessageCodec<TModel> codec, Action<TValues, TModel> pack)
        {
            this.codec = codec;
            this.pack = pack;
        }

        public override uint MessageId => codec.MessageId;

        public override ReadOnlyMemory<byte> Encode(TValues values)
        {
            pack(values, model);
            return codec.Encode(model);
        }
    }

    internal abstract class ServerRpc
    {
        protected ServerRpc(bool requireOwnership)
        {
            RequireOwnership = requireOwnership;
        }

        public bool RequireOwnership { get; }

        public abstract void Invoke(ulong peerId, ReadOnlyMemory<byte> body);
    }

    internal sealed class ServerRpc<T> : ServerRpc
        where T : new()
    {
        private readonly IMessageCodec<T> codec;
        private readonly Action<ulong, T> handler;

        public ServerRpc(IMessageCodec<T> codec, Action<ulong, T> handler, bool requireOwnership)
            : base(requireOwnership)
        {
            this.codec = codec;
            this.handler = handler;
        }

        public override void Invoke(ulong peerId, ReadOnlyMemory<byte> body)
        {
            var value = new T();
            codec.Decode(body, ref value);
            handler(peerId, value);
        }
    }

    internal sealed class EmptyServerRpc : ServerRpc
    {
        private readonly Action<ulong> handler;

        public EmptyServerRpc(Action<ulong> handler, bool requireOwnership)
            : base(requireOwnership)
        {
            this.handler = handler;
        }

        public override void Invoke(ulong peerId, ReadOnlyMemory<byte> body) => handler(peerId);
    }

    internal abstract class ClientRpc
    {
        public abstract void Invoke(ReadOnlyMemory<byte> body);
    }

    internal sealed class ClientRpc<T> : ClientRpc
        where T : new()
    {
        private readonly IMessageCodec<T> codec;
        private readonly Action<T> handler;

        public ClientRpc(IMessageCodec<T> codec, Action<T> handler)
        {
            this.codec = codec;
            this.handler = handler;
        }

        public override void Invoke(ReadOnlyMemory<byte> body)
        {
            var value = new T();
            codec.Decode(body, ref value);
            handler(value);
        }
    }

    internal sealed class EmptyClientRpc : ClientRpc
    {
        private readonly Action handler;

        public EmptyClientRpc(Action handler)
        {
            this.handler = handler;
        }

        public override void Invoke(ReadOnlyMemory<byte> body) => handler();
    }
}
