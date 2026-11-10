void UpdateAutoServiceReturn()
{
    if (IsMinerPowerTransitionActive()) return;
    UpdateAutoServiceReturnRouteBuild();
    if (autoServiceDiagnosticTicks > 0)
        autoServiceDiagnosticTicks = Math.Max(0, autoServiceDiagnosticTicks - UpdateTickDelta());
    if (UpdateAutoServiceFuelCheck())
        return;
    if (stoneEjectionHoldActive)
    {
        if (recallActive || dockingState != DockingState.None || dockRetryPending || pathDockPending)
        {
            EndStoneEjectionHold(false, "Stone ejection hold interrupted.");
            return;
        }

        UpdateStoneEjectionHold();
        return;
    }

    if (autoServiceState != AutoServiceState.None)
    {
        UpdateAutoServiceState();
        return;
    }

    if (!ShouldCheckAutoServiceReturn())
        return;
    if (autoServiceCheckTicks > 0)
    {
        autoServiceCheckTicks = Math.Max(0, autoServiceCheckTicks - UpdateTickDelta());
        return;
    }

    autoServiceCheckTicks = Math.Max(1, autoServiceCheckIntervalTicks);
    double cargoPercent;
    double stonePercent;
    GetMinerCargoStats(out cargoPercent, out stonePercent);
    MaybeLogAutoServiceCheck(cargoPercent, stonePercent);
    if (cargoDetailsCurrent && cargoPercent >= stoneEjectionCargoTriggerPercent && stonePercent > stoneEjectionTriggerPercent)
    {
        StartStoneEjectionHold(cargoPercent, stonePercent);
        return;
    }

    if (cargoDetailsCurrent && cargoPercent >= autoServiceCargoTriggerPercent)
    {
        StartAutoServiceReturn("cargo " + cargoPercent.ToString("0.0") + "%");
        return;
    }

    if (autoServiceFuelSampleValid && autoServiceFuelPercent <= autoServiceHydrogenTriggerPercent)
        StartAutoServiceReturn("hydrogen " + autoServiceFuelPercent.ToString("0.0") + "%");
}

bool UpdateAutoServiceFuelCheck()
{
    bool activePathCheck = ShouldCheckAutoServiceReturn();
    bool returning = autoServiceState == AutoServiceState.Returning
        || autoServiceState == AutoServiceState.Undocking
        || autoServiceState == AutoServiceState.ReturningToWork;
    if ((!activePathCheck && !returning) || IsMinerDocked() || IsMergerDocked())
    {
        autoServiceFuelCheckTicks = 0;
        autoServiceFuelSampleValid = false;
        return false;
    }

    if (autoServiceFuelCheckTicks > 0)
    {
        autoServiceFuelCheckTicks = Math.Max(0, autoServiceFuelCheckTicks - UpdateTickDelta());
        return false;
    }

    RefreshMinerHydrogenCache();
    if (!HasMinerHydrogenTank())
    {
        autoServiceFuelSampleValid = false;
        autoServiceFuelCheckTicks = 900;
        return false;
    }

    autoServiceFuelPercent = GetMinerHydrogenPercent();
    autoServiceFuelSampleValid = true;
    autoServiceFuelCheckTicks = autoServiceFuelPercent >= 10.0 ? 900 : 300;
    if (!stopAtOnePercentFuel || autoServiceFuelPercent > 1.0)
        return false;

    StopPathExecution();
    ScriptLog("Auto service safety stop: fuel reached " + autoServiceFuelPercent.ToString("0.0") + "%.");
    return true;
}

void StartStoneEjectionHold(double cargoPercent, double stonePercent)
{
    if (stoneEjectionHoldActive)
        return;
    stoneEjectionHoldActive = true;
    stoneEjectionResumeAutopilot = autopilotEnabled && hasTarget;
    stoneEjectionHoldTicks = 0;
    stoneEjectionDetailCycleBaseline = cargoDetailCompletedCycles;
    ResetMinerCargoStatsScan();
    autopilotEnabled = false;
    SetDrills(false);
    ResetMovement();
    autoServiceCheckTicks = 0;
    fullStateDirty = true;
    ScriptLog("Stone ejection: holding position at cargo " + cargoPercent.ToString("0.0") + "%, stone " + stonePercent.ToString("0.0") + "%.");
}

