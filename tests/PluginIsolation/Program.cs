using System;
using System.IO;
using System.Linq;
using EMMA3.Client;
using VRage.FileSystem;

static class Program
{
    static int failures, checks;
    static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) { failures++; Console.Error.WriteLine("FAIL " + message); }
    }
    static void Scans()
    {
        var a = new AsteroidMap { EntityId = 101 };
        var b = new AsteroidMap { EntityId = 202 };
        var c = new Emma3Controller(null) { requestedAsteroidEntityId = a.EntityId };
        c.scanner.Active = true;
        c.scanner.Current = b;
        c.backgroundScanActive = true;
        c.Promote();
        Check(c.backgroundScanActive, "a scan of B stays in the background for a request for A");
        c.scanner.Current = a;
        c.Promote();
        Check(!c.backgroundScanActive, "a matching background scan can be promoted");
        // A late result for B must still be checked at the publication boundary.
        c.Finish(b);
        Check(c.published == 0 && c.continued == 1 && c.cache[b.EntityId] == b, "wrong completed scan must be cached and planning must continue for A");
        c.Finish(a);
        Check(c.published == a.EntityId, "matching completed scan can publish");
        c = new Emma3Controller(null);
        c.Finish(b);
        Check(c.published == b.EntityId, "untargeted request accepts a qualifying asteroid");
        c = new Emma3Controller(null) { planningActive = false, backgroundScanActive = true };
        c.Finish(b);
        Check(c.cache[b.EntityId] == b && c.published == 0 && c.continued == 0, "idle background scans only populate the cache");
        c = new Emma3Controller(null) { departurePreparation = new DeparturePreparation { Complete = false }, requestedAsteroidEntityId = a.EntityId };
        c.Finish(b);
        Check(c.cache[b.EntityId] == b && c.published == 0 && c.continued == 0, "unrelated scan during departure stays cached");
    }
    static void Logging(string root)
    {
        Emma3Log.Dispose();
        var plugin = new Plugin();
        MyFileSystem.UserDataPath = null;
        plugin.Init(null);
        plugin.Update();
        Check(Emma3Controller.OpenedPath == null, "unavailable game path must delay controller initialization");
        Check(!Directory.Exists(EMMA3.Client.Environment.GetFolderPath(EMMA3.Client.Environment.SpecialFolder.MyDocuments)), "no shared bootstrap file before initialization");
        string first = Path.Combine(root, "host"), second = Path.Combine(root, "client");
        MyFileSystem.UserDataPath = first;
        for (int i = 0; i < 300; i++) plugin.Update();
        Check(Emma3Controller.OpenedPath == first, "initialization retries with game-selected path");
        Emma3Log.Warn("host warning");
        Emma3Log.Script("Miner A", "host payload");
        string export = Emma3Log.ExportFailure("case-1", "host failure");
        string firstLogs = Path.Combine(first, "Storage", "EMMA3");
        string pluginLog = Path.Combine(firstLogs, "EMM-A 3 Plugin.log");
        Check(File.Exists(pluginLog), "host plugin log under its own user data");
        Check(File.Exists(Path.Combine(firstLogs, "EMM-A 3 Miner.log")) && Path.GetDirectoryName(export) == firstLogs, "script logs and failure exports share the host instance directory");
        if (File.Exists(pluginLog))
        {
            var lines = File.ReadAllLines(pluginLog);
            Check(lines.Count(l => l.Contains("loaded; waiting")) == 1 && lines.Count(l => l.Contains("host warning")) == 1, "retain startup and warning exactly once");
            Check(lines.Any(l => l.Contains("initialization attempt 1 failed")), "retain startup error for diagnostics");
        }
        plugin.Dispose();
        var old = File.Exists(pluginLog) ? File.ReadAllBytes(pluginLog) : Array.Empty<byte>();
        var other = new Plugin();
        MyFileSystem.UserDataPath = second;
        other.Init(null);
        other.Update();
        Emma3Log.Initialize(second, 1024 * 1024, 2);
        Emma3Log.Info(new string('Z', 1024 * 1024));
        Emma3Log.Error("client marker");
        string clientLog = Path.Combine(second, "Storage", "EMMA3", "EMM-A 3 Plugin.log");
        Check(File.Exists(clientLog + ".1") && File.Exists(clientLog) && File.ReadAllText(clientLog).Contains("client marker"), "rotation is local to the second instance");
        Check(File.Exists(pluginLog) && File.ReadAllBytes(pluginLog).SequenceEqual(old), "second instance never appends to or rotates the first");
        other.Dispose();
    }
    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "emma-plugin-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        EMMA3.Client.Environment.TestRoot = root;
        try { Scans(); Logging(root); }
        finally { Emma3Log.Dispose(); Directory.Delete(root, true); }
        Console.WriteLine($"{checks} scan and log isolation checks, {failures} failures.");
        return failures == 0 ? 0 : 1;
    }
}
