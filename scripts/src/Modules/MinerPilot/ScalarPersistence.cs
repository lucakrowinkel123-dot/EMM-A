// Generated prototype: stable legacy indexes; retain original bounded save engine.
readonly string[] _minerSaveKeys = {"autopilotEnabled","scriptPaused","pauseResumeAutopilot","hasTarget","targetPosition","desiredForward","desiredUp","miningEnabled","miningState","laneAnchor","drillAxis","laneDepth","SPEEDF","SPEEDB","MINEH","MINEW","minerRouteStartExtra","mergeAssistedDocking","TOFFR","TOFFU","TOFFF","activeMaxSpeed","pointToPointAccelerationLimit","pointToPointThrustUsePercent","activeMissionId","areaMiningMode","areaLaneIndex","areaPlanValid","areaPlanCornerA","areaPlanCornerB","areaPlanUp","areaPlanForward","returnToMinePoseAfterArea","finishAreaAfterBackout","areaMineEntryPos","areaMineEntryForward","areaMineEntryUp","activeMineAreaRecordIndex","retryLane","stuckBackout","stuckRetryCount","areaLaneCount","pathMode","pathPaused","pathPausedNavResume","pathDrillApproachActive","pathDockPending","pathIndex","","routeProgressMeters","routeProgressTotalMeters","routeProgressPreviousPosition","routeProgressPreviousValid","routeProgressTracking","pathAnchorValid","pathAnchorPos","pathAnchorForward","pathAnchorUp","recallActive","recallWaitingForAreaExit","recallWaitingForDock","recallReverseMode","recallIndex","recallLastDone","recallLastTotal","","","","autoServiceState","autoServiceReason","autoServiceResumeMineArea","autoServiceResumeMineAreaRecordIndex","autoServiceResumeAreaLaneIndex","autoServiceResumeMineEntryPos","autoServiceResumeMineEntryForward","autoServiceResumeMineEntryUp","","autoServiceActionRetryTicks","autoServiceReturnRouteIndex","hangarState","hangarTargetPosition","hangarTargetForward","hangarTargetUp","hangarOperationId","hangarPhaseElapsedTicks","hangarLaunchUndocked","stoneEjectionHoldActive","stoneEjectionResumeAutopilot","","standardMineDepth","dockingState","undockRecompileTicksRemaining","dockMergeReadyTicks","dockConnectRetryTicks","dockConnectRecompileTicksRemaining","dockConnectRecompileArmed","dockRetryPending","dockRetryTicks","dockingPhaseElapsedTicks","dockingAttemptFailures","connectorOffsetLocal","","","","","minerDockConnectorId","minerDockSession","pendingUndock","minerPowerGoal"};
bool OwnsSaveIndex(int index) { switch(index) {case 0:case 3:case 4:case 5:case 6:case 7:case 8:case 9:case 10:case 11:case 21:case 24:case 25:case 26:case 27:case 28:case 29:case 30:case 31:case 32:case 33:case 34:case 35:case 36:case 37:case 38:case 39:case 40:case 41:case 42:case 43:case 45:case 46:case 47:case 54:case 58:case 59:case 60:case 61:case 62:case 68:case 70:case 71:case 72:case 73:case 74:case 75:case 78:case 79:case 83:case 84:case 85:case 86:case 87:case 90:case 96:case 98:case 99:case 105:case 106:case 107:case 108:case 1:case 2:case 12:case 13:case 14:case 15:case 16:case 17:case 18:case 19:case 20:case 22:case 23:case 44:case 49:case 50:case 51:case 52:case 53:case 55:case 56:case 57:case 63:case 64:case 69:case 77:case 80:case 81:case 82:case 89:case 91:case 92:case 93:case 94:case 95:case 97:case 100:return true;default:return false;} }
void CaptureMinerSaveSnapshot(bool full = true) {
CaptureModuleState();
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
CaptureMinerSaveInteger(105, minerDockConnectorId);
CaptureMinerSaveInteger(106, minerDockSession);
CaptureMinerSaveBoolean(107, pendingUndock);
CaptureMinerSaveInteger(108, minerPowerGoal);
if(full) CaptureMinerSaveInteger(1, scriptPaused ? 1 : 0);
if(full) CaptureMinerSaveInteger(2, pauseResumeAutopilot ? 1 : 0);
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
if(full) CaptureMinerSaveInteger(44, pathPausedNavResume ? 1 : 0);
if(full) CaptureMinerSaveNumber(49, routeProgressMeters);
if(full) CaptureMinerSaveNumber(50, routeProgressTotalMeters);
if(full) CaptureMinerSaveVector(51, routeProgressPreviousPosition, false);
if(full) CaptureMinerSaveInteger(52, routeProgressPreviousValid ? 1 : 0);
if(full) CaptureMinerSaveInteger(53, routeProgressTracking ? 1 : 0);
if(full) CaptureMinerSaveVector(55, pathAnchorPos, false);
if(full) CaptureMinerSaveVector(56, pathAnchorForward, true);
if(full) CaptureMinerSaveVector(57, pathAnchorUp, true);
if(full) CaptureMinerSaveInteger(63, recallLastDone);
if(full) CaptureMinerSaveInteger(64, recallLastTotal);
if(full) CaptureMinerSaveText(69, autoServiceReason);
if(full) CaptureMinerSaveInteger(77, autoServiceActionRetryTicks);
if(full) CaptureMinerSaveVector(80, hangarTargetPosition, false);
if(full) CaptureMinerSaveVector(81, hangarTargetForward, true);
if(full) CaptureMinerSaveVector(82, hangarTargetUp, true);
if(full) CaptureMinerSaveNumber(89, standardMineDepth);
if(full) CaptureMinerSaveInteger(91, undockRecompileTicksRemaining);
if(full) CaptureMinerSaveInteger(92, dockMergeReadyTicks);
if(full) CaptureMinerSaveInteger(93, dockConnectRetryTicks);
if(full) CaptureMinerSaveInteger(94, dockConnectRecompileTicksRemaining);
if(full) CaptureMinerSaveInteger(95, dockConnectRecompileArmed ? 1 : 0);
if(full) CaptureMinerSaveInteger(97, dockRetryTicks);
if(full) CaptureMinerSaveVector(100, connectorOffsetLocal, false);
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
autopilotEnabled = StateBool(ini, _minerSaveKeys[0], autopilotEnabled);
scriptPaused = StateBool(ini, _minerSaveKeys[1], scriptPaused);
pauseResumeAutopilot = StateBool(ini, _minerSaveKeys[2], pauseResumeAutopilot);
hasTarget = StateBool(ini, _minerSaveKeys[3], hasTarget);
if (TryParseV3(StateString(ini, _minerSaveKeys[4], V3ToString(targetPosition)), out v))
    targetPosition = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[5], V3ToString(desiredForward)), out v))
    desiredForward = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[6], V3ToString(desiredUp)), out v))
    desiredUp = v;