void UpdateStoneEjectionHold()
{
    stoneEjectionHoldTicks += UpdateTickDelta();
    if (autoServiceCheckTicks > 0)
    {
        autoServiceCheckTicks = Math.Max(0, autoServiceCheckTicks - UpdateTickDelta());
        return;
    }

    autoServiceCheckTicks = Math.Max(1, autoServiceCheckIntervalTicks);
    double cargoPercent;
    double stonePercent;
    GetMinerCargoStats(out cargoPercent, out stonePercent);
    MaybeLogAutoServiceCheck(cargoPercent, stonePercent);
    if (cargoPercent < stoneEjectionCargoResumePercent)
    {
        EndStoneEjectionHold(true, "Stone ejection complete: cargo " + cargoPercent.ToString("0.0") + "%, stone " + stonePercent.ToString("0.0") + "%.");
        return;
    }

    if (cargoDetailsCurrent && cargoDetailCompletedCycles > stoneEjectionDetailCycleBaseline && stonePercent <= stoneEjectionResumePercent)
    {
        EndStoneEjectionHold(
            cargoPercent < autoServiceCargoTriggerPercent,
            "Stone ejection complete: cargo " + cargoPercent.ToString("0.0") + "%, stone " + stonePercent.ToString("0.0") + "%.");
        if (cargoPercent >= autoServiceCargoTriggerPercent)
            StartAutoServiceReturn("cargo " + cargoPercent.ToString("0.0") + "% after stone ejection");
        return;
    }

    if (stoneEjectionHoldTicks >= stoneEjectionMaxHoldTicks)
    {
        EndStoneEjectionHold(false, "Stone ejection timed out; returning for service.");
        StartAutoServiceReturn("stone ejection timeout at cargo " + cargoPercent.ToString("0.0") + "%");
    }
}

void EndStoneEjectionHold(bool resumeMovement, string message)
{
    bool shouldResume = resumeMovement && stoneEjectionResumeAutopilot && hasTarget;
    stoneEjectionHoldActive = false;
    stoneEjectionResumeAutopilot = false;
    stoneEjectionHoldTicks = 0;
    autopilotEnabled = shouldResume;
    if (shouldResume)
    {
        bool activeMiningLane = miningEnabled && (miningState == MiningState.DrillForward || miningState == MiningState.Backout);
        if (pathDrillApproachActive || activeMiningLane)
        {
            if (miningState == MiningState.DrillForward)
                UpdateDrillTerrainClearingMode();
            SetDrills(true);
            if (miningState == MiningState.DrillForward)
                ResetStuckMonitor();
            ScriptLog("Stone ejection resume restored drills for " + (pathDrillApproachActive ? "mining-plane approach." : miningState.ToString() + "."));
        }
    }

    fullStateDirty = true;
    if (!string.IsNullOrWhiteSpace(message))
        ScriptLog(message);
}

void ClearStoneEjectionHold()
{
    stoneEjectionHoldActive = false;
    stoneEjectionResumeAutopilot = false;
    stoneEjectionHoldTicks = 0;
}

bool ShouldCheckAutoServiceReturn()
{
    if (!autoServiceReturnEnabled || recallActive || dockingState != DockingState.None || dockRetryPending || pathDockPending || IsMinerDocked() || IsMergerDocked() || oreTransferStartDelayTicks > 0 || oreTransferActive)
        return false;
    if (pathMode && pathRecords.Count > 0 && !pathPaused)
        return true;
    return false;
}

void MaybeLogAutoServiceCheck(double cargoPercent, double stonePercent)
{
    if (autoServiceDiagnosticTicks > 0)
        return;
    if (cargoPercent < Math.Max(0, autoServiceCargoTriggerPercent - 10) && stonePercent <= Math.Max(0, stoneEjectionTriggerPercent - 0.5))
        return;
    double hydrogenPercent = autoServiceFuelSampleValid ? autoServiceFuelPercent : 100.0;
    ScriptLog("Auto service check: cargo " + cargoPercent.ToString("0.0")
        + "%, stone " + stonePercent.ToString("0.0")
        + "%, H2 " + hydrogenPercent.ToString("0.0")
        + "%, cycles " + cargoStatsCompletedCycles
        + ".");
    autoServiceDiagnosticTicks = 1200;
}

