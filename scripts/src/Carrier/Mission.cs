IMyTimerBlock FindEmma3BridgeTimer()
{
    return emma3BridgeTimer;
}

IMyTimerBlock FindOreTimer()
{
    return oreNameTimer;
}

bool TryReadEmma3Bridge(out MyIni ini)
{
    ini = _bridgeCommandIni;
    ini.Clear();
    IMyTimerBlock timer = FindEmma3BridgeTimer();
    if (timer == null)
        return false;
    MyIniParseResult result;
    if (ini.TryParse(timer.CustomData ?? "", out result))
        return true;
    // A failed parse can leave a partial document; never let a writer reuse it.
    ini.Clear();
    return false;
}

void WriteEmma3Bridge(MyIni ini)
{
    IMyTimerBlock timer = FindEmma3BridgeTimer();
    if (timer != null)
    {
        string raw = ini.ToString();
        timer.CustomData = raw;
        // This script is the authoritative writer for its own request. Update the
        // local cache immediately so the next tick cannot replay the state/message
        // that existed before this write while waiting for the periodic poll.
        emma3BridgeStatusRaw = raw;
        emma3BridgeStatusRawValid = true;
        emma3PluginState = ini.Get("EMMA3", "state").ToString("idle");
        emma3PluginMessage = ini.Get("EMMA3", "message").ToString("");
        emma3ContinueAvailable = ini.Get("EMMA3", "continueAvailable").ToBoolean(false);
        emma3BridgeAutoEnabled = ini.Get("EMMA3", "autonomousMining").ToBoolean(false);
    }
}

void ClearEmma3BridgeFailureForManualHangar()
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        return;

    string state = ini.Get("EMMA3", "state").ToString("idle");
    if (!state.Equals("aborted", StringComparison.OrdinalIgnoreCase)
        && !state.Equals("error", StringComparison.OrdinalIgnoreCase))
        return;

    ini.Set("EMMA3", "state", "idle");
    ini.Set("EMMA3", "message", "");
    ini.Set("EMMA3", "continueAvailable", false);
    WriteEmma3Bridge(ini);
}

void WriteEmma3Settings()
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        ini.Clear();
    ini.Set("EMMA3", "protocol", 1);
    ini.Set("EMMA3", "searchRadius", NumberR(emma3SearchRadius));
    ini.Set("EMMA3", "parkingHeight", NumberR(emma3ParkingHeight));
    ini.Set("EMMA3", "parkingHeight300MigrationDone", true);
    ini.Set("EMMA3", "parkingHeight400MigrationDone", true);
    ini.Set("EMMA3", "planeClearance", NumberR(emma3PlaneClearance));
    ini.Set("EMMA3", "travelClearance", NumberR(emma3TravelClearance));
    ini.Set("EMMA3", "travelClearance50MigrationDone", true);
    ini.Set("EMMA3", "clearance2575MigrationDone", true);
    ini.Set("EMMA3", "visualization", emma3Visualization ? "1" : "0");
    ini.Set("EMMA3", "allowMovement", emma3AllowMovement ? "1" : "0");
    ini.Set("EMMA3", "scriptLogging", emma3ScriptLogging ? "1" : "0");
    ini.Set("EMMA3", "allowSeatExitDuringMining", emma3AllowSeatExitDuringMining ? "1" : "0");
    ini.Set("EMMA3", "iceReserveEnabled", emma3IceReserveEnabled ? "1" : "0");
    ini.Set("EMMA3", "iceReserveLiters", NumberR(emma3IceReserveMillionLiters * 1000000));
    ini.Set("EMMA3", "pluginOreTransfer", emma3PluginOreTransfer ? "1" : "0");
    ini.Set("EMMA3", "pluginPowerTransitions", emma3PluginPowerTransitions ? "1" : "0");
    ini.Set("EMMA3", "pluginRemoteLink", emma3PluginRemoteLink ? "1" : "0");
    ini.Set("EMMA3", "pluginLcdOutput", emma3PluginLcdOutput ? "1" : "0");
    ini.Set("EMMA3", "timerPersistence", timerPersistenceEnabled ? "1" : "0");
    ini.Set("EMMA3", "selectionMode", emma3ItemsMode ? "Items" : "Ore");
    ini.Set("EMMA3", "enabledItems", emma3EnabledItems);
    ini.Set("EMMA3", "autonomousMining", fullAutoEnabled ? "1" : "0");
    WriteEmma3Bridge(ini);
}

void WriteMinerBehaviorSettings()
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        ini.Clear();
    ini.Set("EMMA3", "minerStandardDepth", NumberR(minerStandardDepth));
    ini.Set("EMMA3", "minerLaneWidth", NumberR(minerLaneWidth));
    ini.Set("EMMA3", "minerLaneHeight", NumberR(minerLaneHeight));
    ini.Set("EMMA3", "minerForwardSpeed", NumberR(minerForwardSpeed));
    ini.Set("EMMA3", "minerReturnSpeed", NumberR(minerReturnSpeed));
    ini.Set("EMMA3", "minerAutoService", minerAutoServiceEnabled ? "1" : "0");
    ini.Set("EMMA3", "minerAutoCargo", NumberR(minerAutoServiceCargoPercent));
    ini.Set("EMMA3", "minerAutoHydrogen", NumberR(minerAutoServiceHydrogenPercent));
    ini.Set("EMMA3", "minerStopAtOnePercentFuel", minerStopAtOnePercentFuel ? "1" : "0");
    ini.Set("EMMA3", "minerRouteStartExtra", NumberR(minerRouteStartExtra));
    ini.Set("EMMA3", "minerTargetOffsetRight", NumberR(0));
    ini.Set("EMMA3", "minerTargetOffsetUp", NumberR(0));
    ini.Set("EMMA3", "minerTargetOffsetForward", NumberR(5));
    ini.Set("EMMA3", "minerMergeAssistedDocking", minerMergeAssistedDocking ? "1" : "0");
    ini.Set("EMMA3", "minerHangarEnabled", minerHangarEnabled ? "1" : "0");
    WriteEmma3Bridge(ini);
}

void UpdateEmma3BridgeStatus()
{
    if (emma3CompletedAsteroidsTimer == null)
    {
        emma3BridgeStatusRawValid = false;
        emma3PluginState = "missing";
        emma3PluginMessage = "missing " + emma3CompletedAsteroidsTimerName;
        return;
    }

    IMyTimerBlock bridgeTimer = FindEmma3BridgeTimer();
    if (bridgeTimer == null)
    {
        emma3BridgeStatusRawValid = false;
        emma3PluginState = "missing";
        emma3PluginMessage = "missing " + emma3BridgeTimerName;
        return;
    }

    string raw = bridgeTimer.CustomData ?? "";
    if (emma3BridgeStatusRawValid && emma3BridgeAutoEnabled == fullAutoEnabled && string.Equals(raw, emma3BridgeStatusRaw, StringComparison.Ordinal))
        return;
    if (!TryClaimCarrierHeavyWork())
    {
        emma3BridgeStatusTicks = 0;
        return;
    }
    MyIni ini = _bridgeStatusIni;
    ini.Clear();
    MyIniParseResult parse;
    if (!ini.TryParse(raw, out parse))
    {
        emma3BridgeStatusRaw = raw;
        emma3BridgeStatusRawValid = true;
        emma3PluginState = "missing";
        emma3PluginMessage = "invalid " + emma3BridgeTimerName;
        return;
    }

    emma3BridgeStatusRaw = raw;
    emma3BridgeStatusRawValid = true;
    emma3PluginState = ini.Get("EMMA3", "state").ToString("idle");
    emma3BridgeAutoEnabled = ini.Get("EMMA3", "autonomousMining").ToBoolean(false);
    // Also covers stop/abort commands that change fullAutoEnabled outside the menu.
    if (ini.Get("EMMA3", "autonomousMining").ToBoolean(false) != fullAutoEnabled)
    {
        ini.Set("EMMA3", "autonomousMining", fullAutoEnabled ? "1" : "0");
        WriteEmma3Bridge(ini);
    }
    emma3IceReserveEnabled = ini.Get("EMMA3", "iceReserveEnabled").ToBoolean(emma3IceReserveEnabled);
    emma3IceReserveMillionLiters = ClampIceReserveMillionLiters(ini.Get("EMMA3", "iceReserveLiters").ToDouble(emma3IceReserveMillionLiters * 1000000) / 1000000);
    emma3PluginOreTransfer = ini.Get("EMMA3", "pluginOreTransfer").ToBoolean(emma3PluginOreTransfer);
    emma3PluginPowerTransitions = ini.Get("EMMA3", "pluginPowerTransitions").ToBoolean(emma3PluginPowerTransitions);
    emma3PluginRemoteLink = ini.Get("EMMA3", "pluginRemoteLink").ToBoolean(emma3PluginRemoteLink);
    emma3PluginLcdOutput = ini.Get("EMMA3", "pluginLcdOutput").ToBoolean(emma3PluginLcdOutput);
    pluginLcdReady = ini.Get("EMMA3", "pluginLcdReady").ToBoolean(false);
    int lcdHeartbeat = ini.Get("EMMA3", "pluginLcdHeartbeat").ToInt32(-1);
    if (lcdHeartbeat != pluginLcdHeartbeat)
    {
        pluginLcdHeartbeat = lcdHeartbeat;
        pluginLcdHeartbeatAgeTicks = 0;
    }
    emma3ItemsMode = ini.Get("EMMA3", "selectionMode").ToString(emma3ItemsMode ? "Items" : "Ore").Equals("Items", StringComparison.OrdinalIgnoreCase);
    emma3EnabledItems = ini.Get("EMMA3", "enabledItems").ToString(emma3EnabledItems);
    emma3ProductionStatus = ini.Get("EMMA3", "productionStatus").ToString("");
    emma3PluginMessage = ini.Get("EMMA3", "message").ToString("");
    emma3ContinueAvailable = ini.Get("EMMA3", "continueAvailable").ToBoolean(false);
    emma3CompletedCount = ini.Get("EMMA3", "completedCount").ToInt32(emma3CompletedCount);
    emma3Visualization = ini.Get("EMMA3", "visualization").ToBoolean(emma3Visualization);
    emma3AllowMovement = ini.Get("EMMA3", "allowMovement").ToBoolean(emma3AllowMovement);
    emma3AllowSeatExitDuringMining = ini.Get("EMMA3", "allowSeatExitDuringMining").ToBoolean(emma3AllowSeatExitDuringMining);
    bool loggingBefore = emma3ScriptLogging;
    emma3ScriptLogging = ini.Get("EMMA3", "scriptLogging").ToBoolean(emma3ScriptLogging);
    if (loggingBefore && !emma3ScriptLogging)
        ClearScriptLogQueue();
    double d;
    if (TryParseNumber(ini.Get("EMMA3", "searchRadius").ToString(NumberR(emma3SearchRadius)), out d))
        emma3SearchRadius = d;
    bool hasParkingHeight = ini.ContainsKey("EMMA3", "parkingHeight");
    if (TryParseNumber(ini.Get("EMMA3", "parkingHeight").ToString(NumberR(emma3ParkingHeight)), out d))
    {
        bool parking300MigrationDone = ini.Get("EMMA3", "parkingHeight300MigrationDone").ToBoolean(false);
        bool parking400MigrationDone = ini.Get("EMMA3", "parkingHeight400MigrationDone").ToBoolean(false);
        emma3ParkingHeight = d;
        if (!hasParkingHeight)
        {
            emma3ParkingHeight = 400;
            parking300MigrationDone = true;
            parking400MigrationDone = true;
        }
        else
        {
            if (!parking300MigrationDone)
                emma3ParkingHeight = Math.Min(5000, emma3ParkingHeight + 150);
            if (!parking400MigrationDone)
                emma3ParkingHeight = Math.Min(5000, emma3ParkingHeight + 100);
        }
        if (!hasParkingHeight || !parking300MigrationDone || !parking400MigrationDone)
        {
            ini.Set("EMMA3", "parkingHeight", NumberR(emma3ParkingHeight));
            ini.Set("EMMA3", "parkingHeight300MigrationDone", true);
            ini.Set("EMMA3", "parkingHeight400MigrationDone", true);
            WriteEmma3Bridge(ini);
        }
    }

    bool clearance2575MigrationDone = ini.Get("EMMA3", "clearance2575MigrationDone").ToBoolean(false);
    if (TryParseNumber(ini.Get("EMMA3", "planeClearance").ToString(NumberR(emma3PlaneClearance)), out d))
        emma3PlaneClearance = Math.Max(25, d);
    if (TryParseNumber(ini.Get("EMMA3", "travelClearance").ToString(NumberR(emma3TravelClearance)), out d))
    {
        bool clearanceMigrationDone = ini.Get("EMMA3", "travelClearance50MigrationDone").ToBoolean(false);
        emma3TravelClearance = !clearanceMigrationDone && Math.Abs(d - 75) < 0.001 ? 50 : d;
        if (!clearance2575MigrationDone && Math.Abs(emma3TravelClearance - 50) < 0.001)
            emma3TravelClearance = 75;
        if (!clearanceMigrationDone)
        {
            ini.Set("EMMA3", "travelClearance", NumberR(emma3TravelClearance));
            ini.Set("EMMA3", "travelClearance50MigrationDone", true);
            WriteEmma3Bridge(ini);
        }
    }
    if (!clearance2575MigrationDone)
    {
        ini.Set("EMMA3", "planeClearance", NumberR(emma3PlaneClearance));
        ini.Set("EMMA3", "travelClearance", NumberR(emma3TravelClearance));
        ini.Set("EMMA3", "clearance2575MigrationDone", true);
        WriteEmma3Bridge(ini);
    }

    if (TryParseNumber(ini.Get("EMMA3", "minerStandardDepth").ToString(NumberR(minerStandardDepth)), out d))
        minerStandardDepth = Math.Max(0, d);
    if (TryParseNumber(ini.Get("EMMA3", "minerLaneWidth").ToString(NumberR(minerLaneWidth)), out d))
        minerLaneWidth = Math.Max(0.1, d);
    if (TryParseNumber(ini.Get("EMMA3", "minerLaneHeight").ToString(NumberR(minerLaneHeight)), out d))
        minerLaneHeight = Math.Max(0.1, d);
    if (TryParseNumber(ini.Get("EMMA3", "minerForwardSpeed").ToString(NumberR(minerForwardSpeed)), out d))
        minerForwardSpeed = Math.Max(0.1, d);
    if (TryParseNumber(ini.Get("EMMA3", "minerReturnSpeed").ToString(NumberR(minerReturnSpeed)), out d))
        minerReturnSpeed = Math.Max(0.1, d);
    minerAutoServiceEnabled = ini.Get("EMMA3", "minerAutoService").ToBoolean(minerAutoServiceEnabled);
    if (TryParseNumber(ini.Get("EMMA3", "minerAutoCargo").ToString(NumberR(minerAutoServiceCargoPercent)), out d))
        minerAutoServiceCargoPercent = MathHelper.Clamp(d, 0, 100);
    if (TryParseNumber(ini.Get("EMMA3", "minerAutoHydrogen").ToString(NumberR(minerAutoServiceHydrogenPercent)), out d))
        minerAutoServiceHydrogenPercent = MathHelper.Clamp(d, 0, 100);
    minerStopAtOnePercentFuel = ini.Get("EMMA3", "minerStopAtOnePercentFuel").ToBoolean(minerStopAtOnePercentFuel);
    if (TryParseNumber(ini.Get("EMMA3", "minerRouteStartExtra").ToString(NumberR(minerRouteStartExtra)), out d))
        minerRouteStartExtra = MathHelper.Clamp(d, 0, 500);
    minerMergeAssistedDocking = ini.Get("EMMA3", "minerMergeAssistedDocking").ToBoolean(minerMergeAssistedDocking);
    minerHangarEnabled = ini.Get("EMMA3", "minerHangarEnabled").ToBoolean(minerHangarEnabled);
}

