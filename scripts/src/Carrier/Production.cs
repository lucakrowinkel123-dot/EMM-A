void LoadCarrierItems()
{
    string raw = itemsTimer == null ? "" : (itemsTimer.CustomData ?? "");
    itemNames.Clear();
    itemBlueprints.Clear();
    itemsMenuError = itemsTimer == null ? "Missing EMM-A Ca Items timer." : "";
    var lines = new List<string>();
    foreach (string value in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
    {
        string line = value.Trim();
        if (line.Length > 0 && !line.StartsWith("#") && !line.StartsWith("//"))
            lines.Add(line);
    }
    if (lines.Count % 2 != 0 || lines.Count > 256)
        itemsMenuError = "Items timer needs name/blueprint pairs (max 128).";
    for (int i = 0; i + 1 < lines.Count && i < 256; i += 2)
    {
        string id = lines[i + 1];
        if (!id.StartsWith("MyObjectBuilder_BlueprintDefinition/", StringComparison.Ordinal)
            || id.Length <= "MyObjectBuilder_BlueprintDefinition/".Length || id.IndexOf(',') >= 0 || itemBlueprints.Contains(id))
        {
            itemsMenuError = "Invalid or duplicate blueprint ID in Items timer.";
            continue;
        }
        itemNames.Add(lines[i]);
        itemBlueprints.Add(id);
    }
    PublishCarrierItems();
}

void PublishCarrierItems()
{
    MyIni ini;
    if (!TryReadEmma3Bridge(out ini))
        return;
    var text = new StringBuilder();
    for (int i = 0; i < itemNames.Count; i++)
        text.Append(itemNames[i]).Append('\n').Append(itemBlueprints[i]).Append('\n');
    // Keep the startup snapshot on one INI line for both bridge readers.
    string snapshot = itemsMenuError.Length > 0 ? "error:" + itemsMenuError
        : "1:" + text.ToString().Replace("%", "%25").Replace("\n", "%0A");
    ini.Set("EMMA3", "itemsCatalog", snapshot);
    WriteEmma3Bridge(ini);
}

int GetCarrierItemCount()
{
    return itemNames.Count;
}

bool CarrierItemEnabled(string id)
{
    return ("," + emma3EnabledItems + ",").IndexOf("," + id + ",", StringComparison.Ordinal) >= 0;
}

void AppendCarrierItemRows(StringBuilder sb)
{
    for (int i = 0; i < itemNames.Count; i++)
        AppendCarrierMenuRow(sb, "[" + (CarrierItemEnabled(itemBlueprints[i]) ? "Y" : "X") + "] " + itemNames[i], i + 2, false);
    if (itemNames.Count == 0)
        sb.AppendLine(itemsMenuError.Length > 0 ? "   " + itemsMenuError : "   Add name/blueprint pairs to Items timer.");
}

void ToggleCarrierItem(int index)
{
    if (index < 0 || index >= itemBlueprints.Count || itemsMenuError.Length > 0)
        return;
    string id = itemBlueprints[index];
    var enabled = new List<string>(emma3EnabledItems.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
    if (!enabled.Remove(id))
        enabled.Add(id);
    emma3EnabledItems = string.Join(",", enabled);
    WriteEmma3Settings();
}

double ClampIceReserveMillionLiters(double value)
{
    return double.IsNaN(value) || double.IsInfinity(value) ? 1 : Math.Max(1, Math.Min(1000000, value));
}
