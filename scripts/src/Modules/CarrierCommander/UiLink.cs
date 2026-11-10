readonly Queue<string> uiInputs = new Queue<string>();
string uiPendingId = "", uiPendingInput = "";

bool TryHandleCarrierLcdCommand(string command)
{
    string text=(command??"").Trim().ToLowerInvariant();
    if(text!="up"&&text!="w"&&text!="down"&&text!="s"&&text!="apply"&&text!="e"
        &&text!="back"&&text!="c"&&text!="cursortoggle"&&text!="cursor toggle")return false;
    QueueUiInput(text);return true;
}
void QueueUiInput(string command)
{
    if(!ModuleReady()) { ScriptLog("Menu waiting for Carrier Pilot.");return; }
    if(uiInputs.Count>=32) {ScriptLog("Menu input queue full.");return;}
    uiInputs.Enqueue(command);PublishUiInput();
}
void PublishUiInput()
{
    if(uiPendingId.Length==0 && uiInputs.Count>0) {
        uiPendingId=NextModuleId();uiPendingInput=uiInputs.Dequeue();SaveState();
    }
    MyIni ini=new MyIni();WriteUiSnapshot(ini);
    if(uiPendingId.Length>0) {
        ini.Set("UiInput","id",uiPendingId);ini.Set("UiInput","command",uiPendingInput);
        SendModule("uiInput",ini.ToString());
    } else SendModule("uiSnapshot",ini.ToString());
}
bool ReceiveUiMessage(string kind,string payload)
{
    if(kind!="uiReply"&&kind!="uiDisplay")return false;
    MyIni ini=new MyIni();if(!ini.TryParse(payload))return true;
    if(kind=="uiDisplay") {
        UpdatePluginLcdPayload(ini.Get("Display","main").ToString(""),ini.Get("Display","report").ToString(""));
        return true;
    }
    if(uiPendingId.Length==0||ini.Get("UiReply","id").ToString("")!=uiPendingId)return true;
    ApplyUiEdits(ini);
    if(ini.ContainsKey("UiReply","oreAfter")) {
        IMyTimerBlock timer=FindOreTimer();
        string before=ini.Get("UiReply","oreBefore").ToString(""),after=ini.Get("UiReply","oreAfter").ToString("");
        if(timer!=null && (timer.CustomData==before||timer.CustomData==after))timer.CustomData=after;
        else ScriptLog("Ore list changed while applying menu input; refreshed.");
    }
    foreach(string action in ini.Get("UiReply","actions").ToString("").Split('\n')) {
        if(action=="am:on")SetFullAutoEnabledFromUi(true);
        else if(action=="am:off")SetFullAutoEnabledFromUi(false);
        else if(action=="recall")StartMinerRecall();
        else if(action=="continue")RequestEmma3Continue();
        else if(action=="reset")RequestEmma3Reset();
        else if(action=="clearlog")ClearScriptLogQueue();
        else if(action=="items")LoadCarrierItems();
    }
    WriteEmma3Settings();WriteMinerBehaviorSettings();SendCarrierStatusNow();
    uiPendingId="";uiPendingInput="";SaveState();PublishUiInput();
    return true;
}

// Presentation runs on the Pilot; authoritative actions above remain here.
void UpdateCarrierLcd() { }
void UpdateReportLcd() { }
void InitializeReportLcd() { }
void ConfigureCarrierLcd() { }
void RefreshCachedCarrierDimensions() { }
void UpdateCarrierInfoCache() { }
int GetCarrierLcdStateFingerprint() { return 0; }
void ShowStartupScreen(List<string> missing) { Echo("Missing: "+string.Join(", ",missing)); }
void OpenCarrierMenu(CaLcdMenu menu) { QueueUiInput("menu:"+(int)menu); }
