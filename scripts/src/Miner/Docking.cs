

void UpdateDockPoseAge()
{
    carrierPoseAgeSeconds += secondsPerTick;
}



bool HasFreshCarrierPose()
{
    return carrierConnectorForward.LengthSquared() > 0.001 && carrierConnectorUp.LengthSquared() > 0.001 && carrierPoseAgeSeconds <= dockPoseFreshSeconds;
}

Vector3D GetInnerDockPosePosition()
{
    return GetCarrierConnectorPosition() + carrierConnectorForward * dockInnerPoseDistance;
}





Vector3D GetOuterDockPosePosition()
{
    return GetInnerDockPosePosition() + carrierConnectorUp * (dockOuterPoseUpDistance + minerRouteStartExtra);
}

Vector3D GetCarrierConnectorPosition()
{
    return carrierConnectorPos - carrierConnectorForward * carrierPoseForwardOffset;
}

void UpdateConnectorOffsetLocal()
{
    if (rc == null || minerConnector == null)
        return;
    Vector3D delta = minerConnector.GetPosition() - _tickRcPosition;
    MatrixD wm = _tickRcMatrix;
    connectorOffsetLocal = new Vector3D(Vector3D.Dot(delta, wm.Right), Vector3D.Dot(delta, wm.Up), Vector3D.Dot(delta, wm.Forward));
}

void SetDockingDesiredRcAxes()
{
    Vector3D targetConnectorForward = -carrierConnectorForward;
    Vector3D targetConnectorUp = carrierConnectorUp;
    Vector3D rcForward, rcUp;
    if (TryGetRcAxesForConnectorAlignment(targetConnectorForward, targetConnectorUp, out rcForward, out rcUp))
    {
        desiredForward = rcForward;
        desiredUp = rcUp;
    }
    else
    {
        // Fallback if connector transform cannot be solved.
        desiredForward = targetConnectorForward;
        desiredUp = targetConnectorUp;
    }
}

bool TryGetRcAxesForConnectorAlignment(Vector3D targetConnectorForward, Vector3D targetConnectorUp, out Vector3D rcForward, out Vector3D rcUp)
{
    rcForward = Vector3D.Zero;
    rcUp = Vector3D.Zero;
    if (rc == null || minerConnector == null)
        return false;
    Vector3D cF = Vector3D.Normalize(minerConnector.WorldMatrix.Forward);
    Vector3D cU = Vector3D.Normalize(minerConnector.WorldMatrix.Up);
    Vector3D cR = Vector3D.Cross(cF, cU);
    if (cR.LengthSquared() < 0.001)
        return false;
    cR = Vector3D.Normalize(cR);
    Vector3D tF = Vector3D.Normalize(targetConnectorForward);
    Vector3D tU = Vector3D.Normalize(targetConnectorUp);
    Vector3D tR = Vector3D.Cross(tF, tU);
    if (tR.LengthSquared() < 0.001)
        return false;
    tR = Vector3D.Normalize(tR);
    // Transform RC axes from current connector frame into target connector frame.
    rcForward = RemapVectorBetweenFrames(_tickRcMatrix.Forward, cR, cU, cF, tR, tU, tF);
    rcUp = RemapVectorBetweenFrames(_tickRcMatrix.Up, cR, cU, cF, tR, tU, tF);
    if (rcForward.LengthSquared() < 0.001 || rcUp.LengthSquared() < 0.001)
        return false;
    rcForward = Vector3D.Normalize(rcForward);
    rcUp = Vector3D.Normalize(rcUp);
    return true;
}

Vector3D RemapVectorBetweenFrames(
    Vector3D vectorWorld,
    Vector3D fromRight,
    Vector3D fromUp,
    Vector3D fromForward,
    Vector3D toRight,
    Vector3D toUp,
    Vector3D toForward)
{
    double r = Vector3D.Dot(vectorWorld, fromRight);
    double u = Vector3D.Dot(vectorWorld, fromUp);
    double f = Vector3D.Dot(vectorWorld, fromForward);
    return toRight * r + toUp * u + toForward * f;
}

bool IsMergerDocked()
{
    if (!minerMergeStatusValid)
    {
        cachedMinerMergeConnected = minerMerger != null && minerMerger.IsConnected;
        minerMergeStatusValid = true;
    }
    return cachedMinerMergeConnected;
}

void SetMergerEnabled(bool enabled)
{
    if (minerMerger != null && minerMerger.Enabled != enabled)
    {
        minerMerger.Enabled = enabled;
        InvalidateMinerConnectionStatus();
    }
}

void SetConnectorEnabled(bool enabled)
{
    if (minerConnector != null)
    {
        minerConnector.Enabled = enabled;
        InvalidateMinerConnectionStatus();
    }
}

void ForceMinerConnectorUnlatchedUntilMerge()
{
    RefreshDockHardware();
    if (minerConnector != null && GetMinerConnectorStatus() == MyShipConnectorStatus.Connected && !IsMergerDocked())
    {
        var detail81 = BeginRuntimeDetail(); try { minerConnector.Disconnect(); } finally { EndRuntimeDetail(53, detail81); }
        InvalidateMinerConnectionStatus();
    }
    SetConnectorEnabled(false);
    SetMergerEnabled(true);
}

bool RefreshDockHardware()
{
    return minerConnector != null;
}

