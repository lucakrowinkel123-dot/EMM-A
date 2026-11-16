namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private long resetSkippedAsteroid;
        private bool MatchesRequestedAsteroid(long entityId)
        {
            return requestedAsteroidEntityId == 0 ? entityId != resetSkippedAsteroid : entityId == requestedAsteroidEntityId;
        }

        private void PromoteMatchingBackgroundScan()
        {
            var current = scanner.Current;
            if (scanner.Active && backgroundScanActive && current != null && MatchesRequestedAsteroid(current.EntityId))
            {
                backgroundScanActive = false;
                Emma3Log.Info("Promoted the active idle background scan to the requested planning scan.");
            }
        }

        private void OnScanComplete(AsteroidMap map)
        {
            if (map == null || !map.Complete)
            {
                backgroundScanActive = false;
                return;
            }
            RecordCompletedScan(map);
            if (departurePreparation != null && !departurePreparation.Complete)
            {
                backgroundScanActive = false;
                return;
            }
            Emma3Log.Diagnostic("Candidate asteroid " + map.EntityId + " ores: " + DescribeMapOres(map) + ".");
            if (backgroundScanActive || !MatchesRequestedAsteroid(map.EntityId))
            {
                backgroundScanActive = false;
                if (planningActive)
                    ContinuePlanning();
                return;
            }

            if (!planningActive)
                return;
            OreSelection ores = completed.Remaining(map.EntityId, planningSelection);
            if (Qualifies(map, ores))
                BuildAndPublish(map);
            else
                ContinuePlanning();
        }
    }
}
