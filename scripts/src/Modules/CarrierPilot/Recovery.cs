void LoadState()
{
    MyIni ini;
    if (!ReadStateIni(out ini)) { moduleRecoveryBlocked = !string.IsNullOrWhiteSpace(Storage); return; }
    LoadCarrierProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    if (!RestoreModuleCheckpoint(ini)) { moduleRecoveryBlocked = !modulePairChanged; return; }
    LoadOwnedScalarFields(ini);
    LoadCarrierTurnCalibration(ini);
    RecoverEmma3CarrierRouteAfterLoad();
    RecoverPathRecordsAfterLoad();
    progressStateDirty = false;
}

void RecoverEmma3CarrierRouteAfterLoad()
{
    if (moduleOperation.Verb != "route") return;
    int index = emma3CarrierRouteIndex;
    bool aligning = emma3CarrierParkAligning;
    MyIni route = new MyIni();
    if (!route.TryParse(moduleOperation.Payload) || !LoadEmma3ReadyPlan(route)
        || index < 0 || index > emma3CarrierRoutePositions.Count)
    { moduleRecoveryBlocked = true; return; }
    emma3CarrierRouteIndex = index;
    emma3CarrierParkAligning = aligning;
    emma3MissionState = moduleOperation.State == "accepted" ? Emma3MissionState.FlyingCarrierRoute : Emma3MissionState.WaitingStationary;
    RestoreEmma3CarrierParkFacing();
}

void WriteRoleModuleState(MyIni ini)
{
    ini.Set("Module","lastStop",moduleLastStop);
    ini.Set("Module", "lastControl", pilotLastControl);
    ini.Set("Module","uiLastInputId",uiLastInputId);ini.Set("Module","uiLastReply",uiLastReply);
    MyIni draft=new MyIni();if(carrierUi!=null)carrierUi.WriteDraft(draft);
    ini.Set("Module","uiDraft",carrierUi==null?uiSavedDraft:draft.ToString());
}
void ReadRoleModuleState(MyIni ini)
{
    moduleLastStop=ini.Get("Module","lastStop").ToString("");
    uiLastInputId=ini.Get("Module","uiLastInputId").ToString("");uiLastReply=ini.Get("Module","uiLastReply").ToString("");
    uiSavedDraft=ini.Get("Module","uiDraft").ToString("");
    pilotLastControl = ini.Get("Module", "lastControl").ToString("");
}
