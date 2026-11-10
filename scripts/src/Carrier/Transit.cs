bool cruiseCalibrationStarted, cruiseHeadingReady;
double cruiseTurnElapsedSeconds, cruiseFlipElapsedSeconds, measuredFlipGyroAuthority;
double cruiseCalibrationMass, cruiseCalibrationGyroAuthority;
Vector3D cruiseTurnUp;

void StartPrecisionTarget(Vector3D pos, Vector3D forward, Vector3D up)
{
    rc.DampenersOverride = true;
    targetPosition = pos; desiredForward = forward; desiredUp = up;
    hasTarget = true; autopilotEnabled = true;
    carrierAutopilotMode = CarrierAutopilotMode.Precision;
    BeginCarrierMotionSequence();
    Runtime.UpdateFrequency = tickSpeed;
}

double GetCruiseGyroAuthority()
{
    double authority = 0;
    foreach (var sample in flightCache.GyroSamples)
        if (sample.Block != null && sample.Block.Enabled && sample.Block.IsWorking)
            authority += sample.Block.GyroPower;
    return authority;
}

bool IsCruiseTurnMeasurementValid()
{
    return measuredFlipSeconds > 0 && measuredFlipMass > 0 && measuredFlipGyroAuthority > 0
        && GetShipMass() >= measuredFlipMass / 3 && GetShipMass() <= measuredFlipMass * 3
        && Math.Abs(GetCruiseGyroAuthority() - measuredFlipGyroAuthority) <= measuredFlipGyroAuthority * 0.05;
}

double GetCruisePlannedTurnSeconds()
{
    if (measuredFlipMass <= 0 || measuredFlipSeconds <= 0) return 120;
    // Existing saved values include the x2 allowance and fixed two-second margin.
    // Scale the measured part both ways, leaving the fixed margin unchanged.
    return Math.Max(0, measuredFlipSeconds - 2) * (GetShipMass() / measuredFlipMass) + 2;
}

void RecordCruiseTurnAllowance(double currentMassAllowance)
{
    if (measuredFlipMass <= 0 || measuredFlipSeconds <= 0) return;
    double ratio = GetShipMass() / measuredFlipMass;
    if (ratio <= 0) return;
    // Store observations at the original calibration mass to avoid scaling twice.
    measuredFlipSeconds = Math.Max(measuredFlipSeconds, Math.Max(0, currentMassAllowance - 2) / ratio + 2);
}

double GetCruiseRequiredDistance(double speed, double flipSeconds, double brakeAccel)
{
    if (brakeAccel <= 0 || double.IsNaN(brakeAccel) || double.IsInfinity(brakeAccel)) return double.MaxValue;
    speed = Math.Max(0, speed);
    return (speed * Math.Max(0, flipSeconds) + speed * speed / (2 * brakeAccel)) * 1.25
        + speed * 2 + Math.Max(5, arrivalTolerance);
}

double GetCruiseProfileTime(double distance, double driveAccel, double brakeAccel, double turnSeconds)
{
    if (driveAccel <= 0 || brakeAccel <= 0) return double.MaxValue;
    distance = Math.Max(0, distance);
    if (distance <= 0) return 0;
    double inverse = 1 / driveAccel + 1 / brakeAccel;
    double turn = Math.Max(0, turnSeconds);
    // d = v^2/2 * (1/a_drive + 1/a_brake) + v*t_turn.
    double peak = 2 * distance / (Math.Sqrt(turn * turn + 2 * distance * inverse) + turn);
    double limit = Math.Max(0.1, activeMaxSpeed > 0 ? Math.Min(maxSpeed, activeMaxSpeed) : maxSpeed);
    double velocity = Math.Min(peak, limit);
    double poweredDistance = velocity * velocity * inverse / 2;
    double coastDistance = Math.Max(0, distance - poweredDistance - velocity * turn);
    return velocity * inverse + turn + coastDistance / velocity;
}

