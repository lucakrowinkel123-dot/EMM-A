
bool MergeMinerStatePart(MyIni destination, string data, long generation)
{
    if (string.IsNullOrWhiteSpace(data)) return false;
    MyIni source = new MyIni();
    MyIniParseResult result;
    if (!source.TryParse(data, out result)
        || source.Get(StateIniSection, "snapshotGeneration").ToInt64(-1) != generation) return false;
    for (int i = 0; i < _minerSaveKeys.Length; i++)
    {
        if (!IsMinerRouteSaveIndex(i)) continue;
        if(_minerSaveKeys[i].Length==0)continue;
        MyIniValue value = source.Get(StateIniSection, _minerSaveKeys[i]);
        if (!value.IsEmpty) destination.Set(StateIniSection, _minerSaveKeys[i], value.ToString());
    }
    return true;
}
