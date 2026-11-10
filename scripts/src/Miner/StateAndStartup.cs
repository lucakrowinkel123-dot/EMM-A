// Autominer Script
// ============= Commands ==============
// pause : pauses updates and releases controls without clearing current task
// resume/start : resumes after pause
// stop : aborts current task, clears target, and releases controls
// reset : clears target and releases controls
// dumpgps : appends current GPS + orientation snapshot to Custom Data
// Path timer storage (CustomData of pathTimerTag) uses a compact anchor-relative format:
//   line 1 (header): cx,cy,cz:ax,ay,az,a    -- absolute carrier-anchor pos + axis-angle rotation
//   path point     : dx,dy,dz:ax,ay,az,a    -- delta from anchor + own axis-angle rotation
//   mine area      : dx,dy,dz:ax,ay,az,a:w,h:depth -- corner-A delta, area axis-angle, w/h in area plane, depth
//   lane metadata  : 00;h,w,count:skip-ranges:depth-per-lane:destruction-ranges -- plugin-planned fixed depths and safe terrain-clearing sections
// The plugin writes this route; the Miner only parses and plays it.
// nexthole : back out of current hole if needed, then skip to the next generated area lane anchor
// nexthole all : back out if needed, then jump directly to the last lane
// cancelmine : stops mining state machine and turns drills off
// pathstart : loads records from timer with tag in pathTimerTag and executes row-by-row
// pathpause : pauses queue and active movement; minearea returns to current anchor and waits
// pathcontinue : resumes queue advancement after pathpause
// pathstop : stops path mode and clears remaining queued records
// dock : starts two-stage auto-dock sequence (outer pose, align, inner pose, connector)
// undock : disconnects and flies through the inner and outer dock poses while staying aligned
// SPEEDF <value> : sets forward drilling speed (m/s)
// SPEEDB <value> : sets backing-out speed (m/s)
// MINEH <value> : sets miner lane height footprint (m)
// MINEW <value> : sets miner lane width footprint (m)
// AUTOSERVICE <ON|OFF> : toggles automatic return-to-carrier service during path playback
// AUTOCARGO <percent> : cargo fill percent that triggers automatic service return
// AUTOHYDRO <percent> : hydrogen percent that triggers automatic service return
// TOFF r,u,f : sets target offset in local Right,Up,Forward meters
// TOFFRESET : clears target offset
// =====================================
// ========== Editable Script Settings ==========
string remoteControlName { get { return Mi("Remote Control"); } }
double maxSpeed = 20;
double arrivalTolerance = 2.5;
double miningArrivalTolerance = 0.25;
double orientationToleranceDeg = 1.0;
UpdateFrequency tickSpeed = UpdateFrequency.Update1;
double pointToPointAccelerationLimit = 2.0;
double pointToPointThrustUsePercent = 80;
double TOFFR = 0; // target offset Right (m)
double TOFFU = 0; // target offset Up (m)
double TOFFF = 5; // target offset Forward (m)
string pathTimerTag { get { return Mi("Path"); } }
// Storage owns recovery snapshots; these timers are optional INI mirrors.
string stateIniTimerTag { get { return Mi("INI Core"); } }
string stateRouteTimerTag { get { return Mi("INI Route"); } }
int maxStuckRetries = 3; // skip hole after this many stuck retries; 0 = retry forever
double standardMineDepth = 30.0;
string carrierConnectorName { get { return Ca("Connector (Hangar)"); } }
string minerConnectorName { get { return Mi("Connector"); } }
string carrierMergerName { get { return Ca("Merger"); } }
string minerMergerName { get { return Mi("Merger"); } }
string minerContainerTag { get { return Mi(""); } }
string minerEjectorTag { get { return Mi("Ejector"); } }
string dockBroadcastTag { get { return shipNames.Channel("EMM_A_DOCK_POSE"); } }
string minerPathCompleteTag { get { return shipNames.Channel("EMM_A_MINER_PATH_COMPLETE"); } }
string minerProgressTag { get { return shipNames.Channel("EMM_A_MINER_PROGRESS"); } }
string carrierStatusTag { get { return shipNames.Channel("EMM_A_CARRIER_STATUS"); } }
string minerCommandTag { get { return shipNames.Channel("EMM_A_MINER_COMMAND"); } }
string carrierMinerSettingsTimerName { get { return Ca("Plugin Bridge"); } }
string minerPluginInfoTimerName { get { return Mi("Plugin Info"); } }
string indicatorLightName { get { return Mi("Indicator light"); } }
string miningGravityGeneratorName { get { return Mi("Gravity Generator"); } }
string activeMissionId = "";
double indicatorLightDelaySeconds = 0.5;
double dockInnerPoseDistance = 5;
double dockOuterPoseUpDistance = 200.0;
double minerRouteStartExtra = 0.0; // extra clearance above the normal outer dock pose
bool mergeAssistedDocking = false;
double dockApproachSpeed = 15.0;
double dockFinalSpeed = 15.0;
double dockPoseGateDistance = 50.0;
double dockPoseTolerance = 0.25;
double dockPoseFreshSeconds = 5.0;
double dockConnectDistance = 0.25;
double carrierPoseForwardOffset = -1.25; // broadcaster sends connector position shifted forward by this amount
bool autoServiceReturnEnabled = true;
double autoServiceCargoTriggerPercent = 90.0;
double autoServiceHydrogenTriggerPercent = 20.0;
bool stopAtOnePercentFuel = true;
// Do not require the floating-point tank ratio to reach an exact 100%.
// Tanks commonly settle just below full, especially while the grid is consuming gas.
const double autoServiceHydrogenResumePercent = 99.0;
double autoServiceCargoResumePercent = 1.0;
int autoServiceCheckIntervalTicks = 60;
const double stoneEjectionTriggerPercent = 2.5;
const double stoneEjectionResumePercent = 1.0;
const double stoneEjectionCargoTriggerPercent = 90.0;
const double stoneEjectionCargoResumePercent = 85.0;
const int stoneEjectionMaxHoldTicks = 7200; // two minutes at Update1, then return for service
// =============================================
IMyRemoteControl rc;
FlightDataCache flightCache;
ThrusterControl thrust;
GyroControl gyros;
List<IMyShipDrill> drills = new List<IMyShipDrill>();
bool? _lastDrillTerrainClearing = null;
Vector3D targetPosition = Vector3D.Zero;
bool hasTarget = false;
bool autopilotEnabled = false;
bool scriptPaused = false;
bool pauseResumeAutopilot = false;
Vector3D desiredForward = Vector3D.Zero;
Vector3D desiredUp = Vector3D.Zero;
bool movementResetApplied = false;
bool minerTravelFacingValid = false;
Vector3D minerTravelFacingTarget = Vector3D.Zero;
Vector3D minerTravelFacingOffset = Vector3D.Zero;
Vector3D minerTravelFacingUp = Vector3D.Zero;
Vector3D minerTravelFacingForward = Vector3D.Zero;
string flightDiagCommandMode = "idle";
Vector3D flightDiagForward = Vector3D.Zero;
Vector3D flightDiagUp = Vector3D.Zero;
Vector3D flightDiagCommandAccel = Vector3D.Zero;
double flightDiagCommandSpeedLimit = 0;
bool flightDiagAligned = false;
bool flightDiagEmergency = false;
bool flightDiagAtPosition = false;
enum MiningState
{
    Idle,
    EnterLane,
    DrillForward,
    Backout
}

