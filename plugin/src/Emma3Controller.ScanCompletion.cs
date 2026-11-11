namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private bool MatchesRequestedAsteroid(long entityId)
        {
            return requestedAsteroidEntityId == 0 || entityId == requestedAsteroidEntityId;
        }

        private void PromoteMatchingBackgroundScan()
        {
            if (scanner.Active && backgroundScanActive && scanner.Current != null && MatchesRequestedAsteroid(scanner.Current.EntityId))
            {
                backgroundScanActive = false;
                Emma3Log.Info("Promoted the active idle background scan to the requested planning scan.");
            }
        }

        private void OnScanComplete(AsteroidMap map)
        {
            if (departurePreparation != null && !departurePreparation.Complete)
            {
                if (map != null && map.Complete)
                {
                    cache[map.EntityId] = map;
                    AddDetectedOresToTimer(map);
                }
                backgroundScanActive = false;
                return;
            }
            if (map == null)
                return;
            cache[map.EntityId] = map;
            AddDetectedOresToTimer(map);
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
