bool recallPreparationStartBranchKnown = false;
bool recallPreparationStartsInsideBranch = false;

void StartRecallPath(string missionId = "", bool preserveAutoService = false)
{
    CancelPathLoad();
    if (!string.IsNullOrWhiteSpace(missionId))
        activeMissionId = missionId;
    if (recallActive)
    {
        ScriptLog("Recall already active.");
        return;
    }

    if (!pathMode || pathRecords.Count == 0)
    {
        ScriptLog("Recall ignored: no active path.");
        return;
    }

    bool finishSafeDeparture = IsDetachedDepartureInProgress();
    if (!preserveAutoService)
        ClearAutoServiceState();
    ClearStoneEjectionHold();
    pathDockPending = false;
    ClearDockRetry();
    if (!finishSafeDeparture)
        CancelUndockSequence();
    scriptPaused = false;
    pauseResumeAutopilot = false;
    Runtime.UpdateFrequency = tickSpeed;
    SetDrills(false);
    recallActive = true;
    recallWaitingForAreaExit = false;
    recallWaitingForDock = false;
    recallIndex = 0;
    recallRecords.Clear();
    recallDisplayDone.Clear();
    recallLastDone = Math.Max(0, Math.Min(pathIndex, pathRecords.Count));
    recallLastTotal = pathRecords.Count;
    minerProgressBroadcastTicks = 0;
    pathPaused = false;
    pathPausedNavResume = false;
    pathCompleteBroadcastTicks = 0;
    fullStateDirty = true;
    SendRecallProgress(recallLastDone, recallLastTotal);
    if (TryRecallConnectedMiner())
        return;
    if (finishSafeDeparture)
    {
        ScriptLog("Recall queued: finishing safe separation to the outer dock pose first.");
        return;
    }
    if (miningEnabled || areaMiningMode)
    {
        recallWaitingForAreaExit = true;
        NextHole(true);
        ScriptLog("Recall: finishing current hole before path recall.");
        return;
    }

    BeginRecallPreparation();
}

bool IsDetachedDepartureInProgress()
{
    if (IsMinerDocked())
        return false;
    return IsUndockSequenceActive() || pendingUndock
        || dockingState == DockingState.UndockingPowerUp
        || dockingState == DockingState.UndockingRecompile
        || dockingState == DockingState.UndockingToPose
        || dockingState == DockingState.UndockingToOuterPose;
}

bool TryRecallConnectedMiner()
{
    if (!IsMinerDocked())
        return false;
    // A recall can arrive during the newly staged departure window. Cancel
    // that departure while still attached, then use the ordinary shutdown and
    // completion gates rather than building a route away from the connector.
    CancelUndockSequence();
    recallWaitingForDock = true;
    recallPreparing = false;
    recallPreparationPhase = RecallPreparationPhase.None;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    ApplyDockedPowerState();
    EnterDockingState(DockingState.DockingPowerDown);
    ScriptLog("Recall: already connected; finishing power shutdown.");
    return true;
}

void UpdateRecallRunner()
{
    if (!recallActive || IsMinerPowerTransitionActive())
        return;
    if (IsDetachedDepartureInProgress())
        return;
    if (recallWaitingForAreaExit)
    {
        if (miningEnabled)
            return;
        if (autopilotEnabled && hasTarget)
        {
            if (IsAtTargetPose(targetPosition, desiredForward, desiredUp))
            {
                autopilotEnabled = false;
                hasTarget = false;
                activeMaxSpeed = 0;
                SendRecallProgress(Math.Max(0, Math.Min(pathIndex, pathRecords.Count)), pathRecords.Count);
            }

            return;
        }

        recallWaitingForAreaExit = false;
        BeginRecallPreparation();
    }

    if (recallPreparing)
        return;
    if (!recallWaitingForAreaExit && !recallWaitingForDock && recallRecords.Count == 0)
    {
        BeginRecallPreparation();
        return;
    }
    if (recallWaitingForDock)
    {
        if (dockingState != DockingState.None || dockRetryPending)
            return;
        if (IsMinerDocked() || IsMergerDocked())
        {
            CompleteRecallPath();
            return;
        }

        StartDock();
        return;
    }

    if (autopilotEnabled && hasTarget)
    {
        if (IsAtTargetPose(targetPosition, desiredForward, desiredUp))
        {
            autopilotEnabled = false;
            hasTarget = false;
            activeMaxSpeed = 0;
            if (!recallReverseMode)
                recallLastDone = Math.Max(0, Math.Min(pathIndex, pathRecords.Count));
            SendRecallProgress(recallLastDone, recallLastTotal);
        }

        return;
    }

    if (dockingState != DockingState.None || dockRetryPending)
        return;
    StartNextRecallTarget();
}

