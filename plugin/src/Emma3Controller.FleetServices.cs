using System;
using System.Collections.Generic;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private readonly List<IMyProgrammableBlock> serviceMiners = new List<IMyProgrammableBlock>();
        private int nextServiceRosterFrame, serviceMinerCursor, hardwareMinerCursor, remoteMinerCursor;
        private readonly Dictionary<long, long> powerRequestHistory = new Dictionary<long, long>();
        private readonly Dictionary<long, long> unloadRequestHistory = new Dictionary<long, long>();
        private readonly Dictionary<long, long> dockRequestHistory = new Dictionary<long, long>();
        private readonly Dictionary<long, FeatureReceipt> featureReceipts = new Dictionary<long, FeatureReceipt>();
        private readonly Dictionary<long, int> remoteTelemetryFrames = new Dictionary<long, int>();
        private long remotePoseMiner;
        private sealed class FeatureReceipt { public long Session; public string Value; public int NextFrame; }
        private readonly List<Sandbox.ModAPI.Ingame.IMyTerminalBlock> scriptLogTimers = new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();
        private readonly LoadedMinerScriptLogs loadedMinerScriptLogs = new LoadedMinerScriptLogs();
        private void DrainGridScriptLog(IMyProgrammableBlock owner, string name, string label)
        {
            var grid = (owner as Sandbox.ModAPI.IMyTerminalBlock)?.CubeGrid;
            if (grid == null || owner.Closed) return;
            var terminal = Sandbox.ModAPI.MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(grid);
            if (terminal == null) return;
            scriptLogTimers.Clear();
            terminal.GetBlocksOfType(scriptLogTimers, block => block is SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock
                && block.CubeGrid == owner.CubeGrid && block.CustomName == name);
            if (scriptLogTimers.Count == 1) scriptLogs.DrainTimer(scriptLogTimers[0], label);
        }

        private void DrainFleetLogs()
        {
            try
            {
                DrainGridScriptLog(blocks.CarrierPb ?? lastCarrierPb, blocks.Names.Ca("Script Log"), "Carrier");
                DrainMinerScriptLogs();
            }
            catch (Exception exception)
            {
                Emma3Log.Error("Script Log timer collection failed: " + exception.GetBaseException().Message);
            }
            // Discovery remains available even if an old cached endpoint failed above.
            try
            {
                loadedMinerScriptLogs.Drain(blocks.CarrierPb ?? lastCarrierPb, blocks.Names, scriptLogs, frame);
            }
            catch (Exception exception)
            {
                Emma3Log.Error("Loaded Miner Script Log discovery failed: " + exception.GetBaseException().Message);
            }
        }

        private void DrainMinerScriptLogs()
        {
            // The old envelopes can be consumed after a live upgrade. New logs only use timers.
            scriptLogs.DrainMember(blocks.CarrierPb??lastCarrierPb,"Carrier",true);
            scriptLogs.DrainMember(blocks.CarrierPilotPb??lastCarrierPilotPb,"Carrier",true);
            BridgeDocument fleet=BridgeDocument.Parse(blocks.BridgeTimer?.CustomData);
            int count=Math.Min(3,Math.Max(0,fleet.GetInt("fleetCount")));
            if(count==0)
            {
                DrainGridScriptLog(blocks.MinerPb ?? lastMinerPb, blocks.Names.Mi("Script Log"), "Miner");
                scriptLogs.DrainMember(blocks.MinerPb??lastMinerPb,"Miner",true);
                scriptLogs.DrainMember(blocks.MinerPilotPb??lastMinerPilotPb,"Miner",true);
                return;
            }
            for(int i=0;i<count;i++)for(int role=0;role<2;role++)
            {
                long id;VRage.ModAPI.IMyEntity entity;
                if(long.TryParse(fleet.Get((role==0?"fleetCommander":"fleetPilot")+i),out id)&&Sandbox.ModAPI.MyAPIGateway.Entities.TryGetEntityById(id,out entity)&&!entity.Closed)
                {
                    var owner = entity as IMyProgrammableBlock;
                    string label = fleet.Get("fleetLabel"+i,"Miner "+(char)('A'+i));
                    if (role == 0) DrainGridScriptLog(owner, blocks.Names.Mi("Script Log"), label);
                    scriptLogs.DrainMember(owner,label,true);
                }
            }
        }
        private IMyProgrammableBlock SelectServiceMiner(ref int cursor)
        {
            if (frame >= nextServiceRosterFrame)
            {
                nextServiceRosterFrame = frame + 60; serviceMiners.Clear();
                BridgeDocument fleet = BridgeDocument.Parse(blocks.BridgeTimer?.CustomData);
                if (fleet.GetInt("fleetCount") > 0) serviceMiners.AddRange(blocks.FleetMiners(fleet));
                else if ((blocks.MinerPb ?? lastMinerPb) != null) serviceMiners.Add(blocks.MinerPb ?? lastMinerPb);
                PruneServiceHistory(powerRequestHistory); PruneServiceHistory(unloadRequestHistory); PruneServiceHistory(dockRequestHistory); PruneServiceHistory(featureReceipts);
                PruneServiceHistory(remoteTelemetryFrames);
                PruneServiceHistory(minerInfoTimers); PruneServiceHistory(minerInfoRetryFrames);
            }
            if (serviceMiners.Count == 0) return null;
            if (cursor >= serviceMiners.Count) cursor = 0;
            return serviceMiners[cursor++];
        }
        private void PruneServiceHistory<T>(Dictionary<long, T> history)
        {
            var remove = new List<long>();
            foreach (long id in history.Keys)
            {
                bool current = false; foreach (var miner in serviceMiners) if (miner.EntityId == id) { current = true; break; }
                if (!current) remove.Add(id);
            }
            foreach (long id in remove) history.Remove(id);
        }
        private static bool RequestSeen(Dictionary<long, long> history, long miner, long request)
        { long previous; return history.TryGetValue(miner, out previous) && request <= previous; }
    }
}
