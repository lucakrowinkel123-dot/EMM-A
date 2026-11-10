long minerUnloadSession, minerUnloadCarrierConnector, minerUnloadCarrierPb;
int minerUnloadRequestTicks;
bool minerUnloadCancelled;
string minerUnloadSourceIds = "", minerUnloadPayload = "", minerUnloadLastStatus = "", minerUnloadMission = "";
void StartOreTransfer()
{
    if (!oreTransferActive && !minerUnloadCancelled) ScheduleOreTransfer();
}
void ScheduleOreTransfer()
{
    if (!IsMinerDocked() || minerConnector.OtherConnector == null) return;
    if (dockingState == DockingState.UndockingPowerUp || minerPowerGoal == 2) return;
    if (!minerDockPowerReady)
    {
        minerUnloadAfterPower = true;
        oreTransferStartDelayTicks = 1; // Persist queued intent, not a transfer countdown.
        oreTransferSucceeded = false;
        fullStateDirty = true;
        ApplyDockedPowerState();
        return;
    }
    long destination = minerConnector.OtherConnector.EntityId;
    if (oreTransferActive && minerUnloadPayload.Length > 0 && minerUnloadCarrierConnector == destination && minerUnloadMission == activeMissionId)
        return;
    minerUnloadSession = Math.Max(DateTime.UtcNow.Ticks, minerUnloadSession + 1);
    minerUnloadCarrierConnector = destination;
    minerUnloadCarrierPb = 0;
    minerUnloadCancelled = false;
    minerUnloadLastStatus = "";
    minerUnloadMission = activeMissionId;
    minerUnloadPayload = "Unload|" + minerUnloadSession + "|" + destination + "|"
        + minerConnector.EntityId + "|" + activeMissionId + "|" + minerUnloadSourceIds;
    oreTransferSucceeded = false;
    oreTransferStartDelayTicks = 0;
    oreTransferActive = true; // Pending includes blocked: service must not release the Miner.
    minerUnloadRequestTicks = 0;
    fullStateDirty = true;
    if (_oreTransferSources.Count == 0 || _oreTransferSources.Count > 128)
    {
        minerUnloadPayload = "";
        ScriptLog("Carrier unloading blocked: expected 1-128 eligible cached Miner inventory blocks; found "
            + _oreTransferSources.Count + ". Check names/exclusions and recompile.");
        return;
    }
    if (pluginOreTransferEnabled)
    {
        pluginUnloadRequestId = minerUnloadSession;
        pluginUnloadRequestPending = true;
        pluginUnloadWaitTicks = 0;
        MarkPluginServiceRequestsDirty();
        ScriptLog("Ore unloading requested through the plugin; Carrier PB transfer is standing by as fallback.");
    }
    else
        ScriptLog("Ore unloading requested: Carrier will resolve cached Miner IDs after 2 seconds.");
}
void CancelMinerUnload()
{
    minerUnloadAfterPower = false;
    minerUnloadCancelled = true;
    oreTransferSucceeded = false;
    if (oreTransferActive && minerUnloadSession > 0)
        IGC.SendBroadcastMessage(unloadRequestTag, "Cancel|" + minerUnloadSession + "|"
            + minerUnloadCarrierConnector + "|" + (minerConnector == null ? 0 : minerConnector.EntityId));
    oreTransferActive = false;
    pluginUnloadRequestPending = false;
    pluginUnloadWaitTicks = 0;
    oreTransferStartDelayTicks = 0;
    minerUnloadPayload = "";
    MarkPluginServiceRequestsDirty();
    fullStateDirty = true;
}
void UpdateOreTransfer()
{
    if (!minerDockPowerReady)
    {
        bool departing = minerPowerGoal == 2 || dockingState == DockingState.UndockingPowerUp;
        CancelMinerUnload();
        if (!departing) ScheduleOreTransfer();
        return;
    }
    if (!IsMinerDocked() || minerConnector.OtherConnector == null
        || minerConnector.OtherConnector.EntityId != minerUnloadCarrierConnector)
    {
        CancelMinerUnload();
        oreTransferSucceeded = false;
        return;
    }
    if (pluginUnloadRequestPending)
    {
        pluginUnloadWaitTicks += UpdateTickDelta();
        if (pluginOreTransferEnabled && pluginUnloadWaitTicks < pluginUnloadTimeoutTicks)
            return;
        pluginUnloadRequestPending = false;
        pluginUnloadWaitTicks = 0;
        minerUnloadRequestTicks = 0;
        MarkPluginServiceRequestsDirty();
        ScriptLog("Plugin unloading unavailable; Carrier PB fallback activated.");
    }
    for (int i = 0; i < 4 && IGC.UnicastListener.HasPendingMessage; i++)
    {
        MyIGCMessage message = IGC.UnicastListener.AcceptMessage();
        if (message.Tag == unloadReplyTag) HandleCarrierUnloadReply(message.Source, message.Data as string);
    }
    if (!oreTransferActive || minerUnloadPayload.Length == 0) return;
    minerUnloadRequestTicks -= UpdateTickDelta();
    if (minerUnloadRequestTicks > 0) return;
    minerUnloadRequestTicks = 300;
    IGC.SendBroadcastMessage(unloadRequestTag, minerUnloadPayload);
}

