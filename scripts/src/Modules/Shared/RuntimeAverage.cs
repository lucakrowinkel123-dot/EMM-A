// PBLimiter defaults: skip 20 runs, 0.005 contribution per completed run,
// decay 0.995 per simulation frame. PBs cannot observe the GC exclusion rule.
public sealed class PbRuntimeAverage
{
    public long Samples, Frame = -1;
    public double Average;
    readonly long epoch = DateTime.UtcNow.Ticks;
    bool pending;

    public void Observe(long frame, double previous)
    {
        if (Frame >= 0)
        {
            // LastRunTimeMs belongs to the PREVIOUS Main, at Frame, not frame.
            if (pending && Samples++ >= 20 && previous >= 0 && !double.IsInfinity(previous))
                Average += .005 * previous;
            long elapsed = Math.Max(0, frame - Frame);
            if (elapsed > 0) Average *= elapsed == 1 ? .995 : Math.Pow(.995, elapsed);
        }
        Frame = frame;
        pending = true;
    }
    public void Saved() { pending = false; }
    public string Packet(string role, long id)
    {
        return "EMMA3_RUNTIME|1|" + role + "|" + id + "|" + epoch + "|" + Samples + "|"
            + Average.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
readonly PbRuntimeAverage pbRuntimeAverage = new PbRuntimeAverage();
long runtimeReportFrame = -60;
string runtimeReport = "", pilotRuntimeReport = "";

string GetRuntimeReport()
{
    if (runtimeReport.Length == 0 || pbRuntimeAverage.Frame - runtimeReportFrame >= 60)
    {
        runtimeReportFrame = pbRuntimeAverage.Frame;
        runtimeReport = pbRuntimeAverage.Packet(moduleRole, Me.EntityId);
    }
    return runtimeReport;
}
public void Save()
{
    // The game clears LastRunTimeMs before invoking Save.
    pbRuntimeAverage.Saved();
    SaveState();
}
