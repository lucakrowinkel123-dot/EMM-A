// One owner per PB for live ship readings and reusable actuator samples.
// BeginInvocation also runs for commands: paused scripts must not reuse an old pose.
class FlightDataCache
{
    readonly IMyShipController controller;
    readonly List<ThrusterSample> thrusters = new List<ThrusterSample>();
    readonly List<GyroSample> gyros = new List<GyroSample>();
    readonly List<ThrusterSample>[] axisThrusters = {
        new List<ThrusterSample>(), new List<ThrusterSample>(), new List<ThrusterSample>() };
    readonly double[] axisCapacity = new double[6];
    readonly List<ThrusterSample> obliqueThrusters = new List<ThrusterSample>();
    readonly List<ThrusterSample> unavailableThrusters = new List<ThrusterSample>();
    int preparationStage, preparationIndex, preparationTick = -1;
    int rollingThrustIndex = -1, rollingThrustTick = -1;
    int tick;
    int nextMassTick;
    int nextThrustTick;
    int thrustInterval = 60;
    bool massValid;
    bool thrustValid;
    bool velocityValid;
    bool gravityValid;
    bool queryValid;
    double mass;
    Vector3D linearVelocity;
    Vector3D angularVelocity;
    Vector3D gravity;
    Vector3D queryDirection;
    double queryThrust;
    const int thrustQueryCapacity = 8;
    readonly Vector3D[] queryDirections = new Vector3D[thrustQueryCapacity];
    readonly double[] queryThrusts = new double[thrustQueryCapacity];
    int queryCount;
    int nextQuerySlot;
    public MatrixD Matrix
    {
        get; private set;
    }
    public MatrixD InverseMatrix
    {
        get; private set;
    }
    public Vector3D Position
    {
        get; private set;
    }

    public class ThrusterSample
    {
        public IMyThrust Block;
        public Vector3D LocalForward;
        public float MaxEffectiveThrust;
        public float MaxThrust;
        public int Order;
        public bool Functional;
        public bool FrameValid;
        public IMyCubeGrid Grid;
        public IMyCubeGrid ReferenceGrid;
        public int Axis = -1;
        public int VerifyPhase, NextVerify;
        public bool OutputValid, OutputPercentage;
        public float Output;
    }

    public class GyroSample
    {
        public IMyGyro Block;
        public MatrixD RcToGyro;
        public IMyCubeGrid Grid;
        public IMyCubeGrid ReferenceGrid;
        public bool FrameValid;
        public int VerifyPhase, NextAxisVerify;
        public bool OutputValid;
        public float Pitch, Yaw, Roll;
    }

    public FlightDataCache(IMyShipController controller)
    {
        this.controller = controller;
    }

    public void BeginInvocation(int currentTick)
    {
        tick = currentTick;
        Matrix = controller.WorldMatrix;
        InverseMatrix = MatrixD.Transpose(Matrix);
        Position = controller.GetPosition();
        velocityValid = false;
        gravityValid = false;
        queryValid = false;
        queryCount = nextQuerySlot = 0;
    }

    // Discovery remains at startup. Refreshes below only touch these references.
    public void SetBlocks(List<IMyThrust> thrustBlocks, List<IMyGyro> gyroBlocks)
    {
        thrusters.Clear();
        for (int i = 0; i < thrustBlocks.Count; i++)
            thrusters.Add(new ThrusterSample { Block = thrustBlocks[i], Order = i, VerifyPhase = i % 120 });
        gyros.Clear();
        for (int i = 0; i < gyroBlocks.Count; i++)
            gyros.Add(new GyroSample { Block = gyroBlocks[i], VerifyPhase = (i + 31) % 60 });
        Invalidate();
    }

    public void SetThrustRefreshInterval(int interval)
    {
        interval = Math.Max(1, interval);
        if (thrustInterval == interval)
            return;
        thrustInterval = interval;
        thrustValid = false;
        queryValid = false;
        queryCount = nextQuerySlot = 0;
    }

    public void Invalidate()
    {
        InvalidateCapabilities();
        for (int i = 0; i < thrusters.Count; i++)
            thrusters[i].OutputValid = false;
        for (int i = 0; i < gyros.Count; i++)
            gyros[i].OutputValid = false;
    }

    public bool HasThrustSnapshot { get { return thrustValid; } }

