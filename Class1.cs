// Owner: Dennis Klausing
// Last Updated: 07-06-2026 01:45 EST
// Last Updated By: Dennis Klausing
// Other Modders:
// If you change/add/removed anything, Comment where you did it. If you're removing stuff, comment the old out instead of deleting it, so I can actually see the differences.
//Things to do:
// Keen in their wisdom made it so that when interacting with the chamber via the entry point,
// it will not open the terminal menu nor inventory menu. This is not a bug, just an oversight on their part. My task is to figure out how to get the terminal menu to open when interacting with the chamber via the entry point.
// I have no idea how to do this, but I will figure it out eventually.
// Add in a safey that shifts the pull angles by 1 degree if they are set to 0, 90, 180, or 270.
// This is to prevent the buff pull from going to infinity when any angle is set to a cardinal direction.
// Bugs to fix:
// Decay Timer resets on world reload, should save and load decay time just like growth time.
// In multiplayer, local hosted. When a player is offline, the chamber that had a clone fully grown and stable
// switches to growing at the week long rate the chamber remains stable during all of this.
// It switches back to the 200s growth time when the player comes back online.
// This should not be the case, once the clone is fully grown, it should stay fully grown given the clone stays stable.
// 
using CryoRespawn.Sync;
using EmptyKeys.UserInterface.Generated;
using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.AI;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.EntityComponents.DebugRenders;
using SpaceEngineers.Game.EntityComponents.GameLogic.Buffs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using VRage;
using VRage.Core;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Network;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.GameServices;
using VRage.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Sync;
using VRage.Utils;
using VRageMath;
using VRageRender;
using static CryoRespawn.CryoGrowthEventComponent;
using IMyEntity = VRage.ModAPI.IMyEntity;

