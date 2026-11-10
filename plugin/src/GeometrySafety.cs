using System;
using System.Collections.Generic;
using VRageMath;

namespace EMMA3.Client
{
    internal static class GeometrySafety
    {
        internal static void BuildAsteroidFacingAxes(
            Vector3D carrierPosition,
            Vector3D asteroidCenter,
            Vector3D currentForward,
            Vector3D currentUp,
            out Vector3D forward,
            out Vector3D up)
        {
            Vector3D towardAsteroid = asteroidCenter - carrierPosition;
            forward = towardAsteroid.LengthSquared() > 1e-8 ? Vector3D.Normalize(towardAsteroid) : Vector3D.Down;
            // Preserve the Carrier's existing roll where possible. At the usual
            // top parking point its old Up can be parallel to the new Forward,
            // so use its prior Forward as the second roll reference.
            Vector3D upCandidate = currentUp - forward * Vector3D.Dot(currentUp, forward);
            if (upCandidate.LengthSquared() <= 1e-8)
                upCandidate = currentForward - forward * Vector3D.Dot(currentForward, forward);
            if (upCandidate.LengthSquared() <= 1e-8)
            {
                Vector3D worldReference = Math.Abs(Vector3D.Dot(Vector3D.Up, forward)) < 0.9 ? Vector3D.Up : Vector3D.Forward;
                upCandidate = worldReference - forward * Vector3D.Dot(worldReference, forward);
            }

            Vector3D cross = Vector3D.Cross(forward, upCandidate);
            Vector3D right = cross.LengthSquared() > 1e-8 ? Vector3D.Normalize(cross) : Vector3D.Right;
            Vector3D correctedUp = Vector3D.Cross(right, forward);
            up = correctedUp.LengthSquared() > 1e-8 ? Vector3D.Normalize(correctedUp) : Vector3D.Up;
        }

        internal static void TransformDockedPose(
            MatrixD carrierCurrent,
            MatrixD minerCurrent,
            MatrixD carrierPlanned,
            out Vector3D position,
            out Vector3D forward,
            out Vector3D up)
        {
            MatrixD inverse = MatrixD.Invert(carrierCurrent);
            Vector3D localPosition = Vector3D.Transform(minerCurrent.Translation, inverse);
            Vector3D localForward = Vector3D.TransformNormal(minerCurrent.Forward, inverse);
            Vector3D localUp = Vector3D.TransformNormal(minerCurrent.Up, inverse);
            position = Vector3D.Transform(localPosition, carrierPlanned);
            forward = Vector3D.Normalize(Vector3D.TransformNormal(localForward, carrierPlanned));
            up = Vector3D.Normalize(Vector3D.TransformNormal(localUp, carrierPlanned));
        }

        internal static Vector3D TransformDockedDirection(MatrixD carrierCurrent, Vector3D currentDirection, MatrixD carrierPlanned)
        {
            Vector3D local = Vector3D.TransformNormal(currentDirection, MatrixD.Invert(carrierCurrent));
            return Vector3D.Normalize(Vector3D.TransformNormal(local, carrierPlanned));
        }

        internal static MatrixD ProjectRemoteAtCarrierConnector(
            MatrixD remoteCurrent,
            MatrixD minerConnectorCurrent,
            MatrixD carrierConnectorWorld)
        {
            MatrixD inverseMinerConnector = MatrixD.Invert(minerConnectorCurrent);
            Vector3D localPosition = Vector3D.Transform(remoteCurrent.Translation, inverseMinerConnector);
            Vector3D localForward = Vector3D.TransformNormal(remoteCurrent.Forward, inverseMinerConnector);
            Vector3D localUp = Vector3D.TransformNormal(remoteCurrent.Up, inverseMinerConnector);
            MatrixD targetMinerConnector = MatrixD.CreateWorld(
                carrierConnectorWorld.Translation,
                -carrierConnectorWorld.Forward,
                carrierConnectorWorld.Up);
            return MatrixD.CreateWorld(
                Vector3D.Transform(localPosition, targetMinerConnector),
                Vector3D.Normalize(Vector3D.TransformNormal(localForward, targetMinerConnector)),
                Vector3D.Normalize(Vector3D.TransformNormal(localUp, targetMinerConnector)));
        }

