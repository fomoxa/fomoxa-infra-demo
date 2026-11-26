using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Sessions;

namespace Fomoxa.Networking.Objects
{
    internal sealed class ClientEntities : IObjectSpawner
    {
        private readonly ClientSession session;
        private readonly MessageDispatcher dispatcher;
        private readonly MessageChannels channels;
        private readonly StateProtocol stateProtocol;
        private readonly TransformProtocol transformProtocol;
        private readonly RpcMessageIds rpcIds;
        private readonly ServerEntities hostServer;
        private readonly IClientEntityBackend backend;
        private readonly NetworkLog log;
        private readonly Dictionary<uint, EntityRecord> spawned = new Dictionary<uint, EntityRecord>();
        private readonly Dictionary<uint, Type> rpcTypes = new Dictionary<uint, Type>();
        private readonly HashSet<uint> stateIds = new HashSet<uint>();
        private readonly List<EntityRecord> hiddenStarted = new List<EntityRecord>();
        private StateDelta stateDelta = new StateDelta();
        private CollectionChanges collectionChanges = new CollectionChanges();
        private CollectionContent collectionContent = new CollectionContent();
        private CollectionContents spawnContents = new CollectionContents();
        private readonly CollectionResync collectionResync = new CollectionResync();
        private TransformUpdate transformUpdate = new TransformUpdate();
        private TransformSettle transformSettle = new TransformSettle();
        private TransformAuthority transformAuthority = new TransformAuthority();
        private readonly TransformUpdate ownerUpdate = new TransformUpdate();
        private readonly TransformSettle ownerSettle = new TransformSettle();
        private readonly List<OwnerSend> ownerSends = new List<OwnerSend>();
        private uint ownerTicks;

        public ClientEntities(
            object owner,
            ClientSession session,
            MessageDispatcher dispatcher,
            MessageChannels channels,
            StateProtocol stateProtocol,
            TransformProtocol transformProtocol,
            RpcMessageIds rpcIds,
            ServerEntities hostServer,
            IClientEntityBackend backend,
            NetworkLog log)
        {
            Owner = owner;
            this.session = session;
            this.dispatcher = dispatcher;
            this.channels = channels;
            this.stateProtocol = stateProtocol;
            this.transformProtocol = transformProtocol;
            this.rpcIds = rpcIds;
            this.hostServer = hostServer;
            this.backend = backend;
            this.log = log;
            Representations = new RepresentationTable(spawned);
            dispatcher.RegisterObject(stateProtocol.DeltaCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverDelta(objectId, behaviourIndex, body));
            dispatcher.RegisterObject(stateProtocol.Collections.ChangesCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverChanges(objectId, behaviourIndex, body));
            dispatcher.RegisterObject(stateProtocol.Collections.ContentCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverContent(objectId, behaviourIndex, body));
            dispatcher.RegisterObject(transformProtocol.UpdateCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverTransform(objectId, behaviourIndex, body, false));
            dispatcher.RegisterObject(transformProtocol.SettleCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverTransform(objectId, behaviourIndex, body, true));
            dispatcher.RegisterObject(transformProtocol.AuthorityCodec.MessageId, (peerId, objectId, behaviourIndex, body) => DeliverAuthority(objectId, behaviourIndex, body));
        }

        public event Action<EntityRecord> OnDespawning;

        public object Owner { get; }

        public ClientObjects Objects { get; private set; }

        public bool ConnectedLocally { get; set; }

        public InputRules InputRules { get; set; } = new InputRules();

        public Dictionary<uint, EntityRecord> Spawned => spawned;

        public IReadOnlyDictionary<uint, INetworkEntity> Representations { get; }

        public bool TryGet(uint objectId, out EntityRecord record) => spawned.TryGetValue(objectId, out record);

        public void Attach(ClientObjects objects)
        {
            Objects = objects;
            objects.OnOwnerChanged += RaiseOwnerChanged;
        }

