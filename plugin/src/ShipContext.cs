// Runtime controller state belongs to one Carrier, bridge and configured pair.
public sealed class ShipContext
{
    private long carrierId, bridgeId;
    private string pair;
    public bool Changed(long carrier, long bridge, string names)
    {
        if (carrier == 0 || bridge == 0) return false;
        bool changed = carrierId != 0 && (carrierId != carrier || bridgeId != bridge || pair != names);
        carrierId = carrier; bridgeId = bridge; pair = names;
        return changed;
    }
}
