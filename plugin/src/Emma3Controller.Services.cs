using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMyThrust = Sandbox.ModAPI.IMyThrust;
using IMyGyro = Sandbox.ModAPI.IMyGyro;
using IMyBatteryBlock = Sandbox.ModAPI.IMyBatteryBlock;
using IMyGasTank = Sandbox.ModAPI.IMyGasTank;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyConveyorSorter = Sandbox.ModAPI.Ingame.IMyConveyorSorter;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using ChargeMode = Sandbox.ModAPI.Ingame.ChargeMode;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private const string PowerMarker = "EMMA3_POWER|1|";
        private const string UnloadMarker = "EMMA3_UNLOAD|1|";
        private const string RemoteMarker = "EMMA3_REMOTE|1|";
        private readonly MarkerFieldCache readyFieldsCache = new MarkerFieldCache("EMMA3_FEATURES_READY|1|");
        private readonly MarkerFieldCache powerFieldsCache = new MarkerFieldCache(PowerMarker);
        private readonly MarkerFieldCache unloadFieldsCache = new MarkerFieldCache(UnloadMarker);
        private readonly MarkerFieldCache remoteFieldsCache = new MarkerFieldCache(RemoteMarker);
        private string featureConfiguration = "PluginFeatures|1|1|1";
        private long remoteIdsOwner;
        private string remoteTankIdText, remoteCargoIdText;
        private bool remoteTankIdsValid, remoteCargoIdsValid;
        private readonly List<long> remoteTankIds = new List<long>();
        private readonly List<long> remoteCargoIds = new List<long>();
        private readonly List<MyInventoryItem> remoteItems = new List<MyInventoryItem>();
        private bool pluginOreTransfer = true;
        private bool pluginPowerTransitions = true;
        private bool pluginRemoteLink = true;
        private string sentFeatureConfiguration = "";
        private long sentFeatureMiner;
        private long sentFeatureMinerSession;
        private int nextFeatureConfigurationFrame;
        private PowerServiceJob powerService;
        private UnloadServiceJob unloadService;
        private long lastPowerServiceRequest;
        private long lastUnloadServiceRequest;
        private int nextRemoteServiceFrame;
        private int nextRemoteServiceErrorFrame;
        private long remotePoseSession;
        private int nextRemotePoseFrame;
        private double? lastPluginServiceTick;

        private sealed class PowerServiceJob
        {
            public long RequestId;
            public string PublishedMarker;
            public int Goal;
            public IMyProgrammableBlock MinerPb;
            public readonly List<long> Blocks = new List<long>();
            public int Index;
            public bool Success;
            public string Message = "";
            public bool Acknowledging;
            public double MissingFrames;
        }

        private enum UnloadStage { Delay, Resolve, Read, Transfer, Verify, Report, Acknowledge }

        private sealed class UnloadServiceJob
        {
            public long RequestId;
            public string PublishedMarker;
            public long Session;
            public long CarrierConnector;
            public long MinerConnector;
            public string Mission = "";
            public IMyProgrammableBlock MinerPb;
            public readonly List<long> SourceBlockIds = new List<long>();
            public readonly List<IMyInventory> Sources = new List<IMyInventory>();
            public readonly List<IMyTerminalBlock> Targets = new List<IMyTerminalBlock>();
            public readonly Dictionary<string, double> Delivered = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public UnloadStage Stage;
            public double Frames;
            public int ResolveIndex;
            public int SourceIndex;
            public int TargetIndex;
            public int TargetAttempts;
            public bool MovedThisPass;
            public MyInventoryItem? Item;
            public MyInventoryItem? Before;
            public bool Success;
            public string Message = "";
            public double MissingFrames;
        }

        private void ApplyPluginFeatureSettings(BridgeDocument bridge)
        {
            bool unload = bridge.GetBool("pluginOreTransfer", true);
            bool power = bridge.GetBool("pluginPowerTransitions", true);
            bool remote = bridge.GetBool("pluginRemoteLink", true);
            if (unload == pluginOreTransfer && power == pluginPowerTransitions && remote == pluginRemoteLink)
                return;
            pluginOreTransfer = unload;
            pluginPowerTransitions = power;
            pluginRemoteLink = remote;
            featureConfiguration = "PluginFeatures|" + (unload ? "1" : "0") + "|"
                + (power ? "1" : "0") + "|" + (remote ? "1" : "0");
            sentFeatureConfiguration = "";
            if (!power)
                powerService = null;
            if (!unload)
                unloadService = null;
            if (!remote)
                remotePoseSession = 0;
        }

        private void UpdatePluginServices()
        {
            double tick = EmmaTiming.GameSimulationTime.Ticks;
            double delta = lastPluginServiceTick.HasValue ? Math.Max(0, tick - lastPluginServiceTick.Value) : 0;
            lastPluginServiceTick = tick;
            IMyProgrammableBlock miner = SelectServiceMiner(ref serviceMinerCursor);
            try { PublishFeatureConfiguration(miner); }
            catch (Exception exception)
            {
                Emma3Log.Diagnostic("Plugin feature handshake failed: " + exception.GetBaseException().Message + ".");
                nextFeatureConfigurationFrame = frame + 300;
            }
            try { UpdatePowerService(miner, delta); }
            catch (Exception exception) { FinishPowerService(false, exception.GetBaseException().Message); }
            try { UpdateUnloadService(miner, delta); }
            catch (Exception exception) { FinishUnloadService(false, exception.GetBaseException().Message); }
            if (pluginRemoteLink && frame >= nextRemoteServiceFrame)
            {
                nextRemoteServiceFrame = frame + 10;
                try { UpdateRemoteService(SelectServiceMiner(ref remoteMinerCursor)); }
                catch (Exception exception)
                {
                    if (frame >= nextRemoteServiceErrorFrame)
                    {
                        Emma3Log.Diagnostic("Plugin remote link failed: " + exception.GetBaseException().Message + ".");
                        nextRemoteServiceErrorFrame = frame + 600;
                    }
                }
            }
        }

        private void PublishFeatureConfiguration(IMyProgrammableBlock miner)
        {
            if (miner == null)
                return;
            string[] readyFields;
            if (!readyFieldsCache.TryRead(miner.EntityId, GetMinerPluginInfo(miner), out readyFields))
                return;
            long minerSession;
            if (readyFields.Length != 3 || !long.TryParse(readyFields[2], out minerSession) || minerSession <= 0)
                return;
            string value = featureConfiguration;
            FeatureReceipt receipt;
            if (featureReceipts.TryGetValue(miner.EntityId, out receipt) && minerSession == receipt.Session
                && value == receipt.Value && frame < receipt.NextFrame)
                return;
            if (miner.TryRun(value))
            {
                sentFeatureMiner = miner.EntityId;
                sentFeatureMinerSession = minerSession;
                sentFeatureConfiguration = value;
                nextFeatureConfigurationFrame = frame + 300;
                featureReceipts[miner.EntityId] = new FeatureReceipt { Session = minerSession, Value = value, NextFrame = nextFeatureConfigurationFrame };
            }
        }

        private void UpdatePowerService(IMyProgrammableBlock miner, double delta)
        {
            if (!pluginPowerTransitions)
            {
                powerService = null;
                return;
            }
            if (powerService == null)
            {
                TryBeginPowerService(miner);
                delta = 0;
            }
            PowerServiceJob job = powerService;
            if (job == null)
                return;
            if (!Published(job.MinerPb, job.PublishedMarker))
            {
                if ((job.MissingFrames += delta) <= 10)
                    return;
                powerService = null;
                return;
            }
            job.MissingFrames = 0;
            if (job.Acknowledging)
            {
                string reply = "PluginPower|" + job.RequestId + "|" + (job.Success ? "ok" : "error")
                    + "|" + job.Goal + "|" + CleanMessage(job.Message);
                if (job.MinerPb.TryRun(reply))
                    powerService = null;
                return;
            }
            if (job.Index >= job.Blocks.Count)
            {
                FinishPowerService(true, "all power blocks applied");
                return;
            }
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(job.Blocks[job.Index++], out entity))
            {
                FinishPowerService(false, "power block is not available through the current antenna/replication link");
                return;
            }
            IMyFunctionalBlock block = entity as IMyFunctionalBlock;
            if (block == null || block.Closed || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))
            {
                FinishPowerService(false, "power block is unavailable or access was denied");
                return;
            }
            if (job.MinerPb.CubeGrid == null || block.CubeGrid == null
                || block.CubeGrid.EntityId != job.MinerPb.CubeGrid.EntityId)
            {
                FinishPowerService(false, "power block does not belong to the Miner grid");
                return;
            }
            bool flight = job.Goal == 2;
            IMyThrust thruster = block as IMyThrust;
            IMyGyro gyro = block as IMyGyro;
            IMyBatteryBlock battery = block as IMyBatteryBlock;
            IMyGasTank tank = block as IMyGasTank;
            if (thruster != null)
            {
                if (thruster.ThrustOverride != 0) thruster.ThrustOverride = 0;
                if (thruster.Enabled != flight) thruster.Enabled = flight;
            }
            else if (gyro != null)
            {
                if (gyro.GyroOverride)
                {
                    if (gyro.Pitch != 0) gyro.Pitch = 0;
                    if (gyro.Yaw != 0) gyro.Yaw = 0;
                    if (gyro.Roll != 0) gyro.Roll = 0;
                    gyro.GyroOverride = false;
                }
                if (gyro.Enabled != flight) gyro.Enabled = flight;
            }
            else if (battery != null)
            {
                ChargeMode mode = flight ? ChargeMode.Auto : ChargeMode.Recharge;
                if (battery.ChargeMode != mode) battery.ChargeMode = mode;
            }
            else if (tank != null)
            {
                if (tank.Stockpile == flight) tank.Stockpile = !flight;
            }
            else if (block.Enabled != flight)
                block.Enabled = flight;
        }

        private void TryBeginPowerService(IMyProgrammableBlock miner)
        {
            string[] fields;
            if (miner == null || !powerFieldsCache.TryRead(miner.EntityId, GetMinerPluginInfo(miner), out fields))
                return;
            long requestId, minerId;
            int goal;
            if (fields.Length != 6 || !long.TryParse(fields[2], out requestId) || requestId <= 0
                || RequestSeen(powerRequestHistory, miner.EntityId, requestId) || !int.TryParse(fields[3], out goal) || goal < 1 || goal > 2
                || !long.TryParse(fields[4], out minerId) || minerId != miner.EntityId)
                return;
            var job = new PowerServiceJob { RequestId = requestId, Goal = goal, MinerPb = miner,
                PublishedMarker = PowerMarker + requestId + "|" };
            if (!ParseIds(fields[5], job.Blocks, 256))
            {
                lastPowerServiceRequest = requestId; powerRequestHistory[miner.EntityId] = requestId;
                powerService = job;
                FinishPowerService(false, "invalid or empty power block ID list");
                return;
            }
            lastPowerServiceRequest = requestId; powerRequestHistory[miner.EntityId] = requestId;
            powerService = job;
            Emma3Log.Diagnostic("Plugin power transition " + requestId + " started for " + job.Blocks.Count + " blocks.");
        }

        private void FinishPowerService(bool success, string message)
        {
            if (powerService == null) return;
            powerService.Success = success;
            powerService.Message = message;
            powerService.Acknowledging = true;
            Emma3Log.Diagnostic("Plugin power transition " + powerService.RequestId + " " + (success ? "completed" : "failed") + ": " + message + ".");
        }

        private void UpdateUnloadService(IMyProgrammableBlock miner, double delta)
        {
            if (!pluginOreTransfer)
            {
                unloadService = null;
                return;
            }
            if (unloadService == null)
            {
                TryBeginUnloadService(miner);
                delta = 0;
            }
            UnloadServiceJob job = unloadService;
            if (job == null)
                return;
            if (!Published(job.MinerPb, job.PublishedMarker))
            {
                if ((job.MissingFrames += delta) <= 10)
                    return;
                unloadService = null;
                return;
            }
            job.MissingFrames = 0;
            job.Frames += delta;
            if (job.Frames > 6900 && job.Stage != UnloadStage.Acknowledge)
            {
                FinishUnloadService(false, "plugin transfer timed out");
                return;
            }
            if (job.Stage == UnloadStage.Delay)
            {
                if (job.Frames < 120) return;
                if (!ValidateUnloadDock(job)) { FinishUnloadService(false, "dock connection is no longer valid"); return; }
                BuildUnloadTargets(job);
                if (job.Targets.Count == 0) { FinishUnloadService(false, "no accessible Carrier cargo targets"); return; }
                job.Stage = UnloadStage.Resolve;
                return;
            }
            if (job.Stage == UnloadStage.Resolve)
            {
                if (job.ResolveIndex >= job.SourceBlockIds.Count)
                {
                    if (job.Sources.Count == 0) { FinishUnloadService(false, "no accessible Miner source inventories"); return; }
                    job.Stage = UnloadStage.Read;
                    return;
                }
                IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(job.SourceBlockIds[job.ResolveIndex++], out entity))
                { FinishUnloadService(false, "a Miner cargo source is not available"); return; }
                IMyTerminalBlock block = entity as IMyTerminalBlock;
                if (block == null || block.Closed || !block.HasInventory || block.CubeGrid.EntityId != GetConnectorGrid(job.MinerConnector)
                    || block.CustomName.IndexOf(blocks.Names.Mi(""), StringComparison.OrdinalIgnoreCase) < 0
                    || block is IMyConveyorSorter
                    || block is IMyShipConnector && block.CustomName.IndexOf(blocks.Names.Mi("Ejector"), StringComparison.OrdinalIgnoreCase) >= 0
                    || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))
                { FinishUnloadService(false, "a Miner cargo source failed validation"); return; }
                for (int i = 0; i < block.InventoryCount; i++)
                {
                    IMyInventory inventory = block.GetInventory(i);
                    if (inventory != null) job.Sources.Add(inventory);
                }
                return;
            }
            if (job.Stage == UnloadStage.Read)
            {
                if (job.SourceIndex >= job.Sources.Count)
                {
                    if (job.MovedThisPass)
                    {
                        job.MovedThisPass = false;
                        job.SourceIndex = 0;
                        return;
                    }
                    job.Stage = UnloadStage.Report;
                    return;
                }
                job.Item = job.Sources[job.SourceIndex].GetItemAt(0);
                if (!job.Item.HasValue) { job.SourceIndex++; return; }
                job.TargetAttempts = 0;
                job.Stage = UnloadStage.Transfer;
                return;
            }
            if (job.Stage == UnloadStage.Transfer)
            {
                IMyInventory source = job.Sources[job.SourceIndex];
                MyInventoryItem? before = source.GetItemByID(job.Item.Value.ItemId);
                if (!before.HasValue) { job.Stage = UnloadStage.Read; return; }
                IMyTerminalBlock target = job.Targets[job.TargetIndex];
                IMyInventory destination = target.GetInventory(0);
                if (destination != null && destination != source)
                {
                    job.Before = before;
                    destination.TransferItemFrom(source, before.Value);
                    job.Stage = UnloadStage.Verify;
                    return;
                }
                NextUnloadTarget(job);
                return;
            }
            if (job.Stage == UnloadStage.Verify)
            {
                IMyInventory source = job.Sources[job.SourceIndex];
                MyInventoryItem before = job.Before.Value;
                MyInventoryItem? after = source.GetItemByID(before.ItemId);
                double moved = (double)before.Amount - (after.HasValue ? (double)after.Value.Amount : 0);
                job.Before = null;
                if (moved > 0)
                {
                    if (before.Type.TypeId.EndsWith("_Ore", StringComparison.Ordinal))
                    {
                        double total;
                        job.Delivered.TryGetValue(before.Type.SubtypeId, out total);
                        job.Delivered[before.Type.SubtypeId] = total + moved;
                    }
                    job.MovedThisPass = true;
                    job.Stage = UnloadStage.Read;
                }
                else
                    NextUnloadTarget(job);
                return;
            }
            if (job.Stage == UnloadStage.Report)
            {
                IMyProgrammableBlock carrier = blocks.CarrierPb ?? lastCarrierPb;
                if (carrier == null || !carrier.TryRun(BuildUnloadReport(job)))
                { FinishUnloadService(false, "Carrier report acknowledgement is unavailable"); return; }
                FinishUnloadService(true, "all Miner inventories unloaded");
                return;
            }
            if (job.Stage == UnloadStage.Acknowledge)
            {
                string reply = "PluginUnload|" + job.RequestId + "|" + (job.Success ? "ok" : "error") + "|" + CleanMessage(job.Message);
                if (job.MinerPb.TryRun(reply))
                    unloadService = null;
            }
        }

        private void TryBeginUnloadService(IMyProgrammableBlock miner)
        {
            string[] fields;
            if (miner == null || !unloadFieldsCache.TryRead(miner.EntityId, GetMinerPluginInfo(miner), out fields)) return;
            long requestId, session, carrierConnector, minerConnector;
            if (fields.Length != 9 || !long.TryParse(fields[2], out requestId) || requestId <= 0 || RequestSeen(unloadRequestHistory, miner.EntityId, requestId)
                || fields[3] != "Unload" || !long.TryParse(fields[4], out session) || session <= 0
                || !long.TryParse(fields[5], out carrierConnector) || !long.TryParse(fields[6], out minerConnector)) return;
            var job = new UnloadServiceJob
            {
                RequestId = requestId, Session = session, CarrierConnector = carrierConnector, MinerConnector = minerConnector,
                Mission = fields[7], MinerPb = miner, Stage = UnloadStage.Delay,
                PublishedMarker = UnloadMarker + requestId + "|"
            };
            if (!ParseIds(fields[8], job.SourceBlockIds, 128))
            {
                lastUnloadServiceRequest = requestId; unloadRequestHistory[miner.EntityId] = requestId;
                unloadService = job;
                FinishUnloadService(false, "invalid or empty Miner cargo ID list");
                return;
            }
            lastUnloadServiceRequest = requestId; unloadRequestHistory[miner.EntityId] = requestId;
            unloadService = job;
            Emma3Log.Diagnostic("Plugin unload " + requestId + " started with " + job.SourceBlockIds.Count + " source blocks.");
        }

        private bool ValidateUnloadDock(UnloadServiceJob job)
        {
            IMyEntity carrierEntity, minerEntity;
            if (!MyAPIGateway.Entities.TryGetEntityById(job.CarrierConnector, out carrierEntity)
                || !MyAPIGateway.Entities.TryGetEntityById(job.MinerConnector, out minerEntity)) return false;
            IMyShipConnector carrier = carrierEntity as IMyShipConnector;
            IMyShipConnector miner = minerEntity as IMyShipConnector;
            return carrier != null && miner != null && carrier.Status == MyShipConnectorStatus.Connected
                && miner.Status == MyShipConnectorStatus.Connected && carrier.OtherConnector != null
                && carrier.OtherConnector.EntityId == miner.EntityId && miner.OtherConnector != null
                && miner.OtherConnector.EntityId == carrier.EntityId;
        }

        private long GetConnectorGrid(long connectorId)
        {
            IMyEntity entity;
            IMyTerminalBlock block = MyAPIGateway.Entities.TryGetEntityById(connectorId, out entity) ? entity as IMyTerminalBlock : null;
            return block?.CubeGrid?.EntityId ?? 0;
        }

        private void BuildUnloadTargets(UnloadServiceJob job)
        {
            var carrierBlocks = new List<IMyTerminalBlock>();
            blocks.GetCarrierBlocks(carrierBlocks);
            for (int i = 0; i < carrierBlocks.Count; i++)
            {
                IMyTerminalBlock block = carrierBlocks[i];
                if (block != null && !block.Closed && block.IsFunctional && block.HasInventory
                    && block.CustomName.IndexOf(blocks.Names.Ca("Cargo Container"), StringComparison.OrdinalIgnoreCase) >= 0
                    && block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))
                    job.Targets.Add(block);
            }
        }

        private void NextUnloadTarget(UnloadServiceJob job)
        {
            job.TargetIndex = (job.TargetIndex + 1) % job.Targets.Count;
            if (++job.TargetAttempts >= job.Targets.Count)
                FinishUnloadService(false, "Carrier cargo is full or the conveyor route is blocked");
            else
                job.Stage = UnloadStage.Transfer;
        }

        private string BuildUnloadReport(UnloadServiceJob job)
        {
            var sb = new StringBuilder("PluginUnloadReport|").Append(job.Session).Append('|').Append(job.Mission).Append('|');
            bool first = true;
            foreach (KeyValuePair<string, double> pair in job.Delivered)
            {
                if (!first) sb.Append(';');
                first = false;
                sb.Append(pair.Key).Append('=').Append(pair.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private void FinishUnloadService(bool success, string message)
        {
            if (unloadService == null) return;
            unloadService.Success = success;
            unloadService.Message = message;
            unloadService.Stage = UnloadStage.Acknowledge;
            Emma3Log.Diagnostic("Plugin unload " + unloadService.RequestId + " " + (success ? "completed" : "failed") + ": " + message + ".");
        }

        private void UpdateRemoteService(IMyProgrammableBlock miner)
        {
            string[] fields;
            if (miner == null || !remoteFieldsCache.TryRead(miner.EntityId, GetMinerPluginInfo(miner), out fields)) return;
            long minerId, minerConnector, session, carrierConnector;
            if (fields.Length != 9 || !long.TryParse(fields[2], out minerId) || minerId != miner.EntityId
                || !long.TryParse(fields[3], out minerConnector) || !long.TryParse(fields[5], out session)
                || !long.TryParse(fields[6], out carrierConnector)) return;
            int nextTelemetry;
            if (!remoteTelemetryFrames.TryGetValue(miner.EntityId, out nextTelemetry) || frame >= nextTelemetry)
            {
                remoteTelemetryFrames[miner.EntityId] = frame + 300;
                SendRemoteTelemetry(miner, fields[7], fields[8]);
            }
            if (fields[4] == "1" && session > 0 && carrierConnector > 0
                && (miner.EntityId != remotePoseMiner || session != remotePoseSession || frame >= nextRemotePoseFrame))
            {
                IMyShipConnector connector = blocks.CarrierConnector;
                if (connector == null || connector.EntityId != carrierConnector || connector.Closed) return;
                MatrixD matrix = connector.WorldMatrix;
                Vector3D position = connector.GetPosition() + matrix.Forward;
                string command = "PluginDockPose|" + session + "|" + carrierConnector + "|" + VectorFields(position)
                    + "|" + VectorFields(matrix.Forward) + "|" + VectorFields(matrix.Up);
                if (miner.TryRun(command))
                {
                    remotePoseSession = session;
                    remotePoseMiner = miner.EntityId;
                    nextRemotePoseFrame = frame + 120;
                }
            }
        }

        private void SendRemoteTelemetry(IMyProgrammableBlock miner, string tankIds, string cargoIds)
        {
            if (remoteIdsOwner != miner.EntityId)
            {
                remoteIdsOwner = miner.EntityId;
                remoteTankIdText = remoteCargoIdText = null;
            }
            if (remoteTankIdText != tankIds)
            {
                remoteTankIdsValid = ParseIds(tankIds, remoteTankIds, 64);
                remoteTankIdText = tankIds;
            }
            if (remoteCargoIdText != cargoIds)
            {
                remoteCargoIdsValid = ParseIds(cargoIds, remoteCargoIds, 128);
                remoteCargoIdText = cargoIds;
            }
            List<long> ids = remoteTankIds;
            double water = 0, tankCapacity = 0;
            int tankCount = 0;
            if (remoteTankIdsValid)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    IMyEntity entity;
                    IMyGasTank tank = MyAPIGateway.Entities.TryGetEntityById(ids[i], out entity) ? entity as IMyGasTank : null;
                    if (tank == null || tank.Closed || !tank.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) continue;
                    double capacity = Math.Max(0, tank.Capacity);
                    water += tank.FilledRatio * capacity;
                    tankCapacity += capacity;
                    tankCount++;
                }
            }
            double current = 0, maximum = 0, stoneVolume = 0;
            bool ore = false;
            ids = remoteCargoIds;
            var items = remoteItems;
            if (remoteCargoIdsValid)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    IMyEntity entity;
                    IMyTerminalBlock block = MyAPIGateway.Entities.TryGetEntityById(ids[i], out entity) ? entity as IMyTerminalBlock : null;
                    if (block == null || block.Closed || !block.HasInventory || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) continue;
                    for (int invIndex = 0; invIndex < block.InventoryCount; invIndex++)
                    {
                        IMyInventory inventory = block.GetInventory(invIndex);
                        if (inventory == null) continue;
                        current += (double)inventory.CurrentVolume;
                        maximum += (double)inventory.MaxVolume;
                        items.Clear();
                        inventory.GetItems(items);
                        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                        {
                            MyInventoryItem item = items[itemIndex];
                            if (item.Type.TypeId.EndsWith("_Ore", StringComparison.Ordinal) && item.Amount > 0) ore = true;
                            if (!item.Type.SubtypeId.Equals("Stone", StringComparison.OrdinalIgnoreCase)) continue;
                            var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(MyDefinitionId.Parse(item.Type.ToString()));
                            if (definition != null) stoneVolume += (double)item.Amount * definition.Volume;
                        }
                    }
                }
            }
            double cargoPercent = maximum <= 0 ? 0 : MathHelper.Clamp(current / maximum * 100, 0, 100);
            double stonePercent = maximum <= 0 ? 0 : MathHelper.Clamp(stoneVolume / maximum * 100, 0, 100);
            double waterPercent = tankCapacity <= 0 ? 0 : water / tankCapacity * 100;
            miner.TryRun("PluginRemoteStatus|" + Number(cargoPercent) + "|" + Number(stonePercent) + "|"
                + (ore ? "1" : "0") + "|" + Number(waterPercent) + "|" + tankCount + "|1");
        }

        private bool Published(IMyProgrammableBlock pb, string marker)
        {
            return pb != null && GetMinerPluginInfo(pb).IndexOf(marker, StringComparison.Ordinal) >= 0;
        }

        private static bool ParseIds(string value, List<long> result, int limit)
        {
            result.Clear();
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] tokens = value.Split(',');
            if (tokens.Length == 0 || tokens.Length > limit) return false;
            var seen = new HashSet<long>();
            for (int i = 0; i < tokens.Length; i++)
            {
                long id;
                if (!long.TryParse(tokens[i], out id) || id == 0 || !seen.Add(id)) return false;
                result.Add(id);
            }
            return true;
        }

        private static string VectorFields(Vector3D value)
        {
            return Number(value.X) + "|" + Number(value.Y) + "|" + Number(value.Z);
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string CleanMessage(string message)
        {
            return (message ?? "unspecified result").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
