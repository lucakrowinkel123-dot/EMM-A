void RefreshTickPose()
{
    carrierConnectionStatusValid = false;
    DisableVanillaRemoteAutopilot();
    flightCache.BeginInvocation(runtime);
    flightCache.SetThrustRefreshInterval(GetCarrierFlightCacheRefreshTicks());
    _tickRcMatrix = flightCache.Matrix;
    _tickRcPosition = flightCache.Position;
}

bool carrierConnectionStatusValid;
MyShipConnectorStatus cachedCarrierConnectionStatus;

MyShipConnectorStatus GetCarrierConnectorStatus()
{
    if (!carrierConnectionStatusValid)
    {
        cachedCarrierConnectionStatus = carrierConnector == null ? MyShipConnectorStatus.Unconnected : carrierConnector.Status;
        carrierConnectionStatusValid = true;
    }
    return cachedCarrierConnectionStatus;
}

void DisableVanillaRemoteAutopilot()
{
    if (rc != null && rc.IsAutoPilotEnabled)
        rc.SetAutoPilotEnabled(false);
}

void Move()
{
    movementResetApplied = false;
    if (MoveCruiseTransit()) return;
    MatrixD rcMatrix = _tickRcMatrix;
    Vector3D worldOffset = GetTargetOffsetWorld(rcMatrix);
    Vector3D rcPosition = _tickRcPosition;
    Vector3D position = rcPosition + worldOffset;
    Vector3D toTarget = targetPosition - position;
    double tolerance = Math.Max(0.05, arrivalTolerance);
    bool atPosition = toTarget.LengthSquared() < tolerance * tolerance;
    Vector3D forward = desiredForward;
    bool precisionTravelFacing = carrierAutopilotMode == CarrierAutopilotMode.Precision && desiredForward == Vector3D.Zero && !atPosition && toTarget.LengthSquared() > 0.001;
    if (precisionTravelFacing)
        forward = Vector3D.Normalize(toTarget);
    else if (forward == Vector3D.Zero && toTarget.LengthSquared() > 0.001)
        forward = Vector3D.Normalize(toTarget);
    Vector3D up = precisionTravelFacing ? Vector3D.Zero : desiredUp;
    // Once inside a dynamic waypoint's arrival radius, a tiny offset must not
    // reverse the facing target while we are still shedding the last drift.
    if (atPosition && carrierAutopilotMode == CarrierAutopilotMode.Precision
        && desiredForward == Vector3D.Zero && desiredUp == Vector3D.Zero)
    {
        bool holdTravelPose = carrierMotionPhase == CarrierMotionPhase.Translating && carrierBrakeForward != Vector3D.Zero;
        forward = holdTravelPose ? carrierBrakeForward : rcMatrix.Forward;
        up = holdTravelPose ? carrierBrakeUp : rcMatrix.Up;
    }
    bool freeRollFacing = precisionTravelFacing || carrierAutopilotMode != CarrierAutopilotMode.Precision;
    if (up == Vector3D.Zero && !freeRollFacing)
    {
        Vector3D gravity = flightCache.Gravity;
        if (gravity.LengthSquared() > 0.001)
            up = Vector3D.Normalize(-gravity);
        else
            up = rcMatrix.Up;
    }

    // Dynamic transit points only use their facing vector to make travel orderly;
    // they do not require the final parking pose's precision. A heavy Carrier can
    // otherwise oscillate across the normal 1-degree window forever without ever
    // being allowed to translate toward the waypoint.
    double movementOrientationToleranceDeg = orientationToleranceDeg;
    if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute && desiredForward == Vector3D.Zero && desiredUp == Vector3D.Zero)
        movementOrientationToleranceDeg = Math.Max(movementOrientationToleranceDeg, 5.0);
    bool aligned = IsOrientationAligned(rcMatrix, forward, up, movementOrientationToleranceDeg);
    bool translationStopped = flightCache.LinearVelocity.LengthSquared() <= carrierStoppedSpeedMps * carrierStoppedSpeedMps;
    bool rotationStopped = flightCache.AngularVelocity.LengthSquared() <= carrierStoppedAngularSpeedRad * carrierStoppedAngularSpeedRad;
    if (carrierMotionPhase == CarrierMotionPhase.BrakingBeforeTurn)
    {
        if (carrierBrakeForward == Vector3D.Zero)
        {
            carrierBrakeForward = rcMatrix.Forward;
            carrierBrakeUp = rcMatrix.Up;
        }

        // Hold the attitude that existed when braking began. The Carrier must
        // shed all translational motion before it is allowed to turn.
        gyros.FaceVectors(rcMatrix, carrierBrakeForward, carrierBrakeUp);
        ApplyCarrierAccel(thrust.ControlVelocity(Vector3D.Zero), true);
        if (translationStopped && rotationStopped)
        {
            thrust.Reset();
            carrierTurnSettledTicks = 0;
            gyros.ResetController();
            carrierMotionPhase = CarrierMotionPhase.Turning;
        }

        return;
    }

    if (carrierMotionPhase == CarrierMotionPhase.Turning)
    {
        // Counter any drift while turning; this command targets zero velocity
        // and never deliberately translates the Carrier.
        ApplyCarrierAccel(thrust.ControlVelocity(Vector3D.Zero), true);
        gyros.FaceVectors(rcMatrix, forward, up);
        if (aligned && rotationStopped && translationStopped)
        {
            // Require consecutive settled samples, not a single quiet frame
            // at the reversal of an oscillation. Keep the full half-second
            // between first and last sample, also at Update10/Update100.
            if (carrierTurnSettledTicks >= carrierTurnSettleTicks)
            {
                carrierBrakeForward = rcMatrix.Forward;
                carrierBrakeUp = rcMatrix.Up;
                gyros.ResetController();
                // Replace this invocation's target-facing command immediately.
                gyros.FaceVectors(rcMatrix, carrierBrakeForward, carrierBrakeUp);
                thrust.Reset();
                carrierMotionPhase = CarrierMotionPhase.Translating;
            }
            else
                carrierTurnSettledTicks = Math.Min(carrierTurnSettleTicks, carrierTurnSettledTicks + UpdateTickDelta());
        }
        else
            carrierTurnSettledTicks = 0;

        return;
    }

    // A wider release threshold than the settled threshold prevents chatter.
    double rotationReleaseSpeed = carrierStoppedAngularSpeedRad * 2;
    if (!aligned || flightCache.AngularVelocity.LengthSquared() > rotationReleaseSpeed * rotationReleaseSpeed)
    {
        BeginCarrierBrakeBeforeTurn(rcMatrix);
        ApplyCarrierAccel(thrust.ControlVelocity(Vector3D.Zero), true);
        return;
    }

    // Hold the attained attitude; do not restart a target-facing turn while
    // accelerating merely because transit accepts a small heading error.
    gyros.FaceVectors(rcMatrix, carrierBrakeForward, carrierBrakeUp);
    if (carrierAutopilotMode != CarrierAutopilotMode.Precision)
    {
        MoveCruise(rcMatrix, rcPosition, position, toTarget, forward, up, aligned);
        return;
    }

    if (atPosition)
    {
        if (!translationStopped)
            ApplyCarrierAccel(thrust.ControlVelocity(Vector3D.Zero), true);
        else
        {
            thrust.Reset();
            gyros.Reset();
        }

        return;
    }

    double maxSpeedNow = GetPrecisionSafeSpeed(toTarget);
    if (activeMaxSpeed > 0)
        maxSpeedNow = Math.Min(maxSpeedNow, activeMaxSpeed);
    maxSpeedNow = Math.Min(maxSpeedNow, maxSpeed);
    Vector3D rcTarget = targetPosition - worldOffset;
    Vector3D accel = thrust.ControlPositionFrom(rcPosition, rcTarget, Vector3D.Zero, maxSpeedNow);
    ApplyCarrierAccel(accel, ShouldUseEmergencyThrust(toTarget));
}

