static class LowAllocationText
{
    public static int CountScriptLogBatch(System.Collections.Generic.List<string> pending, int maxEntries, int maxEncodedChars)
    {
        if (pending == null || pending.Count == 0 || maxEntries <= 0 || maxEncodedChars <= 0)
            return 0;
        int limit = System.Math.Min(maxEntries, pending.Count);
        int count = 0;
        int encodedChars = 0;
        for (int i = 0; i < limit; i++)
        {
            string value = pending[i];
            int entryChars = 3; // "L|" plus the line ending.
            if (!string.IsNullOrEmpty(value))
            {
                for (int j = 0; j < value.Length; j++)
                {
                    char c = value[j];
                    entryChars += c == '\\' || c == '\r' || c == '\n' ? 2 : 1;
                }
            }
            if (count > 0 && encodedChars + entryChars > maxEncodedChars)
                break;
            count++;
            encodedChars += entryChars;
            if (encodedChars >= maxEncodedChars)
                break;
        }
        return count;
    }

    public static void AppendScriptLog(System.Text.StringBuilder target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\') target.Append("\\\\");
            else if (c == '\r') target.Append("\\r");
            else if (c == '\n') target.Append("\\n");
            else target.Append(c);
        }
    }

    public static void AppendPluginLcd(System.Text.StringBuilder target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '%') target.Append("%25");
            else if (c == '|') target.Append("%7C");
            else if (c == '\r') target.Append("%0D");
            else if (c == '\n') target.Append("%0A");
            else target.Append(c);
        }
    }
}
