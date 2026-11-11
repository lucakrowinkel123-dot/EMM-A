using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private long fuelWarningCarrierId;
        // Called once when a Carrier bridge is first discovered. This reports only;
        // every planning request independently rechecks before any departure is prepared.
        private void InitializeFuelConnectionWarning(BridgeDocument bridge)
        {
            if (blocks.CarrierPb == null || blocks.CarrierPb.Closed || !blocks.IsCarrierPilot
                || !string.IsNullOrEmpty(blocks.Problem)) return;
            if (fuelWarningCarrierId == blocks.CarrierPb.EntityId) return;
            fuelWarningCarrierId = blocks.CarrierPb.EntityId;
            CheckFuelConnections(bridge, blocks.MinerPb != null && !blocks.MinerPb.Closed);
            string warning = bridge.Get("fuelConnectionWarning");
            if (warning.Length > 0 && (bridge.Get("state").Length == 0 || bridge.Get("state") == "idle"))
                bridge.Set("message", warning);
        }

        private bool ValidateFuelConnectionsForPlanning(BridgeDocument bridge)
        {
            bool liveMiner = blocks.MinerPb != null && !blocks.MinerPb.Closed;
            if (CheckFuelConnections(bridge, liveMiner || (!planningCarrierOnly && planningDimensions == null))) return true;
            Fail(bridge, bridge.Get("fuelConnectionWarning"), true);
            return false;
        }

        // Stored grids cannot be inspected during planning. Check the exact
        // deployed member before its route is sent and bind the result to its
        // current commander and mission, so an old result cannot authorize it.
        private void CheckFleetPublicationFuel(BridgeDocument bridge)
        {
            string request = bridge.Get("fReq");
            string[] fields = request.Split('|');
            if (fields.Length != 4) return;
            string mission = fields[1], identity = fields[2];
            long commander, requestedCommander;
            if (mission.Length == 0 || mission != bridge.Get("fleetAssignmentMission") || fields[0].Length == 0 || identity.Length == 0
                || !long.TryParse(fields[3], out requestedCommander)) return;
            int roster = -1;
            for (int i = 0; i < bridge.GetInt("fleetCount"); i++)
                if (bridge.Get("fleetId" + i) == identity) { roster = i; break; }
            if (roster < 0 || !long.TryParse(bridge.Get("fleetCommander" + roster), out commander) || commander != requestedCommander) return;
            VRage.ModAPI.IMyEntity entity;
            var pb = MyAPIGateway.Entities.TryGetEntityById(commander, out entity) ? entity as Sandbox.ModAPI.Ingame.IMyProgrammableBlock : null;
            var miner = IsAccessibleMinerPb(pb) ? pb as IMyTerminalBlock : null;
            string warning = ""; bool unknown = false, disconnected = false;
            CheckCraftFuelConnections(miner == null ? null : miner.CubeGrid as IMyCubeGrid,
                "Miner", blocks.Names.Mi(""), blocks.Names.Ca(""), ref warning, ref unknown, ref disconnected);
            bridge.Set("fResult", request + "|" + (disconnected ? "blocked" : unknown ? "waiting" : "ready"));
            bridge.Set("fWarning", warning);
        }

        private bool CheckFuelConnections(BridgeDocument bridge, bool includeMiner)
        {
            string warning = "";
            bool unknown = false;
            bool disconnected = false;
            CheckCraftFuelConnections(blocks.CarrierPb == null ? null : blocks.CarrierPb.CubeGrid as IMyCubeGrid,
                "Carrier", blocks.Names.Ca(""), blocks.Names.Mi(""), ref warning, ref unknown, ref disconnected);
            // A stored Miner has no live endpoints. Carrier-only transit is checked
            // now; the full Miner plan checks it after physical deployment.
            if (includeMiner)
                CheckCraftFuelConnections(blocks.MinerPb == null ? null : blocks.MinerPb.CubeGrid as IMyCubeGrid,
                    "Miner", blocks.Names.Mi(""), blocks.Names.Ca(""), ref warning, ref unknown, ref disconnected);
            bridge.Set("fuelConnectionStatus", disconnected ? "blocked" : unknown ? "unknown" : "ready");
            bridge.Set("fuelConnectionWarning", warning);
            return warning.Length == 0;
        }

        private void CheckCraftFuelConnections(IMyCubeGrid root, string craft, string ownTag, string otherTag,
            ref string warning, ref bool unknown, ref bool disconnected)
        {
            try
            {
                if (root == null || root.Closed) throw new InvalidOperationException("craft is not loaded");
                var grids = new List<IMyCubeGrid>();
                MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Mechanical, grids);
                var allowedGrids = new HashSet<long>();
                foreach (var grid in grids) allowedGrids.Add(grid.EntityId);
                if (allowedGrids.Add(root.EntityId)) grids.Add(root);
                var terminal = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(root);
                if (terminal == null) throw new InvalidOperationException("terminal system is initializing");
                var candidates = new List<IMyTerminalBlock>(); terminal.GetBlocks(candidates);
                var thrusters = new List<FuelConnectionPolicy.Thruster>();
                var sources = new List<FuelConnectionPolicy.Source>();
                var endpoints = new Dictionary<long, IMyConveyorEndpoint>();
                bool powered = true;
                foreach (var grid in grids)
                {
                    var concreteGrid = grid as MyCubeGrid;
                    if (concreteGrid == null || concreteGrid.GridSystems == null || concreteGrid.GridSystems.ResourceDistributor == null
                        || concreteGrid.GridSystems.ConveyorSystem == null || concreteGrid.GridSystems.ConveyorSystem.NeedsUpdateLines
                        || concreteGrid.GridSystems.ResourceDistributor.ResourceStateByType(
                            MyResourceDistributorComponent.ElectricityId, false, concreteGrid) == MyResourceStateEnum.NoPower)
                        powered = false;
                }
                foreach (var block in candidates)
                {
                    if (block.Closed || block.CubeGrid == null
                        || !FuelConnectionPolicy.IsCraftEndpoint(block.CubeGrid.EntityId, block.CustomName, allowedGrids, otherTag)) continue;
                    var endpointBlock = block as IMyConveyorEndpointBlock;
                    var endpoint = endpointBlock == null ? null : endpointBlock.ConveyorEndpoint;
                    if (endpoint != null) endpoints[block.EntityId] = endpoint;
                    var thrustBlock = block as MyThrust;
                    // Match the script's exact controlled set, including disabled engines.
                    if (thrustBlock != null && block.CubeGrid.EntityId == root.EntityId
                        && block.CustomName.IndexOf(ownTag, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool initialized = thrustBlock.FuelDefinition != null;
                        bool requiresConveyor = initialized && MyResourceDistributorComponent.IsConveyorConnectionRequiredTotal(thrustBlock.FuelDefinition.Id);
                        thrusters.Add(new FuelConnectionPolicy.Thruster {
                            Id = block.EntityId, Name = block.CustomName,
                            Fuel = initialized ? thrustBlock.FuelDefinition.Id.ToString() : "unknown fuel",
                            RequiresConveyor = requiresConveyor, Initialized = initialized && (!requiresConveyor || endpoint != null)
                        });
                    }
                    var source = block.Components.Get<MyResourceSourceComponent>();
                    if (source != null)
                    {
                        var fuels = new List<string>();
                        foreach (var fuel in source.ResourceTypes) fuels.Add(fuel.ToString());
                        if (endpoint != null)
                            sources.Add(new FuelConnectionPolicy.Source { Id = block.EntityId, Owned = true, Initialized = true, Fuels = fuels.ToArray() });
                        else if (block is MyGasTank || block is MyGasGenerator)
                            sources.Add(new FuelConnectionPolicy.Source { Id = block.EntityId, Owned = true });
                    }
                    else if (block is MyGasTank || block is MyGasGenerator)
                        sources.Add(new FuelConnectionPolicy.Source { Id = block.EntityId, Owned = true });
                }
                // Restrict every traversed endpoint, not just the destination source:
                // a docked other craft must not act as a temporary conveyor bridge.
                Predicate<IMyConveyorEndpoint> ownedEndpoint = endpoint => {
                    if (endpoint == null || endpoint.CubeBlock == null || endpoint.CubeBlock.CubeGrid == null) return false;
                    var terminalBlock = endpoint.CubeBlock as IMyTerminalBlock;
                    return FuelConnectionPolicy.IsCraftEndpoint(endpoint.CubeBlock.CubeGrid.EntityId,
                        terminalBlock == null ? null : terminalBlock.CustomName, allowedGrids, otherTag);
                };
                var reachableCache = new Dictionary<long, HashSet<long>>();
                Func<long, long, bool> reachable = (from, to) => {
                    HashSet<long> found;
                    if (!reachableCache.TryGetValue(from, out found)) {
                        var vertices = new List<IMyConveyorEndpoint>();
                        MyGridConveyorSystem.FindReachable(endpoints[from], vertices, null, ownedEndpoint);
                        found = new HashSet<long>();
                        foreach (var vertex in vertices) if (vertex.CubeBlock != null) found.Add(vertex.CubeBlock.EntityId);
                        reachableCache[from] = found;
                    }
                    return found.Contains(to);
                };
                var result = FuelConnectionPolicy.Check(thrusters, sources, powered, reachable);
                foreach (var thrust in result.Disconnected)
                    Emma3Log.Warn("Fuel connection: " + craft + " thruster '" + thrust.Name + "' (" + thrust.Id + ") has no craft-owned conveyor source for " + thrust.Fuel + ".");
                if (result.Disconnected.Count > 0)
                {
                    if (!disconnected) warning = "AM blocked: " + craft + " fuel conveyor (" + result.Disconnected.Count + "). See log.";
                    disconnected = true;
                }
                if (result.Unknown.Count > 0)
                {
                    unknown = true;
                    Emma3Log.Warn("Fuel connection: " + craft + " check unavailable for " + result.Unknown.Count + " thruster(s); grid power or resource endpoints are not ready.");
                    if (warning.Length == 0) warning = "AM blocked: " + craft + " fuel check not ready. Retry.";
                }
            }
            catch (Exception ex)
            {
                unknown = true;
                Emma3Log.Warn("Fuel connection: " + craft + " check unavailable: " + ex.GetBaseException().Message);
                if (warning.Length == 0) warning = "AM blocked: " + craft + " fuel check not ready. Retry.";
            }
        }
    }
}
