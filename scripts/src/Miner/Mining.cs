void StartAreaMining(string command)
{
    TryStartAreaMiningCommandFromLane(command, 0);
}

bool TryStartAreaMiningCommandFromLane(string command, int startLane)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    // minearea;<gps or x,y,z>;<gps or x,y,z>;Faceup,Faceforward;depth
    string[] args = command.Split(';');
    if (args.Length < 5)
    {
        ScriptLog("Usage: minearea;<gps or x,y,z>;<gps or x,y,z>;Faceup,Faceforward;depth");
        return false;
    }

    Vector3D leftUpper;
    Vector3D rightBottom;
    if (!TryParsePoint(args[1], out leftUpper) || !TryParsePoint(args[2], out rightBottom))
    {
        ScriptLog("Invalid point format. Use GPS:Name:X:Y:Z: or x,y,z");
        return false;
    }

    Vector3D up;
    Vector3D forward;
    if (!TryParseAreaOrientation(args[3], leftUpper, rightBottom, out up, out forward))
    {
        ScriptLog("Invalid orientation mode. Use Faceup,Faceforward or ux,uy,uz,fx,fy,fz");
        return false;
    }

    double depth;
    if (!TryParseNumber(args[4], out depth) || depth <= 0)
    {
        ScriptLog("Invalid depth.");
        return false;
    }

    StartAreaMiningWithCornersFromLane(leftUpper, rightBottom, up, forward, depth, startLane);
    return miningEnabled || autopilotEnabled || areaPlanActivationStage != 0;
    }
    finally { EndRuntimeDetail(43, detail81); }
}

void StartAreaMiningWithCorners(Vector3D cornerA, Vector3D cornerB, Vector3D up, Vector3D forward, double depth)
{
    StartAreaMiningWithCornersFromLane(cornerA, cornerB, up, forward, depth, 0);
}

void ClearAreaLaneSkipState()
{
    areaSkipLaneIndexes.Clear();
    areaLaneDepths.Clear();
    areaLaneDestructionRanges.Clear();
}

void LoadActiveMineAreaSkipMetadata()
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    ClearAreaLaneSkipState();
    if (activeMineAreaRecordIndex < 0 || activeMineAreaRecordIndex + 1 >= pathRecords.Count)
        return;
    double savedHeight, savedWidth;
    int savedLaneCount;
    List<int> parsedSkipLanes = new List<int>();
    List<double> parsedLaneDepths = new List<double>();
    List<List<DepthRange>> parsedDestructionRanges = new List<List<DepthRange>>();
    if (!TryParseMineAreaSkipMetaLine(
        pathRecords[activeMineAreaRecordIndex + 1],
        out savedHeight,
        out savedWidth,
        out savedLaneCount,
        parsedSkipLanes,
        parsedLaneDepths,
        parsedDestructionRanges))
    {
        ScriptLog("Lane metadata ignored: unable to parse 00; record.");
        return;
    }

    if (!MineAreaSkipMetaMatches(savedHeight, savedWidth, savedLaneCount))
    {
        ScriptLog("Lane metadata ignored: dimensions do not match current mining plane.");
        return;
    }

    for (int i = 0; i < parsedSkipLanes.Count; i++)
        if (parsedSkipLanes[i] >= 0 && parsedSkipLanes[i] < areaLaneCount)
            AddLaneIndexUnique(areaSkipLaneIndexes, parsedSkipLanes[i]);
    if (parsedLaneDepths.Count == areaLaneCount)
        for (int i = 0; i < parsedLaneDepths.Count; i++)
            areaLaneDepths.Add(parsedLaneDepths[i]);
    if (parsedDestructionRanges.Count == areaLaneCount)
        for (int i = 0; i < parsedDestructionRanges.Count; i++)
            areaLaneDestructionRanges.Add(parsedDestructionRanges[i]);
    }
    finally { EndRuntimeDetail(40, detail81); }
}

