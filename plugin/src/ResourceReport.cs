using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EMMA3.Client
{
    // Formatting is independent of live blocks so units and missing readings can be tested.
    internal static class ResourceReport
    {
        public static string Format(
            string productName,
            double cargoCurrent,
            double cargoCapacity,
            double hydrogen,
            double hydrogenCapacity,
            IDictionary<string, double> ores,
            DateTime timestamp)
        {
            var document = new StringBuilder();
            document.AppendLine("[" + productName + "]");
            document.AppendLine("Cargo Capacity: " + WholeNumber(cargoCapacity * 1000.0) + " Liters");
            document.AppendLine("Cargo: " + Percentage(cargoCurrent, cargoCapacity, "0.00"));
            document.AppendLine("H2O: " + Percentage(hydrogen, hydrogenCapacity, "0"));
            document.AppendLine();
            var oreNames = new List<string>(ores.Keys);
            oreNames.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string ore in oreNames)
                document.AppendLine(ore + ": " + WholeNumber(ores[ore]));
            document.AppendLine();
            document.AppendLine("Timestamp: " + timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            return document.ToString();
        }

        private static string Percentage(double amount, double capacity, string format)
        {
            return capacity <= 0 ? "Unknown" : (amount / capacity * 100.0).ToString(format, CultureInfo.InvariantCulture) + "%";
        }

        private static string WholeNumber(double value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');
        }
    }
}