    // Grid separation invalidates mass, capability and block frames, but does not
    // make actuator values unknown. The power walk records those values explicitly.
    public void InvalidateCapabilities()
    {
        massValid = false;
        thrustValid = false;
        preparationStage = 0;
        rollingThrustIndex = -1;
        queryValid = false;
        queryCount = nextQuerySlot = 0;
        for (int i = 0; i < thrusters.Count; i++)
            thrusters[i].FrameValid = false;
        for (int i = 0; i < gyros.Count; i++)
            gyros[i].FrameValid = false;
    }

    public void NoteThrusterZero(int index)
    {
        if (index < 0 || index >= thrusters.Count) return;
        ThrusterSample sample = thrusters[index];
        sample.Output = 0;
        sample.OutputPercentage = false;
        sample.OutputValid = true;
        sample.NextVerify = NextRefreshTick(tick, 120, index);
    }

    public void NoteGyroDisabled(int index, bool axesKnownZero)
    {
        if (index < 0 || index >= gyros.Count) return;
        GyroSample sample = gyros[index];
        if (!axesKnownZero) { sample.OutputValid = false; return; }
        sample.Pitch = sample.Yaw = sample.Roll = 0;
        sample.OutputValid = true;
        sample.NextAxisVerify = NextRefreshTick(tick, 120, thrusters.Count + index);
    }

    // Absolute script-tick deadlines: multiple queries never advance the clock.
    // Different phases keep routine mass and thrust refreshes off each other.
    public static int NextRefreshTick(int currentTick, int interval, int phase)
    {
        int remainder = ((currentTick % interval) - (phase % interval) + interval) % interval;
        return currentTick + interval - remainder;
    }

    public double Mass
    {
        get
        {
            if (!massValid || tick >= nextMassTick)
            {
                mass = Math.Max(1, controller.CalculateShipMass().TotalMass);
                nextMassTick = NextRefreshTick(tick, 10, 5);
                massValid = true;
            }

            return mass;
        }
    }

    void ReadVelocity()
    {
        if (velocityValid)
            return;
        var velocities = controller.GetShipVelocities();
        linearVelocity = velocities.LinearVelocity;
        angularVelocity = velocities.AngularVelocity;
        velocityValid = true;
    }

    public Vector3D LinearVelocity
    {
        get
        {
            ReadVelocity();
            return linearVelocity;
        }
    }

    public Vector3D AngularVelocity
    {
        get
        {
            ReadVelocity();
            return angularVelocity;
        }
    }

    public Vector3D Gravity
    {
        get
        {
            if (!gravityValid)
            {
                gravity = controller.GetNaturalGravity();
                gravityValid = true;
            }

            return gravity;
        }
    }

    public List<ThrusterSample> ThrustSamples
    {
        get
        {
            if (!thrustValid)
                RefreshThrust();
            else if (tick >= nextThrustTick)
                RefreshOneThruster();
            return thrusters;
        }
    }

    void RefreshThrust()
    {
        IMyCubeGrid referenceGrid = controller.CubeGrid;
        ClearThrustGroups();
        for (int i = 0; i < thrusters.Count; i++)
            RefreshThruster(thrusters[i], referenceGrid);

        nextThrustTick = NextRefreshTick(tick, thrustInterval, 9);
        rollingThrustIndex = 0;
        thrustValid = true;
        if (preparationStage == 1) { preparationStage = 2; preparationIndex = 0; }
        queryValid = false;
        queryCount = nextQuerySlot = 0;
    }

    void RefreshOneThruster()
    {
        if (rollingThrustTick == tick || thrusters.Count == 0) return;
        rollingThrustTick = tick;
        if (rollingThrustIndex < 0 || rollingThrustIndex >= thrusters.Count)
            rollingThrustIndex = 0;
        ThrusterSample sample = thrusters[rollingThrustIndex++];
        RemoveThrusterSample(sample);
        RefreshThruster(sample, controller.CubeGrid);
        queryValid = false;
        queryCount = nextQuerySlot = 0;
        // Once routine refresh begins it continuously advances by one block per tick.
        nextThrustTick = tick + 1;
    }

