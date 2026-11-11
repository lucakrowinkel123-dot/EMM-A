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
        private readonly string userDataPath;
        private readonly ShipContext shipContext = new ShipContext();
        public bool RebindRequested { get; private set; }
        private readonly BlockContext blocks = new BlockContext();
        private readonly VoxelScanner scanner = new VoxelScanner();
        private readonly RoutePlanner planner = new RoutePlanner();
        private readonly PlanVisualizer visualizer = new PlanVisualizer();
        private readonly VisualizationSnapshotStore visualizationSnapshots;
        private readonly ScriptLogCollector scriptLogs = new ScriptLogCollector();
        private readonly RememberedEntityReference<Sandbox.ModAPI.Ingame.IMyProgrammableBlock> minerPbReference;
        private readonly CarrierResourceTelemetry resources = new CarrierResourceTelemetry();
        private readonly CarrierProduction production = new CarrierProduction();
        private readonly CarrierRefinerySorters refinerySorters = new CarrierRefinerySorters();
        private OreSelection planningSelection = new OreSelection();
        private string planningPurpose = "Ore";
        private bool planningCarrierOnly;
        private bool planningProjectMinerDockPose;
        private MinerDimensions planningDimensions;
        private long requestedAsteroidEntityId;
        private int nextProductionPlanCheck;
        private readonly Dictionary<long, AsteroidMap> cache = new Dictionary<long, AsteroidMap>();
        private readonly HashSet<long> rejectedCandidates = new HashSet<long>();
        private readonly List<MyVoxelBase> candidates = new List<MyVoxelBase>();
        private readonly List<MyVoxelBase> backgroundCandidates = new List<MyVoxelBase>();
        private CompletedAsteroids completed;
        private PlannerSettings settings = new PlannerSettings();
        private MissionPlan activePlan;
        private string completedCarrierRouteMission = "";
        private MissionPlan restoredVisualizationPlan;
        private int frame;
        private int candidateIndex;
        private int handledRequest;
        private int handledReset;
        private int handledCompletion;
        private bool bridgeCountersInitialized;
        private bool planningActive;
        private bool backgroundScanActive;
        private bool discardScanResult;
        private int backgroundIndex;
        private int nextBackgroundRefresh;
        private int matchingCandidates;
        private int planningFailures;
        private string lastPlanningFailure = "";
        private SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock lastBridge;
        private Sandbox.ModAPI.Ingame.IMyProgrammableBlock lastCarrierPb;
        private Sandbox.ModAPI.Ingame.IMyProgrammableBlock lastMinerPb;
        private Sandbox.ModAPI.Ingame.IMyProgrammableBlock lastCarrierPilotPb,lastMinerPilotPb;
        private PlanningJob planningJob;
        private bool cockpitSuspended;
        private int nextVisualizationDiagnosticFrame;
        private int nextVisualizationErrorFrame;
        private string lastVisualizationDiagnostic = "";
        private string lastVisualizationRestoreKey = "";
        private string lastBlockProblem = "";
        private sealed class PlanningJob
        {
            public PlanRequestIdentity Request;
            public AsteroidMap Map;
            public PlanningSnapshot Snapshot;
            public readonly PlanningCancellation Cancellation = new PlanningCancellation();
            public readonly PlanningProgress Progress = new PlanningProgress();
            public MissionPlan Plan;
            public Exception Error;
            public volatile bool Finished;
        }

        public Emma3Controller(string path)
        {
            userDataPath = path;
            minerPbReference = new RememberedEntityReference<Sandbox.ModAPI.Ingame.IMyProgrammableBlock>(
                "minerPbEntityId", ResolveMinerPbEntity, IsAccessibleMinerPb);
            completed = new CompletedAsteroids(userDataPath);
            visualizationSnapshots = new VisualizationSnapshotStore(userDataPath);
        }

        public void Update()
        {
            frame++;
            UpdatePluginServices();
            UpdateDockHardware();
            if (settings.ScriptLogging && frame % 30 == 0) DrainFleetLogs();
            if (frame % 60 == 1)
            {
                bool ready = blocks.Refresh();
                bool registeredFleet = BridgeDocument.Parse(blocks.BridgeTimer?.CustomData).GetInt("fleetCount") > 0;
                if (ready && shipContext.Changed(blocks.CarrierPb.EntityId, blocks.BridgeTimer.EntityId, blocks.Names.Pair))
                {
                    RebindRequested = true;
                    return;
                }
                if(blocks.CarrierPilotPb!=null)lastCarrierPilotPb=blocks.CarrierPilotPb;
                if(blocks.MinerPilotPb!=null)lastMinerPilotPb=blocks.MinerPilotPb;
                // Logging must not depend on every planner prerequisite being valid.
                // Refresh can find the PBs and then return false for a missing timer,
                // wrong cockpit, or another planning-only requirement.
                if (blocks.CarrierPb != null)
                    lastCarrierPb = blocks.CarrierPb;
                if (blocks.MinerPb != null)
                {
                    lastMinerPb = blocks.MinerPb;
                    minerPbReference.Observe(blocks.MinerPb, blocks.MinerPb.EntityId);
                }
                else if (!registeredFleet && (lastMinerPb == null || !IsAccessibleMinerPb(lastMinerPb)) && blocks.BridgeTimer != null)
                {
                    minerPbReference.Load(BridgeDocument.Parse(blocks.BridgeTimer.CustomData));
                    lastMinerPb = minerPbReference.Resolve();
                    if (lastMinerPb != null)
                        Emma3Log.Diagnostic("Reacquired the disconnected Miner programmable block from its persisted bridge identity.");
                }
                bool detachedMinerDiscoveryRequired = planningActive && !planningCarrierOnly;
                if (!detachedMinerDiscoveryRequired && blocks.BridgeTimer != null)
                {
                    BridgeDocument pendingBridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
                    detachedMinerDiscoveryRequired = pendingBridge.Get("state") == "requested"
                        && pendingBridge.Get("command").Equals("plan", StringComparison.OrdinalIgnoreCase)
                        && !pendingBridge.Get("planType").Equals("carrier", StringComparison.OrdinalIgnoreCase);
                }
                if (!registeredFleet && detachedMinerDiscoveryRequired && blocks.MinerPb == null && (lastMinerPb == null || !IsAccessibleMinerPb(lastMinerPb)))
                {
                    lastMinerPb = blocks.FindDetachedMinerNearCarrier();
                    if (lastMinerPb != null)
                    {
                        minerPbReference.Observe(lastMinerPb, lastMinerPb.EntityId);
                        Emma3Log.Diagnostic("Discovered the newly deployed Miner grid near the Carrier by its exact block names.");
                    }
                }
                if (!registeredFleet && blocks.MinerPb == null && lastMinerPb != null && blocks.RestoreDetachedMiner(lastMinerPb))
                    Emma3Log.Diagnostic("Reacquired the deployed Miner grid outside the Carrier terminal system.");
                if(blocks.MinerPilotPb!=null)lastMinerPilotPb=blocks.MinerPilotPb;
                ObservePluginInfoTimers();
                UpdateRuntimeLcd();
                if (!ready)
                {
                    PublishUnavailableBridgeStatus();
                    if (planningActive || activePlan != null)
                    {
                        if (blocks.Problem == "sit in a Carrier cockpit" || blocks.Problem == "planning requires the Carrier cockpit")
                        {
                            if (!planningActive && activePlan != null && settings.AllowSeatExitDuringMining)
                                return;
                            SuspendForCockpitLoss(blocks.Problem);
                        }
                        else
                            Abort(blocks.Problem);
                    }

                    return;
                }

                lastBlockProblem = "";
                if (planningActive && !planningCarrierOnly && planningDimensions == null)
                {
                    string dockedMinerProblem = blocks.DockedMinerProblem();
                    if (dockedMinerProblem.Length != 0)
                    {
                        Abort(dockedMinerProblem);
                        return;
                    }
                }

                lastBridge = blocks.BridgeTimer;
                // Keep the last valid Miner reference after it undocks. Miner
                // blocks disappearing from the Carrier terminal system is the
                // expected mission state, not a reason to abort the active plan.
                if (completed == null)
                    completed = new CompletedAsteroids(userDataPath);
                if (completed.SynchronizeFromTimer(blocks.CompletedAsteroidsTimer.CustomData))
                {
                    blocks.CompletedAsteroidsTimer.CustomData = completed.Serialize();
                    Emma3Log.Info("Migrated the legacy completed-asteroid IDs into EMM-A Ca Asteroid IDs.");
                }

                ProcessBridge();
            }

            UpdateCarrierLcdOutput();
            UpdateRuntimeLcd();

            if (!blocks.IsCarrierPilot || cockpitSuspended)
                return;
            if (scanner.Active)
            {
                try
                {
                    scanner.Step();
                }
                catch (Exception ex)
                {
                    HandleScanFailure(scanner.Current, ex);
                    return;
                }

                if (!scanner.Active)
                {
                    if (discardScanResult)
                    {
                        discardScanResult = false;
                        scanner.ClearCurrent();
                        Emma3Log.Diagnostic("Discarded the in-flight scan result after a clean reset.");
                    }
                    else
                        OnScanComplete(scanner.Current);
                }
                else if (frame % 60 == 0 && planningActive && !backgroundScanActive)
                    PublishScanProgress(frame % 600 == 0);
                else if (frame % 600 == 0 && backgroundScanActive)
                    LogBackgroundScanProgress();
            }
            else if (departurePreparation != null && !departurePreparation.Complete)
                StepDeparturePreparation();
            else if (planningJob != null)
                PollPlanningJob();
            else if (planningActive)
                ContinuePlanning();
            else
                TryContinueBackgroundScan();
            try
            {
                AsteroidMap planningOre = planningActive && activePlan == null && !scanner.Active ? scanner.Current : null;
                visualizer.Draw(planningOre, activePlan ?? restoredVisualizationPlan, settings, settings.Visualization, completedCarrierRouteMission);
                PublishVisualizationDiagnostic();
            }
            catch (Exception ex)
            {
                if (frame >= nextVisualizationErrorFrame)
                {
                    nextVisualizationErrorFrame = frame + 600;
                    Exception root = ex.GetBaseException();
                    Emma3Log.Error("Visualization draw failed but planning will continue: " + root.GetType().Name + ": " + root.Message);
                }
            }
        }

        private static Sandbox.ModAPI.Ingame.IMyProgrammableBlock ResolveMinerPbEntity(long entityId)
        {
            VRage.ModAPI.IMyEntity entity;
            return MyAPIGateway.Entities.TryGetEntityById(entityId, out entity)
                ? entity as Sandbox.ModAPI.Ingame.IMyProgrammableBlock
                : null;
        }

        private bool IsAccessibleMinerPb(Sandbox.ModAPI.Ingame.IMyProgrammableBlock miner)
        {
            var terminal = miner as Sandbox.ModAPI.IMyTerminalBlock;
            var player = MyAPIGateway.Session?.Player;
            return terminal != null && !terminal.Closed && player != null
                && terminal.HasPlayerAccess(player.IdentityId)
                && blocks.MatchesMiner(miner);
        }

        private void PublishVisualizationDiagnostic()
        {
            if (!settings.Visualization)
                return;
            string summary = "Visualization active: lines=" + visualizer.LastLineCount
                + " ores=" + visualizer.LastOreSampleCount
                + " planes=" + visualizer.LastPlaneCount
                + " carrierRoute=" + visualizer.LastCarrierRouteCount
                + " minerRoute=" + visualizer.LastMinerRouteCount + (visualizer.LastSkipReason.Length == 0 ? "" : " skip=" + visualizer.LastSkipReason)
                + ".";
            if (summary == lastVisualizationDiagnostic && frame < nextVisualizationDiagnosticFrame)
                return;
            lastVisualizationDiagnostic = summary;
            nextVisualizationDiagnosticFrame = frame + 600;
            Emma3Log.Diagnostic(summary);
        }
    }
}
