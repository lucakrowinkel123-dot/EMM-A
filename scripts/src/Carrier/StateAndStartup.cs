// Carrier autopilot for EMM-A system.
// Broadcasts dock connector pose and supports nav/path autopilot.
//
// ============= Commands ==============
// Nav;GPS:Name:X:Y:Z:|Ux,Uy,Uz,Fx,Fy,Fz : set target position and orientation vectors
// Nav;GPS:Name:X:Y:Z:|Faceup,Faceforward  : auto-derive up/forward toward target
// pause        : pause updates and release controls without clearing current task
// resume/start : resume after pause
// stop         : abort current task, clear target, and release controls
// reset        : clear target and all path state
// dumpgps      : save current GPS + orientation to Custom Data
// pathstart    : load records from "Carrier Path" timer and execute row-by-row
// pathpause    : pause path execution
// pathcontinue : resume path after pause
// pathstop     : stop path mode and clear queued records
// TOFF r,u,f   : set target offset in local Right,Up,Forward meters
// TOFFRESET    : reset target offset to 0,0,0
// <OreName> Y/U/X : Y targets and mines; U mines if present; X skips
// Fullauto     : run the plugin-planned autonomous mining loop
// Fullautostop : stop the autonomous loop
// HangarStore  : docked Miner -> store in hangar and stop
// HangarDeploy : stored Miner -> deploy -> return to Dock Pose 1 -> dock
// HangarCycle  : docked Miner -> store -> deploy -> return to Dock Pose 1 -> dock
// setallopen   : reset the plugin's completed-asteroid list
//
// Persistent on-disk format (path timer):
//   carrier anchor : cx,cy,cz:ax,ay,az,a     -- absolute pos + axis-angle rotation
//   path point     : dx,dy,dz:ax,ay,az,a     -- delta from anchor + axis-angle
//   mine area      : dx,dy,dz:ax,ay,az,a:w,h:depth -- corner-A delta, area axis-angle, w/h, depth
// Player-pasted "GPS:Name:X:Y:Z:|..." waypoints are still accepted by the Nav;... command
// and as path-timer entries.
// =====================================
// ========== Editable Script Settings ==========
string carrierConnectorName { get { return Ca("Connector (Hangar)"); } }
string carrierConnectorLightName { get { return Ca("Connector Light"); } }
string carrierRemoteName { get { return Ca("Remote Control"); } }
string carrierPbTag { get { return Ca("Prog. Block (Commander)"); } }
string dockBroadcastTag { get { return shipNames.Channel("EMM_A_DOCK_POSE"); } }
string minerPathCompleteTag { get { return shipNames.Channel("EMM_A_MINER_PATH_COMPLETE"); } }
double broadcastForwardOffset = 1.0;
UpdateFrequency tickSpeed = UpdateFrequency.Update1;
double maxSpeed = 500;
double arrivalTolerance = 2.5;
double orientationToleranceDeg = 1.0;
string pathTimerTag = "Carrier Path";
// Storage owns recovery snapshots; these timers are optional INI mirrors.
string stateIniTimerTag { get { return Ca("INI Core"); } }
string stateRouteTimerTag { get { return Ca("INI Route"); } }
string stateReportsTimerTag { get { return Ca("INI Reports"); } }
double TOFFR = 0;
double TOFFU = 0;
double TOFFF = 0;
string oreNameTimerTag { get { return Ca("Ores"); } } // exact name of timer storing ore Y/U/X rows
string minerPbTag { get { return Mi("Prog. Block (Commander)"); } } // name tag for the miner's programmable block
string carrierLcdName { get { return Ca("LCD Main"); } }
string carrierResourcesTimerName { get { return Ca("Resources"); } }
string carrierStatusTag { get { return shipNames.Channel("EMM_A_CARRIER_STATUS"); } }
string minerProgressTag { get { return shipNames.Channel("EMM_A_MINER_PROGRESS"); } }
string minerCommandTag { get { return shipNames.Channel("EMM_A_MINER_COMMAND"); } }
string emma3BridgeTimerName { get { return Ca("Plugin Bridge"); } }
string carrierPluginInfoTimerName { get { return Ca("Plugin Info"); } }
string emma3CompletedAsteroidsTimerName { get { return Ca("Asteroid IDs"); } }
string hangarBlockName { get { return Ca("Hangar - A"); } }
string hangarManagerTimerName { get { return Ca("HangarManager"); } }
double emma3SearchRadius = 50000;
double emma3ParkingHeight = 400;
double emma3PlaneClearance = 25;
double emma3TravelClearance = 75;
bool emma3Visualization = true;
bool emma3AllowMovement = false; // v0.1 safety default
bool emma3ScriptLogging = true;
bool emma3AllowSeatExitDuringMining = false;
bool emma3IceReserveEnabled = false;
double emma3IceReserveMillionLiters = 1;
bool emma3PluginOreTransfer = true;
bool emma3PluginPowerTransitions = true;
bool emma3PluginRemoteLink = true;
bool emma3PluginLcdOutput = true;
bool emma3ItemsMode = false;
string emma3EnabledItems = "";
IMyTimerBlock itemsTimer;
List<string> itemNames = new List<string>();
List<string> itemBlueprints = new List<string>();
string itemsMenuError = "";
bool emma3BridgeAutoEnabled = false;
string emma3ProductionStatus = "";
double minerStandardDepth = 30.0;
double minerLaneWidth = 17.5;
double minerLaneHeight = 12.5;
double minerForwardSpeed = 0.7;
double minerReturnSpeed = 2.5;
bool minerAutoServiceEnabled = true;
double minerAutoServiceCargoPercent = 90.0;
double minerAutoServiceHydrogenPercent = 20.0;
bool minerStopAtOnePercentFuel = true;
double minerRouteStartExtra = 0.0;
bool minerMergeAssistedDocking = false;
bool minerHangarEnabled = false;
double shortRangeDistanceMeters = 50000;
double cruiseOffsetMeters = 2000;
double thrustLimitKn = 5000000;
double thrustUsePercent = 80;
double accelerationLimit = 2.0;
bool turnTestEnabled = true;
double mineStationarySpeedMps = 0.20;
int mineStationaryConfirmTicks = 30;
string indicatorLightName { get { return Ca("Indicator light"); } }
double indicatorLightDelaySeconds = 0.5;
// =============================================
IMyRemoteControl rc;
FlightDataCache flightCache;
ThrusterControl thrust;
GyroControl gyros;
IMyShipConnector carrierConnector;
IMyLightingBlock carrierConnectorLight;
IMyLightingBlock indicatorLight;
IMyTextPanel carrierLcd;
bool carrierLcdConfigured = false;
MatrixD _tickRcMatrix = MatrixD.Identity;
Vector3D _tickRcPosition = Vector3D.Zero;
List<IMyGyro> allGyros = new List<IMyGyro>();
List<IMyThrust> allThrusters = new List<IMyThrust>();
List<IMyShipController> carrierControllers = new List<IMyShipController>();
const int carrierFlightCacheNormalRefreshTicks = 10;
// The actuator cache already refreshed during transit; share that fresher data.
const int carrierFlightCacheTransitRefreshTicks = 60;
int indicatorDelayTicksRemaining = 0;
int indicatorRefreshTicks = 0;
const int indicatorRefreshIntervalTicks = 30;
enum IndicatorTransitionState
{
    None,
    WaitEnableAfterColor,
    WaitNeutralAfterDisable
}