miningEnabled = StateBool(ini, _minerSaveKeys[7], miningEnabled);
if (int.TryParse(StateString(ini, _minerSaveKeys[8], ((int)miningState).ToString()), out n))
    miningState = (MiningState)n;
if (TryParseV3(StateString(ini, _minerSaveKeys[9], V3ToString(laneAnchor)), out v))
    laneAnchor = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[10], V3ToString(drillAxis)), out v))
    drillAxis = v;
if (TryParseNumber(StateString(ini, _minerSaveKeys[11], NumberR(laneDepth)), out d))
    laneDepth = d;
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
if (TryParseNumber(StateString(ini, _minerSaveKeys[21], NumberR(activeMaxSpeed)), out d))
    activeMaxSpeed = d;
if (TryParseNumber(StateString(ini, _minerSaveKeys[22], NumberR(pointToPointAccelerationLimit)), out d))
    pointToPointAccelerationLimit = Math.Max(0.1, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[23], NumberR(pointToPointThrustUsePercent)), out d))
    pointToPointThrustUsePercent = MathHelper.Clamp(d, 1, 100);
activeMissionId = StateString(ini, _minerSaveKeys[24], activeMissionId);
areaMiningMode = StateBool(ini, _minerSaveKeys[25], areaMiningMode);
if (int.TryParse(StateString(ini, _minerSaveKeys[26], areaLaneIndex.ToString()), out n))
    areaLaneIndex = n;
areaPlanValid = StateBool(ini, _minerSaveKeys[27], areaPlanValid);
if (TryParseV3(StateString(ini, _minerSaveKeys[28], V3ToString(areaPlanCornerA)), out v))
    areaPlanCornerA = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[29], V3ToString(areaPlanCornerB)), out v))
    areaPlanCornerB = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[30], V3ToString(areaPlanUp)), out v))
    areaPlanUp = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[31], V3ToString(areaPlanForward)), out v))
    areaPlanForward = v;
