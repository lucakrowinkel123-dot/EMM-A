using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        private static void BuildLaneDestructionRanges(MissionPlan plan, PlanningSnapshot snapshot, PlannerSettings settings, PlanningCancellation cancellation)
        {
            if (plan == null || snapshot == null || snapshot.MinerDrills.Count == 0)
                return;
            var protectedOre = new List<MaterialSample>();
            foreach (var pair in plan.Asteroid.OreSamples)
                protectedOre.AddRange(pair.Value);
            if (protectedOre.Count == 0)
                return;
            for (int planeIndex = 0; planeIndex < plan.Planes.Count; planeIndex++)
            {
                cancellation.ThrowIfCancellationRequested();
                MiningPlane plane = plan.Planes[planeIndex];
                int rows = Math.Max(1, (int)Math.Ceiling(plane.Height / settings.MineHeight));
                int cols = Math.Max(1, (int)Math.Ceiling(plane.Width / settings.MineWidth));
                int laneCount = rows * cols;
                while (plane.LaneDestructionRanges.Count < laneCount)
                    plane.LaneDestructionRanges.Add(new List<DepthInterval>());
                double rowStart = rows > 1 ? settings.MineHeight * 0.5 : plane.Height * 0.5;
                double rowStep = rows > 1 ? (plane.Height - settings.MineHeight) / (rows - 1) : 0;
                double colStart = cols > 1 ? settings.MineWidth * 0.5 : plane.Width * 0.5;
                double colStep = cols > 1 ? (plane.Width - settings.MineWidth) / (cols - 1) : 0;
                for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                    {
                        int laneIndex = row * cols + col;
                        plane.LaneDestructionRanges[laneIndex].Clear();
                        double laneDepth = laneIndex < plane.LaneDepths.Count ? plane.LaneDepths[laneIndex] : plane.Depth;
                        if (laneDepth <= 0 || plane.SkipLanes.Contains(laneIndex))
                            continue;
                        int actualCol = (row & 1) == 1 ? cols - 1 - col : col;
                        Vector3D anchor = plane.CornerA - plane.Up * (rowStart + row * rowStep) + plane.Right * (colStart + actualCol * colStep);
                        var collect = new List<DepthInterval>();
                        for (int sampleIndex = 0; sampleIndex < protectedOre.Count; sampleIndex++)
                        {
                            if ((sampleIndex & 1023) == 0)
                                cancellation.ThrowIfCancellationRequested();
                            MaterialSample sample = protectedOre[sampleIndex];
                            Vector3D rel = sample.Position - anchor;
                            double sx = Vector3D.Dot(rel, plane.Right);
                            double sy = Vector3D.Dot(rel, plane.Up);
                            double sz = Vector3D.Dot(rel, plane.Forward);
                            for (int drillIndex = 0; drillIndex < snapshot.MinerDrills.Count; drillIndex++)
                            {
                                DrillFootprint drill = snapshot.MinerDrills[drillIndex];
                                // Terrain-clearing mode reaches three times farther than the
                                // normal drill cutout. Protect ore against that full destructive
                                // sphere, then add the configured safety margin.
                                double radius = drill.Radius * settings.DestructionRadiusMultiplier + settings.DestructionProtectionMeters;
                                double lateralX = sx - (drill.LocalOffset.X - snapshot.MinerToolOffsetLocal.X);
                                double lateralY = sy - (drill.LocalOffset.Y - snapshot.MinerToolOffsetLocal.Y);
                                double lateralSq = lateralX * lateralX + lateralY * lateralY;
                                double radiusSq = radius * radius;
                                if (lateralSq > radiusSq)
                                    continue;
                                double along = Math.Sqrt(Math.Max(0, radiusSq - lateralSq));
                                double centerDepth = sz - (drill.LocalOffset.Z - snapshot.MinerToolOffsetLocal.Z);
                                AddInterval(collect, Math.Max(0, centerDepth - along), Math.Min(laneDepth, centerDepth + along));
                            }
                        }

                        var merged = MergeIntervals(collect);
                        plane.LaneDestructionRanges[laneIndex].AddRange(InvertIntervals(merged, laneDepth));
                    }
            }
        }

        private static void AddInterval(List<DepthInterval> intervals, double start, double end)
        {
            if (end <= start)
                return;
            intervals.Add(new DepthInterval(start, end));
        }

        private static List<DepthInterval> MergeIntervals(List<DepthInterval> intervals)
        {
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            var result = new List<DepthInterval>();
            for (int i = 0; i < intervals.Count; i++)
            {
                DepthInterval value = intervals[i];
                if (result.Count == 0 || value.Start > result[result.Count - 1].End)
                    result.Add(value);
                else if (value.End > result[result.Count - 1].End)
                {
                    DepthInterval last = result[result.Count - 1];
                    last.End = value.End;
                    result[result.Count - 1] = last;
                }
            }

            return result;
        }

        private static List<DepthInterval> InvertIntervals(List<DepthInterval> blocked, double laneDepth)
        {
            var result = new List<DepthInterval>();
            double current = 0;
            for (int i = 0; i < blocked.Count; i++)
            {
                if (blocked[i].Start - current >= 0.01)
                    result.Add(new DepthInterval(current, blocked[i].Start));
                current = Math.Max(current, blocked[i].End);
            }

            if (laneDepth - current >= 0.01)
                result.Add(new DepthInterval(current, laneDepth));
            return result;
        }
    }
}
