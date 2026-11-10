
bool TryLoadPathRecordsFromTimer(out List<string> records, out string timerName)
{
    records = new List<string>();
    timerName = "";
    pathAnchorValid = false;
    pathAnchorPos = Vector3D.Zero;
    pathAnchorForward = Vector3D.Zero;
    pathAnchorUp = Vector3D.Zero;
    timerName="Commander route";
    MyIni accepted = new MyIni(); if (!accepted.TryParse(moduleOperation.Payload)) return false; string data = accepted.Get("Path", "text").ToString("");
    string[] lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (string line in lines)
    {
        string rec = line.Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (rec.StartsWith("\"") && rec.EndsWith("\"") && rec.Length >= 2)
            rec = rec.Substring(1, rec.Length - 2).Trim();
        if (string.IsNullOrWhiteSpace(rec))
            continue;
        if (!pathAnchorValid)
        {
            Vector3D ap, af, au;
            if (TryParseCarrierAnchorLine(rec, out ap, out af, out au))
            {
                pathAnchorPos = ap;
                pathAnchorForward = af;
                pathAnchorUp = au;
                pathAnchorValid = true;
                continue;
            }
        }

        records.Add(rec);
    }

    return records.Count > 0 || pathAnchorValid;
}
