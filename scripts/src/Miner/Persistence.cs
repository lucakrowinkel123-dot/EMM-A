// BEGIN INTERNAL PERSISTENCE
// Storage always owns a complete checkpoint. Timers are optional inspectable
// mirrors, never the commit record. Length-prefixed parts avoid duplicating INI
// sections and preserve existing multiline values without escaping/reparsing.
bool timerPersistenceEnabled = false;
const string InternalStateHeader = "EMMA-STORAGE-1\n";
string _internalCore = "", _internalRoute = "", _internalReports = "", _internalProgress = "";

void SetTimerPersistence(bool enabled)
{
    if (timerPersistenceEnabled == enabled) return;
    timerPersistenceEnabled = enabled;
    // A mirror may already have committed its old mode internally. Restart the
    // work so a toggle cannot be swallowed by that older full-save job.
    CancelMinerSaveWork();
    RequestMinerSave(true);
}

string EncodeInternalState(string core, string route, string reports, string progress)
{
    core = core ?? ""; route = route ?? ""; reports = reports ?? ""; progress = progress ?? "";
    return InternalStateHeader + (timerPersistenceEnabled ? "1\n" : "0\n")
        + core.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + core
        + route.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + route
        + reports.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + reports
        + progress.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + progress;
}

bool ReadInternalPart(string data, ref int index, out string part)
{
    part = "";
    int end = data.IndexOf('\n', index), length;
    if (end < 0 || !int.TryParse(data.Substring(index, end - index),
        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length)
        || length < 0 || length > data.Length - end - 1) return false;
    index = end + 1;
    part = data.Substring(index, length);
    index += length;
    return true;
}

bool TryReadInternalState()
{
    string data = Storage ?? "";
    if (!data.StartsWith(InternalStateHeader, StringComparison.Ordinal)) return false;
    int index = InternalStateHeader.Length;
    if (index + 2 > data.Length || data[index + 1] != '\n' || data[index] != '0' && data[index] != '1') return false;
    bool enabled = data[index] == '1';
    index += 2;
    string core, route, reports, progress;
    if (!ReadInternalPart(data, ref index, out core) || !ReadInternalPart(data, ref index, out route)
        || !ReadInternalPart(data, ref index, out reports) || !ReadInternalPart(data, ref index, out progress)
        || index != data.Length || string.IsNullOrWhiteSpace(core)) return false;
    _internalCore = core; _internalRoute = route; _internalReports = reports; _internalProgress = progress;
    timerPersistenceEnabled = enabled;
    return true;
}

void PublishInternalState(string core, string route, string reports)
{
    // Build the replacement first; allocation failure must leave the old complete
    // Storage and its cached base untouched.
    string next = EncodeInternalState(core, route, reports, "");
    Storage = next;
    _internalCore = core; _internalRoute = route; _internalReports = reports; _internalProgress = "";
}

bool PublishInternalProgress(string progress)
{
    if (string.IsNullOrWhiteSpace(_internalCore))
    {
        // Never publish an overlay without its complete base. This also handles
        // a newly placed PB before its first full checkpoint has completed.
        RequestMinerSave(true);
        return false;
    }
    string next = EncodeInternalState(_internalCore, _internalRoute, _internalReports, progress);
    if (!string.Equals(Storage, next, StringComparison.Ordinal)) Storage = next;
    _internalProgress = progress;
    return true;
}
// END INTERNAL PERSISTENCE

// BEGIN BOUNDED MINER SAVE
// A snapshot is captured without formatting, then serialized over later ticks.
// No stage reads live mission fields. A successful delayed commit never clears
// dirty flags raised after capture. The existing timer and PB Storage remain
// the only two persistence stores.
struct MinerSaveValue
{
    public int Kind;
    public long Integer;
    public double Number;
    public Vector3D Vector;
    public string Text;
}

const int minerSaveFieldsPerTick = 8;
readonly string[] _minerSaveKeys = {
    "autopilotEnabled",
    "scriptPaused",
    "pauseResumeAutopilot",
    "hasTarget",
    "targetPosition",
    "desiredForward",
    "desiredUp",
    "miningEnabled",
    "miningState",
    "laneAnchor",
    "drillAxis",
    "laneDepth",
    "SPEEDF",
    "SPEEDB",
    "MINEH",
    "MINEW",
    "minerRouteStartExtra",
    "mergeAssistedDocking",
    "TOFFR",
    "TOFFU",
    "TOFFF",
    "activeMaxSpeed",
    "pointToPointAccelerationLimit",
    "pointToPointThrustUsePercent",
    "activeMissionId",
    "areaMiningMode",
    "areaLaneIndex",
    "areaPlanValid",
    "areaPlanCornerA",
    "areaPlanCornerB",
    "areaPlanUp",
    "areaPlanForward",
    "returnToMinePoseAfterArea",
    "finishAreaAfterBackout",
    "areaMineEntryPos",
    "areaMineEntryForward",
    "areaMineEntryUp",
    "activeMineAreaRecordIndex",
    "retryLane",
    "stuckBackout",
    "stuckRetryCount",
    "areaLaneCount",
    "pathMode",
    "pathPaused",
    "pathPausedNavResume",
    "pathDrillApproachActive",
    "pathDockPending",
    "pathIndex",
    "pathCompleteBroadcastTicks",
    "routeProgressMeters",
    "routeProgressTotalMeters",
    "routeProgressPreviousPosition",
    "routeProgressPreviousValid",
    "routeProgressTracking",
    "pathAnchorValid",
    "pathAnchorPos",
    "pathAnchorForward",
    "pathAnchorUp",
    "recallActive",
    "recallWaitingForAreaExit",
    "recallWaitingForDock",
    "recallReverseMode",
    "recallIndex",
    "recallLastDone",
    "recallLastTotal",
    "autoServiceReturnEnabled",
    "autoServiceCargoTriggerPercent",
    "autoServiceHydrogenTriggerPercent",
    "autoServiceState",
    "autoServiceReason",
    "autoServiceResumeMineArea",
    "autoServiceResumeMineAreaRecordIndex",
    "autoServiceResumeAreaLaneIndex",
    "autoServiceResumeMineEntryPos",
    "autoServiceResumeMineEntryForward",
    "autoServiceResumeMineEntryUp",
    "autoServiceCheckTicks",
    "autoServiceActionRetryTicks",
    "autoServiceReturnRouteIndex",
    "hangarState",
    "hangarTargetPosition",
    "hangarTargetForward",
    "hangarTargetUp",
    "hangarOperationId",
    "hangarPhaseElapsedTicks",
    "hangarLaunchUndocked",
    "stoneEjectionHoldActive",
    "stoneEjectionResumeAutopilot",
    "stoneEjectionHoldTicks",
    "standardMineDepth",
    "dockingState",
    "undockRecompileTicksRemaining",
    "dockMergeReadyTicks",
    "dockConnectRetryTicks",
    "dockConnectRecompileTicksRemaining",
    "dockConnectRecompileArmed",
    "dockRetryPending",
    "dockRetryTicks",
    "dockingPhaseElapsedTicks",
    "dockingAttemptFailures",
    "connectorOffsetLocal",
    "oreTransferActive",
    "oreTransferStartDelayTicks",
    "minerUnloadSession",
    "oreTransferSucceeded",
    "minerDockConnectorId",
    "minerDockSession",
    "pendingUndock",
    "minerPowerGoal"
};
readonly int[] _minerProgressSaveIndexes = { 0, 3, 4, 5, 6, 7, 8, 9, 10, 11, 21, 24, 25, 26, 41, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 42, 43, 45, 46, 47, 54, 58, 59, 60, 61, 62, 68, 70, 71, 72, 73, 74, 75, 78, 79, 85, 83, 84, 86, 87, 90, 96, 98, 99, 101, 104, 105, 106, 107, 108 };
readonly int[] _minerPlanVectorSaveIndexes = { 28, 29, 30, 31, 34, 35, 36, 73, 74, 75 };
readonly int[] _minerSavePlanVectorSlots = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 1, 2, 3, -1, -1, 4, 5, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 7, 8, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
readonly MinerSaveValue[] _minerSaveValues = new MinerSaveValue[109];
readonly MyIni _minerPendingSaveIni = new MyIni();
readonly MyIni _minerPendingRouteIni = new MyIni();
readonly MinerSaveValue[] _minerFormattedSaveValues = new MinerSaveValue[109];
readonly string[] _minerFormattedSaveText = new string[109];
string _minerSaveSerialized, _minerRouteSaveSerialized;
bool _minerSavePending, _minerSaveFull;
int _minerSaveIndex, _minerSaveCaptureTick, _minerSaveLastWorkTick, _minerSaveRenderStage, _minerSavePublishStage;
long _stateSnapshotGeneration, _minerSaveGeneration;

bool IsMinerRouteSaveIndex(int index)
{
    return index >= 24 && index <= 64 || index >= 70 && index <= 75;
}

void CaptureMinerSaveInteger(int index, long value)
{
    _minerSaveValues[index].Kind = 0;
    _minerSaveValues[index].Integer = value;
}

void CaptureMinerSaveNumber(int index, double value)
{
    _minerSaveValues[index].Kind = 1;
    _minerSaveValues[index].Number = value;
}

void CaptureMinerSaveVector(int index, Vector3D value, bool direction)
{
    _minerSaveValues[index].Kind = direction ? 3 : 2;
    _minerSaveValues[index].Vector = value;
}

void CaptureMinerSaveText(int index, string value)
{
    _minerSaveValues[index].Kind = 4;
    _minerSaveValues[index].Text = value;
}

void CaptureMinerSaveBoolean(int index, bool value)
{
    _minerSaveValues[index].Kind = 5;
    _minerSaveValues[index].Integer = value ? 1 : 0;
}

