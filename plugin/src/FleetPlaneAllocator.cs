using System;
using System.Collections.Generic;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed class FleetPlaneAllocation
    {
        public int[] Owners, Components;
        public double[] Work;
    }
    internal static class FleetPlaneAllocator
    {
        internal sealed class Volume
        {
            public Vector3D Center;
            public Vector3D[] Axis;
            public double[] Half;
        }
        private sealed class Group
        {
            public int First;
            public double Work;
            public readonly List<int> Planes = new List<int>();
        }
        public static double Weight(MiningPlane plane, double laneWidth, double laneHeight)
        {
            if (plane == null || !Positive(plane.Width) || !Positive(plane.Height) || !Positive(laneWidth) || !Positive(laneHeight))
                throw new ArgumentException("Invalid plane or lane dimensions.");
            double count = Math.Ceiling(plane.Width / laneWidth) * Math.Ceiling(plane.Height / laneHeight);
            if (!Finite(count) || count > 100000) throw new ArgumentException("Mining plane has too many lanes.");
            int lanes = (int)count;
            if (plane.LaneDepths.Count != 0 && plane.LaneDepths.Count != lanes
                || plane.LaneDestructionRanges.Count != 0 && plane.LaneDestructionRanges.Count != lanes)
                throw new ArgumentException("Mining lane metadata does not match the plane.");
            var skipped = new HashSet<int>(plane.SkipLanes);
            double total = 0;
            for (int i = 0; i < lanes; i++)
            {
                if (skipped.Contains(i)) continue;
                double depth = plane.LaneDepths.Count == 0 ? plane.Depth : plane.LaneDepths[i];
                if (!Finite(depth) || depth < 0) throw new ArgumentException("Mining depth must be finite and nonnegative.");
                var ranges = new List<DepthInterval>();
                if (plane.LaneDestructionRanges.Count > 0 && plane.LaneDestructionRanges[i] != null)
                    foreach (DepthInterval interval in plane.LaneDestructionRanges[i])
                    {
                        if (!Finite(interval.Start) || !Finite(interval.End) || interval.End < interval.Start)
                            throw new ArgumentException("Invalid destruction interval.");
                        double start = Math.Max(0, interval.Start), end = Math.Min(depth, interval.End);
                        if (end > start) ranges.Add(new DepthInterval(start, end));
                    }
                ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
                double destruction = 0, from = 0, to = 0;
                foreach (DepthInterval interval in ranges)
                {
                    if (interval.Start > to) { destruction += to - from; from = interval.Start; to = interval.End; }
                    else to = Math.Max(to, interval.End);
                }
                destruction += to - from;
                total += depth - 0.5 * destruction;
            }
            if (!Finite(total)) throw new ArgumentException("Plane work exceeds the supported range.");
            return total;
        }
        public static FleetPlaneAllocation Assign(IList<MiningPlane> planes, int miners, double laneWidth, double laneHeight, double padding, Action cancellation = null)
        {
            if (planes == null || miners < 1 || miners > 3 || !Finite(padding) || padding < 0) throw new ArgumentException("Invalid fleet assignment settings.");
            int count = planes.Count;
            var result = new FleetPlaneAllocation { Owners = new int[count], Components = new int[count], Work = new double[miners] };
            var parent = new int[count]; var weights = new double[count]; var boxes = new Volume[count];
            for (int i = 0; i < count; i++)
            {
                cancellation?.Invoke();
                parent[i] = i; result.Owners[i] = result.Components[i] = -1;
                weights[i] = Weight(planes[i], laneWidth, laneHeight);
                if (weights[i] > 0) boxes[i] = Box(planes[i], padding, false);
            }
            for (int i = 0; i < count; i++)
            {
                cancellation?.Invoke();
                if (boxes[i] == null) continue;
                for (int j = 0; j < i; j++)
                    if (boxes[j] != null && Intersects(boxes[i], boxes[j]))
                    {
                        int a = Root(parent, i), b = Root(parent, j);
                        parent[Math.Max(a, b)] = Math.Min(a, b);
                    }
            }
            var byRoot = new Dictionary<int, Group>();
            for (int i = 0; i < count; i++)
            {
                if (weights[i] == 0) continue;
                int root = Root(parent, i); result.Components[i] = root;
                Group group;
                if (!byRoot.TryGetValue(root, out group)) byRoot[root] = group = new Group { First = i };
                group.Planes.Add(i); group.Work += weights[i];
            }
            var groups = new List<Group>(byRoot.Values);
            groups.Sort((a, b) => { int order = b.Work.CompareTo(a.Work); return order != 0 ? order : a.First.CompareTo(b.First); });
            foreach (Group group in groups)
            {
                int owner = 0;
                for (int i = 1; i < miners; i++) if (result.Work[i] < result.Work[owner]) owner = i;
                result.Work[owner] += group.Work;
                foreach (int plane in group.Planes) result.Owners[plane] = owner;
            }
            return result;
        }
        private static int Root(int[] parent, int node)
        {
            while (parent[node] != node) { parent[node] = parent[parent[node]]; node = parent[node]; }
            return node;
        }
        public static List<Volume> Reservations(IList<MiningPlane> planes, int[] owners, int miner, double padding)
        {
            var result = new List<Volume>();
            for (int i = 0; i < planes.Count; i++) if (owners[i] >= 0 && owners[i] != miner) result.Add(Box(planes[i], padding));
            return result;
        }
        public static bool Clear(IList<Volume> volumes, Vector3D from, Vector3D to)
        {
            foreach (Volume volume in volumes)
            {
                Vector3D delta = to - from, start = from - volume.Center;
                double enter = 0, leave = 1;
                for (int axis = 0; axis < 3; axis++)
                {
                    double p = Vector3D.Dot(start, volume.Axis[axis]), d = Vector3D.Dot(delta, volume.Axis[axis]), h = volume.Half[axis] + 0.001;
                    if (Math.Abs(d) < 1e-12) { if (p < -h || p > h) { enter = 2; break; } }
                    else
                    {
                        double a = (-h - p) / d, b = (h - p) / d;
                        if (a > b) { double swap = a; a = b; b = swap; }
                        enter = Math.Max(enter, a); leave = Math.Min(leave, b);
                        if (enter > leave) break;
                    }
                }
                if (enter <= leave) return false;
            }
            return true;
        }
        public static BoundingBoxD Bounds(Volume volume)
        {
            Vector3D radius = Vector3D.Zero;
            for (int i = 0; i < 3; i++) radius += Vector3D.Abs(volume.Axis[i]) * volume.Half[i];
            return new BoundingBoxD(volume.Center - radius, volume.Center + radius);
        }
        public static bool Overlaps(MiningPlane first, MiningPlane second, double padding)
        {
            if (!Finite(padding) || padding < 0) throw new ArgumentException("Invalid overlap padding.");
            return Intersects(Box(first, padding, false), Box(second, padding, false));
        }
        private static Volume Box(MiningPlane plane, double padding, bool padEnds = true)
        {
            if (plane == null || !Positive(plane.Width) || !Positive(plane.Height) || !Finite(plane.Center.X) || !Finite(plane.Center.Y) || !Finite(plane.Center.Z))
                throw new ArgumentException("Invalid mining volume.");
            Vector3D right = Unit(plane.Right), up = Unit(plane.Up), forward = Unit(plane.Forward);
            if (Math.Abs(Vector3D.Dot(right, up)) > 1e-6 || Math.Abs(Vector3D.Dot(right, forward)) > 1e-6 || Math.Abs(Vector3D.Dot(up, forward)) > 1e-6)
                throw new ArgumentException("Mining plane axes are not orthogonal.");
            double depth = plane.LaneDepths.Count == 0 ? plane.Depth : 0;
            for (int i = 0; i < plane.LaneDepths.Count; i++)
                if (!plane.SkipLanes.Contains(i)) depth = Math.Max(depth, plane.LaneDepths[i]);
            if (!Finite(depth) || depth < 0) throw new ArgumentException("Invalid mining volume depth.");
            return new Volume
            {
                Center = plane.Center + forward * ((depth - 50) * 0.5),
                Axis = new[] { right, up, forward },
                Half = new[] { plane.Width * 0.5 + padding, plane.Height * 0.5 + padding, (depth + 50) * 0.5 + (padEnds ? padding : 0) }
            };
        }
        private static bool Intersects(Volume a, Volume b)
        {
            Vector3D delta = b.Center - a.Center;
            for (int i = 0; i < 3; i++)
                if (Separated(a, b, delta, a.Axis[i]) || Separated(a, b, delta, b.Axis[i])) return false;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (Separated(a, b, delta, Vector3D.Cross(a.Axis[i], b.Axis[j]))) return false;
            return true;
        }
        private static bool Separated(Volume a, Volume b, Vector3D delta, Vector3D axis)
        {
            double length = axis.LengthSquared();
            if (length < 1e-18) return false;
            double radius = 0;
            for (int i = 0; i < 3; i++) radius += a.Half[i] * Math.Abs(Vector3D.Dot(a.Axis[i], axis)) + b.Half[i] * Math.Abs(Vector3D.Dot(b.Axis[i], axis));
            return Math.Abs(Vector3D.Dot(delta, axis)) > radius + 1e-6 * Math.Sqrt(length);
        }
        private static Vector3D Unit(Vector3D axis)
        {
            double length = axis.LengthSquared();
            if (!Finite(length) || length < 1e-12) throw new ArgumentException("Invalid mining plane axis.");
            return axis / Math.Sqrt(length);
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Positive(double value) { return Finite(value) && value > 0; }
    }
}