        // Surface samples are cell centers. Treat every sample as a cube extending
        // half a poll interval in each direction and reject any cube intersecting
        // the rectangular workspace from the mining plane out to staging.
        internal static bool WorkspaceClear(
            IList<MaterialSample> surfaceSamples,
            Vector3D plane,
            Vector3D outward,
            Vector3D right,
            Vector3D up,
            double width,
            double height,
            double workspaceDepth,
            double pollMeters)
        {
            double halfCell = Math.Max(0.5, pollMeters * 0.5);
            double halfWidth = width * 0.5 + halfCell;
            double halfHeight = height * 0.5 + halfCell;
            double minAxis = -halfCell;
            double maxAxis = workspaceDepth + halfCell;
            for (int i = 0; i < surfaceSamples.Count; i++)
            {
                Vector3D delta = surfaceSamples[i].Position - plane;
                double axis = Vector3D.Dot(delta, outward);
                if (axis < minAxis || axis > maxAxis)
                    continue;
                if (Math.Abs(Vector3D.Dot(delta, right)) > halfWidth)
                    continue;
                if (Math.Abs(Vector3D.Dot(delta, up)) > halfHeight)
                    continue;
                return false;
            }

            return true;
        }

        internal static bool TryChooseTwoCornerDetour(Vector3D start, Vector3D end, BoundingBoxD box, out Vector3D first, out Vector3D second)
        {
            first = Vector3D.Zero;
            second = Vector3D.Zero;
            BoundingBoxD expanded = box;
            expanded.Inflate(5);
            Vector3D[] corners = expanded.GetCorners();
            double bestCost = double.MaxValue;
            for (int i = 0; i < corners.Length; i++)
            {
                if (SegmentIntersectsBox(start, corners[i], box))
                    continue;
                for (int j = 0; j < corners.Length; j++)
                {
                    if (i == j || !CornersShareEdge(corners[i], corners[j]))
                        continue;
                    if (SegmentIntersectsBox(corners[j], end, box))
                        continue;
                    double cost = Vector3D.Distance(start, corners[i]) + Vector3D.Distance(corners[i], corners[j]) + Vector3D.Distance(corners[j], end);
                    if (cost >= bestCost)
                        continue;
                    bestCost = cost;
                    first = corners[i];
                    second = corners[j];
                }
            }

            return bestCost < double.MaxValue;
        }

        // Start with the proven two-corner route, then chamfer both hard bends
        // using edge midpoints. The shorter chamfer is accepted only when every
        // resulting segment remains outside the avoidance box; otherwise the
        // original full-corner detour is retained.
        internal static bool TryChooseChamferedDetour(Vector3D start, Vector3D end, BoundingBoxD box, out List<Vector3D> points)
        {
            points = new List<Vector3D>();
            Vector3D first, second;
            if (!TryChooseTwoCornerDetour(start, end, box, out first, out second))
                return false;
            var chamfered = new List<Vector3D>
            {
                Vector3D.Lerp(start, first, 0.5),
                Vector3D.Lerp(first, second, 0.5),
                Vector3D.Lerp(second, end, 0.5)
            };
            Vector3D previous = start;
            for (int i = 0; i <= chamfered.Count; i++)
            {
                Vector3D next = i < chamfered.Count ? chamfered[i] : end;
                if (SegmentIntersectsBox(previous, next, box))
                {
                    points.Add(first);
                    points.Add(second);
                    return true;
                }

                previous = next;
            }

            points.AddRange(chamfered);
            return true;
        }

        // Find the shortest visible route around a conservative AABB. Candidate
        // nodes cover corners, edge midpoints, face centres, and the start/end
        // projections onto every face of a box expanded by the route margin.
        // Dijkstra then chooses the minimum real-distance chain whose every
        // segment remains outside the original avoidance box.
        internal static bool TryFindShortestBoxPath(Vector3D start, Vector3D end, BoundingBoxD box, out List<Vector3D> path)
        {
            path = new List<Vector3D>();
            if (!SegmentIntersectsBox(start, end, box))
            {
                path.Add(end);
                return true;
            }

            if (box.Contains(start) != ContainmentType.Disjoint || box.Contains(end) != ContainmentType.Disjoint)
                return false;
            BoundingBoxD expanded = box;
            expanded.Inflate(5);
            var nodes = new List<Vector3D>();
            AddUnique(nodes, start);
            AddUnique(nodes, end);
            double[] xs =
            {
                expanded.Min.X,
                expanded.Center.X,
                expanded.Max.X
            };
            double[] ys =
            {
                expanded.Min.Y,
                expanded.Center.Y,
                expanded.Max.Y
            };
            double[] zs =
            {
                expanded.Min.Z,
                expanded.Center.Z,
                expanded.Max.Z
            };
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    for (int z = 0; z < 3; z++)
                    {
                        if (x == 1 && y == 1 && z == 1)
                            continue;
                        AddUnique(nodes, new Vector3D(xs[x], ys[y], zs[z]));
                    }

            List<Vector3D> priorChamfer;
            if (TryChooseChamferedDetour(start, end, box, out priorChamfer))
                for (int i = 0; i < priorChamfer.Count; i++)
                    AddUnique(nodes, priorChamfer[i]);
            AddFaceProjections(nodes, start, expanded);
            AddFaceProjections(nodes, end, expanded);
            int count = nodes.Count;
            var distance = new double[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = double.MaxValue;
                previous[i] = -1;
            }

            distance[0] = 0;
            for (int step = 0; step < count; step++)
            {
                int current = -1;
                double best = double.MaxValue;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && distance[i] < best)
                    {
                        best = distance[i];
                        current = i;
                    }

                if (current < 0 || current == 1)
                    break;
                visited[current] = true;
                for (int next = 0; next < count; next++)
                {
                    if (next == current || visited[next] || SegmentIntersectsBox(nodes[current], nodes[next], box))
                        continue;
                    double candidate = distance[current] + Vector3D.Distance(nodes[current], nodes[next]);
                    if (candidate + 1e-6 >= distance[next])
                        continue;
                    distance[next] = candidate;
                    previous[next] = current;
                }
            }

