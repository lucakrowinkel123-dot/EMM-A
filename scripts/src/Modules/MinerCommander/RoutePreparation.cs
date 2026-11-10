CanonicalRouteCompiler commanderRouteCompiler;
string pendingRouteRaw="",pendingRouteMission="",pendingRouteSettings="",pendingRouteId="";

void SubmitMinerPath(string mission)
{
    mission=mission??"";
    MyIni previous=new MyIni();
    if(mission.Length>0 && moduleOperation.Verb=="path" && moduleOperation.State=="acknowledged" && moduleOperation.Result=="path_complete"
        && previous.TryParse(moduleOperation.Payload) && previous.Get("Job","mission").ToString("")==mission) {
        if(moduleOperation.Result=="path_complete")SendPathCompleteBroadcast();
        return;
    }
    if(moduleOperation.State=="accepted" && moduleOperation.Verb=="path") {
        if(pilotPathLoaded && activeMissionId==mission) IGC.SendBroadcastMessage(minerProgressTag,"path_started|"+mission);
        else if(mission.Length>0 && previous.TryParse(moduleOperation.Payload) && previous.Get("Job","mission").ToString("")==mission)
            IGC.SendBroadcastMessage(minerProgressTag,"path_preparing|"+mission);
        return;
    }
    if(pendingRouteId.Length>0 && pendingRouteMission==mission) {
        if(mission.Length>0)IGC.SendBroadcastMessage(minerProgressTag,"path_preparing|"+mission);
        return;
    }
    if(moduleOperation.State=="accepted"||moduleOperation.State=="finished")return;
    IMyTimerBlock timer=FindPathTimer();
    if(timer==null) {ScriptLog("Miner path timer is missing.");return;}
    LoadCarrierMinerSettings();
    pendingRouteRaw=timer.CustomData??"";pendingRouteMission=mission;pendingRouteId=NextModuleId();
    MyIni settings=new MyIni();AddMinerSettings(settings);pendingRouteSettings=settings.ToString();
    commanderRouteCompiler=null;SaveState();
    if(mission.Length>0)IGC.SendBroadcastMessage(minerProgressTag,"path_preparing|"+mission);
}
void CancelCommanderRoute()
{
    if(pendingRouteId.Length==0)return;
    pendingRouteRaw=pendingRouteMission=pendingRouteSettings=pendingRouteId="";
    commanderRouteCompiler=null;SaveState();
}
void PumpCommanderRoute()
{
    if(pendingRouteId.Length==0)return;
    if(moduleOperation.State=="accepted" && moduleOperation.Verb=="path") {CancelCommanderRoute();return;}
    if(commanderRouteCompiler==null) {
        commanderRouteCompiler=new CanonicalRouteCompiler();
        commanderRouteCompiler.Begin(pendingRouteMission,pendingRouteRaw,pendingRouteId,pendingRouteSettings);
    }
    if(!commanderRouteCompiler.Pump())return;
    MyIni job=new MyIni();
    if(!job.TryParse(pendingRouteSettings)) {ScriptLog("Invalid pending route settings.");CancelCommanderRoute();return;}
    job.Set("Job","routeFormat",CanonicalRouteCompiler.Format);
    job.Set("Job","route",commanderRouteCompiler.Result);
    job.Set("Job","mission",pendingRouteMission);
    job.Set("Job","command","PathStart|"+pendingRouteMission);
    job.Set("Job","settingsRevision",pendingRouteId);
    if(SubmitMinerJob("path",job,false)) {
        foreach(string warning in commanderRouteCompiler.Warnings)ScriptLog(warning);
        CancelCommanderRoute();
    }
}
