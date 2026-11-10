// Unload completion remains persistent; activity reports are owned by Carrier.
bool oreTransferSucceeded;
int finalUnloadRetryTicks;
void UpdateFinalUnloadRecovery()
{
    finalUnloadRetryTicks += UpdateTickDelta();
    if (finalUnloadRetryTicks < 300) return;
    finalUnloadRetryTicks = 0;
    if (pathMode && pathIndex >= pathRecords.Count && IsMinerDocked()
        && !oreTransferSucceeded && !oreTransferActive && oreTransferStartDelayTicks <= 0)
        StartOreTransfer();
}