void CaptureMinerSaveSnapshot(bool full = true)
{
    CaptureMinerSaveInteger(0, autopilotEnabled ? 1 : 0);
    CaptureMinerSaveInteger(3, hasTarget ? 1 : 0);
    CaptureMinerSaveVector(4, targetPosition, false);
    CaptureMinerSaveVector(5, desiredForward, true);
    CaptureMinerSaveVector(6, desiredUp, true);
    CaptureMinerSaveInteger(7, miningEnabled ? 1 : 0);
    CaptureMinerSaveInteger(8, (int)miningState);
    CaptureMinerSaveVector(9, laneAnchor, false);
    CaptureMinerSaveVector(10, drillAxis, true);
    CaptureMinerSaveNumber(11, laneDepth);
    CaptureMinerSaveNumber(21, activeMaxSpeed);
    CaptureMinerSaveText(24, activeMissionId);
    CaptureMinerSaveInteger(25, areaMiningMode ? 1 : 0);
    CaptureMinerSaveInteger(26, areaLaneIndex);
    CaptureMinerSaveInteger(27, areaPlanValid ? 1 : 0);
    CaptureMinerSaveVector(28, areaPlanCornerA, false);
    CaptureMinerSaveVector(29, areaPlanCornerB, false);
    CaptureMinerSaveVector(30, areaPlanUp, true);
    CaptureMinerSaveVector(31, areaPlanForward, true);
    CaptureMinerSaveInteger(32, returnToMinePoseAfterArea ? 1 : 0);
    CaptureMinerSaveInteger(33, finishAreaAfterBackout ? 1 : 0);
    CaptureMinerSaveVector(34, areaMineEntryPos, false);
    CaptureMinerSaveVector(35, areaMineEntryForward, true);
    CaptureMinerSaveVector(36, areaMineEntryUp, true);
    CaptureMinerSaveInteger(37, activeMineAreaRecordIndex);
    CaptureMinerSaveInteger(38, retryCurrentLane ? 1 : 0);
    CaptureMinerSaveInteger(39, stuckBackout ? 1 : 0);
    CaptureMinerSaveInteger(40, stuckRetryCount);
    CaptureMinerSaveInteger(41, areaLaneCount);
    CaptureMinerSaveInteger(42, pathMode ? 1 : 0);
    CaptureMinerSaveInteger(43, pathPaused ? 1 : 0);
    CaptureMinerSaveInteger(45, pathDrillApproachActive ? 1 : 0);
    CaptureMinerSaveInteger(46, pathDockPending ? 1 : 0);
    CaptureMinerSaveInteger(47, pathIndex);
    CaptureMinerSaveInteger(54, pathAnchorValid ? 1 : 0);
    CaptureMinerSaveInteger(58, recallActive ? 1 : 0);
    CaptureMinerSaveInteger(59, recallWaitingForAreaExit ? 1 : 0);
    CaptureMinerSaveInteger(60, recallWaitingForDock ? 1 : 0);
    CaptureMinerSaveInteger(61, recallReverseMode ? 1 : 0);
    CaptureMinerSaveInteger(62, recallIndex);
    CaptureMinerSaveInteger(68, (int)autoServiceState);
    CaptureMinerSaveInteger(70, autoServiceResumeMineArea ? 1 : 0);
    CaptureMinerSaveInteger(71, autoServiceResumeMineAreaRecordIndex);
    CaptureMinerSaveInteger(72, autoServiceResumeAreaLaneIndex);
    CaptureMinerSaveVector(73, autoServiceResumeMineEntryPos, false);
    CaptureMinerSaveVector(74, autoServiceResumeMineEntryForward, true);
    CaptureMinerSaveVector(75, autoServiceResumeMineEntryUp, true);
    CaptureMinerSaveInteger(78, autoServiceReturnRouteIndex);
    CaptureMinerSaveInteger(79, (int)hangarState);
    CaptureMinerSaveInteger(83, hangarOperationId);
    CaptureMinerSaveInteger(84, hangarPhaseElapsedTicks);
    CaptureMinerSaveInteger(85, hangarLaunchUndocked ? 1 : 0);
    CaptureMinerSaveInteger(86, stoneEjectionHoldActive ? 1 : 0);
    CaptureMinerSaveInteger(87, stoneEjectionResumeAutopilot ? 1 : 0);
    CaptureMinerSaveInteger(90, (int)dockingState);
    CaptureMinerSaveInteger(96, dockRetryPending ? 1 : 0);
    CaptureMinerSaveInteger(98, dockingPhaseElapsedTicks);
    CaptureMinerSaveInteger(99, dockingAttemptFailures);
    CaptureMinerSaveInteger(101, oreTransferActive ? 1 : 0);
    CaptureMinerSaveBoolean(104, oreTransferSucceeded);
    CaptureMinerSaveInteger(105, minerDockConnectorId);
    CaptureMinerSaveInteger(106, minerDockSession);
    CaptureMinerSaveBoolean(107, pendingUndock);
    CaptureMinerSaveInteger(108, minerPowerGoal);
    if (full)
    {
        CaptureMinerSaveInteger(1, scriptPaused ? 1 : 0);
        CaptureMinerSaveInteger(2, pauseResumeAutopilot ? 1 : 0);
        CaptureMinerSaveNumber(12, SPEEDF);
        CaptureMinerSaveNumber(13, SPEEDB);
        CaptureMinerSaveNumber(14, MINEH);
        CaptureMinerSaveNumber(15, MINEW);
        CaptureMinerSaveNumber(16, minerRouteStartExtra);
        CaptureMinerSaveInteger(17, mergeAssistedDocking ? 1 : 0);
        CaptureMinerSaveNumber(18, TOFFR);
        CaptureMinerSaveNumber(19, TOFFU);
        CaptureMinerSaveNumber(20, TOFFF);
        CaptureMinerSaveNumber(22, pointToPointAccelerationLimit);
        CaptureMinerSaveNumber(23, pointToPointThrustUsePercent);
        CaptureMinerSaveInteger(44, pathPausedNavResume ? 1 : 0);
        CaptureMinerSaveInteger(48, pathCompleteBroadcastTicks);
        CaptureMinerSaveNumber(49, routeProgressMeters);
        CaptureMinerSaveNumber(50, routeProgressTotalMeters);
        CaptureMinerSaveVector(51, routeProgressPreviousPosition, false);
        CaptureMinerSaveInteger(52, routeProgressPreviousValid ? 1 : 0);
        CaptureMinerSaveInteger(53, routeProgressTracking ? 1 : 0);
        CaptureMinerSaveVector(55, pathAnchorPos, false);
        CaptureMinerSaveVector(56, pathAnchorForward, true);
        CaptureMinerSaveVector(57, pathAnchorUp, true);
        CaptureMinerSaveInteger(63, recallLastDone);
        CaptureMinerSaveInteger(64, recallLastTotal);
        CaptureMinerSaveInteger(65, autoServiceReturnEnabled ? 1 : 0);
        CaptureMinerSaveNumber(66, autoServiceCargoTriggerPercent);
        CaptureMinerSaveNumber(67, autoServiceHydrogenTriggerPercent);
        CaptureMinerSaveText(69, autoServiceReason);
        CaptureMinerSaveInteger(76, autoServiceCheckTicks);
        CaptureMinerSaveInteger(77, autoServiceActionRetryTicks);
        CaptureMinerSaveVector(80, hangarTargetPosition, false);
        CaptureMinerSaveVector(81, hangarTargetForward, true);
        CaptureMinerSaveVector(82, hangarTargetUp, true);
        CaptureMinerSaveInteger(88, stoneEjectionHoldTicks);
        CaptureMinerSaveNumber(89, standardMineDepth);
        CaptureMinerSaveInteger(91, undockRecompileTicksRemaining);
        CaptureMinerSaveInteger(92, dockMergeReadyTicks);
        CaptureMinerSaveInteger(93, dockConnectRetryTicks);
        CaptureMinerSaveInteger(94, dockConnectRecompileTicksRemaining);
        CaptureMinerSaveInteger(95, dockConnectRecompileArmed ? 1 : 0);
        CaptureMinerSaveInteger(97, dockRetryTicks);
        CaptureMinerSaveVector(100, connectorOffsetLocal, false);
        CaptureMinerSaveInteger(102, oreTransferStartDelayTicks);
        CaptureMinerSaveInteger(103, minerUnloadSession);
    }
}

void StampMinerSaveGeneration(MyIni ini, string section, long generation)
{
    ini.Set(section, "snapshotGeneration", generation);
}

void InitializeMinerSaveWork()
{
    // Constructor-only warm-up of capture, formatting and serialization. This
    // writes no live store and does not accept a checkpoint or alter dirty flags.
    if (_minerSavePending)
        return;
    bool wasFull = _minerSaveFull;
    _minerSaveFull = true;
    _minerPendingSaveIni.Clear();
    _minerPendingRouteIni.Clear();
    CaptureMinerSaveSnapshot();
    for (int i = 0; i < _minerSaveKeys.Length; i++)
        SetPendingMinerSaveField(i);
    StampMinerSaveGeneration(_minerPendingSaveIni, StateIniSection, _stateSnapshotGeneration);
    _minerPendingSaveIni.ToString();
    _minerPendingRouteIni.ToString();
    _minerPendingSaveIni.Clear();
    _minerSaveFull = wasFull;
}

