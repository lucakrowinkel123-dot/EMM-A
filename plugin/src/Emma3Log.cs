using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EMMA3.Client
{
    internal static class Emma3Log
    {
        private static readonly object Gate = new object();
        private static readonly Queue<string> Startup = new Queue<string>();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private const int StartupLimit = 128;
        private static string directory;
        private static string path;
        private static long maxBytes;
        private static int fileCount;
        private static volatile bool enabled = true;
        public static void SetEnabled(bool value) => enabled = value;

        public static void Initialize(string userDataPath, long maximumBytes, int retainedFiles)
        {
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(userDataPath) || !Path.IsPathRooted(userDataPath))
                    throw new ArgumentException("Expected the Space Engineers user data directory.", nameof(userDataPath));
                string target = Path.Combine(userDataPath, "Storage", "EMMA3");
                Directory.CreateDirectory(target);
                directory = target;
                path = Path.Combine(directory, "EMM-A 3 Plugin.log");
                maxBytes = Math.Max(1024 * 1024, maximumBytes);
                fileCount = Math.Max(1, retainedFiles);
                RotateIfNeeded(path);
                while (Startup.Count > 0)
                {
                    File.AppendAllText(path, Startup.Peek(), Utf8);
                    Startup.Dequeue();
                }
            }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Diagnostic(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Plan(string missionId, string message) => Write("PLAN " + missionId, message);
        public static void BootstrapInfo(string message) => Write("INFO", message);
        public static void BootstrapError(string message) => Write("ERROR", message);

        private static string Line(string level, string message)
        {
            return DateTime.UtcNow.ToString("O") + " [" + level + "] " + message + Environment.NewLine;
        }

        private static void Write(string level, string message)
        {
            if (!enabled) return;
            lock (Gate)
            {
                string line = Line(level, message);
                if (path == null)
                {
                    if (Startup.Count == StartupLimit) Startup.Dequeue();
                    Startup.Enqueue(line);
                    return;
                }
                try
                {
                    RotateIfNeeded(path);
                    File.AppendAllText(path, line, Utf8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static void Script(string craft, string message)
        {
            if (!enabled) return;
            lock (Gate)
            {
                if (directory == null) return;
                try
                {
                    bool fleetMiner = craft != null && craft.StartsWith("Miner ", StringComparison.OrdinalIgnoreCase);
                    string safeCraft = fleetMiner || string.Equals(craft, "Miner", StringComparison.OrdinalIgnoreCase) ? "Miner" : "Carrier";
                    if (fleetMiner) message = craft + ": " + message;
                    string target = Path.Combine(directory, "EMM-A 3 " + safeCraft + ".log");
                    RotateIfNeeded(target);
                    File.AppendAllText(target, Line("SCRIPT", message), Utf8);
                }
                catch (Exception ex)
                {
                    Error("Script log write failed for " + craft + ": " + ex.Message);
                }
            }
        }

        public static string ExportFailure(string missionId, string content)
        {
            if (!enabled) return "";
            lock (Gate)
            {
                if (directory == null) return "";
                string safe = string.IsNullOrWhiteSpace(missionId) ? DateTime.UtcNow.Ticks.ToString() : missionId;
                foreach (char invalid in Path.GetInvalidFileNameChars())
                    safe = safe.Replace(invalid, '-');
                string result = Path.Combine(directory, "failed-plan-" + safe + ".txt");
                File.WriteAllText(result, content ?? "", Utf8);
                return result;
            }
        }

        private static void RotateIfNeeded(string target)
        {
            if (string.IsNullOrEmpty(target) || !File.Exists(target) || new FileInfo(target).Length < maxBytes)
                return;
            for (int i = fileCount - 1; i >= 1; i--)
            {
                string source = target + "." + i;
                string dest = target + "." + (i + 1);
                if (File.Exists(dest))
                    File.Delete(dest);
                if (File.Exists(source))
                    File.Move(source, dest);
            }

            string first = target + ".1";
            if (File.Exists(first))
                File.Delete(first);
            File.Move(target, first);
        }

        public static void Dispose()
        {
            lock (Gate)
            {
                path = null;
                directory = null;
                Startup.Clear();
                enabled = true;
            }
        }
    }
}