    void RemoveThrusterSample(ThrusterSample sample)
    {
        if (sample.Axis >= 0)
        {
            axisThrusters[sample.Axis / 2].Remove(sample);
            if (sample.Functional) axisCapacity[sample.Axis] -= sample.MaxEffectiveThrust;
        }
        else
            obliqueThrusters.Remove(sample);
        unavailableThrusters.Remove(sample);
    }

    void ClearThrustGroups()
    {
        for (int i = 0; i < 3; i++) axisThrusters[i].Clear();
        for (int i = 0; i < 6; i++) axisCapacity[i] = 0;
        obliqueThrusters.Clear();
        unavailableThrusters.Clear();
    }

    void RefreshThruster(ThrusterSample sample, IMyCubeGrid referenceGrid)
    {
        IMyThrust block = sample.Block;
        bool functional = block != null && block.IsFunctional;
        if (functional != sample.Functional) sample.OutputValid = false;
        sample.Functional = functional;
        if (!functional)
        {
            sample.MaxEffectiveThrust = sample.MaxThrust = 0;
            if (block != null) InsertOrdered(unavailableThrusters, sample);
            return;
        }
        sample.MaxEffectiveThrust = block.MaxEffectiveThrust;
        float maximum = block.MaxThrust;
        if (sample.MaxThrust != maximum) sample.OutputValid = false;
        sample.MaxThrust = maximum;
        IMyCubeGrid grid = block.CubeGrid;
        if (!sample.FrameValid || grid != referenceGrid || sample.Grid != grid || sample.ReferenceGrid != referenceGrid)
        {
            sample.LocalForward = Vector3D.TransformNormal(block.WorldMatrix.Forward, InverseMatrix);
            sample.Grid = grid;
            sample.ReferenceGrid = referenceGrid;
            sample.FrameValid = true;
        }
        // Only rigid, axis-aligned blocks qualify. Arbitrary geometry retains the original allocation.
        sample.Axis = grid == referenceGrid ? CardinalAxis(sample.LocalForward) : -1;
        if (sample.Axis < 0) InsertOrdered(obliqueThrusters, sample);
        else
        {
            InsertOrdered(axisThrusters[sample.Axis / 2], sample);
            axisCapacity[sample.Axis] += sample.MaxEffectiveThrust;
        }
    }

    static void InsertOrdered(List<ThrusterSample> list, ThrusterSample sample)
    {
        int index = list.Count;
        while (index > 0 && list[index - 1].Order > sample.Order) index--;
        list.Insert(index, sample);
    }

    static int CardinalAxis(Vector3D value)
    {
        const double tolerance = 1e-6;
        if (Math.Abs(Math.Abs(value.X) - 1) < tolerance && Math.Abs(value.Y) < tolerance && Math.Abs(value.Z) < tolerance) return value.X > 0 ? 0 : 1;
        if (Math.Abs(Math.Abs(value.Y) - 1) < tolerance && Math.Abs(value.X) < tolerance && Math.Abs(value.Z) < tolerance) return value.Y > 0 ? 2 : 3;
        if (Math.Abs(Math.Abs(value.Z) - 1) < tolerance && Math.Abs(value.X) < tolerance && Math.Abs(value.Y) < tolerance) return value.Z > 0 ? 4 : 5;
        return -1;
    }

    static double AxisComponent(Vector3D value, int axis)
    {
        double component = axis < 2 ? value.X : axis < 4 ? value.Y : value.Z;
        return (axis & 1) == 0 ? component : -component;
    }

    // Preparation performs no actuator writes and cannot be used to launch a partial snapshot.
    public void BeginPreparation()
    {
        InvalidateCapabilities();
        ClearThrustGroups();
        preparationStage = 1;
        preparationIndex = 0;
        preparationTick = -1;
    }

    public void BeginMassPreparation()
    {
        massValid = false;
        preparationStage = 3;
        preparationIndex = 0;
        preparationTick = -1;
    }

