using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private const string LcdMarker = "EMMA3_LCD|1|";
        private readonly MarkerFieldCache lcdFieldsCache = new MarkerFieldCache(LcdMarker);
        private bool pluginLcdOutput = true;
        private int pluginLcdHeartbeat;
        private int nextPluginLcdHeartbeatFrame;
        private int lastLcdSequence = -1;
        private long configuredMainLcd;
        private long configuredReportLcd;
        private string lastMainLcdText = "";
        private string lastReportLcdText = "";

        private void ApplyPluginLcdSettings(BridgeDocument bridge)
        {
            bool enabled = bridge.GetBool("pluginLcdOutput", true);
            if (enabled == pluginLcdOutput)
            {
                if (enabled && frame >= nextPluginLcdHeartbeatFrame)
                {
                    pluginLcdHeartbeat++;
                    nextPluginLcdHeartbeatFrame = frame + 300;
                }
                return;
            }

            pluginLcdOutput = enabled;
            lastLcdSequence = -1;
            if (enabled)
            {
                pluginLcdHeartbeat++;
                nextPluginLcdHeartbeatFrame = frame + 300;
            }
            else
                nextPluginLcdHeartbeatFrame = 0;
        }

        private void UpdateCarrierLcdOutput()
        {
            if (!pluginLcdOutput || !blocks.IsCarrierPilot || blocks.CarrierPb == null || blocks.CarrierLcdMain == null)
                return;

            string[] fields;
            if (!lcdFieldsCache.TryRead(blocks.CarrierPb.EntityId, GetCarrierPluginInfo(), out fields))
                return;
            int sequence;
            if (fields.Length != 5 || !int.TryParse(fields[2], out sequence) || sequence == lastLcdSequence)
                return;

            string main = DecodeLcdText(fields[3]);
            string report = DecodeLcdText(fields[4]);
            ConfigureLcd(blocks.CarrierLcdMain, ref configuredMainLcd);
            if (!string.Equals(main, lastMainLcdText, StringComparison.Ordinal))
            {
                blocks.CarrierLcdMain.WriteText(main, false);
                lastMainLcdText = main;
            }

            if (blocks.CarrierLcdReport != null)
            {
                ConfigureLcd(blocks.CarrierLcdReport, ref configuredReportLcd);
                if (!string.Equals(report, lastReportLcdText, StringComparison.Ordinal))
                {
                    blocks.CarrierLcdReport.WriteText(report, false);
                    lastReportLcdText = report;
                }
            }

            lastLcdSequence = sequence;
        }

        private static void ConfigureLcd(IMyTextPanel panel, ref long configuredId)
        {
            if (panel == null || panel.EntityId == configuredId)
                return;
            panel.ContentType = ContentType.TEXT_AND_IMAGE;
            panel.Font = "Debug";
            panel.FontSize = 1.225f;
            panel.TextPadding = 0f;
            panel.Alignment = TextAlignment.LEFT;
            configuredId = panel.EntityId;
        }

        private static string DecodeLcdText(string value)
        {
            return (value ?? "").Replace("%0A", "\n").Replace("%0D", "\r").Replace("%7C", "|").Replace("%25", "%");
        }
    }
}