void StartAutoServiceReturn(string reason)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    if (!ShouldCheckAutoServiceReturn())
        return;
    autoServiceState = AutoServiceState.Returning;
    autoServiceReason = reason;
    autoServiceResumeMineArea = areaMiningMode && activeMineAreaRecordIndex >= 0 && activeMineAreaRecordIndex < pathRecords.Count;
    autoServiceResumeMineAreaRecordIndex = autoServiceResumeMineArea ? activeMineAreaRecordIndex : -1;
    autoServiceResumeAreaLaneIndex = autoServiceResumeMineArea ? Math.Max(0, areaLaneIndex) : 0;
    autoServiceResumeMineEntryPos = autoServiceResumeMineArea ? areaMineEntryPos : Vector3D.Zero;
    autoServiceResumeMineEntryForward = autoServiceResumeMineArea ? areaMineEntryForward : Vector3D.Zero;
    autoServiceResumeMineEntryUp = autoServiceResumeMineArea ? areaMineEntryUp : Vector3D.Zero;
    autoServiceActionRetryTicks = 0;
    autoServiceReturnRouteRecords.Clear();
    autoServiceReturnRouteIndex = 0;
    fullStateDirty = true;
    StartRecallPath("", true);
    if (!recallActive)
    {
        ClearAutoServiceState();
        return;
    }

    ScriptLog("Auto service: returning for " + autoServiceReason + ".");
    }
    finally { EndRuntimeDetail(44, detail81); }
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

        if (autoServiceReturnRouteBuildPending || oreTransferStartDelayTicks > 0 || oreTransferActive || !oreTransferSucceeded)
            return;
        double cargoPercent = GetMinerCargoFillPercent();
        double hydrogenPercent = HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 100.0;
        if (cargoPercent > autoServiceCargoResumePercent || hydrogenPercent < autoServiceHydrogenResumePercent)
            return;
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

void RetryAutoServiceDock()
{
    if (dockingState != DockingState.None || dockRetryPending)
        return;
    if (autoServiceActionRetryTicks > 0)
    {
        autoServiceActionRetryTicks = Math.Max(0, autoServiceActionRetryTicks - UpdateTickDelta());
        return;
    }

    autoServiceActionRetryTicks = autoServiceActionRetryIntervalTicks;
    StartDock();
}

void RetryAutoServiceUndock()
{
    if (dockingState != DockingState.None)
        return;
    if (autoServiceActionRetryTicks > 0)
    {
        autoServiceActionRetryTicks = Math.Max(0, autoServiceActionRetryTicks - UpdateTickDelta());
        return;
    }

    autoServiceActionRetryTicks = autoServiceActionRetryIntervalTicks;
    StartUndock();
    if (dockingState != DockingState.None || pendingUndock)
    {
        autoServiceState = AutoServiceState.Undocking;
        fullStateDirty = true;
        ScriptLog("Auto service: service complete, undocking.");
    }
}

void CompleteAutoServiceRecall()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    BeginAutoServiceReturnRouteBuild();
    SendRecallProgress(recallLastTotal, recallLastTotal);
    pathPaused = true;
    pathPausedNavResume = false;
    pathCompleteBroadcastTicks = 0;
    recallActive = false;
    recallWaitingForAreaExit = false;
    recallWaitingForDock = false;
    recallIndex = 0;
    autoServiceState = AutoServiceState.WaitingService;
    autoServiceActionRetryTicks = 0;
    fullStateDirty = true;
    if (oreTransferStartDelayTicks <= 0 && !oreTransferActive)
        ScheduleOreTransfer();
    ScriptLog("Auto service: docked, unloading and refueling.");
    }
    finally { EndRuntimeDetail(45, detail81); }
}

void BeginAutoServiceReturnRouteBuild()
{
    autoServiceReturnRouteRecords.Clear();
    autoServiceReturnRouteIndex = 0;
    autoServiceReturnRouteBuildIndex = recallRecords.Count - 1;
    autoServiceReturnRouteSkipAnchor = recallReverseMode;
    autoServiceReturnRouteBuildPending = true;
}

