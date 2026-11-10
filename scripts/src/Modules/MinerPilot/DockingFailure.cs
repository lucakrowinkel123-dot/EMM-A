

void LegacyFailDockingMotion(string reason)
{
    bool hangarOperation = hangarState != HangarState.None;
    bool departureFailure = IsDepartureDockingState(dockingState);
    ClearDockHardwareRequest();

    dockHardwareNativeFallback = false;
    dockingState = DockingState.None;
    dockingPhaseElapsedTicks = 0;
    dockingAttemptFailures = 0;
    autopilotEnabled = false;
    hasTarget = false;

    activeMaxSpeed = 0;

    ResetMovement();

    ScriptLog("Docking stopped: " + reason);

    if (hangarOperation)
    {

        hangarState = HangarState.None;

        hangarLaunchUndocked = false;

        SendHangarProgress("failed|" + reason.Replace('|', '/'));

        fullStateDirty = true;

        return;
    }

    if (departureFailure)
    {
        ClearDockRetry();
        pathPaused = pathMode;
        pathPausedNavResume = false;
        pathDockPending = false;
        fullStateDirty = true;
        ScriptLog("Departure stopped; active path paused for manual intervention.");
        return;
    }

    dockingAttemptFailures++;
    if (dockingAttemptFailures < dockingAttemptFailureLimit)

    {

        ArmDockRetry();

        ScriptLog("Dock retry armed after failed attempt " + dockingAttemptFailures + "/" + dockingAttemptFailureLimit + ".");

    }

    else

    {

        ClearDockRetry();

        pathMode = false;

        recallActive = false;

        recallWaitingForDock = false;

        ScriptLog("Docking abandoned after three failed attempts; manual intervention required.");

    }

}


void FailDockingMotion(string reason) {
    bool terminal=hangarState!=HangarState.None || IsDepartureDockingState(dockingState);
    LegacyFailDockingMotion(reason);
    if((terminal || !dockRetryPending) && moduleOperation.Finish(moduleOperation.Id,"error:"+reason)) {
        SaveState();PublishModuleStatus();
    }
}