void ApplyCurrentAreaLaneDepth()
{
    laneDepth = areaLaneDepths.Count == areaLaneCount && areaLaneIndex >= 0 && areaLaneIndex < areaLaneDepths.Count && areaLaneDepths[areaLaneIndex] > 0 ? areaLaneDepths[areaLaneIndex] : Math.Max(0.1, standardMineDepth);
}

bool AdvancePastSkippedAreaLanes()
{
    int firstSkipped = areaLaneIndex;
    while (areaLaneIndex < areaLaneCount && LaneListContains(areaSkipLaneIndexes, areaLaneIndex))
        areaLaneIndex++;
    if (areaLaneIndex > firstSkipped)
        ScriptLog($"Skipping saved empty lanes {firstSkipped + 1}-{areaLaneIndex}.");
    return areaLaneIndex < areaLaneCount;
}

void StartAreaMiningWithCornersFromLane(Vector3D cornerA, Vector3D cornerB, Vector3D up, Vector3D forward, double depth, int startLane)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    CanonicalizeMineAreaCorners(ref cornerA, ref cornerB, forward, up);
    Vector3D right = Vector3D.Cross(forward, up);
    if (right.LengthSquared() < 0.001)
        right = _tickRcMatrix.Right;
    right = Vector3D.Normalize(right);
    if (!ConfigureAreaLanePlan(cornerA, cornerB, up, right, MINEH, MINEW))
    {
        ScriptLog("Unable to generate area lanes.");
        return;
    }

    laneDepth = depth;
    standardMineDepth = depth;
    drillAxis = forward;
    desiredUp = up;
    desiredForward = drillAxis;
    areaPlanValid = true;
    areaPlanCornerA = cornerA;
    areaPlanCornerB = cornerB;
    areaPlanUp = up;
    areaPlanForward = forward;
    areaPlanRight = right;
    areaMineEntryPos = GetTargetControlPointPosition();
    areaMineEntryForward = Vector3D.Normalize(_tickRcMatrix.Forward);
    areaMineEntryUp = Vector3D.Normalize(_tickRcMatrix.Up);
    returnToMinePoseAfterArea = true;
    areaMiningMode = true;
    areaPlanRequestedStartLane = Math.Max(0, Math.Min(startLane, areaLaneCount - 1));
    retryCurrentLane = false;
    finishAreaAfterBackout = false;
    stuckRetryCount = 0;
    miningEnabled = false;
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    areaPlanActivationStage = 1;
    MarkMinerMissionTransition();
    }
    finally { EndRuntimeDetail(42, detail81); }
}

bool UpdateAreaPlanActivation()
{
    if (areaPlanActivationStage == 0) return false;
    if (areaPlanActivationStage == 1)
    {
        LoadActiveMineAreaSkipMetadata();
        areaPlanActivationStage = 2;
        return true;
    }

    areaLaneIndex = areaPlanRequestedStartLane;
    if (!AdvancePastSkippedAreaLanes())
    {
        areaPlanActivationStage = 0;
        miningEnabled = false;
        miningState = MiningState.Idle;
        ScriptLog("Area mining skipped; all lanes are marked empty.");
        FinishAreaMining();
        return true;
    }

    ApplyCurrentAreaLaneDepth();
    laneAnchor = GetAreaLaneAnchor(areaLaneIndex);
    targetPosition = laneAnchor;
    activeMaxSpeed = SPEEDB;
    hasTarget = true;
    autopilotEnabled = true;
    miningEnabled = true;
    miningState = MiningState.EnterLane;
    areaPlanActivationStage = 0;
    fullStateDirty = true;
    if (areaLaneIndex > 0)
        ScriptLog($"Area mining resumed. Lane={areaLaneIndex + 1}/{areaLaneCount}, depth={laneDepth:0.00}m");
    else
        ScriptLog($"Area mining started. Lanes={areaLaneCount}, depth={laneDepth:0.00}m");
    return true;
}

