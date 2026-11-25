using System;

namespace Fomoxa.Networking
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NetworkReconcileModelAttribute : Attribute
    {
        public NetworkReconcileModelAttribute(string model)
        {
            Model = model;
        }

        public string Model { get; }
    }
}
