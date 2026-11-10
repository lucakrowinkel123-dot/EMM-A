using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyTimerBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private string CarrierInfoTimerName { get { return blocks.Names.Ca("Plugin Info"); } }
        private string MinerInfoTimerName { get { return blocks.Names.Mi("Plugin Info"); } }
        private IMyTimerBlock lastCarrierInfoTimer;
        private IMyTimerBlock lastMinerInfoTimer;
        private long minerInfoLookupOwner;
        private readonly List<IMyTerminalBlock> pluginInfoLookupBlocks = new List<IMyTerminalBlock>();

        private void ObservePluginInfoTimers()
        {
            if (blocks.CarrierInfoTimer != null)
                lastCarrierInfoTimer = blocks.CarrierInfoTimer;
            if (blocks.MinerInfoTimer != null)
            {
                lastMinerInfoTimer = blocks.MinerInfoTimer;
                minerInfoLookupOwner = lastMinerPb?.EntityId ?? 0;
                return;
            }

            IMyProgrammableBlock miner = blocks.MinerPb ?? lastMinerPb;
            if (miner == null || miner.EntityId == minerInfoLookupOwner)
                return;
            minerInfoLookupOwner = miner.EntityId;
            lastMinerInfoTimer = FindExactTimerOnGrid(miner, MinerInfoTimerName);
        }

        private IMyTimerBlock FindExactTimerOnGrid(IMyProgrammableBlock owner, string exactName)
        {
            IMyTerminalBlock ownerTerminal = owner as IMyTerminalBlock;
            if (ownerTerminal?.CubeGrid == null)
                return null;
            var terminal = MyAPIGateway.TerminalActionsHelper?.GetTerminalSystemForGrid(ownerTerminal.CubeGrid);
            if (terminal == null)
                return null;
            pluginInfoLookupBlocks.Clear();
            terminal.GetBlocks(pluginInfoLookupBlocks);
            long gridId = ownerTerminal.CubeGrid.EntityId;
            for (int i = 0; i < pluginInfoLookupBlocks.Count; i++)
            {
                IMyTerminalBlock block = pluginInfoLookupBlocks[i];
                IMyTimerBlock timer = block as IMyTimerBlock;
                if (timer != null && block.CubeGrid?.EntityId == gridId
                    && string.Equals(block.CustomName, exactName, StringComparison.Ordinal))
                    return timer;
            }
            return null;
        }

        private string GetMinerPluginInfo(IMyProgrammableBlock miner)
        {
            if (TimerBelongsTo(lastMinerInfoTimer, miner, MinerInfoTimerName))
                return lastMinerInfoTimer.CustomData ?? "";
            return miner?.DetailedInfo ?? "";
        }

        private string GetCarrierPluginInfo()
        {
            IMyProgrammableBlock carrier = blocks.CarrierPb ?? lastCarrierPb;
            if (TimerBelongsTo(lastCarrierInfoTimer, carrier, CarrierInfoTimerName))
                return lastCarrierInfoTimer.CustomData ?? "";
            return carrier?.DetailedInfo ?? "";
        }

        private static bool TimerBelongsTo(IMyTimerBlock timer, IMyProgrammableBlock owner, string exactName)
        {
            IMyTerminalBlock terminal = timer as IMyTerminalBlock;
            return terminal != null && !terminal.Closed && owner?.CubeGrid != null && terminal.CubeGrid != null
                && terminal.CubeGrid.EntityId == owner.CubeGrid.EntityId
                && string.Equals(terminal.CustomName, exactName, StringComparison.Ordinal);
        }
    }
}
