struct CarrierOreRow
{
    public string Name;
    public char Mode;
    public int LineIndex;
}

List<CarrierOreRow> _carrierOreRows = new List<CarrierOreRow>();
string[] _carrierOreLines = new string[0];
string _carrierOreRaw;
string _carrierOreDisplayedRaw;
string _carrierOreMoveOriginalRaw;

void RefreshCarrierOreCache()
{
    IMyTimerBlock timer = FindOreTimer();
    string raw = timer == null ? null : (timer.CustomData ?? "");
    if (string.Equals(raw, _carrierOreRaw, StringComparison.Ordinal))
        return;

    _carrierOreRaw = raw;
    _carrierOreRows.Clear();
    if (raw == null)
    {
        _carrierOreLines = new string[0];
        return;
    }

    // Keep source lines so toggles retain comments and blank lines.
    _carrierOreLines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    for (int i = 0; i < _carrierOreLines.Length; i++)
    {
        string line = _carrierOreLines[i].Trim();
        if (line.Length == 0 || line.StartsWith("//"))
            continue;
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        _carrierOreRows.Add(new CarrierOreRow
        {
            Name = parts[0],
            Mode = ParseCarrierOreMode(parts.Length < 2 ? "Y" : parts[1]),
            LineIndex = i
        });
    }
}

int GetCarrierOreCount()
{
    RefreshCarrierOreCache();
    return _carrierOreRows.Count;
}

void AppendCarrierOreRows(StringBuilder sb)
{
    RefreshCarrierOreCache();
    _carrierOreDisplayedRaw = _carrierOreRaw;
    if (_carrierOreRaw == null)
    {
        sb.AppendLine("   Ore timer missing.");
        return;
    }

    for (int i = 0; i < _carrierOreRows.Count; i++)
    {
        CarrierOreRow row = _carrierOreRows[i];
        int menuIndex = i + 3;
        if (caRefineryOreMoving && caMenuIndex == menuIndex)
            sb.AppendLine("< [" + row.Mode + "] " + row.Name);
        else
            AppendCarrierMenuRow(sb, "[" + row.Mode + "] " + row.Name, menuIndex, false);
    }
}

void BeginCarrierOreMove(int oreIndex)
{
    RefreshCarrierOreCache();
    IMyTimerBlock timer = FindOreTimer();
    if (timer == null || oreIndex < 0 || oreIndex >= _carrierOreRows.Count)
        return;
    if (!string.Equals(_carrierOreDisplayedRaw, _carrierOreRaw, StringComparison.Ordinal))
    {
        ScriptLog("Ore list changed; refreshed menu. Apply again.");
        return;
    }
    _carrierOreMoveOriginalRaw = _carrierOreRaw;
    caRefineryMoveOriginalIndex = oreIndex;
    caRefineryOreMoving = true;
}

void MoveCarrierOre(int delta)
{
    RefreshCarrierOreCache();
    IMyTimerBlock timer = FindOreTimer();
    int oreIndex = caMenuIndex - 3;
    int target = oreIndex + delta;
    if (!caRefineryOreMoving || timer == null || oreIndex < 0 || oreIndex >= _carrierOreRows.Count
        || target < 0 || target >= _carrierOreRows.Count)
        return;
    int firstLine = _carrierOreRows[oreIndex].LineIndex;
    int secondLine = _carrierOreRows[target].LineIndex;
    string swap = _carrierOreLines[firstLine];
    _carrierOreLines[firstLine] = _carrierOreLines[secondLine];
    _carrierOreLines[secondLine] = swap;
    timer.CustomData = string.Join("\n", _carrierOreLines).TrimEnd();
    _carrierOreRaw = null;
    RefreshCarrierOreCache();
    _carrierOreDisplayedRaw = _carrierOreRaw;
    caMenuIndex = target + 3;
}

void EndCarrierOreMove()
{
    if (caRefineryOreMoving)
        ScriptLog("Refinery ore order updated.");
    caRefineryOreMoving = false;
    caRefineryReorderArmed = false;
    caRefineryMoveOriginalIndex = -1;
    _carrierOreMoveOriginalRaw = null;
}

void CancelCarrierOreMove()
{
    IMyTimerBlock timer = FindOreTimer();
    if (caRefineryOreMoving && timer != null && _carrierOreMoveOriginalRaw != null)
    {
        timer.CustomData = _carrierOreMoveOriginalRaw;
        _carrierOreRaw = null;
        RefreshCarrierOreCache();
        _carrierOreDisplayedRaw = _carrierOreRaw;
        if (caRefineryMoveOriginalIndex >= 0)
            caMenuIndex = caRefineryMoveOriginalIndex + 3;
    }
    CancelCarrierOreReorder();
}

void CancelCarrierOreReorder()
{
    caRefineryOreMoving = false;
    caRefineryReorderArmed = false;
    caRefineryMoveOriginalIndex = -1;
    _carrierOreMoveOriginalRaw = null;
}

