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
    CancelCarrierSaveWork();
    RequestCarrierSave(true);
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
        RequestCarrierSave(true);
        return false;
    }
    string next = EncodeInternalState(_internalCore, _internalRoute, _internalReports, progress);
    if (!string.Equals(Storage, next, StringComparison.Ordinal)) Storage = next;
    _internalProgress = progress;
    return true;
}
// END INTERNAL PERSISTENCE

// BEGIN BOUNDED CARRIER SAVE
// Freeze raw values in one invocation, format bounded batches, render and publish
// on separate invocations. Delayed commits never accept newer dirty changes.
struct CarrierSaveValue
{
    public int Kind;
    public long Integer;
    public double Number;
    public Vector3D Vector;
    public string Text;
    public MiningReportData.Snapshot Report;
}
const int carrierSaveFieldsPerTick = 8;
readonly string[] _carrierSaveKeys = {
    "autopilotEnabled",
    "scriptPaused",
    "pauseResumeAutopilot",
    "cockpitAbortPaused",
    "cockpitAbortResumeAutopilot",
    "hasTarget",
    "targetPosition",
    "desiredForward",
    "desiredUp",
    "activeMaxSpeed",
    "carrierAutopilotMode",
    "cruiseFinalPosition",
    "cruiseFinalForward",
    "cruiseFinalUp",
    "cruiseStagingPosition",
    "cruiseDirection",
    "cruiseTurnTestStartForward",
    "cruiseTurnTestTicks",
    "measuredFlipSeconds",
    "measuredFlipMass",
    "maxSpeed",
    "orientationToleranceDeg",
    "arrivalTolerance",
    "shortRangeDistanceMeters",
    "cruiseOffsetMeters",
    "thrustLimitKn",
    "thrustUsePercent",
    "caThrustStepIndex",
    "accelerationLimit",
    "turnTestEnabled",
    "lcdRefreshIntervalTicks",
    "pathMode",
    "pathPaused",
    "pathPausedNavResume",
    "pathIndex",
    "pathAnchorValid",
    "pathAnchorPos",
    "pathAnchorForward",
    "pathAnchorUp",
    "minerPathCompleteReceived",
    "minerPathCompleteKind",
    "mineRecallRequested",
    "mineRecallActive",
    "mineRecallAcknowledged",
    "mineRecallMissionId",
    "mineRecallFailedTicks",
    "mineStationaryTicks",
    "fullAutoEnabled",
    "emma3IceReserveEnabled",
    "emma3IceReserveMillionLiters",
    "emma3ItemsMode",
    "emma3EnabledItems",
    "emma3AllowSeatExitDuringMining",
    "emma3WaitForSeatBeforeNextPlan",
    "fullAutoEnableStationaryTicks",
    "emma3MissionState",
    "emma3RequestSequence",
    "emma3ResetSequence",
    "emma3CompletionSequence",
    "emma3ActiveEntityId",
    "emma3MissionId",
    "emma3CarrierRouteIndex",
    "emma3CarrierParkAligning",
    "minerHangarStored",
    "minerHangarStorePendingForSnapshot",
    "minerHangarStoreCommandSent",
    "minerHangarManagerPending",
    "minerHangarManagerRequestId",
    "minerHangarManagerWaitTicks",
    "minerHangarOnlineWaitTicks",
    "minerHangarOperationId",
    "manualHangarCycleState",
    "manualHangarCycleDelayTicks",
    "manualHangarCycleCommandRetryTicks",
    "manualHangarCycleCommandWaitTicks",
    "reportMission",
    "reportAsteroid",
    "reportCompleted",
    "reportVisible",
    "reportFrozen",
    "finishAndHold",
    "asteroidReport",
    "miningTotals",
    "reportApproachSeconds",
    "reportPark",
    "reportHasBox",
    "reportBoxMin",
    "reportBoxMax",
    "reportTimingVersion",
    "reportParked",
    "reportCompletionCounted",
    "measuredFlipGyroAuthority",
    "minerHangarPoseRequestId",
    "minerHangarStoreCommandText"
};
readonly int[] _carrierProgressSaveIndexes = { 0, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 31, 32, 34, 35, 39, 40, 41, 42, 43, 44, 47, 52, 53, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 92, 93 };
readonly int[] _carrierSaveVectorSlots = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 1, 2, 3, 4, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 6, -1, 7, 8, -1, -1, -1, -1, -1, -1 };
readonly CarrierSaveValue[] _carrierSaveValues = new CarrierSaveValue[94];
readonly CarrierSaveValue[] _carrierFormattedValues = new CarrierSaveValue[94];
readonly string[] _carrierFormattedText = new string[94];
readonly MyIni _carrierPendingSaveIni = new MyIni();
readonly MyIni _carrierPendingRouteIni = new MyIni();
readonly MyIni _carrierPendingReportsIni = new MyIni();
readonly List<MyIniKey> _carrierPreviousSaveKeys = new List<MyIniKey>();
bool _carrierSavePending, _carrierSaveFull, _carrierFullSaveRequested;
int _carrierSaveIndex, _carrierSaveStage, _carrierSaveLastWorkTick, _carrierSaveClearIndex;
long _carrierStateGeneration, _carrierSaveGeneration;
long _carrierProgressRevision, _carrierSaveRevision, _carrierCommittedRevision;
string _carrierSaveSerialized, _carrierRouteSaveSerialized, _carrierReportsSaveSerialized;
int _carrierSavePublishStage;

bool IsCarrierRouteSaveIndex(int index)
{
    return index >= 11 && index <= 17 || index >= 31 && index <= 38 || index >= 55 && index <= 62;
}

bool IsCarrierReportsSaveIndex(int index)
{
    return index >= 75 && index <= 90;
}

MyIni CarrierFullSaveDestination(int index)
{
    if (IsCarrierRouteSaveIndex(index)) return _carrierPendingRouteIni;
    if (IsCarrierReportsSaveIndex(index)) return _carrierPendingReportsIni;
    return _carrierPendingSaveIni;
}