struct DepthRange
{
    public double Start;
    public double End;
    public DepthRange(double start, double end)
    {
        Start = start;
        End = end;
    }
}

MiningState miningState = MiningState.Idle;
bool miningEnabled = false;
Vector3D laneAnchor = Vector3D.Zero;
Vector3D drillAxis = Vector3D.Zero;
double laneDepth = 0;
double SPEEDF = 0.7; // forward drilling speed (m/s)
const double terrainClearingSpeedMultiplier = 2.0;
double SPEEDB = 2.5; // backing-out speed (m/s)
double activeMaxSpeed = 0;
double MINEH = 12.5; // miner lane height footprint (m)
double MINEW = 17.5; // miner lane width footprint (m)
bool areaMiningMode = false;
List<Vector3D> areaLaneAnchors = new List<Vector3D>();
int areaLaneIndex = 0;
int areaLaneCount = 0;
bool areaPlanValid = false;
int areaPlanActivationStage = 0;
int areaPlanRequestedStartLane = 0;
Vector3D areaPlanCornerA = Vector3D.Zero;
Vector3D areaPlanCornerB = Vector3D.Zero;
Vector3D areaPlanUp = Vector3D.Zero;
Vector3D areaPlanForward = Vector3D.Zero;
Vector3D areaPlanRight = Vector3D.Zero;
int areaPlanRows = 0;
int areaPlanCols = 0;
double areaPlanRowStart = 0;
double areaPlanRowStep = 0;
double areaPlanColStart = 0;
double areaPlanColStep = 0;
const string mineAreaSkipMetaPrefix = "00;";
List<int> areaSkipLaneIndexes = new List<int>(); // zero-based lanes loaded from the metadata line below the minearea
List<double> areaLaneDepths = new List<double>(); // plugin-provided fixed depth for every generated lane; zero means skipped
List<List<DepthRange>> areaLaneDestructionRanges = new List<List<DepthRange>>(); // plugin-provided safe terrain-clearing depth ranges per lane
bool retryCurrentLane = false;
bool returnToMinePoseAfterArea = false;
bool finishAreaAfterBackout = false;
Vector3D areaMineEntryPos = Vector3D.Zero;
Vector3D areaMineEntryForward = Vector3D.Zero;
Vector3D areaMineEntryUp = Vector3D.Zero;
bool pathMode = false;
bool pathPaused = false;
bool pathPausedNavResume = false;
bool pathDrillApproachActive = false;
List<string> pathRecords = new List<string>();
int pathIndex = 0;
int pathCompleteBroadcastTicks = 0;
const int pathCompleteBroadcastRepeatTicks = 600;
bool pathDockPending = false;
Vector3D pathAnchorPos = Vector3D.Zero;
Vector3D pathAnchorForward = Vector3D.Zero;
Vector3D pathAnchorUp = Vector3D.Zero;
bool pathAnchorValid = false;
int activeMineAreaRecordIndex = -1;
List<int> miningPlaneIndex = new List<int>();
int miningPlaneTotal;
bool recallActive = false;
bool recallWaitingForAreaExit = false;
bool recallWaitingForDock = false;
bool recallReverseMode = false;
enum RecallPreparationPhase
{
    None,
    EstimateStart,
    EstimateEnd,
    Build
}

