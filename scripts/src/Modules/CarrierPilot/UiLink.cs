CarrierUi carrierUi;
string uiLastInputId="",uiLastReply="",uiSavedDraft="";
int uiNextRender;
void InitializeModuleUi()
{
    carrierUi=new CarrierUi(this);
    MyIni ini=new MyIni();if(ini.TryParse(uiSavedDraft))carrierUi.ReadDraft(ini);
}
bool ReceiveUiMessage(string kind,string payload)
{
    if(kind!="uiSnapshot"&&kind!="uiInput")return false;
    if(carrierUi==null)return true;
    MyIni ini=new MyIni();if(!ini.TryParse(payload))return true;
    if(kind=="uiSnapshot") {carrierUi.ReadSnapshot(ini);return true;}
    string id=ini.Get("UiInput","id").ToString("");
    if(id==uiLastInputId){SendModule("uiReply",uiLastReply);return true;}
    if(!ModuleOperationJournal.IsNewer(id,uiLastInputId))return true;
    carrierUi.ReadSnapshot(ini);
    MyIni reply=new MyIni();reply.TryParse(carrierUi.Input(ini.Get("UiInput","command").ToString("")));
    reply.Set("UiReply","id",id);
    uiLastInputId=id;uiLastReply=reply.ToString();
    SaveState();SendModule("uiReply",uiLastReply);uiNextRender=0;
    return true;
}
void UpdateModuleUi()
{
    if(carrierUi==null||!ModuleReady()||runtime<uiNextRender)return;
    uiNextRender=runtime+Math.Max(60,lcdRefreshIntervalTicks);
    carrierUi.Render();
}
void SendUiDisplay(string main,string report)
{
    MyIni ini=new MyIni();ini.Set("Display","main",main);ini.Set("Display","report",report);
    SendModule("uiDisplay",ini.ToString());
}
