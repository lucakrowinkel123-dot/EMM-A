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
        if (!HasModuleServicePermit()) return;
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
        if (!moduleDeployDecisionReady)
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
        if (!HasModuleServicePermit()) return;
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


void UpdateAutoServiceState()
{
    if (IsMinerPowerTransitionActive()) return;
    if (autoServiceState == AutoServiceState.Returning)
    {
        if (recallActive)
            return;
        if (IsMinerDocked() || IsMergerDocked())
        {
            CompleteAutoServiceRecall();
            return;
        }

        ClearAutoServiceState();
        return;
    }

    if (autoServiceState == AutoServiceState.WaitingService)
    {
        if (!IsMinerDocked() && !IsMergerDocked())
        {
            RetryAutoServiceDock();
            return;
        }

        if (autoServiceReturnRouteBuildPending || !HasModuleServicePermit()) return;
        RetryAutoServiceUndock();
        return;
    }

    if (autoServiceState == AutoServiceState.Undocking)
    {
        if (dockingState != DockingState.None || pendingUndock)
            return;
        if (IsMinerDocked() || IsMergerDocked())
        {
            RetryAutoServiceUndock();
            return;
        }

        autoServiceState = AutoServiceState.ReturningToWork;
        autoServiceActionRetryTicks = 0;
        pathPaused = true;
        pathPausedNavResume = false;
        autoServiceReturnRouteIndex = 0;
        fullStateDirty = true;
        ScriptLog("Auto service: following the safe route back to work.");
        StartNextAutoServiceReturnTarget();
        return;
    }

    if (autoServiceState == AutoServiceState.ReturningToWork)
    {
        if (dockingState != DockingState.None || dockRetryPending)
            return;
        if (autopilotEnabled && hasTarget)
        {
            if (!IsAtTargetPose(targetPosition, desiredForward, desiredUp))
                return;
            autopilotEnabled = false;
            hasTarget = false;
            activeMaxSpeed = 0;
        }

        StartNextAutoServiceReturnTarget();
        return;
    }
}


void FinishHangarPostDeployCheck()
{
    if (!moduleDeployDecisionReady) return;
    bool needsService = moduleDeployNeedsService;
    hangarBroadcastTicks = 0;
    if (!needsService)
    {
        hangarState = HangarState.None;
        hangarLaunchUndocked = true;
        SaveState();
        ScriptLog("Hangar: fuel and cargo are ready; starting the path through Dock Pose 2.");
        StartNextPathRecord();
        return;
    }

    if (!StartHangarDockFromInnerPose())
        return;
    hangarState = HangarState.PostDeployDocking;
    hangarLaunchUndocked = false;
    fullStateDirty = true;
    ScriptLog("Hangar: deployed Miner needs cargo or H2 service; moving directly to Dock Pose 1.");
}
