const string moduleRole = "MinerPilot", modulePeerRole = "MinerCommander";
string modulePeerName { get { return Mi("Prog. Block (Commander)"); } }
bool moduleRecoveryBlocked, moduleSafetyHold;
long moduleDockGeneration, moduleDockOther, moduleUnloadRequest;
bool moduleWasDocked, moduleServiceReady, moduleDeployDecisionReady, moduleDeployNeedsService;
long moduleDeployRequest;
string moduleRoutePayload = "", moduleLastControl = "";
bool modulePathComplete;
int moduleSupplyTick=-10000;

void RemoteCommand(string command) { PilotRemoteCommand(command); }

void TrackModuleDockGeneration()
{
    bool connected = IsMinerDocked();
    long other = connected && minerConnector.OtherConnector != null ? minerConnector.OtherConnector.EntityId : 0;
    if (connected != moduleWasDocked || other != moduleDockOther)
    {
        moduleDockGeneration++;
        moduleWasDocked = connected; moduleDockOther = other;
        moduleServiceReady = false; oreTransferSucceeded = false;
        oreTransferActive = false; oreTransferStartDelayTicks = 0;
        moduleUnloadRequest = 0;
        fullStateDirty = true;
        PublishModuleStatus();
    }
}

string moduleLastStop="";
void ReceiveModule(string kind, string payload)
{
    if (kind == "consume")
    {
        if (moduleOperation.Acknowledge(payload)) SaveState();
        return;
    }
    if(kind=="endpoint") {
        MyIni endpoint=new MyIni();if(endpoint.TryParse(payload))ApplyCarrierDockEndpoint(endpoint.Get("Endpoint","text").ToString(""),endpoint.Get("Endpoint","source").ToInt64(0));
        return;
    }
    if (kind == "callback")
    {
        if (payload.StartsWith("PluginPower|", StringComparison.OrdinalIgnoreCase)) HandlePluginPowerResult(payload);
        else if (payload.StartsWith("PluginDockHardware|", StringComparison.OrdinalIgnoreCase)) HandlePluginDockHardware(payload);
        else if (payload.StartsWith("PluginDockPose|", StringComparison.OrdinalIgnoreCase)) HandlePluginDockPose(payload);
        else if (payload.StartsWith("PluginFeatures|", StringComparison.OrdinalIgnoreCase)) HandlePluginFeatures(payload);
        return;
    }
    if(kind=="settings") { MyIni ini=new MyIni();if(ini.TryParse(payload))ApplyMinerSettings(ini);return; }
    if (kind == "supply") { ReadCommanderSupply(payload); return; }
    if (kind == "control")
    {
        MyIni control = new MyIni();
        if (!control.TryParse(payload)) return;
        string id = control.Get("Control", "id").ToString("");
        if (!ModuleOperationJournal.IsNewer(id, moduleLastControl)) { PublishModuleStatus(); return; }
        string command = control.Get("Control", "command").ToString("");
        if(command=="stop"||command=="reset")moduleLastStop=id;
        if (command.StartsWith("service:", StringComparison.Ordinal))
        { if (stoneEjectionHoldActive) EndStoneEjectionHold(false, "Commander requested service."); StartAutoServiceReturn(command.Substring(8)); }
        else if (command == "hold") StartStoneEjectionHold(_cachedCargoPercent, _cachedStonePercent);
        else if (command == "endhold") EndStoneEjectionHold(true, "Commander ended stone ejection hold.");
        else if (command == "stop") PilotEmergencyStop();
        else if (command == "reset") {PilotRemoteCommand(command);moduleOperation.Finish(moduleOperation.Id,"cancelled");}
        else if (command == "power:docked") ApplyDockedPowerState();
        else if (command == "merge:on") SetMergerEnabled(true);
        else if (command == "merge:off") SetMergerEnabled(false);
        else if (command.StartsWith("endpoint:", StringComparison.Ordinal))
            ApplyCarrierDockEndpoint(command.Substring(9), control.Get("Control", "source").ToInt64(0));
        else PilotRemoteCommand(command);
        moduleLastControl = id;
        SaveState(); PublishModuleStatus();
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
    MyIni job = new MyIni();
    if (!job.TryParse(incoming.Payload)) return;
    if(incoming.Verb=="path" && job.Get("Job","routeFormat").ToString("")!="C1") {
        if(moduleOperation.Accept(incoming.Id,incoming.Verb,incoming.Payload,false)) {
            moduleOperation.Finish(incoming.Id,"error:unsupported route format");SaveState();PublishModuleStatus();
        }
        return;
    }
    bool interrupt = incoming.Verb == "recall" || incoming.Verb == "dock" || incoming.Verb == "hangar";
    if (!moduleOperation.Accept(incoming.Id, incoming.Verb, incoming.Payload, interrupt)) return;
    ApplyMinerSettings(job);
    string commandText = job.Get("Job", "command").ToString("");
    modulePathComplete = false;
    if (incoming.Verb == "path")
    {
        StartPathFromTimer(job.Get("Job", "mission").ToString(""));
    }
    else PilotRemoteCommand(commandText);
    SaveState(); PublishModuleStatus();
}

void PublishModuleStatus()
{
    MyIni status = new MyIni();
    status.Set("Pilot", "runtime", GetRuntimeReport());
    status.Set("Pilot", "id", moduleOperation.Id);
    status.Set("Pilot", "state", moduleOperation.State);
    status.Set("Pilot", "result", moduleOperation.Result);
    status.Set("Pilot", "control", moduleLastControl);
    status.Set("Pilot", "mission", activeMissionId);
    status.Set("Pilot", "recoveryBlocked", moduleRecoveryBlocked);
    status.Set("Pilot", "status", GetMinerStatusText());
    status.Set("Pilot", "serviceEligible", ShouldCheckAutoServiceReturn());
    status.Set("Pilot", "service", (int)autoServiceState);
    status.Set("Pilot", "hangar", (int)hangarState);
    status.Set("Pilot", "docking", (int)dockingState);
    status.Set("Pilot", "dockGeneration", moduleDockGeneration);
    status.Set("Pilot", "dockOther", moduleDockOther);
    status.Set("Pilot", "docked", IsMinerDocked());
    status.Set("Pilot", "powerReady", minerDockPowerReady);
    status.Set("Pilot", "powerGoal", minerPowerGoal);
    status.Set("Pilot", "unloadRequest", moduleUnloadRequest);
    status.Set("Pilot", "deployRequest", moduleDeployRequest);
    status.Set("Pilot", "preflightRequest", undockPreflightStarted || IsUndockSequenceActive() ? modulePreflightRequest : 0);
    status.Set("Pilot", "hold", stoneEjectionHoldActive);
    status.Set("Pilot", "pathLoaded", pathMode && pathLoadJob == null);
    status.Set("Pilot", "pathComplete", modulePathComplete);
    status.Set("Pilot", "pathIndex", pathIndex);
    status.Set("Pilot", "pathCount", pathRecords.Count);
    status.Set("Pilot", "dockSession", minerDockSession);
    status.Set("Pilot", "dockConnector", minerDockConnectorId);
    int plane, total; GetCurrentMiningPlane(out plane, out total);
    status.Set("Pilot", "plane", plane); status.Set("Pilot", "planes", total);
    status.Set("Pilot", "lane", areaLaneCount > 0 ? Math.Min(areaLaneCount, areaLaneIndex + 1) : 0);
    status.Set("Pilot", "lanes", areaLaneCount);
    status.Set("Pilot", "progress", routeProgressMeters);
    status.Set("Pilot", "length", routeProgressTotalMeters);
    SendModule("status", status.ToString());
    PublishPluginServiceRequests();
}

void EmitMinerEvent(string tag, string payload)
{
    SendModule(tag == minerPathCompleteTag ? "completion" : "progress", payload);
}

void UpdatePilotOperationCompletion()
{
    if (moduleOperation.State != "accepted") return;
    string result = "";
    if (moduleOperation.Verb == "path" && modulePathComplete) result = "path_complete";
    else if (moduleOperation.Verb == "hangar" && hangarState == HangarState.AwaitingStore) result = "store_ready";
    else if ((moduleOperation.Verb == "dock" || moduleOperation.Verb == "recall")
        && IsMinerDocked() && dockingState == DockingState.None && !recallActive) result = "docked";
    else if (moduleOperation.Verb == "undock" && !IsMinerDocked() && !IsMergerDocked()
        && dockingState == DockingState.None && !pendingUndock && !hasTarget) result = "undocked";
    if (result.Length > 0)
    {
        moduleOperation.Finish(moduleOperation.Id, result);
        SaveState(); PublishModuleStatus();
    }
}

void PilotEmergencyStop()
{
    PilotRemoteCommand("stop");
    moduleOperation.Finish(moduleOperation.Id, "cancelled");
    SaveState(); PublishModuleStatus();
}

void SendPathCompleteBroadcast()
{
    if (!ModuleReady() || moduleTick-moduleSupplyTick>180 || !IsMinerDocked() || !oreTransferSucceeded || oreTransferActive) return;
    modulePathComplete = true;
    SaveState(); PublishModuleStatus();
}

void ScheduleOreTransfer()
{
    if (!IsMinerDocked() || minerConnector.OtherConnector == null) return;
    if (!minerDockPowerReady)
    {
        minerUnloadAfterPower = true;
        oreTransferStartDelayTicks = 1;
        ApplyDockedPowerState();
        return;
    }
    if (moduleUnloadRequest != 0 && oreTransferActive) return;
    moduleSequence++;
    moduleUnloadRequest = moduleSequence;
    moduleServiceReady = false; oreTransferSucceeded = false;
    oreTransferActive = true; oreTransferStartDelayTicks = 0;
    SaveState(); PublishModuleStatus();
}

void CancelMinerUnload()
{
    moduleUnloadRequest = 0;
    moduleServiceReady = false; oreTransferSucceeded = false;
    oreTransferActive = false; oreTransferStartDelayTicks = 0;
    minerUnloadAfterPower = false;
    fullStateDirty = true;
    PublishModuleStatus();
}

bool HasModuleServicePermit()
{
    return moduleTick-moduleSupplyTick<=180 && ModuleReady() && moduleServiceReady && IsMinerDocked() && moduleWasDocked
        && minerConnector.OtherConnector != null && minerConnector.OtherConnector.EntityId == moduleDockOther;
}

void ReadCommanderSupply(string payload)
{
    MyIni data = new MyIni();
    if (!data.TryParse(payload)) return;
    _cachedCargoPercent = data.Get("Supply", "cargo").ToDouble(100);
    _cachedStonePercent = data.Get("Supply", "stone").ToDouble(0);
    cargoDetailsCurrent = data.Get("Supply", "cargoValid").ToBoolean(false);
    cargoDetailCompletedCycles = data.Get("Supply", "cycles").ToInt32(0);
    cachedMinerHydrogenPercent = data.Get("Supply", "hydrogen").ToDouble(0);
    cachedMinerHydrogenTankCount = data.Get("Supply", "tanks").ToInt32(0);
    minerUnloadSourceIds = data.Get("Supply", "inventoryIds").ToString("");
    moduleSupplyTick=moduleTick;
    ReadUndockPreflight(data);
    bool sameDock = ModuleDockProof.Matches(ModuleReady(),IsMinerDocked(),0,moduleDockGeneration,moduleDockOther,
        moduleUnloadRequest,activeMissionId,data.Get("Supply","generation").ToInt64(-1),data.Get("Supply","other").ToInt64(0),
        data.Get("Supply","request").ToInt64(-1),data.Get("Supply","mission").ToString(""));
    if(!sameDock) {oreTransferSucceeded=false;moduleServiceReady=false;}
    if (sameDock && IsMinerDocked() && moduleUnloadRequest != 0)
    {
        oreTransferSucceeded = data.Get("Supply", "unloaded").ToBoolean(false);
        oreTransferActive = !oreTransferSucceeded;
        moduleServiceReady = oreTransferSucceeded && data.Get("Supply", "serviceReady").ToBoolean(false);
    }
    if (data.Get("Supply", "deployRequest").ToInt64(-1) == moduleDeployRequest && moduleDeployRequest != 0
        && data.Get("Supply","mission").ToString("")==activeMissionId)
    {
        moduleDeployDecisionReady = data.Get("Supply", "deployReady").ToBoolean(false);
        moduleDeployNeedsService = data.Get("Supply", "deployNeedsService").ToBoolean(true);
    }
}

void UpdateAutoServiceReturn()
{
    UpdateAutoServiceReturnRouteBuild();
    if (autoServiceState != AutoServiceState.None) UpdateAutoServiceState();
}

void BeginDeployedMissionCheck()
{
    hangarState = HangarState.PostDeployCheck;
    moduleDeployRequest = ++moduleSequence;
    moduleDeployDecisionReady = false;
    hangarPhaseElapsedTicks = 0;
    hangarBroadcastTicks = 0;
    fullStateDirty = true;
    SaveState(); PublishModuleStatus();
}

void GetMinerCargoStats(out double cargo, out double stone)
{ cargo = _cachedCargoPercent; stone = _cachedStonePercent; }
double GetMinerCargoFillPercent() { return _cachedCargoPercent; }
double GetMinerHydrogenPercent() { return cachedMinerHydrogenPercent; }
bool HasMinerHydrogenTank() { return cachedMinerHydrogenTankCount > 0; }
void RefreshMinerHydrogenCache() { }
void ResetMinerCargoStatsScan() { cargoDetailsCurrent = false; SendModule("freshCargo", ""); }
void HandlePluginUnloadResult(string command) { }
void HandlePluginRemoteStatus(string command) { }
