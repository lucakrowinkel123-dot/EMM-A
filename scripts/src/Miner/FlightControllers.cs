class GyroControl
{
    private List<IMyGyro> gyros;
    FlightDataCache flight;
    public GyroControl(FlightDataCache flight, UpdateFrequency tickSpeed, List<IMyGyro> gyros)
    {
        if (flight == null)
            throw new Exception("Reference block null.");
        this.flight = flight;
        this.gyros = gyros;
        double factor = 1;
        if (tickSpeed == UpdateFrequency.Update10)
            factor = 10;
        else if (tickSpeed == UpdateFrequency.Update100)
            factor = 100;
        double secondsPerTick = (1.0 / 60) * factor;
        anglePID = new VectorPID(gyroP / factor, gyroI / factor, gyroD / factor, -gyroMax, gyroMax, secondsPerTick);
        Reset();
    }

    // In (pitch, yaw, roll)
    VectorPID anglePID;
    public void ResetControllerHistory() { anglePID.Reset(); }
    public void Reset()
    {
        flight.ResetGyros();
        anglePID.Reset();
    }

    /// <summary>
    /// Prioritizes up vector.
    /// </summary>
    Vector3D GetAngles2(MatrixD current, Vector3D forward, Vector3D up)
    {
        Vector3D error = new Vector3D();
        // yaw
        if (forward != Vector3D.Zero)
        {
            // Defer yaw when its projection vanishes; the up-vector turn resolves it.
            error.Y = SignedAngleOnPlane(current.Forward, forward, current.Up, current.Right, true);
        }

        // pitch and roll
        if (up != Vector3D.Zero)
        {
            error.X = SignedAngleOnPlane(current.Up, up, current.Right, current.Forward, true);
            error.Z = SignedAngleOnPlane(current.Up, up, current.Forward, current.Right, true);
        }

        if (Math.Abs(error.X) < 0.001)
            error.X = 0;
        if (Math.Abs(error.Y) < 0.001)
            error.Y = 0;
        if (Math.Abs(error.Z) < 0.001)
            error.Z = 0;
        return error;
    }

    /// <summary>
    /// Prioritizes forward vector.
    /// </summary>
    Vector3D GetAngles(MatrixD current, Vector3D forward, Vector3D up)
    {
        Vector3D error = new Vector3D();
        if (forward != Vector3D.Zero)
        {
            Vector3D normalizedForward = Vector3D.Normalize(forward);
            double forwardDot = MathHelper.Clamp(Vector3D.Dot(current.Forward, normalizedForward), -1, 1);
            if (forwardDot < -0.9995)
            {
                // At exactly 180 degrees, pitch and yaw are both mathematically valid.
                // Choose one deterministic axis so the controller cannot command both
                // and spin diagonally while the travel target moves around the grid.
                error.Y = Math.PI;
            }
            else
            {
                error.X = SignedAngleOnPlane(current.Forward, normalizedForward, current.Right, current.Up, false);
                error.Y = SignedAngleOnPlane(current.Forward, normalizedForward, current.Up, current.Right, true);
            }
        }

        if (up != Vector3D.Zero)
        {
            // Defer roll when its projection vanishes; the forward turn resolves it.
            error.Z = SignedAngleOnPlane(current.Up, up, current.Forward, current.Right, true);
        }

        if (Math.Abs(error.X) < 0.001)
            error.X = 0;
        if (Math.Abs(error.Y) < 0.001)
            error.Y = 0;
        if (Math.Abs(error.Z) < 0.001)
            error.Z = 0;
        return error;
    }

    /// <summary>
    /// Prioritizes up vector.
    /// </summary>
    public void FaceVectors2(Vector3D forward, Vector3D up)
    {
        FaceVectors2(flight.Matrix, forward, up);
    }

    public void FaceVectors2(MatrixD current, Vector3D forward, Vector3D up)
    {
        // In (pitch, yaw, roll)
        Vector3D error = -GetAngles2(current, forward, up);
        Vector3D angles = new Vector3D(anglePID.Control(error));
        ApplyFineDamping(error, ref angles);
        ApplyGyroOverride(current, angles);
    }

    /// <summary>
    /// Prioritizes forward vector.
    /// </summary>
    public void FaceVectors(Vector3D forward, Vector3D up)
    {
        FaceVectors(flight.Matrix, forward, up);
    }

    public void FaceVectors(MatrixD current, Vector3D forward, Vector3D up)
    {
        // In (pitch, yaw, roll)
        Vector3D error = -GetAngles(current, forward, up);
        Vector3D angles = new Vector3D(anglePID.Control(error));
        ApplyFineDamping(error, ref angles);
        ApplyGyroOverride(current, angles);
    }

    void ApplyFineDamping(Vector3D error, ref Vector3D angles)
    {
        if (Math.Abs(error.X) < gyroErrorDeadbandRad)
            angles.X = 0;
        if (Math.Abs(error.Y) < gyroErrorDeadbandRad)
            angles.Y = 0;
        if (Math.Abs(error.Z) < gyroErrorDeadbandRad)
            angles.Z = 0;
        double errMag = error.Length();
        if (errMag < gyroFineDampRangeRad)
        {
            double s = MathHelper.Clamp(errMag / gyroFineDampRangeRad, 0.2, 1.0);
            angles *= s;
        }

        if (Math.Abs(angles.X) < gyroOutputDeadband)
            angles.X = 0;
        if (Math.Abs(angles.Y) < gyroOutputDeadband)
            angles.Y = 0;
        if (Math.Abs(angles.Z) < gyroOutputDeadband)
            angles.Z = 0;
        angles.X = MathHelper.Clamp(angles.X, -gyroRateLimit, gyroRateLimit);
        angles.Y = MathHelper.Clamp(angles.Y, -gyroRateLimit, gyroRateLimit);
        angles.Z = MathHelper.Clamp(angles.Z, -gyroRateLimit, gyroRateLimit);
    }

    void ApplyGyroOverride(MatrixD current, Vector3D localAngles)
    {
        Vector3D worldAngles = Vector3D.TransformNormal(localAngles, current);
        Vector3D rcAngles = Vector3D.TransformNormal(worldAngles, flight.InverseMatrix);
        foreach (FlightDataCache.GyroSample sample in flight.GyroSamples)
        {
            IMyGyro gyro = sample.Block;
            if (gyro == null)
                continue;
            Vector3D transVect = flight.GyroCommand(sample, worldAngles, rcAngles); //Converts To Gyro Local
            if (!transVect.IsValid())
                throw new Exception("Invalid trans vector. " + transVect.ToString());
            flight.ApplyGyro(sample, transVect);
        }
    }

    /// <summary>
    /// Projects a value onto another vector.
    /// </summary>
    /// <param name = "guide">Must be of length 1.</param>
    public static double ScalerProjection(Vector3D value, Vector3D guide)
    {
        double returnValue = Vector3D.Dot(value, guide);
        if (double.IsNaN(returnValue))
            return 0;
        return returnValue;
    }

    public static double SignedAngleOnPlane(Vector3D from, Vector3D to, Vector3D planeNormal, Vector3D signGuide, bool negateWhenPositive)
    {
        Vector3D flat = VectorRejection(to, planeNormal);
        if (flat.LengthSquared() < 1e-12)
            return 0;
        flat = Vector3D.Normalize(flat);
        double dot = MathHelper.Clamp(Vector3D.Dot(from, flat), -1, 1);
        double angle = Math.Acos(dot);
        double scaler = ScalerProjection(flat, signGuide);
        if ((negateWhenPositive && scaler > 0) || (!negateWhenPositive && scaler < 0))
            angle *= -1;
        return angle;
    }

    /// <summary>
    /// Projects a value onto another vector.
    /// </summary>
    /// <param name = "guide">Must be of length 1.</param>
    public static Vector3D VectorPojection(Vector3D value, Vector3D guide)
    {
        return ScalerProjection(value, guide) * guide;
    }

    /// <summary>
    /// Projects a value onto another vector.
    /// </summary>
    /// <param name = "guide">Must be of length 1.</param>
    public static Vector3D VectorRejection(Vector3D value, Vector3D guide)
    {
        return value - VectorPojection(value, guide);
    }
}