IndicatorTransitionState indicatorTransition = IndicatorTransitionState.None;
readonly Color indicatorAutopilotColor = new Color(0, 200, 255);
readonly Color indicatorNeutralColor = Color.White;
Color? _lastIndicatorColor = null;
bool? _lastIndicatorEnabled = null;
void SetIndicatorColor(Color c)
{
    if (_lastIndicatorColor.HasValue && _lastIndicatorColor.Value == c)
        return;
    indicatorLight.Color = c;
    _lastIndicatorColor = c;
}

void SetIndicatorEnabled(bool e)
{
    if (_lastIndicatorEnabled.HasValue && _lastIndicatorEnabled.Value == e)
        return;
    indicatorLight.Enabled = e;
    _lastIndicatorEnabled = e;
}

void SetIndicatorOffNow()
{
    if (indicatorLight == null)
        return;
    indicatorTransition = IndicatorTransitionState.None;
    indicatorDelayTicksRemaining = 0;
    indicatorLight.Enabled = false;
    indicatorLight.Color = indicatorNeutralColor;
    _lastIndicatorEnabled = false;
    _lastIndicatorColor = indicatorNeutralColor;
}

Vector3D targetPosition = Vector3D.Zero;
bool hasTarget = false;
bool autopilotEnabled = false;
bool scriptPaused = false;
bool pauseResumeAutopilot = false;
bool cockpitAbortPaused = false;
bool cockpitAbortResumeAutopilot = false;
Vector3D desiredForward = Vector3D.Zero;
Vector3D desiredUp = Vector3D.Zero;
double activeMaxSpeed = 0;
bool movementResetApplied = false;
enum CarrierAutopilotMode
{
    Precision,
    CruiseTurnTest,
    CruiseAccel,
    CruiseFlip,
    CruiseDecel
}

