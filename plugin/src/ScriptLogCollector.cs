using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace EMMA3.Client
{
    internal sealed class ScriptLogCollector
    {
        private const string Begin = "[EMMA3_SCRIPT_LOG_BEGIN]";
        private const string End = "[EMMA3_SCRIPT_LOG_END]";
        private string lastCarrierFailure = "";
        private string lastMinerFailure = "";
        private bool carrierConnectedLogged;
        private bool minerConnectedLogged;
        private string lastCarrierPilotFailure="",lastMinerPilotFailure="";
        private bool carrierPilotConnectedLogged,minerPilotConnectedLogged;
        private sealed class Receipt { public string Failure=""; public bool Connected; }
        private readonly Dictionary<long,Receipt> members=new Dictionary<long,Receipt>();
        private const string TimerHeader = "EMMA3_TIMER_LOG|1\n";
        private sealed class TimerReceipt
        {
            public string Snapshot = "";
            public HashSet<string> Records = new HashSet<string>(StringComparer.Ordinal);
        }
        private readonly Dictionary<long,TimerReceipt> timers = new Dictionary<long,TimerReceipt>();

        public void DrainTimer(IMyTerminalBlock timer, string label)
        {
            if (timer == null) return;
            string snapshot = timer.CustomData ?? "";
            if (snapshot.Length > 8192 || !snapshot.StartsWith(TimerHeader, StringComparison.Ordinal)) return;
            TimerReceipt receipt;
            if (!timers.TryGetValue(timer.EntityId, out receipt))
            {
                if (timers.Count >= 64) timers.Clear();
                timers.Add(timer.EntityId, receipt = new TimerReceipt());
            }
            if (receipt.Snapshot == snapshot) return;
            var records = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in snapshot.Substring(TimerHeader.Length).Split('\n'))
            {
                int separator = line.IndexOf('|');
                if (separator < 1) continue;
                string id = line.Substring(0, separator);
                if (!records.Add(id) || receipt.Records.Contains(id)) continue;
                Emma3Log.Script(label, Unescape(line.Substring(separator + 1)));
            }
            receipt.Records = records;
            receipt.Snapshot = snapshot;
        }
        public void DrainMember(IMyProgrammableBlock block,string label,bool enabled)
        {
            if(!enabled||block==null)return;
            Receipt receipt;
            if(!members.TryGetValue(block.EntityId,out receipt))
            {
                if(members.Count>=64)members.Clear();
                members.Add(block.EntityId,receipt=new Receipt());
            }
            DrainSafely(block,label,true,ref receipt.Failure,ref receipt.Connected);
        }
        public void Drain(IMyProgrammableBlock carrier, IMyProgrammableBlock miner,
            IMyProgrammableBlock carrierPilot, IMyProgrammableBlock minerPilot, bool enabled)
        {
            Drain(carrier,miner,enabled);
            if(!enabled)return;
            // Preserve craft log labels consumed by the testbed. Runtime messages
            // include the module role; the connected message includes the PB name.
            DrainSafely(carrierPilot,"Carrier",enabled,ref lastCarrierPilotFailure,ref carrierPilotConnectedLogged);
            DrainSafely(minerPilot,"Miner",enabled,ref lastMinerPilotFailure,ref minerPilotConnectedLogged);
        }
        public void Drain(IMyProgrammableBlock carrier, IMyProgrammableBlock miner, bool enabled)
        {
            if (!enabled)
                return;
            DrainSafely(carrier, "Carrier", enabled, ref lastCarrierFailure, ref carrierConnectedLogged);
            DrainSafely(miner, "Miner", enabled, ref lastMinerFailure, ref minerConnectedLogged);
        }

        private static void DrainSafely(IMyProgrammableBlock block, string craft, bool enabled, ref string lastFailure, ref bool connectedLogged)
        {
            if (block == null)
                return;
            try
            {
                int drained = DrainOne(block, craft, enabled);
                if (drained > 0 && !connectedLogged)
                {
                    Emma3Log.Info("Script logger connected to " + craft + " PB; drained " + drained + " message(s).");
                    connectedLogged = true;
                }

                lastFailure = "";
            }
            catch (Exception exception)
            {
                string failure = exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message;
                if (!string.Equals(failure, lastFailure, StringComparison.Ordinal))
                {
                    Emma3Log.Error("Script log collection failed for " + craft + " PB '" + block.CustomName + "': " + failure);
                    lastFailure = failure;
                }
            }
        }

        private static int DrainOne(IMyProgrammableBlock block, string craft, bool enabled)
        {
            string original = block.CustomData ?? "";
            int begin = original.IndexOf(Begin, StringComparison.Ordinal);
            if (begin < 0)
                return 0;
            int contentStart = begin + Begin.Length;
            int end = original.IndexOf(End, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                // Space Engineers truncates oversized CustomData.  A high-volume
                // script buffer can therefore retain the begin marker while losing
                // its end marker.  Remove the damaged tail so the script can create
                // a fresh, valid envelope on its next tick.
                int damagedStart = begin;
                while (damagedStart > 0 && (original[damagedStart - 1] == '\r' || original[damagedStart - 1] == '\n'))
                    damagedStart--;
                string repaired = original.Substring(0, damagedStart);
                block.CustomData = repaired;
                if (string.Equals(block.CustomData ?? "", original, StringComparison.Ordinal))
                    throw new InvalidOperationException("Space Engineers rejected removal of a truncated log envelope");
                Emma3Log.Warn("Recovered a truncated script-log envelope from " + craft + " PB '" + block.CustomName + "'.");
                return 0;
            }

            int drained = 0;
            // Each PB controls whether it produces logs. The Carrier's setting
            // must never discard messages already emitted by the Miner.
            {
                string payload = original.Substring(contentStart, end - contentStart);
                string[] lines = payload.Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("L|", StringComparison.Ordinal))
                        continue;
                    string message = Unescape(line.Substring(2));
                    Emma3Log.Script(craft, message);
                    drained++;
                }
            }

            int removeEnd = end + End.Length;
            while (removeEnd < original.Length && (original[removeEnd] == '\r' || original[removeEnd] == '\n'))
                removeEnd++;
            int removeStart = begin;
            while (removeStart > 0 && (original[removeStart - 1] == '\r' || original[removeStart - 1] == '\n'))
                removeStart--;
            string cleaned = original.Remove(removeStart, removeEnd - removeStart);
            block.CustomData = cleaned;
            if (string.Equals(block.CustomData ?? "", original, StringComparison.Ordinal))
                throw new InvalidOperationException("Space Engineers rejected removal of the collected log envelope");
            return drained;
        }

        private static string Unescape(string value)
        {
            var result = new StringBuilder(value.Length);
            bool escaped = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!escaped && c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (escaped)
                {
                    if (c == 'n')
                        result.Append('\n');
                    else if (c == 'r')
                        result.Append('\r');
                    else
                        result.Append(c);
                    escaped = false;
                }
                else
                    result.Append(c);
            }

            if (escaped)
                result.Append('\\');
            return result.ToString();
        }
    }
}
