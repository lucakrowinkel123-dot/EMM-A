// Local PB transport. Only the uniquely named partner on this exact grid may
// send control messages. Entity IDs are refreshed after reload/respawn.
// The 0.8.42 transport and checkpoint format remains compatible. The 0.8.44
// departure preflight adds fields; update both Miner modules together.
const string moduleProtocol = "EMMA-MODULE/0.8.42/1";
IMyBroadcastListener moduleInbox;
IMyProgrammableBlock modulePeer;
readonly List<IMyProgrammableBlock> moduleBlocks = new List<IMyProgrammableBlock>();
int moduleTick, moduleLastContact = -10000;
bool moduleCompatible;
bool modulePairChanged;
string moduleError = "Waiting for partner";
ModuleOperationJournal moduleOperation = new ModuleOperationJournal();
long moduleSequence;
string moduleIdentity = "";

bool ModuleReady()
{
    return moduleCompatible && modulePeer != null && !modulePeer.Closed
        && modulePeer.Enabled && modulePeer.IsFunctional && moduleTick - moduleLastContact <= 180;
}

void InitializeModule()
{
    moduleInbox = IGC.RegisterBroadcastListener(shipNames.Channel(moduleProtocol + "/" + moduleRole));
    if (moduleIdentity.Length == 0) moduleIdentity = Me.EntityId.ToString();
    FindModulePeer();
    if (modulePairChanged) SaveState();
}

void FindModulePeer()
{
    moduleBlocks.Clear();
    GridTerminalSystem.GetBlocksOfType(moduleBlocks, p => p.CubeGrid == Me.CubeGrid && p.CustomName == modulePeerName);
    IMyProgrammableBlock found = moduleBlocks.Count == 1 ? moduleBlocks[0] : null;
    if (found != modulePeer)
    {
        modulePeer = found;
        moduleCompatible = false;
        moduleLastContact = -10000;
    }
    if (found == null) moduleError = moduleBlocks.Count > 1 ? "Duplicate partner PB names" : "Missing " + modulePeerName;
}

void SendModule(string kind, string payload)
{
    if (modulePeer == null || modulePeer.Closed) return;
    IGC.SendBroadcastMessage(shipNames.Channel(moduleProtocol + "/" + modulePeerRole),
        new MyTuple<long, string, string>(modulePeer.EntityId, kind, payload ?? ""), TransmissionDistance.CurrentConstruct);
}

void UpdateModule()
{
    moduleTick++;
    if (moduleTick % 60 == 1) FindModulePeer();
    int budget = 8;
    while (moduleInbox.HasPendingMessage && budget-- > 0)
    {
        var message = moduleInbox.AcceptMessage();
        if (modulePeer == null || message.Source != modulePeer.EntityId
            || !(message.Data is MyTuple<long, string, string>)) continue;
        var data = (MyTuple<long, string, string>)message.Data;
        if (data.Item1 != Me.EntityId) continue;
        if (data.Item2 == "hello")
        {
            moduleCompatible = data.Item3 == moduleProtocol;
            if (moduleCompatible) moduleLastContact = moduleTick;
            else moduleError = "Commander/Pilot version mismatch";
        }
        else if (moduleCompatible)
        {
            moduleLastContact = moduleTick;
            ReceiveModule(data.Item2, data.Item3);
        }
    }
    if (moduleTick % 60 == 1)
    {
        SendModule("hello", moduleProtocol);
        if (ModuleReady()) PublishModuleStatus();
    }
    if (ModuleReady()) moduleError = "";
    else if (moduleCompatible) moduleError = "No contact with " + modulePeerRole;
}

string NextModuleId()
{
    moduleSequence++;
    return moduleIdentity + "/" + moduleSequence;
}

void WriteModuleState(MyIni ini)
{
    ini.Set("Module", "role", moduleRole);
    ini.Set("Module", "pair", shipNames.Pair);
    ini.Set("Module", "version", moduleProtocol);
    ini.Set("Module", "identity", moduleIdentity);
    ini.Set("Module", "sequence", moduleSequence);
    ini.Set("Module", "operation", moduleOperation.Serialize());
}

bool ReadModuleState(MyIni ini)
{
    if (ini.Get("Module", "role").ToString("") != moduleRole
        || ini.Get("Module", "version").ToString("") != moduleProtocol) return false;
    moduleIdentity = ini.Get("Module", "identity").ToString("");
    moduleSequence = ini.Get("Module", "sequence").ToInt64(0);
    modulePairChanged = ini.Get("Module", "pair").ToString(new ShipNames().Pair) != shipNames.Pair;
    if (modulePairChanged) return false;
    var restored = new ModuleOperationJournal();
    if (!restored.Restore(ini.Get("Module", "operation").ToString(""))) return false;
    moduleIdentity = ini.Get("Module", "identity").ToString("");
    moduleSequence = ini.Get("Module", "sequence").ToInt64(0);
    moduleOperation = restored;
    return moduleIdentity.Length != 0 && moduleSequence >= 0;
}