void RequestMinerSave(bool full)
{
    if (string.IsNullOrWhiteSpace(_internalCore)) full = true;
    // A later full request supersedes an unfinished progress snapshot. An
    // existing full snapshot is completed first; newer flags remain pending.
    if (_minerSavePending)
    {
        if (!full || _minerSaveFull)
            return;
        CancelMinerSaveWork();
    }
    if (!full && !progressStateDirty)
        return;
    CaptureMinerSaveSnapshot(full);
    _minerPendingSaveIni.Clear();
    _minerPendingRouteIni.Clear();
    _minerSaveSerialized = null;
    _minerRouteSaveSerialized = null;
    _minerSaveRenderStage = 0;
    _minerSavePublishStage = 0;
    _minerSaveFull = full;
    _minerSaveIndex = 0;
    _minerSaveCaptureTick = runtime;
    _minerSaveLastWorkTick = runtime;
    _minerSaveGeneration = _stateSnapshotGeneration + (full ? 1 : 0);
    _minerSavePending = true;
    if (full)
    {
        fullStateDirty = false;
        lastProgressStateFingerprint = long.MinValue;
    }
    progressStateDirty = false;
}

void CancelMinerSaveWork()
{
    if (!_minerSavePending)
        return;
    if (_minerSaveFull)
        fullStateDirty = true;
    progressStateDirty = true;
    _minerSavePending = false;
}

void SetPendingMinerSaveField(int index)
{
    MinerSaveValue value = _minerSaveValues[index];
    int planSlot = _minerSavePlanVectorSlots[index];
    if (!_minerSaveFull && planSlot >= 0 && _fullProgressVectorValid[planSlot]
        && value.Vector == _fullProgressVectors[planSlot])
        return;
    string section = _minerSaveFull ? StateIniSection : ProgressIniSection;
    string key = _minerSaveKeys[index];
    MinerSaveValue previous = _minerFormattedSaveValues[index];
    bool unchanged = _minerFormattedSaveText[index] != null && previous.Kind == value.Kind
        && (value.Kind == 0 || value.Kind == 5 ? previous.Integer == value.Integer
            : value.Kind == 1 ? previous.Number == value.Number
            : value.Kind == 2 || value.Kind == 3 ? previous.Vector == value.Vector
            : string.Equals(previous.Text, value.Text, StringComparison.Ordinal));
    if (!unchanged)
    {
        _minerFormattedSaveText[index] = value.Kind == 0 ? value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.Kind == 1 ? Number2(value.Number)
            : value.Kind == 2 ? V3ToString(value.Vector)
            : value.Kind == 3 ? DirectionToString(value.Vector)
            : value.Kind == 4 ? value.Text ?? "" : value.Integer != 0 ? "true" : "false";
        _minerFormattedSaveValues[index] = value;
    }
    MyIni destination = _minerSaveFull && IsMinerRouteSaveIndex(index)
        ? _minerPendingRouteIni : _minerPendingSaveIni;
    destination.Set(section, key, _minerFormattedSaveText[index]);
}

void PumpMinerSaveWork()
{
    // Do not serialize in the same invocation that captured the state.
    if (!_minerSavePending || runtime == _minerSaveCaptureTick || runtime == _minerSaveLastWorkTick)
        return;
    int saveCount = _minerSaveFull ? _minerSaveKeys.Length : _minerProgressSaveIndexes.Length;
    if (_minerSaveIndex >= saveCount && (pathLoadJob != null || areaPlanActivationStage != 0 || IsUndockSequenceActive()))
        return;
    if (_minerSaveIndex >= saveCount && !TryClaimMinerHeavyWork())
        return;
    _minerSaveLastWorkTick = runtime;
    int count = saveCount;
    if (_minerSaveIndex < count)
    {
        int end = Math.Min(count, _minerSaveIndex + minerSaveFieldsPerTick);
        while (_minerSaveIndex < end)
        {
            SetPendingMinerSaveField(_minerSaveFull ? _minerSaveIndex : _minerProgressSaveIndexes[_minerSaveIndex]);
            _minerSaveIndex++;
        }
        return;
    }

    // Rendering the final string and publishing the block property are separate
    // invocations. Neither indivisible operation is claimed to have a hard cap.
    if (_minerSaveRenderStage == 0)
    {
        StampMinerSaveGeneration(_minerPendingSaveIni,
            _minerSaveFull ? StateIniSection : ProgressIniSection, _minerSaveGeneration);
        _minerSaveSerialized = _minerPendingSaveIni.ToString();
        _minerSaveRenderStage = _minerSaveFull ? 1 : 2;
        return;
    }
    if (_minerSaveRenderStage == 1)
    {
        StampMinerSaveGeneration(_minerPendingRouteIni, StateIniSection, _minerSaveGeneration);
        _minerRouteSaveSerialized = _minerPendingRouteIni.ToString();
        _minerSaveRenderStage = 2;
        return;
    }

    // Internal publication is the commit. Optional timer mirrors follow on
    // later invocations, so interruption or missing timers cannot lose the save.
    if (_minerSaveFull)
    {
        if (_minerSavePublishStage == 0)
        {
            PublishInternalState(_minerSaveSerialized, _minerRouteSaveSerialized, "");
            _stateSnapshotGeneration = _minerSaveGeneration;
            for (int i = 0; i < _minerPlanVectorSaveIndexes.Length; i++)
            {
                _fullProgressVectors[i] = _minerSaveValues[_minerPlanVectorSaveIndexes[i]].Vector;
                _fullProgressVectorValid[i] = true;
            }
            _hasProgressState = false;
            _progressIni.Clear();
            _minerSavePublishStage = 1;
            if (timerPersistenceEnabled) return;
        }
        if (timerPersistenceEnabled)
        {
            IMyTimerBlock timer = FindStateIniTimer();
            if (timer == null || _cachedStateRouteTimer == null)
            {
                ScriptLog("INI timer mirror unavailable; complete internal checkpoint was saved.");
            }
            else if (_minerSavePublishStage == 1)
            {
                _cachedStateRouteTimer.CustomData = _minerRouteSaveSerialized ?? "";
                _minerSavePublishStage = 2;
                return;
            }
            else timer.CustomData = _minerSaveSerialized;
        }
    }
    else
    {
        if (!PublishInternalProgress(_minerSaveSerialized))
        {
            CancelMinerSaveWork();
            return;
        }
        _hasProgressState = true;
    }
    _minerSavePending = false;
}
// END BOUNDED MINER SAVE

bool ReadStateIni(out MyIni ini)
{
    ini = new MyIni();
    bool internalState = TryReadInternalState();
    if (!internalState && (Storage ?? "").StartsWith(InternalStateHeader, StringComparison.Ordinal))
    {
        ScriptLog("Internal checkpoint is malformed; no mission state was restored.");
        return false;
    }
    IMyTimerBlock timer = internalState ? null : FindStateIniTimer();
    string data = internalState ? _internalCore : timer == null ? "" : timer.CustomData ?? "";
    if (string.IsNullOrWhiteSpace(data)) return false;
    MyIniParseResult result;
    if (!ini.TryParse(data, out result))
    {
        ScriptLog("State INI parse failed: " + result.ToString());
        return false;
    }
    string route = internalState ? _internalRoute : _cachedStateRouteTimer == null ? "" : _cachedStateRouteTimer.CustomData ?? "";
    long generation;
    if (!long.TryParse(ini.Get(StateIniSection, "snapshotGeneration").ToString("0"), out generation)
        || !MergeMinerStatePart(ini, route, generation))
    {
        ScriptLog("Persistence checkpoint rejected: Core/Route generations do not match; no mission was restored.");
        return false;
    }
    _internalCore = data; _internalRoute = route; _internalReports = "";
    return true;
}

bool MergeMinerStatePart(MyIni destination, string data, long generation)
{
    if (string.IsNullOrWhiteSpace(data)) return false;
    MyIni source = new MyIni();
    MyIniParseResult result;
    if (!source.TryParse(data, out result)
        || source.Get(StateIniSection, "snapshotGeneration").ToInt64(-1) != generation) return false;
    for (int i = 0; i < _minerSaveKeys.Length; i++)
    {
        if (!IsMinerRouteSaveIndex(i)) continue;
        MyIniValue value = source.Get(StateIniSection, _minerSaveKeys[i]);
        if (!value.IsEmpty) destination.Set(StateIniSection, _minerSaveKeys[i], value.ToString());
    }
    return true;
}

bool WriteStateIni(MyIni ini)
{
    // Save() publishes only this PB\'s Storage, never other blocks during a world save.
    MyIni core = new MyIni();
    MyIniParseResult parseResult;
    if (!core.TryParse(ini.ToString(), out parseResult))
    {
        ScriptLog("State INI render failed: " + parseResult.ToString());
        return false;
    }
    MyIni route = new MyIni();
    for (int i = 0; i < _minerSaveKeys.Length; i++)
    {
        if (!IsMinerRouteSaveIndex(i)) continue;
        string key = _minerSaveKeys[i];
        MyIniValue value = core.Get(StateIniSection, key);
        if (!value.IsEmpty) route.Set(StateIniSection, key, value.ToString());
        core.Delete(StateIniSection, key);
    }
    route.Set(StateIniSection, "snapshotGeneration",
        ini.Get(StateIniSection, "snapshotGeneration").ToString("0"));

    PublishInternalState(core.ToString(), route.ToString(), "");
    return true;
}

bool ReadProgressState()
{
    _progressIni.Clear();
    lastProgressStateFingerprint = long.MinValue;
    bool internalState = (Storage ?? "").StartsWith(InternalStateHeader, StringComparison.Ordinal);
    if (internalState && !TryReadInternalState()) return false;
    string data = internalState ? _internalProgress : Storage ?? "";
    if (string.IsNullOrWhiteSpace(data))
        return false;
    MyIniParseResult result;
    if (!_progressIni.TryParse(data, out result))
        return false;
    long generation;
    if (!long.TryParse(_progressIni.Get(ProgressIniSection, "snapshotGeneration").ToString("0"), out generation)
        || generation != _stateSnapshotGeneration)
        return false;
    return true;
}