void UpdateAutoServiceReturnRouteBuild()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    if (!autoServiceReturnRouteBuildPending)
        return;
    if (autoServiceReturnRouteBuildIndex >= 0)
    {
        string record = NormalizePathRecord(recallRecords[autoServiceReturnRouteBuildIndex--]);
        if (string.IsNullOrWhiteSpace(record) || record.Equals("dock", StringComparison.OrdinalIgnoreCase) || IsPathMetadataRecord(record))
            return;
        if (autoServiceReturnRouteSkipAnchor)
        {
            autoServiceReturnRouteSkipAnchor = false;
            return;
        }

        autoServiceReturnRouteRecords.Add(record);
        return;
    }

    autoServiceReturnRouteBuildPending = false;
    recallRecords.Clear();
    recallDisplayDone.Clear();
    fullStateDirty = true;
    ScriptLog("Auto service: saved " + autoServiceReturnRouteRecords.Count + " safe return waypoints.");
    }
    finally { EndRuntimeDetail(46, detail81); }
}

void BuildAutoServiceReturnRoute()
{
    autoServiceReturnRouteRecords.Clear();
    autoServiceReturnRouteIndex = 0;
    int start = recallRecords.Count - 1;
    while (start >= 0 && NormalizePathRecord(recallRecords[start]).Equals("dock", StringComparison.OrdinalIgnoreCase))
        start--;
    // A recall toward the beginning appends the outer dock/path anchor immediately
    // before `dock`. After service, undocking already returns to that same pose,
    // so skip the duplicate before replaying the real route waypoints.
    if (recallReverseMode && start >= 0)
        start--;
    for (int i = start; i >= 0; i--)
    {
        string record = NormalizePathRecord(recallRecords[i]);
        if (string.IsNullOrWhiteSpace(record) || record.Equals("dock", StringComparison.OrdinalIgnoreCase) || IsPathMetadataRecord(record))
            continue;
        autoServiceReturnRouteRecords.Add(record);
    }

    ScriptLog("Auto service: saved " + autoServiceReturnRouteRecords.Count + " safe return waypoints.");
}

void StartNextAutoServiceReturnTarget()
{
    while (autoServiceReturnRouteIndex < autoServiceReturnRouteRecords.Count)
    {
        string record = autoServiceReturnRouteRecords[autoServiceReturnRouteIndex++];
        if (ExecuteAutoServiceReturnRecord(record))
        {
            MarkProgressStateDirty();
            return;
        }
    }

    CompleteAutoServiceReturnToWork();
}

bool ExecuteAutoServiceReturnRecord(string rawRecord)
{
    string record = NormalizePathRecord(rawRecord);
    if (string.IsNullOrWhiteSpace(record) || record.Equals("dock", StringComparison.OrdinalIgnoreCase) || record.Equals("undock", StringComparison.OrdinalIgnoreCase) || record.Equals("drillson", StringComparison.OrdinalIgnoreCase) || IsPathMetadataRecord(record))
        return false;
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
    }
    else
    {
        if (record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase))
            record = record.Substring(8).Trim();
        if (!pathAnchorValid || !TryParsePathPointLine(record, pathAnchorPos, out pos, out fwd, out up))
            return false;
    }

    targetPosition = pos;
    desiredForward = Vector3D.Zero;
    desiredUp = Vector3D.Zero;
    hasTarget = true;
    autopilotEnabled = true;
    activeMaxSpeed = 0;
    return true;
}

void CompleteAutoServiceReturnToWork()
{
    autoServiceState = AutoServiceState.None;
    autoServiceReason = "";
    autoServiceActionRetryTicks = 0;
    autoServiceReturnRouteRecords.Clear();
    autoServiceReturnRouteIndex = 0;
    pathPaused = false;
    pathPausedNavResume = false;
    if (autoServiceResumeMineArea && ResumeAutoServiceMineArea())
    {
        ClearAutoServiceResumeTarget();
        fullStateDirty = true;
        ScriptLog("Auto service: safe route complete; resumed excavation zone.");
        return;
    }

    ClearAutoServiceResumeTarget();
    fullStateDirty = true;
    ScriptLog("Auto service: safe route complete; resumed path.");
}

