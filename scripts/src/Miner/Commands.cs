void RemoteCommand(string command)
{
    if (string.IsNullOrWhiteSpace(command))
        return;
    string trimmed = command.Trim();
    if (trimmed.StartsWith("PluginFeatures|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginFeatures(trimmed);
        return;
    }
    if (trimmed.StartsWith("PluginPower|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginPowerResult(trimmed);
        return;
    }
    if (trimmed.StartsWith("PluginUnload|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginUnloadResult(trimmed);
        return;
    }
    if (trimmed.StartsWith("PluginRemoteStatus|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginRemoteStatus(trimmed);
        return;
    }
    if (trimmed.StartsWith("PluginDockPose|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginDockPose(trimmed);
        return;
    }
    if (trimmed.StartsWith("PluginDockHardware|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginDockHardware(trimmed);
        return;
    }
    string baseCommand, missionId;
    SplitMissionCommand(trimmed, out baseCommand, out missionId);
    // New control intent must never be undone by a route prepared earlier.
    if (baseCommand.Equals("pause", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("stop", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("reset", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("recall", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("pathpause", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("dock", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("undock", StringComparison.OrdinalIgnoreCase)
        || baseCommand.Equals("cancelmine", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("Hangar", StringComparison.OrdinalIgnoreCase))
        CancelPathLoad();
    if (trimmed.StartsWith("HangarStore|", StringComparison.OrdinalIgnoreCase))
    {
        BeginHangarStorage(trimmed);
        return;
    }

    if (trimmed.StartsWith("HangarCycleReturnDock|", StringComparison.OrdinalIgnoreCase))
    {
        BeginHangarCycleReturnDock(trimmed);
        return;
    }

    if (trimmed.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("pathrecord", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("pathclear", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("appendpath", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("adjust corner", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("minearea;", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("minelane;", StringComparison.OrdinalIgnoreCase))
    {
        ScriptLog("EMM-A 3: manual path recording/navigation was replaced by the plugin planner.");
        return;
    }

    if (trimmed.StartsWith("SPEEDF ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 7, ref SPEEDF, "SPEEDF");
        return;
    }

    if (trimmed.StartsWith("SPEEDB ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 7, ref SPEEDB, "SPEEDB");
        return;
    }

    if (trimmed.StartsWith("MINEH ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 6, ref MINEH, "MINEH", "m");
        return;
    }

    if (trimmed.StartsWith("MINEW ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 6, ref MINEW, "MINEW", "m");
        return;
    }

    if (trimmed.StartsWith("AUTOSERVICE ", StringComparison.OrdinalIgnoreCase))
    {
        string mode = trimmed.Substring(12).Trim().ToUpperInvariant();
        if (mode == "ON" || mode == "TRUE" || mode == "1")
            autoServiceReturnEnabled = true;
        else if (mode == "OFF" || mode == "FALSE" || mode == "0")
            autoServiceReturnEnabled = false;
        else
        {
            ScriptLog("Invalid AUTOSERVICE. Use ON or OFF.");
            return;
        }

        ScriptLog("AUTOSERVICE " + (autoServiceReturnEnabled ? "ON" : "OFF"));
        return;
    }

    if (trimmed.StartsWith("AUTOCARGO ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPercentDouble(trimmed, 10, ref autoServiceCargoTriggerPercent, "AUTOCARGO");
        return;
    }

    if (trimmed.StartsWith("AUTOHYDRO ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPercentDouble(trimmed, 10, ref autoServiceHydrogenTriggerPercent, "AUTOHYDRO");
        return;
    }

    if (trimmed.StartsWith("ROUTESTARTEXTRA ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 16, ref minerRouteStartExtra, "ROUTESTARTEXTRA", "m");
        minerRouteStartExtra = MathHelper.Clamp(minerRouteStartExtra, 0, 500);
        fullStateDirty = true;
        return;
    }

    if (trimmed.StartsWith("TOFF ", StringComparison.OrdinalIgnoreCase))
    {
        Vector3D off;
        if (TryParseVector3(trimmed.Substring(5).Trim(), out off))
        {
            TOFFR = off.X;
            TOFFU = off.Y;
            TOFFF = off.Z;
            PublishMinerPlannerSettings();
            ScriptLog($"TOFF set. R/U/F = {TOFFR:0.00}/{TOFFU:0.00}/{TOFFF:0.00} m");
        }
        else
            ScriptLog("Invalid TOFF. Use: TOFF r,u,f");
        return;
    }

    switch (baseCommand.ToLower())
    {
        case "pause":
            CancelMinerUnload();
            ClearDockHardwareRequest();
            dockHardwareNativeFallback = false;
            pauseResumeAutopilot = autopilotEnabled;
            scriptPaused = true;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            StopMinerMovementNow();
            autopilotEnabled = false;
            SetIndicatorOffNow();
            ScriptLog("Paused.");
            break;
        case "resume":
        case "start":
            if (IsMinerDocked() && !oreTransferSucceeded) ScheduleOreTransfer();
            autopilotEnabled = scriptPaused ? pauseResumeAutopilot : hasTarget;
            scriptPaused = false;
            Runtime.UpdateFrequency = tickSpeed;
            pauseResumeAutopilot = false;
            RestoreDrillsAfterPause();
            WriteEcho();
            break;
        case "stop":
        case "reset":
            CancelUndockSequence();
            ClearDockHardwareRequest();
            dockHardwareNativeFallback = false;
            CancelMinerUnload();
            scriptPaused = false;
            pauseResumeAutopilot = false;
            Runtime.UpdateFrequency = tickSpeed;
            autopilotEnabled = false;
            hasTarget = false;
            targetPosition = Vector3D.Zero;
            desiredForward = Vector3D.Zero;
            desiredUp = Vector3D.Zero;
            miningEnabled = false;
            miningState = MiningState.Idle;
            areaMiningMode = false;
            areaLaneAnchors.Clear();
            areaLaneIndex = 0;
            areaLaneCount = 0;
            areaPlanValid = false;
            ClearAreaLaneSkipState();
            retryCurrentLane = false;
            stuckRetryCount = 0;
            finishAreaAfterBackout = false;
            returnToMinePoseAfterArea = false;
            activeMineAreaRecordIndex = -1;
            pathMode = false;
            pathPaused = false;
            pathPausedNavResume = false;
            pathDockPending = false;
            pathRecords.Clear();
            pathIndex = 0;
            routeProgressTracking = false;
            routeProgressPreviousValid = false;
            routeProgressMeters = 0;
            routeProgressTotalMeters = 0;
            pathAnchorValid = false;
            activeMissionId = "";
            ClearRecallState();
            ClearAutoServiceState();
            hangarState = HangarState.None;
            hangarLaunchUndocked = false;
            hangarBroadcastTicks = 0;
            hangarOperationId = 0;
            hangarPhaseElapsedTicks = 0;
            ClearStoneEjectionHold();
            oreTransferActive = false;
            oreTransferStartDelayTicks = 0;
            dockingState = DockingState.None;
            CancelMinerPowerTransition();
            dockingPhaseElapsedTicks = 0;
            dockingAttemptFailures = 0;
            ClearDockRetry();
            activeMaxSpeed = 0;
            SetDrills(false);
            StopMinerMovementNow();
            SetIndicatorOffNow();
            WriteEcho();
            if (trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
                ScriptLog("Stopped.");
            break;
        case "dumpgps":
            DumpCurrentGpsToCustomData();
            break;
        case "cancelmine":
            miningEnabled = false;
            miningState = MiningState.Idle;
            areaMiningMode = false;
            areaLaneAnchors.Clear();
            areaLaneIndex = 0;
            areaLaneCount = 0;
            areaPlanValid = false;
            ClearAreaLaneSkipState();
            retryCurrentLane = false;
            finishAreaAfterBackout = false;
            returnToMinePoseAfterArea = false;
            activeMineAreaRecordIndex = -1;
            autopilotEnabled = false;
            hasTarget = false;
            activeMaxSpeed = 0;
            SetDrills(false);
            ScriptLog("Mining cancelled.");
            break;
        case "nexthole":
            NextHole(false);
            break;
        case "nexthole all":
            NextHole(true);
            break;
        case "recall":
            StartRecallPath(missionId);
            break;
        case "pathstart":
            StartPathFromTimer(missionId);
            break;
        case "pathpause":
            if (recallActive)
            {
                ScriptLog("Cannot pause while recall is active.");
                break;
            }

            if (!pathMode)
                ScriptLog("Path mode is not active.");
            else
            {
                pathPaused = true;
                if (pathDrillApproachActive)
                    SetDrills(false);
                if (miningEnabled)
                {
                    SetDrills(false);
                    retryCurrentLane = true;
                    miningState = MiningState.Backout;
                    autopilotEnabled = true; // return to anchor
                    ScriptLog("Path mode paused. Returning to current anchor.");
                }
                else if (autopilotEnabled && hasTarget)
                {
                    autopilotEnabled = false; // freeze movement in place
                    pathPausedNavResume = true;
                    ScriptLog("Path mode paused. Movement halted.");
                }
                else
                {
                    ScriptLog("Path mode paused.");
                }
            }

            break;
        case "pathcontinue":
            if (!pathMode)
                ScriptLog("Path mode is not active.");
            else
            {
                pathPaused = false;
                if (pathDrillApproachActive)
                    SetDrills(true);
                ScriptLog("Path mode resumed.");
                if (!miningEnabled && miningState != MiningState.Idle)
                {
                    // Mining was parked at anchor waiting for pathcontinue — re-enable it
                    miningEnabled = true;
                    autopilotEnabled = true;
                }
                else if (miningEnabled && hasTarget)
                    autopilotEnabled = true;
                else if (pathPausedNavResume && hasTarget)
                {
                    autopilotEnabled = true;
                    pathPausedNavResume = false;
                }
                else if (!autopilotEnabled && !miningEnabled && !pendingUndock && dockingState == DockingState.None)
                    StartNextPathRecord();
            }

            break;
        case "pathstop":
            StopPathExecution();
            ScriptLog("Path mode stopped.");
            break;
        case "dock":
            StartManualDock();
            break;
        case "undock":
            StartUndock();
            break;
        case "toffreset":
            TOFFR = 0;
            TOFFU = 0;
            TOFFF = 5;
            PublishMinerPlannerSettings();
            ScriptLog("TOFF reset to 0,0,0.");
            break;
    }
}

void StartManualDock()
{
    CancelUndockSequence();
    dockingState = DockingState.None;
    ResetDockingHandshake();
    ClearRecallState();
    ClearAutoServiceState();
    ClearStoneEjectionHold();
    pathMode = false;
    pathPaused = false;
    pathPausedNavResume = false;
    pathDrillApproachActive = false;
    pathDockPending = false;
    pathCompleteBroadcastTicks = 0;
    miningEnabled = false;
    miningState = MiningState.Idle;
    areaMiningMode = false;
    areaLaneAnchors.Clear();
    areaLaneIndex = 0;
    areaLaneCount = 0;
    areaPlanValid = false;
    ClearAreaLaneSkipState();
    retryCurrentLane = false;
    finishAreaAfterBackout = false;
    returnToMinePoseAfterArea = false;
    activeMineAreaRecordIndex = -1;
    SetDrills(false);
    StartDock();
}

bool TryGetMineAreaBasis(Vector3D forward, Vector3D up, out Vector3D forwardN, out Vector3D upN, out Vector3D rightN, out Vector3D downN)
{
    forwardN = Vector3D.Zero;
    upN = Vector3D.Zero;
    rightN = Vector3D.Zero;
    downN = Vector3D.Zero;
    if (forward.LengthSquared() <= 0.001 || up.LengthSquared() <= 0.001)
        return false;
    forwardN = Vector3D.Normalize(forward);
    upN = Vector3D.Normalize(up);
    rightN = Vector3D.Cross(forwardN, upN);
    if (rightN.LengthSquared() <= 0.001)
        return false;
    rightN = Vector3D.Normalize(rightN);
    downN = -upN;
    return true;
}

void CanonicalizeMineAreaCorners(ref Vector3D cornerA, ref Vector3D cornerB, Vector3D forward, Vector3D up)
{
    Vector3D forwardN, upN, right, down;
    if (!TryGetMineAreaBasis(forward, up, out forwardN, out upN, out right, out down))
    {
        ProjectMineAreaToShallowPlane(ref cornerA, ref cornerB, forward);
        return;
    }

    ProjectMineAreaToShallowPlane(ref cornerA, ref cornerB, forwardN);
    double aRight = Vector3D.Dot(cornerA, right);
    double bRight = Vector3D.Dot(cornerB, right);
    double aDown = Vector3D.Dot(cornerA, down);
    double bDown = Vector3D.Dot(cornerB, down);
    double minRight = Math.Min(aRight, bRight);
    double maxRight = Math.Max(aRight, bRight);
    double minDown = Math.Min(aDown, bDown);
    double maxDown = Math.Max(aDown, bDown);
    Vector3D origin = cornerA + right * (minRight - aRight) + down * (minDown - aDown);
    cornerA = origin;
    cornerB = origin + right * (maxRight - minRight) + down * (maxDown - minDown);
}

Vector3D GetInferredMineAreaEntryPose(Vector3D cornerA, Vector3D cornerB)
{
    return (cornerA + cornerB) * 0.5;
}

// SE-style GPS string for human copy-paste (used by `dumpgps` only). Persistent records use
// the compact anchor+delta format instead.
int GetIndicatorDelayTicks()
{
    return Math.Max(1, (int)Math.Ceiling(indicatorLightDelaySeconds / Math.Max(0.001, secondsPerTick)));
}

void UpdateIndicatorLight()
{
    if (indicatorLight == null)
        return;
    bool onMiningPlane = areaMiningMode;
    bool desiredOn = autopilotEnabled || onMiningPlane;
    Color desiredOnColor = onMiningPlane ? indicatorMiningPlaneColor : indicatorAutopilotColor;
    if (desiredOn)
    {
        if (indicatorTransition == IndicatorTransitionState.WaitNeutralAfterDisable)
        {
            indicatorTransition = IndicatorTransitionState.None;
            indicatorDelayTicksRemaining = 0;
        }

        if (!indicatorLight.Enabled)
        {
            if (indicatorTransition != IndicatorTransitionState.WaitEnableAfterColor)
            {
                SetIndicatorColor(desiredOnColor); // set color first
                indicatorDelayTicksRemaining = GetIndicatorDelayTicks();
                indicatorTransition = IndicatorTransitionState.WaitEnableAfterColor;
            }
            else
            {
                SetIndicatorColor(desiredOnColor);
                indicatorDelayTicksRemaining--;
                if (indicatorDelayTicksRemaining <= 0)
                {
                    SetIndicatorEnabled(true); // enable after delay
                    indicatorTransition = IndicatorTransitionState.None;
                }
            }
        }
        else
        {
            SetIndicatorColor(desiredOnColor);
            indicatorTransition = IndicatorTransitionState.None;
            indicatorDelayTicksRemaining = 0;
        }

        return;
    }

    if (indicatorTransition == IndicatorTransitionState.WaitEnableAfterColor)
    {
        indicatorTransition = IndicatorTransitionState.None;
        indicatorDelayTicksRemaining = 0;
    }

    if (indicatorLight.Enabled)
    {
        SetIndicatorEnabled(false); // off first
        indicatorDelayTicksRemaining = GetIndicatorDelayTicks();
        indicatorTransition = IndicatorTransitionState.WaitNeutralAfterDisable;
        return;
    }

    if (indicatorTransition == IndicatorTransitionState.WaitNeutralAfterDisable)
    {
        indicatorDelayTicksRemaining--;
        if (indicatorDelayTicksRemaining <= 0)
        {
            SetIndicatorColor(indicatorNeutralColor); // neutral after delay while off
            indicatorTransition = IndicatorTransitionState.None;
        }

        return;
    }

    SetIndicatorColor(indicatorNeutralColor);
}

void UpdateIndicatorLightThrottled()
{
    if (indicatorTransition == IndicatorTransitionState.None)
    {
        if (indicatorRefreshTicks > 0)
        {
            indicatorRefreshTicks--;
            return;
        }

        indicatorRefreshTicks = indicatorRefreshIntervalTicks - 1;
    }

    UpdateIndicatorLight();
}

bool IsMineAreaSkipMetaLine(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return false;
    string trimmed = line.Trim();
    return trimmed.StartsWith(mineAreaSkipMetaPrefix, StringComparison.OrdinalIgnoreCase);
}

bool TryParseMineAreaSkipMetaLine(
    string line,
    out double mineHeight,
    out double mineWidth,
    out int laneCount,
    List<int> skipLanes,
    List<double> laneDepths,
    List<List<DepthRange>> destructionRanges)
{
    mineHeight = 0;
    mineWidth = 0;
    laneCount = 0;
    skipLanes.Clear();
    laneDepths.Clear();
    destructionRanges.Clear();
    if (!IsMineAreaSkipMetaLine(line))
        return false;
    string trimmed = line.Trim();
    if (!TryParseCompactMineAreaSkipMetaPayload(trimmed.Substring(mineAreaSkipMetaPrefix.Length), out mineHeight, out mineWidth, out laneCount, skipLanes, laneDepths, destructionRanges))
        return false;
    SortLaneIndexes(skipLanes);
    return mineHeight > 0 && mineWidth > 0 && laneCount > 0;
}

bool TryParseCompactMineAreaSkipMetaPayload(
    string payload,
    out double mineHeight,
    out double mineWidth,
    out int laneCount,
    List<int> skipLanes,
    List<double> laneDepths,
    List<List<DepthRange>> destructionRanges)
{
    mineHeight = 0;
    mineWidth = 0;
    laneCount = 0;
    if (string.IsNullOrWhiteSpace(payload))
        return false;
    string[] parts = payload.Split(':');
    if (parts.Length < 1 || parts.Length > 4)
        return false;
    string[] header = parts[0].Split(',');
    if (header.Length != 3)
        return false;
    if (!TryParseNumber(header[0].Trim(), out mineHeight) || !TryParseNumber(header[1].Trim(), out mineWidth) || !int.TryParse(header[2].Trim(), out laneCount))
        return false;
    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
    {
        string[] laneParts = parts[1].Split(',');
        for (int i = 0; i < laneParts.Length; i++)
            if (!TryAddCompactLaneToken(laneParts[i].Trim(), skipLanes))
                return false;
    }

    if (parts.Length >= 3)
    {
        string[] depthParts = parts[2].Split(',');
        if (depthParts.Length == laneCount)
        {
            for (int i = 0; i < depthParts.Length; i++)
            {
                double depth;
                if (!TryParseNumber(depthParts[i].Trim(), out depth) || depth < 0)
                {
                    laneDepths.Clear();
                    break;
                }

                laneDepths.Add(depth);
            }
        }
    }

    if (parts.Length >= 4)
    {
        string[] laneParts = parts[3].Split('|');
        if (laneParts.Length == laneCount)
        {
            for (int i = 0; i < laneParts.Length; i++)
            {
                List<DepthRange> ranges = new List<DepthRange>();
                if (TryParseDestructionRanges(laneParts[i].Trim(), ranges))
                    destructionRanges.Add(ranges);
                else
                    destructionRanges.Add(new List<DepthRange>());
            }
        }
    }

    return mineHeight > 0 && mineWidth > 0 && laneCount > 0;
}

bool TryParseDestructionRanges(string token, List<DepthRange> ranges)
{
    ranges.Clear();
    if (string.IsNullOrWhiteSpace(token) || token == ".")
        return true;
    string[] parts = token.Split('+');
    for (int i = 0; i < parts.Length; i++)
    {
        string part = parts[i].Trim();
        int dash = part.IndexOf('-');
        if (dash <= 0)
            return false;
        double start;
        double end;
        if (!TryParseNumber(part.Substring(0, dash).Trim(), out start) || !TryParseNumber(part.Substring(dash + 1).Trim(), out end) || start < 0)
            return false;
        if (end <= start)
            continue;
        ranges.Add(new DepthRange(start, end));
    }

    return true;
}

bool TryAddCompactLaneToken(string token, List<int> lanes)
{
    if (string.IsNullOrWhiteSpace(token))
        return true;
    int dash = token.IndexOf('-');
    if (dash > 0)
    {
        int first;
        int last;
        if (!int.TryParse(token.Substring(0, dash).Trim(), out first) || !int.TryParse(token.Substring(dash + 1).Trim(), out last) || first <= 0 || last < first)
            return false;
        for (int lane = first; lane <= last; lane++)
            AddLaneIndexUnique(lanes, lane - 1);
        return true;
    }

    int oneBasedLane;
    if (!int.TryParse(token.Trim(), out oneBasedLane) || oneBasedLane <= 0)
        return false;
    AddLaneIndexUnique(lanes, oneBasedLane - 1);
    return true;
}

void ProjectMineAreaToShallowPlane(ref Vector3D cornerA, ref Vector3D cornerB, Vector3D forward)
{
    if (forward.LengthSquared() < 0.001)
        return;
    Vector3D fwd = Vector3D.Normalize(forward);
    double depthA = Vector3D.Dot(cornerA, fwd);
    double depthB = Vector3D.Dot(cornerB, fwd);
    double shallowDepth = Math.Min(depthA, depthB);
    cornerA -= fwd * (depthA - shallowDepth);
    cornerB -= fwd * (depthB - shallowDepth);
}

bool IsPathMetadataRecord(string line)
{
    return IsMineAreaSkipMetaLine(line);
}

bool LaneListContains(List<int> lanes, int laneIndex)
{
    for (int i = 0; i < lanes.Count; i++)
        if (lanes[i] == laneIndex)
            return true;
    return false;
}

void AddLaneIndexUnique(List<int> lanes, int laneIndex)
{
    if (laneIndex < 0 || LaneListContains(lanes, laneIndex))
        return;
    lanes.Add(laneIndex);
}

void SortLaneIndexes(List<int> lanes)
{
    lanes.Sort();
}

string FormatCurrentGps(string name)
{
    Vector3D p = _tickRcPosition;
    Vector3D f = Vector3D.Normalize(_tickRcMatrix.Forward);
    Vector3D u = Vector3D.Normalize(_tickRcMatrix.Up);
    return $"GPS:{name}:{p.X:0.00}:{p.Y:0.00}:{p.Z:0.00}:|{u.X:0.0000},{u.Y:0.0000},{u.Z:0.0000},{f.X:0.0000},{f.Y:0.0000},{f.Z:0.0000}";
}

bool MineAreaSkipMetaMatches(double mineHeight, double mineWidth, int laneCount)
{
    return Math.Abs(mineHeight - MINEH) < 0.01 && Math.Abs(mineWidth - MINEW) < 0.01 && laneCount == areaLaneCount;
}

void ReturnToMinePoseAfterArea()
{
    SetDrills(false);
    miningEnabled = false;
    miningState = MiningState.Idle;
    areaMiningMode = false;
    areaLaneAnchors.Clear();
    areaLaneIndex = 0;
    areaLaneCount = 0;
    areaPlanValid = false;
    finishAreaAfterBackout = false;
    targetPosition = areaMineEntryPos;
    desiredUp = areaMineEntryUp;
    desiredForward = areaMineEntryForward;
    hasTarget = true;
    autopilotEnabled = true;
    activeMaxSpeed = SPEEDB;
    returnToMinePoseAfterArea = false;
}

IMyTimerBlock FindPathTimer()
{
    if (_cachedPathTimer != null && _cachedPathTimer.CustomName.IndexOf(pathTimerTag, StringComparison.OrdinalIgnoreCase) >= 0)
        return _cachedPathTimer;
    foreach (var t in GetTimerBlocksCached())
        if (t != null && !string.IsNullOrWhiteSpace(t.CustomName) && t.CustomName.IndexOf(pathTimerTag, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _cachedPathTimer = t;
            return t;
        }

    _cachedPathTimer = null;
    return null;
}

void UpdatePathRunner()
{
    if (!pathMode || pathRecords.Count == 0)
        return;
    if (pathPaused)
        return;
    if (miningEnabled)
        return;
    if (dockingState != DockingState.None || pendingUndock)
        return;
    if (dockRetryPending)
        return;
    if (autopilotEnabled && hasTarget)
    {
        if (IsAtTargetPose(targetPosition, desiredForward, desiredUp))
        {
            autopilotEnabled = false;
            hasTarget = false;
            activeMaxSpeed = 0;
            if (pathDockPending)
            {
                pathDockPending = false;
                StartDock();
                return;
            }
        }

        return;
    }

    StartNextPathRecord();
}

void StopPathExecution()
{
    CancelPathLoad();
    CancelUndockSequence();
    ClearRecallState();
    ClearAutoServiceState();
    pathMode = false;
    pathPaused = false;
    pathPausedNavResume = false;
    pathDrillApproachActive = false;
    pathDockPending = false;
    pathRecords.Clear();
    pathIndex = 0;
    pathCompleteBroadcastTicks = 0;
    pathAnchorValid = false;
    miningEnabled = false;
    miningState = MiningState.Idle;
    areaMiningMode = false;
    areaLaneAnchors.Clear();
    areaLaneIndex = 0;
    areaLaneCount = 0;
    areaPlanValid = false;
    ClearAreaLaneSkipState();
    retryCurrentLane = false;
    finishAreaAfterBackout = false;
    returnToMinePoseAfterArea = false;
    activeMineAreaRecordIndex = -1;
    stuckRetryCount = 0;
    stuckBackout = false;
    SetDrills(false);
    dockingState = DockingState.None;
    if (IsMinerPowerTransitionActive())
    {
        CancelMinerUnload();
        CancelMinerPowerTransition();
    }
    ClearDockRetry();
    dockMergeReadyTicks = 0;
    dockConnectRetryTicks = 0;
    dockConnectRecompileTicksRemaining = 0;
    dockConnectRecompileArmed = false;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    StopMinerMovementNow();
}
