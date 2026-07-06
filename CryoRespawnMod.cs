using CryoRespawn.Sync;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;


namespace CryoRespawn
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class CryoRespawnMod : MySessionComponentBase
    {
        public static CryoRespawnMod Instance;
        public bool ControlsCreated = false;
        public Networking Networking = new Networking(55299);
        public List<MyEntity> Entities = new List<MyEntity>();
        public PacketBlockSettings CachedPacketSettings;
        public override void LoadData()
        {
            Instance = this;
            Networking.Register();
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdded;
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }
        public override void BeforeStart()
        {
            CryoRespawnConfigManager.Load();
            if (MyAPIGateway.Session.IsServer && CryoRespawnConfigManager.Config.BuffSystemEnabled)
            {
                MyAPIGateway.Session.SessionSettings.EnableSurvivalBuffs = false;
            }
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, TemperatureDamageHandler);
        }
        protected override void UnloadData()
        {
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdded;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered; // ← add
            Instance = null;
            Networking?.Unregister();
            Networking = null;
        }
        private static void OnEntityAdded(VRage.ModAPI.IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null) return;

            // Scan blocks already on the grid
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            foreach (var slim in blocks)
                TryAddEventComponents(slim.FatBlock);

            // Watch for blocks added later
            grid.OnBlockAdded += slim => TryAddEventComponents(slim.FatBlock);
        }
        private static void TryAddEventComponents(IMyCubeBlock block)
        {
            if (block == null) return;
            var ecBlock = block as Sandbox.ModAPI.IMyEventControllerBlock;
            if (ecBlock == null) return;

            if (block.Components.Get<CryoGrowthEventComponent>() == null)
                block.Components.Add(new CryoGrowthEventComponent());
            if (block.Components.Get<CryoStabilityEventComponent>() == null)
                block.Components.Add(new CryoStabilityEventComponent());
        }
        private string Fmt(float val, bool higherIsBetter = true)
        {
            if (Math.Abs(val) < 0.01f) return "Neutral";
            string dir = (val > 0) == higherIsBetter ? "+" : "-";
            return $"{dir}{Math.Abs(val) * 100f:F0}%";
        }
        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (!messageText.Equals("/buffs", StringComparison.OrdinalIgnoreCase)) return;
            sendToOthers = false;
            var localPlayer = MyAPIGateway.Session?.LocalHumanPlayer;
            if (localPlayer == null) return;
            // Find the local player's cryo chamber logic
            CryoRespawnLogic logic = null;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) continue;
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                foreach (var slim in blocks)
                {
                    var chamber = slim.FatBlock as Sandbox.ModAPI.IMyCryoChamber;
                    if (chamber == null || chamber.OwnerId != localPlayer.IdentityId) continue;

                    logic = chamber.GameLogic?.GetAs<CryoRespawnLogic>();
                    if (logic != null) break;
                }
                if (logic != null) break;
            }
            if (logic == null)
            {
                MyAPIGateway.Utilities.ShowMessage("CryoRespawn", "No cryo chamber found for your player.");
                return;
            }
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled)
            {
                MyAPIGateway.Utilities.ShowMessage("CryoRespawn", "Buff system is disabled on this server.");
                return;
            }
            // Movement, welding, grinding, inventory, temperature, radiation, oxygen, fuel, health, heal, energy, drilling
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn", "=== Your Buffs ===");
            float magN = logic.NormalizeBuff(logic.MagBuff, CryoRespawnLogic.MagMin, CryoRespawnLogic.MagMax);
            float cobN = logic.NormalizeBuff(logic.CobBuff, CryoRespawnLogic.CobMin, CryoRespawnLogic.CobMax);
            float goldN = logic.NormalizeBuff(logic.GoldBuff, CryoRespawnLogic.GoldMin, CryoRespawnLogic.GoldMax);
            float platN = logic.NormalizeBuff(logic.PlatBuff, CryoRespawnLogic.PlatMin, CryoRespawnLogic.PlatMax);
            float uraN = logic.NormalizeBuff(logic.UraBuff, CryoRespawnLogic.UraMin, CryoRespawnLogic.UraMax);
            float maxHp = 115f + 85f * magN;
            float healLimit = (0.55f + 0.45f * magN) * 100f;
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn",$"[Magnesium] Max Health: {maxHp:F0}hp | Auto-Heal Limit: {healLimit:F0}% | Impact Resistance: {Fmt(logic.MagBuff)}");
            float drillSpeed = 5.05f + 4.95f * cobN;
            float oxyRate = 1.05f + 0.95f * cobN;
            float invVol = 5.05f + 4.95f * cobN;
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn",$"[Cobalt] Drill Speed: {drillSpeed:F1}x | Oxygen Use: {oxyRate:F2}x | Inventory: {invVol:F1}x");
            float energyRate = 1.05f + 0.95f * goldN;
            float hungerRate = 1.05f - 0.95f * goldN;
            float fuelEff = MathHelper.Clamp(0.05f + 0.95f * goldN, 0.1f, 10f);
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn",
                $"[Gold] Energy Use: {energyRate:F2}x | Hunger Rate: {hungerRate:F2}x | Jetpack Efficiency: {fuelEff:F2}x");
            float moveSpeed = 12f + 5f * platN;
            float weldSpeed = 10f + 2.5f * platN;
            float grindSpeed = 10f + 2.5f * platN;
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn",$"[Platinum] Move: {moveSpeed:F1}m/s | Weld: {weldSpeed:F1}x | Grind: {grindSpeed:F1}x");
            float tempResist = (1.05f - 0.95f * uraN) * 100f;
            MyAPIGateway.Utilities.ShowMessage("CryoRespawn",$"[Uranium] Temp Damage: {tempResist:F0}% of normal | Radiation Resistance: {Fmt(logic.UraBuff)}");
        }
        private void TemperatureDamageHandler(object target, ref MyDamageInformation info)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled) return;
            if (info.Type != MyDamageType.Temperature && info.Type != MyDamageType.Weather && info.Type != MyStringHash.GetOrCompute("Radiation")) return;
            var character = target as IMyCharacter;
            if (character == null || character.IsDead) return;
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var player = players.FirstOrDefault(p => p.Character == character);
            if (player == null) return;
            float uraBuff = GetUraBuffForPlayer(player.IdentityId);
            if (Math.Abs(uraBuff) < 0.001f) return;
            float multiplier = MathHelper.Clamp(1.05f - 0.95f * uraBuff, 0.1f, 2.0f);
            info.Amount *= multiplier;
        }
        private float GetUraBuffForPlayer(long identityId)
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) continue;
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, b => b.FatBlock is IMyCryoChamber);
                foreach (var slim in blocks)
                {
                    var logic = slim.FatBlock?.GameLogic?.GetAs<CryoRespawnLogic>();
                    if (logic == null) continue;
                    // Access owner via the chamber
                    var chamber = slim.FatBlock as IMyCryoChamber;
                    if (chamber?.OwnerId == identityId)
                        return logic.UraBuff; // make UraBuff public
                }
            }
            return 0f;
        }
    }
}