RecallPreparationPhase recallPreparationPhase = RecallPreparationPhase.None;
bool recallPreparing = false;
int recallPreparationIndex = 0;
Vector3D recallPreparationStartPosition = Vector3D.Zero;
Vector3D recallPreparationPrevious = Vector3D.Zero;
double recallPreparationDistanceToStart = 0;
double recallPreparationDistanceToEnd = 0;
bool recallPreparationActiveTargetValid = false;
Vector3D recallPreparationActiveTarget = Vector3D.Zero;
bool recallPreparationAnchorPending = false;
bool recallPreparationDockPending = false;
bool recallPreparationSkippingMiningBranch = false;
bool recallPreparationKeepMiningBranchExit = false;
const int recallPreparationRecordsPerTick = 2;
List<string> recallRecords = new List<string>();
List<int> recallDisplayDone = new List<int>();
int recallIndex = 0;
int recallLastDone = 0;
int recallLastTotal = 0;
enum AutoServiceState
{
    None,
    Returning,
    WaitingService,
    Undocking,
    ReturningToWork
}

AutoServiceState autoServiceState = AutoServiceState.None;
string autoServiceReason = "";
bool autoServiceResumeMineArea = false;
int autoServiceResumeMineAreaRecordIndex = -1;
int autoServiceResumeAreaLaneIndex = 0;
Vector3D autoServiceResumeMineEntryPos = Vector3D.Zero;
Vector3D autoServiceResumeMineEntryForward = Vector3D.Zero;
Vector3D autoServiceResumeMineEntryUp = Vector3D.Zero;
int autoServiceCheckTicks = 0;
int autoServiceFuelCheckTicks = 0;
bool autoServiceFuelSampleValid = false;
double autoServiceFuelPercent = 100.0;
int autoServiceActionRetryTicks = 0;
const int autoServiceActionRetryIntervalTicks = 300;
List<string> autoServiceReturnRouteRecords = new List<string>();
int autoServiceReturnRouteIndex = 0;
bool autoServiceReturnRouteBuildPending = false;
bool autoServiceReturnRouteSkipAnchor = false;
int autoServiceReturnRouteBuildIndex = -1;
enum HangarState
{
    None = 0,
    FinalService = 1,
    UndockingForStore = 2,
    MovingToHangar = 3,
    AwaitingStore = 4,
    PostDeployCheck = 5,
    PostDeployDocking = 7,
    PostDeployService = 8,
    MissionReady = 9
}

