string unloadRequestTag { get { return shipNames.Channel("EMM_A_UNLOAD_REQUEST_V1"); } }
string unloadReplyTag { get { return shipNames.Channel("EMM_A_UNLOAD_REPLY_V1"); } }
class DockUnloadRequest
{
    public long Session, CarrierConnector, MinerConnector;
    public string Mission = "";
    public readonly List<long> Sources = new List<long>();
    public static bool TryParse(string text, out DockUnloadRequest request)
    {
        request = null;
        if (string.IsNullOrEmpty(text) || text.Length > 8192) return false;
        string[] parts = text.Split('|');
        var value = new DockUnloadRequest();
        if (parts.Length != 6 || parts[0] != "Unload"
            || !long.TryParse(parts[1], out value.Session) || value.Session <= 0
            || !long.TryParse(parts[2], out value.CarrierConnector) || value.CarrierConnector == 0
            || !long.TryParse(parts[3], out value.MinerConnector) || value.MinerConnector == 0
            || parts[4].Length > 128) return false;
        value.Mission = parts[4];
        string[] ids = parts[5].Split(',');
        if (ids.Length == 0 || ids.Length > 128) return false;
        var seen = new HashSet<long>();
        foreach (string token in ids)
        {
            long id;
            if (!long.TryParse(token, out id) || id == 0 || !seen.Add(id)) return false;
            value.Sources.Add(id);
        }
        request = value;
        return true;
    }
}