void CaptureCarrierSave(int index, long value)
{
    _carrierSaveValues[index].Kind = 0;
    _carrierSaveValues[index].Integer = value;
}
void CaptureCarrierSave(int index, bool value)
{
    _carrierSaveValues[index].Kind = 5;
    _carrierSaveValues[index].Integer = value ? 1 : 0;
}
void CaptureCarrierSave(int index, double value, int format)
{
    _carrierSaveValues[index].Kind = format;
    _carrierSaveValues[index].Number = value;
}
void CaptureCarrierSave(int index, Vector3D value, bool direction)
{
    _carrierSaveValues[index].Kind = direction ? 3 : 2;
    _carrierSaveValues[index].Vector = value;
}
void CaptureCarrierSave(int index, string value)
{
    _carrierSaveValues[index].Kind = 4;
    _carrierSaveValues[index].Text = value;
}
void CaptureCarrierSave(int index, MiningReportData.Snapshot value)
{
    _carrierSaveValues[index].Kind = 6;
    _carrierSaveValues[index].Report = value;
}
void CaptureCarrierSaveSnapshot(bool full)
{
    MinerSlot miner = ActiveMiner();
    CaptureCarrierSave(0, autopilotEnabled ? "1" : "0");
    CaptureCarrierSave(5, hasTarget ? "1" : "0");
    CaptureCarrierSave(6, targetPosition, false);
    CaptureCarrierSave(7, desiredForward, true);
    CaptureCarrierSave(8, desiredUp, true);
    CaptureCarrierSave(9, activeMaxSpeed, 1);
    CaptureCarrierSave(10, (int)carrierAutopilotMode);
    CaptureCarrierSave(11, cruiseFinalPosition, false);
    CaptureCarrierSave(12, cruiseFinalForward, true);
    CaptureCarrierSave(13, cruiseFinalUp, true);
    CaptureCarrierSave(14, cruiseStagingPosition, false);
    CaptureCarrierSave(15, cruiseDirection, true);
    CaptureCarrierSave(16, cruiseTurnTestStartForward, true);
    CaptureCarrierSave(17, cruiseTurnTestTicks);
    CaptureCarrierSave(31, pathMode ? "1" : "0");
    CaptureCarrierSave(32, pathPaused ? "1" : "0");
    CaptureCarrierSave(34, pathIndex);
    CaptureCarrierSave(35, pathAnchorValid ? "1" : "0");
    CaptureCarrierSave(39, miner.PathCompleteReceived ? "1" : "0");
    CaptureCarrierSave(40, miner.PathCompleteKind);
    CaptureCarrierSave(41, miner.RecallRequested ? "1" : "0");
    CaptureCarrierSave(42, miner.RecallActive ? "1" : "0");
    CaptureCarrierSave(43, miner.RecallAcknowledged ? "1" : "0");
    CaptureCarrierSave(44, miner.RecallMissionId);
    CaptureCarrierSave(47, fullAutoEnabled ? "1" : "0");
    CaptureCarrierSave(52, emma3AllowSeatExitDuringMining ? "1" : "0");
    CaptureCarrierSave(53, emma3WaitForSeatBeforeNextPlan ? "1" : "0");
    CaptureCarrierSave(55, (int)emma3MissionState);
    CaptureCarrierSave(56, emma3RequestSequence);
    CaptureCarrierSave(57, emma3ResetSequence);
    CaptureCarrierSave(58, emma3CompletionSequence);
    CaptureCarrierSave(59, emma3ActiveEntityId);
    CaptureCarrierSave(60, emma3MissionId);
    CaptureCarrierSave(61, emma3CarrierRouteIndex);
    CaptureCarrierSave(62, emma3CarrierParkAligning ? "1" : "0");
    CaptureCarrierSave(63, miner.HangarStored ? "1" : "0");
    CaptureCarrierSave(64, miner.HangarStorePendingForSnapshot ? "1" : "0");
    CaptureCarrierSave(65, miner.HangarStoreCommandSent ? "1" : "0");
    CaptureCarrierSave(66, (int)miner.HangarManagerPending);
    CaptureCarrierSave(67, miner.HangarManagerRequestId);
    CaptureCarrierSave(68, miner.HangarManagerWaitTicks);
    CaptureCarrierSave(69, miner.HangarMinerOnlineWaitTicks);
    CaptureCarrierSave(70, miner.HangarOperationId);
    CaptureCarrierSave(92, miner.HangarPoseRequestId);
    CaptureCarrierSave(93, miner.HangarStoreCommandText);
    CaptureCarrierSave(71, (int)manualHangarCycleState);
    CaptureCarrierSave(72, manualHangarCycleDelayTicks);
    CaptureCarrierSave(73, manualHangarCycleCommandRetryTicks);
    CaptureCarrierSave(74, manualHangarCycleCommandWaitTicks);
    CaptureCarrierSave(75, reportMission);
    CaptureCarrierSave(76, reportAsteroid);
    CaptureCarrierSave(77, reportCompleted);
    CaptureCarrierSave(78, reportVisible);
    CaptureCarrierSave(79, reportFrozen);
    CaptureCarrierSave(80, finishAndHold);
    CaptureCarrierSave(81, asteroidReport.CaptureSnapshot());
    CaptureCarrierSave(82, miningTotals.CaptureSnapshot());
    CaptureCarrierSave(83, reportApproachSeconds, 8);
    CaptureCarrierSave(84, reportPark, false);
    CaptureCarrierSave(85, reportHasBox);
    CaptureCarrierSave(86, reportBoxMin, false);
    CaptureCarrierSave(87, reportBoxMax, false);
    CaptureCarrierSave(88, 2);
    CaptureCarrierSave(89, reportParked);
    CaptureCarrierSave(90, reportCompletionCounted);
    if (full)
    {
        CaptureCarrierSave(1, scriptPaused ? "1" : "0");
        CaptureCarrierSave(2, pauseResumeAutopilot ? "1" : "0");
        CaptureCarrierSave(3, cockpitAbortPaused ? "1" : "0");
        CaptureCarrierSave(4, cockpitAbortResumeAutopilot ? "1" : "0");
        CaptureCarrierSave(18, measuredFlipSeconds, 7);
        CaptureCarrierSave(19, measuredFlipMass, 1);
        CaptureCarrierSave(91, measuredFlipGyroAuthority, 7);
        CaptureCarrierSave(20, maxSpeed, 1);
        CaptureCarrierSave(21, orientationToleranceDeg, 1);
        CaptureCarrierSave(22, arrivalTolerance, 1);
        CaptureCarrierSave(23, shortRangeDistanceMeters, 1);
        CaptureCarrierSave(24, cruiseOffsetMeters, 1);
        CaptureCarrierSave(25, thrustLimitKn, 1);
        CaptureCarrierSave(26, thrustUsePercent, 1);
        CaptureCarrierSave(27, caThrustStepIndex);
        CaptureCarrierSave(28, accelerationLimit, 1);
        CaptureCarrierSave(29, turnTestEnabled ? "1" : "0");
        CaptureCarrierSave(30, lcdRefreshIntervalTicks);
        CaptureCarrierSave(33, pathPausedNavResume ? "1" : "0");
        CaptureCarrierSave(36, pathAnchorPos, false);
        CaptureCarrierSave(37, pathAnchorForward, true);
        CaptureCarrierSave(38, pathAnchorUp, true);
        CaptureCarrierSave(45, miner.RecallFailedTicks);
        CaptureCarrierSave(46, mineStationaryTicks);
        CaptureCarrierSave(48, emma3IceReserveEnabled);
        CaptureCarrierSave(49, emma3IceReserveMillionLiters, 8);
        CaptureCarrierSave(50, emma3ItemsMode);
        CaptureCarrierSave(51, emma3EnabledItems);
        CaptureCarrierSave(54, fullAutoEnableStationaryTicks);
    }
}
void InitializeCarrierSaveWork()
{
    // Constructor-only warm-up. Never publishes or accepts a dirty revision.
    if (_carrierSavePending) return;
    _carrierSaveFull = true;
    CaptureCarrierSaveSnapshot(true);
    _carrierPendingSaveIni.Clear();
    _carrierPendingRouteIni.Clear();
    _carrierPendingReportsIni.Clear();
    for (int i = 0; i < _carrierSaveKeys.Length; i++)
        while (!SetPendingCarrierSaveField(i)) { }
    _carrierPendingSaveIni.ToString();
    _carrierPendingRouteIni.ToString();
    _carrierPendingReportsIni.ToString();
    _carrierPendingSaveIni.Clear();
    _carrierSaveFull = false;
}
void CancelCarrierSaveWork()
{
    if (!_carrierSavePending) return;
    if (_carrierSaveFull) _carrierFullSaveRequested = true;
    progressStateDirty = true;
    _carrierSavePending = false;
}
void RequestCarrierSave(bool full)
{
    if (string.IsNullOrWhiteSpace(_internalCore)) full = true;
    if (full) _carrierFullSaveRequested = true;
    // Complete an older full snapshot before taking another. No later progress
    // overlay may be paired with an as-yet unpublished full generation.
    if (_carrierSavePending)
    {
        if (!_carrierFullSaveRequested || _carrierSaveFull) return;
        CancelCarrierSaveWork();
    }
    full = _carrierFullSaveRequested;
    if (!full && !progressStateDirty) return;
    CaptureCarrierSaveSnapshot(full);
    _carrierSaveFull = full;
    _carrierSaveIndex = 0;
    _carrierSaveStage = 0;
    _carrierSaveLastWorkTick = runtime;
    _carrierSaveGeneration = _carrierStateGeneration + (full ? 1 : 0);
    _carrierSaveRevision = _carrierProgressRevision;
    _carrierSaveSerialized = null;
    _carrierRouteSaveSerialized = null;
    _carrierReportsSaveSerialized = null;
    _carrierSavePublishStage = 0;
    _carrierSavePending = true;
    _carrierFullSaveRequested = false;
    progressStateDirty = false;
}
bool SetPendingCarrierSaveField(int index)
{
    CarrierSaveValue value = _carrierSaveValues[index];
    int slot = _carrierSaveVectorSlots[index];
    if (!_carrierSaveFull && slot >= 0 && _carrierProgressVectorValid[slot]
        && value.Vector == _carrierProgressVectors[slot]) return true;
    string text;
    if (value.Kind == 6)
    {
        if (!value.Report.Prepare(carrierSaveFieldsPerTick)) return false;
        text = value.Report.Text;
    }
    else
    {
        CarrierSaveValue previous = _carrierFormattedValues[index];
        bool unchanged = _carrierFormattedText[index] != null && previous.Kind == value.Kind
            && (value.Kind == 0 || value.Kind == 5 ? previous.Integer == value.Integer
                : value.Kind == 1 || value.Kind == 7 || value.Kind == 8 ? previous.Number == value.Number
                : value.Kind == 2 || value.Kind == 3 ? previous.Vector == value.Vector
                : string.Equals(previous.Text, value.Text, StringComparison.Ordinal));
        if (!unchanged)
        {
            _carrierFormattedText[index] = value.Kind == 0 ? value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value.Kind == 1 ? Number2(value.Number)
                : value.Kind == 7 ? Number3(value.Number)
                : value.Kind == 8 ? NumberR(value.Number)
                : value.Kind == 2 ? V3ToString(value.Vector)
                : value.Kind == 3 ? DirectionToString(value.Vector)
                : value.Kind == 4 ? value.Text ?? "" : value.Integer != 0 ? "true" : "false";
            _carrierFormattedValues[index] = value;
        }
        text = _carrierFormattedText[index];
    }
    MyIni destination = _carrierSaveFull ? CarrierFullSaveDestination(index) : _carrierPendingSaveIni;
    destination.Set(_carrierSaveFull ? StateIniSection : ProgressIniSection, _carrierSaveKeys[index], text);
    return true;
}
void PumpCarrierSaveWork()
{
    if (!_carrierSavePending || runtime == _carrierSaveLastWorkTick) return;
    if (carrierPlanLoadJob != null) return;
    if ((_carrierSaveStage == 2 || _carrierSaveStage == 3 || _carrierSaveStage == 5 || _carrierSaveStage == 6) && !TryClaimCarrierHeavyWork()) return;
    _carrierSaveLastWorkTick = runtime;
    string section = _carrierSaveFull ? StateIniSection : ProgressIniSection;
    if (_carrierSaveStage == 0)
    {
        _carrierPendingSaveIni.Clear();
        _carrierPendingRouteIni.Clear();
        _carrierPendingReportsIni.Clear();
        if (_carrierSaveFull)
        {
            // Only full snapshots touch the timer. Keep unrelated user sections.
            if (!string.IsNullOrWhiteSpace(_internalCore))
            {
                MyIniParseResult result;
                if (!_carrierPendingSaveIni.TryParse(_internalCore, out result))
                    _carrierPendingSaveIni.Clear();
            }
            _carrierPendingSaveIni.GetKeys(StateIniSection, _carrierPreviousSaveKeys);
            _carrierSaveClearIndex = 0;
        }
        _carrierSaveStage = _carrierSaveFull && _carrierPreviousSaveKeys.Count > 0 ? 4 : 1;
        return;
    }
    if (_carrierSaveStage == 4)
    {
        // Game MyIni.DeleteSection matches item NAME rather than section when
        // deleting keys. Explicit section keys avoid that bug and bound cleanup.
        int end = Math.Min(_carrierPreviousSaveKeys.Count, _carrierSaveClearIndex + carrierSaveFieldsPerTick);
        while (_carrierSaveClearIndex < end)
            _carrierPendingSaveIni.Delete(_carrierPreviousSaveKeys[_carrierSaveClearIndex++]);
        if (_carrierSaveClearIndex == _carrierPreviousSaveKeys.Count) _carrierSaveStage = 1;
        return;
    }
    if (_carrierSaveStage == 1)
    {
        int count = _carrierSaveFull ? _carrierSaveKeys.Length : _carrierProgressSaveIndexes.Length;
        int start = _carrierSaveIndex;
        int end = Math.Min(count, start + carrierSaveFieldsPerTick);
        while (_carrierSaveIndex < end)
        {
            int index = _carrierSaveFull ? _carrierSaveIndex : _carrierProgressSaveIndexes[_carrierSaveIndex];
            // Each report receives a separate work invocation, so its ore batch
            // is not piled on top of seven other formatted fields.
            if (_carrierSaveValues[index].Kind == 6 && _carrierSaveIndex != start)
                return;
            if (!SetPendingCarrierSaveField(index)) return;
            _carrierSaveIndex++;
            if (_carrierSaveValues[index].Kind == 6) return;
        }
        if (_carrierSaveIndex == count) _carrierSaveStage = 2;
        return;
    }
    if (_carrierSaveStage == 2)
    {
        _carrierPendingSaveIni.Set(section, "snapshotGeneration", _carrierSaveGeneration);
        _carrierSaveSerialized = _carrierPendingSaveIni.ToString();
        _carrierSaveStage = _carrierSaveFull ? 5 : 3;
        return;
    }
    if (_carrierSaveStage == 5)
    {
        _carrierPendingRouteIni.Set(section, "snapshotGeneration", _carrierSaveGeneration);
        _carrierRouteSaveSerialized = _carrierPendingRouteIni.ToString();
        _carrierSaveStage = 6;
        return;
    }
    if (_carrierSaveStage == 6)
    {
        _carrierPendingReportsIni.Set(section, "snapshotGeneration", _carrierSaveGeneration);
        _carrierReportsSaveSerialized = _carrierPendingReportsIni.ToString();
        _carrierSaveStage = 3;
        return;
    }
    // Internal publication is the commit. Optional timer mirrors follow on
    // later invocations, so interruption or missing timers cannot lose the save.
    if (_carrierSaveFull)
    {
        if (_carrierSavePublishStage == 0)
        {
            PublishInternalState(_carrierSaveSerialized, _carrierRouteSaveSerialized, _carrierReportsSaveSerialized);
            _carrierStateGeneration = _carrierSaveGeneration;
            for (int i = 0; i < _carrierSaveVectorSlots.Length; i++)
            {
                int slot = _carrierSaveVectorSlots[i];
                if (slot < 0) continue;
                _carrierProgressVectors[slot] = _carrierSaveValues[i].Vector;
                _carrierProgressVectorValid[slot] = true;
            }
            _hasProgressState = false;
            _progressIni.Clear();
            _carrierSavePublishStage = 1;
            _carrierCommittedRevision = Math.Max(_carrierCommittedRevision, _carrierSaveRevision);
            if (timerPersistenceEnabled) return;
        }
        if (timerPersistenceEnabled)
        {
            IMyTimerBlock timer = FindStateIniTimer();
            if (timer == null || _cachedStateRouteTimer == null || _cachedStateReportsTimer == null)
            {
                ScriptLog("INI timer mirror unavailable; complete internal checkpoint was saved.");
            }
            else if (_carrierSavePublishStage == 1)
            {
                _cachedStateRouteTimer.CustomData = _carrierRouteSaveSerialized ?? "";
                _carrierSavePublishStage = 2;
                return;
            }
            else if (_carrierSavePublishStage == 2)
            {
                _cachedStateReportsTimer.CustomData = _carrierReportsSaveSerialized ?? "";
                _carrierSavePublishStage = 3;
                return;
            }
            else timer.CustomData = _carrierSaveSerialized;
        }
    }
    else
    {
        if (!PublishInternalProgress(_carrierSaveSerialized))
        {
            CancelCarrierSaveWork();
            return;
        }
        _hasProgressState = true;
    }
    _carrierCommittedRevision = Math.Max(_carrierCommittedRevision, _carrierSaveRevision);
    _carrierSavePending = false;
}
// END BOUNDED CARRIER SAVE


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
    string reports = internalState ? _internalReports : _cachedStateReportsTimer == null ? "" : _cachedStateReportsTimer.CustomData ?? "";
    long generation;
    if (!long.TryParse(ini.Get(StateIniSection, "snapshotGeneration").ToString("0"), out generation)
        || !MergeCarrierStatePart(ini, route, generation, true)
        || !MergeCarrierStatePart(ini, reports, generation, false))
    {
        ScriptLog("Persistence checkpoint rejected: Core/Route/Reports generations do not match; no mission was restored.");
        return false;
    }
    _internalCore = data; _internalRoute = route; _internalReports = reports;
    return true;
}

