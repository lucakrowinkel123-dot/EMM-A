int moduleHoldCargoBaseline,moduleNextCompleteBroadcast;

void UpdateCommanderUnload()
{
    bool connected = IsMinerDocked() && minerConnector.OtherConnector != null
        && minerConnector.OtherConnector.EntityId == pilotDockOther;
    if (!connected || pilotUnloadRequest == 0 || acceptedUnloadGeneration != pilotDockGeneration)
    {
        if (oreTransferActive || oreTransferSucceeded) CancelMinerUnload();
        acceptedUnloadRequest = 0;
        acceptedUnloadGeneration = pilotDockGeneration;
    }
    if (connected && minerDockPowerReady && pilotUnloadRequest != 0
        && acceptedUnloadRequest != pilotUnloadRequest)
    {
        CancelMinerUnload();
        ScheduleOreTransfer();
        if (oreTransferActive)
        {
            acceptedUnloadRequest = pilotUnloadRequest;
            acceptedUnloadGeneration = pilotDockGeneration;
            SaveState();
        }
    }
    if (oreTransferActive) UpdateOreTransfer();
    if (pilotPathComplete && CanConfirmFinalUnload()) SendPathCompleteBroadcast();
}

bool CanConfirmFinalUnload()
{
    return ModuleReady() && IsMinerDocked() && minerConnector.OtherConnector != null
        && minerConnector.OtherConnector.EntityId == pilotDockOther
        && acceptedUnloadGeneration == pilotDockGeneration && acceptedUnloadRequest == pilotUnloadRequest
        && acceptedUnloadRequest != 0 && oreTransferSucceeded && !oreTransferActive;
}

void SendPathCompleteBroadcast()
{
    if (CanConfirmFinalUnload() && moduleTick>=moduleNextCompleteBroadcast) {
        moduleNextCompleteBroadcast=moduleTick+60;
        IGC.SendBroadcastMessage(minerPathCompleteTag, "PathComplete|" + activeMissionId + "|1");
    }
}

void UpdateCommanderServicePolicy()
{
    UpdateCommanderUndockPreflight();
    if (pilotDeployRequest != 0 && pilotDeployRequest != checkedDeployRequest)
    {
        checkedDeployRequest = pilotDeployRequest;
        deployCargoBaseline = cargoDetailCompletedCycles;
        deployReady = false;
        ResetMinerCargoStatsScan(); RefreshMinerHydrogenCache();
    }
    if (checkedDeployRequest != 0 && !deployReady && cargoDetailsCurrent
        && cargoDetailCompletedCycles > deployCargoBaseline && _autoServiceCargoBlocks.Count > 0)
    {
        deployNeedsService = HangarPostDeployNeedsService(true, _cachedCargoPercent, autoServiceCargoTriggerPercent,
            HasMinerHydrogenTank(), HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 100, autoServiceHydrogenTriggerPercent);
        deployReady = true;
        PublishCommanderSupply();
    }
    if (pilotStoneHold) stoneEjectionHoldTicks++;
    else stoneEjectionHoldTicks = 0;
    if (autoServiceCheckTicks > 0) { autoServiceCheckTicks--; return; }
    autoServiceCheckTicks = autoServiceCheckIntervalTicks;
    if (pendingPilotControl.Length > 0 || IsMinerDocked()) return;
    double hydrogen = HasMinerHydrogenTank() ? GetMinerHydrogenPercent() : 100;
    bool travelling = pilotServiceEligible || autoServiceState == AutoServiceState.Returning
        || autoServiceState == AutoServiceState.ReturningToWork || autoServiceState == AutoServiceState.Undocking;
    if (travelling && stopAtOnePercentFuel && hydrogen <= 1)
    { QueuePilotControl("stop"); ScriptLog("Safety stop: fuel reached 1%."); return; }
    if (pilotStoneHold)
    {
        bool detail = cargoDetailsCurrent && cargoDetailCompletedCycles > moduleHoldCargoBaseline;
        if (_cachedCargoPercent < stoneEjectionCargoResumePercent)
            QueuePilotControl("endhold");
        else if (detail && _cachedStonePercent <= stoneEjectionResumePercent || stoneEjectionHoldTicks >= stoneEjectionMaxHoldTicks)
        {
            if (_cachedCargoPercent >= autoServiceCargoTriggerPercent) QueuePilotControl("service:cargo after stone ejection");
            else QueuePilotControl("endhold");
        }
        return;
    }
    if (!autoServiceReturnEnabled || !pilotServiceEligible) return;
    if (cargoDetailsCurrent && _cachedCargoPercent >= stoneEjectionCargoTriggerPercent && _cachedStonePercent > stoneEjectionTriggerPercent)
    {
        moduleHoldCargoBaseline = cargoDetailCompletedCycles;
        QueuePilotControl("hold");
    }
    else if (cargoDetailsCurrent && _cachedCargoPercent >= autoServiceCargoTriggerPercent) QueuePilotControl("service:cargo");
    else if (hydrogen <= autoServiceHydrogenTriggerPercent) QueuePilotControl("service:fuel");
}

void PublishCommanderSupply()
{
    MyIni data = new MyIni();
    data.Set("Supply", "cargo", _cachedCargoPercent); data.Set("Supply", "stone", _cachedStonePercent);
    data.Set("Supply", "cargoValid", cargoDetailsCurrent && _autoServiceCargoBlocks.Count > 0);
    data.Set("Supply", "cycles", cargoDetailCompletedCycles);
    data.Set("Supply", "hydrogen", GetMinerHydrogenPercent());
    data.Set("Supply", "tanks", cachedMinerHydrogenTankCount);
    data.Set("Supply", "inventoryIds", minerUnloadSourceIds);
    data.Set("Supply", "generation", acceptedUnloadGeneration); data.Set("Supply", "request", acceptedUnloadRequest);
    data.Set("Supply", "mission", activeMissionId);
    data.Set("Supply","other",pilotDockOther);
    data.Set("Supply", "unloaded", CanConfirmFinalUnload());
    data.Set("Supply", "serviceReady", cargoDetailsCurrent && _autoServiceCargoBlocks.Count > 0
        && _cachedCargoPercent <= autoServiceCargoResumePercent
        && (!HasMinerHydrogenTank() || GetMinerHydrogenPercent() >= autoServiceHydrogenResumePercent));
    data.Set("Supply", "deployRequest", checkedDeployRequest); data.Set("Supply", "deployReady", deployReady);
    data.Set("Supply", "deployNeedsService", deployNeedsService);
    AddUndockPreflightSupply(data);
    SendModule("supply", data.ToString());
}

bool NeedsMinerCargoDetails()
{
    return pilotPreflightRequest != 0 && !preflightReady || pilotDeployRequest != 0 && !deployReady || pilotStoneHold || IsMinerDocked()
        || _cachedCargoPercent >= Math.Max(0, Math.Min(autoServiceCargoTriggerPercent, stoneEjectionCargoTriggerPercent) - 10);
}