bool minerFlightPreparationActive;
bool minerFlightPreparationIncludesBlocks;
bool undockPreflightStarted, undockPreflightReady;
int undockPreflightCargoCycle;

bool IsUndockBackgroundFrozen()
{
    return undockPreflightReady && IsUndockSequenceActive()
        || dockingState == DockingState.UndockingPowerUp
        || dockingState == DockingState.UndockingRecompile
        || dockingState == DockingState.UndockingToPose
        || dockingState == DockingState.UndockingToOuterPose;
}

bool PrepareMinerDepartureFlight(bool allowMass)
{
    if (!minerFlightPreparationActive)
    {
        // A merge changes the reference grid, so every frame/capability sample
        // must be rebuilt. Connector-only docking retains the existing snapshot;
        // only a fresh compile, which has no snapshot yet, needs the block walk.
        minerFlightPreparationIncludesBlocks = mergeAssistedDocking || !flightCache.HasThrustSnapshot;
        if (minerFlightPreparationIncludesBlocks) flightCache.BeginPreparation();
        else flightCache.BeginMassPreparation();
        minerFlightPreparationActive = true;
    }
    return flightCache.PrepareStep(1, allowMass);
}

void RecompileFlightSystems()
{
    // Block membership is startup-only. The staged power walk already cleared
    // overrides and preparation refreshed the detached flight snapshot.
    thrust.ResetControllerHistory();
    gyros.ResetControllerHistory();
}

void EnterDockingState(DockingState state)
{
    dockingState = state;
    dockingPhaseElapsedTicks = 0;
    fullStateDirty = true;
}

bool IsDepartureDockingState(DockingState state)
{
    return state == DockingState.UndockingRecompile
        || state == DockingState.UndockingToPose
        || state == DockingState.UndockingToOuterPose;
}

void ResetDockingHandshake()
{
    ClearDockHardwareRequest();
    dockHardwareNativeFallback = false;
    minerFlightPreparationActive = false;
    minerFlightPreparationIncludesBlocks = false;
    undockPreflightStarted = undockPreflightReady = false;
    undockRecompileTicksRemaining = 0;
    dockMergeReadyTicks = 0;
    dockConnectRetryTicks = 0;
    dockConnectRecompileTicksRemaining = 0;
    dockConnectRecompileArmed = false;
}

void PrepareDockingMotion(bool enableMerger, bool enableConnector)
{
    ApplyUndockedPowerState();
    UpdateConnectorOffsetLocal();
    SetMergerEnabled(enableMerger);
    SetConnectorEnabled(enableConnector);
    ResetDockingHandshake();
    miningEnabled = false;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    ClearDockRetry();
}

void FailDockingMotion(string reason)
{
    bool hangarOperation = hangarState != HangarState.None;
    bool departureFailure = IsDepartureDockingState(dockingState);
    ClearDockHardwareRequest();
    dockHardwareNativeFallback = false;
    dockingState = DockingState.None;
    dockingPhaseElapsedTicks = 0;
    dockingAttemptFailures = 0;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    ResetMovement();
    ScriptLog("Docking stopped: " + reason);
    if (hangarOperation)
    {
        hangarState = HangarState.None;
        hangarLaunchUndocked = false;
        SendHangarProgress("failed|" + reason.Replace('|', '/'));
        fullStateDirty = true;
        return;
    }

    if (departureFailure)
    {
        ClearDockRetry();
        pathPaused = pathMode;
        pathPausedNavResume = false;
        pathDockPending = false;
        fullStateDirty = true;
        ScriptLog("Departure stopped; active path paused for manual intervention.");
        return;
    }

    dockingAttemptFailures++;
    if (dockingAttemptFailures < dockingAttemptFailureLimit)
    {
        ArmDockRetry();
        ScriptLog("Dock retry armed after failed attempt " + dockingAttemptFailures + "/" + dockingAttemptFailureLimit + ".");
    }
    else
    {
        ClearDockRetry();
        pathMode = false;
        recallActive = false;
        recallWaitingForDock = false;
        ScriptLog("Docking abandoned after three failed attempts; manual intervention required.");
    }
}

void StartUndock()
{
    if (IsUndockSequenceActive()) return;
    if (dockingState == DockingState.UndockingPowerUp && IsMinerPowerTransitionActive()) return;
    pendingUndock = true;
    fullStateDirty = true;
    RequestCarrierDockPose();
    autopilotEnabled = hasTarget = miningEnabled = false;
    activeMaxSpeed = 0;
    movementResetApplied = false;
    ResetMovement();
    undockStageWaitTicks = 0;
    undockPreflightStarted = undockPreflightReady = false;
    undockStage = UndockStage.Validate;
    ScriptLog("Undock staged sequence started.");
}

bool IsUndockSequenceActive()
{
    return undockStage != UndockStage.None;
}

void CancelUndockSequence()
{
    if (dockHardwareRequestAction == "disconnect") ClearDockHardwareRequest();
    pendingUndock = false;
    undockStage = UndockStage.None;
    undockStageWaitTicks = 0;
    undockPreflightStarted = undockPreflightReady = false;
}