    public bool PrepareStep(int blockBudget, bool allowMass)
    {
        if (preparationStage == 0) return false;
        if (preparationStage == 4) return preparationTick != tick;
        if (preparationTick == tick) return false;
        preparationTick = tick;
        if (preparationStage == 1)
        {
            IMyCubeGrid grid = controller.CubeGrid;
            for (int n = 0; n < blockBudget && preparationIndex < thrusters.Count; n++)
                RefreshThruster(thrusters[preparationIndex++], grid);
            if (preparationIndex == thrusters.Count) { preparationStage = 2; preparationIndex = 0; }
            return false;
        }
        if (preparationStage == 2)
        {
            for (int n = 0; n < blockBudget && preparationIndex < gyros.Count; n++)
            {
                GyroSample sample = gyros[preparationIndex++];
                if (sample.Block != null) GyroCommand(sample, Vector3D.Zero, Vector3D.Zero);
            }
            if (preparationIndex == gyros.Count) preparationStage = 3;
            return false;
        }
        if (!allowMass) return false;
        massValid = false;
        double preparedMass = Mass;
        nextMassTick = tick + 10;
        nextThrustTick = tick + thrustInterval;
        rollingThrustIndex = 0;
        thrustValid = true;
        preparationStage = 4;
        return false; // First control pass is a different invocation from the native mass read.
    }

    public double EffectiveThrust(Vector3D worldDirection)
    {
        if (!worldDirection.IsValid() || worldDirection.LengthSquared() < 0.001)
            return 0;
        // Ensure freshness before accepting a cached directional result.
        List<ThrusterSample> samples = ThrustSamples;
        if (queryValid && queryDirection == worldDirection)
            return queryThrust;
        // Keep the one-entry fast path, plus a bounded history for alternating directions.
        for (int i = 0; i < queryCount; i++)
        {
            if (queryDirections[i] != worldDirection)
                continue;
            queryValid = true;
            queryDirection = worldDirection;
            queryThrust = queryThrusts[i];
            return queryThrust;
        }
        Vector3D localDirection = Vector3D.TransformNormal(Vector3D.Normalize(worldDirection), InverseMatrix);
        double total = 0;
        for (int i = 0; i < 6; i++)
            total += axisCapacity[i] * Math.Max(0, AxisComponent(localDirection, i));
        for (int i = 0; i < obliqueThrusters.Count; i++)
        {
            ThrusterSample sample = obliqueThrusters[i];
            double dot = Vector3D.Dot(sample.LocalForward, localDirection);
            if (sample.Functional && dot > 0)
                total += sample.MaxEffectiveThrust * dot;
        }

        queryDirections[nextQuerySlot] = worldDirection;
        queryThrusts[nextQuerySlot] = total;
        nextQuerySlot++;
        if (nextQuerySlot == thrustQueryCapacity) nextQuerySlot = 0;
        if (queryCount < thrustQueryCapacity) queryCount++;
        queryValid = true;
        queryDirection = worldDirection;
        queryThrust = total;
        return total;
    }

    public void ResetThrust()
    {
        for (int i = 0; i < thrusters.Count; i++)
        {
            ThrusterSample sample = thrusters[i];
            sample.OutputValid = false;
            if (sample.Block != null && sample.Block.ThrustOverride != 0) sample.Block.ThrustOverride = 0;
        }
    }

