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

T GetBlock<T>(string name, bool useSubgrids = false)
    where T : class, IMyTerminalBlock
{
    if (useSubgrids)
        return GridTerminalSystem.GetBlockWithName(name) as T;
    foreach (T block in GetBlocks<T>(false))
        if (block.CustomName == name)
            return block;
    return null;
}

T GetBlock<T>(bool useSubgrids = false)
    where T : class, IMyTerminalBlock
{
    List<T> blocks = GetBlocks<T>(useSubgrids);
    return blocks.FirstOrDefault();
}

List<T> GetBlocks<T>(string groupName, bool useSubgrids = false)
    where T : class, IMyTerminalBlock
{
    IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(groupName);
    if (group == null)
        return new List<T>();
    List<T> blocks = new List<T>();
    if (useSubgrids)
        group.GetBlocksOfType(blocks);
    else
        group.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
    return blocks;
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

const double gyroP = 10;
const double gyroI = 0;
const double gyroD = 5;
const double gyroMax = 1000;
const double gyroErrorDeadbandRad = 0.003; // ~0.17 deg
const double gyroOutputDeadband = 0.002;
const double gyroFineDampRangeRad = 0.12; // start damping under ~7 deg total error
const double gyroRateLimit = 0.35; // max per-axis command (rad/s equivalent)
