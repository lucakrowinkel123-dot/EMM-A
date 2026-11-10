// Carrier-owned report persistence. Legacy service/interruption fields preserve old saves.
class MiningReportData
{
    public double Travel, Mining, Service, Elapsed;
    public int Interruptions;
    // A save freezes this small ore table by reference. The next actual delivery
    // copies it once; capture itself never formats ores or walks the table.
    public Dictionary<string, double> Ores { get { return oreState.Values; } }
    public long OreRevision { get; private set; }
    sealed class OreState
    {
        public Dictionary<string, double> Values = new Dictionary<string, double>(StringComparer.Ordinal);
        public string Encoded = "";
        public bool Frozen;
    }
    OreState oreState = new OreState();

    public sealed class Snapshot
    {
        internal double Travel, Mining, Service, Elapsed;
        internal int Interruptions;
        OreState ores;
        Dictionary<string, double>.Enumerator cursor;
        StringBuilder builder;
        public string Text { get; private set; }

        internal static Snapshot Capture(MiningReportData source)
        {
            source.oreState.Frozen = true;
            return new Snapshot { Travel = source.Travel, Mining = source.Mining,
                Service = source.Service, Elapsed = source.Elapsed,
                Interruptions = source.Interruptions, ores = source.oreState };
        }

        public bool Prepare(int oreBudget)
        {
            if (Text != null) return true;
            if (ores.Encoded == null)
            {
                if (builder == null)
                {
                    builder = new StringBuilder();
                    cursor = ores.Values.GetEnumerator();
                }
                int written = 0;
                while (written < Math.Max(1, oreBudget))
                {
                    if (!cursor.MoveNext())
                    {
                        ores.Encoded = builder.ToString();
                        break;
                    }
                    var pair = cursor.Current;
                    if (builder.Length > 0) builder.Append(';');
                    builder.Append(pair.Key).Append('=').Append(N(pair.Value));
                    written++;
                }
                if (ores.Encoded == null) return false;
            }
            Text = N(Travel) + "~" + N(Mining) + "~" + N(Service) + "~" + N(Elapsed)
                + "~" + Interruptions + "~" + ores.Encoded;
            return true;
        }
    }

    public Snapshot CaptureSnapshot() { return Snapshot.Capture(this); }
    static string N(double value) { return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
    public static bool Number(string raw, out double value)
    {
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
    public void AddOre(string name, double amount)
    {
        if (amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
        if (oreState.Frozen)
            oreState = new OreState { Values = new Dictionary<string, double>(oreState.Values, StringComparer.Ordinal) };
        double previous;
        Ores.TryGetValue(name, out previous);
        Ores[name] = previous + amount;
        OreRevision++;
        oreState.Encoded = null;
    }
    public string Encode()
    {
        Snapshot snapshot = CaptureSnapshot();
        snapshot.Prepare(int.MaxValue);
        return snapshot.Text;
    }
    public static MiningReportData Decode(string raw)
    {
        MiningReportData result;
        return TryDecode(raw, out result) ? result : new MiningReportData();
    }
    public static bool TryDecode(string raw, out MiningReportData result)
    {
        result = new MiningReportData();
        string[] fields = (raw ?? "").Split('~');
        if (fields.Length != 6) return false;
        if (!Number(fields[0], out result.Travel) || !Number(fields[1], out result.Mining)
            || !Number(fields[2], out result.Service) || !Number(fields[3], out result.Elapsed)
            || !int.TryParse(fields[4], out result.Interruptions) || result.Interruptions < 0)
            return false;
        if (fields[5].Length == 0) return true;
        foreach (string ore in fields[5].Split(';'))
        {
            int split = ore.IndexOf('=');
            double amount;
            if (split <= 0 || !Number(ore.Substring(split + 1), out amount)) return false;
            string name = ore.Substring(0, split);
            if (result.Ores.ContainsKey(name)) return false;
            result.AddOre(name, amount);
        }
        return true;
    }
}
