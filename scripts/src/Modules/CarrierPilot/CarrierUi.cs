// The UI owns presentation and drafts. Commander authorizes every mission action.
class CarrierUi {
readonly Program uiHost;
ShipNames shipNames { get { return uiHost.shipNames; } }
string Ca(string suffix) { return shipNames.Ca(suffix); }
string Mi(string suffix) { return shipNames.Mi(suffix); }
IMyProgrammableBlock Me { get { return uiHost.Me; } }
IMyGridTerminalSystem GridTerminalSystem { get { return uiHost.GridTerminalSystem; } }
IMyGridProgramRuntimeInfo Runtime { get { return uiHost.Runtime; } }
void Echo(string text) { uiHost.Echo(text); }
public CarrierUi(Program host) {
 uiHost=host; rc=host.rc;
 carrierLcd=GridTerminalSystem.GetBlockWithName(carrierLcdName) as IMyTextPanel;
 reportLcd=GridTerminalSystem.GetBlockWithName(Ca("LCD Report")) as IMyTextPanel;
 carrierResourcesTimer=GridTerminalSystem.GetBlockWithName(Ca("Resources")) as IMyTimerBlock;
 itemsTimer=GridTerminalSystem.GetBlockWithName(Ca("Items")) as IMyTimerBlock;
 hangarBlock=GridTerminalSystem.GetBlockWithName(hangarBlockName);
 LoadCarrierItems(); InitializeReportLcd(); RefreshCachedCarrierDimensions();
}
string carrierRemoteName { get { return Ca("Remote Control"); } }


string carrierPbTag { get { return Ca("Prog. Block (Commander)"); } }


double maxSpeed = 500;

double arrivalTolerance = 2.5;


double orientationToleranceDeg = 1.0;


// Storage owns recovery snapshots; these timers are optional INI mirrors.

string stateIniTimerTag { get { return Ca("INI Core"); } }


string stateRouteTimerTag { get { return Ca("INI Route"); } }


string stateReportsTimerTag { get { return Ca("INI Reports"); } }


string oreNameTimerTag { get { return Ca("Ores"); } } // exact name of timer storing ore Y/U/X rows


string carrierLcdName { get { return Ca("LCD Main"); } }


string emma3BridgeTimerName { get { return Ca("Plugin Bridge"); } }


string emma3CompletedAsteroidsTimerName { get { return Ca("Asteroid IDs"); } }


string hangarBlockName { get { return Ca("Hangar - A"); } }


string hangarManagerTimerName { get { return Ca("HangarManager"); } }


double emma3SearchRadius = 50000;


double emma3ParkingHeight = 400;

double emma3PlaneClearance = 25;

double emma3TravelClearance = 75;

bool emma3Visualization = true;

bool emma3AllowMovement = false; // v0.1 safety default


bool emma3ScriptLogging = true;


bool emma3AllowSeatExitDuringMining = false;


bool emma3IceReserveEnabled = false;


double emma3IceReserveMillionLiters = 1;


bool emma3PluginOreTransfer = true;


bool emma3PluginPowerTransitions = true;


bool emma3PluginRemoteLink = true;


bool emma3PluginLcdOutput = true;


bool emma3ItemsMode = false;


string emma3EnabledItems = "";


IMyTimerBlock itemsTimer;


List<string> itemNames = new List<string>();


List<string> itemBlueprints = new List<string>();


string itemsMenuError = "";


string emma3ProductionStatus = "";


double minerStandardDepth = 30.0;


double minerLaneWidth = 17.5;


double minerLaneHeight = 12.5;


double minerForwardSpeed = 0.7;


double minerReturnSpeed = 2.5;


bool minerAutoServiceEnabled = true;


double minerAutoServiceCargoPercent = 90.0;


double minerAutoServiceHydrogenPercent = 20.0;


bool minerStopAtOnePercentFuel = true;


double minerRouteStartExtra = 0.0;


bool minerMergeAssistedDocking = false;


bool minerHangarEnabled = false;


double shortRangeDistanceMeters = 50000;


double cruiseOffsetMeters = 2000;


double thrustLimitKn = 5000000;


double thrustUsePercent = 80;


double accelerationLimit = 2.0;


bool turnTestEnabled = true;


// =============================================

IMyRemoteControl rc;


IMyTextPanel carrierLcd;


MatrixD _tickRcMatrix = MatrixD.Identity;




Vector3D targetPosition = Vector3D.Zero;


double measuredFlipSeconds = 0;


double measuredFlipMass = 0;


int runtime = 0;


string carrierLcdLastText = "";


string carrierLcdLastNativeText = "";


string pluginLcdMainText = "";


string pluginLcdReportText = "";


int pluginLcdHeartbeatAgeTicks = int.MaxValue;


bool pluginLcdReady = false;


StringBuilder _lcdSb = new StringBuilder(1024);


string _lcdPluginStateText;


string _lcdPluginMessageText;


string _lcdMinerStatusText;


int _lcdPluginStateHash;


int _lcdPluginMessageHash;


int _lcdMinerStatusHash;


int lcdRefreshIntervalTicks = 60;


const int lcdRefreshMinTicks = 60;


int carrierLcdStateFingerprint = int.MinValue;


int caMenuIndex = 0;


int caBehaviorPage = 0;


int caMinerBehaviorPage = 0;


bool caEditingValue = false;

bool caRefineryReorderArmed = false;

bool caRefineryOreMoving = false;

int caRefineryMoveOriginalIndex = -1;

int caEditingIndex = -1;

double caEditValue = 0;


double caThrustEditLimitKn = 0;


double caThrustEditUsePercent = 0;


int caThrustStepIndex = 5;


double cachedCarrierShipHeight = 0;


double cachedCarrierShipWidth = 0;


double cachedCarrierShipLength = 0;


double cachedCarrierHydrogenPercent = -1;


double cachedCarrierCargoPercent = -1;


int carrierInfoRefreshTicks = 173;


const int carrierInfoRefreshIntervalTicks = 300;


IMyTimerBlock carrierResourcesTimer;




CaLcdMenu caLcdMenu = CaLcdMenu.Main;


const double minerContactTimeoutSeconds = 60;

bool fullAutoEnabled = false;




Emma3MissionState emma3MissionState = Emma3MissionState.None;




ManualHangarCycleState manualHangarCycleState = ManualHangarCycleState.None;


IMyTerminalBlock hangarBlock;


long emma3ActiveEntityId = 0;


string emma3PluginState = "idle";


string emma3PluginMessage = "waiting for plugin";


bool emma3ContinueAvailable = false;


int emma3CompletedCount = 0;


List<Vector3D> emma3CarrierRoutePositions = new List<Vector3D>();


int emma3CarrierRouteIndex = 0;


int fullAutoEnableStationaryTicks = 0;


IMyTextPanel reportLcd;


MiningReportData asteroidReport = new MiningReportData();


MiningReportData miningTotals = new MiningReportData();


long reportAsteroid;


int reportCompleted;


bool reportVisible;


bool finishAndHold;


int reportScroll;


readonly List<string> reportRows = new List<string>();


readonly List<string> reportOreNames = new List<string>();


MiningReportData reportRowsSource;


long reportRowsOreRevision = -1;


string reportLastText;


string reportLastNativeText;


IMyTextPanel reportLastPanel;


readonly StringBuilder reportBuilder = new StringBuilder(1024);




string _lcdHangarInventoryData;


string _lcdHangarContentsLabel = "Unknown";




List<CarrierOreRow> _carrierOreRows = new List<CarrierOreRow>();


string[] _carrierOreLines = new string[0];


string _carrierOreRaw;


string _carrierOreDisplayedRaw;

string _carrierOreMoveOriginalRaw;

// BEGIN INTERNAL PERSISTENCE

// Storage always owns a complete checkpoint. Timers are optional inspectable

// mirrors, never the commit record. Length-prefixed parts avoid duplicating INI

// sections and preserve existing multiline values without escaping/reparsing.

bool timerPersistenceEnabled = false;


bool carrierLcdConfigured;
MinerSlot uiMiner = new MinerSlot();
string uiMission = "";
string uiActions = "";


void AdjustCarrierEdit(int direction)
{
    double step = 1.0;
    if (caEditingIndex == 1)
        step = GetCarrierMaxSpeedEditStep(direction);
    else if (caEditingIndex == 2)
        step = 0.1;

    else if (caEditingIndex == 3)

        step = 0.5;

    else if (caEditingIndex == 9)

        step = 500;

    else if (caEditingIndex == 10)

        step = 250;

    else if (caEditingIndex == 11)

        step = 500;

    else if (caEditingIndex == 12)

        step = 5;

    else if (caEditingIndex == 13)

        step = 0.1;

    else if (caEditingIndex == 16)

        step = 1000;

    else if (caEditingIndex == 17)

        step = 25;

    else if (caEditingIndex == 18)

        step = 1;

    else if (caEditingIndex == 19)

        step = 5;

    else if (caEditingIndex == 101)

        step = 2.5;

    else if (caEditingIndex == 102 || caEditingIndex == 103)

        step = 0.5;

    else if (caEditingIndex == 104 || caEditingIndex == 105)

        step = 0.1;

    else if (caEditingIndex == 107 || caEditingIndex == 108)

        step = 2.5;

    else if (caEditingIndex == 109)
        step = 5;
    caEditValue = Math.Max(0, caEditValue + step * direction);
    if (caEditingIndex == 1)
        caEditValue = MathHelper.Clamp(caEditValue, 1, 50000);
    if (caEditingIndex == 25)
        caEditValue = Math.Max(1, Math.Min(1000000, caEditValue));

    if (caEditingIndex == 7)

        caEditValue = Math.Max(lcdRefreshMinTicks, caEditValue);

    else if (caEditingIndex == 9)

        caEditValue = Math.Max(100, caEditValue);

    else if (caEditingIndex == 10)

        caEditValue = Math.Max(100, caEditValue);

    else if (caEditingIndex == 11)

        caEditValue = Math.Max(1, caEditValue);

    else if (caEditingIndex == 12)

        caEditValue = MathHelper.Clamp(caEditValue, 1, 100);

    else if (caEditingIndex == 13)

        caEditValue = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 16)

        caEditValue = MathHelper.Clamp(caEditValue, 1000, 50000);

    else if (caEditingIndex == 17)

        caEditValue = Math.Max(25, caEditValue);

    else if (caEditingIndex == 18)

        caEditValue = Math.Max(1, caEditValue);

    else if (caEditingIndex == 19)

        caEditValue = Math.Max(10, caEditValue);

    else if (caEditingIndex == 102 || caEditingIndex == 103 || caEditingIndex == 104 || caEditingIndex == 105)

        caEditValue = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 107 || caEditingIndex == 108)

        caEditValue = MathHelper.Clamp(caEditValue, 0, 100);

    else if (caEditingIndex == 109)

        caEditValue = MathHelper.Clamp(caEditValue, 0, 500);
}



void AdjustCarrierThrustLimitEdit(int direction)

{

    if (caEditingIndex == 11)

        caEditValue = Math.Max(1, caEditValue + GetCarrierThrustStepKn() * direction);

    else if (caEditingIndex == 12)

        caEditValue = MathHelper.Clamp(caEditValue + 5 * direction, 1, 100);

    else if (caEditingIndex == 15)

        caEditValue = MathHelper.Clamp(Math.Round(caEditValue) + direction, 0, GetCarrierThrustStepCount() - 1);

}



void AppendCarrierBehaviorThrustLimitRow(StringBuilder sb, int rowIndex)
{

    bool selected = caMenuIndex == rowIndex;

    sb.Append(selected ? "> " : "-  ");

    sb.AppendLine("Thrust limit:");

    sb.Append("   ");

    sb.Append(FormatWholeNumberWithCommas(thrustLimitKn));

    sb.AppendLine(" kN");

}




