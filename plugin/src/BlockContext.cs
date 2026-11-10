using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRageMath;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyShipController = Sandbox.ModAPI.Ingame.IMyShipController;
using IMyRemoteControl = Sandbox.ModAPI.Ingame.IMyRemoteControl;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyTimerBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyShipDrill = Sandbox.ModAPI.Ingame.IMyShipDrill;
using IMyTextPanel = Sandbox.ModAPI.Ingame.IMyTextPanel;
using MyEntities = Sandbox.Game.Entities.MyEntities;

namespace EMMA3.Client
{
    internal sealed class BlockContext
    {
        public ShipNames Names = new ShipNames();
        private readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
        private readonly List<IMyTerminalBlock> detachedMinerBlocks = new List<IMyTerminalBlock>();
        public IMyCubeGrid ControlledGrid
        {
            get; private set;
        }
        public IMyShipController OccupiedController
        {
            get; private set;
        }
        public IMyShipController CarrierRemote
        {
            get; private set;
        }
        public IMyRemoteControl MinerRemote
        {
            get; private set;
        }
        public IMyShipConnector CarrierConnector
        {
            get; private set;
        }
        public IMyShipConnector MinerConnector
        {
            get; private set;
        }
        public IMyTimerBlock BridgeTimer
        {
            get; private set;
        }
        public IMyTimerBlock OreTimer
        {
            get; private set;
        }
        public IMyTimerBlock ResourcesTimer
        {
            get; private set;
        }
        public IMyTimerBlock CompletedAsteroidsTimer
        {
            get; private set;
        }
        public IMyTimerBlock MinerPathTimer
        {
            get; private set;
        }
        public IMyTimerBlock MinerStateTimer
        {
            get; private set;
        }
        public IMyTimerBlock CarrierInfoTimer
        {
            get; private set;
        }
        public IMyTimerBlock MinerInfoTimer
        {
            get; private set;
        }
        public IMyProgrammableBlock CarrierPb
        {
            get; private set;
        }
        public IMyProgrammableBlock MinerPb
        {
            get; private set;
        }
        public IMyProgrammableBlock CarrierPilotPb { get; private set; }
        public IMyProgrammableBlock MinerPilotPb { get; private set; }
        public IMyTextPanel CarrierLcdMain
        {
            get; private set;
        }
        public IMyTextPanel CarrierLcdReport
        {
            get; private set;
        }
        public IMyTextPanel CarrierLcdRuntime { get; private set; }
        private IMyTerminalBlock carrierHangar;
        public bool IsMinerHangared
        {
            get
            {
                if (carrierHangar == null || carrierHangar.Closed || carrierHangar.MarkedForClose) return false;
                var ini = new VRage.Game.ModAPI.Ingame.Utilities.MyIni();
                return ini.TryParse(carrierHangar.CustomData ?? "")
                    && ini.Get("HangarManagerInventory", "Status").ToString("").Equals("Ready", StringComparison.OrdinalIgnoreCase)
                    && ini.Get("HangarManagerInventory", "Count").ToInt32(-1) > 0;
            }
        }

        public readonly List<IMyShipDrill> MinerDrills = new List<IMyShipDrill>();
        public string Problem
        {
            get; private set;
        }
        public bool IsCarrierPilot
        {
            get; private set;
        }

