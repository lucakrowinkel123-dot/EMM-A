const string moduleRole = "CarrierCommander", modulePeerRole = "CarrierPilot";
string modulePeerName { get { return Ca("Prog. Block (Pilot)"); } }
bool pilotRecoveryBlocked=true, pilotCalibrationValid;
double pilotMaxThrust;
string pilotSettingsSent = "", pilotControlPending = "";

void CommanderRemoteCommand(string command)
{
    string normalized = (command ?? "").Trim().ToLowerInvariant();
    if (normalized == "pause" || normalized == "pathpause" || normalized == "plugincockpitpause")
    {
        QueueCarrierControl("pause");
    }
    else if (normalized == "resume" || normalized == "start" || normalized == "pathcontinue" || normalized == "plugincockpitresume")
    {
        QueueCarrierControl("resume");
    }
    LegacyCommanderCommand(command);
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

void RemoteCommand(string command) { CommanderRemoteCommand(command); }

bool SubmitPilotJob(string verb, MyIni payload)
{
    if (!ModuleReady() || pilotRecoveryBlocked)
    {
        ScriptLog("Pilot unavailable: " + (pilotRecoveryBlocked ? "recovery needs attention" : moduleError));
        return false;
    }
    if (moduleOperation.State == "accepted" || moduleOperation.State == "finished") return false;
    AddPilotSettings(payload);
    string id = NextModuleId();
    if (!moduleOperation.Accept(id, verb, payload.ToString(), false)) return false;
    SaveState();
    SendModule("job", moduleOperation.Serialize());
    return true;
}

void PublishModuleStatus()
{
    PublishUiInput();
    if (pilotControlPending.Length > 0) SendModule("control", pilotControlPending);
    if (moduleOperation.State == "accepted") SendModule("job", moduleOperation.Serialize());
    else if (moduleOperation.State == "acknowledged") SendModule("consume", moduleOperation.Id);
    MyIni settings = new MyIni();
    AddPilotSettings(settings);
    string text = settings.ToString();
    SendModule("settings", text);
}

void ReceiveModule(string kind, string payload)
{
    if(ReceiveUiMessage(kind,payload))return;
    if (kind != "status") return;
    MyIni status = new MyIni();
    if (!status.TryParse(payload)) return;
    pilotRuntimeReport = status.Get("Pilot", "runtime").ToString("");
    pilotRecoveryBlocked = status.Get("Pilot", "recoveryBlocked").ToBoolean(true);
    MyIni pending=new MyIni();
    if(pending.TryParse(pilotControlPending) && status.Get("Pilot","control").ToString("")==pending.Get("Control","id").ToString("-")) {pilotControlPending="";MarkProgressStateDirty();}
    autopilotEnabled = status.Get("Pilot", "autopilot").ToBoolean(false);
    hasTarget = status.Get("Pilot", "target").ToBoolean(false);
    Vector3D value;
    if (TryParseV3(status.Get("Pilot", "position").ToString(), out value)) targetPosition = value;
    if (TryParseV3(status.Get("Pilot", "forward").ToString(), out value)) desiredForward = value;
    if (TryParseV3(status.Get("Pilot", "up").ToString(), out value)) desiredUp = value;
    carrierAutopilotMode = (CarrierAutopilotMode)status.Get("Pilot", "mode").ToInt32(0);
    carrierMotionPhase = (CarrierMotionPhase)status.Get("Pilot", "phase").ToInt32(0);
    emma3CarrierRouteIndex = status.Get("Pilot", "routeIndex").ToInt32(0);
    emma3CarrierParkAligning = status.Get("Pilot", "aligning").ToBoolean(false);
    pathMode = status.Get("Pilot", "path").ToBoolean(false);
    pathPaused = status.Get("Pilot", "pathPaused").ToBoolean(false);
    pathIndex = status.Get("Pilot", "pathIndex").ToInt32(0);
    pilotMaxThrust = status.Get("Pilot", "maxThrust").ToDouble(0);
    measuredFlipSeconds = status.Get("Pilot", "flipSeconds").ToDouble(0);
    measuredFlipMass = status.Get("Pilot", "flipMass").ToDouble(0);
    measuredFlipGyroAuthority = status.Get("Pilot", "gyroAuthority").ToDouble(0);
    pilotCalibrationValid = status.Get("Pilot", "calibrationValid").ToBoolean(false);
    string id = status.Get("Pilot", "id").ToString("");
    if (id != moduleOperation.Id) return;
    if (status.Get("Pilot", "state").ToString("") == "finished")
    {
        moduleOperation.Finish(id, status.Get("Pilot", "result").ToString(""));
        UpdateCommanderPilotOperation();
    }
}

void UpdateCommanderPilotOperation()
{
    if (moduleOperation.State != "finished") return;
    string result = moduleOperation.Result;
    if (moduleOperation.Verb == "route" && result == "parked"
        && emma3MissionState == Emma3MissionState.FlyingCarrierRoute)
    {
        emma3MissionState = Emma3MissionState.WaitingStationary;
        PublishEmma3CarrierRouteCompletion();
        mineStationaryTicks = 0;
        StartAsteroidReport(false);
        ScriptLog("Carrier Pilot confirmed parked and aligned.");
    }
    else if (result.StartsWith("error:", StringComparison.Ordinal)) AbortEmma3Mission(result);
    moduleOperation.Acknowledge(moduleOperation.Id);
    SaveState();
    SendModule("consume", moduleOperation.Id);
}

void AddPilotSettings(MyIni ini)
{
    ini.Set("Flight", "speed", maxSpeed);
    ini.Set("Flight","arrival",arrivalTolerance);ini.Set("Flight","orientation",orientationToleranceDeg);
    ini.Set("Flight","turnTest",turnTestEnabled);ini.Set("Flight","lcdTicks",lcdRefreshIntervalTicks);
    ini.Set("Flight","logging",emma3ScriptLogging);
    ini.Set("Flight", "thrust", thrustLimitKn);
    ini.Set("Flight", "use", thrustUsePercent);
    ini.Set("Flight", "acceleration", accelerationLimit);
    ini.Set("Flight", "offset", cruiseOffsetMeters);
    ini.Set("Flight", "right", TOFFR);
    ini.Set("Flight", "up", TOFFU);
    ini.Set("Flight", "forward", TOFFF);
}

void StartNextEmma3CarrierRoutePoint()
{
    if (moduleOperation.State == "accepted" && moduleOperation.Verb == "route") return;
    MyIni route = new MyIni();
    route.Set("EMMA3", "missionId", emma3MissionId);
    route.Set("EMMA3", "asteroidEntityId", emma3ActiveEntityId);
    route.Set("EMMA3", "carrierRouteCount", emma3CarrierRoutePositions.Count);
    for (int i = 0; i < emma3CarrierRoutePositions.Count; i++)
        route.Set("EMMA3", "carrierRoute" + i, V3ToString(emma3CarrierRoutePositions[i]) + "|"
            + V3ToString(emma3CarrierRouteForwards[i]) + "|" + V3ToString(emma3CarrierRouteUps[i]));
    if (!SubmitPilotJob("route", route)) AbortEmma3Mission("Carrier Pilot did not accept a route request.");
}

void UpdateEmma3CarrierRoute() { UpdateCommanderPilotOperation(); }

void StartPrecisionTarget(Vector3D position, Vector3D forward, Vector3D up)
{ SubmitNavigation(position, forward, up, false); }
void StartCruiseTarget(Vector3D position, Vector3D forward, Vector3D up)
{ SubmitNavigation(position, forward, up, true); }

void SubmitNavigation(Vector3D position, Vector3D forward, Vector3D up, bool cruise)
{
    MyIni job = new MyIni();
    job.Set("Nav", "position", V3ToString(position)); job.Set("Nav", "forward", V3ToString(forward));
    job.Set("Nav", "up", V3ToString(up)); job.Set("Nav", "cruise", cruise);
    SubmitPilotJob("navigate", job);
}

void StartPathFromTimer()
{
    IMyTimerBlock timer = FindPathTimer();
    if (timer == null) { ScriptLog("Carrier path timer is missing."); return; }
    MyIni job = new MyIni(); job.Set("Path", "text", timer.CustomData ?? "");
    SubmitPilotJob("path", job);
}
void StartNextPathRecord() { QueueCarrierControl("resume"); }
void UpdatePathRunner() { UpdateCommanderPilotOperation(); }

void ResetMovement()
{
    QueueCarrierControl(scriptPaused || cockpitAbortPaused ? "pause" : "stop");
}

// Commander samples pose for dock broadcasts/UI. Pilot alone writes actuators.
void RefreshTickPose()
{
    carrierConnectionStatusValid = false;
    flightCache.BeginInvocation(runtime);
    _tickRcMatrix = flightCache.Matrix;
    _tickRcPosition = flightCache.Position;
}
void BeginCarrierTransitFlightCache() { flightCache.Invalidate(); }
void EndCarrierTransitFlightCache() { flightCache.Invalidate(); }
double GetDetectedMaxCarrierThrustN() { return pilotMaxThrust; }
bool IsCruiseTurnMeasurementValid() { return ModuleReady() && pilotCalibrationValid; }

void RecoverEmma3CarrierRouteAfterLoad()
{
    // The Pilot restores its own immutable route and position. The Commander
    // waits for the persisted operation, never starts a leg based on an index.
    if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute && moduleOperation.Id.Length == 0)
    {
        fullAutoEnabled = false;
        emma3MissionState = Emma3MissionState.None;
        ScriptLog("Legacy active mission requires a stopped four-module upgrade.");
    }
}
void RecoverPathRecordsAfterLoad() { }

void QueueCarrierControl(string command)
{
    MyIni pending=new MyIni();
    if(pending.TryParse(pilotControlPending) && pending.Get("Control","command").ToString("")==command) return;
    pending.Clear();pending.Set("Control","id",NextModuleId());pending.Set("Control","command",command);
    pilotControlPending=pending.ToString();SaveState();SendModule("control",pilotControlPending);
}
