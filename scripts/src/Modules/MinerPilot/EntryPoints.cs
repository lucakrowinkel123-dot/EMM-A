
public Program()
{
    InitializeShipNames(false, true);
    // Every flight calculation uses this one explicitly named Remote Control.
    // Do not fall back to a cockpit or an arbitrary controller: a different
    // block position/orientation would change the Miner's navigation frame.
    rc = GetRequiredMinerRemoteControl();
    flightCache = new FlightDataCache(rc);
    RefreshTickPose();
    allGyros = GetBlocks<IMyGyro>().FindAll(b => HasMinerTag(b));
    allThrusters = GetBlocks<IMyThrust>().FindAll(b => HasMinerTag(b));
    flightCache.SetBlocks(allThrusters, allGyros);
    thrust = new ThrusterControl(flightCache, tickSpeed, allThrusters);
    gyros = new GyroControl(flightCache, tickSpeed, allGyros);
    drills = GetBlocks<IMyShipDrill>();
    antennas = GetBlocks<IMyRadioAntenna>();
    batteries = GetBlocks<IMyBatteryBlock>();
    hydrogenTanks = GetBlocks<IMyGasTank>();
    CacheMinerPowerBlocks();
    minerConnector = GetBlock<IMyShipConnector>(minerConnectorName, true) ?? GetBlock<IMyShipConnector>();
    carrierConnector = GetBlock<IMyShipConnector>(carrierConnectorName, true);
    minerMerger = GetBlock<IMyShipMergeBlock>(minerMergerName, true);
    carrierMerger = GetBlock<IMyShipMergeBlock>(carrierMergerName, true);
    indicatorLight = GetBlock<IMyLightingBlock>(indicatorLightName, true);
    miningGravityGenerator = GetBlock<IMyGravityGenerator>(miningGravityGeneratorName, true);
    CachePluginServiceIds();
    RefreshTimerCache();
    CacheNamedTimerReferences();
    carrierMinerSettingsTimer = GridTerminalSystem.GetBlockWithName(carrierMinerSettingsTimerName) as IMyTimerBlock;
    minerPluginInfoTimer = GridTerminalSystem.GetBlockWithName(minerPluginInfoTimerName) as IMyTimerBlock;
    dockListener = IGC.RegisterBroadcastListener(dockBroadcastTag + "_V2");
    UpdateConnectorOffsetLocal();
    if (tickSpeed == UpdateFrequency.Update10)
    {
        secondsPerTick = 10.0 / 60.0;
        echoFrequency = 10;
    }
    else if (tickSpeed == UpdateFrequency.Update100)
    {
        secondsPerTick = 100.0 / 60.0;
        echoFrequency = 1;
    }

    LoadState();
    InitializeModule();
    if (timerPersistenceEnabled && (_cachedStateIniTimer == null || _cachedStateRouteTimer == null))
        ScriptLog("WARNING: INI timer copies unavailable; Miner state still saves internally.");
    if (!mergeAssistedDocking) SetMergerEnabled(false);
    RebuildMiningPlaneIndex();
    UpdateMinerDockDemand();
    // Upgrade/recompile recovery uses a fresh session, retaining startup-cached IDs.
    bool resumeUnload = oreTransferActive || oreTransferStartDelayTicks > 0
        || autoServiceState == AutoServiceState.WaitingService
        || hangarState == HangarState.FinalService || hangarState == HangarState.PostDeployService
        || pathMode && pathIndex >= pathRecords.Count;
    oreTransferActive = false;
    oreTransferStartDelayTicks = 0;
    RecoverMinerPowerTransition();
    InitializeMinerSaveWork();
    if (resumeUnload && IsMinerDocked()) ScheduleOreTransfer();
    UpdateMiningGravityGenerator();
    if (miningGravityGenerator == null)
        ScriptLog("Mining gravity generator not found: '" + miningGravityGeneratorName + "'.");
    ScriptLog("Ready. Miner Pilot v0.8.56; flight diagnostics format 1.");
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}


