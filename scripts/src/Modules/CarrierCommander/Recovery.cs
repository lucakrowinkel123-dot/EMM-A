void LoadState()
{
    MyIni ini;
    if (!ReadStateIni(out ini)) return;
    LoadCarrierProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    RestoreModuleCheckpoint(ini);
    if (modulePairChanged)
    {
        // Only behavior settings cross a rename. Never replay the previous pair's jobs.
        MyIni settings = new MyIni();
        foreach (int index in new int[] {20,21,22,23,24,25,26,27,28,29,30,48,49,50,51,52,53})
        {
            string key = _carrierSaveKeys[index];
            settings.Set(StateIniSection, key, StateString(ini, key, ""));
        }
        ini = settings; _hasProgressState = false;
        ScriptLog("Ship names changed; automation stopped.");
    }
    LoadOwnedScalarFields(ini);
    if (ActiveMiner().PathCompleteReceived && string.IsNullOrWhiteSpace(ActiveMiner().PathCompleteKind))
        ActiveMiner().PathCompleteKind = "path";
    if (!RecallStateMatchesActiveMission()) ResetMinerRecallState();
    LoadMiningReports(ini);
    if (!moduleRestored)
    {
        fullAutoEnabled = false;
        emma3MissionState = Emma3MissionState.None;
        manualHangarCycleState = ManualHangarCycleState.None;
        ActiveMiner().HangarManagerPending = HangarManagerPendingCommand.None;
        ScriptLog("Four-module upgrade: settings retained; AM starts stopped.");
    }
    RecoverEmma3CarrierRouteAfterLoad();
    progressStateDirty = false;
}

void WriteRoleModuleState(MyIni ini)
{
    ini.Set("Module", "pendingControl", pilotControlPending);
    ini.Set("Module","uiPendingId",uiPendingId);ini.Set("Module","uiPendingInput",uiPendingInput);
}

void ReadRoleModuleState(MyIni ini)
{
    uiPendingId=ini.Get("Module","uiPendingId").ToString("");uiPendingInput=ini.Get("Module","uiPendingInput").ToString("");
    pilotControlPending = ini.Get("Module", "pendingControl").ToString("");
}