HangarState hangarState = HangarState.None;
Vector3D hangarTargetPosition = Vector3D.Zero;
Vector3D hangarTargetForward = Vector3D.Zero;
Vector3D hangarTargetUp = Vector3D.Zero;
const double hangarMinerCenterForwardOffset = 0;
bool hangarLaunchUndocked = false;
int hangarBroadcastTicks = 0;
const int hangarBroadcastIntervalTicks = 60;
int hangarPostDeployCargoCycleBaseline = 0;
int hangarOperationId = 0;
int parsedHangarOperationId = 0;
double parsedHangarZoneHalfHeight = 0;
int hangarPhaseElapsedTicks = 0;
bool stoneEjectionHoldActive = false;
bool stoneEjectionResumeAutopilot = false;
int stoneEjectionHoldTicks = 0;
Vector3D stuckSamplePos = Vector3D.Zero;
double stuckNoMoveSeconds = 0;
int stuckRetryCount = 0;
bool stuckBackout = false;
const double stuckTimeoutSeconds = 5.0;
const double stuckMoveThreshold = 0.25;
double secondsPerTick = 1.0 / 60.0;
IMyShipConnector minerConnector;
IMyShipConnector carrierConnector;
IMyShipMergeBlock minerMerger;
IMyShipMergeBlock carrierMerger;
IMyLightingBlock indicatorLight;
IMyGravityGenerator miningGravityGenerator;
bool? _lastMiningGravityEnabled = null;
IMyTimerBlock carrierMinerSettingsTimer;
IMyTimerBlock minerPluginInfoTimer;
IMyBroadcastListener dockListener;
IMyBroadcastListener carrierStatusListener;
IMyBroadcastListener minerCommandListener;
MatrixD _tickRcMatrix = MatrixD.Identity;
Vector3D _tickRcPosition = Vector3D.Zero;
List<IMyGyro> allGyros = new List<IMyGyro>();
List<IMyThrust> allThrusters = new List<IMyThrust>();
List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
Vector3D carrierConnectorPos = Vector3D.Zero;
Vector3D carrierConnectorForward = Vector3D.Zero;
Vector3D carrierConnectorUp = Vector3D.Zero;
double carrierPoseAgeSeconds = 9999;
Vector3D connectorOffsetLocal = Vector3D.Zero;
enum DockingState
{
    None,
    UndockingRecompile,
    UndockingToPose,
    DockingToPose,
    DockingFinalApproach,
    UndockingToOuterPose,
    DockingAlignAtOuterPose,
    DockingToInnerPose,
    DockingPowerDown,
    UndockingPowerUp
}