void RequestEmma3Continue()
{
    if (!ShowContinueMenuItem())
        return;
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        return;
    emma3RequestSequence = Math.Max(emma3RequestSequence, ini.Get("EMMA3", "request").ToInt32(0)) + 1;
    ini.Set("EMMA3", "request", emma3RequestSequence);
    ini.Set("EMMA3", "command", "continue");
    ini.Set("EMMA3", "state", "requested");
    ini.Set("EMMA3", "message", "Continue requested from Carrier LCD");
    WriteEmma3Bridge(ini);
    ScriptLog("EMM-A 3: continuing retained cockpit-loss mission.");
}

void RequestEmma3Plan()
{
    RequestEmma3Plan(false, 0, false);
}

void RequestEmma3CarrierPlanForStoredMiner()
{
    RequestEmma3Plan(true, 0, true);
}

void RequestEmma3MinerPlanForActiveAsteroid()
{
    RequestEmma3Plan(false, emma3ActiveEntityId, true);
}

void RequestEmma3Plan(bool carrierOnly, long requestedAsteroidEntityId, bool allowDeployedMiner)
{
    reportApproachSeconds = 0;
    ResetMinerRecallState();
    ResetMinerPathCompleteState();
    if (!carrierOnly && !allowDeployedMiner && (carrierConnector == null || GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected))
    {
        AbortEmma3Mission("Miner must be docked before planning.");
        return;
    }

    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
    {
        AbortEmma3Mission("Plugin bridge timer is missing or invalid.");
        return;
    }

    emma3RequestSequence = Math.Max(emma3RequestSequence, ini.Get("EMMA3", "request").ToInt32(0)) + 1;
    ini.Set("EMMA3", "request", emma3RequestSequence);
    ini.Set("EMMA3", "command", "plan");
    ini.Set("EMMA3", "planType", carrierOnly ? "carrier" : "full");
    ini.Set("EMMA3", "requestedAsteroidEntityId", requestedAsteroidEntityId.ToString());
    ini.Set("EMMA3", "projectMinerDockPose", allowDeployedMiner && !carrierOnly ? "1" : "0");
    ini.Set("EMMA3", "state", "requested");
    ini.Set("EMMA3", "message", carrierOnly ? "Carrier requested stored-Miner transit" : "Carrier requested dynamic mission");
    WriteEmma3Bridge(ini);
    emma3MissionState = allowDeployedMiner && !carrierOnly ? Emma3MissionState.WaitingMinerPlan : Emma3MissionState.WaitingPlan;
    emma3PlanPollTicks = 0;
    emma3CarrierRoutePositions.Clear();
    emma3CarrierRouteForwards.Clear();
    emma3CarrierRouteUps.Clear();
    emma3CarrierRouteIndex = 0;
    emma3CarrierParkAligning = false;
    ScriptLog(carrierOnly
        ? "EMM-A 3: Carrier route requested while Miner A remains stored."
        : (allowDeployedMiner ? "EMM-A 3: Miner route requested for the parked asteroid." : "EMM-A 3: dynamic plan requested."));
}

bool IsCurrentEmma3PlanCarrierOnly()
{
    MyIni ini;
    return TryReadEmma3Bridge(out ini)
        && ini.Get("EMMA3", "missionId").ToString("") == emma3MissionId
        && ini.Get("EMMA3", "planType").ToString("").Equals("carrier", StringComparison.OrdinalIgnoreCase);
}

bool StartDeployedMinerFromReadyPlan(MyIni ini)
{
    long entityId;
    string missionId = ini.Get("EMMA3", "missionId").ToString("");
    if (!ini.Get("EMMA3", "planType").ToString("").Equals("full", StringComparison.OrdinalIgnoreCase)
        || !long.TryParse(ini.Get("EMMA3", "asteroidEntityId").ToString("0"), out entityId)
        || entityId != emma3ActiveEntityId
        || missionId.Length == 0
        || ini.Get("EMMA3", "pathRecords").ToInt32(0) <= 0
        || ini.Get("EMMA3", "pathHash").ToString("").Length == 0)
    {
        AbortEmma3Mission("Plugin returned an invalid Miner route for the parked asteroid.");
        return false;
    }

    emma3MissionId = missionId;
    StartAsteroidReport();
    ResetMinerPathCompleteState();
    ResetMinerRecallState();
    BeginMinerPathStartHandshake();
    return true;
}

void BeginMinerPathStartHandshake()
{
    if (string.IsNullOrWhiteSpace(emma3MissionId))
    {
        AbortEmma3Mission("Miner path could not start because its mission ID is missing.");
        return;
    }
    minerPathStartAcceptedReceived = false;
    minerPathStartPreparingReceived = false;
    minerPathStartWaitTicks = 0;
    minerPathStartRetryTicks = minerPathStartRetryIntervalTicks;
    emma3MissionState = Emma3MissionState.WaitingMinerStart;
    MarkProgressStateDirty();
    SendMinerCommand("PathStart|" + emma3MissionId);
    ScriptLog("EMM-A 3: PathStart sent; waiting for Miner acknowledgement.");
}

void UpdateMinerPathStartHandshake()
{
    if (emma3MissionState != Emma3MissionState.WaitingMinerStart)
        return;
    if (minerPathStartAcceptedReceived)
    {
        minerPathStartAcceptedReceived = false;
        minerPathStartPreparingReceived = false;
        minerPathStartWaitTicks = 0;
        minerPathStartRetryTicks = 0;
        emma3MissionState = Emma3MissionState.WaitingMiner;
        MarkProgressStateDirty();
        ScriptLog("EMM-A 3: Miner path started.");
        return;
    }

    int delta = UpdateTickDelta();
    minerPathStartWaitTicks += delta;
    // Receipt and route readiness are different stages. Repeated receipts never
    // reset this absolute deadline, so an unfinished parse cannot wait forever.
    int timeout = minerPathStartPreparingReceived ? minerPathStartLoadingTimeoutTicks : minerPathStartTimeoutTicks;
    if (minerPathStartWaitTicks >= timeout)
    {
        AbortEmma3Mission(minerPathStartPreparingReceived
            ? "Miner path loading timed out (60s)."
            : "Miner did not respond to PathStart (10s).");
        return;
    }
    minerPathStartRetryTicks = Math.Max(0, minerPathStartRetryTicks - delta);
    if (minerPathStartRetryTicks > 0)
        return;
    minerPathStartRetryTicks = minerPathStartRetryIntervalTicks;
    SendMinerCommand("PathStart|" + emma3MissionId);
}