void AppendCarrierBehaviorToggleRow(StringBuilder sb, string label, int settingIndex, int rowIndex)
{
    bool selected = caMenuIndex == rowIndex;

    bool editSelected = caEditingValue && selected && caEditingIndex == settingIndex;

    if (selected)

        sb.Append(editSelected ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.AppendLine(label);
}



void AppendCarrierBehaviorValueRow(StringBuilder sb, string label, int settingIndex, int rowIndex, string unit, string fmt)

{

    bool selected = caMenuIndex == rowIndex;

    bool editSelected = caEditingValue && selected && caEditingIndex == settingIndex;

    if (selected)

        sb.Append(editSelected ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.Append(label);

    sb.Append(": ");

    sb.Append(GetCarrierValue(settingIndex).ToString(fmt));

    sb.AppendLine(unit);

}




void AppendCarrierFooter(StringBuilder sb)

{

    while (CountLines(sb) < 11)

        sb.AppendLine();

    sb.AppendLine("—————————————————");

    sb.Append(" ");

    sb.AppendLine(GetCarrierFooterInfo());

    sb.Append(" ");

    sb.Append(Runtime.LastRunTimeMs.ToString("0.000"));

    sb.Append("ms");

}




void AppendCarrierHeader(StringBuilder sb)

{

    if (caLcdMenu == CaLcdMenu.AutonomousMining || caLcdMenu == CaLcdMenu.SelectOres || caLcdMenu == CaLcdMenu.ResetAsteroidsConfirm)

    {

        sb.Append(' ').Append(shipNames.Carrier).AppendLine(" | Auto-mining");

    }

    else if (caLcdMenu == CaLcdMenu.MinerA)

    {

        sb.Append(" EMM-A Miner A | Auto ");

        sb.AppendLine(fullAutoEnabled ? "On" : "Off");

    }

    else if (caLcdMenu == CaLcdMenu.MinerOverview)
    {
        sb.Append(" EMM-A Miner | Auto ");
        sb.AppendLine(fullAutoEnabled ? "On" : "Off");
    }

    else if (caLcdMenu == CaLcdMenu.ThrustLimit)

    {

        sb.Append(' ').Append(shipNames.Carrier).AppendLine(" | Thrust limit");

    }

    else

    {

        sb.Append(' ').Append(shipNames.Carrier).Append(" | Auto ");

        sb.AppendLine(fullAutoEnabled ? "On" : "Off");

    }



    sb.AppendLine("—————————————————");

}




void AppendCarrierItemRows(StringBuilder sb)

{

    for (int i = 0; i < itemNames.Count; i++)

        AppendCarrierMenuRow(sb, "[" + (CarrierItemEnabled(itemBlueprints[i]) ? "Y" : "X") + "] " + itemNames[i], i + 2, false);

    if (itemNames.Count == 0)

        sb.AppendLine(itemsMenuError.Length > 0 ? "   " + itemsMenuError : "   Add name/blueprint pairs to Items timer.");

}




void AppendCarrierMenuRow(StringBuilder sb, string text, int index, bool isBack)

{

    bool selected = caMenuIndex == index;

    bool editSelected = caEditingValue && selected;

    if (selected)

        sb.Append((isBack || editSelected) ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.AppendLine(text);

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



void AppendCarrierRows(StringBuilder sb)

{

    if (caLcdMenu == CaLcdMenu.Main)

    {

        AppendCarrierMenuRow(sb, "Autonomous mining", 0, false);

        AppendCarrierMenuRow(sb, "Miners", 1, false);

        AppendCarrierMenuRow(sb, "Behavior settings", 2, false);

        AppendCarrierMenuRow(sb, "Info", 3, false);

        return;

    }



    AppendCarrierMenuRow(sb, "Back", 0, true);

    if (caLcdMenu == CaLcdMenu.AutonomousMining)

    {

        string fullAutoLabel = "Auto-mining [" + (fullAutoEnabled ? "On" : "Off") + "]";

        if (!fullAutoEnabled && fullAutoEnableStationaryTicks > 0)

            fullAutoLabel += " " + GetLoadingSymbol();

        AppendCarrierMenuRow(sb, fullAutoLabel, 1, false);

        AppendCarrierMenuRow(sb, "Select mode", 2, false);

        if (ShowContinueMenuItem())

            AppendCarrierMenuRow(sb, "Continue", 3, false);

        else if (ShowRecallMinerMenuItem())

            AppendCarrierMenuRow(sb, ActiveMiner().RecallRequested ? "Recall sent" : "Recall miner", 3, false);

        else

            sb.AppendLine();

        if (ShowFinishAndHold())

            AppendCarrierMenuRow(sb, "Finish & Hold [" + (finishAndHold ? "On" : "Off") + "]", FinishAndHoldIndex(), false);

        else sb.AppendLine();

        sb.Append("   Plan: ");

        sb.Append(GetEmma3PluginStateLabel(emma3PluginState));

        if (PluginStateIsActivelyWorking())

        {

            sb.Append(" ");

            sb.Append(GetLoadingSymbol());

        }



        sb.AppendLine();

        sb.Append("   ");

        sb.AppendLine(emma3PluginMessage);

        sb.Append("   Mission: ");

        sb.AppendLine(GetEmma3MissionStateLabel(emma3MissionState));

        if (emma3ItemsMode && emma3ProductionStatus.Length > 0)

            sb.AppendLine("   " + emma3ProductionStatus);



        if (emma3MissionState == Emma3MissionState.FlyingCarrierRoute)

        {

            sb.Append("   Route: ");

            sb.Append(emma3CarrierRouteIndex);

            sb.Append("/");

            sb.Append(emma3CarrierRoutePositions.Count);

            sb.Append(", ");

            sb.Append(Vector3D.Distance(GetControlledPosition(), targetPosition).ToString("0.0"));

            sb.AppendLine("m");

        }



        return;

    }



    if (caLcdMenu == CaLcdMenu.MinerOverview)
    {
        AppendCarrierMenuRow(sb, "Miner A", 1, false);
        AppendMinerRouteProgressLine(sb);
        return;
    }


    if (caLcdMenu == CaLcdMenu.MinerA)

    {

        MinerSlot miner = ActiveMiner();

        AppendCarrierMenuRow(sb, miner.RecallRequested ? "Recall sent" : "Recall Miner A", 1, false);

        sb.Append("   Status: ");

        sb.AppendLine(GetMinerDetailStatus());

        bool readingsAvailable = MinerDetailReadingsAvailable();

        sb.Append("   Cargo: ");

        sb.Append(readingsAvailable ? miner.StatusCargoPercent.ToString("0") : "-");

        sb.AppendLine("%");

        sb.Append("   H2O: ");

        sb.Append(readingsAvailable ? miner.StatusWaterPercent.ToString("0") : "-");

        sb.AppendLine("%");

        sb.Append("   Last contact: ");

        if (miner.StatusAgeSeconds >= minerContactTimeoutSeconds)
            sb.AppendLine("No contact");
        else

        {

            sb.Append(Math.Max(0, (int)Math.Floor(miner.StatusAgeSeconds)));

            sb.AppendLine("s ago");

        }

        sb.Append("   Hangar: ");

        sb.AppendLine(GetMinerHangarContentsLabel());

        return;

    }



    if (caLcdMenu == CaLcdMenu.MinerBehaviorSettings)
    {
        AppendCarrierMenuRow(sb, "Next page " + (caMinerBehaviorPage + 1) + "/3 Miner", 1, false);
        sb.AppendLine();

        if (caMinerBehaviorPage == 0)

        {

            AppendCarrierBehaviorValueRow(sb, "Standard depth", 101, 2, " m", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Lane width", 102, 3, " m", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Lane height", 103, 4, " m", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Forward speed", 104, 5, " m/s", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Return speed", 105, 6, " m/s", "0.0");

        }

        else if (caMinerBehaviorPage == 1)

        {

            AppendCarrierBehaviorToggleRow(sb, "Resupply [" + (minerAutoServiceEnabled ? "On" : "Off") + "]", 106, 2);

            AppendCarrierBehaviorValueRow(sb, "Resupply Cargo", 107, 3, "%", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Resupply H2", 108, 4, "%", "0.0");

            AppendCarrierBehaviorValueRow(sb, "Route start extra", 109, 5, " m", "0");

            AppendCarrierBehaviorToggleRow(sb, "Merge docking [" + (minerMergeAssistedDocking ? "On" : "Off") + "]", 110, 6);

            AppendCarrierBehaviorToggleRow(sb, "Stop at 1% fuel [" + (minerStopAtOnePercentFuel ? "On" : "Off") + "]", 112, 7);
        }

        else

        {

            AppendCarrierBehaviorToggleRow(sb, "Hangar storage [" + (minerHangarEnabled ? "On" : "Off") + "]", 111, 2);

            sb.AppendLine();

            sb.Append("   Hangar: ");

            sb.AppendLine(hangarBlockName);

            sb.Append("   Timer: ");

            sb.AppendLine(hangarManagerTimerName);

            sb.Append("   Miner: ");
            sb.AppendLine(GetMinerHangarMinerStateLabel());
        }



        return;

    }



    if (caLcdMenu == CaLcdMenu.SelectOres)
    {
        AppendCarrierMenuRow(sb, emma3ItemsMode ? "Mode: Items" : "Mode: Ore", 1, false);
        if (emma3ItemsMode)
            AppendCarrierItemRows(sb);
        else
        {
            AppendCarrierMenuRow(sb, "Refinery order", 2, false);
            AppendCarrierOreRows(sb);
        }
        return;
    }



    if (caLcdMenu == CaLcdMenu.ResetAsteroidsConfirm)

    {

        sb.AppendLine();

        sb.AppendLine("   This stops the current mission");

        sb.AppendLine("   and clears completed IDs,");

        sb.AppendLine("   scans, caches, and generated");

        sb.AppendLine("   Carrier/Miner paths.");

        AppendCarrierMenuRow(sb, "Yes", 1, false);

        return;

    }



    if (caLcdMenu == CaLcdMenu.ThrustLimit)

    {

        AppendCarrierMenuRow(sb, "Save", 1, false);

        sb.AppendLine();

        AppendCarrierThrustLimitValueRow(sb);

        sb.AppendLine();

        AppendCarrierThrustStepRow(sb);

        AppendCarrierThrustUseRow(sb);

        if (caMenuIndex == 2 && caEditingValue && caEditingIndex == 11)

        {

            AppendCarrierThrustDirectionPercentRows(sb);

        }



        if (caMenuIndex == 4)

        {

            sb.Append("   EMM-A keeps ");

            sb.Append(FormatPercent(Math.Max(0, 100 - GetCarrierThrustUseEditPercent())));

            sb.AppendLine(" of allowed ");

            sb.AppendLine("   thrust as overhead. ");

        }



        return;

    }



    if (caLcdMenu == CaLcdMenu.BehaviorSettings)

    {

        AppendCarrierMenuRow(sb, "Next page " + (caBehaviorPage + 1) + "/5 Carrier", 1, false);
        sb.AppendLine();

        if (caBehaviorPage == 0)

        {

            AppendCarrierBehaviorValueRow(sb, "Max speed", 1, 2, " m/s", "0");
            AppendCarrierBehaviorValueRow(sb, "Orientation tolerance", 2, 3, " deg", "0.#");
            AppendCarrierBehaviorValueRow(sb, "Arrival tolerance", 3, 4, " m", "0.#");
            AppendCarrierTurnCalibrationRows(sb);
        }

        else if (caBehaviorPage == 1)

        {

            AppendCarrierBehaviorValueRow(sb, "LCD refresh", 7, 2, " ticks", "0");

            sb.AppendLine("   Transit: adaptive flip braking");

            AppendCarrierBehaviorValueRow(sb, "Cruise offset", 10, 4, " m", "0");

            AppendCarrierBehaviorThrustLimitRow(sb, 5);

        }

        else if (caBehaviorPage == 2)

        {

            AppendCarrierBehaviorValueRow(sb, "Acceleration limit", 13, 2, " m/s²", "0.0");
            AppendCarrierBehaviorToggleRow(sb, "Allow movement [" + (emma3AllowMovement ? "On" : "Off") + "]", 21, 4);
            AppendCarrierBehaviorToggleRow(sb, "Seat exit mining [" + (emma3AllowSeatExitDuringMining ? "On" : "Off") + "]", 23, 5);
            AppendCarrierBehaviorToggleRow(sb, "Ice reserve [" + (emma3IceReserveEnabled ? "On" : "Off") + "]", 24, 6);
            if (emma3IceReserveEnabled)
                AppendCarrierBehaviorValueRow(sb, "Ice reserve", 25, 7, " million L", "0");
        }

        else if (caBehaviorPage == 3)

        {

            AppendCarrierBehaviorValueRow(sb, "Search radius", 16, 2, " m", "0");

            AppendCarrierBehaviorValueRow(sb, "Parking height", 17, 3, " m", "0");

            AppendCarrierBehaviorValueRow(sb, "Plane clearance", 18, 4, " m", "0");

            AppendCarrierBehaviorValueRow(sb, "Travel clearance", 19, 5, " m", "0");

            AppendCarrierBehaviorToggleRow(sb, "Visualization [" + (emma3Visualization ? "On" : "Off") + "]", 20, 6);

            AppendCarrierBehaviorToggleRow(sb, "Script logging [" + (emma3ScriptLogging ? "On" : "Off") + "]", 22, 7);

        }

        else if (caBehaviorPage == 4)

        {

            AppendCarrierBehaviorToggleRow(sb, "Plugin ore transfer [" + (emma3PluginOreTransfer ? "On" : "Off") + "]", 26, 2);

            AppendCarrierBehaviorToggleRow(sb, "Plugin power transitions [" + (emma3PluginPowerTransitions ? "On" : "Off") + "]", 27, 3);

            AppendCarrierBehaviorToggleRow(sb, "Plugin remote link [" + (emma3PluginRemoteLink ? "On" : "Off") + "]", 28, 4);

            AppendCarrierBehaviorToggleRow(sb, "Plugin LCD output [" + (emma3PluginLcdOutput ? "On" : "Off") + "]", 29, 5);

            AppendCarrierBehaviorToggleRow(sb, "Timer persistence [" + (timerPersistenceEnabled ? "On" : "Off") + "]", 30, 6);

        }



        return;

    }



    if (caLcdMenu == CaLcdMenu.Info)

    {

        sb.AppendLine("   Ship height: " + cachedCarrierShipHeight.ToString("0.0") + "m");

        sb.AppendLine("   Ship width: " + cachedCarrierShipWidth.ToString("0.0") + "m");

        sb.AppendLine("   Ship length: " + cachedCarrierShipLength.ToString("0.0") + "m");

        if (cachedCarrierHydrogenPercent < 0)

            sb.AppendLine("   Resource telemetry: Missing");

        else

        {

            sb.AppendLine("   Hydrogen: " + cachedCarrierHydrogenPercent.ToString("0.0") + "%");

            if (cachedCarrierCargoPercent >= 0)

                sb.AppendLine("   Cargo: " + cachedCarrierCargoPercent.ToString("0.0") + "%");

        }



        sb.AppendLine("   Plan: " + GetEmma3PluginStateLabel(emma3PluginState));

        sb.AppendLine("   " + emma3PluginMessage);

        AppendCarrierMenuRow(sb, "Clean reset", 1, false);

        sb.AppendLine("   " + emma3CompletedCount + " completed entity IDs.");

    }

}




void AppendCarrierThrustDirectionPercentRows(StringBuilder sb)

{

    MatrixD m = rc == null ? _tickRcMatrix : _tickRcMatrix;

    double limitN = Math.Max(1, GetCarrierThrustLimitEditKn()) * 1000.0;

    sb.Append("   % of FWD Thrust: ");

    sb.AppendLine(FormatThrustRatioPercent(limitN, GetEffectiveThrustInDirection(m.Backward)));

    sb.Append("   % of BWD Thrust: ");

    sb.AppendLine(FormatThrustRatioPercent(limitN, GetEffectiveThrustInDirection(m.Forward)));

}




void AppendCarrierThrustLimitValueRow(StringBuilder sb)

{

    bool selected = caMenuIndex == 2;

    bool editSelected = caEditingValue && selected && caEditingIndex == 11;

    if (selected)

        sb.Append(editSelected ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.Append(FormatWholeNumberWithCommas(GetCarrierThrustLimitEditKn()));

    sb.AppendLine(" kN");

}




void AppendCarrierThrustStepRow(StringBuilder sb)

{

    bool selected = caMenuIndex == 3;

    bool editSelected = caEditingValue && selected && caEditingIndex == 15;

    if (selected)

        sb.Append(editSelected ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.Append("Step: ");

    sb.AppendLine(GetCarrierThrustStepLabel(GetCarrierThrustStepEditIndex()));

}




void AppendCarrierThrustUseRow(StringBuilder sb)

{

    bool selected = caMenuIndex == 4;

    bool editSelected = caEditingValue && selected && caEditingIndex == 12;

    if (selected)

        sb.Append(editSelected ? "< " : "> ");

    else

        sb.Append("-  ");

    sb.Append("Thrust use: ");

    sb.Append(FormatPercent(GetCarrierThrustUseEditPercent()));

    sb.AppendLine();

}



void AppendCarrierTurnCalibrationRows(StringBuilder sb)
{
    sb.Append("   Turn test: ");
    sb.AppendLine(IsCruiseTurnMeasurementValid() ? "Ready" : "Needed");
    sb.Append("   Calibrated: ");
    if (measuredFlipMass <= 0 || measuredFlipSeconds <= 0)
    {
        sb.AppendLine("None");
        return;
    }

    sb.Append(FormatWholeNumberWithCommas(measuredFlipMass / 1000.0));
    sb.Append(" t / ");
    sb.Append(Math.Max(0, (measuredFlipSeconds - 2) / 2).ToString("0.0").Replace(',', '.'));
    sb.AppendLine("s");
}



void AppendMinerRouteProgressLine(StringBuilder sb)

{

    const int bulletCount = 31;

    double ratio = ActiveMiner().ProgressTotalMeters > 0 ? MathHelper.Clamp(ActiveMiner().ProgressMeters / ActiveMiner().ProgressTotalMeters, 0, 1) : 0;

    int marker = (int)Math.Round(ratio * bulletCount);

    marker = Math.Max(0, Math.Min(bulletCount, marker));

    sb.Append("   ");

    for (int i = 0; i <= bulletCount; i++)

        sb.Append(i == marker ? '\u00A4' : '\u2022');

    sb.AppendLine();

}




void BackToCarrierAutonomousMiningMenu()

{

    OpenCarrierMenu(CaLcdMenu.AutonomousMining);

    caMenuIndex = 2;

}




void BeginCarrierEdit(int index)

{

    if ((index < 1 || index > 23) && index != 25 && (index < 101 || index > 109))

        return;

    caEditingIndex = index;

    caEditValue = GetCarrierStoredValue(index);

    caEditingValue = true;

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



void BeginCarrierThrustLimitEdit(int index)

{

    if (index != 11 && index != 12 && index != 15)

        return;

    caEditingIndex = index;

    if (index == 11)

        caEditValue = caThrustEditLimitKn;

    else if (index == 12)

        caEditValue = caThrustEditUsePercent;

    else

        caEditValue = caThrustStepIndex;

    caEditingValue = true;

}




void CancelCarrierEdit()

{

    caEditingValue = false;

    caEditingIndex = -1;

}



void CancelCarrierOreMove()
{
    IMyTimerBlock timer = FindOreTimer();
    if (caRefineryOreMoving && timer != null && _carrierOreMoveOriginalRaw != null)
    {
        SetUiOreRaw(timer,_carrierOreMoveOriginalRaw);
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



bool CarrierItemEnabled(string id)

{

    return ("," + emma3EnabledItems + ",").IndexOf("," + id + ",", StringComparison.Ordinal) >= 0;

}




void CloseCarrierThrustLimitMenu()

{

    caLcdMenu = CaLcdMenu.BehaviorSettings;

    caBehaviorPage = 1;

    caMenuIndex = 6;

    CancelCarrierEdit();

}




int CountLines(StringBuilder sb)

{

    int count = 0;

    for (int i = 0; i < sb.Length; i++)

        if (sb[i] == '\n')

            count++;

    return count;

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



void EndCarrierThrustLimitEdit()

{

    if (caEditingIndex == 11)

        caThrustEditLimitKn = Math.Max(1, caEditValue);

    else if (caEditingIndex == 12)

        caThrustEditUsePercent = MathHelper.Clamp(caEditValue, 1, 100);

    else if (caEditingIndex == 15)

        caThrustStepIndex = Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, (int)Math.Round(caEditValue)));

    CancelCarrierEdit();

}


int FinishAndHoldIndex() { return ShowContinueMenuItem() || ShowRecallMinerMenuItem() ? 4 : 3; }




string FormatCompactThrustN(double thrustN)

{

    double abs = Math.Abs(thrustN);

    double scaled;

    string unit;

    if (abs >= 1000000000.0)

    {

        scaled = thrustN / 1000000000.0;

        unit = "GN";

    }

    else if (abs >= 1000000.0)

    {

        scaled = thrustN / 1000000.0;

        unit = "MN";

    }

    else

    {

        scaled = thrustN / 1000.0;

        unit = "kN";

    }



    return scaled.ToString("000.0").Replace(',', '.') + " " + unit;

}




string FormatPercent(double value)

{

    return Math.Round(value, 1).ToString("0.#").Replace(',', '.') + "%";

}




string FormatThrustRatioPercent(double limitN, double availableN)

{

    if (availableN <= 0)

        return "---";

    return Math.Round(limitN / availableN * 100.0).ToString("0") + "%";

}




string FormatWholeNumberWithCommas(double value)

{

    long n = Math.Max(0, (long)Math.Round(value));

    string s = n.ToString();

    StringBuilder sb = new StringBuilder(s.Length + s.Length / 3);

    for (int i = 0; i < s.Length; i++)

    {

        if (i > 0 && (s.Length - i) % 3 == 0)

            sb.Append(',');

        sb.Append(s[i]);

    }



    return sb.ToString();

}




int GetCarrierBehaviorSettingIndexForRow(int rowIndex)

{

    if (rowIndex < 2)

        return -1;

    if (caBehaviorPage == 0 && rowIndex >= 2 && rowIndex <= 4)

        return rowIndex - 1;

    if (caBehaviorPage == 1)

    {

        if (rowIndex == 2)

            return 7;

        if (rowIndex == 3)

            return 9;

        if (rowIndex == 4)

            return 10;

        if (rowIndex == 5)

            return 11;

    }



    if (caBehaviorPage == 2)

    {

        if (rowIndex == 2)

            return 13;

        if (rowIndex == 3)

            return 14;

        if (rowIndex == 4)

            return 21;

        if (rowIndex == 5)

            return 23;

        if (rowIndex == 6)

            return 24;

        if (rowIndex == 7 && emma3IceReserveEnabled)

            return 25;

    }



    if (caBehaviorPage == 3)

    {

        if (rowIndex >= 2 && rowIndex <= 6)

            return rowIndex + 14;

        if (rowIndex == 7)

            return 22;

    }



    if (caBehaviorPage == 4)

    {

        if (rowIndex == 2)

            return 26;

        if (rowIndex == 3)

            return 27;

        if (rowIndex == 4)

            return 28;

        if (rowIndex == 5)

            return 29;

        if (rowIndex == 6)

            return 30;

    }



    return -1;

}




string GetCarrierFooterInfo()

{

    if (caLcdMenu == CaLcdMenu.Main)

    {

        if (caMenuIndex == 0)

            return "Open auto-mining.";

        if (caMenuIndex == 1)

            return "Miner status and behavior.";
        if (caMenuIndex == 2)

            return "Adjust carrier behavior.";

        if (caMenuIndex == 3)

            return "Show carrier stats.";

    }



    if (caLcdMenu == CaLcdMenu.SelectOres && caMenuIndex == 0)

        return "Back to main menu.";

    if (caMenuIndex == 0 && caLcdMenu != CaLcdMenu.ThrustLimit)

        return "Back to main menu.";

    if (caLcdMenu == CaLcdMenu.AutonomousMining)

    {

        if (caMenuIndex == 1)

            return "Toggle asteroid mining.";

        if (caMenuIndex == 2)

            return "Choose ore mining or item production.";

        if (ShowFinishAndHold() && caMenuIndex == FinishAndHoldIndex())

            return finishAndHold ? "Cancel Finish & Hold." : "Turn off AM when finished.";

        if (caMenuIndex == 3 && ShowContinueMenuItem())

            return "Continue the interrupted mission.";

        if (caMenuIndex == 3)

            return ActiveMiner().RecallRequested ? "Waiting for miner confirmation." : "Call the miner back to dock.";

    }



    if (caLcdMenu == CaLcdMenu.MinerOverview)
    {
        if (caMenuIndex == 1)
            return "Open Miner A.";
    }


    if (caLcdMenu == CaLcdMenu.MinerA && caMenuIndex == 1)

        return ActiveMiner().RecallRequested ? "Waiting for miner confirmation." : "Recall Miner A.";

    if (caLcdMenu == CaLcdMenu.MinerBehaviorSettings)
    {
        if (caMenuIndex == 1)
            return caMinerBehaviorPage < 2 ? "Show next miner settings page." : "Show Carrier settings page.";
        if (GetMinerBehaviorSettingIndexForCarrierRow(caMenuIndex) == 110)

            return minerMergeAssistedDocking ? "Require merge before connector." : "Dock using connector only.";

        if (GetMinerBehaviorSettingIndexForCarrierRow(caMenuIndex) == 111)

            return "Store Miner A between asteroids.";

        if (GetMinerBehaviorSettingIndexForCarrierRow(caMenuIndex) == 112)

            return "Stop Miner at 1% fuel during auto service.";
        return "Adjust all miner's behavior.";

    }



    if (caLcdMenu == CaLcdMenu.BehaviorSettings)
    {
        if (caMenuIndex == 1)
            return caBehaviorPage < 4 ? "Show next Carrier settings page." : "Show miner settings page.";
        int settingIndex = GetCarrierBehaviorSettingIndexForRow(caMenuIndex);

        if (settingIndex == 24)

            return "Prioritize ice when H2O falls below 50%.";

        if (settingIndex == 25)

            return "Stored ice target; minimum 1 million L.";

        if (settingIndex == 1)

            return "Set maximum flight speed.";
        if (settingIndex == 2)

            return "Set required facing accuracy.";

        if (settingIndex == 3)

            return "Set arrival distance.";

        if (settingIndex == 4)

            return "Set asteroid avoid radius.";

        if (settingIndex == 5)

            return "Set failed search retry delay.";

        if (settingIndex == 6)

            return "Set approach point distance.";

        if (settingIndex == 7)

            return "Set LCD update interval.";

        if (settingIndex == 8)

            return "Set safety checks per tick.";

        if (settingIndex == 9)

            return "Set cruise handoff distance.";

        if (settingIndex == 10)

            return "Set cruise stop offset.";

        if (settingIndex == 11)

            return "Set total thrust ceiling.";

        if (settingIndex == 12)

            return "Set normal thrust budget.";

        if (settingIndex == 13)

            return "Set acceleration cap.";

        if (settingIndex == 14)

            return "Test turn rate before cruise.";

        if (settingIndex == 22)

            return "Save Carrier/Miner logs through plugin.";
        if (settingIndex == 23)

            return "Allow leaving Carrier seat after mining starts.";
        if (settingIndex == 26)

            return "Plugin unloads ore; Carrier PB remains fallback.";
        if (settingIndex == 27)

            return "Plugin stages Miner power; Miner PB is fallback.";
        if (settingIndex == 28)

            return "Antenna plugin telemetry/docks; fallbacks remain.";
    }



    if (caLcdMenu == CaLcdMenu.ThrustLimit)

    {

        if (caMenuIndex == 0)

            return "Revert and exit.";

        if (caMenuIndex == 1)

            return "Save and exit.";

        if (caMenuIndex == 2 && caEditingValue && caEditingIndex == 11)

            return "Detected Max: " + FormatCompactThrustN(GetDetectedMaxCarrierThrustN());

        if (caMenuIndex == 2)

            return "Set total thrust ceiling.";

        if (caMenuIndex == 3)

            return "Select stepsize.";

        if (caMenuIndex == 4)

            return "Set normal thrust budget.";

    }



    if (caLcdMenu == CaLcdMenu.SelectOres && caMenuIndex == 1)
        return "Switch between Ore and Items.";
    if (caLcdMenu == CaLcdMenu.SelectOres && emma3ItemsMode)
        return itemsMenuError.Length > 0 ? itemsMenuError : "Y continuously produce; X disabled.";
    if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryOreMoving)
        return "Move ore; Apply saves, Back cancels.";
    if (caLcdMenu == CaLcdMenu.SelectOres && caMenuIndex == 2)
        return caRefineryReorderArmed ? "Choose an ore to move." : "Reorder refinery priority.";
    if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryReorderArmed && caMenuIndex > 2)
        return "Apply to pick up this ore.";
    if (caLcdMenu == CaLcdMenu.SelectOres && caMenuIndex > 2)
        return "Y target; U mine if there; X skip.";
    if (caLcdMenu == CaLcdMenu.ResetAsteroidsConfirm)

    {

        if (caMenuIndex == 1)

            return "Return the plugin and paths to a clean state.";

    }



    if (caLcdMenu == CaLcdMenu.Info && caMenuIndex == 1)

        return "Clean-reset IDs, scans, caches, and paths.";

    return "";

}




int GetCarrierItemCount()

{

    return itemNames.Count;

}




int GetCarrierLcdStateFingerprint()

{

    // Command-driven settings redraw immediately. Only fingerprint live fields

    // belonging to the visible page; periodic redraw still updates age/runtime.

    int hash = 17;

    hash = hash * 31 + (int)caLcdMenu;

    hash = hash * 31 + caMenuIndex;
    hash = hash * 31 + (caEditingValue ? 1 : 0);
    hash = hash * 31 + (caRefineryReorderArmed ? 1 : 0);
    hash = hash * 31 + (caRefineryOreMoving ? 1 : 0);
    hash = hash * 31 + (fullAutoEnabled ? 1 : 0);

    if (caLcdMenu == CaLcdMenu.AutonomousMining || caLcdMenu == CaLcdMenu.Info)

    {

        string stateText = emma3PluginState ?? "", messageText = emma3PluginMessage ?? "";

        if (!object.ReferenceEquals(stateText, _lcdPluginStateText))

        {

            _lcdPluginStateText = stateText;

            _lcdPluginStateHash = stateText.GetHashCode();

        }

        if (!object.ReferenceEquals(messageText, _lcdPluginMessageText))

        {

            _lcdPluginMessageText = messageText;

            _lcdPluginMessageHash = messageText.GetHashCode();

        }

        hash = hash * 31 + _lcdPluginStateHash;

        hash = hash * 31 + _lcdPluginMessageHash;

        if (caLcdMenu == CaLcdMenu.AutonomousMining)

        {

            hash = hash * 31 + (finishAndHold ? 1 : 0);

            hash = hash * 31 + (int)emma3MissionState;

        }

        else

        {

            hash = hash * 31 + (int)Math.Round(cachedCarrierHydrogenPercent);

            hash = hash * 31 + (int)Math.Round(cachedCarrierCargoPercent);

        }

    }

    else if (caLcdMenu == CaLcdMenu.MinerA)

    {

        MinerSlot miner = ActiveMiner();

        string minerText = miner.StatusText ?? "";

        if (!object.ReferenceEquals(minerText, _lcdMinerStatusText))

        {

            _lcdMinerStatusText = minerText;

            _lcdMinerStatusHash = minerText.GetHashCode();

        }

        hash = hash * 31 + _lcdMinerStatusHash;

        hash = hash * 31 + (int)Math.Round(miner.StatusCargoPercent);

        hash = hash * 31 + (int)Math.Round(miner.StatusWaterPercent);

        hash = hash * 31 + GetMinerDetailStatus().GetHashCode();

        hash = hash * 31 + GetMinerHangarContentsLabel().GetHashCode();

        hash = hash * 31 + (MinerDetailReadingsAvailable() ? 1 : 0);

        hash = hash * 31 + (miner.RecallRequested ? 1 : 0);

    }

    else if (caLcdMenu == CaLcdMenu.MinerBehaviorSettings && caMinerBehaviorPage == 2)
        hash = hash * 31 + GetMinerHangarMinerStateLabel().GetHashCode();
    // Route distances/ages are already rendered on the normal refresh cadence.

    return hash;

}



double GetCarrierMaxSpeedEditStep(int direction)
{
    // At a boundary, Down uses the preceding range so every boundary remains
    // reachable in both directions (for example 500 -> 475 -> 500).
    double value = caEditValue;
    if (direction < 0 && value > 1)
        value -= 0.000001;
    if (value < 100) return 5;
    if (value < 250) return 10;
    if (value < 500) return 25;
    if (value < 1000) return 50;
    if (value < 5000) return 1000;
    return 5000;
}



int GetCarrierMenuItemCount()

{

    if (caLcdMenu == CaLcdMenu.Main)

        return 4;

    if (caLcdMenu == CaLcdMenu.AutonomousMining)

        return (ShowContinueMenuItem() || ShowRecallMinerMenuItem() ? 4 : 3) + (ShowFinishAndHold() ? 1 : 0);

    if (caLcdMenu == CaLcdMenu.MinerOverview)
        return 2;
    if (caLcdMenu == CaLcdMenu.MinerA)

        return 2;

    if (caLcdMenu == CaLcdMenu.MinerBehaviorSettings)

        return caMinerBehaviorPage == 2 ? 3 : (caMinerBehaviorPage == 1 ? 8 : 7);

    if (caLcdMenu == CaLcdMenu.SelectOres)
        return emma3ItemsMode ? 2 + GetCarrierItemCount() : 3 + GetCarrierOreCount();
    if (caLcdMenu == CaLcdMenu.BehaviorSettings)

    {

        if (caBehaviorPage == 0)

            return 5;

        if (caBehaviorPage == 1)

            return 6;

        if (caBehaviorPage == 2)

            return emma3IceReserveEnabled ? 8 : 7;

        if (caBehaviorPage == 3)

            return 8;

        return 7;

    }



    if (caLcdMenu == CaLcdMenu.ThrustLimit)

        return 5;

    if (caLcdMenu == CaLcdMenu.ResetAsteroidsConfirm)

        return 2;

    if (caLcdMenu == CaLcdMenu.Info)

        return 2;

    return 1;

}




int GetCarrierOreCount()

{

    RefreshCarrierOreCache();

    return _carrierOreRows.Count;

}




double GetCarrierStoredValue(int index)

{

    if (index == 1)

        return maxSpeed;

    if (index == 2)

        return orientationToleranceDeg;

    if (index == 3)

        return arrivalTolerance;

    if (index == 7)

        return lcdRefreshIntervalTicks;

    if (index == 9)

        return shortRangeDistanceMeters;

    if (index == 10)

        return cruiseOffsetMeters;

    if (index == 11)

        return thrustLimitKn;

    if (index == 12)

        return thrustUsePercent;

    if (index == 13)

        return accelerationLimit;

    if (index == 14)

        return turnTestEnabled ? 1 : 0;

    if (index == 16)

        return emma3SearchRadius;

    if (index == 17)

        return emma3ParkingHeight;

    if (index == 18)

        return emma3PlaneClearance;

    if (index == 19)

        return emma3TravelClearance;

    if (index == 20)

        return emma3Visualization ? 1 : 0;

    if (index == 21)

        return emma3AllowMovement ? 1 : 0;

    if (index == 25)

        return emma3IceReserveMillionLiters;

    if (index == 101)

        return minerStandardDepth;

    if (index == 102)

        return minerLaneWidth;

    if (index == 103)

        return minerLaneHeight;

    if (index == 104)

        return minerForwardSpeed;

    if (index == 105)

        return minerReturnSpeed;

    if (index == 106)

        return minerAutoServiceEnabled ? 1 : 0;

    if (index == 107)

        return minerAutoServiceCargoPercent;

    if (index == 108)

        return minerAutoServiceHydrogenPercent;

    if (index == 109)

        return minerRouteStartExtra;

    if (index == 112)

        return minerStopAtOnePercentFuel ? 1 : 0;

    return 0;

}




double GetCarrierThrustLimitEditKn()

{

    if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit && caEditingIndex == 11)

        return caEditValue;

    return caThrustEditLimitKn;

}




int GetCarrierThrustStepCount()

{

    return 7;

}




int GetCarrierThrustStepEditIndex()

{

    if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit && caEditingIndex == 15)

        return Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, (int)Math.Round(caEditValue)));

    return caThrustStepIndex;

}




double GetCarrierThrustStepKn()

{

    int index = Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, caThrustStepIndex));

    if (index == 0)

        return 1;

    if (index == 1)

        return 10;

    if (index == 2)

        return 100;

    if (index == 3)

        return 1000;

    if (index == 4)

        return 10000;

    if (index == 5)

        return 100000;

    return 1000000;

}




string GetCarrierThrustStepLabel(int index)

{

    if (index == 0)

        return "1 kN";

    if (index == 1)

        return "10 kN";

    if (index == 2)

        return "100 kN";

    if (index == 3)

        return "1 MN";

    if (index == 4)

        return "10 MN";

    if (index == 5)

        return "100 MN";

    return "1 GN";

}




double GetCarrierThrustUseEditPercent()

{

    if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit && caEditingIndex == 12)

        return caEditValue;

    return caThrustEditUsePercent;

}




