IMyTextPanel reportLcd;
MiningReportData asteroidReport = new MiningReportData();
MiningReportData miningTotals = new MiningReportData();
string reportMission = "";
long reportAsteroid;
int reportCompleted;
bool reportVisible, reportFrozen, finishAndHold;
int reportRefreshTicks = 43, reportScroll;
double reportApproachSeconds;
Vector3D reportPark, reportBoxMin, reportBoxMax;
bool reportHasBox;
Vector3D plannedReportBoxMin, plannedReportBoxMax;
bool plannedReportHasBox;
bool reportParked, reportCompletionCounted, reportTimeDirty;
readonly List<string> reportRows = new List<string>();
readonly List<string> reportOreNames = new List<string>();
MiningReportData reportRowsSource;
long reportRowsOreRevision = -1;
string reportLastText;
string reportLastNativeText;
IMyTextPanel reportLastPanel;
readonly StringBuilder reportBuilder = new StringBuilder(1024);

void InitializeReportLcd()
{
    if (reportLcd == null) return;
    reportLastText = null;
    reportLastNativeText = null;
    reportLastPanel = null;
    reportLcd.ContentType = ContentType.TEXT_AND_IMAGE;
    reportLcd.Font = "Debug";
    reportLcd.FontSize = 1.225f;
    reportLcd.TextPadding = 0f;
    reportLcd.Alignment = TextAlignment.LEFT;
}
bool ShowFinishAndHold()
{
    return fullAutoEnabled && emma3ActiveEntityId != 0 &&
        (emma3MissionState == Emma3MissionState.WaitingStationary
        || emma3MissionState == Emma3MissionState.WaitingMiner
        || emma3MissionState == Emma3MissionState.WaitingHangarDeploy);
}
int FinishAndHoldIndex() { return ShowContinueMenuItem() || ShowRecallMinerMenuItem() ? 4 : 3; }
void StartAsteroidReport(bool show = true)
{
    if (reportMission != emma3MissionId)
    {
        MyIni bridge;
        if (TryReadEmma3Bridge(out bridge))
            plannedReportHasBox = TryParseV3(bridge.Get("EMMA3", "asteroidBoxMin").ToString(""), out plannedReportBoxMin)
                && TryParseV3(bridge.Get("EMMA3", "asteroidBoxMax").ToString(""), out plannedReportBoxMax);
        reportMission = emma3MissionId;
        reportAsteroid = emma3ActiveEntityId;
        asteroidReport = new MiningReportData();
        asteroidReport.Travel = reportApproachSeconds;
        asteroidReport.Elapsed = reportApproachSeconds;
        reportParked = true;
        reportCompletionCounted = false;
        reportApproachSeconds = 0;
        reportFrozen = false;
        reportPark = GetControlledPosition();
        reportHasBox = plannedReportHasBox;
        reportBoxMin = plannedReportBoxMin;
        reportBoxMax = plannedReportBoxMax;
        reportScroll = 0;
    }
    if (show) reportVisible = true;
    MarkProgressStateDirty();
}

void CompleteAsteroidReport()
{
    if (reportMission != emma3MissionId || reportCompletionCounted) return;
    reportCompletionCounted = true;
    reportCompleted++;
    MarkProgressStateDirty();
    // Parked time continues through servicing/Finish & Hold until actual departure.
}

void StopParkedReportClock()
{
    if (!reportParked) return;
    reportParked = false;
    reportFrozen = true;
    reportTimeDirty = true;
    MarkProgressStateDirty();
}

