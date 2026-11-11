using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        // Called on the game thread while a registered reference miner is live.
        // A nonempty record is never replaced without an explicit user reset.
        private void CaptureRegisteredMinerDimensions(BridgeDocument bridge)
        {
            string existing = bridge.Get("minerDimensions");
            if (existing.Length != 0)
            {
                MinerDimensions parsed;
                bridge.Set("minerDimensionsStatus", MinerDimensions.TryParse(existing, out parsed) ? "ready" : "invalid");
                return;
            }
            if (!bridge.GetBool("minerDimensionsCapture", true)) return;
            if (blocks.MinerPb == null || blocks.MinerRemote == null || blocks.MinerConnector == null
                || blocks.MinerRemote.CubeGrid == null || blocks.MinerDrills.Count == 0) return;
            string identity = "";
            int count = Math.Min(3, Math.Max(0, bridge.GetInt("fleetCount")));
            for (int i = 0; i < count; i++)
            {
                long commander;
                if (long.TryParse(bridge.Get("fleetCommander" + i), out commander) && commander == blocks.MinerPb.EntityId)
                { identity = bridge.Get("fleetId" + i); break; }
            }
            if (identity.Length == 0) return;
            string requested = bridge.Get("minerDimensionsCaptureIdentity");
            if (requested.Length > 0 && requested != identity) return;
            MatrixD remote = blocks.MinerRemote.WorldMatrix;
            var drills = new List<DrillFootprint>();
            for (int i = 0; i < blocks.MinerDrills.Count; i++)
            {
                IMyTerminalBlock block = blocks.MinerDrills[i] as IMyTerminalBlock;
                if (block == null || block.PositionComp == null) continue;
                var cube = block as VRage.Game.ModAPI.IMyCubeBlock;
                MyShipDrillDefinition def = cube != null && cube.SlimBlock != null ? cube.SlimBlock.BlockDefinition as MyShipDrillDefinition : null;
                double radius = def != null && def.CutOutRadius > 0 ? def.CutOutRadius : Math.Max(1, block.PositionComp.WorldAABB.HalfExtents.Length());
                double offset = def != null ? def.CutOutOffset : 0;
                Vector3D delta = block.WorldMatrix.Translation + block.WorldMatrix.Forward * offset - remote.Translation;
                drills.Add(new DrillFootprint { LocalOffset = new Vector3D(Vector3D.Dot(delta, remote.Right), Vector3D.Dot(delta, remote.Up), Vector3D.Dot(delta, remote.Forward)), Radius = radius });
            }
            try
            {
                var dimensions = MinerDimensions.Capture(identity, blocks.MinerRemote.CubeGrid.WorldVolume.Radius, remote, blocks.MinerConnector.WorldMatrix, drills, settings.MinerTargetOffsetLocal);
                bridge.Set("minerDimensions", dimensions.Serialize());
                bridge.Set("minerDimensionsStatus", "ready");
                bridge.Set("minerDimensionsCapture", 0);
                bridge.Remove("minerDimensionsCaptureIdentity");
                Emma3Log.Info("Saved registered Miner dimensions from " + identity + ".");
            }
            catch (ArgumentException ex)
            {
                bridge.Set("minerDimensionsStatus", "invalid");
                Emma3Log.Warn("Registered Miner dimensions could not be saved: " + ex.Message);
            }
        }
    }
}
