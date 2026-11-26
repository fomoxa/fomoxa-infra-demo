using System;
using System.Collections;
using System.Collections.Generic;

namespace Fomoxa.Networking.Sessions
{
    public sealed class PeerIds : IReadOnlyCollection<ulong>
    {
        private readonly HashSet<ulong> ids = new HashSet<ulong>();
        private int version;

        internal PeerIds()
        {
        }

        public int Count => ids.Count;

        public bool Contains(ulong peerId) => ids.Contains(peerId);

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<ulong> IEnumerable<ulong>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Add(ulong peerId)
        {
            if (ids.Add(peerId))
            {
                version++;
            }
        }

        internal void Remove(ulong peerId)
        {
            if (ids.Remove(peerId))
            {
                version++;
            }
        }

        public struct Enumerator : IEnumerator<ulong>
        {
            private readonly PeerIds owner;
            private readonly int version;
            private HashSet<ulong>.Enumerator inner;

            internal Enumerator(PeerIds owner)
            {
                this.owner = owner;
                version = owner.version;
                inner = owner.ids.GetEnumerator();
            }

            public ulong Current => inner.Current;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (version != owner.version)
                {
                    throw new InvalidOperationException("the started peers changed while they were being enumerated; copy them before kicking, transferring or rejecting a peer in the loop");
                }

                return inner.MoveNext();
            }

            public void Reset()
            {
                if (version != owner.version)
                {
                    throw new InvalidOperationException("the started peers changed while they were being enumerated");
                }

                inner = owner.ids.GetEnumerator();
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
