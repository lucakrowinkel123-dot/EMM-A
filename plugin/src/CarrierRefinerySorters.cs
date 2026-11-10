using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace EMMA3.Client
{
    internal sealed class CarrierRefinerySorters
    {
        private ShipNames names = new ShipNames();
        internal string InputTag { get { return names.Ca("Refinery In"); } }
        internal string OutputTag { get { return names.Ca("Refinery Out"); } }
        private readonly List<IMyConveyorSorter> sorters = new List<IMyConveyorSorter>();
        private readonly List<MyInventoryItemFilter> filters = new List<MyInventoryItemFilter>();
        private string signature = "";
        private string selectedOre = "";
        private int nextSorter;
        private int rolloutRemaining;

        public string Update(IList<IMyTerminalBlock> blocks, string requestedOre, ShipNames configured = null)
        {
            if (configured != null) names = configured;
            Refresh(blocks);
            string ore = (requestedOre ?? "").Trim();
            if (!string.Equals(ore, selectedOre, StringComparison.OrdinalIgnoreCase)
                && (selectedOre.Length == 0 || rolloutRemaining == 0))
            {
                selectedOre = ore;
                nextSorter = 0;
                rolloutRemaining = sorters.Count;
                if (ore.Length > 0)
                    Emma3Log.Info("Refinery sorter priority changed to " + ore + ".");
            }
            ore = selectedOre;
            if (sorters.Count == 0)
                return "No refinery sorters";
            if (ore.Length == 0)
                return "Waiting for available refinery ore";

            if (nextSorter >= sorters.Count)
                nextSorter = 0;
            IMyConveyorSorter sorter = sorters[nextSorter];
            nextSorter = (nextSorter + 1) % sorters.Count;
            if (rolloutRemaining > 0)
                rolloutRemaining--;
            bool input = string.Equals(sorter.CustomName, InputTag, StringComparison.OrdinalIgnoreCase);
            MyConveyorSorterMode wantedMode = input ? MyConveyorSorterMode.Whitelist : MyConveyorSorterMode.Blacklist;
            filters.Clear();
            sorter.GetFilterList(filters);
            string wantedItem = "MyObjectBuilder_Ore/" + ore;
            bool matches = sorter.Mode == wantedMode && filters.Count == 1
                && string.Equals(filters[0].ItemId.ToString(), wantedItem, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                filters.Clear();
                filters.Add(new MyInventoryItemFilter(wantedItem, false));
                sorter.SetFilter(wantedMode, filters);
            }
            return "Refinery " + ore + "; sorter " + (nextSorter == 0 ? sorters.Count : nextSorter) + "/" + sorters.Count;
        }

        private void Refresh(IList<IMyTerminalBlock> blocks)
        {
            var found = new List<IMyConveyorSorter>();
            if (blocks != null)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    IMyTerminalBlock block = blocks[i];
                    IMyConveyorSorter sorter = block as IMyConveyorSorter;
                    if (sorter == null || block == null || !block.IsFunctional
                        || !block.HasPlayerAccess(Sandbox.ModAPI.MyAPIGateway.Session.Player.IdentityId))
                        continue;
                    if (string.Equals(block.CustomName, InputTag, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(block.CustomName, OutputTag, StringComparison.OrdinalIgnoreCase))
                        found.Add(sorter);
                }
            }
            found.Sort((a, b) =>
            {
                bool aOutput = string.Equals(a.CustomName, OutputTag, StringComparison.OrdinalIgnoreCase);
                bool bOutput = string.Equals(b.CustomName, OutputTag, StringComparison.OrdinalIgnoreCase);
                if (aOutput != bOutput)
                    return aOutput ? -1 : 1;
                return a.EntityId.CompareTo(b.EntityId);
            });
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < found.Count; i++)
                text.Append(found[i].EntityId).Append(';');
            string updated = text.ToString();
            if (updated == signature)
                return;
            signature = updated;
            sorters.Clear();
            sorters.AddRange(found);
            nextSorter = 0;
            rolloutRemaining = sorters.Count;
        }
    }
}
