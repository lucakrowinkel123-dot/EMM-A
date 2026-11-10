using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private void ContinueBackgroundScan()
        {
            if (frame < nextBackgroundRefresh && backgroundIndex >= backgroundCandidates.Count)
                return;
            if (backgroundIndex >= backgroundCandidates.Count)
            {
                backgroundCandidates.Clear();
                Vector3D current = blocks.CarrierRemote.GetPosition();
                BoundingSphereD sphere = new BoundingSphereD(current, settings.SearchRadius);
                var entities = MyEntities.GetEntitiesInSphere(ref sphere);
                if (entities != null)
                    foreach (MyEntity entity in entities)
                    {
                        var voxel = entity as MyVoxelBase;
                        if (voxel == null || voxel is MyPlanet || voxel.Closed || voxel.MarkedForClose)
                            continue;
                        backgroundCandidates.Add(voxel);
                    }

                backgroundCandidates.Sort((a, b) => DistanceToBox(current, a.PositionComp.WorldAABB).CompareTo(DistanceToBox(current, b.PositionComp.WorldAABB)));
                backgroundIndex = 0;
                nextBackgroundRefresh = frame + 3600;
            }

            while (backgroundIndex < backgroundCandidates.Count)
            {
                MyVoxelBase voxel = backgroundCandidates[backgroundIndex++];
                if (voxel == null || voxel.Closed || voxel.MarkedForClose)
                    continue;
                if (completed == null)
                    completed = new CompletedAsteroids(userDataPath);
                long entityId = voxel.EntityId;
                if (completed.Contains(entityId))
                    continue;
                if (rejectedCandidates.Contains(entityId))
                    continue;
                if (cache.ContainsKey(entityId))
                    continue;
                backgroundScanActive = true;
                scanner.Begin(voxel);
                Emma3Log.Diagnostic("Background pre-scan started for asteroid " + entityId + ".");
                return;
            }
        }

        private void TryContinueBackgroundScan()
        {
            try
            {
                ContinueBackgroundScan();
            }
            catch (Exception ex)
            {
                // Idle pre-scanning is optional and must never disable mission
                // handling. Defer another attempt and leave the plugin alive.
                backgroundScanActive = false;
                backgroundCandidates.Clear();
                backgroundIndex = 0;
                nextBackgroundRefresh = frame + 600;
                Emma3Log.Error("Idle background pre-scan setup failed; mission requests remain available: " + ex);
            }
        }
    }
}
