using System;
using System.Collections.Generic;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed class OrganicVoxelRouter
    {
        private readonly IList<MaterialSample> samples;
        private readonly BoundingBoxD occupied;
        private readonly double clearance;
        private readonly double grid;
        private readonly double turnRadius;
        private readonly double bucketSize;
        private readonly Dictionary<string, List<Vector3D>> sampleBuckets = new Dictionary<string, List<Vector3D>>();
        private readonly Dictionary<string, List<Vector3D>> routeCache = new Dictionary<string, List<Vector3D>>();
        private readonly PlanningCancellation cancellation;
        private readonly int maxExpandedNodes;
        private readonly int endpointSearchRadiusCells;
        public int SearchCount
        {
            get; private set;
        }
        public int FallbackCount
        {
            get; private set;
        }
        public string LastFailureReason
        {
            get; private set;
        }
        public int LastExpandedNodes
        {
            get; private set;
        }

        public OrganicVoxelRouter(
            IList<MaterialSample> samples,
            BoundingBoxD occupied,
            double clearance,
            double sampleMeters,
            double gridMeters = 12.0,
            double turnRadiusMeters = 25.0,
            PlanningCancellation cancellation = null,
            int maxExpandedNodes = 120000,
            int endpointSearchRadiusCells = 1)
        {
            this.samples = samples ?? new List<MaterialSample>();
            this.cancellation = cancellation ?? new PlanningCancellation();
            this.occupied = occupied;
            this.clearance = Math.Max(1, clearance + Math.Max(0, sampleMeters) * 0.5);
            grid = Math.Max(4, gridMeters);
            turnRadius = Math.Max(0, turnRadiusMeters);
            this.maxExpandedNodes = Math.Max(1000, maxExpandedNodes);
            this.endpointSearchRadiusCells = Math.Max(1, endpointSearchRadiusCells);
            bucketSize = Math.Max(12, this.clearance * 0.5);
            for (int i = 0; i < this.samples.Count; i++)
            {
                if ((i & 1023) == 0)
                    this.cancellation.ThrowIfCancellationRequested();
                Vector3D p = this.samples[i].Position;
                string key = BucketKey(p);
                List<Vector3D> bucket;
                if (!sampleBuckets.TryGetValue(key, out bucket))
                    sampleBuckets[key] = bucket = new List<Vector3D>();
                bucket.Add(p);
            }
        }

        public bool TryFindRoute(Vector3D start, Vector3D end, out List<Vector3D> route)
        {
            cancellation.ThrowIfCancellationRequested();
            LastFailureReason = null;
            LastExpandedNodes = 0;
            string key = RouteKey(start, end);
            List<Vector3D> cached;
            if (routeCache.TryGetValue(key, out cached))
            {
                route = new List<Vector3D>(cached);
                return true;
            }

            SearchCount++;
            if (SegmentClear(start, end))
            {
                route = new List<Vector3D>
                {
                    end
                };
                CacheBothDirections(start, end, route);
                return true;
            }

            List<Vector3D> gridPath;
            if (!TryGridRoute(start, end, out gridPath))
            {
                FallbackCount++;
                route = null;
                return false;
            }

            List<Vector3D> shortened = Shortcut(start, gridPath);
            route = RoundCorners(start, shortened);
            if (!Validate(start, route))
                route = shortened;
            if (!Validate(start, route))
            {
                LastFailureReason = "post-process-validation";
                FallbackCount++;
                route = null;
                return false;
            }

            CacheBothDirections(start, end, route);
            return true;
        }

        public bool TryRefineRoute(Vector3D start, IList<Vector3D> guide, out List<Vector3D> route)
        {
            route = null;
            if (guide == null || guide.Count == 0)
                return false;
            var raw = new List<Vector3D>(guide);
            List<Vector3D> shortened = Shortcut(start, raw);
            List<Vector3D> rounded = RoundCorners(start, shortened);
            if (Validate(start, rounded))
            {
                route = rounded;
                return true;
            }
            if (Validate(start, shortened))
            {
                route = shortened;
                return true;
            }
            return false;
        }

        public static double PathLength(Vector3D start, IList<Vector3D> route)
        {
            double total = 0;
            Vector3D current = start;
            if (route != null)
                for (int i = 0; i < route.Count; i++)
                {
                    total += Vector3D.Distance(current, route[i]);
                    current = route[i];
                }

            return total;
        }

        private bool TryGridRoute(Vector3D start, Vector3D end, out List<Vector3D> route)
        {
            route = null;
            BoundingBoxD search = occupied;
            search.Inflate(clearance + grid * 4);
            search.Include(start);
            search.Include(end);
            search.Inflate(grid * 2);
            NodeKey min = GridCell(search.Min), max = GridCell(search.Max);
            NodeKey startCell = GridCell(start);
            NodeKey endCell = GridCell(end);
            var open = new MinHeap();
            var g = new Dictionary<NodeKey, double>();
            var parents = new Dictionary<NodeKey, NodeKey>();
            var closed = new HashSet<NodeKey>();
            var clearCache = new Dictionary<NodeKey, bool>();
            var goalCells = new HashSet<NodeKey>();
            int endpointRadius = endpointSearchRadiusCells;
            for (int dx = -endpointRadius; dx <= endpointRadius; dx++)
                for (int dy = -endpointRadius; dy <= endpointRadius; dy++)
                    for (int dz = -endpointRadius; dz <= endpointRadius; dz++)
                    {
                        NodeKey goal = new NodeKey(endCell.X + dx, endCell.Y + dy, endCell.Z + dz);
                        if (!Inside(goal, min, max))
                            continue;
                        Vector3D p = GridPoint(goal);
                        if (CellClear(goal, p, clearCache) && SegmentClear(p, end))
                            goalCells.Add(goal);
                    }

            if (goalCells.Count == 0)
            {
                LastFailureReason = "no-goal-connection";
                return false;
            }
            for (int dx = -endpointRadius; dx <= endpointRadius; dx++)
                for (int dy = -endpointRadius; dy <= endpointRadius; dy++)
                    for (int dz = -endpointRadius; dz <= endpointRadius; dz++)
                    {
                        NodeKey first = new NodeKey(startCell.X + dx, startCell.Y + dy, startCell.Z + dz);
                        if (!Inside(first, min, max))
                            continue;
                        Vector3D p = GridPoint(first);
                        if (!CellClear(first, p, clearCache) || !SegmentClear(start, p))
                            continue;
                        double firstG = Vector3D.Distance(start, p);
                        double old;
                        if (g.TryGetValue(first, out old) && old <= firstG)
                            continue;
                        g[first] = firstG;
                        open.Push(first, firstG + Vector3D.Distance(p, end));
                    }

            if (open.Count == 0)
            {
                LastFailureReason = "no-start-connection";
                return false;
            }

            int expanded = 0;
            while (open.Count > 0 && expanded < maxExpandedNodes)
            {
                if ((expanded & 127) == 0)
                    cancellation.ThrowIfCancellationRequested();
                HeapItem item = open.Pop();
                NodeKey current = item.Key;
                if (closed.Contains(current))
                    continue;
                double currentG;
                if (!g.TryGetValue(current, out currentG))
                    continue;
                closed.Add(current);
                expanded++;
                LastExpandedNodes = expanded;
                Vector3D currentPoint = GridPoint(current);
                if (goalCells.Contains(current))
                {
                    route = Reconstruct(current, parents);
                    route.Add(end);
                    LastFailureReason = null;
                    return true;
                }

                NodeKey parent;
                Vector3D incoming = Vector3D.Zero;
                if (parents.TryGetValue(current, out parent))
                    incoming = Vector3D.Normalize(currentPoint - GridPoint(parent));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0)
                                continue;
                            NodeKey next = new NodeKey(current.X + dx, current.Y + dy, current.Z + dz);
                            if (!Inside(next, min, max) || closed.Contains(next))
                                continue;
                            Vector3D nextPoint = GridPoint(next);
                            if (!CellClear(next, nextPoint, clearCache) || !SegmentClear(currentPoint, nextPoint))
                                continue;
                            Vector3D outgoing = Vector3D.Normalize(nextPoint - currentPoint);
                            double edge = Vector3D.Distance(currentPoint, nextPoint);
                            double bend = incoming.LengthSquared() > 0.5 ? edge * 0.08 * (1 - Vector3D.Dot(incoming, outgoing)) : 0;
                            double candidate = currentG + edge + bend;
                            double old;
                            if (g.TryGetValue(next, out old) && old <= candidate)
                                continue;
                            g[next] = candidate;
                            parents[next] = current;
                            open.Push(next, candidate + Vector3D.Distance(nextPoint, end));
                        }
            }

            LastFailureReason = expanded >= maxExpandedNodes ? "node-limit" : "search-exhausted";
            return false;
        }

        private List<Vector3D> Shortcut(Vector3D start, List<Vector3D> raw)
        {
            var full = new List<Vector3D>
            {
                start
            };
            full.AddRange(raw);
            var result = new List<Vector3D>();
            int current = 0;
            while (current + 1 < full.Count)
            {
                int next = full.Count - 1;
                while (next > current + 1 && !SegmentClear(full[current], full[next]))
                    next--;
                result.Add(full[next]);
                current = next;
            }

            return result;
        }

        private List<Vector3D> RoundCorners(Vector3D start, List<Vector3D> route)
        {
            if (turnRadius <= 0 || route.Count < 2)
                return new List<Vector3D>(route);
            var full = new List<Vector3D>
            {
                start
            };
            full.AddRange(route);
            var result = new List<Vector3D>();
            Vector3D last = start;
            for (int i = 1; i + 1 < full.Count; i++)
            {
                Vector3D previous = full[i - 1], corner = full[i], next = full[i + 1];
                Vector3D inDirection = SafeNormalize(corner - previous);
                Vector3D outDirection = SafeNormalize(next - corner);
                double trim = Math.Min(turnRadius, Math.Min(Vector3D.Distance(previous, corner), Vector3D.Distance(corner, next)) * 0.35);
                if (trim < 1 || Vector3D.Dot(inDirection, outDirection) > 0.995)
                {
                    result.Add(corner);
                    last = corner;
                    continue;
                }

                Vector3D entry = corner - inDirection * trim;
                Vector3D exit = corner + outDirection * trim;
                int steps = Math.Max(3, (int)Math.Ceiling((trim * 2) / 8.0));
                var curve = new List<Vector3D>
                {
                    entry
                };
                for (int step = 1; step <= steps; step++)
                {
                    double t = step / (double)steps, one = 1 - t;
                    curve.Add(entry * (one * one) + corner * (2 * one * t) + exit * (t * t));
                }

                bool safe = SegmentClear(last, entry);
                for (int n = 1; safe && n < curve.Count; n++)
                    safe = SegmentClear(curve[n - 1], curve[n]);
                if (!safe)
                {
                    result.Add(corner);
                    last = corner;
                    continue;
                }

                result.AddRange(curve);
                last = curve[curve.Count - 1];
            }

            result.Add(full[full.Count - 1]);
            return result;
        }

        private bool Validate(Vector3D start, IList<Vector3D> route)
        {
            Vector3D current = start;
            for (int i = 0; i < route.Count; i++)
            {
                if (!SegmentClear(current, route[i]))
                    return false;
                current = route[i];
            }

            return true;
        }

        internal bool SegmentClear(Vector3D a, Vector3D b)
        {
            double length = Vector3D.Distance(a, b);
            int steps = Math.Max(1, (int)Math.Ceiling(length / 6.0));
            double sampleGuard = length / steps * 0.5;
            for (int i = 0; i <= steps; i++)
            {
                if ((i & 63) == 0)
                    cancellation.ThrowIfCancellationRequested();
                if (!PointClear(Vector3D.Lerp(a, b, i / (double)steps), clearance + sampleGuard))
                    return false;
            }

            return true;
        }

        private bool CellClear(NodeKey key, Vector3D point, Dictionary<NodeKey, bool> cache)
        {
            bool clear;
            if (cache.TryGetValue(key, out clear))
                return clear;
            clear = PointClear(point, clearance);
            cache[key] = clear;
            return clear;
        }

        private bool PointClear(Vector3D point, double radius)
        {
            if (samples.Count == 0)
                return true;
            int minX = (int)Math.Floor((point.X - radius) / bucketSize), maxX = (int)Math.Floor((point.X + radius) / bucketSize);
            int minY = (int)Math.Floor((point.Y - radius) / bucketSize), maxY = (int)Math.Floor((point.Y + radius) / bucketSize);
            int minZ = (int)Math.Floor((point.Z - radius) / bucketSize), maxZ = (int)Math.Floor((point.Z + radius) / bucketSize);
            double radiusSq = radius * radius;
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        List<Vector3D> bucket;
                        if (!sampleBuckets.TryGetValue(BucketKey(x, y, z), out bucket))
                            continue;
                        for (int i = 0; i < bucket.Count; i++)
                            if (Vector3D.DistanceSquared(point, bucket[i]) < radiusSq)
                                return false;
                    }

            return true;
        }

        private List<Vector3D> Reconstruct(NodeKey end, Dictionary<NodeKey, NodeKey> parents)
        {
            var route = new List<Vector3D>();
            NodeKey current = end;
            route.Add(GridPoint(current));
            NodeKey parent;
            while (parents.TryGetValue(current, out parent))
            {
                current = parent;
                route.Add(GridPoint(current));
            }

            route.Reverse();
            return route;
        }

        private void CacheBothDirections(Vector3D start, Vector3D end, List<Vector3D> route)
        {
            routeCache[RouteKey(start, end)] = new List<Vector3D>(route);
            var full = new List<Vector3D>
            {
                start
            };
            full.AddRange(route);
            full.Reverse();
            if (full.Count > 0)
                full.RemoveAt(0);
            routeCache[RouteKey(end, start)] = full;
        }

        private string RouteKey(Vector3D a, Vector3D b) => PointKey(a) + "|" + PointKey(b);
        private string PointKey(Vector3D p) => Math.Round(p.X, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + "," + Math.Round(p.Y, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + "," + Math.Round(p.Z, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        private NodeKey GridCell(Vector3D p) => new NodeKey((int)Math.Floor(p.X / grid), (int)Math.Floor(p.Y / grid), (int)Math.Floor(p.Z / grid));
        private Vector3D GridPoint(NodeKey key) => new Vector3D((key.X + 0.5) * grid, (key.Y + 0.5) * grid, (key.Z + 0.5) * grid);
        private bool Inside(NodeKey p, NodeKey min, NodeKey max) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && p.Z >= min.Z && p.Z <= max.Z;
        private string BucketKey(Vector3D p) => BucketKey((int)Math.Floor(p.X / bucketSize), (int)Math.Floor(p.Y / bucketSize), (int)Math.Floor(p.Z / bucketSize));
        private static string BucketKey(int x, int y, int z) => x + ":" + y + ":" + z;
        private static Vector3D SafeNormalize(Vector3D value) => value.LengthSquared() > 1e-12 ? Vector3D.Normalize(value) : Vector3D.Zero;
        private struct NodeKey : IEquatable<NodeKey>
        {
            public readonly int X, Y, Z;
            public NodeKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(NodeKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is NodeKey && Equals((NodeKey)obj);
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((X * 397) ^ Y) * 397 ^ Z;
                }
            }
        }

        private struct HeapItem
        {
            public NodeKey Key;
            public double Priority;
        }

        private sealed class MinHeap
        {
            private readonly List<HeapItem> items = new List<HeapItem>();
            public int Count => items.Count;

            public void Push(NodeKey key, double priority)
            {
                items.Add(new HeapItem { Key = key, Priority = priority });
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].Priority <= items[i].Priority)
                        break;
                    HeapItem swap = items[parent];
                    items[parent] = items[i];
                    items[i] = swap;
                    i = parent;
                }
            }

            public HeapItem Pop()
            {
                HeapItem root = items[0], last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0)
                    return root;
                items[0] = last;
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, best = i;
                    if (left < items.Count && items[left].Priority < items[best].Priority)
                        best = left;
                    if (right < items.Count && items[right].Priority < items[best].Priority)
                        best = right;
                    if (best == i)
                        break;
                    HeapItem swap = items[best];
                    items[best] = items[i];
                    items[i] = swap;
                    i = best;
                }

                return root;
            }
        }
    }
}