bool UpdateUndockSequence()
{
    if (!IsUndockSequenceActive()) return false;
    autopilotEnabled = hasTarget = miningEnabled = false;
    activeMaxSpeed = 0;
    pendingUndock = true;

    if (undockStage == UndockStage.Validate)
    {
        RefreshDockHardware();
        if (!HasFreshCarrierPose())
        {
            RequestCarrierDockPose();
            return true;
        }
        if (minerConnector == null)
        {
            pendingUndock = false;
            undockStage = UndockStage.None;
            ScriptLog($"Undock denied: miner connector '{minerConnectorName}' not found.");
            return true;
        }
        if (!undockPreflightStarted)
        {
            // Finish departure-dependent reads while still docked. Once this
            // cargo cycle completes the snapshot stays frozen through departure.
            CancelMinerUnload();
            LoadCarrierMinerSettings();
            RefreshMinerHydrogenCache();
            BeginUndockPreflight();
            undockPreflightStarted = true;
            return true;
        }
        if (!HasFreshUndockPreflight())
            return true;
        undockPreflightReady = true;
        bool serviceDeparture = autoServiceState == AutoServiceState.WaitingService || autoServiceState == AutoServiceState.Undocking
            || hangarState == HangarState.FinalService || hangarState == HangarState.UndockingForStore;
        double departureCargoPercent = GetMinerCargoFillPercent();
        bool cargoNotReady = departureCargoPercent > autoServiceCargoResumePercent;
        bool fuelNotReady = HasMinerHydrogenTank() && GetMinerHydrogenPercent() < autoServiceHydrogenResumePercent;
        if (serviceDeparture && (cargoNotReady || fuelNotReady))
        {
            CancelUndockSequence();
            if (autoServiceState == AutoServiceState.Undocking) autoServiceState = AutoServiceState.WaitingService;
            if (hangarState == HangarState.UndockingForStore) hangarState = HangarState.FinalService;
            if (IsMinerDocked())
            {
                if (dockingState == DockingState.UndockingPowerUp)
                {
                    dockingState = DockingState.None;
                    ApplyDockedPowerState();
                }
                ScheduleOreTransfer();
            }
            ScriptLog("Undock held before power-up: service incomplete (cargo "
                + departureCargoPercent.ToString("0.0") + "% / " + autoServiceCargoResumePercent.ToString("0.0")
                + "%, H2O " + GetMinerHydrogenPercent().ToString("0.0") + "% / " + autoServiceHydrogenResumePercent.ToString("0.0") + "%).");
            return true;
        }
        if (!minerFlightPowerReady)
        {
            CancelMinerUnload();
            ApplyUndockedPowerState();
            minerFlightPreparationActive = false;
            EnterDockingState(DockingState.UndockingPowerUp);
            return true;
        }
        UpdateConnectorOffsetLocal();
        BeginDockHardwareRequest("disconnect");
        undockStage = UndockStage.WaitPluginDisconnect;
        return true;
    }
    if (undockStage == UndockStage.WaitPluginDisconnect)
    {
        dockHardwareRequestWaitTicks += UpdateTickDelta();
        if (dockHardwareRequestPending && dockHardwareRequestWaitTicks < dockHardwareDisconnectTimeoutTicks)
            return true;
        if (GetMinerConnectorStatus() != MyShipConnectorStatus.Connected)
        {
            SetConnectorEnabled(false);
            ClearDockHardwareRequest();
            undockStage = UndockStage.ResetHandshake;
            ScriptLog("Dock hardware disconnect was observed after acknowledgement timeout; departure remains staged.");
            return true;
        }
        ClearDockHardwareRequest();
        dockHardwareNativeFallback = true;
        undockStage = UndockStage.DisableMerger;
        ScriptLog("Dock hardware plugin did not complete disconnect; using native Miner fallback.");
        return true;
    }
    if (undockStage == UndockStage.DisableMerger)
    {
        SetMergerEnabled(false);
        undockStage = UndockStage.DisconnectConnector;
        return true;
    }
    if (undockStage == UndockStage.DisconnectConnector)
    {
        var detail81 = BeginRuntimeDetail(); try { minerConnector.Disconnect(); } finally { EndRuntimeDetail(53, detail81); }
        InvalidateMinerConnectionStatus();
        undockStageWaitTicks = 0;
        undockStage = UndockStage.WaitDisconnected;
        return true;
    }
    if (undockStage == UndockStage.WaitDisconnected)
    {
        if (GetMinerConnectorStatus() == MyShipConnectorStatus.Connected)
        {
            undockStageWaitTicks += UpdateTickDelta();
            if (undockStageWaitTicks < undockDisconnectTimeoutTicks) return true;
            pendingUndock = false;
            undockStage = UndockStage.None;
            ScriptLog("Undock stopped: connector did not release within 5 seconds.");
            return true;
        }
        undockStage = UndockStage.DisableConnector;
        return true;
    }
    if (undockStage == UndockStage.DisableConnector)
    {
        SetConnectorEnabled(false);
        undockStage = UndockStage.ResetHandshake;
        return true;
    }
    if (undockStage == UndockStage.ResetHandshake)
    {
        ResetDockingHandshake();
        undockStage = UndockStage.PrepareDeparture;
        return true;
    }

    pendingUndock = false;
    undockStage = UndockStage.None;
    undockRecompileTicksRemaining = undockRecompileTicks;
    EnterDockingState(DockingState.UndockingRecompile);
    ScriptLog("Undock detach complete; preparing departure.");
    return true;
}