bool LoadEmma3ReadyPlan(MyIni ini)
{
    int count = ini.Get("EMMA3", "carrierRouteCount").ToInt32(0);
    long entityId;
    if (count <= 0 || !long.TryParse(ini.Get("EMMA3", "asteroidEntityId").ToString("0"), out entityId) || entityId == 0)
        return false;
    var positions = new List<Vector3D>();
    var forwards = new List<Vector3D>();
    var ups = new List<Vector3D>();
    for (int i = 0; i < count; i++)
    {
        string raw = ini.Get("EMMA3", "carrierRoute" + i).ToString("");
        string[] parts = raw.Split('|');
        Vector3D pos, forward, up;
        if (parts.Length != 3 || !TryParseV3(parts[0], out pos) || !TryParseV3(parts[1], out forward) || !TryParseV3(parts[2], out up))
            return false;
        positions.Add(pos);
        forwards.Add(forward);
        ups.Add(up);
    }

    emma3CarrierRoutePositions = positions;
    emma3CarrierRouteForwards = forwards;
    emma3CarrierRouteUps = ups;
    emma3CarrierRouteIndex = 0;
    emma3CarrierParkAligning = false;
    emma3ActiveEntityId = entityId;
    plannedReportHasBox = TryParseV3(ini.Get("EMMA3", "asteroidBoxMin").ToString(""), out plannedReportBoxMin)
        && TryParseV3(ini.Get("EMMA3", "asteroidBoxMax").ToString(""), out plannedReportBoxMax);
    emma3MissionId = ini.Get("EMMA3", "missionId").ToString("");
    return true;
}

bool BeginEmma3ReadyPlanLoad(MyIni ini)
{
    int count = ini.Get("EMMA3", "carrierRouteCount").ToInt32(0);
    long entityId;
    if (count <= 0 || !long.TryParse(ini.Get("EMMA3", "asteroidEntityId").ToString("0"), out entityId) || entityId == 0)
        return false;
    carrierPlanLoadJob = new CarrierPlanLoadJob { Ini = ini, Count = count, EntityId = entityId };
    return true;
}

int PumpEmma3ReadyPlanLoad()
{
    CarrierPlanLoadJob job = carrierPlanLoadJob;
    if (job == null) return 1;
    if (job.Index < job.Count)
    {
        string raw = job.Ini.Get("EMMA3", "carrierRoute" + job.Index).ToString("");
        string[] parts = raw.Split('|');
        Vector3D pos, forward, up;
        if (parts.Length != 3 || !TryParseV3(parts[0], out pos) || !TryParseV3(parts[1], out forward) || !TryParseV3(parts[2], out up))
        {
            carrierPlanLoadJob = null;
            return -1;
        }
        job.Positions.Add(pos);
        job.Forwards.Add(forward);
        job.Ups.Add(up);
        job.Index++;
        return 0;
    }

    emma3CarrierRoutePositions = job.Positions;
    emma3CarrierRouteForwards = job.Forwards;
    emma3CarrierRouteUps = job.Ups;
    emma3CarrierRouteIndex = 0;
    emma3CarrierParkAligning = false;
    emma3ActiveEntityId = job.EntityId;
    plannedReportHasBox = TryParseV3(job.Ini.Get("EMMA3", "asteroidBoxMin").ToString(""), out plannedReportBoxMin)
        && TryParseV3(job.Ini.Get("EMMA3", "asteroidBoxMax").ToString(""), out plannedReportBoxMax);
    emma3MissionId = job.Ini.Get("EMMA3", "missionId").ToString("");
    carrierPlanLoadJob = null;
    MarkCarrierMissionTransition();
    return 1;
}

void ActivateLoadedEmma3Plan()
{
    if ((ActiveMiner().HangarStoreCommandSent || ActiveMiner().HangarManagerPending == HangarManagerPendingCommand.Enable) && !ActiveMiner().HangarStored)
    {
        emma3MissionState = Emma3MissionState.WaitingHangarStore;
        ScriptLog("EMM-A 3: next plan ready; waiting for Miner A to enter its hangar.");
        return;
    }
    emma3MissionState = Emma3MissionState.FlyingCarrierRoute;
    BeginCarrierTransitFlightCache();
    StartNextEmma3CarrierRoutePoint();
}

void PublishEmma3CarrierRouteCompletion()
{
    // Publish only at park completion or when restoring a post-arrival state.
    // A route index alone is insufficient: it advances when a leg starts.
    if (emma3MissionState != Emma3MissionState.WaitingStationary
        && emma3MissionState != Emma3MissionState.WaitingMiner
        && emma3MissionState != Emma3MissionState.WaitingHangarDeploy)
        return;
    if (emma3MissionId.Length == 0)
        return;
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini)
        || ini.Get("EMMA3", "missionId").ToString("") != emma3MissionId
        || ini.Get("EMMA3", "carrierRouteCompletedMission").ToString("") == emma3MissionId)
        return;
    // Post-arrival reloads retain the index, but do not reload route geometry.
    int count = ini.Get("EMMA3", "carrierRouteCount").ToInt32(0);
    if (count <= 0 || emma3CarrierRouteIndex < count)
        return;
    ini.Set("EMMA3", "carrierRouteCompletedMission", emma3MissionId);
    WriteEmma3Bridge(ini);
}

void StartNextEmma3CarrierRoutePoint()
{
    if (emma3CarrierRouteIndex >= emma3CarrierRoutePositions.Count)
    {
        EndCarrierTransitFlightCache();
        emma3MissionState = Emma3MissionState.WaitingStationary;
        PublishEmma3CarrierRouteCompletion();
        mineStationaryTicks = 0;
        StartAsteroidReport(false);
        ScriptLog("EMM-A 3: Carrier parked. Waiting to start Miner.");
        return;
    }

    Vector3D pos = emma3CarrierRoutePositions[emma3CarrierRouteIndex];
    Vector3D forward = emma3CarrierRouteForwards[emma3CarrierRouteIndex];
    Vector3D up = emma3CarrierRouteUps[emma3CarrierRouteIndex];
    emma3CarrierRouteIndex++;
    emma3CarrierParkAligning = false;
    // The final lane is flown nose-first; its recorded pose is applied only
    // after stopping at park. Do not use cruise's flip-braking on this leg.
    if (emma3CarrierRouteIndex == emma3CarrierRoutePositions.Count)
        StartPrecisionTarget(pos, Vector3D.Zero, Vector3D.Zero);
    else
        StartCruiseTarget(pos, forward, up);
    ScriptLog("EMM-A 3: Carrier route " + emma3CarrierRouteIndex + "/" + emma3CarrierRoutePositions.Count + ".");
}

void UpdateEmma3CarrierRoute()
{
    if ((autopilotEnabled || hasTarget) && carrierAutopilotMode != CarrierAutopilotMode.Precision)
        return; // A cruise staging point is not the completed route destination.
    if (autopilotEnabled || hasTarget)
    {
        bool parkTarget = emma3CarrierRoutePositions.Count > 0 && emma3CarrierRouteIndex == emma3CarrierRoutePositions.Count;
        double tolerance = parkTarget ? arrivalTolerance : Math.Max(arrivalTolerance, 5);
        if (!IsAtTargetPose(targetPosition, desiredForward, desiredUp, tolerance))
            return;
        if (parkTarget)
        {
            if (flightCache.LinearVelocity.LengthSquared() > carrierStoppedSpeedMps * carrierStoppedSpeedMps
                || flightCache.AngularVelocity.LengthSquared() > carrierStoppedAngularSpeedRad * carrierStoppedAngularSpeedRad)
                return;
            if (!emma3CarrierParkAligning)
            {
                emma3CarrierParkAligning = true;
                int last = emma3CarrierRoutePositions.Count - 1;
                StartPrecisionTarget(emma3CarrierRoutePositions[last], emma3CarrierRouteForwards[last], emma3CarrierRouteUps[last]);
                MarkProgressStateDirty();
                ScriptLog("EMM-A 3: stopped at park; aligning parking pose.");
                return;
            }
        }

        autopilotEnabled = false;
        hasTarget = false;
        activeMaxSpeed = 0;
        ScriptLog("EMM-A 3: Carrier route " + emma3CarrierRouteIndex + "/" + emma3CarrierRoutePositions.Count + " reached.");
    }

    StartNextEmma3CarrierRoutePoint();
}

bool TryGetHangarPose(out Vector3D position, out Vector3D forward, out Vector3D up, out Vector3D halfExtent)
{
    string ignoredReason;
    return TryGetHangarPose(out position, out forward, out up, out halfExtent, out ignoredReason);
}

bool TryGetHangarPose(out Vector3D position, out Vector3D forward, out Vector3D up, out Vector3D halfExtent, out string failureReason)
{
    position = Vector3D.Zero;
    forward = Vector3D.Zero;
    up = Vector3D.Zero;
    halfExtent = Vector3D.Zero;
    failureReason = "";
    if (hangarBlock == null)
    {
        failureReason = "hangar block '" + hangarBlockName + "' was not found";
        return false;
    }

    if (hangarManagerTimer == null)
    {
        failureReason = "timer '" + hangarManagerTimerName + "' was not found";
        return false;
    }

    MyIni ini = new MyIni();
    MyIniParseResult result;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out result))
    {
        failureReason = "timer Custom Data is not valid INI: " + result.ToString();
        return false;
    }

    string poseStatus = ini.Get("HangarManagerPose", "Status").ToString("");
    if (!poseStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase))
    {
        string poseError = ini.Get("HangarManagerPose", "Error").ToString("");
        failureReason = "pose status is '" + (poseStatus.Length == 0 ? "missing" : poseStatus) + "'" + (poseError.Length == 0 ? "" : ": " + poseError);
        return false;
    }

    if (!TryParseV3(ini.Get("HangarManagerPose", "Center").ToString(""), out position))
    {
        failureReason = "Center is missing or invalid";
        return false;
    }

    if (!TryParseV3(ini.Get("HangarManagerPose", "Forward").ToString(""), out forward))
    {
        failureReason = "Forward is missing or invalid";
        return false;
    }

    if (!TryParseV3(ini.Get("HangarManagerPose", "Up").ToString(""), out up))
    {
        failureReason = "Up is missing or invalid";
        return false;
    }

    if (!TryParseV3(ini.Get("HangarManagerPose", "HalfExtent").ToString(""), out halfExtent))
    {
        failureReason = "HalfExtent is missing or invalid";
        return false;
    }

    if (forward.LengthSquared() < 0.001)
    {
        failureReason = "Forward has zero length";
        return false;
    }

    if (up.LengthSquared() < 0.001)
    {
        failureReason = "Up has zero length";
        return false;
    }

    if (halfExtent.Y <= 0)
    {
        failureReason = "HalfExtent.Y is not positive";
        return false;
    }

    forward = Vector3D.Normalize(forward);
    up = Vector3D.Normalize(up);
    return true;
}

bool TryGetFreshHangarPose(int expectedRequestId, out Vector3D position, out Vector3D forward, out Vector3D up, out Vector3D halfExtent, out string failureReason)
{
    if (!TryGetHangarPose(out position, out forward, out up, out halfExtent, out failureReason))
        return false;
    MyIni ini = new MyIni();
    MyIniParseResult result;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out result))
    {
        failureReason = "timer Custom Data changed while reading the fresh pose";
        return false;
    }
    int poseRequestId = ini.Get("HangarManagerPose", "RequestId").ToInt32(0);
    if (poseRequestId != expectedRequestId)
    {
        failureReason = "fresh pose request " + expectedRequestId + " does not match published pose request " + poseRequestId;
        return false;
    }
    string poseHangar = ini.Get("HangarManagerPose", "Hangar").ToString("");
    if (hangarBlock == null || !poseHangar.Equals(hangarBlock.CustomName, StringComparison.Ordinal))
    {
        failureReason = "fresh pose hangar '" + poseHangar + "' does not match configured hangar '" + (hangarBlock == null ? hangarBlockName : hangarBlock.CustomName) + "'";
        return false;
    }
    return true;
}

