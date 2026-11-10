using System;
using System.Collections.Generic;
using VRage.Game;
using VRageMath;
using VRage.Utils;

namespace EMMA3.Client
{
    internal sealed class PlanVisualizer
    {
        private static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("WeaponLaserIgnoreDepth");
        private static readonly Color NormalLaneColor = new Color(255, 150, 20);
        private static readonly Color DestructionLaneColor = Color.Red;
        private static readonly Color ApproachColor = Color.Magenta;
        public int LastLineCount
        {
            get; private set;
        }
        public int LastOreSampleCount
        {
            get; private set;
        }
        public int LastPlaneCount
        {
            get; private set;
        }
        public int LastCarrierRouteCount
        {
            get; private set;
        }
        public int LastMinerRouteCount
        {
            get; private set;
        }
        public string LastSkipReason { get; private set; } = "";

        private int lineCount;
        public void Draw(AsteroidMap scanning, MissionPlan plan, PlannerSettings settings, bool enabled, string completedCarrierRouteMission = "")
        {
            lineCount = 0;
            LastLineCount = 0;
            LastOreSampleCount = CountOreSamples(scanning);
            LastPlaneCount = plan != null ? plan.Planes.Count : 0;
            bool showCarrierRoute = plan != null && (string.IsNullOrEmpty(plan.MissionId)
                || !string.Equals(plan.MissionId, completedCarrierRouteMission, StringComparison.Ordinal));
            LastCarrierRouteCount = showCarrierRoute ? plan.CarrierRoute.Count : 0;
            LastMinerRouteCount = plan != null ? plan.MinerRoute.Count : 0;
            LastSkipReason = "";
            if (!enabled)
            {
                LastSkipReason = "disabled";
                return;
            }

            if (scanning != null)
            {
                int stride = Math.Max(1, scanning.Samples.Count / 1500);
                for (int i = 0; i < scanning.Samples.Count; i += stride)
                {
                    MaterialSample sample = scanning.Samples[i];
                    if (!sample.IsStone)
                        DrawCross(sample.Position, Color.Yellow, 0.45);
                }
            }

            if (plan == null && scanning == null)
            {
                LastSkipReason = "no scan or plan";
                LastLineCount = lineCount;
                return;
            }

            if (plan == null)
            {
                LastLineCount = lineCount;
                return;
            }

            foreach (MiningPlane p in plan.Planes)
            {
                DrawLine(p.CornerA, p.CornerA + p.Right * p.Width, Color.Cyan, 0.16f);
                DrawLine(p.CornerA, p.CornerA - p.Up * p.Height, Color.Cyan, 0.16f);
                DrawLine(p.CornerB, p.CornerB - p.Right * p.Width, Color.Cyan, 0.16f);
                DrawLine(p.CornerB, p.CornerB + p.Up * p.Height, Color.Cyan, 0.16f);
                DrawLine(p.Center, p.Staging, ApproachColor, 0.22f);
                DrawCross(p.Staging, ApproachColor, 1.2);
                DrawLaneDepths(p, settings);
            }

            Color routeColor = new Color(0, 200, 255);
            if (showCarrierRoute)
            {
                // Direct-to-park plans contain only the destination, not a flight waypoint at the start.
                if (plan.CarrierRoute.Count == 1 && plan.CarrierApproachStart.HasValue
                    && Vector3D.DistanceSquared(plan.CarrierApproachStart.Value, plan.CarrierRoute[0].Position) > 0.000001)
                    DrawLine(plan.CarrierApproachStart.Value, plan.CarrierRoute[0].Position, ApproachColor, 0.42f);
                DrawRoute(plan.CarrierRoute, routeColor, 0.42f, true);
                DrawCross(plan.CarrierPosition, routeColor, 2.0);
            }
            DrawRoute(plan.MinerRoute, routeColor, 0.225f);
            LastLineCount = lineCount;
        }

