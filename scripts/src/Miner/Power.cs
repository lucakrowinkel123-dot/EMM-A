// Persist the goal only. A reload restarts this small idempotent walk, never a stale cursor.
int minerPowerGoal;
int minerPowerIndex, minerPowerStartedRuntime = -1;
bool minerDockPowerReady, minerFlightPowerReady, minerUnloadAfterPower;
readonly List<IMyFunctionalBlock> minerPowerBlocks = new List<IMyFunctionalBlock>();

void CacheMinerPowerBlocks()
{
    minerPowerBlocks.Clear();
    foreach (var block in allThrusters) minerPowerBlocks.Add(block);
    foreach (var block in allGyros) minerPowerBlocks.Add(block);
    foreach (var block in antennas) if (HasMinerTag(block)) minerPowerBlocks.Add(block);
    foreach (var block in batteries) if (HasMinerTag(block)) minerPowerBlocks.Add(block);
    foreach (var block in hydrogenTanks) if (HasMinerTag(block)) minerPowerBlocks.Add(block);
}

bool IsMinerPowerTransitionActive() { return minerPowerGoal != 0; }

void BeginMinerPowerTransition(int goal)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    if (minerPowerGoal == goal || (minerPowerGoal == 0 && (goal == 1 ? minerDockPowerReady : minerFlightPowerReady))) return;
    bool unloadAfter = goal == 1 && (minerUnloadAfterPower || oreTransferActive);
    if (oreTransferActive) CancelMinerUnload();
    minerUnloadAfterPower = unloadAfter;
    // Existing persisted delay also records a deferred request across reloads.
    oreTransferStartDelayTicks = unloadAfter ? 1 : 0;
    minerPowerGoal = goal;
    minerPowerIndex = 0;
    minerPowerStartedRuntime = runtime;
    pluginPowerRequestPending = pluginPowerTransitionsEnabled;
    pluginPowerRequestId = pluginPowerRequestPending ? Math.Max(DateTime.UtcNow.Ticks, pluginPowerRequestId + 1) : 0;
    pluginPowerWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    minerDockPowerReady = minerFlightPowerReady = false;
    // Each selected block clears its own overrides. Do not reset every actuator
    // again through the ordinary idle flight path while this walk is in progress.
    movementResetApplied = true;
    fullStateDirty = true;
    }
    finally { EndRuntimeDetail(48, detail81); }
}

void UpdateMinerPowerTransition()
{
    if (!IsMinerPowerTransitionActive() || scriptPaused || runtime == minerPowerStartedRuntime) return;
    if (!IsMinerDocked() && (minerPowerGoal == 1 || dockingState == DockingState.UndockingPowerUp))
    {
        // An external disconnect must not leave a partly disabled craft or launch
        // a saved departure automatically. Restore controls gradually and hold.
        HoldAfterMinerPowerDisconnect();
        if (minerPowerGoal != 2) BeginMinerPowerTransition(2);
        ScriptLog("Power transition: connector released unexpectedly; restoring flight blocks and holding.");
        return;
    }
    if (pluginPowerRequestPending)
    {
        pluginPowerWaitTicks += UpdateTickDelta();
        if (pluginPowerTransitionsEnabled && pluginPowerWaitTicks < pluginPowerTimeoutTicks)
            return;
        pluginPowerRequestPending = false;
        pluginPowerWaitTicks = 0;
        minerPowerIndex = 0;
        MarkPluginServiceRequestsDirty();
        ScriptLog("Plugin power transition unavailable; Miner PB fallback activated.");
    }
    if (minerPowerIndex < minerPowerBlocks.Count)
    {
        int powerIndex = minerPowerIndex++;
        IMyFunctionalBlock block = minerPowerBlocks[powerIndex];
        if (block == null || block.Closed) return;
        bool flight = minerPowerGoal == 2;
        IMyThrust thruster = block as IMyThrust;
        IMyGyro gyro = block as IMyGyro;
        IMyBatteryBlock battery = block as IMyBatteryBlock;
        IMyGasTank tank = block as IMyGasTank;
        if (thruster != null)
        {
            var detail81 = BeginRuntimeDetail();
            try {
            if (thruster.ThrustOverride != 0) thruster.ThrustOverride = 0;
            if (thruster.Enabled != flight) thruster.Enabled = flight;
            flightCache.NoteThrusterZero(powerIndex);
            } finally { EndRuntimeDetail(54, detail81); }
        }
        else if (gyro != null)
        {
            var detail81 = BeginRuntimeDetail();
            try {
            // Axis setters only take effect while override is enabled in SE.
            bool axesKnownZero = gyro.GyroOverride;
            if (axesKnownZero)
            {
                if (gyro.Pitch != 0) gyro.Pitch = 0;
                if (gyro.Yaw != 0) gyro.Yaw = 0;
                if (gyro.Roll != 0) gyro.Roll = 0;
                gyro.GyroOverride = false;
            }
            if (gyro.Enabled != flight) gyro.Enabled = flight;
            flightCache.NoteGyroDisabled(powerIndex - allThrusters.Count, axesKnownZero);
            } finally { EndRuntimeDetail(55, detail81); }
        }
        else if (battery != null)
        {
            var detail81 = BeginRuntimeDetail();
            try {
            ChargeMode mode = flight ? ChargeMode.Auto : ChargeMode.Recharge;
            if (battery.ChargeMode != mode) battery.ChargeMode = mode;
            } finally { EndRuntimeDetail(56, detail81); }
        }
        else if (tank != null)
        {
            var detail81 = BeginRuntimeDetail();
            try {
            if (tank.Stockpile == flight) tank.Stockpile = !flight;
            } finally { EndRuntimeDetail(57, detail81); }
        }
        else if (block.Enabled != flight) block.Enabled = flight;
        return; // One block, even if it needed several safety properties changed.
    }
    CompleteMinerPowerTransition();
}

