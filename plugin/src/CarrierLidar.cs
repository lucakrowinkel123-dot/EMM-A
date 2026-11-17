using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;
using IMyConveyorSorter = Sandbox.ModAPI.Ingame.IMyConveyorSorter;

namespace EMMA3.Client
{
    internal sealed class CarrierLidar
    {
        private IMyTerminalBlock commander;
        private readonly List<IMyTerminalBlock> lidars = new List<IMyTerminalBlock>();

        public void Clear()
        {
            commander = null;
            lidars.Clear();
        }

        public void Bind(IMyTerminalBlock carrier, List<IMyTerminalBlock> blocks)
        {
            Clear();
            commander = carrier;
            long grid = carrier.CubeGrid.EntityId;
            foreach (var block in blocks)
                if (block.CubeGrid?.EntityId == grid && block is IMyConveyorSorter
                    && DetectorScanPolicy.IsSdxLidar(block.BlockDefinition.SubtypeName))
                    lidars.Add(block);
        }

        // A zero occupied-grid ID allows existing out-of-seat services on the bound carrier.
        // Ore queries must originate from the occupied carrier.
        public bool Available(long player, long occupiedGrid, BoundingBoxD? asteroid = null)
        {
            if (player == 0 || commander == null || commander.Closed || commander.MarkedForClose || commander.CubeGrid == null) return false;
            long carrierGrid = commander.CubeGrid.EntityId;
            if (occupiedGrid == 0 && asteroid.HasValue) return false;
            if (occupiedGrid != 0 && occupiedGrid != carrierGrid) return false;
            foreach (var block in lidars)
            {
                if (block.Closed || block.MarkedForClose || block.CubeGrid?.EntityId != carrierGrid ||
                    !((IMyConveyorSorter)block).Enabled || !block.IsWorking || !block.HasPlayerAccess(player))
                    continue;
                if (!asteroid.HasValue) return true;

                var slider = block.GetProperty("Weapon Range") as ITerminalProperty<float>;
                if (slider == null) continue;
                double radius = DetectorScanPolicy.Range(slider.GetValue(block), slider.GetMaximum(block));
                if (DetectorScanPolicy.Overlaps(block.GetPosition(), radius, asteroid.Value)) return true;
            }
            return false;
        }
    }
}
