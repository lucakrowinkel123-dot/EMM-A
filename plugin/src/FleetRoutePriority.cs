using System;
using System.Collections.Generic;
using System.Globalization;

namespace EMMA3.Client
{
    internal static class FleetRoutePriority
    {
        public static int[] Order(IList<string> paths)
        {
            var order = new List<int>();
            for (int i = 0; i < paths.Count; i++) order.Add(i);
            order.Sort((a, b) => {
                int result = RouteMeters(paths[b]).CompareTo(RouteMeters(paths[a]));
                return result != 0 ? result : a.CompareTo(b);
            });
            return order.ToArray();
        }
        public static double RouteMeters(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            foreach (string raw in path.Split('\n'))
            {
                string line = raw.Trim();
                const string prefix = "00;routeMeters:";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                double value;
                if (double.TryParse(line.Substring(prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0) return value;
                return 0;
            }
            return 0;
        }
        public static bool TryParseOrder(string value, int count, out int[] order)
        {
            order = null;
            string[] parts = (value ?? "").Split(',');
            if (parts.Length != count) return false;
            var result = new int[count]; var used = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int index;
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    || index < 0 || index >= count || used[index]) return false;
                used[index] = true; result[i] = index;
            }
            order = result; return true;
        }
        public static int Next(int[] order, int acknowledged, IList<string> paths)
        {
            int start = 0;
            if (acknowledged != -1)
            {
                start = Array.IndexOf(order, acknowledged);
                if (start < 0) return -2;
                start++;
            }
            for (int i = start; i < order.Length; i++)
                if (!string.IsNullOrEmpty(paths[order[i]])) return order[i];
            return -1;
        }
    }
}