void CachePluginServiceIds()
{
    StringBuilder ids = new StringBuilder();
    for (int i = 0; i < minerPowerBlocks.Count; i++)
    {
        IMyFunctionalBlock block = minerPowerBlocks[i];
        if (block == null) continue;
        if (ids.Length > 0) ids.Append(',');
        ids.Append(block.EntityId);
    }
    pluginPowerBlockIds = ids.ToString();
    ids.Clear();
    for (int i = 0; i < hydrogenTanks.Count; i++)
    {
        IMyGasTank tank = hydrogenTanks[i];
        if (tank == null || !HasMinerTag(tank)) continue;
        if (ids.Length > 0) ids.Append(',');
        ids.Append(tank.EntityId);
    }
    pluginHydrogenTankIds = ids.ToString();
}

void HandlePluginFeatures(string command)
{
    string[] fields = command.Split('|');
    if (fields.Length != 4) return;
    bool unload = fields[1] == "1";
    bool power = fields[2] == "1";
    bool remote = fields[3] == "1";
    bool mailboxChanged = remote != pluginRemoteLinkEnabled;
    if (!unload && pluginUnloadRequestPending)
    {
        pluginUnloadRequestPending = false;
        pluginUnloadWaitTicks = 0;
        minerUnloadRequestTicks = 0;
        mailboxChanged = true;
    }
    if (!power && pluginPowerRequestPending)
    {
        pluginPowerRequestPending = false;
        pluginPowerWaitTicks = 0;
        minerPowerIndex = 0;
        mailboxChanged = true;
    }
    pluginOreTransferEnabled = unload;
    pluginPowerTransitionsEnabled = power;
    pluginRemoteLinkEnabled = remote;
    pluginFeatureAgeTicks = 0;
    if (!remote)
    {
        pluginRemoteTelemetryAgeTicks = int.MaxValue;
        pluginRemotePoseWaitTicks = 0;
    }
    if (mailboxChanged)
        MarkPluginServiceRequestsDirty();
}

void UpdatePluginServiceAges()
{
    int delta = UpdateTickDelta();
    if (pluginFeatureAgeTicks < int.MaxValue - delta)
        pluginFeatureAgeTicks += delta;
    if (pluginFeatureAgeTicks > pluginFeatureLeaseTicks)
    {
        bool mailboxChanged = pluginRemoteLinkEnabled || pluginUnloadRequestPending || pluginPowerRequestPending;
        pluginOreTransferEnabled = false;
        pluginPowerTransitionsEnabled = false;
        pluginRemoteLinkEnabled = false;
        if (mailboxChanged)
            MarkPluginServiceRequestsDirty();
    }
    if (pluginRemoteTelemetryAgeTicks < int.MaxValue - delta)
        pluginRemoteTelemetryAgeTicks += delta;
}

bool PluginRemoteTelemetryFresh()
{
    return pluginRemoteLinkEnabled && pluginRemoteTelemetryAgeTicks <= pluginRemoteTelemetryFreshTicks;
}

// Keys are primitives/strings: checking them must not build another message.
class PluginMessageCache
{
    public string Text;
    long keyA, keyB, keyC, keyD, keyE, keyF;
    string keyTextA, keyTextB;

    public bool Changed(long a = 0, long b = 0, long c = 0, long d = 0, long e = 0, long f = 0,
        string textA = "", string textB = "")
    {
        if (Text != null && a == keyA && b == keyB && c == keyC && d == keyD && e == keyE && f == keyF
            && textA == keyTextA && textB == keyTextB) return false;
        keyA = a; keyB = b; keyC = c; keyD = d; keyE = e; keyF = f;
        keyTextA = textA; keyTextB = textB;
        return true;
    }
}

