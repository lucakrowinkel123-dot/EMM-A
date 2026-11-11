// Shared by the plugin, all four PB modules and offline naming checks.
public sealed class ShipNames
{
    public string Carrier = "EMM-A Ca", Miner = "EMM-A Mi";
    public string Pair { get { return Carrier.Length + ":" + Carrier + Miner.Length + ":" + Miner; } }
    public string Ca(string suffix) { return Carrier + " " + suffix; }
    public string Mi(string suffix) { return Miner + " " + suffix; }
    public string Channel(string tag) { return Carrier == "EMM-A Ca" && Miner == "EMM-A Mi" ? tag : tag + "|" + Pair; }
    public static ShipNames Read(string text, bool carrier)
    {
        var result = new ShipNames();
        bool inside = false, seen = false, ownSeen = false, peerSeen = false;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[", System.StringComparison.Ordinal))
            {
                inside = line.Equals("[EMMA]", System.StringComparison.OrdinalIgnoreCase);
                if (inside && seen) throw new System.ArgumentException("Duplicate [EMMA] section");
                seen |= inside;
                continue;
            }
            if (!inside) continue;
            int split = line.IndexOf('=');
            if (split < 0) continue;
            string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
            if (key.Equals(carrier ? "NameCarrier" : "NameMiner", System.StringComparison.OrdinalIgnoreCase))
                throw new System.ArgumentException("Wrong Commander naming role");
            bool own = key.Equals("NameShip", System.StringComparison.OrdinalIgnoreCase);
            bool peer = key.Equals(carrier ? "NameMiner" : "NameCarrier", System.StringComparison.OrdinalIgnoreCase);
            if (!own && !peer) continue;
            if (own && ownSeen || peer && peerSeen) throw new System.ArgumentException("Duplicate " + key);
            if (value.Length == 0 || value.Length > 96) throw new System.ArgumentException("Invalid " + key);
            foreach (char c in value) if (char.IsControl(c)) throw new System.ArgumentException("Invalid " + key);
            if (own) ownSeen = true; else peerSeen = true;
            if (own == carrier) result.Carrier = value; else result.Miner = value;
        }
        if (result.Carrier == result.Miner) throw new System.ArgumentException("Carrier and Miner names must differ");
        return result;
    }
}
