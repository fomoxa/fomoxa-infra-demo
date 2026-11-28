using System;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    public sealed class TransformProtocol
    {
        public TransformProtocol(IMessageCodec<TransformFrame> frameCodec, IMessageCodec<TransformSettle> settleCodec, IMessageCodec<TransformAuthority> authorityCodec, MessageChannels channels)
        {
            FrameCodec = frameCodec ?? throw new ArgumentNullException(nameof(frameCodec));
            SettleCodec = settleCodec ?? throw new ArgumentNullException(nameof(settleCodec));
            AuthorityCodec = authorityCodec ?? throw new ArgumentNullException(nameof(authorityCodec));
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            ObjectProtocol.EnsureReliable(channels, settleCodec.MessageId, nameof(TransformSettle));
            ObjectProtocol.EnsureReliable(channels, authorityCodec.MessageId, nameof(TransformAuthority));
        }

        public IMessageCodec<TransformFrame> FrameCodec { get; }

        public IMessageCodec<TransformSettle> SettleCodec { get; }

        public IMessageCodec<TransformAuthority> AuthorityCodec { get; }

        public static TransformProtocol From(FomoxaRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return new TransformProtocol(registry.Codec<TransformFrame>(), registry.Codec<TransformSettle>(), registry.Codec<TransformAuthority>(), registry.Channels);
        }
    }
}
