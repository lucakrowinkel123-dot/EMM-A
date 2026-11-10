const string moduleRole = "CarrierPilot", modulePeerRole = "CarrierCommander";
string modulePeerName { get { return Ca("Prog. Block (Commander)"); } }
bool moduleRecoveryBlocked;
string pilotLastControl = "";

public Program()
{
    InitializeShipNames(true, true);
    rc = GridTerminalSystem.GetBlockWithName(carrierRemoteName) as IMyRemoteControl;
    if (rc == null || rc.CubeGrid != Me.CubeGrid) throw new Exception("Carrier Pilot: missing local remote control");
    flightCache = new FlightDataCache(rc);
    allThrusters = GetBlocks<IMyThrust>().FindAll(b => b.CubeGrid == Me.CubeGrid && HasCarrierTag(b));
    allGyros = GetBlocks<IMyGyro>().FindAll(b => b.CubeGrid == Me.CubeGrid && HasCarrierTag(b));
    flightCache.SetBlocks(allThrusters, allGyros);
    thrust = new ThrusterControl(flightCache, tickSpeed, allThrusters);
    gyros = new GyroControl(flightCache, tickSpeed, allGyros);
    RefreshTickPose();
    RefreshTimerCache();
    LoadState();
    InitializeModule();
    InitializeModuleUi();
    ResetMovement();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    ScriptLog("Ready. Carrier Pilot v0.8.56.");
}

public void Main(string argument, UpdateType updateSource)
{
    pbRuntimeAverage.Observe(Runtime.LifetimeTicks, Runtime.LastRunTimeMs);
    if ((updateSource & UpdateType.Update1) == 0)
    {
        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase)) PilotControl("stop");
        return;
    }
    UpdateRuntimeMetrics();
    RefreshTickPose();
    carrierHeavyWorkClaimed=false;
    UpdateModule();
    if (!ModuleReady() || moduleRecoveryBlocked || scriptPaused)
    {
        ResetMovement();
        Echo(moduleRecoveryBlocked ? "Pilot recovery needs attention" : scriptPaused ? "Paused" : moduleError);
    }
    else
    {
        if (autopilotEnabled && hasTarget) Move();
        else ResetMovement();
        if (moduleOperation.State == "accepted")
        {
            if (moduleOperation.Verb == "route") UpdateEmma3CarrierRoute();
            else if (moduleOperation.Verb == "path")
            {
                UpdatePathRunner();
                if (!pathMode && !hasTarget) FinishPilotOperation("arrived");
            }
            else if (moduleOperation.Verb == "navigate" && IsAtTargetPose(targetPosition, desiredForward, desiredUp)
                && flightCache.LinearVelocity.LengthSquared() <= carrierStoppedSpeedMps * carrierStoppedSpeedMps
                && flightCache.AngularVelocity.LengthSquared() <= carrierStoppedAngularSpeedRad * carrierStoppedAngularSpeedRad)
            {
                autopilotEnabled = false; hasTarget = false;
                ResetMovement(); FinishPilotOperation("arrived");
            }
        }
    }
    UpdateModuleUi();
    if(runtime%60==0) { MarkProgressStateDirty();RequestCarrierSave(false); }
    PumpCarrierSaveWork();
    UpdateScriptLogFlush();
    runtime++;
}



string moduleLastStop="";
void ReceiveModule(string kind, string payload)
{
    if(ReceiveUiMessage(kind,payload))return;
    if (kind == "consume")
    {
        if (moduleOperation.Acknowledge(payload)) SaveState();
        return;
    }
    if (kind == "control") {
        MyIni control=new MyIni();if(!control.TryParse(payload))return;
        string id=control.Get("Control","id").ToString("");
        if(!ModuleOperationJournal.IsNewer(id,pilotLastControl)){PublishModuleStatus();return;}
        string command=control.Get("Control","command").ToString("");
        if(command=="stop")moduleLastStop=id;
        pilotLastControl=id;PilotControl(command);return;
    }
    if (kind == "settings")
    {
        MyIni settings = new MyIni();
        if (settings.TryParse(payload)) ApplyPilotSettings(settings);
        return;
    }
    if (kind != "job" || moduleRecoveryBlocked) return;
    var incoming = new ModuleOperationJournal();
    if (!incoming.Restore(payload) || incoming.State != "accepted") return;
    if (incoming.Id == moduleOperation.Id) { PublishModuleStatus(); return; }
    if(moduleLastStop.Length>0 && !ModuleOperationJournal.IsNewer(incoming.Id,moduleLastStop)) {
        if(moduleOperation.Accept(incoming.Id,incoming.Verb,incoming.Payload,false)) {
            moduleOperation.Finish(incoming.Id,"cancelled");SaveState();
        }
        PublishModuleStatus();return;
    }
    MyIni plan = new MyIni();
    if (!plan.TryParse(incoming.Payload)) return;
    if (incoming.Verb != "route" && incoming.Verb != "navigate" && incoming.Verb != "path") return;
    if (!moduleOperation.Accept(incoming.Id, incoming.Verb, incoming.Payload, false)) return;
    ApplyPilotSettings(plan);
    scriptPaused = false;
    if (incoming.Verb == "route")
    {
        if (!LoadEmma3ReadyPlan(plan)) { FinishPilotOperation("error:invalid route"); return; }
        emma3MissionState = Emma3MissionState.FlyingCarrierRoute;
        BeginCarrierTransitFlightCache();
        StartNextEmma3CarrierRoutePoint();
    }
    else if (incoming.Verb == "path") StartPathFromTimer();
    else
    {
        Vector3D position, forward, up;
        if (!TryParseV3(plan.Get("Nav", "position").ToString(), out position)
            || !TryParseV3(plan.Get("Nav", "forward").ToString(), out forward)
            || !TryParseV3(plan.Get("Nav", "up").ToString(), out up))
        { FinishPilotOperation("error:invalid navigation pose"); return; }
        if (plan.Get("Nav", "cruise").ToBoolean(false)) StartCruiseTarget(position, forward, up);
        else StartPrecisionTarget(position, forward, up);
    }
    SaveState();
    PublishModuleStatus();
}

