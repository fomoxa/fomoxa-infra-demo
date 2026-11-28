using System;
using Fomoxa.Networking.Objects;

namespace Fomoxa.Networking
{
    public sealed class ClientScenes
    {
        private readonly ClientManager client;

        internal ClientScenes(ClientManager client)
        {
            this.client = client;
        }

        public event Action<NetworkScene, uint> OnLoadStarted
        {
            add => Tier.OnSceneLoadStarted += value;
            remove => Tier.OnSceneLoadStarted -= value;
        }

        public event Action<NetworkScene, float> OnLoadProgress
        {
            add => Tier.OnSceneLoadProgress += value;
            remove => Tier.OnSceneLoadProgress -= value;
        }

        public event Action<NetworkScene> OnLoaded
        {
            add => Tier.OnSceneLoaded += value;
            remove => Tier.OnSceneLoaded -= value;
        }

        public event Action<NetworkScene> OnUnloaded
        {
            add => Tier.OnSceneUnloaded += value;
            remove => Tier.OnSceneUnloaded -= value;
        }

        internal ClientManager Client => client;

        private ClientObjects Tier => client.Objects;

        public uint SceneIdOf(NetworkScene scene) => Tier.SceneIdOf(scene);
    }
}