bool ResumeAutoServiceMineArea()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    if (!autoServiceResumeMineArea || autoServiceResumeMineAreaRecordIndex < 0 || autoServiceResumeMineAreaRecordIndex >= pathRecords.Count)
        return false;
    string record = NormalizePathRecord(pathRecords[autoServiceResumeMineAreaRecordIndex]);
    if (string.IsNullOrWhiteSpace(record))
        return false;
    activeMineAreaRecordIndex = autoServiceResumeMineAreaRecordIndex;
    int startLane = Math.Max(0, autoServiceResumeAreaLaneIndex);
    if (record.StartsWith("minearea;", StringComparison.OrdinalIgnoreCase))
    {
        if (!TryStartAreaMiningCommandFromLane(record, startLane))
            return false;
        RestoreAutoServiceMineEntry();
        return miningEnabled || autopilotEnabled || areaPlanActivationStage != 0;
    }

    if (!pathAnchorValid || !LineLooksLikeMineArea(record))
        return false;
    Vector3D cornerA, cornerB, areaUp, areaForward;
    double depth;
    if (!TryParseMineAreaLine(record, pathAnchorPos, out cornerA, out cornerB, out areaUp, out areaForward, out depth))
        return false;
    StartAreaMiningWithCornersFromLane(cornerA, cornerB, areaUp, areaForward, depth, startLane);
    RestoreAutoServiceMineEntry();
    return miningEnabled || autopilotEnabled || areaPlanActivationStage != 0;
    }
    finally { EndRuntimeDetail(47, detail81); }
}

void RestoreAutoServiceMineEntry()
{
    if (autoServiceResumeMineEntryForward.LengthSquared() <= 0.001 || autoServiceResumeMineEntryUp.LengthSquared() <= 0.001)
        return;
    areaMineEntryPos = autoServiceResumeMineEntryPos;
    areaMineEntryForward = autoServiceResumeMineEntryForward;
    areaMineEntryUp = autoServiceResumeMineEntryUp;
}

void ClearAutoServiceState()
{
    autoServiceState = AutoServiceState.None;
    autoServiceReason = "";
    autoServiceActionRetryTicks = 0;
    autoServiceReturnRouteRecords.Clear();
    autoServiceReturnRouteIndex = 0;
    autoServiceReturnRouteBuildPending = false;
    autoServiceReturnRouteSkipAnchor = false;
    autoServiceReturnRouteBuildIndex = -1;
    ClearAutoServiceResumeTarget();
}

void ClearAutoServiceResumeTarget()
{
    autoServiceResumeMineArea = false;
    autoServiceResumeMineAreaRecordIndex = -1;
    autoServiceResumeAreaLaneIndex = 0;
    autoServiceResumeMineEntryPos = Vector3D.Zero;
    autoServiceResumeMineEntryForward = Vector3D.Zero;
    autoServiceResumeMineEntryUp = Vector3D.Zero;
}

void BeginHangarStorage(string command)
{
    if (!TryApplyHangarPose(command))
    {
        ScriptLog("Hangar: rejected invalid storage pose.");
        return;
    }

    bool activeStorageOperation = hangarState == HangarState.FinalService
        || hangarState == HangarState.UndockingForStore
        || hangarState == HangarState.MovingToHangar
        || hangarState == HangarState.AwaitingStore;
    if (activeStorageOperation && parsedHangarOperationId > 0 && hangarOperationId > 0)
    {
        if (parsedHangarOperationId != hangarOperationId)
        {
            ScriptLog("Hangar: rejected a new storage operation while operation " + hangarOperationId + " is active.");
            return;
        }

        SendHangarProgress(hangarState == HangarState.AwaitingStore ? "store_ready" : "store_accepted");
        ScriptLog("Hangar: repeated storage command acknowledged without restarting the active operation.");
        return;
    }

    if (!IsMinerDocked() && !IsMergerDocked())
    {
        ScriptLog("Hangar: storage request rejected because Miner is not docked.");
        return;
    }

    CaptureHangarConnectorOrientation();
    if (parsedHangarOperationId > 0)
        hangarOperationId = parsedHangarOperationId;
    hangarState = HangarState.FinalService;
    hangarLaunchUndocked = false;
    hangarBroadcastTicks = 0;
    hangarPhaseElapsedTicks = 0;
    pathCompleteBroadcastTicks = 0;
    SetDrills(false);
    // A recompile while already connected can leave docked power settings unapplied.
    // Reassert them here so tanks stockpile and batteries recharge during final service.
    ApplyDockedPowerState();
    ScheduleOreTransfer();
    fullStateDirty = true;
    SendHangarProgress("store_accepted");
    ScriptLog("Hangar: finishing service; preserving the docked Miner orientation for storage.");
}

