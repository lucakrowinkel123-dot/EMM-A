void Move()
{
    movementResetApplied = false;
    flightDiagCommandMode = "evaluating";
    flightDiagCommandAccel = Vector3D.Zero;
    flightDiagCommandSpeedLimit = 0;
    flightDiagEmergency = false;
    MatrixD rcMatrix = _tickRcMatrix;
    Vector3D worldOffset = GetActiveTargetOffsetWorld(rcMatrix);
    Vector3D rcPosition = _tickRcPosition;
    Vector3D position = rcPosition + worldOffset;
    Vector3D toTarget = targetPosition - position;
    double tolerance = GetActiveArrivalTolerance();
    bool pointToPointPrecision = UsePointToPointPrecisionAutopilot();
    bool atPosition = toTarget.LengthSquared() < tolerance * tolerance;
    Vector3D forward = desiredForward;
    bool precisionTravelFacing = pointToPointPrecision && desiredForward == Vector3D.Zero;
    if (precisionTravelFacing)
        forward = GetMinerTravelForward(rcMatrix, toTarget, atPosition);
    else
        minerTravelFacingValid = false;
    if (!precisionTravelFacing && forward == Vector3D.Zero && toTarget.LengthSquared() > 0.001)
        forward = Vector3D.Normalize(toTarget);
    Vector3D up = precisionTravelFacing && !atPosition ? Vector3D.Zero : desiredUp;
    if (up == Vector3D.Zero && !precisionTravelFacing)
    {
        Vector3D gravity = flightCache.Gravity;
        if (gravity.LengthSquared() > 0.001)
            up = Vector3D.Normalize(-gravity);
        else
            up = rcMatrix.Up;
    }

    flightDiagForward = forward;
    flightDiagUp = up;
    bool aligned = IsOrientationAligned(rcMatrix, forward, up, orientationToleranceDeg);
    bool holdArrival = dockingState == DockingState.DockingFinalApproach || hangarState == HangarState.MovingToHangar;
    // Docking and hangaring retain control until connection or settled arrival.
    if (!atPosition || !aligned || holdArrival)
        gyros.FaceVectors(rcMatrix, forward, up);
    flightDiagAligned = aligned;
    flightDiagAtPosition = atPosition;
    if (atPosition)
    {
        if (holdArrival)
        {
            // Keep both translation and orientation under script control until the
            // connector locks or the Miner settles at the hangar center.
            double holdSpeed = Math.Min(0.25, Math.Max(0.05, activeMaxSpeed));
            Vector3D holdTarget = targetPosition - worldOffset;
            Vector3D holdVertical = desiredUp.LengthSquared() > 0.001 ? Vector3D.Normalize(desiredUp) : rcMatrix.Up;
            Vector3D holdAccel = thrust.ControlPositionPrioritizedFrom(rcPosition, holdTarget, Vector3D.Zero, holdSpeed, holdVertical);
            flightDiagCommandMode = hangarState == HangarState.MovingToHangar ? "hangar-hold" : "docking-hold";
            flightDiagCommandSpeedLimit = holdSpeed;
            flightDiagCommandAccel = holdAccel;
            thrust.ApplyAccel(holdAccel);
        }
        else
        {
            flightDiagCommandMode = aligned ? "at-pose" : "rotate-at-position";
            thrust.Reset();
            if (aligned)
                gyros.Reset();
        }

        return;
    }

    double maxSpeedNow = activeMaxSpeed > 0 ? activeMaxSpeed : maxSpeed;
    flightDiagCommandSpeedLimit = maxSpeedNow;
    Vector3D rcTarget = targetPosition - worldOffset;
    if (dockingState != DockingState.None)
    {
        maxSpeedNow = Math.Min(maxSpeedNow, GetPointToPointSafeSpeed(toTarget, tolerance));
        flightDiagCommandSpeedLimit = maxSpeedNow;
        // During docking/undocking: prioritize lateral and forward/backward over vertical.
        // Use desiredUp as the vertical axis; fall back to RC's current up if unset.
        Vector3D vertAxis = desiredUp.LengthSquared() > 0.001 ? Vector3D.Normalize(desiredUp) : rcMatrix.Up;
        Vector3D accel = thrust.ControlPositionPrioritizedFrom(rcPosition, rcTarget, Vector3D.Zero, maxSpeedNow, vertAxis);
        flightDiagCommandMode = "docking-prioritized";
        flightDiagCommandAccel = accel;
        thrust.ApplyAccel(accel);
    }
    else if (pointToPointPrecision)
    {
        if (!aligned)
        {
            flightDiagCommandMode = "rotate-hold";
            thrust.Reset();
            return;
        }

        maxSpeedNow = Math.Min(maxSpeedNow, GetPointToPointSafeSpeed(toTarget, tolerance));
        maxSpeedNow = Math.Min(maxSpeedNow, maxSpeed);
        flightDiagCommandSpeedLimit = maxSpeedNow;
        Vector3D accel = thrust.ControlPositionFrom(rcPosition, rcTarget, Vector3D.Zero, maxSpeedNow);
        bool emergency = ShouldUsePointToPointEmergencyThrust(toTarget, tolerance);
        flightDiagCommandMode = "precision-travel";
        flightDiagCommandAccel = accel;
        flightDiagEmergency = emergency;
        ApplyPointToPointAccel(accel, emergency);
    }
    else
    {
        Vector3D accel = thrust.ControlPositionFrom(rcPosition, rcTarget, Vector3D.Zero, maxSpeedNow);
        flightDiagCommandMode = "normal-travel";
        flightDiagCommandAccel = accel;
        thrust.ApplyAccel(accel);
    }
}

