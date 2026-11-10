long pilotPreflightRequest, checkedPreflightRequest, checkedPreflightGeneration;
int preflightCargoBaseline;
bool preflightReady;

void UpdateCommanderUndockPreflight()
{
    if (pilotPreflightRequest != checkedPreflightRequest || pilotDockGeneration != checkedPreflightGeneration)
    {
        checkedPreflightRequest = pilotPreflightRequest;
        checkedPreflightGeneration = pilotDockGeneration;
        preflightCargoBaseline = cargoDetailCompletedCycles;
        preflightReady = false;
        if (checkedPreflightRequest != 0) ResetMinerCargoStatsScan();
    }
    if (checkedPreflightRequest != 0 && !preflightReady && cargoDetailsCurrent
        && cargoDetailCompletedCycles > preflightCargoBaseline && _autoServiceCargoBlocks.Count > 0)
    {
        RefreshMinerHydrogenCache();
        preflightReady = true;
        PublishCommanderSupply();
    }
}

void AddUndockPreflightSupply(MyIni data)
{
    long other = IsMinerDocked() && minerConnector.OtherConnector != null ? minerConnector.OtherConnector.EntityId : 0;
    data.Set("Supply", "preflightRequest", checkedPreflightRequest);
    data.Set("Supply", "preflightGeneration", checkedPreflightGeneration);
    data.Set("Supply", "preflightReady", preflightReady && pilotPreflightRequest == checkedPreflightRequest
        && pilotDockGeneration == checkedPreflightGeneration && other == pilotDockOther);
}