double GetCarrierValue(int index)

{

    if (caEditingValue && caEditingIndex == index)

        return caEditValue;

    return GetCarrierStoredValue(index);

}




string GetEmma3MissionStateLabel(Emma3MissionState state)

{

    switch (state)

    {

        case Emma3MissionState.None:

            return "Idle";

        case Emma3MissionState.WaitingPlan:

            return "Waiting for plan";

        case Emma3MissionState.PreviewReady:

            return "Preview ready";

        case Emma3MissionState.FlyingCarrierRoute:

            return "Carrier en route";

        case Emma3MissionState.WaitingStationary:

            return "Settling carrier";

        case Emma3MissionState.WaitingMiner:

            return "Miner active";

        case Emma3MissionState.WaitingHangarStore:

            return "Storing miner";

        case Emma3MissionState.WaitingHangarDeploy:
            return "Deploying miner";
        case Emma3MissionState.WaitingStartupHangarInventory:
            return "Checking hangar";
        case Emma3MissionState.WaitingMinerPlan:
            return "Planning miner route";
        case Emma3MissionState.WaitingMinerStart:
            return "Starting miner";
        case Emma3MissionState.WaitingCompletionAck:
            return "Recording completion";
        case Emma3MissionState.WaitingPostCompletionHangarStore:
            return "Storing miner";
    }


    return state.ToString();

}




