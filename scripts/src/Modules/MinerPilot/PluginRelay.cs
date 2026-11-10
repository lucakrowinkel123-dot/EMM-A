void SetPluginInfoMessage(int slot, string text)
{
    pluginInfoMessages[slot] = text ?? "";
}

int moduleNextPluginRelay;
void PublishPluginServiceRequests()
{
    if (!ModuleReady() || !pluginServiceRequestsDirty && moduleTick<moduleNextPluginRelay) return;
    moduleNextPluginRelay=moduleTick+60;
    PublishDockHardwareRequest();
    long receiver = modulePeer.EntityId;
    string power = pluginPowerRequestPending
        ? "EMMA3_POWER|1|" + pluginPowerRequestId + "|" + minerPowerGoal + "|" + receiver + "|" + pluginPowerBlockIds : "";
    SetPluginInfoMessage(2, power);
    string remote = pluginRemoteLinkEnabled && minerConnector != null
        ? "EMMA3_REMOTE|1|" + receiver + "|" + minerConnector.EntityId + "|" + (minerDockDemand ? "1" : "0")
            + "|" + minerDockSession + "|" + minerDockConnectorId + "|" + pluginHydrogenTankIds + "|" + minerUnloadSourceIds : "";
    SetPluginInfoMessage(3, remote);
    for (int slot = 2; slot <= 4; slot++) SendModule("plugin", slot + "|" + (pluginInfoMessages[slot] ?? ""));
    pluginServiceRequestsDirty = false;
}