namespace CryoRespawn
{
    [ProtoContract]
    public class CryoChamberBlockSettings
    { //Varible that get save between sessions, stored in the chamber's storage
        public static CryoChamberBlockSettings Instance;
        [ProtoMember(1)]
        public bool Grown;
        [ProtoMember(2)]
        public float Growth;
        [ProtoMember(3)]
        public float Decay;
        [ProtoMember(4)]
        public long Playerdied;
        [ProtoMember(5)]
        public float SavedMagBuff;
        [ProtoMember(6)]
        public float SavedCobBuff;
        [ProtoMember(7)]
        public float SavedGoldBuff;
        [ProtoMember(8)]
        public float SavedPlatBuff;
        [ProtoMember(9)]
        public float SavedUraBuff;
        [ProtoMember(10)]
        public long Eaten;
        [ProtoMember(11)]
        public long Sloweat;
        [ProtoMember(12)]
        public bool BotSpawned;
        [ProtoMember(13)]
        public long SpawnedBotEntityId;
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SurvivalKit), false)]
    public class SurvivalKitDisableRespawn : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            MyLog.Default.WriteLine("SurvivalKitDisableRespawn::Init");
            base.Init(objectBuilder);
        }
        public override void UpdateOnceBeforeFrame()
        {
            MyLog.Default.WriteLine("SurvivalKitDisableRespawn::OnceBeforeFrame");
            base.UpdateOnceBeforeFrame();
            Entity.Components.Remove<MyEntityRespawnComponentBase>();
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MedicalRoom), false)]
    public class MedicalbayDisableRespawn : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            MyLog.Default.WriteLine("MedicalbayDisableRespawn::Init");
        }
        public override void UpdateOnceBeforeFrame()
        {
            MyLog.Default.WriteLine("MedicalbayDisableRespawn::OnceBeforeFrame");
            base.UpdateOnceBeforeFrame();
            Entity.Components.Remove<MyEntityRespawnComponentBase>();
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_EventControllerBlock), false)]
    public class EventControllerEventInjector : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (Entity.Components.Get<CryoGrowthEventComponent>() == null)
                Entity.Components.Add(new CryoGrowthEventComponent());
            if (Entity.Components.Get<CryoStabilityEventComponent>() == null)
                Entity.Components.Add(new CryoStabilityEventComponent());
        }
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false) => null;
    }
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_CryoGrowthEventComponent : MyObjectBuilder_ComponentBase { }
    [MyComponentBuilder(typeof(MyObjectBuilder_CryoGrowthEventComponent))]
    public class CryoGrowthEventComponent : MyEntityComponentBase, IMyEventComponentWithGui
    {
        private static bool _controlsCreated = false;
        public readonly List<Sandbox.ModAPI.IMyTerminalBlock> MonitoredBlocks = new List<Sandbox.ModAPI.IMyTerminalBlock>();
        public MyStringId EventDisplayName => MyStringId.GetOrCompute("Clone Growth %");
        public bool IsSelected { get; set; }
        public long UniqueSelectionId => 183829104738291001L;
        public string YesNoToolbarYesDescription => "Growth threshold met";
        public string YesNoToolbarNoDescription => "Growth threshold not met";
        public bool IsBlocksListUsed => true;
        public bool IsConditionSelectionUsed => true;
        public bool IsThresholdUsed => true;
        private static readonly Guid GrowthStorageKey = new Guid("a0a5f8f9-d897-4d0c-b5b7-0689efcbe4a6");
        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            var term = Container?.Entity as Sandbox.ModAPI.IMyTerminalBlock;
            if (term != null) term.AppendingCustomInfo += OnAppendCustomInfo;
            RestoreMonitoredBlocks();
        }
        public override void OnBeforeRemovedFromContainer()
        {
            var term = Container?.Entity as Sandbox.ModAPI.IMyTerminalBlock;
            if (term != null) term.AppendingCustomInfo -= OnAppendCustomInfo;
            base.OnBeforeRemovedFromContainer();
        }
        private void RestoreMonitoredBlocks()
        {
            var entity = Container?.Entity;
            if (entity?.Storage == null) return;

            string data;
            if (!entity.Storage.TryGetValue(GrowthStorageKey, out data)
                || string.IsNullOrEmpty(data)) return;

            foreach (var idStr in data.Split(','))
            {
                long id;
                if (!long.TryParse(idStr.Trim(), out id)) continue;
                var block = MyAPIGateway.Entities.GetEntityById(id)
                    as Sandbox.ModAPI.IMyTerminalBlock;
                if (block != null && !MonitoredBlocks.Contains(block))
                    MonitoredBlocks.Add(block);
            }
        }
        private void SaveMonitoredBlocks()
        {
            var entity = Container?.Entity;
            if (entity == null) return;

            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();

            entity.Storage[GrowthStorageKey] = string.Join(",",
                MonitoredBlocks.ConvertAll(b => b.EntityId.ToString()));
        }
        private void OnAppendCustomInfo(Sandbox.ModAPI.IMyTerminalBlock block, System.Text.StringBuilder sb)
        {
            var ecBlock = block as Sandbox.ModAPI.IMyEventControllerBlock;
            if (!(ecBlock?.SelectedEvent is CryoGrowthEventComponent)) return;
            foreach (var mon in MonitoredBlocks.ToList())
            {
                var logic = mon?.GameLogic?.GetAs<CryoRespawnLogic>();
                if (logic == null) continue;
                sb.AppendLine($"Input: {mon.DisplayNameText} at {logic.PercentGrown:F1} %");
            }
        }
        public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
       => new MyObjectBuilder_CryoGrowthEventComponent();
        public override void Deserialize(MyObjectBuilder_ComponentBase builder) { }
        public override bool IsSerialized() => false;
        // Explicit interface impl avoids the ambiguous constraint error
        void IMyEventControllerEntityComponent.CreateTerminalInterfaceControls<T>()
        {
            if (_controlsCreated) return;
            _controlsCreated = true;
            var slider = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, T>("CryoRespawn_GrowthPercent_Evt");
            slider.Title = MyStringId.GetOrCompute("Clone Growth %");
            slider.Tooltip = MyStringId.GetOrCompute("Current clone growth percentage (0-100)");
            slider.SetLimits(0f, 100f);
            slider.Getter = block => block.GameLogic?.GetAs<CryoRespawnLogic>()?.PercentGrown ?? 0f;
            slider.Setter = (block, value) => { };
            slider.Writer = (block, sb) =>
                sb.Append($"{block.GameLogic?.GetAs<CryoRespawnLogic>()?.PercentGrown ?? 0f:F1}%");
            slider.Visible = block => block.GameLogic?.GetAs<CryoRespawnLogic>() != null;
            slider.Enabled = block => false;
            MyAPIGateway.TerminalControls.AddControl<T>(slider);
        }
        public bool IsBlockValidForList(Sandbox.ModAPI.IMyTerminalBlock block)
            => block.GameLogic?.GetAs<CryoRespawnLogic>() != null;
        public void AddBlocks(List<Sandbox.ModAPI.IMyTerminalBlock> blocks)
        {
            foreach (var b in blocks)
                if (!MonitoredBlocks.Contains(b)) MonitoredBlocks.Add(b);
            SaveMonitoredBlocks();
        }
        public void RemoveBlocks(IEnumerable<Sandbox.ModAPI.IMyTerminalBlock> blocks)
        {
            foreach (var b in blocks) MonitoredBlocks.Remove(b);
            SaveMonitoredBlocks();
        }
        public void NotifyValuesChanged() { }
        public override string ComponentTypeDebugString => "CryoGrowthEventComponent";
    }
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_CryoStabilityEventComponent : MyObjectBuilder_ComponentBase { }
    [MyComponentBuilder(typeof(MyObjectBuilder_CryoStabilityEventComponent))]
    public class CryoStabilityEventComponent : MyEntityComponentBase, IMyEventComponentWithGui
    {
        private static bool _controlsCreated = false;
        public readonly List<Sandbox.ModAPI.IMyTerminalBlock> MonitoredBlocks = new List<Sandbox.ModAPI.IMyTerminalBlock>();
        public MyStringId EventDisplayName => MyStringId.GetOrCompute("Clone Stable");
        public bool IsSelected { get; set; }
        public long UniqueSelectionId => 183829104738291002L;
        public string YesNoToolbarYesDescription => "Chamber is stable";
        public string YesNoToolbarNoDescription => "Chamber is not stable";
        public bool IsBlocksListUsed => true;
        public bool IsConditionSelectionUsed => false;
        public bool IsThresholdUsed => false;
        private static readonly Guid StabilityStorageKey = new Guid("939f87ad-a2f7-4252-9665-dc4c2022d118");
        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            var term = Container?.Entity as Sandbox.ModAPI.IMyTerminalBlock;
            if (term != null) term.AppendingCustomInfo += OnAppendCustomInfo;
            RestoreMonitoredBlocks();
        }
        public override void OnBeforeRemovedFromContainer()
        {
            var term = Container?.Entity as Sandbox.ModAPI.IMyTerminalBlock;
            if (term != null) term.AppendingCustomInfo -= OnAppendCustomInfo;
            base.OnBeforeRemovedFromContainer();
        }
        private void RestoreMonitoredBlocks()
        {
            var entity = Container?.Entity;
            if (entity?.Storage == null) return;

            string data;
            if (!entity.Storage.TryGetValue(StabilityStorageKey, out data)
                || string.IsNullOrEmpty(data)) return;

            foreach (var idStr in data.Split(','))
            {
                long id;
                if (!long.TryParse(idStr.Trim(), out id)) continue;
                var block = MyAPIGateway.Entities.GetEntityById(id)
                    as Sandbox.ModAPI.IMyTerminalBlock;
                if (block != null && !MonitoredBlocks.Contains(block))
                    MonitoredBlocks.Add(block);
            }
        }
        private void SaveMonitoredBlocks()
        {
            var entity = Container?.Entity;
            if (entity == null) return;

            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();

            entity.Storage[StabilityStorageKey] = string.Join(",",
                MonitoredBlocks.ConvertAll(b => b.EntityId.ToString()));
        }
        private void OnAppendCustomInfo(Sandbox.ModAPI.IMyTerminalBlock block, System.Text.StringBuilder sb)
        {
            var ecBlock = block as Sandbox.ModAPI.IMyEventControllerBlock;
            if (!(ecBlock?.SelectedEvent is CryoStabilityEventComponent)) return;
            foreach (var mon in MonitoredBlocks.ToList())
            {
                var logic = mon?.GameLogic?.GetAs<CryoRespawnLogic>();
                if (logic == null) continue;
                sb.AppendLine($"Input: {mon.DisplayNameText} = {(logic.Stable ? "Stable" : "Unstable")}");
            }
        }
        public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
        => new MyObjectBuilder_CryoStabilityEventComponent();
        public override void Deserialize(MyObjectBuilder_ComponentBase builder) { }
        public override bool IsSerialized() => false;
        void IMyEventControllerEntityComponent.CreateTerminalInterfaceControls<T>()
        {
            if (_controlsCreated) return;
            _controlsCreated = true;
            var check = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlCheckbox, T>("CryoRespawn_IsStable_Evt");
            check.Title = MyStringId.GetOrCompute("Clone Stable");
            check.Tooltip = MyStringId.GetOrCompute("Whether the clone chamber is currently stable");
            check.Getter = block => block.GameLogic?.GetAs<CryoRespawnLogic>()?.Stable ?? false;
            check.Setter = (block, value) => { };
            check.Visible = block => block.GameLogic?.GetAs<CryoRespawnLogic>() != null;
            check.Enabled = block => false;
            MyAPIGateway.TerminalControls.AddControl<T>(check);
        }
        public bool IsBlockValidForList(Sandbox.ModAPI.IMyTerminalBlock block)
            => block.GameLogic?.GetAs<CryoRespawnLogic>() != null;
        public void AddBlocks(List<Sandbox.ModAPI.IMyTerminalBlock> blocks)
        {
            foreach (var b in blocks)
                if (!MonitoredBlocks.Contains(b)) MonitoredBlocks.Add(b);
            SaveMonitoredBlocks();
        }
        public void RemoveBlocks(IEnumerable<Sandbox.ModAPI.IMyTerminalBlock> blocks)
        {
            foreach (var b in blocks) MonitoredBlocks.Remove(b);
            SaveMonitoredBlocks();
        }
        public void NotifyValuesChanged() { }
        public override string ComponentTypeDebugString => "CryoStabilityEventComponent";
    }
    public class CryoRespawnConfig
    {
        public string Version = "1.000.800";
        // Growth phase - ticks between each growth increment (default 60 = 1 second)
        public float GrowthTickRate = 60f;
        // Decay phase - ticks between each decay increment when unstable (default 129600 = 36 min)
        public int DecayTickRate = 216000;
        // Consumption - default tick fallback when no food data yet (default 3600 = 1 min for gravel)
        public int DefaultEatenTicks = 3600;
        // Offline pause rate - ticks used for all rates when owner is offline (default 36288000 = ~1 week)
        public int OfflineTickRate = 36288000;
        // Gold/Platinum/Uranium ingot amount required per buff tick
        public int BuffIngotAmount = 10;
        // Minimum gravel (Stone ingot) required to keep a grown clone stable
        public int GravelMinAmount = 1;
        // Minimum ice required during growth phase
        public int IceMinAmount = 4;
        // Minimum silver required during growth phase
        public int SilverMinAmount = 2;
        // Total growth points needed to fully grow a clone (default 200)
        public float MaxGrowth = 200f;
        //Food consumption multipliers
        public float FoodMultiplier = 1f;
        // Toggle for buff system, turns on/off all buffs and related mechanics.
        public bool BuffSystemEnabled = false;
        // Buff pull rates
        public float MagPull = 0.6f;
        public float CobPull = 0.8f;
        public float GoldPull = 0.9f;
        public float PlatPull = 0.7f;
        public float UraPull = 0.5f;
        // Buff pull angles
        public int MagAng = 45;
        public int CobAng = 117;
        public int GoldAng = 333;
        public int PlatAng = 189;
        public int UraAng = 261;
        //DebugMode
        public bool DebugMode = false;
    }
    public static class CryoRespawnConfigManager
    {
        private const string CONFIG_FILE = "CryoRespawnConfig.xml";
        private const string MOD_VERSION = "1.000.800";
        public static CryoRespawnConfig Config = new CryoRespawnConfig();

        public static void Load()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE, typeof(CryoRespawnMod)))
                {
                    var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE, typeof(CryoRespawnMod));
                    string xml = reader.ReadToEnd();
                    reader.Close();
                    Config = MyAPIGateway.Utilities.SerializeFromXML<CryoRespawnConfig>(xml);
                    MyLog.Default.WriteLineAndConsole("CryoRespawn: Config loaded successfully.");
                }
                else
                {
                    Save();
                    MyLog.Default.WriteLineAndConsole("CryoRespawn: No config found, defaults written to " + CONFIG_FILE);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("CryoRespawn: Failed to load config, using defaults. Error: " + e);
                Config = new CryoRespawnConfig();
            }
        }
        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                sb.AppendLine("<!-- ==========================================");
                sb.AppendLine("     CryoRespawn Configuration File");
                sb.AppendLine("     Mod Version: " + MOD_VERSION);
                sb.AppendLine("     Edit values below to customize behaviour.");
                sb.AppendLine("     Changes take effect on world reload.");
                sb.AppendLine("     ========================================== -->");
                sb.AppendLine("<CryoRespawnConfig>");
                sb.AppendLine();
                sb.AppendLine("  <!-- How many game ticks between each growth increment.");
                sb.AppendLine("       60 ticks = 1 second. Default: 60 -->");
                sb.AppendLine("  <GrowthTickRate>" + Config.GrowthTickRate + "</GrowthTickRate>");
                sb.AppendLine();
                sb.AppendLine("  <!-- How many ticks between each decay increment when the clone is unstable.");
                sb.AppendLine("       129600 ticks = 36 minutes. Default: 129600 -->");
                sb.AppendLine("  <DecayTickRate>" + Config.DecayTickRate + "</DecayTickRate>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Default food consumption interval in ticks when no specific food data is found.");
                sb.AppendLine("       3600 ticks = 1 minute. Default: 3600 -->");
                sb.AppendLine("  <DefaultEatenTicks>" + Config.DefaultEatenTicks + "</DefaultEatenTicks>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Tick rate applied to all timers when the chamber owner is offline.");
                sb.AppendLine("       36288000 ticks = approximately 1 week. Default: 36288000 -->");
                sb.AppendLine("  <OfflineTickRate>" + Config.OfflineTickRate + "</OfflineTickRate>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Number of Gold, Platinum, or Uranium ingots consumed per buff");
                sb.AppendLine("       application during the growth phase. Default: 10 -->");
                sb.AppendLine("  <BuffIngotAmount>" + Config.BuffIngotAmount + "</BuffIngotAmount>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Minimum Stone ingots (gravel) required to keep a grown clone stable.");
                sb.AppendLine("       Default: 1 -->");
                sb.AppendLine("  <GravelMinAmount>" + Config.GravelMinAmount + "</GravelMinAmount>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Minimum Ice ore required during the growth phase. Default: 4 -->");
                sb.AppendLine("  <IceMinAmount>" + Config.IceMinAmount + "</IceMinAmount>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Minimum Silver ingots required during the growth phase. Default: 2 -->");
                sb.AppendLine("  <SilverMinAmount>" + Config.SilverMinAmount + "</SilverMinAmount>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Total growth points required to fully grow a clone.");
                sb.AppendLine("       Combined with GrowthTickRate determines total grow time.");
                sb.AppendLine("       Default 200 with 60 tick rate = 200 seconds to fully grow. -->");
                sb.AppendLine("  <MaxGrowth>" + Config.MaxGrowth + "</MaxGrowth>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Food consumption multiplier, affects how long a clone . Default: 1.0 -->");
                sb.AppendLine("  <FoodMultiplier>" + Config.FoodMultiplier + "</FoodMultiplier>"); 
                sb.AppendLine();
                sb.AppendLine("  <!-- Toggle for buff system, turns on/off all buffs and related mechanics. Default: false -->");
                sb.AppendLine("  <BuffSystemEnabled>" + Config.BuffSystemEnabled.ToString().ToLower() + "</BuffSystemEnabled>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Buff pull rates, values between 0.0 and 1.0. Default: Mag=0.6, Cob=0.8, Gold=0.9, Plat=0.7, Ura=0.5 -->");
                sb.AppendLine("  <MagPull>" + Config.MagPull + "</MagPull>");
                sb.AppendLine("  <CobPull>" + Config.CobPull + "</CobPull>");
                sb.AppendLine("  <GoldPull>" + Config.GoldPull + "</GoldPull>");
                sb.AppendLine("  <PlatPull>" + Config.PlatPull + "</PlatPull>");
                sb.AppendLine("  <UraPull>" + Config.UraPull + "</UraPull>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Buff pull angles, values between 0 and 359 degrees. Default: Mag=45, Cob=117, Gold=333, Plat=189, Ura=261 -->");
                sb.AppendLine("  <!--Try not to set any of these angles to 0, 90, 180, or 270.-->");
                sb.AppendLine("  <!--Setting them to cardinal directions can cause the value to become massive.-->");
                sb.AppendLine("  <MagAng>" + Config.MagAng + "</MagAng>");
                sb.AppendLine("  <CobAng>" + Config.CobAng + "</CobAng>");
                sb.AppendLine("  <GoldAng>" + Config.GoldAng + "</GoldAng>");
                sb.AppendLine("  <PlatAng>" + Config.PlatAng + "</PlatAng>");
                sb.AppendLine("  <UraAng>" + Config.UraAng + "</UraAng>");
                sb.AppendLine();
                sb.AppendLine("  <!-- Debug mode, enables additional logging and terminal info. Default: false -->");
                sb.AppendLine("  <DebugMode>" + Config.DebugMode.ToString().ToLower() + "</DebugMode>");
                sb.AppendLine("</CryoRespawnConfig>");
                var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE, typeof(CryoRespawnMod));
                writer.Write(sb.ToString());
                writer.Close();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("CryoRespawn: Failed to save config. Error: " + e);
            }
        }
    }
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CryoChamber), false)]
    public class CryoRespawnLogic : MyGameLogicComponent, IMyEventProxy
    {
        public Guid SETTINGS_GUID = new Guid("b7243792-1cf3-467d-9c90-9e071c134637");
        public static ushort NetworkId = 42007;
        private int delayCounter;
        private const int delayTicks = 60;
        public long Eaten
        {
            get { return Settings.Eaten; }
            set { Settings.Eaten = value; }
        }
        public long Sloweat
        {
            get { return Settings.Sloweat; }
            set { Settings.Sloweat = value; }
        }
        Sandbox.ModAPI.IMyCryoChamber Chamber;
        Sandbox.ModAPI.IMyTerminalBlock Block;
        public CryoChamberBlockSettings Settings = new CryoChamberBlockSettings();
        public bool Stable;
        bool Playerdead;
        bool OxygenFilled;
        bool EnoughGravel;
        bool EnoughIce;
        bool EnoughSilver;
        bool Podplaced;
        bool Grown;
        bool FoodC;
        bool wasOwnerOnline = true;
        int StableCounts;
        long PlayerID;
        float PGrowth;
        public float PercentGrown;
        float grownslowed = 0;
        int Slowdecay = 0;
        long owner;
        string Playername;
        string rawData;
        bool Loadsetting;
        int syncCountdown;
        int DEat;
        int stepTicks;
        float GEat;// Open to admin
        float OfflineRate = 36288000;// Open to admin
        const int SETTINGS_CHANGED_COUNTDOWN = 60;
        private MatrixD ChamberSpot;
        private Vector3D ChamberVelocity;
        int BuffOverrideTimer = 0;
        float MBuffEating = 0;
        float CBuffEating = 0;
        float GBuffEating = 0;
        float PBuffEating = 0;
        float UBuffEating = 0;
        float BuffEats = 0;
        int Xoffset = 0;
        int Yoffset = 0;
        float TME = 0;
        float TCE = 0;
        float TGE = 0;
        float TPE = 0;
        float TUE = 0;
        //Final buffs multiplers, ranges -1 to 1
        public float MagBuff = 0;
        public float CobBuff = 0;
        public float GoldBuff = 0;
        public float PlatBuff = 0;
        public float UraBuff = 0;
        // Resulting Buffs
        float EnergyDrainMultipler = 1.0f;
        float HungerDrainMultiplier = 1.0f;
        int Growthcycle = 1;// Used to manage the growth cycle of the clone.
        int Maxgrowthcycle;//Sets number of cycles
        int XStability = 0;
        int YStability = 0;
        int Buffunstable = 0;
        int StartingStability = 10;
        int StepStability = 5;// Amount of stability gained per cycle
        int FinalStability = 0;
        int AllowedStatbility = 0;
        //=======================
        int DisplayMag;
        int DisplayCob;
        int DisplayGold;
        int DisplayPlat;
        int DisplayUra;
        float autoHealLimit = 0.7f;
        int bufflevel = 0;
        //========================
        public const float MagMin = -1.92f, MagMax = 2.23f;
        public const float CobMin = -5.98f, CobMax = 4.24f;
        public const float PlatMin = -5.03f, PlatMax = 4.73f;
        public const float UraMin = -4.64f, UraMax = 4.18f;
        public const float GoldMin = -6.47f, GoldMax = 7.4f;
        //========================
        private int _ecNotifyCountdown = 0;
        private const int EC_NOTIFY_INTERVAL = 60;
        private readonly HashSet<long> _ourSpawnedIds = new HashSet<long>();
        CryoRespawnMod Mod => CryoRespawnMod.Instance;
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Chamber = (Sandbox.ModAPI.IMyCryoChamber)(Entity as Sandbox.ModAPI.Ingame.IMyCryoChamber);
            base.Init(objectBuilder);
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                Chamber = (Sandbox.ModAPI.IMyCryoChamber)Entity;
                Block = (Sandbox.ModAPI.IMyTerminalBlock)Entity;
                LoadSettings();
                Grown = Settings.Grown;
                SaveSettings();
                SettingsChanged();
                SyncSettings();
                PGrowth = CryoRespawnConfigManager.Config.MaxGrowth;
                DEat = CryoRespawnConfigManager.Config.DecayTickRate;
                GEat = CryoRespawnConfigManager.Config.GrowthTickRate;
                Maxgrowthcycle = (int)CryoRespawnConfigManager.Config.MaxGrowth;
                Foodmodcheck();
                if (Chamber.OwnerId == 0)
                {
                    NeedsUpdate = MyEntityUpdateEnum.NONE;
                    return;
                }
                if (Entity.Storage == null) { Entity.Storage = new MyModStorageComponent(); }
                if (Chamber.CubeGrid.Physics != null)
                {
                    NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
                    Podplaced = true;
                }
                if (Block.CubeGrid?.Physics == null)
                    return;
                Block.AppendingCustomInfo += AppendingCustomInfo;
                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }
        void AppendingCustomInfo(Sandbox.ModAPI.IMyTerminalBlock block, StringBuilder sb)
        {
            sb.Append("==CryoRespawn==\n");
            if (!Stable && PercentGrown > 0)
            {
                sb.Append("Clone has decayed: " + Settings.Decay + "%\n");
                int decayStepsRemaining = (int)Math.Min(100 - Settings.Decay, Settings.Growth);
                int totalDecayTicks = Math.Max(0, stepTicks - Slowdecay) + (Math.Max(0, decayStepsRemaining - 1) * stepTicks);
                sb.Append("Time left untill fully decayed: " + TicksToTime(totalDecayTicks) + "\n");
            }
            else
            {
                if (!Grown && Stable)
                {
                    int growthStepsRemaining = (int)(PGrowth - Settings.Growth);
                    int totalGrowthTicks = Math.Max(0, (int)GEat - (int)grownslowed) + (Math.Max(0, growthStepsRemaining - 1) * (int)GEat);
                    sb.Append("Time until fully grown: " + TicksToTime(totalGrowthTicks) + "\n");
                }
            }
            if (!Grown)
            {
                if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
                    sb.AppendLine($"Buff Stability: {100 - ((float)FinalStability / AllowedStatbility * 100f)}%");
                if (FinalStability > AllowedStatbility)
                {
                    sb.AppendLine("Warning: Buff instability! Clone will die.");
                    sb.AppendLine("Stop feeding clone buff material or feed it counteracting materials to stabilize it.");
                    sb.AppendLine($"Clone death in: {Buffunstable / 60}s");
                }
                sb.Append("Chamber Stable: " + (Stable ? "YES" : "NO") + "\n");
                sb.Append("Clone has grown: " + PercentGrown + "%\n");
                sb.Append("Chamber functionality: " + (Chamber.IsWorking ? "OPERATIONAL" : "DISABLED") + "\n");
                sb.Append("Chamber has oxygen: " + (OxygenFilled ? "YES" : "NO") + "\n");
                sb.Append("Chamber has gravel: " + (EnoughGravel ? "YES" : "NO") + "\n");
                sb.Append("Chamber has ice: " + (EnoughIce ? "YES" : "NO") + "\n");
                sb.Append("Chamber has silver ingot: " + (EnoughSilver ? "YES" : "NO") + "\n");
            }
            else
            {
                if (CryoRespawnConfigManager.Config.DebugMode)
                {
                    sb.Append("Clone has grown: " + PercentGrown + "%\n");
                    sb.Append("Clone has decayed: " + Settings.Decay + "%\n");
                    sb.Append("Clone is grown: " + (Grown ? "YES" : "NO") + "\n");
                    sb.Append("Chamber functionality: " + (Chamber.IsWorking ? "OPERATIONAL" : "DISABLED") + "\n");
                    sb.Append("Chamber has oxygen: " + (OxygenFilled ? "YES" : "NO") + "\n");
                    sb.Append("Chamber has gravel: " + (EnoughGravel ? "YES" : "NO") + "\n");
                    sb.Append("Chamber has ice: " + (EnoughIce ? "YES" : "NO") + "\n");
                    sb.Append("Chamber has silver ingot: " + (EnoughSilver ? "YES" : "NO") + "\n");
                }
                sb.Append(Playername + " can spawn in this chamber.\n");
                sb.Append("Chamber Stable: " + (Stable ? "YES" : "NO") + "\n");
                sb.Append("Chamber has food: " + (FoodC || EnoughGravel ? "YES" : "NO") + "\n");
                sb.Append("Next meal in: " + TicksToTime((int)Math.Max(0, Eaten - Sloweat)) + "\n");
                sb.Append("Food lasts: " + GetFoodDuration() + "\n");
            }
            if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
            {
                sb.Append("==Stats==\n");
                sb.AppendLine($"Stability Location: ({XStability}, {YStability})");
                sb.AppendLine($"Magnesium Buffs: {DisplayMag}%, Total: {TME}kg");
                sb.AppendLine($"Cobalt Buffs: {DisplayCob}%, Total: {TCE}kg");
                sb.AppendLine($"Gold Buffs: {DisplayGold}%, Total: {TGE}kg");
                sb.AppendLine($"Platinum Buffs: {DisplayPlat}%, Total: {TPE}kg");
                sb.AppendLine($"Uranium Buffs: {DisplayUra}%, Total: {TUE}kg");
            }
            sb.Append("==Spawn Count==\n");
            sb.Append(Playername + " has used this chamber " + Settings.Playerdied + " times for respawn\n");
        }
        void LogError(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"ERROR on {GetType().FullName}: {e}");
            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR on {GetType().FullName}: CryoRespawn had an error. Send log to Ph.D Composer, Discord ID 276789062197706753", 10000, MyFontEnum.Red);
        }
        public override void UpdateAfterSimulation()
        {
            if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
            {
                Autohealing();
            }
            if (Podplaced)
            {
                //Check if the pod is owned by an NPC
                if (MyAPIGateway.Players == null)
                {
                    return;
                }
                if (Chamber.OwnerId == 0) return;
                if (Chamber == null || Block == null)
                {
                    LogError(new NullReferenceException("Chamber or Block is null"));
                    return;
                }
                OfflineCheck();
                GrownState();
                var Player = new List<IMyPlayer>();
                var play = new List<IMyIdentity>();
                owner = Chamber.OwnerId;
                if (MyAPIGateway.Players == null)
                {
                    LogError(new NullReferenceException("MyAPIGateway.Players is null"));
                    return;
                }
                MyAPIGateway.Players.GetPlayers(Player);
                MyAPIGateway.Players.GetAllIdentites(play);
                foreach (var plays in play)
                {
                    PlayerID = plays.IdentityId;
                    if (PlayerID == owner)
                    {
                        Playername = plays.DisplayName;
                    }
                }
                foreach (var player in Player)
                {
                    if (player == null)
                    {
                        LogError(new NullReferenceException("Player is null in Player list"));
                        continue;
                    }
                    PlayerID = player.IdentityId;
                    if (PlayerID == owner)
                    {
                        if (player.Character == null)//Common spot to hit
                        {
                            // Retry logic: delay and try again if character is null
                            MyLog.Default.WriteLineAndConsole($"Player character is null for player ID {PlayerID}, retrying...");
                            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
                            return;
                        }
                        Playerdead = player.Character.IsDead;
                    }
                    if (Playerdead)
                    {
                        StableCheck();
                        SpawnPlayer();
                    }
                    if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
                        ApplyEnergyDrain(player.Character);
                }
                if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
                {
                    Block.RefreshCustomInfo();
                    Block.SetDetailedInfoDirty();
                }
                if (--_ecNotifyCountdown <= 0)
                {
                    _ecNotifyCountdown = EC_NOTIFY_INTERVAL;
                    NotifyEventControllers();
                }
            }
        }
        void SaveSettings()
        {
            if (Chamber == null)
                return;
            if (Settings == null)
                throw new NullReferenceException($"Settings == null on entId={Entity?.EntityId}; modInstance={CryoRespawnMod.Instance != null}");
            if (MyAPIGateway.Utilities == null)
                throw new NullReferenceException($"MyAPIGateway.Utilities == null; entId={Entity?.EntityId}; modInstance={CryoRespawnMod.Instance != null}");
            if (Chamber.Storage == null)
                Chamber.Storage = new MyModStorageComponent();
            Settings.SavedMagBuff = MagBuff;
            Settings.SavedCobBuff = CobBuff;
            Settings.SavedGoldBuff = GoldBuff;
            Settings.SavedPlatBuff = PlatBuff;
            Settings.SavedUraBuff = UraBuff;
            Chamber.Storage.SetValue(SETTINGS_GUID, Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Settings)));
        }
        void SettingsChanged()
        {
            if (syncCountdown == 0)
                syncCountdown = SETTINGS_CHANGED_COUNTDOWN;
        }
        void SyncSettings()
        {
            if (syncCountdown > 0 && --syncCountdown <= 0)
            {
                SaveSettings();
                Mod.CachedPacketSettings.Send(Chamber.EntityId, Settings);
            }
        }
        public override bool IsSerialized()
        {
            try
            {
                SaveSettings();
            }
            catch (Exception e)
            {
                LogError(e);
            }
            return base.IsSerialized();
        }
        void LoadSettings()
        {
            if (Chamber.Storage == null)
            { Loadsetting = false; return; }
            if (!Chamber.Storage.TryGetValue(SETTINGS_GUID, out rawData))
            { Loadsetting = false; return; }
            try
            {
                var loadedSettings = MyAPIGateway.Utilities.SerializeFromBinary<CryoChamberBlockSettings>(Convert.FromBase64String(rawData));
                if (loadedSettings != null)
                {
                    Settings.Grown = loadedSettings.Grown;
                    Settings.Growth = loadedSettings.Growth;
                    Settings.Decay = loadedSettings.Decay;
                    Settings.Playerdied = loadedSettings.Playerdied;
                    Settings.Eaten = CryoRespawnConfigManager.Config.DefaultEatenTicks;
                    Settings.Sloweat = loadedSettings.Sloweat;
                    MagBuff = loadedSettings.SavedMagBuff;
                    CobBuff = loadedSettings.SavedCobBuff;
                    GoldBuff = loadedSettings.SavedGoldBuff;
                    PlatBuff = loadedSettings.SavedPlatBuff;
                    UraBuff = loadedSettings.SavedUraBuff;
                    if (loadedSettings.Eaten == 0)
                        Settings.Eaten = 3600;
                    else
                        Settings.Eaten = loadedSettings.Eaten;
                    //Settings.Sloweat = loadedSettings.Sloweat;
                    Loadsetting = true;
                }
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }
        public void GrownState()
        {
            StableCheck();
            if (Stable)
            {
                if (Grown)
                {
                    /*if (!botspawned)
                    {
                        if (BOTcount == 0)
                        {
                            SpawnBOT();
                            botspawned = true;
                        }
                    }*/
                    Sloweat++;
                    if (Sloweat >= Eaten)
                    {
                        bool ate = ChamberFoodConsumption();
                        if (ate)
                        {
                            Sloweat = 0;
                            PercentGrown = 100;
                            Settings.Decay = 0;
                            Settings.Growth = PGrowth;
                        }
                        else
                        {
                            Sloweat = Eaten;
                        }
                    }
                }
                else
                {
                    grownslowed++;
                    if (grownslowed == GEat)
                    {
                        Settings.Growth++;
                        Chamber.GetInventory().RemoveItemsOfType(4, new SerializableDefinitionId(typeof(MyObjectBuilder_Ore), "Ice"));
                        Chamber.GetInventory().RemoveItemsOfType(1, new SerializableDefinitionId(typeof(MyObjectBuilder_Ingot), "Stone"));
                        Chamber.GetInventory().RemoveItemsOfType(2, new SerializableDefinitionId(typeof(MyObjectBuilder_Ingot), "Silver"));
                        grownslowed = 0;
                        if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
                            NewBuffsystem();
                        // Set emissive color to indicate growth, color of green
                        var Color = new Color(0, 255, 25);
                        Chamber.SetEmissiveParts("Emissive", Color, 1f);
                        if (Settings.Decay > 0)
                        {
                            Settings.Decay = 0;
                        }
                    }
                }
            }
            else
            {
                DecayStatus();
            }
            PercentGrown = Settings.Growth / PGrowth * 100;
            if (Settings.Growth == PGrowth)
            {
                Grown = true;
                Settings.Grown = true;
            }
        }
        public void GrowthCheck()
        {
            if (!Chamber.HasInventory) return;
            var inventory = (MyInventory)Chamber.GetInventory();
            var gravelId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Stone");
            EnoughGravel = inventory.ContainItems(CryoRespawnConfigManager.Config.GravelMinAmount, gravelId);
            if (EnoughGravel) { StableCounts++; }
            else
            {
                StableCounts = 0;
                // Set emissive color to indicate missing gravel, color of cyan
                var Color = new Color(0, 255, 255);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
            }
            var iceId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), "Ice");
            EnoughIce = inventory.ContainItems(CryoRespawnConfigManager.Config.IceMinAmount, iceId);
            if (EnoughIce) { StableCounts++; }
            else
            {
                StableCounts = 0;
                // Set emissive color to indicate missing ice, color of orange
                var Color = new Color(255, 128, 0);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
            }
            var silverId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Silver");
            EnoughSilver = inventory.ContainItems(CryoRespawnConfigManager.Config.SilverMinAmount, silverId);
            if (EnoughSilver) { StableCounts++; }
            else
            {
                StableCounts = 0;
                // Set emissive color to indicate missing silver, color of magenta
                var Color = new Color(255, 0, 255);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
            }
            if (Chamber.OxygenFilledRatio == 0)
            {
                OxygenFilled = false;
                // Set emissive color to indicate missing oxygen, color of red
                var Color = new Color(255, 0, 0);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
                StableCounts = 0;
            }
            else
            {
                OxygenFilled = true;
                StableCounts++;
            }
        }
        public void GrownCheck()
        {
            if (!Chamber.HasInventory) return;
            var inventory = (MyInventory)Chamber.GetInventory();
            var gravelId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Stone");
            EnoughGravel = inventory.ContainItems(1, gravelId);
            var foodSubtypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {"SteakDinner", "SearedSabiroid", "FrontierStew", "Burrito", "Lasagna","Spaghetti", "Dumplings", "GreenPellets", "Curry", "VeggieBurger","FruitPastry", "Ramen", "FlatBread",
                "Chili", "RedPellets","GardenSlaw", "FruitBar", "MammalMeatCooked", "InsectMeatCooked","Kelp", "Mushroom", "Fruit", "Vegetables"};
            foreach (var mf in moddedFoods)
                foodSubtypes.Add(mf.Sub);
            var iinv = Chamber.GetInventory() as VRage.Game.ModAPI.IMyInventory;
            var items = new List<MyInventoryItem>();
            if (iinv != null) iinv.GetItems(items);
            FoodC = items.Any(item =>
            {
                string fullType = item.Type.ToString();
                return foodSubtypes.Any(sub => fullType.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0);
            });

            if (EnoughGravel || FoodC || Sloweat < Eaten)
            {
                StableCounts++;
                StableCounts++;
                var Color = new Color(255, 255, 255);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
            }
            else
            {
                StableCounts = 0;
                // Set emissive color to indicate missing food, color of amber
                var Color = new Color(255, 191, 0);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
            }
            if (Chamber.OxygenFilledRatio == 0)
            {
                OxygenFilled = false;
                // Set emissive color to indicate missing oxygen, color of red
                var Color = new Color(255, 0, 0);
                Chamber.SetEmissiveParts("Emissive", Color, 1f);
                StableCounts = 0;
            }
            else
            {
                StableCounts++;
                StableCounts++;
                OxygenFilled = true;
            }
        }
        public void StableCheck()
        {
            if (!Chamber.IsWorking || !Chamber.IsFunctional)
            {
                Stable = false;
                StableCounts = 0;
            }
            else
            {
                StableCounts++;
                if (Grown)
                {
                    GrownCheck();
                }
                else
                {
                    GrowthCheck();
                }
            }
            if (StableCounts >= 4)
            {
                Stable = true;
            }
            else
            {
                Stable = false;
            }
            StableCounts = 0;
        }
        public void DecayStatus()
        {
            stepTicks = Math.Max(1, DEat / 100);
            Slowdecay++;
            if (Slowdecay == stepTicks)
            {
                Settings.Decay++;
                /*Settings.Growth--;
                if (Settings.Growth < 0)
                { Settings.Growth = 0; Settings.Decay = 0; }*/
                Slowdecay = 0;
                if (Settings.Decay == 100 || Settings.Growth == 0)
                {
                    //DespawnBOT();
                    Grown = false;
                    Settings.Grown = false;
                    Settings.Growth = 0;
                    Settings.Decay = 0;
                }
            }
        }
        public void SpawnPlayer()
        {
            if (!Grown) return;
            if (!MyAPIGateway.Session.IsServer) return;
            var AllPlayers = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(AllPlayers);
            long owner = Chamber.OwnerId;
            foreach (var player in AllPlayers)
            {
                if (player.IdentityId != owner) continue;
                bool playerIsSpawning = player.Character == null || player.Character.IsDead;
                if (!playerIsSpawning) continue;
                if (delayCounter < delayTicks)
                {
                    delayCounter++;
                    return;
                }
                //DespawnBOT();
                MatrixD chamberSpot = Chamber.WorldMatrix;
                Vector3D chamberVelocity = Chamber.CubeGrid.Physics.LinearVelocity;
                MyVisualScriptLogicProvider.SpawnPlayer(chamberSpot, chamberVelocity, player.IdentityId);
                bufflevel = 0;
                BuffOverrideTimer = 0;
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    // Re-fetch the player to get the fresh character reference
                    var freshPlayers = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(freshPlayers);
                    var freshPlayer = freshPlayers.Find(p => p.IdentityId == owner);
                    if (freshPlayer?.Character == null || freshPlayer.Character.IsDead) return;
                    if (CryoRespawnConfigManager.Config.BuffSystemEnabled)
                        ApplynewBuffs(freshPlayer.Character);
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        MyVisualScriptLogicProvider.CockpitInsertPilot(Chamber.Name, true, freshPlayer.IdentityId);
                    });
                    MagBuff = 0;
                    CobBuff = 0;
                    GoldBuff = 0;
                    PlatBuff = 0;
                    UraBuff = 0;
                    XStability = 0;
                    YStability = 0;
                    Growthcycle = 1;
                    AllowedStatbility = StartingStability;
                });
                // Reset state
                delayCounter = 0;
                Grown = false;
                Settings.Grown = false;
                Settings.Growth = 0;
                Settings.Decay = 0;
                Settings.Playerdied++;
                HungerDrainMultiplier = 1.0f;
                EnergyDrainMultipler = 1.0f;
                break;
            }
        }
        class FoodDef
        {
            public string Sub;
            public int Ticks;
            public string[] Types;
            public FoodDef(string sub, int ticks, string[] types)
            {
                Sub = sub; Ticks = ticks; Types = types;
            }
        }
        public bool ChamberFoodConsumption()
        {
            VRage.Game.ModAPI.IMyInventory inv = Chamber.GetInventory();
            // priority list: subtype, ticks, accepted builder types
            var food = new List<FoodDef>
            {
        new FoodDef("SteakDinner",    (int)(216000 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("SearedSabiroid", (int)(216000 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("FrontierStew",   (int)(203040 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Burrito",        (int)(192240 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Lasagna",        (int)(162000 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Spaghetti",      (int)(153360 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Dumplings",      (int)(110160 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("GreenPellets",   (int)(103680 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Curry",          (int)(103680 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("VeggieBurger",    (int)(99360 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("FruitPastry",     (int)(86400 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Ramen",           (int)(77760 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("FlatBread",       (int)(73440 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Chili",           (int)(64800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("RedPellets",      (int)(49680 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("GardenSlaw",      (int)(47520 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("FruitBar",        (int)(38880 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("MammalMeatCooked",(int)(38880 * CryoRespawnConfigManager.Config.FoodMultiplier),new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("InsectMeatCooked",(int)(30240 * CryoRespawnConfigManager.Config.FoodMultiplier),new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Kelp",            (int)(25920 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Mushroom",        (int)(12960 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Fruit",           (int)(12960 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
        new FoodDef("Vegetables",      (int)(10800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem", "MyObjectBuilder_Ore" }),
            };
            if (moddedFoods.Count > 0)
            {
                food.AddRange(moddedFoods);
                food = food.OrderByDescending(f => f.Ticks).ToList();
            }
            // Try foods first
            foreach (FoodDef f in food)
            {
                int ticks;
                if (TryConsume(inv, f.Sub, 1, out ticks, f.Ticks, f.Types))
                {
                    Eaten = ticks;
                    return true;
                }
            }
            // Fallback: gravel (stone ingot)
            int gravelTicks;
            if (TryConsume(inv, "Stone", 1, out gravelTicks, 3600, new[] { "MyObjectBuilder_Ingot" }))
            {
                Eaten = gravelTicks;
                return true;
            }
            return false;
        }
        private bool TryConsume(VRage.Game.ModAPI.IMyInventory inv, string subtype, int amount, out int eatenTicks, int ticksValue, params string[] possibleTypes)
        {
            var items = new List<MyInventoryItem>();
            inv.GetItems(items);

            foreach (var it in items)
            {
                // it.Type.SubtypeId and it.Type.TypeId are implementation-specific types; ToString() is safe for comparison
                string itemSubtype = it.Type.SubtypeId.ToString();
                string itemTypeFull = it.Type.ToString(); // e.g. "MyObjectBuilder_Ore/Ice" or similar

                // match either exact subtype or appear-in-type string (both case-insensitive)
                bool subtypeMatches = string.Equals(itemSubtype, subtype, StringComparison.OrdinalIgnoreCase);
                bool typeStringContains = itemTypeFull.IndexOf(subtype, StringComparison.OrdinalIgnoreCase) >= 0;

                if ((subtypeMatches || typeStringContains) && it.Amount >= (MyFixedPoint)amount)
                {
                    // remove by ItemId so we remove the exact stack
                    inv.RemoveItems(it.ItemId, (MyFixedPoint)amount);
                    eatenTicks = ticksValue;
                    return true;
                }
            }
            eatenTicks = 0;
            return false;
        }
        private bool TryConsumeExact(VRage.Game.ModAPI.IMyInventory inv, string typeName, string subtype, int amount)
        {
            MyDefinitionId id;
            if (!MyDefinitionId.TryParse(typeName, subtype, out id))
                return false;
            if (((MyInventory)inv).ContainItems((MyFixedPoint)amount, id))
            {
                ((MyInventory)inv).RemoveItemsOfType((MyFixedPoint)amount, (SerializableDefinitionId)id);
                return true;
            }
            return false;
        }
        /*private void ApplyBuffs(IMyCharacter character, float decay, float mBuff, float wBuff, float gBuff, float oBuff, double iBuff, float rBuff, double tBuff)
        {
            try
            {
                var stats = character.Components.Get<MyCharacterStatComponent>();
                if (stats != null)
                {
                    //Movement speed
                    MyEntityStat moveStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("MovementSpeed"), out moveStat) && moveStat != null)
                        moveStat.Value = 12f * (mBuff / 150f);
                    // Welding speed
                    MyEntityStat weldingStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("WeldingSpeed"), out weldingStat) && weldingStat != null)
                        weldingStat.Value = 10f * (wBuff / 150f);
                    // Grinding speed
                    MyEntityStat grindingStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("GrindingSpeed"), out grindingStat) && grindingStat != null)
                        grindingStat.Value = 10f * (gBuff / 150f);
                    // Oxygen consumption
                    MyEntityStat oxygenStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("OxygenConsumption"), out oxygenStat) && oxygenStat != null)
                        oxygenStat.Value = Math.Max(0.1f, 1f - (oBuff / 100f));
                    // Radiation resistance
                    MyEntityStat radiationStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("RadiationResistance"), out radiationStat) && radiationStat != null)
                        radiationStat.Value = rBuff / 150f;// if Rbuff is 150, turn on full radiation resistance.
                    // Temperature resistance
                    MyEntityStat tempStat;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("TemperatureResistance"), out tempStat) && tempStat != null)
                        tempStat.Value = (float)tBuff;
                }
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }*/
        public void OfflineCheck()
        {
            //maybe pull the grown and growth values from storage here to prevent offline progress loss? Would require saving every tick, which is a bit much, but maybe every 10 ticks or so?
            if (owner == 0) return;
            bool OfflineToggle = true;// Open to admin configuration
            if (OfflineToggle)
            {
                var Player = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(Player);
                bool ownerOnline = Player.Any(p => p.IdentityId == owner);
                if (!ownerOnline)
                {
                    if (wasOwnerOnline)
                    {
                        Eaten = CryoRespawnConfigManager.Config.OfflineTickRate;
                        DEat = CryoRespawnConfigManager.Config.OfflineTickRate;
                        GEat = CryoRespawnConfigManager.Config.OfflineTickRate;
                        Sloweat = 0;
                        Slowdecay = 0;
                        Grown = Settings.Grown;
                        wasOwnerOnline = false;
                    }
                }
                else
                {
                    if (!wasOwnerOnline)
                    {
                        DEat = CryoRespawnConfigManager.Config.DecayTickRate;
                        GEat = CryoRespawnConfigManager.Config.GrowthTickRate;
                        Eaten = CryoRespawnConfigManager.Config.DefaultEatenTicks;
                        Sloweat = 0;
                        Slowdecay = 0;
                        Grown = Settings.Grown;
                        wasOwnerOnline = true;
                    }
                }
            }
        }
        private string GetFoodDuration()
        {
            var inv = Chamber.GetInventory() as VRage.Game.ModAPI.IMyInventory;
            if (inv == null) return "Unknown";

            var items = new List<MyInventoryItem>();
            inv.GetItems(items);

            // Same priority order as ChamberFoodConsumption
            var food = new List<FoodDef>
    {
        new FoodDef("SteakDinner",    216000, null),
        new FoodDef("SearedSabiroid", 216000, null),
        new FoodDef("FrontierStew",   203040, null),
        new FoodDef("Burrito",        192240, null),
        new FoodDef("Lasagna",        162000, null),
        new FoodDef("Spaghetti",      153360, null),
        new FoodDef("Dumplings",      110160, null),
        new FoodDef("GreenPellets",   103680, null),
        new FoodDef("Curry",          103680, null),
        new FoodDef("VeggieBurger",    99360, null),
        new FoodDef("FruitPastry",     86400, null),
        new FoodDef("Ramen",           77760, null),
        new FoodDef("FlatBread",       73440, null),
        new FoodDef("Chili",           64800, null),
        new FoodDef("RedPellets",      49680, null),
        new FoodDef("GardenSlaw",     47520, null),
        new FoodDef("FruitBar",        38880, null),
        new FoodDef("MammalMeatCooked",38880, null),
        new FoodDef("InsectMeatCooked",30240, null),
        new FoodDef("Kelp",            25920, null),
        new FoodDef("Mushroom",        12960, null),
        new FoodDef("Fruit",           12960, null),
        new FoodDef("Vegetables",      10800, null),
        new FoodDef("Stone",            3600, null), // gravel fallback, not really working. Will only show the 3600tick duration for the total time, even with 1000 gravel in the inventory.
    };
            if (moddedFoods.Count > 0)
            {
                food.AddRange(moddedFoods);
                food = food.OrderByDescending(f => f.Ticks).ToList();
            }
            int totalTicks = (int)Math.Max(0, Eaten - Sloweat);
            bool anyMatched = false;
            foreach (var f in food)
            {
                var matches = items.Where(item => item.Type.ToString().IndexOf(f.Sub, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (matches.Count == 0) continue;
                int count = (int)Math.Ceiling(matches.Sum(item => (double)item.Amount));
                if (!anyMatched)
                {
                    totalTicks += Math.Max(0, count - 1) * f.Ticks;
                    anyMatched = true;
                }
                else
                {
                    totalTicks += count * f.Ticks;
                }
            }
            return anyMatched ? TicksToTime(totalTicks) : "No food";
        }
        private string TicksToTime(int ticks)
        {
            //Used for the terminal info.
            int totalSeconds = ticks / 60;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            if (hours > 0)
                return $"{hours}h {minutes}m {seconds}s";
            if (minutes > 0)
                return $"{minutes}m {seconds}s";
            return $"{seconds}s";
        }
        private void Foodmodcheck()// Check for modded foods that can be used in the chamber.
        {
            moddedFoods.Clear();
            if (MyAPIGateway.Session?.Mods != null)
            {
                bool hasCoffeeMod = MyAPIGateway.Session.Mods.Any(m => m.FriendlyName != null && m.FriendlyName.IndexOf("Engineered Coffee (the Mod) + Potato VI-X", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasCoffeeDS = MyAPIGateway.Session.Mods.Any(m => m.FriendlyName != null && m.FriendlyName.IndexOf("Engineered Coffee (DS VERSION)", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasPotato = MyAPIGateway.Session.Mods.Any(m => m.FriendlyName != null && m.FriendlyName.IndexOf("Potato VI-X Fuel the Future, Sustain the Legend.", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasMuchfood = MyAPIGateway.Session.Mods.Any(m => m.FriendlyName != null && m.FriendlyName.IndexOf("CAT - Much Foods v3.07", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasCoffeeMod || hasCoffeeDS)
                {
                    moddedFoods.Add(new FoodDef("MealPack_CoffeeBrisket", (int)(216000 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_Bugsmasherspie", (int)(155520 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("ECDarkRoast", (int)(172800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("Mealpack_LittleOkayBurger", (int)(172800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("Mealpack_SabiroidSushi", (int)(129600 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_CoffeeCake", (int)(77760 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("CosmicCoffee", (int)(8640 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_wildlifealeme", (int)(21600 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("SereniTea", (int)(10800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_RoastedCoffee", (int)(4320 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("Coffee", (int)(4320 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("CoffeeBean", (int)(2160 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    MyLog.Default.WriteLineAndConsole("CryoRespawn: Engineered Coffee foods registered.");
                    hasPotato = true;// They merged the potato mod into the coffee mod, so if either coffee mod is present, register the potato foods as well.
                }
                if (hasPotato)
                {
                    moddedFoods.Add(new FoodDef("MealPack_IvansPottageDa", (int)(172800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_Shepherdspie",   (int)(140400 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_Bruiserabites",   (int)(38880 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("Potatoes",                 (int)(10800 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_Masheffectbombs", (int)(43200 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    moddedFoods.Add(new FoodDef("MealPack_vodkavix",         (int)(4320 * CryoRespawnConfigManager.Config.FoodMultiplier), new[] { "MyObjectBuilder_ConsumableItem" }));
                    MyLog.Default.WriteLineAndConsole("CryoRespawn: Potato VI-X foods registered.");
                }
                if (hasMuchfood)
                {
                    /*
                    moddedFoods.Add(new FoodDef("MF_HighItem", 192240, new[] { "MyObjectBuilder_ConsumableItem" })); // replace with real subtype
                    moddedFoods.Add(new FoodDef("MF_MidItem", 103680, new[] { "MyObjectBuilder_ConsumableItem" })); // replace with real subtype
                    moddedFoods.Add(new FoodDef("MF_LowItem", 38880, new[] { "MyObjectBuilder_ConsumableItem" })); // replace with real subtype
                    */
                    MyLog.Default.WriteLineAndConsole("CryoRespawn: CAT Much Foods foods registered.");
                }
            }
        }
        private bool _prevGrowthCond = true;
        private bool _prevStableCond = true;
        private List<FoodDef> moddedFoods = new List<FoodDef>();
        private void NotifyEventControllers()
        {
            var blocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
            Chamber.CubeGrid.GetBlocks(blocks);

            foreach (var slim in blocks)
            {
                var ecBlock = slim.FatBlock as Sandbox.ModAPI.IMyEventControllerBlock;
                if (ecBlock == null || !ecBlock.IsWorking) continue;

                // Refresh the readout
                ecBlock.Components.Get<CryoGrowthEventComponent>()?.NotifyValuesChanged();
                ecBlock.Components.Get<CryoStabilityEventComponent>()?.NotifyValuesChanged();

                var selected = ecBlock.SelectedEvent;

                if (selected is CryoGrowthEventComponent)
                {
                    float threshold = ecBlock.Threshold;
                    float normalizedGrowth = PercentGrown / 100f;
                    bool condNow = ecBlock.IsLowerOrEqualCondition
                        ? normalizedGrowth <= threshold
                        : normalizedGrowth >= threshold;

                    if (condNow != _prevGrowthCond)
                    {
                        _prevGrowthCond = condNow;
                        ecBlock.TriggerAction(condNow ? 0 : 1);
                    }
                }
                else if (selected is CryoStabilityEventComponent)
                {
                    bool condNow = Stable;
                    if (condNow != _prevStableCond)
                    {
                        _prevStableCond = condNow;
                        ecBlock.TriggerAction(condNow ? 0 : 1);
                    }
                }
            }
        }
        public void NotifyValuesChanged()
        {
            (Container?.Entity as Sandbox.ModAPI.IMyTerminalBlock)?.RefreshCustomInfo();
        }
        public void NewBuffsystem()
        {
            // Materials to use: Magnesium, Cobalt, Gold, Uranium, Platinum.
            // Buffs to apply: Max Health, Oxygen consumption, Radiation resistance, Temperature tolerance, Grinding speed, Welding speed, Movement speed, Hunger Rate, Energy consumption, Drill Speed,Feul consumption, Inventory volume, Impact durability, Auto heal limit
            // Magnesium: Max Health, Auto heal limit, Impact durability
            // Cobalt: Drill Speed, Oxygen consumption, Inventory volume
            // Gold: Energy consumption, Hunger Rate, Fuel consumption
            // Platinum: Welding speed, Grinding speed, Movement speed
            // Uranium: Radiation resistance, Temperature tolerance
            // Varibles admins can adjust: Will add to admin configuration later, but for now just set here.
            //====================================================
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled) return;
            float MagPull = CryoRespawnConfigManager.Config.MagPull;
            float CobPull = CryoRespawnConfigManager.Config.CobPull;
            float GoldPull = CryoRespawnConfigManager.Config.GoldPull;
            float PlatPull = CryoRespawnConfigManager.Config.PlatPull;
            float UraPull = CryoRespawnConfigManager.Config.UraPull;
            int MagAng = CryoRespawnConfigManager.Config.MagAng;
            int CobAng = CryoRespawnConfigManager.Config.CobAng;
            int GoldAng = CryoRespawnConfigManager.Config.GoldAng;
            int PlatAng = CryoRespawnConfigManager.Config.PlatAng;
            int UraAng = CryoRespawnConfigManager.Config.UraAng;
            //====================================================
            // Normalize angles to be within 0-359 degrees and prevent any issues with buff calculations.
            Angles(MagAng, CobAng, GoldAng, PlatAng, UraAng);
            int TimeBuffdelay = 1800;// Amount of ticks the clone can be unstable before losing all growth from buffing, 10 seconds.
            
            //====================================================
            if (Growthcycle <= Maxgrowthcycle)
            {
                AllowedStatbility = StartingStability + StepStability * Growthcycle;
                Growthcycle++;
            }
            if (FinalStability > AllowedStatbility)
            {
                Buffunstable++;
                if (Buffunstable >= TimeBuffdelay)
                {

                    Settings.Growth = 0;
                    Settings.Grown = false;
                    Grown = false;
                }
            }
            else
            {
                BuffInv();
                Xoffset = (int)(MagPull * MBuffEating * Math.Cos(MathHelper.ToRadians(MagAng))) + (int)(CobPull * CBuffEating * Math.Cos(MathHelper.ToRadians(CobAng))) + (int)(GoldPull * GBuffEating * Math.Cos(MathHelper.ToRadians(GoldAng))) + (int)(PlatPull * PBuffEating * Math.Cos(MathHelper.ToRadians(PlatAng))) + (int)(UraPull * UBuffEating * Math.Cos(MathHelper.ToRadians(UraAng)));
                Yoffset = (int)(MagPull * MBuffEating * Math.Sin(MathHelper.ToRadians(MagAng))) + (int)(CobPull * CBuffEating * Math.Sin(MathHelper.ToRadians(CobAng))) + (int)(GoldPull * GBuffEating * Math.Sin(MathHelper.ToRadians(GoldAng))) + (int)(PlatPull * PBuffEating * Math.Sin(MathHelper.ToRadians(PlatAng))) + (int)(UraPull * UBuffEating * Math.Sin(MathHelper.ToRadians(UraAng)));
                XStability = XStability + Xoffset;
                YStability = YStability + Yoffset;
                Buffunstable = 0;
                FinalStability = (int)Math.Sqrt(Math.Pow(XStability, 2) + Math.Pow(YStability, 2));
                MagBuff = ((float)(XStability / Math.Cos(MathHelper.ToRadians(MagAng)) + YStability / Math.Sin(MathHelper.ToRadians(MagAng)))) / AllowedStatbility;
                CobBuff = ((float)(XStability / Math.Cos(MathHelper.ToRadians(CobAng)) + YStability / Math.Sin(MathHelper.ToRadians(CobAng)))) / AllowedStatbility;
                GoldBuff = ((float)(XStability / Math.Cos(MathHelper.ToRadians(GoldAng)) + YStability / Math.Sin(MathHelper.ToRadians(GoldAng)))) / AllowedStatbility;
                PlatBuff = ((float)(XStability / Math.Cos(MathHelper.ToRadians(PlatAng)) + YStability / Math.Sin(MathHelper.ToRadians(PlatAng)))) / AllowedStatbility;
                UraBuff = ((float)(XStability / Math.Cos(MathHelper.ToRadians(UraAng)) + YStability / Math.Sin(MathHelper.ToRadians(UraAng)))) / AllowedStatbility;
                DisplayMag = (int)(MagBuff * 100);
                DisplayCob = (int)(CobBuff * 100);
                DisplayGold = (int)(GoldBuff * 100);
                DisplayPlat = (int)(PlatBuff * 100);
                DisplayUra = (int)(UraBuff * 100);
            }
        }
        public void Angles(int MagAng, int CobAng, int GoldAng, int PlatAng, int UraAng)
        {
            // Normalize the angle to be within 0-359 degrees
            if (MagAng >= 360)
            {
                // Calculate how many full rotations (360 degrees) are in the angle
                float Ang = MagAng / 360f;
                //Round the angle down to the nearest integer to get the number of full rotations
                int AngR = Convert.ToInt32(Math.Floor(Ang));
                // Subtract the full rotations to get the normalized angle
                MagAng = MagAng-(360*AngR);
            }
            if(CobAng >= 360)
            {
                float Ang = CobAng / 360f;
                int AngR = Convert.ToInt32(Math.Floor(Ang));
                CobAng = CobAng-(360*AngR);
            }
            if(GoldAng >= 360)
            {
                float Ang = GoldAng / 360f;
                int AngR = Convert.ToInt32(Math.Floor(Ang));
                GoldAng = GoldAng-(360*AngR);
            }
            if(PlatAng >= 360)
            {
                float Ang = PlatAng / 360f;
                int AngR = Convert.ToInt32(Math.Floor(Ang));
                PlatAng = PlatAng-(360*AngR);
            }
            if(UraAng >= 360)
            {
                float Ang = UraAng / 360f;
                int AngR = Convert.ToInt32(Math.Floor(Ang));
                UraAng = UraAng-(360*AngR);
            }
            if (MagAng < 0)
            {
                float Ang = MagAng / 360f;
                int AngR = Convert.ToInt32(Math.Ceiling(Ang));
                MagAng = MagAng+(360*AngR);
            }
            if(CobAng < 0)
            {
                float Ang = CobAng / 360f;
                int AngR = Convert.ToInt32(Math.Ceiling(Ang));
                CobAng = CobAng+(360*AngR);
            }
            if(GoldAng < 0)
            {
                float Ang = GoldAng / 360f;
                int AngR = Convert.ToInt32(Math.Ceiling(Ang));
                GoldAng = GoldAng+(360*AngR);
            }
            if(PlatAng < 0)
            {
                float Ang = PlatAng / 360f;
                int AngR = Convert.ToInt32(Math.Ceiling(Ang));
                PlatAng = PlatAng+(360*AngR);
            }
            if(UraAng < 0)
            {
                float Ang = UraAng / 360f;
                int AngR = Convert.ToInt32(Math.Ceiling(Ang));
                UraAng = UraAng+(360*AngR);
            }
            if (MagAng == 0 || CobAng == 0 || GoldAng == 0 || PlatAng == 0 || UraAng == 0)
            {
                MagAng ++;
                CobAng ++;
                GoldAng ++;
                PlatAng ++;
                UraAng ++;
            }
            if(MagAng == 90 || CobAng == 90 || GoldAng == 90 || PlatAng == 90 || UraAng == 90)
            {
                MagAng --;
                CobAng --;
                GoldAng --;
                PlatAng --;
                UraAng --;
            }
            if(MagAng == 180 || CobAng == 180 || GoldAng == 180 || PlatAng == 180 || UraAng == 180)
            {
                MagAng --;
                CobAng --;
                GoldAng --;
                PlatAng --;
                UraAng --;
            }
            if (MagAng == 270 || CobAng == 270 || GoldAng == 270 || PlatAng == 270 || UraAng == 270)
            {
                MagAng --;
                CobAng --;
                GoldAng --;
                PlatAng --;
                UraAng --;
            }
            return;
        }
        public float NormalizeBuff(float raw, float min, float max)
        {
            if (raw >= 0f)
                return raw / max;
            return raw / -min; // min is negative, so -min is a positive magnitude
        }
        public void BuffInv()
        {
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled) return;
            var inventory = Chamber.GetInventory() as VRage.Game.ModAPI.IMyInventory;
            if (inventory == null) return;
            // Check for materials, up to 10.
            var MagnesiumId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Magnesium");
            var CobaltId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Cobalt");
            var UraniumId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Uranium");
            var GoldId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Gold");
            var PlatinumId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Platinum");
            bool magnesiumcheck = inventory.ContainItems(0, MagnesiumId);
            bool cobaltcheck = inventory.ContainItems(0, CobaltId);
            bool platinumcheck = inventory.ContainItems(0, PlatinumId);
            bool uraniumcheck = inventory.ContainItems(0, UraniumId);
            bool goldcheck = inventory.ContainItems(0, GoldId);
            BuffEats = CryoRespawnConfigManager.Config.BuffIngotAmount;
            if (magnesiumcheck)
            {
                int eat;
                MyFixedPoint amount = inventory.GetItemAmount(MagnesiumId);
                if (amount != null)
                {
                    eat = (int)Math.Min(BuffEats, (double)amount);
                    inventory.RemoveItemsOfType(eat, MagnesiumId);
                    MBuffEating = eat;
                }
            }
            else
            {
                MBuffEating = 0;
            }
            TME = MBuffEating + TME;
            if (cobaltcheck)
            {
                int eat;
                MyFixedPoint amount = inventory.GetItemAmount(CobaltId);
                if (amount != null)
                {
                    eat = (int)Math.Min(BuffEats, (double)amount);
                    inventory.RemoveItemsOfType(eat, CobaltId);
                    CBuffEating = eat;
                }
            }
            else
            {
                CBuffEating = 0;
            }
            TCE = CBuffEating + TCE;
            if (platinumcheck)
            {
                int eat;
                MyFixedPoint amount = inventory.GetItemAmount(PlatinumId);
                if (amount != null)
                {
                    eat = (int)Math.Min(BuffEats, (double)amount);
                    inventory.RemoveItemsOfType(eat, PlatinumId);
                    PBuffEating = eat;
                }
            }
            else
            {
                PBuffEating = 0;
            }
            TPE = PBuffEating + TPE;
            if (uraniumcheck)
            {
                int eat;
                MyFixedPoint amount = inventory.GetItemAmount(UraniumId);
                if (amount != null)
                {
                    eat = (int)Math.Min(BuffEats, (double)amount);
                    inventory.RemoveItemsOfType(eat, UraniumId);
                    UBuffEating = eat;
                }
            }
            else
            {
                UBuffEating = 0;
            }
            TUE = UBuffEating + TUE;
            if (goldcheck)
            {
                int eat;
                MyFixedPoint amount = inventory.GetItemAmount(GoldId);
                if (amount != null)
                {
                    eat = (int)Math.Min(BuffEats, (double)amount);
                    inventory.RemoveItemsOfType(eat, GoldId);
                    GBuffEating = eat;
                }
            }
            else
            {
                GBuffEating = 0;
            }
            TGE = GBuffEating + TGE;
        }
        void ApplynewBuffs(IMyCharacter character)
        {
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled) return;
            var player = MyAPIGateway.Session.Player.Character;
            var stats = character.Components.Get<MyCharacterStatComponent>();
            if (player == null) return;
            if (stats != null)
            {
                //Set Buff level.
                // Bug: If buffed with magnesium, the max health valve is still at 100 when it should be  at 200.
                // Found another mod that changes the max health stat,  https://steamcommunity.com/sharedfiles/filedetails/?id=3573303431, 
                // Platinum: Welding speed, Grinding speed, Movement speed
                float magN = NormalizeBuff(MagBuff, MagMin, MagMax);
                float cobN = NormalizeBuff(CobBuff, CobMin, CobMax);
                float platN = NormalizeBuff(PlatBuff, PlatMin, PlatMax);
                float uraN = NormalizeBuff(UraBuff, UraMin, UraMax);
                float goldN = NormalizeBuff(GoldBuff, GoldMin, GoldMax);
                MyEntityStat moveStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("MovementSpeedBuff"), out moveStat) && moveStat != null)
                    //Range 7m/s to 17m/s
                    moveStat.Value = 12f + (5.0f * platN);
                MyEntityStat weldStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("WeldingSpeedBuff"), out weldStat) && weldStat != null)
                    //Range 7.5x to 12.5x of default
                    weldStat.Value = 10f + (2.5f * platN);
                MyEntityStat grindStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("GrindingSpeedBuff"), out grindStat) && grindStat != null)
                    //Range 7.5x to 12.5x of default
                    grindStat.Value = 10f + (2.5f * platN);
                //============================
                //Magnesium: Max Health, Auto heal limit, Impact durability
                MyEntityStat healthStat;
                MyEntityStat maxHealthStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("Health"), out healthStat) && healthStat != null)
                {
                    float newMax = 115f + 85f * magN;
                    float bonus = newMax - 100f; // delta above base
                    bool wasFull = healthStat.Value >= healthStat.MaxValue - 0.01f;
                    healthStat.SetMaxValue(newMax);
                    if (wasFull || healthStat.Value > healthStat.MaxValue)
                        healthStat.Value = healthStat.MaxValue;
                    if (stats.TryGetStat(MyStringHash.GetOrCompute("MaxHealthBuff"), out maxHealthStat) && maxHealthStat != null)
                        maxHealthStat.Value = bonus;
                }
                // Range 10% to 100%
                autoHealLimit = 0.55f + 0.45f * magN;
                //============================
                //Cobalt: Drill Speed, Oxygen consumption, Inventory volume
                MyEntityStat oxygenStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("OxygenConsumptionRateBuff"), out oxygenStat) && oxygenStat != null)
                {//Range 0.1x to 2x
                    oxygenStat.Value = 1.05f + 0.95f * cobN;
                }
                var recheckPlayers = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(recheckPlayers);
                var target = recheckPlayers.Find(p => p.IdentityId == owner);
                if (target?.Character == null || target.Character.IsDead) return;
                var inv = target.Character.GetInventory() as MyInventory;
                if (inv != null)
                {//Range 0.1 to 10
                    float defaultVolume = (float)inv.MaxVolume;
                    if (defaultVolume == 0)
                        inv.MaxVolume = (MyFixedPoint)0.4f;
                    else
                        inv.MaxVolume = (MyFixedPoint)(5.05f + 4.95f * cobN);
                }
                //Drill speed Range 0.1x to 10x
                MyHandDrillDefinition drillDef;
                if (MyDefinitionManager.Static.TryGetDefinition(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), "HandDrill"), out drillDef) && drillDef != null)
                {
                    drillDef.HarvestRatioMultiplier = 5.05f + 4.95f * cobN;
                }
                //============================
                //Gold: Energy consumption, Hunger Rate, Fuel consumption
                //Range 0.1x to 2x
                EnergyDrainMultipler = 1.05f + 0.95f * goldN;
                HungerDrainMultiplier = 1.05f - 0.95f * goldN;
                MyEntityStat hungerStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("HungerAmount"), out hungerStat) && hungerStat != null)
                {
                    const float FOOD_BASE_DECAY_RATE = 0.000556f;
                    float worldFoodRate = MyAPIGateway.Session.SessionSettings.FoodConsumptionRate;
                    if (worldFoodRate <= 0f) return;
                    float vanillaDrain = FOOD_BASE_DECAY_RATE * worldFoodRate;
                    float cancelFraction = 1.0f - HungerDrainMultiplier;
                    float regenPerSecond = vanillaDrain * cancelFraction;
                    var effectId = MyStringHash.GetOrCompute("CryoRespawn_HungerBuff");
                    hungerStat.RemoveEffect((int)effectId);
                    if (HungerDrainMultiplier < 1.0f)
                    {
                        hungerStat.AddEffect(0f, regenPerSecond, -1f, (float)effectId);
                    }
                }
                var jetpack = character.Components.Get<MyCharacterJetpackComponent>();
                if (jetpack != null)
                {
                    var converter = jetpack.FuelConverterDefinition;
                    converter.Efficiency = MathHelper.Clamp(0.05f + 0.95f * goldN, 0.1f, 10f);
                }
                //============================
                //Uranium: Radiation resistance, Temperature tolerance: handled in CryoRespawnMod.cs
                MyEntityStat radImmunityStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("RadiationImmunity"), out radImmunityStat) && radImmunityStat != null)
                    // UraBuff=1: full immunity (100), UraBuff=-1: no immunity (0), neutral: 50
                    radImmunityStat.Value = MathHelper.Clamp(50f + 50f * uraN, 0f, 100f);
            }
        }
        void Autohealing()
        {
            if (!CryoRespawnConfigManager.Config.BuffSystemEnabled) return;
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var player = players.Find(p => p.IdentityId == owner);
            if (player?.Character == null || player.Character.IsDead) return;
            var character = player.Character;
            var stats = character.Components.Get<MyCharacterStatComponent>();
            if (stats != null)
            {
                MyEntityStat healthStat;
                if (stats.TryGetStat(MyStringHash.GetOrCompute("Health"), out healthStat) && healthStat != null)
                {
                    if (healthStat.Value < healthStat.MaxValue * autoHealLimit)
                    {
                        healthStat.Value = Math.Min(healthStat.MaxValue * autoHealLimit, healthStat.Value + 1f);
                    }
                }
            }
        }
        void ApplyEnergyDrain(IMyCharacter character)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            if (character == null || character.IsDead) return;
            if (Math.Abs(EnergyDrainMultipler - 1.0f) < 0.001f) return;
            var sink = character.Components.Get<MyResourceSinkComponent>();
            if (sink == null) return;
            var electricityID = MyResourceDistributorComponent.ElectricityId;
            const float baselifesupport = 1E-05f;
            float newrequiered = baselifesupport * EnergyDrainMultipler;
            sink.SetRequiredInputByType(electricityID, newrequiered);
            sink.Update();
        }
    }
}