bool StartHangarRouteDeparture()
{
    RefreshDockHardware();
    if (minerConnector == null)
    {
        ScriptLog($"Hangar departure paused: miner connector '{minerConnectorName}' not found.");
        return false;
    }

    if (IsMinerDocked())
    {
        StartUndock();
        return dockingState != DockingState.None || pendingUndock;
    }

    PrepareDockingMotion(false, false);
    EnterDockingState(DockingState.UndockingToOuterPose);
    if (HasFreshCarrierPose())
    {
        SetDockingDesiredRcAxes();
        targetPosition = GetOuterDockPosePosition();
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = dockApproachSpeed;
    }

    else
    {
        RequestCarrierDockPose();
        ScriptLog("Hangar departure is waiting for a fresh Carrier pose.");
    }

    ScriptLog("Hangar departure started: moving straight to Dock Pose 2.");
    return true;
}

bool StartHangarDockFromInnerPose()
{
    RefreshDockHardware();
    if (minerConnector == null)
    {
        ScriptLog($"Hangar docking paused: miner connector '{minerConnectorName}' not found.");
        return false;
    }

    if (IsMinerDocked())
    {
        ApplyDockedPowerState();
        EnterDockingState(DockingState.DockingPowerDown);
        return true;
    }

    if (mergeAssistedDocking && minerMerger == null)
    {
        ScriptLog($"Hangar docking paused: miner merge block '{minerMergerName}' not found.");
        return false;
    }

    PrepareDockingMotion(mergeAssistedDocking, !mergeAssistedDocking);
    if (mergeAssistedDocking)
    {
        SetConnectorEnabled(false);
        SetMergerEnabled(true);
    }
    EnterDockingState(DockingState.DockingToInnerPose);
    if (HasFreshCarrierPose())
    {
        SetDockingDesiredRcAxes();
        targetPosition = GetInnerDockPosePosition();
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = dockApproachSpeed;
    }
    else
    {
        RequestCarrierDockPose();
        ScriptLog("Hangar docking is waiting for a fresh Carrier pose.");
    }

    return true;
}

void LoadCarrierMinerSettings()
{
    IMyTimerBlock timer = carrierMinerSettingsTimer;
    if (timer == null)
        return;
    MyIni ini = new MyIni();
    MyIniParseResult result;
    if (!ini.TryParse(timer.CustomData ?? "", out result))
        return;
    double value;
    if (TryParseNumber(ini.Get("EMMA3", "minerStandardDepth").ToString(NumberR(standardMineDepth)), out value))
        standardMineDepth = Math.Max(0, value);
    if (TryParseNumber(ini.Get("EMMA3", "minerLaneWidth").ToString(NumberR(MINEW)), out value))
        MINEW = Math.Max(0.1, value);
    if (TryParseNumber(ini.Get("EMMA3", "minerLaneHeight").ToString(NumberR(MINEH)), out value))
        MINEH = Math.Max(0.1, value);
    if (TryParseNumber(ini.Get("EMMA3", "minerForwardSpeed").ToString(NumberR(SPEEDF)), out value))
        SPEEDF = Math.Max(0.1, value);
    if (TryParseNumber(ini.Get("EMMA3", "minerReturnSpeed").ToString(NumberR(SPEEDB)), out value))
        SPEEDB = Math.Max(0.1, value);
    autoServiceReturnEnabled = ini.Get("EMMA3", "minerAutoService").ToBoolean(autoServiceReturnEnabled);
    if (TryParseNumber(ini.Get("EMMA3", "minerAutoCargo").ToString(NumberR(autoServiceCargoTriggerPercent)), out value))
        autoServiceCargoTriggerPercent = MathHelper.Clamp(value, 0, 100);
    if (TryParseNumber(ini.Get("EMMA3", "minerAutoHydrogen").ToString(NumberR(autoServiceHydrogenTriggerPercent)), out value))
        autoServiceHydrogenTriggerPercent = MathHelper.Clamp(value, 0, 100);
    stopAtOnePercentFuel = ini.Get("EMMA3", "minerStopAtOnePercentFuel").ToBoolean(stopAtOnePercentFuel);
    SetTimerPersistence(ini.Get("EMMA3", "timerPersistence").ToBoolean(timerPersistenceEnabled));
    autoServiceFuelCheckTicks = 0;
    if (TryParseNumber(ini.Get("EMMA3", "minerRouteStartExtra").ToString(NumberR(minerRouteStartExtra)), out value))
        minerRouteStartExtra = MathHelper.Clamp(value, 0, 500);
    mergeAssistedDocking = ini.Get("EMMA3", "minerMergeAssistedDocking").ToBoolean(mergeAssistedDocking);
    if (!mergeAssistedDocking) SetMergerEnabled(false);
    laneDepth = standardMineDepth;
    fullStateDirty = true;
    ScriptLog("Loaded Miner behavior settings from Carrier.");
}