void PilotControl(string command)
{

    if (command == "pause" && !scriptPaused)
    {
        pauseResumeAutopilot = autopilotEnabled;
        scriptPaused = true; autopilotEnabled = false; ResetMovement();
    }
    else if (command == "resume" && scriptPaused)
    {
        scriptPaused = false; autopilotEnabled = pauseResumeAutopilot && hasTarget;
        pauseResumeAutopilot = false;
    }
    else if (command == "stop")
    {
        StopPathExecution(); emma3MissionState = Emma3MissionState.None;
        scriptPaused = false;
        FinishPilotOperation("cancelled");
    }
    SaveState(); PublishModuleStatus();
}

void FinishPilotOperation(string result)
{
    if (!moduleOperation.Finish(moduleOperation.Id, result)) return;
    SaveState(); PublishModuleStatus();
}

void StartNextEmma3CarrierRoutePoint()
{
    if (emma3CarrierRouteIndex >= emma3CarrierRoutePositions.Count)
    {
        EndCarrierTransitFlightCache();
        emma3MissionState = Emma3MissionState.WaitingStationary;
        FinishPilotOperation("parked");
        return;
    }
    int index = emma3CarrierRouteIndex++;
    emma3CarrierParkAligning = false;
    if (emma3CarrierRouteIndex == emma3CarrierRoutePositions.Count)
        StartPrecisionTarget(emma3CarrierRoutePositions[index], Vector3D.Zero, Vector3D.Zero);
    else StartCruiseTarget(emma3CarrierRoutePositions[index], emma3CarrierRouteForwards[index], emma3CarrierRouteUps[index]);
    MarkProgressStateDirty();
}

void PublishModuleStatus()
{
    MyIni status = new MyIni();
    status.Set("Pilot", "runtime", GetRuntimeReport());
    status.Set("Pilot", "id", moduleOperation.Id);
    status.Set("Pilot", "state", moduleOperation.State);
    status.Set("Pilot", "result", moduleOperation.Result);
    status.Set("Pilot", "control", pilotLastControl);
    status.Set("Pilot", "autopilot", autopilotEnabled);
    status.Set("Pilot", "target", hasTarget);
    status.Set("Pilot", "position", V3ToString(targetPosition));
    status.Set("Pilot", "forward", V3ToString(desiredForward));
    status.Set("Pilot", "up", V3ToString(desiredUp));
    status.Set("Pilot", "mode", (int)carrierAutopilotMode);
    status.Set("Pilot", "phase", (int)carrierMotionPhase);
    status.Set("Pilot", "routeIndex", emma3CarrierRouteIndex);
    status.Set("Pilot", "routeCount", emma3CarrierRoutePositions.Count);
    status.Set("Pilot", "aligning", emma3CarrierParkAligning);
    status.Set("Pilot", "path", pathMode);
    status.Set("Pilot", "pathPaused", pathPaused);
    status.Set("Pilot", "pathIndex", pathIndex);
    status.Set("Pilot", "maxThrust", GetDetectedMaxCarrierThrustN());
    status.Set("Pilot", "flipSeconds", measuredFlipSeconds);
    status.Set("Pilot", "flipMass", measuredFlipMass);
    status.Set("Pilot", "gyroAuthority", measuredFlipGyroAuthority);
    status.Set("Pilot", "calibrationValid", IsCruiseTurnMeasurementValid());
    status.Set("Pilot", "recoveryBlocked", moduleRecoveryBlocked);
    SendModule("status", status.ToString());
}

void ApplyPilotSettings(MyIni ini)
{
    arrivalTolerance=Math.Max(0.1,ini.Get("Flight","arrival").ToDouble(arrivalTolerance));
    orientationToleranceDeg=Math.Max(0.1,ini.Get("Flight","orientation").ToDouble(orientationToleranceDeg));
    turnTestEnabled=ini.Get("Flight","turnTest").ToBoolean(turnTestEnabled);
    lcdRefreshIntervalTicks=Math.Max(60,ini.Get("Flight","lcdTicks").ToInt32(lcdRefreshIntervalTicks));
    emma3ScriptLogging=ini.Get("Flight","logging").ToBoolean(emma3ScriptLogging);
    maxSpeed = MathHelper.Clamp(ini.Get("Flight", "speed").ToDouble(maxSpeed), 1, 50000);
    thrustLimitKn = Math.Max(1, ini.Get("Flight", "thrust").ToDouble(thrustLimitKn));
    thrustUsePercent = MathHelper.Clamp(ini.Get("Flight", "use").ToDouble(thrustUsePercent), 1, 100);
    accelerationLimit = Math.Max(0.1, ini.Get("Flight", "acceleration").ToDouble(accelerationLimit));
    cruiseOffsetMeters = Math.Max(100, ini.Get("Flight", "offset").ToDouble(cruiseOffsetMeters));
    TOFFR = ini.Get("Flight", "right").ToDouble(TOFFR);
    TOFFU = ini.Get("Flight", "up").ToDouble(TOFFU);
    TOFFF = ini.Get("Flight", "forward").ToDouble(TOFFF);
}
