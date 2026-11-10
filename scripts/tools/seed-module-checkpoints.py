from pathlib import Path
import json
root = Path(__file__).resolve().parents[1]
for ship in ('Carrier', 'Miner'):
    library=json.loads((root/f'generated/{ship}.library.json').read_text(encoding='utf-8'))
    def member(name):
        found=[m['source'] for m in library if m['name']==name]
        assert len(found)==1,name
        return found[0].replace('\r\n','\n')
    for owner in ('Commander','Pilot'):
        folder=root/f'src/Modules/{ship}{owner}'
        scalar=folder/'ScalarPersistence.cs'
        text=scalar.read_text(encoding='utf-8')
        needle=f'void Capture{ship}SaveSnapshot(bool full = true) {{'
        assert needle in text
        text=text.replace(needle, needle+'\nCaptureModuleState();',1)
        scalar.write_text(text,encoding='utf-8')
        publish=member('PublishInternalState')
        needle='    string next = EncodeInternalState(core, route,'
        assert needle in publish
        publish=publish.replace(needle,'    core = AddCapturedModuleState(core);\n'+needle,1)
        progress=member('PublishInternalProgress')
        needle='    string next = EncodeInternalState('
        assert needle in progress
        progress=progress.replace(needle,'    progress = AddCapturedModuleState(progress);\n'+needle,1)
        extra=''
        if owner=='Pilot':
            extra=f'\nstring stateIniTimerTag = "EMM-A {"Ca" if ship=="Carrier" else "Mi"} INI Pilot Core";\nstring stateRouteTimerTag = "EMM-A {"Ca" if ship=="Carrier" else "Mi"} INI Pilot Route";\n'
            if ship=='Carrier': extra+='string stateReportsTimerTag = "EMM-A Ca INI Pilot Reports";\n'
        (folder/'CheckpointPublication.cs').write_text(publish+'\n'+progress+extra,encoding='utf-8')