void BeginRecallPreparation()
{
    recallRecords.Clear();
    recallDisplayDone.Clear();
    recallIndex = 0;
    recallPreparing = true;
    recallPreparationPhase = RecallPreparationPhase.EstimateStart;
    recallPreparationStartPosition = _tickRcPosition;
    recallPreparationPrevious = recallPreparationStartPosition;
    recallPreparationDistanceToStart = 0;
    recallPreparationDistanceToEnd = 0;
    recallPreparationStartBranchKnown = false;
    recallPreparationStartsInsideBranch = false;
    recallPreparationIndex = Math.Max(0, Math.Min(pathIndex, pathRecords.Count)) - 1;
    ResetRecallMiningBranchFilter(true, pathIndex);
    recallPreparationActiveTargetValid = autopilotEnabled && hasTarget;
    recallPreparationActiveTarget = targetPosition;
    recallPreparationAnchorPending = pathAnchorValid;
    recallPreparationDockPending = false;
    fullStateDirty = true;
    ScriptLog("Recall: preparing route.");
}

void UpdateRecallPreparation()
{
    if (!recallActive || !recallPreparing || recallWaitingForAreaExit)
        return;
    int budget = recallPreparationRecordsPerTick;
    while (budget-- > 0 && recallPreparing)
    {
        if (recallPreparationPhase == RecallPreparationPhase.EstimateStart)
        {
            if (recallPreparationIndex >= 0)
            {
                AddRecallEstimate(pathRecords[recallPreparationIndex--], true, ref recallPreparationPrevious, ref recallPreparationDistanceToStart);
                continue;
            }

            if (recallPreparationAnchorPending)
            {
                recallPreparationAnchorPending = false;
                AddRecallEstimatePosition(pathAnchorPos, ref recallPreparationPrevious, ref recallPreparationDistanceToStart);
                continue;
            }

            recallPreparationPhase = RecallPreparationPhase.EstimateEnd;
            recallPreparationPrevious = recallPreparationStartPosition;
            recallPreparationIndex = Math.Max(0, Math.Min(pathIndex, pathRecords.Count));
            ResetRecallPreparationMiningBranchFilter(false);
            if (recallPreparationActiveTargetValid)
            {
                AddRecallEstimatePosition(recallPreparationActiveTarget, ref recallPreparationPrevious, ref recallPreparationDistanceToEnd);
                recallPreparationActiveTargetValid = false;
            }

            continue;
        }

        if (recallPreparationPhase == RecallPreparationPhase.EstimateEnd)
        {
            if (recallPreparationIndex < pathRecords.Count)
            {
                AddRecallEstimate(pathRecords[recallPreparationIndex++], false, ref recallPreparationPrevious, ref recallPreparationDistanceToEnd);
                continue;
            }

            recallReverseMode = recallPreparationDistanceToStart < recallPreparationDistanceToEnd;
            recallPreparationPhase = RecallPreparationPhase.Build;
            recallPreparationIndex = recallReverseMode ? Math.Max(0, Math.Min(pathIndex, pathRecords.Count)) - 1 : Math.Max(0, Math.Min(pathIndex, pathRecords.Count));
            recallPreparationAnchorPending = recallReverseMode && pathAnchorValid;
            recallPreparationDockPending = recallReverseMode;
            ResetRecallPreparationMiningBranchFilter(recallReverseMode);
            fullStateDirty = true;
            continue;
        }

        if (recallPreparationPhase == RecallPreparationPhase.Build)
        {
            if (recallReverseMode && recallPreparationIndex >= 0)
            {
                int sourceIndex = recallPreparationIndex--;
                AddRecallRecordIfRunnable(pathRecords[sourceIndex], sourceIndex, true);
                continue;
            }

            if (!recallReverseMode && recallPreparationIndex < pathRecords.Count)
            {
                int sourceIndex = recallPreparationIndex++;
                AddRecallRecordIfRunnable(pathRecords[sourceIndex], Math.Min(sourceIndex + 1, pathRecords.Count), false);
                continue;
            }

            if (recallPreparationAnchorPending)
            {
                recallPreparationAnchorPending = false;
                AddRecallRecord(FormatPathPointLine(pathAnchorPos, pathAnchorPos, pathAnchorForward, pathAnchorUp), 0);
                continue;
            }

            if (recallPreparationDockPending)
            {
                recallPreparationDockPending = false;
                AddRecallRecord("dock", 0);
                continue;
            }

            CompleteRecallPreparation();
        }
    }
}

