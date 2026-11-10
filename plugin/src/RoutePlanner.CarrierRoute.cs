using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        internal MissionPlan BuildCarrierTransit(PlanningSnapshot snapshot, PlanningCancellation cancellation, PlanningProgress progress)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Asteroid == null || !snapshot.Asteroid.Complete)
                throw new InvalidOperationException("cannot plan Carrier transit from an incomplete asteroid map");
            if (progress == null)
                progress = new PlanningProgress();
            cancellation.ThrowIfCancellationRequested();
            var plan = new MissionPlan
            {
                MissionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
                AsteroidEntityId = snapshot.Asteroid.EntityId,
                Asteroid = snapshot.Asteroid,
                MinerPathText = "",
                PathHash = "",
                ValidationMessage = "Carrier transit only"
            };
            progress.Set("Planning Carrier route");
            BuildCarrierPoseAndRoute(plan, snapshot, snapshot.Settings, cancellation);
            progress.Set("Carrier route ready; publishing");
            return plan;
        }

        internal static void BuildCarrierPoseAndRoute(MissionPlan plan, PlanningSnapshot snapshot, PlannerSettings s, PlanningCancellation cancellation)
        {
            const double carrierLaneWidth = 100.0;
            double carrierVoxelClearance = carrierLaneWidth * 0.5;
            BoundingBoxD box = plan.Asteroid.OccupiedBox;
            plan.CarrierPosition = new Vector3D((box.Min.X + box.Max.X) * 0.5, plan.Asteroid.HighestOccupiedY + s.ParkingHeight, (box.Min.Z + box.Max.Z) * 0.5);
            GeometrySafety.BuildAsteroidFacingAxes(
                plan.CarrierPosition,
                box.Center,
                snapshot.CarrierRemoteWorld.Forward,
                snapshot.CarrierRemoteWorld.Up,
                out plan.CarrierForward,
                out plan.CarrierUp);
            var obstacles = new List<BoundingBoxD>();
            foreach (BoundingBoxD voxelBox in snapshot.OtherVoxelBoxes)
            {
                BoundingBoxD b = voxelBox;
                b.Inflate(Math.Max(s.TravelClearance, carrierVoxelClearance));
                obstacles.Add(b);
            }

            Vector3D current = snapshot.CarrierRemoteWorld.Translation;
            plan.CarrierApproachStart = current;
            BoundingBoxD targetBoundary = plan.Asteroid.EntityBox;
            var carrierVoxelRouter = new OrganicVoxelRouter(plan.Asteroid.Samples, box, carrierVoxelClearance, s.PollMeters, cancellation: cancellation);
            // Replanning at the parked pose must not send the Carrier back out to a corner.
            bool alreadyParked = (targetBoundary.Contains(current) != ContainmentType.Disjoint
                || Vector3D.DistanceSquared(current, plan.CarrierPosition) <= 25)
                && carrierVoxelRouter.SegmentClear(current, plan.CarrierPosition)
                && CarrierSegmentClear(current, plan.CarrierPosition, obstacles)
                && DepartureSegmentClear(current, plan.CarrierPosition, snapshot, s, cancellation);
            var outerObstacles = new List<BoundingBoxD>(obstacles) { targetBoundary };

            // A park pose outside the game's asteroid box does not need an
            // artificial boundary waypoint. Fly straight to it whenever that
            // complete 100 m lane is clear. If it is obstructed, retain the
            // corner router below as the safe fallback instead of flying blind.
            bool parkOutsideBoundary = targetBoundary.Contains(plan.CarrierPosition) == ContainmentType.Disjoint;
            bool directOutsideParkClear = parkOutsideBoundary
                && carrierVoxelRouter.SegmentClear(current, plan.CarrierPosition)
                && CarrierSegmentClear(current, plan.CarrierPosition, obstacles)
                && DepartureSegmentClear(current, plan.CarrierPosition, snapshot, s, cancellation);
            if (directOutsideParkClear)
            {
                Emma3Log.Plan(plan.MissionId, "Carrier park direct: park is outside game boundary; clear 100m lane selected");
                plan.CarrierRoute.Add(new RoutePose { Position = plan.CarrierPosition, Forward = plan.CarrierForward, Up = plan.CarrierUp, Kind = "carrier-park" });
                return;
            }

            // Prefer the park pose's own arrival line over an arbitrary box corner.
            // This is only needed when park lies inside the game's box. Trace
            // backward from the park orientation (-CarrierForward) until the ray
            // exits through the box wall. Never use the forward/in-front face.
            Vector3D parkBoundary = Vector3D.Zero;
            bool parkLineClear = !parkOutsideBoundary
                && TryBacktrackParkDirectionToBoundary(targetBoundary, plan.CarrierPosition, plan.CarrierForward, out parkBoundary)
                && carrierVoxelRouter.SegmentClear(parkBoundary, plan.CarrierPosition)
                && CarrierSegmentClear(parkBoundary, plan.CarrierPosition, obstacles)
                && DepartureSegmentClear(parkBoundary, plan.CarrierPosition, snapshot, s, cancellation);
            List<Vector3D> parkLineApproach = null;
            if (!alreadyParked && parkLineClear)
            {
                if (targetBoundary.Contains(current) != ContainmentType.Disjoint)
                {
                    if (carrierVoxelRouter.SegmentClear(current, parkBoundary)
                        && CarrierSegmentClear(current, parkBoundary, obstacles)
                        && DepartureSegmentClear(current, parkBoundary, snapshot, s, cancellation))
                        parkLineApproach = new List<Vector3D> { parkBoundary };
                }
                else
                {
                    Vector3D outward = SafeNormalize(parkBoundary - targetBoundary.Center, Vector3D.Up);
                    Vector3D gate = parkBoundary + outward * 5;
                    if (carrierVoxelRouter.SegmentClear(gate, parkBoundary)
                        && CarrierSegmentClear(gate, parkBoundary, obstacles)
                        && DepartureSegmentClear(gate, parkBoundary, snapshot, s, cancellation))
                    {
                        try
                        {
                            parkLineApproach = BuildDepartureRoute(current, gate, outerObstacles, snapshot, s, cancellation);
                        }
                        catch (InvalidOperationException)
                        {
                            parkLineApproach = null;
                        }
                        if (parkLineApproach == null || !CarrierRouteClear(current, parkLineApproach, outerObstacles))
                            parkLineApproach = null;
                        else
                            parkLineApproach.Add(parkBoundary);
                    }
                }
            }

            Emma3Log.Plan(plan.MissionId, "Carrier park-line boundary=" + parkBoundary
                + " clear=" + parkLineClear + " reachable=" + (parkLineApproach != null)
                + " selected=" + (!alreadyParked && parkLineApproach != null)
                + " laneWidth=100m alreadyParked=" + alreadyParked
                + " parkOutsideBoundary=" + parkOutsideBoundary);
            if (alreadyParked || parkLineApproach != null)
            {
                if (!alreadyParked)
                    AppendCarrierTransit(plan.CarrierRoute, parkLineApproach);
                plan.CarrierRoute.Add(new RoutePose { Position = plan.CarrierPosition, Forward = plan.CarrierForward, Up = plan.CarrierUp, Kind = "carrier-park" });
                return;
            }

            // The preferred face lane was obstructed or unreachable. Preserve
            // the existing eight-corner search as the safe fallback.
            List<Vector3D> bestApproach = null;
            double bestLength = double.MaxValue;
            int clearCorners = 0, reachableCorners = 0, selectedCorner = -1;
            for (int i = 0; i < 8; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3D corner = targetBoundary.GetCorner(i);
                if (!carrierVoxelRouter.SegmentClear(corner, plan.CarrierPosition)
                    || !CarrierSegmentClear(corner, plan.CarrierPosition, obstacles)
                    || !DepartureSegmentClear(corner, plan.CarrierPosition, snapshot, s, cancellation))
                    continue;
                clearCorners++;
                if (alreadyParked)
                    continue;

                List<Vector3D> approach;
                if (targetBoundary.Contains(current) != ContainmentType.Disjoint)
                {
                    // An inside start cannot use the box as an obstacle. Its entire
                    // leg to the selected corner must pass the same voxel check.
                    if (!carrierVoxelRouter.SegmentClear(current, corner)
                        || !CarrierSegmentClear(current, corner, obstacles)
                        || !DepartureSegmentClear(current, corner, snapshot, s, cancellation))
                        continue;
                    approach = new List<Vector3D> { corner };
                }
                else
                {
                    // Box routing requires an outside endpoint. Keep this small
                    // gate outside the real corner, then visit the exact corner.
                    Vector3D gate = corner + SafeNormalize(corner - targetBoundary.Center, Vector3D.Up) * 5;
                    if (!carrierVoxelRouter.SegmentClear(gate, corner)
                        || !CarrierSegmentClear(gate, corner, obstacles)
                        || !DepartureSegmentClear(gate, corner, snapshot, s, cancellation))
                        continue;
                    try
                    {
                        approach = BuildDepartureRoute(current, gate, outerObstacles, snapshot, s, cancellation);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    if (approach == null || !CarrierRouteClear(current, approach, outerObstacles))
                        continue;
                    approach.Add(corner);
                }

                reachableCorners++;
                double length = OrganicVoxelRouter.PathLength(current, approach) + Vector3D.Distance(corner, plan.CarrierPosition);
                if (length >= bestLength)
                    continue;
                bestLength = length;
                bestApproach = approach;
                selectedCorner = i;
            }

            Emma3Log.Plan(plan.MissionId, "Carrier corner lanes checked=8 clear=" + clearCorners
                + " reachable=" + reachableCorners + " selected=" + (selectedCorner + 1)
                + " laneWidth=100m alreadyParked=" + alreadyParked
                + (selectedCorner < 0 ? "" : " corner=" + targetBoundary.GetCorner(selectedCorner) + " routeLength=" + bestLength.ToString("0.0") + "m"));
            if (!alreadyParked)
            {
                if (bestApproach == null)
                    throw new InvalidOperationException(clearCorners == 0
                        ? "No straight 100 m lane from any corner to park"
                        : "Carrier cannot reach a clear corner-to-park lane");
                AppendCarrierTransit(plan.CarrierRoute, bestApproach);
            }
            plan.CarrierRoute.Add(new RoutePose { Position = plan.CarrierPosition, Forward = plan.CarrierForward, Up = plan.CarrierUp, Kind = "carrier-park" });
        }

        private static bool TryBacktrackParkDirectionToBoundary(BoundingBoxD box, Vector3D park, Vector3D parkDirection, out Vector3D boundary)
        {
            boundary = Vector3D.Zero;
            if (parkDirection.LengthSquared() < 1e-12)
                return false;
            Vector3D backward = -Vector3D.Normalize(parkDirection);
            double entry = 0, exit = double.MaxValue;
            if (!ClipParkRayAxis(park.X, backward.X, box.Min.X, box.Max.X, ref entry, ref exit)
                || !ClipParkRayAxis(park.Y, backward.Y, box.Min.Y, box.Max.Y, ref entry, ref exit)
                || !ClipParkRayAxis(park.Z, backward.Z, box.Min.Z, box.Max.Z, ref entry, ref exit)
                || exit < 0)
            {
                return false;
            }
            double distance = box.Contains(park) == ContainmentType.Disjoint ? entry : exit;
            if (distance < 0 || distance == double.MaxValue)
                return false;
            boundary = park + backward * distance;
            return true;
        }

        private static bool ClipParkRayAxis(double origin, double direction, double min, double max, ref double entry, ref double exit)
        {
            if (Math.Abs(direction) < 1e-12)
                return origin >= min && origin <= max;
            double a = (min - origin) / direction;
            double b = (max - origin) / direction;
            if (a > b)
            {
                double swap = a;
                a = b;
                b = swap;
            }
            entry = Math.Max(entry, a);
            exit = Math.Min(exit, b);
            return entry <= exit;
        }

        // Every leg is checked against fresh departure voxels, including detours
        // that could otherwise re-enter a departure box after leaving it.
        private static bool DepartureSegmentClear(Vector3D a, Vector3D b, PlanningSnapshot snapshot, PlannerSettings settings, PlanningCancellation cancellation)
        {
            if (snapshot.DepartureRouters.Count != snapshot.DepartureMaps.Count)
            {
                snapshot.DepartureRouters.Clear();
                foreach (AsteroidMap map in snapshot.DepartureMaps)
                    snapshot.DepartureRouters.Add(new OrganicVoxelRouter(map.Samples, map.OccupiedBox, 50, settings.PollMeters, cancellation: cancellation));
            }
            foreach (OrganicVoxelRouter router in snapshot.DepartureRouters)
                if (!router.SegmentClear(a, b)) return false;
            return true;
        }

        private static bool DepartureRouteClear(Vector3D start, IList<Vector3D> route, PlanningSnapshot snapshot, PlannerSettings settings, PlanningCancellation cancellation)
        {
            foreach (Vector3D next in route)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!DepartureSegmentClear(start, next, snapshot, settings, cancellation)) return false;
                start = next;
            }
            return true;
        }

        internal static List<Vector3D> BuildDepartureRoute(Vector3D start, Vector3D end, List<BoundingBoxD> obstacles, PlanningSnapshot snapshot, PlannerSettings settings, PlanningCancellation cancellation)
        {
            List<Vector3D> best = null;
            double bestLength = double.MaxValue;
            try
            {
                var direct = RouteAroundBoxes(start, end, obstacles, 10, true, true);
                if (CarrierRouteClear(start, direct, obstacles) && DepartureRouteClear(start, direct, snapshot, settings, cancellation))
                    return direct;
            }
            catch (InvalidOperationException) { }
            // An exit gate clears the inflated departure box on all three axes.
            // All other mapped departures remain voxel obstacles; overlapping
            // asteroids therefore fail closed when no complete safe exit exists.
            foreach (AsteroidMap map in snapshot.DepartureMaps)
            {
                BoundingBoxD inflated = map.EntityBox;
                inflated.Inflate(Math.Max(50, settings.TravelClearance) + 10);
                var outsideObstacles = new List<BoundingBoxD>(obstacles);
                foreach (AsteroidMap departure in snapshot.DepartureMaps)
                {
                    BoundingBoxD guard = departure.EntityBox;
                    guard.Inflate(Math.Max(50, settings.TravelClearance));
                    outsideObstacles.Add(guard);
                }
                for (int i = 0; i < 8; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Vector3D corner = map.EntityBox.GetCorner(i), gate = inflated.GetCorner(i);
                    var prefix = new List<Vector3D> { corner, gate };
                    if (!CarrierRouteClear(start, prefix, obstacles) || !DepartureRouteClear(start, prefix, snapshot, settings, cancellation)) continue;
                    try
                    {
                        var tail = RouteAroundBoxes(gate, end, outsideObstacles, 10, true, true);
                        if (!CarrierRouteClear(gate, tail, outsideObstacles) || !DepartureRouteClear(gate, tail, snapshot, settings, cancellation)) continue;
                        prefix.AddRange(tail);
                        double length = OrganicVoxelRouter.PathLength(start, prefix);
                        if (length < bestLength) { best = prefix; bestLength = length; }
                    }
                    catch (InvalidOperationException) { }
                }
            }
            return best;
        }

        private static void AppendCarrierTransit(List<RoutePose> route, IList<Vector3D> points)
        {
            if (points == null)
                return;
            for (int i = 0; i < points.Count; i++)
                AddCarrierTransit(route, points[i]);
        }

        private static void AddCarrierTransit(List<RoutePose> route, Vector3D point)
        {
            if (route.Count > 0 && Vector3D.DistanceSquared(route[route.Count - 1].Position, point) < 0.000001)
                return;
            route.Add(new RoutePose { Position = point, Forward = Vector3D.Zero, Up = Vector3D.Zero, Kind = "carrier" });
        }

        private static bool CarrierSegmentClear(Vector3D start, Vector3D end, IList<BoundingBoxD> obstacles)
        {
            for (int i = 0; i < obstacles.Count; i++)
                if (GeometrySafety.SegmentIntersectsBox(start, end, obstacles[i]))
                    return false;
            return true;
        }

        private static bool CarrierRouteClear(Vector3D start, IList<Vector3D> route, IList<BoundingBoxD> obstacles)
        {
            Vector3D current = start;
            for (int i = 0; i < route.Count; i++)
            {
                if (!CarrierSegmentClear(current, route[i], obstacles))
                    return false;
                current = route[i];
            }
            return true;
        }

        private static void BuildPlannedMinerDockPose(MissionPlan plan, PlanningSnapshot snapshot, PlannerSettings s)
        {
            MatrixD carrierPlanned = MatrixD.CreateWorld(plan.CarrierPosition, plan.CarrierForward, plan.CarrierUp);
            GeometrySafety.TransformDockedPose(
                snapshot.CarrierRemoteWorld,
                snapshot.MinerRemoteWorld,
                carrierPlanned,
                out plan.MinerDockPosition,
                out plan.MinerDockForward,
                out plan.MinerDockUp);
            // Match the Miner's two-stage dock controller: first move 5 m out
            // on the Carrier connector's Forward axis, then 200 m up on its local
            // +Y/Up axis. Route-start extra extends only the outer Up leg.
            Vector3D connectorForward = !snapshot.HasCarrierConnector ? -plan.CarrierForward : GeometrySafety.TransformDockedDirection(snapshot.CarrierRemoteWorld, snapshot.CarrierConnectorForward, carrierPlanned);
            Vector3D connectorUp = !snapshot.HasCarrierConnector ? plan.CarrierUp : GeometrySafety.TransformDockedDirection(snapshot.CarrierRemoteWorld, snapshot.CarrierConnectorUp, carrierPlanned);
            Vector3D innerPose = plan.MinerDockPosition + SafeNormalize(connectorForward, -plan.CarrierForward) * 5.0;
            plan.MinerDeparturePosition = innerPose + SafeNormalize(connectorUp, plan.CarrierUp) * (s.DockOuterPoseDistance + s.MinerRouteStartExtra);
        }
    }
}