void MoveCruise(
    MatrixD rcMatrix,
    Vector3D rcPosition,
    Vector3D position,
    Vector3D toTarget,
    Vector3D forward,
    Vector3D up,
    bool aligned)
{
    if (carrierAutopilotMode == CarrierAutopilotMode.CruiseTurnTest || carrierAutopilotMode == CarrierAutopilotMode.CruiseFlip)
    {
        thrust.Reset();
        return;
    }

    if (!aligned)
    {
        thrust.Reset();
        return;
    }

    if (cruiseDirection.LengthSquared() < 0.001)
    {
        rc.DampenersOverride = true;
        carrierAutopilotMode = CarrierAutopilotMode.Precision;
        return;
    }

    Vector3D dir = Vector3D.Normalize(cruiseDirection);
    if (carrierAutopilotMode == CarrierAutopilotMode.CruiseDecel)
    {
        Vector3D vel = flightCache.LinearVelocity;
        if (vel.LengthSquared() < 0.01)
        {
            thrust.Reset();
            return;
        }

        ApplyCarrierAccel(thrust.ControlVelocity(Vector3D.Zero), ShouldUseCruiseEmergencyThrust());
    }
    else
    {
        double accelMag = GetPlannedAcceleration(-dir);
        if (accelMag <= 0)
        {
            thrust.Reset();
            return;
        }

        ApplyCarrierAccel(-dir * accelMag, false);
    }
}