CarrierAutopilotMode carrierAutopilotMode = CarrierAutopilotMode.Precision;
enum CarrierMotionPhase
{
    BrakingBeforeTurn,
    Turning,
    Translating
}

CarrierMotionPhase carrierMotionPhase = CarrierMotionPhase.BrakingBeforeTurn;
int carrierTurnSettledTicks = 0;
const int carrierTurnSettleTicks = 30;
Vector3D carrierBrakeForward = Vector3D.Zero;
Vector3D carrierBrakeUp = Vector3D.Zero;
const double carrierStoppedSpeedMps = 0.10;
const double carrierStoppedAngularSpeedRad = 0.01;
Vector3D cruiseFinalPosition = Vector3D.Zero;
Vector3D cruiseFinalForward = Vector3D.Zero;
Vector3D cruiseFinalUp = Vector3D.Zero;
Vector3D cruiseStagingPosition = Vector3D.Zero;
Vector3D cruiseDirection = Vector3D.Zero;
Vector3D cruiseTurnTestStartForward = Vector3D.Zero;
int cruiseTurnTestTicks = 0;
double measuredFlipSeconds = 0;
double measuredFlipMass = 0;
bool carrierConnectorStateKnown = false;
bool lastCarrierConnectorConnected = false;
bool pathMode = false;
bool pathPaused = false;
bool pathPausedNavResume = false;
List<string> pathRecords = new List<string>();
int pathIndex = 0;
Vector3D pathAnchorPos = Vector3D.Zero;
Vector3D pathAnchorForward = Vector3D.Zero;
Vector3D pathAnchorUp = Vector3D.Zero;
bool pathAnchorValid = false;
double secondsPerTick = 1.0 / 60.0;
readonly int progressSaveIntervalTicks = 600;
int progressSaveTicks = 41;
int runtime = 0;
bool carrierHeavyWorkClaimed = false;
bool carrierMissionTransitionThisTick = false;
int carrierRuntimeWorkMask = 0;
string carrierLcdLastText = "";
string carrierLcdLastNativeText = "";
string pluginLcdMainText = "";
string pluginLcdReportText = "";
string pluginLcdMarker = "";
string publishedPluginLcdMarker = null;
int pluginLcdSequence = 0;
int pluginLcdHeartbeat = -1;
int pluginLcdHeartbeatAgeTicks = int.MaxValue;
bool pluginLcdReady = false;
double runtimePeakLastMinuteMs = 0;
double runtimePeakWindowElapsedSeconds = 0;
readonly string[] runtimeProfileSectionNames =
{
    "input-comms",
    "state-save",
    "flight-control",
    "mission-control",
    "lcd-output"
};
double[] runtimeProfileLastMs = new double[5];
int[] runtimeProfileLastInstructions = new int[5];
double[] runtimeProfilePeakMs = new double[5];
int[] runtimeProfilePeakInstructions = new int[5];
long runtimeProfileMarkerTicks = 0;
int runtimeProfileMarkerInstructions = 0;
bool runtimeProfileActive = false;
    readonly string[] runtimeDetailNames = {
        "pose", "connector-light", "connector-mass", "dock-pose", "path-complete-poll", "miner-progress-poll",
        "save-request", "save-pump", "progress-save", "autopilot-runner", "move", "reset-movement",
        "stationary-gate", "bridge-status", "hangar-enable-result", "hangar-store-result", "hangar-deploy-result",
        "full-auto", "path-runner", "manual-hangar", "unloading", "reports", "indicator", "info-cache",
        "recall-timeout", "lcd-fingerprint", "lcd-render", "plugin-lcd-publish", "log-flush"
    };
    double[] runtimeDetailLastMs = new double[29];
    int[] runtimeDetailLastInstructions = new int[29];
    int runtimeDetailMarkerInstructions = 0;