DockingState dockingState = DockingState.None;
enum UndockStage
{
    None,
    Validate,
    WaitPluginDisconnect,
    DisableMerger,
    DisconnectConnector,
    WaitDisconnected,
    DisableConnector,
    ResetHandshake,
    PrepareDeparture
}

UndockStage undockStage = UndockStage.None;
int undockStageWaitTicks = 0;
const int undockDisconnectTimeoutTicks = 300;
long dockHardwareRequestId = 0;
string dockHardwareRequestAction = "";
int dockHardwareRequestWaitTicks = 0;
bool dockHardwareRequestPending = false;
bool dockHardwareNativeFallback = false;
const int dockHardwareDisconnectTimeoutTicks = 120;
const int dockHardwareConnectTimeoutTicks = 2160;
int undockRecompileTicksRemaining = 0;
const int undockRecompileTicks = 30;
int dockMergeReadyTicks = 0;
int dockConnectRetryTicks = 0;
int dockConnectRecompileTicksRemaining = 0;
bool dockConnectRecompileArmed = false;
const int dockConnectRecompileTicks = 30;
bool dockRetryPending = false;
int dockRetryTicks = 0;
const int dockRetryIntervalTicks = 900;
int dockingPhaseElapsedTicks = 0;
int dockingAttemptFailures = 0;
const int dockingPhaseTimeoutTicks = 3600;
const int dockingAlignmentTimeoutTicks = 10800;
const int dockingTravelPhaseStartTicks = dockingAlignmentTimeoutTicks + 1;
const int dockingAttemptFailureLimit = 3;
readonly int echoFrequency = 100; // every 100th game tick
int runtime = 0;
bool minerHeavyWorkClaimed = false;
bool minerMissionTransitionThisTick = false;

bool TryClaimMinerHeavyWork()
{
    if (minerHeavyWorkClaimed || minerMissionTransitionThisTick)
        return false;
    minerHeavyWorkClaimed = true;
    return true;
}