void ToggleCarrierOre(int oreIndex)
{
    RefreshCarrierOreCache();
    IMyTimerBlock oreTimer = FindOreTimer();
    if (oreTimer == null || oreIndex < 0 || oreIndex >= _carrierOreRows.Count)
        return;
    // Never apply an old row index to a timer edited since the last drawing.
    if (!string.Equals(_carrierOreDisplayedRaw, _carrierOreRaw, StringComparison.Ordinal))
    {
        ScriptLog("Ore list changed; refreshed menu. Apply again.");
        return;
    }

    CarrierOreRow row = _carrierOreRows[oreIndex];
    char next = row.Mode == 'Y' ? 'U' : (row.Mode == 'U' ? 'X' : 'Y');
    _carrierOreLines[row.LineIndex] = row.Name + " " + next;
    oreTimer.CustomData = string.Join("\n", _carrierOreLines).TrimEnd();
    RefreshCarrierOreCache();
    ScriptLog("Ore state: " + row.Name + " set to " + next + ".");
}

void UpdateCarrierInfoCache()
{
    if (carrierInfoRefreshTicks > 0)
    {
        carrierInfoRefreshTicks--;
        return;
    }

    carrierInfoRefreshTicks = carrierInfoRefreshIntervalTicks - 1;
    RefreshCarrierResourceTelemetry();
}

bool ShouldUpdateCarrierInfoCache()
{
    return caLcdMenu == CaLcdMenu.Info;
}

void RefreshCachedCarrierDimensions()
{
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, b => b.CubeGrid.EntityId == Me.CubeGrid.EntityId);
    if (blocks.Count == 0)
    {
        cachedCarrierShipHeight = 0;
        cachedCarrierShipWidth = 0;
        cachedCarrierShipLength = 0;
        return;
    }

    Vector3I min = blocks[0].Min;
    Vector3I max = blocks[0].Max;
    foreach (var b in blocks)
    {
        min.X = Math.Min(min.X, b.Min.X);
        min.Y = Math.Min(min.Y, b.Min.Y);
        min.Z = Math.Min(min.Z, b.Min.Z);
        max.X = Math.Max(max.X, b.Max.X);
        max.Y = Math.Max(max.Y, b.Max.Y);
        max.Z = Math.Max(max.Z, b.Max.Z);
    }

    double size = Me.CubeGrid.GridSize;
    cachedCarrierShipWidth = (Math.Abs(max.X - min.X) + 1) * size;
    cachedCarrierShipHeight = (Math.Abs(max.Y - min.Y) + 1) * size;
    cachedCarrierShipLength = (Math.Abs(max.Z - min.Z) + 1) * size;
}

void RefreshCarrierResourceTelemetry()
{
    if (carrierResourcesTimer == null)
    {
        cachedCarrierHydrogenPercent = -1;
        cachedCarrierCargoPercent = -1;
        return;
    }

    cachedCarrierHydrogenPercent = -1;
    cachedCarrierCargoPercent = -1;
    foreach (string raw in (carrierResourcesTimer.CustomData ?? "").Split('\n'))
    {
        string line = raw.Trim();
        int separator = line.IndexOf(':');
        if (separator < 0)
            continue;
        string label = line.Substring(0, separator).Trim();
        if (label != "Cargo" && label != "H2O")
            continue;
        double value;
        if (!TryParseNumber(line.Substring(separator + 1).Trim().TrimEnd('%'), out value) || value < 0)
            continue;
        if (label == "Cargo")
            cachedCarrierCargoPercent = MathHelper.Clamp(value, 0, 100);
        else
            cachedCarrierHydrogenPercent = MathHelper.Clamp(value, 0, 100);
    }
}

char ParseCarrierOreMode(string value)
{
    string mode = (value ?? "").Trim();
    if (mode.Equals("Y", StringComparison.OrdinalIgnoreCase) || mode.Equals("on", StringComparison.OrdinalIgnoreCase) || mode.Equals("true", StringComparison.OrdinalIgnoreCase) || mode == "1" || mode.Equals("target", StringComparison.OrdinalIgnoreCase))
        return 'Y';
    if (mode.Equals("X", StringComparison.OrdinalIgnoreCase) || mode.Equals("exclude", StringComparison.OrdinalIgnoreCase) || mode.Equals("excluded", StringComparison.OrdinalIgnoreCase) || mode.Equals("never", StringComparison.OrdinalIgnoreCase))
        return 'X';
    return 'U';
}

void WriteEcho()
{
    StringBuilder sb = _echoSb;
    sb.Clear();
    sb.AppendLine("Autopilot Mode");
    sb.AppendLine(autopilotEnabled ? "State: Enabled" : "State: Disabled");
    if (hasTarget)
        sb.AppendLine($"Target: {targetPosition.ToString("0.00")}");
    else
        sb.AppendLine("Target: <none>");
    sb.AppendLine($"Runtime (last tick): {Runtime.LastRunTimeMs:0.000} ms");
    sb.AppendLine($"Runtime peak (last minute): {runtimePeakLastMinuteMs:0.000} ms");
    Echo(sb.ToString());
}