void StartCruiseTarget(Vector3D finalPos, Vector3D finalForward, Vector3D finalUp)
{
    Vector3D offset = finalPos - GetControlledPosition();
    double distance = offset.Length();
    if (distance <= Math.Max(5, arrivalTolerance) || flightCache.Gravity.LengthSquared() > 0.001)
    { StartPrecisionTarget(finalPos, finalForward, finalUp); return; }
    Vector3D dir = offset / distance;
    double drive = GetPlannedAcceleration(-_tickRcMatrix.Forward);
    double reverse = GetPlannedAcceleration(_tickRcMatrix.Forward);
    double turn = IsCruiseTurnMeasurementValid() ? GetCruisePlannedTurnSeconds() : 120;
    // An unmeasured estimate only decides whether testing is worthwhile.
    // It never authorizes an acceleration burn without calibration.
    if (GetCruiseGyroAuthority() <= 0 || drive <= 0
        || GetCruiseProfileTime(distance, drive, drive, turn) >= GetCruiseProfileTime(distance, drive, reverse, 0))
    { StartPrecisionTarget(finalPos, finalForward, finalUp); return; }
    cruiseFinalPosition = finalPos; cruiseFinalForward = finalForward; cruiseFinalUp = finalUp;
    cruiseDirection = dir;
    double clearance = Math.Min(Math.Max(25, cruiseOffsetMeters), distance * 0.1);
    cruiseStagingPosition = finalPos - dir * clearance;
    targetPosition = cruiseStagingPosition; desiredForward = dir; desiredUp = Vector3D.Zero;
    hasTarget = true; autopilotEnabled = true;
    Runtime.UpdateFrequency = tickSpeed; rc.DampenersOverride = false;
    cruiseCalibrationStarted = false; cruiseHeadingReady = false;
    cruiseTurnElapsedSeconds = 0; cruiseFlipElapsedSeconds = 0; carrierTurnSettledTicks = 0;
    carrierBrakeForward = _tickRcMatrix.Forward; carrierBrakeUp = _tickRcMatrix.Up;
    gyros.ResetController();
    carrierAutopilotMode = IsCruiseTurnMeasurementValid() ? CarrierAutopilotMode.CruiseAccel : CarrierAutopilotMode.CruiseTurnTest;
    if (flightCache.LinearVelocity.LengthSquared() > carrierStoppedSpeedMps * carrierStoppedSpeedMps) BeginCruiseMovingFlip();
    ScriptLog("Transit: adaptive main-engine braking selected.");
}

void UpdateCarrierAutopilotRunner()
{
    // Advance transit in Move, using the same fresh sample as actuator control.
}

bool IsCruiseAttitudeSettled(Vector3D forward, Vector3D up)
{
    bool quiet = IsOrientationAligned(_tickRcMatrix, forward, up, orientationToleranceDeg)
        && flightCache.AngularVelocity.LengthSquared() <= carrierStoppedAngularSpeedRad * carrierStoppedAngularSpeedRad;
    if (!quiet) { carrierTurnSettledTicks = 0; return false; }
    if (carrierTurnSettledTicks >= carrierTurnSettleTicks) return true;
    carrierTurnSettledTicks = Math.Min(carrierTurnSettleTicks, carrierTurnSettledTicks + UpdateTickDelta());
    return false;
}

Vector3D GetCruiseTurnUp(Vector3D forward, Vector3D preferredUp)
{
    Vector3D up = preferredUp - forward * Vector3D.Dot(preferredUp, forward);
    if (up.LengthSquared() < 0.001)
    {
        Vector3D fallback = Math.Abs(Vector3D.Dot(forward, Vector3D.Up)) < 0.9 ? Vector3D.Up : Vector3D.Right;
        up = fallback - forward * Vector3D.Dot(fallback, forward);
    }
    return Vector3D.Normalize(up);
}

void BeginCruiseMovingFlip()
{
    carrierAutopilotMode = CarrierAutopilotMode.CruiseFlip;
    cruiseFlipElapsedSeconds = 0; carrierTurnSettledTicks = 0;
    cruiseTurnUp = GetCruiseTurnUp(Vector3D.Normalize(flightCache.LinearVelocity), _tickRcMatrix.Up);
    gyros.ResetController(); thrust.Reset(); rc.DampenersOverride = false;
    ScriptLog("Transit: coasting through braking flip.");
}