void StartDock()
{
    RefreshDockHardware();
    if (IsMinerDocked())
    {
        ApplyDockedPowerState();
        EnterDockingState(DockingState.DockingPowerDown);
        return;
    }
    if (!HasFreshCarrierPose())
    {
        ScriptLog("Dock denied: no fresh carrier connector pose broadcast.");
        autopilotEnabled = false;
        hasTarget = false;
        activeMaxSpeed = 0;
        ResetMovement();
        ArmDockRetry();
        RequestCarrierDockPose();
        return;
    }

    if (minerConnector == null)
    {
        ScriptLog($"Dock denied: miner connector '{minerConnectorName}' not found.");
        ClearDockRetry();
        return;
    }

    if (mergeAssistedDocking && minerMerger == null)
    {
        ScriptLog($"Dock denied: miner merge block '{minerMergerName}' not found.");
        ClearDockRetry();
        return;
    }

    // A manual detach, interrupted power-down, or restored docking state can
    // otherwise leave the autonomous controller commanding disabled hardware.
    PrepareDockingMotion(mergeAssistedDocking, !mergeAssistedDocking);
    Vector3D outerDockPose = GetOuterDockPosePosition();
    double distToPose = Vector3D.Distance(minerConnector.GetPosition(), outerDockPose);
    if (distToPose > dockPoseGateDistance)
    {
        ScriptLog($"Dock waiting: {distToPose:0.0}m from outer dock pose (max {dockPoseGateDistance:0.0}m).");
        ArmDockRetry();
        return;
    }

    if (mergeAssistedDocking)
    {
        SetConnectorEnabled(false);
        SetMergerEnabled(true);
    }
    desiredForward = Vector3D.Zero;
    desiredUp = Vector3D.Zero;
    targetPosition = outerDockPose;
    hasTarget = true;
    autopilotEnabled = true;
    activeMaxSpeed = dockApproachSpeed;
    EnterDockingState(DockingState.DockingToPose);
    ScriptLog("Dock started (phase 1: outer dock pose, " + (mergeAssistedDocking ? "merge assisted" : "connector only") + ").");
}

void ArmDockRetry()
{
    dockRetryPending = true;
    if (dockRetryTicks <= 0)
        dockRetryTicks = dockRetryIntervalTicks;
}

void ClearDockRetry()
{
    dockRetryPending = false;
    dockRetryTicks = 0;
}

void UpdateDockRetry()
{
    if (!dockRetryPending || dockingState != DockingState.None)
        return;
    if (dockRetryTicks > 0)
    {
        dockRetryTicks = Math.Max(0, dockRetryTicks - UpdateTickDelta());
        return;
    }

    dockRetryTicks = dockRetryIntervalTicks;
    if (!HasFreshCarrierPose())
    {
        RequestCarrierDockPose();
        return;
    }

    ScriptLog("Dock retry: carrier pose restored.");
    StartDock();
}

bool UpdateDockingPhaseTimeout()
{
    int delta = UpdateTickDelta();
    if (dockingState == DockingState.UndockingToPose
        || dockingState == DockingState.UndockingToOuterPose)
    {
        // A hangar-deployed Miner can enter this state before the Carrier has
        // republished its connector pose. Do not spend either departure window
        // until that pose has armed an actual movement target.
        if (!hasTarget) return false;
        if (!IsOrientationAligned(desiredForward, desiredUp, orientationToleranceDeg))
        {
            // Re-entering alignment after translation starts gets a fresh alignment
            // window. Translation remains disabled by the state handler below.
            if (dockingPhaseElapsedTicks >= dockingTravelPhaseStartTicks)
                dockingPhaseElapsedTicks = 0;
            dockingPhaseElapsedTicks += delta;
            if (dockingPhaseElapsedTicks >= dockingAlignmentTimeoutTicks)
            {
                FailDockingMotion("phase " + dockingState + " alignment exceeded 180 seconds");
                return true;
            }

            return false;
        }

        // Alignment time does not consume the 60-second departure travel
        // window. The offset is persisted in the existing phase-timer field.
        if (dockingPhaseElapsedTicks < dockingTravelPhaseStartTicks)
            dockingPhaseElapsedTicks = dockingTravelPhaseStartTicks;
        else
            dockingPhaseElapsedTicks += delta;
        if (dockingPhaseElapsedTicks - dockingTravelPhaseStartTicks >= dockingPhaseTimeoutTicks)
        {
            FailDockingMotion("phase " + dockingState + " travel exceeded 60 seconds");
            return true;
        }

        return false;
    }

    dockingPhaseElapsedTicks += delta;
    if (dockingPhaseElapsedTicks >= dockingPhaseTimeoutTicks)
    {
        FailDockingMotion("phase " + dockingState + " exceeded 60 seconds");
        return true;
    }

    return false;
}