void WriteProgressState(MyIni ini)
{
    string next = ini.ToString();
    if (!PublishInternalProgress(next)) return;
    _hasProgressState = true;
    progressStateDirty = false;
}

string StateString(MyIni ini, string key, string fallback)
{
    if (_hasProgressState)
    {
        string progress;
        if (_progressIni.Get(ProgressIniSection, key).TryGetString(out progress))
            return progress;
    }

    return ini.Get(StateIniSection, key).ToString(fallback);
}

bool StateBool(MyIni ini, string key, bool fallback)
{
    string s = StateString(ini, key, fallback ? "1" : "0");
    return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
}

void LoadLaneIndexList(MyIni ini, string keyPrefix, List<int> lanes)
{
    lanes.Clear();
    int count;
    if (!int.TryParse(StateString(ini, keyPrefix + "Count", "0"), out count) || count <= 0)
        return;
    for (int i = 0; i < count; i++)
    {
        int lane;
        if (int.TryParse(StateString(ini, keyPrefix + i, "-1"), out lane))
            AddLaneIndexUnique(lanes, lane);
    }

    SortLaneIndexes(lanes);
}

void LoadLaneDepthList(MyIni ini, string keyPrefix, List<double> depths)
{
    depths.Clear();
    int count;
    if (!int.TryParse(StateString(ini, keyPrefix + "Count", "0"), out count) || count <= 0)
        return;
    for (int i = 0; i < count; i++)
    {
        double depth;
        if (!TryParseNumber(StateString(ini, keyPrefix + i, "-1"), out depth) || depth < 0)
        {
            depths.Clear();
            return;
        }

        depths.Add(depth);
    }
}

IMyTimerBlock FindStateIniTimer()
{
    if (_cachedStateIniTimer != null && _cachedStateIniTimer.CustomName.Equals(stateIniTimerTag, StringComparison.OrdinalIgnoreCase))
        return _cachedStateIniTimer;
    foreach (var t in GetTimerBlocksCached())
    {
        if (t.CustomName.Equals(stateIniTimerTag, StringComparison.OrdinalIgnoreCase))
        {
            _cachedStateIniTimer = t;
            return _cachedStateIniTimer;
        }
    }

    _cachedStateIniTimer = null;
    return null;
}