void MarkMinerMissionTransition()
{
    minerMissionTransitionThisTick = true;
}
double runtimePeakLastMinuteMs = 0;
double runtimePeakWindowElapsedSeconds = 0;
readonly string[] runtimeProfileSectionNames =
{
    "input-comms",
    "state-save",
    "mission-state",
    "flight-path",
    "transfer-output"
};
double[] runtimeProfileLastMs = new double[5];
int[] runtimeProfileLastInstructions = new int[5];
double[] runtimeProfilePeakMs = new double[5];
int[] runtimeProfilePeakInstructions = new int[5];
long runtimeProfileMarkerTicks = 0;
int runtimeProfileMarkerInstructions = 0;
bool runtimeProfileActive = false;
int minerRuntimeWorkMask = 0;
readonly string[] runtimeDetailNames = {
    "pose", "plugin-age", "fuel-cache", "cargo-scan", "route-meter", "dock-pose-age", "dock-pose-message",
    "carrier-status-poll", "command-poll", "path-load", "echo", "save-pump", "save-request", "log-flush",
    "power-transition", "area-plan", "mining-state", "undock", "docking-state", "dock-retry", "auto-service",
    "hangar", "movement-reset", "move", "recall-prep", "recall-runner", "path-runner", "gravity",
    "unload-recovery", "path-complete", "ore-transfer", "indicator", "progress-broadcast", "status-broadcast",
    "dock-demand", "dock-hardware-publish", "plugin-publish",
    "path-record-start", "path-commit", "path-record-parse", "area-metadata", "area-lane-layout",
    "area-start", "area-command-parse", "service-start", "service-recall-complete", "service-route-build",
    "service-area-resume", "power-begin", "power-complete", "echo-format", "log-escape",
    "native-connect", "native-disconnect", "power-thruster", "power-gyro", "power-battery", "power-tank",
    "echo-api", "log-enqueue"
};
double[] runtimeDetailLastMs = new double[60];
int[] runtimeDetailLastInstructions = new int[60];
long runtimeDetailChargedTicks, runtimeDetailChargedInstructions;
bool runtimeProfilePeakAvailable = false;
string runtimeProfilePeakDominant = "none";
StringBuilder _runtimeProfileSb = new StringBuilder(3072);
long dockingFinalProfileStartTicks = 0;
double dockingFinalProfileAxesMs = 0;
double dockingFinalProfileStatusMs = 0;
double dockingFinalProfileRequestMs = 0;
double dockingFinalProfileFallbackMs = 0;
StringBuilder _echoSb = new StringBuilder(256);
readonly string[] pluginInfoMessages = new string[5];
readonly StringBuilder _pluginInfoSb = new StringBuilder(1024);
bool pluginServiceRequestsDirty = true;
bool pluginInfoDirty = true;
string publishedPluginInfo = null;
const int igcPollIntervalTicks = 10;
int dockPosePollTicks = 3; // keep polling off the 100-tick echo/save slot
int minerProgressBroadcastTicks = 7;
const int minerProgressBroadcastIntervalTicks = 300;
int minerStatusBroadcastTicks = 17; // avoid echo/save and communications polling
const int minerStatusBroadcastIntervalTicks = 900; // fifteen seconds of game ticks
double routeProgressMeters = 0;
double routeProgressTotalMeters = 0;
Vector3D routeProgressPreviousPosition = Vector3D.Zero;
bool routeProgressPreviousValid = false;
bool routeProgressTracking = false;
bool emma3ScriptLogging = true;
List<string> _scriptLogPending = new List<string>();
StringBuilder _scriptLogSb = new StringBuilder(512);
const int scriptLogPendingLimit = 128;
const int scriptLogFlushBatchSize = 16;
const int scriptLogFlushIntervalTicks = 60;
const int scriptLogBacklogIntervalTicks = 10;
int nextScriptLogFlushRuntime = 0;
const string scriptLogBegin = "[EMMA3_SCRIPT_LOG_BEGIN]";
const string scriptLogEnd = "[EMMA3_SCRIPT_LOG_END]";
bool oreTransferActive = false;
int oreTransferStartDelayTicks = 0;
bool pluginOreTransferEnabled = false;
bool pluginPowerTransitionsEnabled = false;
bool pluginRemoteLinkEnabled = false;
readonly long pluginServiceSession = DateTime.UtcNow.Ticks;
int pluginFeatureAgeTicks = int.MaxValue;
const int pluginFeatureLeaseTicks = 480;
bool pluginUnloadRequestPending = false;
long pluginUnloadRequestId = 0;
int pluginUnloadWaitTicks = 0;
const int pluginUnloadTimeoutTicks = 7200;
bool pluginPowerRequestPending = false;
long pluginPowerRequestId = 0;
int pluginPowerWaitTicks = 0;
const int pluginPowerTimeoutTicks = 600;
int pluginRemoteTelemetryAgeTicks = int.MaxValue;
int pluginRemotePoseWaitTicks = 0;
const int pluginRemoteTelemetryFreshTicks = 420;
const int pluginRemotePoseGraceTicks = 120;
string pluginPowerBlockIds = "";
string pluginHydrogenTankIds = "";
List<IMyTerminalBlock> _oreTransferSources = new List<IMyTerminalBlock>();
List<IMyTerminalBlock> _autoServiceCargoBlocks = new List<IMyTerminalBlock>();
int _cargoStatsBlockIndex = 0;
int _cargoStatsInventoryIndex = 0;
double _cargoStatsCurrent = 0;
double _cargoStatsMax = 0;
double _cargoStatsStoneVolume = 0;
bool _cargoStatsOrePresent = false;
double _cachedCargoPercent = 0;
double _cachedStonePercent = 0;
bool _cachedOrePresent = false;
int autoServiceDiagnosticTicks = 0;
int cargoStatsCompletedCycles = 0;
int cargoDetailCompletedCycles = 0;
bool cargoDetailsCurrent = false;
bool _cargoStatsScanStarted = false;
bool _cargoStatsIncludeItems = false;
int stoneEjectionDetailCycleBaseline = 0;
bool minerHydrogenCacheValid = false;
int minerHydrogenCacheTicks = 0;
int cachedMinerHydrogenTankCount = 0;
double cachedMinerHydrogenPercent = 0;
int dockingPausedEchoTicks = 0;
const int dockingPausedEchoIntervalTicks = 120;
// Cached block/item lists reused by GetOreAmount to avoid per-call GTS scans and allocations
List<MyInventoryItem> _invItems = new List<MyInventoryItem>();
// Cached path timer reference to avoid repeated GTS scans during path recording
List<IMyTimerBlock> _timerBlocks = new List<IMyTimerBlock>();
IMyTimerBlock _cachedPathTimer;
IMyTimerBlock _cachedStateIniTimer;
IMyTimerBlock _cachedStateRouteTimer;
const string StateIniSection = "state";
const string ProgressIniSection = "progress";
MyIni _stateIni = new MyIni();
MyIni _progressIni = new MyIni();
bool _hasProgressState = false;
bool fullStateDirty = false;
bool progressStateDirty = false;
long lastProgressStateFingerprint = long.MinValue;
int indicatorDelayTicksRemaining = 0;
int indicatorRefreshTicks = 11;
const int indicatorRefreshIntervalTicks = 30;
enum IndicatorTransitionState
{
    None,
    WaitEnableAfterColor,
    WaitNeutralAfterDisable
}

