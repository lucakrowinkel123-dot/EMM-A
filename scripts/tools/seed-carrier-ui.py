"""One-time migration of the existing menu presentation into the Carrier Pilot."""
from pathlib import Path
import json,re
root=Path(__file__).resolve().parents[1]
analysis=root.parents[1]/'split-0842-analysis'
spec=json.loads((analysis/'Carrier-UI-extraction.json').read_text())
library=json.loads((root/'generated/Carrier.library.json').read_text())
members={m['name']:m['source'] for m in library}
def clean(s): return re.sub(r'^\s*#(?:end)?region.*$', '',s,flags=re.M)
methods=[clean(members[n]) for n in spec['moveMethods']]
fields=[clean(members[f['name']]) for f in spec['fields']]
fields += ['bool carrierLcdConfigured;','MinerSlot uiMiner = new MinerSlot();','string uiMission = "";','string uiActions = "";']
primitives={'int':'ToInt32','long':'ToInt64','double':'ToDouble','bool':'ToBoolean','string':'ToString'}
excluded={'runtime','pluginLcdMainText','pluginLcdReportText','lcdRefreshMinTicks','minerContactTimeoutSeconds'}
snapshot=[f for f in spec['fields'] if f['uiKind']=='COMMANDER_SNAPSHOT_OR_SHARED_SETTING' and f['name'] not in excluded and not f['name'].startswith('_') and (f['type'] in primitives or f['type'].endswith('Emma3MissionState') or f['type'].endswith('ManualHangarCycleState'))]
editable=set('maxSpeed arrivalTolerance orientationToleranceDeg emma3SearchRadius emma3ParkingHeight emma3PlaneClearance emma3TravelClearance emma3Visualization emma3AllowMovement emma3ScriptLogging emma3AllowSeatExitDuringMining emma3IceReserveEnabled emma3IceReserveMillionLiters emma3PluginOreTransfer emma3PluginPowerTransitions emma3PluginRemoteLink emma3PluginLcdOutput emma3ItemsMode emma3EnabledItems minerStandardDepth minerLaneWidth minerLaneHeight minerForwardSpeed minerReturnSpeed minerAutoServiceEnabled minerAutoServiceCargoPercent minerAutoServiceHydrogenPercent minerStopAtOnePercentFuel minerRouteStartExtra minerMergeAssistedDocking minerHangarEnabled shortRangeDistanceMeters cruiseOffsetMeters thrustLimitKn thrustUsePercent accelerationLimit turnTestEnabled lcdRefreshIntervalTicks finishAndHold timerPersistenceEnabled'.split())
def getter(f,section='Ui',target=None):
 n=f['name'];typ=f['type'];dest=target or n
 if typ in primitives:return f'{dest}=ini.Get("{section}","{n}").{primitives[typ]}({dest});'
 return f'{dest}=({typ.split(".")[-1]})ini.Get("{section}","{n}").ToInt32((int){dest});'
def setter(f,section='Ui',target=None):
 n=f['name'];v=target or n
 if f['type'] not in primitives:v='(int)'+v
 return f'ini.Set("{section}","{n}",{v});'
minerfields=[]
for typ,name in re.findall(r'public (string|int|double|bool|HangarManagerPendingCommand) (\w+)',members['MinerSlot']):
 if any(re.search(r'\b(?:miner|ActiveMiner\(\))\.'+name+r'\b',m) for m in methods):minerfields.append({'name':name,'type':typ})
# Enums in the miner snapshot have stable numeric wire values.
for f in minerfields:
 if f['type']=='HangarManagerPendingCommand': f['type']='IngameScript.Program.HangarManagerPendingCommand'