bool MergeCarrierStatePart(MyIni destination, string data, long generation, bool route)
{
    if (string.IsNullOrWhiteSpace(data)) return false;
    MyIni source = new MyIni();
    MyIniParseResult result;
    if (!source.TryParse(data, out result)
        || source.Get(StateIniSection, "snapshotGeneration").ToInt64(-1) != generation) return false;
    for (int i = 0; i < _carrierSaveKeys.Length; i++)
    {
        if (route != IsCarrierRouteSaveIndex(i) || !route && !IsCarrierReportsSaveIndex(i)) continue;
        MyIniValue value = source.Get(StateIniSection, _carrierSaveKeys[i]);
        if (!value.IsEmpty) destination.Set(StateIniSection, _carrierSaveKeys[i], value.ToString());
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
    MyIni reports = new MyIni();
    for (int i = 0; i < _carrierSaveKeys.Length; i++)
    {
        bool routeField = IsCarrierRouteSaveIndex(i);
        bool reportField = IsCarrierReportsSaveIndex(i);
        if (!routeField && !reportField) continue;
        string key = _carrierSaveKeys[i];
        MyIniValue value = core.Get(StateIniSection, key);
        if (!value.IsEmpty)
            (routeField ? route : reports).Set(StateIniSection, key, value.ToString());
        core.Delete(StateIniSection, key);
    }
    string generation = ini.Get(StateIniSection, "snapshotGeneration").ToString("0");
    route.Set(StateIniSection, "snapshotGeneration", generation);
    reports.Set(StateIniSection, "snapshotGeneration", generation);

    PublishInternalState(core.ToString(), route.ToString(), reports.ToString());
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
        || generation != _carrierStateGeneration)
        return false;
    return true;
}

