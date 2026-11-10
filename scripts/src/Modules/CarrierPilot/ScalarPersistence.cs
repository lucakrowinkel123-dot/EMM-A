// Generated prototype: stable legacy indexes; retain original bounded save engine.
readonly string[] _carrierSaveKeys = {"autopilotEnabled","scriptPaused","pauseResumeAutopilot","","","hasTarget","targetPosition","desiredForward","desiredUp","activeMaxSpeed","carrierAutopilotMode","cruiseFinalPosition","cruiseFinalForward","cruiseFinalUp","cruiseStagingPosition","cruiseDirection","cruiseTurnTestStartForward","cruiseTurnTestTicks","measuredFlipSeconds","measuredFlipMass","maxSpeed","orientationToleranceDeg","arrivalTolerance","shortRangeDistanceMeters","cruiseOffsetMeters","thrustLimitKn","thrustUsePercent","","accelerationLimit","turnTestEnabled","","pathMode","pathPaused","pathPausedNavResume","pathIndex","pathAnchorValid","pathAnchorPos","pathAnchorForward","pathAnchorUp","","","","","","","","","","","","","","","","","","","","","emma3ActiveEntityId","emma3MissionId","emma3CarrierRouteIndex","emma3CarrierParkAligning","","","","","","","","","","","","","","","","","","","","","","","","","","","","","measuredFlipGyroAuthority","",""};
bool OwnsSaveIndex(int index) { switch(index) {case 0:case 5:case 6:case 7:case 8:case 9:case 10:case 11:case 12:case 13:case 14:case 15:case 16:case 17:case 31:case 32:case 34:case 35:case 59:case 60:case 61:case 62:case 1:case 2:case 18:case 19:case 91:case 20:case 21:case 22:case 23:case 24:case 25:case 26:case 28:case 29:case 33:case 36:case 37:case 38:return true;default:return false;} }
void CaptureCarrierSaveSnapshot(bool full = true) {
CaptureModuleState();
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
CaptureCarrierSave(59, emma3ActiveEntityId);
CaptureCarrierSave(60, emma3MissionId);
CaptureCarrierSave(61, emma3CarrierRouteIndex);
CaptureCarrierSave(62, emma3CarrierParkAligning ? "1" : "0");
if(full) CaptureCarrierSave(1, scriptPaused ? "1" : "0");
if(full) CaptureCarrierSave(2, pauseResumeAutopilot ? "1" : "0");
if(full) CaptureCarrierSave(18, measuredFlipSeconds, 7);
if(full) CaptureCarrierSave(19, measuredFlipMass, 1);
if(full) CaptureCarrierSave(91, measuredFlipGyroAuthority, 7);
if(full) CaptureCarrierSave(20, maxSpeed, 1);
if(full) CaptureCarrierSave(21, orientationToleranceDeg, 1);
if(full) CaptureCarrierSave(22, arrivalTolerance, 1);
if(full) CaptureCarrierSave(23, shortRangeDistanceMeters, 1);
if(full) CaptureCarrierSave(24, cruiseOffsetMeters, 1);
if(full) CaptureCarrierSave(25, thrustLimitKn, 1);
if(full) CaptureCarrierSave(26, thrustUsePercent, 1);
if(full) CaptureCarrierSave(28, accelerationLimit, 1);
if(full) CaptureCarrierSave(29, turnTestEnabled ? "1" : "0");
if(full) CaptureCarrierSave(33, pathPausedNavResume ? "1" : "0");
if(full) CaptureCarrierSave(36, pathAnchorPos, false);
if(full) CaptureCarrierSave(37, pathAnchorForward, true);
if(full) CaptureCarrierSave(38, pathAnchorUp, true);
}
void PumpCarrierSaveWork(bool force = false)
{
    if (!_carrierSavePending || (!force && (runtime == _carrierSaveLastWorkTick)))
        return;
    if (!force && (carrierPlanLoadJob != null))
        return;
    if (!force && ((_carrierSaveStage == 2 || _carrierSaveStage == 3 || _carrierSaveStage == 5 || _carrierSaveStage == 6) && !TryClaimCarrierHeavyWork()))
        return;
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
        if (_carrierSaveClearIndex == _carrierPreviousSaveKeys.Count)
            _carrierSaveStage = 1;
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
            if (!SetPendingCarrierSaveField(index))
                return;
            _carrierSaveIndex++;
            if (_carrierSaveValues[index].Kind == 6)
                return;
        }

        if (_carrierSaveIndex == count)
            _carrierSaveStage = 2;
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
                if (slot < 0)
                    continue;
                _carrierProgressVectors[slot] = _carrierSaveValues[i].Vector;
                _carrierProgressVectorValid[slot] = true;
            }

            _hasProgressState = false;
            _progressIni.Clear();
            _carrierSavePublishStage = 1;
            _carrierCommittedRevision = Math.Max(_carrierCommittedRevision, _carrierSaveRevision);
            if (timerPersistenceEnabled)
                return;
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
            else
                timer.CustomData = _carrierSaveSerialized;
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
bool SetPendingCarrierSaveField(int index)
{
    if (!OwnsSaveIndex(index))
        return true;
    CarrierSaveValue value = _carrierSaveValues[index];
    int slot = _carrierSaveVectorSlots[index];
    if (!_carrierSaveFull && slot >= 0 && _carrierProgressVectorValid[slot] && value.Vector == _carrierProgressVectors[slot])
        return true;
    string text;
    if (value.Kind == 6)
    {
        if (!value.Report.Prepare(carrierSaveFieldsPerTick))
            return false;
        text = value.Report.Text;
    }
    else
    {
        CarrierSaveValue previous = _carrierFormattedValues[index];
        bool unchanged = _carrierFormattedText[index] != null && previous.Kind == value.Kind && (value.Kind == 0 || value.Kind == 5 ? previous.Integer == value.Integer : value.Kind == 1 || value.Kind == 7 || value.Kind == 8 ? previous.Number == value.Number : value.Kind == 2 || value.Kind == 3 ? previous.Vector == value.Vector : string.Equals(previous.Text, value.Text, StringComparison.Ordinal));
        if (!unchanged)
        {
            _carrierFormattedText[index] = value.Kind == 0 ? value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture) : value.Kind == 1 ? Number2(value.Number) : value.Kind == 7 ? Number3(value.Number) : value.Kind == 8 ? NumberR(value.Number) : value.Kind == 2 ? V3ToString(value.Vector) : value.Kind == 3 ? DirectionToString(value.Vector) : value.Kind == 4 ? value.Text ?? "" : value.Integer != 0 ? "true" : "false";
            _carrierFormattedValues[index] = value;
        }

        text = _carrierFormattedText[index];
    }

    MyIni destination = _carrierSaveFull ? CarrierFullSaveDestination(index) : _carrierPendingSaveIni;
    destination.Set(_carrierSaveFull ? StateIniSection : ProgressIniSection, _carrierSaveKeys[index], text);
    return true;
}
void SaveState() { CancelCarrierSaveWork(); RequestCarrierSave(true); while(_carrierSavePending) PumpCarrierSaveWork(true); }
void SaveStateProgressOnly() { CancelCarrierSaveWork(); progressStateDirty=true; RequestCarrierSave(false); while(_carrierSavePending) PumpCarrierSaveWork(true); }
void LoadOwnedScalarFields(MyIni ini) { int n; double d; Vector3D v; long emma3LoadedEntity; int loadedHangarPending; bool legacyHangarRouteDeparture=false;
autopilotEnabled = StateBool(ini, _carrierSaveKeys[0], autopilotEnabled);
scriptPaused = StateBool(ini, _carrierSaveKeys[1], scriptPaused);
pauseResumeAutopilot = StateBool(ini, _carrierSaveKeys[2], pauseResumeAutopilot);
hasTarget = StateBool(ini, _carrierSaveKeys[5], hasTarget);
if (TryParseV3(StateString(ini, _carrierSaveKeys[6], V3ToString(targetPosition)), out v))
    targetPosition = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[7], V3ToString(desiredForward)), out v))
    desiredForward = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[8], V3ToString(desiredUp)), out v))
    desiredUp = v;
