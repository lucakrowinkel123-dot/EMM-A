void LoadState()
{
    MyIni ini;
    if (!ReadStateIni(out ini)) return;
    LoadFullProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    RestoreModuleCheckpoint(ini);
    if (modulePairChanged)
    {
        // Only behavior settings cross a rename. Never replay the previous pair's jobs.
        MyIni settings = new MyIni();
        foreach (int index in new int[] {12,13,14,15,16,17,18,19,20,22,23,65,66,67,89})
        {
            string key = _minerSaveKeys[index];
            settings.Set(StateIniSection, key, StateString(ini, key, ""));
        }
        stopAtOnePercentFuel = (_hasProgressState ? _progressIni : ini).Get("Module", "stopAtOnePercent").ToBoolean(true);
        ini = settings; _hasProgressState = false;
        ScriptLog("Ship names changed; automation stopped.");
    }
    LoadOwnedScalarFields(ini);
    // Unload acknowledgement must be re-established against this physical dock.
    oreTransferActive = false; oreTransferSucceeded = false; oreTransferStartDelayTicks = 0;
    acceptedUnloadRequest = 0;
    if (!moduleRestored) { scriptPaused = false; activeMissionId = ""; }
    fullStateDirty = true;
}

void WriteRoleModuleState(MyIni ini)
{
    ini.Set("Module","pendingRaw",pendingRouteRaw);ini.Set("Module","pendingMission",pendingRouteMission);
    ini.Set("Module","pendingSettings",pendingRouteSettings);ini.Set("Module","pendingRouteId",pendingRouteId);
    ini.Set("Module", "control", pendingPilotControl);
    ini.Set("Module","controlQueue",string.Join("\n",pilotControlQueue.ToArray()));
    ini.Set("Module", "controlId", pilotControlId);
    ini.Set("Module", "stopAtOnePercent", stopAtOnePercentFuel);
}
void ReadRoleModuleState(MyIni ini)
{
    foreach(string queued in ini.Get("Module","controlQueue").ToString("").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries))if(pilotControlQueue.Count<32)pilotControlQueue.Enqueue(queued);
    pendingRouteRaw=ini.Get("Module","pendingRaw").ToString("");pendingRouteMission=ini.Get("Module","pendingMission").ToString("");
    pendingRouteSettings=ini.Get("Module","pendingSettings").ToString("");pendingRouteId=ini.Get("Module","pendingRouteId").ToString("");
    pendingPilotControl = ini.Get("Module", "control").ToString("");
    pilotControlId = ini.Get("Module", "controlId").ToString("");
    stopAtOnePercentFuel = ini.Get("Module", "stopAtOnePercent").ToBoolean(true);
}
