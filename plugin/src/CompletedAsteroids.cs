using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;

namespace EMMA3.Client
{
    internal sealed class CompletedAsteroids
    {
        private readonly OreVisitHistory ids = new OreVisitHistory();
        private bool timerInitialized;
        public CompletedAsteroids(string userDataPath)
        {
            // Read the former plugin-file storage once so an existing installation
            // can migrate its IDs into the Carrier timer. New changes are saved only
            // in the timer and this legacy file is never written again.
            string session = Sanitize(MyAPIGateway.Session?.Name ?? "unknown-session");
            ulong server = MyAPIGateway.Multiplayer == null ? 0UL : MyAPIGateway.Multiplayer.ServerId;
            string dir = Path.Combine(userDataPath, "Storage", "EMMA3");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "completed-" + server + "-" + session + ".txt");
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                    AddParsed(line);
        }

        public bool Contains(long id) => ids.Contains(id);
        public int Count => ids.Count;

        public void Add(long id, string ores = "")
        {
            if (id != 0)
                ids.Add(id, ores);
        }

        public OreSelection Remaining(long id, OreSelection selection) => ids.Remaining(id, selection);

        public void Clear()
        {
            ids.Clear();
        }

        // Returns true when legacy IDs need to be written into a newly-created,
        // empty Carrier timer. Later timer edits are authoritative, including a
        // deliberately cleared list.
        public bool SynchronizeFromTimer(string text)
        {
            bool firstSync = !timerInitialized;
            timerInitialized = true;
            bool emptyNewTimer = firstSync && string.IsNullOrWhiteSpace(text);
            if (emptyNewTimer && ids.Count > 0)
                return true;
            ids.Clear();
            foreach (string line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                AddParsed(line);
            return false;
        }

        public string Serialize()
        {
            return ids.Serialize();
        }

        private void AddParsed(string line)
        {
            ids.AddLine(line ?? "");
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Length > 80 ? value.Substring(0, 80) : value;
        }
    }
}