string GetEmma3PluginStateLabel(string state)

{

    if (string.IsNullOrWhiteSpace(state))

        return "Idle";

    if (state.Equals("idle", StringComparison.OrdinalIgnoreCase))

        return "Idle";

    if (state.Equals("scanning", StringComparison.OrdinalIgnoreCase))

        return "Scanning";

    if (state.Equals("planning", StringComparison.OrdinalIgnoreCase))

        return "Planning route";

    if (state.Equals("producing", StringComparison.OrdinalIgnoreCase))

        return "Producing";

    if (state.Equals("ready_dryrun", StringComparison.OrdinalIgnoreCase))

        return "Preview ready";

    if (state.Equals("ready", StringComparison.OrdinalIgnoreCase))

        return "Plan ready";

    if (state.Equals("error", StringComparison.OrdinalIgnoreCase))

        return "Error";

    if (state.Equals("aborted", StringComparison.OrdinalIgnoreCase))

        return "Aborted";

    if (state.Equals("completed", StringComparison.OrdinalIgnoreCase))

        return "Complete";

    if (state.Equals("missing", StringComparison.OrdinalIgnoreCase))

        return "Missing setup";

    if (state.Equals("requested", StringComparison.OrdinalIgnoreCase))

        return "Requested";

    return state;

}