        public SpawnResult Spawn(in SpawnedObject spawnedObject)
        {
            bool isSceneObject = spawnedObject.SceneObjectId != 0;
            if (!isSceneObject)
            {
                SpawnResult checkedPrefab = backend.CheckPrefab(spawnedObject);
                if (checkedPrefab != SpawnResult.Spawned)
                {
                    return checkedPrefab;
                }
            }

            uint objectId = spawnedObject.ObjectId;
            EntityRecord record;
            INetworkEntity entity;
            if (ConnectedLocally)
            {
                if (hostServer == null || !hostServer.TryGet(objectId, out record))
                {
                    return SpawnResult.Spawned;
                }

                entity = record.Representation;
                if (spawned.TryGetValue(objectId, out EntityRecord hidden) && hidden == record)
                {
                    if (!TryApplySpawn(record.Representation.EntityBehaviours, spawnedObject, true))
                    {
                        return SpawnResult.InvalidSpawn;
                    }

                    backend.ShowOnHost(entity);
                    return SpawnResult.Spawned;
                }
            }
            else
            {
                if (isSceneObject)
                {
                    SpawnResult placed = backend.PlaceSceneObject(spawnedObject, out entity);
                    if (placed != SpawnResult.Spawned)
                    {
                        return placed;
                    }
                }
                else
                {
                    entity = backend.Create(spawnedObject);
                }

                record = new EntityRecord(entity, spawnedObject.PrefabFingerprint) { ObjectId = objectId };
                entity.Bind(record);
            }

            record.Client = this;
            spawned.Add(objectId, record);
            foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
            {
                if (behaviour.TransformSlot != null)
                {
                    behaviour.TransformSlot.Record = record;
                }
            }

            bool statesValid;
            try
            {
                foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                {
                    behaviour.Register(rpcIds, channels, stateProtocol, InputRules);
                    record.HasInput |= behaviour.InputSlot != null;
                }

                statesValid = TryApplySpawn(record.Representation.EntityBehaviours, spawnedObject, record.Server != null);
                if (statesValid)
                {
                    ResetReceive(entity.EntityBehaviours);
                    RouteRpcs(entity.EntityBehaviours);
                    RouteStates(entity.EntityBehaviours);
                    foreach (EntityBehaviour behaviour in entity.EntityBehaviours)
                    {
                        behaviour.OnStartClient();
                    }
                }
            }
            catch
            {
                Abandon(record);
                throw;
            }

            if (!statesValid)
            {
                Abandon(record);
                return SpawnResult.InvalidSpawn;
            }

            if (record.Server != null)
            {
                backend.ShowOnHost(entity);
            }

            return SpawnResult.Spawned;
        }