//Whip's PID controller class v6 - 11/22/17
public class PID
{
    double _kP = 0;
    double _kI = 0;
    double _kD = 0;
    double _integralDecayRatio = 0;
    double _lowerBound = 0;
    double _upperBound = 0;
    double _timeStep = 0;
    double _inverseTimeStep = 0;
    double _errorSum = 0;
    double _lastError = 0;
    bool _firstRun = true;
    bool _integralDecay = false;
    public double Value
    {
        get; private set;
    }

    public PID(double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
    {
        _kP = kP;
        _kI = kI;
        _kD = kD;
        _lowerBound = lowerBound;
        _upperBound = upperBound;
        _timeStep = timeStep;
        _inverseTimeStep = 1 / _timeStep;
        _integralDecay = false;
    }

    public PID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
    {
        _kP = kP;
        _kI = kI;
        _kD = kD;
        _timeStep = timeStep;
        _inverseTimeStep = 1 / _timeStep;
        _integralDecayRatio = integralDecayRatio;
        _integralDecay = true;
    }

    public double Control(double error)
    {
        //Compute derivative term
        var errorDerivative = (error - _lastError) * _inverseTimeStep;
        if (_firstRun)
        {
            errorDerivative = 0;
            _firstRun = false;
        }

        //Compute integral term
        if (!_integralDecay)
        {
            _errorSum += error * _timeStep;
            //Clamp integral term
            if (_errorSum > _upperBound)
                _errorSum = _upperBound;
            else if (_errorSum < _lowerBound)
                _errorSum = _lowerBound;
        }
        else
        {
            _errorSum = _errorSum * (1.0 - _integralDecayRatio) + error * _timeStep;
        }

        //Store this error as last error
        _lastError = error;
        //Construct output
        this.Value = _kP * error + _kI * _errorSum + _kD * errorDerivative;
        return this.Value;
    }

    public double Control(double error, double timeStep)
    {
        _timeStep = timeStep;
        _inverseTimeStep = 1 / _timeStep;
        return Control(error);
    }

    public void Reset()
    {
        _errorSum = 0;
        _lastError = 0;
        _firstRun = true;
    }
}

public class VectorPID
{
    private PID X;
    private PID Y;
    private PID Z;
    public VectorPID(double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
    {
        X = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
        Y = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
        Z = new PID(kP, kI, kD, lowerBound, upperBound, timeStep);
    }

    public VectorPID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
    {
        X = new PID(kP, kI, kD, integralDecayRatio, timeStep);
        Y = new PID(kP, kI, kD, integralDecayRatio, timeStep);
        Z = new PID(kP, kI, kD, integralDecayRatio, timeStep);
    }

    public Vector3D Control(Vector3D error)
    {
        return new Vector3D(X.Control(error.X), Y.Control(error.Y), Z.Control(error.Z));
    }

    public void Reset()
    {
        X.Reset();
        Y.Reset();
        Z.Reset();
    }
}

const double thrustP = 3;
const double thrustI = 2;
const double thrustD = 5;
const double thrustDecay = 0.2;
const float minThrust = 1.0000001f;
class ThrusterControl
{
    private List<IMyThrust> thrusters = new List<IMyThrust>();
    FlightDataCache flight;
    VectorPID pid;
    public void ResetControllerHistory() { pid.Reset(); }
    public void Reset()
    {
        flight.ResetThrust();
    }