void UpdateDockingState()
{
    if (IsMinerPowerTransitionActive()) return;
    if (dockingState == DockingState.DockingPowerDown)
    {
        if (!IsMinerDocked())
        {
            HoldAfterMinerPowerDisconnect();
            ApplyUndockedPowerState();
            return;
        }
        if (IsMinerDocked() && minerDockPowerReady)
            FinishDockingMotion("Dock complete (connected; power shutdown complete).", true);
        return;
    }
    if (dockingState == DockingState.UndockingPowerUp)
    {
        if (!IsMinerDocked())
        {
            HoldAfterMinerPowerDisconnect();
            ApplyUndockedPowerState();
            return;
        }
        if (minerFlightPowerReady) StartUndock();
        return;
    }
    if (!HasFreshCarrierPose())
    {
        if (dockingPausedEchoTicks <= 0)
        {
            ScriptLog("Docking paused: awaiting fresh carrier pose.");
            RequestCarrierDockPose();
            dockingPausedEchoTicks = dockingPausedEchoIntervalTicks;
        }
        else
            dockingPausedEchoTicks--;
        autopilotEnabled = false;
        return;
    }

    dockingPausedEchoTicks = 0;
    if ((dockingState == DockingState.UndockingToPose || dockingState == DockingState.UndockingToOuterPose) && !hasTarget)
    {
        // StartHangarRouteDeparture may have begun while the carrier pose was
        // stale. Arm the appropriate departure pose as soon as the requested
        // fresh carrier pose arrives.
        SetDockingDesiredRcAxes();
        targetPosition = dockingState == DockingState.UndockingToOuterPose
            ? GetOuterDockPosePosition()
            : GetInnerDockPosePosition();
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = dockApproachSpeed;
        fullStateDirty = true;
    }
    if (UpdateDockingPhaseTimeout())
        return;

    if (dockingState == DockingState.UndockingRecompile)
    {
        autopilotEnabled = false;
        hasTarget = false;
        activeMaxSpeed = 0;
        if (undockRecompileTicksRemaining > 0) undockRecompileTicksRemaining--;
        // Capability/frame reads overlap the separation wait. Mass is sampled
        // only after it, and navigation starts on a later invocation.
        bool flightReady = PrepareMinerDepartureFlight(undockRecompileTicksRemaining <= 0 && !IsMinerDocked());
        if (undockRecompileTicksRemaining > 0 || !flightReady || IsMinerDocked()) return;

        RecompileFlightSystems();
        SetDockingDesiredRcAxes();
        targetPosition = GetInnerDockPosePosition();
        hasTarget = true;
        autopilotEnabled = true;
        activeMaxSpeed = dockApproachSpeed;
        MarkMinerMissionTransition();
        EnterDockingState(DockingState.UndockingToPose);
        ScriptLog("Undock recompile complete. Moving to inner dock pose.");
        return;
    }

    if (dockingState == DockingState.UndockingToPose)
    {
        SetDockingDesiredRcAxes();
        if (!IsOrientationAligned(desiredForward, desiredUp, orientationToleranceDeg))
        {
            targetPosition = minerConnector.GetPosition();
            activeMaxSpeed = 0;
            autopilotEnabled = true;
            return;
        }

        targetPosition = GetInnerDockPosePosition();
        activeMaxSpeed = dockApproachSpeed;
        autopilotEnabled = true;
        if (IsAtDockPose(targetPosition))
        {
            if (hangarState == HangarState.UndockingForStore)
            {
                FinishDockingMotion("Hangar departure reached inner dock pose.");
                return;
            }

            EnterDockingState(DockingState.UndockingToOuterPose);
            targetPosition = GetOuterDockPosePosition();
            ScriptLog("Undock phase 2: moving to outer dock pose.");
        }

        return;
    }

    if (dockingState == DockingState.UndockingToOuterPose)
    {
        SetDockingDesiredRcAxes();
        targetPosition = GetOuterDockPosePosition();
        activeMaxSpeed = dockApproachSpeed;
        autopilotEnabled = true;
        if (IsAtDockPose(targetPosition))
        {
            FinishDockingMotion("Undock complete (at outer dock pose).");
        }

        return;
    }

    if (dockingState == DockingState.DockingToPose)
    {
        if (mergeAssistedDocking)
        {
            SetConnectorEnabled(false);
            SetMergerEnabled(true);
        }
        else
        {
            SetMergerEnabled(false);
            SetConnectorEnabled(true);
        }

        desiredForward = Vector3D.Zero;
        desiredUp = Vector3D.Zero;
        targetPosition = GetOuterDockPosePosition();
        activeMaxSpeed = dockApproachSpeed;
        autopilotEnabled = true;
        if (IsAtDockPosition(targetPosition))
        {
            SetDockingDesiredRcAxes();
            EnterDockingState(DockingState.DockingAlignAtOuterPose);
            ScriptLog("Dock phase 2: aligning at outer dock pose.");
        }

        return;
    }

    if (dockingState == DockingState.DockingAlignAtOuterPose)
    {
        SetDockingDesiredRcAxes();
        targetPosition = GetOuterDockPosePosition();
        activeMaxSpeed = dockApproachSpeed;
        autopilotEnabled = true;
        if (IsAtDockPose(targetPosition))
        {
            EnterDockingState(DockingState.DockingToInnerPose);
            targetPosition = GetInnerDockPosePosition();
            ScriptLog("Dock phase 3: moving to inner dock pose.");
        }

        return;
    }

    if (dockingState == DockingState.DockingToInnerPose)
    {
        SetDockingDesiredRcAxes();
        if (!IsOrientationAligned(desiredForward, desiredUp, orientationToleranceDeg))
        {
            targetPosition = minerConnector.GetPosition();
            activeMaxSpeed = 0;
            autopilotEnabled = true;
            return;
        }

        targetPosition = GetInnerDockPosePosition();
        activeMaxSpeed = dockApproachSpeed;
        autopilotEnabled = true;
        if (IsAtDockPose(targetPosition))
        {
            EnterDockingState(DockingState.DockingFinalApproach);
            dockMergeReadyTicks = 0;
            dockConnectRetryTicks = 0;
            dockConnectRecompileTicksRemaining = 0;
            dockConnectRecompileArmed = false;
            ScriptLog("Dock phase 4: final connector approach.");
        }

        return;
    }

    if (dockingState == DockingState.DockingFinalApproach)
    {
        BeginDockingFinalApproachProfile();
        SetDockingDesiredRcAxes();
        targetPosition = GetCarrierConnectorPosition();
        activeMaxSpeed = dockFinalSpeed;
        autopilotEnabled = true;
        MarkDockingFinalApproachProfile(ref dockingFinalProfileAxesMs);
        MyShipConnectorStatus connectorStatus = GetMinerConnectorStatus();
        MarkDockingFinalApproachProfile(ref dockingFinalProfileStatusMs);
        if (!dockHardwareNativeFallback)
        {
            if (!dockHardwareRequestPending && connectorStatus == MyShipConnectorStatus.Connected)
            {
                ClearDockHardwareRequest();
                autopilotEnabled = hasTarget = false;
                ApplyDockedPowerState();
                EnterDockingState(DockingState.DockingPowerDown);
                FinishDockingFinalApproachProfile();
                return;
            }
            if (!dockHardwareRequestPending)
                BeginDockHardwareRequest("connect");
            else
                dockHardwareRequestWaitTicks += UpdateTickDelta();
            MarkDockingFinalApproachProfile(ref dockingFinalProfileRequestMs);
            if (dockHardwareRequestWaitTicks < dockHardwareConnectTimeoutTicks)
            {
                FinishDockingFinalApproachProfile();
                return;
            }
            if (connectorStatus == MyShipConnectorStatus.Connected)
            {
                ClearDockHardwareRequest();
                autopilotEnabled = hasTarget = false;
                ApplyDockedPowerState();
                EnterDockingState(DockingState.DockingPowerDown);
                ScriptLog("Dock hardware connection was observed after acknowledgement timeout.");
                FinishDockingFinalApproachProfile();
                return;
            }
            ClearDockHardwareRequest();
            dockHardwareNativeFallback = true;
            ScriptLog("Dock hardware plugin did not complete connect; using native Miner fallback.");
        }
        bool mergedNow = mergeAssistedDocking && IsMergerDocked();
        if (mergedNow)
            dockMergeReadyTicks++;
        else
            dockMergeReadyTicks = 0;
        bool mergeReady = !mergeAssistedDocking || dockMergeReadyTicks >= 2; // connector-only mode needs no merge/recompile gate
        bool canAttemptConnect = mergeReady;
        if (mergeAssistedDocking && !mergeReady)
            ForceMinerConnectorUnlatchedUntilMerge();
        if (mergeAssistedDocking && mergeReady)
        {
            if (!dockConnectRecompileArmed)
            {
                dockConnectRecompileArmed = true;
                dockConnectRecompileTicksRemaining = dockConnectRecompileTicks;
                dockConnectRetryTicks = 0;
            }

            if (dockConnectRecompileTicksRemaining > 0)
            {
                SetConnectorEnabled(false);
                dockConnectRecompileTicksRemaining--;
                canAttemptConnect = false;
            }
            else if (dockConnectRecompileTicksRemaining == 0)
            {
                RefreshDockHardware(); // simulate post-merge recompile/rescan before connector retries
                SetConnectorEnabled(true);
                dockConnectRecompileTicksRemaining = -1;
            }
        }
        else if (mergeAssistedDocking)
        {
            dockConnectRetryTicks = 0;
            dockConnectRecompileTicksRemaining = 0;
            dockConnectRecompileArmed = false;
            canAttemptConnect = false;
        }
        else
        {
            SetMergerEnabled(false);
            SetConnectorEnabled(true);
            dockConnectRecompileTicksRemaining = -1;
            dockConnectRecompileArmed = false;
        }

        if (canAttemptConnect)
        {
            dockConnectRetryTicks++;
            if (dockConnectRetryTicks >= 10)
            {
                dockConnectRetryTicks = 0;
                SetConnectorEnabled(true);
                if (minerConnector != null && GetMinerConnectorStatus() != MyShipConnectorStatus.Connected)
                {
                    var detail81 = BeginRuntimeDetail(); try { minerConnector.Connect(); } finally { EndRuntimeDetail(52, detail81); }
                    InvalidateMinerConnectionStatus();
                    // The native connect request owns this tick. Confirm the
                    // resulting status and begin power-down next invocation.
                    FinishDockingFinalApproachProfile();
                    return;
                }
            }
        }

        bool connectorDocked = minerConnector != null && GetMinerConnectorStatus() == MyShipConnectorStatus.Connected;
        if (connectorDocked)
        {
            autopilotEnabled = hasTarget = false;
            ApplyDockedPowerState();
            EnterDockingState(DockingState.DockingPowerDown);
        }
        FinishDockingFinalApproachProfile();
    }
}