IndicatorTransitionState indicatorTransition = IndicatorTransitionState.None;
readonly Color indicatorAutopilotColor = new Color(0, 200, 255);
readonly Color indicatorMiningPlaneColor = new Color(255, 128, 0);
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

void UpdateMiningGravityGenerator()
{
    if (miningGravityGenerator == null)
        return;
    bool shouldEnable = areaMiningMode;
    if (_lastMiningGravityEnabled.HasValue && _lastMiningGravityEnabled.Value == shouldEnable)
        return;
    miningGravityGenerator.Enabled = shouldEnable;
    _lastMiningGravityEnabled = shouldEnable;
    ScriptLog("Mining gravity generator " + (shouldEnable ? "enabled at mining plane." : "disabled away from mining plane."));
}

public Program()
{
    InitializeShipNames(false, false);
    // Every flight calculation uses this one explicitly named Remote Control.
    // Do not fall back to a cockpit or an arbitrary controller: a different
    // block position/orientation would change the Miner's navigation frame.
    rc = GetRequiredMinerRemoteControl();
    flightCache = new FlightDataCache(rc);
    RefreshTickPose();
    allGyros = GetBlocks<IMyGyro>().FindAll(b => HasMinerTag(b));
    allThrusters = GetBlocks<IMyThrust>().FindAll(b => HasMinerTag(b));
    flightCache.SetBlocks(allThrusters, allGyros);
    thrust = new ThrusterControl(flightCache, tickSpeed, allThrusters);
    gyros = new GyroControl(flightCache, tickSpeed, allGyros);
    drills = GetBlocks<IMyShipDrill>();
    antennas = GetBlocks<IMyRadioAntenna>();
    batteries = GetBlocks<IMyBatteryBlock>();
    hydrogenTanks = GetBlocks<IMyGasTank>();
    CacheMinerPowerBlocks();
    minerConnector = GetBlock<IMyShipConnector>(minerConnectorName, true) ?? GetBlock<IMyShipConnector>();
    carrierConnector = GetBlock<IMyShipConnector>(carrierConnectorName, true);
    minerMerger = GetBlock<IMyShipMergeBlock>(minerMergerName, true);
    carrierMerger = GetBlock<IMyShipMergeBlock>(carrierMergerName, true);
    indicatorLight = GetBlock<IMyLightingBlock>(indicatorLightName, true);
    miningGravityGenerator = GetBlock<IMyGravityGenerator>(miningGravityGeneratorName, true);
    RefreshMinerCargoStatsBlocks();
    CacheOreTransferBlocks();
    CachePluginServiceIds();
    RefreshTimerCache();
    CacheNamedTimerReferences();
    carrierMinerSettingsTimer = GridTerminalSystem.GetBlockWithName(carrierMinerSettingsTimerName) as IMyTimerBlock;
    minerPluginInfoTimer = GridTerminalSystem.GetBlockWithName(minerPluginInfoTimerName) as IMyTimerBlock;
    dockListener = IGC.RegisterBroadcastListener(dockBroadcastTag + "_V2");
    carrierStatusListener = IGC.RegisterBroadcastListener(carrierStatusTag);
    minerCommandListener = IGC.RegisterBroadcastListener(minerCommandTag);
    UpdateConnectorOffsetLocal();
    if (tickSpeed == UpdateFrequency.Update10)
    {
        secondsPerTick = 10.0 / 60.0;
        echoFrequency = 10;
    }
    else if (tickSpeed == UpdateFrequency.Update100)
    {
        secondsPerTick = 100.0 / 60.0;
        echoFrequency = 1;
    }

    LoadState();
    PublishMinerPlannerSettings();
    if (timerPersistenceEnabled && (_cachedStateIniTimer == null || _cachedStateRouteTimer == null))
        ScriptLog("WARNING: INI timer copies unavailable; Miner state still saves internally.");
    if (!mergeAssistedDocking) SetMergerEnabled(false);
    RebuildMiningPlaneIndex();
    UpdateMinerDockDemand();
    // Upgrade/recompile recovery uses a fresh session, retaining startup-cached IDs.
    bool resumeUnload = oreTransferActive || oreTransferStartDelayTicks > 0
        || autoServiceState == AutoServiceState.WaitingService
        || hangarState == HangarState.FinalService || hangarState == HangarState.PostDeployService
        || pathMode && pathIndex >= pathRecords.Count;
    oreTransferActive = false;
    oreTransferStartDelayTicks = 0;
    RecoverMinerPowerTransition();
    InitializeMinerSaveWork();
    if (resumeUnload && IsMinerDocked()) ScheduleOreTransfer();
    UpdateMiningGravityGenerator();
    if (miningGravityGenerator == null)
        ScriptLog("Mining gravity generator not found: '" + miningGravityGeneratorName + "'.");
    ScriptLog("Ready. Miner script v0.8.41; flight diagnostics format 1.");
    Runtime.UpdateFrequency = scriptPaused ? UpdateFrequency.None : tickSpeed;
}