void WriteProgressState(MyIni ini)
{
    string next = ini.ToString();
    if (!PublishInternalProgress(next)) return;
    _hasProgressState = true;
    progressStateDirty = false;
    _carrierCommittedRevision = _carrierProgressRevision;
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
    _cachedStateReportsTimer = null;
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
        else if (_cachedStateReportsTimer == null && timer.CustomName.Equals(stateReportsTimerTag, StringComparison.OrdinalIgnoreCase))
            _cachedStateReportsTimer = timer;
    }
}

List<IMyTimerBlock> GetTimerBlocksCached()
{
    return _timerBlocks;
}

void SaveState()
{
    CancelCarrierSaveWork();
    MyIni ini = _stateIni;
    ini.Clear();
    if (!string.IsNullOrWhiteSpace(_internalCore))
    {
        MyIniParseResult existingParseResult;
        if (!ini.TryParse(_internalCore, out existingParseResult))
            ini.Clear();
    }

    // Preserve unrelated timer sections, but replace our snapshot completely so
    // keys removed in newer versions cannot linger as hidden duplicate state.
    var oldStateKeys = new List<MyIniKey>();
    ini.GetKeys(StateIniSection, oldStateKeys);
    for (int i = 0; i < oldStateKeys.Count; i++) ini.Delete(oldStateKeys[i]);
    ini.Set(StateIniSection, "autopilotEnabled", autopilotEnabled ? "1" : "0");
    ini.Set(StateIniSection, "scriptPaused", scriptPaused ? "1" : "0");
    ini.Set(StateIniSection, "pauseResumeAutopilot", pauseResumeAutopilot ? "1" : "0");
    ini.Set(StateIniSection, "cockpitAbortPaused", cockpitAbortPaused ? "1" : "0");
    ini.Set(StateIniSection, "cockpitAbortResumeAutopilot", cockpitAbortResumeAutopilot ? "1" : "0");
    ini.Set(StateIniSection, "hasTarget", hasTarget ? "1" : "0");
    ini.Set(StateIniSection, "targetPosition", V3ToString(targetPosition));
    ini.Set(StateIniSection, "desiredForward", DirectionToString(desiredForward));
    ini.Set(StateIniSection, "desiredUp", DirectionToString(desiredUp));
    ini.Set(StateIniSection, "activeMaxSpeed", Number2(activeMaxSpeed));
    ini.Set(StateIniSection, "carrierAutopilotMode", (int)carrierAutopilotMode);
    ini.Set(StateIniSection, "cruiseFinalPosition", V3ToString(cruiseFinalPosition));
    ini.Set(StateIniSection, "cruiseFinalForward", DirectionToString(cruiseFinalForward));
    ini.Set(StateIniSection, "cruiseFinalUp", DirectionToString(cruiseFinalUp));
    ini.Set(StateIniSection, "cruiseStagingPosition", V3ToString(cruiseStagingPosition));
    ini.Set(StateIniSection, "cruiseDirection", DirectionToString(cruiseDirection));
    ini.Set(StateIniSection, "cruiseTurnTestStartForward", DirectionToString(cruiseTurnTestStartForward));
    ini.Set(StateIniSection, "cruiseTurnTestTicks", cruiseTurnTestTicks);
    ini.Set(StateIniSection, "measuredFlipSeconds", Number3(measuredFlipSeconds));
    ini.Set(StateIniSection, "measuredFlipMass", Number2(measuredFlipMass));
    ini.Set(StateIniSection, "measuredFlipGyroAuthority", Number3(measuredFlipGyroAuthority));
    ini.Set(StateIniSection, "maxSpeed", Number2(maxSpeed));
    ini.Set(StateIniSection, "orientationToleranceDeg", Number2(orientationToleranceDeg));
    ini.Set(StateIniSection, "arrivalTolerance", Number2(arrivalTolerance));
    ini.Set(StateIniSection, "shortRangeDistanceMeters", Number2(shortRangeDistanceMeters));
    ini.Set(StateIniSection, "cruiseOffsetMeters", Number2(cruiseOffsetMeters));
    ini.Set(StateIniSection, "thrustLimitKn", Number2(thrustLimitKn));
    ini.Set(StateIniSection, "thrustUsePercent", Number2(thrustUsePercent));
    ini.Set(StateIniSection, "caThrustStepIndex", caThrustStepIndex);
    ini.Set(StateIniSection, "accelerationLimit", Number2(accelerationLimit));
    ini.Set(StateIniSection, "turnTestEnabled", turnTestEnabled ? "1" : "0");
    ini.Set(StateIniSection, "lcdRefreshIntervalTicks", lcdRefreshIntervalTicks);
    ini.Set(StateIniSection, "pathMode", pathMode ? "1" : "0");
    ini.Set(StateIniSection, "pathPaused", pathPaused ? "1" : "0");
    ini.Set(StateIniSection, "pathPausedNavResume", pathPausedNavResume ? "1" : "0");
    ini.Set(StateIniSection, "pathIndex", pathIndex);
    ini.Set(StateIniSection, "pathAnchorValid", pathAnchorValid ? "1" : "0");
    ini.Set(StateIniSection, "pathAnchorPos", V3ToString(pathAnchorPos));
    ini.Set(StateIniSection, "pathAnchorForward", DirectionToString(pathAnchorForward));
    ini.Set(StateIniSection, "pathAnchorUp", DirectionToString(pathAnchorUp));
    ini.Set(StateIniSection, "minerPathCompleteReceived", ActiveMiner().PathCompleteReceived ? "1" : "0");
    ini.Set(StateIniSection, "minerPathCompleteKind", ActiveMiner().PathCompleteKind);
    ini.Set(StateIniSection, "mineRecallRequested", ActiveMiner().RecallRequested ? "1" : "0");
    ini.Set(StateIniSection, "mineRecallActive", ActiveMiner().RecallActive ? "1" : "0");
    ini.Set(StateIniSection, "mineRecallAcknowledged", ActiveMiner().RecallAcknowledged ? "1" : "0");
    ini.Set(StateIniSection, "mineRecallMissionId", ActiveMiner().RecallMissionId);
    ini.Set(StateIniSection, "mineRecallFailedTicks", ActiveMiner().RecallFailedTicks);
    ini.Set(StateIniSection, "mineStationaryTicks", mineStationaryTicks);
    ini.Set(StateIniSection, "fullAutoEnabled", fullAutoEnabled ? "1" : "0");
    ini.Set(StateIniSection, "emma3IceReserveEnabled", emma3IceReserveEnabled);
    ini.Set(StateIniSection, "emma3IceReserveMillionLiters", NumberR(emma3IceReserveMillionLiters));
    ini.Set(StateIniSection, "emma3ItemsMode", emma3ItemsMode);
    ini.Set(StateIniSection, "emma3EnabledItems", emma3EnabledItems);
    ini.Set(StateIniSection, "emma3AllowSeatExitDuringMining", emma3AllowSeatExitDuringMining ? "1" : "0");
    ini.Set(StateIniSection, "emma3WaitForSeatBeforeNextPlan", emma3WaitForSeatBeforeNextPlan ? "1" : "0");
    ini.Set(StateIniSection, "fullAutoEnableStationaryTicks", fullAutoEnableStationaryTicks);
    SaveEmma3State(ini, StateIniSection);
    SaveMiningReports(ini, StateIniSection);
    ini.Set(StateIniSection, "snapshotGeneration", _carrierStateGeneration + 1);
    if (!WriteStateIni(ini))
    {
        _carrierFullSaveRequested = true;
        progressStateDirty = true;
        return;
    }
    _carrierStateGeneration++;
    _carrierFullSaveRequested = false;
    _carrierCommittedRevision = _carrierProgressRevision;
    CaptureCarrierProgressVectorBaseline();
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
    CancelCarrierSaveWork();
    MyIni ini = _progressIni;
    ini.Clear();
    ini.Set(ProgressIniSection, "autopilotEnabled", autopilotEnabled ? "1" : "0");
    ini.Set(ProgressIniSection, "hasTarget", hasTarget ? "1" : "0");
    ini.Set(ProgressIniSection, "targetPosition", V3ToString(targetPosition));
    ini.Set(ProgressIniSection, "desiredForward", DirectionToString(desiredForward));
    ini.Set(ProgressIniSection, "desiredUp", DirectionToString(desiredUp));
    ini.Set(ProgressIniSection, "activeMaxSpeed", Number2(activeMaxSpeed));
    ini.Set(ProgressIniSection, "carrierAutopilotMode", (int)carrierAutopilotMode);
    SetCarrierProgressVector(ini, ProgressIniSection, 0, cruiseFinalPosition, false);
    SetCarrierProgressVector(ini, ProgressIniSection, 1, cruiseFinalForward, true);
    SetCarrierProgressVector(ini, ProgressIniSection, 2, cruiseFinalUp, true);
    SetCarrierProgressVector(ini, ProgressIniSection, 3, cruiseStagingPosition, false);
    SetCarrierProgressVector(ini, ProgressIniSection, 4, cruiseDirection, true);
    SetCarrierProgressVector(ini, ProgressIniSection, 5, cruiseTurnTestStartForward, true);
    ini.Set(ProgressIniSection, "cruiseTurnTestTicks", cruiseTurnTestTicks);
    ini.Set(ProgressIniSection, "pathMode", pathMode ? "1" : "0");
    ini.Set(ProgressIniSection, "pathPaused", pathPaused ? "1" : "0");
    ini.Set(ProgressIniSection, "pathIndex", pathIndex);
    ini.Set(ProgressIniSection, "pathAnchorValid", pathAnchorValid ? "1" : "0");
    ini.Set(ProgressIniSection, "minerPathCompleteReceived", ActiveMiner().PathCompleteReceived ? "1" : "0");
    ini.Set(ProgressIniSection, "minerPathCompleteKind", ActiveMiner().PathCompleteKind);
    ini.Set(ProgressIniSection, "mineRecallRequested", ActiveMiner().RecallRequested ? "1" : "0");
    ini.Set(ProgressIniSection, "mineRecallActive", ActiveMiner().RecallActive ? "1" : "0");
    ini.Set(ProgressIniSection, "mineRecallAcknowledged", ActiveMiner().RecallAcknowledged ? "1" : "0");
    ini.Set(ProgressIniSection, "mineRecallMissionId", ActiveMiner().RecallMissionId);
    ini.Set(ProgressIniSection, "fullAutoEnabled", fullAutoEnabled ? "1" : "0");
    ini.Set(ProgressIniSection, "emma3AllowSeatExitDuringMining", emma3AllowSeatExitDuringMining ? "1" : "0");
    ini.Set(ProgressIniSection, "emma3WaitForSeatBeforeNextPlan", emma3WaitForSeatBeforeNextPlan ? "1" : "0");
    SaveEmma3State(ini, ProgressIniSection);
    SaveMiningReports(ini, ProgressIniSection);
    ini.Set(ProgressIniSection, "snapshotGeneration", _carrierStateGeneration);
    WriteProgressState(ini);
}