bool TryParseAreaOrientation(string modeText, Vector3D pointA, Vector3D pointB, out Vector3D up, out Vector3D forward)
{
    up = Vector3D.Zero;
    forward = Vector3D.Zero;
    string[] modes = modeText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    if (modes.Length == 2)
    {
        string upMode = modes[0].Trim();
        string forwardMode = modes[1].Trim();
        if (!upMode.Equals("Faceup", StringComparison.OrdinalIgnoreCase) || !forwardMode.Equals("Faceforward", StringComparison.OrdinalIgnoreCase))
            return false;
        Vector3D gravity = flightCache.Gravity;
        if (gravity.LengthSquared() > 0.001)
            up = Vector3D.Normalize(-gravity);
        else
            up = _tickRcMatrix.Up;
        Vector3D toward = pointB - pointA;
        toward = toward - Vector3D.Dot(toward, up) * up; // flatten into the area plane
        if (toward.LengthSquared() > 0.001)
            forward = Vector3D.Normalize(toward);
        else
            forward = _tickRcMatrix.Forward;
        return true;
    }

    if (modes.Length == 6)
    {
        double ux, uy, uz, fx, fy, fz;
        if (TryParseNumber(modes[0].Trim(), out ux) && TryParseNumber(modes[1].Trim(), out uy) && TryParseNumber(modes[2].Trim(), out uz) && TryParseNumber(modes[3].Trim(), out fx) && TryParseNumber(modes[4].Trim(), out fy) && TryParseNumber(modes[5].Trim(), out fz))
        {
            up = new Vector3D(ux, uy, uz);
            forward = new Vector3D(fx, fy, fz);
            if (up.LengthSquared() > 0.001)
                up = Vector3D.Normalize(up);
            if (forward.LengthSquared() > 0.001)
                forward = Vector3D.Normalize(forward);
            return up.LengthSquared() > 0.001 && forward.LengthSquared() > 0.001;
        }
    }

    return false;
}

bool ConfigureAreaLanePlan(Vector3D leftUpper, Vector3D rightBottom, Vector3D up, Vector3D right, double laneHeight, double laneWidth)
{
    var detail81 = BeginRuntimeDetail();
    try
    {
    if (laneHeight <= 0 || laneWidth <= 0)
        return false;
    Vector3D down = -up;
    Vector3D delta = rightBottom - leftUpper;
    double height = Math.Abs(Vector3D.Dot(delta, down));
    double width = Math.Abs(Vector3D.Dot(delta, right));
    areaPlanRows = CountAreaLanes(height, laneHeight);
    areaPlanCols = CountAreaLanes(width, laneWidth);
    areaPlanRowStart = areaPlanRows > 1 ? laneHeight * 0.5 : height * 0.5;
    areaPlanRowStep = areaPlanRows > 1 ? (height - laneHeight) / (areaPlanRows - 1) : 0;
    areaPlanColStart = areaPlanCols > 1 ? laneWidth * 0.5 : width * 0.5;
    areaPlanColStep = areaPlanCols > 1 ? (width - laneWidth) / (areaPlanCols - 1) : 0;
    areaLaneCount = areaPlanRows * areaPlanCols;
    return areaLaneCount > 0;
    }
    finally { EndRuntimeDetail(41, detail81); }
}

int CountAreaLanes(double extent, double spacing)
{
    // Rotating serialized corners back into the plane introduces tiny rounding
    // errors (17.5 became 17.500000000000348). Do not invent an overlapping
    // column at an exact multiple. Real overhangs still require another lane.
    double count = extent / spacing;
    double nearest = Math.Round(count);
    if (Math.Abs(count - nearest) <= 1e-9)
        count = nearest;
    return Math.Max(1, (int)Math.Ceiling(count));
}

