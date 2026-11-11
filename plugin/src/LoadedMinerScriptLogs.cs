using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyTimerBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock;

namespace EMMA3.Client
{
    // Diagnostic discovery never changes fleet membership or writes Miner blocks.
    // Grid snapshots are infrequent; terminal scans are spread across log updates.
    internal sealed class LoadedMinerScriptLogs
    {
        private readonly HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
        private readonly List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
        private readonly List<Sandbox.ModAPI.Ingame.IMyTerminalBlock> candidates = new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();
        private int cursor, nextScanFrame;
        private long carrierId, playerId;
        private string pair = "";

        public void Drain(IMyProgrammableBlock carrier, ShipNames names, ScriptLogCollector collector, int frame)
        {
            var owner = carrier as IMyTerminalBlock;
            var player = MyAPIGateway.Session?.Player;
            if (owner == null || owner.Closed || owner.MarkedForClose || owner.CubeGrid == null || player == null
                || !owner.HasPlayerAccess(player.IdentityId)) return;
            if (carrierId != owner.EntityId || pair != names.Pair || playerId != player.IdentityId)
            {
                carrierId = owner.EntityId; pair = names.Pair; playerId = player.IdentityId;
                grids.Clear(); cursor = nextScanFrame = 0;
            }
            if (cursor >= grids.Count)
            {
                if (frame < nextScanFrame) return;
                nextScanFrame = frame + 300;
                grids.Clear(); entities.Clear(); cursor = 0;
                MyAPIGateway.Entities?.GetEntities(entities, entity => entity is IMyCubeGrid && !entity.Closed && !entity.MarkedForClose);
                foreach (var entity in entities) grids.Add((IMyCubeGrid)entity);
                entities.Clear();
            }
            int budget = 4;
            while (cursor < grids.Count && budget-- > 0)
            {
                var grid = grids[cursor++];
                if (grid == null || grid.Closed || grid.MarkedForClose || grid == owner.CubeGrid) continue;
                try { DrainGrid(grid, names, collector, player.IdentityId); }
                catch (Exception) { /* Replication can remove a grid mid-scan. Retry on the next sweep. */ }
            }
        }

        private void DrainGrid(IMyCubeGrid grid, ShipNames names, ScriptLogCollector collector, long identity)
        {
            var terminal = MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(grid);
            if (terminal == null) return;
            candidates.Clear();
            string commanderName = names.Mi("Prog. Block (Commander)"), timerName = names.Mi("Script Log");
            terminal.GetBlocksOfType(candidates, block => block.CubeGrid == grid
                && (block is IMyProgrammableBlock && block.CustomName == commanderName || block is IMyTimerBlock && block.CustomName == timerName));
            IMyProgrammableBlock commander = null;
            IMyTimerBlock timer = null;
            foreach (var block in candidates)
            {
                if (block is IMyProgrammableBlock)
                { if (commander != null) return; commander = (IMyProgrammableBlock)block; }
                else
                { if (timer != null) return; timer = (IMyTimerBlock)block; }
            }
            var commanderTerminal = commander as IMyTerminalBlock;
            var timerTerminal = timer as IMyTerminalBlock;
            if (commanderTerminal == null || timerTerminal == null || commanderTerminal.Closed || commanderTerminal.MarkedForClose
                || timerTerminal.Closed || timerTerminal.MarkedForClose || !commanderTerminal.HasPlayerAccess(identity)
                || !timerTerminal.HasPlayerAccess(identity)) return;
            ShipNames actual;
            try { actual = ShipNames.Read(commander.CustomData, false); }
            catch (ArgumentException) { return; }
            if (actual.Pair != names.Pair) return;
            collector.DrainTimer(timer, "Miner [grid " + grid.EntityId + "]");
        }
    }
}
