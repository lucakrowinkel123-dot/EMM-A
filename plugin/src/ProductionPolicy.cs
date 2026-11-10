using System;
using System.Collections.Generic;

namespace EMMA3.Client
{
    internal static class ProductionPolicy
    {
        public const double MinimumIceLiters = 1000000;
        public static bool IcePriority(bool enabled, bool active, double waterPercent, double iceLiters, double targetLiters)
        {
            if (!enabled || double.IsNaN(iceLiters) || double.IsInfinity(iceLiters) || iceLiters < 0)
                return false;
            double target = ValidIceTarget(targetLiters);
            if (iceLiters >= target)
                return false;
            return active || (!double.IsNaN(waterPercent) && waterPercent >= 0 && waterPercent < 50);
        }

        public static double ValidIceTarget(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? MinimumIceLiters : Math.Max(MinimumIceLiters, Math.Min(1e12, value));
        }
    }

    internal sealed class ItemCatalog
    {
        internal sealed class Entry
        {
            public string Name;
            public string Blueprint;
            public bool Enabled;
        }
        public readonly List<Entry> Entries = new List<Entry>();
        public string Error = "";
        public static ItemCatalog FromSnapshot(string snapshot)
        {
            if (snapshot.StartsWith("1:", StringComparison.Ordinal))
                return Parse(snapshot.Substring(2).Replace("%0A", "\n").Replace("%25", "%"), "");
            return new ItemCatalog { Error = snapshot.StartsWith("error:", StringComparison.Ordinal)
                ? snapshot.Substring(6) : "Recompile the Carrier PB to load EMM-A Ca Items." };
        }

        public void ApplySelection(string enabledIds)
        {
            var enabled = new HashSet<string>((enabledIds ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            foreach (var entry in Entries)
                entry.Enabled = enabled.Contains(entry.Blueprint);
        }

        public static ItemCatalog Parse(string text, string enabledIds)
        {
            var result = new ItemCatalog();
            var lines = new List<string>();
            foreach (string raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith("#") && !line.StartsWith("//"))
                    lines.Add(line);
            }
            if (lines.Count % 2 != 0 || lines.Count > 256)
            {
                result.Error = "Items needs name/blueprint pairs (max 128).";
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            const string prefix = "MyObjectBuilder_BlueprintDefinition/";
            for (int i = 0; i < lines.Count; i += 2)
            {
                string id = lines[i + 1];
                if (!id.StartsWith(prefix, StringComparison.Ordinal) || id.Length <= prefix.Length || id.IndexOf(',') >= 0 || !seen.Add(id))
                {
                    result.Error = "Invalid or duplicate blueprint ID for " + lines[i] + ".";
                    result.Entries.Clear();
                    return result;
                }
                result.Entries.Add(new Entry { Name = lines[i], Blueprint = id });
            }
            result.ApplySelection(enabledIds);
            return result;
        }
    }
}
