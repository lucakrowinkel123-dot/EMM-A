public void Save()
{
    SaveState();
}

public void Main(string argument, UpdateType updateSource)
{
    if (updateSource == UpdateType.Update100 || updateSource == UpdateType.Update10 || updateSource == UpdateType.Update1)
    {
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
        UpdatePluginServiceAges();
        EndRuntimeDetail(1, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateMinerHydrogenCache();
        EndRuntimeDetail(2, runtimeDetail);
        if (!IsUndockBackgroundFrozen() && !PluginRemoteTelemetryFresh())
        {
            minerRuntimeWorkMask |= 2;
            runtimeDetail = BeginRuntimeDetail();
            UpdateMinerCargoStatsScan();
            EndRuntimeDetail(3, runtimeDetail);
        }
        runtimeDetail = BeginRuntimeDetail();
        UpdateRouteMeterProgress();
        EndRuntimeDetail(4, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateDockPoseAge();
        EndRuntimeDetail(5, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        UpdateDockPoseFromBroadcast();
        EndRuntimeDetail(6, runtimeDetail);
        if (dockPosePollTicks > 0)
            dockPosePollTicks--;
        else
        {
            dockPosePollTicks = igcPollIntervalTicks - 1;
            runtimeDetail = BeginRuntimeDetail();
            PollCarrierStatus();
            EndRuntimeDetail(7, runtimeDetail);
            runtimeDetail = BeginRuntimeDetail();
            PollMinerCommands();
            EndRuntimeDetail(8, runtimeDetail);
            if ((carrierStatusListener != null && carrierStatusListener.HasPendingMessage)
                || (minerCommandListener != null && minerCommandListener.HasPendingMessage))
                dockPosePollTicks = 0;
        }
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
                Runtime.UpdateFrequency = UpdateFrequency.None;
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
        UpdateFinalUnloadRecovery();
        EndRuntimeDetail(28, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (pathCompleteBroadcastTicks > 0)
            UpdatePathCompleteBroadcast();
        EndRuntimeDetail(29, runtimeDetail);
        if (oreTransferActive)
        {
            minerRuntimeWorkMask |= 128;
            runtimeDetail = BeginRuntimeDetail();
            UpdateOreTransfer();
            EndRuntimeDetail(30, runtimeDetail);
        }
        runtimeDetail = BeginRuntimeDetail();
        UpdateIndicatorLightThrottled();
        EndRuntimeDetail(31, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        if (routeProgressTracking && routeProgressTotalMeters > 0)
            BroadcastMinerProgress();
        EndRuntimeDetail(32, runtimeDetail);
        runtimeDetail = BeginRuntimeDetail();
        BroadcastMinerStatus();
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
        RemoteCommand(argument);
        UpdateMinerDockDemand();
        UpdateMiningGravityGenerator();
        MarkRuntimeProfileSection(2);
        if (!argument.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase))
            RequestMinerSave(true);
        MarkRuntimeProfileSection(1);
    }
}

void LogPreviousMinerSpikeTrace(int workMask)
{
    if (!emma3ScriptLogging || Runtime.LastRunTimeMs < 0.300) return;
    StringBuilder sb = _runtimeProfileSb;
    sb.Clear();
    sb.Append("PERF-SPIKE: previous=").Append(Runtime.LastRunTimeMs.ToString("0.000"));
    sb.Append(" ms; observed-at-tick=").Append(runtime);
    sb.Append("; work=").Append(workMask);
    for (int i = 0; i < runtimeProfileSectionNames.Length; i++)
        sb.Append("; ").Append(runtimeProfileSectionNames[i]).Append('=').Append(runtimeProfileLastMs[i].ToString("0.000"));
    sb.Append("; state=").Append(miningState).Append('/').Append(dockingState).Append('/').Append(hangarState);
    sb.Append("; auto=").Append(autopilotEnabled ? 1 : 0).Append('/').Append(hasTarget ? 1 : 0).Append('/').Append(miningEnabled ? 1 : 0).Append('/').Append(recallActive ? 1 : 0);
    sb.Append("; flight=").Append(flightDiagCommandMode).Append('/').Append(movementResetApplied ? 1 : 0);
    sb.Append("; path=").Append(pathIndex).Append('/').Append(pathRecords.Count);
    sb.Append("; lane=").Append(areaLaneIndex).Append('/').Append(areaLaneCount).Append('/').Append(areaPlanActivationStage);
    sb.Append("; undock=").Append(undockStage).Append("; service=").Append(autoServiceState);
    sb.Append("; power=").Append(minerPowerGoal).Append('/').Append(minerPowerIndex);
    sb.Append("; path-load=").Append(pathLoadJob != null ? 1 : 0);
    sb.Append("; save=").Append(_minerSavePending ? 1 : 0).Append('/').Append(_minerSaveIndex).Append('/').Append(_minerSaveRenderStage).Append('/').Append(_minerSavePublishStage);
    sb.Append("; cargo-cursor=").Append(_cargoStatsBlockIndex).Append('/').Append(_cargoStatsInventoryIndex);
    sb.Append("; transfer=").Append(oreTransferActive ? 1 : 0).Append('/').Append(oreTransferStartDelayTicks);
    sb.Append("; dock-action=").Append(dockHardwareRequestAction);
    for (int i = 0; i < runtimeDetailNames.Length; i++)
        if (runtimeDetailLastMs[i] >= 0.001 || runtimeDetailLastInstructions[i] > 0)
            sb.Append("; op-").Append(runtimeDetailNames[i]).Append('=').Append(runtimeDetailLastMs[i].ToString("0.000")).Append("ms/").Append(runtimeDetailLastInstructions[i]).Append('i');
    sb.Append("; flags=pose1,cargo2,path-load4,save8,power16,dock32,flight64,transfer128,plugin256");
    ScriptLog(sb.ToString());
}

bool IsMinerRoutineSaveDue()
{
    int saveInterval = Math.Max(echoFrequency, 600 / Math.Max(1, UpdateTickDelta()));
    return (runtime + echoFrequency / 2) % saveInterval == 0;
}

void WriteEcho()
{
    if (!emma3ScriptLogging)
        return;
    StringBuilder sb = _echoSb;
    sb.Clear();
    sb.AppendLine("Autopilot Mode");
    sb.AppendLine(autopilotEnabled ? "State: Enabled" : "State: Disabled");
    if (hasTarget)
        sb.AppendLine($"Target: {targetPosition.ToString("0.00")}");
    else
        sb.AppendLine("Target: <none>");
    if (ShouldWriteFlightDiagnostics())
        AppendFlightDiagnostics(sb);
    sb.AppendLine($"Runtime (last tick): {Runtime.LastRunTimeMs:0.000} ms");
    sb.AppendLine($"Runtime peak (last minute): {runtimePeakLastMinuteMs:0.000} ms");
    ScriptLog(sb.ToString());
}

bool ShouldWriteFlightDiagnostics()
{
    return recallActive || miningState == MiningState.Backout || dockingState != DockingState.None || (pathMode && autopilotEnabled && hasTarget);
}

void AppendFlightDiagnostics(StringBuilder sb)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    Vector3D controlPoint = _tickRcPosition + GetActiveTargetOffsetWorld(_tickRcMatrix);
    Vector3D toTarget = hasTarget ? targetPosition - controlPoint : Vector3D.Zero;
    double distance = toTarget.Length();
    Vector3D velocity = flightCache.LinearVelocity;
    double speed = velocity.Length();
    double speedToward = 0;
    double lateralSpeed = speed;
    Vector3D travelForward = flightDiagForward;
    if (toTarget.LengthSquared() > 0.001)
    {
        Vector3D targetDirection = Vector3D.Normalize(toTarget);
        speedToward = Vector3D.Dot(velocity, targetDirection);
        lateralSpeed = Math.Sqrt(Math.Max(0, speed * speed - speedToward * speedToward));
    }

    sb.AppendLine("Flight diagnostics:");
    sb.AppendLine("  Context: " + GetFlightDiagnosticContext());
    sb.AppendLine($"  Position: {_tickRcPosition.ToString("0.00")}");
    sb.AppendLine($"  Control point: {controlPoint.ToString("0.00")}");
    sb.AppendLine($"  Distance: {distance:0.00} m; tolerance {GetActiveArrivalTolerance():0.00} m");
    sb.AppendLine($"  Velocity: {velocity.ToString("0.00")}; speed {speed:0.00} m/s; toward {speedToward:0.00}; lateral {lateralSpeed:0.00}");
    sb.AppendLine("  Facing error: " + FormatAngleError(_tickRcMatrix.Forward, travelForward) + "; up error: " + FormatAngleError(_tickRcMatrix.Up, flightDiagUp));
    sb.AppendLine("  Waypoint bearing error: " + FormatAngleError(_tickRcMatrix.Forward, toTarget));
    sb.AppendLine($"  Command: {flightDiagCommandMode}; accel {flightDiagCommandAccel.ToString("0.00")}; speed cap {flightDiagCommandSpeedLimit:0.00} m/s; aligned {flightDiagAligned}; emergency {flightDiagEmergency}; at position {flightDiagAtPosition}");
    sb.AppendLine($"  Target offset R/U/F: {TOFFR:0.00}/{TOFFU:0.00}/{TOFFF:0.00} m");
    if (miningState != MiningState.Idle || areaMiningMode)
        sb.AppendLine($"  Mining: {miningState}; lane {areaLaneIndex + 1}/{Math.Max(1, areaLaneCount)}; depth {GetLaneProgressDepth():0.00}/{GetLaneTargetDepth():0.00} m; finish after backout {finishAreaAfterBackout}");
    if (recallActive)
        sb.AppendLine($"  Recall: target {recallIndex}/{recallRecords.Count}; path {Math.Max(0, Math.Min(pathIndex, pathRecords.Count))}/{pathRecords.Count}; direction {(recallReverseMode ? "start" : "end")}; wait area {recallWaitingForAreaExit}; wait dock {recallWaitingForDock}");
    }
    finally { EndRuntimeDetail(50, detail81); }
}

string GetFlightDiagnosticContext()
{
    if (recallActive && recallWaitingForAreaExit)
        return "recall-area-exit/" + miningState;
    if (recallActive && recallWaitingForDock)
        return "recall-docking/" + dockingState;
    if (recallActive)
        return "recall-route";
    if (miningState == MiningState.Backout)
        return "mining-backout";
    if (dockingState != DockingState.None)
        return "docking/" + dockingState;
    if (pathMode)
        return "path-route";
    return "autopilot";
}

string FormatAngleError(Vector3D actual, Vector3D desired)
{
    if (actual.LengthSquared() < 0.001 || desired.LengthSquared() < 0.001)
        return "n/a";
    actual = Vector3D.Normalize(actual);
    desired = Vector3D.Normalize(desired);
    double dot = Math.Max(-1.0, Math.Min(1.0, Vector3D.Dot(actual, desired)));
    return (Math.Acos(dot) * 180.0 / Math.PI).ToString("0.00") + " deg";
}

void UpdateRuntimeMetrics()
{
    double lastTickMs = Runtime.LastRunTimeMs;
    if (lastTickMs > runtimePeakLastMinuteMs)
    {
        runtimePeakLastMinuteMs = lastTickMs;
        CaptureRuntimeProfilePeak();
    }

    runtimePeakWindowElapsedSeconds += secondsPerTick;
    if (runtimePeakWindowElapsedSeconds >= 60.0)
    {
        LogRuntimeProfileWindow();
        runtimePeakLastMinuteMs = lastTickMs;
        CaptureRuntimeProfilePeak();
        runtimePeakWindowElapsedSeconds = 0;
    }
}

void BeginRuntimeProfile()
{
    // Latch once: a logging command received mid-invocation must not turn
    // half a tick into a valid section sample. Total runtime tracking stays on.
    runtimeProfileActive = emma3ScriptLogging;
    if (!runtimeProfileActive)
        return;
    for (int i = 0; i < runtimeProfileLastMs.Length; i++)
    {
        runtimeProfileLastMs[i] = 0;
        runtimeProfileLastInstructions[i] = 0;
    }
    for (int i = 0; i < runtimeDetailLastMs.Length; i++)
    {
        runtimeDetailLastMs[i] = 0;
        runtimeDetailLastInstructions[i] = 0;
    }

    runtimeProfileMarkerTicks = DateTime.UtcNow.Ticks;
    runtimeProfileMarkerInstructions = Runtime.CurrentInstructionCount;
}

struct RuntimeDetailSample
{
    public long Ticks, ChargedTicks, ChargedInstructions;
    public int Instructions;
}

RuntimeDetailSample BeginRuntimeDetail()
{
    if (!runtimeProfileActive) return default(RuntimeDetailSample);
    return new RuntimeDetailSample {
        Ticks = DateTime.UtcNow.Ticks, Instructions = Runtime.CurrentInstructionCount,
        ChargedTicks = runtimeDetailChargedTicks, ChargedInstructions = runtimeDetailChargedInstructions
    };
}

void EndRuntimeDetail(int section, RuntimeDetailSample started)
{
    if (!runtimeProfileActive || started.Ticks == 0 || section < 0 || section >= runtimeDetailLastMs.Length)
        return;
    // Exclusive timings: nested operations get their own buckets, never double
    // counted in their caller. Value-type start markers allow safe nesting.
    long ticks = Math.Max(0, DateTime.UtcNow.Ticks - started.Ticks - (runtimeDetailChargedTicks - started.ChargedTicks));
    int instructions = (int)Math.Max(0, Runtime.CurrentInstructionCount - started.Instructions - (runtimeDetailChargedInstructions - started.ChargedInstructions));
    runtimeDetailLastMs[section] += ticks / 10000.0;
    runtimeDetailLastInstructions[section] += instructions;
    runtimeDetailChargedTicks += ticks;
    runtimeDetailChargedInstructions += instructions;
}

void MarkRuntimeProfileSection(int section)
{
    if (!runtimeProfileActive)
        return;
    long nowTicks = DateTime.UtcNow.Ticks;
    int nowInstructions = Runtime.CurrentInstructionCount;
    if (section >= 0 && section < runtimeProfileLastMs.Length)
    {
        runtimeProfileLastMs[section] += (nowTicks - runtimeProfileMarkerTicks) / 10000.0;
        runtimeProfileLastInstructions[section] += Math.Max(0, nowInstructions - runtimeProfileMarkerInstructions);
    }

    runtimeProfileMarkerTicks = nowTicks;
    runtimeProfileMarkerInstructions = nowInstructions;
}

void CaptureRuntimeProfilePeak()
{
    runtimeProfilePeakAvailable = runtimeProfileActive;
    if (!runtimeProfilePeakAvailable)
    {
        runtimeProfilePeakDominant = "none";
        return;
    }
    double dominantMs = -1;
    runtimeProfilePeakDominant = "none";
    for (int i = 0; i < runtimeProfileLastMs.Length; i++)
    {
        runtimeProfilePeakMs[i] = runtimeProfileLastMs[i];
        runtimeProfilePeakInstructions[i] = runtimeProfileLastInstructions[i];
        if (runtimeProfileLastMs[i] > dominantMs)
        {
            dominantMs = runtimeProfileLastMs[i];
            runtimeProfilePeakDominant = runtimeProfileSectionNames[i];
        }
    }
}

void LogRuntimeProfileWindow()
{
    if (!emma3ScriptLogging || runtimePeakLastMinuteMs <= 0)
        return;
    if (!runtimeProfilePeakAvailable)
    {
        ScriptLog("PERF-SECTIONS: peakTotal=" + runtimePeakLastMinuteMs.ToString("0.000")
            + " ms; sections=not sampled (script logging was off).");
        return;
    }
    double accountedMs = 0;
    double dominantMs = -1;
    string dominant = runtimeProfilePeakDominant;
    for (int i = 0; i < runtimeProfilePeakMs.Length; i++)
    {
        accountedMs += runtimeProfilePeakMs[i];
        if (runtimeProfilePeakMs[i] > dominantMs)
            dominantMs = runtimeProfilePeakMs[i];
    }

    double overheadMs = Math.Max(0, runtimePeakLastMinuteMs - accountedMs);
    if (overheadMs > dominantMs)
        dominant = "runtime-overhead";
    StringBuilder sb = _runtimeProfileSb;
    sb.Clear();
    sb.Append("PERF-SECTIONS: peakTotal=").Append(runtimePeakLastMinuteMs.ToString("0.000"));
    sb.Append(" ms; dominant=").Append(dominant);
    for (int i = 0; i < runtimeProfileSectionNames.Length; i++)
    {
        sb.Append("; ").Append(runtimeProfileSectionNames[i]).Append('=');
        sb.Append(runtimeProfilePeakMs[i].ToString("0.000")).Append(" ms/");
        sb.Append(runtimeProfilePeakInstructions[i]).Append(" instr");
    }

    sb.Append("; runtime-overhead=").Append(overheadMs.ToString("0.000")).Append(" ms");
    ScriptLog(sb.ToString());
}

void PollCarrierStatus()
{
    if (carrierStatusListener == null)
        return;
    for (int processed = 0; processed < 1 && carrierStatusListener.HasPendingMessage; processed++)
    {
        MyIGCMessage msg = carrierStatusListener.AcceptMessage();
        string payload = msg.Data as string;
        if (string.IsNullOrWhiteSpace(payload))
            continue;
        string[] fields = payload.Split(';');
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i].Trim();
            if (field.Equals("scriptlog:on", StringComparison.OrdinalIgnoreCase))
                emma3ScriptLogging = true;
            else if (field.Equals("scriptlog:off", StringComparison.OrdinalIgnoreCase))
            {
                emma3ScriptLogging = false;
                ClearScriptLogQueue();
            }
            else if (field.Equals("mergeassist:on", StringComparison.OrdinalIgnoreCase))
            {
                if (!mergeAssistedDocking)
                {
                    fullStateDirty = true;
                    MarkPluginServiceRequestsDirty();
                }
                mergeAssistedDocking = true;
            }
            else if (field.Equals("mergeassist:off", StringComparison.OrdinalIgnoreCase))
            {
                if (mergeAssistedDocking)
                {
                    fullStateDirty = true;
                    MarkPluginServiceRequestsDirty();
                }
                mergeAssistedDocking = false;
                SetMergerEnabled(false);
            }
            else if (field.Equals("timerpersistence:on", StringComparison.OrdinalIgnoreCase))
                SetTimerPersistence(true);
            else if (field.Equals("timerpersistence:off", StringComparison.OrdinalIgnoreCase))
                SetTimerPersistence(false);
        }
    }
}

