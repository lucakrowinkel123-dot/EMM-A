// Generated prototype: stable legacy indexes; retain original bounded save engine.
readonly string[] _minerSaveKeys = {"","scriptPaused","","","","","","","","","","","SPEEDF","SPEEDB","MINEH","MINEW","minerRouteStartExtra","mergeAssistedDocking","TOFFR","TOFFU","TOFFF","","pointToPointAccelerationLimit","pointToPointThrustUsePercent","activeMissionId","","","","","","","","","","","","","","","","","","","","","","","","pathCompleteBroadcastTicks","","","","","","","","","","","","","","","","","autoServiceReturnEnabled","autoServiceCargoTriggerPercent","autoServiceHydrogenTriggerPercent","","","","","","","","","autoServiceCheckTicks","","","","","","","","","","","","stoneEjectionHoldTicks","standardMineDepth","","","","","","","","","","","","oreTransferActive","oreTransferStartDelayTicks","minerUnloadSession","oreTransferSucceeded","","","",""};
bool OwnsSaveIndex(int index) { switch(index) {case 24:case 101:case 104:case 1:case 12:case 13:case 14:case 15:case 16:case 17:case 18:case 19:case 20:case 22:case 23:case 48:case 65:case 66:case 67:case 76:case 88:case 89:case 102:case 103:return true;default:return false;} }
void CaptureMinerSaveSnapshot(bool full = true) {
CaptureModuleState();
CaptureMinerSaveText(24, activeMissionId);
CaptureMinerSaveInteger(101, oreTransferActive ? 1 : 0);
CaptureMinerSaveBoolean(104, oreTransferSucceeded);
if(full) CaptureMinerSaveInteger(1, scriptPaused ? 1 : 0);
if(full) CaptureMinerSaveNumber(12, SPEEDF);
if(full) CaptureMinerSaveNumber(13, SPEEDB);
if(full) CaptureMinerSaveNumber(14, MINEH);
if(full) CaptureMinerSaveNumber(15, MINEW);
if(full) CaptureMinerSaveNumber(16, minerRouteStartExtra);
if(full) CaptureMinerSaveInteger(17, mergeAssistedDocking ? 1 : 0);
if(full) CaptureMinerSaveNumber(18, TOFFR);
if(full) CaptureMinerSaveNumber(19, TOFFU);
if(full) CaptureMinerSaveNumber(20, TOFFF);
if(full) CaptureMinerSaveNumber(22, pointToPointAccelerationLimit);
if(full) CaptureMinerSaveNumber(23, pointToPointThrustUsePercent);
if(full) CaptureMinerSaveInteger(48, pathCompleteBroadcastTicks);
if(full) CaptureMinerSaveInteger(65, autoServiceReturnEnabled ? 1 : 0);
if(full) CaptureMinerSaveNumber(66, autoServiceCargoTriggerPercent);
if(full) CaptureMinerSaveNumber(67, autoServiceHydrogenTriggerPercent);
if(full) CaptureMinerSaveInteger(76, autoServiceCheckTicks);
if(full) CaptureMinerSaveInteger(88, stoneEjectionHoldTicks);
if(full) CaptureMinerSaveNumber(89, standardMineDepth);
if(full) CaptureMinerSaveInteger(102, oreTransferStartDelayTicks);
if(full) CaptureMinerSaveInteger(103, minerUnloadSession);
}
void PumpMinerSaveWork(bool force = false)
{
    // Do not serialize in the same invocation that captured the state.
    if (!_minerSavePending || (!force && (runtime == _minerSaveCaptureTick || runtime == _minerSaveLastWorkTick)))
        return;
    int saveCount = _minerSaveFull ? _minerSaveKeys.Length : _minerProgressSaveIndexes.Length;
    if (!force && (_minerSaveIndex >= saveCount && (pathLoadJob != null || areaPlanActivationStage != 0 || IsUndockSequenceActive())))
        return;
    if (!force && (_minerSaveIndex >= saveCount && !TryClaimMinerHeavyWork()))
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
        StampMinerSaveGeneration(_minerPendingSaveIni, _minerSaveFull ? StateIniSection : ProgressIniSection, _minerSaveGeneration);
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
            if (timerPersistenceEnabled)
                return;
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
            else
                timer.CustomData = _minerSaveSerialized;
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
void SetPendingMinerSaveField(int index)
{
    if (!OwnsSaveIndex(index))
        return;
    MinerSaveValue value = _minerSaveValues[index];
    int planSlot = _minerSavePlanVectorSlots[index];
    if (!_minerSaveFull && planSlot >= 0 && _fullProgressVectorValid[planSlot] && value.Vector == _fullProgressVectors[planSlot])
        return;
    string section = _minerSaveFull ? StateIniSection : ProgressIniSection;
    string key = _minerSaveKeys[index];
    MinerSaveValue previous = _minerFormattedSaveValues[index];
    bool unchanged = _minerFormattedSaveText[index] != null && previous.Kind == value.Kind && (value.Kind == 0 || value.Kind == 5 ? previous.Integer == value.Integer : value.Kind == 1 ? previous.Number == value.Number : value.Kind == 2 || value.Kind == 3 ? previous.Vector == value.Vector : string.Equals(previous.Text, value.Text, StringComparison.Ordinal));
    if (!unchanged)
    {
        _minerFormattedSaveText[index] = value.Kind == 0 ? value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture) : value.Kind == 1 ? Number2(value.Number) : value.Kind == 2 ? V3ToString(value.Vector) : value.Kind == 3 ? DirectionToString(value.Vector) : value.Kind == 4 ? value.Text ?? "" : value.Integer != 0 ? "true" : "false";
        _minerFormattedSaveValues[index] = value;
    }

    MyIni destination = _minerSaveFull && IsMinerRouteSaveIndex(index) ? _minerPendingRouteIni : _minerPendingSaveIni;
    destination.Set(section, key, _minerFormattedSaveText[index]);
}
void SaveState() { CancelMinerSaveWork(); RequestMinerSave(true); while(_minerSavePending) PumpMinerSaveWork(true); }
void SaveStateProgressOnly() { CancelMinerSaveWork(); progressStateDirty=true; RequestMinerSave(false); while(_minerSavePending) PumpMinerSaveWork(true); }
void LoadOwnedScalarFields(MyIni ini) { int n; double d; Vector3D v; long emma3LoadedEntity; int loadedHangarPending; bool legacyHangarRouteDeparture=false;
scriptPaused = StateBool(ini, _minerSaveKeys[1], scriptPaused);
if (TryParseNumber(StateString(ini, _minerSaveKeys[12], NumberR(SPEEDF)), out d))
    SPEEDF = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[13], NumberR(SPEEDB)), out d))
    SPEEDB = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[14], NumberR(MINEH)), out d))
    MINEH = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[15], NumberR(MINEW)), out d))
    MINEW = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[16], NumberR(minerRouteStartExtra)), out d))
    minerRouteStartExtra = MathHelper.Clamp(d, 0, 500);