returnToMinePoseAfterArea = StateBool(ini, _minerSaveKeys[32], returnToMinePoseAfterArea);
finishAreaAfterBackout = StateBool(ini, _minerSaveKeys[33], finishAreaAfterBackout);
if (TryParseV3(StateString(ini, _minerSaveKeys[34], V3ToString(areaMineEntryPos)), out v))
    areaMineEntryPos = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[35], V3ToString(areaMineEntryForward)), out v))
    areaMineEntryForward = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[36], V3ToString(areaMineEntryUp)), out v))
    areaMineEntryUp = v;
if (int.TryParse(StateString(ini, _minerSaveKeys[37], activeMineAreaRecordIndex.ToString()), out n))
    activeMineAreaRecordIndex = n;
retryCurrentLane = StateBool(ini, _minerSaveKeys[38], retryCurrentLane);
stuckBackout = StateBool(ini, _minerSaveKeys[39], stuckBackout);
if (int.TryParse(StateString(ini, _minerSaveKeys[40], stuckRetryCount.ToString()), out n))
    stuckRetryCount = n;
if (int.TryParse(StateString(ini, _minerSaveKeys[41], "0"), out n))
    areaLaneCount = Math.Max(0, n);
pathMode = StateBool(ini, _minerSaveKeys[42], pathMode);
pathPaused = StateBool(ini, _minerSaveKeys[43], pathPaused);
pathPausedNavResume = StateBool(ini, _minerSaveKeys[44], pathPausedNavResume);
pathDrillApproachActive = StateBool(ini, _minerSaveKeys[45], pathDrillApproachActive);
pathDockPending = StateBool(ini, _minerSaveKeys[46], pathDockPending);
if (int.TryParse(StateString(ini, _minerSaveKeys[47], pathIndex.ToString()), out n))
    pathIndex = n;
if (TryParseNumber(StateString(ini, _minerSaveKeys[49], NumberR(routeProgressMeters)), out d))
    routeProgressMeters = Math.Max(0, d);
if (TryParseNumber(StateString(ini, _minerSaveKeys[50], NumberR(routeProgressTotalMeters)), out d))
    routeProgressTotalMeters = Math.Max(0, d);
if (TryParseV3(StateString(ini, _minerSaveKeys[51], V3ToString(routeProgressPreviousPosition)), out v))
    routeProgressPreviousPosition = v;
routeProgressPreviousValid = StateBool(ini, _minerSaveKeys[52], routeProgressPreviousValid);
routeProgressTracking = StateBool(ini, _minerSaveKeys[53], routeProgressTracking);
pathAnchorValid = StateBool(ini, _minerSaveKeys[54], pathAnchorValid);
if (TryParseV3(StateString(ini, _minerSaveKeys[55], V3ToString(pathAnchorPos)), out v))
    pathAnchorPos = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[56], V3ToString(pathAnchorForward)), out v))
    pathAnchorForward = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[57], V3ToString(pathAnchorUp)), out v))
    pathAnchorUp = v;
recallActive = StateBool(ini, _minerSaveKeys[58], recallActive);
recallWaitingForAreaExit = StateBool(ini, _minerSaveKeys[59], recallWaitingForAreaExit);
recallWaitingForDock = StateBool(ini, _minerSaveKeys[60], recallWaitingForDock);
recallReverseMode = StateBool(ini, _minerSaveKeys[61], recallReverseMode);
if (int.TryParse(StateString(ini, _minerSaveKeys[62], recallIndex.ToString()), out n))
    recallIndex = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[63], recallLastDone.ToString()), out n))
    recallLastDone = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[64], recallLastTotal.ToString()), out n))
    recallLastTotal = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[68], ((int)autoServiceState).ToString()), out n))
{
    if (n < 0 || n > (int)AutoServiceState.ReturningToWork)
        n = 0;
    autoServiceState = (AutoServiceState)n;
}
autoServiceReason = StateString(ini, _minerSaveKeys[69], autoServiceReason);
autoServiceResumeMineArea = StateBool(ini, _minerSaveKeys[70], autoServiceResumeMineArea);
if (int.TryParse(StateString(ini, _minerSaveKeys[71], autoServiceResumeMineAreaRecordIndex.ToString()), out n))
    autoServiceResumeMineAreaRecordIndex = n;