void CompleteRecallPreparation()
{
    recallPreparing = false;
    recallPreparationPhase = RecallPreparationPhase.None;
    if (recallReverseMode)
    {
        autopilotEnabled = false;
        hasTarget = false;
        activeMaxSpeed = 0;
        ResetMovement();
    }

    fullStateDirty = true;
    ScriptLog("Recall route: " + (recallReverseMode ? "start" : "end")
        + " is shorter (start " + recallPreparationDistanceToStart.ToString("0")
        + " m, end " + recallPreparationDistanceToEnd.ToString("0")
        + " m). Recall started.");
}

void AddRecallEstimate(string rawRecord, bool reverseTraversal, ref Vector3D previous, ref double distance)
{
    string record = NormalizePathRecord(rawRecord);
    // This already-budgeted backward pass sees the same nearest boundary as
    // IsRecallIndexInsideMiningBranch. Retain it instead of rescanning the
    // entire prefix at each preparation phase transition.
    if (reverseTraversal && recallPreparationPhase == RecallPreparationPhase.EstimateStart
        && !recallPreparationStartBranchKnown)
    {
        bool drillsOn = record.Equals("drillson", StringComparison.OrdinalIgnoreCase);
        if (drillsOn || record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase)
            || record.Equals("dock", StringComparison.OrdinalIgnoreCase)
            || record.Equals("undock", StringComparison.OrdinalIgnoreCase))
        {
            recallPreparationStartBranchKnown = true;
            recallPreparationStartsInsideBranch = drillsOn;
        }
    }
    if (ShouldSkipRecallMiningBranchRecord(record, reverseTraversal))
        return;
    if (string.IsNullOrWhiteSpace(record) || IsPathMetadataRecord(record) || record.Equals("undock", StringComparison.OrdinalIgnoreCase) || record.Equals("drillson", StringComparison.OrdinalIgnoreCase))
        return;
    if (record.Equals("dock", StringComparison.OrdinalIgnoreCase))
    {
        if (pathAnchorValid)
            AddRecallEstimatePosition(pathAnchorPos, ref previous, ref distance);
        return;
    }

    Vector3D pos, fwd, up;
    if (LineLooksLikeMineArea(record))
    {
        Vector3D cornerA, cornerB;
        double depth;
        if (pathAnchorValid && TryParseMineAreaLine(record, pathAnchorPos, out cornerA, out cornerB, out up, out fwd, out depth))
            AddRecallEstimatePosition(GetInferredMineAreaEntryPose(cornerA, cornerB), ref previous, ref distance);
        return;
    }

    string navCmd = null;
    if (record.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase))
        navCmd = record.Substring(4).Trim();
    else if (record.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        navCmd = record;
    if (navCmd != null)
    {
        if (TryParseGpsCommand(navCmd, out pos, out fwd, out up))
            AddRecallEstimatePosition(pos, ref previous, ref distance);
        return;
    }

    if (record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase))
        record = record.Substring(8).Trim();
    if (pathAnchorValid && TryParsePathPointLine(record, pathAnchorPos, out pos, out fwd, out up))
        AddRecallEstimatePosition(pos, ref previous, ref distance);
}

void AddRecallEstimatePosition(Vector3D position, ref Vector3D previous, ref double distance)
{
    distance += Vector3D.Distance(previous, position);
    previous = position;
}

