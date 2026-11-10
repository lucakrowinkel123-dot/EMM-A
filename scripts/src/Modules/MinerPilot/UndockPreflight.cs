long modulePreflightRequest, modulePreflightGeneration, modulePreflightOther;
bool modulePreflightReady;

void BeginUndockPreflight()
{
    modulePreflightRequest = ++moduleSequence;
    modulePreflightGeneration = moduleDockGeneration;
    modulePreflightOther = moduleDockOther;
    modulePreflightReady = false;
    SaveState(); PublishModuleStatus();
}

bool HasFreshUndockPreflight()
{
    if (modulePreflightGeneration != moduleDockGeneration || modulePreflightOther != moduleDockOther)
    {
        BeginUndockPreflight();
        return false;
    }
    return ModuleReady() && moduleTick-moduleSupplyTick <= 180 && modulePreflightReady
        && cargoDetailsCurrent && _cachedCargoPercent >= 0 && _cachedCargoPercent <= 100
        && cachedMinerHydrogenPercent >= 0 && cachedMinerHydrogenPercent <= 100;
}

void ReadUndockPreflight(MyIni data)
{
    modulePreflightReady = modulePreflightRequest != 0
        && data.Get("Supply", "preflightRequest").ToInt64(0) == modulePreflightRequest
        && data.Get("Supply", "preflightGeneration").ToInt64(-1) == modulePreflightGeneration
        && data.Get("Supply", "other").ToInt64(-1) == modulePreflightOther
        && data.Get("Supply", "mission").ToString("") == activeMissionId
        && data.Get("Supply", "preflightReady").ToBoolean(false);
}
