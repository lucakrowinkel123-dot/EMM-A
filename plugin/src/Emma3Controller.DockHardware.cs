using System;
using Sandbox.ModAPI;
using VRage.ModAPI;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;
using IMyShipMergeBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private const string DockHardwareMarker = "EMMA3_DOCK_HW|1|";
        private readonly MarkerFieldCache dockFieldsCache = new MarkerFieldCache(DockHardwareMarker);
        private const int DockHardwareTimeoutFrames = 300;
        private const int DockHardwareApproachTimeoutFrames = 1800;
        private const int DockHardwareMergeSettleFrames = 30;
        private const int DockHardwareDetachSettleFrames = 2;

        private enum DockHardwareStage
        {
            None,
            DisableMerger,
            WaitMergerReleased,
            DisconnectConnector,
            WaitDisconnected,
            DisableConnector,
            WaitDetachSettle,
            EnableMerger,
            WaitMergerConnected,
            WaitMergeSettle,
            EnableConnector,
            ConnectConnector,
            WaitConnected,
            Acknowledge
        }

        private sealed class DockHardwareTransaction
        {
            public long RequestId;
            public string PublishedMarker;
            public string Action;
            public IMyProgrammableBlock MinerPb;
            public IMyShipConnector Connector;
            public IMyShipMergeBlock Merger;
            public bool MergeAssisted;
            public DockHardwareStage Stage;
            public double ElapsedFrames;
            public bool ConnectStarted;
            public double ConnectFrames;
            public double StageFrames;
            public double StableFrames;
            public double RetryFrames;
            public bool Success;
            public string Message;
            public double MissingFrames;
        }

        private DockHardwareTransaction dockHardware;
        private long lastDockHardwareRequest;
        private double? lastDockHardwareTick;

        private void UpdateDockHardware()
        {
            double tick = EmmaTiming.GameSimulationTime.Ticks;
            double delta = lastDockHardwareTick.HasValue ? Math.Max(0, tick - lastDockHardwareTick.Value) : 0;
            lastDockHardwareTick = tick;
            if (delta <= 0) return;
            IMyProgrammableBlock miner = SelectServiceMiner(ref hardwareMinerCursor);
            if (dockHardware == null)
            {
                TryBeginDockHardware(miner);
                delta = 0; // A new request does not inherit time before it existed.
            }
            if (dockHardware == null)
                return;

            if (!IsDockHardwareRequestPublished(dockHardware))
            {
                if ((dockHardware.MissingFrames += delta) <= 10)
                    return;
                Emma3Log.Diagnostic("Dock hardware request " + dockHardware.RequestId
                    + " cancelled because the Miner no longer publishes it.");
                dockHardware = null;
                return;
            }
            dockHardware.MissingFrames = 0;

            try
            {
                StepDockHardware(delta);
            }
            catch (Exception exception)
            {
                FinishDockHardware(false, exception.GetBaseException().Message);
            }
        }

        private bool IsDockHardwareRequestPublished(DockHardwareTransaction job)
        {
            if (job == null || job.MinerPb == null)
                return false;
            string detail = GetMinerPluginInfo(job.MinerPb);
            return detail.IndexOf(job.PublishedMarker, StringComparison.Ordinal) >= 0;
        }

        private void TryBeginDockHardware(IMyProgrammableBlock miner)
        {
            if (miner == null)
                return;
            string[] fields;
            if (!dockFieldsCache.TryRead(miner.EntityId, GetMinerPluginInfo(miner), out fields))
                return;
            long requestId, connectorId, mergerId;
            long minerPbId;
            if (fields.Length != 8 || fields[0] != "EMMA3_DOCK_HW" || fields[1] != "1"
                || !long.TryParse(fields[2], out requestId) || requestId <= 0 || RequestSeen(dockRequestHistory, miner.EntityId, requestId)
                || !long.TryParse(fields[4], out connectorId) || !long.TryParse(fields[5], out mergerId)
                || !long.TryParse(fields[7], out minerPbId) || minerPbId != miner.EntityId)
                return;
            string action = fields[3];
            if (!action.Equals("connect", StringComparison.OrdinalIgnoreCase)
                && !action.Equals("disconnect", StringComparison.OrdinalIgnoreCase))
                return;

            IMyEntity connectorEntity;
            if (!MyAPIGateway.Entities.TryGetEntityById(connectorId, out connectorEntity))
            {
                SendDockHardwareImmediateFailure(miner, requestId, action, "connector entity not found");
                return;
            }
            var connector = connectorEntity as IMyShipConnector;
            if (connector == null)
            {
                SendDockHardwareImmediateFailure(miner, requestId, action, "connector entity has wrong type");
                return;
            }

            IMyShipMergeBlock merger = null;
            if (mergerId != 0)
            {
                IMyEntity mergerEntity;
                if (MyAPIGateway.Entities.TryGetEntityById(mergerId, out mergerEntity))
                    merger = mergerEntity as IMyShipMergeBlock;
            }
            bool mergeAssisted = fields[6] == "1";
            if (mergeAssisted && merger == null)
            {
                SendDockHardwareImmediateFailure(miner, requestId, action, "merge block entity not found");
                return;
            }

            lastDockHardwareRequest = requestId;
            dockRequestHistory[miner.EntityId] = requestId;
            dockHardware = new DockHardwareTransaction
            {
                RequestId = requestId,
                PublishedMarker = DockHardwareMarker + requestId + "|",
                Action = action.ToLowerInvariant(),
                MinerPb = miner,
                Connector = connector,
                Merger = merger,
                MergeAssisted = mergeAssisted,
                Stage = action.Equals("disconnect", StringComparison.OrdinalIgnoreCase)
                    ? DockHardwareStage.DisableMerger
                    : (mergeAssisted ? DockHardwareStage.EnableMerger : DockHardwareStage.DisableMerger)
            };
            Emma3Log.Diagnostic("Dock hardware request " + requestId + " started: " + dockHardware.Action
                + (mergeAssisted ? " with merge assistance." : " in connector-only mode."));
        }

        private void StepDockHardware(double delta = 1)
        {
            DockHardwareTransaction job = dockHardware;
            if (job.Stage == DockHardwareStage.Acknowledge)
            {
                TryAcknowledgeDockHardware();
                return;
            }
            job.ElapsedFrames += delta;
            job.StageFrames += delta;
            if (job.ConnectStarted)
                job.ConnectFrames += delta;
            if (job.Action == "connect"
                ? (job.ConnectStarted ? job.ConnectFrames > DockHardwareTimeoutFrames
                    : job.ElapsedFrames > DockHardwareApproachTimeoutFrames)
                : job.ElapsedFrames > DockHardwareTimeoutFrames)
            {
                FinishDockHardware(false, job.Action == "connect" && !job.ConnectStarted
                    ? "docking approach timed out before connector range"
                    : "hardware transaction timed out");
                return;
            }

            if (job.Stage == DockHardwareStage.DisableMerger)
            {
                if (job.Merger != null)
                    job.Merger.Enabled = false;
                AdvanceDockHardware(job.Action == "disconnect"
                    ? DockHardwareStage.WaitMergerReleased
                    : DockHardwareStage.EnableConnector);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitMergerReleased)
            {
                if (job.Merger != null && job.Merger.IsConnected)
                    return;
                AdvanceDockHardware(DockHardwareStage.DisconnectConnector);
                return;
            }
            if (job.Stage == DockHardwareStage.DisconnectConnector)
            {
                if (job.Connector.Status == MyShipConnectorStatus.Connected)
                    job.Connector.Disconnect();
                AdvanceDockHardware(DockHardwareStage.WaitDisconnected);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitDisconnected)
            {
                if (job.Connector.Status == MyShipConnectorStatus.Connected)
                    return;
                AdvanceDockHardware(DockHardwareStage.DisableConnector);
                return;
            }
            if (job.Stage == DockHardwareStage.DisableConnector)
            {
                job.Connector.Enabled = false;
                AdvanceDockHardware(DockHardwareStage.WaitDetachSettle);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitDetachSettle)
            {
                if (job.StageFrames < DockHardwareDetachSettleFrames)
                    return;
                FinishDockHardware(true, "connector detached and stable");
                return;
            }
            if (job.Stage == DockHardwareStage.EnableMerger)
            {
                job.Connector.Enabled = false;
                job.Merger.Enabled = true;
                AdvanceDockHardware(DockHardwareStage.WaitMergerConnected);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitMergerConnected)
            {
                if (!job.Merger.IsConnected)
                {
                    job.StableFrames = 0;
                    return;
                }
                if ((job.StableFrames += delta) < 2)
                    return;
                AdvanceDockHardware(DockHardwareStage.WaitMergeSettle);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitMergeSettle)
            {
                if (!job.Merger.IsConnected)
                {
                    job.StableFrames = 0;
                    AdvanceDockHardware(DockHardwareStage.WaitMergerConnected);
                    return;
                }
                if (job.StageFrames < DockHardwareMergeSettleFrames)
                    return;
                AdvanceDockHardware(DockHardwareStage.EnableConnector);
                return;
            }
            if (job.Stage == DockHardwareStage.EnableConnector)
            {
                job.Connector.Enabled = true;
                AdvanceDockHardware(DockHardwareStage.ConnectConnector);
                return;
            }
            if (job.Stage == DockHardwareStage.ConnectConnector)
            {
                MyShipConnectorStatus status = job.Connector.Status;
                if (status == MyShipConnectorStatus.Unconnected)
                    return;
                // Final approach can take longer than the hardware operation itself.
                // Arm once in range; retries and a later loss of range keep this deadline.
                job.ConnectStarted = true;
                if (status == MyShipConnectorStatus.Connectable)
                    job.Connector.Connect();
                job.RetryFrames = 0;
                AdvanceDockHardware(DockHardwareStage.WaitConnected);
                return;
            }
            if (job.Stage == DockHardwareStage.WaitConnected)
            {
                if (job.Connector.Status == MyShipConnectorStatus.Connected)
                {
                    if ((job.StableFrames += delta) >= 2)
                        FinishDockHardware(true, "connector attached and stable");
                    return;
                }
                job.StableFrames = 0;
                if ((job.RetryFrames += delta) >= 10)
                    AdvanceDockHardware(DockHardwareStage.ConnectConnector);
                return;
            }
        }

        private void AdvanceDockHardware(DockHardwareStage next)
        {
            dockHardware.Stage = next;
            dockHardware.StageFrames = 0;
            dockHardware.StableFrames = 0;
        }

        private void FinishDockHardware(bool success, string message)
        {
            if (dockHardware == null)
                return;
            dockHardware.Success = success;
            dockHardware.Message = SanitizeDockHardwareMessage(message);
            dockHardware.Stage = DockHardwareStage.Acknowledge;
            dockHardware.StageFrames = 0;
            Emma3Log.Diagnostic("Dock hardware request " + dockHardware.RequestId + " "
                + (success ? "completed" : "failed") + ": " + dockHardware.Message + ".");
        }

        private void TryAcknowledgeDockHardware()
        {
            DockHardwareTransaction job = dockHardware;
            string command = "PluginDockHardware|" + job.RequestId + "|" + (job.Success ? "ok" : "error")
                + "|" + job.Action + "|" + job.Message;
            if (job.MinerPb != null && job.MinerPb.TryRun(command))
                dockHardware = null;
        }

        private void SendDockHardwareImmediateFailure(IMyProgrammableBlock miner, long requestId, string action, string message)
        {
            lastDockHardwareRequest = requestId;
            string clean = SanitizeDockHardwareMessage(message);
            miner.TryRun("PluginDockHardware|" + requestId + "|error|" + action + "|" + clean);
            Emma3Log.Warn("Dock hardware request " + requestId + " rejected: " + clean + ".");
        }

        private static string SanitizeDockHardwareMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "unspecified result";
            return message.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