void ScriptLog(string text)
{
    if (!emma3ScriptLogging)
        return;
    var echoDetail = BeginRuntimeDetail();
    try { Echo(text); }
    finally { EndRuntimeDetail(58, echoDetail); }
    var enqueueDetail = BeginRuntimeDetail();
    try {
    if (_scriptLogPending.Count >= scriptLogPendingLimit)
        _scriptLogPending.RemoveAt(0);
    _scriptLogPending.Add(text ?? "");
    } finally { EndRuntimeDetail(59, enqueueDetail); }
}

void UpdateScriptLogFlush()
{
    if (!emma3ScriptLogging || _scriptLogPending.Count == 0 || runtime < nextScriptLogFlushRuntime)
        return;
    if (IsUndockBackgroundFrozen())
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
    minerTravelFacingValid = false;
    if (movementResetApplied)
        return;
    gyros.Reset();
    thrust.Reset();
    movementResetApplied = true;
    flightDiagCommandMode = "controls-reset";
    flightDiagForward = Vector3D.Zero;
    flightDiagUp = Vector3D.Zero;
    flightDiagCommandAccel = Vector3D.Zero;
    flightDiagCommandSpeedLimit = 0;
    flightDiagAligned = false;
    flightDiagEmergency = false;
    flightDiagAtPosition = false;
}
