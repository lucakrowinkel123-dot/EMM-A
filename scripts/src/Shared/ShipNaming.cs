ShipNames shipNames = new ShipNames();
string Ca(string suffix) { return shipNames.Ca(suffix); }
string Mi(string suffix) { return shipNames.Mi(suffix); }
void InitializeShipNames(bool carrier, bool pilot)
{
    const string commanderSuffix = " Prog. Block (Commander)", pilotSuffix = " Prog. Block (Pilot)";
    IMyProgrammableBlock commander = Me;
    var candidates = new List<IMyProgrammableBlock>();
    if (pilot)
    {
        if (!Me.CustomName.EndsWith(pilotSuffix, StringComparison.Ordinal)) throw new Exception("Pilot name must end with" + pilotSuffix);
        string name = Me.CustomName.Substring(0, Me.CustomName.Length - pilotSuffix.Length) + commanderSuffix;
        GridTerminalSystem.GetBlocksOfType(candidates, p => p.CubeGrid == Me.CubeGrid && p.CustomName == name);
        if (candidates.Count != 1) throw new Exception("Expected one local " + name);
        commander = candidates[0];
    }
    shipNames = ShipNames.Read(commander.CustomData, carrier);
    string own = carrier ? shipNames.Carrier : shipNames.Miner;
    if (commander.CustomName != own + commanderSuffix || Me.CustomName != own + (pilot ? pilotSuffix : commanderSuffix))
        throw new Exception("NameShip does not match PB name");
    string other = (carrier ? shipNames.Miner : shipNames.Carrier) + commanderSuffix;
    candidates.Clear();
    GridTerminalSystem.GetBlocksOfType(candidates, p => p.CustomName == other);
    if (candidates.Count > 1 || candidates.Count == 1 && ShipNames.Read(candidates[0].CustomData, !carrier).Pair != shipNames.Pair)
        throw new Exception("Carrier/Miner naming mismatch");
}
