using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        const int RetainedReplies = 16;
        string cachedIntent;
        BridgeDocument cachedResponse;
        Queue<string> legacyFiles;
        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "state", "message", "planningExhausted", "planResultRequest", "planResultIntent", "planResultType", "planResultTarget",
            "missionId", "planType", "asteroidEntityId", "asteroidBoxMin", "asteroidBoxMax", "pathHash", "pathRecords", "planeCount",
            "fleetAssignmentMission", "fleetOwners", "fleetAssignmentCount", "fleetPublishOrder", "missionMinedOres", "missionPurpose", "carrierRouteCount"
        };
        public PlannerResponseJournal(string directory) { this.directory = Path.GetFullPath(directory); }
        static bool Numbered(string key, string prefix)
        {
            int index; return key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.Substring(prefix.Length), out index) && index >= 0;
        }
        static bool Owns(string key) { return Fields.Contains(key) || Numbered(key, "carrierRoute") || Numbered(key, "fleetAssignment") || Numbered(key, "fleetWorkMeters"); }
        static bool Terminal(BridgeDocument value) { string state = value.Get("state"); return state == "ready" || state == "ready_dryrun" || state == "error"; }
        static string Digest(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
        static string ReplyName(string intent) { return Digest(Encoding.UTF8.GetBytes(intent)) + ".txt"; }
        static string ScopedName(string intent)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(intent))).TrimEnd('=').Replace('/', '_').Replace('+', '-') + ".txt";
        }
        string CarrierDirectory(string intent)
        {
            int slash = intent.IndexOf('/');
            long carrier;
            if (slash < 1 || !long.TryParse(intent.Substring(0, slash), NumberStyles.Integer, CultureInfo.InvariantCulture, out carrier)) return directory;
            return Path.Combine(directory, carrier.ToString(CultureInfo.InvariantCulture));
        }
        string ExistingFile(string intent)
        {
            string folder = CarrierDirectory(intent);
            if (folder != directory)
            {
                string file = Path.Combine(folder, ScopedName(intent));
                if (File.Exists(file)) return file;
            }
            return Path.Combine(directory, ReplyName(intent));
        }
        void Cache(string intent, BridgeDocument response) { cachedIntent = intent; cachedResponse = response; }

        // A legacy save may contain many flat files. Move a small batch on each new
        // commit; both layouts remain readable even if the session ends mid-migration.
        void MaintainHistory(string intent, string committed)
        {
            try
            {
                if (legacyFiles == null) legacyFiles = new Queue<string>(Directory.GetFiles(directory, "*.txt"));
                for (int i = 0; i < 8 && legacyFiles.Count > 0; i++)
                {
                    string old = legacyFiles.Dequeue();
                    if (Path.GetFileNameWithoutExtension(old).Length != 64) continue;
                    try
                    {
                        string key = ReadDocument(old).Get("planResultIntent");
                        string dest = CarrierDirectory(key);
                        if (dest == directory || Path.GetFileName(old) != ReplyName(key)) continue;
                        Directory.CreateDirectory(dest);
                        File.Move(old, Path.Combine(dest, ScopedName(key)));
                    }
                    catch (IOException) { } // Another instance may have moved this file.
                    catch (UnauthorizedAccessException) { }
                    catch (InvalidDataException) { }
                    catch (FormatException) { }
                }
                string folder = CarrierDirectory(intent);
                if (folder == directory) return;
                var older = new DirectoryInfo(folder).GetFiles("*.txt")
                    .Where(f => f.FullName != committed && f.Name.Length == 47 && f.Name.Take(43).All(c =>
                        c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_'))
                    .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).ToArray();
                // Two commits can share a clock tick. Keep their write order even after a restart.
                if (older.Length > 0 && File.GetLastWriteTimeUtc(committed) <= older[0].LastWriteTimeUtc)
                    File.SetLastWriteTimeUtc(committed, older[0].LastWriteTimeUtc.AddTicks(1));
                foreach (var stale in older.Skip(RetainedReplies - 1)) stale.Delete();
            }
            catch (IOException) { } // A later commit retries retention without invalidating this committed plan.
            catch (UnauthorizedAccessException) { }
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
                string file = ExistingFile(request.Intent);
                if (File.Exists(file))
                {
                    var existing = Read(request);
                    if (existing.Get("missionId") != response.Get("missionId") || existing.Get("state") != response.Get("state"))
                        throw new InvalidOperationException("A committed planning result cannot be replaced for the same intent");
                    Cache(request.Intent, existing); return;
                }
                string folder = CarrierDirectory(request.Intent);
                Directory.CreateDirectory(folder);
                file = Path.Combine(folder, folder == directory ? ReplyName(request.Intent) : ScopedName(request.Intent));
                string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".new");
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(response.Serialize());
                    File.WriteAllText(temporary, "EMMA3-PLAN-REPLY-1\n" + Digest(bytes) + "\n" + Convert.ToBase64String(bytes), Encoding.UTF8);
                    File.Move(temporary, file); // Commit before any ready result becomes visible.
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                MaintainHistory(request.Intent, file);
            }
            Cache(request.Intent, response);
        }
        BridgeDocument Read(PlanRequestIdentity request)
        {
            string file = ExistingFile(request.Intent);
            if (!File.Exists(file)) return null;
            var response = ReadDocument(file);
            if (!request.MatchesResult(response) || !Terminal(response)) throw new InvalidDataException("Planning reply does not match its saved intent");
            return response;
        }
        static BridgeDocument ReadDocument(string file)
        {
            if (new FileInfo(file).Length > 250000) throw new InvalidDataException("Planning reply journal is oversized");
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length != 3 || lines[0] != "EMMA3-PLAN-REPLY-1") throw new InvalidDataException("Invalid planning reply journal");
            byte[] bytes = Convert.FromBase64String(lines[2]);
            if (Digest(bytes) != lines[1]) throw new InvalidDataException("Planning reply checksum mismatch");
            return BridgeDocument.Parse(Encoding.UTF8.GetString(bytes));
        }
        public bool Replay(PlanRequestIdentity request, BridgeDocument bridge)
        {
            if (request == null || request.Intent.Length == 0 || !request.Matches(bridge)) return false;
            BridgeDocument response = cachedIntent == request.Intent ? cachedResponse : Read(request);
            if (response == null) return false;
            if (!request.MatchesResult(response)) throw new InvalidDataException("Conflicting planning intent");
            bool newMission = response.Get("missionId").Length > 0 && response.Get("missionId") != bridge.Get("missionId");
            bool newFleet = response.Get("fleetAssignmentMission").Length > 0 && response.Get("fleetAssignmentMission") != bridge.Get("fleetAssignmentMission");
            if (newMission) bridge.Remove("carrierRouteCompletedMission");
            if (newFleet) { bridge.Set("fleetPathAck", -1); bridge.Set("fleetPublishSlot", -1); }
            foreach (var pair in response.Entries) if (Owns(pair.Key)) bridge.Set(pair.Key, pair.Value);
            if (bridge.Get("state") == "ready" || bridge.Get("state") == "ready_dryrun")
                bridge.Set("state", bridge.GetBool("allowMovement", true) ? "ready" : "ready_dryrun");
            Cache(request.Intent, response); return true;
        }
    }
}