draft=[f for f in spec['fields'] if f['uiKind']=='UI_LOCAL_OR_DRAFT' and (f['name'].startswith('ca') and f['name'] not in {'carrierLcdName','carrierRemoteName','carrierPbTag','carrierLcdLastText','carrierLcdLastNativeText','carrierLcdStateFingerprint'} or f['name']=='_carrierOreMoveOriginalRaw') and f['type'] in primitives]
draft.append({'name':'caLcdMenu','type':'IngameScript.Program.CaLcdMenu'})
ui='''// The UI owns presentation and drafts. Commander authorizes every mission action.
class CarrierUi {
readonly Program uiHost;
IMyProgrammableBlock Me { get { return uiHost.Me; } }
IMyGridTerminalSystem GridTerminalSystem { get { return uiHost.GridTerminalSystem; } }
IMyGridProgramRuntimeInfo Runtime { get { return uiHost.Runtime; } }
void Echo(string text) { uiHost.Echo(text); }
public CarrierUi(Program host) {
 uiHost=host; rc=host.rc;
 carrierLcd=GridTerminalSystem.GetBlockWithName(carrierLcdName) as IMyTextPanel;
 reportLcd=GridTerminalSystem.GetBlockWithName("EMM-A Ca LCD Report") as IMyTextPanel;
 carrierResourcesTimer=GridTerminalSystem.GetBlockWithName("EMM-A Ca Resources") as IMyTimerBlock;
 itemsTimer=GridTerminalSystem.GetBlockWithName("EMM-A Ca Items") as IMyTimerBlock;
 hangarBlock=GridTerminalSystem.GetBlockWithName(hangarBlockName);
 LoadCarrierItems(); InitializeReportLcd(); RefreshCachedCarrierDimensions();
}
'''+ '\n'.join(fields+methods)
facades={
'TryClaimCarrierHeavyWork':'return uiHost.TryClaimCarrierHeavyWork();',
'ActiveMiner':'return uiMiner;',
'ScriptLog':'uiHost.ScriptLog(text);',
'ClearScriptLogQueue':'Intent("clearlog");',
'PluginLcdTakeoverActive':'return emma3PluginLcdOutput && pluginLcdReady && pluginLcdHeartbeatAgeTicks <= 720;',
'UpdatePluginLcdPayload':'pluginLcdMainText=main??"";pluginLcdReportText=report??"";',
'ConfigureCarrierLcd':re.search(r'\{(.*)\}',members['ConfigureCarrierLcd'],re.S)[1],
'SendCarrierStatusNow':'Intent("status");',
'SetFullAutoEnabledFromUi':'Intent(enabled ? "am:on" : "am:off");',
'RecallStateMatchesActiveMission':'return uiMission.Length>0 && uiMiner.RecallMissionId==uiMission;',
'StartMinerRecall':'Intent("recall");',
'PublishCarrierItems':'Intent("items");',
'GetEffectiveThrustInDirection':'return uiHost.GetEffectiveThrustInDirection(direction);',
'GetDetectedMaxCarrierThrustN':'return uiHost.GetDetectedMaxCarrierThrustN();',
'GetControlledPosition':'return uiHost.GetControlledPosition();',
'FindEmma3BridgeTimer':'return GridTerminalSystem.GetBlockWithName(emma3BridgeTimerName) as IMyTimerBlock;',
'FindOreTimer':'return GridTerminalSystem.GetBlockWithName(oreNameTimerTag) as IMyTimerBlock;',
'WriteEmma3Settings':'Intent("settings");',
'WriteMinerBehaviorSettings':'Intent("settings");',
'RequestEmma3Continue':'Intent("continue");',
'RequestEmma3Reset':'Intent("reset");',
'IsCruiseTurnMeasurementValid':'return uiHost.IsCruiseTurnMeasurementValid();',
'TryParseNumber':'return uiHost.TryParseNumber(text,out value);',
'SetTimerPersistence':'timerPersistenceEnabled=enabled;Intent("settings");',
'MarkProgressStateDirty':'Intent("settings");'}
for f in json.loads((analysis/'CarrierUi.facades.json').read_text()):ui+='\n'+f['signature']+' { '+facades[f['name']]+' }\n'
ui+='''
void Intent(string action) { uiActions += action+"\\n"; }
public void ReadSnapshot(MyIni ini) {
'''+ '\n'.join(getter(f) for f in snapshot)+'\n'+ '\n'.join(getter(f,'UiMiner','uiMiner.'+f['name']) for f in minerfields)+'''
 uiMission=ini.Get("Ui","mission").ToString("");
 asteroidReport=MiningReportData.Decode(ini.Get("Ui","asteroidReport").ToString(""));
 miningTotals=MiningReportData.Decode(ini.Get("Ui","miningTotals").ToString(""));
 emma3CarrierRoutePositions.Clear();
 int count=ini.Get("Ui","routeCount").ToInt32(0);
 for(int i=0;i<Math.Min(count,1024);i++)emma3CarrierRoutePositions.Add(Vector3D.Zero);
 targetPosition=uiHost.targetPosition; _tickRcMatrix=uiHost._tickRcMatrix;
}
public void Render() {
 runtime=uiHost.runtime; _tickRcMatrix=uiHost._tickRcMatrix;
 UpdateCarrierInfoCache(); UpdateCarrierLcd(); UpdateReportLcd();
 uiHost.SendUiDisplay(pluginLcdMainText,pluginLcdReportText);
}
public string Input(string command) {
 MyIni before=new MyIni();WriteEditable(before);
 uiActions="";
 if(command.StartsWith("menu:",StringComparison.Ordinal)) OpenCarrierMenu((CaLcdMenu)int.Parse(command.Substring(5)));
 else TryHandleCarrierLcdCommand(command);
 MyIni ini=new MyIni();WriteEditable(ini);
 List<MyIniKey> keys=new List<MyIniKey>();ini.GetKeys(keys);
 foreach(MyIniKey key in keys) if(before.Get(key).ToString()==ini.Get(key).ToString()) ini.Delete(key);
 ini.Set("UiReply","actions",uiActions);return ini.ToString();
}
void WriteEditable(MyIni ini) {
'''+ '\n'.join(setter(f,'UiEdit') for f in snapshot if f['name'] in editable)+'''
}
public void WriteDraft(MyIni ini) {
'''+ '\n'.join(setter(f,'UiDraft') for f in draft)+'''
}
public void ReadDraft(MyIni ini) {
'''+ '\n'.join(getter(f,'UiDraft') for f in draft)+'''
}
}
'''
(root/'src/Modules/CarrierPilot/CarrierUi.cs').write_text(ui,encoding='utf-8')
owner='''void WriteUiSnapshot(MyIni ini) {
'''+ '\n'.join(setter(f) for f in snapshot)+'\n'+ '\n'.join(setter(f,'UiMiner','ActiveMiner().'+f['name']) for f in minerfields)+'''
ini.Set("Ui","mission",emma3MissionId);
ini.Set("Ui","routeCount",emma3CarrierRoutePositions.Count);
ini.Set("Ui","asteroidReport",asteroidReport.Encode());
ini.Set("Ui","miningTotals",miningTotals.Encode());
}
void ApplyUiEdits(MyIni ini) {
'''+ '\n'.join(getter(f,'UiEdit') if f['name']!='timerPersistenceEnabled' else 'SetTimerPersistence(ini.Get("UiEdit","timerPersistenceEnabled").ToBoolean(timerPersistenceEnabled));' for f in snapshot if f['name'] in editable)+'''
}
'''
(root/'src/Modules/CarrierCommander/UiSnapshot.cs').write_text(owner,encoding='utf-8')
print('Extracted UI',len(methods),'methods;',len(snapshot),'scalar snapshots;',len(minerfields),'miner readings')
