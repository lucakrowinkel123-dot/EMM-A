// Generated prototype: stable legacy indexes; retain original bounded save engine.
readonly string[] _carrierSaveKeys = {"","scriptPaused","","cockpitAbortPaused","cockpitAbortResumeAutopilot","","","","","","","","","","","","","","","","maxSpeed","orientationToleranceDeg","arrivalTolerance","shortRangeDistanceMeters","cruiseOffsetMeters","thrustLimitKn","thrustUsePercent","caThrustStepIndex","accelerationLimit","turnTestEnabled","lcdRefreshIntervalTicks","","","","","","","","","minerPathCompleteReceived","minerPathCompleteKind","mineRecallRequested","mineRecallActive","mineRecallAcknowledged","mineRecallMissionId","mineRecallFailedTicks","mineStationaryTicks","fullAutoEnabled","emma3IceReserveEnabled","emma3IceReserveMillionLiters","emma3ItemsMode","emma3EnabledItems","emma3AllowSeatExitDuringMining","emma3WaitForSeatBeforeNextPlan","fullAutoEnableStationaryTicks","emma3MissionState","emma3RequestSequence","emma3ResetSequence","emma3CompletionSequence","emma3ActiveEntityId","emma3MissionId","","","minerHangarStored","minerHangarStorePendingForSnapshot","minerHangarStoreCommandSent","minerHangarManagerPending","minerHangarManagerRequestId","minerHangarManagerWaitTicks","minerHangarOnlineWaitTicks","minerHangarOperationId","manualHangarCycleState","manualHangarCycleDelayTicks","manualHangarCycleCommandRetryTicks","manualHangarCycleCommandWaitTicks","reportMission","reportAsteroid","reportCompleted","reportVisible","reportFrozen","finishAndHold","asteroidReport","miningTotals","reportApproachSeconds","reportPark","reportHasBox","reportBoxMin","reportBoxMax","reportTimingVersion","reportParked","reportCompletionCounted","","minerHangarPoseRequestId","minerHangarStoreCommandText"};
bool OwnsSaveIndex(int index) { switch(index) {case 39:case 40:case 41:case 42:case 43:case 44:case 47:case 52:case 53:case 55:case 56:case 57:case 58:case 59:case 60:case 63:case 64:case 65:case 66:case 67:case 68:case 69:case 70:case 92:case 93:case 71:case 72:case 73:case 74:case 75:case 76:case 77:case 78:case 79:case 80:case 81:case 82:case 83:case 84:case 85:case 86:case 87:case 88:case 89:case 90:case 1:case 3:case 4:case 20:case 21:case 22:case 23:case 24:case 25:case 26:case 27:case 28:case 29:case 30:case 45:case 46:case 48:case 49:case 50:case 51:case 54:return true;default:return false;} }
void CaptureCarrierSaveSnapshot(bool full = true) {
CaptureModuleState();
MinerSlot miner=ActiveMiner();
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
if(full) CaptureCarrierSave(1, scriptPaused ? "1" : "0");
if(full) CaptureCarrierSave(3, cockpitAbortPaused ? "1" : "0");
if(full) CaptureCarrierSave(4, cockpitAbortResumeAutopilot ? "1" : "0");
if(full) CaptureCarrierSave(20, maxSpeed, 1);
if(full) CaptureCarrierSave(21, orientationToleranceDeg, 1);
if(full) CaptureCarrierSave(22, arrivalTolerance, 1);
if(full) CaptureCarrierSave(23, shortRangeDistanceMeters, 1);
if(full) CaptureCarrierSave(24, cruiseOffsetMeters, 1);
if(full) CaptureCarrierSave(25, thrustLimitKn, 1);
if(full) CaptureCarrierSave(26, thrustUsePercent, 1);
if(full) CaptureCarrierSave(27, caThrustStepIndex);
if(full) CaptureCarrierSave(28, accelerationLimit, 1);
if(full) CaptureCarrierSave(29, turnTestEnabled ? "1" : "0");
if(full) CaptureCarrierSave(30, lcdRefreshIntervalTicks);
if(full) CaptureCarrierSave(45, miner.RecallFailedTicks);
if(full) CaptureCarrierSave(46, mineStationaryTicks);
if(full) CaptureCarrierSave(48, emma3IceReserveEnabled);
if(full) CaptureCarrierSave(49, emma3IceReserveMillionLiters, 8);
if(full) CaptureCarrierSave(50, emma3ItemsMode);
if(full) CaptureCarrierSave(51, emma3EnabledItems);
if(full) CaptureCarrierSave(54, fullAutoEnableStationaryTicks);
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
scriptPaused = StateBool(ini, _carrierSaveKeys[1], scriptPaused);
cockpitAbortPaused = StateBool(ini, _carrierSaveKeys[3], cockpitAbortPaused);
cockpitAbortResumeAutopilot = StateBool(ini, _carrierSaveKeys[4], cockpitAbortResumeAutopilot);
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
if (int.TryParse(StateString(ini, _carrierSaveKeys[27], caThrustStepIndex.ToString()), out n))
    caThrustStepIndex = Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, n));
if (TryParseNumber(StateString(ini, _carrierSaveKeys[28], NumberR(accelerationLimit)), out d))
    accelerationLimit = Math.Max(0.1, d);
