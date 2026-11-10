sealed class PathLoadJob
{
    public string Data, MissionId, TimerName;
    public IMyTimerBlock Timer;
    public int Position, LineStart, PlaneTotal;
    public bool AnchorValid, RouteMetersFound;
    public Vector3D AnchorPosition, AnchorForward, AnchorUp;
    public double RouteMeters;
    public List<string> Records = new List<string>();
    public List<int> PlaneIndex = new List<int>();
}
PathLoadJob pathLoadJob;
const int pathLoadCharactersPerTick = 1024;
const int pathLoadRecordsPerTick = 2;

void CancelPathLoad()
{
    pathLoadJob = null;
}

void StartPathFromTimer(string missionId = "")
{
    string requestedMissionId = missionId ?? "";
    // A repeated delivery of the same request must not restart a partial parse.
    if (pathLoadJob != null && pathLoadJob.MissionId == requestedMissionId)
        return;
    // The Carrier retries PathStart until it sees the acknowledgement. Once a
    // mission has committed, repeat deliveries only repeat that acknowledgement.
    if (requestedMissionId.Length > 0 && activeMissionId == requestedMissionId)
    {
        SendPathStartAccepted(requestedMissionId);
        return;
    }
    IMyTimerBlock timer = FindPathTimer();
    if (timer == null)
    {
        ScriptLog($"Unable to load path records from timer tag '{pathTimerTag}'.");
        return;
    }
    pathLoadJob = new PathLoadJob { Timer = timer, TimerName = timer.CustomName, MissionId = requestedMissionId };
    // Parsing has no navigation side effects. In particular, do not undock or
    // overwrite the active anchor until the whole replacement is ready.
    Runtime.UpdateFrequency = tickSpeed;
}

void PumpPathLoad()
{
    PathLoadJob job = pathLoadJob;
    if (job == null)
        return;
    if (job.Data == null)
    {
        if (!TryClaimMinerHeavyWork()) return;
        job.Data = job.Timer == null ? "" : job.Timer.CustomData ?? "";
        job.Timer = null;
        return;
    }
    int scanned = 0, records = 0;
    while (job.Position < job.Data.Length && scanned < pathLoadCharactersPerTick && records < pathLoadRecordsPerTick)
    {
        char c = job.Data[job.Position++];
        scanned++;
        if (c != '\n' && c != '\r')
            continue;
        string raw = job.Data.Substring(job.LineStart, job.Position - job.LineStart - 1);
        job.LineStart = job.Position;
        AddLoadedPathRecord(job, raw);
        records++;
    }
    if (job.Position < job.Data.Length)
        return;
    if (job.LineStart < job.Data.Length)
    {
        if (records >= pathLoadRecordsPerTick)
            return;
        AddLoadedPathRecord(job, job.Data.Substring(job.LineStart));
        job.LineStart = job.Data.Length;
        // Publish on a separate tick, never alongside the last record parsing.
        return;
    }
    if (scanned > 0)
        return;
    pathLoadJob = null;
    if (job.Records.Count == 0 && !job.AnchorValid)
    {
        ScriptLog($"Unable to load path records from timer tag '{pathTimerTag}'.");
        return;
    }
    CommitLoadedPath(job);
}

void AddLoadedPathRecord(PathLoadJob job, string raw)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    string record = NormalizePathRecord(raw);
    if (string.IsNullOrWhiteSpace(record))
        return;
    if (!job.AnchorValid)
    {
        Vector3D position, forward, up;
        if (TryParseCarrierAnchorLine(record, out position, out forward, out up))
        {
            job.AnchorPosition = position;
            job.AnchorForward = forward;
            job.AnchorUp = up;
            job.AnchorValid = true;
            return;
        }
    }
    if (!IsPathMetadataRecord(record) && LineLooksLikeMineArea(record))
        job.PlaneTotal++;
    job.Records.Add(record);
    job.PlaneIndex.Add(job.PlaneTotal);
    if (!job.RouteMetersFound && record.StartsWith("00;routeMeters:", StringComparison.OrdinalIgnoreCase))
    {
        double meters;
        if (TryParseNumber(record.Substring(15).Trim(), out meters))
            job.RouteMeters = Math.Max(0, meters);
        job.RouteMetersFound = true;
    }
    }
    finally { EndRuntimeDetail(39, detail81); }
}

