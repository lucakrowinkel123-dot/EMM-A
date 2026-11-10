
public Program()
{
    InitializeShipNames(true, false);
    EnsureMinerSlots();
    List<string> missingAtBoot = GetMissingRequirements();
    if (missingAtBoot.Contains(carrierLcdName))
        ShowStartupScreen(missingAtBoot);
    if (missingAtBoot.Contains(carrierRemoteName))
    {
        ShowStartupScreen(missingAtBoot);
        throw new Exception("Missing required Carrier remote control.");
    }

    rc = GridTerminalSystem.GetBlockWithName(carrierRemoteName) as IMyRemoteControl;
    flightCache = new FlightDataCache(rc);
    RefreshTickPose();
    carrierConnector = GridTerminalSystem.GetBlockWithName(carrierConnectorName) as IMyShipConnector;
    InitializeCarrierUnloading();
    carrierConnectorLight = GridTerminalSystem.GetBlockWithName(carrierConnectorLightName) as IMyLightingBlock;
    UpdateCarrierConnectorLight();
    indicatorLight = GridTerminalSystem.GetBlockWithName(indicatorLightName) as IMyLightingBlock;
    carrierLcd = GridTerminalSystem.GetBlockWithName(carrierLcdName) as IMyTextPanel;
    ConfigureCarrierLcd();
    reportLcd = GridTerminalSystem.GetBlockWithName(Ca("LCD Report")) as IMyTextPanel;
    RefreshTimerCache();
    CacheNamedTimerReferences();
    RefreshCachedCarrierDimensions();
    carrierResourcesTimer = GridTerminalSystem.GetBlockWithName(carrierResourcesTimerName) as IMyTimerBlock;
    emma3BridgeTimer = GridTerminalSystem.GetBlockWithName(emma3BridgeTimerName) as IMyTimerBlock;
    carrierPluginInfoTimer = GridTerminalSystem.GetBlockWithName(carrierPluginInfoTimerName) as IMyTimerBlock;
    oreNameTimer = GridTerminalSystem.GetBlockWithName(oreNameTimerTag) as IMyTimerBlock;
    itemsTimer = GridTerminalSystem.GetBlockWithName(Ca("Items")) as IMyTimerBlock;
    hangarBlock = GridTerminalSystem.GetBlockWithName(hangarBlockName);
    hangarManagerTimer = GridTerminalSystem.GetBlockWithName(hangarManagerTimerName) as IMyTimerBlock;
    EnsureHangarManagerConfiguration();
    int initialHangarContents;
    TryGetFreshHangarInventoryCount(out initialHangarContents);
    emma3CompletedAsteroidsTimer = GridTerminalSystem.GetBlockWithName(emma3CompletedAsteroidsTimerName) as IMyTimerBlock;
    ActiveMiner().Pb = GridTerminalSystem.GetBlockWithName(ActiveMiner().PbName) as IMyProgrammableBlock;
    GridTerminalSystem.GetBlocksOfType<IMyShipController>(carrierControllers, c => c != null && c.CubeGrid != null && rc.CubeGrid != null && c.CubeGrid.EntityId == rc.CubeGrid.EntityId);
    if (carrierResourcesTimer == null)
        ScriptLog("WARNING: Missing '" + carrierResourcesTimerName + "' timer; Carrier resource telemetry is unavailable.");
    if (tickSpeed == UpdateFrequency.Update10)
        secondsPerTick = 10.0 / 60.0;
    else if (tickSpeed == UpdateFrequency.Update100)
        secondsPerTick = 100.0 / 60.0;
    LoadState();
    InitializeModule();
    if (timerPersistenceEnabled && (_cachedStateIniTimer == null || _cachedStateRouteTimer == null || _cachedStateReportsTimer == null))
        ScriptLog("WARNING: INI timer copies unavailable; Carrier state still saves internally.");
    InitializeCarrierSaveWork();
    PublishEmma3CarrierRouteCompletion();
    InitializeReportLcd();
    if (reportMission.Length == 0 && emma3MissionState == Emma3MissionState.WaitingMiner && emma3MissionId.Length > 0)
        StartAsteroidReport();
    if (reportLcd == null)
        ScriptLog("Optional report LCD not found: EMM-A Ca LCD Report.");
    MinerSlot miner = ActiveMiner();
    miner.PathCompleteListener = IGC.RegisterBroadcastListener(miner.CompleteTag);
    miner.ProgressListener = IGC.RegisterBroadcastListener(miner.ProgressTag);
    dockDemandListener = IGC.RegisterBroadcastListener(miner.CommandTag + "_DOCK_V2");
    UpdateEmma3BridgeStatus();
    LoadCarrierItems();
    if (emma3CompletedAsteroidsTimer == null)
        ScriptLog("WARNING: Missing '" + emma3CompletedAsteroidsTimerName + "' timer; completed asteroid IDs cannot persist.");
    ScriptLog("Ready. Carrier Commander v0.8.56.");
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}