readonly PluginMessageCache pluginReadyMessage = new PluginMessageCache();
readonly PluginMessageCache pluginUnloadMessage = new PluginMessageCache();
readonly PluginMessageCache pluginPowerMessage = new PluginMessageCache();
readonly PluginMessageCache pluginRemoteMessage = new PluginMessageCache();
readonly PluginMessageCache pluginDockMessage = new PluginMessageCache();

void MarkPluginServiceRequestsDirty()
{
    pluginServiceRequestsDirty = true;
}

void SetPluginInfoMessage(int slot, string text)
{
    text = text ?? "";
    if (string.Equals(pluginInfoMessages[slot], text, StringComparison.Ordinal))
        return;
    pluginInfoMessages[slot] = text;
    pluginInfoDirty = true;
}

void FlushPluginInfoMessages()
{
    if (minerPluginInfoTimer == null)
    {
        bool emitted = false;
        for (int i = 0; i < pluginInfoMessages.Length; i++)
            if (!string.IsNullOrEmpty(pluginInfoMessages[i]))
            {
                Echo(pluginInfoMessages[i]);
                emitted = true;
            }
        if (!emitted)
            Echo("EMMA3_DOCK_HW_IDLE");
        return;
    }
    if (!pluginInfoDirty)
        return;

    StringBuilder sb = _pluginInfoSb;
    sb.Clear();
    for (int i = 0; i < pluginInfoMessages.Length; i++)
    {
        string message = pluginInfoMessages[i];
        if (string.IsNullOrEmpty(message))
            continue;
        if (sb.Length > 0)
            sb.Append('\n');
        sb.Append(message);
    }
    string payload = sb.ToString();
    if (!string.Equals(publishedPluginInfo, payload, StringComparison.Ordinal))
    {
        minerPluginInfoTimer.CustomData = payload;
        publishedPluginInfo = payload;
    }
    pluginInfoDirty = false;
}

void PublishPluginServiceRequests()
{
    if (!pluginServiceRequestsDirty)
        return;
    pluginServiceRequestsDirty = false;
    PublishDockHardwareRequest();
    if (pluginReadyMessage.Changed(pluginServiceSession))
        pluginReadyMessage.Text = "EMMA3_FEATURES_READY|1|" + pluginServiceSession;
    SetPluginInfoMessage(0, pluginReadyMessage.Text);
    if (pluginUnloadRequestPending && minerUnloadPayload.Length > 0)
    {
        if (pluginUnloadMessage.Changed(pluginUnloadRequestId, textA: minerUnloadPayload))
            pluginUnloadMessage.Text = "EMMA3_UNLOAD|1|" + pluginUnloadRequestId + "|" + minerUnloadPayload;
        SetPluginInfoMessage(1, pluginUnloadMessage.Text);
    }
    else SetPluginInfoMessage(1, "");
    if (pluginPowerRequestPending)
    {
        if (pluginPowerMessage.Changed(pluginPowerRequestId, minerPowerGoal, Me.EntityId, textA: pluginPowerBlockIds))
            pluginPowerMessage.Text = "EMMA3_POWER|1|" + pluginPowerRequestId + "|" + minerPowerGoal + "|" + Me.EntityId + "|" + pluginPowerBlockIds;
        SetPluginInfoMessage(2, pluginPowerMessage.Text);
    }
    else SetPluginInfoMessage(2, "");
    if (pluginRemoteLinkEnabled && minerConnector != null)
    {
        if (pluginRemoteMessage.Changed(Me.EntityId, minerConnector.EntityId, minerDockDemand ? 1 : 0,
            minerDockSession, minerDockConnectorId, textA: pluginHydrogenTankIds, textB: minerUnloadSourceIds))
            pluginRemoteMessage.Text = "EMMA3_REMOTE|1|" + Me.EntityId + "|" + minerConnector.EntityId + "|"
                + (minerDockDemand ? "1" : "0") + "|" + minerDockSession + "|" + minerDockConnectorId + "|"
                + pluginHydrogenTankIds + "|" + minerUnloadSourceIds;
        SetPluginInfoMessage(3, pluginRemoteMessage.Text);
    }
    else SetPluginInfoMessage(3, "");
    FlushPluginInfoMessages();
}

void HandlePluginUnloadResult(string command)
{
    string[] fields = command.Split('|');
    long requestId;
    if (fields.Length < 4 || !long.TryParse(fields[1], out requestId)
        || !pluginUnloadRequestPending || requestId != pluginUnloadRequestId) return;
    pluginUnloadRequestPending = false;
    pluginUnloadWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    if (fields[2].Equals("ok", StringComparison.OrdinalIgnoreCase))
    {
        oreTransferSucceeded = true;
        oreTransferActive = false;
        minerUnloadPayload = "";
        fullStateDirty = true;
        ScriptLog("Plugin ore unloading complete.");
    }
    else
    {
        minerUnloadRequestTicks = 0;
        ScriptLog("Plugin ore unloading failed: " + fields[3] + ". Carrier PB fallback activated.");
    }
}