        public void Despawn(uint objectId)
        {
            if (session.State == ConnectionState.Started && spawned.TryGetValue(objectId, out EntityRecord shared) && shared.Server != null)
            {
                backend.HideOnHost(shared.Representation);
                return;
            }

            if (!spawned.Remove(objectId, out EntityRecord record))
            {
                return;
            }

            OnDespawning?.Invoke(record);
            try
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    behaviour.OnStopClient();
                }
            }
            catch (Exception exception) when (session.State == ConnectionState.Stopped)
            {
                log.Exception(exception);
            }
            finally
            {
                Detach(record);
            }
        }

        public void EndShared(EntityRecord record)
        {
            if (record.Client != this || !spawned.Remove(record.ObjectId))
            {
                return;
            }

            try
            {
                foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
                {
                    behaviour.OnStopClient();
                }
            }
            finally
            {
                record.Client = null;
            }
        }

        public bool ForgetDestroyed(INetworkEntity entity)
        {
            EntityRecord record = entity.Record;
            if (record == null || record.Client != this || !spawned.Remove(record.ObjectId))
            {
                return false;
            }

            log.Warning($"Fomoxa: object {record.ObjectId} (prefab 0x{entity.PrefabId:X8}) was destroyed on the client without a despawn from the server");
            record.Client = null;
            if (record.Server == null)
            {
                entity.Unbind(record);
            }

            return true;
        }

        public void DespawnHiddenOnHost()
        {
            hiddenStarted.Clear();
            foreach (EntityRecord record in spawned.Values)
            {
                if (!Objects.TryGet(record.ObjectId, out _))
                {
                    hiddenStarted.Add(record);
                }
            }

            for (int index = hiddenStarted.Count - 1; index >= 0; index--)
            {
                Despawn(hiddenStarted[index].ObjectId);
            }

            hiddenStarted.Clear();
        }

        private void Abandon(EntityRecord record)
        {
            spawned.Remove(record.ObjectId);
            Detach(record);
        }

        private void Detach(EntityRecord record)
        {
            StopSending(record);
            record.Client = null;
            if (record.Server == null)
            {
                INetworkEntity entity = record.Representation;
                entity.Unbind(record);
                backend.End(entity);
            }
        }

        private bool TryApplySpawn(IReadOnlyList<EntityBehaviour> behaviours, in SpawnedObject spawnedObject, bool shared)
        {
            if (!TryStageCollections(behaviours, spawnedObject.Collections) || !TryApplyStates(behaviours, spawnedObject.States, shared))
            {
                return false;
            }

            foreach (EntityBehaviour behaviour in behaviours)
            {
                foreach (CollectionSlot slot in behaviour.Collections)
                {
                    slot.Commit(shared, false);
                }
            }

            return true;
        }

        private bool TryStageCollections(IReadOnlyList<EntityBehaviour> behaviours, IReadOnlyList<ReadOnlyMemory<byte>> collections)
        {
            if (collections.Count != behaviours.Count)
            {
                return false;
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                IReadOnlyList<CollectionSlot> slots = behaviours[index].Collections;
                if (slots.Count == 0 || collections[index].IsEmpty)
                {
                    if (slots.Count != 0 || !collections[index].IsEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                try
                {
                    stateProtocol.Collections.ContentsCodec.Decode(collections[index], ref spawnContents);
                    if (spawnContents.Collections.Count != slots.Count)
                    {
                        return false;
                    }

                    for (int at = 0; at < slots.Count; at++)
                    {
                        slots[at].Stage(spawnContents.Collections[at]);
                    }
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryApplyStates(IReadOnlyList<EntityBehaviour> behaviours, IReadOnlyList<ReadOnlyMemory<byte>> states, bool shared)
        {
            if (states.Count != behaviours.Count)
            {
                return false;
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                StateSlot slot = behaviours[index].StateSlot;
                if (slot == null || states[index].IsEmpty)
                {
                    if (slot != null || !states[index].IsEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                try
                {
                    slot.Validate(states[index]);
                }
                catch (MessageDecodeException)
                {
                    return false;
                }
            }

            for (int index = 0; index < behaviours.Count; index++)
            {
                behaviours[index].StateSlot?.ApplyInitial(states[index], shared);
            }

            return true;
        }

        private void RouteRpcs(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                Type type = behaviour.DeclaringType;
                foreach (uint messageId in behaviour.ClientRpcIds)
                {
                    if (stateIds.Contains(messageId))
                    {
                        throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a state model; {type.FullName} cannot use it as a client RPC");
                    }

                    if (rpcTypes.TryGetValue(messageId, out Type existing))
                    {
                        if (existing != type)
                        {
                            throw new HandlerRegistrationException($"message id 0x{messageId:X8} is already a client RPC of {existing.FullName}; {type.FullName} cannot use it");
                        }

                        continue;
                    }

                    uint routed = messageId;
                    dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverRpc(routed, objectId, behaviourIndex, body));
                    rpcTypes.Add(routed, type);
                }
            }
        }

        private void RouteStates(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                StateSlot slot = behaviour.StateSlot;
                if (slot == null || stateIds.Contains(slot.MessageId))
                {
                    continue;
                }

                if (rpcTypes.ContainsKey(slot.MessageId))
                {
                    throw new HandlerRegistrationException($"message id 0x{slot.MessageId:X8} is already a client RPC of {rpcTypes[slot.MessageId].FullName}; {behaviour.DeclaringType.FullName} cannot use it as a state model");
                }

                uint routed = slot.MessageId;
                dispatcher.RegisterObject(routed, (peerId, objectId, behaviourIndex, body) => DeliverState(routed, objectId, behaviourIndex, body));
                stateIds.Add(routed);
            }
        }

        private void DeliverState(uint messageId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour))
            {
                return;
            }

            StateSlot slot = behaviour.StateSlot;
            if (slot == null || slot.MessageId != messageId)
            {
                return;
            }

            try
            {
                slot.Receive(body, record.Server != null);
            }
            catch (MessageDecodeException)
            {
                LoseSync(slot, objectId, behaviourIndex);
                throw;
            }
        }

        internal static void ResetReceive(IReadOnlyList<EntityBehaviour> behaviours)
        {
            foreach (EntityBehaviour behaviour in behaviours)
            {
                behaviour.TransformSlot?.Receiver.ResetReceive();
            }
        }

        internal static bool IsPredicting(IReadOnlyList<EntityBehaviour> behaviours)
        {
            for (int index = 0; index < behaviours.Count; index++)
            {
                if (behaviours[index].IsPredicting)
                {
                    return true;
                }
            }

            return false;
        }

        private void DeliverTransform(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body, bool settle)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record) || record.Server != null)
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (IsPredicting(behaviours) || behaviourIndex >= behaviours.Count)
            {
                return;
            }

            TransformSlot slot = behaviours[behaviourIndex].TransformSlot;
            if (slot == null)
            {
                return;
            }

            uint tick;
            byte mask;
            byte generation;
            List<float> values;
            if (settle)
            {
                transformProtocol.SettleCodec.Decode(body, ref transformSettle);
                tick = transformSettle.Tick;
                mask = transformSettle.Mask;
                generation = transformSettle.Generation;
                values = transformSettle.Values;
            }
            else
            {
                transformProtocol.UpdateCodec.Decode(body, ref transformUpdate);
                tick = transformUpdate.Tick;
                mask = transformUpdate.Mask;
                generation = transformUpdate.Generation;
                values = transformUpdate.Values;
            }

            if (!SpawnTransform.TryUnpack(mask, values, out Vector3 position, out Quaternion rotation, out Vector3 scale))
            {
                throw new MessageDecodeException($"transform mask 0x{mask:X2} does not match its {values.Count} values", null);
            }

            TransformSync sync = slot.Sync;
            if (sync.OwnerSending && sync.HasDownstream && generation == sync.DownstreamGeneration)
            {
                return;
            }

            sync.HasDownstream = true;
            sync.DownstreamGeneration = generation;
            slot.Receiver.Receive(new TransformSample(tick, mask, position, rotation, scale, settle, generation));
            if (sync.OwnerSending)
            {
                sync.OwnerGeneration++;
                sync.OwnerSettlePending = true;
            }
        }

        public void SyncOwnerTransforms()
        {
            ownerTicks++;
            for (int index = ownerSends.Count - 1; index >= 0; index--)
            {
                OwnerSend send = ownerSends[index];
                EntityRecord record = send.Record;
                TransformSlot slot = record.Representation.EntityBehaviours[send.Index].TransformSlot;
                TransformSync sync = slot.Sync;
                if (record.Client != this || !sync.OwnerSending || !Objects.IsOwner(record.ObjectId))
                {
                    sync.StopSending();
                    ownerSends.RemoveAt(index);
                    continue;
                }

                slot.Source.ReadLocal(out Vector3 position, out Quaternion rotation, out Vector3 scale);
                if (sync.OwnerSettlePending)
                {
                    sync.OwnerSettlePending = false;
                    sync.MarkSettled(position, rotation, scale);
                    SendOwnerSettle(record.ObjectId, send.Index, sync, sync.SelectedMask, position, rotation, scale);
                    continue;
                }

                TransformSend sample = sync.Sample(position, rotation, scale, out byte mask);
                if (sample == TransformSend.Update)
                {
                    ownerUpdate.Tick = ownerTicks;
                    ownerUpdate.Mask = mask;
                    ownerUpdate.Generation = sync.OwnerGeneration;
                    SpawnTransform.PackSelected(mask, position, rotation, scale, ownerUpdate.Values);
                    session.SendToObject(transformProtocol.UpdateCodec.MessageId, record.ObjectId, send.Index, transformProtocol.UpdateCodec.Encode(ownerUpdate).Span);
                }
                else if (sample == TransformSend.Settle)
                {
                    SendOwnerSettle(record.ObjectId, send.Index, sync, mask, position, rotation, scale);
                }
            }
        }

        private void SendOwnerSettle(uint objectId, byte behaviourIndex, TransformSync sync, byte mask, in Vector3 position, in Quaternion rotation, in Vector3 scale)
        {
            ownerSettle.Tick = ownerTicks;
            ownerSettle.Mask = mask;
            ownerSettle.Generation = sync.OwnerGeneration;
            SpawnTransform.PackSelected(mask, position, rotation, scale, ownerSettle.Values);
            session.SendToObject(transformProtocol.SettleCodec.MessageId, objectId, behaviourIndex, transformProtocol.SettleCodec.Encode(ownerSettle).Span);
        }

        private void DeliverAuthority(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!spawned.TryGetValue(objectId, out EntityRecord record) || record.Server != null)
            {
                return;
            }

            IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
            if (behaviourIndex >= behaviours.Count || behaviours[behaviourIndex].TransformSlot == null)
            {
                return;
            }

            transformProtocol.AuthorityCodec.Decode(body, ref transformAuthority);
            TransformSlot slot = behaviours[behaviourIndex].TransformSlot;
            TransformSync sync = slot.Sync;
            if (!transformAuthority.Enabled || !Objects.IsOwner(objectId))
            {
                sync.StopSending();
                return;
            }

            if (!sync.OwnerSending)
            {
                ownerSends.Add(new OwnerSend(record, behaviourIndex));
            }

            sync.OwnerSending = true;
            sync.OwnerSettlePending = false;
            sync.OwnerGeneration = transformAuthority.Generation;
            slot.Source.ReadLocal(out Vector3 position, out Quaternion rotation, out Vector3 scale);
            sync.MarkSettled(position, rotation, scale);
        }

        private static void StopSending(EntityRecord record)
        {
            foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
            {
                behaviour.TransformSlot?.Sync.StopSending();
            }
        }

        private void DeliverDelta(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour))
            {
                return;
            }

            StateSlot slot = behaviour.StateSlot;
            if (slot == null || slot.OutOfSync)
            {
                return;
            }

            try
            {
                stateProtocol.DeltaCodec.Decode(body, ref stateDelta);
                slot.ReceiveDelta(stateDelta.Data, record.Server != null);
            }
            catch (MessageDecodeException)
            {
                LoseSync(slot, objectId, behaviourIndex);
                throw;
            }
        }

        private void DeliverChanges(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour) || behaviour.Collections.Count == 0)
            {
                return;
            }

            try
            {
                stateProtocol.Collections.ChangesCodec.Decode(body, ref collectionChanges);
            }
            catch (MessageDecodeException)
            {
                foreach (CollectionSlot unknown in behaviour.Collections)
                {
                    if (!unknown.OutOfSync)
                    {
                        LoseSync(unknown, objectId, behaviourIndex);
                    }
                }

                throw;
            }

            CollectionSlot slot = behaviour.CollectionAt(collectionChanges.Collection);
            if (slot == null || slot.OutOfSync)
            {
                return;
            }

            try
            {
                slot.Receive(collectionChanges, record.Server != null);
            }
            catch (MessageDecodeException)
            {
                LoseSync(slot, objectId, behaviourIndex);
                throw;
            }
        }

        private void DeliverContent(uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (!TryGetBehaviour(objectId, behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour) || behaviour.Collections.Count == 0)
            {
                return;
            }

            stateProtocol.Collections.ContentCodec.Decode(body, ref collectionContent);
            CollectionSlot slot = behaviour.CollectionAt(collectionContent.Collection);
            if (slot == null || !slot.OutOfSync)
            {
                return;
            }

            slot.Stage(collectionContent);
            slot.Commit(record.Server != null, true);
        }

        private void DeliverRpc(uint messageId, uint objectId, byte behaviourIndex, ReadOnlyMemory<byte> body)
        {
            if (TryGetBehaviour(objectId, behaviourIndex, out EntityRecord _, out EntityBehaviour behaviour)
                && behaviour.TryGetClientRpc(messageId, out ClientRpc rpc))
            {
                rpc.Invoke(body);
            }
        }

        private void LoseSync(StateSlot slot, uint objectId, byte behaviourIndex)
        {
            slot.MarkOutOfSync();
            session.SendToObject(stateProtocol.ResyncCodec.MessageId, objectId, behaviourIndex, ReadOnlySpan<byte>.Empty);
        }

        private void LoseSync(CollectionSlot slot, uint objectId, byte behaviourIndex)
        {
            slot.MarkOutOfSync();
            collectionResync.Collection = slot.Index;
            IMessageCodec<CollectionResync> codec = stateProtocol.Collections.ResyncCodec;
            session.SendToObject(codec.MessageId, objectId, behaviourIndex, codec.Encode(collectionResync).Span);
        }

        private void RaiseOwnerChanged(ObjectOwnerChangedArgs args)
        {
            if (!spawned.TryGetValue(args.ObjectId, out EntityRecord record))
            {
                return;
            }

            if (!Objects.IsOwner(args.ObjectId))
            {
                StopSending(record);
            }

            foreach (EntityBehaviour behaviour in record.Representation.EntityBehaviours)
            {
                behaviour.OnOwnerChangedClient(args.PreviousOwnerId);
            }
        }

        private bool TryGetBehaviour(uint objectId, byte behaviourIndex, out EntityRecord record, out EntityBehaviour behaviour)
        {
            if (spawned.TryGetValue(objectId, out record))
            {
                IReadOnlyList<EntityBehaviour> behaviours = record.Representation.EntityBehaviours;
                if (behaviourIndex < behaviours.Count)
                {
                    behaviour = behaviours[behaviourIndex];
                    return true;
                }
            }

            behaviour = null;
            return false;
        }

        private readonly struct OwnerSend
        {
            public OwnerSend(EntityRecord record, byte index)
            {
                Record = record;
                Index = index;
            }

            public EntityRecord Record { get; }

            public byte Index { get; }
        }
    }
}
