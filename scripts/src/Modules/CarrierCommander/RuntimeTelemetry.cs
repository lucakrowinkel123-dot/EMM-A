string runtimePublishedOwn = "", runtimePublishedPeer = "", runtimePublishedLcd = "";
void PublishPluginLcdPayload()
{
    if (emma3PluginLcdOutput && pluginLcdMarker.Length == 0)
    {
        StringBuilder sb = _pluginLcdMarkerSb;
        sb.Clear();
        sb.Append("EMMA3_LCD|1|").Append(pluginLcdSequence).Append('|');
        LowAllocationText.AppendPluginLcd(sb, pluginLcdMainText);
        sb.Append('|');
        LowAllocationText.AppendPluginLcd(sb, pluginLcdReportText);
        pluginLcdMarker = sb.ToString();
    }
    string own = GetRuntimeReport(), lcd = emma3PluginLcdOutput ? pluginLcdMarker : "";
    if (runtimePublishedOwn != own || runtimePublishedPeer != pilotRuntimeReport || runtimePublishedLcd != lcd)
    {
        runtimePublishedOwn = own; runtimePublishedPeer = pilotRuntimeReport; runtimePublishedLcd = lcd;
        publishedPluginLcdMarker = own + "\n" + pilotRuntimeReport + (lcd.Length > 0 ? "\n" + lcd : "");
    }
    if (carrierPluginInfoTimer != null)
    {
        if (carrierPluginInfoTimer.CustomData != publishedPluginLcdMarker)
            carrierPluginInfoTimer.CustomData = publishedPluginLcdMarker;
    }
    else if (lcd.Length > 0) Echo(lcd);
}
