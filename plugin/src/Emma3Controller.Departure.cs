using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed partial class Emma3Controller
    {
        private sealed class DeparturePreparation
        {
            public readonly VoxelScanner Scanner = new VoxelScanner();
            public readonly List<MyVoxelBase> Queue = new List<MyVoxelBase>();
            public readonly Dictionary<long, AsteroidMap> Maps = new Dictionary<long, AsteroidMap>();
            public int Index;
            public bool Complete;
        }
        private DeparturePreparation departurePreparation;
        private bool restartPlanningAfterCockpitLoss;

        private void CancelDeparturePreparation()
        {
            if (departurePreparation != null) departurePreparation.Scanner.Cancel();
            departurePreparation = null;
        }

        private void PrepareDeparture(BridgeDocument bridge)
        {
            var job = new DeparturePreparation();
            Vector3D start = blocks.CarrierRemote.GetPosition();
            long previous;
            long.TryParse(bridge.Get("completedEntityId"), out previous);
            // Candidate enumeration includes completed entities; ore eligibility
            // is applied later. Exit safety must never depend on ore selection.
            foreach (MyVoxelBase voxel in candidates)
            {
                BoundingBoxD guard = voxel.PositionComp.WorldAABB;
                guard.Inflate(Math.Max(50, settings.TravelClearance));
                if (voxel.EntityId == previous || guard.Contains(start) != ContainmentType.Disjoint)
                    job.Queue.Add(voxel);
            }
            job.Complete = job.Queue.Count == 0;
            departurePreparation = job;
        }

        private void StepDeparturePreparation()
        {
            var job = departurePreparation;
            if (job == null || job.Complete) return;
            try
            {
                if (job.Scanner.Active)
                {
                    job.Scanner.Step();
                    if (job.Scanner.Active) return;
                    AsteroidMap map = job.Scanner.Current;
                    if (map == null || !map.Complete) throw new InvalidOperationException("scan incomplete");
                    job.Maps.Add(map.EntityId, map);
                    cache[map.EntityId] = map;
                    AddDetectedOresToTimer(map);
                    job.Scanner.ClearCurrent();
                }
                if (job.Index == job.Queue.Count)
                {
                    job.Complete = true;
                    return;
                }
                MyVoxelBase voxel = job.Queue[job.Index++];
                if (voxel == null || voxel.Closed || voxel.MarkedForClose)
                    throw new InvalidOperationException("asteroid disappeared");
                job.Scanner.Begin(voxel);
                UpdateStatus("scanning", "checking fresh departure voxels " + job.Index + "/" + job.Queue.Count);
            }
            catch (Exception ex)
            {
                CancelDeparturePreparation();
                CancelPlanningJob("departure scan failed");
                planningActive = false;
                BridgeDocument bridge = BridgeDocument.Parse(blocks.BridgeTimer.CustomData);
                Fail(bridge, "Departure unsafe: " + ex.GetBaseException().Message, true);
                blocks.BridgeTimer.CustomData = bridge.Serialize();
            }
        }
    }
}
