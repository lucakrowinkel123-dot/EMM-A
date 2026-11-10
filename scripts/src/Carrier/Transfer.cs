string carrierUnloadCargoTag { get { return Ca("Cargo Container"); } }
readonly List<IMyTerminalBlock> carrierUnloadTargets = new List<IMyTerminalBlock>();
readonly List<IMyInventory> carrierUnloadSources = new List<IMyInventory>();
IMyBroadcastListener carrierUnloadListener;
DockUnloadRequest carrierUnloadRequest;
long carrierUnloadPeer;
bool carrierUnloadPeerVerified;
enum CarrierUnloadPhase { None, Delay, Peer, Resolve, Read, Transfer, Verify, Blocked, Complete, Cancelled, Saving }
CarrierUnloadPhase carrierUnloadPhase;
int carrierUnloadDelay, carrierUnloadLease, carrierUnloadPollTicks = 4, carrierUnloadWorkTicks;
int carrierUnloadResolveIndex, carrierUnloadResolveAttempts, carrierUnloadSourceIndex, carrierUnloadTargetIndex, carrierUnloadTargetAttempts;
bool carrierUnloadMovedThisPass;
long carrierUnloadSaveRevision;
MyInventoryItem? carrierUnloadItem;
MyInventoryItem? carrierUnloadBeforeItem;

