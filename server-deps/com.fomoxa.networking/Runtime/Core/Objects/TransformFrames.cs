using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Objects
{
    internal delegate void TransformFrameSender(ulong peerId, ReadOnlySpan<byte> payload);

    internal sealed class TransformFrames
    {
        public const int FrameOverhead = sizeof(uint) + sizeof(uint);

        private readonly IMessageCodec<TransformFrame> codec;
        private readonly Func<ulong, int> budgetOf;
        private readonly TransformFrameSender send;
        private readonly Dictionary<ulong, PeerFrame> frames = new Dictionary<ulong, PeerFrame>();
        private readonly List<PeerFrame> active = new List<PeerFrame>();
        private readonly Stack<PeerFrame> spare = new Stack<PeerFrame>();
        private readonly TransformFrame frame = new TransformFrame();
        private readonly byte[] item = new byte[TransformItems.MaxLength];
        private int itemLength;
        private uint tick;

        public TransformFrames(IMessageCodec<TransformFrame> codec, Func<ulong, int> budgetOf, TransformFrameSender send)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.budgetOf = budgetOf ?? throw new ArgumentNullException(nameof(budgetOf));
            this.send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public void Begin(uint frameTick) => tick = frameTick;

        public void SetItem(uint objectId, byte behaviourIndex, byte generation, byte mask, in Vector3 position, in Quaternion rotation, in Vector3 scale) =>
            itemLength = TransformItems.Write(item, objectId, behaviourIndex, generation, mask, position, rotation, scale);

        public void AddTo(ulong peerId)
        {
            if (!frames.TryGetValue(peerId, out PeerFrame peer))
            {
                peer = spare.Count > 0 ? spare.Pop() : new PeerFrame();
                peer.Reset(peerId, DataLimit(budgetOf(peerId)));
                frames.Add(peerId, peer);
                active.Add(peer);
            }

            if (peer.Length > 0 && peer.Length + itemLength > peer.Limit)
            {
                Send(peer);
            }

            peer.Append(item, itemLength);
        }

        public void Flush()
        {
            foreach (PeerFrame peer in active)
            {
                if (peer.Length > 0)
                {
                    Send(peer);
                }

                spare.Push(peer);
            }

            active.Clear();
            frames.Clear();
        }

        internal static int DataLimit(int frameBudget) =>
            Math.Max(TransformItems.MaxLength, frameBudget - FomoxaWire.DataFrameHeaderSize - BundleFormat.BundleOverhead - BundleFormat.EntryOverhead - FrameOverhead);

        private void Send(PeerFrame peer)
        {
            frame.Tick = tick;
            frame.Data = new ReadOnlyMemory<byte>(peer.Buffer, 0, peer.Length);
            send(peer.PeerId, codec.Encode(frame).Span);
            frame.Data = default;
            peer.Length = 0;
        }

        private sealed class PeerFrame
        {
            public ulong PeerId { get; private set; }

            public int Limit { get; private set; }

            public byte[] Buffer { get; private set; } = new byte[256];

            public int Length { get; set; }

            public void Reset(ulong peerId, int limit)
            {
                PeerId = peerId;
                Limit = limit;
                Length = 0;
            }

            public void Append(byte[] source, int length)
            {
                if (Buffer.Length - Length < length)
                {
                    var grown = new byte[Math.Max(Buffer.Length * 2, Length + length)];
                    Buffer.AsSpan(0, Length).CopyTo(grown);
                    Buffer = grown;
                }

                source.AsSpan(0, length).CopyTo(Buffer.AsSpan(Length));
                Length += length;
            }
        }
    }
}