void SaveEmma3State(MyIni ini, string section)
{
    ini.Set(section, "emma3MissionState", (int)emma3MissionState);
    ini.Set(section, "emma3RequestSequence", emma3RequestSequence);
    ini.Set(section, "emma3ResetSequence", emma3ResetSequence);
    ini.Set(section, "emma3CompletionSequence", emma3CompletionSequence);
    ini.Set(section, "emma3ActiveEntityId", emma3ActiveEntityId.ToString());
    ini.Set(section, "emma3MissionId", emma3MissionId);
    ini.Set(section, "emma3CarrierRouteIndex", emma3CarrierRouteIndex);
    ini.Set(section, "emma3CarrierParkAligning", emma3CarrierParkAligning ? "1" : "0");
    ini.Set(section, "minerHangarStored", ActiveMiner().HangarStored ? "1" : "0");
    ini.Set(section, "minerHangarStorePendingForSnapshot", ActiveMiner().HangarStorePendingForSnapshot ? "1" : "0");
    ini.Set(section, "minerHangarStoreCommandSent", ActiveMiner().HangarStoreCommandSent ? "1" : "0");
    ini.Set(section, "minerHangarManagerPending", (int)ActiveMiner().HangarManagerPending);
    ini.Set(section, "minerHangarManagerRequestId", ActiveMiner().HangarManagerRequestId);
    ini.Set(section, "minerHangarManagerWaitTicks", ActiveMiner().HangarManagerWaitTicks);
    ini.Set(section, "minerHangarOnlineWaitTicks", ActiveMiner().HangarMinerOnlineWaitTicks);
    ini.Set(section, "minerHangarOperationId", ActiveMiner().HangarOperationId);
    ini.Set(section, "minerHangarPoseRequestId", ActiveMiner().HangarPoseRequestId);
    ini.Set(section, "minerHangarStoreCommandText", ActiveMiner().HangarStoreCommandText);
    ini.Set(section, "manualHangarCycleState", (int)manualHangarCycleState);
    ini.Set(section, "manualHangarCycleDelayTicks", manualHangarCycleDelayTicks);
    ini.Set(section, "manualHangarCycleCommandRetryTicks", manualHangarCycleCommandRetryTicks);
    ini.Set(section, "manualHangarCycleCommandWaitTicks", manualHangarCycleCommandWaitTicks);
}