if (TryParseNumber(StateString(ini, _carrierSaveKeys[9], NumberR(activeMaxSpeed)), out d))
    activeMaxSpeed = d;
if (int.TryParse(StateString(ini, _carrierSaveKeys[10], ((int)carrierAutopilotMode).ToString()), out n))
{
    if (n < 0 || n > (int)CarrierAutopilotMode.CruiseDecel)
        n = 0;
    carrierAutopilotMode = (CarrierAutopilotMode)n;
}
if (TryParseV3(StateString(ini, _carrierSaveKeys[11], V3ToString(cruiseFinalPosition)), out v))
    cruiseFinalPosition = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[12], V3ToString(cruiseFinalForward)), out v))
    cruiseFinalForward = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[13], V3ToString(cruiseFinalUp)), out v))
    cruiseFinalUp = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[14], V3ToString(cruiseStagingPosition)), out v))
    cruiseStagingPosition = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[15], V3ToString(cruiseDirection)), out v))
    cruiseDirection = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[16], V3ToString(cruiseTurnTestStartForward)), out v))
    cruiseTurnTestStartForward = v;
if (int.TryParse(StateString(ini, _carrierSaveKeys[17], cruiseTurnTestTicks.ToString()), out n))
    cruiseTurnTestTicks = Math.Max(0, n);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[20], NumberR(maxSpeed)), out d))
    maxSpeed = MathHelper.Clamp(d, 1, 50000);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[21], NumberR(orientationToleranceDeg)), out d))
    orientationToleranceDeg = d;