void PollMinerCommands()
{
    if (minerCommandListener == null)
        return;
    for (int processed = 0; processed < 1 && minerCommandListener.HasPendingMessage; processed++)
    {
        MyIGCMessage msg = minerCommandListener.AcceptMessage();
        string payload = msg.Data as string;
        if (string.IsNullOrWhiteSpace(payload))
            continue;
        string command = payload.Trim();
        string baseCommand, missionId;
        SplitMissionCommand(command, out baseCommand, out missionId);
        if (baseCommand.Equals("Recall", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("Hangar", StringComparison.OrdinalIgnoreCase))
            CancelPathLoad();
        if (baseCommand.Equals("Recall", StringComparison.OrdinalIgnoreCase))
            StartRecallPath(missionId);
        else if (baseCommand.Equals("PathStart", StringComparison.OrdinalIgnoreCase))
            StartPathFromTimer(missionId);
        else if (command.StartsWith("HangarCycleReturnDock|", StringComparison.OrdinalIgnoreCase))
            BeginHangarCycleReturnDock(command);
        else if (command.StartsWith("HangarStore|", StringComparison.OrdinalIgnoreCase))
            BeginHangarStorage(command);
        else if (command.StartsWith("DockEndpoint|", StringComparison.OrdinalIgnoreCase))
            ApplyCarrierDockEndpoint(command, msg.Source);
    }
}

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
    IGC.SendBroadcastMessage(
        minerProgressTag,
        "route:" + routeProgressMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + routeProgressTotalMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
}

void BroadcastMinerStatus()
{
    if (minerStatusBroadcastTicks > 0)
    {
        minerStatusBroadcastTicks = Math.Max(0, minerStatusBroadcastTicks - UpdateTickDelta());
        return;
    }

    minerStatusBroadcastTicks = minerStatusBroadcastIntervalTicks - UpdateTickDelta();
    double cargoPercent, stonePercent;
    GetMinerCargoStats(out cargoPercent, out stonePercent);
    double waterPercent = HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 0;
    int plane, planeTotal;
    GetCurrentMiningPlane(out plane, out planeTotal);
    int lane = areaLaneCount > 0 ? Math.Max(1, Math.Min(areaLaneCount, areaLaneIndex + 1)) : 0;
    int laneTotal = Math.Max(0, areaLaneCount);
    IGC.SendBroadcastMessage(
        minerProgressTag,
        "status|" + GetMinerStatusText()
        + "|" + cargoPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        + "|" + waterPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        + "|" + plane
        + "|" + planeTotal
        + "|" + lane
        + "|" + laneTotal);
}

string GetMinerStatusText()
{
    if (pathLoadJob != null) return "Loading route";
    if (dockingState == DockingState.DockingPowerDown) return "Miner powering down";
    if (dockingState == DockingState.UndockingPowerUp) return "Miner powering up";
    if (pathMode && pathIndex >= pathRecords.Count && IsMinerDocked())
        return "Final unloading";
    if (hangarState == HangarState.FinalService)
        return "Final service";
    if (hangarState == HangarState.UndockingForStore)
        return "Miner undocking";
    if (hangarState == HangarState.MovingToHangar)
        return "Entering hangar";
    if (hangarState == HangarState.AwaitingStore)
        return "Ready to store";
    if (hangarState == HangarState.PostDeployCheck)
        return "Checking after deploy";
    if (hangarState == HangarState.PostDeployDocking)
        return "Miner docking";
    if (hangarState == HangarState.PostDeployService)
        return "Post-deploy service";
    if (hangarState == HangarState.MissionReady)
        return "Ready";
    if (recallWaitingForDock || pathDockPending || dockRetryPending)
        return "Miner docking";
    if (recallPreparing)
        return "Miner returning";
    if (recallActive)
        return "Miner returning";
    if (stoneEjectionHoldActive)
        return "Miner ejecting stone";
    if (autoServiceState == AutoServiceState.Returning)
        return "Miner returning";
    if (autoServiceState == AutoServiceState.WaitingService)
        return "Servicing";
    if (autoServiceState == AutoServiceState.Undocking)
        return "Miner undocking";
    if (autoServiceState == AutoServiceState.ReturningToWork)
        return "Miner returning";
    if (areaMiningMode)
        return "Miner mining";
    if (pathPaused || scriptPaused)
        return "Paused";
    if (dockingState == DockingState.DockingToPose || dockingState == DockingState.DockingAlignAtOuterPose || dockingState == DockingState.DockingToInnerPose || dockingState == DockingState.DockingFinalApproach)
        return "Miner docking";
    if (dockingState == DockingState.UndockingRecompile || dockingState == DockingState.UndockingToPose || dockingState == DockingState.UndockingToOuterPose)
        return "Miner undocking";
    if (minerConnector != null && GetMinerConnectorStatus() == MyShipConnectorStatus.Connected)
        return "Docked";
    if (pathMode || autopilotEnabled)
        return "Travelling";
    return "Idle";
}

void RebuildMiningPlaneIndex()
{
    miningPlaneIndex.Clear();
    miningPlaneTotal = 0;
    for (int i = 0; i < pathRecords.Count; i++)
    {
        string record = NormalizePathRecord(pathRecords[i]);
        if (!IsPathMetadataRecord(record) && LineLooksLikeMineArea(record)) miningPlaneTotal++;
        miningPlaneIndex.Add(miningPlaneTotal);
    }
}

void GetCurrentMiningPlane(out int plane, out int total)
{
    total = pathRecords.Count == 0 ? 0 : miningPlaneTotal;
    plane = activeMineAreaRecordIndex < 0 || pathRecords.Count == 0 || miningPlaneIndex.Count == 0 ? 0
        : miningPlaneIndex[Math.Min(activeMineAreaRecordIndex, miningPlaneIndex.Count - 1)];
}

void LogPreviousTickTrace()
{
    if (!emma3ScriptLogging)
        return;
    double lastTickMs = Runtime.LastRunTimeMs;
    ScriptLog("PERF: tick=" + runtime
        + "; previous=" + lastTickMs.ToString("0.000")
        + " ms; status=" + GetMinerStatusText()
        + "; mining=" + miningState
        + "; docking=" + dockingState
        + "; recall=" + recallActive
        + "; auto-service=" + autoServiceState
        + "; hangar=" + hangarState
        + "; path=" + pathMode);
}

void UpdateRouteMeterProgress()
{
    if (!routeProgressTracking)
    {
        routeProgressPreviousValid = false;
        return;
    }

    if (!routeProgressPreviousValid)
    {
        routeProgressPreviousPosition = _tickRcPosition;
        routeProgressPreviousValid = true;
        return;
    }

    double moved = Vector3D.Distance(routeProgressPreviousPosition, _tickRcPosition);
    routeProgressPreviousPosition = _tickRcPosition;
    if (moved < 0.001 || scriptPaused)
        return;
    int direction = 1;
    if (recallActive && recallReverseMode)
        direction = -1;
    if (autoServiceState == AutoServiceState.WaitingService)
        direction = 0;
    else if (autoServiceState == AutoServiceState.Undocking || autoServiceState == AutoServiceState.ReturningToWork)
        direction = recallReverseMode ? 1 : -1;
    routeProgressMeters = MathHelper.Clamp(routeProgressMeters + moved * direction, 0, Math.Max(0, routeProgressTotalMeters));
}

void LoadRouteMeterMetadata()
{
    routeProgressTotalMeters = 0;
    for (int i = 0; i < pathRecords.Count; i++)
    {
        string record = NormalizePathRecord(pathRecords[i]);
        if (!record.StartsWith("00;routeMeters:", StringComparison.OrdinalIgnoreCase))
            continue;
        double total;
        if (TryParseNumber(record.Substring(15).Trim(), out total))
            routeProgressTotalMeters = Math.Max(0, total);
        break;
    }

    routeProgressMeters = 0;
    routeProgressTracking = routeProgressTotalMeters > 0;
    routeProgressPreviousPosition = _tickRcPosition;
    routeProgressPreviousValid = routeProgressTracking;
    minerProgressBroadcastTicks = 0;
}

bool IsMinerDocked()
{
    return minerConnector != null && GetMinerConnectorStatus() == MyShipConnectorStatus.Connected;
}

bool minerConnectionStatusValid, minerMergeStatusValid;
MyShipConnectorStatus cachedMinerConnectionStatus;
bool cachedMinerMergeConnected;

void InvalidateMinerConnectionStatus()
{
    minerConnectionStatusValid = false;
    minerMergeStatusValid = false;
}

MyShipConnectorStatus GetMinerConnectorStatus()
{
    if (!minerConnectionStatusValid)
    {
        cachedMinerConnectionStatus = minerConnector == null ? MyShipConnectorStatus.Unconnected : minerConnector.Status;
        minerConnectionStatusValid = true;
    }
    return cachedMinerConnectionStatus;
}

void CacheOreTransferBlocks()
{
    _oreTransferSources.Clear();
    GridTerminalSystem.GetBlocksOfType(
        _oreTransferSources,
        b => b.HasInventory && b.IsSameConstructAs(Me) && !IsExcludedMinerCargoInventory(b) && b.CustomName.IndexOf(minerContainerTag, StringComparison.OrdinalIgnoreCase) >= 0);
    StringBuilder ids = new StringBuilder();
    for (int i = 0; i < _oreTransferSources.Count; i++)
    {
        if (i > 0) ids.Append(',');
        ids.Append(_oreTransferSources[i].EntityId);
    }
    minerUnloadSourceIds = ids.ToString();
}

bool IsExcludedMinerCargoInventory(IMyTerminalBlock block)
{
    if (block is IMyConveyorSorter)
        return true;
    return block is IMyShipConnector && block.CustomName.IndexOf(minerEjectorTag, StringComparison.OrdinalIgnoreCase) >= 0;
}

double GetMinerHydrogenPercent()
{
    if (!minerHydrogenCacheValid || (minerHydrogenCacheTicks == 0 && !IsUndockBackgroundFrozen())) RefreshMinerHydrogenCache();
    return cachedMinerHydrogenPercent;
}

void UpdateMinerHydrogenCache()
{
    minerHydrogenCacheTicks = Math.Max(0, minerHydrogenCacheTicks - UpdateTickDelta());
}

void RefreshMinerHydrogenCache()
{
    double filled = 0, capacity = 0;
    int count = 0;
    foreach (var tank in hydrogenTanks)
    {
        if (tank == null || !HasMinerTag(tank))
            continue;
        double tankCapacity = Math.Max(0, tank.Capacity);
        filled += tank.FilledRatio * tankCapacity;
        capacity += tankCapacity;
        count++;
    }

    cachedMinerHydrogenTankCount = count;
    cachedMinerHydrogenPercent = capacity <= 0 ? 0 : (filled / capacity) * 100.0;
    minerHydrogenCacheValid = true;
    minerHydrogenCacheTicks = 60;
}

bool HasMinerHydrogenTank()
{
    if (!minerHydrogenCacheValid || (minerHydrogenCacheTicks == 0 && !IsUndockBackgroundFrozen())) RefreshMinerHydrogenCache();
    return cachedMinerHydrogenTankCount > 0;
}

double GetMinerCargoFillPercent()
{
    double cargoPercent;
    double stonePercent;
    GetMinerCargoStats(out cargoPercent, out stonePercent);
    return cargoPercent;
}

void GetMinerCargoStats(out double cargoPercent, out double stonePercent)
{
    cargoPercent = _cachedCargoPercent;
    stonePercent = _cachedStonePercent;
}

void RefreshMinerCargoStatsBlocks()
{
    _autoServiceCargoBlocks.Clear();
    GridTerminalSystem.GetBlocksOfType(
        _autoServiceCargoBlocks,
        b => b.HasInventory && !IsExcludedMinerCargoInventory(b) && b.CustomName.IndexOf(minerContainerTag, StringComparison.OrdinalIgnoreCase) >= 0);
    ResetMinerCargoStatsScan();
    _cachedCargoPercent = 0;
    _cachedStonePercent = 0;
    _cachedOrePresent = false;
}

void ResetMinerCargoStatsScan()
{
    _cargoStatsBlockIndex = 0;
    _cargoStatsInventoryIndex = 0;
    _cargoStatsCurrent = 0;
    _cargoStatsMax = 0;
    _cargoStatsStoneVolume = 0;
    _cargoStatsOrePresent = false;
    _cargoStatsScanStarted = false;
    cargoDetailsCurrent = false;
}

bool NeedsMinerCargoDetails()
{
    return stoneEjectionHoldActive || hangarState == HangarState.PostDeployCheck
        || (ShouldCheckAutoServiceReturn()
            && _cachedCargoPercent >= Math.Max(0, Math.Min(autoServiceCargoTriggerPercent, stoneEjectionCargoTriggerPercent) - 10));
}

void UpdateMinerCargoStatsScan()
{
    if (!_cargoStatsScanStarted)
    {
        _cargoStatsIncludeItems = NeedsMinerCargoDetails();
        _cargoStatsScanStarted = true;
    }
    if (_autoServiceCargoBlocks.Count == 0)
    {
        _cachedCargoPercent = 0;
        _cachedStonePercent = 0;
        _cachedOrePresent = false;
        cargoStatsCompletedCycles++;
        cargoDetailCompletedCycles++;
        cargoDetailsCurrent = true;
        _cargoStatsScanStarted = false;
        return;
    }

    // Skip unusable blocks/inventories cheaply, but read at most one real
    // inventory per game tick. This removes the former all-in-one cargo spike.
    while (_cargoStatsBlockIndex < _autoServiceCargoBlocks.Count)
    {
        IMyTerminalBlock block = _autoServiceCargoBlocks[_cargoStatsBlockIndex];
        if (block == null || !block.IsFunctional || _cargoStatsInventoryIndex >= block.InventoryCount)
        {
            _cargoStatsBlockIndex++;
            _cargoStatsInventoryIndex = 0;
            continue;
        }

        IMyInventory inv = block.GetInventory(_cargoStatsInventoryIndex++);
        if (inv == null)
            continue;
        _cargoStatsCurrent += (double)inv.CurrentVolume;
        _cargoStatsMax += (double)inv.MaxVolume;
        if (_cargoStatsIncludeItems)
        {
            _invItems.Clear();
            inv.GetItems(_invItems);
            foreach (var item in _invItems)
            {
                if (string.Equals(item.Type.TypeId, "MyObjectBuilder_Ore", StringComparison.OrdinalIgnoreCase) && item.Amount > 0)
                    _cargoStatsOrePresent = true;
                if (string.Equals(item.Type.SubtypeId, "Stone", StringComparison.OrdinalIgnoreCase))
                    _cargoStatsStoneVolume += (double)item.Amount * (double)item.Type.GetItemInfo().Volume;
            }
        }

        return;
    }

    _cachedCargoPercent = _cargoStatsMax <= 0 ? 0 : MathHelper.Clamp(_cargoStatsCurrent / _cargoStatsMax * 100.0, 0, 100);
    bool completedDetails = _cargoStatsIncludeItems;
    if (completedDetails)
    {
        _cachedStonePercent = _cargoStatsMax <= 0 ? 0 : MathHelper.Clamp(_cargoStatsStoneVolume / _cargoStatsMax * 100.0, 0, 100);
        _cachedOrePresent = _cargoStatsOrePresent;
        cargoDetailCompletedCycles++;
    }
    cargoStatsCompletedCycles++;
    ResetMinerCargoStatsScan();
    cargoDetailsCurrent = completedDetails;
}

void RefreshTickPose()
{
    InvalidateMinerConnectionStatus();
    DisableVanillaRemoteAutopilot();
    flightCache.BeginInvocation(runtime);
    _tickRcMatrix = flightCache.Matrix;
    _tickRcPosition = flightCache.Position;
}

IMyRemoteControl GetRequiredMinerRemoteControl()
{
    List<IMyRemoteControl> remotes = GetBlocks<IMyRemoteControl>();
    IMyRemoteControl match = null;
    for (int i = 0; i < remotes.Count; i++)
    {
        IMyRemoteControl candidate = remotes[i];
        if (candidate == null || candidate.CustomName != remoteControlName)
            continue;
        if (match != null)
            throw new Exception("Multiple Remote Controls named exactly '" + remoteControlName + "' exist on the Miner grid.");
        match = candidate;
    }

    if (match == null)
        throw new Exception("Required Remote Control not found on the Miner grid: " + remoteControlName);
    return match;
}

void DisableVanillaRemoteAutopilot()
{
    if (rc != null && rc.IsAutoPilotEnabled)
        rc.SetAutoPilotEnabled(false);
}