void RefreshTimerCache()
{
    _timerBlocks.Clear();
    GridTerminalSystem.GetBlocksOfType(_timerBlocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
}

void CacheNamedTimerReferences()
{
    _cachedPathTimer = null;
    _cachedStateIniTimer = null;
    _cachedStateRouteTimer = null;
    for (int i = 0; i < _timerBlocks.Count; i++)
    {
        IMyTimerBlock timer = _timerBlocks[i];
        if (timer == null || string.IsNullOrWhiteSpace(timer.CustomName))
            continue;
        if (_cachedPathTimer == null && timer.CustomName.IndexOf(pathTimerTag, StringComparison.OrdinalIgnoreCase) >= 0)
            _cachedPathTimer = timer;
        if (_cachedStateIniTimer == null && timer.CustomName.Equals(stateIniTimerTag, StringComparison.OrdinalIgnoreCase))
            _cachedStateIniTimer = timer;
        else if (_cachedStateRouteTimer == null && timer.CustomName.Equals(stateRouteTimerTag, StringComparison.OrdinalIgnoreCase))
            _cachedStateRouteTimer = timer;
    }
}

List<IMyTimerBlock> GetTimerBlocksCached()
{
    return _timerBlocks;
}

void SaveState()
{
    CancelMinerSaveWork();
    MyIni ini = _stateIni;
    ini.Clear();
    ini.Set(StateIniSection, "autopilotEnabled", autopilotEnabled ? "1" : "0");
    ini.Set(StateIniSection, "scriptPaused", scriptPaused ? "1" : "0");
    ini.Set(StateIniSection, "pauseResumeAutopilot", pauseResumeAutopilot ? "1" : "0");
    ini.Set(StateIniSection, "hasTarget", hasTarget ? "1" : "0");
    ini.Set(StateIniSection, "targetPosition", V3ToString(targetPosition));
    ini.Set(StateIniSection, "desiredForward", DirectionToString(desiredForward));
    ini.Set(StateIniSection, "desiredUp", DirectionToString(desiredUp));
    ini.Set(StateIniSection, "miningEnabled", miningEnabled ? "1" : "0");
    ini.Set(StateIniSection, "miningState", (int)miningState);
    ini.Set(StateIniSection, "laneAnchor", V3ToString(laneAnchor));
    ini.Set(StateIniSection, "drillAxis", DirectionToString(drillAxis));
    ini.Set(StateIniSection, "laneDepth", Number2(laneDepth));
    ini.Set(StateIniSection, "SPEEDF", Number2(SPEEDF));
    ini.Set(StateIniSection, "SPEEDB", Number2(SPEEDB));
    ini.Set(StateIniSection, "MINEH", Number2(MINEH));
    ini.Set(StateIniSection, "MINEW", Number2(MINEW));
    ini.Set(StateIniSection, "minerRouteStartExtra", Number2(minerRouteStartExtra));
    ini.Set(StateIniSection, "mergeAssistedDocking", mergeAssistedDocking ? "1" : "0");
    ini.Set(StateIniSection, "TOFFR", Number2(TOFFR));
    ini.Set(StateIniSection, "TOFFU", Number2(TOFFU));
    ini.Set(StateIniSection, "TOFFF", Number2(TOFFF));
    ini.Set(StateIniSection, "activeMaxSpeed", Number2(activeMaxSpeed));
    ini.Set(StateIniSection, "pointToPointAccelerationLimit", Number2(pointToPointAccelerationLimit));
    ini.Set(StateIniSection, "pointToPointThrustUsePercent", Number2(pointToPointThrustUsePercent));
    ini.Set(StateIniSection, "activeMissionId", activeMissionId);
    ini.Set(StateIniSection, "areaMiningMode", areaMiningMode ? "1" : "0");
    ini.Set(StateIniSection, "areaLaneIndex", areaLaneIndex);
    ini.Set(StateIniSection, "areaPlanValid", areaPlanValid ? "1" : "0");
    ini.Set(StateIniSection, "areaPlanCornerA", V3ToString(areaPlanCornerA));
    ini.Set(StateIniSection, "areaPlanCornerB", V3ToString(areaPlanCornerB));
    ini.Set(StateIniSection, "areaPlanUp", DirectionToString(areaPlanUp));
    ini.Set(StateIniSection, "areaPlanForward", DirectionToString(areaPlanForward));
    ini.Set(StateIniSection, "returnToMinePoseAfterArea", returnToMinePoseAfterArea ? "1" : "0");
    ini.Set(StateIniSection, "finishAreaAfterBackout", finishAreaAfterBackout ? "1" : "0");
    ini.Set(StateIniSection, "areaMineEntryPos", V3ToString(areaMineEntryPos));
    ini.Set(StateIniSection, "areaMineEntryForward", DirectionToString(areaMineEntryForward));
    ini.Set(StateIniSection, "areaMineEntryUp", DirectionToString(areaMineEntryUp));
    ini.Set(StateIniSection, "activeMineAreaRecordIndex", activeMineAreaRecordIndex);
    ini.Set(StateIniSection, "retryLane", retryCurrentLane ? "1" : "0");
    ini.Set(StateIniSection, "stuckBackout", stuckBackout ? "1" : "0");
    ini.Set(StateIniSection, "stuckRetryCount", stuckRetryCount);
    ini.Set(StateIniSection, "areaLaneCount", areaLaneCount);
    ini.Set(StateIniSection, "pathMode", pathMode ? "1" : "0");
    ini.Set(StateIniSection, "pathPaused", pathPaused ? "1" : "0");
    ini.Set(StateIniSection, "pathPausedNavResume", pathPausedNavResume ? "1" : "0");
    ini.Set(StateIniSection, "pathDrillApproachActive", pathDrillApproachActive ? "1" : "0");
    ini.Set(StateIniSection, "pathDockPending", pathDockPending ? "1" : "0");
    ini.Set(StateIniSection, "pathIndex", pathIndex);
    ini.Set(StateIniSection, "pathCompleteBroadcastTicks", pathCompleteBroadcastTicks);
    ini.Set(StateIniSection, "routeProgressMeters", Number2(routeProgressMeters));
    ini.Set(StateIniSection, "routeProgressTotalMeters", Number2(routeProgressTotalMeters));
    ini.Set(StateIniSection, "routeProgressPreviousPosition", V3ToString(routeProgressPreviousPosition));
    ini.Set(StateIniSection, "routeProgressPreviousValid", routeProgressPreviousValid ? "1" : "0");
    ini.Set(StateIniSection, "routeProgressTracking", routeProgressTracking ? "1" : "0");
    ini.Set(StateIniSection, "pathAnchorValid", pathAnchorValid ? "1" : "0");
    ini.Set(StateIniSection, "pathAnchorPos", V3ToString(pathAnchorPos));
    ini.Set(StateIniSection, "pathAnchorForward", DirectionToString(pathAnchorForward));
    ini.Set(StateIniSection, "pathAnchorUp", DirectionToString(pathAnchorUp));
    ini.Set(StateIniSection, "recallActive", recallActive ? "1" : "0");
    ini.Set(StateIniSection, "recallWaitingForAreaExit", recallWaitingForAreaExit ? "1" : "0");
    ini.Set(StateIniSection, "recallWaitingForDock", recallWaitingForDock ? "1" : "0");
    ini.Set(StateIniSection, "recallReverseMode", recallReverseMode ? "1" : "0");
    ini.Set(StateIniSection, "recallIndex", recallIndex);
    ini.Set(StateIniSection, "recallLastDone", recallLastDone);
    ini.Set(StateIniSection, "recallLastTotal", recallLastTotal);
    ini.Set(StateIniSection, "autoServiceReturnEnabled", autoServiceReturnEnabled ? "1" : "0");
    ini.Set(StateIniSection, "autoServiceCargoTriggerPercent", Number2(autoServiceCargoTriggerPercent));
    ini.Set(StateIniSection, "autoServiceHydrogenTriggerPercent", Number2(autoServiceHydrogenTriggerPercent));
    ini.Set(StateIniSection, "autoServiceState", (int)autoServiceState);
    ini.Set(StateIniSection, "autoServiceReason", autoServiceReason);
    ini.Set(StateIniSection, "autoServiceResumeMineArea", autoServiceResumeMineArea ? "1" : "0");
    ini.Set(StateIniSection, "autoServiceResumeMineAreaRecordIndex", autoServiceResumeMineAreaRecordIndex);
    ini.Set(StateIniSection, "autoServiceResumeAreaLaneIndex", autoServiceResumeAreaLaneIndex);
    ini.Set(StateIniSection, "autoServiceResumeMineEntryPos", V3ToString(autoServiceResumeMineEntryPos));
    ini.Set(StateIniSection, "autoServiceResumeMineEntryForward", DirectionToString(autoServiceResumeMineEntryForward));
    ini.Set(StateIniSection, "autoServiceResumeMineEntryUp", DirectionToString(autoServiceResumeMineEntryUp));
    ini.Set(StateIniSection, "autoServiceCheckTicks", autoServiceCheckTicks);
    ini.Set(StateIniSection, "autoServiceActionRetryTicks", autoServiceActionRetryTicks);
    ini.Set(StateIniSection, "autoServiceReturnRouteIndex", autoServiceReturnRouteIndex);
    ini.Set(StateIniSection, "hangarState", (int)hangarState);
    ini.Set(StateIniSection, "hangarTargetPosition", V3ToString(hangarTargetPosition));
    ini.Set(StateIniSection, "hangarTargetForward", DirectionToString(hangarTargetForward));
    ini.Set(StateIniSection, "hangarTargetUp", DirectionToString(hangarTargetUp));
    ini.Set(StateIniSection, "hangarOperationId", hangarOperationId);
    ini.Set(StateIniSection, "hangarPhaseElapsedTicks", hangarPhaseElapsedTicks);
    ini.Set(StateIniSection, "hangarLaunchUndocked", hangarLaunchUndocked ? "1" : "0");
    ini.Set(StateIniSection, "stoneEjectionHoldActive", stoneEjectionHoldActive ? "1" : "0");
    ini.Set(StateIniSection, "stoneEjectionResumeAutopilot", stoneEjectionResumeAutopilot ? "1" : "0");
    ini.Set(StateIniSection, "stoneEjectionHoldTicks", stoneEjectionHoldTicks);
    ini.Set(StateIniSection, "standardMineDepth", Number2(standardMineDepth));
    ini.Set(StateIniSection, "dockingState", (int)dockingState);
    ini.Set(StateIniSection, "undockRecompileTicksRemaining", undockRecompileTicksRemaining);
    ini.Set(StateIniSection, "dockMergeReadyTicks", dockMergeReadyTicks);
    ini.Set(StateIniSection, "dockConnectRetryTicks", dockConnectRetryTicks);
    ini.Set(StateIniSection, "dockConnectRecompileTicksRemaining", dockConnectRecompileTicksRemaining);
    ini.Set(StateIniSection, "dockConnectRecompileArmed", dockConnectRecompileArmed ? "1" : "0");
    ini.Set(StateIniSection, "dockRetryPending", dockRetryPending ? "1" : "0");
    ini.Set(StateIniSection, "dockRetryTicks", dockRetryTicks);
    ini.Set(StateIniSection, "dockingPhaseElapsedTicks", dockingPhaseElapsedTicks);
    ini.Set(StateIniSection, "dockingAttemptFailures", dockingAttemptFailures);
    ini.Set(StateIniSection, "connectorOffsetLocal", V3ToString(connectorOffsetLocal));
    ini.Set(StateIniSection, "oreTransferActive", oreTransferActive ? "1" : "0");
    ini.Set(StateIniSection, "oreTransferStartDelayTicks", oreTransferStartDelayTicks);
    ini.Set(StateIniSection, "minerUnloadSession", minerUnloadSession);
    ini.Set(StateIniSection, "oreTransferSucceeded", oreTransferSucceeded);
    ini.Set(StateIniSection, "minerDockConnectorId", minerDockConnectorId);
    ini.Set(StateIniSection, "minerDockSession", minerDockSession);
    ini.Set(StateIniSection, "pendingUndock", pendingUndock);
    ini.Set(StateIniSection, "minerPowerGoal", minerPowerGoal);
    StampMinerSaveGeneration(ini, StateIniSection, _stateSnapshotGeneration + 1);
    if (!WriteStateIni(ini))
    {
        fullStateDirty = true;
        progressStateDirty = true;
        return;
    }
    _stateSnapshotGeneration++;
    lastProgressStateFingerprint = long.MinValue;
    CaptureSavedProgressVectorBaseline();
    fullStateDirty = false;
    progressStateDirty = false;
    _hasProgressState = false;
    _progressIni.Clear();
}

// Cheap save: writes only volatile progress into PB Storage. This avoids
// serializing the full timer CustomData INI during regular Update ticks.
void SaveStateProgressOnly()
{
    if (string.IsNullOrWhiteSpace(_internalCore)) { SaveState(); return; }
    if (!progressStateDirty)
        return;
    CancelMinerSaveWork();
    MyIni ini = _progressIni;
    ini.Clear();
    ini.Set(ProgressIniSection, "autopilotEnabled", autopilotEnabled ? "1" : "0");
    ini.Set(ProgressIniSection, "hasTarget", hasTarget ? "1" : "0");
    ini.Set(ProgressIniSection, "targetPosition", V3ToString(targetPosition));
    ini.Set(ProgressIniSection, "desiredForward", DirectionToString(desiredForward));
    ini.Set(ProgressIniSection, "desiredUp", DirectionToString(desiredUp));
    ini.Set(ProgressIniSection, "miningEnabled", miningEnabled ? "1" : "0");
    ini.Set(ProgressIniSection, "miningState", (int)miningState);
    ini.Set(ProgressIniSection, "laneAnchor", V3ToString(laneAnchor));
    ini.Set(ProgressIniSection, "drillAxis", DirectionToString(drillAxis));
    ini.Set(ProgressIniSection, "laneDepth", Number2(laneDepth));
    ini.Set(ProgressIniSection, "activeMaxSpeed", Number2(activeMaxSpeed));
    ini.Set(ProgressIniSection, "activeMissionId", activeMissionId);
    ini.Set(ProgressIniSection, "areaMiningMode", areaMiningMode ? "1" : "0");
    ini.Set(ProgressIniSection, "areaLaneIndex", areaLaneIndex);
    ini.Set(ProgressIniSection, "areaLaneCount", areaLaneCount);
    ini.Set(ProgressIniSection, "areaPlanValid", areaPlanValid ? "1" : "0");
    SetProgressPlanVector(ini, 0, areaPlanCornerA, false);
    SetProgressPlanVector(ini, 1, areaPlanCornerB, false);
    SetProgressPlanVector(ini, 2, areaPlanUp, true);
    SetProgressPlanVector(ini, 3, areaPlanForward, true);
    ini.Set(ProgressIniSection, "returnToMinePoseAfterArea", returnToMinePoseAfterArea ? "1" : "0");
    ini.Set(ProgressIniSection, "finishAreaAfterBackout", finishAreaAfterBackout ? "1" : "0");
    SetProgressPlanVector(ini, 4, areaMineEntryPos, false);
    SetProgressPlanVector(ini, 5, areaMineEntryForward, true);
    SetProgressPlanVector(ini, 6, areaMineEntryUp, true);
    ini.Set(ProgressIniSection, "activeMineAreaRecordIndex", activeMineAreaRecordIndex);
    ini.Set(ProgressIniSection, "retryLane", retryCurrentLane ? "1" : "0");
    ini.Set(ProgressIniSection, "stuckBackout", stuckBackout ? "1" : "0");
    ini.Set(ProgressIniSection, "stuckRetryCount", stuckRetryCount);
    ini.Set(ProgressIniSection, "pathMode", pathMode ? "1" : "0");
    ini.Set(ProgressIniSection, "pathPaused", pathPaused ? "1" : "0");
    ini.Set(ProgressIniSection, "pathDrillApproachActive", pathDrillApproachActive ? "1" : "0");
    ini.Set(ProgressIniSection, "pathDockPending", pathDockPending ? "1" : "0");
    ini.Set(ProgressIniSection, "pathIndex", pathIndex);
    ini.Set(ProgressIniSection, "pathAnchorValid", pathAnchorValid ? "1" : "0");
    ini.Set(ProgressIniSection, "recallActive", recallActive ? "1" : "0");
    ini.Set(ProgressIniSection, "recallWaitingForAreaExit", recallWaitingForAreaExit ? "1" : "0");
    ini.Set(ProgressIniSection, "recallWaitingForDock", recallWaitingForDock ? "1" : "0");
    ini.Set(ProgressIniSection, "recallReverseMode", recallReverseMode ? "1" : "0");
    ini.Set(ProgressIniSection, "recallIndex", recallIndex);
    ini.Set(ProgressIniSection, "autoServiceState", (int)autoServiceState);
    ini.Set(ProgressIniSection, "autoServiceResumeMineArea", autoServiceResumeMineArea ? "1" : "0");
    ini.Set(ProgressIniSection, "autoServiceResumeMineAreaRecordIndex", autoServiceResumeMineAreaRecordIndex);
    ini.Set(ProgressIniSection, "autoServiceResumeAreaLaneIndex", autoServiceResumeAreaLaneIndex);
    SetProgressPlanVector(ini, 7, autoServiceResumeMineEntryPos, false);
    SetProgressPlanVector(ini, 8, autoServiceResumeMineEntryForward, true);
    SetProgressPlanVector(ini, 9, autoServiceResumeMineEntryUp, true);
    ini.Set(ProgressIniSection, "autoServiceReturnRouteIndex", autoServiceReturnRouteIndex);
    ini.Set(ProgressIniSection, "hangarState", (int)hangarState);
    ini.Set(ProgressIniSection, "hangarLaunchUndocked", hangarLaunchUndocked ? "1" : "0");
    ini.Set(ProgressIniSection, "hangarOperationId", hangarOperationId);
    ini.Set(ProgressIniSection, "hangarPhaseElapsedTicks", hangarPhaseElapsedTicks);
    ini.Set(ProgressIniSection, "stoneEjectionHoldActive", stoneEjectionHoldActive ? "1" : "0");
    ini.Set(ProgressIniSection, "stoneEjectionResumeAutopilot", stoneEjectionResumeAutopilot ? "1" : "0");
    ini.Set(ProgressIniSection, "dockingState", (int)dockingState);
    ini.Set(ProgressIniSection, "dockRetryPending", dockRetryPending ? "1" : "0");
    ini.Set(ProgressIniSection, "dockingPhaseElapsedTicks", dockingPhaseElapsedTicks);
    ini.Set(ProgressIniSection, "dockingAttemptFailures", dockingAttemptFailures);
    ini.Set(ProgressIniSection, "oreTransferActive", oreTransferActive ? "1" : "0");
    ini.Set(ProgressIniSection, "oreTransferSucceeded", oreTransferSucceeded);
    ini.Set(ProgressIniSection, "minerDockConnectorId", minerDockConnectorId);
    ini.Set(ProgressIniSection, "minerDockSession", minerDockSession);
    ini.Set(ProgressIniSection, "pendingUndock", pendingUndock);
    ini.Set(ProgressIniSection, "minerPowerGoal", minerPowerGoal);
    StampMinerSaveGeneration(ini, ProgressIniSection, _stateSnapshotGeneration);
    WriteProgressState(ini);
}

readonly string[] _fullProgressVectorKeys = { "areaPlanCornerA", "areaPlanCornerB", "areaPlanUp", "areaPlanForward", "areaMineEntryPos", "areaMineEntryForward", "areaMineEntryUp", "autoServiceResumeMineEntryPos", "autoServiceResumeMineEntryForward", "autoServiceResumeMineEntryUp" };
readonly Vector3D[] _fullProgressVectors = new Vector3D[10];
readonly bool[] _fullProgressVectorValid = new bool[10];

void LoadFullProgressVectorBaseline(MyIni ini)
{
    // Read the full snapshot itself, never the newer progress overlay.
    long.TryParse(ini.Get(StateIniSection, "snapshotGeneration").ToString("0"), out _stateSnapshotGeneration);
    for (int i = 0; i < _fullProgressVectorKeys.Length; i++)
        _fullProgressVectorValid[i] = TryParseV3(ini.Get(StateIniSection, _fullProgressVectorKeys[i]).ToString(""), out _fullProgressVectors[i]);
}

void CaptureSavedProgressVectorBaseline()
{
    _fullProgressVectors[0] = areaPlanCornerA;
    _fullProgressVectors[1] = areaPlanCornerB;
    _fullProgressVectors[2] = areaPlanUp;
    _fullProgressVectors[3] = areaPlanForward;
    _fullProgressVectors[4] = areaMineEntryPos;
    _fullProgressVectors[5] = areaMineEntryForward;
    _fullProgressVectors[6] = areaMineEntryUp;
    _fullProgressVectors[7] = autoServiceResumeMineEntryPos;
    _fullProgressVectors[8] = autoServiceResumeMineEntryForward;
    _fullProgressVectors[9] = autoServiceResumeMineEntryUp;
    for (int i = 0; i < _fullProgressVectorValid.Length; i++) _fullProgressVectorValid[i] = true;
}

void SetProgressPlanVector(MyIni ini, int index, Vector3D value, bool direction)
{
    // An override must survive every progress save until a FULL save includes it.
    if (!_fullProgressVectorValid[index] || value != _fullProgressVectors[index])
        ini.Set(ProgressIniSection, _fullProgressVectorKeys[index], direction ? DirectionToString(value) : V3ToString(value));
}

void MarkProgressStateDirty()
{
    progressStateDirty = true;
}

long MixProgressHash(long hash, long value)
{
    unchecked
    {
        return (hash ^ value) * 1099511628211L;
    }
}

long QuantizedProgressValue(double value, double step)
{
    return (long)Math.Round(value / Math.Max(0.000001, step));
}

bool HasMinerProgressStateChanged()
{
    long h = 1469598103934665603L;
    h = MixProgressHash(h, autopilotEnabled ? 1 : 0);
    h = MixProgressHash(h, hasTarget ? 1 : 0);
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.X, 0.1));
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.Y, 0.1));
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.Z, 0.1));
    h = MixProgressHash(h, miningEnabled ? 1 : 0);
    h = MixProgressHash(h, (int)miningState);
    h = MixProgressHash(h, areaMiningMode ? 1 : 0);
    h = MixProgressHash(h, areaLaneIndex);
    h = MixProgressHash(h, activeMineAreaRecordIndex);
    h = MixProgressHash(h, pathMode ? 1 : 0);
    h = MixProgressHash(h, pathPaused ? 1 : 0);
    h = MixProgressHash(h, pathDrillApproachActive ? 1 : 0);
    h = MixProgressHash(h, pathDockPending ? 1 : 0);
    h = MixProgressHash(h, pathIndex);
    h = MixProgressHash(h, activeMissionId == null ? 0 : activeMissionId.GetHashCode());
    h = MixProgressHash(h, recallActive ? 1 : 0);
    h = MixProgressHash(h, recallPreparing ? 1 : 0);
    h = MixProgressHash(h, recallWaitingForAreaExit ? 1 : 0);
    h = MixProgressHash(h, recallWaitingForDock ? 1 : 0);
    h = MixProgressHash(h, recallReverseMode ? 1 : 0);
    h = MixProgressHash(h, recallIndex);
    h = MixProgressHash(h, (int)autoServiceState);
    h = MixProgressHash(h, autoServiceReturnRouteIndex);
    h = MixProgressHash(h, (int)hangarState);
    h = MixProgressHash(h, hangarLaunchUndocked ? 1 : 0);
    h = MixProgressHash(h, hangarOperationId);
    h = MixProgressHash(h, hangarPhaseElapsedTicks / 60);
    h = MixProgressHash(h, stoneEjectionHoldActive ? 1 : 0);
    h = MixProgressHash(h, (int)dockingState);
    h = MixProgressHash(h, dockRetryPending ? 1 : 0);
    h = MixProgressHash(h, pendingUndock ? 1 : 0);
    h = MixProgressHash(h, minerDockConnectorId);
    h = MixProgressHash(h, minerDockSession);
    h = MixProgressHash(h, dockingPhaseElapsedTicks / 60);
    h = MixProgressHash(h, dockingAttemptFailures);
    h = MixProgressHash(h, oreTransferActive ? 1 : 0);
    if (h == lastProgressStateFingerprint)
        return false;
    lastProgressStateFingerprint = h;
    return true;
}