bool TryBuildHangarMinerCommand(string commandName, out string command)
{
    command = "";
    Vector3D position, forward, up, halfExtent;
    if (!TryGetHangarPose(out position, out forward, out up, out halfExtent))
        return false;
    command = commandName + "|" + V3ToString(position) + "|" + V3ToString(forward) + "|" + V3ToString(up) + "|" + NumberR(halfExtent.Y);
    if (ActiveMiner().HangarOperationId > 0)
        command += "|" + ActiveMiner().HangarOperationId;
    return true;
}

bool TryBuildCarrierDockPoseCommand(out string command)
{
    command = "";
    if (carrierConnector == null || carrierConnector.Closed) return false;
    command = "DockEndpoint|" + carrierConnector.EntityId;
    return true;
}

void SendCurrentCarrierDockPoseCommand()
{
    string command;
    if (!TryBuildCarrierDockPoseCommand(out command))
    {
        ScriptLog("Dock-pose request failed: Carrier connector is unavailable.");
        return;
    }

    SendMinerCommand(command);
}

bool SendMinerToHangar()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarStoreCommandSent)
        return true;
    if (miner.HangarManagerPending == HangarManagerPendingCommand.RefreshPose)
        return true;
    if (hangarManagerTimer == null)
    {
        AbortEmma3Mission("HangarManager timer is unavailable.");
        return false;
    }

    int requestId;
    if (!TriggerHangarManagerCommand("RefreshPose", out requestId))
    {
        AbortEmma3Mission("HangarStore could not request a fresh storage pose.");
        return false;
    }
    miner.HangarManagerPending = HangarManagerPendingCommand.RefreshPose;
    miner.HangarManagerRequestId = requestId;
    miner.HangarManagerWaitTicks = 0;
    miner.HangarPoseRequestId = 0;
    miner.HangarStoreCommandText = "";
    MarkProgressStateDirty();
    ScriptLog("Hangar: requested a fresh pose for the configured hangar; Miner remains docked until it is confirmed.");
    return true;
}

bool SendMinerHangarStoreCommandFromCurrentPose(out string failureReason)
{
    failureReason = "";
    MinerSlot miner = ActiveMiner();
    Vector3D position, forward, up, halfExtent;
    if (miner.HangarPoseRequestId <= 0 || !TryGetFreshHangarPose(miner.HangarPoseRequestId, out position, out forward, out up, out halfExtent, out failureReason))
        return false;
    string storeCommand = "HangarStore|" + V3ToString(position) + "|" + V3ToString(forward) + "|" + V3ToString(up) + "|" + NumberR(halfExtent.Y) + "|" + miner.HangarOperationId;
    if (!SendMinerCommand(storeCommand))
    {
        failureReason = "Miner programmable block rejected the HangarStore command";
        return false;
    }

    miner.HangarStoreAcceptedReceived = false;
    miner.HangarStoreReadyReceived = false;
    miner.HangarOnlineReceived = false;
    miner.HangarStoreCommandSent = true;
    miner.HangarStoreCommandText = storeCommand;
    miner.HangarManagerPending = HangarManagerPendingCommand.None;
    miner.HangarManagerRequestId = 0;
    miner.HangarManagerWaitTicks = 0;
    miner.ProgressPollTicks = 0;
    MarkProgressStateDirty();
    ScriptLog("Hangar: sent the storage-zone centre while leaving hangar power off until the Miner is stationary.");
    return true;
}

bool ResendMinerHangarStoreCommandFromCurrentPose(out string failureReason)
{
    failureReason = "";
    MinerSlot miner = ActiveMiner();
    if (string.IsNullOrWhiteSpace(miner.HangarStoreCommandText))
    {
        failureReason = "the persisted HangarStore command is missing";
        return false;
    }
    if (!SendMinerCommand(miner.HangarStoreCommandText))
    {
        failureReason = "Miner programmable block rejected the HangarStore command";
        return false;
    }

    miner.ProgressPollTicks = 0;
    return true;
}

void ResetHangarTransaction(MinerSlot miner, bool clearStoredState)
{
    miner.HangarStoreAcceptedReceived = false;
    miner.HangarStoreReadyReceived = false;
    miner.HangarOnlineReceived = false;
    miner.HangarMissionReadyReceived = false;
    if (clearStoredState)
        miner.HangarStored = false;
    miner.HangarStorePendingForSnapshot = false;
    miner.HangarStoreCommandSent = false;
    miner.HangarManagerPending = HangarManagerPendingCommand.None;
    miner.HangarManagerRequestId = 0;
    miner.HangarManagerWaitTicks = 0;
    miner.HangarMinerOnlineWaitTicks = 0;
    miner.HangarOperationId = 0;
    miner.HangarPoseRequestId = 0;
    miner.HangarStoreCommandText = "";
}

void ClearPendingHangarManagerCommand(MinerSlot miner)
{
    miner.HangarManagerPending = HangarManagerPendingCommand.None;
    miner.HangarManagerRequestId = 0;
    miner.HangarManagerWaitTicks = 0;
}

void StartManualHangarCycle()
{
    if (manualHangarCycleState != ManualHangarCycleState.None)
    {
        ScriptLog("Hangar cycle is already active.");
        return;
    }

    if (fullAutoEnabled || emma3MissionState != Emma3MissionState.None)
    {
        ScriptLog("HangarCycle denied: stop autonomous mining first.");
        return;
    }

    if (carrierConnector == null || GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected)
    {
        ScriptLog("HangarCycle denied: Miner must begin docked to the Carrier connector.");
        return;
    }

    if (hangarManagerTimer == null)
    {
        ScriptLog("HangarCycle denied: missing '" + hangarManagerTimerName + "'.");
        return;
    }

    MinerSlot miner = ActiveMiner();
    ResetHangarTransaction(miner, true);
    manualHangarCycleDelayTicks = 0;
    manualHangarCycleCommandRetryTicks = manualHangarCycleCommandRetryIntervalTicks;
    manualHangarCycleCommandWaitTicks = 0;
    manualHangarCycleState = ManualHangarCycleState.WaitingStoreReady;
    MarkProgressStateDirty();
    if (!SendMinerToHangar())
    {
        manualHangarCycleState = ManualHangarCycleState.None;
        MarkProgressStateDirty();
        ScriptLog("HangarCycle failed to start.");
        return;
    }

    ClearEmma3BridgeFailureForManualHangar();
    ScriptLog("HangarCycle started: Miner will service, enter the hangar, store, deploy, return to Dock Pose 1, and dock.");
}

void StartManualHangarStore()
{
    if (manualHangarCycleState != ManualHangarCycleState.None)
    {
        ScriptLog("HangarStore denied: another manual hangar operation is already active.");
        return;
    }

    if (fullAutoEnabled || emma3MissionState != Emma3MissionState.None)
    {
        ScriptLog("HangarStore denied: stop autonomous mining first.");
        return;
    }

    if (carrierConnector == null || GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected)
    {
        ScriptLog("HangarStore denied: Miner must begin docked to the Carrier connector.");
        return;
    }

    if (hangarManagerTimer == null)
    {
        ScriptLog("HangarStore denied: missing '" + hangarManagerTimerName + "'.");
        return;
    }

    MinerSlot miner = ActiveMiner();
    ResetHangarTransaction(miner, true);
    manualHangarCycleDelayTicks = 0;
    manualHangarCycleCommandRetryTicks = manualHangarCycleCommandRetryIntervalTicks;
    manualHangarCycleCommandWaitTicks = 0;
    manualHangarCycleState = ManualHangarCycleState.WaitingStoreOnlyReady;
    MarkProgressStateDirty();
    if (!SendMinerToHangar())
    {
        manualHangarCycleState = ManualHangarCycleState.None;
        MarkProgressStateDirty();
        ScriptLog("HangarStore failed to start.");
        return;
    }

    ClearEmma3BridgeFailureForManualHangar();
    ScriptLog("HangarStore started: Miner will service, leave the connector, enter the hangar, and remain stored.");
}

void StartManualHangarDeploy()
{
    if (manualHangarCycleState != ManualHangarCycleState.None)
    {
        ScriptLog("HangarDeploy denied: another manual hangar operation is already active.");
        return;
    }

    if (fullAutoEnabled || emma3MissionState != Emma3MissionState.None)
    {
        ScriptLog("HangarDeploy denied: stop autonomous mining first.");
        return;
    }

    if (hangarManagerTimer == null)
    {
        ScriptLog("HangarDeploy denied: missing '" + hangarManagerTimerName + "'.");
        return;
    }

    int confirmedCount;
    if (!TryGetFreshHangarInventoryCount(out confirmedCount) || confirmedCount <= 0)
    {
        ScriptLog("HangarDeploy denied: hangar contents must be confirmed Filled.");
        return;
    }

    if (carrierConnector != null && GetCarrierConnectorStatus() == MyShipConnectorStatus.Connected)
    {
        ScriptLog("HangarDeploy denied: a Miner is already connected to the Carrier.");
        return;
    }

    MinerSlot miner = ActiveMiner();
    ResetHangarTransaction(miner, false);
    miner.HangarStored = true;
    manualHangarCycleDelayTicks = 0;
    manualHangarCycleCommandRetryTicks = 0;
    manualHangarCycleCommandWaitTicks = 0;
    manualHangarCycleState = ManualHangarCycleState.DeployOnlyStart;
    MarkProgressStateDirty();
    ClearEmma3BridgeFailureForManualHangar();
    ScriptLog("HangarDeploy started: Miner A will deploy, return to Dock Pose 1, dock, and complete service.");
}

bool IsManualHangarDeployState(ManualHangarCycleState state)
{
    return state == ManualHangarCycleState.DeployOnlyStart
        || state == ManualHangarCycleState.WaitingDeployOnlyReady
        || state == ManualHangarCycleState.WaitingDeployOnlyReturnDock;
}

string GetManualHangarOperationName()
{
    if (IsManualHangarDeployState(manualHangarCycleState)) return "HangarDeploy";
    if (manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady) return "HangarStore";
    return "HangarCycle";
}

