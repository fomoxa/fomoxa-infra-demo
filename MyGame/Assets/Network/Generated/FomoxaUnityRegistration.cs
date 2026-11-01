#if UNITY_5_3_OR_NEWER
namespace Generated
{
    internal static class FomoxaUnityRegistration
    {
        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            FomoxaAdapters.RegisterAll(global::Fomoxa.Networking.FomoxaRegistry.Default);
        }
    }
}
#endif
