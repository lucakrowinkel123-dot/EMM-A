void RemoteCommand(string command)
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
            Runtime.UpdateFrequency = UpdateFrequency.None;
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

void HandlePluginUnloadReport(string command)
{
    string[] fields = command.Split('|');
    long session;
    if (fields.Length != 4 || !long.TryParse(fields[1], out session) || session <= 0
        || reportFrozen || reportMission.Length == 0 || fields[2] != reportMission) return;
    string[] rows = fields[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    for (int i = 0; i < rows.Length; i++)
    {
        int equals = rows[i].IndexOf('=');
        if (equals <= 0) continue;
        string ore = rows[i].Substring(0, equals).Trim();
        double amount;
        if (ore.Length == 0 || !TryParseNumber(rows[i].Substring(equals + 1), out amount) || amount <= 0) continue;
        asteroidReport.AddOre(ore, amount);
        miningTotals.AddOre(ore, amount);
    }
    MarkProgressStateDirty();
    RequestCarrierSave(false);
    ScriptLog("Plugin ore delivery report accepted for unload " + session + ".");
}

void UpdatePathRunner()
{
    if (!pathMode || pathRecords.Count == 0)
        return;
    if (pathPaused)
        return;
    if (autopilotEnabled && hasTarget)
    {
        if (carrierAutopilotMode == CarrierAutopilotMode.Precision && IsAtTargetPose(targetPosition, desiredForward, desiredUp))
        {
            autopilotEnabled = false;
            hasTarget = false;
            activeMaxSpeed = 0;
        }

        return;
    }

    StartNextPathRecord();
}

void StopPathExecution()
{
    pathMode = false;
    pathPaused = false;
    pathPausedNavResume = false;
    pathRecords.Clear();
    pathIndex = 0;
    pathAnchorValid = false;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    ResetMovement();
}

void PollMinerPathCompletion()
{
    MinerSlot miner = ActiveMiner();
    if (miner.PathCompleteListener == null)
        return;
    for (int processed = 0; processed < 1 && miner.PathCompleteListener.HasPendingMessage; processed++)
    {
        MyIGCMessage msg = miner.PathCompleteListener.AcceptMessage();
        string payload = msg.Data as string;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            ScriptLog($"Miner broadcast: {payload}");
            string kind;
            string missionId;
            if (!TryParseMinerCompletionPayload(payload, out kind, out missionId))
            {
                ScriptLog("Miner completion ignored: unknown payload.");
                continue;
            }

            if (!string.Equals(missionId, emma3MissionId, StringComparison.Ordinal))
            {
                ScriptLog("Miner completion ignored: mission mismatch.");
                continue;
            }

            if (kind.Equals("recall", StringComparison.OrdinalIgnoreCase))
            {
                miner.RecallRequested = false;
                miner.RecallActive = true;
                miner.RecallAcknowledged = true;
                miner.RecallRequestElapsedSeconds = 0;
                if (emma3MissionState == Emma3MissionState.WaitingMiner)
                {
                    miner.PathCompleteReceived = true;
                    miner.PathCompleteKind = "recall";
                    if (!RecallStateMatchesActiveMission())
                        miner.RecallMissionId = missionId;
                    ScriptLog("Miner recall completion accepted as recall stop.");
                }

                continue;
            }

            if (kind.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                miner.PathCompleteReceived = true;
                miner.PathCompleteKind = "path";
            }
        }
    }
}

void BeginCarrierBrakeBeforeTurn(MatrixD rcMatrix)
{
    carrierMotionPhase = CarrierMotionPhase.BrakingBeforeTurn;
    carrierTurnSettledTicks = 0;
    carrierBrakeForward = rcMatrix.Forward;
    carrierBrakeUp = rcMatrix.Up;
    gyros.ResetController();
    gyros.FaceVectors(rcMatrix, carrierBrakeForward, carrierBrakeUp);
}

void BeginCarrierMotionSequence()
{
    MatrixD current = rc == null ? MatrixD.Identity : _tickRcMatrix;
    BeginCarrierBrakeBeforeTurn(current);
}

int carrierConnectorLightTicks = 6;

void UpdateCarrierConnectorLightThrottled()
{
    if (carrierConnectorLight == null) return;
    carrierConnectorLightTicks -= UpdateTickDelta();
    if (carrierConnectorLightTicks > 0) return;
    carrierConnectorLightTicks = 6;
    UpdateCarrierConnectorLight();
}

void UpdateCarrierConnectorLight()
{
    if (carrierConnectorLight == null)
        return;
    Color desiredColor = Color.Red;
    if (carrierConnector != null)
    {
        MyShipConnectorStatus status = GetCarrierConnectorStatus();
        if (status == MyShipConnectorStatus.Connected)
            desiredColor = Color.Green;
        else if (status == MyShipConnectorStatus.Connectable)
            desiredColor = Color.Yellow;
    }

    if (carrierConnectorLight.Color != desiredColor)
        carrierConnectorLight.Color = desiredColor;
    if (!carrierConnectorLight.Enabled)
        carrierConnectorLight.Enabled = true;
}

void LogPreviousTickTrace()
{
    double lastTickMs = Runtime.LastRunTimeMs;
    ScriptLog("PERF: tick=" + runtime
        + "; previous=" + lastTickMs.ToString("0.000")
        + " ms; mission=" + emma3MissionState
        + "; fullauto=" + fullAutoEnabled
        + "; autopilot=" + autopilotEnabled
        + "; target=" + hasTarget
        + "; flightPhase=" + carrierMotionPhase
        + "; speed=" + flightCache.LinearVelocity.Length().ToString("0.000")
        + "; angularSpeed=" + flightCache.AngularVelocity.Length().ToString("0.0000")
        + "; miner=" + ActiveMiner().StatusText);
}

bool TryParseMinerCompletionPayload(string payload, out string kind, out string missionId)
{
    kind = "";
    missionId = "";
    if (string.IsNullOrWhiteSpace(payload))
        return false;
    string[] parts = payload.Trim().Split('|');
    if (parts.Length >= 3 && parts[0].Equals("PathComplete", StringComparison.OrdinalIgnoreCase))
    {
        kind = "path";
        missionId = parts[1];
        return true;
    }

    if (parts.Length >= 3 && parts[0].Equals("RecallComplete", StringComparison.OrdinalIgnoreCase))
    {
        kind = "recall";
        missionId = parts[1];
        return true;
    }

    return false;
}

int UpdateTickDelta()
{
    if (tickSpeed == UpdateFrequency.Update10)
        return 10;
    if (tickSpeed == UpdateFrequency.Update100)
        return 100;
    return 1;
}
