using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed class VisualizationSnapshotStore
    {
        private const string Header = "EMMA3-VISUALIZATION-1";
        private readonly string path;
        public VisualizationSnapshotStore(string userDataPath) : this(userDataPath, BuildIdentity())
        {
        }

        internal VisualizationSnapshotStore(string userDataPath, string identity)
        {
            string directory = Path.Combine(userDataPath, "Storage", "EMMA3");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "current-visualization-" + Sanitize(identity) + ".txt");
        }

        public void Save(MissionPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.MissionId) || string.IsNullOrWhiteSpace(plan.PathHash))
                return;
            var sb = new StringBuilder(Math.Max(4096, (plan.MinerPathText ?? "").Length * 2));
            sb.AppendLine(Header);
            sb.AppendLine("mission=" + Encode(plan.MissionId));
            sb.AppendLine("hash=" + plan.PathHash);
            sb.AppendLine("asteroid=" + plan.AsteroidEntityId.ToString(CultureInfo.InvariantCulture));
            if (plan.CarrierApproachStart.HasValue)
                sb.AppendLine("carrierStart=" + FormatVector(plan.CarrierApproachStart.Value));
            for (int i = 0; i < plan.CarrierRoute.Count; i++)
            {
                RoutePose pose = plan.CarrierRoute[i];
                sb.AppendLine("carrier=" + FormatVector(pose.Position) + "|" + FormatVector(pose.Forward) + "|" + FormatVector(pose.Up));
            }

            sb.AppendLine("path=" + Encode(plan.MinerPathText ?? ""));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public bool TryLoad(string missionId, string pathHash, double travelClearance, out MissionPlan plan, out string reason)
        {
            plan = null;
            reason = "";
            if (string.IsNullOrWhiteSpace(missionId) || string.IsNullOrWhiteSpace(pathHash) || !File.Exists(path))
            {
                reason = "no matching visualization snapshot";
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 5 || lines[0].Trim() != Header)
                {
                    reason = "visualization snapshot format is invalid";
                    return false;
                }

                string savedMission = "", savedHash = "", encodedPath = "";
                long asteroid = 0;
                Vector3D? carrierStart = null;
                var carrierRoute = new List<RoutePose>();
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int equals = line.IndexOf('=');
                    if (equals <= 0)
                        continue;
                    string key = line.Substring(0, equals);
                    string value = line.Substring(equals + 1);
                    if (key == "mission")
                        savedMission = Decode(value);
                    else if (key == "hash")
                        savedHash = value;
                    else if (key == "asteroid")
                        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out asteroid);
                    else if (key == "path")
                        encodedPath = value;
                    else if (key == "carrierStart")
                    {
                        Vector3D start;
                        if (TryVector(value, out start) && start.IsValid())
                            carrierStart = start;
                    }
                    else if (key == "carrier")
                    {
                        string[] parts = value.Split('|');
                        Vector3D position, forward, up;
                        if (parts.Length == 3 && TryVector(parts[0], out position) && TryVector(parts[1], out forward) && TryVector(parts[2], out up))
                            carrierRoute.Add(new RoutePose { Position = position, Forward = forward, Up = up, Kind = "carrier" });
                    }
                }

                if (!string.Equals(savedMission, missionId, StringComparison.Ordinal) || !string.Equals(savedHash, pathHash, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "visualization snapshot belongs to another mission";
                    return false;
                }

                string minerPath = Decode(encodedPath);
                if (!string.Equals(PathCodec.Hash(minerPath), pathHash, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "visualization snapshot path hash does not match the bridge";
                    return false;
                }

                List<RoutePose> minerRoute;
                List<MiningPlane> planes;
                if (!PathCodec.TryReadVisualization(minerPath, travelClearance, out minerRoute, out planes, out reason))
                    return false;
                plan = new MissionPlan
                {
                    MissionId = savedMission,
                    PathHash = savedHash,
                    AsteroidEntityId = asteroid,
                    CarrierApproachStart = carrierStart,
                    MinerPathText = minerPath
                };
                plan.CarrierRoute.AddRange(carrierRoute);
                plan.MinerRoute.AddRange(minerRoute);
                plan.Planes.AddRange(planes);
                if (carrierRoute.Count > 0)
                    plan.CarrierPosition = carrierRoute[carrierRoute.Count - 1].Position;
                reason = "restored mission " + savedMission;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public void Clear()
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Emma3Log.Warn("Could not remove the current visualization snapshot: " + ex.Message);
            }
        }

        private static string BuildIdentity()
        {
            string session = MyAPIGateway.Session?.Name ?? "unknown-session";
            ulong server = MyAPIGateway.Multiplayer == null ? 0UL : MyAPIGateway.Multiplayer.ServerId;
            return server.ToString(CultureInfo.InvariantCulture) + "-" + session;
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        private static string FormatVector(Vector3D value) => value.X.ToString("R", CultureInfo.InvariantCulture) + "," + value.Y.ToString("R", CultureInfo.InvariantCulture) + "," + value.Z.ToString("R", CultureInfo.InvariantCulture);
        private static bool TryVector(string text, out Vector3D value)
        {
            value = Vector3D.Zero;
            string[] parts = (text ?? "").Split(',');
            double x, y, z;
            if (parts.Length != 3 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                return false;
            value = new Vector3D(x, y, z);
            return true;
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Length > 100 ? value.Substring(0, 100) : value;
        }
    }
}
