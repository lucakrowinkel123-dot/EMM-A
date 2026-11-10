public void Save()
{
    SaveState();
}

public void Main(string argument, UpdateType updateSource)
{
    if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10 || updateSource == UpdateType.Update100)
    {
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
        if (autopilotEnabled) carrierRuntimeWorkMask |= 16;
        runtimeDetail = BeginRuntimeDetail();
        UpdateCarrierAutopilotRunner();
        EndRuntimeDetail(9, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (autopilotEnabled && hasTarget)
        {
            Move();
            EndRuntimeDetail(10, runtimeDetail);
        }
        else
        {
            ResetMovement();
            EndRuntimeDetail(11, runtimeDetail);
        }
        MarkRuntimeProfileSection(2);
        if (!cockpitAbortPaused)
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
        if (!cockpitAbortPaused)
        {
            if (fullAutoEnabled || carrierPlanLoadJob != null) carrierRuntimeWorkMask |= 32;
            runtimeDetail = BeginRuntimeDetail();
            UpdateFullAuto();
            EndRuntimeDetail(17, runtimeDetail);
            runtimeDetail = BeginRuntimeDetail();
            UpdatePathRunner();
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
        RemoteCommand(argument);
        MarkRuntimeProfileSection(3);
        RequestCarrierSave(true);
        MarkRuntimeProfileSection(1);
        carrierLcdStateFingerprint = int.MinValue;
        MarkRuntimeProfileSection(4);
    }
}

void LogPreviousCarrierSpikeTrace(int workMask)
{
    if (!emma3ScriptLogging || Runtime.LastRunTimeMs < 0.300) return;
    StringBuilder sb = _runtimeProfileSb;
    sb.Clear();
    sb.Append("PERF-SPIKE: previous=").Append(Runtime.LastRunTimeMs.ToString("0.000"));
    sb.Append(" ms; observed-at-tick=").Append(runtime);
    sb.Append("; work=").Append(workMask);
    for (int i = 0; i < runtimeProfileSectionNames.Length; i++)
        sb.Append("; ").Append(runtimeProfileSectionNames[i]).Append('=').Append(runtimeProfileLastMs[i].ToString("0.000"));
    sb.Append("; state=").Append(emma3MissionState);
    sb.Append("; auto=").Append(fullAutoEnabled ? 1 : 0).Append('/').Append(autopilotEnabled ? 1 : 0).Append('/').Append(hasTarget ? 1 : 0);
    sb.Append("; flight=").Append(carrierMotionPhase).Append('/').Append(carrierAutopilotMode);
    sb.Append("; route=").Append(emma3CarrierRouteIndex).Append('/').Append(emma3CarrierRoutePositions.Count);
    sb.Append("; plan-load=").Append(carrierPlanLoadJob != null ? 1 : 0);
    sb.Append("; save=").Append(_carrierSavePending ? 1 : 0).Append('/').Append(_carrierSaveStage).Append('/').Append(_carrierSaveIndex).Append('/').Append(_carrierSavePublishStage);
    sb.Append("; unload=").Append(carrierUnloadRequest != null ? 1 : 0);
    sb.Append("; report-ticks=").Append(reportRefreshTicks).Append("; lcd-ticks=").Append(lcdRefreshTicks);
    for (int i = 0; i < runtimeDetailNames.Length; i++)
        if (runtimeDetailLastMs[i] >= 0.001 || runtimeDetailLastInstructions[i] > 0)
            sb.Append("; op-").Append(runtimeDetailNames[i]).Append('=').Append(runtimeDetailLastMs[i].ToString("0.000")).Append("ms/").Append(runtimeDetailLastInstructions[i]).Append('i');
    sb.Append("; flags=pose1,connector2,dock4,save8,flight16,mission32,transfer64,report128,lcd256");
    ScriptLog(sb.ToString());
}

bool PluginLcdTakeoverActive()
{
    return emma3PluginLcdOutput && pluginLcdReady && pluginLcdHeartbeatAgeTicks <= 720;
}

void UpdatePluginLcdPayload(string main, string report)
{
    string nextMain = main ?? "";
    string nextReport = report ?? "";
    if (string.Equals(pluginLcdMainText, nextMain, StringComparison.Ordinal)
        && string.Equals(pluginLcdReportText, nextReport, StringComparison.Ordinal))
        return;
    pluginLcdMainText = nextMain;
    pluginLcdReportText = nextReport;
    pluginLcdSequence++;
    pluginLcdMarker = "";
}

void PublishPluginLcdPayload()
{
    if (!emma3PluginLcdOutput)
    {
        if (carrierPluginInfoTimer != null && !string.IsNullOrEmpty(publishedPluginLcdMarker))
        {
            carrierPluginInfoTimer.CustomData = "";
            publishedPluginLcdMarker = "";
        }
        return;
    }
    if (pluginLcdMarker.Length == 0)
    {
        StringBuilder sb = _pluginLcdMarkerSb;
        sb.Clear();
        sb.Append("EMMA3_LCD|1|").Append(pluginLcdSequence).Append('|');
        LowAllocationText.AppendPluginLcd(sb, pluginLcdMainText);
        sb.Append('|');
        LowAllocationText.AppendPluginLcd(sb, pluginLcdReportText);
        pluginLcdMarker = sb.ToString();
    }
    if (carrierPluginInfoTimer == null)
    {
        Echo(pluginLcdMarker);
        return;
    }
    if (!string.Equals(publishedPluginLcdMarker, pluginLcdMarker, StringComparison.Ordinal))
    {
        carrierPluginInfoTimer.CustomData = pluginLcdMarker;
        publishedPluginLcdMarker = pluginLcdMarker;
    }
}

int GetIndicatorDelayTicks()
{
    return Math.Max(1, (int)Math.Ceiling(indicatorLightDelaySeconds / Math.Max(0.001, secondsPerTick)));
}

void UpdateIndicatorLight()
{
    if (indicatorLight == null)
        return;
    bool desiredOn = autopilotEnabled;
    Color desiredOnColor = indicatorAutopilotColor;
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
                    SetIndicatorEnabled(true); // turn on after delay
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
        SetIndicatorEnabled(false); // turn off first
        indicatorDelayTicksRemaining = GetIndicatorDelayTicks();
        indicatorTransition = IndicatorTransitionState.WaitNeutralAfterDisable;
        return;
    }

    if (indicatorTransition == IndicatorTransitionState.WaitNeutralAfterDisable)
    {
        indicatorDelayTicksRemaining--;
        if (indicatorDelayTicksRemaining <= 0)
        {
            SetIndicatorColor(indicatorNeutralColor); // set neutral after off delay
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





void ConfigureCarrierLcd()
{
    if (carrierLcd == null)
        return;
    if (carrierLcdConfigured)
        return;
    carrierLcd.ContentType = ContentType.TEXT_AND_IMAGE;
    carrierLcd.Font = "Debug";
    carrierLcd.FontSize = 1.225f;
    carrierLcd.TextPadding = 0f;
    carrierLcd.Alignment = TextAlignment.LEFT;
    carrierLcdConfigured = true;
}

void SendCarrierStatusNow()
{
    IGC.SendBroadcastMessage(
        carrierStatusTag,
        (emma3ScriptLogging ? "scriptlog:on" : "scriptlog:off") + ";" + (minerMergeAssistedDocking ? "mergeassist:on" : "mergeassist:off")
        + ";" + (timerPersistenceEnabled ? "timerpersistence:on" : "timerpersistence:off"));
}

void PollMinerProgress()
{
    MinerSlot miner = ActiveMiner();
    if (miner.ProgressListener == null)
        return;
    int budget = emma3MissionState == Emma3MissionState.WaitingMinerStart ? 8 : 1;
    for (int processed = 0; processed < budget && miner.ProgressListener.HasPendingMessage; processed++)
    {
        MyIGCMessage msg = miner.ProgressListener.AcceptMessage();
        string payload = msg.Data as string;
        if (string.IsNullOrWhiteSpace(payload))
            continue;
        if (payload.StartsWith("status|", StringComparison.OrdinalIgnoreCase))
        {
            string[] fields = payload.Split('|');
            double cargo, water;
            int plane, planeTotal, lane, laneTotal;
            if (fields.Length >= 8 && TryParseNumber(fields[2], out cargo) && TryParseNumber(fields[3], out water) && int.TryParse(fields[4], out plane) && int.TryParse(fields[5], out planeTotal) && int.TryParse(fields[6], out lane) && int.TryParse(fields[7], out laneTotal))
            {
                miner.StatusText = fields[1];
                miner.StatusCargoPercent = MathHelper.Clamp(cargo, 0, 100);
                miner.StatusWaterPercent = MathHelper.Clamp(water, 0, 100);
                miner.StatusPlane = Math.Max(0, plane);
                miner.StatusPlaneTotal = Math.Max(0, planeTotal);
                miner.StatusLane = Math.Max(0, lane);
                miner.StatusLaneTotal = Math.Max(0, laneTotal);
                miner.StatusAgeSeconds = 0;
            }

            continue;
        }

        if (payload.StartsWith("path_preparing|", StringComparison.OrdinalIgnoreCase))
        {
            string[] fields = payload.Split('|');
            if (fields.Length == 2 && fields[1].Length > 0
                && emma3MissionState == Emma3MissionState.WaitingMinerStart
                && fields[1] == emma3MissionId)
                minerPathStartPreparingReceived = true;
            continue;
        }

        if (payload.StartsWith("path_started|", StringComparison.OrdinalIgnoreCase))
        {
            string[] fields = payload.Split('|');
            if (fields.Length >= 2
                && emma3MissionState == Emma3MissionState.WaitingMinerStart
                && fields[1] == emma3MissionId)
            {
                minerPathStartAcceptedReceived = true;
                ScriptLog("EMM-A 3: Miner accepted and loaded the path.");
            }
            continue;
        }

        string[] hangarFields = payload.Split('|');
        bool isHangarMessage = hangarFields.Length >= 2 && hangarFields[0].Equals("hangar", StringComparison.OrdinalIgnoreCase);
        int hangarOperationId = 0;
        if (isHangarMessage && hangarFields.Length >= 3)
            int.TryParse(hangarFields[hangarFields.Length - 1], out hangarOperationId);
        bool matchingHangarOperation = hangarOperationId <= 0 || miner.HangarOperationId <= 0 || hangarOperationId == miner.HangarOperationId;
        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("store_ready", StringComparison.OrdinalIgnoreCase))
        {
            bool expectingStore = manualHangarCycleState == ManualHangarCycleState.WaitingStoreReady
                || manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady
                || emma3MissionState == Emma3MissionState.WaitingHangarStore
                || emma3MissionState == Emma3MissionState.WaitingPostCompletionHangarStore;
            if (expectingStore)
            {
                miner.HangarStoreReadyReceived = true;
                ScriptLog("Hangar: Miner is centered and ready to store.");
                HandleHangarStoreReady();
            }

            continue;
        }

        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("online", StringComparison.OrdinalIgnoreCase))
        {
            miner.HangarOnlineReceived = true;
            continue;
        }

        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("dock_pose_request", StringComparison.OrdinalIgnoreCase))
        {
            SendCurrentCarrierDockPoseCommand();
            continue;
        }

        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("store_accepted", StringComparison.OrdinalIgnoreCase))
        {
            miner.HangarStoreAcceptedReceived = true;
            manualHangarCycleCommandRetryTicks = 0;
            manualHangarCycleCommandWaitTicks = 0;
            ScriptLog("Hangar: Miner accepted the storage command.");
            continue;
        }

        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("mission_ready", StringComparison.OrdinalIgnoreCase))
        {
            miner.HangarMissionReadyReceived = true;
            ScriptLog("Hangar: deployed Miner is ready for the mission.");
            continue;
        }

        if (isHangarMessage && matchingHangarOperation && hangarFields[1].Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            string reason = hangarFields.Length >= 3 ? hangarFields[2].Trim() : "unknown hangar movement failure";
            TriggerHangarManagerCommand("Disable");
            if (manualHangarCycleState != ManualHangarCycleState.None)
            {
                string operationName = GetManualHangarOperationName();
                ResetManualHangarCycleState();
                ScriptLog(operationName + " stopped: " + reason);
            }
            else if (emma3MissionState != Emma3MissionState.None)
                AbortEmma3Mission("Miner hangar movement failed: " + reason);
            else
                ScriptLog("Miner hangar movement failed: " + reason);
            continue;
        }

        string[] parts = payload.Split(':');
        if (parts.Length >= 3 && parts[0].Equals("route", StringComparison.OrdinalIgnoreCase))
        {
            double doneMeters, totalMeters;
            if (TryParseNumber(parts[1], out doneMeters) && TryParseNumber(parts[2], out totalMeters))
            {
                miner.ProgressMeters = Math.Max(0, doneMeters);
                miner.ProgressTotalMeters = Math.Max(0, totalMeters);
                if (miner.ProgressTotalMeters > 0)
                    miner.ProgressMeters = Math.Min(miner.ProgressMeters, miner.ProgressTotalMeters);
            }

            continue;
        }

        if (parts.Length < 2 || !parts[0].Equals("progress", StringComparison.OrdinalIgnoreCase))
            continue;
        string[] nums = parts[1].Split('/');
        int done, total;
        if (nums.Length == 2 && int.TryParse(nums[0], out done) && int.TryParse(nums[1], out total))
        {
            if (!miner.RecallRequested && !miner.RecallActive)
                continue;
            miner.RecallRequested = false;
            miner.RecallActive = true;
            miner.RecallAcknowledged = true;
            miner.RecallRequestElapsedSeconds = 0;
            if (caLcdMenu == CaLcdMenu.AutonomousMining && caMenuIndex >= GetCarrierMenuItemCount())
                caMenuIndex = Math.Max(0, GetCarrierMenuItemCount() - 1);
        }
    }
}
