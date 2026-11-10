void UpdateHangarState()
{
    if (IsMinerPowerTransitionActive()) return;
    if (hangarState == HangarState.None)
        return;
    if (hangarState == HangarState.MovingToHangar || hangarState == HangarState.PostDeployCheck)
    {
        hangarPhaseElapsedTicks += UpdateTickDelta();
        int timeout = hangarState == HangarState.MovingToHangar ? 7200 : 600;
        if (hangarPhaseElapsedTicks >= timeout)
        {
            FailDockingMotion(hangarState == HangarState.MovingToHangar ? "hangar approach exceeded 120 seconds" : "post-deploy status check exceeded 10 seconds");
            return;
        }
    }

    if (hangarState == HangarState.FinalService)
    {
        if (!IsMinerDocked() && !IsMergerDocked())
            return;
        if (oreTransferStartDelayTicks > 0 || oreTransferActive || !oreTransferSucceeded)
            return;
        double cargo = GetMinerCargoFillPercent();
        double hydrogen = HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 100.0;
        if (cargo > autoServiceCargoResumePercent || hydrogen < autoServiceHydrogenResumePercent)
            return;
        StartUndock();
        if (dockingState != DockingState.None || pendingUndock)
        {
            hangarState = HangarState.UndockingForStore;
            fullStateDirty = true;
        }

        return;
    }

    if (hangarState == HangarState.UndockingForStore)
    {
        if (dockingState != DockingState.None || pendingUndock)
            return;
        if (IsMinerDocked() || IsMergerDocked())
            return;
        targetPosition = hangarTargetPosition;
        desiredForward = hangarTargetForward;
        desiredUp = hangarTargetUp;
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = SPEEDB;
        hangarState = HangarState.MovingToHangar;
        hangarPhaseElapsedTicks = 0;
        fullStateDirty = true;
        ScriptLog("Hangar: Dock Pose 1 reached; moving directly to hangar center.");
        return;
    }

    if (hangarState == HangarState.MovingToHangar)
    {
        if (!IsReadyForHangarStorage())
            return;
        autopilotEnabled = false;
        hasTarget = false;
        activeMaxSpeed = 0;
        ResetMovement();
        hangarState = HangarState.AwaitingStore;
        hangarPhaseElapsedTicks = 0;
        hangarBroadcastTicks = 0;
        fullStateDirty = true;
        SaveState();
        SendHangarProgress("store_ready");
        ScriptLog("Hangar: centered and aligned; storage approved.");
        return;
    }

    if (hangarState == HangarState.AwaitingStore)
    {
        BroadcastAwaitingStoreProgress();
        return;
    }

    if (hangarState == HangarState.PostDeployCheck)
    {
        if (!cargoDetailsCurrent || cargoDetailCompletedCycles <= hangarPostDeployCargoCycleBaseline)
            return;
        FinishHangarPostDeployCheck();
        return;
    }

    if (hangarState == HangarState.PostDeployDocking)
    {
        if (dockingState != DockingState.None || dockRetryPending)
            return;
        if (!IsMinerDocked() && !IsMergerDocked())
        {
            StartHangarDockFromInnerPose();
            return;
        }

        hangarState = HangarState.PostDeployService;
        fullStateDirty = true;
        return;
    }

    if (hangarState == HangarState.PostDeployService)
    {
        if (oreTransferStartDelayTicks > 0 || oreTransferActive || !oreTransferSucceeded)
            return;
        double cargo = GetMinerCargoFillPercent();
        double hydrogen = HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 100.0;
        if (cargo > autoServiceCargoResumePercent || hydrogen < autoServiceHydrogenResumePercent)
            return;
        RefreshMinerHydrogenCache();
        if (HasMinerHydrogenTank() && GetMinerHydrogenPercent() < autoServiceHydrogenResumePercent)
            return;
        hangarState = HangarState.None;
        hangarLaunchUndocked = false;
        hangarBroadcastTicks = 0;
        SaveState();
        ScriptLog("Hangar: service complete; starting the loaded path.");
        StartNextPathRecord();
        return;
    }

    if (hangarState == HangarState.MissionReady)
        BroadcastHangarProgress("mission_ready");
}

void BroadcastHangarProgress(string status)
{
    if (hangarBroadcastTicks > 0)
    {
        hangarBroadcastTicks = Math.Max(0, hangarBroadcastTicks - UpdateTickDelta());
        return;
    }

    hangarBroadcastTicks = hangarBroadcastIntervalTicks;
    SendHangarProgress(status);
}

void BroadcastAwaitingStoreProgress()
{
    if (hangarBroadcastTicks > 0)
    {
        hangarBroadcastTicks = Math.Max(0, hangarBroadcastTicks - UpdateTickDelta());
        return;
    }

    hangarBroadcastTicks = hangarBroadcastIntervalTicks;
    // The Carrier interprets these by protocol phase: store_ready is only a
    // physical storage approval, while online is the post-deploy handshake.
    SendHangarProgress("store_ready");
    SendHangarProgress("online");
}

void SendHangarProgress(string status)
{
    IGC.SendBroadcastMessage(minerProgressTag, "hangar|" + status + "|" + hangarOperationId);
}
