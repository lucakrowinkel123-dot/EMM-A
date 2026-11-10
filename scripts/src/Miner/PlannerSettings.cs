const string minerPlannerSettingsPrefix = "EMMA3-PLANNER: ";

// A tiny replicated settings record, not a persistence snapshot. Storage is
// server-only; the client planner still needs custom target offsets on SDX2.
// Called only at startup and explicit TOFF commands, never in the tick loop.
void PublishMinerPlannerSettings()
{
    string line = minerPlannerSettingsPrefix + "TOFFR=" + NumberR(TOFFR)
        + ";TOFFU=" + NumberR(TOFFU) + ";TOFFF=" + NumberR(TOFFF);
    string data = Me.CustomData ?? "";
    int start = 0;
    if (!data.StartsWith(minerPlannerSettingsPrefix, StringComparison.Ordinal))
    {
        start = data.IndexOf("\n" + minerPlannerSettingsPrefix, StringComparison.Ordinal);
        if (start >= 0) start++;
    }
    if (start >= 0)
    {
        int end = data.IndexOf('\n', start);
        if (end < 0) end = data.Length;
        string oldLine = data.Substring(start, end - start).TrimEnd('\r');
        if (oldLine == line) return;
        Me.CustomData = data.Substring(0, start) + line + data.Substring(end);
    }
    else Me.CustomData = line + "\n" + data;
}