        private void DrawLaneDepths(MiningPlane plane, PlannerSettings settings)
        {
            if (plane == null || settings == null || plane.LaneDepths.Count == 0)
                return;
            int rows = Math.Max(1, (int)Math.Ceiling(plane.Height / settings.MineHeight));
            int cols = Math.Max(1, (int)Math.Ceiling(plane.Width / settings.MineWidth));
            int laneCount = rows * cols;
            if (plane.LaneDepths.Count != laneCount)
                return;
            double rowStart = rows > 1 ? settings.MineHeight * 0.5 : plane.Height * 0.5;
            double rowStep = rows > 1 ? (plane.Height - settings.MineHeight) / (rows - 1) : 0;
            double colStart = cols > 1 ? settings.MineWidth * 0.5 : plane.Width * 0.5;
            double colStep = cols > 1 ? (plane.Width - settings.MineWidth) / (cols - 1) : 0;
            double maxDepth = 0;
            for (int i = 0; i < laneCount; i++)
                maxDepth = Math.Max(maxDepth, plane.LaneDepths[i]);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int laneIndex = row * cols + col;
                    int actualCol = (row & 1) == 1 ? cols - 1 - col : col;
                    Vector3D anchor = plane.CornerA - plane.Up * (rowStart + row * rowStep) + plane.Right * (colStart + actualCol * colStep);
                    double depth = plane.LaneDepths[laneIndex];
                    if (depth <= 0 || plane.SkipLanes.Contains(laneIndex))
                    {
                        DrawPlaneCross(anchor, plane.Right, plane.Up, Color.Gray, 0.65);
                        continue;
                    }

                    Vector3D end = anchor + plane.Forward * depth;
                    if (plane.LaneDestructionRanges.Count == laneCount)
                    {
                        List<DepthInterval> ranges = plane.LaneDestructionRanges[laneIndex];
                        if (ranges != null && ranges.Count > 0)
                        {
                            DrawLaneSegments(anchor, plane.Forward, depth, ranges);
                            DrawPlaneCross(end, plane.Right, plane.Up, NormalLaneColor, 0.45);
                            continue;
                        }
                    }

                    DrawLine(anchor, end, NormalLaneColor, 0.12f);
                    DrawPlaneCross(end, plane.Right, plane.Up, NormalLaneColor, 0.45);
                }
            }
        }

        private void DrawLaneSegments(Vector3D anchor, Vector3D forward, double depth, List<DepthInterval> destructionRanges)
        {
            double cursor = 0;
            for (int n = 0; n < destructionRanges.Count; n++)
            {
                double start = MathHelper.Clamp(destructionRanges[n].Start, 0, depth);
                double finish = MathHelper.Clamp(destructionRanges[n].End, 0, depth);
                if (finish <= start)
                    continue;
                if (start > cursor)
                    DrawLine(anchor + forward * cursor, anchor + forward * start, NormalLaneColor, 0.12f);
                DrawLine(anchor + forward * start, anchor + forward * finish, DestructionLaneColor, 0.18f);
                cursor = Math.Max(cursor, finish);
            }

            if (cursor < depth)
                DrawLine(anchor + forward * cursor, anchor + forward * depth, NormalLaneColor, 0.12f);
        }

        private void DrawRoute(System.Collections.Generic.IList<RoutePose> route, Color color, float thickness, bool highlightFinalApproach = false)
        {
            for (int i = 1; i < route.Count; i++)
                DrawLine(route[i - 1].Position, route[i].Position,
                    highlightFinalApproach && i == route.Count - 1 ? ApproachColor : color, thickness);
            foreach (RoutePose pose in route)
                if (pose.Kind != "plane" && pose.Kind != "drills-on")
                    DrawCross(pose.Position, color, 0.35);
        }

        private void DrawPlaneCross(Vector3D p, Vector3D right, Vector3D up, Color color, double size)
        {
            DrawLine(p - right * size, p + right * size, color, 0.08f);
            DrawLine(p - up * size, p + up * size, color, 0.08f);
        }

        private void DrawCross(Vector3D p, Color color, double size)
        {
            DrawLine(p - Vector3D.Right * size, p + Vector3D.Right * size, color, 0.08f);
            DrawLine(p - Vector3D.Up * size, p + Vector3D.Up * size, color, 0.08f);
            DrawLine(p - Vector3D.Forward * size, p + Vector3D.Forward * size, color, 0.08f);
        }

        private void DrawLine(Vector3D a, Vector3D b, Color color, float thickness)
        {
            Vector4 value = color.ToVector4();
            MySimpleObjectDraw.DrawLine(a, b, LineMaterial, ref value, thickness * 3f);
            lineCount++;
        }

        private static int CountOreSamples(AsteroidMap scanning)
        {
            if (scanning == null)
                return 0;
            int count = 0;
            foreach (var pair in scanning.OreSamples)
                if (pair.Value != null)
                    count += pair.Value.Count;
            return count;
        }
    }
}
