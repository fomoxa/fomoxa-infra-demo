using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkStateAttribute : Attribute
    {
        public NetworkStateAttribute()
        {
        }

        public NetworkStateAttribute(string wireType)
        {
            WireType = wireType;
        }

        public string WireType { get; }

        public string OnChanged { get; set; }

        public byte Index { get; set; }
    }
}