double GetPrecisionSafeSpeed(Vector3D toTarget)
{
    double distance = Math.Max(0, toTarget.Length() - Math.Max(0.05, arrivalTolerance));
    if (distance <= 0)
        return 0;
    Vector3D dir = Vector3D.Normalize(toTarget);
    // Thruster Forward is exhaust direction: braking toward dir uses that same dir.
    double brakeAccel = GetPlannedAcceleration(dir);
    if (brakeAccel <= 0)
        return 0;
    // Reserve two seconds of response distance for the velocity controller;
    // sqrt(2*a*d) alone assumes full braking appears instantly.
    double responseSpeed = brakeAccel * 2.0;
    double brakingTerm = 2 * brakeAccel * distance;
    return brakingTerm / (Math.Sqrt(responseSpeed * responseSpeed + brakingTerm) + responseSpeed);
}

bool ShouldUseEmergencyThrust(Vector3D toTarget)
{
    Vector3D vel = flightCache.LinearVelocity;
    double dist = Math.Max(0, toTarget.Length() - Math.Max(0.05, arrivalTolerance));
    if (dist <= 0 || vel.LengthSquared() < 0.01 || toTarget.LengthSquared() < 0.01)
        return false;
    Vector3D dir = Vector3D.Normalize(toTarget);
    double speedToward = Vector3D.Dot(vel, dir);
    if (speedToward <= 0)
        return false;
    double accel = GetPlannedAcceleration(dir);
    if (accel <= 0)
        return false;
    return (speedToward * speedToward) / (2 * accel) > dist;
}

bool ShouldUseCruiseEmergencyThrust()
{
    if (cruiseDirection.LengthSquared() < 0.001)
        return false;
    Vector3D dir = Vector3D.Normalize(cruiseDirection);
    Vector3D toStage = cruiseStagingPosition - GetControlledPosition();
    double dist = Math.Max(0, Vector3D.Dot(toStage, dir));
    double speed = Vector3D.Dot(flightCache.LinearVelocity, dir);
    if (dist <= 0 || speed <= 0)
        return true;
    double accel = GetPlannedAcceleration(dir);
    if (accel <= 0)
        return false;
    return (speed * speed) / (2 * accel) > dist;
}

double GetShipMass()
{
    return flightCache.Mass;
}

double GetEffectiveThrustInDirection(Vector3D direction)
{
    return flightCache.EffectiveThrust(direction);
}

int GetCarrierFlightCacheRefreshTicks()
{
    return emma3MissionState == Emma3MissionState.FlyingCarrierRoute ? carrierFlightCacheTransitRefreshTicks : carrierFlightCacheNormalRefreshTicks;
}