string GetLoadingSymbol()

{

    return ((runtime / 20) % 2 == 0) ? ".. ." : ". ..";

}




int GetMinerBehaviorSettingIndexForCarrierRow(int rowIndex)

{

    if (rowIndex < 2)

        return -1;

    if (caMinerBehaviorPage == 0 && rowIndex >= 2 && rowIndex <= 6)

        return 99 + rowIndex;

    if (caMinerBehaviorPage == 1)

    {

        if (rowIndex == 2)

            return 106;

        if (rowIndex == 3)

            return 107;

        if (rowIndex == 4)

            return 108;

        if (rowIndex == 5)

            return 109;

        if (rowIndex == 6)

            return 110;

        if (rowIndex == 7)

            return 112;

    }



    if (caMinerBehaviorPage == 2 && rowIndex == 2)

        return 111;

    return -1;

}



string GetMinerDetailStatus()
{

    MinerSlot miner = ActiveMiner();

    if (miner.HangarManagerPending == HangarManagerPendingCommand.Deploy

        || emma3MissionState == Emma3MissionState.WaitingHangarDeploy

        || manualHangarCycleState == ManualHangarCycleState.WaitingDeployReady)

        return "Deploying";

    if (miner.HangarManagerPending == HangarManagerPendingCommand.Enable

        || miner.HangarManagerPending == HangarManagerPendingCommand.Store
        || emma3MissionState == Emma3MissionState.WaitingHangarStore
        || emma3MissionState == Emma3MissionState.WaitingPostCompletionHangarStore
        || manualHangarCycleState == ManualHangarCycleState.WaitingStoreReady
        || manualHangarCycleState == ManualHangarCycleState.WaitingStoreOnlyReady)

        return "Storing";

    if (miner.HangarStored) return "Stored";
    if (miner.StatusAgeSeconds >= minerContactTimeoutSeconds) return "No contact";
    return string.IsNullOrWhiteSpace(miner.StatusText) ? "-" : miner.StatusText;
}


string GetMinerHangarContentsLabel()
{

    if (hangarBlock == null)

    {

        _lcdHangarInventoryData = null;

        _lcdHangarContentsLabel = "Unknown";

        return _lcdHangarContentsLabel;

    }

    string data = hangarBlock.CustomData ?? "";

    if (string.Equals(data, _lcdHangarInventoryData, StringComparison.Ordinal))

        return _lcdHangarContentsLabel;

    _lcdHangarInventoryData = data;

    _lcdHangarContentsLabel = "Unknown";

    MyIni ini = new MyIni();

    MyIniParseResult error;

    if (!ini.TryParse(data, out error)) return _lcdHangarContentsLabel;

    if (!ini.Get("HangarManagerInventory", "Status").ToString("").Equals("Ready", StringComparison.OrdinalIgnoreCase))

        return _lcdHangarContentsLabel;

    int count = ini.Get("HangarManagerInventory", "Count").ToInt32(-1);

    if (count >= 0) _lcdHangarContentsLabel = count > 0 ? "Filled" : "Empty";

    return _lcdHangarContentsLabel;
}


string GetMinerHangarMinerStateLabel()
{
    string contents = GetMinerHangarContentsLabel();
    if (contents == "Filled") return "Stored";
    if (contents == "Empty") return "Deployed";
    return "Unknown";
}



// Checks the blocks EMM-A needs to operate at all. Returns the display name of each one missing.

List<string> GetMissingRequirements()

{

    var missing = new List<string>();

    if (GridTerminalSystem.GetBlockWithName(carrierLcdName) as IMyTextPanel == null)

        missing.Add(carrierLcdName);

    if (GridTerminalSystem.GetBlockWithName(stateIniTimerTag) as IMyTimerBlock == null)

        missing.Add(stateIniTimerTag);

    if (GridTerminalSystem.GetBlockWithName(stateRouteTimerTag) as IMyTimerBlock == null)

        missing.Add(stateRouteTimerTag);

    if (GridTerminalSystem.GetBlockWithName(stateReportsTimerTag) as IMyTimerBlock == null)

        missing.Add(stateReportsTimerTag);

    if (FindEmma3BridgeTimer() == null)

        missing.Add(emma3BridgeTimerName);

    if (GridTerminalSystem.GetBlockWithName(oreNameTimerTag) as IMyTimerBlock == null)

        missing.Add(oreNameTimerTag);

    if (GridTerminalSystem.GetBlockWithName(emma3CompletedAsteroidsTimerName) as IMyTimerBlock == null)

        missing.Add(emma3CompletedAsteroidsTimerName);

    if (Me.CustomName.IndexOf(carrierPbTag, StringComparison.OrdinalIgnoreCase) < 0)

        missing.Add(carrierPbTag);

    IMyRemoteControl rcCheck = GridTerminalSystem.GetBlockWithName(carrierRemoteName) as IMyRemoteControl;

    if (rcCheck == null)

        missing.Add(carrierRemoteName);

    return missing;

}




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


void LoadCarrierItems()

{

    string raw = itemsTimer == null ? "" : (itemsTimer.CustomData ?? "");

    itemNames.Clear();

    itemBlueprints.Clear();

    itemsMenuError = itemsTimer == null ? "Missing EMM-A Ca Items timer." : "";

    var lines = new List<string>();

    foreach (string value in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))

    {

        string line = value.Trim();

        if (line.Length > 0 && !line.StartsWith("#") && !line.StartsWith("//"))

            lines.Add(line);

    }

    if (lines.Count % 2 != 0 || lines.Count > 256)

        itemsMenuError = "Items timer needs name/blueprint pairs (max 128).";

    for (int i = 0; i + 1 < lines.Count && i < 256; i += 2)

    {

        string id = lines[i + 1];

        if (!id.StartsWith("MyObjectBuilder_BlueprintDefinition/", StringComparison.Ordinal)

            || id.Length <= "MyObjectBuilder_BlueprintDefinition/".Length || id.IndexOf(',') >= 0 || itemBlueprints.Contains(id))

        {

            itemsMenuError = "Invalid or duplicate blueprint ID in Items timer.";

            continue;

        }

        itemNames.Add(lines[i]);

        itemBlueprints.Add(id);

    }

    PublishCarrierItems();

}




bool MinerDetailReadingsAvailable()

{

    MinerSlot miner = ActiveMiner();

    return miner.StatusAgeSeconds < minerContactTimeoutSeconds && !miner.HangarStored
        && miner.HangarManagerPending != HangarManagerPendingCommand.Deploy

        && emma3MissionState != Emma3MissionState.WaitingHangarDeploy

        && manualHangarCycleState != ManualHangarCycleState.WaitingDeployReady;

}




void MoveCarrierCursor(int delta)

{

    int count = Math.Max(1, GetCarrierMenuItemCount());

    caMenuIndex += delta;

    if (caMenuIndex < 0)

        caMenuIndex = count - 1;

    if (caMenuIndex >= count)

        caMenuIndex = 0;

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
    SetUiOreRaw(timer,string.Join("\n", _carrierOreLines).TrimEnd());
    _carrierOreRaw = null;
    RefreshCarrierOreCache();
    _carrierOreDisplayedRaw = _carrierOreRaw;
    caMenuIndex = target + 3;
}



void OpenCarrierMenu(CaLcdMenu menu)
{
    CancelCarrierOreReorder();
    caLcdMenu = menu;
    caMenuIndex = 0;

    if (menu == CaLcdMenu.BehaviorSettings)

        caBehaviorPage = 0;

    if (menu == CaLcdMenu.MinerBehaviorSettings)

        caMinerBehaviorPage = 0;

    CancelCarrierEdit();

}




void OpenCarrierThrustLimitMenu()

{

    caLcdMenu = CaLcdMenu.ThrustLimit;

    caMenuIndex = 2;

    caThrustEditLimitKn = thrustLimitKn;

    caThrustEditUsePercent = thrustUsePercent;

    CancelCarrierEdit();

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




bool PluginStateIsActivelyWorking()

{

    return emma3PluginState.Equals("scanning", StringComparison.OrdinalIgnoreCase) || emma3PluginState.Equals("planning", StringComparison.OrdinalIgnoreCase);

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




void RefreshCarrierOreCache()

{

    IMyTimerBlock timer = FindOreTimer();

    string raw = timer == null ? null : (GetUiOreRaw(timer) ?? "");

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


string ReportDuration(double seconds)

{

    long value = Math.Max(0, (long)seconds);

    return value >= 3600 ? (value / 3600) + ":" + ((value / 60) % 60).ToString("00") + ":" + (value % 60).ToString("00")

        : (value / 60) + ":" + (value % 60).ToString("00");

}




void SaveCarrierEdit()
{
    if (caEditingIndex == 1)
        maxSpeed = MathHelper.Clamp(caEditValue, 1, 50000);
    else if (caEditingIndex == 2)

        orientationToleranceDeg = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 3)

        arrivalTolerance = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 7)

        lcdRefreshIntervalTicks = Math.Max(lcdRefreshMinTicks, (int)Math.Round(caEditValue));

    else if (caEditingIndex == 9)

        shortRangeDistanceMeters = Math.Max(100, caEditValue);

    else if (caEditingIndex == 10)

        cruiseOffsetMeters = Math.Max(100, caEditValue);

    else if (caEditingIndex == 11)

        thrustLimitKn = Math.Max(1, caEditValue);

    else if (caEditingIndex == 12)

        thrustUsePercent = MathHelper.Clamp(caEditValue, 1, 100);

    else if (caEditingIndex == 13)

        accelerationLimit = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 16)

        emma3SearchRadius = MathHelper.Clamp(caEditValue, 1000, 50000);

    else if (caEditingIndex == 17)

        emma3ParkingHeight = Math.Max(25, caEditValue);

    else if (caEditingIndex == 18)

        emma3PlaneClearance = Math.Max(25, caEditValue);
    else if (caEditingIndex == 19)

        emma3TravelClearance = Math.Max(10, caEditValue);

    else if (caEditingIndex == 25)

        emma3IceReserveMillionLiters = Math.Max(1, Math.Min(1000000, caEditValue));

    else if (caEditingIndex == 101)

        minerStandardDepth = Math.Max(0, caEditValue);

    else if (caEditingIndex == 102)

        minerLaneWidth = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 103)

        minerLaneHeight = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 104)

        minerForwardSpeed = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 105)

        minerReturnSpeed = Math.Max(0.1, caEditValue);

    else if (caEditingIndex == 107)

        minerAutoServiceCargoPercent = MathHelper.Clamp(caEditValue, 0, 100);

    else if (caEditingIndex == 108)

        minerAutoServiceHydrogenPercent = MathHelper.Clamp(caEditValue, 0, 100);

    else if (caEditingIndex == 109)

        minerRouteStartExtra = MathHelper.Clamp(caEditValue, 0, 500);

    if ((caEditingIndex >= 16 && caEditingIndex <= 19) || caEditingIndex == 25)

        WriteEmma3Settings();

    if (caEditingIndex >= 101 && caEditingIndex <= 109)

        WriteMinerBehaviorSettings();

    CancelCarrierEdit();

}