bool TryApplyHangarPose(string command)
{
    string[] parts = command.Split('|');
    Vector3D position, forward, up;
    double zoneHalfHeight;
    int operationId = 0;
    if ((parts.Length != 5 && parts.Length != 6) || !TryParseV3(parts[1], out position) || !TryParseV3(parts[2], out forward) || !TryParseV3(parts[3], out up) || !double.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out zoneHalfHeight) || zoneHalfHeight <= 0 || forward.LengthSquared() < 0.001 || up.LengthSquared() < 0.001)
        return false;
    if (parts.Length == 6 && (!int.TryParse(parts[5], out operationId) || operationId <= 0))
        return false;
    bool beginsNewOperation = parts[0].Equals("HangarStore", StringComparison.OrdinalIgnoreCase);
    if (!beginsNewOperation && operationId > 0 && hangarOperationId > 0 && operationId != hangarOperationId)
        return false;
    hangarTargetPosition = position;
    parsedHangarOperationId = operationId;
    parsedHangarZoneHalfHeight = zoneHalfHeight;
    fullStateDirty = true;
    return true;
}

void CaptureHangarConnectorOrientation()
{
    hangarTargetForward = Vector3D.Normalize(rc.WorldMatrix.Forward);
    hangarTargetUp = Vector3D.Normalize(rc.WorldMatrix.Up);
    fullStateDirty = true;
}

void BeginDeployedMissionCheck()
{
    hangarState = HangarState.PostDeployCheck;
    hangarPostDeployCargoCycleBaseline = cargoDetailCompletedCycles;
    ResetMinerCargoStatsScan();
    hangarBroadcastTicks = 0;
    hangarPhaseElapsedTicks = 0;
    fullStateDirty = true;
    SaveState();
    ScriptLog("Hangar: checking deployed Miner cargo and H2 before starting its loaded path.");
}

void BeginHangarCycleReturnDock(string command)
{
    if (hangarState != HangarState.AwaitingStore && hangarState != HangarState.MissionReady)
    {
        ScriptLog("HangarCycle return rejected: Miner is not freshly deployed from storage.");
        return;
    }

    if (!TryApplyHangarPose(command))
    {
        ScriptLog("HangarCycle return rejected: current storage-zone pose is invalid.");
        return;
    }

    if (parsedHangarOperationId > 0 && hangarOperationId > 0 && parsedHangarOperationId != hangarOperationId)
    {
        ScriptLog("HangarCycle return rejected: stale operation " + parsedHangarOperationId + ".");
        return;
    }

    if (parsedHangarOperationId > 0)
        hangarOperationId = parsedHangarOperationId;
    if (!StartHangarDockFromInnerPose())
    {
        ScriptLog("HangarCycle return paused: direct Dock Pose 1 approach could not start.");
        return;
    }

    hangarState = HangarState.PostDeployDocking;
    hangarLaunchUndocked = false;
    hangarBroadcastTicks = 0;
    fullStateDirty = true;
    ScriptLog("HangarCycle: returning directly to Dock Pose 1 and docking.");
}

void FinishHangarPostDeployCheck()
{
    RefreshMinerHydrogenCache();
    bool hasHydrogenTank = HasMinerHydrogenTank();
    bool needsService = HangarPostDeployNeedsService(
        cargoDetailsCurrent,
        _cachedCargoPercent,
        autoServiceCargoTriggerPercent,
        hasHydrogenTank,
        hasHydrogenTank ? GetMinerHydrogenPercent() : 100.0,
        autoServiceHydrogenTriggerPercent);
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

bool HangarPostDeployNeedsService(bool cargoSampleValid, double cargoPercent, double cargoThreshold, bool hasHydrogenTank, double hydrogenPercent, double hydrogenThreshold)
{
    return !cargoSampleValid
        || cargoPercent >= cargoThreshold
        || (hasHydrogenTank && hydrogenPercent <= hydrogenThreshold);
}