bool HasVolatileProgressState()
{
    return autopilotEnabled || hasTarget || miningEnabled || areaMiningMode || pathMode || pathPaused || routeProgressTracking || recallActive || autoServiceState != AutoServiceState.None || hangarState != HangarState.None || stoneEjectionHoldActive || dockingState != DockingState.None || oreTransferActive || oreTransferStartDelayTicks > 0 || pathCompleteBroadcastTicks > 0 || dockRetryPending || pendingUndock;
}

void LoadState()
{
    Array.Clear(_fullProgressVectorValid, 0, _fullProgressVectorValid.Length);
    MyIni ini;
    if (!ReadStateIni(out ini))
        return;
    LoadFullProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    int n;
    double d;
    Vector3D v;
    bool legacyHangarRouteDeparture = false;
    autopilotEnabled = StateBool(ini, "autopilotEnabled", autopilotEnabled);
    scriptPaused = StateBool(ini, "scriptPaused", scriptPaused);
    pauseResumeAutopilot = StateBool(ini, "pauseResumeAutopilot", pauseResumeAutopilot);
    hasTarget = StateBool(ini, "hasTarget", hasTarget);
    if (TryParseV3(StateString(ini, "targetPosition", V3ToString(targetPosition)), out v))
        targetPosition = v;
    if (TryParseV3(StateString(ini, "desiredForward", V3ToString(desiredForward)), out v))
        desiredForward = v;
    if (TryParseV3(StateString(ini, "desiredUp", V3ToString(desiredUp)), out v))
        desiredUp = v;
    miningEnabled = StateBool(ini, "miningEnabled", miningEnabled);
    if (int.TryParse(StateString(ini, "miningState", ((int)miningState).ToString()), out n))
        miningState = (MiningState)n;
    if (TryParseV3(StateString(ini, "laneAnchor", V3ToString(laneAnchor)), out v))
        laneAnchor = v;
    if (TryParseV3(StateString(ini, "drillAxis", V3ToString(drillAxis)), out v))
        drillAxis = v;
    if (TryParseNumber(StateString(ini, "laneDepth", NumberR(laneDepth)), out d))
        laneDepth = d;
    if (TryParseNumber(StateString(ini, "SPEEDF", NumberR(SPEEDF)), out d))
        SPEEDF = Math.Max(0.1, d);
    if (TryParseNumber(StateString(ini, "SPEEDB", NumberR(SPEEDB)), out d))
        SPEEDB = Math.Max(0.1, d);
    if (TryParseNumber(StateString(ini, "MINEH", NumberR(MINEH)), out d))
        MINEH = Math.Max(0.1, d);
    if (TryParseNumber(StateString(ini, "MINEW", NumberR(MINEW)), out d))
        MINEW = Math.Max(0.1, d);
    if (TryParseNumber(StateString(ini, "minerRouteStartExtra", NumberR(minerRouteStartExtra)), out d))
        minerRouteStartExtra = MathHelper.Clamp(d, 0, 500);
    mergeAssistedDocking = StateBool(ini, "mergeAssistedDocking", mergeAssistedDocking);
    if (TryParseNumber(StateString(ini, "TOFFR", NumberR(TOFFR)), out d))
        TOFFR = d;
    if (TryParseNumber(StateString(ini, "TOFFU", NumberR(TOFFU)), out d))
        TOFFU = d;
    if (TryParseNumber(StateString(ini, "TOFFF", NumberR(TOFFF)), out d))
        TOFFF = d;
    if (TryParseNumber(StateString(ini, "activeMaxSpeed", NumberR(activeMaxSpeed)), out d))
        activeMaxSpeed = d;
    if (TryParseNumber(StateString(ini, "pointToPointAccelerationLimit", NumberR(pointToPointAccelerationLimit)), out d))
        pointToPointAccelerationLimit = Math.Max(0.1, d);
    if (TryParseNumber(StateString(ini, "pointToPointThrustUsePercent", NumberR(pointToPointThrustUsePercent)), out d))
        pointToPointThrustUsePercent = MathHelper.Clamp(d, 1, 100);
    activeMissionId = StateString(ini, "activeMissionId", activeMissionId);
    oreTransferSucceeded = StateBool(ini, "oreTransferSucceeded", false);
    areaMiningMode = StateBool(ini, "areaMiningMode", areaMiningMode);
    if (int.TryParse(StateString(ini, "areaLaneIndex", areaLaneIndex.ToString()), out n))
        areaLaneIndex = n;
    areaPlanValid = StateBool(ini, "areaPlanValid", areaPlanValid);
    if (TryParseV3(StateString(ini, "areaPlanCornerA", V3ToString(areaPlanCornerA)), out v))
        areaPlanCornerA = v;
    if (TryParseV3(StateString(ini, "areaPlanCornerB", V3ToString(areaPlanCornerB)), out v))
        areaPlanCornerB = v;
    if (TryParseV3(StateString(ini, "areaPlanUp", V3ToString(areaPlanUp)), out v))
        areaPlanUp = v;
    if (TryParseV3(StateString(ini, "areaPlanForward", V3ToString(areaPlanForward)), out v))
        areaPlanForward = v;
    returnToMinePoseAfterArea = StateBool(ini, "returnToMinePoseAfterArea", returnToMinePoseAfterArea);
    finishAreaAfterBackout = StateBool(ini, "finishAreaAfterBackout", finishAreaAfterBackout);
    if (TryParseV3(StateString(ini, "areaMineEntryPos", V3ToString(areaMineEntryPos)), out v))
        areaMineEntryPos = v;
    if (TryParseV3(StateString(ini, "areaMineEntryForward", V3ToString(areaMineEntryForward)), out v))
        areaMineEntryForward = v;
    if (TryParseV3(StateString(ini, "areaMineEntryUp", V3ToString(areaMineEntryUp)), out v))
        areaMineEntryUp = v;
    if (int.TryParse(StateString(ini, "activeMineAreaRecordIndex", activeMineAreaRecordIndex.ToString()), out n))
        activeMineAreaRecordIndex = n;
    retryCurrentLane = StateBool(ini, "retryLane", retryCurrentLane);
    stuckBackout = StateBool(ini, "stuckBackout", stuckBackout);
    if (int.TryParse(StateString(ini, "stuckRetryCount", stuckRetryCount.ToString()), out n))
        stuckRetryCount = n;
    if (int.TryParse(StateString(ini, "areaLaneCount", "0"), out n))
        areaLaneCount = Math.Max(0, n);
    pathMode = StateBool(ini, "pathMode", pathMode);
    pathPaused = StateBool(ini, "pathPaused", pathPaused);
    pathPausedNavResume = StateBool(ini, "pathPausedNavResume", pathPausedNavResume);
    pathDrillApproachActive = StateBool(ini, "pathDrillApproachActive", pathDrillApproachActive);
    pathDockPending = StateBool(ini, "pathDockPending", pathDockPending);
    if (int.TryParse(StateString(ini, "pathIndex", pathIndex.ToString()), out n))
        pathIndex = n;
    if (int.TryParse(StateString(ini, "pathCompleteBroadcastTicks", pathCompleteBroadcastTicks.ToString()), out n))
        pathCompleteBroadcastTicks = n;
    if (TryParseNumber(StateString(ini, "routeProgressMeters", NumberR(routeProgressMeters)), out d))
        routeProgressMeters = Math.Max(0, d);
    if (TryParseNumber(StateString(ini, "routeProgressTotalMeters", NumberR(routeProgressTotalMeters)), out d))
        routeProgressTotalMeters = Math.Max(0, d);
    if (TryParseV3(StateString(ini, "routeProgressPreviousPosition", V3ToString(routeProgressPreviousPosition)), out v))
        routeProgressPreviousPosition = v;
    routeProgressPreviousValid = StateBool(ini, "routeProgressPreviousValid", routeProgressPreviousValid);
    routeProgressTracking = StateBool(ini, "routeProgressTracking", routeProgressTracking);
    pathAnchorValid = StateBool(ini, "pathAnchorValid", pathAnchorValid);
    if (TryParseV3(StateString(ini, "pathAnchorPos", V3ToString(pathAnchorPos)), out v))
        pathAnchorPos = v;
    if (TryParseV3(StateString(ini, "pathAnchorForward", V3ToString(pathAnchorForward)), out v))
        pathAnchorForward = v;
    if (TryParseV3(StateString(ini, "pathAnchorUp", V3ToString(pathAnchorUp)), out v))
        pathAnchorUp = v;
    recallActive = StateBool(ini, "recallActive", recallActive);
    recallWaitingForAreaExit = StateBool(ini, "recallWaitingForAreaExit", recallWaitingForAreaExit);
    recallWaitingForDock = StateBool(ini, "recallWaitingForDock", recallWaitingForDock);
    recallReverseMode = StateBool(ini, "recallReverseMode", recallReverseMode);
    if (int.TryParse(StateString(ini, "recallIndex", recallIndex.ToString()), out n))
        recallIndex = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "recallLastDone", recallLastDone.ToString()), out n))
        recallLastDone = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "recallLastTotal", recallLastTotal.ToString()), out n))
        recallLastTotal = Math.Max(0, n);
    autoServiceReturnEnabled = StateBool(ini, "autoServiceReturnEnabled", autoServiceReturnEnabled);
    if (TryParseNumber(StateString(ini, "autoServiceCargoTriggerPercent", NumberR(autoServiceCargoTriggerPercent)), out d))
        autoServiceCargoTriggerPercent = MathHelper.Clamp(d, 0, 100);
    if (TryParseNumber(StateString(ini, "autoServiceHydrogenTriggerPercent", NumberR(autoServiceHydrogenTriggerPercent)), out d))
        autoServiceHydrogenTriggerPercent = MathHelper.Clamp(d, 0, 100);
    if (int.TryParse(StateString(ini, "autoServiceState", ((int)autoServiceState).ToString()), out n))
    {
        if (n < 0 || n > (int)AutoServiceState.ReturningToWork)
            n = 0;
        autoServiceState = (AutoServiceState)n;
    }

    autoServiceReason = StateString(ini, "autoServiceReason", autoServiceReason);
    autoServiceResumeMineArea = StateBool(ini, "autoServiceResumeMineArea", autoServiceResumeMineArea);
    if (int.TryParse(StateString(ini, "autoServiceResumeMineAreaRecordIndex", autoServiceResumeMineAreaRecordIndex.ToString()), out n))
        autoServiceResumeMineAreaRecordIndex = n;
    if (int.TryParse(StateString(ini, "autoServiceResumeAreaLaneIndex", autoServiceResumeAreaLaneIndex.ToString()), out n))
        autoServiceResumeAreaLaneIndex = Math.Max(0, n);
    if (TryParseV3(StateString(ini, "autoServiceResumeMineEntryPos", V3ToString(autoServiceResumeMineEntryPos)), out v))
        autoServiceResumeMineEntryPos = v;
    if (TryParseV3(StateString(ini, "autoServiceResumeMineEntryForward", V3ToString(autoServiceResumeMineEntryForward)), out v))
        autoServiceResumeMineEntryForward = v;
    if (TryParseV3(StateString(ini, "autoServiceResumeMineEntryUp", V3ToString(autoServiceResumeMineEntryUp)), out v))
        autoServiceResumeMineEntryUp = v;
    if (int.TryParse(StateString(ini, "autoServiceCheckTicks", autoServiceCheckTicks.ToString()), out n))
        autoServiceCheckTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "autoServiceActionRetryTicks", autoServiceActionRetryTicks.ToString()), out n))
        autoServiceActionRetryTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "autoServiceReturnRouteIndex", "0"), out n))
        autoServiceReturnRouteIndex = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "hangarState", ((int)hangarState).ToString()), out n))
    {
        // One-time migration for v0.5.80-v0.5.81 clearance states that no
        // longer exist in the live state machine.
        if (n == 6 || n == 11)
            hangarState = HangarState.PostDeployDocking;
        else if (n == 10)
            hangarState = HangarState.MovingToHangar;
        else if (n == 12)
        {
            hangarState = HangarState.MissionReady;
            legacyHangarRouteDeparture = true;
        }
        else if (n == 0 || n == 1 || n == 2 || n == 3 || n == 4 || n == 5 || n == 7 || n == 8 || n == 9)
            hangarState = (HangarState)n;
        else
            hangarState = HangarState.None;
    }

    if (TryParseV3(StateString(ini, "hangarTargetPosition", V3ToString(hangarTargetPosition)), out v))
        hangarTargetPosition = v;
    if (TryParseV3(StateString(ini, "hangarTargetForward", V3ToString(hangarTargetForward)), out v))
        hangarTargetForward = v;
    if (TryParseV3(StateString(ini, "hangarTargetUp", V3ToString(hangarTargetUp)), out v))
        hangarTargetUp = v;
    if (int.TryParse(StateString(ini, "hangarOperationId", "0"), out n))
        hangarOperationId = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "hangarPhaseElapsedTicks", "0"), out n))
        hangarPhaseElapsedTicks = Math.Max(0, n);
    hangarLaunchUndocked = legacyHangarRouteDeparture || StateBool(ini, "hangarLaunchUndocked", hangarLaunchUndocked);
    stoneEjectionHoldActive = StateBool(ini, "stoneEjectionHoldActive", stoneEjectionHoldActive);
    stoneEjectionResumeAutopilot = StateBool(ini, "stoneEjectionResumeAutopilot", stoneEjectionResumeAutopilot);
    if (int.TryParse(StateString(ini, "stoneEjectionHoldTicks", stoneEjectionHoldTicks.ToString()), out n))
        stoneEjectionHoldTicks = Math.Max(0, n);
    if (TryParseNumber(StateString(ini, "standardMineDepth", StateString(ini, "pathRecordMineDepth", NumberR(standardMineDepth))), out d))
        standardMineDepth = d;
    if (int.TryParse(StateString(ini, "dockingState", ((int)dockingState).ToString()), out n))
        dockingState = (DockingState)n;
    if (int.TryParse(StateString(ini, "undockRecompileTicksRemaining", undockRecompileTicksRemaining.ToString()), out n))
        undockRecompileTicksRemaining = n;
    if (int.TryParse(StateString(ini, "dockMergeReadyTicks", dockMergeReadyTicks.ToString()), out n))
        dockMergeReadyTicks = n;
    if (int.TryParse(StateString(ini, "dockConnectRetryTicks", dockConnectRetryTicks.ToString()), out n))
        dockConnectRetryTicks = n;
    if (int.TryParse(StateString(ini, "dockConnectRecompileTicksRemaining", dockConnectRecompileTicksRemaining.ToString()), out n))
        dockConnectRecompileTicksRemaining = n;
    dockConnectRecompileArmed = StateBool(ini, "dockConnectRecompileArmed", dockConnectRecompileArmed);
    dockRetryPending = StateBool(ini, "dockRetryPending", dockRetryPending);
    if (int.TryParse(StateString(ini, "dockRetryTicks", dockRetryTicks.ToString()), out n))
        dockRetryTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "dockingPhaseElapsedTicks", "0"), out n))
        dockingPhaseElapsedTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "dockingAttemptFailures", "0"), out n))
        dockingAttemptFailures = Math.Max(0, Math.Min(dockingAttemptFailureLimit, n));
    if (TryParseV3(StateString(ini, "connectorOffsetLocal", V3ToString(connectorOffsetLocal)), out v))
        connectorOffsetLocal = v;
    oreTransferActive = StateBool(ini, "oreTransferActive", oreTransferActive);
    long.TryParse(StateString(ini, "minerUnloadSession", "0"), out minerUnloadSession);
    long.TryParse(StateString(ini, "minerDockConnectorId", "0"), out minerDockConnectorId);
    long.TryParse(StateString(ini, "minerDockSession", "0"), out minerDockSession);
    pendingUndock = StateBool(ini, "pendingUndock", false);
    if (!int.TryParse(StateString(ini, "minerPowerGoal", "0"), out minerPowerGoal)
        || minerPowerGoal < 0 || minerPowerGoal > 2)
        minerPowerGoal = 0;
    if (int.TryParse(StateString(ini, "oreTransferStartDelayTicks", oreTransferStartDelayTicks.ToString()), out n))
        oreTransferStartDelayTicks = Math.Max(0, n);
    carrierConnectorPos = Vector3D.Zero;
    carrierConnectorForward = Vector3D.Zero;
    carrierConnectorUp = Vector3D.Zero;
    carrierPoseAgeSeconds = 9999;
    RecoverPathRecordsAfterLoad();
    RecoverAreaLanesAfterLoad();
    RecoverDockPoseAfterLoad();
    RecoverDerivedRoutesAfterLoad();
    RecoverPathFacingAfterLoad();
    if (autoServiceState == AutoServiceState.WaitingService || autoServiceState == AutoServiceState.Undocking || autoServiceState == AutoServiceState.ReturningToWork)
        pathPaused = true;
    // If drills were actively running when the reload happened, turn them back on
    // and reset the stuck monitor so the stuck timer starts fresh.
    if (miningEnabled && miningState == MiningState.DrillForward)
    {
        UpdateDrillTerrainClearingMode();
        SetDrills(true);
        ResetStuckMonitor();
    }

    fullStateDirty = false;
    progressStateDirty = false;
}

