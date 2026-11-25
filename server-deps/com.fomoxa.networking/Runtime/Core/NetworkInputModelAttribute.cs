using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkInputModelAttribute : Attribute
    {
        public NetworkInputModelAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }
    }
}