string runtimeProfilePeakDominant = "none";
    StringBuilder _runtimeProfileSb = new StringBuilder(2048);
StringBuilder _echoSb = new StringBuilder(256);
StringBuilder _lcdSb = new StringBuilder(1024);
StringBuilder _scriptLogSb = new StringBuilder(512);
StringBuilder _pluginLcdMarkerSb = new StringBuilder(2048);
// Separate bridge readers: status polling must not clear a command's document.
MyIni _bridgeCommandIni = new MyIni();
MyIni _bridgeStatusIni = new MyIni();
string _lcdPluginStateText, _lcdPluginMessageText, _lcdMinerStatusText;
int _lcdPluginStateHash, _lcdPluginMessageHash, _lcdMinerStatusHash;
List<string> _scriptLogPending = new List<string>();
const int scriptLogPendingLimit = 128;
const int scriptLogFlushBatchSize = 16;
const int scriptLogFlushIntervalTicks = 60;
const int scriptLogBacklogIntervalTicks = 10;
int nextScriptLogFlushRuntime = 0;
const string scriptLogBegin = "[EMMA3_SCRIPT_LOG_BEGIN]";
const string scriptLogEnd = "[EMMA3_SCRIPT_LOG_END]";
const int igcPollIntervalTicks = 10;
int lcdRefreshTicks = 7;
int lcdRefreshIntervalTicks = 60;
const int lcdRefreshMinTicks = 60;
int carrierLcdStateFingerprint = int.MinValue;

bool TryClaimCarrierHeavyWork()
{
    if (carrierHeavyWorkClaimed || carrierMissionTransitionThisTick)
        return false;
    carrierHeavyWorkClaimed = true;
    return true;
}

void MarkCarrierMissionTransition()
{
    carrierMissionTransitionThisTick = true;
}
int caMenuIndex = 0;
int caBehaviorPage = 0;
int caMinerBehaviorPage = 0;
bool caEditingValue = false;
bool caRefineryReorderArmed = false;
bool caRefineryOreMoving = false;
int caRefineryMoveOriginalIndex = -1;
int caEditingIndex = -1;
double caEditValue = 0;
double caThrustEditLimitKn = 0;
double caThrustEditUsePercent = 0;
int caThrustStepIndex = 5;
double cachedCarrierShipHeight = 0;
double cachedCarrierShipWidth = 0;
double cachedCarrierShipLength = 0;
double cachedCarrierHydrogenPercent = -1;
double cachedCarrierCargoPercent = -1;
int carrierInfoRefreshTicks = 173;
const int carrierInfoRefreshIntervalTicks = 300;
IMyTimerBlock carrierResourcesTimer;
enum CaLcdMenu
{
    Main,
    AutonomousMining,
    SelectOres,
    MinerOverview,
    MinerA,
    MinerBehaviorSettings,
    BehaviorSettings,
    ThrustLimit,
    ResetAsteroidsConfirm,
    Info
}