readonly string[] _carrierProgressVectorKeys = { "cruiseFinalPosition", "cruiseFinalForward", "cruiseFinalUp", "cruiseStagingPosition", "cruiseDirection", "cruiseTurnTestStartForward", "reportPark", "reportBoxMin", "reportBoxMax" };
readonly Vector3D[] _carrierProgressVectors = new Vector3D[9];
readonly bool[] _carrierProgressVectorValid = new bool[9];

void LoadCarrierProgressVectorBaseline(MyIni ini)
{
    long.TryParse(ini.Get(StateIniSection, "snapshotGeneration").ToString("0"), out _carrierStateGeneration);
    // Capture only the full snapshot, before applying any newer progress overlay.
    for (int i = 0; i < _carrierProgressVectorKeys.Length; i++)
        _carrierProgressVectorValid[i] = TryParseV3(ini.Get(StateIniSection, _carrierProgressVectorKeys[i]).ToString(""), out _carrierProgressVectors[i]);
}

void CaptureCarrierProgressVectorBaseline()
{
    _carrierProgressVectors[0] = cruiseFinalPosition;
    _carrierProgressVectors[1] = cruiseFinalForward;
    _carrierProgressVectors[2] = cruiseFinalUp;
    _carrierProgressVectors[3] = cruiseStagingPosition;
    _carrierProgressVectors[4] = cruiseDirection;
    _carrierProgressVectors[5] = cruiseTurnTestStartForward;
    _carrierProgressVectors[6] = reportPark;
    _carrierProgressVectors[7] = reportBoxMin;
    _carrierProgressVectors[8] = reportBoxMax;
    for (int i = 0; i < _carrierProgressVectorValid.Length; i++) _carrierProgressVectorValid[i] = true;
}

void SetCarrierProgressVector(MyIni ini, string section, int index, Vector3D value, bool direction)
{
    if (section != ProgressIniSection || !_carrierProgressVectorValid[index] || value != _carrierProgressVectors[index])
        ini.Set(section, _carrierProgressVectorKeys[index], direction ? DirectionToString(value) : V3ToString(value));
}

void MarkProgressStateDirty()
{
    progressStateDirty = true;
    _carrierProgressRevision++;
}

long RequestCarrierProgressCheckpoint()
{
    MarkProgressStateDirty();
    RequestCarrierSave(false);
    return _carrierProgressRevision;
}

