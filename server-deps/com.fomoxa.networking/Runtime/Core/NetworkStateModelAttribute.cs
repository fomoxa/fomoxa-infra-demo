using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkStateModelAttribute : Attribute
    {
        public NetworkStateModelAttribute()
        {
        }

        public NetworkStateModelAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }

        public bool OnlyWhenDirty { get; set; }
    }
}
