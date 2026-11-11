using System;
using EMMA3.Client;
using Sandbox.ModAPI.Ingame;

namespace Sandbox.ModAPI
{
    public interface IMyTerminalBlock { bool Closed { get; } bool MarkedForClose { get; } }
}
namespace Sandbox.ModAPI.Ingame
{
    public interface IMyProgrammableBlock { long EntityId {get;} }
    public interface IMyTextPanel
    {
        VRageMath.Vector2 SurfaceSize {get;} bool IsWorking { get; }
        VRage.Game.GUI.TextPanel.ContentType ContentType { get; set; }
        string Font { get; set; }
        float FontSize { get; set; }
        float TextPadding { get; set; }
        VRage.Game.GUI.TextPanel.TextAlignment Alignment { get; set; }
        VRageMath.Color FontColor { get; set; }
        VRageMath.Color BackgroundColor { get; set; }
        bool WriteText(string text,bool append);
        VRageMath.Vector2 MeasureStringInPixels(System.Text.StringBuilder text,string font,float scale);
    }
}
namespace VRage.Game.GUI.TextPanel { public enum ContentType { SCRIPT, TEXT_AND_IMAGE } public enum TextAlignment { LEFT } }
namespace VRageMath { public struct Color { public static Color White, Black; } public struct Vector2 { public float X,Y; } public struct Vector3D {public double X,Y,Z;} }
namespace Sandbox.Game.Multiplayer { public static class Sync { public static bool IsServer=true; } }
namespace EMMA3.Client
{
    static class Emma3Log { public static void Warn(string text) { } }
    class Timer {public string CustomData="",Name="";}
    class Names { public string Carrier="Typhon",Miner="EMM-A Mi"; }
    class Blocks
    {
        public IMyTextPanel CarrierLcdRuntime;
        public Timer BridgeTimer=new Timer();
        public bool IsCarrierPilot=true;
        public bool IsMinerHangared;
        public IMyProgrammableBlock CarrierPb,CarrierPilotPb,CarrierLcdPb,MinerPb,MinerPilotPb;
        public Names Names=new Names();
    }
    internal sealed partial class Emma3Controller
    {
        internal Blocks blocks=new Blocks();
        internal bool pluginLcdOutput=true;
        int frame;
        internal Timer lastCarrierInfoTimer=new Timer(),lastMinerInfoTimer=new Timer();
        string CarrierInfoTimerName="Typhon Plugin Info",MinerInfoTimerName="EMM-A Mi Plugin Info";
        static bool TimerBelongsTo(Timer timer,IMyProgrammableBlock pb,string name){return timer!=null&&pb!=null&&timer.Name==name;}