void CompleteMinerPowerTransition()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    minerDockPowerReady = minerPowerGoal == 1;
    minerFlightPowerReady = minerPowerGoal == 2;
    minerPowerGoal = 0;
    pluginPowerRequestPending = false;
    pluginPowerWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    gyros.ResetControllerHistory();
    thrust.ResetControllerHistory();
    fullStateDirty = true;
    if (minerDockPowerReady && minerUnloadAfterPower)
    {
        minerUnloadAfterPower = false;
        ScheduleOreTransfer();
    }
    }
    finally { EndRuntimeDetail(49, detail81); }
}

void CancelMinerPowerTransition()
{
    bool wasActive = IsMinerPowerTransitionActive();
    bool wasFlightReady = minerFlightPowerReady;
    minerPowerGoal = 0;
    pluginPowerRequestPending = false;
    pluginPowerWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    minerUnloadAfterPower = false;
    minerDockPowerReady = minerFlightPowerReady = false;
    if (IsMinerDocked()) BeginMinerPowerTransition(1);
    else if (wasActive) BeginMinerPowerTransition(2);
    else minerFlightPowerReady = wasFlightReady;
    fullStateDirty = true;
}

void HandlePluginPowerResult(string command)
{
    string[] fields = command.Split('|');
    long requestId;
    int goal;
    if (fields.Length < 5 || !long.TryParse(fields[1], out requestId) || requestId != pluginPowerRequestId
        || !int.TryParse(fields[3], out goal) || goal != minerPowerGoal || !pluginPowerRequestPending) return;
    pluginPowerRequestPending = false;
    pluginPowerWaitTicks = 0;
    MarkPluginServiceRequestsDirty();
    if (fields[2].Equals("ok", StringComparison.OrdinalIgnoreCase))
    {
        CompleteMinerPowerTransition();
        ScriptLog("Plugin completed Miner " + (goal == 1 ? "docked" : "flight") + " power transition.");
    }
    else
    {
        minerPowerIndex = 0;
        ScriptLog("Plugin power transition failed: " + fields[4] + ". Miner PB fallback activated.");
    }
}

void RecoverMinerPowerTransition()
{
    int savedGoal = minerPowerGoal;
    minerPowerGoal = 0;
    minerDockPowerReady = minerFlightPowerReady = false;
    if (IsMinerDocked())
    {
        bool departing = dockingState == DockingState.UndockingPowerUp || pendingUndock;
        if (departing) dockingState = DockingState.UndockingPowerUp;
        BeginMinerPowerTransition(departing ? 2 : 1);
    }
    else if (savedGoal != 0 || dockingState == DockingState.DockingPowerDown || dockingState == DockingState.UndockingPowerUp)
    {
        // An interrupted flight preparation continues only after every block is ready.
        if (dockingState == DockingState.DockingPowerDown || dockingState == DockingState.UndockingPowerUp)
        {
            HoldAfterMinerPowerDisconnect();
        }
        BeginMinerPowerTransition(2);
    }
    else
    {
        // Startup is the only readiness audit. Normal flight/recompile must not
        // introduce a long control gap when every cached block is already ready.
        minerFlightPowerReady = true;
        foreach (IMyFunctionalBlock block in minerPowerBlocks)
        {
            if (block == null || block.Closed) continue;
            IMyBatteryBlock battery = block as IMyBatteryBlock;
            IMyGasTank tank = block as IMyGasTank;
            if (battery != null ? battery.ChargeMode != ChargeMode.Auto : tank != null ? tank.Stockpile : !block.Enabled)
            {
                minerFlightPowerReady = false;
                break;
            }
        }
        if (!minerFlightPowerReady) BeginMinerPowerTransition(2);
    }
}

void HoldAfterMinerPowerDisconnect()
{
    dockingState = DockingState.None;
    CancelUndockSequence();
    autopilotEnabled = hasTarget = miningEnabled = false;
    pathPaused = true;
    pathPausedNavResume = false;
    minerDockPowerReady = false;
    pathDockPending = false;
    pathCompleteBroadcastTicks = 0;
    ClearRecallState();
    ClearAutoServiceState();
    ClearStoneEjectionHold();
    ClearDockRetry();
    hangarState = HangarState.None;
    hangarLaunchUndocked = false;
    CancelMinerUnload();
    // Safety cancellation is exceptional: immediately release any remaining
    // live overrides before restoring disabled blocks one at a time.
    StopMinerMovementNow();
    fullStateDirty = true;
}

void StopMinerMovementNow()
{
    movementResetApplied = false;
    ResetMovement();
}
