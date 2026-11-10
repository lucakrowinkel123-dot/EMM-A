using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private void PublishPlan(BridgeDocument bridge, MissionPlan plan, int records)
        {
            bridge.Set("state", settings.AllowMovement ? "ready" : "ready_dryrun");
            bridge.Set("message", settings.AllowMovement ? "plan ready and validated" : "plan ready; Allow movement is Off");
            bridge.Set("missionId", plan.MissionId);
            bridge.Set("planType", planningCarrierOnly ? "carrier" : "full");
            bridge.Remove("carrierRouteCompletedMission");
            completedCarrierRouteMission = "";
            bridge.Set("asteroidEntityId", plan.AsteroidEntityId);
            bridge.Set("asteroidBoxMin", BridgeDocument.FormatCarrierRoutePosition(plan.Asteroid.EntityBox.Min));
            bridge.Set("asteroidBoxMax", BridgeDocument.FormatCarrierRoutePosition(plan.Asteroid.EntityBox.Max));
            bridge.Set("pathHash", plan.PathHash);
            bridge.Set("pathRecords", records);
            bridge.Set("planeCount", plan.Planes.Count);
            var minedOres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MiningPlane plane in plan.Planes)
                minedOres.UnionWith(plane.Ores);
            bridge.Set("missionMinedOres", string.Join(",", minedOres));
            bridge.Set("missionPurpose", planningPurpose);
            bridge.Set("carrierRouteCount", plan.CarrierRoute.Count);
            for (int i = 0; i < plan.CarrierRoute.Count; i++)
            {
                RoutePose pose = plan.CarrierRoute[i];
                bridge.Set(
                    "carrierRoute" + i,
                    BridgeDocument.FormatCarrierRoutePosition(pose.Position) + "|" + BridgeDocument.FormatCarrierRouteAxis(pose.Forward) + "|" + BridgeDocument.FormatCarrierRouteAxis(pose.Up));
            }
        }

        private void TryRestoreVisualization(BridgeDocument bridge)
        {
            if (!settings.Visualization || activePlan != null || planningActive || restoredVisualizationPlan != null)
                return;
            string missionId = bridge.Get("missionId");
            string pathHash = bridge.Get("pathHash");
            string key = missionId + "|" + pathHash;
            if (missionId.Length == 0 || pathHash.Length == 0 || key == lastVisualizationRestoreKey)
                return;
            lastVisualizationRestoreKey = key;
            MissionPlan restored;
            string reason;
            if (visualizationSnapshots.TryLoad(missionId, pathHash, settings.TravelClearance, out restored, out reason))
            {
                // Older snapshots lack the origin; capture the remaining approach once on restore.
                if (!restored.CarrierApproachStart.HasValue && blocks.CarrierRemote != null)
                    restored.CarrierApproachStart = blocks.CarrierRemote.GetPosition();
                restoredVisualizationPlan = restored;
                Emma3Log.Info("Restored read-only mission visualization after reload: planes=" + restored.Planes.Count
                    + " carrierRoute=" + restored.CarrierRoute.Count
                    + " minerRoute=" + restored.MinerRoute.Count
                    + ".");
            }
            else
                Emma3Log.Warn("Mission visualization could not be restored: " + reason + ".");
        }

        private void SaveVisualizationSnapshot(MissionPlan plan)
        {
            try
            {
                visualizationSnapshots.Save(plan);
                restoredVisualizationPlan = null;
                lastVisualizationRestoreKey = plan.MissionId + "|" + plan.PathHash;
            }
            catch (Exception ex)
            {
                Emma3Log.Warn("Mission plan was published, but its read-only visualization snapshot could not be saved: " + ex.Message);
            }
        }

        private void BuildCandidateList()
        {
            candidates.Clear();
            candidateIndex = 0;
            Vector3D current = blocks.CarrierRemote.GetPosition();
            BoundingSphereD sphere = new BoundingSphereD(current, settings.SearchRadius);
            var entities = MyEntities.GetEntitiesInSphere(ref sphere);
            if (entities != null)
                foreach (MyEntity entity in entities)
                {
                    var voxel = entity as MyVoxelBase;
                    if (voxel == null || voxel is MyPlanet || voxel.Closed || voxel.MarkedForClose)
                        continue;
                    candidates.Add(voxel);
                }

            candidates.Sort((a, b) => DistanceToBox(current, a.PositionComp.WorldAABB).CompareTo(DistanceToBox(current, b.PositionComp.WorldAABB)));
        }

        private static bool Qualifies(AsteroidMap map, OreSelection selection)
        {
            foreach (string ore in map.OreSamples.Keys)
                if (selection.Matches(ore))
                    return true;
            return false;
        }
    }
}
