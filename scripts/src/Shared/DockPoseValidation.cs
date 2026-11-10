bool ValidDockPose(Vector3D position, Vector3D forward, Vector3D up)
{
    return FiniteDockVector(position) && FiniteDockVector(forward) && FiniteDockVector(up)
        && forward.LengthSquared() > 0.001 && up.LengthSquared() > 0.001
        && Vector3D.Cross(Vector3D.Normalize(forward), Vector3D.Normalize(up)).LengthSquared() > 0.99;
}

bool FiniteDockVector(Vector3D value)
{
    return !double.IsNaN(value.X) && !double.IsNaN(value.Y) && !double.IsNaN(value.Z)
        && !double.IsInfinity(value.X) && !double.IsInfinity(value.Y) && !double.IsInfinity(value.Z);
}