void CommitLoadedPath(PathLoadJob job)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    MarkMinerMissionTransition();
    bool launchingFromHangar = !IsMinerDocked() && !IsMergerDocked()
        && (hangarState == HangarState.AwaitingStore
            || hangarState == HangarState.PostDeployCheck
            || hangarState == HangarState.PostDeployDocking
            || hangarState == HangarState.PostDeployService
            || hangarState == HangarState.MissionReady);
    // A coherent snapshot from the previous mission must not publish after
    // this new mission becomes active. The next save captures the new route.
    CancelMinerSaveWork();

    // Commit the new mission only after its authoritative path has loaded.
    // A failed PathStart must leave a deployed Miner's MissionReady handshake intact.
    activeMissionId = job.MissionId;
    CancelUndockSequence();
    ClearRecallState();
    ClearAutoServiceState();
    ClearDockRetry();
    dockingAttemptFailures = 0;
    if (!launchingFromHangar)
        hangarState = HangarState.None;
    hangarLaunchUndocked = launchingFromHangar;
    hangarBroadcastTicks = 0;
    // PathStart begins a newly issued mission. A previous cockpit-loss abort can
    // leave the PB paused with UpdateFrequency.None; wake it before starting the
    // first record so an accepted route can actually advance through undocking.
    scriptPaused = false;
    pauseResumeAutopilot = false;
    Runtime.UpdateFrequency = tickSpeed;
    pathRecords = job.Records;
    miningPlaneIndex = job.PlaneIndex;
    miningPlaneTotal = job.PlaneTotal;
    pathAnchorValid = job.AnchorValid;
    pathAnchorPos = job.AnchorPosition;
    pathAnchorForward = job.AnchorForward;
    pathAnchorUp = job.AnchorUp;
    pathIndex = 0;
    routeProgressTracking = false;
    routeProgressPreviousValid = false;
    routeProgressMeters = 0;
    routeProgressTotalMeters = 0;
    routeProgressTotalMeters = job.RouteMeters;
    routeProgressTracking = routeProgressTotalMeters > 0;
    routeProgressPreviousPosition = _tickRcPosition;
    routeProgressPreviousValid = routeProgressTracking;
    minerProgressBroadcastTicks = 0;
    pathMode = true;
    pathPaused = false;
    pathPausedNavResume = false;
    pathDrillApproachActive = false;
    SetDrills(false);
    pathDockPending = false;
    pathCompleteBroadcastTicks = 0;
    fullStateDirty = true;
    MarkProgressStateDirty();
    ScriptLog($"Loaded {pathRecords.Count} path records from timer '{job.TimerName}'.");
    SendPathStartAccepted(job.MissionId);
    if (launchingFromHangar)
        BeginDeployedMissionCheck();
    else
        StartNextPathRecord();
    }
    finally { EndRuntimeDetail(38, detail81); }
}

void SendPathStartAccepted(string missionId)
{
    if (string.IsNullOrWhiteSpace(missionId))
        return;
    IGC.SendBroadcastMessage(minerProgressTag, "path_started|" + missionId);
}

bool IsPathMiningApproachAt(int nextRecordIndex)
{
    for (int i = Math.Min(nextRecordIndex, pathRecords.Count) - 1; i >= 0; i--)
    {
        string record = NormalizePathRecord(pathRecords[i]);
        if (IsPathMetadataRecord(record))
            continue;
        if (record.Equals("drillson", StringComparison.OrdinalIgnoreCase))
            return true;
        if (record.Equals("dock", StringComparison.OrdinalIgnoreCase)
            || record.Equals("undock", StringComparison.OrdinalIgnoreCase)
            || record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase)
            || record.StartsWith("minearea;", StringComparison.OrdinalIgnoreCase)
            || LineLooksLikeMineArea(record))
            return false;
    }
    return false;
}

