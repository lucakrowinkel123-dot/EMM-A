using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        private static void BuildMinerRoute(
            MissionPlan plan,
            PlannerSettings s,
            OrganicVoxelRouter organicRouter,
            ref OrganicVoxelRouter relaxedOrganicRouter,
            PlanningCancellation cancellation,
            PlanningProgress progress)
        {
            Vector3D f = plan.MinerDockForward, u = plan.MinerDockUp;
            BoundingBoxD avoid = plan.Asteroid.OccupiedBox;
            avoid.Inflate(s.TravelClearance);
            // The Miner's `undock` record already flies through the inner pose to
            // MinerDeparturePosition (the outer dock pose plus any route-start
            // extra). Do not encode those same legs as path waypoints.
            Vector3D current = plan.MinerDeparturePosition;
            Vector3D currentOutward = SafeNormalize(plan.MinerDeparturePosition - plan.Asteroid.OccupiedBox.Center, -plan.CarrierForward);
            int totalLegs = plan.Planes.Count + 1;
            for (int i = 0; i < plan.Planes.Count; i++)
            {
                MiningPlane plane = plan.Planes[i];
                Vector3D planeOutward = SafeNormalize(plane.Staging - plane.Center, -plane.Forward);
                progress.Set("Routing leg " + (i + 1) + "/" + totalLegs);
                var legTimer = Stopwatch.StartNew();
                AddVoxelAwareTransit(
                    plan.MinerRoute,
                    current,
                    currentOutward,
                    plane.Staging,
                    planeOutward,
                    f,
                    u,
                    s,
                    avoid,
                    organicRouter,
                    ref relaxedOrganicRouter,
                    plan.Asteroid,
                    cancellation,
                    plan.MissionId);
                Emma3Log.Plan(plan.MissionId, "Timing route leg " + (i + 1) + "/" + totalLegs + "=" + legTimer.Elapsed.TotalMilliseconds.ToString("0") + "ms");
                // Hold the exact plane orientation at staging before enabling the
                // drills and entering the validated radial mining corridor.
                plan.MinerRoute.Add(new RoutePose { Position = plane.Staging, Forward = plane.Forward, Up = plane.Up, Kind = "waypoint" });
                current = plane.Staging;
                // Only the final clear-space approach to the mining plane should
                // run the drills. Ordinary asteroid transit remains drill-off.
                plan.MinerRoute.Add(new RoutePose { Position = plane.Staging, Forward = plane.Forward, Up = plane.Up, Kind = "drills-on" });
                AddSpaced(plan.MinerRoute, current, plane.Center, plane.Forward, plane.Up, s.NearPathSpacing, null);
                current = plane.Center;
                plan.MinerRoute.Add(new RoutePose { Position = plane.Center, Forward = plane.Forward, Up = plane.Up, Kind = "plane" });
                // Withdraw from the plane to its staging point without turning the
                // Miner around. Reverse waypoints retain the plane orientation,
                // so the drills/nose continue to face the vein while translating
                // backward until the ordinary safe route resumes at staging.
                plan.MinerRoute.Add(new RoutePose { Position = plane.Staging, Forward = plane.Forward, Up = plane.Up, Kind = "reverse-waypoint" });
                current = plane.Staging;
                currentOutward = planeOutward;
            }

            // Return to the outer path anchor above the inner pose. The final
            // `dock` record then handles alignment, inner-pose travel, and connection.
            Vector3D departureOutward = SafeNormalize(plan.MinerDeparturePosition - plan.Asteroid.OccupiedBox.Center, -plan.CarrierForward);
            progress.Set("Routing leg " + totalLegs + "/" + totalLegs);
            var returnTimer = Stopwatch.StartNew();
            AddVoxelAwareTransit(
                plan.MinerRoute,
                current,
                currentOutward,
                plan.MinerDeparturePosition,
                departureOutward,
                f,
                u,
                s,
                avoid,
                organicRouter,
                ref relaxedOrganicRouter,
                plan.Asteroid,
                cancellation,
                plan.MissionId);
            Emma3Log.Plan(
                plan.MissionId,
                "Timing route leg " + totalLegs + "/" + totalLegs + "=" + returnTimer.Elapsed.TotalMilliseconds.ToString("0") + "ms return");
        }

        private static void AddVoxelAwareTransit(
            List<RoutePose> route,
            Vector3D from,
            Vector3D fromOutward,
            Vector3D to,
            Vector3D toOutward,
            Vector3D forward,
            Vector3D up,
            PlannerSettings settings,
            BoundingBoxD fallbackAvoid,
            OrganicVoxelRouter organicRouter,
            ref OrganicVoxelRouter relaxedOrganicRouter,
            AsteroidMap asteroid,
            PlanningCancellation cancellation,
            string missionId)
        {
            List<Vector3D> organic;
            if (organicRouter.TryFindRoute(from, to, out organic))
            {
                Vector3D current = from;
                for (int i = 0; i < organic.Count; i++)
                {
                    AddSpaced(route, current, organic[i], forward, up, settings.OpenPathSpacing, null);
                    current = organic[i];
                }

                return;
            }
            string fineFailure = organicRouter.LastFailureReason ?? "unknown";
            int fineExpanded = organicRouter.LastExpandedNodes;

            // If the close organic route cannot validate a leg, try one coarser
            // voxel-aware pass before using the old rectangular-box detour. This
            // keeps odd legs near the scanned asteroid surface instead of jumping
            // out to far AABB corners whenever the fine grid exhausts its budget.
            if (relaxedOrganicRouter == null)
                relaxedOrganicRouter = new OrganicVoxelRouter(
                    asteroid.Samples,
                    asteroid.OccupiedBox,
                    settings.TravelClearance,
                    settings.PollMeters,
                    24.0,
                    15.0,
                    cancellation,
                    300000,
                    4);
            if (relaxedOrganicRouter.TryFindRoute(from, to, out organic))
            {
                List<Vector3D> refined;
                if (organicRouter.TryRefineRoute(from, organic, out refined))
                    organic = refined;
                Vector3D current = from;
                for (int i = 0; i < organic.Count; i++)
                {
                    AddSpaced(route, current, organic[i], forward, up, settings.OpenPathSpacing, null);
                    current = organic[i];
                }

                return;
            }

            Emma3Log.Plan(
                missionId,
                "Miner route using box fallback fineFailure=" + fineFailure
                + " fineExpanded=" + fineExpanded
                + " relaxedFailure=" + (relaxedOrganicRouter.LastFailureReason ?? "unknown")
                + " relaxedExpanded=" + relaxedOrganicRouter.LastExpandedNodes
                + " from=" + from
                + " to=" + to);

            // Proven v0.1.34 box route retained as a fail-closed fallback.
            Vector3D fromGate = GeometrySafety.MoveOutsideBoxAlong(from, fromOutward, fallbackAvoid, 5);
            Vector3D toGate = GeometrySafety.MoveOutsideBoxAlong(to, toOutward, fallbackAvoid, 5);
            if (Vector3D.DistanceSquared(from, fromGate) > 0.01)
                AddSpaced(route, from, fromGate, forward, up, settings.NearPathSpacing, null);
            AddSpaced(route, fromGate, toGate, forward, up, settings.OpenPathSpacing, fallbackAvoid);
            if (Vector3D.DistanceSquared(toGate, to) > 0.01)
                AddSpaced(route, toGate, to, forward, up, settings.NearPathSpacing, null);
        }

        private static void AddSpaced(
            List<RoutePose> route,
            Vector3D from,
            Vector3D to,
            Vector3D forward,
            Vector3D up,
            double spacing,
            BoundingBoxD? obstacle,
            string kind = "waypoint")
        {
            var points = obstacle.HasValue ? RouteAroundBoxes(from, to, new List<BoundingBoxD> { obstacle.Value }, 4, true, true) : new List<Vector3D>
            {
                to
            };
            Vector3D start = from;
            foreach (Vector3D end in points)
            {
                double distance = Vector3D.Distance(start, end);
                int count = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(1, spacing)));
                for (int i = 1; i <= count; i++)
                    route.Add(new RoutePose { Position = Vector3D.Lerp(start, end, i / (double)count), Forward = forward, Up = up, Kind = kind });
                start = end;
            }
        }

        internal static List<Vector3D> RouteAroundBoxes(
            Vector3D start,
            Vector3D end,
            IList<BoundingBoxD> boxes,
            int maxDetours,
            bool requireClear,
            bool useTwoCornerDetours = false)
        {
            var result = new List<Vector3D>();
            Vector3D current = start;
            for (int n = 0; n < maxDetours; n++)
            {
                BoundingBoxD hit = default(BoundingBoxD);
                bool found = false;
                double bestT = double.MaxValue;
                foreach (BoundingBoxD box in boxes)
                {
                    double d;
                    if (SegmentBoxEntryDistance(current, end, box, out d) && d > 0.5 && d < bestT)
                    {
                        bestT = d;
                        hit = box;
                        found = true;
                    }
                }

                if (!found)
                    break;
                List<Vector3D> detour;
                if (useTwoCornerDetours && GeometrySafety.TryFindShortestBoxPath(current, end, hit, out detour))
                {
                    // The shortest-path result includes the final destination;
                    // leave that for the normal loop terminator so it is not
                    // duplicated in the generated timer route.
                    for (int i = 0; i + 1 < detour.Count; i++)
                        result.Add(detour[i]);
                    if (detour.Count > 1)
                        current = detour[detour.Count - 2];
                    continue;
                }

                Vector3D best = Vector3D.Zero;
                double cost = double.MaxValue;
                foreach (Vector3D corner in hit.GetCorners())
                {
                    Vector3D outward = SafeNormalize(corner - hit.Center, Vector3D.Up);
                    Vector3D candidate = corner + outward * 5;
                    double c = Vector3D.DistanceSquared(current, candidate) + Vector3D.DistanceSquared(candidate, end);
                    if (c < cost)
                    {
                        cost = c;
                        best = candidate;
                    }
                }

                if (best == Vector3D.Zero)
                    break;
                result.Add(best);
                current = best;
            }

            if (requireClear)
            {
                foreach (BoundingBoxD box in boxes)
                {
                    double remaining;
                    if (SegmentBoxEntryDistance(current, end, box, out remaining) && remaining > 0.5)
                        throw new InvalidOperationException("Travel blocked after " + maxDetours + " asteroid detours");
                }
            }

            result.Add(end);
            return result;
        }

        private static bool SegmentBoxEntryDistance(Vector3D a, Vector3D b, BoundingBoxD box, out double distance)
        {
            distance = 0;
            Vector3D d = b - a;
            double length = d.Length();
            if (length < 1e-8)
                return box.Contains(a) != ContainmentType.Disjoint;
            double tMin = 0, tMax = 1;
            if (!ClipAxis(a.X, d.X, box.Min.X, box.Max.X, ref tMin, ref tMax) || !ClipAxis(a.Y, d.Y, box.Min.Y, box.Max.Y, ref tMin, ref tMax) || !ClipAxis(a.Z, d.Z, box.Min.Z, box.Max.Z, ref tMin, ref tMax))
                return false;
            distance = tMin * length;
            return tMax >= tMin;
        }

        private static bool ClipAxis(double origin, double direction, double min, double max, ref double tMin, ref double tMax)
        {
            if (Math.Abs(direction) < 1e-12)
                return origin >= min && origin <= max;
            double a = (min - origin) / direction, b = (max - origin) / direction;
            if (a > b)
            {
                double t = a;
                a = b;
                b = t;
            }

            tMin = Math.Max(tMin, a);
            tMax = Math.Min(tMax, b);
            return tMin <= tMax;
        }

        private static void OrderPlanes(
            List<MiningPlane> planes,
            Vector3D departure,
            BoundingBoxD occupied,
            PlannerSettings settings,
            Vector3D carrierForward,
            PlanningProgress progress)
        {
            int count = planes.Count;
            if (count < 2)
                return;
            BoundingBoxD avoid = occupied;
            avoid.Inflate(settings.TravelClearance);
            Vector3D departureOutward = SafeNormalize(departure - occupied.Center, -carrierForward);
            Vector3D startGate = GeometrySafety.MoveOutsideBoxAlong(departure, departureOutward, avoid, 5);
            var gates = new Vector3D[count];
            var points = new Vector3D[count + 1];
            points[count] = departure;
            for (int i = 0; i < count; i++)
            {
                Vector3D outward = SafeNormalize(planes[i].Staging - planes[i].Center, -planes[i].Forward);
                gates[i] = GeometrySafety.MoveOutsideBoxAlong(planes[i].Staging, outward, avoid, 5);
                points[i] = planes[i].Staging;
            }

            var distances = new double[count + 1, count + 1];
            int pair = 0;
            int totalPairs = count * (count + 1) / 2;
            for (int i = 0; i <= count; i++)
                for (int j = i + 1; j <= count; j++)
                {
                    pair++;
                    progress.Set("Ordering mining planes " + pair + "/" + totalPairs);
                    Vector3D a = points[i], b = points[j];
                    Vector3D aGate = i == count ? startGate : gates[i];
                    Vector3D bGate = j == count ? startGate : gates[j];
                    List<Vector3D> path;
                    double d = GeometrySafety.TryFindShortestBoxPath(aGate, bGate, avoid, out path) ? Vector3D.Distance(a, aGate) + GeometrySafety.PathLength(aGate, path) + Vector3D.Distance(bGate, b) : Vector3D.Distance(a, b);
                    distances[i, j] = distances[j, i] = d;
                }

            var order = new List<int>();
            var used = new bool[count];
            int current = count;
            for (int position = 0; position < count; position++)
            {
                int best = -1;
                double bestDistance = double.MaxValue;
                for (int candidate = 0; candidate < count; candidate++)
                    if (!used[candidate] && distances[current, candidate] < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distances[current, candidate];
                    }

                if (best < 0)
                    for (int candidate = 0; candidate < count; candidate++)
                        if (!used[candidate])
                        {
                            best = candidate;
                            break;
                        }

                used[best] = true;
                order.Add(best);
                current = best;
            }

            // Closed-tour 2-opt: reverse subsequences whenever doing so shortens
            // the real safe-route distance from and back to the departure gate.
            bool improved;
            do
            {
                improved = false;
                for (int i = 0; i < count - 1 && !improved; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        int before = i == 0 ? count : order[i - 1];
                        int first = order[i], last = order[j];
                        int after = j == count - 1 ? count : order[j + 1];
                        double oldCost = distances[before, first] + distances[last, after];
                        double newCost = distances[before, last] + distances[first, after];
                        if (newCost + 0.01 >= oldCost)
                            continue;
                        order.Reverse(i, j - i + 1);
                        improved = true;
                        break;
                    }
            }
            while (improved);
            var sorted = new List<MiningPlane>(count);
            for (int i = 0; i < order.Count; i++)
                sorted.Add(planes[order[i]]);
            planes.Clear();
            planes.AddRange(sorted);
        }

        private static IEnumerable<Vector3D> FibonacciDirections(int count)
        {
            double golden = Math.PI * (3 - Math.Sqrt(5));
            for (int i = 0; i < count; i++)
            {
                double y = 1 - (i / (double)(count - 1)) * 2, radius = Math.Sqrt(Math.Max(0, 1 - y * y)), theta = golden * i;
                yield return new Vector3D(Math.Cos(theta) * radius, y, Math.Sin(theta) * radius);
            }
        }

        private static Vector3D BestUp(Vector3D forward, Vector3D preferred)
        {
            Vector3D up = preferred - forward * Vector3D.Dot(preferred, forward);
            if (up.LengthSquared() < 0.01)
                up = Vector3D.Right - forward * Vector3D.Dot(Vector3D.Right, forward);
            return Vector3D.Normalize(up);
        }

        private static Vector3D SafeNormalize(Vector3D v, Vector3D fallback) => v.LengthSquared() > 1e-8 ? Vector3D.Normalize(v) : fallback;
        private static Vector3I Cell(Vector3D p, double size) => new Vector3I((int)Math.Floor(p.X / size), (int)Math.Floor(p.Y / size), (int)Math.Floor(p.Z / size));
        private static string CellKey(Vector3D p, double size)
        {
            Vector3I c = Cell(p, size);
            return Key(c.X, c.Y, c.Z);
        }

        private static string Key(int x, int y, int z) => x + ":" + y + ":" + z;
        private static double BoxDistance(BoundingBoxD a, BoundingBoxD b)
        {
            double x = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X)), y = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y)), z = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
