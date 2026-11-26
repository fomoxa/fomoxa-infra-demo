using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class TransformProtocol
    {
        public TransformProtocol(IMessageCodec<TransformUpdate> updateCodec, IMessageCodec<TransformSettle> settleCodec, IMessageCodec<TransformAuthority> authorityCodec, MessageChannels channels)
        {
            UpdateCodec = updateCodec ?? throw new ArgumentNullException(nameof(updateCodec));
            SettleCodec = settleCodec ?? throw new ArgumentNullException(nameof(settleCodec));
            AuthorityCodec = authorityCodec ?? throw new ArgumentNullException(nameof(authorityCodec));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            ObjectProtocol.EnsureReliable(channels, settleCodec.MessageId, nameof(TransformSettle));
            ObjectProtocol.EnsureReliable(channels, authorityCodec.MessageId, nameof(TransformAuthority));
        }

        public IMessageCodec<TransformUpdate> UpdateCodec { get; }

        public IMessageCodec<TransformSettle> SettleCodec { get; }

        public IMessageCodec<TransformAuthority> AuthorityCodec { get; }

        public static TransformProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new TransformProtocol(registry.Codec<TransformUpdate>(), registry.Codec<TransformSettle>(), registry.Codec<TransformAuthority>(), registry.Channels);
        }
    }
}