void RecoverPathFacingAfterLoad()
{
    if (!pathMode || pathRecords.Count == 0)
        return;
    // Rebuild this derived flag once on reload, never during the per-tick loop.
    pathDrillApproachActive = IsPathMiningApproachAt(pathIndex);
    if (!hasTarget || !pathAnchorValid || pathIndex <= 0 || pathIndex > pathRecords.Count
        || miningEnabled || areaMiningMode || recallActive || pathDockPending
        || dockingState != DockingState.None || pendingUndock || hangarState != HangarState.None
        || autoServiceState != AutoServiceState.None)
        return;
    string record = NormalizePathRecord(pathRecords[pathIndex - 1]);
    bool reverseWaypoint = record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase);
    if (reverseWaypoint)
        record = record.Substring(8).Trim();
    Vector3D pos, forward, up;
    if (!TryParsePathPointLine(record, pathAnchorPos, out pos, out forward, out up)
        || Vector3D.DistanceSquared(targetPosition, pos) > 0.01)
        return;
    bool holdRecordedPose = reverseWaypoint || pathDrillApproachActive;
    desiredForward = holdRecordedPose ? forward : Vector3D.Zero;
    desiredUp = holdRecordedPose ? up : Vector3D.Zero;
}

void StartNextPathRecord()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    int visited = 0;
    while (pathMode && pathIndex < pathRecords.Count && visited < pathLoadRecordsPerTick)
    {
        visited++;
        string record = pathRecords[pathIndex];
        pathIndex++;
        if (IsPathMetadataRecord(record))
            continue;
        if (ExecutePathRecord(record))
            return;
        ScriptLog($"Skipping invalid path record #{pathIndex}.");
    }

    if (pathMode && pathIndex >= pathRecords.Count)
    {
        // Docking schedules unloading; completing the route alone is not enough.
        if (!IsMinerDocked() || oreTransferStartDelayTicks > 0 || oreTransferActive || !oreTransferSucceeded)
            return;
        MarkProgressStateDirty();
        pathMode = false;
        pathDrillApproachActive = false;
        SetDrills(false);
        ScriptLog("Path execution complete.");
        pathCompleteBroadcastTicks = pathCompleteBroadcastRepeatTicks;
        SendPathCompleteBroadcast();
    }
    }
    finally { EndRuntimeDetail(37, detail81); }
}

int UpdateTickDelta()
{
    if (tickSpeed == UpdateFrequency.Update10)
        return 10;
    if (tickSpeed == UpdateFrequency.Update100)
        return 100;
    return 1;
}

void UpdatePathCompleteBroadcast()
{
    if (pathCompleteBroadcastTicks <= 0)
        return;
    if (runtime % igcPollIntervalTicks == 0)
        SendPathCompleteBroadcast();
    pathCompleteBroadcastTicks--;
}

void SendPathCompleteBroadcast()
{
    IGC.SendBroadcastMessage(minerPathCompleteTag, "PathComplete|" + activeMissionId + "|1");
}

