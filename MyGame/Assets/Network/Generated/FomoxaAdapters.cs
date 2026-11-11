namespace Generated
{
    public static class FomoxaAdapters
    {
        public static void RegisterAll(global::Fomoxa.Networking.FomoxaRegistry registry)
        {
            registry.SetSchema(NetSchema.Build());
            foreach (FomoxaMessage message in Handshake.FomoxaMessages)
            {
                registry.SetName(message.Id, message.Name);
            }
            registry.SetCodec(MessageBundleNetAdapter.Instance);
            registry.SetCodec(ReliableAckNetAdapter.Instance);
            registry.SetCodec(LocalPeerNetAdapter.Instance);
            registry.SetCodec(ObjectSpawnNetAdapter.Instance);
            registry.SetCodec(ObjectSceneSpawnNetAdapter.Instance);
            registry.SetCodec(ObjectDespawnNetAdapter.Instance);
            registry.SetCodec(ObjectOwnerChangeNetAdapter.Instance);
            registry.SetCodec(StateDeltaNetAdapter.Instance);
            registry.SetCodec(StateResyncNetAdapter.Instance);
            registry.SetCodec(TransformUpdateNetAdapter.Instance);
            registry.SetCodec(TransformSettleNetAdapter.Instance);
            registry.SetCodec(AnimatorStateNetAdapter.Instance);
            registry.SetCodec(SceneLoadNetAdapter.Instance);
            registry.SetCodec(SceneUnloadNetAdapter.Instance);
            registry.SetCodec(SceneLoadedNetAdapter.Instance);
            registry.SetCodec(TickPingNetAdapter.Instance);
            registry.SetCodec(TickPongNetAdapter.Instance);
            registry.SetCodec(InputFramesNetAdapter.Instance);
            registry.SetCodec(ReconcileStateNetAdapter.Instance);
            registry.SetCodec(PeerLeaveNetAdapter.Instance);
            registry.SetCodec(FragmentStartNetAdapter.Instance);
            registry.SetCodec(FragmentPartNetAdapter.Instance);
            registry.SetCodec(SessionCloseNetAdapter.Instance);
            registry.SetCodec(SceneFileNetAdapter.Instance);
            registry.SetCodec(CollectionChangesNetAdapter.Instance);
            registry.SetCodec(CollectionContentNetAdapter.Instance);
            registry.SetCodec(CollectionContentsNetAdapter.Instance);
            registry.SetCodec(CollectionResyncNetAdapter.Instance);
            registry.SetCodec(BoolValueNetAdapter.Instance);
            registry.SetCodec(I8ValueNetAdapter.Instance);
            registry.SetCodec(U8ValueNetAdapter.Instance);
            registry.SetCodec(I16ValueNetAdapter.Instance);
            registry.SetCodec(U16ValueNetAdapter.Instance);
            registry.SetCodec(I32ValueNetAdapter.Instance);
            registry.SetCodec(U32ValueNetAdapter.Instance);
            registry.SetCodec(I64ValueNetAdapter.Instance);
            registry.SetCodec(U64ValueNetAdapter.Instance);
            registry.SetCodec(F32ValueNetAdapter.Instance);
            registry.SetCodec(F64ValueNetAdapter.Instance);
            registry.SetCodec(StringValueNetAdapter.Instance);
            registry.SetCodec(PeerTransferNetAdapter.Instance);
            registry.SetCodec(TransferTicketNetAdapter.Instance);
            registry.SetCodec(ConnectTokenNetAdapter.Instance);
            registry.SetCodec(ConnectTokenSecretNetAdapter.Instance);
            registry.SetCodec(ConnectionGrantNetAdapter.Instance);
            registry.Channels.Set(AnimatorStateNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(CollectionChangesNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(CollectionContentNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(CollectionResyncNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(FragmentPartNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(FragmentStartNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(LocalPeerNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(ObjectDespawnNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(ObjectOwnerChangeNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(ObjectSceneSpawnNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(ObjectSpawnNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(PeerTransferNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(SceneLoadNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(SceneLoadedNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(SceneUnloadNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(SessionCloseNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(StateDeltaNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(StateResyncNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
            registry.Channels.Set(TransformSettleNetCodec.MessageId, global::Fomoxa.Networking.Channel.ReliableOrdered);
        }
    }
}