Vector3D GetAreaLaneAnchor(int index)
{
    if (areaPlanCols <= 0)
        return laneAnchor;
    if (index < 0)
        index = 0;
    if (areaLaneCount > 0 && index >= areaLaneCount)
        index = areaLaneCount - 1;
    int r = index / areaPlanCols;
    int c = index % areaPlanCols;
    bool reverse = (r % 2) == 1;
    int ci = reverse ? (areaPlanCols - 1 - c) : c;
    Vector3D down = -areaPlanUp;
    return areaPlanCornerA + down * (areaPlanRowStart + r * areaPlanRowStep) + areaPlanRight * (areaPlanColStart + ci * areaPlanColStep);
}

bool TryParseVector3(string csv, out Vector3D value)
{
    value = Vector3D.Zero;
    string[] t = csv.Split(',');
    if (t.Length != 3)
        return false;
    double x, y, z;
    if (!TryParseNumber(t[0].Trim(), out x) || !TryParseNumber(t[1].Trim(), out y) || !TryParseNumber(t[2].Trim(), out z))
        return false;
    value = new Vector3D(x, y, z);
    return true;
}

// === Compact path/zone serialization (anchor + axis-angle deltas) ===
// On-disk record shapes (path timer / Asteroid DB / Zone DB):
//   anchor (header line 1): cx,cy,cz:ax,ay,az,a   -- absolute carrier pos + axis-angle
//   path point             : dx,dy,dz:ax,ay,az,a   -- delta from anchor + axis-angle
//   mine area              : dx,dy,dz:ax,ay,az,a:w,h:depth -- corner A delta, area axis-angle, w/h, depth
// Disambiguation: header by file position; mine area by 3 ':'-segments, path point by 2.
string FormatPosCsv(Vector3D p)
{
    return p.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        + "," + p.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        + "," + p.Z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

string FormatAxisAngle(Vector3D forward, Vector3D up)
{
    if (forward.LengthSquared() < 1e-9 || up.LengthSquared() < 1e-9)
        return "0.0000,0.0000,0.0000,0.0000";
    Quaternion q = Quaternion.CreateFromForwardUp((Vector3)Vector3D.Normalize(forward), (Vector3)Vector3D.Normalize(up));
    double w = MathHelper.Clamp((double)q.W, -1.0, 1.0);
    double angle = 2.0 * Math.Acos(w);
    double s = Math.Sqrt(Math.Max(0.0, 1.0 - w * w));
    if (angle < 1e-6 || s < 1e-9)
        return "0.0000,0.0000,0.0000,0.0000";
    double ax = q.X / s, ay = q.Y / s, az = q.Z / s;
    return ax.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
        + "," + ay.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
        + "," + az.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
        + "," + angle.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
}

bool TryParseAxisAngle(string s, out Vector3D forward, out Vector3D up)
{
    forward = new Vector3D(0, 0, -1);
    up = new Vector3D(0, 1, 0);
    if (string.IsNullOrWhiteSpace(s))
        return false;
    string[] t = s.Split(',');
    if (t.Length != 4)
        return false;
    double ax, ay, az, angle;
    if (!TryParseNumber(t[0].Trim(), out ax) || !TryParseNumber(t[1].Trim(), out ay) || !TryParseNumber(t[2].Trim(), out az) || !TryParseNumber(t[3].Trim(), out angle))
        return false;
    Vector3D axis = new Vector3D(ax, ay, az);
    if (Math.Abs(angle) < 1e-6 || axis.LengthSquared() < 1e-12)
        return true; // identity orientation
    axis = Vector3D.Normalize(axis);
    Quaternion q = Quaternion.CreateFromAxisAngle((Vector3)axis, (float)angle);
    forward = Vector3D.Normalize(Vector3D.Transform(new Vector3D(0, 0, -1), q));
    up = Vector3D.Normalize(Vector3D.Transform(new Vector3D(0, 1, 0), q));
    return true;
}

bool TryParseCarrierAnchorLine(string line, out Vector3D pos, out Vector3D forward, out Vector3D up)
{
    pos = Vector3D.Zero;
    forward = new Vector3D(0, 0, -1);
    up = new Vector3D(0, 1, 0);
    if (string.IsNullOrWhiteSpace(line))
        return false;
    string[] parts = line.Split(':');
    if (parts.Length != 2)
        return false;
    if (!TryParseVector3(parts[0], out pos))
        return false;
    return TryParseAxisAngle(parts[1], out forward, out up);
}

string FormatPathPointLine(Vector3D pos, Vector3D anchor, Vector3D forward, Vector3D up)
{
    return FormatPosCsv(pos - anchor) + ":" + FormatAxisAngle(forward, up);
}

bool TryParsePathPointLine(string line, Vector3D anchor, out Vector3D pos, out Vector3D forward, out Vector3D up)
{
    pos = Vector3D.Zero;
    forward = new Vector3D(0, 0, -1);
    up = new Vector3D(0, 1, 0);
    if (string.IsNullOrWhiteSpace(line))
        return false;
    string[] parts = line.Split(':');
    if (parts.Length != 2)
        return false;
    Vector3D delta;
    if (!TryParseVector3(parts[0], out delta))
        return false;
    if (!TryParseAxisAngle(parts[1], out forward, out up))
        return false;
    pos = anchor + delta;
    return true;
}

// Mine area line format: dx,dy,dz : ax,ay,az,angle : w,h : depth
//   dx,dy,dz       — corner A as delta from the path-timer anchor position
//   ax,ay,az,angle — axis-angle rotation supplied by the plugin route
//   w,h            — corner B offset, projected into the recorded rotation's right/up plane
//   depth          — drill depth along the recorded rotation's forward
bool TryParseMineAreaLine(
    string line,
    Vector3D anchor,
    out Vector3D cornerA,
    out Vector3D cornerB,
    out Vector3D areaUp,
    out Vector3D areaForward,
    out double depth)
{
    cornerA = Vector3D.Zero;
    cornerB = Vector3D.Zero;
    areaUp = new Vector3D(0, 1, 0);
    areaForward = new Vector3D(0, 0, -1);
    depth = 0;
    if (string.IsNullOrWhiteSpace(line))
        return false;
    string[] parts = line.Split(':');
    if (parts.Length != 4)
        return false;
    Vector3D aDelta;
    if (!TryParseVector3(parts[0], out aDelta))
        return false;
    if (!TryParseAxisAngle(parts[1], out areaForward, out areaUp))
        return false;
    string[] wh = parts[2].Split(',');
    if (wh.Length != 2)
        return false;
    double w, h;
    if (!TryParseNumber(wh[0].Trim(), out w) || !TryParseNumber(wh[1].Trim(), out h))
        return false;
    if (!TryParseNumber(parts[3].Trim(), out depth))
        return false;
    cornerA = anchor + aDelta;
    areaForward = Vector3D.Normalize(areaForward);
    areaUp = Vector3D.Normalize(areaUp);
    Vector3D right = Vector3D.Cross(areaForward, areaUp);
    if (right.LengthSquared() > 1e-9)
        right = Vector3D.Normalize(right);
    cornerB = cornerA + right * w + areaUp * h;
    return true;
}

bool LineLooksLikeMineArea(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return false;
    int colons = 0;
    for (int i = 0; i < line.Length; i++)
        if (line[i] == ':' && ++colons > 3)
            return false;
    return colons == 3;
}

bool TryParsePoint(string text, out Vector3D value)
{
    value = Vector3D.Zero;
    if (string.IsNullOrWhiteSpace(text))
        return false;
    string t = text.Trim();
    if (t.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
    {
        string[] parts = t.Split(':');
        if (parts.Length < 5)
            return false;
        double x, y, z;
        if (!TryParseNumber(parts[2], out x) || !TryParseNumber(parts[3], out y) || !TryParseNumber(parts[4], out z))
            return false;
        value = new Vector3D(x, y, z);
        return true;
    }

    return TryParseVector3(t, out value);
}

void UpdateMiningState()
{
    switch (miningState)
    {
        case MiningState.EnterLane:
            targetPosition = laneAnchor;
            desiredForward = drillAxis;
            activeMaxSpeed = SPEEDB;
            if (IsAtTargetPose(targetPosition, desiredForward, desiredUp, miningArrivalTolerance))
            {
                miningState = MiningState.DrillForward;
                UpdateDrillTerrainClearingMode();
                SetDrills(true);
                ResetStuckMonitor();
            }

            break;
        case MiningState.DrillForward:
            targetPosition = laneAnchor + drillAxis * GetLaneTargetDepth();
            desiredForward = drillAxis;
            bool terrainClearingAllowed = IsCurrentLaneTerrainClearingAllowed();
            activeMaxSpeed = terrainClearingAllowed ? SPEEDF * terrainClearingSpeedMultiplier : SPEEDF;
            SetDrillTerrainClearing(terrainClearingAllowed);
            if (CheckStuckInLane())
            {
                retryCurrentLane = true;
                stuckBackout = true;
                SetDrillTerrainClearing(false);
                miningState = MiningState.Backout;
                ScriptLog("Stuck detected. Returning to anchor and retrying lane.");
            }
            else if (ShouldFinishDrillForward())
            {
                SetDrillTerrainClearing(false);
                miningState = MiningState.Backout;
            }

            break;
        case MiningState.Backout:
            targetPosition = laneAnchor;
            desiredForward = drillAxis;
            activeMaxSpeed = SPEEDB;
            SetDrillTerrainClearing(false);
            if (IsAtTargetPosition(targetPosition, miningArrivalTolerance))
            {
                if (!retryCurrentLane && (!areaMiningMode || finishAreaAfterBackout))
                    SetDrills(false);
                if (pathMode && pathPaused)
                {
                    SetDrills(false);
                    autopilotEnabled = false;
                    activeMaxSpeed = 0;
                    miningEnabled = false; // stop spin loop; pathcontinue re-enables
                    return;
                }

                if (retryCurrentLane)
                {
                    retryCurrentLane = false;
                    bool wasStuck = stuckBackout;
                    stuckBackout = false;
                    if (wasStuck)
                        stuckRetryCount++;
                    bool giveUp = wasStuck && maxStuckRetries > 0 && stuckRetryCount >= maxStuckRetries;
                    if (giveUp && areaMiningMode && areaLaneIndex + 1 < areaLaneCount)
                    {
                        ScriptLog($"Max retries ({maxStuckRetries}) reached. Skipping lane {areaLaneIndex + 1}.");
                        stuckRetryCount = 0;
                        areaLaneIndex++;
                        if (!AdvancePastSkippedAreaLanes())
                        {
                            FinishAreaMining();
                            return;
                        }

                        ApplyCurrentAreaLaneDepth();
                        laneAnchor = GetAreaLaneAnchor(areaLaneIndex);
                        miningState = MiningState.EnterLane;
                    }
                    else if (giveUp)
                    {
                        ScriptLog($"Max retries ({maxStuckRetries}) reached. Mining stopped.");
                        SetDrills(false);
                        miningEnabled = false;
                        miningState = MiningState.Idle;
                        autopilotEnabled = false;
                        activeMaxSpeed = 0;
                        areaMiningMode = false;
                        areaLaneAnchors.Clear();
                        areaLaneIndex = 0;
                        areaLaneCount = 0;
                        areaPlanValid = false;
                        ClearAreaLaneSkipState();
                    }
                    else
                    {
                        miningState = MiningState.EnterLane;
                        string retryInfo = maxStuckRetries > 0 ? $"attempt {stuckRetryCount + 1}/{maxStuckRetries}" : "no limit";
                        ScriptLog($"Retrying lane {areaLaneIndex + 1}/{Math.Max(1, areaLaneCount)} ({retryInfo})");
                    }
                }
                else if (finishAreaAfterBackout)
                {
                    finishAreaAfterBackout = false;
                    FinishAreaMining();
                }
                else if (areaMiningMode && areaLaneIndex + 1 < areaLaneCount)
                {
                    stuckRetryCount = 0;
                    areaLaneIndex++;
                    if (!AdvancePastSkippedAreaLanes())
                    {
                        FinishAreaMining();
                        return;
                    }

                    ApplyCurrentAreaLaneDepth();
                    laneAnchor = GetAreaLaneAnchor(areaLaneIndex);
                    miningState = MiningState.EnterLane;
                    ScriptLog($"Proceeding to lane {areaLaneIndex + 1}/{areaLaneCount}");
                }
                else
                {
                    FinishAreaMining();
                }
            }

            break;
    }
}

void ResetStuckMonitor()
{
    stuckSamplePos = _tickRcPosition;
    stuckNoMoveSeconds = 0;
}

double GetLaneProgressDepth()
{
    return Math.Max(0, Vector3D.Dot((_tickRcPosition + GetTargetOffsetWorld(_tickRcMatrix)) - laneAnchor, drillAxis));
}

double GetLaneTargetDepth()
{
    return laneDepth;
}

bool ShouldFinishDrillForward()
{
    double depthProgress = GetLaneProgressDepth();
    return depthProgress >= laneDepth || IsAtTargetPosition(targetPosition, miningArrivalTolerance);
}

bool CheckStuckInLane()
{
    Vector3D currentPosition = _tickRcPosition;
    double moved = Vector3D.DistanceSquared(currentPosition, stuckSamplePos);
    if (moved < stuckMoveThreshold * stuckMoveThreshold)
    {
        stuckNoMoveSeconds += secondsPerTick;
        if (stuckNoMoveSeconds >= stuckTimeoutSeconds)
        {
            ResetStuckMonitor();
            return true;
        }
    }
    else
    {
        stuckSamplePos = currentPosition;
        stuckNoMoveSeconds = 0;
    }

    return false;
}

void FinishAreaMining()
{
    activeMineAreaRecordIndex = -1;
    pathDrillApproachActive = false;
    SetDrills(false);
    miningEnabled = false;
    miningState = MiningState.Idle;
    areaMiningMode = false;
    areaLaneAnchors.Clear();
    areaLaneIndex = 0;
    areaLaneCount = 0;
    areaPlanValid = false;
    ClearAreaLaneSkipState();
    finishAreaAfterBackout = false;
    fullStateDirty = true;
    if (returnToMinePoseAfterArea)
    {
        ReturnToMinePoseAfterArea();
        {
        }

        ScriptLog("Mining complete. Returning to mine pose...");
    }
    else
    {
        autopilotEnabled = false;
        activeMaxSpeed = 0;
        ScriptLog("Mining operation complete.");
    }
}

void NextHole(bool skipToLast = false)
{
    if (areaLaneCount == 0)
    {
        if (skipToLast && returnToMinePoseAfterArea)
        {
            ReturnToMinePoseAfterArea();
            ScriptLog("Minearea skipped. Returning to mine pose...");
            return;
        }

        ScriptLog("No area plan loaded. Start with minearea first.");
        return;
    }

    int targetIndex = skipToLast ? areaLaneCount - 1 : areaLaneIndex + 1;
    bool atLastLane = skipToLast || targetIndex >= areaLaneCount || targetIndex <= areaLaneIndex;
    if (!areaMiningMode)
        SetDrills(false);
    stuckRetryCount = 0;
    retryCurrentLane = false;
    if (atLastLane)
    {
        if (miningState == MiningState.DrillForward || miningState == MiningState.Backout)
        {
            areaMiningMode = true;
            autopilotEnabled = true;
            miningEnabled = true;
            finishAreaAfterBackout = true;
            miningState = MiningState.Backout;
            ScriptLog("Backing out before finishing mining...");
        }
        else
        {
            FinishAreaMining();
        }

        return;
    }

    areaMiningMode = true;
    autopilotEnabled = true;
    miningEnabled = true;
    finishAreaAfterBackout = false;
    if (miningState == MiningState.DrillForward)
    {
        miningState = MiningState.Backout;
        ScriptLog($"Backing out before advancing to hole {areaLaneIndex + 2}/{areaLaneCount}...");
    }
    else
    {
        areaLaneIndex = targetIndex;
        if (!AdvancePastSkippedAreaLanes())
        {
            FinishAreaMining();
            return;
        }

        ApplyCurrentAreaLaneDepth();
        laneAnchor = GetAreaLaneAnchor(areaLaneIndex);
        targetPosition = laneAnchor;
        desiredForward = drillAxis.LengthSquared() > 0.001 ? drillAxis : _tickRcMatrix.Forward;
        activeMaxSpeed = SPEEDB;
        hasTarget = true;
        miningState = MiningState.EnterLane;
        ScriptLog($"Advancing to hole {areaLaneIndex + 1}/{areaLaneCount}");
    }
}

bool IsAtTargetPosition(Vector3D target, double toleranceMeters = -1)
{
    double baseTolerance = toleranceMeters > 0 ? toleranceMeters : arrivalTolerance;
    double tolerance = Math.Max(0.05, baseTolerance);
    return Vector3D.DistanceSquared(_tickRcPosition + GetActiveTargetOffsetWorld(_tickRcMatrix), target) < tolerance * tolerance;
}

bool IsAtTargetPose(Vector3D target, Vector3D forward, Vector3D up, double toleranceMeters = -1)
{
    return IsAtTargetPosition(target, toleranceMeters) && IsOrientationAligned(forward, up, orientationToleranceDeg);
}

void SetDrills(bool enabled)
{
    foreach (var d in drills)
    {
        if (d == null || !d.IsFunctional)
            continue;
        d.Enabled = enabled;
    }

    if (!enabled)
        SetDrillTerrainClearing(false);
}

void UpdateDrillTerrainClearingMode()
{
    SetDrillTerrainClearing(IsCurrentLaneTerrainClearingAllowed());
}

bool IsCurrentLaneTerrainClearingAllowed()
{
    if (!miningEnabled || miningState != MiningState.DrillForward || areaLaneDestructionRanges.Count != areaLaneCount || areaLaneIndex < 0 || areaLaneIndex >= areaLaneDestructionRanges.Count)
        return false;
    double depth = GetLaneProgressDepth();
    List<DepthRange> ranges = areaLaneDestructionRanges[areaLaneIndex];
    for (int i = 0; i < ranges.Count; i++)
        if (depth >= ranges[i].Start && depth <= ranges[i].End)
            return true;
    return false;
}

void SetDrillTerrainClearing(bool enabled)
{
    if (_lastDrillTerrainClearing.HasValue && _lastDrillTerrainClearing.Value == enabled)
        return;
    foreach (var d in drills)
    {
        if (d == null || !d.IsFunctional)
            continue;
        d.TerrainClearingMode = enabled;
    }

    _lastDrillTerrainClearing = enabled;
}

void RestoreDrillsAfterPause()
{
    bool activeMiningLane = miningEnabled && (miningState == MiningState.DrillForward || miningState == MiningState.Backout);
    if (!pathDrillApproachActive && !activeMiningLane)
        return;
    SetDrills(true);
    if (miningState == MiningState.DrillForward)
        ResetStuckMonitor();
    ScriptLog("Continue restored drills for " + (pathDrillApproachActive ? "mining-plane approach." : miningState.ToString() + "."));
}

void DumpCurrentGpsToCustomData()
{
    StringBuilder sb = new StringBuilder();
    string existing = Me.CustomData ?? "";
    if (!string.IsNullOrWhiteSpace(existing))
        sb.Append(existing.TrimEnd());
    sb.AppendLine();
    sb.AppendLine();
    sb.AppendLine("[AutopilotDump]");
    sb.AppendLine(FormatCurrentGps("Here"));
    Me.CustomData = sb.ToString();
    ScriptLog("Dumped GPS/orientation to Custom Data.");
}
// Recovery snapshots live in PB Storage; INI timers are optional copies.