public void Main(string argument, UpdateType updateSource)
{
    pbRuntimeAverage.Observe(Runtime.LifetimeTicks, Runtime.LastRunTimeMs);
    if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10 || updateSource == UpdateType.Update100)
    {
        UpdateModule();
        carrierHeavyWorkClaimed = false;
        carrierMissionTransitionThisTick = false;
        if (pluginLcdHeartbeatAgeTicks < int.MaxValue - UpdateTickDelta())
            pluginLcdHeartbeatAgeTicks += UpdateTickDelta();
        UpdateRuntimeMetrics();
        LogPreviousCarrierSpikeTrace(carrierRuntimeWorkMask);
        carrierRuntimeWorkMask = 0;
        BeginRuntimeProfile();
        if (runtime % 3600 == 0)
            LogPreviousTickTrace();
        carrierRuntimeWorkMask |= 1;
        long runtimeDetail = BeginRuntimeDetail();
        RefreshTickPose();
        EndRuntimeDetail(0, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateCarrierConnectorLightThrottled();
        EndRuntimeDetail(1, runtimeDetail);
        carrierRuntimeWorkMask |= 2;
        runtimeDetail = BeginRuntimeDetail();
        UpdateCarrierConnectorMassWatch();
        EndRuntimeDetail(2, runtimeDetail);
        carrierRuntimeWorkMask |= 4;
        runtimeDetail = BeginRuntimeDetail();
        UpdateCarrierDockPoses();
        EndRuntimeDetail(3, runtimeDetail);
        MinerSlot miner = ActiveMiner();
        if (miner.PathCompletePollTicks > 0)
            miner.PathCompletePollTicks--;
        else
        {
            miner.PathCompletePollTicks = igcPollIntervalTicks - 1;
            runtimeDetail = BeginRuntimeDetail();
            PollMinerPathCompletion();
            EndRuntimeDetail(4, runtimeDetail);
            if (miner.PathCompleteListener != null && miner.PathCompleteListener.HasPendingMessage)
                miner.PathCompletePollTicks = 0;
        }

        if (miner.ProgressPollTicks > 0 && emma3MissionState != Emma3MissionState.WaitingMinerStart)
            miner.ProgressPollTicks--;
        else
        {
            miner.ProgressPollTicks = minerProgressPollIntervalTicks - 1;
            runtimeDetail = BeginRuntimeDetail();
            PollMinerProgress();
            EndRuntimeDetail(5, runtimeDetail);
            if (miner.ProgressListener != null && miner.ProgressListener.HasPendingMessage)
                miner.ProgressPollTicks = 0;
        }

        MarkRuntimeProfileSection(0);
        if (_carrierFullSaveRequested && !_carrierSavePending)
        {
            runtimeDetail = BeginRuntimeDetail();
            RequestCarrierSave(true);
            EndRuntimeDetail(6, runtimeDetail);
        }
        else
        {
            if (_carrierSavePending) carrierRuntimeWorkMask |= 8;
            runtimeDetail = BeginRuntimeDetail();
            PumpCarrierSaveWork();
            EndRuntimeDetail(7, runtimeDetail);
        }
        if (progressSaveTicks > 0)
            progressSaveTicks--;
        else
        {
            progressSaveTicks = progressSaveIntervalTicks - 1;
            runtimeDetail = BeginRuntimeDetail();
            if (HasVolatileProgressState() && HasCarrierProgressStateChanged())
                MarkProgressStateDirty();
            RequestCarrierSave(false);
            EndRuntimeDetail(8, runtimeDetail);
        }

        MarkRuntimeProfileSection(1);
        MarkRuntimeProfileSection(2);
        if (!cockpitAbortPaused && !scriptPaused && ModuleReady() && !pilotRecoveryBlocked)
        {
            runtimeDetail = BeginRuntimeDetail();
            UpdateFullAutoEnableStationaryGate();
            EndRuntimeDetail(12, runtimeDetail);
        }
        if (emma3BridgeStatusTicks > 0)
            emma3BridgeStatusTicks--;
        else
        {
            emma3BridgeStatusTicks = emma3BridgeStatusIntervalTicks - 1;
            runtimeDetail = BeginRuntimeDetail();
            UpdateEmma3BridgeStatus();
            EndRuntimeDetail(13, runtimeDetail);
        }

        runtimeDetail = BeginRuntimeDetail();
        UpdateHangarManagerRefreshPoseResult();
        UpdateHangarManagerRefreshContentsResult();
        UpdateHangarManagerEnableResult();
        EndRuntimeDetail(14, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateHangarManagerStoreResult();
        UpdateHangarManagerDisableResult();
        EndRuntimeDetail(15, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateHangarManagerDeployResult();
        UpdateHangarInventorySnapshot();
        EndRuntimeDetail(16, runtimeDetail);
        if (!cockpitAbortPaused && !scriptPaused && ModuleReady() && !pilotRecoveryBlocked)
        {
            if (fullAutoEnabled || carrierPlanLoadJob != null) carrierRuntimeWorkMask |= 32;
            runtimeDetail = BeginRuntimeDetail();
            UpdateFullAuto();
            EndRuntimeDetail(17, runtimeDetail);
            runtimeDetail = BeginRuntimeDetail();
            UpdateCommanderPilotOperation();
            EndRuntimeDetail(18, runtimeDetail);
        }

        runtimeDetail = BeginRuntimeDetail();
        UpdateManualHangarCycle();
        EndRuntimeDetail(19, runtimeDetail);
        if (carrierUnloadRequest != null) carrierRuntimeWorkMask |= 64;
        runtimeDetail = BeginRuntimeDetail();
        UpdateCarrierUnloading();
        EndRuntimeDetail(20, runtimeDetail);
        if (reportRefreshTicks <= 0) carrierRuntimeWorkMask |= 128;
        runtimeDetail = BeginRuntimeDetail();
        UpdateMiningReports();
        EndRuntimeDetail(21, runtimeDetail);
        MarkRuntimeProfileSection(3);
        runtimeDetail = BeginRuntimeDetail();
        UpdateIndicatorLightThrottled();
        EndRuntimeDetail(22, runtimeDetail);
        if (ShouldUpdateCarrierInfoCache())
        {
            runtimeDetail = BeginRuntimeDetail();
            UpdateCarrierInfoCache();
            EndRuntimeDetail(23, runtimeDetail);
        }
        if (miner.StatusAgeSeconds < 9999)
            miner.StatusAgeSeconds += secondsPerTick;
        runtimeDetail = BeginRuntimeDetail();
        UpdateMinerRecallRequestTimeout();
        EndRuntimeDetail(24, runtimeDetail);
        if (miner.RecallFailedTicks > 0)
            miner.RecallFailedTicks--;
        runtimeDetail = BeginRuntimeDetail();
        int currentLcdFingerprint = GetCarrierLcdStateFingerprint();
        EndRuntimeDetail(25, runtimeDetail);
        if (currentLcdFingerprint != carrierLcdStateFingerprint)
        {
            lcdRefreshTicks = lcdRefreshIntervalTicks - 1;
            carrierRuntimeWorkMask |= 256;
            runtimeDetail = BeginRuntimeDetail();
            UpdateCarrierLcd();
            EndRuntimeDetail(26, runtimeDetail);
        }
        else if (lcdRefreshTicks > 0)
            lcdRefreshTicks--;
        else
        {
            lcdRefreshTicks = lcdRefreshIntervalTicks - 1;
            carrierRuntimeWorkMask |= 256;
            runtimeDetail = BeginRuntimeDetail();
            UpdateCarrierLcd();
            EndRuntimeDetail(26, runtimeDetail);
        }

        MarkRuntimeProfileSection(4);
        runtimeDetail = BeginRuntimeDetail();
        PublishPluginLcdPayload();
        EndRuntimeDetail(27, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateScriptLogFlush();
        EndRuntimeDetail(28, runtimeDetail);
        runtime++;
    }
    else
    {
        BeginRuntimeProfile();
        RefreshTickPose();
        CommanderRemoteCommand(argument);
        MarkRuntimeProfileSection(3);
        RequestCarrierSave(true);
        MarkRuntimeProfileSection(1);
        carrierLcdStateFingerprint = int.MinValue;
        MarkRuntimeProfileSection(4);
    }
}

void LegacyCommanderCommand(string command)
{
    if (string.IsNullOrWhiteSpace(command))
        return;
    string trimmed = command.Trim();
    if (trimmed.StartsWith("PluginUnloadReport|", StringComparison.OrdinalIgnoreCase))
    {
        HandlePluginUnloadReport(trimmed);
        return;
    }
    if (trimmed.Equals("report reset", StringComparison.OrdinalIgnoreCase))
    {
        ResetMiningReports();
        ScriptLog("Mining reports reset. Mission and completed-asteroid history preserved.");
        return;
    }
    if (TryHandleCarrierLcdCommand(trimmed))
        return;
    if (trimmed.StartsWith("loadasteroid ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("goto ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("mine ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("saveasteroid", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("updateasteroid", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("audit", StringComparison.OrdinalIgnoreCase))
    {
        ScriptLog("EMM-A 3: saved asteroid databases were replaced by the plugin planner.");
        return;
    }

    if (trimmed.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase))
    {
        string navArgs = trimmed.Substring(4).Trim();
        Vector3D pos, forward, up;
        if (!TryParseGpsCommand(navArgs, out pos, out forward, out up))
        {
            ScriptLog("Invalid Nav command. Use: Nav;GPS:Name:X:Y:Z:|Faceup,Faceforward");
            return;
        }

        StartPrecisionTarget(pos, forward, up);
        WriteEcho();
        return;
    }

    if (trimmed.StartsWith("Cruise;", StringComparison.OrdinalIgnoreCase))
    {
        string navArgs = trimmed.Substring(7).Trim();
        Vector3D pos, forward, up;
        if (!TryParseGpsCommand(navArgs, out pos, out forward, out up))
        {
            ScriptLog("Invalid Cruise command. Use: Cruise;GPS:Name:X:Y:Z:|Faceup,Faceforward");
            return;
        }

        StartCruiseTarget(pos, forward, up);
        WriteEcho();
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
            ScriptLog($"TOFF set. R/U/F = {TOFFR:0.00}/{TOFFU:0.00}/{TOFFF:0.00} m");
        }
        else
            ScriptLog("Invalid TOFF. Use: TOFF r,u,f");
        return;
    }

    if (trimmed.Equals("recall miner", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("recallminer", StringComparison.OrdinalIgnoreCase))
    {
        StartMinerRecall();
        return;
    }

    if (TryHandleOreNameStateCommand(trimmed))
        return;
    switch (trimmed.ToLower())
    {
        case "pause":
            pauseResumeAutopilot = autopilotEnabled;
            scriptPaused = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            ResetMovement();
            autopilotEnabled = false;
            SetIndicatorOffNow();
            ScriptLog("Paused.");
            break;
        case "resume":
        case "start":
            autopilotEnabled = scriptPaused ? pauseResumeAutopilot : hasTarget;
            scriptPaused = false;
            Runtime.UpdateFrequency = tickSpeed;
            pauseResumeAutopilot = false;
            WriteEcho();
            break;
        case "stop":
        case "clear":
        case "reset":
            CancelCarrierUnloading();
            if (trimmed.Equals("reset", StringComparison.OrdinalIgnoreCase)) ResetMiningReports();
            StopParkedReportClock();
            reportApproachSeconds = 0;
            finishAndHold = false;
            string cancelledHangarOperation = manualHangarCycleState == ManualHangarCycleState.None
                ? ""
                : GetManualHangarOperationName();
            ResetManualHangarCycleState();
            scriptPaused = false;
            pauseResumeAutopilot = false;
            Runtime.UpdateFrequency = tickSpeed;
            autopilotEnabled = false;
            hasTarget = false;
            targetPosition = Vector3D.Zero;
            desiredForward = Vector3D.Zero;
            desiredUp = Vector3D.Zero;
            carrierAutopilotMode = CarrierAutopilotMode.Precision;
            ResetMinerPathCompleteState();
            ResetMinerRecallState();
            mineStationaryTicks = 0;
            fullAutoEnabled = false;
            fullAutoEnableStationaryTicks = 0;
            pathMode = false;
            pathPaused = false;
            pathPausedNavResume = false;
            pathRecords.Clear();
            pathIndex = 0;
            pathAnchorValid = false;
            activeMaxSpeed = 0;
            ResetMovement();
            SetIndicatorOffNow();
            if (cancelledHangarOperation.Length > 0)
            {
                SendMinerCommand("reset");
                TriggerHangarManagerCommand("Disable");
                ScriptLog(cancelledHangarOperation + " cancelled by Carrier reset.");
            }

            WriteEcho();
            if (trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
                ScriptLog("Stopped.");
            break;
        case "dumpgps":
            DumpCurrentGpsToCustomData();
            break;
        case "pathstart":
            StartPathFromTimer();
            break;
        case "pathpause":
            if (!pathMode)
                ScriptLog("Path mode is not active.");
            else
            {
                pathPaused = true;
                if (autopilotEnabled && hasTarget)
                {
                    autopilotEnabled = false;
                    pathPausedNavResume = true;
                    ScriptLog("Path mode paused. Movement halted.");
                }
                else
                    ScriptLog("Path mode paused.");
            }

            break;
        case "pathcontinue":
            if (!pathMode)
                ScriptLog("Path mode is not active.");
            else
            {
                pathPaused = false;
                ScriptLog("Path mode resumed.");
                if (pathPausedNavResume && hasTarget)
                {
                    autopilotEnabled = true;
                    pathPausedNavResume = false;
                }
                else if (!autopilotEnabled)
                    StartNextPathRecord();
            }

            break;
        case "pathstop":
            StopPathExecution();
            ScriptLog("Path mode stopped.");
            break;
        case "toffreset":
            TOFFR = 0;
            TOFFU = 0;
            TOFFF = 0;
            ScriptLog("TOFF reset to 0,0,0.");
            break;
        case "setallopen":
            RequestEmma3Reset();
            break;
        case "fullauto":
            SetFullAutoEnabledFromUi(true);
            break;
        case "fullautostop":
            SetFullAutoEnabledFromUi(false);
            break;
        case "hangarcycle":
            StartManualHangarCycle();
            break;
        case "hangarstore":
        case "hangar store":
            StartManualHangarStore();
            break;
        case "hangardeploy":
        case "hangar deploy":
            StartManualHangarDeploy();
            break;
        case "pluginabort":
            {
                string pluginAbortReason = "Plugin requested a full abort without providing a reason.";
                MyIni pluginAbortIni;
                if (TryReadEmma3Bridge(out pluginAbortIni))
                {
                    string reported = pluginAbortIni.Get("EMMA3", "message").ToString("").Trim();
                    if (!string.IsNullOrWhiteSpace(reported))
                        pluginAbortReason = reported;
                }

                AbortEmma3Mission(pluginAbortReason);
                break;
            }

        case "plugincockpitabort":
            if (!cockpitAbortPaused)
            {
                cockpitAbortResumeAutopilot = autopilotEnabled;
                cockpitAbortPaused = true;
                autopilotEnabled = false;
                activeMaxSpeed = 0;
                ResetMovement();
                ScriptLog("Cockpit-loss abort. Mission retained for Continue.");
            }

            break;
        case "plugincontinue":
            if (cockpitAbortPaused)
            {
                cockpitAbortPaused = false;
                if (cockpitAbortResumeAutopilot && hasTarget)
                    autopilotEnabled = true;
                cockpitAbortResumeAutopilot = false;
                ScriptLog("Continued retained cockpit-loss mission.");
            }

            break;
    }
}
