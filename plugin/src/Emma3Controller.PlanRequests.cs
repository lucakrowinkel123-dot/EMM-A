using System;
using System.IO;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private PlanRequestIdentity acceptedPlanRequest;
        private PlannerResponseJournal plannerReplies;
        private PlannerResponseJournal PlannerReplies
        {
            get { return plannerReplies ?? (plannerReplies = new PlannerResponseJournal(Path.Combine(userDataPath, "Storage", "EMMA3", "planner-replies"))); }
        }
        private bool CurrentPlanningRequest(BridgeDocument bridge)
        { return acceptedPlanRequest == null || acceptedPlanRequest.Matches(bridge); }
        private void RememberPlanningResponse(BridgeDocument bridge)
        {
            if (acceptedPlanRequest != null && acceptedPlanRequest.Matches(bridge))
            { acceptedPlanRequest.Stamp(bridge); PlannerReplies.Remember(acceptedPlanRequest, bridge); }
        }
        private void HandlePlanningRequest(BridgeDocument bridge, bool replayOnly = false)
        {
            var request = new PlanRequestIdentity(bridge);
            if (acceptedPlanRequest != null && request.Sequence == acceptedPlanRequest.Sequence && !acceptedPlanRequest.Matches(bridge))
            { bridge.Set("state", "error"); bridge.Set("message", "Conflicting intent for an accepted planning sequence"); request.Stamp(bridge); return; }
            try
            {
                if (PlannerReplies.Replay(request, bridge))
                {
                    if (bridge.Get("state") == "ready" || bridge.Get("state") == "ready_dryrun")
                    {
                        int count = bridge.GetInt("fleetAssignmentCount");
                        // Carrier transit is fully contained in this reply. Its reused bridge
                        // may still describe the completed mining fleet, whose routes are not
                        // dependencies of the carrier-only result (and may use pre-remap IDs).
                        if (request.Kind != "carrier" && count > 0 && !LoadFleetPublication(bridge.Get("fleetAssignmentMission"), count))
                            throw new InvalidDataException("Committed planning reply has no matching fleet-route artifacts");
                    }
                    if (bridge.Get("state") == "ready" || bridge.Get("state") == "ready_dryrun" || bridge.Get("state") == "error"
                        || planningActive || planningJob != null)
                    { acceptedPlanRequest = request; return; }
                }
                if (replayOnly) return;
                if (acceptedPlanRequest != null && acceptedPlanRequest.Matches(bridge) && (planningActive || planningJob != null))
                { request.Stamp(bridge); bridge.Set("state", "planning"); bridge.Set("message", "Planning request remains active"); return; }
                acceptedPlanRequest = request;
                BeginPlanning(bridge);
                RememberPlanningResponse(bridge);
            }
            catch (Exception ex)
            {
                bridge.Set("state", "error"); bridge.Set("message", "Planning response recovery held: " + ex.Message); request.Stamp(bridge);
            }
        }
        private void RetryPlanningState(BridgeDocument bridge)
        {
            if (bridge.GetInt("request") != handledRequest || !CurrentPlanningRequest(bridge)) return;
            string state = bridge.Get("state");
            if (acceptedPlanRequest == null && state == "ready_dryrun" && settings.AllowMovement && bridge.Get("planIntent").Length > 0)
            { HandlePlanningRequest(bridge, true); return; }
            if (bridge.GetBool("autonomousMining") && (state == "waiting_replication" || (state == "producing" && frame >= nextProductionPlanCheck)))
            { nextProductionPlanCheck = frame + 300; HandlePlanningRequest(bridge); }
            if (activePlan == null && acceptedPlanRequest != null && bridge.Get("state") == "ready_dryrun" && settings.AllowMovement)
                HandlePlanningRequest(bridge);
        }
    }
}
