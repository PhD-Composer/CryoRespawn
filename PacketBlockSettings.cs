using ProtoBuf;
using Sandbox.ModAPI;

namespace CryoRespawn.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketBlockSettings : PacketBase
    {
        [ProtoMember(1)]
        public long EntityId;

        [ProtoMember(2)]
        public CryoChamberBlockSettings Settings;

        public PacketBlockSettings() { } // Empty constructor required for deserialization

        public void Send(long entityId, CryoChamberBlockSettings settings)
        {
            //SenderId = MyAPIGateway.Multiplayer.MyId;
            EntityId = entityId;
            Settings = settings;

            if (MyAPIGateway.Multiplayer.IsServer)
                Networking.RelayToClients(this);
            else
                Networking.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            var block = MyAPIGateway.Entities.GetEntityById(this.EntityId) as Sandbox.ModAPI.IMyCryoChamber;

            if (block == null)
                return;

            var logic = block.GameLogic?.GetAs<CryoRespawnLogic>();

            if (logic == null)
                return;

            //logic.Settings.Range = this.Settings.Range;
            //logic.Settings.Strength = this.Settings.Strength;
            logic.Settings.Grown = this.Settings.Grown;
            logic.Settings.Growth = this.Settings.Growth;
            logic.Settings.Decay = this.Settings.Decay;
            logic.Settings.Playerdied = this.Settings.Playerdied;
            logic.Settings.SavedMagBuff = this.Settings.SavedMagBuff;
            logic.Settings.SavedCobBuff = this.Settings.SavedCobBuff;
            logic.Settings.SavedGoldBuff = this.Settings.SavedGoldBuff;
            logic.Settings.SavedPlatBuff = this.Settings.SavedPlatBuff;
            logic.Settings.SavedUraBuff = this.Settings.SavedUraBuff;
            logic.Settings.Eaten = this.Settings.Eaten;
            logic.Settings.Sloweat = this.Settings.Sloweat;
            logic.Settings.BotSpawned = this.Settings.BotSpawned;
            logic.Settings.SpawnedBotEntityId = this.Settings.SpawnedBotEntityId;

            relay = true;
        }
    }
}