Vector3D GetMinerTravelForward(MatrixD rcMatrix, Vector3D toTarget, bool atPosition)
{
    Vector3D offset = new Vector3D(TOFFR, TOFFU, TOFFF);
    if (!minerTravelFacingValid || minerTravelFacingTarget != targetPosition
        || minerTravelFacingOffset != offset || minerTravelFacingUp != desiredUp)
    {
        minerTravelFacingTarget = targetPosition;
        minerTravelFacingOffset = offset;
        minerTravelFacingUp = desiredUp;
        // Turning moves the offset control point around the ship's centre.
        // Keep a world-space command for this leg instead of chasing that arc
        // or declaring the current nose direction aligned with a new waypoint.
        minerTravelFacingForward = !atPosition && toTarget.LengthSquared() > 0.001
            ? Vector3D.Normalize(toTarget) : rcMatrix.Forward;
        minerTravelFacingValid = true;
    }
    return minerTravelFacingForward;
}

bool UsePointToPointPrecisionAutopilot()
{
    return dockingState == DockingState.None && !miningEnabled;
}

// Brake slightly inside the unchanged arrival sphere, so a stopped ship can
// cross the boundary instead of approaching its edge with zero commanded speed.
double GetPointToPointBrakeDistance(Vector3D toTarget, double tolerance)
{
    double radius = Math.Max(0.05, tolerance);
    return Math.Max(0, toTarget.Length() - radius + Math.Min(0.05, radius * 0.2));
}

double GetPointToPointSafeSpeed(Vector3D toTarget, double tolerance)
{
    double distance = GetPointToPointBrakeDistance(toTarget, tolerance);
    if (distance <= 0)
        return 0;
    Vector3D dir = Vector3D.Normalize(toTarget);
    double brakeAccel = GetPointToPointPlannedAcceleration(dir);
    if (brakeAccel <= 0)
        return 0;
    // Thruster Forward is exhaust; reserve two seconds for braking response.
    double responseSpeed = brakeAccel * 2.0;
    double brakingTerm = 2 * brakeAccel * distance;
    return brakingTerm / (Math.Sqrt(responseSpeed * responseSpeed + brakingTerm) + responseSpeed);
}

bool ShouldUsePointToPointEmergencyThrust(Vector3D toTarget, double tolerance)
{
    Vector3D vel = flightCache.LinearVelocity;
    double dist = GetPointToPointBrakeDistance(toTarget, tolerance);
    if (dist <= 0 || vel.LengthSquared() < 0.01 || toTarget.LengthSquared() < 0.01)
        return false;
    Vector3D dir = Vector3D.Normalize(toTarget);
    double speedToward = Vector3D.Dot(vel, dir);
    if (speedToward <= 0)
        return false;
    double accel = GetPointToPointPlannedAcceleration(dir);
    if (accel <= 0)
        return true;
    return (speedToward * speedToward) / (2 * accel) > dist;
}

double GetShipMass()
{
    return flightCache.Mass;
}

double GetEffectiveThrustInDirection(Vector3D direction)
{
    return flightCache.EffectiveThrust(direction);
}

void InvalidateEffectiveThrustCache()
{
    flightCache.InvalidateCapabilities();
}

double GetPointToPointPlannedThrustN(Vector3D direction)
{
    double thrustPercent = MathHelper.Clamp(pointToPointThrustUsePercent, 1, 100) / 100.0;
    return GetEffectiveThrustInDirection(direction) * thrustPercent;
}

double GetPointToPointPlannedAcceleration(Vector3D direction)
{
    double mass = GetShipMass();
    double force = GetPointToPointPlannedThrustN(direction);
    return Math.Min(Math.Max(0.1, pointToPointAccelerationLimit), force / mass);
}

void ApplyPointToPointAccel(Vector3D accel, bool emergency)
{
    if (emergency)
    {
        thrust.ApplyAccel(accel);
        return;
    }

    double maxForce = GetShipMass() * Math.Max(0.1, pointToPointAccelerationLimit);
    thrust.ApplyAccel(accel, maxForce);
}