void UpdateManualHangarCycle()
{
    if (manualHangarCycleState == ManualHangarCycleState.None)
        return;
    MinerSlot miner = ActiveMiner();
    if (manualHangarCycleState == ManualHangarCycleState.WaitingStoreReady
        || manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady)
    {
        if (miner.HangarStoreReadyReceived || miner.HangarStoreAcceptedReceived || (carrierConnector != null && GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected))
        {
            manualHangarCycleCommandRetryTicks = 0;
            manualHangarCycleCommandWaitTicks = 0;
            return;
        }

        if (miner.HangarManagerPending == HangarManagerPendingCommand.RefreshPose)
            return;

        int delta = UpdateTickDelta();
        manualHangarCycleCommandWaitTicks += delta;
        manualHangarCycleCommandRetryTicks = Math.Max(0, manualHangarCycleCommandRetryTicks - delta);
        if (manualHangarCycleCommandWaitTicks >= manualHangarCycleCommandTimeoutTicks)
        {
            ResetManualHangarCycleState();
            SendMinerCommand("reset");
            TriggerHangarManagerCommand("Disable");
            ScriptLog("HangarCycle stopped: Miner did not acknowledge the storage command within 10 seconds.");
            return;
        }

        if (miner.HangarManagerPending == HangarManagerPendingCommand.Enable)
            return;
        if (manualHangarCycleCommandRetryTicks <= 0)
        {
            manualHangarCycleCommandRetryTicks = manualHangarCycleCommandRetryIntervalTicks;
            string retryFailure = "";
            bool retrySent = miner.HangarStoreCommandSent
                ? ResendMinerHangarStoreCommandFromCurrentPose(out retryFailure)
                : SendMinerToHangar();
            if (!retrySent)
            {
                ResetManualHangarCycleState();
                ScriptLog("HangarCycle stopped: storage-command retry failed" + (miner.HangarStoreCommandSent && retryFailure.Length > 0 ? ": " + retryFailure + "." : "."));
                return;
            }

            ScriptLog("HangarCycle: storage command resent while awaiting Miner acknowledgement.");
        }

        return;
    }

    if (manualHangarCycleState == ManualHangarCycleState.DeployDelay
        || manualHangarCycleState == ManualHangarCycleState.DeployOnlyStart)
    {
        bool deployOnly = manualHangarCycleState == ManualHangarCycleState.DeployOnlyStart;
        string operationName = deployOnly ? "HangarDeploy" : "HangarCycle";
        if (manualHangarCycleDelayTicks > 0)
        {
            manualHangarCycleDelayTicks = Math.Max(0, manualHangarCycleDelayTicks - UpdateTickDelta());
            return;
        }

        int requestId;
        if (!TriggerHangarManagerCommand("Deploy", out requestId))
        {
            manualHangarCycleState = ManualHangarCycleState.None;
            MarkProgressStateDirty();
            ScriptLog(operationName + " stopped: HangarManager could not deploy slot 1.");
            return;
        }

        miner.HangarStoreReadyReceived = false;
        miner.HangarOnlineReceived = false;
        miner.HangarMissionReadyReceived = false;
        miner.StatusAgeSeconds = 9999;
        miner.HangarManagerPending = HangarManagerPendingCommand.Deploy;
        miner.HangarManagerRequestId = requestId;
        miner.HangarManagerWaitTicks = 0;
        miner.HangarMinerOnlineWaitTicks = 0;
        manualHangarCycleState = deployOnly
            ? ManualHangarCycleState.WaitingDeployOnlyReady
            : ManualHangarCycleState.WaitingDeployReady;
        MarkProgressStateDirty();
        ScriptLog(operationName + ": deploying Miner A from slot 1.");
        return;
    }

    if (manualHangarCycleState == ManualHangarCycleState.WaitingDeployReady
        || manualHangarCycleState == ManualHangarCycleState.WaitingDeployOnlyReady)
    {
        bool deployOnly = manualHangarCycleState == ManualHangarCycleState.WaitingDeployOnlyReady;
        string operationName = deployOnly ? "HangarDeploy" : "HangarCycle";
        if (miner.HangarManagerPending == HangarManagerPendingCommand.Disable)
            return;
        if (miner.HangarManagerPending != HangarManagerPendingCommand.Deploy)
            manualHangarCycleCommandWaitTicks += UpdateTickDelta();
        if (manualHangarCycleCommandWaitTicks >= hangarMinerOnlineTimeoutTicks)
        {
            ResetManualHangarCycleState();
            TriggerHangarManagerCommand("Disable");
            ScriptLog(operationName + " stopped: deployed Miner did not come online within 30 seconds.");
            return;
        }

        if (!miner.HangarOnlineReceived)
            return;
        miner.HangarOnlineReceived = false;
        miner.HangarStored = false;
        string returnCommand;
        if (!TryBuildHangarMinerCommand("HangarCycleReturnDock", out returnCommand) || !SendMinerCommand(returnCommand))
        {
            ResetManualHangarCycleState();
            ScriptLog(operationName + " stopped: current hangar pose was unavailable or Miner rejected the return-to-dock command.");
            return;
        }

        manualHangarCycleState = deployOnly
            ? ManualHangarCycleState.WaitingDeployOnlyReturnDock
            : ManualHangarCycleState.WaitingReturnDock;
        manualHangarCycleCommandWaitTicks = 0;
        MarkProgressStateDirty();
        ScriptLog(operationName + ": Miner deployed; clearing the storage zone, then returning to Dock Pose 1 and docking.");
        return;
    }

    if (manualHangarCycleState == ManualHangarCycleState.WaitingReturnDock
        || manualHangarCycleState == ManualHangarCycleState.WaitingDeployOnlyReturnDock)
    {
        bool deployOnly = manualHangarCycleState == ManualHangarCycleState.WaitingDeployOnlyReturnDock;
        string operationName = deployOnly ? "HangarDeploy" : "HangarCycle";
        manualHangarCycleCommandWaitTicks += UpdateTickDelta();
        if (manualHangarCycleCommandWaitTicks >= hangarMinerOnlineTimeoutTicks * 2)
        {
            ResetManualHangarCycleState();
            TriggerHangarManagerCommand("Disable");
            ScriptLog(operationName + " stopped: Miner did not return and dock within 60 seconds.");
            return;
        }

        if (!miner.HangarMissionReadyReceived)
            return;
        miner.HangarMissionReadyReceived = false;
        manualHangarCycleState = ManualHangarCycleState.None;
        manualHangarCycleDelayTicks = 0;
        manualHangarCycleCommandRetryTicks = 0;
        manualHangarCycleCommandWaitTicks = 0;
        MarkProgressStateDirty();
        if (deployOnly)
            ScriptLog("HangarDeploy complete: Miner A is docked and serviced.");
        else
            ScriptLog("HangarCycle complete: Miner A is docked and serviced.");
    }
}

void ResetManualHangarCycleState()
{
    bool clearStoredState = !IsManualHangarDeployState(manualHangarCycleState);
    manualHangarCycleState = ManualHangarCycleState.None;
    manualHangarCycleDelayTicks = 0;
    manualHangarCycleCommandRetryTicks = 0;
    manualHangarCycleCommandWaitTicks = 0;
    MinerSlot miner = ActiveMiner();
    ResetHangarTransaction(miner, clearStoredState);
    MarkProgressStateDirty();
}

void CompleteManualHangarStore()
{
    manualHangarCycleState = ManualHangarCycleState.None;
    manualHangarCycleDelayTicks = 0;
    manualHangarCycleCommandRetryTicks = 0;
    manualHangarCycleCommandWaitTicks = 0;
    MinerSlot miner = ActiveMiner();
    ResetHangarTransaction(miner, false);
    miner.HangarStored = true;
    MarkProgressStateDirty();
    ScriptLog("HangarStore complete: Miner A is stored in the hangar.");
}

bool TriggerHangarManagerCommand(string command)
{
    int ignoredRequestId;
    return TriggerHangarManagerCommand(command, out ignoredRequestId);
}

bool TriggerHangarManagerCommand(string command, out int requestId)
{
    requestId = 0;
    if (hangarBlock == null || hangarManagerTimer == null)
    {
        ScriptLog("HangarManager: missing '" + (hangarBlock == null ? hangarBlockName : hangarManagerTimerName) + "'.");
        return false;
    }

    MyIni ini = new MyIni();
    MyIniParseResult result;
    ini.TryParse(hangarManagerTimer.CustomData ?? "", out result);
    requestId = Math.Max(ini.Get("HangarManager", "RequestId").ToInt32(0), ini.Get("HangarManagerResult", "RequestId").ToInt32(0)) + 1;
    ini.Set("HangarManager", "Command", command);
    ini.Set("HangarManager", "Hangar", hangarBlock.CustomName);
    ini.Set("HangarManager", "RequestId", requestId);
    ini.Set("HangarManagerResult", "Command", command);
    ini.Set("HangarManagerResult", "RequestId", 0);
    ini.Set("HangarManagerResult", "Status", "Requested");
    ini.Set("HangarManagerResult", "Message", "Waiting for HangarManager plugin.");
    if (command.Equals("Deploy", StringComparison.OrdinalIgnoreCase))
        ini.Set("HangarManager", "Slot", 1);
    else
        ini.Delete("HangarManager", "Slot");
    hangarManagerTimer.CustomData = ini.ToString();
    ScriptLog("HangarManager: queued automatic " + command + " request " + requestId + " for '" + hangarBlock.CustomName + "'.");
    return true;
}

bool TryReadHangarManagerResult(string command, int requestId, out string status, out string message)
{
    status = "";
    message = "";
    if (hangarManagerTimer == null)
        return false;
    MyIni ini = new MyIni();
    MyIniParseResult result;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out result))
        return false;
    if (!ini.Get("HangarManagerResult", "Command").ToString("").Equals(command, StringComparison.OrdinalIgnoreCase) || ini.Get("HangarManagerResult", "RequestId").ToInt32(0) != requestId)
        return false;
    status = ini.Get("HangarManagerResult", "Status").ToString("");
    message = ini.Get("HangarManagerResult", "Message").ToString("");
    return true;
}

void FailHangarManagerCommand(string message)
{
    if (manualHangarCycleState != ManualHangarCycleState.None)
    {
        string operationName = GetManualHangarOperationName();
        ResetManualHangarCycleState();
        SendMinerCommand("reset");
        ScriptLog(operationName + " stopped: " + message);
        return;
    }

    AbortEmma3Mission(message);
}

void UpdateHangarManagerRefreshPoseResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending != HangarManagerPendingCommand.RefreshPose)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    string status, message;
    if (!TryReadHangarManagerResult("RefreshPose", miner.HangarManagerRequestId, out status, out message))
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not acknowledge the fresh pose request within 10 seconds. Miner remained docked.");
        return;
    }
    if (status.Equals("Requested", StringComparison.OrdinalIgnoreCase) || status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not publish a fresh pose within 10 seconds. Miner remained docked.");
        return;
    }
    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand((message.Length == 0 ? "HangarManager could not publish a fresh pose" : message) + ". Miner remained docked.");
        return;
    }
    if (carrierConnector == null || GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected)
    {
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("Fresh hangar pose arrived after the Miner was no longer connected; storage was not started.");
        return;
    }
    int requestId = miner.HangarManagerRequestId;
    miner.HangarPoseRequestId = requestId;
    miner.HangarOperationId = requestId;
    string failureReason;
    if (!SendMinerHangarStoreCommandFromCurrentPose(out failureReason))
    {
        ClearPendingHangarManagerCommand(miner);
        miner.HangarPoseRequestId = 0;
        miner.HangarOperationId = 0;
        FailHangarManagerCommand("HangarStore could not use the fresh pose: " + failureReason + ". Miner remained docked.");
        return;
    }
    ScriptLog("Hangar: matching fresh pose confirmed; storage command sent to the docked Miner.");
}

void UpdateHangarManagerRefreshContentsResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending != HangarManagerPendingCommand.RefreshContents)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    string status, message;
    if (!TryReadHangarManagerResult("RefreshContents", miner.HangarManagerRequestId, out status, out message)
        || status.Equals("Requested", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Pending", StringComparison.OrdinalIgnoreCase)
        || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < hangarManagerInventoryRefreshTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        AbortEmma3Mission("HangarManager did not confirm the requested contents refresh within 40 seconds.");
        return;
    }

    ClearPendingHangarManagerCommand(miner);
    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        AbortEmma3Mission(message.Length == 0 ? "Hangar contents refresh failed; occupancy remains Unknown." : message);
        return;
    }

    ScriptLog("HangarManager: fresh hangar contents confirmed.");
}

void UpdateHangarManagerEnableResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending == HangarManagerPendingCommand.StoreDelay)
    {
        miner.HangarManagerWaitTicks += UpdateTickDelta();
        if (miner.HangarManagerWaitTicks < hangarStorePowerOnDelayTicks)
            return;
        int storeRequestId;
        if (!TriggerHangarManagerCommand("Store", out storeRequestId))
        {
            TriggerHangarManagerCommand("Disable");
            FailHangarManagerCommand("HangarManager store request could not be queued after power-on.");
            return;
        }

        miner.HangarManagerPending = HangarManagerPendingCommand.Store;
        miner.HangarManagerRequestId = storeRequestId;
        miner.HangarManagerWaitTicks = 0;
        miner.HangarStoreAcceptedReceived = false;
        MarkProgressStateDirty();
        ScriptLog("Hangar: one-second power-on delay complete; storage requested.");
        return;
    }
    if (miner.HangarManagerPending != HangarManagerPendingCommand.Enable)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    string status, message;
    if (!TryReadHangarManagerResult("Enable", miner.HangarManagerRequestId, out status, out message))
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        TriggerHangarManagerCommand("Disable");
        FailHangarManagerCommand("HangarManager did not acknowledge the automatic Enable request within 10 seconds.");
        return;
    }

    if (status.Equals("Requested", StringComparison.OrdinalIgnoreCase) || status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < hangarManagerStoreOperationTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        TriggerHangarManagerCommand("Disable");
        FailHangarManagerCommand("HangarManager did not finish hangar inspection and Enable within 65 seconds.");
        return;
    }

    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        ClearPendingHangarManagerCommand(miner);
        TriggerHangarManagerCommand("Disable");
        MarkProgressStateDirty();
        FailHangarManagerCommand(message.Length == 0 ? "HangarManager could not power on the hangar." : message);
        return;
    }

    miner.HangarManagerPending = HangarManagerPendingCommand.StoreDelay;
    miner.HangarManagerRequestId = 0;
    miner.HangarManagerWaitTicks = 0;
    MarkProgressStateDirty();
    ScriptLog("Hangar: power-on confirmed; waiting one second before storage.");
}

void HandleHangarStoreReady()
{
    bool manualCycleStore = manualHangarCycleState == ManualHangarCycleState.WaitingStoreReady
        || manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady;
    if ((!minerHangarEnabled && !manualCycleStore) || !ActiveMiner().HangarStoreCommandSent || ActiveMiner().HangarStored)
        return;
    if (ActiveMiner().HangarManagerPending != HangarManagerPendingCommand.None)
        return;
    int requestId;
    if (!TriggerHangarManagerCommand("Enable", out requestId))
    {
        FailHangarManagerCommand("HangarManager could not queue the hangar power-on request.");
        return;
    }

    ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.Enable;
    ActiveMiner().HangarManagerRequestId = requestId;
    ActiveMiner().HangarManagerWaitTicks = 0;
    MarkProgressStateDirty();
    ScriptLog("Hangar: Miner is stationary in position; hangar power-on requested once.");
}

string hangarInventoryCheckedUtc = "";
string hangarInventoryObservedRevision;
long hangarInventoryObservedTick;
bool hangarInventoryRefreshed;

bool ObserveHangarInventoryRevision(MyIni ini)
{
    string revision = ini.Get("HangarManagerInventory", "Revision").ToString(ini.Get("HangarManagerInventory", "CheckedUtc").ToString(""));
    if (revision.Length == 0) { if (hangarInventoryObservedRevision == null) hangarInventoryObservedRevision = ""; return false; }
    if (revision != hangarInventoryObservedRevision)
    {
        // The first reading can be persisted Custom Data. Only a changed
        // publication proves fresh activity; client/server UTC never compares.
        hangarInventoryRefreshed = hangarInventoryObservedRevision != null;
        hangarInventoryObservedRevision = revision;
        hangarInventoryObservedTick = Runtime.LifetimeTicks;
    }
    return hangarInventoryRefreshed && Runtime.LifetimeTicks >= hangarInventoryObservedTick
        && Runtime.LifetimeTicks - hangarInventoryObservedTick <= 1800;
}
int hangarInventoryPollTicks;
void EnsureHangarManagerConfiguration()
{
    if (hangarManagerTimer == null) return;
    MyIni ini = new MyIni();
    MyIniParseResult error;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out error))
    {
        ScriptLog("HangarManager: timer Custom Data is invalid; hangar name was not changed.");
        return;
    }

    string configuredName = ini.Get("HangarManager", "Hangar").ToString("");
    if (configuredName.Equals(hangarBlockName, StringComparison.Ordinal)) return;
    ini.Set("HangarManager", "Hangar", hangarBlockName);
    hangarManagerTimer.CustomData = ini.ToString();
    ScriptLog("HangarManager: configured hangar '" + hangarBlockName + "'.");
}

bool TryGetFreshHangarInventoryCount(out int count)
{
    count = -1;
    if (hangarManagerTimer == null) return false;
    MyIni ini = new MyIni();
    MyIniParseResult error;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out error)) return false;
    IMyTerminalBlock hangar = hangarBlock ?? GridTerminalSystem.GetBlockWithName(hangarBlockName);
    if (hangar == null || !ini.TryParse(hangar.CustomData ?? "", out error)) return false;
    bool fresh = ObserveHangarInventoryRevision(ini);
    if (!fresh || !ini.Get("HangarManagerInventory", "Status").ToString("").Equals("Ready", StringComparison.OrdinalIgnoreCase)) return false;
    count = ini.Get("HangarManagerInventory", "Count").ToInt32(-1);
    return count >= 0;
}

void UpdateHangarInventorySnapshot()
{
    if (hangarInventoryPollTicks > 0) { hangarInventoryPollTicks -= UpdateTickDelta(); return; }
    hangarInventoryPollTicks = 60;
    MinerSlot miner = ActiveMiner();
    if (hangarManagerTimer == null || miner.HangarManagerPending != HangarManagerPendingCommand.None)
        return;
    MyIni ini = new MyIni();
    MyIniParseResult error;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out error)) return;
    IMyTerminalBlock hangar = hangarBlock ?? GridTerminalSystem.GetBlockWithName(hangarBlockName);
    if (hangar == null || !ini.TryParse(hangar.CustomData ?? "", out error)) return;
    bool fresh = ObserveHangarInventoryRevision(ini);
    if (!fresh || !ini.Get("HangarManagerInventory", "Status").ToString("").Equals("Ready", StringComparison.OrdinalIgnoreCase)) return;
    string stamp = hangarInventoryObservedRevision;
    if (stamp == hangarInventoryCheckedUtc) return;
    int count = ini.Get("HangarManagerInventory", "Count").ToInt32(-1);
    if (count < 0) return;
    hangarInventoryCheckedUtc = stamp;
    bool stored = count > 0;
    if (miner.HangarStored == stored) return;
    miner.HangarStored = stored;
    MarkProgressStateDirty();
    ScriptLog("Hangar: refreshed inventory reports " + count + " stored grid(s).");
}

void UpdateHangarManagerStoreResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending != HangarManagerPendingCommand.Store)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    if (hangarManagerTimer == null)
    {
        if (miner.HangarManagerWaitTicks >= manualHangarCycleCommandTimeoutTicks)
        {
            ClearPendingHangarManagerCommand(miner);
            FailHangarManagerCommand("HangarManager timer disappeared while Store was pending.");
        }

        return;
    }

    MyIni ini = new MyIni();
    MyIniParseResult result;
    if (!ini.TryParse(hangarManagerTimer.CustomData ?? "", out result))
        return;
    if (!ini.Get("HangarManagerResult", "Command").ToString("").Equals("Store", StringComparison.OrdinalIgnoreCase) || ini.Get("HangarManagerResult", "RequestId").ToInt32(0) != miner.HangarManagerRequestId)
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not acknowledge the automatic Store request within 10 seconds.");
        return;
    }

    string status = ini.Get("HangarManagerResult", "Status").ToString("");
    if (status.Equals("Requested", StringComparison.OrdinalIgnoreCase) || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not start the automatic Store request within 10 seconds.");
        return;
    }

    if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
    {
        if (miner.HangarManagerWaitTicks < hangarManagerStoreOperationTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not finish inspecting and storing Miner A within 65 seconds.");
        return;
    }

    ClearPendingHangarManagerCommand(miner);
    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        TriggerHangarManagerCommand("Disable");
        string message = ini.Get("HangarManagerResult", "Message").ToString("Hangar storage failed.");
        if (manualHangarCycleState != ManualHangarCycleState.None)
        {
            ResetManualHangarCycleState();
            SendMinerCommand("reset");
            ScriptLog("HangarCycle stopped: " + message);
        }
        else
            AbortEmma3Mission("Hangar storage failed: " + message);
        return;
    }

    miner.HangarStored = true;
    miner.HangarStoreAcceptedReceived = false;
    miner.HangarStoreCommandSent = false;
    miner.HangarStorePendingForSnapshot = false;
    MarkProgressStateDirty();
    if (!BeginTrackedHangarDisable(miner, "Miner A was stored, but the hangar power-off request could not be queued."))
        return;
    ScriptLog("HangarManager: storage confirmed; waiting for hangar power-off confirmation.");
}

bool BeginTrackedHangarDisable(MinerSlot miner, string failureMessage)
{
    int disableRequestId;
    if (!TriggerHangarManagerCommand("Disable", out disableRequestId))
    {
        FailHangarManagerCommand(failureMessage);
        return false;
    }
    miner.HangarManagerPending = HangarManagerPendingCommand.Disable;
    miner.HangarManagerRequestId = disableRequestId;
    miner.HangarManagerWaitTicks = 0;
    MarkProgressStateDirty();
    return true;
}

void UpdateHangarManagerDisableResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending != HangarManagerPendingCommand.Disable)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    string status, message;
    if (!TryReadHangarManagerResult("Disable", miner.HangarManagerRequestId, out status, out message))
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not acknowledge the automatic Disable request within 10 seconds.");
        return;
    }

    if (status.Equals("Requested", StringComparison.OrdinalIgnoreCase) || status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not confirm hangar power-off within 10 seconds.");
        return;
    }

    ClearPendingHangarManagerCommand(miner);
    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        FailHangarManagerCommand(message.Length == 0 ? "HangarManager could not turn off the hangar." : message);
        return;
    }

    if (manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady)
    {
        CompleteManualHangarStore();
        return;
    }
    if (manualHangarCycleState == ManualHangarCycleState.WaitingStoreReady)
    {
        manualHangarCycleDelayTicks = manualHangarCycleStoreDelayTicks;
        manualHangarCycleState = ManualHangarCycleState.DeployDelay;
        MarkProgressStateDirty();
        ScriptLog("HangarCycle: hangar power-off confirmed; waiting briefly before deployment.");
        return;
    }
    if (emma3MissionState == Emma3MissionState.WaitingHangarStore)
    {
        ScriptLog("Hangar: storage and power-off confirmed; Carrier transit may begin.");
        emma3MissionState = Emma3MissionState.FlyingCarrierRoute;
        BeginCarrierTransitFlightCache();
        StartNextEmma3CarrierRoutePoint();
        return;
    }
    if (emma3MissionState == Emma3MissionState.WaitingHangarDeploy)
    {
        RequestEmma3MinerPlanForActiveAsteroid();
        ScriptLog("Hangar: deployment and power-off confirmed; requesting the parked-asteroid Miner route.");
        return;
    }
    if (emma3MissionState != Emma3MissionState.WaitingPostCompletionHangarStore)
        return;
    if (emma3AllowSeatExitDuringMining && !IsCarrierControlled())
    {
        emma3MissionState = Emma3MissionState.None;
        emma3WaitForSeatBeforeNextPlan = true;
        MarkProgressStateDirty();
        ScriptLog("EMM-A 3: Miner stored and hangar off; waiting for Carrier seat before next asteroid.");
        return;
    }

    ScriptLog("EMM-A 3: Miner stored and hangar off; requesting the next Carrier route.");
    RequestEmma3CarrierPlanForStoredMiner();
}

