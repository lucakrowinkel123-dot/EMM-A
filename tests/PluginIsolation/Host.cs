using System;
using System.Collections.Generic;
using System.IO;

// Give even a regressed logger a private Documents and roaming folder in this test.
namespace EMMA3.Client
{
    static class Environment
    {
        public enum SpecialFolder { MyDocuments, ApplicationData }
        public static string TestRoot;
        public static string GetFolderPath(SpecialFolder folder) => Path.Combine(TestRoot, folder.ToString());
        public static string NewLine => System.Environment.NewLine;
    }

    class AsteroidMap { public long EntityId; public bool Complete = true; public int OreSamples = 1; }
    class OreSelection { public OreSelection Remaining(long id, OreSelection selection) => selection; }
    class DeparturePreparation { public bool Complete; }
    class Scanner { public bool Active; public AsteroidMap Current; }

    internal sealed partial class Emma3Controller
    {
        internal static string OpenedPath;
        internal Emma3Controller(string path) { OpenedPath = path; }
        internal bool RebindRequested => false;
        internal void Detach() {}
        internal void Update() {}
        internal void Abort(string reason) {}
        internal void Dispose() {}
        internal long requestedAsteroidEntityId, published;
        internal bool planningActive = true, backgroundScanActive;
        internal int continued;
        internal DeparturePreparation departurePreparation;
        internal readonly Scanner scanner = new Scanner();
        internal readonly Dictionary<long, AsteroidMap> cache = new Dictionary<long, AsteroidMap>();
        readonly OreSelection completed = new OreSelection(), planningSelection = new OreSelection();
        string DescribeMapOres(AsteroidMap map) => "ore";
        void AddDetectedOresToTimer(AsteroidMap map) {}
        void ContinuePlanning() { continued++; }
        bool Qualifies(AsteroidMap map, OreSelection selection) => true;
        void BuildAndPublish(AsteroidMap map) { published = map.EntityId; }
        internal void Promote() => PromoteMatchingBackgroundScan();
        internal void Finish(AsteroidMap map) => OnScanComplete(map);
    }
}
namespace VRage.Plugins { public interface IPlugin {} }
namespace VRage.FileSystem { static class MyFileSystem { public static string UserDataPath; } }
namespace Sandbox.ModAPI
{
    class Session { public object Player = new object(); }
    static class MyAPIGateway { public static Session Session = new Session(); }
}
namespace EmmaTiming { static class GameSimulationTime { public static void Poll() {} } }
