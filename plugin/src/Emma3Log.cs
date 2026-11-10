using System;
using System.IO;
using System.Text;

namespace EMMA3.Client
{
    internal static class Emma3Log
    {
        private static readonly object Gate = new object();
        private static string directory;
        private static string path;
        private static string bootstrapPath;
        private static long maxBytes;
        private static int fileCount;
        private static volatile bool enabled = true;
        public static void SetEnabled(bool value) => enabled = value;
        public static void InitializeBootstrap()
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(bootstrapPath))
                    return;
                try
                {
                    string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (string.IsNullOrWhiteSpace(documents))
                        throw new InvalidOperationException("Windows Documents folder is unavailable.");
                    string bootstrapDirectory = Path.Combine(documents, "EMM-A 3 Logs");
                    Directory.CreateDirectory(bootstrapDirectory);
                    bootstrapPath = Path.Combine(bootstrapDirectory, "EMM-A 3 Plugin.log");
                }
                catch
                {
                    try
                    {
                        bootstrapPath = Path.Combine(Path.GetTempPath(), "EMMA3-bootstrap.log");
                    }
                    catch
                    {
                        bootstrapPath = null;
                    }
                }
            }
        }

        public static void Initialize(string userDataPath, long maximumBytes, int retainedFiles)
        {
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(userDataPath))
                    throw new ArgumentException("Space Engineers user-data path is empty.", nameof(userDataPath));
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                directory = string.IsNullOrWhiteSpace(documents) ? Path.Combine(userDataPath, "Storage", "EMMA3") : Path.Combine(documents, "EMM-A 3 Logs");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "EMM-A 3 Plugin.log");
                maxBytes = Math.Max(1024 * 1024, maximumBytes);
                fileCount = Math.Max(1, retainedFiles);
                RotateIfNeeded();
            }
        }

        public static void Info(string message)
        {
            if (enabled) Write("INFO", message);
        }
        public static void Warn(string message)
        {
            if (!enabled) return;
            Write("WARN", message);
            WriteBootstrap("WARN", message);
        }

        public static void Diagnostic(string message)
        {
            if (!enabled) return;
            Write("INFO", message);
            WriteBootstrap("INFO", message);
        }

        public static void Error(string message)
        {
            if (!enabled) return;
            // While logging is enabled, mirror errors to the bootstrap log because
            // MyFileSystem.UserDataPath can be redirected by a sandbox.
            Write("ERROR", message);
            WriteBootstrap("ERROR", message);
        }

        public static void Plan(string missionId, string message)
        {
            if (!enabled) return;
            string level = "PLAN " + missionId;
            Write(level, message);
            WriteBootstrap(level, message);
        }

        public static void BootstrapInfo(string message)
        {
            if (enabled) WriteBootstrapOnly("INFO", message);
        }
        public static void BootstrapError(string message)
        {
            if (enabled) WriteBootstrapOnly("ERROR", message);
        }
        public static void Script(string craft, string message)
        {
            if (!enabled) return;
            lock (Gate)
            {
                try
                {
                    if (string.IsNullOrEmpty(directory))
                        return;
                    string safeCraft = string.Equals(craft, "Miner", StringComparison.OrdinalIgnoreCase) ? "Miner" : "Carrier";
                    string target = Path.Combine(directory, "EMM-A 3 " + safeCraft + ".log");
                    RotateIfNeeded(target);
                    Append(target, "SCRIPT", message);
                }
                catch (Exception ex)
                {
                    WriteBootstrapUnsafe("ERROR", "Script log write failed for " + craft + ": " + ex.Message);
                }
            }
        }

        private static void Write(string level, string message)
        {
            lock (Gate)
            {
                try
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        WriteBootstrapUnsafe(level, message);
                        return;
                    }

                    RotateIfNeeded();
                    Append(path, level, message);
                }
                catch (Exception ex)
                {
                    WriteBootstrapUnsafe("ERROR", "Main log write failed: " + ex.Message + "; original " + level + ": " + message);
                }
            }
        }

        private static void WriteBootstrap(string level, string message)
        {
            lock (Gate)
            {
                if (string.IsNullOrEmpty(bootstrapPath))
                    InitializeBootstrap();
                WriteBootstrapUnsafe(level, message);
            }
        }

        private static void WriteBootstrapOnly(string level, string message)
        {
            lock (Gate)
            {
                if (string.IsNullOrEmpty(bootstrapPath))
                    InitializeBootstrap();
                try
                {
                    if (!string.IsNullOrEmpty(bootstrapPath))
                        Append(bootstrapPath, level, message);
                }
                catch
                {
                }
            }
        }

        private static void WriteBootstrapUnsafe(string level, string message)
        {
            try
            {
                // Once normal logging is initialized, bootstrap and plugin logs
                // intentionally share one file. Avoid writing every warning or
                // diagnostic twice while still retaining pre-session messages.
                if (!string.IsNullOrEmpty(bootstrapPath) && !string.Equals(bootstrapPath, path, StringComparison.OrdinalIgnoreCase))
                    Append(bootstrapPath, level, message);
            }
            catch
            {
            }
        }

        private static void Append(string target, string level, string message)
        {
            File.AppendAllText(target, DateTime.UtcNow.ToString("O") + " [" + level + "] " + (message ?? "") + Environment.NewLine, new UTF8Encoding(false));
        }

        public static string ExportFailure(string missionId, string content)
        {
            if (!enabled) return "";
            lock (Gate)
            {
                if (string.IsNullOrEmpty(directory))
                    return "";
                string safe = string.IsNullOrWhiteSpace(missionId) ? DateTime.UtcNow.Ticks.ToString() : missionId.Replace(':', '-');
                string result = Path.Combine(directory, "failed-plan-" + safe + ".txt");
                File.WriteAllText(result, content ?? "", new UTF8Encoding(false));
                return result;
            }
        }

        private static void RotateIfNeeded()
        {
            RotateIfNeeded(path);
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
        }
    }
}
