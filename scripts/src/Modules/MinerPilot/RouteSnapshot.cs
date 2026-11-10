
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
    modulePathLoadPending=true;
    MyIni pending=new MyIni();
    if(!pending.TryParse(moduleOperation.Payload)) {modulePathLoadPending=false;moduleOperation.Finish(moduleOperation.Id,"error:invalid route job");return;}
    pathLoadJob=new PathLoadJob {Data=pending.Get("Job","route").ToString(""),TimerName="Commander route",MissionId=requestedMissionId};
    // Parsing has no navigation side effects. In particular, do not undock or
    // overwrite the active anchor until the whole replacement is ready.
    Runtime.UpdateFrequency = tickSpeed;
}


bool TryLoadPathRecordsFromTimer(out List<string> records, out string timerName)
{
    records = new List<string>();
    timerName = "";
    pathAnchorValid = false;
    pathAnchorPos = Vector3D.Zero;
    pathAnchorForward = Vector3D.Zero;
    pathAnchorUp = Vector3D.Zero;
    timerName="Commander route";
    string data = moduleRoutePayload;
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

bool modulePathLoadPending;
void CancelPathLoad()
{
    if(modulePathLoadPending && moduleOperation.Verb=="path")moduleOperation.Finish(moduleOperation.Id,"cancelled");
    modulePathLoadPending=false;pathLoadJob=null;
}



void CommitLoadedPath(PathLoadJob job)

{

    var detail81 = BeginRuntimeDetail();

    try

    {

    modulePathLoadPending=false;
    moduleRoutePayload=job.Data;
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
