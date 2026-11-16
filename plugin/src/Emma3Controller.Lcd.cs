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
        private IMyTextPanel configuredMainLcd;
        private IMyTextPanel configuredReportLcd;
        private string lastMainLcdField;
        private string lastReportLcdField;

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
            if (!pluginLcdOutput || !blocks.IsCarrierPilot || blocks.CarrierPb == null)
            {
                configuredMainLcd = configuredReportLcd = null;
                return;
            }

            string[] fields;
            if (!lcdFieldsCache.TryRead(blocks.CarrierPb.EntityId, GetCarrierPluginInfo(), out fields))
                return;
            int sequence;
            if (fields.Length != 5 || !int.TryParse(fields[2], out sequence))
                return;

            WriteLcd(blocks.CarrierLcdMain, fields[3], ref configuredMainLcd, ref lastMainLcdField);
            WriteLcd(blocks.CarrierLcdReport, fields[4], ref configuredReportLcd, ref lastReportLcdField);
        }

        private static void WriteLcd(IMyTextPanel panel, string field, ref IMyTextPanel configured, ref string lastField)
        {
            if (panel == null || !panel.IsWorking)
            {
                configured = null;
                return;
            }
            // Seamless transfers can create a new panel object with the old entity ID.
            if (!ReferenceEquals(panel, configured))
            {
                panel.ContentType = ContentType.TEXT_AND_IMAGE;
                panel.Font = "Debug";
                panel.FontSize = 1.225f;
                panel.TextPadding = 0f;
                panel.Alignment = TextAlignment.LEFT;
                configured = panel;
                lastField = null;
            }
            if (!string.Equals(field, lastField, StringComparison.Ordinal) && panel.WriteText(DecodeLcdText(field), false))
                lastField = field;
        }

        private static string DecodeLcdText(string value)
        {
            return (value ?? "").Replace("%0A", "\n").Replace("%0D", "\r").Replace("%7C", "|").Replace("%25", "%");
        }
    }
}
