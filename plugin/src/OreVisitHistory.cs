using System;
using System.Collections.Generic;
using System.Text;

namespace EMMA3.Client
{
    internal sealed class OreVisitHistory
    {
        private readonly HashSet<long> legacy = new HashSet<long>();
        private readonly Dictionary<long, HashSet<string>> ores = new Dictionary<long, HashSet<string>>();
        public int Count { get { var all = new HashSet<long>(legacy); all.UnionWith(ores.Keys); return all.Count; } }
        public bool Contains(long id) { return legacy.Contains(id); }
        public void Clear() { legacy.Clear(); ores.Clear(); }
        public void Add(long id, string minedOres)
        {
            if (id == 0)
                return;
            if (string.IsNullOrWhiteSpace(minedOres))
            {
                legacy.Add(id);
                return;
            }
            HashSet<string> set;
            if (!ores.TryGetValue(id, out set))
                ores[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string ore in minedOres.Split(','))
            {
                string normalized = OreSelection.Normalize(ore);
                if (normalized.Length > 0)
                    set.Add(normalized);
            }
        }
        public void AddLine(string line)
        {
            string[] parts = line.Trim().Split('|');
            long id;
            if (parts.Length <= 2 && long.TryParse(parts[0], out id))
                Add(id, parts.Length == 2 ? parts[1] : "");
        }
        public OreSelection Remaining(long id, OreSelection selection)
        {
            var remaining = new OreSelection();
            if (legacy.Contains(id))
                return remaining;
            HashSet<string> mined;
            ores.TryGetValue(id, out mined);
            foreach (string ore in selection.Targets)
                if (mined == null || !mined.Contains(ore))
                    remaining.Targets.Add(ore);
            foreach (string ore in selection.Mineable)
                if (mined == null || !mined.Contains(ore))
                    remaining.Mineable.Add(ore);
            return remaining;
        }
        public string Serialize()
        {
            var result = new StringBuilder("# EMM-A asteroid visits: legacy ID = whole asteroid; ID|ore,ore = completed ores\n");
            var all = new HashSet<long>(legacy);
            all.UnionWith(ores.Keys);
            var sorted = new List<long>(all);
            sorted.Sort();
            foreach (long id in sorted)
            {
                result.Append(id);
                if (!legacy.Contains(id))
                {
                    var names = new List<string>(ores[id]);
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    result.Append('|').Append(string.Join(",", names));
                }
                result.AppendLine();
            }
            return result.ToString();
        }
    }
}
