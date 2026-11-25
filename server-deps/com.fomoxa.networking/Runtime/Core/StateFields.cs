using System;
using System.ComponentModel;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateFields
    {
        [ThreadStatic]
        private static byte[] previousBytes;

        public static bool SameBytes<T>(IMessageCodec<T> codec, T previous, T current)
        {
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            ReadOnlySpan<byte> encodedPrevious = codec.Encode(previous).Span;
            if (previousBytes == null || previousBytes.Length < encodedPrevious.Length)
            {
                previousBytes = new byte[Math.Max(encodedPrevious.Length, 64)];
            }

            encodedPrevious.CopyTo(previousBytes);
            int previousLength = encodedPrevious.Length;
            return codec.Encode(current).Span.SequenceEqual(new ReadOnlySpan<byte>(previousBytes, 0, previousLength));
        }
    }
}