void RecoverPathRecordsAfterLoad()
{
    bool needsPath = pathMode || areaMiningMode || recallActive || autoServiceState != AutoServiceState.None;
    if (!needsPath || pathRecords.Count > 0)
        return;
    List<string> loaded;
    string timerName;
    if (TryLoadPathRecordsFromTimer(out loaded, out timerName))
    {
        pathRecords = loaded;
        if (pathIndex < 0)
            pathIndex = 0;
        if (pathIndex > pathRecords.Count)
            pathIndex = pathRecords.Count;
    }
    else
    {
        autopilotEnabled = false;
        hasTarget = false;
        miningEnabled = false;
        pathMode = false;
        areaMiningMode = false;
        recallActive = false;
        recallWaitingForAreaExit = false;
        recallWaitingForDock = false;
        autoServiceState = AutoServiceState.None;
        pathIndex = 0;
        SetDrills(false);
        ResetMovement();
        ScriptLog("Reload stopped safely: authoritative Miner path timer is missing or invalid.");
    }
}

void RecoverAreaLanesAfterLoad()
{
    if (!areaMiningMode)
        return;
    if (areaPlanValid)
    {
        Vector3D right = Vector3D.Cross(areaPlanForward, areaPlanUp);
        if (right.LengthSquared() > 0.001)
        {
            right = Vector3D.Normalize(right);
            areaPlanRight = right;
            ConfigureAreaLanePlan(areaPlanCornerA, areaPlanCornerB, areaPlanUp, right, MINEH, MINEW);
        }
    }
    else if (areaLaneCount <= 0 && areaLaneAnchors.Count > 0)
        areaLaneCount = areaLaneAnchors.Count;
    if (areaLaneCount <= 0)
    {
        areaMiningMode = false;
        areaLaneIndex = 0;
        return;
    }

    if (areaLaneIndex < 0)
        areaLaneIndex = 0;
    if (areaLaneIndex >= areaLaneCount)
        areaLaneIndex = areaLaneCount - 1;
    if (activeMineAreaRecordIndex >= 0 && pathRecords.Count == 0)
    {
        List<string> loaded;
        string timerName;
        if (TryLoadPathRecordsFromTimer(out loaded, out timerName))
            pathRecords = loaded;
    }

    if (areaSkipLaneIndexes.Count == 0 && areaLaneDepths.Count == 0 && activeMineAreaRecordIndex >= 0)
        LoadActiveMineAreaSkipMetadata();
    ApplyCurrentAreaLaneDepth();
    laneAnchor = areaPlanValid ? GetAreaLaneAnchor(areaLaneIndex) : areaLaneAnchors[areaLaneIndex];
}

