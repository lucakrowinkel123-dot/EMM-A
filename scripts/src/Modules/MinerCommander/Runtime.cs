const string moduleRole = "MinerCommander", modulePeerRole = "MinerPilot";
string modulePeerName { get { return Mi("Prog. Block (Pilot)"); } }
bool pilotRecoveryBlocked=true, pilotServiceEligible, pilotStoneHold, pilotPathLoaded, pilotPathComplete;
string pilotStatus = "Waiting for Pilot", pendingPilotControl = "", pilotControlId = "";
long pilotDockGeneration, pilotDockOther, pilotUnloadRequest, acceptedUnloadRequest, acceptedUnloadGeneration;
long pilotDeployRequest, checkedDeployRequest;
int deployCargoBaseline, pilotPlane, pilotPlanes, pilotLane, pilotLanes;
bool deployReady, deployNeedsService = true;
double pilotRouteMeters, pilotRouteLength;
string featureReply = "PluginFeatures|0|0|0";

public Program()
{
    InitializeShipNames(false, false);
    rc = GetRequiredMinerRemoteControl();
    flightCache = new FlightDataCache(rc);
    minerConnector = GetBlock<IMyShipConnector>(minerConnectorName, true);
    carrierConnector = GetBlock<IMyShipConnector>(carrierConnectorName, true);
    minerMerger = GetBlock<IMyShipMergeBlock>(minerMergerName, true);
    hydrogenTanks = GetBlocks<IMyGasTank>();
    RefreshMinerCargoStatsBlocks(); CacheOreTransferBlocks();
    RefreshTimerCache(); CacheNamedTimerReferences();
    carrierMinerSettingsTimer = GridTerminalSystem.GetBlockWithName(carrierMinerSettingsTimerName) as IMyTimerBlock;
    minerPluginInfoTimer = GridTerminalSystem.GetBlockWithName(minerPluginInfoTimerName) as IMyTimerBlock;
    carrierStatusListener = IGC.RegisterBroadcastListener(carrierStatusTag);
    minerCommandListener = IGC.RegisterBroadcastListener(minerCommandTag);
    LoadState(); InitializeModule(); LoadCarrierMinerSettings();
    RefreshTickPose(); PublishMinerPlannerSettings();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    ScriptLog("Ready. Miner Commander v0.8.56.");
}

public void Main(string argument, UpdateType updateSource)
{
    pbRuntimeAverage.Observe(Runtime.LifetimeTicks, Runtime.LastRunTimeMs);
    RefreshTickPose();
    if ((updateSource & UpdateType.Update1) == 0) { RemoteCommand(argument); return; }
    minerHeavyWorkClaimed = false;
    UpdateModule();
    UpdateRuntimeMetrics();
    UpdatePluginServiceAges();
    UpdateMinerHydrogenCache();
    if (!PluginRemoteTelemetryFresh()) UpdateMinerCargoStatsScan();
    PollMinerCommands(); PollCarrierStatus();
    if(!scriptPaused)PumpCommanderRoute();
    if (ModuleReady() && !pilotRecoveryBlocked)
    {
        UpdateCommanderUnload();
        if (!scriptPaused) UpdateCommanderServicePolicy();
        if (runtime % 900 == 0) BroadcastMinerStatus();
    }
    PublishPluginServiceRequests();
    PumpMinerSaveWork();
    if (IsMinerRoutineSaveDue()) RequestMinerSave(true);
    UpdateScriptLogFlush();
    runtime++;
}



void RefreshTickPose()
{
    InvalidateMinerConnectionStatus();
    flightCache.BeginInvocation(runtime);
    _tickRcMatrix = flightCache.Matrix; _tickRcPosition = flightCache.Position;
}

void RemoteCommand(string command)
{
    string text = (command ?? "").Trim();
    if (text.Length == 0) return;
    if (text.StartsWith("PluginFeatures|", StringComparison.OrdinalIgnoreCase))
    { HandlePluginFeatures(text); featureReply = text; SendModule("callback", text); return; }
    if (text.StartsWith("PluginUnload|", StringComparison.OrdinalIgnoreCase)) { HandlePluginUnloadResult(text); return; }
    if (text.StartsWith("PluginRemoteStatus|", StringComparison.OrdinalIgnoreCase)) { HandlePluginRemoteStatus(text); return; }
    if (text.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase)) { SendModule("callback", text); return; }
    if(HandleCommanderSetting(text))return;
    string verb, mission; SplitMissionCommand(text, out verb, out mission);
    string lower = verb.ToLowerInvariant();
    if (lower == "pathstart") { SubmitMinerPath(mission); return; }
    if(lower=="pause"||lower=="stop"||lower=="reset"||lower=="recall"||lower=="pathpause"||lower=="dock"||lower=="undock"||text.StartsWith("Hangar",StringComparison.OrdinalIgnoreCase))CancelCommanderRoute();
    if (lower == "pause") scriptPaused = true;
    if (lower == "resume" || lower == "start" || lower == "stop" || lower == "reset") scriptPaused = false;
    if (lower == "recall" || lower == "dock" || lower == "undock" || text.StartsWith("Hangar", StringComparison.OrdinalIgnoreCase))
    {
        if(text.StartsWith("Hangar",StringComparison.OrdinalIgnoreCase))LoadCarrierMinerSettings();
        string action = lower == "recall" ? "recall" : lower == "undock" ? "undock"
            : text.StartsWith("HangarStore|", StringComparison.OrdinalIgnoreCase) ? "hangar" : "dock";
        MyIni job = new MyIni(); job.Set("Job", "command", text);
        SubmitMinerJob(action, job, action != "undock");
        return;
    }
    QueuePilotControl(text);
}