void SaveCarrierThrustLimitMenu()

{

    thrustLimitKn = Math.Max(1, caThrustEditLimitKn);

    thrustUsePercent = MathHelper.Clamp(caThrustEditUsePercent, 1, 100);

    CloseCarrierThrustLimitMenu();

}




void SelectCarrierCursor()

{

    if (caLcdMenu == CaLcdMenu.Main)

    {

        if (caMenuIndex == 0)

        {

            OpenCarrierMenu(CaLcdMenu.AutonomousMining);

            caMenuIndex = 1;

        }

        else if (caMenuIndex == 1)

        {

            OpenCarrierMenu(CaLcdMenu.MinerOverview);

        }

        else if (caMenuIndex == 2)

        {

            OpenCarrierMenu(CaLcdMenu.BehaviorSettings);

        }

        else if (caMenuIndex == 3)

            OpenCarrierMenu(CaLcdMenu.Info);

        return;

    }



    if (caLcdMenu == CaLcdMenu.ThrustLimit && caMenuIndex == 0)

    {

        CloseCarrierThrustLimitMenu();

        return;

    }



    if (caMenuIndex == 0)

    {

        if (caLcdMenu == CaLcdMenu.SelectOres)

        {

            BackToCarrierAutonomousMiningMenu();

            return;

        }



        OpenCarrierMenu(CaLcdMenu.Main);

        return;

    }



    if (caLcdMenu == CaLcdMenu.AutonomousMining)

    {

        if (caMenuIndex == 1)

        {

            SetFullAutoEnabledFromUi(!fullAutoEnabled);

        }

        else if (caMenuIndex == 2)

        {

            OpenCarrierMenu(CaLcdMenu.SelectOres);

            caMenuIndex = 1;

        }

        else if (ShowFinishAndHold() && caMenuIndex == FinishAndHoldIndex())

        {

            finishAndHold = !finishAndHold;

            MarkProgressStateDirty();

        }

        else if (caMenuIndex == 3)

        {

            if (ShowContinueMenuItem())

                RequestEmma3Continue();

            else if (ShowRecallMinerMenuItem() && !ActiveMiner().RecallRequested)

                StartMinerRecall();

        }



        return;

    }



    if (caLcdMenu == CaLcdMenu.MinerOverview)
    {
        if (caMenuIndex == 1)
            OpenCarrierMenu(CaLcdMenu.MinerA);
        return;
    }


    if (caLcdMenu == CaLcdMenu.MinerA)

    {

        if (caMenuIndex == 1 && !ActiveMiner().RecallRequested)

            StartMinerRecall();

        return;

    }



    if (caLcdMenu == CaLcdMenu.MinerBehaviorSettings)

    {

        if (caMenuIndex == 1)
        {
            if (caMinerBehaviorPage < 2)
                caMinerBehaviorPage++;
            else
            {
                caLcdMenu = CaLcdMenu.BehaviorSettings;
                caBehaviorPage = 0;
            }
            caMenuIndex = 1;
            CancelCarrierEdit();
            return;
        }



        int minerSetting = GetMinerBehaviorSettingIndexForCarrierRow(caMenuIndex);

        if (minerSetting == 106)

        {

            minerAutoServiceEnabled = !minerAutoServiceEnabled;

            WriteMinerBehaviorSettings();

        }

        else if (minerSetting == 110)

        {

            minerMergeAssistedDocking = !minerMergeAssistedDocking;

            WriteMinerBehaviorSettings();

            SendCarrierStatusNow();

        }

        else if (minerSetting == 111)

        {

            minerHangarEnabled = !minerHangarEnabled;

            WriteMinerBehaviorSettings();

            ScriptLog("Hangar storage " + (minerHangarEnabled ? "enabled." : "disabled."));

        }

        else if (minerSetting == 112)

        {

            minerStopAtOnePercentFuel = !minerStopAtOnePercentFuel;

            WriteMinerBehaviorSettings();

        }

        else if (minerSetting > 0)

            BeginCarrierEdit(minerSetting);

        return;

    }



    if (caLcdMenu == CaLcdMenu.Info)

    {

        if (caMenuIndex == 1)

            OpenCarrierMenu(CaLcdMenu.ResetAsteroidsConfirm);

        return;

    }



    if (caLcdMenu == CaLcdMenu.SelectOres)
    {
        if (caRefineryOreMoving)
        {
            EndCarrierOreMove();
            return;
        }
        if (caMenuIndex == 1)
        {
            CancelCarrierOreReorder();
            emma3ItemsMode = !emma3ItemsMode;
            WriteEmma3Settings();
        }
        else if (emma3ItemsMode)
            ToggleCarrierItem(caMenuIndex - 2);
        else if (caMenuIndex == 2)
            caRefineryReorderArmed = !caRefineryReorderArmed;
        else if (caRefineryReorderArmed)
            BeginCarrierOreMove(caMenuIndex - 3);
        else
            ToggleCarrierOre(caMenuIndex - 3);
        return;
    }



    if (caLcdMenu == CaLcdMenu.BehaviorSettings)

    {

        if (caMenuIndex == 1)
        {
            if (caBehaviorPage < 4)
                caBehaviorPage++;
            else
            {
                caLcdMenu = CaLcdMenu.MinerBehaviorSettings;
                caMinerBehaviorPage = 0;
            }
            caMenuIndex = 1;
            CancelCarrierEdit();
            return;
        }



        int settingIndex = GetCarrierBehaviorSettingIndexForRow(caMenuIndex);

        if (settingIndex == 11)

            OpenCarrierThrustLimitMenu();

        else if (settingIndex == 14)

            turnTestEnabled = !turnTestEnabled;

        else if (settingIndex == 20)

        {

            emma3Visualization = !emma3Visualization;

            WriteEmma3Settings();

        }

        else if (settingIndex == 21)

        {

            emma3AllowMovement = !emma3AllowMovement;

            WriteEmma3Settings();

        }

        else if (settingIndex == 22)

        {

            emma3ScriptLogging = !emma3ScriptLogging;

            if (!emma3ScriptLogging)

                ClearScriptLogQueue();

            WriteEmma3Settings();

            SendCarrierStatusNow();

            if (emma3ScriptLogging)

                ScriptLog("Carrier and Miner script logging enabled from LCD.");

        }

        else if (settingIndex == 23)

        {

            emma3AllowSeatExitDuringMining = !emma3AllowSeatExitDuringMining;

            WriteEmma3Settings();

            ScriptLog("Seat exit during mining " + (emma3AllowSeatExitDuringMining ? "allowed." : "blocked."));

        }

        else if (settingIndex == 24)

        {

            emma3IceReserveEnabled = !emma3IceReserveEnabled;

            WriteEmma3Settings();

        }

        else if (settingIndex == 26)

        {

            emma3PluginOreTransfer = !emma3PluginOreTransfer;

            WriteEmma3Settings();

        }

        else if (settingIndex == 27)

        {

            emma3PluginPowerTransitions = !emma3PluginPowerTransitions;

            WriteEmma3Settings();

        }

        else if (settingIndex == 28)

        {

            emma3PluginRemoteLink = !emma3PluginRemoteLink;

            WriteEmma3Settings();

        }

        else if (settingIndex == 29)

        {

            emma3PluginLcdOutput = !emma3PluginLcdOutput;

            pluginLcdHeartbeatAgeTicks = int.MaxValue;

            pluginLcdReady = false;

            carrierLcdLastNativeText = "";

            reportLastNativeText = "";

            WriteEmma3Settings();

        }

        else if (settingIndex == 30)

        {

            SetTimerPersistence(!timerPersistenceEnabled);

            WriteEmma3Settings();

            SendCarrierStatusNow();

        }

        else if (settingIndex > 0)

            BeginCarrierEdit(settingIndex);

        return;

    }



    if (caLcdMenu == CaLcdMenu.ThrustLimit)

    {

        if (caMenuIndex == 1)

        {

            SaveCarrierThrustLimitMenu();

            return;

        }



        if (caMenuIndex == 2)

            BeginCarrierThrustLimitEdit(11);

        else if (caMenuIndex == 3)

            BeginCarrierThrustLimitEdit(15);

        else if (caMenuIndex == 4)

            BeginCarrierThrustLimitEdit(12);

        return;

    }



    if (caLcdMenu == CaLcdMenu.ResetAsteroidsConfirm)

    {

        if (caMenuIndex == 1)

        {

            RequestEmma3Reset();

            OpenCarrierMenu(CaLcdMenu.Info);

            caMenuIndex = 1;

        }



        return;

    }

}




bool ShouldUpdateCarrierInfoCache()

{

    return caLcdMenu == CaLcdMenu.Info;

}




bool ShowContinueMenuItem()

{

    return emma3PluginState.Equals("aborted", StringComparison.OrdinalIgnoreCase) && emma3ContinueAvailable;

}


bool ShowFinishAndHold()

{

    return fullAutoEnabled && emma3ActiveEntityId != 0 &&

        (emma3MissionState == Emma3MissionState.WaitingStationary

        || emma3MissionState == Emma3MissionState.WaitingMiner

        || emma3MissionState == Emma3MissionState.WaitingHangarDeploy);

}




bool ShowRecallMinerMenuItem()

{

    return emma3MissionState == Emma3MissionState.WaitingMiner && (!RecallStateMatchesActiveMission() || !ActiveMiner().RecallAcknowledged) && !ActiveMiner().PathCompleteReceived;

}




void ShowStartupScreen(List<string> missing)

{

    if (carrierLcd == null)

        carrierLcd = GridTerminalSystem.GetBlockWithName(carrierLcdName) as IMyTextPanel;

    if (carrierLcd == null)

    {

        Echo(shipNames.Carrier + " | Startup\n-  Add or rename an LCD to\n   " + carrierLcdName);

        return;

    }



    ConfigureCarrierLcd();

    StringBuilder sb = _lcdSb;

    sb.Clear();

    sb.Append(' ').Append(shipNames.Carrier).AppendLine(" | Startup");

    sb.AppendLine("—————————————————");

    for (int i = 0; i < missing.Count; i++)

    {

        if (missing[i] == carrierLcdName)

            continue;

        if (missing[i] == carrierPbTag)

        {

            sb.AppendLine("-  Rename EMM-A's prog. block to");

            sb.AppendLine("   " + carrierPbTag);

        }

        else

            sb.AppendLine("-  " + missing[i]);

    }



    while (CountLines(sb) < 11)

        sb.AppendLine();

    sb.AppendLine("—————————————————");

    sb.Append(" ");

    sb.AppendLine("Missing requirements.");

    sb.Append(" ");

    sb.Append(Runtime.LastRunTimeMs.ToString("0.000"));

    sb.Append("ms");

    carrierLcd.WriteText(sb.ToString(), false);

}




void ToggleCarrierItem(int index)