public void Main(string argument, UpdateType updateSource)
{
    pbRuntimeAverage.Observe(Runtime.LifetimeTicks, Runtime.LastRunTimeMs);
    if (updateSource == UpdateType.Update100 || updateSource == UpdateType.Update10 || updateSource == UpdateType.Update1)
    {
        RefreshTickPose();
        TrackModuleDockGeneration();
        UpdateModule();
        if (!ModuleReady() || moduleRecoveryBlocked)
        {
            if (!moduleSafetyHold) { movementResetApplied = false; ResetMovement(); SetDrills(false); moduleSafetyHold = true; }
            runtime++; return;
        }
        if (moduleSafetyHold) { moduleSafetyHold = false; RestoreDrillsAfterPause(); }
        minerHeavyWorkClaimed = false;
        minerMissionTransitionThisTick = false;
        bool slowInactivePoll = runtime % igcPollIntervalTicks == 0;
        UpdateRuntimeMetrics();
        LogPreviousMinerSpikeTrace(minerRuntimeWorkMask);
        minerRuntimeWorkMask = 0;
        BeginRuntimeProfile();
        if (runtime % 3600 == 0)
            LogPreviousTickTrace();
        minerRuntimeWorkMask |= 1;
        var runtimeDetail = BeginRuntimeDetail();
        RefreshTickPose();
        EndRuntimeDetail(0, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateRouteMeterProgress();
        EndRuntimeDetail(4, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateDockPoseAge();
        EndRuntimeDetail(5, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateDockPoseFromBroadcast();
        EndRuntimeDetail(6, runtimeDetail);
        if (pathLoadJob != null) minerRuntimeWorkMask |= 4;
        runtimeDetail = BeginRuntimeDetail();
        PumpPathLoad();
        EndRuntimeDetail(9, runtimeDetail);
        MarkRuntimeProfileSection(0);
        // PathStart may temporarily wake a paused PB solely to prepare its new
        // route. Do not advance the old paused mining/docking state meanwhile.
        if (scriptPaused)
        {
            if (pathLoadJob == null)
                Runtime.UpdateFrequency = UpdateFrequency.Update1;
            MarkRuntimeProfileSection(1);
            MarkRuntimeProfileSection(2);
            MarkRuntimeProfileSection(3);
            MarkRuntimeProfileSection(4);
            runtime++;
            return;
        }
        if (runtime % echoFrequency == 0)
        {
            runtimeDetail = BeginRuntimeDetail();
            WriteEcho();
            EndRuntimeDetail(10, runtimeDetail);
        }
        if (!IsUndockBackgroundFrozen())
        {
            runtimeDetail = BeginRuntimeDetail();
            if (_minerSavePending) minerRuntimeWorkMask |= 8;
            PumpMinerSaveWork();
            EndRuntimeDetail(11, runtimeDetail);
            if (IsMinerRoutineSaveDue())
            {
                runtimeDetail = BeginRuntimeDetail();
                if (fullStateDirty)
                    RequestMinerSave(true);
                else
                {
                    if (HasVolatileProgressState() && HasMinerProgressStateChanged())
                        MarkProgressStateDirty();
                    RequestMinerSave(false);
                }
                EndRuntimeDetail(12, runtimeDetail);
            }
            runtimeDetail = BeginRuntimeDetail();
            UpdateScriptLogFlush();
            EndRuntimeDetail(13, runtimeDetail);
        }

        MarkRuntimeProfileSection(1);
        if (IsMinerPowerTransitionActive()) minerRuntimeWorkMask |= 16;
        runtimeDetail = BeginRuntimeDetail();
        UpdateMinerPowerTransition();
        EndRuntimeDetail(14, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        bool areaPlanWorkedThisTick = UpdateAreaPlanActivation();
        EndRuntimeDetail(15, runtimeDetail);
        if (miningEnabled && !stoneEjectionHoldActive && !IsMinerPowerTransitionActive())
        {
            runtimeDetail = BeginRuntimeDetail();
            UpdateMiningState();
            EndRuntimeDetail(16, runtimeDetail);
        }
        if (IsUndockSequenceActive() || dockingState != DockingState.None) minerRuntimeWorkMask |= 32;
        runtimeDetail = BeginRuntimeDetail();
        bool undockWorkedThisTick = UpdateUndockSequence();
        EndRuntimeDetail(17, runtimeDetail);
        if (!undockWorkedThisTick && dockingState != DockingState.None)
        {
            runtimeDetail = BeginRuntimeDetail();
            UpdateDockingState();
            EndRuntimeDetail(18, runtimeDetail);
        }
        runtimeDetail = BeginRuntimeDetail();
        if (dockRetryPending)
            UpdateDockRetry();
        EndRuntimeDetail(19, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateAutoServiceReturn();
        EndRuntimeDetail(20, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (hangarState != HangarState.None)
            UpdateHangarState();
        EndRuntimeDetail(21, runtimeDetail);
        MarkRuntimeProfileSection(2);
        bool actuatorOwned = IsMinerPowerTransitionActive() || undockWorkedThisTick || IsUndockSequenceActive() || areaPlanWorkedThisTick
            || areaPlanActivationStage != 0 || minerMissionTransitionThisTick;
        runtimeDetail = BeginRuntimeDetail();
        if (actuatorOwned)
        {
            // Power transitions own actuator writes until all blocks are safe.
            ResetMovement();
            EndRuntimeDetail(22, runtimeDetail);
        }
        else if (stoneEjectionHoldActive)
        {
            ResetMovement();
            EndRuntimeDetail(22, runtimeDetail);
        }
        else
        {
            if (autopilotEnabled && hasTarget)
            {
                minerRuntimeWorkMask |= 64;
                Move();
                EndRuntimeDetail(23, runtimeDetail);
            }
            else
            {
                ResetMovement();
                EndRuntimeDetail(22, runtimeDetail);
            }
        }
        if (!actuatorOwned && !stoneEjectionHoldActive)
        {
            if (recallActive)
            {
                runtimeDetail = BeginRuntimeDetail();
                UpdateRecallPreparation();
                EndRuntimeDetail(24, runtimeDetail);
                runtimeDetail = BeginRuntimeDetail();
                UpdateRecallRunner();
                EndRuntimeDetail(25, runtimeDetail);
            }
            if (!recallActive && hangarState == HangarState.None && areaPlanActivationStage == 0)
            {
                runtimeDetail = BeginRuntimeDetail();
                UpdatePathRunner();
                EndRuntimeDetail(26, runtimeDetail);
            }
        }

        runtimeDetail = BeginRuntimeDetail();
        if (miningGravityGenerator != null && (!_lastMiningGravityEnabled.HasValue || _lastMiningGravityEnabled.Value != areaMiningMode))
            UpdateMiningGravityGenerator();
        EndRuntimeDetail(27, runtimeDetail);
        MarkRuntimeProfileSection(3);
        runtimeDetail = BeginRuntimeDetail();
        UpdateIndicatorLightThrottled();
        EndRuntimeDetail(31, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (routeProgressTracking && routeProgressTotalMeters > 0)
            BroadcastMinerProgress();
        EndRuntimeDetail(32, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdatePilotOperationCompletion();
        EndRuntimeDetail(33, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (minerDockDemand || slowInactivePoll)
            UpdateMinerDockDemand();
        EndRuntimeDetail(34, runtimeDetail);
        minerRuntimeWorkMask |= 256;
        runtimeDetail = BeginRuntimeDetail();
        PublishPluginServiceRequests();
        EndRuntimeDetail(36, runtimeDetail);
        MarkRuntimeProfileSection(4);
        runtime++;
    }
    else
    {
        BeginRuntimeProfile();
        RefreshTickPose();
        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase)) PilotEmergencyStop();
        UpdateMinerDockDemand();
        UpdateMiningGravityGenerator();
        MarkRuntimeProfileSection(2);
        if (!argument.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase))
            RequestMinerSave(true);
        MarkRuntimeProfileSection(1);
    }
}

void PilotRemoteCommand(string command)
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

    switch (baseCommand.ToLower())
    {
        case "pause":
            CancelMinerUnload();
            ClearDockHardwareRequest();
            dockHardwareNativeFallback = false;
            pauseResumeAutopilot = autopilotEnabled;
            scriptPaused = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
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
