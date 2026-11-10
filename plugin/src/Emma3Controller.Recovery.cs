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
        private void UpdateStatus(string state, string message)
        {
            if (blocks.BridgeTimer == null)
                return;
            BridgeDocument bridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
            bridge.Set("state", state);
            bridge.Set("message", message);
            blocks.BridgeTimer.CustomData = bridge.Serialize();
        }

        private void PublishScanProgress(bool writeLog)
        {
            AsteroidMap map = scanner.Current;
            if (map == null)
                return;
            string message = "Map " + map.EntityId
                + " " + candidateIndex
                + "/" + candidates.Count
                + ": " + (scanner.Progress * 100).ToString("0.0")
                + "% ETA " + FormatDuration(scanner.EstimatedRemaining);
            UpdateStatus("scanning", message);
            if (writeLog)
                Emma3Log.Info(message
                    + "; tested=" + map.TestedSamples
                    + " skippedEmpty=" + map.SkippedEmptySamples
                    + " chunks=" + map.NonEmptyChunks
                    + "+" + map.EmptyChunks
                    + " elapsed=" + FormatDuration(scanner.Elapsed));
        }

        private void LogBackgroundScanProgress()
        {
            AsteroidMap map = scanner.Current;
            if (map == null)
                return;
            Emma3Log.Info("background mapping " + map.EntityId
                + " [" + scanner.Phase
                + "] " + (scanner.Progress * 100).ToString("0.0")
                + "% " + FormatCount(map.ProcessedSamples)
                + "/" + FormatCount(map.TotalSamples)
                + " ETA " + FormatDuration(scanner.EstimatedRemaining));
        }

        private void Fail(BridgeDocument bridge, string reason, bool stop)
        {
            Emma3Log.Error(reason);
            bridge.Set("state", "error");
            bridge.Set("message", reason);
            if (stop)
            {
                TryRun(blocks.CarrierPb, "PluginAbort");
                TryRun(blocks.MinerPb, "stop");
            }
        }

        public void Abort(string reason)
        {
            restartPlanningAfterCockpitLoss = false;
            CancelDeparturePreparation();
            CancelPlanningJob(reason);
            if (scanner.Active)
            {
                discardScanResult = true;
                scanner.Cancel();
            }
            else
            {
                scanner.ClearCurrent();
            }

            cockpitSuspended = false;
            backgroundScanActive = false;
            planningActive = false;
            activePlan = null;
            if (lastBridge != null)
            {
                BridgeDocument b = BridgeDocument.Parse(lastBridge.CustomData);
                b.Set("state", "aborted");
                b.Set("message", reason);
                b.Set("continueAvailable", 0);
                lastBridge.CustomData = b.Serialize();
            }

            TryRun(lastCarrierPb ?? blocks.CarrierPb, "PluginAbort");
            TryRun(lastMinerPb ?? blocks.MinerPb, "stop");
            Emma3Log.Warn("Abort: " + reason);
        }

        private void SuspendForCockpitLoss(string reason)
        {
            if (cockpitSuspended)
                return;
            cockpitSuspended = true;
            if (planningActive)
            {
                restartPlanningAfterCockpitLoss = true;
                CancelDeparturePreparation();
                CancelPlanningJob("cockpit lost during planning");
                discardScanResult = scanner.Active;
                scanner.Cancel();
            }
            if (lastBridge != null)
            {
                BridgeDocument bridge = BridgeDocument.Parse(lastBridge.CustomData);
                bridge.Set("state", "aborted");
                bridge.Set("message", "sit in a Carrier cockpit");
                bridge.Set("continueAvailable", 0);
                lastBridge.CustomData = bridge.Serialize();
            }

            TryRun(lastCarrierPb ?? blocks.CarrierPb, "PluginCockpitAbort");
            TryRun(lastMinerPb ?? blocks.MinerPb, "pause");
            Emma3Log.Warn("Cockpit-loss abort: mission state retained for Continue. " + reason);
        }

        private void ContinueAfterCockpitLoss(BridgeDocument bridge)
        {
            if (!cockpitSuspended)
            {
                bridge.Set("message", "no cockpit-loss abort is waiting");
                bridge.Set("continueAvailable", 0);
                return;
            }

            if (!blocks.IsCarrierPilot)
            {
                bridge.Set("state", "aborted");
                bridge.Set("message", "sit in a Carrier cockpit");
                bridge.Set("continueAvailable", 0);
                return;
            }

            cockpitSuspended = false;
            TryRun(blocks.CarrierPb ?? lastCarrierPb, "PluginContinue");
            TryRun(blocks.MinerPb ?? lastMinerPb, "resume");
            if (restartPlanningAfterCockpitLoss)
            {
                restartPlanningAfterCockpitLoss = false;
                BeginPlanning(bridge);
                bridge.Set("continueAvailable", 0);
                return;
            }
            if (scanner.Active)
            {
                bridge.Set("state", "scanning");
                bridge.Set("message", "continuing asteroid scan");
            }
            else if (planningJob != null || planningActive)
            {
                bridge.Set("state", "planning");
                bridge.Set("message", "continuing route planning");
            }
            else
            {
                bridge.Set("state", settings.AllowMovement ? "ready" : "ready_dryrun");
                bridge.Set("message", "continued interrupted mission");
            }

            bridge.Set("continueAvailable", 0);
            Emma3Log.Info("Continued cockpit-loss abort without resetting mission progress.");
        }

        public void Dispose()
        {
            runtimeReadout.Clear();
            production.Dispose();
            scanner.Cancel();
            CancelDeparturePreparation();
            CancelPlanningJob("plugin unloaded");
            if (planningActive || activePlan != null)
                Abort("EMM-A plugin unloaded");
            planningActive = false;
        }

        public void Detach()
        {
            runtimeReadout.Clear();
            production.Dispose();
            scanner.Cancel();
            CancelDeparturePreparation();
            CancelPlanningJob("world detached");
            planningActive = false;
            activePlan = null;
            Emma3Log.Diagnostic("Controller detached from previous world without issuing craft commands.");
        }
    }
}