Vector3D GetTargetOffsetWorld()
{
    return GetTargetOffsetWorld(_tickRcMatrix);
}

Vector3D GetTargetOffsetWorld(MatrixD wm)
{
    return wm.Right * TOFFR + wm.Up * TOFFU + wm.Forward * TOFFF;
}

Vector3D GetTargetControlPointPosition()
{
    MatrixD wm = _tickRcMatrix;
    return _tickRcPosition + GetTargetOffsetWorld(wm);
}

Vector3D GetDockConnectorOffsetWorld()
{
    return GetDockConnectorOffsetWorld(_tickRcMatrix);
}

Vector3D GetDockConnectorOffsetWorld(MatrixD wm)
{
    return wm.Right * connectorOffsetLocal.X + wm.Up * connectorOffsetLocal.Y + wm.Forward * connectorOffsetLocal.Z;
}

Vector3D GetHangarMinerCenterOffsetWorld(MatrixD wm)
{
    // Align the grid center with the hangar target, accounting for RC placement.
    return rc.CubeGrid.WorldAABB.Center - _tickRcPosition + wm.Forward * hangarMinerCenterForwardOffset;
}

Vector3D GetActiveTargetOffsetWorld()
{
    return GetActiveTargetOffsetWorld(_tickRcMatrix);
}

Vector3D GetActiveTargetOffsetWorld(MatrixD wm)
{
    if (dockingState != DockingState.None)
        return GetDockConnectorOffsetWorld(wm);
    if (hangarState == HangarState.MovingToHangar)
        return GetHangarMinerCenterOffsetWorld(wm);
    return GetTargetOffsetWorld(wm);
}

bool IsOrientationAligned(Vector3D forward, Vector3D up, double toleranceDeg)
{
    return IsOrientationAligned(_tickRcMatrix, forward, up, toleranceDeg);
}

bool IsOrientationAligned(MatrixD wm, Vector3D forward, Vector3D up, double toleranceDeg)
{
    double minDot = Math.Cos(Math.Max(0.1, toleranceDeg) * Math.PI / 180.0);
    double forwardLenSq = forward.LengthSquared();
    if (forwardLenSq > 0.001)
    {
        if (Math.Abs(forwardLenSq - 1) > 0.001)
            forward /= Math.Sqrt(forwardLenSq);
        if (Vector3D.Dot(wm.Forward, forward) < minDot)
            return false;
    }

    double upLenSq = up.LengthSquared();
    if (upLenSq > 0.001)
    {
        if (Math.Abs(upLenSq - 1) > 0.001)
            up /= Math.Sqrt(upLenSq);
        if (Vector3D.Dot(wm.Up, up) < minDot)
            return false;
    }

    return true;
}

double GetActiveArrivalTolerance()
{
    if (dockingState == DockingState.DockingFinalApproach)
        return Math.Max(0.05, dockConnectDistance);
    if (dockingState != DockingState.None || hangarState == HangarState.MovingToHangar)
        return Math.Max(0.05, dockPoseTolerance);
    if (miningEnabled)
        return Math.Max(0.05, miningArrivalTolerance);
    return Math.Max(0.05, arrivalTolerance);
}

bool IsReadyForHangarStorage()
{
    return IsAtTargetPose(hangarTargetPosition, hangarTargetForward, hangarTargetUp, GetActiveArrivalTolerance())
        && flightCache.LinearVelocity.LengthSquared() <= 0.05 * 0.05
        && flightCache.AngularVelocity.LengthSquared() <= 0.005 * 0.005;
}

bool TrySetPositiveDouble(string cmd, int skip, ref double field, string label, string unit = "m/s", double minVal = 0, string fmt = "0.00")
{
    double v;
    if (double.TryParse(cmd.Substring(skip).Trim(), out v) && v > minVal)
    {
        field = v;
        ScriptLog(label + " set to " + field.ToString(fmt) + " " + unit);
        return true;
    }

    ScriptLog("Invalid " + label + " value.");
    return false;
}

bool TrySetPercentDouble(string cmd, int skip, ref double field, string label)
{
    double v;
    if (double.TryParse(cmd.Substring(skip).Trim(), out v) && v >= 0 && v <= 100)
    {
        field = v;
        ScriptLog(label + " set to " + field.ToString("0.0") + "%");
        return true;
    }

    ScriptLog("Invalid " + label + " value. Use 0-100.");
    return false;
}

void SplitMissionCommand(string command, out string baseCommand, out string missionId)
{
    baseCommand = command ?? "";
    missionId = "";
    string[] parts = baseCommand.Split('|');
    if (parts.Length > 0)
        baseCommand = parts[0].Trim();
    if (parts.Length > 1)
        missionId = parts[1].Trim();
}
