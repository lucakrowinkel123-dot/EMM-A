
void SetFullAutoEnabledFromUi(bool enabled)
{
    if (enabled && (!ModuleReady() || pilotRecoveryBlocked)) { ScriptLog("AM blocked: Carrier Pilot is not ready."); return; }
    bool wasEnabled = fullAutoEnabled;
    bool hadEmma3Mission = emma3MissionState != Emma3MissionState.None;
    if (enabled && !fullAutoEnabled)
    {
        double speed = rc == null ? 0 : flightCache.LinearVelocity.Length();
        if (speed > Math.Max(0, mineStationarySpeedMps))
        {
            fullAutoEnableStationaryTicks = 0;
            ScriptLog($"Fullauto denied: carrier must be stationary ({speed:0.00} m/s).");
            return;
        }

        if (fullAutoEnableStationaryTicks < Math.Max(1, mineStationaryConfirmTicks))
        {
            fullAutoEnableStationaryTicks = 1;
            ScriptLog("Fullauto waiting for stationary confirmation.");
            return;
        }
    }

    fullAutoEnableStationaryTicks = 0;
    if (enabled && !fullAutoEnabled)
        ClearConflictingAutomationForEmma3();
    fullAutoEnabled = enabled;
    WriteEmma3Settings();
    if (!enabled)
    {
        StopParkedReportClock();
        reportApproachSeconds = 0;
        finishAndHold = false;
        EndCarrierTransitFlightCache();
        emma3MissionState = Emma3MissionState.None;
        emma3WaitForSeatBeforeNextPlan = false;
        if (wasEnabled || hadEmma3Mission)
            RequestEmma3PlannerAbort("Autonomous mining stopped.");
    }

    ScriptLog("Fullauto: " + (enabled ? "enabled." : "stopped."));
}