CaLcdMenu caLcdMenu = CaLcdMenu.Main;
List<IMyTimerBlock> _timerBlocks = new List<IMyTimerBlock>();
IMyTimerBlock _cachedPathTimer;
IMyTimerBlock _cachedStateIniTimer;
IMyTimerBlock _cachedStateRouteTimer;
IMyTimerBlock _cachedStateReportsTimer;
const string StateIniSection = "state";
const string ProgressIniSection = "progress";
MyIni _stateIni = new MyIni();
MyIni _progressIni = new MyIni();
bool _hasProgressState = false;
bool progressStateDirty = false;
long lastProgressStateFingerprint = long.MinValue;
enum HangarManagerPendingCommand
{
    None,
    Enable,
    Store,
    Deploy,
    StoreDelay,
    RefreshPose,
    RefreshContents,
    Disable
}

class MinerSlot
{
    public string Name = "Miner A";
    public string PbName = "";
    public string CommandTag = "";
    public string ProgressTag = "";
    public string CompleteTag = "";
    public IMyProgrammableBlock Pb;
    public IMyBroadcastListener PathCompleteListener;
    public IMyBroadcastListener ProgressListener;
    public int PathCompletePollTicks = 3;
    public int ProgressPollTicks = 227;
    public bool PathCompleteReceived = false;
    public string PathCompleteKind = "";
    public double ProgressMeters = 0;
    public double ProgressTotalMeters = 0;
    public string StatusText = "No contact";
    public double StatusCargoPercent = 0;
    public double StatusWaterPercent = 0;
    public int StatusPlane = 0;
    public int StatusPlaneTotal = 0;
    public int StatusLane = 0;
    public int StatusLaneTotal = 0;
    public double StatusAgeSeconds = 9999;
    public bool RecallRequested = false;
    public bool RecallActive = false;
    public bool RecallAcknowledged = false;
    public string RecallMissionId = "";
    public int RecallFailedTicks = 0;
    public double RecallRequestElapsedSeconds = 0;
    public bool HangarStoreReadyReceived = false;
    public bool HangarStoreAcceptedReceived = false;
    public bool HangarOnlineReceived = false;
    public bool HangarMissionReadyReceived = false;
    public bool HangarStored = false;
    public bool HangarStorePendingForSnapshot = false;
    public bool HangarStoreCommandSent = false;
    public HangarManagerPendingCommand HangarManagerPending = HangarManagerPendingCommand.None;
    public int HangarManagerRequestId = 0;
    public int HangarManagerWaitTicks = 0;
    public int HangarMinerOnlineWaitTicks = 0;
    public int HangarOperationId = 0;
    public int HangarPoseRequestId = 0;
    public string HangarStoreCommandText = "";
}

List<MinerSlot> minerSlots = new List<MinerSlot>();
int mineStationaryTicks = 0;
const int minerProgressPollIntervalTicks = 300;
const double minerContactTimeoutSeconds = 60;
const double mineRecallRequestTimeoutSeconds = 10.0;
bool fullAutoEnabled = false;
enum Emma3MissionState
{
    None = 0,
    WaitingPlan = 1,
    PreviewReady = 2,
    FlyingCarrierRoute = 3,
    WaitingStationary = 4,
    WaitingMiner = 5,
    WaitingHangarStore = 6,
    WaitingHangarDeploy = 7,
    WaitingStartupHangarInventory = 8,
    ReservedStartupHangarDeploy = 9,
    ReservedStartupHangarReturnDock = 10,
    WaitingMinerPlan = 11,
    WaitingCompletionAck = 12,
    WaitingPostCompletionHangarStore = 13,
    WaitingMinerStart = 14
}

Emma3MissionState emma3MissionState = Emma3MissionState.None;
enum ManualHangarCycleState
{
    None,
    WaitingStoreReady,
    DeployDelay,
    WaitingDeployReady,
    WaitingReturnDock,
    WaitingStoreOnlyReady,
    DeployOnlyStart,
    WaitingDeployOnlyReady,
    WaitingDeployOnlyReturnDock
}