        public bool Refresh()
        {
            Clear();
            var controlled = MyAPIGateway.Session?.ControlledObject as IMyControllableEntity;
            var occupiedBlock = controlled?.Entity as IMyCubeBlock;
            OccupiedController = controlled?.Entity as IMyShipController;
            ControlledGrid = occupiedBlock?.CubeGrid;
            if (ControlledGrid == null || OccupiedController == null)
            {
                Problem = "sit in a Carrier cockpit";
                return false;
            }

            var terminal = MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(ControlledGrid);
            if (terminal == null)
            {
                Problem = "no terminal system for occupied grid";
                return false;
            }

            blocks.Clear();
            terminal.GetBlocks(blocks);
            // Bootstrap from the occupied grid, before resolving any prefixed hardware.
            foreach (var block in blocks)
            {
                var commander = block as IMyProgrammableBlock;
                if (commander == null || block.CubeGrid != ControlledGrid
                    || !block.CustomName.EndsWith(" Prog. Block (Commander)", StringComparison.Ordinal)) continue;
                ShipNames candidate;
                try { candidate = ShipNames.Read(block.CustomData, true); }
                catch (ArgumentException) { continue; }
                if (block.CustomName != candidate.Ca("Prog. Block (Commander)")) continue;
                if (CarrierPb != null) { Problem = "duplicate Carrier Commanders"; return false; }
                CarrierPb = commander; Names = candidate;
            }
            if (CarrierPb == null) { Problem = "missing Carrier Commander or invalid naming settings"; return false; }
            MinerPb = Find<IMyProgrammableBlock>(Names.Mi("Prog. Block (Commander)"));
            if (MinerPb != null && !MatchesMiner(MinerPb)) { Problem = "Carrier/Miner naming mismatch"; return false; }

            CarrierRemote = Find<IMyShipController>(Names.Ca("Remote Control"), ControlledGrid);
            MinerRemote = MinerPb == null ? null : Find<IMyRemoteControl>(Names.Mi("Remote Control"), MinerPb.CubeGrid);
            CarrierConnector = Find<IMyShipConnector>(Names.Ca("Connector (Hangar)"), ControlledGrid);
            MinerConnector = MinerPb == null ? null : Find<IMyShipConnector>(Names.Mi("Connector"), MinerPb.CubeGrid);
            BridgeTimer = Find<IMyTimerBlock>(Names.Ca("Plugin Bridge"), ControlledGrid);
            OreTimer = Find<IMyTimerBlock>(Names.Ca("Ores"), ControlledGrid);
            ResourcesTimer = Find<IMyTimerBlock>(Names.Ca("Resources"), ControlledGrid);
            CompletedAsteroidsTimer = Find<IMyTimerBlock>(Names.Ca("Asteroid IDs"), ControlledGrid);
            MinerPathTimer = MinerPb == null ? null : Find<IMyTimerBlock>(Names.Mi("Path"), MinerPb.CubeGrid);
            MinerStateTimer = MinerPb == null ? null : Find<IMyTimerBlock>(Names.Mi("INI Core"), MinerPb.CubeGrid);
            CarrierInfoTimer = Find<IMyTimerBlock>(Names.Ca("Plugin Info"), ControlledGrid);
            MinerInfoTimer = MinerPb == null ? null : Find<IMyTimerBlock>(Names.Mi("Plugin Info"), MinerPb.CubeGrid);
            CarrierPilotPb = FindModulePilot(blocks,CarrierPb,Names.Ca("Prog. Block (Pilot)"));
            MinerPilotPb = FindModulePilot(blocks,MinerPb,Names.Mi("Prog. Block (Pilot)"));
            CarrierLcdMain = Find<IMyTextPanel>(Names.Ca("LCD Main"), ControlledGrid);
            CarrierLcdReport = Find<IMyTextPanel>(Names.Ca("LCD Report"), ControlledGrid);
            CarrierLcdRuntime = Find<IMyTextPanel>(Names.Ca("LCD Runtime"), ControlledGrid);
            carrierHangar = Find<IMyTerminalBlock>(Names.Ca("Hangar - A"), ControlledGrid);
            if (MinerRemote != null && MinerRemote.CubeGrid != null)
                FindBlocksOnGrid(MinerRemote.CubeGrid.EntityId, MinerDrills);
            IsCarrierPilot = CarrierRemote != null && CarrierRemote.CubeGrid != null && CarrierRemote.CubeGrid.EntityId == ControlledGrid.EntityId;
            if (!IsCarrierPilot)
                Problem = "planning requires the Carrier cockpit";
            else if (BridgeTimer == null)
                Problem = "missing Plugin Bridge timer";
            else if (OreTimer == null)
                Problem = "missing Ores timer";
            else if (CompletedAsteroidsTimer == null)
                Problem = "missing Asteroid IDs timer";
            else if (CarrierPb == null)
                Problem = "missing Carrier programmable block";
            else
                Problem = "";
            return Problem.Length == 0;
        }

