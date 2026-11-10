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

void MoveCarrierCursor(int delta)
{
    int count = Math.Max(1, GetCarrierMenuItemCount());
    caMenuIndex += delta;
    if (caMenuIndex < 0)
        caMenuIndex = count - 1;
    if (caMenuIndex >= count)
        caMenuIndex = 0;
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

void BackToCarrierAutonomousMiningMenu()
{
    OpenCarrierMenu(CaLcdMenu.AutonomousMining);
    caMenuIndex = 2;
}

void OpenCarrierThrustLimitMenu()
{
    caLcdMenu = CaLcdMenu.ThrustLimit;
    caMenuIndex = 2;
    caThrustEditLimitKn = thrustLimitKn;
    caThrustEditUsePercent = thrustUsePercent;
    CancelCarrierEdit();
}

void CloseCarrierThrustLimitMenu()
{
    caLcdMenu = CaLcdMenu.BehaviorSettings;
    caBehaviorPage = 1;
    caMenuIndex = 6;
    CancelCarrierEdit();
}

void SaveCarrierThrustLimitMenu()
{
    thrustLimitKn = Math.Max(1, caThrustEditLimitKn);
    thrustUsePercent = MathHelper.Clamp(caThrustEditUsePercent, 1, 100);
    CloseCarrierThrustLimitMenu();
}

void SetFullAutoEnabledFromUi(bool enabled)
{
    bool wasEnabled = fullAutoEnabled;
    bool hadEmma3Mission = emma3MissionState != Emma3MissionState.None;
    if (enabled && !fullAutoEnabled)
    {
        double speed = rc == null ? 0 : flightCache.LinearVelocity.Length();
        if (speed > Math.Max(0, mineStationarySpeedMps))
        {
            fullAutoEnableStationaryTicks = 0;
            ScriptLog($"Fullauto denied: carrier must be stationary ({speed:0.00} m/s).");
            return;
        }

        if (fullAutoEnableStationaryTicks < Math.Max(1, mineStationaryConfirmTicks))
        {
            fullAutoEnableStationaryTicks = 1;
            ScriptLog("Fullauto waiting for stationary confirmation.");
            return;
        }
    }

    fullAutoEnableStationaryTicks = 0;
    if (enabled && !fullAutoEnabled)
        ClearConflictingAutomationForEmma3();
    fullAutoEnabled = enabled;
    WriteEmma3Settings();
    if (!enabled)
    {
        StopParkedReportClock();
        reportApproachSeconds = 0;
        finishAndHold = false;
        EndCarrierTransitFlightCache();
        emma3MissionState = Emma3MissionState.None;
        emma3WaitForSeatBeforeNextPlan = false;
        if (wasEnabled || hadEmma3Mission)
            RequestEmma3PlannerAbort("Autonomous mining stopped.");
    }

    ScriptLog("Fullauto: " + (enabled ? "enabled." : "stopped."));
}

void RequestEmma3PlannerAbort(string reason)
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        return;
    emma3RequestSequence = Math.Max(emma3RequestSequence, ini.Get("EMMA3", "request").ToInt32(0)) + 1;
    ini.Set("EMMA3", "request", emma3RequestSequence);
    ini.Set("EMMA3", "command", "abort");
    ini.Set("EMMA3", "state", "aborted");
    ini.Set("EMMA3", "message", reason);
    WriteEmma3Bridge(ini);
}

void ClearConflictingAutomationForEmma3()
{
    EndCarrierTransitFlightCache();
    autopilotEnabled = false;
    hasTarget = false;
    activeMaxSpeed = 0;
    pathMode = false;
    pathPaused = false;
    pathPausedNavResume = false;
    pathRecords.Clear();
    pathIndex = 0;
    pathAnchorValid = false;
    carrierAutopilotMode = CarrierAutopilotMode.Precision;
    ResetMovement();
}