if (TryParseNumber(StateString(ini, _carrierSaveKeys[22], NumberR(arrivalTolerance)), out d))
    arrivalTolerance = d;
if (TryParseNumber(StateString(ini, _carrierSaveKeys[23], NumberR(shortRangeDistanceMeters)), out d))
    shortRangeDistanceMeters = Math.Max(100, d);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[24], NumberR(cruiseOffsetMeters)), out d))
    cruiseOffsetMeters = Math.Max(100, d);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[25], NumberR(thrustLimitKn)), out d))
    thrustLimitKn = Math.Max(1, d);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[26], NumberR(thrustUsePercent)), out d))
    thrustUsePercent = MathHelper.Clamp(d, 1, 100);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[28], NumberR(accelerationLimit)), out d))
    accelerationLimit = Math.Max(0.1, d);
turnTestEnabled = StateBool(ini, _carrierSaveKeys[29], turnTestEnabled);
pathMode = StateBool(ini, _carrierSaveKeys[31], pathMode);
pathPaused = StateBool(ini, _carrierSaveKeys[32], pathPaused);
pathPausedNavResume = StateBool(ini, _carrierSaveKeys[33], pathPausedNavResume);
if (int.TryParse(StateString(ini, _carrierSaveKeys[34], pathIndex.ToString()), out n))
    pathIndex = n;
pathAnchorValid = StateBool(ini, _carrierSaveKeys[35], pathAnchorValid);
if (TryParseV3(StateString(ini, _carrierSaveKeys[36], V3ToString(pathAnchorPos)), out v))
    pathAnchorPos = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[37], V3ToString(pathAnchorForward)), out v))
    pathAnchorForward = v;
if (TryParseV3(StateString(ini, _carrierSaveKeys[38], V3ToString(pathAnchorUp)), out v))
    pathAnchorUp = v;
if (long.TryParse(StateString(ini, _carrierSaveKeys[59], "0"), out emma3LoadedEntity))
    emma3ActiveEntityId = emma3LoadedEntity;
emma3MissionId = StateString(ini, _carrierSaveKeys[60], emma3MissionId);
if (int.TryParse(StateString(ini, _carrierSaveKeys[61], "0"), out n))
    emma3CarrierRouteIndex = Math.Max(0, n);
emma3CarrierParkAligning = StateBool(ini, _carrierSaveKeys[62], false);
}