        public string DockedMinerProblem()
        {
            if (MinerPathTimer == null)
                return "missing Miner Path timer (Miner must be docked)";
            if (MinerRemote == null)
                return "Dock Miner: missing Miner Remote Control";
            if (MinerPb == null)
                return "Dock Miner: missing Miner PB";
            return "";
        }

        public string ProjectedMinerPoseProblem()
        {
            if (CarrierConnector == null)
                return "missing Carrier connector for Miner pose";
            if (MinerConnector == null)
                return "missing Miner connector for dock pose";
            return "";
        }

        public bool RestoreDetachedMiner(IMyProgrammableBlock rememberedMinerPb)
        {
            var minerTerminal = rememberedMinerPb as IMyTerminalBlock;
            if (minerTerminal == null || minerTerminal.Closed || minerTerminal.CubeGrid == null
                || !MatchesMiner(rememberedMinerPb))
                return false;
            var terminal = MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(minerTerminal.CubeGrid);
            if (terminal == null)
                return false;
            detachedMinerBlocks.Clear();
            terminal.GetBlocks(detachedMinerBlocks);
            MinerPb = rememberedMinerPb;
            MinerPilotPb = FindModulePilot(detachedMinerBlocks,MinerPb,Names.Mi("Prog. Block (Pilot)"));
            MinerRemote = FindIn<IMyRemoteControl>(detachedMinerBlocks, Names.Mi("Remote Control"), minerTerminal.CubeGrid);
            MinerConnector = FindIn<IMyShipConnector>(detachedMinerBlocks, Names.Mi("Connector"), minerTerminal.CubeGrid);
            MinerPathTimer = FindIn<IMyTimerBlock>(detachedMinerBlocks, Names.Mi("Path"), minerTerminal.CubeGrid);
            MinerStateTimer = FindIn<IMyTimerBlock>(detachedMinerBlocks, Names.Mi("INI Core"), minerTerminal.CubeGrid);
            MinerInfoTimer = FindIn<IMyTimerBlock>(detachedMinerBlocks, Names.Mi("Plugin Info"), minerTerminal.CubeGrid);
            MinerDrills.Clear();
            if (MinerRemote != null && MinerRemote.CubeGrid != null)
            {
                long gridId = MinerRemote.CubeGrid.EntityId;
                for (int i = 0; i < detachedMinerBlocks.Count; i++)
                {
                    var drill = detachedMinerBlocks[i] as IMyShipDrill;
                    if (drill != null && detachedMinerBlocks[i].CubeGrid != null && detachedMinerBlocks[i].CubeGrid.EntityId == gridId)
                        MinerDrills.Add(drill);
                }
            }
            return MinerRemote != null && MinerPathTimer != null;
        }

        public IMyProgrammableBlock FindDetachedMinerNearCarrier()
        {
            if (CarrierRemote == null || CarrierRemote.CubeGrid == null)
                return null;
            var player = MyAPIGateway.Session?.Player;
            if (player == null)
                return null;

            Vector3D carrierPosition = CarrierRemote.GetPosition();
            BoundingSphereD sphere = new BoundingSphereD(carrierPosition, 500.0);
            List<MyEntity> entities = MyEntities.GetEntitiesInSphere(ref sphere);
            IMyProgrammableBlock nearest = null;
            try
            {
                if (entities == null)
                    return null;
                for (int i = 0; i < entities.Count; i++)
                {
                    IMyCubeGrid grid = entities[i] as IMyCubeGrid;
                    if (grid == null || grid.EntityId == CarrierRemote.CubeGrid.EntityId)
                        continue;
                    var terminal = MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(grid);
                    if (terminal == null)
                        continue;
                    detachedMinerBlocks.Clear();
                    terminal.GetBlocks(detachedMinerBlocks);
                    IMyProgrammableBlock candidate = FindIn<IMyProgrammableBlock>(detachedMinerBlocks, Names.Mi("Prog. Block (Commander)"), grid);
                    var candidateTerminal = candidate as IMyTerminalBlock;
                    if (candidateTerminal == null || !MatchesMiner(candidate) || candidateTerminal.Closed || !candidateTerminal.HasPlayerAccess(player.IdentityId)
                        || FindIn<IMyRemoteControl>(detachedMinerBlocks, Names.Mi("Remote Control"), grid) == null
                        || FindIn<IMyTimerBlock>(detachedMinerBlocks, Names.Mi("Path"), grid) == null
                        || FindIn<IMyShipConnector>(detachedMinerBlocks, Names.Mi("Connector"), grid) == null)
                        continue;
                    if (!AcceptUniqueMiner(ref nearest, candidate)) return null;
                }
            }
            finally
            {
                entities?.Clear();
                detachedMinerBlocks.Clear();
            }
            return nearest;
        }