        internal void Tick(bool force=false) { frame++;if(force)lastRuntimeSecond=-1;UpdateRuntimeLcd(); }
    }
}
class Panel : IMyTextPanel
{
    public VRageMath.Vector2 SurfaceSize { get; set; } = new VRageMath.Vector2 { X=1024,Y=1024 };
    public bool IsWorking { get; set; }=true;
    public VRage.Game.GUI.TextPanel.ContentType ContentType { get; set; }
    public string Font { get; set; }
    public float FontSize { get; set; }
    public float TextPadding { get; set; }
    public VRage.Game.GUI.TextPanel.TextAlignment Alignment { get; set; }
    public VRageMath.Color FontColor { get; set; }
    public VRageMath.Color BackgroundColor { get; set; }
    public string Text;
    public int Writes;
    public bool Fail;
    public bool EditorOpen;
    public VRageMath.Vector2 MeasureStringInPixels(System.Text.StringBuilder text,string font,float scale) {
        var lines=text.ToString().Replace("\r","").Split('\n');
        int widest=0;
        foreach(var line in lines) {
            int width=0;
            foreach(char ch in line) {
                int glyph=font!="Debug"?18:ch==' '?8:"ilI".Contains(ch.ToString())?5:"WMm".Contains(ch.ToString())?24:15;
                width+=glyph+2;
            }
            widest=Math.Max(widest,width-2);
        }
        return new VRageMath.Vector2 { X=widest*scale,Y=lines.Length*30*scale };
    }
    public bool WriteText(string text,bool append) { if(Fail)throw new InvalidOperationException("panel unavailable");if(EditorOpen)return false;Writes++;Text=text;return true; }
}
class PB : IMyProgrammableBlock,Sandbox.ModAPI.IMyTerminalBlock
{
    public long EntityId { get; set; }
    public bool Closed { get; set; }
    public bool MarkedForClose { get; set; }
}
static class ControllerTests
{
    public static void Run(Action<bool,string> check)
    {
        var controller=new Emma3Controller();var panel=new Panel();var pb=new PB{EntityId=1};
        controller.blocks.CarrierLcdRuntime=panel;controller.blocks.CarrierPb=pb;
        controller.blocks.CarrierPilotPb=new PB{EntityId=2};controller.blocks.MinerPb=new PB{EntityId=3};controller.blocks.MinerPilotPb=new PB{EntityId=4};
        controller.blocks.CarrierLcdPb=new PB{EntityId=9};
        Sandbox.Game.Multiplayer.Sync.IsServer=false;
        controller.lastCarrierInfoTimer.Name="Typhon Plugin Info";
        controller.lastCarrierInfoTimer.CustomData="EMMA3_RUNTIME|1|CarrierCommander|1|100|30|0.123\nEMMA3_RUNTIME|1|CarrierLcd|9|100|30|0.007";
        controller.Tick();check(panel.Writes==1&&panel.Text.Contains("Typhon | Runtime"),"Render on ordinary remote client");
        check(panel.ContentType==VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE && panel.Font=="Debug" && panel.FontSize==1.225f && panel.TextPadding==0,"Runtime uses the same native text and font as the other LCDs");
        CheckDashPositions(panel, 5, check);
        controller.blocks.BridgeTimer.CustomData="fleetCount=3\nfleetLabel0=WWW\nfleetLabel1=iii\nfleetStored1=true\nfleetLabel2=Miner C";
        controller.Tick(true);
        CheckDashPositions(panel, 8, check);
        check(panel.Text.Split('\n')[8].Trim()=="iii Pilot","The stored pilot has no dash or reading");
        controller.blocks.BridgeTimer.CustomData="";
        controller.Tick(true);
        int initialWrites=panel.Writes;
        controller.Tick();check(panel.Writes==initialWrites,"No redraw on each tick");
        controller.Tick(true);check(panel.Writes==initialWrites,"No unchanged text rewrite");
        controller.lastCarrierInfoTimer.CustomData="EMMA3_RUNTIME|1|CarrierCommander|1|100|90|0.123\nEMMA3_RUNTIME|1|CarrierLcd|9|100|90|0.007";
        controller.Tick(true);check(panel.Text.Contains("0.123 ms"),"Client reads replicated server average");
        check(panel.Text.Split('\n')[4].Contains("0.007 ms"),"Controller binds and renders the exact current LCD PB report");
        var replacement=new Panel{SurfaceSize=new VRageMath.Vector2 { X=512,Y=512 }};controller.blocks.CarrierLcdRuntime=replacement;controller.Tick(true);
        check(replacement.Writes==1,"Replacement panel redraws same content");
        check(replacement.FontSize>0 && replacement.MeasureStringInPixels(new System.Text.StringBuilder(replacement.Text),replacement.Font,replacement.FontSize).X<=512.01f,"All runtime columns fit a 512 pixel surface");
        pb.Closed=true;controller.Tick(true);check(!replacement.Text.Contains("0.123"),"Closed PB clears old reading");
        controller.blocks.IsMinerHangared=true;controller.Tick(true);
        check(!replacement.Text.Contains("Hangared"),"Live Miner blocks override stored contents during replication transitions");
        controller.blocks.MinerPb=null;controller.blocks.MinerPilotPb=null;controller.Tick(true);
        string[] stored=replacement.Text.Replace("\r","").Split('\n');
        check(stored[5].EndsWith("Hangared")&&stored[6].Trim()=="Miner A Pilot"&&stored[2].EndsWith("Not loaded")&&stored[12].Contains("Total - --"),"Only confirmed stored Miner Commander shows Hangared; absent Carrier and Total keep their existing states");
        controller.blocks.IsMinerHangared=false;controller.Tick(true);
        check(!replacement.Text.Contains("Hangared")&&replacement.Text.Contains("Not loaded"),"Empty or unknown hangar returns absent Miner rows to Not loaded");
        controller.pluginLcdOutput=false;int writes=replacement.Writes;controller.Tick(true);
        check(replacement.Writes==writes,"Disabled LCD output does not write");
        controller.pluginLcdOutput=true;replacement.Fail=true;controller.Tick(true);
        replacement.Fail=false;controller.Tick(true);check(replacement.Text.Contains("Typhon | Runtime"),"Display errors retry without aborting mission");
        var editing=new Panel{EditorOpen=true};controller.blocks.CarrierLcdRuntime=editing;controller.Tick(true);
        editing.EditorOpen=false;controller.Tick(true);check(editing.Writes==1,"Rejected editor-open write retries");
        controller.blocks.CarrierLcdRuntime=null;controller.Tick(true);
    }
    static void CheckDashPositions(Panel panel,int expectedRows,Action<bool,string> check)
    {
        double left=double.MaxValue,right=0;
        int rows=0;
        foreach(string line in panel.Text.Split('\n')) {
            if(!line.StartsWith("   ",StringComparison.Ordinal))continue;
            int dash=line.LastIndexOf(" - ",StringComparison.Ordinal);
            if(dash<0)continue;
            var prefix=new System.Text.StringBuilder(line.Substring(0,dash+1));
            double x=panel.MeasureStringInPixels(prefix,"Debug",1).X;
            left=Math.Min(left,x);right=Math.Max(right,x);rows++;
        }
        check(rows==expectedRows && right-left<=10,"All displayed dashes align within one ordinary space in the proportional font");
    }
}

namespace EmmaTiming { public static class GameSimulationTime { public static double Ticks; } }