void BeginDockingFinalApproachProfile()
{
    dockingFinalProfileAxesMs = dockingFinalProfileStatusMs = dockingFinalProfileRequestMs = dockingFinalProfileFallbackMs = 0;
    dockingFinalProfileStartTicks = emma3ScriptLogging ? DateTime.UtcNow.Ticks : 0;
}

void MarkDockingFinalApproachProfile(ref double bucket)
{
    if (dockingFinalProfileStartTicks == 0) return;
    long now = DateTime.UtcNow.Ticks;
    bucket += (now - dockingFinalProfileStartTicks) / 10000.0;
    dockingFinalProfileStartTicks = now;
}

void FinishDockingFinalApproachProfile()
{
    if (dockingFinalProfileStartTicks == 0) return;
    MarkDockingFinalApproachProfile(ref dockingFinalProfileFallbackMs);
    double total = dockingFinalProfileAxesMs + dockingFinalProfileStatusMs
        + dockingFinalProfileRequestMs + dockingFinalProfileFallbackMs;
    dockingFinalProfileStartTicks = 0;
    if (total < 0.25) return;
    ScriptLog("Dock final profile: total=" + total.ToString("0.000")
        + " ms; axes=" + dockingFinalProfileAxesMs.ToString("0.000")
        + "; status=" + dockingFinalProfileStatusMs.ToString("0.000")
        + "; request=" + dockingFinalProfileRequestMs.ToString("0.000")
        + "; fallback=" + dockingFinalProfileFallbackMs.ToString("0.000") + ".");
}

