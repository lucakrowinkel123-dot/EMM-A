using System;
using System.Collections.Generic;
using System.Text;

namespace EMMA3.Client
{
    internal sealed class OreSelection
    {
        public readonly HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Mineable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Ordered = new List<string>();
        public static OreSelection Parse(string text)
        {
            var result = new OreSelection();
            foreach (string raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("//"))
                    continue;
                string[] parts = line.Split(new[] { ' ', '\t', '=', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;
                string ore = Normalize(parts[0]);
                if (ore.Length == 0)
                    continue;
                if (!result.Ordered.Exists(x => string.Equals(x, ore, StringComparison.OrdinalIgnoreCase)))
                    result.Ordered.Add(ore);
                char mode = ParseMode(parts.Length < 2 ? "Y" : parts[1]);
                if (mode == 'Y')
                    result.Targets.Add(ore);
                if (mode != 'X')
                    result.Mineable.Add(ore);
            }

            return result;
        }

        public bool Matches(string ore) => Targets.Contains(Normalize(ore));
        public bool ShouldMine(string ore) => Mineable.Contains(Normalize(ore));
        public static string AddMissingOpportunistic(string text, IEnumerable<string> detectedOres, out List<string> added)
        {
            string original = text ?? "";
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in original.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("//"))
                    continue;
                string[] parts = line.Split(new[] { ' ', '\t', '=', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;
                string ore = Normalize(parts[0]);
                if (ore.Length > 0)
                    known.Add(ore);
            }

            added = new List<string>();
            if (detectedOres != null)
            {
                foreach (string raw in detectedOres)
                {
                    string ore = Normalize(raw);
                    if (ore.Length > 0 && known.Add(ore))
                        added.Add(ore);
                }
            }

            added.Sort(StringComparer.OrdinalIgnoreCase);
            if (added.Count == 0)
                return original;
            var result = new StringBuilder(original.Replace("\r", "").TrimEnd());
            if (result.Length > 0)
                result.AppendLine();
            for (int i = 0; i < added.Count; i++)
                result.AppendLine(added[i] + " U");
            return result.ToString().TrimEnd();
        }

        private static char ParseMode(string value)
        {
            string mode = (value ?? "").Trim();
            if (mode.Equals("Y", StringComparison.OrdinalIgnoreCase) || mode.Equals("on", StringComparison.OrdinalIgnoreCase) || mode.Equals("true", StringComparison.OrdinalIgnoreCase) || mode == "1" || mode.Equals("target", StringComparison.OrdinalIgnoreCase))
                return 'Y';
            if (mode.Equals("X", StringComparison.OrdinalIgnoreCase) || mode.Equals("exclude", StringComparison.OrdinalIgnoreCase) || mode.Equals("excluded", StringComparison.OrdinalIgnoreCase) || mode.Equals("never", StringComparison.OrdinalIgnoreCase))
                return 'X';
            // Legacy Off/False/0 entries had exactly this behaviour: they did not
            // select an asteroid, but were still mined when another ore selected it.
            return 'U';
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            string result = value.Trim();
            int slash = Math.Max(result.LastIndexOf('/'), result.LastIndexOf('\\'));
            if (slash >= 0 && slash + 1 < result.Length)
                result = result.Substring(slash + 1);
            if (result.EndsWith("Ore", StringComparison.OrdinalIgnoreCase) && result.Length > 3)
                result = result.Substring(0, result.Length - 3);
            return result.Trim();
        }
    }
}
