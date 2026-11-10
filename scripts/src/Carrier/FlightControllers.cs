const double gyroP = 10;
const double gyroI = 0;
const double gyroD = 5;
const double gyroMax = 1000;
const double gyroErrorDeadbandRad = 0.003;
const double gyroOutputDeadband = 0.002;
const double gyroFineDampRangeRad = 0.12;
const double gyroRateLimit = 0.15;
class GyroControl
{
    private List<IMyGyro> gyros;
    FlightDataCache flight;
    VectorPID anglePID;
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
        double spt = (1.0 / 60) * factor;
        anglePID = new VectorPID(gyroP / factor, gyroI / factor, gyroD / factor, -gyroMax, gyroMax, spt);
        Reset();
    }

    public void Reset()
    {
        flight.ResetGyros();
        ResetController();
    }

    public void ResetController()
    {
        anglePID.Reset();
    }

    Vector3D GetAngles(MatrixD current, Vector3D forward, Vector3D up)
    {
        Vector3D error = new Vector3D();
        if (forward != Vector3D.Zero)
        {
            error.X = SignedAngleOnPlane(current.Forward, forward, current.Right, current.Up, false);
            error.Y = SignedAngleOnPlane(current.Forward, forward, current.Up, current.Right, true);
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

    public void FaceVectors(Vector3D forward, Vector3D up)
    {
        FaceVectors(flight.Matrix, forward, up);
    }

    public void FaceVectors(MatrixD current, Vector3D forward, Vector3D up)
    {
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
            Vector3D tv = flight.GyroCommand(sample, worldAngles, rcAngles);
            if (!tv.IsValid())
                throw new Exception("Invalid trans vector: " + tv.ToString());
            flight.ApplyGyro(sample, tv);
        }
    }

    static double ScalerProjection(Vector3D value, Vector3D guide)
    {
        double r = Vector3D.Dot(value, guide);
        return double.IsNaN(r) ? 0 : r;
    }

    static double SignedAngleOnPlane(Vector3D from, Vector3D to, Vector3D planeNormal, Vector3D signGuide, bool negateWhenPositive)
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

    static Vector3D VectorPojection(Vector3D value, Vector3D guide)
    {
        return ScalerProjection(value, guide) * guide;
    }

    static Vector3D VectorRejection(Vector3D value, Vector3D guide)
    {
        return value - VectorPojection(value, guide);
    }
}

// ---- ThrusterControl ----
const double thrustP = 3;
const double thrustI = 2;
const double thrustD = 2;
const double thrustDecay = 0.2;
const float minThrust = 1.0000001f;
class ThrusterControl
{
    private List<IMyThrust> thrusters;
    FlightDataCache flight;
    VectorPID pid;
    public ThrusterControl(FlightDataCache flight, UpdateFrequency tickSpeed, List<IMyThrust> thrusters)
    {
        if (flight == null)
            throw new Exception("Ship controller null.");
        double factor = 1;
        if (tickSpeed == UpdateFrequency.Update10)
            factor = 10;
        else if (tickSpeed == UpdateFrequency.Update100)
            factor = 100;
        double spt = (1.0 / 60) * factor;
        pid = new VectorPID(thrustP / factor, thrustI / factor, thrustD / factor, thrustDecay / factor, spt);
        this.flight = flight;
        this.thrusters = thrusters;
        Reset();
    }

    public void Reset()
    {
        flight.ResetThrust();
    }

    public Vector3D ControlPositionFrom(Vector3D currentPosition, Vector3D targetPosition, Vector3D targetVelocity, double maxSpeed = double.PositiveInfinity)
    {
        return ControlVelocity(targetVelocity + VectorClamp(-pid.Control(currentPosition - targetPosition), maxSpeed));
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
        if (accel.Equals(Vector3D.Zero, 0.1))
            accel = Vector3D.Zero;
        Vector3D thrustVec = Vector3D.TransformNormal(accel * flight.Mass, flight.InverseMatrix);
        double len = thrustVec.Length();
        if (len > maxTotalThrust && len > 0)
            thrustVec *= maxTotalThrust / len;
        flight.ApplyThrust(thrustVec, minThrust);
    }
}

// ---- PID Classes ----
public class PID
{
    double _kP, _kI, _kD, _integralDecayRatio, _lowerBound, _upperBound, _timeStep, _inverseTimeStep, _errorSum, _lastError;
    bool _firstRun = true, _integralDecay = false;
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
        _inverseTimeStep = 1 / timeStep;
    }

    public PID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
    {
        _kP = kP;
        _kI = kI;
        _kD = kD;
        _timeStep = timeStep;
        _inverseTimeStep = 1 / timeStep;
        _integralDecayRatio = integralDecayRatio;
        _integralDecay = true;
    }

    public double Control(double error)
    {
        var errorDerivative = (error - _lastError) * _inverseTimeStep;
        if (_firstRun)
        {
            errorDerivative = 0;
            _firstRun = false;
        }

        if (!_integralDecay)
        {
            _errorSum += error * _timeStep;
            if (_errorSum > _upperBound)
                _errorSum = _upperBound;
            else if (_errorSum < _lowerBound)
                _errorSum = _lowerBound;
        }
        else
            _errorSum = _errorSum * (1.0 - _integralDecayRatio) + error * _timeStep;
        _lastError = error;
        this.Value = _kP * error + _kI * _errorSum + _kD * errorDerivative;
        return this.Value;
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
    private PID X, Y, Z;
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