void BuildRecallRecords()
{
    recallRecords.Clear();
    recallDisplayDone.Clear();
    recallIndex = 0;
    recallLastTotal = pathRecords.Count;
    int current = Math.Max(0, Math.Min(pathIndex, pathRecords.Count));
    if (recallReverseMode)
    {
        ResetRecallMiningBranchFilter(true, current);
        for (int i = current - 1; i >= 0; i--)
            AddRecallRecordIfRunnable(pathRecords[i], i, true);
        if (pathAnchorValid)
            AddRecallRecord(FormatPathPointLine(pathAnchorPos, pathAnchorPos, pathAnchorForward, pathAnchorUp), 0);
        AddRecallRecord("dock", 0);
        return;
    }

    ResetRecallMiningBranchFilter(false, current);
    for (int i = current; i < pathRecords.Count; i++)
        AddRecallRecordIfRunnable(pathRecords[i], Math.Min(i + 1, pathRecords.Count), false);
}

void AddRecallRecordIfRunnable(string rawRecord, int displayDone, bool reverseTraversal)
{
    string record = NormalizePathRecord(rawRecord);
    if (ShouldSkipRecallMiningBranchRecord(record, reverseTraversal))
        return;
    if (string.IsNullOrWhiteSpace(record))
        return;
    if (IsPathMetadataRecord(record))
        return;
    if (LineLooksLikeMineArea(record) && pathAnchorValid)
    {
        Vector3D cornerA, cornerB, areaUp, areaForward;
        double depth;
        if (TryParseMineAreaLine(record, pathAnchorPos, out cornerA, out cornerB, out areaUp, out areaForward, out depth))
            AddRecallRecord(FormatPathPointLine(GetInferredMineAreaEntryPose(cornerA, cornerB), pathAnchorPos, areaForward, areaUp), displayDone);
        return;
    }

    if (record.Equals("undock", StringComparison.OrdinalIgnoreCase) || record.Equals("drillson", StringComparison.OrdinalIgnoreCase) || record.StartsWith("minearea;", StringComparison.OrdinalIgnoreCase))
        return;
    AddRecallRecord(record, displayDone);
}

void ResetRecallPreparationMiningBranchFilter(bool reverseTraversal)
{
    recallPreparationSkippingMiningBranch = !reverseTraversal && recallPreparationStartsInsideBranch;
    recallPreparationKeepMiningBranchExit = recallPreparationSkippingMiningBranch;
}

void ResetRecallMiningBranchFilter(bool reverseTraversal, int currentIndex)
{
    recallPreparationSkippingMiningBranch = false;
    recallPreparationKeepMiningBranchExit = false;
    // Forward recall can begin after the current plane's minearea record but
    // before its reverse withdrawal. Skip the already-finished approach while
    // retaining that one withdrawal so the Miner still reaches safe staging.
    if (!reverseTraversal && IsRecallIndexInsideMiningBranch(currentIndex))
    {
        recallPreparationSkippingMiningBranch = true;
        recallPreparationKeepMiningBranchExit = true;
    }
}

bool IsRecallIndexInsideMiningBranch(int currentIndex)
{
    int start = Math.Max(0, Math.Min(currentIndex, pathRecords.Count)) - 1;
    for (int i = start; i >= 0; i--)
    {
        string record = NormalizePathRecord(pathRecords[i]);
        if (record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase))
            return false;
        if (record.Equals("drillson", StringComparison.OrdinalIgnoreCase))
            return true;
        if (record.Equals("dock", StringComparison.OrdinalIgnoreCase) || record.Equals("undock", StringComparison.OrdinalIgnoreCase))
            return false;
    }

    return false;
}

bool ShouldSkipRecallMiningBranchRecord(string record, bool reverseTraversal)
{
    if (string.IsNullOrWhiteSpace(record))
        return false;
    bool drillsOn = record.Equals("drillson", StringComparison.OrdinalIgnoreCase);
    bool withdrawal = record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase);
    bool routeBoundary = record.Equals("dock", StringComparison.OrdinalIgnoreCase) || record.Equals("undock", StringComparison.OrdinalIgnoreCase);
    if (reverseTraversal)
    {
        // Travelling toward the route start encounters the withdrawal staging
        // point first. Keep that safe-spine point, then discard the minearea and
        // its approach until the matching drills-on marker is reached.
        if (recallPreparationSkippingMiningBranch)
        {
            if (drillsOn || routeBoundary)
                recallPreparationSkippingMiningBranch = false;
            return !routeBoundary;
        }

        if (withdrawal)
        {
            recallPreparationSkippingMiningBranch = true;
            return false;
        }

        return drillsOn;
    }

    if (recallPreparationSkippingMiningBranch)
    {
        if (withdrawal)
        {
            bool keepExit = recallPreparationKeepMiningBranchExit;
            recallPreparationSkippingMiningBranch = false;
            recallPreparationKeepMiningBranchExit = false;
            return !keepExit;
        }

        if (routeBoundary)
        {
            recallPreparationSkippingMiningBranch = false;
            recallPreparationKeepMiningBranchExit = false;
            return false;
        }

        return true;
    }

    if (drillsOn)
    {
        recallPreparationSkippingMiningBranch = true;
        recallPreparationKeepMiningBranchExit = false;
        return true;
    }

    return false;
}