bool SubmitMinerJob(string verb, MyIni payload, bool interrupt)
{
    if (!ModuleReady() || pilotRecoveryBlocked) { ScriptLog("Miner Pilot unavailable: " + moduleError); return false; }
    MyIni previous=new MyIni();
    if((moduleOperation.State=="accepted" || moduleOperation.State=="acknowledged" && verb=="hangar" && moduleOperation.Result=="store_ready") && moduleOperation.Verb==verb && previous.TryParse(moduleOperation.Payload)
        && previous.Get("Job","command").ToString("")==payload.Get("Job","command").ToString(""))return true;
    if(!payload.ContainsKey("Settings","SPEEDF"))AddMinerSettings(payload);
    if (!moduleOperation.Accept(NextModuleId(), verb, payload.ToString(), interrupt)) return false;
    SaveState(); SendModule("job", moduleOperation.Serialize());
    return true;
}

readonly Queue<string> pilotControlQueue=new Queue<string>();
string pendingEndpoint="";
void QueuePilotControl(string command, long source = 0)
{
    if(command.StartsWith("endpoint:",StringComparison.Ordinal)) {
        MyIni endpoint=new MyIni();endpoint.Set("Endpoint","source",source);endpoint.Set("Endpoint","text",command.Substring(9));
        pendingEndpoint=endpoint.ToString();SendModule("endpoint",pendingEndpoint);return;
    }
    if(command=="stop"||command=="reset")pilotControlQueue.Clear();
    if (pendingPilotControl.Length > 0 && command != "stop" && command != "reset")
    {
        MyIni pending=new MyIni();
        if(pending.TryParse(pendingPilotControl)&&pending.Get("Control","command").ToString("")==command)return;
        if(!pilotControlQueue.Contains(command)) {
            if(pilotControlQueue.Count>=32) {ScriptLog("Pilot control queue full.");return;}
            pilotControlQueue.Enqueue(command);SaveState();
        }
        return;
    }
    MyIni control = new MyIni();
    pilotControlId = NextModuleId();
    control.Set("Control", "id", pilotControlId); control.Set("Control", "command", command);
    control.Set("Control", "source", source);
    pendingPilotControl = control.ToString();
    SaveState(); SendModule("control", pendingPilotControl);
}

void PublishModuleStatus()
{
    PublishCompletedRecall();
    PublishMinerSettings();
    if(pendingEndpoint.Length>0)SendModule("endpoint",pendingEndpoint);
    if (pendingPilotControl.Length > 0) SendModule("control", pendingPilotControl);
    if (moduleOperation.State == "accepted") SendModule("job", moduleOperation.Serialize());
    else if (moduleOperation.State == "acknowledged") SendModule("consume", moduleOperation.Id);
    SendModule("callback", featureReply);
    PublishCommanderSupply();
}