ManualHangarCycleState manualHangarCycleState = ManualHangarCycleState.None;
int manualHangarCycleDelayTicks = 0;
int manualHangarCycleCommandRetryTicks = 0;
int manualHangarCycleCommandWaitTicks = 0;
const int manualHangarCycleStoreDelayTicks = 120;
const int hangarStorePowerOnDelayTicks = 60;
const int manualHangarCycleCommandRetryIntervalTicks = 60;
const int manualHangarCycleCommandTimeoutTicks = 600;
const int hangarManagerStoreOperationTimeoutTicks = 3900;
const int hangarManagerInventoryRefreshTimeoutTicks = 2400;
const int hangarManagerDeployOperationTimeoutTicks = 3900;
const int hangarMinerOnlineTimeoutTicks = 1800;
bool minerPathStartAcceptedReceived = false;
bool minerPathStartPreparingReceived = false;
int minerPathStartWaitTicks = 0;
int minerPathStartRetryTicks = 0;
const int minerPathStartRetryIntervalTicks = 60;
const int minerPathStartTimeoutTicks = 600;
const int minerPathStartLoadingTimeoutTicks = 3600;
IMyTimerBlock emma3BridgeTimer;
IMyTimerBlock carrierPluginInfoTimer;
IMyTimerBlock emma3CompletedAsteroidsTimer;
IMyTimerBlock oreNameTimer;
IMyTerminalBlock hangarBlock;
IMyTimerBlock hangarManagerTimer;
int emma3RequestSequence = 0;
int emma3ResetSequence = 0;
int emma3CompletionSequence = 0;
int emma3BridgeStatusTicks = 17; // offset from path-completion polling at phase 3
const int emma3BridgeStatusIntervalTicks = 30;
string emma3BridgeStatusRaw = "";
bool emma3BridgeStatusRawValid = false;
int emma3PlanPollTicks = 0;
long emma3ActiveEntityId = 0;
string emma3PluginState = "idle";
string emma3PluginMessage = "waiting for plugin";
string emma3MissionId = "";
bool emma3ContinueAvailable = false;
int emma3CompletedCount = 0;
List<Vector3D> emma3CarrierRoutePositions = new List<Vector3D>();
List<Vector3D> emma3CarrierRouteForwards = new List<Vector3D>();
List<Vector3D> emma3CarrierRouteUps = new List<Vector3D>();
int emma3CarrierRouteIndex = 0;
bool emma3CarrierParkAligning = false;
int fullAutoEnableStationaryTicks = 0;
bool emma3WaitForSeatBeforeNextPlan = false;
sealed class CarrierPlanLoadJob
{
    public MyIni Ini;
    public int Count, Index;
    public long EntityId;
    public List<Vector3D> Positions = new List<Vector3D>();
    public List<Vector3D> Forwards = new List<Vector3D>();
    public List<Vector3D> Ups = new List<Vector3D>();
}
CarrierPlanLoadJob carrierPlanLoadJob;
void EnsureMinerSlots()
{
    if (minerSlots.Count > 0)
        return;
    minerSlots.Add(new MinerSlot
    {
        Name = "Miner A",
        PbName = minerPbTag,
        CommandTag = minerCommandTag,
        ProgressTag = minerProgressTag,
        CompleteTag = minerPathCompleteTag
    });
}

MinerSlot ActiveMiner()
{
    EnsureMinerSlots();
    return minerSlots[0];
}

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
    allGyros = GetBlocks<IMyGyro>().FindAll(b => HasCarrierTag(b));
    allThrusters = GetBlocks<IMyThrust>().FindAll(b => HasCarrierTag(b));
    flightCache.SetBlocks(allThrusters, allGyros);
    thrust = new ThrusterControl(flightCache, tickSpeed, allThrusters);
    gyros = new GyroControl(flightCache, tickSpeed, allGyros);
    if (tickSpeed == UpdateFrequency.Update10)
        secondsPerTick = 10.0 / 60.0;
    else if (tickSpeed == UpdateFrequency.Update100)
        secondsPerTick = 100.0 / 60.0;
    LoadState();
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
    ScriptLog("Ready. Carrier script v0.8.41.");
    Runtime.UpdateFrequency = scriptPaused ? UpdateFrequency.None : tickSpeed;
}