mergeAssistedDocking = StateBool(ini, _minerSaveKeys[17], mergeAssistedDocking);
if (TryParseNumber(StateString(ini, _minerSaveKeys[18], NumberR(TOFFR)), out d))
    TOFFR = d;
if (TryParseNumber(StateString(ini, _minerSaveKeys[19], NumberR(TOFFU)), out d))
    TOFFU = d;
if (TryParseNumber(StateString(ini, _minerSaveKeys[20], NumberR(TOFFF)), out d))
    TOFFF = d;
if (TryParseNumber(StateString(ini, _minerSaveKeys[22], NumberR(pointToPointAccelerationLimit)), out d))
    pointToPointAccelerationLimit = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[23], NumberR(pointToPointThrustUsePercent)), out d))
    pointToPointThrustUsePercent = MathHelper.Clamp(d, 1, 100);
activeMissionId = StateString(ini, _minerSaveKeys[24], activeMissionId);
oreTransferSucceeded = StateBool(ini, _minerSaveKeys[104], false);
if (int.TryParse(StateString(ini, _minerSaveKeys[48], pathCompleteBroadcastTicks.ToString()), out n))
    pathCompleteBroadcastTicks = n;
autoServiceReturnEnabled = StateBool(ini, _minerSaveKeys[65], autoServiceReturnEnabled);
if (TryParseNumber(StateString(ini, _minerSaveKeys[66], NumberR(autoServiceCargoTriggerPercent)), out d))
    autoServiceCargoTriggerPercent = MathHelper.Clamp(d, 0, 100);
if (TryParseNumber(StateString(ini, _minerSaveKeys[67], NumberR(autoServiceHydrogenTriggerPercent)), out d))
    autoServiceHydrogenTriggerPercent = MathHelper.Clamp(d, 0, 100);
if (int.TryParse(StateString(ini, _minerSaveKeys[76], autoServiceCheckTicks.ToString()), out n))
    autoServiceCheckTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[88], stoneEjectionHoldTicks.ToString()), out n))
    stoneEjectionHoldTicks = Math.Max(0, n);
if (TryParseNumber(StateString(ini, _minerSaveKeys[89], StateString(ini, "pathRecordMineDepth", NumberR(standardMineDepth))), out d))
    standardMineDepth = d;
oreTransferActive = StateBool(ini, _minerSaveKeys[101], oreTransferActive);
long.TryParse(StateString(ini, _minerSaveKeys[103], "0"), out minerUnloadSession);
if (int.TryParse(StateString(ini, _minerSaveKeys[102], oreTransferStartDelayTicks.ToString()), out n))
    oreTransferStartDelayTicks = Math.Max(0, n);
}