    public ThrusterControl(FlightDataCache flight, UpdateFrequency tickSpeed, List<IMyThrust> thrusters)
    {
        if (flight == null)
            throw new Exception("Ship controller null.");
        double factor = 1;
        if (tickSpeed == UpdateFrequency.Update10)
            factor = 10;
        else if (tickSpeed == UpdateFrequency.Update100)
            factor = 100;
        double secondsPerTick = (1.0 / 60) * factor;
        pid = new VectorPID(thrustP / factor, thrustI / factor, thrustD / factor, thrustDecay / factor, secondsPerTick);
        this.flight = flight;
        this.thrusters = thrusters;
        Reset();
    }

    public Vector3D ControlPosition(Vector3D targetPosition, Vector3D targetVelocity, double maxSpeed = double.PositiveInfinity)
    {
        return ControlPositionFrom(flight.Position, targetPosition, targetVelocity, maxSpeed);
    }

    public Vector3D ControlPositionFrom(Vector3D currentPosition, Vector3D targetPosition, Vector3D targetVelocity, double maxSpeed = double.PositiveInfinity)
    {
        return ControlVelocity(targetVelocity + VectorClamp(-pid.Control(currentPosition - targetPosition), maxSpeed));
    }

    // Like ControlPosition but caps vertical speed (along verticalAxis) to 30% of maxSpeed,
    // letting lateral and forward/backward movement use the full budget.
    public Vector3D ControlPositionPrioritizedFrom(
        Vector3D currentPosition,
        Vector3D targetPosition,
        Vector3D targetVelocity,
        double maxSpeed,
        Vector3D verticalAxis,
        double verticalFraction = 0.2)
    {
        Vector3D rawVel = targetVelocity + VectorClamp(-pid.Control(currentPosition - targetPosition), maxSpeed);
        double vertComp = Vector3D.Dot(rawVel, verticalAxis);
        Vector3D horizVel = rawVel - vertComp * verticalAxis;
        horizVel = VectorClamp(horizVel, maxSpeed);
        double vertCap = maxSpeed * verticalFraction;
        vertComp = MathHelper.Clamp(vertComp, -vertCap, vertCap);
        return ControlVelocity(horizVel + vertComp * verticalAxis);
    }

    public Vector3D VectorClamp(Vector3D v, double max)
    {
        double len2 = v.LengthSquared();
        if (len2 > max * max)
            v = (v / Math.Sqrt(len2)) * max;
        return v;
    }

    public Vector3D ControlVelocity(Vector3D targetVelocity)
    {
        // Calculate the needed thrust to get to velocity
        Vector3D myVel = flight.LinearVelocity;
        Vector3D deltaV = myVel - targetVelocity;
        if (Vector3D.IsZero(deltaV))
            return Vector3D.Zero;
        Vector3D gravity = flight.Gravity;
        return 2 * deltaV + gravity;
    }

    public void ApplyAccel(Vector3D accel)
    {
        ApplyAccel(accel, double.PositiveInfinity);
    }

    public void ApplyAccel(Vector3D accel, double maxTotalThrust)
    {
        // Small corrections still matter outside an arrival radius. The output
        // cache coalesces noise relative to demand without cancelling the demand.
        Vector3D thrust = Vector3D.TransformNormal(accel * flight.Mass, flight.InverseMatrix);
        double thrustLength = thrust.Length();
        if (thrustLength > maxTotalThrust && thrustLength > 0)
            thrust *= maxTotalThrust / thrustLength;
        flight.ApplyThrust(thrust, minThrust);
    }
}