void InvalidateCarrierFlightCache()
{
    flightCache.SetThrustRefreshInterval(GetCarrierFlightCacheRefreshTicks());
    flightCache.Invalidate();
}

void BeginCarrierTransitFlightCache()
{
    InvalidateCarrierFlightCache();
}

void EndCarrierTransitFlightCache()
{
    InvalidateCarrierFlightCache();
}

double GetDetectedMaxCarrierThrustN()
{
    MatrixD m = rc == null ? _tickRcMatrix : _tickRcMatrix;
    double max = 0;
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Forward));
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Backward));
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Left));
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Right));
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Up));
    max = Math.Max(max, GetEffectiveThrustInDirection(m.Down));
    return max;
}

string FormatCompactThrustN(double thrustN)
{
    double abs = Math.Abs(thrustN);
    double scaled;
    string unit;
    if (abs >= 1000000000.0)
    {
        scaled = thrustN / 1000000000.0;
        unit = "GN";
    }
    else if (abs >= 1000000.0)
    {
        scaled = thrustN / 1000000.0;
        unit = "MN";
    }
    else
    {
        scaled = thrustN / 1000.0;
        unit = "kN";
    }

    return scaled.ToString("000.0").Replace(',', '.') + " " + unit;
}

double GetHardThrustLimitN()
{
    return Math.Max(1, thrustLimitKn) * 1000.0;
}

double GetPlannedThrustLimitN()
{
    return GetHardThrustLimitN() * MathHelper.Clamp(thrustUsePercent, 1, 100) / 100.0;
}

double GetPlannedAcceleration(Vector3D direction)
{
    double mass = GetShipMass();
    double force = Math.Min(GetEffectiveThrustInDirection(direction), GetPlannedThrustLimitN());
    return Math.Min(Math.Max(0.1, accelerationLimit), force / mass);
}

void ApplyCarrierAccel(Vector3D accel, bool emergency)
{
    double mass = GetShipMass();
    double maxForce = emergency ? GetHardThrustLimitN() : GetPlannedThrustLimitN();
    if (!emergency)
        maxForce = Math.Min(maxForce, mass * Math.Max(0.1, accelerationLimit));
    thrust.ApplyAccel(accel, maxForce);
}

Vector3D GetControlledPosition()
{
    return _tickRcPosition + GetTargetOffsetWorld(_tickRcMatrix);
}

void UpdateCarrierConnectorMassWatch()
{
    if (carrierConnector == null)
        return;
    bool connected = GetCarrierConnectorStatus() == MyShipConnectorStatus.Connected;
    if (!carrierConnectorStateKnown)
    {
        carrierConnectorStateKnown = true;
        lastCarrierConnectorConnected = connected;
        return;
    }

    if (connected == lastCarrierConnectorConnected)
        return;
    lastCarrierConnectorConnected = connected;
    InvalidateCarrierFlightCache();
    // Refresh flight data; calibration validity depends on actual mass and gyro strength.
}

Vector3D GetTargetOffsetWorld()
{
    return GetTargetOffsetWorld(_tickRcMatrix);
}

Vector3D GetTargetOffsetWorld(MatrixD wm)
{
    return wm.Right * TOFFR + wm.Up * TOFFU + wm.Forward * TOFFF;
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

bool IsAtTargetPosition(Vector3D target, double toleranceMeters = -1)
{
    double baseTolerance = toleranceMeters > 0 ? toleranceMeters : arrivalTolerance;
    double tolerance = Math.Max(0.05, baseTolerance);
    return Vector3D.DistanceSquared(_tickRcPosition + GetTargetOffsetWorld(_tickRcMatrix), target) < tolerance * tolerance;
}

bool IsAtTargetPose(Vector3D target, Vector3D forward, Vector3D up, double toleranceMeters = -1)
{
    return IsAtTargetPosition(target, toleranceMeters) && IsOrientationAligned(forward, up, orientationToleranceDeg);
}
