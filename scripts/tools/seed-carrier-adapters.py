"""One-time migration helper; resulting role sources are reviewed and maintained."""
from pathlib import Path
import json
root = Path(__file__).resolve().parents[1]
library = json.loads((root / 'generated/Carrier.library.json').read_text(encoding='utf-8'))
def member(name):
    matches = [m['source'] for m in library if m['name'] == name]
    assert len(matches) == 1, name
    return matches[0].replace('\r\n', '\n')
def replace(text, old, new):
    assert text.count(old) == 1, old
    return text.replace(old, new)
out = root / 'src/Modules/CarrierCommander'
out.mkdir(parents=True, exist_ok=True)
ctor = member('Program')
start = ctor.index('    allGyros = ')
end = ctor.index('    if (tickSpeed ==', start)
ctor = ctor[:start] + ctor[end:]
ctor = replace(ctor, '    LoadState();', '    LoadState();\n    InitializeModule();')
ctor = ctor.replace('Carrier script v0.8.41.', 'Carrier Commander v0.8.42.')
ctor = replace(ctor, '    Runtime.UpdateFrequency = scriptPaused ? UpdateFrequency.None : tickSpeed;', '    Runtime.UpdateFrequency = UpdateFrequency.Update1;')
main = member('Main')
main = replace(main, '        carrierHeavyWorkClaimed = false;', '        UpdateModule();\n        carrierHeavyWorkClaimed = false;')
start = main.index('        if (autopilotEnabled) carrierRuntimeWorkMask |= 16;')
end = main.index('        MarkRuntimeProfileSection(2);', start)
main = main[:start] + main[end:]
main = main.replace('if (!cockpitAbortPaused)', 'if (!cockpitAbortPaused && !scriptPaused && ModuleReady() && !pilotRecoveryBlocked)')
main = replace(main, '            UpdatePathRunner();', '            UpdateCommanderPilotOperation();')
main = replace(main, '        RemoteCommand(argument);', '        CommanderRemoteCommand(argument);')
# Keep the external command dispatcher and LCD behavior. Wrap navigation control
# commands here so none of their old per-field writes can act on physical blocks.
remote = member('RemoteCommand')
remote = replace(remote, 'void RemoteCommand(string command)', 'void LegacyCommanderCommand(string command)')
remote = remote.replace('Runtime.UpdateFrequency = UpdateFrequency.None;', 'Runtime.UpdateFrequency = UpdateFrequency.Update1;')
(out/'EntryPoints.cs').write_text(ctor + '\n' + main + '\n' + remote, encoding='utf-8')
