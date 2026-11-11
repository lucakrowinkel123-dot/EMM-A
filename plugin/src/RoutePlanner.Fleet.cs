using System;
using System.Collections.Generic;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        private static int BuildFleetPaths(MissionPlan plan, PlanningSnapshot snapshot, OrganicVoxelRouter router, PlanningCancellation cancellation, PlanningProgress progress)
        {
            PlannerSettings settings = snapshot.Settings;
            var allocation = FleetPlaneAllocator.Assign(plan.Planes, snapshot.FleetCount, settings.MineWidth, settings.MineHeight,
                5.0, cancellation.ThrowIfCancellationRequested);
            plan.FleetOwners = allocation.Owners;
            plan.FleetMembers = snapshot.FleetMembers;
            int records = 0;
            for (int miner = 0; miner < snapshot.FleetCount; miner++)
            {
                cancellation.ThrowIfCancellationRequested();
                var sortie = new MissionPlan { MissionId = plan.MissionId, AsteroidEntityId = plan.AsteroidEntityId, Asteroid = plan.Asteroid,
                    CarrierForward = plan.CarrierForward, MinerDeparturePosition = plan.MinerDeparturePosition,
                    MinerDockPosition = plan.MinerDockPosition, MinerDockForward = plan.MinerDockForward, MinerDockUp = plan.MinerDockUp };
                for (int i = 0; i < plan.Planes.Count; i++) if (allocation.Owners[i] == miner) sortie.Planes.Add(plan.Planes[i]);
                if (sortie.Planes.Count == 0) { plan.FleetPaths.Add(""); plan.FleetRoutes.Add(new List<RoutePose>()); continue; }
                var reserved = FleetPlaneAllocator.Reservations(plan.Planes, allocation.Owners, miner, Math.Max(0, snapshot.MinerRadius));
                router.SetReservations(reserved);
                OrganicVoxelRouter relaxed = null;
                BuildMinerRoute(sortie, settings, router, ref relaxed, cancellation, progress);
                Vector3D previous = sortie.MinerDeparturePosition;
                foreach (RoutePose pose in sortie.MinerRoute)
                {
                    if (!FleetPlaneAllocator.Clear(reserved, previous, pose.Position))
                        throw new InvalidOperationException("Miner transit crosses another miner's protected plane volume.");
                    previous = pose.Position;
                }
                string text = PathCodec.Build(sortie.MinerDeparturePosition, sortie.MinerDockForward, sortie.MinerDockUp,
                    sortie.MinerRoute, sortie.Planes, settings.MineHeight, settings.MineWidth,
                    Vector3D.Distance(sortie.MinerDockPosition, sortie.MinerDeparturePosition));
                int count; string validation;
                if (text.Length > 61000 || !PathCodec.Validate(text, out count, out validation))
                    throw new InvalidOperationException("Fleet route invalid or exceeds 61,000 characters.");
                records += count; plan.FleetPaths.Add(text); plan.FleetRoutes.Add(new List<RoutePose>(sortie.MinerRoute)); plan.MinerRoute.AddRange(sortie.MinerRoute);
                if (string.IsNullOrEmpty(plan.MinerPathText)) plan.MinerPathText = text;
            }
            if (records == 0) throw new InvalidOperationException("No mineable lanes remain in the fleet assignment.");
            plan.PathHash = PathCodec.Hash(plan.MinerPathText); plan.ValidationMessage = "Validated " + snapshot.FleetCount + " fleet assignments";
            return records;
        }
    }
}
