using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private string fleetPublicationMission = "";
        private readonly List<string> fleetPublicationPaths = new List<string>();
        private string FleetPublicationFile
        {
            get { return Path.Combine(userDataPath, "Storage", "EMMA3", "fleet-routes-" + blocks.CarrierPb.EntityId.ToString(CultureInfo.InvariantCulture) + ".txt"); }
        }
        private static uint FleetRouteHash(string text)
        {
            uint hash = 2166136261;
            unchecked { foreach (char ch in text) { hash ^= ch; hash *= 16777619; } }
            return hash;
        }
        private void SaveFleetPublication(MissionPlan plan)
        {
            if (plan.FleetPaths.Count != plan.FleetMembers.Length) throw new InvalidOperationException("Fleet route count does not match registration.");
            var lines = new List<string> { "EMMA3-FLEET-ROUTES-1", Convert.ToBase64String(Encoding.UTF8.GetBytes(plan.MissionId)) };
            foreach (string path in plan.FleetPaths)
                lines.Add(PathCodec.Hash(path) + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
            string target = FleetPublicationFile, temporary = target + ".new";
            Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllLines(temporary, lines, Encoding.UTF8);
            if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            fleetPublicationMission = plan.MissionId; fleetPublicationPaths.Clear(); fleetPublicationPaths.AddRange(plan.FleetPaths);
        }
        private bool LoadFleetPublication(string mission, int count)
        {
            if (fleetPublicationMission == mission && fleetPublicationPaths.Count == count) return true;
            try
            {
                string path = FleetPublicationFile;
                if (!File.Exists(path) || new FileInfo(path).Length > 300000) return false;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length != count + 2 || lines[0] != "EMMA3-FLEET-ROUTES-1" || Encoding.UTF8.GetString(Convert.FromBase64String(lines[1])) != mission) return false;
                var paths = new List<string>();
                for (int i = 2; i < lines.Length; i++)
                {
                    string[] fields = lines[i].Split('|'); if (fields.Length != 2) return false;
                    string route = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
                    int records; string error;
                    if (route.Length > 61000 || PathCodec.Hash(route) != fields[0] || route.Length > 0 && !PathCodec.Validate(route, out records, out error)) return false;
                    paths.Add(route);
                }
                fleetPublicationMission = mission; fleetPublicationPaths.Clear(); fleetPublicationPaths.AddRange(paths); return true;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException) { return false; }
        }
        private void PumpFleetPublication(BridgeDocument bridge)
        {
            string mission = bridge.Get("fleetAssignmentMission"); int count = bridge.GetInt("fleetAssignmentCount");
            if (mission.Length == 0 || count < 1 || count > 3 || blocks.CarrierPb == null || blocks.FleetPathTimer == null) return;
            if (!LoadFleetPublication(mission, count)) return;
            int[] order;
            string savedOrder = bridge.Get("fleetPublishOrder");
            if (savedOrder.Length == 0)
            {
                // Existing in-flight missions keep their original registration order.
                order = new int[count]; for (int i = 0; i < count; i++) order[i] = i;
            }
            else if (!FleetRoutePriority.TryParseOrder(savedOrder, count, out order)) return;
            int next = FleetRoutePriority.Next(order, bridge.GetInt("fleetPathAck", -1), fleetPublicationPaths);
            if (next == -2) return; // Never skip an unrecognized acknowledgement.
            if (next == -1) { bridge.Set("fleetPublishSlot", count); return; }
            string text = fleetPublicationPaths[next];
            if (bridge.GetInt("fleetPublishSlot", -1) != next || blocks.FleetPathTimer.CustomData != text)
            {
                blocks.FleetPathTimer.CustomData = text;
                if (blocks.FleetPathTimer.CustomData != text) throw new InvalidOperationException("Carrier fleet path read-back failed.");
                bridge.Set("fleetPublishSlot", next); bridge.Set("fleetPublishLength", text.Length);
                bridge.Set("fleetPublishHash", FleetRouteHash(text));
            }
        }
    }
}
