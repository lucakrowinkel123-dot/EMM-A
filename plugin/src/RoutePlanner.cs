using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class RoutePlanner
    {
        public MissionPlan Build(PlanningSnapshot snapshot, PlanningCancellation cancellation, PlanningProgress progress)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (progress == null)
                progress = new PlanningProgress();
            if (snapshot.CarrierOnly)
                return BuildCarrierTransit(snapshot, cancellation, progress);
            AsteroidMap map = snapshot.Asteroid;
            PlannerSettings settings = snapshot.Settings;
            cancellation.ThrowIfCancellationRequested();
            var plan = new MissionPlan
            {
                MissionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
                AsteroidEntityId = map.EntityId,
                Asteroid = map
            };
            var totalTimer = Stopwatch.StartNew();
            var stageTimer = Stopwatch.StartNew();
            progress.Set("Grouping ore veins");
            List<OreGroup> groups = BuildGroups(map, snapshot.OreSelection, settings, cancellation);
            LogTiming(plan.MissionId, "Grouping ore veins", stageTimer, "groups=" + groups.Count);
            if (groups.Count == 0)
                throw new InvalidOperationException("asteroid contains no mineable ore groups");
            stageTimer.Restart();
            int groupIndex = 0;
            foreach (OreGroup group in groups)
            {
                groupIndex++;
                progress.Set("Creating mining planes " + groupIndex + "/" + groups.Count);
                cancellation.ThrowIfCancellationRequested();
                MiningPlane plane = FindPlane(map, group, settings, cancellation);
                if (plane != null)
                    plan.Planes.Add(plane);
                else
                    Emma3Log.Plan(plan.MissionId, "Skipped ore group because no clear plane/workspace was found: " + string.Join(",", group.Ores));
            }

            LogTiming(plan.MissionId, "Creating mining planes", stageTimer, "accepted=" + plan.Planes.Count + " groups=" + groups.Count);
            if (plan.Planes.Count == 0)
                throw new InvalidOperationException("no ore group has a safe mining plane");
            progress.Set("Planning Carrier and dock poses");
            stageTimer.Restart();
            BuildCarrierPoseAndRoute(plan, snapshot, settings, cancellation);
            BuildPlannedMinerDockPose(plan, snapshot, settings);
            LogTiming(plan.MissionId, "Planning Carrier and dock poses", stageTimer, "carrierPoints=" + plan.CarrierRoute.Count);
            progress.Set("Indexing asteroid surface");
            stageTimer.Restart();
            var organicRouter = new OrganicVoxelRouter(
                map.Samples,
                map.OccupiedBox,
                settings.TravelClearance,
                settings.PollMeters,
                cancellation: cancellation,
                maxExpandedNodes: 600000,
                endpointSearchRadiusCells: 4);
            OrganicVoxelRouter relaxedOrganicRouter = null;
            LogTiming(plan.MissionId, "Indexing asteroid surface", stageTimer, "samples=" + map.Samples.Count);
            progress.Set("Ordering mining planes");
            stageTimer.Restart();
            OrderPlanes(plan.Planes, plan.MinerDeparturePosition, plan.Asteroid.OccupiedBox, settings, plan.CarrierForward, progress);
            LogTiming(plan.MissionId, "Ordering mining planes", stageTimer, "planes=" + plan.Planes.Count + " organicSearches=0");
            progress.Set("Marking destruction-safe lane sections");
            stageTimer.Restart();
            BuildLaneDestructionRanges(plan, snapshot, settings, cancellation);
            LogTiming(plan.MissionId, "Marking destruction-safe lane sections", stageTimer, "drills=" + snapshot.MinerDrills.Count);
            stageTimer.Restart();
            int count = BuildFleetPaths(plan, snapshot, organicRouter, cancellation, progress);
            LogTiming(plan.MissionId, "Encoding and validating fleet routes", stageTimer, "records=" + count);
            progress.Set("Route ready; publishing");
            int relaxedSearches = relaxedOrganicRouter == null ? 0 : relaxedOrganicRouter.SearchCount;
            int relaxedFallbacks = relaxedOrganicRouter == null ? 0 : relaxedOrganicRouter.FallbackCount;
            Emma3Log.Plan(
                plan.MissionId,
                "Plan complete elapsedMs=" + totalTimer.Elapsed.TotalMilliseconds.ToString("0")
                + " asteroid=" + map.EntityId
                + " planes=" + plan.Planes.Count
                + " pathRecords=" + count
                + " hash=" + plan.PathHash
                + " carrierPark=" + plan.CarrierPosition
                + " minerDock=" + plan.MinerDockPosition
                + " minerClearPoint=" + plan.MinerDeparturePosition
                + " dockClearance=" + Vector3D.Distance(plan.MinerDockPosition, plan.MinerDeparturePosition).ToString("0.0")
                + "m organicSearches=" + organicRouter.SearchCount
                + " organicFallbacks=" + organicRouter.FallbackCount
                + " relaxedOrganicSearches=" + relaxedSearches
                + " boxFallbacks=" + relaxedFallbacks);
            return plan;
        }

        private static void LogTiming(string missionId, string stage, Stopwatch timer, string details)
        {
            Emma3Log.Plan(
                missionId,
                "Timing " + stage + "=" + timer.Elapsed.TotalMilliseconds.ToString("0") + "ms" + (string.IsNullOrEmpty(details) ? "" : " " + details));
        }
    }
}
