
bool MergeCarrierStatePart(MyIni destination, string data, long generation, bool route)
{
    if (string.IsNullOrWhiteSpace(data)) return false;
    MyIni source = new MyIni();
    MyIniParseResult result;
    if (!source.TryParse(data, out result)
        || source.Get(StateIniSection, "snapshotGeneration").ToInt64(-1) != generation) return false;
    for (int i = 0; i < _carrierSaveKeys.Length; i++)
    {
        if (route != IsCarrierRouteSaveIndex(i) || !route && !IsCarrierReportsSaveIndex(i)) continue;
        if(_carrierSaveKeys[i].Length==0)continue;
        MyIniValue value = source.Get(StateIniSection, _carrierSaveKeys[i]);
        if (!value.IsEmpty) destination.Set(StateIniSection, _carrierSaveKeys[i], value.ToString());
    }
    return true;
}