void UpdateFullAutoEnableStationaryGate()
{
    if (fullAutoEnableStationaryTicks <= 0)
        return;
    double speed = rc == null ? 0 : flightCache.LinearVelocity.Length();
    if (speed <= Math.Max(0, mineStationarySpeedMps))
        fullAutoEnableStationaryTicks++;
    else
    {
        fullAutoEnableStationaryTicks = 0;
        ScriptLog($"Fullauto denied: carrier moved ({speed:0.00} m/s).");
        return;
    }

    if (fullAutoEnableStationaryTicks < Math.Max(1, mineStationaryConfirmTicks))
        return;
    fullAutoEnableStationaryTicks = Math.Max(1, mineStationaryConfirmTicks);
    SetFullAutoEnabledFromUi(true);
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

bool ShowRecallMinerMenuItem()
{
    return emma3MissionState == Emma3MissionState.WaitingMiner && (!RecallStateMatchesActiveMission() || !ActiveMiner().RecallAcknowledged) && !ActiveMiner().PathCompleteReceived;
}

bool RecallStateMatchesActiveMission()
{
    return !string.IsNullOrWhiteSpace(emma3MissionId) && string.Equals(ActiveMiner().RecallMissionId, emma3MissionId, StringComparison.Ordinal);
}

void ResetMinerRecallState()
{
    for (int i = 0; i < minerSlots.Count; i++)
    {
        MinerSlot miner = minerSlots[i];
        miner.RecallRequested = false;
        miner.RecallActive = false;
        miner.RecallAcknowledged = false;
        miner.RecallMissionId = "";
        miner.RecallFailedTicks = 0;
        miner.RecallRequestElapsedSeconds = 0;
    }
}

void ResetMinerPathCompleteState()
{
    for (int i = 0; i < minerSlots.Count; i++)
    {
        MinerSlot miner = minerSlots[i];
        miner.PathCompleteReceived = false;
        miner.PathCompleteKind = "";
    }
}

bool AllAssignedMinersDone()
{
    if (minerSlots.Count == 0)
        return false;
    for (int i = 0; i < minerSlots.Count; i++)
        if (!minerSlots[i].PathCompleteReceived)
            return false;
    return true;
}

bool ShowContinueMenuItem()
{
    return emma3PluginState.Equals("aborted", StringComparison.OrdinalIgnoreCase) && emma3ContinueAvailable;
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

bool PluginStateIsActivelyWorking()
{
    return emma3PluginState.Equals("scanning", StringComparison.OrdinalIgnoreCase) || emma3PluginState.Equals("planning", StringComparison.OrdinalIgnoreCase);
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

void StartMinerRecall()
{
    MinerSlot miner = ActiveMiner();
    if (!RecallStateMatchesActiveMission())
        ResetMinerRecallState();
    if (!ShowRecallMinerMenuItem())
    {
        miner.RecallFailedTicks = 300;
        ScriptLog("Recall: miner recall is not available right now.");
        return;
    }

    miner.RecallRequested = true;
    miner.RecallActive = false;
    miner.RecallAcknowledged = false;
    miner.RecallMissionId = emma3MissionId;
    miner.RecallFailedTicks = 0;
    miner.RecallRequestElapsedSeconds = 0;
    miner.ProgressPollTicks = 0;
    Runtime.UpdateFrequency = tickSpeed;
    SendMinerRecallCommand();
    ScriptLog("Recall: requested Miner return for the active plugin mission.");
}

void SendMinerRecallCommand()
{
    MinerSlot miner = ActiveMiner();
    IGC.SendBroadcastMessage(miner.CommandTag, "Recall|" + emma3MissionId);
}

void UpdateMinerRecallRequestTimeout()
{
    MinerSlot miner = ActiveMiner();
    if (!miner.RecallRequested || miner.RecallAcknowledged)
    {
        miner.RecallRequestElapsedSeconds = 0;
        return;
    }

    miner.RecallRequestElapsedSeconds += secondsPerTick;
    if (miner.RecallRequestElapsedSeconds < mineRecallRequestTimeoutSeconds)
        return;
    miner.RecallRequested = false;
    miner.RecallActive = false;
    miner.RecallAcknowledged = false;
    miner.RecallRequestElapsedSeconds = 0;
    miner.RecallFailedTicks = 600;
    MarkProgressStateDirty();
    ScriptLog("Recall: Miner did not confirm within 10 seconds; Recall is available again.");
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

void AppendCarrierBehaviorThrustLimitRow(StringBuilder sb, int rowIndex)
{
    bool selected = caMenuIndex == rowIndex;
    sb.Append(selected ? "> " : "-  ");
    sb.AppendLine("Thrust limit:");
    sb.Append("   ");
    sb.Append(FormatWholeNumberWithCommas(thrustLimitKn));
    sb.AppendLine(" kN");
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

void AppendCarrierThrustDirectionPercentRows(StringBuilder sb)
{
    MatrixD m = rc == null ? _tickRcMatrix : _tickRcMatrix;
    double limitN = Math.Max(1, GetCarrierThrustLimitEditKn()) * 1000.0;
    sb.Append("   % of FWD Thrust: ");
    sb.AppendLine(FormatThrustRatioPercent(limitN, GetEffectiveThrustInDirection(m.Backward)));
    sb.Append("   % of BWD Thrust: ");
    sb.AppendLine(FormatThrustRatioPercent(limitN, GetEffectiveThrustInDirection(m.Forward)));
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

void BeginCarrierEdit(int index)
{
    if ((index < 1 || index > 23) && index != 25 && (index < 101 || index > 109))
        return;
    caEditingIndex = index;
    caEditValue = GetCarrierStoredValue(index);
    caEditingValue = true;
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

void AdjustCarrierThrustLimitEdit(int direction)
{
    if (caEditingIndex == 11)
        caEditValue = Math.Max(1, caEditValue + GetCarrierThrustStepKn() * direction);
    else if (caEditingIndex == 12)
        caEditValue = MathHelper.Clamp(caEditValue + 5 * direction, 1, 100);
    else if (caEditingIndex == 15)
        caEditValue = MathHelper.Clamp(Math.Round(caEditValue) + direction, 0, GetCarrierThrustStepCount() - 1);
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

void CancelCarrierEdit()
{
    caEditingValue = false;
    caEditingIndex = -1;
}

double GetCarrierValue(int index)
{
    if (caEditingValue && caEditingIndex == index)
        return caEditValue;
    return GetCarrierStoredValue(index);
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

double GetCarrierThrustUseEditPercent()
{
    if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit && caEditingIndex == 12)
        return caEditValue;
    return caThrustEditUsePercent;
}

int GetCarrierThrustStepEditIndex()
{
    if (caEditingValue && caLcdMenu == CaLcdMenu.ThrustLimit && caEditingIndex == 15)
        return Math.Max(0, Math.Min(GetCarrierThrustStepCount() - 1, (int)Math.Round(caEditValue)));
    return caThrustStepIndex;
}

int GetCarrierThrustStepCount()
{
    return 7;
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

int CountLines(StringBuilder sb)
{
    int count = 0;
    for (int i = 0; i < sb.Length; i++)
        if (sb[i] == '\n')
            count++;
    return count;
}

string GetLoadingSymbol()
{
    return ((runtime / 20) % 2 == 0) ? ".. ." : ". ..";
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

string _lcdHangarInventoryData;
string _lcdHangarContentsLabel = "Unknown";
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

bool MinerDetailReadingsAvailable()
{
    MinerSlot miner = ActiveMiner();
    return miner.StatusAgeSeconds < minerContactTimeoutSeconds && !miner.HangarStored
        && miner.HangarManagerPending != HangarManagerPendingCommand.Deploy
        && emma3MissionState != Emma3MissionState.WaitingHangarDeploy
        && manualHangarCycleState != ManualHangarCycleState.WaitingDeployReady;
}