            if (distance[1] == double.MaxValue)
                return false;
            var reversed = new List<Vector3D>();
            for (int at = 1; at > 0;)
            {
                reversed.Add(nodes[at]);
                at = previous[at];
                if (at < 0)
                    return false;
            }

            reversed.Reverse();
            path.AddRange(reversed);
            return path.Count > 0;
        }

        internal static double PathLength(Vector3D start, IList<Vector3D> path)
        {
            double result = 0;
            Vector3D previous = start;
            for (int i = 0; i < path.Count; i++)
            {
                result += Vector3D.Distance(previous, path[i]);
                previous = path[i];
            }

            return result;
        }

        private static void AddFaceProjections(List<Vector3D> nodes, Vector3D source, BoundingBoxD box)
        {
            double x = MathHelper.Clamp(source.X, box.Min.X, box.Max.X);
            double y = MathHelper.Clamp(source.Y, box.Min.Y, box.Max.Y);
            double z = MathHelper.Clamp(source.Z, box.Min.Z, box.Max.Z);
            AddUnique(nodes, new Vector3D(box.Min.X, y, z));
            AddUnique(nodes, new Vector3D(box.Max.X, y, z));
            AddUnique(nodes, new Vector3D(x, box.Min.Y, z));
            AddUnique(nodes, new Vector3D(x, box.Max.Y, z));
            AddUnique(nodes, new Vector3D(x, y, box.Min.Z));
            AddUnique(nodes, new Vector3D(x, y, box.Max.Z));
        }

        private static void AddUnique(List<Vector3D> nodes, Vector3D point)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (Vector3D.DistanceSquared(nodes[i], point) < 0.000001)
                    return;
            nodes.Add(point);
        }

        internal static Vector3D MoveOutsideBoxAlong(Vector3D point, Vector3D direction, BoundingBoxD box, double margin)
        {
            if (box.Contains(point) == ContainmentType.Disjoint)
                return point;
            if (direction.LengthSquared() < 1e-12)
                direction = point - box.Center;
            if (direction.LengthSquared() < 1e-12)
                direction = Vector3D.Up;
            direction = Vector3D.Normalize(direction);
            double exit = double.MaxValue;
            AddRayExit(point.X, direction.X, box.Min.X, box.Max.X, ref exit);
            AddRayExit(point.Y, direction.Y, box.Min.Y, box.Max.Y, ref exit);
            AddRayExit(point.Z, direction.Z, box.Min.Z, box.Max.Z, ref exit);
            if (exit == double.MaxValue)
                return point;
            return point + direction * (Math.Max(0, exit) + Math.Max(0.1, margin));
        }

        private static void AddRayExit(double origin, double direction, double min, double max, ref double exit)
        {
            if (direction > 1e-12)
                exit = Math.Min(exit, (max - origin) / direction);
            else if (direction < -1e-12)
                exit = Math.Min(exit, (min - origin) / direction);
        }

        private static bool CornersShareEdge(Vector3D a, Vector3D b)
        {
            int changed = 0;
            if (Math.Abs(a.X - b.X) > 0.001)
                changed++;
            if (Math.Abs(a.Y - b.Y) > 0.001)
                changed++;
            if (Math.Abs(a.Z - b.Z) > 0.001)
                changed++;
            return changed == 1;
        }

        internal static bool SegmentIntersectsBox(Vector3D a, Vector3D b, BoundingBoxD box)
        {
            Vector3D d = b - a;
            double tMin = 0;
            double tMax = 1;
            return ClipAxis(a.X, d.X, box.Min.X, box.Max.X, ref tMin, ref tMax) && ClipAxis(a.Y, d.Y, box.Min.Y, box.Max.Y, ref tMin, ref tMax) && ClipAxis(a.Z, d.Z, box.Min.Z, box.Max.Z, ref tMin, ref tMax) && tMax >= tMin;
        }

        private static bool ClipAxis(double origin, double direction, double min, double max, ref double tMin, ref double tMax)
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

            tMin = Math.Max(tMin, a);
            tMax = Math.Min(tMax, b);
            return tMin <= tMax;
        }
    }
}