bool HasCarrierProgressCheckpoint(long revision)
{
    return _carrierCommittedRevision >= revision;
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

bool HasCarrierProgressStateChanged()
{
    long h = 1469598103934665603L;
    h = MixProgressHash(h, autopilotEnabled ? 1 : 0);
    h = MixProgressHash(h, hasTarget ? 1 : 0);
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.X, 0.1));
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.Y, 0.1));
    h = MixProgressHash(h, QuantizedProgressValue(targetPosition.Z, 0.1));
    h = MixProgressHash(h, (int)carrierAutopilotMode);
    h = MixProgressHash(h, pathMode ? 1 : 0);
    h = MixProgressHash(h, pathPaused ? 1 : 0);
    h = MixProgressHash(h, pathIndex);
    h = MixProgressHash(h, ActiveMiner().PathCompleteReceived ? 1 : 0);
    h = MixProgressHash(
        h,
        ActiveMiner().PathCompleteKind.Equals("recall", StringComparison.OrdinalIgnoreCase) ? 2 : (ActiveMiner().PathCompleteKind.Equals("path", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
    h = MixProgressHash(h, ActiveMiner().RecallRequested ? 1 : 0);
    h = MixProgressHash(h, ActiveMiner().RecallActive ? 1 : 0);
    h = MixProgressHash(h, ActiveMiner().RecallAcknowledged ? 1 : 0);
    h = MixProgressHash(h, fullAutoEnabled ? 1 : 0);
    h = MixProgressHash(h, emma3AllowSeatExitDuringMining ? 1 : 0);
    h = MixProgressHash(h, emma3WaitForSeatBeforeNextPlan ? 1 : 0);
    h = MixProgressHash(h, (int)emma3MissionState);
    h = MixProgressHash(h, emma3CarrierRouteIndex);
    h = MixProgressHash(h, emma3CarrierParkAligning ? 1 : 0);
    h = MixProgressHash(h, emma3RequestSequence);
    h = MixProgressHash(h, emma3CompletionSequence);
    h = MixProgressHash(h, (int)ActiveMiner().HangarManagerPending);
    h = MixProgressHash(h, ActiveMiner().HangarManagerRequestId);
    h = MixProgressHash(h, ActiveMiner().HangarManagerWaitTicks / 60);
    h = MixProgressHash(h, ActiveMiner().HangarMinerOnlineWaitTicks);
    h = MixProgressHash(h, ActiveMiner().HangarOperationId);
    h = MixProgressHash(h, (int)manualHangarCycleState);
    if (h == lastProgressStateFingerprint)
        return false;
    lastProgressStateFingerprint = h;
    return true;
}

bool HasVolatileProgressState()
{
    return autopilotEnabled || hasTarget || cockpitAbortPaused || carrierAutopilotMode != CarrierAutopilotMode.Precision || pathMode || pathPaused || ActiveMiner().PathCompleteReceived || ActiveMiner().RecallRequested || ActiveMiner().RecallActive || ActiveMiner().RecallAcknowledged || ActiveMiner().RecallFailedTicks > 0 || mineStationaryTicks > 0 || fullAutoEnableStationaryTicks > 0 || emma3WaitForSeatBeforeNextPlan || emma3MissionState != Emma3MissionState.None || ActiveMiner().HangarManagerPending != HangarManagerPendingCommand.None || manualHangarCycleState != ManualHangarCycleState.None;
}

void LoadState()
{
    Array.Clear(_carrierProgressVectorValid, 0, _carrierProgressVectorValid.Length);
    MyIni ini;
    if (!ReadStateIni(out ini))
        return;
    LoadCarrierProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    int n;
    double d;
    Vector3D v;
    autopilotEnabled = StateBool(ini, "autopilotEnabled", autopilotEnabled);
    scriptPaused = StateBool(ini, "scriptPaused", scriptPaused);
    pauseResumeAutopilot = StateBool(ini, "pauseResumeAutopilot", pauseResumeAutopilot);
    cockpitAbortPaused = StateBool(ini, "cockpitAbortPaused", cockpitAbortPaused);
    cockpitAbortResumeAutopilot = StateBool(ini, "cockpitAbortResumeAutopilot", cockpitAbortResumeAutopilot);
    hasTarget = StateBool(ini, "hasTarget", hasTarget);
    if (TryParseV3(StateString(ini, "targetPosition", V3ToString(targetPosition)), out v))
        targetPosition = v;
    if (TryParseV3(StateString(ini, "desiredForward", V3ToString(desiredForward)), out v))
        desiredForward = v;
    if (TryParseV3(StateString(ini, "desiredUp", V3ToString(desiredUp)), out v))
        desiredUp = v;
    if (TryParseNumber(StateString(ini, "activeMaxSpeed", NumberR(activeMaxSpeed)), out d))
        activeMaxSpeed = d;
    if (int.TryParse(StateString(ini, "carrierAutopilotMode", ((int)carrierAutopilotMode).ToString()), out n))
    {
        if (n < 0 || n > (int)CarrierAutopilotMode.CruiseDecel)
            n = 0;
        carrierAutopilotMode = (CarrierAutopilotMode)n;
    }

    if (TryParseV3(StateString(ini, "cruiseFinalPosition", V3ToString(cruiseFinalPosition)), out v))
        cruiseFinalPosition = v;
    if (TryParseV3(StateString(ini, "cruiseFinalForward", V3ToString(cruiseFinalForward)), out v))
        cruiseFinalForward = v;
    if (TryParseV3(StateString(ini, "cruiseFinalUp", V3ToString(cruiseFinalUp)), out v))
        cruiseFinalUp = v;
    if (TryParseV3(StateString(ini, "cruiseStagingPosition", V3ToString(cruiseStagingPosition)), out v))
        cruiseStagingPosition = v;
    if (TryParseV3(StateString(ini, "cruiseDirection", V3ToString(cruiseDirection)), out v))
        cruiseDirection = v;
    if (TryParseV3(StateString(ini, "cruiseTurnTestStartForward", V3ToString(cruiseTurnTestStartForward)), out v))
        cruiseTurnTestStartForward = v;
    if (int.TryParse(StateString(ini, "cruiseTurnTestTicks", cruiseTurnTestTicks.ToString()), out n))
        cruiseTurnTestTicks = Math.Max(0, n);
    LoadCarrierTurnCalibration(ini);
    if (TryParseNumber(StateString(ini, "maxSpeed", NumberR(maxSpeed)), out d))
        maxSpeed = MathHelper.Clamp(d, 1, 50000);
    if (TryParseNumber(StateString(ini, "orientationToleranceDeg", NumberR(orientationToleranceDeg)), out d))
        orientationToleranceDeg = d;
    if (TryParseNumber(StateString(ini, "arrivalTolerance", NumberR(arrivalTolerance)), out d))
        arrivalTolerance = d;
    if (TryParseNumber(StateString(ini, "shortRangeDistanceMeters", NumberR(shortRangeDistanceMeters)), out d))
        shortRangeDistanceMeters = Math.Max(100, d);
    if (TryParseNumber(StateString(ini, "cruiseOffsetMeters", NumberR(cruiseOffsetMeters)), out d))
        cruiseOffsetMeters = Math.Max(100, d);
    if (TryParseNumber(StateString(ini, "thrustLimitKn", NumberR(thrustLimitKn)), out d))
        thrustLimitKn = Math.Max(1, d);
    if (TryParseNumber(StateString(ini, "thrustUsePercent", NumberR(thrustUsePercent)), out d))
        thrustUsePercent = MathHelper.Clamp(d, 1, 100);
    if (int.TryParse(StateString(ini, "caThrustStepIndex", caThrustStepIndex.ToString()), out n))
        caThrustStepIndex = Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, n));
    if (TryParseNumber(StateString(ini, "accelerationLimit", NumberR(accelerationLimit)), out d))
        accelerationLimit = Math.Max(0.1, d);
    turnTestEnabled = StateBool(ini, "turnTestEnabled", turnTestEnabled);
    if (int.TryParse(StateString(ini, "lcdRefreshIntervalTicks", lcdRefreshIntervalTicks.ToString()), out n))
        lcdRefreshIntervalTicks = Math.Max(lcdRefreshMinTicks, n);
    pathMode = StateBool(ini, "pathMode", pathMode);
    pathPaused = StateBool(ini, "pathPaused", pathPaused);
    pathPausedNavResume = StateBool(ini, "pathPausedNavResume", pathPausedNavResume);
    if (int.TryParse(StateString(ini, "pathIndex", pathIndex.ToString()), out n))
        pathIndex = n;
    pathAnchorValid = StateBool(ini, "pathAnchorValid", pathAnchorValid);
    if (TryParseV3(StateString(ini, "pathAnchorPos", V3ToString(pathAnchorPos)), out v))
        pathAnchorPos = v;
    if (TryParseV3(StateString(ini, "pathAnchorForward", V3ToString(pathAnchorForward)), out v))
        pathAnchorForward = v;
    if (TryParseV3(StateString(ini, "pathAnchorUp", V3ToString(pathAnchorUp)), out v))
        pathAnchorUp = v;
    ActiveMiner().PathCompleteReceived = StateBool(ini, "minerPathCompleteReceived", ActiveMiner().PathCompleteReceived);
    ActiveMiner().PathCompleteKind = StateString(ini, "minerPathCompleteKind", ActiveMiner().PathCompleteKind);
    if (ActiveMiner().PathCompleteReceived && string.IsNullOrWhiteSpace(ActiveMiner().PathCompleteKind))
        ActiveMiner().PathCompleteKind = "path";
    ActiveMiner().RecallRequested = StateBool(ini, "mineRecallRequested", ActiveMiner().RecallRequested);
    ActiveMiner().RecallActive = StateBool(ini, "mineRecallActive", ActiveMiner().RecallActive);
    ActiveMiner().RecallAcknowledged = StateBool(ini, "mineRecallAcknowledged", ActiveMiner().RecallAcknowledged);
    ActiveMiner().RecallMissionId = StateString(ini, "mineRecallMissionId", ActiveMiner().RecallMissionId);
    if (int.TryParse(StateString(ini, "mineRecallFailedTicks", ActiveMiner().RecallFailedTicks.ToString()), out n))
        ActiveMiner().RecallFailedTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "mineStationaryTicks", mineStationaryTicks.ToString()), out n))
        mineStationaryTicks = n;
    fullAutoEnabled = StateBool(ini, "fullAutoEnabled", fullAutoEnabled);
    emma3IceReserveEnabled = StateBool(ini, "emma3IceReserveEnabled", false);
    if (TryParseNumber(StateString(ini, "emma3IceReserveMillionLiters", "1"), out d))
        emma3IceReserveMillionLiters = ClampIceReserveMillionLiters(d);
    emma3ItemsMode = StateBool(ini, "emma3ItemsMode", false);
    emma3EnabledItems = StateString(ini, "emma3EnabledItems", "");
    emma3AllowSeatExitDuringMining = StateBool(ini, "emma3AllowSeatExitDuringMining", emma3AllowSeatExitDuringMining);
    emma3WaitForSeatBeforeNextPlan = StateBool(ini, "emma3WaitForSeatBeforeNextPlan", emma3WaitForSeatBeforeNextPlan);
    if (int.TryParse(StateString(ini, "fullAutoEnableStationaryTicks", fullAutoEnableStationaryTicks.ToString()), out n))
        fullAutoEnableStationaryTicks = n;
    if (int.TryParse(StateString(ini, "emma3MissionState", ((int)emma3MissionState).ToString()), out n) && n >= 0 && n <= (int)Emma3MissionState.WaitingMinerStart)
        emma3MissionState = (Emma3MissionState)n;
    if (int.TryParse(StateString(ini, "emma3RequestSequence", "0"), out n))
        emma3RequestSequence = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "emma3ResetSequence", "0"), out n))
        emma3ResetSequence = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "emma3CompletionSequence", "0"), out n))
        emma3CompletionSequence = Math.Max(0, n);
    long emma3LoadedEntity;
    if (long.TryParse(StateString(ini, "emma3ActiveEntityId", "0"), out emma3LoadedEntity))
        emma3ActiveEntityId = emma3LoadedEntity;
    emma3MissionId = StateString(ini, "emma3MissionId", emma3MissionId);
    if (int.TryParse(StateString(ini, "emma3CarrierRouteIndex", "0"), out n))
        emma3CarrierRouteIndex = Math.Max(0, n);
    emma3CarrierParkAligning = StateBool(ini, "emma3CarrierParkAligning", false);
    ActiveMiner().HangarStored = StateBool(ini, "minerHangarStored", ActiveMiner().HangarStored);
    ActiveMiner().HangarStorePendingForSnapshot = StateBool(ini, "minerHangarStorePendingForSnapshot", ActiveMiner().HangarStorePendingForSnapshot);
    ActiveMiner().HangarStoreCommandSent = StateBool(ini, "minerHangarStoreCommandSent", ActiveMiner().HangarStoreCommandSent);
    int loadedHangarPending;
    if (int.TryParse(StateString(ini, "minerHangarManagerPending", "-1"), out loadedHangarPending) && loadedHangarPending >= 0 && loadedHangarPending <= (int)HangarManagerPendingCommand.Disable)
    {
        ActiveMiner().HangarManagerPending = (HangarManagerPendingCommand)loadedHangarPending;
        if (int.TryParse(StateString(ini, "minerHangarManagerRequestId", "0"), out n))
            ActiveMiner().HangarManagerRequestId = Math.Max(0, n);
        if (int.TryParse(StateString(ini, "minerHangarManagerWaitTicks", "0"), out n))
            ActiveMiner().HangarManagerWaitTicks = Math.Max(0, n);
    }
    else
    {
        // Migrate the three pre-0.5.89 pending-command latches into one transaction.
        if (StateBool(ini, "minerHangarManagerEnablePending", false))
        {
            ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.Enable;
            if (int.TryParse(StateString(ini, "minerHangarManagerEnableRequestId", "0"), out n))
                ActiveMiner().HangarManagerRequestId = Math.Max(0, n);
        }
        else if (StateBool(ini, "minerHangarManagerStorePending", false))
        {
            ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.Store;
            if (int.TryParse(StateString(ini, "minerHangarManagerStoreRequestId", "0"), out n))
                ActiveMiner().HangarManagerRequestId = Math.Max(0, n);
        }
        else if (StateBool(ini, "minerHangarManagerDeployPending", false))
        {
            ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.Deploy;
            if (int.TryParse(StateString(ini, "minerHangarManagerDeployRequestId", "0"), out n))
                ActiveMiner().HangarManagerRequestId = Math.Max(0, n);
        }
    }

    if (int.TryParse(StateString(ini, "minerHangarOnlineWaitTicks", "0"), out n))
        ActiveMiner().HangarMinerOnlineWaitTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "minerHangarOperationId", "0"), out n))
        ActiveMiner().HangarOperationId = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "minerHangarPoseRequestId", "0"), out n))
        ActiveMiner().HangarPoseRequestId = Math.Max(0, n);
    ActiveMiner().HangarStoreCommandText = StateString(ini, "minerHangarStoreCommandText", "");
    if (emma3MissionState == Emma3MissionState.ReservedStartupHangarDeploy
        || emma3MissionState == Emma3MissionState.ReservedStartupHangarReturnDock)
    {
        // These persisted values belonged to the removed deploy-before-planning
        // startup path. Restart from normal connector/hangar discovery instead
        // of leaving an upgraded save in an unhandled state.
        emma3MissionState = Emma3MissionState.None;
        ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.None;
        ActiveMiner().HangarManagerRequestId = 0;
        ActiveMiner().HangarManagerWaitTicks = 0;
    }
    if (int.TryParse(StateString(ini, "manualHangarCycleState", "0"), out n) && n >= 0 && n <= (int)ManualHangarCycleState.WaitingDeployOnlyReturnDock)
        manualHangarCycleState = (ManualHangarCycleState)n;
    if (int.TryParse(StateString(ini, "manualHangarCycleDelayTicks", "0"), out n))
        manualHangarCycleDelayTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "manualHangarCycleCommandRetryTicks", "0"), out n))
        manualHangarCycleCommandRetryTicks = Math.Max(0, n);
    if (int.TryParse(StateString(ini, "manualHangarCycleCommandWaitTicks", "0"), out n))
        manualHangarCycleCommandWaitTicks = Math.Max(0, n);
    // v0.5.29 scopes the recall latch to a mission. Old persisted recall flags
    // have no mission ID and are discarded here, making Recall immediately
    // available in an already-running mission after upgrading.
    if (!RecallStateMatchesActiveMission())
        ResetMinerRecallState();
    LoadMiningReports(ini);
    RecoverPathRecordsAfterLoad();
    RecoverEmma3CarrierRouteAfterLoad();
    progressStateDirty = false;
}