void AddRecallRecord(string record, int displayDone)
{
    recallRecords.Add(record);
    recallDisplayDone.Add(Math.Max(0, Math.Min(displayDone, recallLastTotal)));
}

void StartNextRecallTarget()
{
    while (recallIndex < recallRecords.Count)
    {
        string record = recallRecords[recallIndex];
        int displayDone = recallIndex < recallDisplayDone.Count ? recallDisplayDone[recallIndex] : recallLastDone;
        recallIndex++;
        recallLastDone = displayDone;
        if (ExecuteRecallRecord(record))
            return;
    }

    CompleteRecallPath();
}

bool ExecuteRecallRecord(string rawRecord)
{
    string record = NormalizePathRecord(rawRecord);
    if (string.IsNullOrWhiteSpace(record))
        return false;
    if (record.Equals("dock", StringComparison.OrdinalIgnoreCase))
    {
        SendRecallProgress(recallLastDone, recallLastTotal);
        if (IsMinerDocked() || IsMergerDocked())
        {
            CompleteRecallPath();
            return true;
        }

        StartDock();
        recallWaitingForDock = true;
        return true;
    }

    Vector3D pos, fwd, up;
    string navCmd = null;
    if (record.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase))
        navCmd = record.Substring(4).Trim();
    else if (record.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        navCmd = record;
    if (navCmd != null)
    {
        if (!TryParseGpsCommand(navCmd, out pos, out fwd, out up))
            return false;
        targetPosition = pos;
        // Recall route travel uses the same natural facing as ordinary playback:
        // point the miner along its direction of travel instead of holding the
        // mining-plane pose and translating sideways.
        desiredForward = Vector3D.Zero;
        desiredUp = Vector3D.Zero;
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = 0;
        return true;
    }

    if (record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase))
        record = record.Substring(8).Trim();
    if (pathAnchorValid && TryParsePathPointLine(record, pathAnchorPos, out pos, out fwd, out up))
    {
        targetPosition = pos;
        desiredForward = Vector3D.Zero;
        desiredUp = Vector3D.Zero;
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = 0;
        return true;
    }

    return false;
}

string NormalizePathRecord(string rawRecord)
{
    if (string.IsNullOrWhiteSpace(rawRecord))
        return "";
    string record = rawRecord.Trim();
    if (record.StartsWith("\"") && record.EndsWith("\"") && record.Length >= 2)
        record = record.Substring(1, record.Length - 2).Trim();
    return record;
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
    IGC.SendBroadcastMessage(minerPathCompleteTag, "RecallComplete|" + activeMissionId + "|1");
    ScriptLog("Recall complete.");
}

void ClearRecallState()
{
    recallActive = false;
    recallWaitingForAreaExit = false;
    recallWaitingForDock = false;
    recallReverseMode = false;
    recallPreparing = false;
    recallPreparationPhase = RecallPreparationPhase.None;
    recallPreparationIndex = 0;
    recallPreparationAnchorPending = false;
    recallPreparationDockPending = false;
    recallPreparationSkippingMiningBranch = false;
    recallPreparationKeepMiningBranchExit = false;
    recallRecords.Clear();
    recallDisplayDone.Clear();
    recallIndex = 0;
    recallLastDone = 0;
    recallLastTotal = 0;
}

void SendRecallProgress(int done, int total)
{
    done = Math.Max(0, done);
    total = Math.Max(0, total);
    if (total > 0)
        done = Math.Min(done, total);
    IGC.SendBroadcastMessage(minerProgressTag, "progress:" + done + "/" + total);
}
