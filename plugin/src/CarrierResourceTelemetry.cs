using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Ingame;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyTimerBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock;
using IMyGasTank = Sandbox.ModAPI.IMyGasTank;
using VRage.Game;

namespace EMMA3.Client
{
    internal sealed class CarrierResourceTelemetry
    {
        public readonly List<IMyTerminalBlock> CarrierBlocks = new List<IMyTerminalBlock>();
        public readonly Dictionary<string, double> Stock = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly Dictionary<string, double> RawOres = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> itemVolumes = new Dictionary<string, double>(StringComparer.Ordinal);
        public double IceLiters { get; private set; }
        public double WaterPercent { get; private set; }
        private readonly List<MyInventoryItem> items = new List<MyInventoryItem>();
        public void Publish(BlockContext context, bool collectProduction = false)
        {
            IMyTimerBlock timer = context.ResourcesTimer;
            context.GetCarrierBlocks(CarrierBlocks);
            RawOres.Clear();
            Stock.Clear();
            IceLiters = 0;
            double cargoCurrent = 0;
            double cargoCapacity = 0;
            double hydrogen = 0;
            double hydrogenCapacity = 0;
            for (int blockIndex = 0; blockIndex < CarrierBlocks.Count; blockIndex++)
            {
                IMyTerminalBlock block = CarrierBlocks[blockIndex];
                if (block == null)
                    continue;
                IMyGasTank tank = block as IMyGasTank;
                var tankDefinition = tank == null ? null : MyDefinitionManager.Static.GetCubeBlockDefinition(block.BlockDefinition) as MyGasTankDefinition;
                if (tankDefinition != null && string.Equals(tankDefinition.StoredGasId.SubtypeName, "Hydrogen", StringComparison.OrdinalIgnoreCase))
                {
                    hydrogen += tank.FilledRatio * tank.Capacity;
                    hydrogenCapacity += tank.Capacity;
                }

                bool namedCargo = block.CustomName.IndexOf(context.Names.Ca("Cargo Container"), StringComparison.OrdinalIgnoreCase) >= 0;
                bool productionStock = collectProduction && (block is IMyCargoContainer || block is IMyProductionBlock);
                bool iceStorage = collectProduction && (productionStock || block is IMyGasGenerator);
                if (!block.IsFunctional || !block.HasInventory || (!namedCargo && !iceStorage)
                    || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)
                    || block is IMyConveyorSorter || block is IMyShipConnector)
                    continue;
                for (int inventoryIndex = 0; inventoryIndex < block.InventoryCount; inventoryIndex++)
                {
                    var inventory = block.GetInventory(inventoryIndex);
                    if (inventory == null)
                        continue;
                    if (namedCargo)
                    {
                        cargoCurrent += (double)inventory.CurrentVolume;
                        cargoCapacity += (double)inventory.MaxVolume;
                    }
                    items.Clear();
                    inventory.GetItems(items);
                    for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                    {
                        MyInventoryItem item = items[itemIndex];
                        string itemId = item.Type.ToString();
                        double quantity = (double)item.Amount;
                        double current;
                        if (productionStock)
                        {
                            Stock.TryGetValue(itemId, out current);
                            Stock[itemId] = current + quantity;
                        }
                        if (iceStorage && itemId == "MyObjectBuilder_Ore/Ice")
                        {
                            double volume;
                            if (!itemVolumes.TryGetValue(itemId, out volume))
                            {
                                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(MyDefinitionId.Parse(itemId));
                                volume = definition == null ? 0 : definition.Volume * 1000.0;
                                itemVolumes[itemId] = volume;
                            }
                            IceLiters += quantity * volume;
                        }
                        if (!namedCargo || !string.Equals(item.Type.TypeId, "MyObjectBuilder_Ore", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string ore = item.Type.SubtypeId;
                        RawOres.TryGetValue(ore, out current);
                        RawOres[ore] = current + quantity;
                    }
                }
            }

            WaterPercent = hydrogenCapacity > 0 ? 100 * hydrogen / hydrogenCapacity : -1;
            if (timer == null)
                return;
            string serialized = ResourceReport.Format(Plugin.ProductName, cargoCurrent, cargoCapacity, hydrogen, hydrogenCapacity, RawOres, DateTime.Now);
            if (!string.Equals(timer.CustomData, serialized, StringComparison.Ordinal))
                timer.CustomData = serialized;
        }
    }
}