void ScriptLog(string text)
{
    if (!emma3ScriptLogging)
        return;
    Echo(text);
    if (_scriptLogPending.Count >= scriptLogPendingLimit)
        _scriptLogPending.RemoveAt(0);
    _scriptLogPending.Add(text ?? "");
}

void UpdateScriptLogFlush()
{
    if (!emma3ScriptLogging || _scriptLogPending.Count == 0 || runtime < nextScriptLogFlushRuntime)
        return;
    FlushScriptLog();
    nextScriptLogFlushRuntime = runtime + (_scriptLogPending.Count > 0 ? scriptLogBacklogIntervalTicks : scriptLogFlushIntervalTicks);
}

void FlushScriptLog()
{
    if (!emma3ScriptLogging || _scriptLogPending.Count == 0)
        return;
    string data = Me.CustomData ?? "";
    int batchCount = LowAllocationText.CountScriptLogBatch(_scriptLogPending, scriptLogFlushBatchSize, 4096);
    if (batchCount == 0)
        return;
    int existingBegin = data.IndexOf(scriptLogBegin, StringComparison.Ordinal);
    if (existingBegin >= 0)
    {
        int existingEnd = data.IndexOf(scriptLogEnd, existingBegin + scriptLogBegin.Length, StringComparison.Ordinal);
        if (existingEnd < 0)
            return;
        StringBuilder queued = _scriptLogSb;
        queued.Clear();
        for (int i = 0; i < batchCount; i++)
        {
            queued.Append("L|");
            LowAllocationText.AppendScriptLog(queued, _scriptLogPending[i]);
            queued.AppendLine();
        }
        Me.CustomData = data.Insert(existingEnd, queued.ToString());
        _scriptLogPending.RemoveRange(0, batchCount);
        return;
    }

    StringBuilder sb = _scriptLogSb;
    sb.Clear();
    if (data.Length > 0 && !data.EndsWith("\n"))
        sb.AppendLine();
    sb.AppendLine(scriptLogBegin);
    for (int i = 0; i < batchCount; i++)
    {
        sb.Append("L|");
        LowAllocationText.AppendScriptLog(sb, _scriptLogPending[i]);
        sb.AppendLine();
    }
    sb.AppendLine(scriptLogEnd);
    Me.CustomData = data + sb.ToString();
    _scriptLogPending.RemoveRange(0, batchCount);
}

void ClearScriptLogQueue()
{
    _scriptLogPending.Clear();
    string data = Me.CustomData ?? "";
    int begin = data.IndexOf(scriptLogBegin, StringComparison.Ordinal);
    if (begin < 0)
        return;
    int end = data.IndexOf(scriptLogEnd, begin + scriptLogBegin.Length, StringComparison.Ordinal);
    if (end < 0)
        return;
    end += scriptLogEnd.Length;
    while (end < data.Length && (data[end] == '\r' || data[end] == '\n'))
        end++;
    while (begin > 0 && (data[begin - 1] == '\r' || data[begin - 1] == '\n'))
        begin--;
    Me.CustomData = data.Remove(begin, end - begin);
}

void ResetMovement()
{
    if (movementResetApplied)
        return;
    rc.DampenersOverride = true;
    gyros.Reset();
    thrust.Reset();
    carrierMotionPhase = CarrierMotionPhase.BrakingBeforeTurn;
    carrierTurnSettledTicks = 0;
    carrierBrakeForward = Vector3D.Zero;
    carrierBrakeUp = Vector3D.Zero;
    movementResetApplied = true;
}
