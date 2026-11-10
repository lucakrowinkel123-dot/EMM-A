using System;
using System.Collections.Generic;

namespace EMMA3.Client
{
    internal static class RefineryPriority
    {
        private const double Epsilon = 0.000001;

        public static string ChooseOreMode(IList<string> ordered, IDictionary<string, double> availableRaw)
        {
            if (ordered == null || availableRaw == null)
                return "";
            for (int i = 0; i < ordered.Count; i++)
            {
                string actual;
                if (TryAvailable(availableRaw, ordered[i], out actual))
                    return actual;
            }
            return "";
        }

        public static string ChooseItemsMode(IDictionary<string, double> required,
            IDictionary<string, double> deficit, IDictionary<string, double> availableRaw, IList<string> ordered)
        {
            if (required == null || deficit == null || availableRaw == null)
                return "";
            string best = "";
            double bestShortage = -1;
            int bestOrder = int.MaxValue;
            foreach (var entry in required)
            {
                string ore = entry.Key == null ? "" : entry.Key.Trim();
                string actual;
                if (ore.Length == 0 || entry.Value <= Epsilon || !TryAvailable(availableRaw, ore, out actual))
                    continue;
                double missing;
                deficit.TryGetValue(ore, out missing);
                if (missing <= Epsilon)
                    continue;
                double shortage = Math.Min(1, Math.Max(0, missing / entry.Value));
                int order = OrderOf(ordered, ore);
                if (shortage > bestShortage + Epsilon
                    || (Math.Abs(shortage - bestShortage) <= Epsilon && order < bestOrder)
                    || (Math.Abs(shortage - bestShortage) <= Epsilon && order == bestOrder
                        && string.Compare(ore, best, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    best = actual;
                    bestShortage = shortage;
                    bestOrder = order;
                }
            }
            return best;
        }

        private static bool TryAvailable(IDictionary<string, double> values, string ore, out string actual)
        {
            double amount;
            if (values.TryGetValue(ore, out amount) && amount > Epsilon)
            {
                actual = ore;
                return true;
            }
            string normalized = OreSelection.Normalize(ore);
            foreach (var value in values)
            {
                if (value.Value > Epsilon && string.Equals(OreSelection.Normalize(value.Key), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    actual = value.Key;
                    return true;
                }
            }
            actual = "";
            return false;
        }

        private static int OrderOf(IList<string> ordered, string ore)
        {
            if (ordered != null)
                for (int i = 0; i < ordered.Count; i++)
                    if (string.Equals(OreSelection.Normalize(ordered[i]), OreSelection.Normalize(ore), StringComparison.OrdinalIgnoreCase))
                        return i;
            return int.MaxValue;
        }
    }
}