void RecoverEmma3CarrierRouteAfterLoad()
{
    if (emma3MissionState != Emma3MissionState.FlyingCarrierRoute && emma3MissionState != Emma3MissionState.WaitingHangarStore)
        return;
    int savedIndex = emma3CarrierRouteIndex;
    bool savedParkAligning = emma3CarrierParkAligning;
    long savedEntityId = emma3ActiveEntityId;
    string savedMissionId = emma3MissionId;
    MyIni bridge;
    string bridgeMissionId = "";
    if (TryReadEmma3Bridge(out bridge))
        bridgeMissionId = bridge.Get("EMMA3", "missionId").ToString("");
    if (string.IsNullOrWhiteSpace(savedMissionId) || !string.Equals(savedMissionId, bridgeMissionId, StringComparison.Ordinal) || !LoadEmma3ReadyPlan(bridge) || emma3ActiveEntityId != savedEntityId)
    {
        emma3CarrierRoutePositions.Clear();
        emma3CarrierRouteForwards.Clear();
        emma3CarrierRouteUps.Clear();
        emma3CarrierRouteIndex = 0;
        emma3MissionState = Emma3MissionState.None;
        fullAutoEnabled = false;
        autopilotEnabled = false;
        hasTarget = false;
        ResetMovement();
        ScriptLog("EMM-A 3 reload stopped safely: authoritative Plugin Bridge route is unavailable or belongs to another mission.");
        return;
    }

    emma3CarrierRouteIndex = Math.Max(0, Math.Min(savedIndex, emma3CarrierRoutePositions.Count));
    emma3CarrierParkAligning = savedParkAligning && emma3CarrierRouteIndex == emma3CarrierRoutePositions.Count;
    RestoreEmma3CarrierParkFacing();
}

void RestoreEmma3CarrierParkFacing()
{
    if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute && emma3CarrierRoutePositions.Count > 0 && emma3CarrierRouteIndex == emma3CarrierRoutePositions.Count && hasTarget)
    {
        // Older saved missions may already be imposing the parking pose in flight.
        // Repair the target without waking a user-paused or cockpit-aborted mission.
        int last = emma3CarrierRoutePositions.Count - 1;
        targetPosition = emma3CarrierRoutePositions[last];
        desiredForward = emma3CarrierParkAligning ? emma3CarrierRouteForwards[last] : Vector3D.Zero;
        desiredUp = emma3CarrierParkAligning ? emma3CarrierRouteUps[last] : Vector3D.Zero;
        carrierAutopilotMode = CarrierAutopilotMode.Precision;
        BeginCarrierMotionSequence();
    }
}

void RecoverPathRecordsAfterLoad()
{
    if (!pathMode || pathRecords.Count > 0)
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
        pathMode = false;
        pathIndex = 0;
    }
}
// ---- Parse helpers ----

void LoadCarrierTurnCalibration(MyIni ini)
{
    double d;
    measuredFlipSeconds = 0; measuredFlipMass = 0; measuredFlipGyroAuthority = 0;
    if (TryParseNumber(StateString(ini, "measuredFlipSeconds", "0"), out d))
        measuredFlipSeconds = Math.Max(0, d);
    if (TryParseNumber(StateString(ini, "measuredFlipMass", "0"), out d))
        measuredFlipMass = Math.Max(0, d);
    if (TryParseNumber(StateString(ini, "measuredFlipGyroAuthority", "0"), out d))
        measuredFlipGyroAuthority = Math.Max(0, d);
}
