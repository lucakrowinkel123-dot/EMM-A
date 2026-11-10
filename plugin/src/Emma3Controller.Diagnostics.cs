using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private static void TryRun(Sandbox.ModAPI.Ingame.IMyProgrammableBlock pb, string command)
        {
            try
            {
                pb?.TryRun(command);
            }
            catch
            {
            }
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        private static double DistanceToBox(Vector3D p, BoundingBoxD b)
        {
            return Vector3D.Distance(p, Vector3D.Clamp(p, b.Min, b.Max));
        }

        private static string FormatCount(long value)
        {
            if (value >= 1000000000)
                return (value / 1000000000.0).ToString("0.0") + "B";
            if (value >= 1000000)
                return (value / 1000000.0).ToString("0.0") + "M";
            if (value >= 1000)
                return (value / 1000.0).ToString("0.0") + "K";
            return value.ToString();
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value == TimeSpan.Zero)
                return "calculating";
            if (value.TotalHours >= 1)
                return value.TotalHours.ToString("0.0") + "h";
            if (value.TotalMinutes >= 1)
                return value.TotalMinutes.ToString("0.0") + "m";
            return value.TotalSeconds.ToString("0") + "s";
        }

        private static string DescribeTargetOres(OreSelection selection)
        {
            if (selection == null || selection.Targets.Count == 0)
                return "none";
            var names = new List<string>(selection.Targets);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", names);
        }

        private string DescribeDetectedOres()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AsteroidMap value in cache.Values)
                foreach (string ore in value.OreSamples.Keys)
                    names.Add(ore);
            if (names.Count == 0)
                return "none";
            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", sorted);
        }

        private static string DescribeMapOres(AsteroidMap value)
        {
            if (value == null || value.OreSamples.Count == 0)
                return "none";
            var names = new List<string>();
            foreach (var pair in value.OreSamples)
                names.Add(pair.Key + "=" + pair.Value.Count);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", names);
        }

        private static string DescribeFailure(AsteroidMap map, MissionPlan plan, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("EMM-A 0.6.79 failed plan");
            sb.AppendLine(ex.ToString());
            if (map != null)
            {
                sb.AppendLine("Asteroid=" + map.EntityId);
                sb.AppendLine("EntityBox=" + map.EntityBox);
                sb.AppendLine("OccupiedBox=" + map.OccupiedBox);
                sb.AppendLine("Samples=" + map.Samples.Count);
                foreach (var pair in map.OreSamples)
                    sb.AppendLine("Ore " + pair.Key + "=" + pair.Value.Count);
            }

            if (plan != null)
            {
                sb.AppendLine("Mission=" + plan.MissionId);
                sb.AppendLine("Planes=" + plan.Planes.Count);
                sb.AppendLine(plan.MinerPathText ?? "");
            }

            return sb.ToString();
        }
    }
}
