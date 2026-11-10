bool TryParseGpsCommand(string command, out Vector3D gps, out Vector3D forward, out Vector3D up)
{
    gps = Vector3D.Zero;
    forward = Vector3D.Zero;
    up = Vector3D.Zero;
    string[] parts = command.Split('|');
    string gpsPart = parts[0];
    string[] gpsTokens = gpsPart.Split(':');
    if (gpsTokens.Length < 5)
        return false;
    double x, y, z;
    if (!TryParseNumber(gpsTokens[2], out x) || !TryParseNumber(gpsTokens[3], out y) || !TryParseNumber(gpsTokens[4], out z))
        return false;
    gps = new Vector3D(x, y, z);
    if (parts.Length > 1)
    {
        string[] modes = parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
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
                return true;
            }
        }

        Vector3D toTarget = gps - _tickRcPosition;
        foreach (string rawMode in modes)
        {
            string mode = rawMode.Trim().ToLower();
            if (mode == "faceforward")
            {
                if (toTarget.LengthSquared() > 0.001)
                    forward = Vector3D.Normalize(toTarget);
            }
            else if (mode == "faceup")
            {
                Vector3D gravity = flightCache.Gravity;
                if (gravity.LengthSquared() > 0.001)
                    up = Vector3D.Normalize(-gravity);
                else
                    up = _tickRcMatrix.Up;
            }
        }
    }

    return true;
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

// === Compact path serialization (anchor + axis-angle deltas) ===
// On-disk route record shapes:
//   anchor (header line 1): cx,cy,cz:ax,ay,az,a   -- absolute carrier pos + axis-angle
//   path point             : dx,dy,dz:ax,ay,az,a   -- delta from anchor + axis-angle
//   mine area              : dx,dy,dz:ax,ay,az,a:w,h:depth -- corner A delta, area axis-angle, w/h, depth
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
        return true;
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

bool HasCarrierTag(IMyTerminalBlock b)
{
    return b.CustomName.IndexOf(Ca(""), StringComparison.OrdinalIgnoreCase) >= 0;
}

T GetBlock<T>(bool useSubgrids = false)
    where T : class, IMyTerminalBlock
{
    return GetBlocks<T>(useSubgrids).FirstOrDefault();
}

List<T> GetBlocks<T>(bool useSubgrids = false)
    where T : class, IMyTerminalBlock
{
    List<T> blocks = new List<T>();
    if (useSubgrids)
        GridTerminalSystem.GetBlocksOfType(blocks);
    else
        GridTerminalSystem.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
    return blocks;
}
// ---- GyroControl ----
