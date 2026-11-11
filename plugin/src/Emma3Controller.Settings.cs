using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private void ReadSettings(BridgeDocument bridge)
        {
            settings.SearchRadius = Clamp(bridge.GetDouble("searchRadius", 50000), 1000, 50000);
            settings.ParkingHeight = Clamp(bridge.GetDouble("parkingHeight", 400), 25, 5000);
            settings.PlaneClearance = Clamp(bridge.GetDouble("planeClearance", 25), 25, 100);
            settings.TravelClearance = Clamp(bridge.GetDouble("travelClearance", 75), 10, 1000);
            settings.Visualization = bridge.GetBool("visualization");
            settings.AllowMovement = bridge.GetBool("allowMovement");
            settings.AllowSeatExitDuringMining = bridge.GetBool("allowSeatExitDuringMining");
            settings.ScriptLogging = bridge.Get("scriptLogging").Length == 0 || bridge.GetBool("scriptLogging");
            Emma3Log.SetEnabled(settings.ScriptLogging);
            BridgeDocument miner = BridgeDocument.ReadMinerPlannerSettings(blocks.MinerPb?.CustomData, blocks.MinerStateTimer?.CustomData);
            settings.MineHeight = Clamp(bridge.GetDouble("minerLaneHeight", miner.GetDouble("MINEH", 12.5)), 0.1, 500);
            settings.MineWidth = Clamp(bridge.GetDouble("minerLaneWidth", miner.GetDouble("MINEW", 17.5)), 0.1, 500);
            settings.MinerRouteStartExtra = Clamp(bridge.GetDouble("minerRouteStartExtra", miner.GetDouble("minerRouteStartExtra", 0)), 0, 500);
            settings.MinerTargetOffsetLocal = new Vector3D(
                miner.GetDouble("TOFFR", bridge.GetDouble("minerTargetOffsetRight", 0)),
                miner.GetDouble("TOFFU", bridge.GetDouble("minerTargetOffsetUp", 0)),
                miner.GetDouble("TOFFF", bridge.GetDouble("minerTargetOffsetForward", 5)));
        }

        private static void EnsureVisualizationDefault(BridgeDocument bridge)
        {
            if (bridge.GetBool("visualizationDefault904Done")) return;
            bridge.Set("visualization", 1);
            bridge.Set("visualizationDefault904Done", 1);
        }

        private static void EnsureDefaults(BridgeDocument bridge)
        {
            if (bridge.Get("protocol").Length == 0)
                bridge.Set("protocol", 1);
            if (bridge.Get("request").Length == 0)
                bridge.Set("request", 0);
            if (bridge.Get("state").Length == 0)
                bridge.Set("state", "idle");
            if (bridge.Get("allowMovement").Length == 0)
                bridge.Set("allowMovement", 0);
            EnsureVisualizationDefault(bridge);
            if (bridge.Get("scriptLogging").Length == 0)
                bridge.Set("scriptLogging", 1);
            if (bridge.Get("allowSeatExitDuringMining").Length == 0)
                bridge.Set("allowSeatExitDuringMining", 0);
            if (bridge.Get("iceReserveEnabled").Length == 0)
                bridge.Set("iceReserveEnabled", 0);
            if (bridge.Get("iceReserveLiters").Length == 0)
                bridge.Set("iceReserveLiters", ProductionPolicy.MinimumIceLiters);
            if (bridge.Get("pluginOreTransfer").Length == 0)
                bridge.Set("pluginOreTransfer", 1);
            if (bridge.Get("pluginPowerTransitions").Length == 0)
                bridge.Set("pluginPowerTransitions", 1);
            if (bridge.Get("pluginRemoteLink").Length == 0)
                bridge.Set("pluginRemoteLink", 1);
            if (bridge.Get("pluginLcdOutput").Length == 0)
                bridge.Set("pluginLcdOutput", 1);
            if (bridge.Get("selectionMode").Length == 0)
                bridge.Set("selectionMode", "Ore");
            if (bridge.Get("searchRadius").Length == 0)
                bridge.Set("searchRadius", 50000);
            bool hasParkingHeight = bridge.Get("parkingHeight").Length != 0;
            if (!hasParkingHeight)
            {
                bridge.Set("parkingHeight", 400);
                bridge.Set("parkingHeight300MigrationDone", 1);
                bridge.Set("parkingHeight400MigrationDone", 1);
            }
            else
            {
                double parkingHeight = bridge.GetDouble("parkingHeight", 150);
                if (bridge.Get("parkingHeight300MigrationDone").Length == 0)
                {
                    parkingHeight = Math.Min(5000, parkingHeight + 150);
                    bridge.Set("parkingHeight300MigrationDone", 1);
                }
                if (bridge.Get("parkingHeight400MigrationDone").Length == 0)
                {
                    parkingHeight = Math.Min(5000, parkingHeight + 100);
                    bridge.Set("parkingHeight400MigrationDone", 1);
                }
                bridge.Set("parkingHeight", parkingHeight);
            }

            bool clearance2575MigrationDone = bridge.GetBool("clearance2575MigrationDone");
            if (bridge.Get("planeClearance").Length == 0 || bridge.GetDouble("planeClearance", 25) < 25)
                bridge.Set("planeClearance", 25);
            if (bridge.Get("travelClearance").Length == 0
                || (!clearance2575MigrationDone && Math.Abs(bridge.GetDouble("travelClearance", 75) - 50) < 0.001))
                bridge.Set("travelClearance", 75);
            if (!clearance2575MigrationDone)
                bridge.Set("clearance2575MigrationDone", 1);
            if (bridge.Get("resetSequence").Length == 0)
                bridge.Set("resetSequence", 0);
            if (bridge.Get("continueAvailable").Length == 0)
                bridge.Set("continueAvailable", 0);
            if (bridge.Get("minerStandardDepth").Length == 0)
                bridge.Set("minerStandardDepth", 30);
            if (bridge.Get("minerLaneWidth").Length == 0)
                bridge.Set("minerLaneWidth", 17.5);
            if (bridge.Get("minerLaneHeight").Length == 0)
                bridge.Set("minerLaneHeight", 12.5);
            if (bridge.Get("minerForwardSpeed").Length == 0)
                bridge.Set("minerForwardSpeed", 1);
            if (bridge.Get("minerReturnSpeed").Length == 0)
                bridge.Set("minerReturnSpeed", 6);
            if (bridge.Get("minerAutoService").Length == 0)
                bridge.Set("minerAutoService", 1);
            if (bridge.Get("minerAutoCargo").Length == 0)
                bridge.Set("minerAutoCargo", 90);
            if (bridge.Get("minerAutoHydrogen").Length == 0)
                bridge.Set("minerAutoHydrogen", 20);
            if (bridge.Get("minerRouteStartExtra").Length == 0)
                bridge.Set("minerRouteStartExtra", 0);
            if (bridge.Get("minerTargetOffsetRight").Length == 0)
                bridge.Set("minerTargetOffsetRight", 0);
            if (bridge.Get("minerTargetOffsetUp").Length == 0)
                bridge.Set("minerTargetOffsetUp", 0);
            if (bridge.Get("minerTargetOffsetForward").Length == 0)
                bridge.Set("minerTargetOffsetForward", 5);
        }
    }
}
