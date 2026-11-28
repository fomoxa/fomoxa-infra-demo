using System;

namespace Fomoxa.Networking.Simulation
{
    public readonly struct StaticGroup : IEquatable<StaticGroup>
    {
        public StaticGroup(NetworkScene scene, int id, int firstIndex, int count)
        {
            Scene = scene;
            Id = id;
            FirstIndex = firstIndex;
            Count = count;
        }

        public NetworkScene Scene { get; }

        public int Id { get; }

        public int FirstIndex { get; }

        public int Count { get; }

        public bool IsValid => Id != 0;

        public bool Equals(StaticGroup other) => Scene == other.Scene && Id == other.Id && FirstIndex == other.FirstIndex && Count == other.Count;

        public override bool Equals(object obj) => obj is StaticGroup other && Equals(other);

        public override int GetHashCode() => ((int)Scene.Id * 397) ^ Id;
    }
}
