using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        private static List<OreGroup> BuildGroups(AsteroidMap map, OreSelection selection, PlannerSettings settings, PlanningCancellation cancellation)
        {
            var all = new List<MaterialSample>();
            foreach (var pair in map.OreSamples)
                if (selection == null || selection.ShouldMine(pair.Key))
                    all.AddRange(pair.Value);
            var groups = new List<OreGroup>();
            if (all.Count == 0)
                return groups;
            double cell = Math.Max(1, settings.VeinJoinMeters);
            var buckets = new Dictionary<string, List<int>>();
            for (int i = 0; i < all.Count; i++)
            {
                if ((i & 1023) == 0)
                    cancellation.ThrowIfCancellationRequested();
                string key = CellKey(all[i].Position, cell);
                List<int> list;
                if (!buckets.TryGetValue(key, out list))
                    buckets[key] = list = new List<int>();
                list.Add(i);
            }

            var seen = new bool[all.Count];
            double joinSq = settings.VeinJoinMeters * settings.VeinJoinMeters;
            for (int root = 0; root < all.Count; root++)
            {
                if ((root & 255) == 0)
                    cancellation.ThrowIfCancellationRequested();
                if (seen[root])
                    continue;
                var group = new OreGroup();
                var queue = new Queue<int>();
                queue.Enqueue(root);
                seen[root] = true;
                while (queue.Count > 0)
                {
                    if ((queue.Count & 255) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    int i = queue.Dequeue();
                    MaterialSample sample = all[i];
                    group.Add(sample);
                    Vector3I c = Cell(sample.Position, cell);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                List<int> list;
                                if (!buckets.TryGetValue(Key(c.X + dx, c.Y + dy, c.Z + dz), out list))
                                    continue;
                                for (int n = 0; n < list.Count; n++)
                                {
                                    int j = list[n];
                                    if (!seen[j] && Vector3D.DistanceSquared(sample.Position, all[j].Position) <= joinSq)
                                    {
                                        seen[j] = true;
                                        queue.Enqueue(j);
                                    }
                                }
                            }
                }

                groups.Add(group);
            }

            bool merged;
            do
            {
                cancellation.ThrowIfCancellationRequested();
                merged = false;
                for (int i = 0; i < groups.Count && !merged; i++)
                    for (int j = i + 1; j < groups.Count; j++)
                    {
                        if (BoxDistance(groups[i].Box, groups[j].Box) <= settings.GroupJoinMeters)
                        {
                            groups[i].Merge(groups[j]);
                            groups.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
            }
            while (merged);
            return groups;
        }

        private static MiningPlane FindPlane(AsteroidMap map, OreGroup group, PlannerSettings s, PlanningCancellation cancellation)
        {
            MiningPlane best = null;
            double bestCost = double.MaxValue;
            int directions = 0, projectedSurfaceFound = 0, blockedWorkspaces = 0;
            foreach (Vector3D outward in FibonacciDirections(Math.Max(12, s.CandidateDirections)))
            {
                cancellation.ThrowIfCancellationRequested();
                directions++;
                Vector3D forward = -outward, up = BestUp(forward, Vector3D.Up), right = Vector3D.Normalize(Vector3D.Cross(forward, up));
                up = Vector3D.Normalize(Vector3D.Cross(right, forward));
                double minR = double.MaxValue, maxR = double.MinValue, minU = double.MaxValue, maxU = double.MinValue;
                int sampleIndex = 0;
                foreach (MaterialSample sample in group.Samples)
                {
                    if ((sampleIndex++ & 1023) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    double r = Vector3D.Dot(sample.Position, right), u = Vector3D.Dot(sample.Position, up);
                    minR = Math.Min(minR, r);
                    maxR = Math.Max(maxR, r);
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                }

                minR -= s.OrePadding;
                maxR += s.OrePadding;
                minU -= s.OrePadding;
                maxU += s.OrePadding;
                double width = Math.Max(s.MineWidth, maxR - minR), height = Math.Max(s.MineHeight, maxU - minU), centerR = (minR + maxR) * 0.5, centerU = (minU + maxU) * 0.5;
                double surface = double.MinValue;
                sampleIndex = 0;
                foreach (MaterialSample sample in map.Samples)
                {
                    if ((sampleIndex++ & 1023) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    double r = Vector3D.Dot(sample.Position, right), u = Vector3D.Dot(sample.Position, up);
                    if (Math.Abs(r - centerR) <= width * 0.5 && Math.Abs(u - centerU) <= height * 0.5)
                        surface = Math.Max(surface, Vector3D.Dot(sample.Position, outward));
                }

                if (surface == double.MinValue)
                    continue;
                projectedSurfaceFound++;
                // The scan positions are centers of 4 m cells. Add half a cell so
                // PlaneClearance is measured from the outside edge of the rock.
                double planeAxis = surface + s.PollMeters * 0.5 + s.PlaneClearance, deepest = double.MaxValue, totalDepth = 0;
                foreach (MaterialSample sample in group.Samples)
                {
                    double axis = Vector3D.Dot(sample.Position, outward);
                    deepest = Math.Min(deepest, axis);
                    totalDepth += planeAxis - axis;
                }

                Vector3D center = outward * planeAxis + right * centerR + up * centerU;
                Vector3D staging = center + outward * s.TravelClearance;
                if (!GeometrySafety.WorkspaceClear(map.Samples, center, outward, right, up, width, height, s.TravelClearance, s.PollMeters))
                {
                    blockedWorkspaces++;
                    continue;
                }

                double cost = totalDepth / Math.Max(1, group.Samples.Count);
                if (cost >= bestCost)
                    continue;
                bestCost = cost;
                best = new MiningPlane
                {
                    Center = center,
                    Staging = staging,
                    Forward = forward,
                    Up = up,
                    Right = right,
                    Width = width,
                    Height = height,
                    Depth = Math.Max(0.01, planeAxis - deepest)
                };
                best.CornerA = center - right * width * 0.5 + up * height * 0.5;
                best.CornerB = center + right * width * 0.5 - up * height * 0.5;
                foreach (string ore in group.Ores)
                    best.Ores.Add(ore);
                BuildLaneMetadata(best, group, s, cancellation);
            }

            if (best == null)
                Emma3Log.Diagnostic("Mining-plane rejection asteroid=" + map.EntityId
                    + " ores=" + string.Join(",", group.Ores)
                    + " samples=" + group.Samples.Count
                    + " box=" + group.Box
                    + " directions=" + directions
                    + " projectedSurface=" + projectedSurfaceFound
                    + " blockedWorkspace=" + blockedWorkspaces
                    + ".");
            return best;
        }

        private static void BuildLaneMetadata(MiningPlane p, OreGroup group, PlannerSettings s, PlanningCancellation cancellation)
        {
            int rows = Math.Max(1, (int)Math.Ceiling(p.Height / s.MineHeight)), cols = Math.Max(1, (int)Math.Ceiling(p.Width / s.MineWidth));
            double rowStart = rows > 1 ? s.MineHeight * 0.5 : p.Height * 0.5, rowStep = rows > 1 ? (p.Height - s.MineHeight) / (rows - 1) : 0;
            double colStart = cols > 1 ? s.MineWidth * 0.5 : p.Width * 0.5, colStep = cols > 1 ? (p.Width - s.MineWidth) / (cols - 1) : 0;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int actualCol = (row & 1) == 1 ? cols - 1 - col : col;
                    Vector3D anchor = p.CornerA - p.Up * (rowStart + row * rowStep) + p.Right * (colStart + actualCol * colStep);
                    double deepestLaneOre = double.MinValue;
                    foreach (MaterialSample sample in group.Samples)
                    {
                        Vector3D d = sample.Position - anchor;
                        double depth = Vector3D.Dot(d, p.Forward);
                        if (Math.Abs(Vector3D.Dot(d, p.Right)) <= s.MineWidth * 0.5 + s.OrePollMeters && Math.Abs(Vector3D.Dot(d, p.Up)) <= s.MineHeight * 0.5 + s.OrePollMeters && depth >= -s.OrePollMeters)
                            deepestLaneOre = Math.Max(deepestLaneOre, depth);
                    }

                    if (deepestLaneOre == double.MinValue)
                    {
                        p.SkipLanes.Add(row * cols + col);
                        p.LaneDepths.Add(0);
                        p.LaneDestructionRanges.Add(new List<DepthInterval>());
                    }
                    else
                    {
                        p.LaneDepths.Add(Math.Min(p.Depth, Math.Max(0.01, deepestLaneOre)));
                        p.LaneDestructionRanges.Add(new List<DepthInterval>());
                    }
                }
        }
    }
}