void InitializeCarrierUnloading()
{
    GridTerminalSystem.GetBlocksOfType(carrierUnloadTargets, b => b.HasInventory && b.IsSameConstructAs(Me)
        && b.CustomName.IndexOf(carrierUnloadCargoTag, StringComparison.OrdinalIgnoreCase) >= 0);
    carrierUnloadListener = IGC.RegisterBroadcastListener(unloadRequestTag);
}
bool CarrierUnloadDockMatches(DockUnloadRequest request)
{
    return request != null && carrierConnector != null
        && carrierConnector.EntityId == request.CarrierConnector
        && GetCarrierConnectorStatus() == MyShipConnectorStatus.Connected
        && carrierConnector.OtherConnector != null
        && carrierConnector.OtherConnector.EntityId == request.MinerConnector
        && carrierConnector.OtherConnector.OtherConnector != null
        && carrierConnector.OtherConnector.OtherConnector.EntityId == carrierConnector.EntityId;
}
void ReplyCarrierUnload(string state)
{
    if (carrierUnloadRequest == null) return;
    IGC.SendUnicastMessage(carrierUnloadPeer, unloadReplyTag, "UnloadState|" + carrierUnloadRequest.Session
        + "|" + carrierUnloadRequest.CarrierConnector + "|" + carrierUnloadRequest.MinerConnector + "|" + state);
}
void CancelCarrierUnloading()
{
    if (carrierUnloadRequest == null || carrierUnloadPhase == CarrierUnloadPhase.Cancelled) return;
    carrierUnloadPhase = CarrierUnloadPhase.Cancelled;
    carrierUnloadSources.Clear();
    carrierUnloadItem = null;
    carrierUnloadBeforeItem = null;
    ReplyCarrierUnload("cancelled");
}
void AcceptCarrierUnload(long sender, string text)
{
    if (string.IsNullOrEmpty(text) || text.Length > 8192) return;
    if (text.StartsWith("Cancel|", StringComparison.Ordinal))
    {
        if (carrierUnloadRequest != null && sender == carrierUnloadPeer
            && text == "Cancel|" + carrierUnloadRequest.Session + "|" + carrierUnloadRequest.CarrierConnector
                + "|" + carrierUnloadRequest.MinerConnector)
            CancelCarrierUnloading();
        return;
    }
    DockUnloadRequest request;
    if (!DockUnloadRequest.TryParse(text, out request) || !CarrierUnloadDockMatches(request)) return;
    if (carrierUnloadPeerVerified && carrierUnloadPhase != CarrierUnloadPhase.Complete
        && carrierUnloadPhase != CarrierUnloadPhase.Cancelled && sender != carrierUnloadPeer
        && CarrierUnloadDockMatches(carrierUnloadRequest)) return;
    if (carrierUnloadRequest != null && sender == carrierUnloadPeer)
    {
        if (request.Session < carrierUnloadRequest.Session) return;
        if (request.Session == carrierUnloadRequest.Session)
        {
            if (request.CarrierConnector != carrierUnloadRequest.CarrierConnector
                || request.MinerConnector != carrierUnloadRequest.MinerConnector
                || request.Mission != carrierUnloadRequest.Mission) return;
            carrierUnloadLease = 900;
            if (carrierUnloadPhase == CarrierUnloadPhase.Blocked)
            {
                if (carrierUnloadTargets.Count == 0)
                {
                    ReplyCarrierUnload("blocked");
                    return;
                }
                if (carrierUnloadResolveIndex >= carrierUnloadRequest.Sources.Count && carrierUnloadSources.Count > 0)
                {
                    carrierUnloadSourceIndex = carrierUnloadTargetAttempts = 0;
                    carrierUnloadMovedThisPass = false;
                    carrierUnloadPhase = CarrierUnloadPhase.Read;
                }
                else
                {
                    // Retry only advertised IDs; never enumerate the connected grids.
                    carrierUnloadResolveIndex = carrierUnloadResolveAttempts = 0;
                    carrierUnloadSources.Clear();
                    carrierUnloadDelay = 120;
                    carrierUnloadPhase = CarrierUnloadPhase.Delay;
                }
            }
            ReplyCarrierUnload(carrierUnloadPhase == CarrierUnloadPhase.Complete ? "complete"
                : carrierUnloadPhase == CarrierUnloadPhase.Cancelled ? "cancelled" : "busy");
            return;
        }
    }
    carrierUnloadRequest = request;
    carrierUnloadPeer = sender;
    carrierUnloadPeerVerified = false;
    carrierUnloadPhase = CarrierUnloadPhase.Delay;
    carrierUnloadDelay = 120;
    carrierUnloadLease = 900;
    carrierUnloadResolveIndex = carrierUnloadResolveAttempts = 0;
    carrierUnloadWorkTicks = 0;
    carrierUnloadSources.Clear();
    carrierUnloadItem = null;
    carrierUnloadBeforeItem = null;
    ReplyCarrierUnload("busy");
    ScriptLog("Carrier unloading: waiting 2 seconds, then resolving " + request.Sources.Count + " Miner cargo IDs.");
}
void BlockCarrierUnload(string reason)
{
    carrierUnloadPhase = CarrierUnloadPhase.Blocked;
    carrierUnloadItem = null;
    ReplyCarrierUnload("blocked");
    ScriptLog("Carrier unloading blocked: " + reason);
}
void UpdateCarrierUnloading()
{
    bool acceptedNew = false;
    carrierUnloadPollTicks -= UpdateTickDelta();
    if (carrierUnloadPollTicks <= 0)
    {
        carrierUnloadPollTicks = 10;
        long previousSession = carrierUnloadRequest == null ? 0 : carrierUnloadRequest.Session;
        CarrierUnloadPhase previousPhase = carrierUnloadPhase;
        for (int i = 0; i < 2 && carrierUnloadListener != null && carrierUnloadListener.HasPendingMessage; i++)
        {
            MyIGCMessage message = carrierUnloadListener.AcceptMessage();
            AcceptCarrierUnload(message.Source, message.Data as string);
        }
        acceptedNew = carrierUnloadRequest != null && (previousSession != carrierUnloadRequest.Session
            || previousPhase != CarrierUnloadPhase.Delay && carrierUnloadPhase == CarrierUnloadPhase.Delay);
    }
    if (carrierUnloadRequest == null || carrierUnloadPhase == CarrierUnloadPhase.Cancelled
        || carrierUnloadPhase == CarrierUnloadPhase.Complete) return;
    if (!CarrierUnloadDockMatches(carrierUnloadRequest))
    {
        CancelCarrierUnloading();
        return;
    }
    if (carrierUnloadPhase == CarrierUnloadPhase.Blocked) return;
    carrierUnloadLease -= UpdateTickDelta();
    if (carrierUnloadLease <= 0)
    {
        BlockCarrierUnload("Miner request timed out; waiting for its next request.");
        return;
    }
    if (acceptedNew) return; // Do not charge the incoming request for time before it arrived.
    if (carrierUnloadPhase == CarrierUnloadPhase.Saving)
    {
        if (!HasCarrierProgressCheckpoint(carrierUnloadSaveRevision))
        {
            RequestCarrierSave(false);
            return;
        }
        carrierUnloadPhase = CarrierUnloadPhase.Complete;
        ReplyCarrierUnload("complete");
        ScriptLog("Carrier unloading complete.");
        return;
    }
    if (carrierUnloadPhase == CarrierUnloadPhase.Delay)
    {
        carrierUnloadDelay -= UpdateTickDelta();
        if (carrierUnloadDelay > 0) return;
        carrierUnloadPhase = CarrierUnloadPhase.Peer;
    }
    if (carrierUnloadPhase == CarrierUnloadPhase.Peer)
    {
        var pb = GridTerminalSystem.GetBlockWithId(carrierUnloadPeer) as IMyProgrammableBlock;
        if (pb == null)
        {
            RetryCarrierUnloadResolution("Miner programmable block is not visible/accessible.");
            return;
        }
        if (!pb.IsSameConstructAs(carrierConnector.OtherConnector)
            || pb.CustomName.IndexOf(ActiveMiner().PbName, StringComparison.OrdinalIgnoreCase) < 0)
        {
            CancelCarrierUnloading();
            return;
        }
        carrierUnloadPhase = CarrierUnloadPhase.Resolve;
        carrierUnloadPeerVerified = true;
        return;
    }
    if (carrierUnloadPhase == CarrierUnloadPhase.Resolve)
    {
        if (carrierUnloadResolveIndex >= carrierUnloadRequest.Sources.Count)
        {
            if (carrierUnloadTargets.Count == 0 || carrierUnloadSources.Count == 0)
            {
                BlockCarrierUnload("No cached Carrier targets or no valid Miner inventories. Check names and recompile.");
                return;
            }
            carrierUnloadSourceIndex = carrierUnloadTargetIndex = carrierUnloadTargetAttempts = 0;
            carrierUnloadMovedThisPass = false;
            carrierUnloadItem = null;
            carrierUnloadBeforeItem = null;
            carrierUnloadPhase = CarrierUnloadPhase.Read;
            ScriptLog("Carrier unloading started (" + carrierUnloadSources.Count + " source inventories, "
                + carrierUnloadTargets.Count + " cached targets).");
            return;
        }
        var block = GridTerminalSystem.GetBlockWithId(carrierUnloadRequest.Sources[carrierUnloadResolveIndex]);
        if (block == null)
        {
            RetryCarrierUnloadResolution("An advertised Miner cargo block is missing or inaccessible.");
            return;
        }
        if (!block.HasInventory || !block.IsSameConstructAs(carrierConnector.OtherConnector)
            || block.CustomName.IndexOf(Mi(""), StringComparison.OrdinalIgnoreCase) < 0
            || block is IMyConveyorSorter
            || block is IMyShipConnector && block.CustomName.IndexOf(Mi("Ejector"), StringComparison.OrdinalIgnoreCase) >= 0
            || block.InventoryCount > 16)
        {
            BlockCarrierUnload("Rejected ineligible Miner cargo ID " + block.EntityId + ".");
            return;
        }
        for (int i = 0; i < block.InventoryCount; i++)
        {
            var inventory = block.GetInventory(i);
            if (inventory == null) { BlockCarrierUnload("Miner inventory unavailable."); return; }
            carrierUnloadSources.Add(inventory);
        }
        carrierUnloadResolveIndex++;
        return; // At most one advertised source block is resolved per update.
    }
    carrierUnloadWorkTicks -= UpdateTickDelta();
    if (carrierUnloadWorkTicks > 0) return;
    carrierUnloadWorkTicks = 10;
    if (carrierUnloadPhase == CarrierUnloadPhase.Read)
    {
        if (carrierUnloadSourceIndex >= carrierUnloadSources.Count)
        {
            if (carrierUnloadMovedThisPass)
            {
                carrierUnloadMovedThisPass = false;
                carrierUnloadSourceIndex = 0; // Verify an empty pass after the last actual delivery.
                return;
            }
            // The miner must not finish its path until the delivered totals are
            // durable, but serialization no longer runs inside this transfer tick.
            carrierUnloadPhase = CarrierUnloadPhase.Saving;
            carrierUnloadSaveRevision = RequestCarrierProgressCheckpoint();
            return;
        }
        carrierUnloadItem = carrierUnloadSources[carrierUnloadSourceIndex].GetItemAt(0);
        if (!carrierUnloadItem.HasValue) { carrierUnloadSourceIndex++; return; }
        carrierUnloadTargetAttempts = 0;
        carrierUnloadPhase = CarrierUnloadPhase.Transfer;
        return;
    }
    if (carrierUnloadPhase == CarrierUnloadPhase.Verify)
    {
        if (!carrierUnloadBeforeItem.HasValue)
        {
            carrierUnloadPhase = CarrierUnloadPhase.Read;
            return;
        }
        var verifySource = carrierUnloadSources[carrierUnloadSourceIndex];
        MyInventoryItem beforeItem = carrierUnloadBeforeItem.Value;
        MyInventoryItem? after = verifySource.GetItemByID(beforeItem.ItemId);
        double delivered = (double)beforeItem.Amount - (after.HasValue ? (double)after.Value.Amount : 0);
        carrierUnloadBeforeItem = null;
        if (delivered > 0)
        {
            RecordCarrierDeliveredOre(beforeItem, delivered, carrierUnloadRequest.Mission);
            carrierUnloadMovedThisPass = true;
            carrierUnloadPhase = CarrierUnloadPhase.Read;
            return;
        }
        carrierUnloadTargetIndex = (carrierUnloadTargetIndex + 1) % carrierUnloadTargets.Count;
        carrierUnloadTargetAttempts++;
        if (carrierUnloadTargetAttempts >= carrierUnloadTargets.Count)
            BlockCarrierUnload("Carrier cargo is full or the conveyor route is blocked.");
        else
            carrierUnloadPhase = CarrierUnloadPhase.Transfer;
        return;
    }
    if (carrierUnloadPhase != CarrierUnloadPhase.Transfer) return;
    if (!TryClaimCarrierHeavyWork()) return;
    var source = carrierUnloadSources[carrierUnloadSourceIndex];
    MyInventoryItem? before = source.GetItemByID(carrierUnloadItem.Value.ItemId);
    if (!before.HasValue) { carrierUnloadPhase = CarrierUnloadPhase.Read; return; }
    var targetBlock = carrierUnloadTargets[carrierUnloadTargetIndex];
    if (targetBlock != null && targetBlock.HasInventory)
    {
        var destination = targetBlock.GetInventory(0);
        if (destination != null && destination != source)
        {
            // The game's transfer checks conveyor connectivity and caps to available space.
            // One attempt per step, with no nested target loop or partial-fit probes.
            carrierUnloadBeforeItem = before;
            destination.TransferItemFrom(source, before.Value);
            carrierUnloadPhase = CarrierUnloadPhase.Verify;
            return;
        }
    }
    carrierUnloadTargetIndex = (carrierUnloadTargetIndex + 1) % carrierUnloadTargets.Count;
    carrierUnloadTargetAttempts++;
    if (carrierUnloadTargetAttempts >= carrierUnloadTargets.Count)
        BlockCarrierUnload("Carrier cargo is full or the conveyor route is blocked.");
}
void RetryCarrierUnloadResolution(string reason)
{
    carrierUnloadResolveAttempts++;
    if (carrierUnloadResolveAttempts >= 3) { BlockCarrierUnload(reason); return; }
    carrierUnloadSources.Clear();
    carrierUnloadResolveIndex = 0;
    carrierUnloadDelay = 120;
    carrierUnloadPhase = CarrierUnloadPhase.Delay;
}
void RecordCarrierDeliveredOre(MyInventoryItem item, double moved, string mission)
{
    if (moved <= 0 || reportFrozen || reportMission.Length == 0 || mission != reportMission
        || !item.Type.TypeId.EndsWith("_Ore", StringComparison.Ordinal)) return;
    asteroidReport.AddOre(item.Type.SubtypeId, moved);
    miningTotals.AddOre(item.Type.SubtypeId, moved);
    MarkProgressStateDirty();
}