        public void GetCarrierBlocks(List<IMyTerminalBlock> result)
        {
            result.Clear();
            long carrierGridId = CarrierRemote?.CubeGrid?.EntityId ?? 0;
            if (carrierGridId == 0)
                return;
            for (int i = 0; i < blocks.Count; i++)
            {
                IMyTerminalBlock block = blocks[i];
                if (block?.CubeGrid != null && block.CubeGrid.EntityId == carrierGridId)
                    result.Add(block);
            }
        }

        private T Find<T>(string exactName, VRage.Game.ModAPI.Ingame.IMyCubeGrid owner = null)
            where T : class
        {
            T found = null;
            for (int i = 0; i < blocks.Count; i++)
            {
                T value = blocks[i] as T;
                if (value == null || owner != null && blocks[i].CubeGrid != owner || blocks[i].CustomName != exactName) continue;
                if (found != null) return null;
                found = value;
            }
            return found;
        }

        private static bool AcceptUniqueMiner(ref IMyProgrammableBlock selected, IMyProgrammableBlock candidate)
        {
            if (selected != null && selected.EntityId != candidate.EntityId) return false;
            selected = candidate;
            return true;
        }

        private static T FindIn<T>(List<IMyTerminalBlock> source, string exactName, VRage.Game.ModAPI.Ingame.IMyCubeGrid owner)
            where T : class
        {
            T found = null;
            for (int i = 0; i < source.Count; i++)
            {
                T value = source[i] as T;
                if (value == null || source[i].CubeGrid != owner || source[i].CustomName != exactName) continue;
                if (found != null) return null;
                found = value;
            }
            return found;
        }

        private static IMyProgrammableBlock FindModulePilot(List<IMyTerminalBlock> source, IMyProgrammableBlock commander, string name)
        {
            if(commander == null || commander.CubeGrid == null)return null;
            IMyProgrammableBlock found=null;
            foreach(var block in source) {
                var pb=block as IMyProgrammableBlock;
                if(pb==null || pb.CubeGrid==null || pb.CubeGrid.EntityId!=commander.CubeGrid.EntityId || pb.CustomName!=name)continue;
                if(found!=null)return null;
                found=pb;
            }
            return found;
        }

        public bool MatchesMiner(IMyProgrammableBlock miner)
        {
            if (miner == null || miner.CustomName != Names.Mi("Prog. Block (Commander)")) return false;
            try { return ShipNames.Read(miner.CustomData, false).Pair == Names.Pair; }
            catch (ArgumentException) { return false; }
        }

        private void Clear()
        {
            ControlledGrid = null;
            OccupiedController = null;
            CarrierRemote = null;
            MinerRemote = null;
            CarrierConnector = null;
            MinerConnector = null;
            BridgeTimer = null;
            OreTimer = null;
            ResourcesTimer = null;
            CompletedAsteroidsTimer = null;
            MinerPathTimer = null;
            MinerStateTimer = null;
            CarrierInfoTimer = null;
            MinerInfoTimer = null;
            CarrierPb = null;
            MinerPb = null;
            CarrierPilotPb = null; MinerPilotPb = null;
            CarrierLcdMain = null;
            CarrierLcdReport = null;
            CarrierLcdRuntime = null;
            carrierHangar = null;
            IsCarrierPilot = false;
            Problem = "";
            MinerDrills.Clear();
        }

        private void FindBlocksOnGrid<T>(long gridId, List<T> result)
            where T : class
        {
            result.Clear();
            for (int i = 0; i < blocks.Count; i++)
            {
                T value = blocks[i] as T;
                if (value == null || blocks[i].CubeGrid == null || blocks[i].CubeGrid.EntityId != gridId)
                    continue;
                result.Add(value);
            }
        }
    }
}
