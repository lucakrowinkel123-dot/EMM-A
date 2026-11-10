
void BroadcastMinerProgress()
{
    if (!routeProgressTracking || routeProgressTotalMeters <= 0)
        return;
    if (minerProgressBroadcastTicks > 0)
    {
        minerProgressBroadcastTicks = Math.Max(0, minerProgressBroadcastTicks - UpdateTickDelta());
        return;
    }

    minerProgressBroadcastTicks = minerProgressBroadcastIntervalTicks - UpdateTickDelta();
    EmitMinerEvent(
        minerProgressTag,
        "route:" + routeProgressMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + routeProgressTotalMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
}


void SendRecallProgress(int done, int total)
{
    done = Math.Max(0, done);
    total = Math.Max(0, total);
    if (total > 0)
        done = Math.Min(done, total);
    EmitMinerEvent(minerProgressTag, "progress:" + done + "/" + total);
}


void SendHangarProgress(string status)
{
    EmitMinerEvent(minerProgressTag, "hangar|" + status + "|" + hangarOperationId);
}


void SendPathStartAccepted(string missionId)
{
    if (string.IsNullOrWhiteSpace(missionId))
        return;
    EmitMinerEvent(minerProgressTag, "path_started|" + missionId);
}


void CompleteRecallPath()
{
    if (IsMinerDocked() && !minerDockPowerReady)
        return;
    if (autoServiceState == AutoServiceState.Returning)
    {
        CompleteAutoServiceRecall();
        return;
    }

    SendRecallProgress(recallLastTotal, recallLastTotal);
    pathMode = false;
    pathPaused = false;
    pathPausedNavResume = false;
    pathRecords.Clear();
    pathIndex = 0;
    pathCompleteBroadcastTicks = 0;
    recallActive = false;
    recallWaitingForAreaExit = false;
    recallWaitingForDock = false;
    recallPreparing = false;
    recallPreparationPhase = RecallPreparationPhase.None;
    recallRecords.Clear();
    recallDisplayDone.Clear();
    recallIndex = 0;
    EmitMinerEvent(minerPathCompleteTag, "RecallComplete|" + activeMissionId + "|1");
    ScriptLog("Recall complete.");
}
