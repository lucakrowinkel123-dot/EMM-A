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
        private readonly Row[] rows = new Row[4];
        private static readonly string[] roles = { "CarrierCommander", "CarrierPilot", "MinerCommander", "MinerPilot" };
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
                int role = Array.IndexOf(roles, fields[2]);
                if (role < 0) continue;
                Row row = rows[role];
                long block, epoch, samples; double average;
                if (row == null || row.Block == 0
                    || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out block) || block != row.Block
                    || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out epoch) || epoch <= 0
                    || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out samples)
                    || !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out average)
                    || average < 0 || double.IsNaN(average) || double.IsInfinity(average)) continue;
                if (epoch < row.Epoch || epoch == row.Epoch && samples <= row.Samples) continue;
                row.Progress = epoch == row.Epoch;
                row.Epoch = epoch; row.Samples = samples; row.Average = average; row.Changed = second;
            }
        }
        public string Render(string carrier, string miner, double second, Func<string, double> measure = null, bool minerHangared = false)
        {
            measure = measure ?? (value => value.Length);
            var labels = new string[5];
            var widths = new double[5];
            double widest = 0, space = Math.Max(1, measure(" "));
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = i == 4 ? "Total" : Short(i < 2 ? carrier : miner, 18) + (i % 2 == 0 ? " Commander" : " Pilot");
                widths[i] = measure(labels[i]);
                widest = Math.Max(widest, widths[i]);
            }
            var text = new StringBuilder(500);
            text.Append(' ').Append(Short(carrier, 28)).AppendLine(" | Runtime");
            text.AppendLine("—————————————————");
            text.AppendLine();
            double total = 0;
            bool complete = true;
            for (int i = 0; i < 4; i++)
            {
                Row row = rows[i];
                AppendLabel(text, labels[i], widths[i], widest, space);
                string unavailable = row == null || row.Block == 0 ? (i >= 2 && minerHangared ? "Hangared" : "Not loaded")
                    : row.Samples < 0 ? "Waiting"
                    : second - row.Changed > 5 ? "Stale"
                    : !row.Progress ? "Waiting"
                    : row.Samples <= 20 ? "WARMING" : null;
                if (unavailable != null) { text.Append(unavailable); complete = false; }
                else { AppendAverage(text, row.Average); total += row.Average; }
                text.AppendLine();
            }
            text.AppendLine();
            AppendLabel(text, labels[4], widths[4], widest, space);
            if (complete) AppendAverage(text, total); else text.Append("--");
            text.AppendLine();
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("—————————————————");
            text.Append(" Average per frame | 1s refresh");
            return text.ToString();
        }
        private static void AppendLabel(StringBuilder text, string label, double width, double widest, double space)
        {
            text.Append("   ").Append(label).Append(' ', 2 + (int)Math.Round((widest - width) / space)).Append("- ");
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
