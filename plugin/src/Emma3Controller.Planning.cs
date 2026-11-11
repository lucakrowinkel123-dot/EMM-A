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
        private void BeginPlanning(BridgeDocument bridge)
        {
            if (acceptedPlanRequest == null || !acceptedPlanRequest.Matches(bridge)) acceptedPlanRequest = new PlanRequestIdentity(bridge);
            planningCarrierOnly = bridge.Get("planType").Equals("carrier", StringComparison.OrdinalIgnoreCase);
            planningProjectMinerDockPose = bridge.GetBool("projectMinerDockPose");
            planningDimensions = null;
            if (!long.TryParse(bridge.Get("requestedAsteroidEntityId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out requestedAsteroidEntityId))
                requestedAsteroidEntityId = 0;
            restartPlanningAfterCockpitLoss = false;
            CancelDeparturePreparation();
            CancelPlanningJob("new autonomous mining request");
            PromoteMatchingBackgroundScan();

            cockpitSuspended = false;
            planningActive = false;
            activePlan = null;
            restoredVisualizationPlan = null;
            lastVisualizationRestoreKey = "";
            if (!blocks.IsCarrierPilot)
            {
                Fail(bridge, "Stay in the Carrier cockpit while planning", true);
                return;
            }

            bool fleetPlanning = !planningCarrierOnly && bridge.GetInt("fleetCount") > 0;
            if (fleetPlanning && !MinerDimensions.TryParse(bridge.Get("minerDimensions"), out planningDimensions))
            {
                bridge.Set("state", "waiting_replication");
                bridge.Set("message", "Register a miner to record its dimensions before fleet planning");
                return;
            }
            string dockedMinerProblem = planningCarrierOnly || fleetPlanning ? "" : blocks.DockedMinerProblem();
            if (dockedMinerProblem.Length != 0)
            {
                if (bridge.GetInt("fleetCount") > 0) { bridge.Set("state", "waiting_replication"); bridge.Set("message", "Waiting for registered Miner hardware"); return; }
                Fail(bridge, dockedMinerProblem, true);
                return;
            }
            if (planningProjectMinerDockPose)
            {
                string projectedPoseProblem = fleetPlanning ? (blocks.CarrierConnector == null ? "missing Carrier connector for Miner pose" : "") : blocks.ProjectedMinerPoseProblem();
                if (projectedPoseProblem.Length != 0)
                {
                    Fail(bridge, projectedPoseProblem, true);
                    return;
                }
            }

            if (!ValidateFuelConnectionsForPlanning(bridge))
                return;

            OreSelection ores;
            string selectionProblem;
            if (!TryChoosePlanningOres(bridge, out ores, out selectionProblem))
            {
                Fail(bridge, selectionProblem, true);
                return;
            }
            if (ores.Targets.Count == 0)
            {
                if (planningPurpose == "Items")
                {
                    bridge.Set("state", "producing");
                    bridge.Set("message", production.Status);
                    nextProductionPlanCheck = frame + 300;
                }
                else
                    Fail(bridge, "no ores are set to Y in EMM-A Ca Ores", true);
                return;
            }
            planningSelection = ores;

            activePlan = null;
            planningActive = true;
            matchingCandidates = 0;
            planningFailures = 0;
            lastPlanningFailure = "";
            BuildCandidateList();
            PrepareDeparture(bridge);
            bridge.Set("state", "scanning");
            bridge.Set("message", "scanning nearest streamed asteroids");
            bridge.Set("missionId", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Emma3Log.Info("Plan request " + handledRequest + " accepted; type=" + (planningCarrierOnly ? "carrier" : "full")
                + " target=" + requestedAsteroidEntityId + " candidates=" + candidates.Count + ".");
        }

        private void ContinuePlanning()
        {
            if (departurePreparation != null && !departurePreparation.Complete) return;
            OreSelection ores = planningSelection;
            while (candidateIndex < candidates.Count)
            {
                MyVoxelBase voxel = candidates[candidateIndex++];
                if (voxel == null || voxel.Closed || voxel.MarkedForClose || completed.Contains(voxel.EntityId) || rejectedCandidates.Contains(voxel.EntityId))
                    continue;
                if (!MatchesRequestedAsteroid(voxel.EntityId))
                    continue;
                AsteroidMap map;
                if (cache.TryGetValue(voxel.EntityId, out map) && map.Complete)
                {
                    if (Qualifies(map, completed.Remaining(map.EntityId, ores)))
                    {
                        BuildAndPublish(map);
                        return;
                    }

                    continue;
                }

                backgroundScanActive = false;
                scanner.Begin(voxel);
                UpdateStatus("scanning", "Map " + voxel.EntityId + " " + candidateIndex + "/" + candidates.Count + ": 0.0%");
                return;
            }

            string noMatch;
            if (matchingCandidates > 0)
                noMatch = "Routes failed: " + planningFailures + "/" + matchingCandidates
                    + "; last=" + (lastPlanningFailure.Length == 0 ? "unknown" : lastPlanningFailure);
            else
                noMatch = "No " + DescribeTargetOres(ores) + " in " + cache.Count + "; found " + DescribeDetectedOres();
            UpdateStatus("error", noMatch, matchingCandidates == 0);
            Emma3Log.Diagnostic(noMatch);
            planningActive = false;
            TryRun(blocks.MinerPb, "stop");
        }

        private void HandleScanFailure(AsteroidMap map, Exception ex)
        {
            Exception root = ex.GetBaseException();
            string asteroid = map == null ? "unknown asteroid" : "asteroid " + map.EntityId;
            string reason = asteroid + " scan failed: " + root.GetType().Name + ": " + root.Message;
            if (map != null)
            {
                rejectedCandidates.Add(map.EntityId);
                cache.Remove(map.EntityId);
            }

            discardScanResult = false;
            scanner.ClearCurrent();
            backgroundScanActive = false;
            planningFailures++;
            lastPlanningFailure = reason;
            Emma3Log.Error(reason);
            if (planningActive)
            {
                UpdateStatus("scanning", "Skip " + asteroid + ": " + root.GetType().Name + ": " + root.Message);
                ContinuePlanning();
            }
            else
            {
                nextBackgroundRefresh = frame + 600;
            }
        }

        private void AddDetectedOresToTimer(AsteroidMap map)
        {
            if (map == null || blocks.OreTimer == null || map.OreSamples.Count == 0)
                return;
            List<string> added;
            string updated = OreSelection.AddMissingOpportunistic(blocks.OreTimer.CustomData, map.OreSamples.Keys, out added);
            if (added.Count == 0)
                return;
            blocks.OreTimer.CustomData = updated;
            Emma3Log.Diagnostic("Added newly detected ore(s) to EMM-A Ca Ores as U: " + string.Join(",", added) + ".");
        }

        private void BuildAndPublish(AsteroidMap map)
        {
            matchingCandidates++;
            try
            {
                var job = new PlanningJob
                {
                    Request = acceptedPlanRequest,
                    Map = map,
                    Snapshot = CapturePlanningSnapshot(map)
                };
                planningJob = job;
                UpdateStatus("planning", "Planning route");
                Emma3Log.Info("Background route planning started for asteroid " + map.EntityId + ".");
                MyAPIGateway.Parallel.StartBackground(() => RunPlanningJob(job));
            }
            catch (Exception ex)
            {
                HandlePlanningFailure(map, ex);
            }
        }

        private void RunPlanningJob(PlanningJob job)
        {
            try
            {
                job.Plan = planner.Build(job.Snapshot, job.Cancellation, job.Progress);
            }
            catch (Exception ex)
            {
                job.Error = ex;
            }
            finally
            {
                job.Finished = true;
            }
        }

        private PlanningSnapshot CapturePlanningSnapshot(AsteroidMap map)
        {
            if (departurePreparation != null && !departurePreparation.Complete)
                throw new InvalidOperationException("departure voxel preparation is incomplete");
            if (map == null || !map.Complete)
                throw new InvalidOperationException("cannot plan from an incomplete asteroid map");
            MatrixD actualMinerRemoteWorld = planningCarrierOnly || planningDimensions != null ? MatrixD.Identity : blocks.MinerRemote.WorldMatrix;
            MatrixD minerRemoteWorld = actualMinerRemoteWorld;
            if (planningDimensions != null)
                minerRemoteWorld = planningDimensions.Project(blocks.CarrierConnector.WorldMatrix);
            else if (planningProjectMinerDockPose)
                minerRemoteWorld = GeometrySafety.ProjectRemoteAtCarrierConnector(
                    minerRemoteWorld,
                    blocks.MinerConnector.WorldMatrix,
                    blocks.CarrierConnector.WorldMatrix);
            var snapshot = new PlanningSnapshot
            {
                CarrierOnly = planningCarrierOnly,
                Asteroid = map,
                OreSelection = completed.Remaining(map.EntityId, planningSelection),
                Settings = settings.Clone(),
                CarrierRemoteWorld = blocks.CarrierRemote.WorldMatrix,
                MinerRemoteWorld = minerRemoteWorld,
                HasCarrierConnector = blocks.CarrierConnector != null,
                CarrierConnectorForward = blocks.CarrierConnector == null ? Vector3D.Zero : blocks.CarrierConnector.WorldMatrix.Forward,
                CarrierConnectorUp = blocks.CarrierConnector == null ? Vector3D.Zero : blocks.CarrierConnector.WorldMatrix.Up,
                MinerToolOffsetLocal = planningDimensions != null ? planningDimensions.ToolOffsetLocal : settings.MinerTargetOffsetLocal
            };
            BridgeDocument fleetBridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
            int fleetCount = fleetBridge.GetInt("fleetCount");
            if (fleetCount > 0)
            {
                if (fleetCount > 3 || blocks.FleetPathTimer == null) throw new InvalidOperationException("Fleet planning requires 1–3 registered miners and the Carrier Fleet Path timer.");
                fleetCount = Math.Min(fleetCount, Math.Max(1, Math.Min(3, fleetBridge.GetInt("minerUseCount", 3))));
                snapshot.FleetCount = fleetCount; snapshot.FleetMembers = new string[fleetCount];
                for (int i = 0; i < fleetCount; i++)
                {
                    snapshot.FleetMembers[i] = fleetBridge.Get("fleetId" + i);
                    if (snapshot.FleetMembers[i].Length == 0) throw new InvalidOperationException("Fleet registration changed during planning.");
                }
            }
            snapshot.MinerRadius = planningCarrierOnly ? 0 : planningDimensions != null ? planningDimensions.Radius : blocks.MinerRemote.CubeGrid.WorldVolume.Radius;
            if (planningDimensions != null) snapshot.MinerDrills.AddRange(planningDimensions.Drills);
            for (int i = 0; !planningCarrierOnly && planningDimensions == null && i < blocks.MinerDrills.Count; i++)
            {
                IMyTerminalBlock drillBlock = blocks.MinerDrills[i] as IMyTerminalBlock;
                if (drillBlock == null || drillBlock.PositionComp == null)
                    continue;
                MyShipDrillDefinition drillDefinition = null;
                VRage.Game.ModAPI.IMyCubeBlock cubeBlock = drillBlock as VRage.Game.ModAPI.IMyCubeBlock;
                if (cubeBlock != null && cubeBlock.SlimBlock != null)
                    drillDefinition = cubeBlock.SlimBlock.BlockDefinition as MyShipDrillDefinition;
                double cutOutRadius = drillDefinition != null && drillDefinition.CutOutRadius > 0 ? drillDefinition.CutOutRadius : Math.Max(1, drillBlock.PositionComp.WorldAABB.HalfExtents.Length());
                double cutOutOffset = drillDefinition != null ? drillDefinition.CutOutOffset : 0;
                Vector3D cutOutCenter = drillBlock.WorldMatrix.Translation + drillBlock.WorldMatrix.Forward * cutOutOffset;
                Vector3D delta = cutOutCenter - actualMinerRemoteWorld.Translation;
                Vector3D localOffset = new Vector3D(
                    Vector3D.Dot(delta, actualMinerRemoteWorld.Right),
                    Vector3D.Dot(delta, actualMinerRemoteWorld.Up),
                    Vector3D.Dot(delta, actualMinerRemoteWorld.Forward));
                snapshot.MinerDrills.Add(new DrillFootprint { LocalOffset = localOffset, Radius = cutOutRadius });
            }

            LogDrillFootprints(snapshot);
            for (int i = 0; i < candidates.Count; i++)
            {
                MyVoxelBase voxel = candidates[i];
                if (voxel != null && voxel.EntityId != map.EntityId)
                {
                    AsteroidMap departure;
                    if (departurePreparation != null && departurePreparation.Maps.TryGetValue(voxel.EntityId, out departure))
                        snapshot.DepartureMaps.Add(departure);
                    else snapshot.OtherVoxelBoxes.Add(voxel.PositionComp.WorldAABB);
                }
            }

            return snapshot;
        }

        private static void LogDrillFootprints(PlanningSnapshot snapshot)
        {
            if (snapshot == null || snapshot.MinerDrills.Count == 0)
            {
                Emma3Log.Diagnostic("Planner captured no Miner drill cutout spheres; destruction-mode ranges will be empty.");
                return;
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            double maxRadius = 0;
            for (int i = 0; i < snapshot.MinerDrills.Count; i++)
            {
                DrillFootprint drill = snapshot.MinerDrills[i];
                minX = Math.Min(minX, drill.LocalOffset.X);
                maxX = Math.Max(maxX, drill.LocalOffset.X);
                minY = Math.Min(minY, drill.LocalOffset.Y);
                maxY = Math.Max(maxY, drill.LocalOffset.Y);
                minZ = Math.Min(minZ, drill.LocalOffset.Z);
                maxZ = Math.Max(maxZ, drill.LocalOffset.Z);
                maxRadius = Math.Max(maxRadius, drill.Radius);
            }

            Emma3Log.Diagnostic("Planner captured " + snapshot.MinerDrills.Count
                + " Miner drill cutout sphere(s): localX=" + F(minX)
                + ".." + F(maxX)
                + " localY=" + F(minY)
                + ".." + F(maxY)
                + " localZ=" + F(minZ)
                + ".." + F(maxZ)
                + " toolZ=" + F(snapshot.MinerToolOffsetLocal.Z)
                + " maxRadius=" + F(maxRadius)
                + ".");
        }

        private static string F(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void PollPlanningJob()
        {
            PlanningJob job = planningJob;
            if (job == null)
                return;
            if (!job.Finished)
            {
                if (frame % 30 == 0)
                    UpdateStatus("planning", job.Progress.Message);
                return;
            }

            BridgeDocument currentRequest = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
            if (job.Request != null && (!job.Request.Matches(currentRequest) || !CurrentPlanningRequest(currentRequest))) return;

            planningJob = null;
            if (job.Cancellation.IsCancellationRequested)
                return;
            if (job.Error != null)
            {
                HandlePlanningFailure(job.Map, job.Error);
                return;
            }

            activePlan = job.Plan;
            BridgeDocument bridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
            try
            {
                int records = 0;
                if (!planningCarrierOnly)
                {
                    string validation;
                    if (!PathCodec.Validate(activePlan.MinerPathText, out records, out validation))
                        throw new InvalidOperationException(validation);
                    if (activePlan.MinerPathText.Length > 61000)
                        throw new InvalidOperationException("Miner path has " + activePlan.MinerPathText.Length + " chars; safe limit is 61,000");
                    if (activePlan.FleetMembers != null) SaveFleetPublication(activePlan);
                    else blocks.MinerPathTimer.CustomData = activePlan.MinerPathText;
                    int confirmRecords;
                    string confirm;
                    if (activePlan.FleetMembers == null && (!PathCodec.Validate(blocks.MinerPathTimer.CustomData, out confirmRecords, out confirm) || PathCodec.Hash(blocks.MinerPathTimer.CustomData) != activePlan.PathHash))
                        throw new InvalidOperationException("Miner path read-back failed: " + confirm);
                }
                PublishPlan(bridge, activePlan, records);
                RememberPlanningResponse(bridge);
                if (!planningCarrierOnly)
                    SaveVisualizationSnapshot(activePlan);
                planningActive = false;
                Emma3Log.Plan(activePlan.MissionId, planningCarrierOnly
                    ? "Published Carrier-only transit plan while Miner remains stored. Movement=" + settings.AllowMovement
                    : "Published and read-back validated background-planned Miner path. Movement=" + settings.AllowMovement);
            }
            catch (Exception ex)
            {
                string export = Emma3Log.ExportFailure(activePlan?.MissionId, DescribeFailure(job.Map, activePlan, ex));
                if (blocks.MinerPathTimer != null)
                    blocks.MinerPathTimer.CustomData = "";
                activePlan = null;
                Fail(bridge, ex.Message + (export.Length == 0 ? "" : "; details: " + export), true);
                planningActive = false;
            }

            blocks.BridgeTimer.CustomData = bridge.Serialize();
        }

        private void HandlePlanningFailure(AsteroidMap map, Exception ex)
        {
            rejectedCandidates.Add(map.EntityId);
            planningFailures++;
            lastPlanningFailure = "asteroid " + map.EntityId + ": " + ex.Message;
            string export = Emma3Log.ExportFailure("candidate-" + map.EntityId, DescribeFailure(map, activePlan, ex));
            Emma3Log.Warn("Skipped asteroid " + map.EntityId + " because its mining geometry could not be planned: " + ex.Message + (export.Length == 0 ? "" : "; details: " + export));
            activePlan = null;
            UpdateStatus("scanning", "Skip " + map.EntityId + ": " + ex.Message);
            if (planningActive)
                ContinuePlanning();
        }

        private void CancelPlanningJob(string reason)
        {
            PlanningJob job = planningJob;
            planningJob = null;
            if (job == null)
                return;
            job.Cancellation.Cancel();
            Emma3Log.Info("Cancelled outdated background route plan for asteroid " + job.Map.EntityId + ": " + reason + ".");
        }
    }
}
