using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed class BridgeDocument
    {
        private static readonly string[] PreferredKeys =
        {
            "protocol", "request", "command", "state", "message", "allowMovement",
            "allowSeatExitDuringMining", "visualization", "searchRadius", "parkingHeight",
            "parkingHeight300MigrationDone", "parkingHeight400MigrationDone", "planeClearance", "travelClearance", "clearance2575MigrationDone", "resetSequence",
            "resetAck", "missionId", "asteroidEntityId", "carrierRouteCount", "pathHash",
            "pathRecords", "planeCount"
        };
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool Dirty { get; private set; }
        internal IEnumerable<KeyValuePair<string, string>> Entries { get { return values; } }
        public static BridgeDocument Parse(string text)
        {
            var result = new BridgeDocument();
            foreach (string raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '[' || line[0] == '#' || line[0] == ';')
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                result.values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            return result;
        }

        public string Get(string key, string fallback = "")
        {
            string value;
            return values.TryGetValue(key, out value) ? value : fallback;
        }

        public static BridgeDocument ReadMinerPlannerSettings(string customData, string legacyTimerData)
        {
            const string prefix = "EMMA3-PLANNER: ";
            string data = customData ?? "";
            int start = 0;
            if (!data.StartsWith(prefix, StringComparison.Ordinal))
            {
                start = data.IndexOf("\n" + prefix, StringComparison.Ordinal);
                if (start >= 0) start++;
            }
            if (start < 0) return Parse(legacyTimerData);
            start += prefix.Length;
            int end = data.IndexOf('\n', start);
            if (end < 0) end = data.Length;
            return Parse(data.Substring(start, end - start).Replace(';', '\n'));
        }

        public int GetInt(string key, int fallback = 0)
        {
            int v;
            return int.TryParse(Get(key), out v) ? v : fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            double v;
            return double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public bool GetBool(string key, bool fallback = false)
        {
            string v = Get(key);
            if (v.Length == 0)
                return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        public void Set(string key, object value)
        {
            string next = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            string current;
            if (values.TryGetValue(key, out current) && string.Equals(current, next, StringComparison.Ordinal))
                return;
            values[key] = next;
            Dirty = true;
        }

        public void SetVector(string key, Vector3D value)
        {
            Set(key, FormatVector(value));
        }

        public void Remove(string key)
        {
            if (values.Remove(key))
                Dirty = true;
        }

        public void RemovePrefix(string prefix)
        {
            var remove = new List<string>();
            foreach (string key in values.Keys)
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    remove.Add(key);
            for (int i = 0; i < remove.Count; i++)
                if (values.Remove(remove[i]))
                    Dirty = true;
        }

        public string Serialize()
        {
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder(1024);
            sb.AppendLine("[EMMA3]");
            foreach (string key in PreferredKeys)
            {
                string value;
                if (!values.TryGetValue(key, out value))
                    continue;
                sb.Append(key).Append('=').AppendLine(value);
                emitted.Add(key);
            }

            var rest = new List<string>();
            foreach (string key in values.Keys)
                if (!emitted.Contains(key))
                    rest.Add(key);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string key in rest)
                sb.Append(key).Append('=').AppendLine(values[key]);
            return sb.ToString().TrimEnd();
        }

        public static string FormatVector(Vector3D value) => value.X.ToString("0.00", CultureInfo.InvariantCulture) + "," + value.Y.ToString("0.00", CultureInfo.InvariantCulture) + "," + value.Z.ToString("0.00", CultureInfo.InvariantCulture);
        public static string FormatCarrierRoutePosition(Vector3D value) => value.X.ToString("0.00", CultureInfo.InvariantCulture) + ":" + value.Y.ToString("0.00", CultureInfo.InvariantCulture) + ":" + value.Z.ToString("0.00", CultureInfo.InvariantCulture);
        public static string FormatCarrierRouteAxis(Vector3D value) => value.X.ToString("0.000000", CultureInfo.InvariantCulture)
            + ":" + value.Y.ToString("0.000000", CultureInfo.InvariantCulture)
            + ":" + value.Z.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
