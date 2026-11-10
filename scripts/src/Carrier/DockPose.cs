// Typed, demand-driven dock poses. Renewal and heartbeat contain no coordinates.
IMyBroadcastListener dockDemandListener;
readonly long dockCarrierEpoch = DateTime.UtcNow.Ticks;
long dockPeer, dockSession, dockRevision, dockAckEpoch, dockAckRevision;
int dockLeaseTicks, dockSampleTicks, dockHeartbeatTicks;
bool dockPoseSent;
Vector3D dockSentPosition, dockSentForward, dockSentUp;

void UpdateCarrierDockPoses()
{
    int delta = UpdateTickDelta();
    dockLeaseTicks = Math.Max(0, dockLeaseTicks - delta);
    dockSampleTicks = Math.Max(0, dockSampleTicks - delta);
    dockHeartbeatTicks = Math.Max(0, dockHeartbeatTicks - delta);
    if (dockDemandListener != null)
    {
        int budget = 1;
        while (budget-- > 0 && dockDemandListener.HasPendingMessage)
        {
            MyIGCMessage message = dockDemandListener.AcceptMessage();
            if (!(message.Data is MyTuple<long, long, long, long, bool>)) continue;
            var request = (MyTuple<long, long, long, long, bool>)message.Data;
            if (request.Item1 <= 0 || carrierConnector == null || request.Item2 != carrierConnector.EntityId) continue;
            if (!request.Item5)
            {
                if (message.Source == dockPeer && request.Item1 == dockSession) dockLeaseTicks = 0;
                continue;
            }
            if (dockLeaseTicks > 0 && message.Source != dockPeer) continue;
            if (message.Source == dockPeer && request.Item1 < dockSession) continue;
            if (message.Source != dockPeer || request.Item1 != dockSession)
            {
                dockPeer = message.Source;
                dockSession = request.Item1;
                dockRevision = 0;
                dockPoseSent = false;
                dockSampleTicks = 0;
            }
            dockAckEpoch = request.Item3;
            dockAckRevision = request.Item4;
            dockLeaseTicks = 240;
        }
    }
    if (dockLeaseTicks <= 0 || carrierConnector == null || carrierConnector.Closed) return;
    // Read hardware only at the two-second sample; heartbeat merely confirms contact.
    if (dockSampleTicks <= 0)
    {
        dockSampleTicks = 120;
        MatrixD matrix = carrierConnector.WorldMatrix;
        Vector3D position = carrierConnector.GetPosition() + matrix.Forward * broadcastForwardOffset;
        Vector3D forward = matrix.Forward, up = matrix.Up;
        if (!ValidDockPose(position, forward, up)) { dockPoseSent = false; return; }
        bool changed = !dockPoseSent || position != dockSentPosition || forward != dockSentForward || up != dockSentUp;
        if (changed || dockAckEpoch != dockCarrierEpoch || dockAckRevision != dockRevision)
        {
            if (changed) dockRevision++;
            dockSentPosition = position;
            dockSentForward = forward;
            dockSentUp = up;
            dockPoseSent = true;
            IGC.SendBroadcastMessage(dockBroadcastTag + "_V2",
                new MyTuple<long, long, long, long, long, MyTuple<Vector3D, Vector3D, Vector3D>>(
                    dockPeer, dockSession, dockCarrierEpoch, dockRevision, carrierConnector.EntityId,
                    new MyTuple<Vector3D, Vector3D, Vector3D>(position, forward, up)));
            dockHeartbeatTicks = 60;
        }
    }
    if (dockPoseSent && dockHeartbeatTicks <= 0)
    {
        dockHeartbeatTicks = 60;
        IGC.SendBroadcastMessage(dockBroadcastTag + "_V2",
            new MyTuple<long, long, long, long, long>(dockPeer, dockSession, dockCarrierEpoch, dockRevision, carrierConnector.EntityId));
    }
}
