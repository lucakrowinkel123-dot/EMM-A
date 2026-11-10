// A new session invalidates queued data after recompile, demand-off, or contact loss.
long minerDockSession, minerDockConnectorId, minerDockCarrier, minerDockEpoch, minerDockRevision;
bool minerDockDemand, pendingUndock;
int minerDockRenewTicks, minerDockBootstrapTicks, minerDockSessionTicks;

bool NeedsCarrierDockPose()
{
    if (scriptPaused) return false;
    return pendingUndock || pathDockPending || dockRetryPending || dockingState != DockingState.None
        || recallActive || recallPreparing || recallWaitingForDock
        || autoServiceState == AutoServiceState.Returning || autoServiceState == AutoServiceState.Undocking
        || hangarState == HangarState.UndockingForStore || hangarState == HangarState.PostDeployDocking;
}

void BeginMinerDockSession()
{
    minerDockSession = Math.Max(minerDockSession + 1, DateTime.UtcNow.Ticks);
    minerDockEpoch = minerDockRevision = minerDockCarrier = 0;
    carrierPoseAgeSeconds = 9999;
    minerDockRenewTicks = minerDockBootstrapTicks = minerDockSessionTicks = 0;
    pluginRemotePoseWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    fullStateDirty = true;
}

void UpdateMinerDockDemand()
{
    bool needed = NeedsCarrierDockPose();
    if (!needed)
    {
        if (minerDockDemand) SendMinerDockDemand(false);
        if (minerDockDemand) MarkPluginServiceRequestsDirty();
        minerDockDemand = false;
        carrierPoseAgeSeconds = 9999;
        pluginRemotePoseWaitTicks = 0;
        return;
    }
    if (!minerDockDemand)
    {
        minerDockDemand = true;
        BeginMinerDockSession();
    }
    int delta = UpdateTickDelta();
    minerDockSessionTicks += delta;
    minerDockRenewTicks -= delta;
    minerDockBootstrapTicks -= delta;
    if (pluginRemoteLinkEnabled && !HasFreshCarrierPose())
        pluginRemotePoseWaitTicks += delta;
    else
        pluginRemotePoseWaitTicks = 0;
    bool useLegacyLink = !pluginRemoteLinkEnabled || pluginRemotePoseWaitTicks >= pluginRemotePoseGraceTicks;
    if (!HasFreshCarrierPose() && minerDockSessionTicks >= 360)
        BeginMinerDockSession();
    if (minerDockConnectorId == 0 && carrierConnector != null && !carrierConnector.Closed)
    {
        minerDockConnectorId = carrierConnector.EntityId;
        MarkPluginServiceRequestsDirty();
    }
    if (useLegacyLink && !HasFreshCarrierPose() && minerDockBootstrapTicks <= 0)
    {
        minerDockBootstrapTicks = 300;
        // Discovery remains Carrier-owned and uses its startup-cached connector.
        IGC.SendBroadcastMessage(minerProgressTag, "hangar|dock_pose_request|0");
    }
    if (useLegacyLink && minerDockRenewTicks <= 0)
    {
        minerDockRenewTicks = 60;
        SendMinerDockDemand(true);
    }
}

void SendMinerDockDemand(bool needed)
{
    if (minerDockConnectorId <= 0) return;
    IGC.SendBroadcastMessage(minerCommandTag + "_DOCK_V2",
        new MyTuple<long, long, long, long, bool>(minerDockSession, minerDockConnectorId, minerDockEpoch, minerDockRevision, needed));
}

void ApplyCarrierDockEndpoint(string command, long source)
{
    long connectorId;
    if (!minerDockDemand || !long.TryParse(command.Substring("DockEndpoint|".Length), out connectorId) || connectorId <= 0) return;
    if (minerDockCarrier != 0 && source != minerDockCarrier && HasFreshCarrierPose()) return;
    if (minerDockConnectorId != connectorId)
    {
        minerDockConnectorId = connectorId;
        BeginMinerDockSession();
    }
    minerDockRenewTicks = 0;
}

void UpdateDockPoseFromBroadcast()
{
    if (dockListener == null) return;
    // Expired contact cannot be revived by queued heartbeats from the old session.
    if (minerDockDemand && minerDockEpoch != 0 && !HasFreshCarrierPose())
        BeginMinerDockSession();
    int budget = 1;
    while (budget-- > 0 && dockListener.HasPendingMessage)
    {
        MyIGCMessage message = dockListener.AcceptMessage();
        if (!minerDockDemand) continue;
        if (message.Data is MyTuple<long, long, long, long, long, MyTuple<Vector3D, Vector3D, Vector3D>>)
        {
            var pose = (MyTuple<long, long, long, long, long, MyTuple<Vector3D, Vector3D, Vector3D>>)message.Data;
            if (pose.Item1 != Me.EntityId || pose.Item2 != minerDockSession || pose.Item5 != minerDockConnectorId
                || pose.Item3 <= 0 || pose.Item4 <= 0 || (minerDockCarrier != 0 && message.Source != minerDockCarrier)) continue;
            if (pose.Item3 == minerDockEpoch && pose.Item4 < minerDockRevision) continue;
            if (!ValidDockPose(pose.Item6.Item1, pose.Item6.Item2, pose.Item6.Item3)) continue;
            minerDockCarrier = message.Source;
            minerDockEpoch = pose.Item3;
            minerDockRevision = pose.Item4;
            carrierConnectorPos = pose.Item6.Item1;
            carrierConnectorForward = Vector3D.Normalize(pose.Item6.Item2);
            carrierConnectorUp = Vector3D.Normalize(pose.Item6.Item3);
            carrierPoseAgeSeconds = 0;
            minerDockSessionTicks = 0;
            if (dockRetryPending) dockRetryTicks = 0;
        }
        else if (message.Data is MyTuple<long, long, long, long, long>)
        {
            var heartbeat = (MyTuple<long, long, long, long, long>)message.Data;
            if (heartbeat.Item1 != Me.EntityId || heartbeat.Item2 != minerDockSession || heartbeat.Item5 != minerDockConnectorId
                || message.Source != minerDockCarrier || heartbeat.Item3 != minerDockEpoch || heartbeat.Item4 != minerDockRevision) continue;
            carrierPoseAgeSeconds = 0;
            minerDockSessionTicks = 0;
        }
    }
    if (pendingUndock && !scriptPaused && HasFreshCarrierPose())
        StartUndock();
}

void RequestCarrierDockPose()
{
    UpdateMinerDockDemand();
}