void ReceiveModule(string kind, string payload)
{
    if (kind == "plugin")
    {
        int split = payload.IndexOf('|'), slot;
        if (split > 0 && int.TryParse(payload.Substring(0, split), out slot) && slot >= 2 && slot <= 4)
        { SetPluginInfoMessage(slot, payload.Substring(split + 1)); FlushPluginInfoMessages(); }
        return;
    }
    if (kind == "freshCargo") { ResetMinerCargoStatsScan(); return; }
    if (kind == "progress") { IGC.SendBroadcastMessage(minerProgressTag, payload); return; }
    if (kind == "completion")
    {
        if (payload.StartsWith("RecallComplete|" + activeMissionId + "|", StringComparison.Ordinal))
            IGC.SendBroadcastMessage(minerPathCompleteTag, payload);
        return;
    }
    if (kind != "status") return;
    MyIni status = new MyIni(); if (!status.TryParse(payload)) return;
    pilotRuntimeReport = status.Get("Pilot", "runtime").ToString("");
    pilotRecoveryBlocked = status.Get("Pilot", "recoveryBlocked").ToBoolean(true);
    pilotStatus = status.Get("Pilot", "status").ToString("Unknown");
    if (pendingPilotControl.Length>0 && status.Get("Pilot", "control").ToString("") == pilotControlId) {
        pendingPilotControl="";
        if(pilotControlQueue.Count>0)QueuePilotControl(pilotControlQueue.Dequeue());else SaveState();
    }
    activeMissionId = status.Get("Pilot", "mission").ToString("");
    pilotServiceEligible = status.Get("Pilot", "serviceEligible").ToBoolean(false);
    autoServiceState = (AutoServiceState)status.Get("Pilot", "service").ToInt32(0);
    hangarState = (HangarState)status.Get("Pilot", "hangar").ToInt32(0);
    dockingState = (DockingState)status.Get("Pilot", "docking").ToInt32(0);
    minerPowerGoal = status.Get("Pilot", "powerGoal").ToInt32(0);
    minerDockPowerReady = status.Get("Pilot", "powerReady").ToBoolean(false);
    pilotDockGeneration = status.Get("Pilot", "dockGeneration").ToInt64(0);
    pilotDockOther = status.Get("Pilot", "dockOther").ToInt64(0);
    pilotUnloadRequest = status.Get("Pilot", "unloadRequest").ToInt64(0);
    pilotDeployRequest = status.Get("Pilot", "deployRequest").ToInt64(0);
    pilotPreflightRequest = status.Get("Pilot", "preflightRequest").ToInt64(0);
    pilotStoneHold = status.Get("Pilot", "hold").ToBoolean(false);
    pilotPathLoaded = status.Get("Pilot", "pathLoaded").ToBoolean(false);
    pilotPathComplete = status.Get("Pilot", "pathComplete").ToBoolean(false);
    minerDockSession = status.Get("Pilot", "dockSession").ToInt64(0);
    minerDockConnectorId = status.Get("Pilot", "dockConnector").ToInt64(0);
    pilotPlane = status.Get("Pilot", "plane").ToInt32(0); pilotPlanes = status.Get("Pilot", "planes").ToInt32(0);
    pilotLane = status.Get("Pilot", "lane").ToInt32(0); pilotLanes = status.Get("Pilot", "lanes").ToInt32(0);
    pilotRouteMeters = status.Get("Pilot", "progress").ToDouble(0); pilotRouteLength = status.Get("Pilot", "length").ToDouble(0);
    if (status.Get("Pilot", "id").ToString("") == moduleOperation.Id
        && status.Get("Pilot", "state").ToString("") == "finished")
    {
        moduleOperation.Finish(moduleOperation.Id, status.Get("Pilot", "result").ToString(""));
        if (moduleOperation.Result == "path_complete" && !CanConfirmFinalUnload()) return;
        if (moduleOperation.Result == "path_complete") SendPathCompleteBroadcast();
        PublishCompletedRecall();
        if(moduleOperation.Result.StartsWith("error:",StringComparison.Ordinal))ScriptLog("Pilot "+moduleOperation.Result);
        moduleOperation.Acknowledge(moduleOperation.Id);
        SaveState(); SendModule("consume", moduleOperation.Id);
    }
}

void PublishCompletedRecall()
{
    if(moduleOperation.Verb=="recall" && moduleOperation.Result=="docked"
        && (moduleOperation.State=="finished" || moduleOperation.State=="acknowledged")
        && ModuleReady() && !pilotRecoveryBlocked && IsMinerDocked() && minerDockPowerReady)
        IGC.SendBroadcastMessage(minerPathCompleteTag,"RecallComplete|"+activeMissionId+"|1");
}

void PollMinerCommands()
{
    if (minerCommandListener == null) return;
    for (int i = 0; i < 4 && minerCommandListener.HasPendingMessage; i++)
    {
        var message = minerCommandListener.AcceptMessage();
        string command = message.Data as string;
        if (command == null) continue;
        if (command.StartsWith("DockEndpoint|", StringComparison.OrdinalIgnoreCase)) QueuePilotControl("endpoint:" + command, message.Source);
        else RemoteCommand(command);
    }
}

void BroadcastMinerStatus()
{
    IGC.SendBroadcastMessage(minerProgressTag, "status|" + pilotStatus + "|" + Number2(_cachedCargoPercent)
        + "|" + Number2(HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 0)
        + "|" + pilotPlane + "|" + pilotPlanes + "|" + pilotLane + "|" + pilotLanes);
    if (pilotRouteLength > 0) IGC.SendBroadcastMessage(minerProgressTag, "route:" + Number2(pilotRouteMeters) + ":" + Number2(pilotRouteLength));
}

int moduleNextPluginPublish;
void PublishPluginServiceRequests()
{
    if(moduleTick<moduleNextPluginPublish)return;
    moduleNextPluginPublish=moduleTick+60;
    SetPluginInfoMessage(5, GetRuntimeReport() + "\n" + pilotRuntimeReport);
    SetPluginInfoMessage(0, "EMMA3_FEATURES_READY|1|" + pluginServiceSession);
    SetPluginInfoMessage(1, pluginUnloadRequestPending && minerUnloadPayload.Length > 0
        ? "EMMA3_UNLOAD|1|" + pluginUnloadRequestId + "|" + minerUnloadPayload : "");
    FlushPluginInfoMessages();
}

void ApplyDockedPowerState() { QueuePilotControl("power:docked"); }
void SetMergerEnabled(bool enabled) { PublishMinerSettings(); }
void WriteEcho() { Echo(pilotStatus); }
bool IsUndockBackgroundFrozen() { return false; }