void UpdateRuntimeMetrics()
{
    double lastTickMs = Runtime.LastRunTimeMs;
    if (lastTickMs > runtimePeakLastMinuteMs)
    {
        runtimePeakLastMinuteMs = lastTickMs;
        CaptureRuntimeProfilePeak();
    }

    runtimePeakWindowElapsedSeconds += secondsPerTick;
    if (runtimePeakWindowElapsedSeconds >= 60.0)
    {
        LogRuntimeProfileWindow();
        runtimePeakLastMinuteMs = lastTickMs;
        CaptureRuntimeProfilePeak();
        runtimePeakWindowElapsedSeconds = 0;
    }
}

void BeginRuntimeProfile()
{
    runtimeProfileActive = emma3ScriptLogging;
    if (!runtimeProfileActive)
        return;
    for (int i = 0; i < runtimeProfileLastMs.Length; i++)
    {
        runtimeProfileLastMs[i] = 0;
        runtimeProfileLastInstructions[i] = 0;
    }
    for (int i = 0; i < runtimeDetailLastMs.Length; i++)
    {
        runtimeDetailLastMs[i] = 0;
        runtimeDetailLastInstructions[i] = 0;
    }

    runtimeProfileMarkerTicks = DateTime.UtcNow.Ticks;
    runtimeProfileMarkerInstructions = Runtime.CurrentInstructionCount;
}

void MarkRuntimeProfileSection(int section)
{
    if (!runtimeProfileActive)
        return;
    long nowTicks = DateTime.UtcNow.Ticks;
    int nowInstructions = Runtime.CurrentInstructionCount;
    if (section >= 0 && section < runtimeProfileLastMs.Length)
    {
        runtimeProfileLastMs[section] += (nowTicks - runtimeProfileMarkerTicks) / 10000.0;
        runtimeProfileLastInstructions[section] += Math.Max(0, nowInstructions - runtimeProfileMarkerInstructions);
    }

    runtimeProfileMarkerTicks = nowTicks;
    runtimeProfileMarkerInstructions = nowInstructions;
}

long BeginRuntimeDetail()
{
    if (!runtimeProfileActive) return 0;
    runtimeDetailMarkerInstructions = Runtime.CurrentInstructionCount;
    return DateTime.UtcNow.Ticks;
}

void EndRuntimeDetail(int section, long started)
{
    if (!runtimeProfileActive || started == 0 || section < 0 || section >= runtimeDetailLastMs.Length)
        return;
    runtimeDetailLastMs[section] += (DateTime.UtcNow.Ticks - started) / 10000.0;
    runtimeDetailLastInstructions[section] += Math.Max(0, Runtime.CurrentInstructionCount - runtimeDetailMarkerInstructions);
}

void CaptureRuntimeProfilePeak()
{
    double dominantMs = -1;
    runtimeProfilePeakDominant = "none";
    for (int i = 0; i < runtimeProfileLastMs.Length; i++)
    {
        runtimeProfilePeakMs[i] = runtimeProfileLastMs[i];
        runtimeProfilePeakInstructions[i] = runtimeProfileLastInstructions[i];
        if (runtimeProfileLastMs[i] > dominantMs)
        {
            dominantMs = runtimeProfileLastMs[i];
            runtimeProfilePeakDominant = runtimeProfileSectionNames[i];
        }
    }
}

void LogRuntimeProfileWindow()
{
    if (runtimePeakLastMinuteMs <= 0)
        return;
    double accountedMs = 0;
    double dominantMs = -1;
    string dominant = runtimeProfilePeakDominant;
    for (int i = 0; i < runtimeProfilePeakMs.Length; i++)
    {
        accountedMs += runtimeProfilePeakMs[i];
        if (runtimeProfilePeakMs[i] > dominantMs)
            dominantMs = runtimeProfilePeakMs[i];
    }

    double overheadMs = Math.Max(0, runtimePeakLastMinuteMs - accountedMs);
    if (overheadMs > dominantMs)
        dominant = "runtime-overhead";
    StringBuilder sb = _runtimeProfileSb;
    sb.Clear();
    sb.Append("PERF-SECTIONS: peakTotal=").Append(runtimePeakLastMinuteMs.ToString("0.000"));
    sb.Append(" ms; dominant=").Append(dominant);
    for (int i = 0; i < runtimeProfileSectionNames.Length; i++)
    {
        sb.Append("; ").Append(runtimeProfileSectionNames[i]).Append('=');
        sb.Append(runtimeProfilePeakMs[i].ToString("0.000")).Append(" ms/");
        sb.Append(runtimeProfilePeakInstructions[i]).Append(" instr");
    }

    sb.Append("; runtime-overhead=").Append(overheadMs.ToString("0.000")).Append(" ms");
    ScriptLog(sb.ToString());
}
