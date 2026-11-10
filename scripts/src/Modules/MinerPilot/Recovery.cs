void LoadState()
{
    MyIni ini;
    if (!ReadStateIni(out ini)) { moduleRecoveryBlocked = !string.IsNullOrWhiteSpace(Storage); return; }
    LoadFullProgressVectorBaseline(ini);
    _hasProgressState = ReadProgressState();
    if (!RestoreModuleCheckpoint(ini)) { moduleRecoveryBlocked = !modulePairChanged; return; }
    LoadOwnedScalarFields(ini);
    carrierConnectorPos = carrierConnectorForward = carrierConnectorUp = Vector3D.Zero;
    carrierPoseAgeSeconds = 9999;
    RecoverPathRecordsAfterLoad(); RecoverAreaLanesAfterLoad();
    RecoverDockPoseAfterLoad(); RecoverDerivedRoutesAfterLoad(); RecoverPathFacingAfterLoad();
    if (autoServiceState == AutoServiceState.WaitingService || autoServiceState == AutoServiceState.Undocking
        || autoServiceState == AutoServiceState.ReturningToWork) pathPaused = true;
    // Readiness is a fresh Commander decision after every reload.
    moduleServiceReady = false; oreTransferSucceeded = false; moduleDeployDecisionReady = false;
    if (hangarState == HangarState.PostDeployCheck) moduleDeployRequest = ++moduleSequence;
    if(modulePathLoadPending && moduleOperation.State=="accepted" && moduleOperation.Verb=="path") {
        MyIni accepted=new MyIni();if(accepted.TryParse(moduleOperation.Payload))StartPathFromTimer(accepted.Get("Job","mission").ToString(""));
        else moduleRecoveryBlocked=true;
    }
    fullStateDirty = false; progressStateDirty = false;
}

void WriteRoleModuleState(MyIni ini)
{
    ini.Set("Module","lastStop",moduleLastStop);
    ini.Set("Module","pathLoadPending",modulePathLoadPending);
    ini.Set("Module", "route", moduleRoutePayload);
    ini.Set("Module", "dockGeneration", moduleDockGeneration);
    ini.Set("Module", "dockOther", moduleDockOther);
    ini.Set("Module", "wasDocked", moduleWasDocked);
    ini.Set("Module", "unloadRequest", moduleUnloadRequest);
    ini.Set("Module", "deployRequest", moduleDeployRequest);
    ini.Set("Module", "lastControl", moduleLastControl);
    ini.Set("Module", "pathComplete", modulePathComplete);
}
void ReadRoleModuleState(MyIni ini)
{
    moduleLastStop=ini.Get("Module","lastStop").ToString("");
    modulePathLoadPending=ini.Get("Module","pathLoadPending").ToBoolean(false);
    moduleRoutePayload = ini.Get("Module", "route").ToString("");
    moduleDockGeneration = ini.Get("Module", "dockGeneration").ToInt64(0);
    moduleDockOther = ini.Get("Module", "dockOther").ToInt64(0);
    moduleWasDocked = ini.Get("Module", "wasDocked").ToBoolean(false);
    moduleUnloadRequest = ini.Get("Module", "unloadRequest").ToInt64(0);
    moduleDeployRequest = ini.Get("Module", "deployRequest").ToInt64(0);
    moduleLastControl = ini.Get("Module", "lastControl").ToString("");
    modulePathComplete = ini.Get("Module", "pathComplete").ToBoolean(false);
}