void RecoverDockPoseAfterLoad()
{
    if (dockingState != DockingState.None && !HasFreshCarrierPose())
        autopilotEnabled = false;
}

void RecoverDerivedRoutesAfterLoad()
{
    if (pathRecords.Count == 0)
        return;
    if (recallActive && !recallWaitingForAreaExit && !recallWaitingForDock)
    {
        int savedIndex = recallIndex;
        int savedDone = recallLastDone;
        int savedTotal = recallLastTotal;
        BuildRecallRecords();
        recallIndex = Math.Max(0, Math.Min(savedIndex, recallRecords.Count));
        recallLastDone = Math.Max(0, savedDone);
        recallLastTotal = savedTotal > 0 ? savedTotal : pathRecords.Count;
    }

    if (autoServiceState != AutoServiceState.WaitingService && autoServiceState != AutoServiceState.Undocking && autoServiceState != AutoServiceState.ReturningToWork)
        return;
    int savedReturnIndex = autoServiceReturnRouteIndex;
    bool savedRecallActive = recallActive;
    bool savedWaitArea = recallWaitingForAreaExit;
    bool savedWaitDock = recallWaitingForDock;
    int savedRecallIndex = recallIndex;
    int savedRecallDone = recallLastDone;
    int savedRecallTotal = recallLastTotal;
    BuildRecallRecords();
    BuildAutoServiceReturnRoute();
    autoServiceReturnRouteIndex = Math.Max(0, Math.Min(savedReturnIndex, autoServiceReturnRouteRecords.Count));
    recallActive = savedRecallActive;
    recallWaitingForAreaExit = savedWaitArea;
    recallWaitingForDock = savedWaitDock;
    recallIndex = savedRecallIndex;
    recallLastDone = savedRecallDone;
    recallLastTotal = savedRecallTotal;
    if (!recallActive)
    {
        recallRecords.Clear();
        recallDisplayDone.Clear();
    }
}
