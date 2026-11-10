using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRageMath;

namespace EMMA3.Client
{
    internal sealed class MaterialSample
    {
        public Vector3D Position;
        public string Material;
        public string Ore;
        public bool IsStone;
        public bool IsMineableOre;
    }

    internal sealed class AsteroidMap
    {
        public MyVoxelBase Voxel;
        public long EntityId;
        public BoundingBoxD EntityBox;
        public BoundingBoxD OccupiedBox = BoundingBoxD.CreateInvalid();
        public readonly List<MaterialSample> Samples = new List<MaterialSample>();
        public readonly Dictionary<string, List<MaterialSample>> OreSamples = new Dictionary<string, List<MaterialSample>>(StringComparer.OrdinalIgnoreCase);
        public bool Complete;
        public long TestedSamples;
        public long ProcessedSamples;
        public long TotalSamples;
        public long SkippedEmptySamples;
        public int EmptyChunks;
        public int NonEmptyChunks;
        public double HighestOccupiedY = double.NegativeInfinity;
        public void Add(MaterialSample sample)
        {
            IncludeOccupied(sample.Position);
            AddSurface(sample);
            if (sample.IsMineableOre && !string.IsNullOrWhiteSpace(sample.Ore))
                AddOre(sample);
        }

        public void IncludeOccupied(Vector3D position)
        {
            if (OccupiedBox.Min.X > OccupiedBox.Max.X)
                OccupiedBox = new BoundingBoxD(position, position);
            else
                OccupiedBox.Include(position);
            HighestOccupiedY = Math.Max(HighestOccupiedY, position.Y);
        }

        public void AddSurface(MaterialSample sample)
        {
            Samples.Add(sample);
        }

        public void AddOre(MaterialSample sample)
        {
            List<MaterialSample> list;
            if (!OreSamples.TryGetValue(sample.Ore, out list))
                OreSamples[sample.Ore] = list = new List<MaterialSample>();
            list.Add(sample);
        }
    }

    internal sealed class OreGroup
    {
        public readonly List<MaterialSample> Samples = new List<MaterialSample>();
        public readonly HashSet<string> Ores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public BoundingBoxD Box = BoundingBoxD.CreateInvalid();
        public Vector3D Center => Box.Center;

        public void Add(MaterialSample sample)
        {
            Samples.Add(sample);
            Ores.Add(sample.Ore ?? sample.Material ?? "unknown");
            if (Box.Min.X > Box.Max.X)
                Box = new BoundingBoxD(sample.Position, sample.Position);
            else
                Box.Include(sample.Position);
        }

        public void Merge(OreGroup other)
        {
            foreach (MaterialSample sample in other.Samples)
                Add(sample);
        }
    }

    internal sealed class MiningPlane
    {
        public Vector3D CornerA;
        public Vector3D CornerB;
        public Vector3D Forward;
        public Vector3D Up;
        public Vector3D Right;
        public Vector3D Center;
        public Vector3D Staging;
        public double Width;
        public double Height;
        public double Depth;
        public readonly List<int> SkipLanes = new List<int>();
        public readonly List<double> LaneDepths = new List<double>();
        public readonly List<List<DepthInterval>> LaneDestructionRanges = new List<List<DepthInterval>>();
        public readonly HashSet<string> Ores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal struct DepthInterval
    {
        public double Start;
        public double End;
        public DepthInterval(double start, double end)
        {
            Start = start;
            End = end;
        }
    }

    internal struct DrillFootprint
    {
        public Vector3D LocalOffset;
        public double Radius;
    }

    internal sealed class RoutePose
    {
        public Vector3D Position;
        public Vector3D Forward;
        public Vector3D Up;
        public string Kind;
    }

    internal sealed class MissionPlan
    {
        public string MissionId;
        public long AsteroidEntityId;
        public AsteroidMap Asteroid;
        public Vector3D CarrierPosition;
        public Vector3D? CarrierApproachStart;
        public Vector3D CarrierForward;
        public Vector3D CarrierUp;
        public Vector3D MinerDockPosition;
        public Vector3D MinerDockForward;
        public Vector3D MinerDockUp;
        public Vector3D MinerDeparturePosition;
        public readonly List<RoutePose> CarrierRoute = new List<RoutePose>();
        public readonly List<RoutePose> MinerRoute = new List<RoutePose>();
        public readonly List<MiningPlane> Planes = new List<MiningPlane>();
        public string MinerPathText;
        public string PathHash;
        public string ValidationMessage;
    }

    internal sealed class PlannerSettings
    {
        public double SearchRadius = 50000;
        public double PollMeters = 4;
        public double OrePollMeters = 2;
        public double DestructionRadiusMultiplier = 3;
        public double DestructionProtectionMeters = 4;
        public double DockOuterPoseDistance = 200;
        public double VeinJoinMeters = 8;
        public double GroupJoinMeters = 16;
        public double OrePadding = 1;
        public double PlaneClearance = 25;
        public double TravelClearance = 75;
        public double MinerRouteStartExtra = 0;
        public double ParkingHeight = 400;
        public double MineWidth = 17.5;
        public double MineHeight = 12.5;
        public Vector3D MinerTargetOffsetLocal = new Vector3D(0, 0, 5);
        public double OpenPathSpacing = 100;
        public double NearPathSpacing = 15;
        public int CandidateDirections = 80;
        public bool Visualization;
        public bool AllowMovement;
        public bool ScriptLogging = true;
        public bool AllowSeatExitDuringMining;
        public PlannerSettings Clone()
        {
            return (PlannerSettings)MemberwiseClone();
        }
    }

    // Everything the route worker needs is captured on the game thread.  The
    // completed asteroid map is immutable after VoxelScanner.Step consumes it;
    // the remaining values are structs or copied lists, so the worker never
    // dereferences a live block, grid, connector, or voxel entity.
    internal sealed class PlanningSnapshot
    {
        public bool CarrierOnly;
        public AsteroidMap Asteroid;
        public OreSelection OreSelection;
        public PlannerSettings Settings;
        public MatrixD CarrierRemoteWorld;
        public MatrixD MinerRemoteWorld;
        public bool HasCarrierConnector;
        public Vector3D CarrierConnectorForward;
        public Vector3D CarrierConnectorUp;
        public Vector3D MinerToolOffsetLocal;
        public readonly List<DrillFootprint> MinerDrills = new List<DrillFootprint>();
        public readonly List<AsteroidMap> DepartureMaps = new List<AsteroidMap>();
        // Request-local, worker-only spatial indexes. Never shared with live entities.
        public readonly List<OrganicVoxelRouter> DepartureRouters = new List<OrganicVoxelRouter>();
        public readonly List<BoundingBoxD> OtherVoxelBoxes = new List<BoundingBoxD>();
    }

    internal sealed class PlanningCancellation
    {
        private volatile bool cancelled;
        public bool IsCancellationRequested => cancelled;

        public void Cancel()
        {
            cancelled = true;
        }

        public void ThrowIfCancellationRequested()
        {
            if (cancelled)
                throw new OperationCanceledException("route planning cancelled");
        }
    }

    internal sealed class PlanningProgress
    {
        private volatile string message = "Planning route";
        public readonly DateTime StartedUtc = DateTime.UtcNow;
        public string Message => message;
        public TimeSpan Elapsed => DateTime.UtcNow - StartedUtc;

        public void Set(string value)
        {
            message = string.IsNullOrWhiteSpace(value) ? "Planning route" : value;
        }
    }
}
