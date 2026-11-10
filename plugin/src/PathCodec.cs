using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VRageMath;

namespace EMMA3.Client
{
    internal static class PathCodec
    {
        public static string Build(
            Vector3D anchorPosition,
            Vector3D anchorForward,
            Vector3D anchorUp,
            IList<RoutePose> route,
            IList<MiningPlane> planes,
            double mineHeight,
            double mineWidth,
            double dockLegMeters = 0)
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine(FormatPose(anchorPosition, Vector3D.Zero, anchorForward, anchorUp));
            sb.AppendLine("undock");
            sb.AppendLine("00;routeMeters:" + F(EstimateRouteMeters(anchorPosition, route, planes, mineHeight, mineWidth, dockLegMeters)));
            int planeIndex = 0;
            for (int i = 0; i < route.Count; i++)
            {
                RoutePose pose = route[i];
                if (pose.Kind == "plane")
                {
                    if (planeIndex >= planes.Count)
                        throw new InvalidOperationException("Route has more poses than generated mining planes.");
                    MiningPlane plane = planes[planeIndex++];
                    sb.AppendLine(FormatMineArea(plane, anchorPosition));
                    sb.AppendLine(FormatLaneMetadata(mineHeight, mineWidth, GetLaneCount(plane, mineHeight, mineWidth), plane.SkipLanes, plane.LaneDepths, plane.LaneDestructionRanges));
                }
                else if (pose.Kind == "drills-on")
                    sb.AppendLine("drillson");
                else if (pose.Kind == "reverse-waypoint")
                    sb.AppendLine("reverse;" + FormatPose(pose.Position, anchorPosition, pose.Forward, pose.Up));
                else
                    sb.AppendLine(FormatPose(pose.Position, anchorPosition, pose.Forward, pose.Up));
            }

            sb.AppendLine("dock");
            return sb.ToString();
        }

        private static double EstimateRouteMeters(
            Vector3D anchor,
            IList<RoutePose> route,
            IList<MiningPlane> planes,
            double mineHeight,
            double mineWidth,
            double dockLegMeters)
        {
            double total = Math.Max(0, dockLegMeters) * 2;
            Vector3D current = anchor;
            int planeIndex = 0;
            for (int i = 0; i < route.Count; i++)
            {
                RoutePose pose = route[i];
                if (pose.Kind == "drills-on")
                    continue;
                if (pose.Kind == "plane")
                {
                    if (planeIndex < planes.Count)
                        total += EstimateMiningLoopMeters(current, planes[planeIndex++], mineHeight, mineWidth);
                    continue;
                }

                total += Vector3D.Distance(current, pose.Position);
                current = pose.Position;
            }

            return total;
        }

        private static double EstimateMiningLoopMeters(Vector3D entry, MiningPlane plane, double mineHeight, double mineWidth)
        {
            int rows = Math.Max(1, (int)Math.Ceiling(plane.Height / mineHeight));
            int cols = Math.Max(1, (int)Math.Ceiling(plane.Width / mineWidth));
            double rowStart = rows > 1 ? mineHeight * 0.5 : plane.Height * 0.5;
            double rowStep = rows > 1 ? (plane.Height - mineHeight) / (rows - 1) : 0;
            double colStart = cols > 1 ? mineWidth * 0.5 : plane.Width * 0.5;
            double colStep = cols > 1 ? (plane.Width - mineWidth) / (cols - 1) : 0;
            Vector3D previous = entry;
            double total = 0;
            bool moved = false;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int index = row * cols + col;
                    if (plane.SkipLanes.Contains(index))
                        continue;
                    int actualCol = (row & 1) == 1 ? cols - 1 - col : col;
                    Vector3D lane = plane.CornerA - plane.Up * (rowStart + row * rowStep) + plane.Right * (colStart + actualCol * colStep);
                    total += Vector3D.Distance(previous, lane);
                    double depth = index < plane.LaneDepths.Count ? plane.LaneDepths[index] : plane.Depth;
                    total += Math.Max(0, depth) * 2;
                    previous = lane;
                    moved = true;
                }