    public void ApplyThrust(Vector3D force, float minimum)
    {
        List<ThrusterSample> samples = ThrustSamples;
        if (obliqueThrusters.Count != 0)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                ThrusterSample sample = samples[i];
                if (sample.Block == null || !sample.Block.IsFunctional) { sample.OutputValid = false; continue; }
                float amount = (float)Vector3D.Dot(sample.LocalForward, force);
                if (!sample.Functional || sample.MaxEffectiveThrust <= 0 || sample.MaxThrust <= 0 || amount <= 0)
                    SetThrustOutput(sample, false, minimum);
                else
                {
                    float proportion = MathHelper.Clamp(amount / sample.MaxEffectiveThrust, minimum / sample.MaxThrust, 1);
                    SetThrustOutput(sample, true, proportion);
                    force -= sample.LocalForward * proportion * sample.MaxEffectiveThrust;
                }
            }
            return;
        }
        for (int axis = 0; axis < 3; axis++)
        {
            double remaining = AxisComponent(force, axis * 2);
            List<ThrusterSample> group = axisThrusters[axis];
            for (int i = 0; i < group.Count; i++)
            {
                ThrusterSample sample = group[i];
                int sign = (sample.Axis & 1) == 0 ? 1 : -1;
                float amount = (float)(remaining * sign);
                if (amount <= 0 || sample.MaxEffectiveThrust <= 0 || sample.MaxThrust <= 0)
                    SetThrustOutput(sample, false, minimum);
                else
                {
                    // Failed active thrusters must not consume the demand assigned to healthy ones.
                    if (!sample.Block.IsFunctional) { sample.OutputValid = false; continue; }
                    float proportion = MathHelper.Clamp(amount / sample.MaxEffectiveThrust, minimum / sample.MaxThrust, 1);
                    SetThrustOutput(sample, true, proportion);
                    remaining -= sign * (double)proportion * sample.MaxEffectiveThrust;
                }
            }
        }
        // Repairs can reactivate a retained override before the capability refresh.
        for (int i = 0; i < unavailableThrusters.Count; i++)
        {
            ThrusterSample sample = unavailableThrusters[i];
            if (sample.Block.IsFunctional) SetThrustOutput(sample, false, minimum);
        }
    }

    void SetThrustOutput(ThrusterSample sample, bool percentage, float value)
    {
        // Absolute and percentage overrides are equivalent at zero, so a known
        // zero can switch representation without a native getter on first flight.
        bool verify = !sample.OutputValid || tick >= sample.NextVerify
            || (percentage != sample.OutputPercentage && sample.Output != 0);
        if (verify)
        {
            sample.Output = percentage ? sample.Block.ThrustOverridePercentage : sample.Block.ThrustOverride;
            sample.OutputPercentage = percentage;
            sample.OutputValid = true;
            sample.NextVerify = NextRefreshTick(tick, 120, sample.VerifyPhase);
        }
        // Keep the 0.1%-of-full-thrust noise ceiling at high demand, but use 1%
        // of requested demand below it. Tiny starts and reductions must reach the
        // actuator rather than freezing at zero or retaining a much larger output.
        bool changed = percentage ? (value == 0 ? sample.Output != 0
            : Math.Abs(sample.Output - value) > Math.Min(0.001f, value * 0.01f)) : sample.Output != value;
        if (!changed) return;
        if (percentage) sample.Block.ThrustOverridePercentage = value;
        else sample.Block.ThrustOverride = value;
        sample.Output = value;
    }

    public void ResetGyros()
    {
        for (int i = 0; i < gyros.Count; i++)
        {
            GyroSample sample = gyros[i];
            sample.OutputValid = false;
            if (sample.Block != null && sample.Block.GyroOverride) sample.Block.GyroOverride = false;
        }
    }

    public void ApplyGyro(GyroSample sample, Vector3D command)
    {
        IMyGyro block = sample.Block;
        bool first = !sample.OutputValid;
        bool enabled = block.GyroOverride;
        if (!enabled) block.GyroOverride = true;
        if (first || !enabled || tick >= sample.NextAxisVerify)
        {
            sample.Pitch = block.Pitch; sample.Yaw = block.Yaw; sample.Roll = block.Roll;
            sample.NextAxisVerify = NextRefreshTick(tick, 60, sample.VerifyPhase);
        }
        sample.OutputValid = true;
        float pitch = (float)command.X, yaw = (float)command.Y, roll = (float)command.Z;
        if (sample.Pitch != pitch) { block.Pitch = pitch; sample.Pitch = pitch; }
        if (sample.Yaw != yaw) { block.Yaw = yaw; sample.Yaw = yaw; }
        if (sample.Roll != roll) { block.Roll = roll; sample.Roll = roll; }
    }

    public List<GyroSample> GyroSamples
    {
        get
        {
            return gyros;
        }
    }

    public Vector3D GyroCommand(GyroSample sample, Vector3D worldAngles, Vector3D rcAngles)
    {
        IMyGyro block = sample.Block;
        IMyCubeGrid grid = block.CubeGrid;
        IMyCubeGrid referenceGrid = controller.CubeGrid;
        // Moving subgrids retain a live transform, never a rigid-grid assumption.
        if (grid != referenceGrid)
        {
            sample.FrameValid = false;
            return Vector3D.TransformNormal(worldAngles, MatrixD.Transpose(block.WorldMatrix));
        }

        if (!sample.FrameValid || sample.Grid != grid || sample.ReferenceGrid != referenceGrid)
        {
            sample.RcToGyro = Matrix * MatrixD.Transpose(block.WorldMatrix);
            sample.Grid = grid;
            sample.ReferenceGrid = referenceGrid;
            sample.FrameValid = true;
        }

        return Vector3D.TransformNormal(rcAngles, sample.RcToGyro);
    }
}
