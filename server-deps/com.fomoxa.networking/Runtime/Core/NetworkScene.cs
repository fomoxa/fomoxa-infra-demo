using System;

namespace Fomoxa.Networking
{
    public readonly struct NetworkScene : IEquatable<NetworkScene>
    {
        public NetworkScene(uint id)
        {
            Id = id;
        }

        public uint Id { get; }

        public bool Equals(NetworkScene other) => Id == other.Id;

        public override bool Equals(object obj) => obj is NetworkScene other && Equals(other);

        public override int GetHashCode() => (int)Id;

        public override string ToString() => $"scene #{Id}";

        public static bool operator ==(NetworkScene left, NetworkScene right) => left.Id == right.Id;

        public static bool operator !=(NetworkScene left, NetworkScene right) => left.Id != right.Id;
    }

    public enum ScenePhysics : byte
    {
        Shared = 0,
        Own = 1,
    }
}
