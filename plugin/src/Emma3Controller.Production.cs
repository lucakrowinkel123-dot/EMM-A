using System;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private void UpdateProduction(BridgeDocument bridge)
        {
            bool active = bridge.GetBool("iceReserveActive");
            bool enabled = bridge.GetBool("iceReserveEnabled");
            double target = ProductionPolicy.ValidIceTarget(bridge.GetDouble("iceReserveLiters", ProductionPolicy.MinimumIceLiters));
            if (bridge.GetBool("autonomousMining") || !enabled || resources.IceLiters >= target)
                active = ProductionPolicy.IcePriority(enabled, active, resources.WaterPercent, resources.IceLiters, target);
            bridge.Set("iceReserveActive", active ? 1 : 0);
            bridge.Set("iceStoredLiters", Math.Floor(resources.IceLiters));
            try
            {
                production.Update(blocks, resources, bridge, frame);
                bridge.Set("productionStatus", production.Status);
            }
            catch (Exception ex)
            {
                bridge.Set("productionStatus", "Production error: " + ex.GetBaseException().Message);
                Emma3Log.Warn("Production update failed: " + ex.GetBaseException().Message);
            }
        }

        private void UpdateRefinerySorters(BridgeDocument bridge)
        {
            string selected = "";
            string status;
            try
            {
                OreSelection order = OreSelection.Parse(blocks.OreTimer == null ? "" : blocks.OreTimer.CustomData);
                if (bridge.Get("selectionMode").Equals("Items", StringComparison.OrdinalIgnoreCase))
                    selected = RefineryPriority.ChooseItemsMode(production.RefineryRequired,
                        production.RefineryDeficit, resources.RawOres, order.Ordered);
                else
                    selected = RefineryPriority.ChooseOreMode(order.Ordered, resources.RawOres);
                status = pluginOreTransfer
                    ? refinerySorters.Update(resources.CarrierBlocks, selected, blocks.Names)
                    : "Refinery sorter control Off with plugin ore transfer";
            }
            catch (Exception ex)
            {
                status = "Refinery sorter error: " + ex.GetBaseException().Message;
                Emma3Log.Warn(status);
            }
            bridge.Set("refineryOre", selected);
            bridge.Set("refinerySorterStatus", status);
        }

        private bool TryChoosePlanningOres(BridgeDocument bridge, out OreSelection ores, out string problem)
        {
            problem = "";
            ores = new OreSelection();
            // Freeze this choice for the entire published mission. No reserve or
            // menu change interrupts a Carrier route or an asteroid being mined.
            if (bridge.GetBool("iceReserveEnabled") && bridge.GetBool("iceReserveActive"))
            {
                planningPurpose = "Ice reserve";
                ores.Targets.Add("Ice");
                ores.Mineable.Add("Ice");
                return true;
            }
            if (bridge.Get("selectionMode").Equals("Items", StringComparison.OrdinalIgnoreCase))
            {
                planningPurpose = "Items";
                try
                {
                    production.Update(blocks, resources, bridge, frame, true);
                    problem = production.Error;
                    ores.Targets.UnionWith(production.NeededOres.Targets);
                    ores.Mineable.UnionWith(production.NeededOres.Mineable);
                }
                catch (Exception ex)
                {
                    problem = "Production calc failed: " + ex.GetBaseException().Message;
                }
                return problem.Length == 0;
            }
            planningPurpose = "Ore";
            ores = OreSelection.Parse(blocks.OreTimer.CustomData);
            return true;
        }
    }
}
