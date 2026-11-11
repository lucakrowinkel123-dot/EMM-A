using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EMMA3.Client
{
    internal sealed class PlanRequestIdentity
    {
        public readonly int Sequence;
        public readonly string Intent, Kind;
        public readonly long Target;
        public PlanRequestIdentity(BridgeDocument bridge)
        {
            Sequence = bridge.GetInt("request"); Intent = bridge.Get("planIntent");
            Kind = bridge.Get("planType"); long.TryParse(bridge.Get("requestedAsteroidEntityId"), out Target);
        }
        public bool Matches(BridgeDocument bridge)
        {
            long target; long.TryParse(bridge.Get("requestedAsteroidEntityId"), out target);
            return Sequence == bridge.GetInt("request") && Intent == bridge.Get("planIntent")
                && Kind == bridge.Get("planType") && Target == target && bridge.Get("command") == "plan";
        }
        public void Stamp(BridgeDocument bridge)
        {
            bridge.Set("planResultRequest", Sequence); bridge.Set("planResultIntent", Intent);
            bridge.Set("planResultType", Kind); bridge.Set("planResultTarget", Target);
        }
        public bool MatchesResult(BridgeDocument bridge)
        {
            long target; long.TryParse(bridge.Get("planResultTarget"), out target);
            return Sequence == bridge.GetInt("planResultRequest") && Intent == bridge.Get("planResultIntent")
                && Kind == bridge.Get("planResultType") && Target == target;
        }
    }

    // Only correlated planner response fields are journaled. Carrier commands,
    // permission switches and evolving route acknowledgements remain authoritative.
    internal sealed class PlannerResponseJournal
    {
        readonly string directory;
        readonly Dictionary<string, BridgeDocument> memory = new Dictionary<string, BridgeDocument>();
        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "state", "message", "planningExhausted", "planResultRequest", "planResultIntent", "planResultType", "planResultTarget",
            "missionId", "planType", "asteroidEntityId", "asteroidBoxMin", "asteroidBoxMax", "pathHash", "pathRecords", "planeCount",
            "fleetAssignmentMission", "fleetOwners", "fleetAssignmentCount", "fleetPublishOrder", "missionMinedOres", "missionPurpose", "carrierRouteCount"
        };
        public PlannerResponseJournal(string directory) { this.directory = directory; }
        static bool Numbered(string key, string prefix)
        {
            int index; return key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.Substring(prefix.Length), out index) && index >= 0;
        }
        static bool Owns(string key) { return Fields.Contains(key) || Numbered(key, "carrierRoute") || Numbered(key, "fleetAssignment") || Numbered(key, "fleetWorkMeters"); }
        static bool Terminal(BridgeDocument value) { string state = value.Get("state"); return state == "ready" || state == "ready_dryrun" || state == "error"; }
        static string Digest(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
        string FileName(PlanRequestIdentity request)
        {
            using (var hash = SHA256.Create()) return Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(request.Intent))).Replace("-", "") + ".txt");
        }
        static BridgeDocument Response(BridgeDocument source)
        {
            var response = new BridgeDocument();
            foreach (var pair in source.Entries) if (Owns(pair.Key)) response.Set(pair.Key, pair.Value);
            return response;
        }
        public void Remember(PlanRequestIdentity request, BridgeDocument bridge)
        {
            if (request == null || request.Intent.Length == 0 || !request.Matches(bridge)) return;
            request.Stamp(bridge);
            BridgeDocument response = Response(bridge);
            if (Terminal(response))
            {
                string file = FileName(request);
                if (File.Exists(file))
                {
                    var existing = Read(request);
                    if (existing.Get("missionId") != response.Get("missionId") || existing.Get("state") != response.Get("state"))
                        throw new InvalidOperationException("A committed planning result cannot be replaced for the same intent");
                    memory[request.Intent] = existing; return;
                }
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".new");
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(response.Serialize());
                    File.WriteAllText(temporary, "EMMA3-PLAN-REPLY-1\n" + Digest(bytes) + "\n" + Convert.ToBase64String(bytes), Encoding.UTF8);
                    File.Move(temporary, file); // Commit before any ready result becomes visible.
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            memory[request.Intent] = response;
        }
        BridgeDocument Read(PlanRequestIdentity request)
        {
            string file = FileName(request);
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > 250000) throw new InvalidDataException("Planning reply journal is oversized");
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length != 3 || lines[0] != "EMMA3-PLAN-REPLY-1") throw new InvalidDataException("Invalid planning reply journal");
            byte[] bytes = Convert.FromBase64String(lines[2]);
            if (Digest(bytes) != lines[1]) throw new InvalidDataException("Planning reply checksum mismatch");
            var response = BridgeDocument.Parse(Encoding.UTF8.GetString(bytes));
            if (!request.MatchesResult(response) || !Terminal(response)) throw new InvalidDataException("Planning reply does not match its saved intent");
            return response;
        }
        public bool Replay(PlanRequestIdentity request, BridgeDocument bridge)
        {
            if (request == null || request.Intent.Length == 0 || !request.Matches(bridge)) return false;
            BridgeDocument response;
            if (!memory.TryGetValue(request.Intent, out response)) response = Read(request);
            if (response == null) return false;
            if (!request.MatchesResult(response)) throw new InvalidDataException("Conflicting planning intent");
            bool newMission = response.Get("missionId").Length > 0 && response.Get("missionId") != bridge.Get("missionId");
            bool newFleet = response.Get("fleetAssignmentMission").Length > 0 && response.Get("fleetAssignmentMission") != bridge.Get("fleetAssignmentMission");
            if (newMission) bridge.Remove("carrierRouteCompletedMission");
            if (newFleet) { bridge.Set("fleetPathAck", -1); bridge.Set("fleetPublishSlot", -1); }
            foreach (var pair in response.Entries) if (Owns(pair.Key)) bridge.Set(pair.Key, pair.Value);
            if (bridge.Get("state") == "ready" || bridge.Get("state") == "ready_dryrun")
                bridge.Set("state", bridge.GetBool("allowMovement", true) ? "ready" : "ready_dryrun");
            memory[request.Intent] = response; return true;
        }
    }
}
