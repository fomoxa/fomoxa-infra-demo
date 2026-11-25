using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkInputAttribute : Attribute
    {
        public NetworkInputAttribute()
        {
        }

        public NetworkInputAttribute(string wireType)
        {
            WireType = wireType;
        }

        public string WireType { get; }
    }
}
