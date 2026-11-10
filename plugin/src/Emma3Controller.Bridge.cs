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
        private void ProcessBridge()
        {
            BridgeDocument bridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
            minerPbReference.Publish(bridge);
            EnsureDefaults(bridge);
            ReadSettings(bridge);
            InitializeBridgeCounters(bridge);
            InitializeFuelConnectionWarning(bridge);
            ApplyPluginFeatureSettings(bridge);
            ApplyPluginLcdSettings(bridge);
            resources.Publish(blocks, bridge.GetBool("iceReserveEnabled") || bridge.Get("selectionMode").Equals("Items", StringComparison.OrdinalIgnoreCase));
            UpdateProduction(bridge);
            UpdateRefinerySorters(bridge);
            TryRestoreVisualization(bridge);
            int reset = bridge.GetInt("resetSequence");
            if (reset > handledReset)
            {
                handledReset = reset;
                restartPlanningAfterCockpitLoss = false;
                CancelDeparturePreparation();
                CancelPlanningJob("clean reset");
                discardScanResult = discardScanResult || scanner.Active;
                scanner.Cancel();
                if (!scanner.Active)
                    scanner.ClearCurrent();
                planningActive = false;
                cockpitSuspended = false;
                backgroundScanActive = false;
                activePlan = null;
                restoredVisualizationPlan = null;
                lastVisualizationRestoreKey = "";
                visualizationSnapshots.Clear();
                completed.Clear();
                blocks.CompletedAsteroidsTimer.CustomData = completed.Serialize();
                cache.Clear();
                rejectedCandidates.Clear();
                candidates.Clear();
                backgroundCandidates.Clear();
                candidateIndex = 0;
                backgroundIndex = 0;
                nextBackgroundRefresh = frame + 60;
                matchingCandidates = 0;
                planningFailures = 0;
                lastPlanningFailure = "";
                handledRequest = Math.Max(handledRequest, bridge.GetInt("request"));
                handledCompletion = Math.Max(handledCompletion, bridge.GetInt("completionSequence"));
                if (blocks.MinerPathTimer != null)
                    blocks.MinerPathTimer.CustomData = "";
                TryRun(blocks.CarrierPb, "PluginAbort");
                TryRun(blocks.MinerPb, "stop");
                ClearPublishedMission(bridge);
                bridge.Set("resetAck", reset);
                bridge.Set("completionAck", handledCompletion);
                bridge.Set("state", "idle");
                bridge.Set("message", "Reset complete; paths, scans, caches, IDs cleared");
                Emma3Log.Info("Clean reset completed from Carrier menu: completed IDs, active mission, scan/cache state, rejected candidates, Miner path, and Carrier route cleared.");
            }

            int completion = bridge.GetInt("completionSequence");
            if (completion > handledCompletion)
            {
                handledCompletion = completion;
                long id;
                if (long.TryParse(bridge.Get("completedEntityId"), out id) && id != 0)
                {
                    completed.Add(id, bridge.Get("missionMinedOres"));
                    blocks.CompletedAsteroidsTimer.CustomData = completed.Serialize();
                    cache.Remove(id);
                    Emma3Log.Info("Marked asteroid entity " + id + " complete in EMM-A Ca Asteroid IDs.");
                }

                activePlan = null;
                restoredVisualizationPlan = null;
                lastVisualizationRestoreKey = "";
                visualizationSnapshots.Clear();
                bridge.Set("completionAck", completion);
            }

            int request = bridge.GetInt("request");
            string command = bridge.Get("command");
            if (request > handledRequest && command.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                handledRequest = request;
                BeginPlanning(bridge);
            }
            else if (request > handledRequest && command.Equals("abort", StringComparison.OrdinalIgnoreCase))
            {
                handledRequest = request;
                Abort("Carrier requested abort");
                bridge.Set("state", "aborted");
                bridge.Set("message", "planner aborted");
            }
            else if (request > handledRequest && command.Equals("continue", StringComparison.OrdinalIgnoreCase))
            {
                handledRequest = request;
                ContinueAfterCockpitLoss(bridge);
            }

            if (bridge.Get("state") == "producing" && bridge.GetBool("autonomousMining") && frame >= nextProductionPlanCheck)
            {
                nextProductionPlanCheck = frame + 300;
                BeginPlanning(bridge);
            }

            if (bridge.Get("state") == "completed")
            {
                activePlan = null;
                restoredVisualizationPlan = null;
                visualizationSnapshots.Clear();
            }

            if (activePlan != null && bridge.Get("state") == "ready_dryrun" && settings.AllowMovement)
            {
                bridge.Set("state", "ready");
                bridge.Set("message", "validated plan released for movement");
            }
            else if (activePlan != null && bridge.Get("state") == "ready" && !settings.AllowMovement)
            {
                bridge.Set("state", "ready_dryrun");
                bridge.Set("message", "plan ready; Allow movement is Off");
            }

            bridge.Set("completedCount", completed.Count);
            bridge.Set("continueAvailable", cockpitSuspended && blocks.IsCarrierPilot ? 1 : 0);
            bridge.Set("pluginLcdReady", pluginLcdOutput && blocks.IsCarrierPilot && blocks.CarrierLcdMain != null ? 1 : 0);
            bridge.Set("pluginLcdHeartbeat", pluginLcdHeartbeat);
            completedCarrierRouteMission = bridge.Get("carrierRouteCompletedMission");
            if (bridge.Dirty)
                blocks.BridgeTimer.CustomData = bridge.Serialize();
        }

        private void PublishUnavailableBridgeStatus()
        {
            string problem = string.IsNullOrWhiteSpace(blocks.Problem) ? "planner prerequisites are unavailable" : blocks.Problem;
            if (!string.Equals(problem, lastBlockProblem, StringComparison.Ordinal))
            {
                Emma3Log.Warn("Planner unavailable: " + problem + ".");
                lastBlockProblem = problem;
            }

            var bridgeTimer = blocks.BridgeTimer ?? lastBridge;
            if (bridgeTimer == null)
                return;
            BridgeDocument bridge = BridgeDocument.Parse(bridgeTimer.CustomData);
            EnsureDefaults(bridge);
            Emma3Log.SetEnabled(bridge.GetBool("scriptLogging"));
            InitializeBridgeCounters(bridge, false);
            if (!bridgeCountersInitialized) return;
            int request = bridge.GetInt("request");
            string command = bridge.Get("command");
            bool actionable = command.Equals("plan", StringComparison.OrdinalIgnoreCase) || command.Equals("continue", StringComparison.OrdinalIgnoreCase) || command.Equals("abort", StringComparison.OrdinalIgnoreCase);
            if (request <= handledRequest || !actionable)
                return;
            handledRequest = request;
            bridge.Set("state", "error");
            bridge.Set("message", CompactUnavailableMessage(problem));
            bridgeTimer.CustomData = bridge.Serialize();
            Emma3Log.Error("Rejected " + command + " request " + request + ": " + problem + ".");
        }

        private static string CompactUnavailableMessage(string problem)
        {
            if (problem == "sit in a Carrier cockpit" || problem == "planning requires the Carrier cockpit")
                return "Unavailable: use the Carrier cockpit.";
            if (problem == "no terminal system for occupied grid")
                return "Unavailable: no terminal system for this grid.";
            if (problem == "missing Plugin Bridge timer")
                return "Unavailable: missing Plugin Bridge timer.";
            if (problem == "missing Ores timer")
                return "Unavailable: missing Ores timer.";
            if (problem == "missing Asteroid IDs timer")
                return "Unavailable: missing Asteroid IDs timer.";
            if (problem == "missing Carrier programmable block")
                return "Unavailable: missing Carrier PB.";
            return "Unavailable: " + problem + ".";
        }

        private static void ClearPublishedMission(BridgeDocument bridge)
        {
            bridge.Set("command", "");
            bridge.Remove("missionId");
            bridge.Remove("asteroidEntityId");
            bridge.Remove("targetPosition");
            bridge.Remove("targetForward");
            bridge.Remove("targetUp");
            bridge.Remove("carrierRouteCount");
            bridge.RemovePrefix("carrierRoute");
            bridge.Remove("pathHash");
            bridge.Remove("pathRecords");
            bridge.Remove("planeCount");
            bridge.Remove("completedEntityId");
            bridge.Remove("missionMinedOres");
            bridge.Remove("missionPurpose");
            bridge.Remove("planType");
            bridge.Remove("requestedAsteroidEntityId");
            bridge.Remove("projectMinerDockPose");
        }

        private void InitializeBridgeCounters(BridgeDocument bridge, bool resumeInterruptedPlanning = true)
        {
            if (bridgeCountersInitialized)
                return;
            int request = bridge.GetInt("request");
            string state = bridge.Get("state");
            string command = bridge.Get("command");
            bool interruptedPlan = request > 0
                && (state.Equals("scanning", StringComparison.OrdinalIgnoreCase) || state.Equals("planning", StringComparison.OrdinalIgnoreCase))
                && (command.Equals("plan", StringComparison.OrdinalIgnoreCase) || command.Equals("continue", StringComparison.OrdinalIgnoreCase));
            if (interruptedPlan && !resumeInterruptedPlanning) return;
            if (interruptedPlan)
            {
                // Scan workers and planning jobs do not survive a session reload.
                command = "plan";
                state = "requested";
                bridge.Set("command", command);
                bridge.Set("state", state);
                bridge.Set("message", "Resuming interrupted planning after reload");
            }
            bool pending = state.Equals("requested", StringComparison.OrdinalIgnoreCase) && (command.Equals("plan", StringComparison.OrdinalIgnoreCase) || command.Equals("continue", StringComparison.OrdinalIgnoreCase) || command.Equals("abort", StringComparison.OrdinalIgnoreCase));
            handledRequest = pending && request > 0 ? request - 1 : request;
            int resetSequence = bridge.GetInt("resetSequence");
            string resetAckText = bridge.Get("resetAck");
            if (resetAckText.Length == 0)
            {
                handledReset = resetSequence;
                bridge.Set("resetAck", resetSequence);
            }
            else
            {
                handledReset = bridge.GetInt("resetAck");
            }

            handledCompletion = bridge.GetInt("completionAck");
            bridgeCountersInitialized = true;
            Emma3Log.Diagnostic("Initialized bridge command counters: request=" + handledRequest + " reset=" + handledReset + " completionAck=" + handledCompletion + ".");
        }
    }
}