void BeginDockHardwareRequest(string action)
{
    dockHardwareRequestId = DateTime.UtcNow.Ticks;
    dockHardwareRequestAction = action;
    dockHardwareRequestWaitTicks = 0;
    dockHardwareRequestPending = true;
    dockHardwareNativeFallback = false;
    MarkPluginServiceRequestsDirty();
    fullStateDirty = true;
}

void ClearDockHardwareRequest()
{
    dockHardwareRequestPending = false;
    dockHardwareRequestAction = "";
    dockHardwareRequestWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    PublishPluginServiceRequests();
}

void PublishDockHardwareRequest()
{
    if (!dockHardwareRequestPending || minerConnector == null)
    {
        SetPluginInfoMessage(4, "");
        return;
    }
    long mergerId = minerMerger == null ? 0 : minerMerger.EntityId;
    if (pluginDockMessage.Changed(dockHardwareRequestId, minerConnector.EntityId, mergerId,
        mergeAssistedDocking ? 1 : 0, Me.EntityId, textA: dockHardwareRequestAction))
        pluginDockMessage.Text = "EMMA3_DOCK_HW|1|" + dockHardwareRequestId + "|" + dockHardwareRequestAction + "|"
            + minerConnector.EntityId + "|" + mergerId + "|" + (mergeAssistedDocking ? "1" : "0") + "|" + Me.EntityId;
    SetPluginInfoMessage(4, pluginDockMessage.Text);
}

void HandlePluginDockHardware(string command)
{
    string[] fields = command.Split('|');
    long requestId;
    if (fields.Length < 5 || !long.TryParse(fields[1], out requestId)
        || !dockHardwareRequestPending || requestId != dockHardwareRequestId)
        return;
    string result = fields[2];
    string action = fields[3];
    string message = fields[4];
    if (!action.Equals(dockHardwareRequestAction, StringComparison.OrdinalIgnoreCase))
        return;

    bool success = result.Equals("ok", StringComparison.OrdinalIgnoreCase);
    ClearDockHardwareRequest();
    if (!success)
    {
        dockHardwareNativeFallback = true;
        if (action.Equals("disconnect", StringComparison.OrdinalIgnoreCase) && undockStage == UndockStage.WaitPluginDisconnect)
            undockStage = UndockStage.DisableMerger;
        ScriptLog("Dock hardware plugin failed " + action + ": " + message + ". Native fallback enabled.");
        return;
    }

    if (action.Equals("disconnect", StringComparison.OrdinalIgnoreCase) && undockStage == UndockStage.WaitPluginDisconnect)
    {
        undockStage = UndockStage.ResetHandshake;
        ScriptLog("Dock hardware plugin completed disconnect.");
        return;
    }
    if (action.Equals("connect", StringComparison.OrdinalIgnoreCase) && dockingState == DockingState.DockingFinalApproach)
    {
        autopilotEnabled = hasTarget = false;
        ApplyDockedPowerState();
        EnterDockingState(DockingState.DockingPowerDown);
        ScriptLog("Dock hardware plugin completed connection.");
    }
}

bool IsAtDockPosition(Vector3D pose)
{
    return Vector3D.DistanceSquared(minerConnector.GetPosition(), pose) <= dockPoseTolerance * dockPoseTolerance;
}

bool IsAtDockPose(Vector3D pose)
{
    return IsAtDockPosition(pose) && IsOrientationAligned(desiredForward, desiredUp, orientationToleranceDeg);
}

bool HasMinerTag(IMyTerminalBlock b)
{
    return b.CustomName.IndexOf(Mi(""), StringComparison.OrdinalIgnoreCase) >= 0;
}

void ApplyDockedPowerState()
{
    BeginMinerPowerTransition(1);
}

void ApplyUndockedPowerState()
{
    BeginMinerPowerTransition(2);
}

void FinishDockingMotion(string message, bool transferOre = false)
{
    dockingState = DockingState.None;
    dockingPhaseElapsedTicks = 0;
    dockingAttemptFailures = 0;
    ClearDockRetry();
    ResetDockingHandshake();
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    ResetMovement();
    ScriptLog(message);
    if (transferOre)
    {
        ScheduleOreTransfer();
    }
}

// The single-PB library scans locally; the split Pilot overrides these two
// boundaries with a request to the Commander that owns cargo and fuel reads.
void BeginUndockPreflight()
{
    undockPreflightCargoCycle = cargoStatsCompletedCycles;
}
bool HasFreshUndockPreflight()
{
    return cargoStatsCompletedCycles > undockPreflightCargoCycle;
}
