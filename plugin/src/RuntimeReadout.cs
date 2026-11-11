using System;
using System.Globalization;
using System.Text;

namespace EMMA3.Client
{
    // Consumes replicated PB reports only. There is no client execution measurement.
    public sealed class RuntimeReadout
    {
        private sealed class Row
        {
            public long Block, Epoch, Samples = -1;
            public double Changed;
            public double Average;
            public bool Progress;
        }
        private readonly Row[] rows = new Row[9];
        private static readonly string[] roles = { "CarrierCommander", "CarrierPilot", "MinerCommander", "MinerPilot", "CarrierLcd" };
        public void Bind(int role, long block)
        {
            if (rows[role] == null || rows[role].Block != block) rows[role] = new Row { Block = block };
        }
        public void Observe(string payload, double second)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > 131072) return;
            foreach (string raw in payload.Split('\n'))
            {
                if (!raw.StartsWith("EMMA3_RUNTIME|1|", StringComparison.Ordinal)) continue;
                string[] fields = raw.TrimEnd('\r').Split('|');
                if (fields.Length != 7) continue;
                long block, epoch, samples; double average;
                if (!long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out block) || block <= 0
                    || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out epoch) || epoch <= 0
                    || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out samples)
                    || !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out average)
                    || average < 0 || double.IsNaN(average) || double.IsInfinity(average)) continue;
                Row row = null;
                for (int i = 0; i < rows.Length; i++)
                    if (rows[i] != null && rows[i].Block == block && roles[i == 8 ? 4 : i < 2 ? i : 2 + (i - 2) % 2] == fields[2]) { row = rows[i]; break; }
                if (row == null) continue;
                if (epoch < row.Epoch || epoch == row.Epoch && samples <= row.Samples) continue;
                row.Progress = epoch == row.Epoch;
                row.Epoch = epoch; row.Samples = samples; row.Average = average; row.Changed = second;
            }
        }
        public string RenderFleet(string carrier, string[] miners, bool[] hangared, double second, Func<string, double> measure = null)
        {
            if (miners == null || hangared == null || miners.Length < 1 || miners.Length > 3 || hangared.Length != miners.Length) throw new ArgumentException("Invalid runtime fleet.");
            measure = measure ?? (value => value.Length);
            int count = 3 + miners.Length * 2;
            var labels = new string[count]; var widths = new double[count];
            double widest = 0, space = Math.Max(1, measure(" "));
            for (int i = 0; i < count; i++)
            {
                labels[i] = Short(i < 3 ? carrier : miners[(i - 3) / 2], 18)
                    + (i == 2 ? " LCD" : (i < 2 ? i : i - 3) % 2 == 0 ? " Commander" : " Pilot");
                widths[i] = measure(labels[i]); widest = Math.Max(widest, widths[i]);
            }
            var text = new StringBuilder(650); int lines = 2;
            text.Append(' ').Append(Short(carrier, 28)).AppendLine(" | Runtime").AppendLine("—————————————————");
            double total = 0; bool complete = true;
            for (int i = 0; i < count; i++)
            {
                bool stored = i >= 3 && hangared[(i - 3) / 2];
                if (stored && (i - 3) % 2 == 1) { text.Append("   ").AppendLine(labels[i]); lines++; continue; }
                Row row = rows[i < 2 ? i : i == 2 ? 8 : i - 1];
                AppendLabel(text, labels[i], widths[i], widest, space);
                string unavailable = stored ? "Hangared" : row == null || row.Block == 0 ? "Not loaded"
                    : row.Samples < 0 ? "Waiting" : second - row.Changed > 5 && (row.Progress || miners.Length == 1) ? "Stale"
                    : !row.Progress ? "Waiting" : row.Samples <= 20 ? "WARMING" : null;
                if (unavailable == null) { AppendAverage(text, row.Average); total += row.Average; }
                else { text.Append(unavailable); if (!stored) complete = false; }
                text.AppendLine(); lines++;
            }
            while (lines < 11) { text.AppendLine(); lines++; }
            text.AppendLine("—————————————————").Append(" Total - ");
            if (complete) AppendAverage(text, total); else text.Append("--");
            return text.ToString();
        }
        public string Render(string carrier, string miner, double second, Func<string, double> measure = null, bool minerHangared = false)
        {
            return RenderFleet(carrier, new[] { miner }, new[] { minerHangared }, second, measure);
        }
        private static void AppendLabel(StringBuilder text, string label, double width, double widest, double space)
        {
            text.Append("   ").Append(label).Append(' ', 3 + (int)Math.Round((widest - width) / space)).Append("- ");
        }
        private static void AppendAverage(StringBuilder text, double average)
        {
            text.Append(average >= 1000 ? ">=1000" : average.ToString("0.000", CultureInfo.InvariantCulture)).Append(" ms");
        }
        private static string Short(string value, int length)
        {
            value = (value ?? "Ship").Replace('\r', ' ').Replace('\n', ' ');
            return value.Length > length ? value.Substring(0, length - 3) + "..." : value;
        }
        public void Clear() { Array.Clear(rows, 0, rows.Length); }
    }
}