            if (moved)
                total += Vector3D.Distance(previous, entry);
            return total;
        }

        public static bool Validate(string text, out int executableRecords, out string reason)
        {
            executableRecords = 0;
            reason = "";
            string[] lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                reason = "path has fewer than three records";
                return false;
            }

            Vector3D anchor, f, u;
            if (!TryParsePose(lines[0], Vector3D.Zero, out anchor, out f, out u))
            {
                reason = "invalid anchor";
                return false;
            }

            bool sawUndock = false, sawDock = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Equals("undock", StringComparison.OrdinalIgnoreCase))
                {
                    sawUndock = true;
                    executableRecords++;
                    continue;
                }

                if (line.Equals("dock", StringComparison.OrdinalIgnoreCase))
                {
                    sawDock = true;
                    executableRecords++;
                    continue;
                }

                if (line.Equals("drillson", StringComparison.OrdinalIgnoreCase))
                {
                    executableRecords++;
                    continue;
                }

                if (line.StartsWith("00;", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(8).Trim();
                string[] parts = line.Split(':');
                if (parts.Length == 2)
                {
                    Vector3D p;
                    if (!TryParsePose(line, anchor, out p, out f, out u))
                    {
                        reason = "invalid pose at line " + (i + 1);
                        return false;
                    }

                    executableRecords++;
                    continue;
                }

                if (parts.Length == 4)
                {
                    Vector3D delta;
                    double width, height, depth;
                    if (!TryVector(parts[0], out delta) || !TryAxis(parts[1], out f, out u) || !TryPair(parts[2], out width, out height) || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out depth) || depth <= 0)
                    {
                        reason = "invalid mine area at line " + (i + 1);
                        return false;
                    }

                    executableRecords++;
                    continue;
                }

                reason = "unknown record at line " + (i + 1);
                return false;
            }

            if (!sawUndock || !sawDock)
            {
                reason = "path must contain undock and dock";
                return false;
            }

            reason = "valid EMM-A path; " + executableRecords + " executable records";
            return true;
        }

        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool TryReadVisualization(string text, double travelClearance, out List<RoutePose> route, out List<MiningPlane> planes, out string reason)
        {
            route = new List<RoutePose>();
            planes = new List<MiningPlane>();
            int records;
            if (!Validate(text, out records, out reason))
                return false;
            string[] lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Vector3D anchor, forward, up;
            if (!TryParsePose(lines[0], Vector3D.Zero, out anchor, out forward, out up))
            {
                reason = "invalid visualization anchor";
                return false;
            }

            MiningPlane pendingPlane = null;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Equals("undock", StringComparison.OrdinalIgnoreCase) || line.Equals("dock", StringComparison.OrdinalIgnoreCase) || line.Equals("drillson", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.StartsWith("00;", StringComparison.OrdinalIgnoreCase))
                {
                    if (pendingPlane != null && !line.StartsWith("00;routeMeters:", StringComparison.OrdinalIgnoreCase))
                        TryReadLaneVisualization(line, pendingPlane);
                    continue;
                }

                bool reverse = line.StartsWith("reverse;", StringComparison.OrdinalIgnoreCase);
                if (reverse)
                    line = line.Substring(8).Trim();
                string[] parts = line.Split(':');
                if (parts.Length == 2)
                {
                    Vector3D position;
                    if (!TryParsePose(line, anchor, out position, out forward, out up))
                    {
                        reason = "invalid visualization pose at line " + (i + 1);
                        return false;
                    }

                    route.Add(new RoutePose { Position = position, Forward = forward, Up = up, Kind = reverse ? "reverse-waypoint" : "waypoint" });
                    pendingPlane = null;
                    continue;
                }

                Vector3D delta;
                double signedWidth, signedHeight, depth;
                if (parts.Length != 4 || !TryVector(parts[0], out delta) || !TryAxis(parts[1], out forward, out up) || !TryPair(parts[2], out signedWidth, out signedHeight) || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out depth))
                {
                    reason = "invalid visualization mining plane at line " + (i + 1);
                    return false;
                }

                Vector3D right = Vector3D.Normalize(Vector3D.Cross(forward, up));
                Vector3D cornerA = anchor + delta;
                Vector3D cornerB = cornerA + right * signedWidth + up * signedHeight;
                pendingPlane = new MiningPlane
                {
                    CornerA = cornerA,
                    CornerB = cornerB,
                    Center = (cornerA + cornerB) * 0.5,
                    Forward = forward,
                    Up = up,
                    Right = right,
                    Width = Math.Abs(signedWidth),
                    Height = Math.Abs(signedHeight),
                    Depth = Math.Max(0, depth)
                };
                pendingPlane.Staging = pendingPlane.Center - pendingPlane.Forward * Math.Max(0, travelClearance);
                planes.Add(pendingPlane);
                route.Add(new RoutePose { Position = pendingPlane.Center, Forward = forward, Up = up, Kind = "plane" });
            }

            reason = "visualization path restored";
            return true;
        }

        private static void TryReadLaneVisualization(string line, MiningPlane plane)
        {
            string[] sections = line.Substring(3).Split(':');
            if (sections.Length < 2)
                return;
            string[] header = sections[0].Split(',');
            int laneCount;
            if (header.Length != 3 || !int.TryParse(header[2], out laneCount) || laneCount <= 0)
                return;
            foreach (string item in sections[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] range = item.Split('-');
                int start, end;
                if (!int.TryParse(range[0], out start))
                    continue;
                end = range.Length > 1 && int.TryParse(range[1], out end) ? end : start;
                for (int value = Math.Max(1, start); value <= Math.Min(laneCount, end); value++)
                    plane.SkipLanes.Add(value - 1);
            }

            if (sections.Length >= 3)
            {
                string[] values = sections[2].Split(',');
                if (values.Length == laneCount)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        double value;
                        if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        {
                            plane.LaneDepths.Clear();
                            break;
                        }

                        plane.LaneDepths.Add(Math.Max(0, value));
                    }
                }
            }

            if (sections.Length < 4)
                return;
            string[] lanes = sections[3].Split('|');
            if (lanes.Length != laneCount)
                return;
            for (int i = 0; i < laneCount; i++)
            {
                var intervals = new List<DepthInterval>();
                if (lanes[i] != ".")
                {
                    foreach (string encoded in lanes[i].Split('+'))
                    {
                        int dash = encoded.IndexOf('-');
                        double start, end;
                        if (dash <= 0 || !double.TryParse(encoded.Substring(0, dash), NumberStyles.Float, CultureInfo.InvariantCulture, out start) || !double.TryParse(encoded.Substring(dash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out end) || end <= start)
                            continue;
                        intervals.Add(new DepthInterval(start, end));
                    }
                }

                plane.LaneDestructionRanges.Add(intervals);
            }
        }

        private static string FormatMineArea(MiningPlane plane, Vector3D anchor)
        {
            Vector3D delta = plane.CornerB - plane.CornerA;
            double width = Vector3D.Dot(delta, plane.Right);
            double height = Vector3D.Dot(delta, plane.Up);
            return V(plane.CornerA - anchor) + ":" + Axis(plane.Forward, plane.Up) + ":" + F(width) + "," + F(height) + ":" + F(plane.Depth);
        }

        private static string FormatPose(Vector3D position, Vector3D anchor, Vector3D forward, Vector3D up) => V(position - anchor) + ":" + Axis(forward, up);
        private static string Axis(Vector3D forward, Vector3D up)
        {
            if (forward.LengthSquared() < 1e-9 || up.LengthSquared() < 1e-9)
                return "0.000000,0.000000,0.000000,0.000000";
            Quaternion q = Quaternion.CreateFromForwardUp((Vector3)Vector3D.Normalize(forward), (Vector3)Vector3D.Normalize(up));
            double w = MathHelper.Clamp(q.W, -1f, 1f), angle = 2.0 * Math.Acos(w), s = Math.Sqrt(Math.Max(0, 1 - w * w));
            if (angle < 1e-6 || s < 1e-9)
                return "0.000000,0.000000,0.000000,0.000000";
            return F6(q.X / s) + "," + F6(q.Y / s) + "," + F6(q.Z / s) + "," + F6(angle);
        }

        private static string FormatLaneMetadata(
            double height,
            double width,
            int laneCount,
            List<int> skips,
            List<double> depths,
            List<List<DepthInterval>> destructionRanges)
        {
            var sorted = new List<int>(skips ?? new List<int>());
            sorted.Sort();
            var ranges = new StringBuilder();
            for (int i = 0; i < sorted.Count;)
            {
                int start = sorted[i] + 1, end = start;
                i++;
                while (i < sorted.Count && sorted[i] + 1 <= end + 1)
                {
                    int value = sorted[i] + 1;
                    if (value > end)
                        end = value;
                    i++;
                }

                if (ranges.Length > 0)
                    ranges.Append(',');
                ranges.Append(start);
                if (end != start)
                    ranges.Append('-').Append(end);
            }

            string result = "00;" + F(height) + "," + F(width) + "," + laneCount + ":" + ranges;
            if (depths != null && depths.Count == laneCount)
            {
                var values = new StringBuilder();
                for (int i = 0; i < depths.Count; i++)
                {
                    if (i > 0)
                        values.Append(',');
                    values.Append(F(Math.Max(0, depths[i])));
                }

                result += ":" + values;
            }

            if (destructionRanges != null && destructionRanges.Count == laneCount)
            {
                var values = new StringBuilder();
                for (int i = 0; i < destructionRanges.Count; i++)
                {
                    if (i > 0)
                        values.Append('|');
                    List<DepthInterval> rangesForLane = destructionRanges[i];
                    if (rangesForLane == null || rangesForLane.Count == 0)
                    {
                        values.Append('.');
                        continue;
                    }

                    int emitted = 0;
                    for (int n = 0; n < rangesForLane.Count; n++)
                    {
                        double start = Math.Round(Math.Max(0, rangesForLane[n].Start), 2);
                        double end = Math.Round(Math.Max(0, rangesForLane[n].End), 2);
                        if (end <= start)
                            continue;
                        if (emitted > 0)
                            values.Append('+');
                        values.Append(F(start)).Append('-').Append(F(end));
                        emitted++;
                    }

                    if (emitted == 0)
                        values.Append('.');
                }

                result += ":" + values;
            }

            return result;
        }

        private static int GetLaneCount(MiningPlane p, double h, double w) => Math.Max(1, (int)Math.Ceiling(p.Height / h)) * Math.Max(1, (int)Math.Ceiling(p.Width / w));
        private static string V(Vector3D p) => F2(p.X) + "," + F2(p.Y) + "," + F2(p.Z);
        private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string F2(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        private static string F6(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
        private static bool TryParsePose(string line, Vector3D anchor, out Vector3D pos, out Vector3D forward, out Vector3D up)
        {
            pos = Vector3D.Zero;
            forward = Vector3D.Forward;
            up = Vector3D.Up;
            string[] p = line.Split(':');
            Vector3D d;
            if (p.Length != 2 || !TryVector(p[0], out d) || !TryAxis(p[1], out forward, out up))
                return false;
            pos = anchor + d;
            return true;
        }

        private static bool TryVector(string s, out Vector3D v)
        {
            v = Vector3D.Zero;
            string[] p = s.Split(',');
            double x, y, z;
            if (p.Length != 3 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                return false;
            v = new Vector3D(x, y, z);
            return true;
        }

        private static bool TryPair(string s, out double a, out double b)
        {
            a = b = 0;
            string[] p = s.Split(',');
            return p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b);
        }

        private static bool TryAxis(string s, out Vector3D forward, out Vector3D up)
        {
            forward = Vector3D.Forward;
            up = Vector3D.Up;
            string[] p = s.Split(',');
            double x, y, z, a;
            if (p.Length != 4 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z) || !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                return false;
            Vector3D axis = new Vector3D(x, y, z);
            if (Math.Abs(a) < 1e-6 || axis.LengthSquared() < 1e-12)
                return true;
            Quaternion q = Quaternion.CreateFromAxisAngle((Vector3)Vector3D.Normalize(axis), (float)a);
            forward = Vector3D.Normalize(Vector3D.Transform(Vector3D.Forward, q));
            up = Vector3D.Normalize(Vector3D.Transform(Vector3D.Up, q));
            return true;
        }
    }
}
