// Ephemeral permission: a proof is valid only for this physical docking cycle.
static class ModuleDockProof
{
    public static bool Matches(bool contact, bool docked, int age, long generation, long other,
        long request, string mission, long proofGeneration, long proofOther, long proofRequest, string proofMission)
    {
        return contact && docked && age >= 0 && age <= 180 && other != 0 && request > 0
            && generation == proofGeneration && other == proofOther && request == proofRequest
            && string.Equals(mission, proofMission, System.StringComparison.Ordinal);
    }
}
