"""One-time migration helper for the explicit Miner role entry points."""
from pathlib import Path
import json
root = Path(__file__).resolve().parents[1]
library = json.loads((root/'generated/Miner.library.json').read_text(encoding='utf-8'))
def member(name):
    found = [m['source'] for m in library if m['name'] == name]
    assert len(found) == 1, name
    return found[0].replace('\r\n', '\n')
def replace(text, old, new):
    assert text.count(old) == 1, old
    return text.replace(old, new)
out = root/'src/Modules/MinerPilot'
out.mkdir(parents=True, exist_ok=True)
ctor = member('Program')
for line in ('    RefreshMinerCargoStatsBlocks();\n', '    CacheOreTransferBlocks();\n',
             '    PublishMinerPlannerSettings();\n', '    carrierStatusListener = IGC.RegisterBroadcastListener(carrierStatusTag);\n',
             '    minerCommandListener = IGC.RegisterBroadcastListener(minerCommandTag);\n'):
    ctor = replace(ctor, line, '')
ctor = replace(ctor, '    LoadState();', '    LoadState();\n    InitializeModule();')
ctor = ctor.replace('Miner script v0.8.41;', 'Miner Pilot v0.8.42;')
ctor = replace(ctor, '    Runtime.UpdateFrequency = scriptPaused ? UpdateFrequency.None : tickSpeed;', '    Runtime.UpdateFrequency = UpdateFrequency.Update1;')
main = member('Main')
main = replace(main, '        minerHeavyWorkClaimed = false;', '        UpdateModule();\n        TrackModuleDockGeneration();\n        if (!ModuleReady() || moduleRecoveryBlocked)\n        {\n            ResetMovement(); SetDrills(false); runtime++; return;\n        }\n        minerHeavyWorkClaimed = false;')
start = main.index('        UpdatePluginServiceAges();')
end = main.index('        UpdateRouteMeterProgress();', start)
main = main[:start] + main[end:]
start = main.index('        if (dockPosePollTicks > 0)')
end = main.index('        if (pathLoadJob != null)', start)
main = main[:start] + main[end:]
main = main.replace('Runtime.UpdateFrequency = UpdateFrequency.None;', 'Runtime.UpdateFrequency = UpdateFrequency.Update1;')
start = main.index('        UpdateFinalUnloadRecovery();')
end = main.index('        UpdateIndicatorLightThrottled();', start)
main = main[:start] + main[end:]
main = replace(main, '        BroadcastMinerStatus();', '        UpdatePilotOperationCompletion();')
main = replace(main, '        RemoteCommand(argument);', '        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase)) PilotEmergencyStop();')
remote = member('RemoteCommand')
remote = replace(remote, 'void RemoteCommand(string command)', 'void PilotRemoteCommand(string command)')
remote = remote.replace('Runtime.UpdateFrequency = UpdateFrequency.None;', 'Runtime.UpdateFrequency = UpdateFrequency.Update1;')
(out/'EntryPoints.cs').write_text(ctor+'\n'+main+'\n'+remote, encoding='utf-8')
# All cross-ship progress is emitted by Commander. The Pilot reports local events.
events=[]
for name in ('BroadcastMinerProgress','SendRecallProgress','SendHangarProgress','SendPathStartAccepted'):
    text=member(name).replace('IGC.SendBroadcastMessage(', 'EmitMinerEvent(')
    events.append(text)
recall=member('CompleteRecallPath').replace('IGC.SendBroadcastMessage(', 'EmitMinerEvent(')
events.append(recall)
(out/'Events.cs').write_text('\n'.join(events), encoding='utf-8')
# Service readiness belongs to Commander; Pilot retains the entire navigation
# sequence for the trip to/from service and the final hangar approach.
hangar=member('UpdateHangarState')
start=hangar.index('        if (oreTransferStartDelayTicks > 0')
end=hangar.index('        StartUndock();',start)
hangar=hangar[:start]+'        if (!HasModuleServicePermit()) return;\n'+hangar[end:]
start=hangar.index('        if (oreTransferStartDelayTicks > 0')
end=hangar.index('        hangarState = HangarState.None;',start)
hangar=hangar[:start]+'        if (!HasModuleServicePermit()) return;\n'+hangar[end:]
hangar=replace(hangar,'        if (!cargoDetailsCurrent || cargoDetailCompletedCycles <= hangarPostDeployCargoCycleBaseline)', '        if (!moduleDeployDecisionReady)')
service=member('UpdateAutoServiceState')
start=service.index('        if (autoServiceReturnRouteBuildPending ||')
end=service.index('        RetryAutoServiceUndock();',start)
service=service[:start]+'        if (autoServiceReturnRouteBuildPending || !HasModuleServicePermit()) return;\n'+service[end:]
deploy=member('FinishHangarPostDeployCheck')
start=deploy.index('    RefreshMinerHydrogenCache();')
end=deploy.index('    hangarBroadcastTicks = 0;',start)
deploy=deploy[:start]+'    if (!moduleDeployDecisionReady) return;\n    bool needsService = moduleDeployNeedsService;\n'+deploy[end:]
(out/'ServiceNavigation.cs').write_text(hangar+'\n'+service+'\n'+deploy,encoding='utf-8')
# Plugin performs hardware jobs on behalf of Pilot but replies through Commander.
dock=member('PublishDockHardwareRequest').replace('Me.EntityId', '(modulePeer == null ? 0 : modulePeer.EntityId)')
(out/'DockHardware.cs').write_text(dock, encoding='utf-8')
