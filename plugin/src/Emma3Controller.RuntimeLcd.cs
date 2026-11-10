using System;
using System.Diagnostics;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyTextPanel = Sandbox.ModAPI.Ingame.IMyTextPanel;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private readonly RuntimeReadout runtimeReadout = new RuntimeReadout();
        private long lastRuntimeSecond = -1;
        private IMyTextPanel configuredRuntimePanel;
        private string lastRuntimeText;
        private int nextRuntimeErrorFrame;

        private void UpdateRuntimeLcd()
        {
            long second = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (second == lastRuntimeSecond) return;
            lastRuntimeSecond = second;
            double simulationSecond = EmmaTiming.GameSimulationTime.Ticks / 60;
            try
            {
                IMyTextPanel panel = blocks.CarrierLcdRuntime;
                if (!pluginLcdOutput || !blocks.IsCarrierPilot || panel == null || !panel.IsWorking)
                {
                    runtimeReadout.Clear(); configuredRuntimePanel = null; lastRuntimeText = null;
                    return;
                }
                runtimeReadout.Bind(0, LiveRuntimeId(blocks.CarrierPb));
                runtimeReadout.Bind(1, LiveRuntimeId(blocks.CarrierPilotPb));
                runtimeReadout.Bind(2, LiveRuntimeId(blocks.MinerPb));
                runtimeReadout.Bind(3, LiveRuntimeId(blocks.MinerPilotPb));
                // DetailedInfo is subscription-dependent; only timer CustomData is a reliable unattended server transport.
                if (TimerBelongsTo(lastCarrierInfoTimer, blocks.CarrierPb, CarrierInfoTimerName))
                    runtimeReadout.Observe(lastCarrierInfoTimer.CustomData, simulationSecond);
                if (TimerBelongsTo(lastMinerInfoTimer, blocks.MinerPb, MinerInfoTimerName))
                    runtimeReadout.Observe(lastMinerInfoTimer.CustomData, simulationSecond);
                string miner = blocks.Names.Miner == "EMM-A Mi" ? "Miner A" : blocks.Names.Miner;
                bool minerHangared = LiveRuntimeId(blocks.MinerPb) == 0 && LiveRuntimeId(blocks.MinerPilotPb) == 0
                    && blocks.IsMinerHangared;
                string text = runtimeReadout.Render(blocks.Names.Carrier, miner, simulationSecond,
                    label => panel.MeasureStringInPixels(new System.Text.StringBuilder(label), "Debug", 1.225f).X, minerHangared);
                if (!ReferenceEquals(configuredRuntimePanel, panel))
                {
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;
                    panel.Font = "Debug"; panel.FontSize = 1.225f; panel.TextPadding = 0f;
                    panel.Alignment = TextAlignment.LEFT;
                    panel.FontColor = Color.White; panel.BackgroundColor = Color.Black;
                    configuredRuntimePanel = panel; lastRuntimeText = null;
                }
                if (!string.Equals(text, lastRuntimeText, StringComparison.Ordinal))
                    if (panel.WriteText(text, false)) lastRuntimeText = text;
            }
            catch (Exception error)
            {
                // Display faults must not abort flight or mining.
                configuredRuntimePanel = null; lastRuntimeText = null;
                if (frame >= nextRuntimeErrorFrame)
                {
                    nextRuntimeErrorFrame = frame + 600;
                    Emma3Log.Warn("Runtime LCD update failed: " + error.GetBaseException().Message);
                }
            }
        }
        private static long LiveRuntimeId(IMyProgrammableBlock pb)
        {
            var block = pb as IMyTerminalBlock;
            return block == null || block.Closed || block.MarkedForClose ? 0 : pb.EntityId;
        }
    }
}
