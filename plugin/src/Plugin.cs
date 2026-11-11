using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Plugins;

namespace EMMA3.Client
{
    public sealed class Plugin : IPlugin
    {
        internal const string ProductName = "EMM-A 0.9.40";
        private Emma3Controller controller;
        private bool failed;
        private int initializationRetryTicks;
        private int initializationAttempts;
        private int consecutiveUpdateFailures;
        private object activeSession;
        private const int MaxConsecutiveUpdateFailures = 5;
        public void Init(object gameInstance)
        {
            // Startup messages are buffered until the game supplies its instance directory.
            Emma3Log.BootstrapInfo(ProductName + " loaded; waiting for the Space Engineers session.");
        }

        public void Update()
        {
            EmmaTiming.GameSimulationTime.Poll();
            object currentSession = MyAPIGateway.Session;
            if (currentSession == null || MyAPIGateway.Session.Player == null)
                return;
            if (!ReferenceEquals(activeSession, currentSession))
            {
                controller?.Detach();
                if (activeSession != null) Emma3Log.Dispose();
                controller = null;
                activeSession = currentSession;
                failed = false;
                initializationRetryTicks = 0;
                initializationAttempts = 0;
                consecutiveUpdateFailures = 0;
                Emma3Log.BootstrapInfo(ProductName + " detected a new Space Engineers world/session; request counters and asteroid caches were reset.");
            }

            if (failed)
                return;
            if (controller == null)
            {
                initializationRetryTicks++;
                if (initializationAttempts == 0 || initializationRetryTicks >= 300)
                    TryInitializeController();
                if (controller == null)
                    return;
            }

            try
            {
                controller.Update();
                if (controller.RebindRequested)
                {
                    controller.Detach();
                    controller = null;
                    TryInitializeController();
                }
                consecutiveUpdateFailures = 0;
            }
            catch (Exception ex)
            {
                consecutiveUpdateFailures++;
                Emma3Log.Error("Update failed; aborting planner for this session: " + ex);
                Exception root = ex.GetBaseException();
                try
                {
                    controller.Abort(CompactPluginFailure(root));
                }
                catch (Exception abortEx)
                {
                    Emma3Log.Error("Abort after plugin exception also failed: " + abortEx);
                }

                try
                {
                    controller.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Emma3Log.Error("Dispose after plugin exception failed: " + disposeEx);
                }

                controller = null;
                initializationRetryTicks = 0;
                if (consecutiveUpdateFailures >= MaxConsecutiveUpdateFailures)
                {
                    failed = true;
                    Emma3Log.Error("Controller failed " + consecutiveUpdateFailures + " times in a row; plugin retry loop stopped for this session. Reload the world/plugin after fixing the underlying error.");
                }
                else
                    Emma3Log.Warn("Controller will retry initialization after the normal retry delay instead of staying failed for the whole session. Consecutive failures " + consecutiveUpdateFailures
                        + "/" + MaxConsecutiveUpdateFailures
                        + ".");
            }
        }

        private static string CompactPluginFailure(Exception root)
        {
            const string workshopPoseFailure = "Workshop poses require finite x,y,z values; no default flight positions are assumed.";
            if (root is InvalidOperationException && string.Equals(root.Message, workshopPoseFailure, StringComparison.Ordinal))
                return "Error: Workshop pose X/Y/Z must be finite";
            return "Error: " + root.GetType().Name + ": " + root.Message;
        }

        public void Dispose()
        {
            try
            {
                controller?.Dispose();
            }
            finally
            {
                activeSession = null;
                Emma3Log.Info(ProductName + " stopped.");
                Emma3Log.Dispose();
            }
        }

        private void TryInitializeController()
        {
            initializationRetryTicks = 0;
            initializationAttempts++;
            try
            {
                string userDataPath = GetUserDataPath();
                Emma3Log.Initialize(userDataPath, 25 * 1024 * 1024, 3);
                controller = new Emma3Controller(userDataPath);
                Emma3Log.Diagnostic(ProductName + " session initialized after " + initializationAttempts + " attempt(s). User data: " + userDataPath);
            }
            catch (Exception ex)
            {
                // A temporarily unavailable game path must not disable the plugin.
                // Update retries this every 300 ticks while the player remains in-session.
                Emma3Log.BootstrapError("Session initialization attempt " + initializationAttempts + " failed; retrying: " + ex);
            }
        }

        private static string GetUserDataPath()
        {
            string result = MyFileSystem.UserDataPath;
            if (string.IsNullOrWhiteSpace(result) || !Path.IsPathRooted(result))
                throw new InvalidOperationException("No Space Engineers user-data path is available yet.");
            Directory.CreateDirectory(result);
            return result;
        }
    }
}
