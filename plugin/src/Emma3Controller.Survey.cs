using System.Collections.Generic;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        // Completed mining can evict a map. Keep its scan credit for this session.
        private readonly HashSet<long> scannedAsteroids = new HashSet<long>();

        private void RecordCompletedScan(AsteroidMap map)
        {
            cache[map.EntityId] = map;
            scannedAsteroids.Add(map.EntityId);
            AddDetectedOresToTimer(map);
        }
    }
}