bool ExecutePathRecord(string rawRecord)
{
    if (string.IsNullOrWhiteSpace(rawRecord))
        return false;
    string record = rawRecord.Trim();
    if (record.StartsWith("\"") && record.EndsWith("\"") && record.Length >= 2)
        record = record.Substring(1, record.Length - 2).Trim();
    if (string.IsNullOrWhiteSpace(record))
        return false;
    if (IsPathMetadataRecord(record))
        return false;
    if (record.Equals("drillson", StringComparison.OrdinalIgnoreCase))
    {
        pathDrillApproachActive = true;
        SetDrills(true);
        ScriptLog("Mining-plane approach started; drills enabled.");
        return true;
    }

    if (record.StartsWith("minearea;", StringComparison.OrdinalIgnoreCase))
    {
        activeMineAreaRecordIndex = Math.Max(0, pathIndex - 1);
        pathDrillApproachActive = false;
        StartAreaMining(record);
        return miningEnabled || autopilotEnabled || areaPlanActivationStage != 0;
    }

    if (record.Equals("dock", StringComparison.OrdinalIgnoreCase))
    {
        if (pathMode && pathAnchorValid)
        {
            targetPosition = pathAnchorPos;
            desiredForward = pathAnchorForward;
            desiredUp = pathAnchorUp;
            hasTarget = true;
            autopilotEnabled = true;
            pathDockPending = true;
            return true;
        }

        StartDock();
        return dockingState != DockingState.None || dockRetryPending;
    }

    if (record.Equals("undock", StringComparison.OrdinalIgnoreCase))
    {
        if (hangarLaunchUndocked && !IsMinerDocked() && !IsMergerDocked())
        {
            if (!pathAnchorValid)
                return false;
            if (!StartHangarRouteDeparture())
                return false;
            hangarState = HangarState.None;
            hangarLaunchUndocked = false;
            fullStateDirty = true;
            ScriptLog("Hangar departure: moving directly to Dock Pose 2.");
            return dockingState != DockingState.None;
        }

        StartUndock();
        return dockingState != DockingState.None || pendingUndock;
    }

    // SE-style GPS records remain parseable for player input pasted into the timer.
    string navCmd = null;
    if (record.StartsWith("Nav;", StringComparison.OrdinalIgnoreCase))
        navCmd = record.Substring(4).Trim();
    else if (record.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        navCmd = record;
    if (navCmd != null)
    {
        Vector3D gps, forward, up;
        if (!TryParseGpsCommand(navCmd, out gps, out forward, out up))
            return false;
        targetPosition = gps;
        // Ordinary forward path travel should look where it is going. Mining,
        // docking, reverse withdrawal, and recall use their own explicit pose
        // handlers and therefore keep their required orientations.
        desiredForward = Vector3D.Zero;
        desiredUp = Vector3D.Zero;
        hasTarget = true;
        autopilotEnabled = true;
        return true;
    }

    // Compact records (mine area / path point) are decoded against the loaded anchor.
    if (!pathAnchorValid)
        return false;
    bool reverseWaypoint = record.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase);
    if (reverseWaypoint)
        record = record.Substring(8).Trim();
    if (LineLooksLikeMineArea(record))
    {
        Vector3D cornerA, cornerB, areaUp, areaForward;
        double depth;
        if (!TryParseMineAreaLine(record, pathAnchorPos, out cornerA, out cornerB, out areaUp, out areaForward, out depth))
            return false;
        Vector3D entryPose = GetInferredMineAreaEntryPose(cornerA, cornerB);
        if (!IsAtTargetPose(entryPose, areaForward, areaUp))
        {
            pathIndex = Math.Max(0, pathIndex - 1);
            targetPosition = entryPose;
            desiredForward = areaForward;
            desiredUp = areaUp;
            hasTarget = true;
            autopilotEnabled = true;
            activeMaxSpeed = SPEEDB;
            return true;
        }

        activeMineAreaRecordIndex = Math.Max(0, pathIndex - 1);
        pathDrillApproachActive = false;
        StartAreaMiningWithCorners(cornerA, cornerB, areaUp, areaForward, depth);
        return miningEnabled || autopilotEnabled || areaPlanActivationStage != 0;
    }

    Vector3D pos, fwd, upN;
    if (TryParsePathPointLine(record, pathAnchorPos, out pos, out fwd, out upN))
    {
        targetPosition = pos;
        // Normal transit points look where they are travelling. Once the
        // mining-plane approach begins, hold the recorded plane pose through
        // every remaining approach point instead of making one large turn at
        // the mine-area entry. Reverse withdrawal holds that pose as well.
        bool holdRecordedPose = reverseWaypoint || pathDrillApproachActive;
        desiredForward = holdRecordedPose ? fwd : Vector3D.Zero;
        desiredUp = holdRecordedPose ? upN : Vector3D.Zero;
        hasTarget = true;
        autopilotEnabled = true;
        return true;
    }

    return false;
}

bool TryLoadPathRecordsFromTimer(out List<string> records, out string timerName)
{
    records = new List<string>();
    timerName = "";
    pathAnchorValid = false;
    pathAnchorPos = Vector3D.Zero;
    pathAnchorForward = Vector3D.Zero;
    pathAnchorUp = Vector3D.Zero;
    IMyTimerBlock timer = FindPathTimer();
    if (timer == null)
        return false;
    timerName = timer.CustomName;
    string data = timer.CustomData ?? "";
    string[] lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (string line in lines)
    {
        string rec = line.Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (rec.StartsWith("\"") && rec.EndsWith("\"") && rec.Length >= 2)
            rec = rec.Substring(1, rec.Length - 2).Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (!pathAnchorValid)
        {
            Vector3D ap, af, au;
            if (TryParseCarrierAnchorLine(rec, out ap, out af, out au))
            {
                pathAnchorPos = ap;
                pathAnchorForward = af;
                pathAnchorUp = au;
                pathAnchorValid = true;
                continue; // header is consumed, not run as a record
            }
        }

        records.Add(rec);
    }

    return records.Count > 0 || pathAnchorValid;
}
