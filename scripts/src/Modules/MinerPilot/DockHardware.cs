
void PublishDockHardwareRequest()
{
    if (!dockHardwareRequestPending || minerConnector == null)
    {
        SetPluginInfoMessage(4, "");
        return;
    }
    long mergerId = minerMerger == null ? 0 : minerMerger.EntityId;
    if (pluginDockMessage.Changed(dockHardwareRequestId, minerConnector.EntityId, mergerId,
        mergeAssistedDocking ? 1 : 0, (modulePeer == null ? 0 : modulePeer.EntityId), textA: dockHardwareRequestAction))
        pluginDockMessage.Text = "EMMA3_DOCK_HW|1|" + dockHardwareRequestId + "|" + dockHardwareRequestAction + "|"
            + minerConnector.EntityId + "|" + mergerId + "|" + (mergeAssistedDocking ? "1" : "0") + "|" + (modulePeer == null ? 0 : modulePeer.EntityId);
    SetPluginInfoMessage(4, pluginDockMessage.Text);
}