void HandlePluginRemoteStatus(string command)
{
    string[] fields = command.Split('|');
    double cargo, stone, water;
    int tanks;
    if (fields.Length != 7 || !TryParseNumber(fields[1], out cargo)
        || !TryParseNumber(fields[2], out stone) || !TryParseNumber(fields[4], out water)
        || !int.TryParse(fields[5], out tanks)) return;
    _cachedCargoPercent = MathHelper.Clamp(cargo, 0, 100);
    _cachedStonePercent = MathHelper.Clamp(stone, 0, 100);
    _cachedOrePresent = fields[3] == "1";
    cargoStatsCompletedCycles++;
    cargoDetailCompletedCycles++;
    cargoDetailsCurrent = true;
    cachedMinerHydrogenPercent = MathHelper.Clamp(water, 0, 100);
    cachedMinerHydrogenTankCount = Math.Max(0, tanks);
    minerHydrogenCacheValid = true;
    minerHydrogenCacheTicks = pluginRemoteTelemetryFreshTicks;
    pluginRemoteTelemetryAgeTicks = 0;
}

void HandlePluginDockPose(string command)
{
    string[] fields = command.Split('|');
    long session, connectorId;
    double px, py, pz, fx, fy, fz, ux, uy, uz;
    if (fields.Length != 12 || !long.TryParse(fields[1], out session) || session != minerDockSession
        || !long.TryParse(fields[2], out connectorId) || connectorId != minerDockConnectorId
        || !TryParseNumber(fields[3], out px) || !TryParseNumber(fields[4], out py) || !TryParseNumber(fields[5], out pz)
        || !TryParseNumber(fields[6], out fx) || !TryParseNumber(fields[7], out fy) || !TryParseNumber(fields[8], out fz)
        || !TryParseNumber(fields[9], out ux) || !TryParseNumber(fields[10], out uy) || !TryParseNumber(fields[11], out uz)) return;
    Vector3D position = new Vector3D(px, py, pz);
    Vector3D forward = new Vector3D(fx, fy, fz);
    Vector3D up = new Vector3D(ux, uy, uz);
    if (!ValidDockPose(position, forward, up)) return;
    carrierConnectorPos = position;
    carrierConnectorForward = Vector3D.Normalize(forward);
    carrierConnectorUp = Vector3D.Normalize(up);
    carrierPoseAgeSeconds = 0;
    minerDockSessionTicks = 0;
    pluginRemotePoseWaitTicks = 0;
    if (dockRetryPending) dockRetryTicks = 0;
}
void HandleCarrierUnloadReply(long sender, string text)
{
    if (!oreTransferActive || string.IsNullOrEmpty(text) || text.Length > 512) return;
    string[] parts = text.Split('|');
    long session, destination, connector;
    if (parts.Length != 5 || parts[0] != "UnloadState"
        || !long.TryParse(parts[1], out session) || session != minerUnloadSession
        || !long.TryParse(parts[2], out destination) || destination != minerUnloadCarrierConnector
        || !long.TryParse(parts[3], out connector) || connector != minerConnector.EntityId
        || !IsMinerDocked() || minerConnector.OtherConnector == null
        || minerConnector.OtherConnector.EntityId != destination) return;
    if (minerUnloadCarrierPb == 0)
    {
        var pb = GridTerminalSystem.GetBlockWithId(sender) as IMyProgrammableBlock;
        if (pb == null || !pb.IsSameConstructAs(minerConnector.OtherConnector)
            || pb.CustomName.IndexOf(Ca("Prog. Block"), StringComparison.OrdinalIgnoreCase) < 0) return;
        minerUnloadCarrierPb = sender;
    }
    if (sender != minerUnloadCarrierPb) return;
    string status = parts[4];
    if (status != "busy" && status != "blocked" && status != "complete" && status != "cancelled") return;
    if (status != minerUnloadLastStatus)
    {
        minerUnloadLastStatus = status;
        ScriptLog("Carrier ore unloading: " + status + ".");
    }
    if (status == "complete")
    {
        oreTransferSucceeded = true;
        oreTransferActive = false;
        minerUnloadPayload = "";
        MarkPluginServiceRequestsDirty();
        fullStateDirty = true;
    }
    else if (status == "cancelled")
    {
        CancelMinerUnload();
        oreTransferSucceeded = false;
    }
}