void ResetMiningReports()
{
    miningTotals = new MiningReportData();
    asteroidReport = new MiningReportData();
    reportCompleted = 0;
    reportApproachSeconds = 0;
    reportScroll = 0;
    reportTimeDirty = false;
    // Preserve the active park/completion identity; reset is not a new mission.
    MarkProgressStateDirty();
}
void UpdateMiningReports()
{
    Vector3D position = GetControlledPosition();
    bool leftPark = Vector3D.DistanceSquared(position, reportPark) > 25;
    if (reportParked && leftPark)
    {
        reportParked = false;
        reportFrozen = true;
        MarkProgressStateDirty();
    }
    if (reportParked)
    {
        asteroidReport.Mining += secondsPerTick;
        miningTotals.Mining += secondsPerTick;
        reportTimeDirty = true;
    }
    if ((!scriptPaused && !cockpitAbortPaused && fullAutoEnabled) || reportParked)
    {
        miningTotals.Elapsed += secondsPerTick;
        if (reportParked) asteroidReport.Elapsed += secondsPerTick;
        if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute)
        {
            reportApproachSeconds += secondsPerTick;
            miningTotals.Travel += secondsPerTick;
        }
        reportTimeDirty = true;
    }
    if (reportVisible && leftPark
        && (!reportHasBox || new BoundingBoxD(reportBoxMin, reportBoxMax).Contains(position) == ContainmentType.Disjoint))
    {
        reportVisible = false;
        reportScroll = 0;
        MarkProgressStateDirty();
    }
    reportRefreshTicks -= UpdateTickDelta();
    if (reportRefreshTicks > 0) return;
    reportRefreshTicks = 300;
    if (reportTimeDirty)
    {
        reportTimeDirty = false;
        MarkProgressStateDirty();
    }
    UpdateReportLcd();
}
string ReportDuration(double seconds)
{
    long value = Math.Max(0, (long)seconds);
    return value >= 3600 ? (value / 3600) + ":" + ((value / 60) % 60).ToString("00") + ":" + (value % 60).ToString("00")
        : (value / 60) + ":" + (value % 60).ToString("00");
}
void UpdateReportLcd()
{
    if (reportLcd == null) return;
    MiningReportData data = reportVisible ? asteroidReport : miningTotals;
    if (reportRowsSource != data || reportRowsOreRevision != data.OreRevision)
    {
        reportRows.Clear();
        reportOreNames.Clear();
        reportOreNames.AddRange(data.Ores.Keys);
        reportOreNames.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string name in reportOreNames)
            reportRows.Add(name + " " + data.Ores[name].ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.') + " Kg");
        if (reportOreNames.Count == 0) reportRows.Add("No ore delivered yet");
        reportRows.Add("");
        reportRows.Add("");
        reportRowsSource = data;
        reportRowsOreRevision = data.OreRevision;
    }
    reportRows[reportRows.Count - 2] = "Travelling: " + ReportDuration(data.Travel);
    reportRows[reportRows.Count - 1] = "Mining: " + ReportDuration(data.Mining);
    reportBuilder.Clear();
    reportBuilder.Append(' ').Append(shipNames.Carrier).AppendLine(reportVisible ? " | Asteroid report" : " | Mining totals");
    reportBuilder.AppendLine("—————————————————");
    if (reportRows.Count <= 9) reportScroll = 0;
    else reportScroll %= reportRows.Count;
    for (int i = 0; i < 9; i++)
        reportBuilder.Append("   ").AppendLine(i < reportRows.Count ? reportRows[(reportScroll + i) % reportRows.Count] : "");
    reportBuilder.AppendLine("—————————————————");
    reportBuilder.AppendLine(reportVisible ? " Asteroid ID: " + reportAsteroid : " Asteroids completed: " + reportCompleted);
    reportBuilder.Append(" ").AppendLine(ReportDuration(reportVisible ? asteroidReport.Elapsed : miningTotals.Elapsed));
    string text = reportBuilder.ToString();
    if (!string.Equals(reportLastText, text, StringComparison.Ordinal))
    {
        reportLastText = text;
        UpdatePluginLcdPayload(pluginLcdMainText, text);
    }
    if (!PluginLcdTakeoverActive() && (reportLastPanel != reportLcd || !string.Equals(reportLastNativeText, text, StringComparison.Ordinal)))
    {
        if (!TryClaimCarrierHeavyWork()) return;
        if (reportLcd.WriteText(text, false))
        {
            reportLastNativeText = text;
            reportLastPanel = reportLcd;
        }
    }
    if (reportRows.Count > 9) reportScroll++;
}
void SaveMiningReports(MyIni ini, string section)
{
    ini.Set(section, "reportMission", reportMission);
    ini.Set(section, "reportAsteroid", reportAsteroid);
    ini.Set(section, "reportCompleted", reportCompleted);
    ini.Set(section, "reportVisible", reportVisible);
    ini.Set(section, "reportFrozen", reportFrozen);
    ini.Set(section, "finishAndHold", finishAndHold);
    ini.Set(section, "asteroidReport", asteroidReport.Encode());
    ini.Set(section, "miningTotals", miningTotals.Encode());
    ini.Set(section, "reportApproachSeconds", NumberR(reportApproachSeconds));
    SetCarrierProgressVector(ini, section, 6, reportPark, false);
    ini.Set(section, "reportHasBox", reportHasBox);
    SetCarrierProgressVector(ini, section, 7, reportBoxMin, false);
    SetCarrierProgressVector(ini, section, 8, reportBoxMax, false);
    ini.Set(section, "reportTimingVersion", 2);
    ini.Set(section, "reportParked", reportParked);
    ini.Set(section, "reportCompletionCounted", reportCompletionCounted);
}
void LoadMiningReports(MyIni ini)
{
    reportMission = StateString(ini, "reportMission", "");
    long.TryParse(StateString(ini, "reportAsteroid", "0"), out reportAsteroid);
    int.TryParse(StateString(ini, "reportCompleted", "0"), out reportCompleted);
    reportVisible = StateBool(ini, "reportVisible", false);
    reportFrozen = StateBool(ini, "reportFrozen", false);
    finishAndHold = StateBool(ini, "finishAndHold", false);
    asteroidReport = MiningReportData.Decode(StateString(ini, "asteroidReport", ""));
    miningTotals = MiningReportData.Decode(StateString(ini, "miningTotals", ""));
    MiningReportData.Number(StateString(ini, "reportApproachSeconds", "0"), out reportApproachSeconds);
    TryParseV3(StateString(ini, "reportPark", ""), out reportPark);
    reportHasBox = StateBool(ini, "reportHasBox", false);
    TryParseV3(StateString(ini, "reportBoxMin", ""), out reportBoxMin);
    TryParseV3(StateString(ini, "reportBoxMax", ""), out reportBoxMax);
    reportCompletionCounted = StateBool(ini, "reportCompletionCounted", reportFrozen);
    reportParked = StateBool(ini, "reportParked", reportVisible && Vector3D.DistanceSquared(GetControlledPosition(), reportPark) <= 25);
    if (reportParked) reportFrozen = false; // .48 upgrades keep totals and resume only future parked time.
    reportTimeDirty = false;
}