bool MoveCruiseTransit()
{
    if (carrierAutopilotMode == CarrierAutopilotMode.Precision) return false;
    double dt = Math.Max(1.0 / 60, Runtime.TimeSinceLastRun.TotalSeconds);
    Vector3D velocity = flightCache.LinearVelocity;
    double speed = velocity.Length();
    if (cruiseDirection.LengthSquared() < 0.001) cruiseDirection = _tickRcMatrix.Forward;
    if (flightCache.Gravity.LengthSquared() > 0.001 || GetCruiseGyroAuthority() <= 0)
    { StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp); return true; }
    rc.DampenersOverride = false;
    if (carrierAutopilotMode == CarrierAutopilotMode.CruiseTurnTest)
    {
        if (!cruiseCalibrationStarted)
        {
            if (speed > carrierStoppedSpeedMps) { BeginCruiseMovingFlip(); return true; }
            cruiseCalibrationStarted = true; cruiseTurnElapsedSeconds = 0;
            cruiseCalibrationMass = GetShipMass(); cruiseCalibrationGyroAuthority = GetCruiseGyroAuthority();
            desiredForward = -_tickRcMatrix.Forward; // Full 180 degrees from the actual starting pose.
            cruiseTurnUp = _tickRcMatrix.Up; carrierTurnSettledTicks = 0;
            gyros.ResetController();
            ScriptLog("Transit: timing a complete 180-degree turn and settling.");
        }
        thrust.Reset(); gyros.FaceVectors(_tickRcMatrix, desiredForward, cruiseTurnUp);
        cruiseTurnElapsedSeconds += dt;
        if (speed > carrierStoppedSpeedMps || cruiseTurnElapsedSeconds > 300
            || Math.Abs(GetShipMass() - cruiseCalibrationMass) > cruiseCalibrationMass * 0.05
            || Math.Abs(GetCruiseGyroAuthority() - cruiseCalibrationGyroAuthority) > cruiseCalibrationGyroAuthority * 0.05)
        {
            measuredFlipSeconds = 0;
            StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp);
            ScriptLog("Transit: calibration interrupted; using precision flight.");
            return true;
        }
        if (IsCruiseAttitudeSettled(desiredForward, cruiseTurnUp))
        {
            measuredFlipSeconds = cruiseTurnElapsedSeconds * 2 + 2;
            measuredFlipMass = GetShipMass(); measuredFlipGyroAuthority = GetCruiseGyroAuthority();
            carrierAutopilotMode = CarrierAutopilotMode.CruiseAccel;
            cruiseHeadingReady = false; carrierTurnSettledTicks = 0; gyros.ResetController();
            RequestCarrierSave(true);
            ScriptLog("Transit: full turn took " + Number2(cruiseTurnElapsedSeconds) + " s; planning with " + Number2(measuredFlipSeconds) + " s.");
        }
        return true;
    }
    if (carrierAutopilotMode == CarrierAutopilotMode.CruiseAccel)
    {
        Vector3D dir = Vector3D.Normalize(cruiseDirection);
        if (!cruiseHeadingReady)
        {
            if (speed > carrierStoppedSpeedMps) { BeginCruiseMovingFlip(); return true; }
            thrust.Reset(); desiredForward = dir;
            cruiseTurnUp = GetCruiseTurnUp(dir, _tickRcMatrix.Up);
            gyros.FaceVectors(_tickRcMatrix, dir, cruiseTurnUp);
            if (IsCruiseAttitudeSettled(dir, cruiseTurnUp))
            { cruiseHeadingReady = true; carrierBrakeUp = _tickRcMatrix.Up; gyros.ResetController(); }
            return true;
        }
        double drive = GetPlannedAcceleration(-_tickRcMatrix.Forward);
        double remaining = Vector3D.Dot(cruiseStagingPosition - GetControlledPosition(), dir);
        double along = Math.Max(0, Vector3D.Dot(velocity, dir));
        double flipTime = IsCruiseTurnMeasurementValid() ? GetCruisePlannedTurnSeconds() : Math.Max(120, GetCruisePlannedTurnSeconds() * 2);
        if (!IsCruiseTurnMeasurementValid() || !IsOrientationAligned(_tickRcMatrix, dir, Vector3D.Zero, 5)
            || flightCache.AngularVelocity.LengthSquared() > 0.0004
            || remaining <= GetCruiseRequiredDistance(speed, flipTime, drive))
        {
            if (speed <= carrierStoppedSpeedMps) StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp);
            else BeginCruiseMovingFlip();
            return true;
        }
        gyros.FaceVectors(_tickRcMatrix, dir, carrierBrakeUp);
        double limit = activeMaxSpeed > 0 ? Math.Min(maxSpeed, activeMaxSpeed) : maxSpeed;
        // Thruster commands point along the exhaust, opposite the desired acceleration.
        ApplyCarrierAccel(-dir * Math.Min(drive, Math.Max(0, limit - along) / dt), false);
        return true;
    }
    if (carrierAutopilotMode == CarrierAutopilotMode.CruiseFlip)
    {
        thrust.Reset(); // Rotate while coasting, without the old stop-before-turn gate.
        if (speed <= carrierStoppedSpeedMps)
        { StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp); return true; }
        desiredForward = -velocity / speed;
        if (cruiseTurnUp.LengthSquared() < 0.001) cruiseTurnUp = GetCruiseTurnUp(desiredForward, _tickRcMatrix.Up);
        gyros.FaceVectors(_tickRcMatrix, desiredForward, cruiseTurnUp);
        cruiseFlipElapsedSeconds += dt;
        double angle = AngleBetween(_tickRcMatrix.Forward, desiredForward);
        double angularSpeed = flightCache.AngularVelocity.Length();
        if (cruiseFlipElapsedSeconds > GetCruisePlannedTurnSeconds() && measuredFlipSeconds > 0)
            RecordCruiseTurnAllowance(cruiseFlipElapsedSeconds + Math.Max(10, angularSpeed > 0.001 ? angle / angularSpeed * 1.5 : 60));
        if (IsCruiseAttitudeSettled(desiredForward, cruiseTurnUp))
        {
            RecordCruiseTurnAllowance(cruiseFlipElapsedSeconds * 2 + 2);
            carrierAutopilotMode = CarrierAutopilotMode.CruiseDecel; gyros.ResetController();
            RequestCarrierSave(true);
            ScriptLog("Transit: main-engine braking aligned.");
        }
        else if (cruiseFlipElapsedSeconds > 300)
        { StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp); ScriptLog("Transit: turn failed; using auxiliary braking."); }
        return true;
    }
    if (speed <= carrierStoppedSpeedMps)
    { StartPrecisionTarget(cruiseFinalPosition, cruiseFinalForward, cruiseFinalUp); return true; }
    desiredForward = -velocity / speed;
    gyros.FaceVectors(_tickRcMatrix, desiredForward, cruiseTurnUp);
    if (!IsOrientationAligned(_tickRcMatrix, desiredForward, Vector3D.Zero, orientationToleranceDeg)
        || flightCache.AngularVelocity.LengthSquared() > 0.0004)
    { BeginCruiseMovingFlip(); return true; }
    // Turning early creates a safety reserve. Coast inside that reserve, then
    // follow a capped braking envelope rather than stopping kilometres short.
    Vector3D direction = velocity / speed;
    double distanceToStage = Math.Max(0, Vector3D.Dot(cruiseStagingPosition - GetControlledPosition(), direction));
    double availableBrake = GetPlannedAcceleration(direction);
    double response = availableBrake * 2;
    double brakingTerm = 2 * availableBrake * distanceToStage;
    double safeSpeed = availableBrake > 0 ? brakingTerm / (Math.Sqrt(response * response + brakingTerm) + response) : 0;
    if (speed <= safeSpeed) thrust.Reset();
    else ApplyCarrierAccel(thrust.ControlVelocity(direction * safeSpeed), false);
    return true;
}

double AngleBetween(Vector3D a, Vector3D b)
{
    if (a.LengthSquared() < 0.001 || b.LengthSquared() < 0.001) return 0;
    return Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(a), Vector3D.Normalize(b)), -1, 1));
}