void UpdateHangarManagerDeployResult()
{
    MinerSlot miner = ActiveMiner();
    if (miner.HangarManagerPending != HangarManagerPendingCommand.Deploy)
        return;
    miner.HangarManagerWaitTicks += UpdateTickDelta();
    string status, message;
    if (!TryReadHangarManagerResult("Deploy", miner.HangarManagerRequestId, out status, out message))
    {
        if (miner.HangarManagerWaitTicks < manualHangarCycleCommandTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not acknowledge the automatic Deploy request within 10 seconds.");
        return;
    }

    if (status.Equals("Requested", StringComparison.OrdinalIgnoreCase) || status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Length == 0)
    {
        if (miner.HangarManagerWaitTicks < hangarManagerDeployOperationTimeoutTicks)
            return;
        ClearPendingHangarManagerCommand(miner);
        FailHangarManagerCommand("HangarManager did not finish inspection and confirm deployment within 65 seconds.");
        return;
    }

    int deployRequestId = miner.HangarManagerRequestId;
    ClearPendingHangarManagerCommand(miner);
    MarkProgressStateDirty();
    if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
    {
        FailHangarManagerCommand(message.Length == 0 ? "HangarManager could not deploy Miner A." : message);
        return;
    }

    miner.HangarStored = false;
    miner.HangarOperationId = deployRequestId;
    MarkProgressStateDirty();
    if (!BeginTrackedHangarDisable(miner, "Miner A was deployed, but the hangar power-off request could not be queued."))
        return;
    ScriptLog("HangarManager: physical deployment confirmed; waiting for hangar power-off confirmation.");
}

void StartHangarDeploy()
{
    int requestId;
    if (!TriggerHangarManagerCommand("Deploy", out requestId))
    {
        AbortEmma3Mission("HangarManager deploy timer is unavailable or not configured.");
        return;
    }

    // AwaitingStore is persisted by the Miner while it is stored. Use the
    // phase-neutral online signal after deployment; store_ready still means
    // physically centered and is accepted only during the store phase.
    ActiveMiner().HangarStoreReadyReceived = false;
    ActiveMiner().HangarOnlineReceived = false;
    ActiveMiner().HangarMissionReadyReceived = false;
    ActiveMiner().StatusAgeSeconds = 9999;
    ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.Deploy;
    ActiveMiner().HangarManagerRequestId = requestId;
    ActiveMiner().HangarOperationId = requestId;
    ActiveMiner().HangarManagerWaitTicks = 0;
    ActiveMiner().HangarMinerOnlineWaitTicks = 0;
    ActiveMiner().ProgressPollTicks = 0;
    emma3MissionState = Emma3MissionState.WaitingHangarDeploy;
    ScriptLog("Hangar: deploying Miner A from slot 1.");
}

void CompleteEmma3Mission()
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
    {
        AbortEmma3Mission("Plugin bridge became unavailable before asteroid completion could be recorded.");
        return;
    }

    CompleteAsteroidReport();
    EndCarrierTransitFlightCache();
    ResetMinerRecallState();
    emma3CompletionSequence = Math.Max(emma3CompletionSequence, ini.Get("EMMA3", "completionSequence").ToInt32(0)) + 1;
    ini.Set("EMMA3", "completionSequence", emma3CompletionSequence);
    ini.Set("EMMA3", "completedEntityId", emma3ActiveEntityId.ToString());
    ini.Set("EMMA3", "state", "completed");
    ini.Set("EMMA3", "message", "Miner completed path and docked");
    WriteEmma3Bridge(ini);
    emma3MissionState = Emma3MissionState.WaitingCompletionAck;
    MarkProgressStateDirty();
    ScriptLog("EMM-A 3: Miner docked and unloaded; waiting for plugin completion acknowledgement.");
}

void UpdateEmma3CompletionAck()
{
    if (emma3MissionState != Emma3MissionState.WaitingCompletionAck)
        return;
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
    {
        AbortEmma3Mission("Plugin bridge became unavailable while recording asteroid completion.");
        return;
    }
    if (ini.Get("EMMA3", "completionAck").ToInt32(0) < emma3CompletionSequence)
        return;
    BeginPostCompletionHangarStore();
}

void BeginPostCompletionHangarStore()
{
    ResetMinerPathCompleteState();
    emma3MissionState = Emma3MissionState.None;
    emma3ActiveEntityId = 0;
    emma3MissionId = "";
    if (finishAndHold)
    {
        finishAndHold = false;
        fullAutoEnabled = false;
        StopParkedReportClock();
        emma3WaitForSeatBeforeNextPlan = false;
        WriteEmma3Settings();
        RequestCarrierSave(true);
        ScriptLog("Finish & Hold: asteroid completed and unloaded; autonomous mining is Off.");
        return;
    }

    if (minerHangarEnabled)
    {
        ResetHangarTransaction(ActiveMiner(), true);
        emma3MissionState = Emma3MissionState.WaitingPostCompletionHangarStore;
        if (!SendMinerToHangar())
            return;
        ScriptLog("EMM-A 3: completion recorded; storing Miner A before planning the next asteroid.");
        return;
    }

    if (emma3AllowSeatExitDuringMining && !IsCarrierControlled())
    {
        emma3WaitForSeatBeforeNextPlan = true;
        ScriptLog("EMM-A 3: mission completed; waiting for Carrier seat before next asteroid.");
        return;
    }

    ScriptLog("EMM-A 3: mission completed; requesting next asteroid.");
}

void RequestEmma3Reset()
{
    AbortEmma3Mission("Clean reset requested.");
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
    {
        ScriptLog("EMM-A 3 reset failed: bridge timer missing.");
        return;
    }

    emma3ResetSequence = Math.Max(emma3ResetSequence, ini.Get("EMMA3", "resetSequence").ToInt32(0)) + 1;
    ini.Set("EMMA3", "resetSequence", emma3ResetSequence);
    ini.Set("EMMA3", "command", "reset");
    ini.Set("EMMA3", "state", "requested");
    ini.Set("EMMA3", "message", "Carrier requested clean plugin reset");
    WriteEmma3Bridge(ini);
    ScriptLog("EMM-A 3: clean reset requested for IDs, scans, caches, and paths.");
}

void AbortEmma3Mission(string reason)
{
    StopParkedReportClock();
    reportApproachSeconds = 0;
    finishAndHold = false;
    EndCarrierTransitFlightCache();
    ResetMinerRecallState();
    ResetMinerPathCompleteState();
    minerPathStartAcceptedReceived = false;
    minerPathStartPreparingReceived = false;
    minerPathStartWaitTicks = 0;
    minerPathStartRetryTicks = 0;
    cockpitAbortPaused = false;
    cockpitAbortResumeAutopilot = false;
    fullAutoEnabled = false;
    emma3MissionState = Emma3MissionState.None;
    emma3WaitForSeatBeforeNextPlan = false;
    ResetHangarTransaction(ActiveMiner(), false);
    ClearConflictingAutomationForEmma3();
    SendMinerCommand("stop");
    MyIni ini;
    if (TryReadEmma3Bridge(out ini))
    {
        ini.Set("EMMA3", "state", "aborted");
        ini.Set("EMMA3", "message", reason);
        WriteEmma3Bridge(ini);
    }

    ScriptLog("EMM-A 3 ABORT: " + reason);
}

bool IsCarrierConnectorConnected()
{
    return carrierConnector != null && GetCarrierConnectorStatus() == MyShipConnectorStatus.Connected;
}

void BeginFullAutoStoredMinerStartup()
{
    int confirmedCount;
    if (!TryGetFreshHangarInventoryCount(out confirmedCount))
    {
        MinerSlot miner = ActiveMiner();
        int requestId;
        if (!TriggerHangarManagerCommand("RefreshContents", out requestId))
        {
            AbortEmma3Mission("HangarManager could not request a fresh contents check.");
            return;
        }
        miner.HangarManagerPending = HangarManagerPendingCommand.RefreshContents;
        miner.HangarManagerRequestId = requestId;
        miner.HangarManagerWaitTicks = 0;
        miner.HangarMinerOnlineWaitTicks = 0;
        emma3MissionState = Emma3MissionState.WaitingStartupHangarInventory;
        MarkProgressStateDirty();
        ScriptLog("Fullauto: requested a fresh hangar inventory check.");
        return;
    }

    if (confirmedCount <= 0)
    {
        AbortEmma3Mission("Miner is not docked and the hangar is confirmed Empty.");
        return;
    }

    ActiveMiner().HangarStored = true;
    RequestEmma3CarrierPlanForStoredMiner();
    ScriptLog("Fullauto: stored Miner confirmed; planning Carrier transit without deployment.");
}

void UpdateFullAutoStoredMinerStartup()
{
    MinerSlot miner = ActiveMiner();
    if (emma3MissionState == Emma3MissionState.WaitingStartupHangarInventory)
    {
        if (IsCarrierConnectorConnected())
        {
            ClearPendingHangarManagerCommand(miner);
            miner.HangarMinerOnlineWaitTicks = 0;
            emma3MissionState = Emma3MissionState.None;
            MarkProgressStateDirty();
            ScriptLog("Fullauto: Miner connected while checking the hangar; requesting the first plan.");
            return;
        }

        if (miner.HangarManagerPending == HangarManagerPendingCommand.RefreshContents)
            return;
        int confirmedCount;
        if (!TryGetFreshHangarInventoryCount(out confirmedCount))
        {
            AbortEmma3Mission("HangarManager completed the contents refresh without publishing a fresh result.");
            return;
        }

        if (confirmedCount <= 0)
        {
            AbortEmma3Mission("Miner is not docked and the hangar is confirmed Empty.");
            return;
        }

        miner.HangarStored = true;
        RequestEmma3CarrierPlanForStoredMiner();
        ScriptLog("Fullauto: stored Miner confirmed; planning Carrier transit without deployment.");
        return;
    }
}

