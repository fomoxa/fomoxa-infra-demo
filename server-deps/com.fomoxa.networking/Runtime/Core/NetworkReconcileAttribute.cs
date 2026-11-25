using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkReconcileAttribute : Attribute
    {
        public NetworkReconcileAttribute()
        {
        }

        public NetworkReconcileAttribute(string wireType)
        {
            WireType = wireType;
        }

        public string WireType { get; }

        public float Tolerance { get; set; }
    }
}