{

    if (index < 0 || index >= itemBlueprints.Count || itemsMenuError.Length > 0)

        return;

    string id = itemBlueprints[index];

    var enabled = new List<string>(emma3EnabledItems.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

    if (!enabled.Remove(id))

        enabled.Add(id);

    emma3EnabledItems = string.Join(",", enabled);

    WriteEmma3Settings();

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

    SetUiOreRaw(oreTimer,string.Join("\n", _carrierOreLines).TrimEnd());

    RefreshCarrierOreCache();

    ScriptLog("Ore state: " + row.Name + " set to " + next + ".");

}


bool TryHandleCarrierLcdCommand(string trimmed)

{

    if (trimmed.Equals("up", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("w", StringComparison.OrdinalIgnoreCase))
    {
        if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryOreMoving)
            MoveCarrierOre(-1);
        else if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit)
            AdjustCarrierThrustLimitEdit(1);

        else if (caEditingValue)

            AdjustCarrierEdit(1);

        else

            MoveCarrierCursor(-1);

        UpdateCarrierLcd();

        return true;

    }



    if (trimmed.Equals("down", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("s", StringComparison.OrdinalIgnoreCase))
    {
        if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryOreMoving)
            MoveCarrierOre(1);
        else if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit)
            AdjustCarrierThrustLimitEdit(-1);

        else if (caEditingValue)

            AdjustCarrierEdit(-1);

        else

            MoveCarrierCursor(1);

        UpdateCarrierLcd();

        return true;

    }



    if (trimmed.Equals("apply", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("e", StringComparison.OrdinalIgnoreCase))

    {

        if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit)

            EndCarrierThrustLimitEdit();

        else if (caEditingValue)

            SaveCarrierEdit();

        else

            SelectCarrierCursor();

        UpdateCarrierLcd();

        return true;

    }



    if (trimmed.Equals("back", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("c", StringComparison.OrdinalIgnoreCase))
    {
        if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryOreMoving)
            CancelCarrierOreMove();
        else if (caLcdMenu == CaLcdMenu.SelectOres && caRefineryReorderArmed)
            caRefineryReorderArmed = false;
        else if (caEditingValue)
            CancelCarrierEdit();

        else if (caLcdMenu == CaLcdMenu.ThrustLimit)

            CloseCarrierThrustLimitMenu();

        else if (caLcdMenu == CaLcdMenu.SelectOres)

            BackToCarrierAutonomousMiningMenu();

        else

        {

            caLcdMenu = CaLcdMenu.Main;

            caMenuIndex = 0;

        }



        UpdateCarrierLcd();

        return true;

    }



    if (trimmed.Equals("cursortoggle", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("cursor toggle", StringComparison.OrdinalIgnoreCase))

    {

        UpdateCarrierLcd();

        ScriptLog("LCD commands: up, down, apply, back.");

        return true;

    }



    return false;

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




void UpdateCarrierLcd()

{

    if (carrierLcd == null)

        return;

    bool pluginOutput = PluginLcdTakeoverActive();

    if (!pluginOutput)

        ConfigureCarrierLcd();

    int itemCount = Math.Max(1, GetCarrierMenuItemCount());

    if (caMenuIndex >= itemCount)

        caMenuIndex = itemCount - 1;

    StringBuilder sb = _lcdSb;

    sb.Clear();

    AppendCarrierHeader(sb);

    AppendCarrierRows(sb);

    AppendCarrierFooter(sb);

    string text = sb.ToString();

    if (!string.Equals(text, carrierLcdLastText, StringComparison.Ordinal))

    {

        carrierLcdLastText = text;

        UpdatePluginLcdPayload(text, pluginLcdReportText);

    }

    if (!pluginOutput && !string.Equals(text, carrierLcdLastNativeText, StringComparison.Ordinal))

    {

        if (!TryClaimCarrierHeavyWork())

            return;

        if (carrierLcd.WriteText(text, false))

            carrierLcdLastNativeText = text;

    }

    carrierLcdStateFingerprint = GetCarrierLcdStateFingerprint();

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


bool TryClaimCarrierHeavyWork() { return uiHost.TryClaimCarrierHeavyWork(); }

MinerSlot ActiveMiner() { return uiMiner; }

void ScriptLog(string text) { uiHost.ScriptLog(text); }

void ClearScriptLogQueue() { Intent("clearlog"); }

bool PluginLcdTakeoverActive() { return emma3PluginLcdOutput && pluginLcdReady && pluginLcdHeartbeatAgeTicks <= 720; }

void UpdatePluginLcdPayload(string main, string report) { pluginLcdMainText=main??"";pluginLcdReportText=report??""; }

void ConfigureCarrierLcd() { 

    if (carrierLcd == null)

        return;

    if (carrierLcdConfigured)

        return;

    carrierLcd.ContentType = ContentType.TEXT_AND_IMAGE;

    carrierLcd.Font = "Debug";

    carrierLcd.FontSize = 1.225f;

    carrierLcd.TextPadding = 0f;

    carrierLcd.Alignment = TextAlignment.LEFT;

    carrierLcdConfigured = true;

 }

void SendCarrierStatusNow() { Intent("status"); }

void SetFullAutoEnabledFromUi(bool enabled) { Intent(enabled ? "am:on" : "am:off"); }

bool RecallStateMatchesActiveMission() { return uiMission.Length>0 && uiMiner.RecallMissionId==uiMission; }

void StartMinerRecall() { Intent("recall"); }

void PublishCarrierItems() { Intent("items"); }

double GetEffectiveThrustInDirection(Vector3D direction) { return uiHost.GetEffectiveThrustInDirection(direction); }

double GetDetectedMaxCarrierThrustN() { return uiHost.GetDetectedMaxCarrierThrustN(); }

Vector3D GetControlledPosition() { return uiHost.GetControlledPosition(); }

IMyTimerBlock FindEmma3BridgeTimer() { return GridTerminalSystem.GetBlockWithName(emma3BridgeTimerName) as IMyTimerBlock; }

IMyTimerBlock FindOreTimer() { return GridTerminalSystem.GetBlockWithName(oreNameTimerTag) as IMyTimerBlock; }

void WriteEmma3Settings() { Intent("settings"); }

void WriteMinerBehaviorSettings() { Intent("settings"); }

void RequestEmma3Continue() { Intent("continue"); }

void RequestEmma3Reset() { Intent("reset"); }

bool IsCruiseTurnMeasurementValid() { return uiHost.IsCruiseTurnMeasurementValid(); }

bool TryParseNumber(string text, out double value) { return uiHost.TryParseNumber(text,out value); }

void SetTimerPersistence(bool enabled) { timerPersistenceEnabled=enabled;Intent("settings"); }

void MarkProgressStateDirty() { Intent("settings"); }

void Intent(string action) { uiActions += action+"\n"; }
public void ReadSnapshot(MyIni ini) {
 uiOreDraft=null;uiOreBefore=null;
maxSpeed=ini.Get("Ui","maxSpeed").ToDouble(maxSpeed);
arrivalTolerance=ini.Get("Ui","arrivalTolerance").ToDouble(arrivalTolerance);
orientationToleranceDeg=ini.Get("Ui","orientationToleranceDeg").ToDouble(orientationToleranceDeg);
emma3SearchRadius=ini.Get("Ui","emma3SearchRadius").ToDouble(emma3SearchRadius);
emma3ParkingHeight=ini.Get("Ui","emma3ParkingHeight").ToDouble(emma3ParkingHeight);
emma3PlaneClearance=ini.Get("Ui","emma3PlaneClearance").ToDouble(emma3PlaneClearance);
emma3TravelClearance=ini.Get("Ui","emma3TravelClearance").ToDouble(emma3TravelClearance);
emma3Visualization=ini.Get("Ui","emma3Visualization").ToBoolean(emma3Visualization);
emma3AllowMovement=ini.Get("Ui","emma3AllowMovement").ToBoolean(emma3AllowMovement);
emma3ScriptLogging=ini.Get("Ui","emma3ScriptLogging").ToBoolean(emma3ScriptLogging);
emma3AllowSeatExitDuringMining=ini.Get("Ui","emma3AllowSeatExitDuringMining").ToBoolean(emma3AllowSeatExitDuringMining);
emma3IceReserveEnabled=ini.Get("Ui","emma3IceReserveEnabled").ToBoolean(emma3IceReserveEnabled);
emma3IceReserveMillionLiters=ini.Get("Ui","emma3IceReserveMillionLiters").ToDouble(emma3IceReserveMillionLiters);
emma3PluginOreTransfer=ini.Get("Ui","emma3PluginOreTransfer").ToBoolean(emma3PluginOreTransfer);
emma3PluginPowerTransitions=ini.Get("Ui","emma3PluginPowerTransitions").ToBoolean(emma3PluginPowerTransitions);
emma3PluginRemoteLink=ini.Get("Ui","emma3PluginRemoteLink").ToBoolean(emma3PluginRemoteLink);
emma3PluginLcdOutput=ini.Get("Ui","emma3PluginLcdOutput").ToBoolean(emma3PluginLcdOutput);
emma3ItemsMode=ini.Get("Ui","emma3ItemsMode").ToBoolean(emma3ItemsMode);
emma3EnabledItems=ini.Get("Ui","emma3EnabledItems").ToString(emma3EnabledItems);
itemsMenuError=ini.Get("Ui","itemsMenuError").ToString(itemsMenuError);
emma3ProductionStatus=ini.Get("Ui","emma3ProductionStatus").ToString(emma3ProductionStatus);
minerStandardDepth=ini.Get("Ui","minerStandardDepth").ToDouble(minerStandardDepth);
minerLaneWidth=ini.Get("Ui","minerLaneWidth").ToDouble(minerLaneWidth);
minerLaneHeight=ini.Get("Ui","minerLaneHeight").ToDouble(minerLaneHeight);
minerForwardSpeed=ini.Get("Ui","minerForwardSpeed").ToDouble(minerForwardSpeed);
minerReturnSpeed=ini.Get("Ui","minerReturnSpeed").ToDouble(minerReturnSpeed);
minerAutoServiceEnabled=ini.Get("Ui","minerAutoServiceEnabled").ToBoolean(minerAutoServiceEnabled);
minerAutoServiceCargoPercent=ini.Get("Ui","minerAutoServiceCargoPercent").ToDouble(minerAutoServiceCargoPercent);
minerAutoServiceHydrogenPercent=ini.Get("Ui","minerAutoServiceHydrogenPercent").ToDouble(minerAutoServiceHydrogenPercent);
minerStopAtOnePercentFuel=ini.Get("Ui","minerStopAtOnePercentFuel").ToBoolean(minerStopAtOnePercentFuel);
minerRouteStartExtra=ini.Get("Ui","minerRouteStartExtra").ToDouble(minerRouteStartExtra);
minerMergeAssistedDocking=ini.Get("Ui","minerMergeAssistedDocking").ToBoolean(minerMergeAssistedDocking);
minerHangarEnabled=ini.Get("Ui","minerHangarEnabled").ToBoolean(minerHangarEnabled);
shortRangeDistanceMeters=ini.Get("Ui","shortRangeDistanceMeters").ToDouble(shortRangeDistanceMeters);
cruiseOffsetMeters=ini.Get("Ui","cruiseOffsetMeters").ToDouble(cruiseOffsetMeters);
thrustLimitKn=ini.Get("Ui","thrustLimitKn").ToDouble(thrustLimitKn);
thrustUsePercent=ini.Get("Ui","thrustUsePercent").ToDouble(thrustUsePercent);
accelerationLimit=ini.Get("Ui","accelerationLimit").ToDouble(accelerationLimit);
turnTestEnabled=ini.Get("Ui","turnTestEnabled").ToBoolean(turnTestEnabled);
measuredFlipSeconds=ini.Get("Ui","measuredFlipSeconds").ToDouble(measuredFlipSeconds);
measuredFlipMass=ini.Get("Ui","measuredFlipMass").ToDouble(measuredFlipMass);
pluginLcdHeartbeatAgeTicks=ini.Get("Ui","pluginLcdHeartbeatAgeTicks").ToInt32(pluginLcdHeartbeatAgeTicks);
pluginLcdReady=ini.Get("Ui","pluginLcdReady").ToBoolean(pluginLcdReady);
lcdRefreshIntervalTicks=ini.Get("Ui","lcdRefreshIntervalTicks").ToInt32(lcdRefreshIntervalTicks);
fullAutoEnabled=ini.Get("Ui","fullAutoEnabled").ToBoolean(fullAutoEnabled);
emma3MissionState=(Emma3MissionState)ini.Get("Ui","emma3MissionState").ToInt32((int)emma3MissionState);
manualHangarCycleState=(ManualHangarCycleState)ini.Get("Ui","manualHangarCycleState").ToInt32((int)manualHangarCycleState);
emma3ActiveEntityId=ini.Get("Ui","emma3ActiveEntityId").ToInt64(emma3ActiveEntityId);
emma3PluginState=ini.Get("Ui","emma3PluginState").ToString(emma3PluginState);
emma3PluginMessage=ini.Get("Ui","emma3PluginMessage").ToString(emma3PluginMessage);
emma3ContinueAvailable=ini.Get("Ui","emma3ContinueAvailable").ToBoolean(emma3ContinueAvailable);
emma3CompletedCount=ini.Get("Ui","emma3CompletedCount").ToInt32(emma3CompletedCount);
emma3CarrierRouteIndex=ini.Get("Ui","emma3CarrierRouteIndex").ToInt32(emma3CarrierRouteIndex);
fullAutoEnableStationaryTicks=ini.Get("Ui","fullAutoEnableStationaryTicks").ToInt32(fullAutoEnableStationaryTicks);
reportAsteroid=ini.Get("Ui","reportAsteroid").ToInt64(reportAsteroid);
reportCompleted=ini.Get("Ui","reportCompleted").ToInt32(reportCompleted);
reportVisible=ini.Get("Ui","reportVisible").ToBoolean(reportVisible);
finishAndHold=ini.Get("Ui","finishAndHold").ToBoolean(finishAndHold);
timerPersistenceEnabled=ini.Get("Ui","timerPersistenceEnabled").ToBoolean(timerPersistenceEnabled);
uiMiner.PathCompleteReceived=ini.Get("UiMiner","PathCompleteReceived").ToBoolean(uiMiner.PathCompleteReceived);
uiMiner.ProgressMeters=ini.Get("UiMiner","ProgressMeters").ToDouble(uiMiner.ProgressMeters);
uiMiner.ProgressTotalMeters=ini.Get("UiMiner","ProgressTotalMeters").ToDouble(uiMiner.ProgressTotalMeters);
uiMiner.StatusText=ini.Get("UiMiner","StatusText").ToString(uiMiner.StatusText);
uiMiner.StatusCargoPercent=ini.Get("UiMiner","StatusCargoPercent").ToDouble(uiMiner.StatusCargoPercent);
uiMiner.StatusWaterPercent=ini.Get("UiMiner","StatusWaterPercent").ToDouble(uiMiner.StatusWaterPercent);
uiMiner.StatusAgeSeconds=ini.Get("UiMiner","StatusAgeSeconds").ToDouble(uiMiner.StatusAgeSeconds);
uiMiner.RecallRequested=ini.Get("UiMiner","RecallRequested").ToBoolean(uiMiner.RecallRequested);
uiMiner.RecallAcknowledged=ini.Get("UiMiner","RecallAcknowledged").ToBoolean(uiMiner.RecallAcknowledged);
uiMiner.HangarStored=ini.Get("UiMiner","HangarStored").ToBoolean(uiMiner.HangarStored);
uiMiner.HangarManagerPending=(HangarManagerPendingCommand)ini.Get("UiMiner","HangarManagerPending").ToInt32((int)uiMiner.HangarManagerPending);
 uiMission=ini.Get("Ui","mission").ToString("");
 asteroidReport=MiningReportData.Decode(ini.Get("Ui","asteroidReport").ToString(""));
 miningTotals=MiningReportData.Decode(ini.Get("Ui","miningTotals").ToString(""));
 emma3CarrierRoutePositions.Clear();
 int count=ini.Get("Ui","routeCount").ToInt32(0);
 for(int i=0;i<Math.Min(count,1024);i++)emma3CarrierRoutePositions.Add(Vector3D.Zero);
 targetPosition=uiHost.targetPosition; _tickRcMatrix=uiHost._tickRcMatrix;
}
public void Render() {
 runtime=uiHost.runtime; _tickRcMatrix=uiHost._tickRcMatrix;
 UpdateCarrierInfoCache(); UpdateCarrierLcd(); UpdateReportLcd();
 uiHost.SendUiDisplay(pluginLcdMainText,pluginLcdReportText);
}
public string Input(string command) {
 MyIni before=new MyIni();WriteEditable(before);
 uiOreDraft=null;uiOreBefore=null;
 uiActions="";
 if(command.StartsWith("menu:",StringComparison.Ordinal)) OpenCarrierMenu((CaLcdMenu)int.Parse(command.Substring(5)));
 else TryHandleCarrierLcdCommand(command);
 MyIni ini=new MyIni();WriteEditable(ini);
 List<MyIniKey> keys=new List<MyIniKey>();ini.GetKeys(keys);
 foreach(MyIniKey key in keys) if(before.Get(key).ToString()==ini.Get(key).ToString()) ini.Delete(key);
 ini.Set("UiReply","actions",uiActions);
 if(uiOreDraft!=null){ini.Set("UiReply","oreBefore",uiOreBefore);ini.Set("UiReply","oreAfter",uiOreDraft);}
 return ini.ToString();
}
void WriteEditable(MyIni ini) {
ini.Set("UiEdit","maxSpeed",maxSpeed);
ini.Set("UiEdit","arrivalTolerance",arrivalTolerance);
ini.Set("UiEdit","orientationToleranceDeg",orientationToleranceDeg);
ini.Set("UiEdit","emma3SearchRadius",emma3SearchRadius);
ini.Set("UiEdit","emma3ParkingHeight",emma3ParkingHeight);
ini.Set("UiEdit","emma3PlaneClearance",emma3PlaneClearance);
ini.Set("UiEdit","emma3TravelClearance",emma3TravelClearance);
ini.Set("UiEdit","emma3Visualization",emma3Visualization);
ini.Set("UiEdit","emma3AllowMovement",emma3AllowMovement);
ini.Set("UiEdit","emma3ScriptLogging",emma3ScriptLogging);
ini.Set("UiEdit","emma3AllowSeatExitDuringMining",emma3AllowSeatExitDuringMining);
ini.Set("UiEdit","emma3IceReserveEnabled",emma3IceReserveEnabled);
ini.Set("UiEdit","emma3IceReserveMillionLiters",emma3IceReserveMillionLiters);
ini.Set("UiEdit","emma3PluginOreTransfer",emma3PluginOreTransfer);
ini.Set("UiEdit","emma3PluginPowerTransitions",emma3PluginPowerTransitions);
ini.Set("UiEdit","emma3PluginRemoteLink",emma3PluginRemoteLink);
ini.Set("UiEdit","emma3PluginLcdOutput",emma3PluginLcdOutput);
ini.Set("UiEdit","emma3ItemsMode",emma3ItemsMode);
ini.Set("UiEdit","emma3EnabledItems",emma3EnabledItems);
ini.Set("UiEdit","minerStandardDepth",minerStandardDepth);
ini.Set("UiEdit","minerLaneWidth",minerLaneWidth);
ini.Set("UiEdit","minerLaneHeight",minerLaneHeight);
ini.Set("UiEdit","minerForwardSpeed",minerForwardSpeed);
ini.Set("UiEdit","minerReturnSpeed",minerReturnSpeed);
ini.Set("UiEdit","minerAutoServiceEnabled",minerAutoServiceEnabled);
ini.Set("UiEdit","minerAutoServiceCargoPercent",minerAutoServiceCargoPercent);
ini.Set("UiEdit","minerAutoServiceHydrogenPercent",minerAutoServiceHydrogenPercent);
ini.Set("UiEdit","minerStopAtOnePercentFuel",minerStopAtOnePercentFuel);
ini.Set("UiEdit","minerRouteStartExtra",minerRouteStartExtra);
ini.Set("UiEdit","minerMergeAssistedDocking",minerMergeAssistedDocking);
ini.Set("UiEdit","minerHangarEnabled",minerHangarEnabled);
ini.Set("UiEdit","shortRangeDistanceMeters",shortRangeDistanceMeters);
ini.Set("UiEdit","cruiseOffsetMeters",cruiseOffsetMeters);
ini.Set("UiEdit","thrustLimitKn",thrustLimitKn);
ini.Set("UiEdit","thrustUsePercent",thrustUsePercent);
ini.Set("UiEdit","accelerationLimit",accelerationLimit);
ini.Set("UiEdit","turnTestEnabled",turnTestEnabled);
ini.Set("UiEdit","lcdRefreshIntervalTicks",lcdRefreshIntervalTicks);
ini.Set("UiEdit","finishAndHold",finishAndHold);
ini.Set("UiEdit","timerPersistenceEnabled",timerPersistenceEnabled);
}
public void WriteDraft(MyIni ini) {
ini.Set("UiDraft","caMenuIndex",caMenuIndex);
ini.Set("UiDraft","caBehaviorPage",caBehaviorPage);
ini.Set("UiDraft","caMinerBehaviorPage",caMinerBehaviorPage);
ini.Set("UiDraft","caEditingValue",caEditingValue);
ini.Set("UiDraft","caRefineryReorderArmed",caRefineryReorderArmed);
ini.Set("UiDraft","caRefineryOreMoving",caRefineryOreMoving);
ini.Set("UiDraft","caRefineryMoveOriginalIndex",caRefineryMoveOriginalIndex);
ini.Set("UiDraft","caEditingIndex",caEditingIndex);
ini.Set("UiDraft","caEditValue",caEditValue);
ini.Set("UiDraft","caThrustEditLimitKn",caThrustEditLimitKn);
ini.Set("UiDraft","caThrustEditUsePercent",caThrustEditUsePercent);
ini.Set("UiDraft","caThrustStepIndex",caThrustStepIndex);
ini.Set("UiDraft","cachedCarrierShipHeight",cachedCarrierShipHeight);
ini.Set("UiDraft","cachedCarrierShipWidth",cachedCarrierShipWidth);
ini.Set("UiDraft","cachedCarrierShipLength",cachedCarrierShipLength);
ini.Set("UiDraft","cachedCarrierHydrogenPercent",cachedCarrierHydrogenPercent);
ini.Set("UiDraft","cachedCarrierCargoPercent",cachedCarrierCargoPercent);
ini.Set("UiDraft","carrierInfoRefreshTicks",carrierInfoRefreshTicks);
ini.Set("UiDraft","carrierInfoRefreshIntervalTicks",carrierInfoRefreshIntervalTicks);
ini.Set("UiDraft","_carrierOreMoveOriginalRaw",_carrierOreMoveOriginalRaw);
ini.Set("UiDraft","caLcdMenu",(int)caLcdMenu);
}
public void ReadDraft(MyIni ini) {
caMenuIndex=ini.Get("UiDraft","caMenuIndex").ToInt32(caMenuIndex);
caBehaviorPage=ini.Get("UiDraft","caBehaviorPage").ToInt32(caBehaviorPage);
caMinerBehaviorPage=ini.Get("UiDraft","caMinerBehaviorPage").ToInt32(caMinerBehaviorPage);
caEditingValue=ini.Get("UiDraft","caEditingValue").ToBoolean(caEditingValue);
caRefineryReorderArmed=ini.Get("UiDraft","caRefineryReorderArmed").ToBoolean(caRefineryReorderArmed);
caRefineryOreMoving=ini.Get("UiDraft","caRefineryOreMoving").ToBoolean(caRefineryOreMoving);
caRefineryMoveOriginalIndex=ini.Get("UiDraft","caRefineryMoveOriginalIndex").ToInt32(caRefineryMoveOriginalIndex);
caEditingIndex=ini.Get("UiDraft","caEditingIndex").ToInt32(caEditingIndex);
caEditValue=ini.Get("UiDraft","caEditValue").ToDouble(caEditValue);
caThrustEditLimitKn=ini.Get("UiDraft","caThrustEditLimitKn").ToDouble(caThrustEditLimitKn);
caThrustEditUsePercent=ini.Get("UiDraft","caThrustEditUsePercent").ToDouble(caThrustEditUsePercent);
caThrustStepIndex=ini.Get("UiDraft","caThrustStepIndex").ToInt32(caThrustStepIndex);
cachedCarrierShipHeight=ini.Get("UiDraft","cachedCarrierShipHeight").ToDouble(cachedCarrierShipHeight);
cachedCarrierShipWidth=ini.Get("UiDraft","cachedCarrierShipWidth").ToDouble(cachedCarrierShipWidth);
cachedCarrierShipLength=ini.Get("UiDraft","cachedCarrierShipLength").ToDouble(cachedCarrierShipLength);
cachedCarrierHydrogenPercent=ini.Get("UiDraft","cachedCarrierHydrogenPercent").ToDouble(cachedCarrierHydrogenPercent);
cachedCarrierCargoPercent=ini.Get("UiDraft","cachedCarrierCargoPercent").ToDouble(cachedCarrierCargoPercent);
carrierInfoRefreshTicks=ini.Get("UiDraft","carrierInfoRefreshTicks").ToInt32(carrierInfoRefreshTicks);
_carrierOreMoveOriginalRaw=ini.Get("UiDraft","_carrierOreMoveOriginalRaw").ToString(_carrierOreMoveOriginalRaw);
caLcdMenu=(CaLcdMenu)ini.Get("UiDraft","caLcdMenu").ToInt32((int)caLcdMenu);
}
string uiOreDraft,uiOreBefore;
string GetUiOreRaw(IMyTimerBlock timer) {return uiOreDraft??timer.CustomData??"";}
void SetUiOreRaw(IMyTimerBlock timer,string raw) {if(uiOreBefore==null)uiOreBefore=timer.CustomData??"";uiOreDraft=raw;}
}
