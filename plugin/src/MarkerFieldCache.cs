using System;

namespace EMMA3.Client
{
    // One entry per fixed protocol channel, not per request. Never substitutes for presence checks.
    internal sealed class MarkerFieldCache
    {
        private readonly string marker;
        private long owner;
        private string line;
        private string[] fields;

        internal MarkerFieldCache(string marker) { this.marker = marker; }

        internal bool TryRead(long ownerId, string detail, out string[] result)
        {
            result = null;
            if (detail == null) return false;
            int start = detail.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            int end = detail.IndexOf('\n', start);
            int cr = detail.IndexOf('\r', start);
            if (end < 0 || cr >= 0 && cr < end) end = cr;
            int length = (end < 0 ? detail.Length : end) - start;
            if (owner != ownerId || line == null || line.Length != length
                || string.CompareOrdinal(detail, start, line, 0, length) != 0)
            {
                owner = ownerId;
                line = detail.Substring(start, length);
                fields = line.Split('|');
            }
            result = fields;
            return true;
        }
    }
}