void UpdateEmma3FullAuto()
{
    if (emma3MissionState == Emma3MissionState.WaitingCompletionAck)
    {
        UpdateEmma3CompletionAck();
        return;
    }

    if (emma3MissionState == Emma3MissionState.WaitingPostCompletionHangarStore)
        return;

    if (emma3MissionState == Emma3MissionState.WaitingMinerStart)
    {
        UpdateMinerPathStartHandshake();
        return;
    }

    if (emma3MissionState == Emma3MissionState.None)
    {
        if (emma3WaitForSeatBeforeNextPlan)
        {
            if (!IsCarrierControlled())
                return;
            emma3WaitForSeatBeforeNextPlan = false;
            if (minerHangarEnabled)
            {
                if (ActiveMiner().HangarStored)
                {
                    ScriptLog("EMM-A 3: Carrier seat occupied; requesting the next Carrier route for stored Miner A.");
                    RequestEmma3CarrierPlanForStoredMiner();
                    return;
                }

                ResetHangarTransaction(ActiveMiner(), true);
                ActiveMiner().HangarStorePendingForSnapshot = true;
                RequestEmma3Plan();
                ScriptLog("EMM-A 3: Carrier seat occupied; publishing the next plan before storing Miner A.");
                return;
            }

            ScriptLog("EMM-A 3: Carrier seat occupied; requesting next asteroid.");
        }

        if (minerHangarEnabled && !IsCarrierConnectorConnected())
        {
            BeginFullAutoStoredMinerStartup();
            return;
        }

        if (minerHangarEnabled)
        {
            ResetHangarTransaction(ActiveMiner(), true);
            ActiveMiner().HangarStorePendingForSnapshot = true;
            RequestEmma3Plan();
            ScriptLog("EMM-A 3: first plan will be published before storing Miner A for Carrier transit.");
            return;
        }

        RequestEmma3Plan();
        return;
    }

    if (emma3MissionState == Emma3MissionState.WaitingStartupHangarInventory)
    {
        UpdateFullAutoStoredMinerStartup();
        return;
    }

    if (emma3MissionState == Emma3MissionState.WaitingPlan
        || emma3MissionState == Emma3MissionState.WaitingMinerPlan
        || emma3MissionState == Emma3MissionState.PreviewReady)
    {
        if (carrierPlanLoadJob != null)
        {
            int loadResult = PumpEmma3ReadyPlanLoad();
            if (loadResult < 0)
                AbortEmma3Mission("Plugin returned an invalid Carrier route.");
            else if (loadResult > 0)
                ActivateLoadedEmma3Plan();
            return;
        }
        if (emma3PlanPollTicks > 0)
        {
            emma3PlanPollTicks--;
            return;
        }

        emma3PlanPollTicks = emma3BridgeStatusIntervalTicks - 1;
        string state = emma3PluginState;
        if (state == "missing")
        {
            AbortEmma3Mission("Plugin bridge became unavailable.");
            return;
        }

        if (state == "error" || state == "aborted")
        {
            AbortEmma3Mission(emma3PluginMessage.Length == 0 ? "plugin planning failed" : emma3PluginMessage);
            return;
        }

        // The plugin still needs the physical Miner path timer when it publishes,
        // so storage waits for the fully written and validated next plan.
        if (ActiveMiner().HangarStorePendingForSnapshot && state == "ready")
        {
            ActiveMiner().HangarStorePendingForSnapshot = false;
            if (!SendMinerToHangar())
                return;
        }

        if (state == "ready_dryrun")
        {
            emma3MissionState = Emma3MissionState.PreviewReady;
            return;
        }

        if (state != "ready")
            return;
        MyIni ini;
        if (!TryReadEmma3Bridge(out ini))
        {
            AbortEmma3Mission("Plugin bridge became unavailable.");
            return;
        }

        if (emma3MissionState == Emma3MissionState.WaitingMinerPlan)
        {
            StartDeployedMinerFromReadyPlan(ini);
            return;
        }

        if (!BeginEmma3ReadyPlanLoad(ini))
        {
            AbortEmma3Mission("Plugin returned an invalid Carrier route.");
            return;
        }
        return;
    }

    if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute)
    {
        UpdateEmma3CarrierRoute();
        return;
    }

    if (emma3MissionState == Emma3MissionState.WaitingStationary)
    {
        double speed = flightCache.LinearVelocity.Length();
        if (speed <= Math.Max(0, mineStationarySpeedMps))
            mineStationaryTicks++;
        else
            mineStationaryTicks = 0;
        if (mineStationaryTicks < Math.Max(1, mineStationaryConfirmTicks))
            return;
        if (ActiveMiner().HangarStored)
        {
            StartHangarDeploy();
            return;
        }

        StartAsteroidReport();
        ResetMinerPathCompleteState();
        ResetMinerRecallState();
        BeginMinerPathStartHandshake();
        return;
    }

    if (emma3MissionState == Emma3MissionState.WaitingHangarStore)
        return;
    if (emma3MissionState == Emma3MissionState.WaitingMiner && AllAssignedMinersDone())
    {
        MinerSlot miner = ActiveMiner();
        if (carrierConnector == null || GetCarrierConnectorStatus() != MyShipConnectorStatus.Connected)
        {
            AbortEmma3Mission("Miner reported completion but is not docked.");
            return;
        }

        if (miner.PathCompleteKind.Equals("recall", StringComparison.OrdinalIgnoreCase))
        {
            finishAndHold = false;
            RequestEmma3PlannerAbort("Miner recalled and docked.");
            ResetMinerRecallState();
            ResetMinerPathCompleteState();
            emma3MissionState = Emma3MissionState.None;
            emma3ActiveEntityId = 0;
            emma3MissionId = "";
            fullAutoEnabled = false;
            StopParkedReportClock();
            emma3WaitForSeatBeforeNextPlan = false;
            ScriptLog("EMM-A 3: Miner recalled and docked; autonomous mining stopped without completing the asteroid.");
            return;
        }

        if (!miner.PathCompleteKind.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            ScriptLog("EMM-A 3: ignored Miner completion with unknown completion kind.");
            ResetMinerPathCompleteState();
            return;
        }

        CompleteEmma3Mission();
    }
}

void UpdateFullAuto()
{
    if (fullAutoEnabled)
        UpdateEmma3FullAuto();
}

bool IsCarrierControlled()
{
    if (rc == null)
        return false;
    if (rc.IsUnderControl)
        return true;
    for (int i = 0; i < carrierControllers.Count; i++)
        if (carrierControllers[i] != null && carrierControllers[i].IsUnderControl)
            return true;
    return false;
}

void StartPathFromTimer()
{
    List<string> loaded;
    string timerName;
    if (!TryLoadPathRecordsFromTimer(out loaded, out timerName))
    {
        ScriptLog($"Unable to load path records from timer tag '{pathTimerTag}'.");
        return;
    }

    pathRecords = loaded;
    pathIndex = 0;
    pathMode = true;
    pathPaused = false;
    pathPausedNavResume = false;
    ScriptLog($"Loaded {pathRecords.Count} records from timer '{timerName}'.");
    StartNextPathRecord();
}

void StartNextPathRecord()
{
    while (pathMode && pathIndex < pathRecords.Count)
    {
        string record = pathRecords[pathIndex];
        pathIndex++;
        if (ExecutePathRecord(record))
            return;
        ScriptLog($"Skipping invalid path record #{pathIndex}.");
    }

    if (pathMode && pathIndex >= pathRecords.Count)
    {
        pathMode = false;
        ScriptLog("Path execution complete.");
    }
}

bool ExecutePathRecord(string rawRecord)
{
    if (string.IsNullOrWhiteSpace(rawRecord))
        return false;
    string record = rawRecord.Trim();
    if (record.StartsWith("\"") && record.EndsWith("\"") && record.Length >= 2)
        record = record.Substring(1, record.Length - 2).Trim();
    if (string.IsNullOrWhiteSpace(record))
        return false;
    string navCmd = null;
    if (record.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase))
        navCmd = record.Substring(4).Trim();
    else if (record.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        navCmd = record;
    if (navCmd != null)
    {
        Vector3D gps, forward, up;
        if (!TryParseGpsCommand(navCmd, out gps, out forward, out up))
            return false;
        StartPrecisionTarget(gps, forward, up);
        return true;
    }

    if (pathAnchorValid)
    {
        Vector3D pos, fwd, upN;
        if (TryParsePathPointLine(record, pathAnchorPos, out pos, out fwd, out upN))
        {
            StartPrecisionTarget(pos, fwd, upN);
            return true;
        }
    }

    return false;
}

bool TryLoadPathRecordsFromTimer(out List<string> records, out string timerName)
{
    records = new List<string>();
    timerName = "";
    pathAnchorValid = false;
    pathAnchorPos = Vector3D.Zero;
    pathAnchorForward = Vector3D.Zero;
    pathAnchorUp = Vector3D.Zero;
    IMyTimerBlock timer = FindPathTimer();
    if (timer == null)
        return false;
    timerName = timer.CustomName;
    string data = timer.CustomData ?? "";
    string[] lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (string line in lines)
    {
        string rec = line.Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (rec.StartsWith("\"") && rec.EndsWith("\"") && rec.Length >= 2)
            rec = rec.Substring(1, rec.Length - 2).Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (!pathAnchorValid)
        {
            Vector3D ap, af, au;
            if (TryParseCarrierAnchorLine(rec, out ap, out af, out au))
            {
                pathAnchorPos = ap;
                pathAnchorForward = af;
                pathAnchorUp = au;
                pathAnchorValid = true;
                continue;
            }
        }

        records.Add(rec);
    }

    return records.Count > 0 || pathAnchorValid;
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

string FormatCurrentGps(string name)
{
    Vector3D p = _tickRcPosition;
    Vector3D f = Vector3D.Normalize(_tickRcMatrix.Forward);
    Vector3D u = Vector3D.Normalize(_tickRcMatrix.Up);
    return $"GPS:{name}:{p.X:0.00}:{p.Y:0.00}:{p.Z:0.00}:|{u.X:0.0000},{u.Y:0.0000},{u.Z:0.0000},{f.X:0.0000},{f.Y:0.0000},{f.Z:0.0000}";
}

void DumpCurrentGpsToCustomData()
{
    StringBuilder sb = new StringBuilder();
    string existing = Me.CustomData ?? "";
    if (!string.IsNullOrWhiteSpace(existing))
        sb.Append(existing.TrimEnd());
    sb.AppendLine();
    sb.AppendLine();
    sb.AppendLine("[AutopilotDump]");
    sb.AppendLine(FormatCurrentGps("Here"));
    Me.CustomData = sb.ToString();
    ScriptLog("Dumped GPS/orientation to Custom Data.");
}

// ---- SaveAsteroid ----
bool TryHandleOreNameStateCommand(string command)
{
    string[] parts = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
        return false;
    string oreName = parts[0].Trim();
    string rawMode = parts[1].Trim();
    string normalizedMode = rawMode.ToLowerInvariant();
    bool recognizedMode = normalizedMode == "y" || normalizedMode == "u" || normalizedMode == "x" || normalizedMode == "on" || normalizedMode == "off";
    if (oreName.Length == 0 || !recognizedMode)
        return false;
    char mode = ParseCarrierOreMode(rawMode);
    // Avoid hijacking existing two-word commands that happen to end with a mode token.
    if (oreName.Equals("fullauto", StringComparison.OrdinalIgnoreCase) || oreName.Equals("autopilot", StringComparison.OrdinalIgnoreCase))
        return false;
    IMyTimerBlock oreTimer = FindOreTimer();
    if (oreTimer == null)
    {
        ScriptLog($"Ore state: ore timer '{oreNameTimerTag}' not found.");
        return true;
    }

    var lines = new List<string>((oreTimer.CustomData ?? "").Split('\n'));
    bool updated = false;
    for (int i = 0; i < lines.Count; i++)
    {
        string line = lines[i] ?? "";
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("//"))
            continue;
        string[] rowParts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (rowParts.Length == 0)
            continue;
        if (rowParts[0].Equals(oreName, StringComparison.OrdinalIgnoreCase))
        {
            lines[i] = oreName + " " + mode;
            updated = true;
            break;
        }
    }

    if (!updated)
        lines.Add(oreName + " " + mode);
    oreTimer.CustomData = string.Join("\n", lines).Replace("\r", "").TrimEnd();
    ScriptLog($"Ore state: {oreName} set to {mode}.");
    return true;
}

// so that manually deleted entries are reused before allocating new slots.
// Formats a non-negative integer with dots as thousands separators (e.g. 9343432 -> "9.343.432").
// Returns a map of ore subtype -> total amount, excluding Stone.
// Finds and returns the miner's Path timer block (null if not found).
// Reads and returns all non-empty lines from the miner's Path timer custom data.
// Finds and returns the miner's programmable block (null if not found).
IMyProgrammableBlock FindMinerPb()
{
    return ActiveMiner().Pb;
}

// Sends a command string to the miner's programmable block.
bool SendMinerCommand(string command)
{
    // IGC remains valid after hangar storage recreates the Miner grid and PB,
    // while block discovery is intentionally startup-only.
    IGC.SendBroadcastMessage(ActiveMiner().CommandTag, command);
    ScriptLog("Broadcast '" + command + "' to the Miner.");
    return true;
}
// Loads the path records of a saved asteroid into the miner's Path timer.
// Usage: LoadAsteroid 002
