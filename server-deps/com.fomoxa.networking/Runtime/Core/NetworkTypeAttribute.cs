using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkTypeAttribute : Attribute
    {
        public NetworkTypeAttribute(string wireType)
        {
            WireType = wireType;
        }

        public string WireType { get; }
    }
}