if (int.TryParse(StateString(ini, _minerSaveKeys[72], autoServiceResumeAreaLaneIndex.ToString()), out n))
    autoServiceResumeAreaLaneIndex = Math.Max(0, n);
if (TryParseV3(StateString(ini, _minerSaveKeys[73], V3ToString(autoServiceResumeMineEntryPos)), out v))
    autoServiceResumeMineEntryPos = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[74], V3ToString(autoServiceResumeMineEntryForward)), out v))
    autoServiceResumeMineEntryForward = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[75], V3ToString(autoServiceResumeMineEntryUp)), out v))
    autoServiceResumeMineEntryUp = v;
if (int.TryParse(StateString(ini, _minerSaveKeys[77], autoServiceActionRetryTicks.ToString()), out n))
    autoServiceActionRetryTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[78], "0"), out n))
    autoServiceReturnRouteIndex = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[79], ((int)hangarState).ToString()), out n))
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
if (TryParseV3(StateString(ini, _minerSaveKeys[80], V3ToString(hangarTargetPosition)), out v))
    hangarTargetPosition = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[81], V3ToString(hangarTargetForward)), out v))
    hangarTargetForward = v;
if (TryParseV3(StateString(ini, _minerSaveKeys[82], V3ToString(hangarTargetUp)), out v))
    hangarTargetUp = v;
if (int.TryParse(StateString(ini, _minerSaveKeys[83], "0"), out n))
    hangarOperationId = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[84], "0"), out n))
    hangarPhaseElapsedTicks = Math.Max(0, n);
hangarLaunchUndocked = legacyHangarRouteDeparture || StateBool(ini, _minerSaveKeys[85], hangarLaunchUndocked);
stoneEjectionHoldActive = StateBool(ini, _minerSaveKeys[86], stoneEjectionHoldActive);
stoneEjectionResumeAutopilot = StateBool(ini, _minerSaveKeys[87], stoneEjectionResumeAutopilot);
if (TryParseNumber(StateString(ini, _minerSaveKeys[89], StateString(ini, "pathRecordMineDepth", NumberR(standardMineDepth))), out d))
    standardMineDepth = d;
if (int.TryParse(StateString(ini, _minerSaveKeys[90], ((int)dockingState).ToString()), out n))
    dockingState = (DockingState)n;
if (int.TryParse(StateString(ini, _minerSaveKeys[91], undockRecompileTicksRemaining.ToString()), out n))
    undockRecompileTicksRemaining = n;
if (int.TryParse(StateString(ini, _minerSaveKeys[92], dockMergeReadyTicks.ToString()), out n))
    dockMergeReadyTicks = n;
if (int.TryParse(StateString(ini, _minerSaveKeys[93], dockConnectRetryTicks.ToString()), out n))
    dockConnectRetryTicks = n;
if (int.TryParse(StateString(ini, _minerSaveKeys[94], dockConnectRecompileTicksRemaining.ToString()), out n))
    dockConnectRecompileTicksRemaining = n;
dockConnectRecompileArmed = StateBool(ini, _minerSaveKeys[95], dockConnectRecompileArmed);
dockRetryPending = StateBool(ini, _minerSaveKeys[96], dockRetryPending);
if (int.TryParse(StateString(ini, _minerSaveKeys[97], dockRetryTicks.ToString()), out n))
    dockRetryTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[98], "0"), out n))
    dockingPhaseElapsedTicks = Math.Max(0, n);
if (int.TryParse(StateString(ini, _minerSaveKeys[99], "0"), out n))
    dockingAttemptFailures = Math.Max(0, Math.Min(dockingAttemptFailureLimit, n));
if (TryParseV3(StateString(ini, _minerSaveKeys[100], V3ToString(connectorOffsetLocal)), out v))
    connectorOffsetLocal = v;
long.TryParse(StateString(ini, _minerSaveKeys[105], "0"), out minerDockConnectorId);
long.TryParse(StateString(ini, _minerSaveKeys[106], "0"), out minerDockSession);
pendingUndock = StateBool(ini, _minerSaveKeys[107], false);
if (!int.TryParse(StateString(ini, _minerSaveKeys[108], "0"), out minerPowerGoal) || minerPowerGoal < 0 || minerPowerGoal > 2)
    minerPowerGoal = 0;
}