turnTestEnabled = StateBool(ini, _carrierSaveKeys[29], turnTestEnabled);
if (int.TryParse(StateString(ini, _carrierSaveKeys[30], lcdRefreshIntervalTicks.ToString()), out n))
    lcdRefreshIntervalTicks = Math.Max(lcdRefreshMinTicks, n);
ActiveMiner().PathCompleteReceived = StateBool(ini, _carrierSaveKeys[39], ActiveMiner().PathCompleteReceived);
ActiveMiner().PathCompleteKind = StateString(ini, _carrierSaveKeys[40], ActiveMiner().PathCompleteKind);
ActiveMiner().RecallRequested = StateBool(ini, _carrierSaveKeys[41], ActiveMiner().RecallRequested);
ActiveMiner().RecallActive = StateBool(ini, _carrierSaveKeys[42], ActiveMiner().RecallActive);
ActiveMiner().RecallAcknowledged = StateBool(ini, _carrierSaveKeys[43], ActiveMiner().RecallAcknowledged);
ActiveMiner().RecallMissionId = StateString(ini, _carrierSaveKeys[44], ActiveMiner().RecallMissionId);
if (int.TryParse(StateString(ini, _carrierSaveKeys[45], ActiveMiner().RecallFailedTicks.ToString()), out n))
    ActiveMiner().RecallFailedTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[46], mineStationaryTicks.ToString()), out n))
    mineStationaryTicks = n;
fullAutoEnabled = StateBool(ini, _carrierSaveKeys[47], fullAutoEnabled);
emma3IceReserveEnabled = StateBool(ini, _carrierSaveKeys[48], false);
if (TryParseNumber(StateString(ini, _carrierSaveKeys[49], "1"), out d))
    emma3IceReserveMillionLiters = ClampIceReserveMillionLiters(d);
emma3ItemsMode = StateBool(ini, _carrierSaveKeys[50], false);
emma3EnabledItems = StateString(ini, _carrierSaveKeys[51], "");
emma3AllowSeatExitDuringMining = StateBool(ini, _carrierSaveKeys[52], emma3AllowSeatExitDuringMining);
emma3WaitForSeatBeforeNextPlan = StateBool(ini, _carrierSaveKeys[53], emma3WaitForSeatBeforeNextPlan);
if (int.TryParse(StateString(ini, _carrierSaveKeys[54], fullAutoEnableStationaryTicks.ToString()), out n))
    fullAutoEnableStationaryTicks = n;
if (int.TryParse(StateString(ini, _carrierSaveKeys[55], ((int)emma3MissionState).ToString()), out n) && n >= 0 && n <= (int)Emma3MissionState.WaitingMinerStart)
    emma3MissionState = (Emma3MissionState)n;
if (int.TryParse(StateString(ini, _carrierSaveKeys[56], "0"), out n))
    emma3RequestSequence = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[57], "0"), out n))
    emma3ResetSequence = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[58], "0"), out n))
    emma3CompletionSequence = Math.Max(0, n);
if (long.TryParse(StateString(ini, _carrierSaveKeys[59], "0"), out emma3LoadedEntity))
    emma3ActiveEntityId = emma3LoadedEntity;
emma3MissionId = StateString(ini, _carrierSaveKeys[60], emma3MissionId);
ActiveMiner().HangarStored = StateBool(ini, _carrierSaveKeys[63], ActiveMiner().HangarStored);
ActiveMiner().HangarStorePendingForSnapshot = StateBool(ini, _carrierSaveKeys[64], ActiveMiner().HangarStorePendingForSnapshot);
ActiveMiner().HangarStoreCommandSent = StateBool(ini, _carrierSaveKeys[65], ActiveMiner().HangarStoreCommandSent);
if (int.TryParse(StateString(ini, _carrierSaveKeys[66], "-1"), out loadedHangarPending) && loadedHangarPending >= 0 && loadedHangarPending <= (int)HangarManagerPendingCommand.Disable)
{
    ActiveMiner().HangarManagerPending = (HangarManagerPendingCommand)loadedHangarPending;
    if (int.TryParse(StateString(ini, _carrierSaveKeys[67], "0"), out n))
        ActiveMiner().HangarManagerRequestId = Math.Max(0, n);
    if (int.TryParse(StateString(ini, _carrierSaveKeys[68], "0"), out n))
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
if (int.TryParse(StateString(ini, _carrierSaveKeys[69], "0"), out n))
    ActiveMiner().HangarMinerOnlineWaitTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[70], "0"), out n))
    ActiveMiner().HangarOperationId = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[92], "0"), out n))
    ActiveMiner().HangarPoseRequestId = Math.Max(0, n);
ActiveMiner().HangarStoreCommandText = StateString(ini, _carrierSaveKeys[93], "");
if (int.TryParse(StateString(ini, _carrierSaveKeys[71], "0"), out n) && n >= 0 && n <= (int)ManualHangarCycleState.WaitingDeployOnlyReturnDock)
    manualHangarCycleState = (ManualHangarCycleState)n;
if (int.TryParse(StateString(ini, _carrierSaveKeys[72], "0"), out n))
    manualHangarCycleDelayTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[73], "0"), out n))
    manualHangarCycleCommandRetryTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _carrierSaveKeys[74], "0"), out n))
    manualHangarCycleCommandWaitTicks = Math.Max(0, n);
}
