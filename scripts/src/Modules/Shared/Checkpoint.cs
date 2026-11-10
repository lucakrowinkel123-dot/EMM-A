string moduleCapturedState = "";
bool moduleRestored;

void CaptureModuleState()
{
    MyIni snapshot = new MyIni();
    WriteModuleState(snapshot);
    WriteRoleModuleState(snapshot);
    moduleCapturedState = snapshot.ToString();
}

string AddCapturedModuleState(string data)
{
    MyIni destination = new MyIni(), snapshot = new MyIni();
    if (!destination.TryParse(data ?? "") || !snapshot.TryParse(moduleCapturedState))
        throw new Exception("Cannot publish incomplete module checkpoint");
    var keys = new List<MyIniKey>();
    snapshot.GetKeys("Module", keys);
    foreach (var key in keys) destination.Set(key, snapshot.Get(key).ToString());
    return destination.ToString();
}

bool RestoreModuleCheckpoint(MyIni core)
{
    MyIni state = _hasProgressState && _progressIni.ContainsKey("Module", "role") ? _progressIni : core;
    moduleRestored = ReadModuleState(state);
    if (moduleRestored) ReadRoleModuleState(state);
    return moduleRestored;
}
