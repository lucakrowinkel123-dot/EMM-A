using System;
using System.Collections.Generic;
using System.Threading;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Voxels;
using VRage.Voxels;
using VRageMath;

namespace EMMA3.Client
{
    // Asteroid Scanner-style whole-storage scan at LOD2 (4 metres), followed
    // inside the same background job by local LOD1 (2 metre) ore refinement.
    internal sealed class VoxelScanner
    {
        private const int QueryLod = 2;
        private const int SampleMeters = 1 << QueryLod;
        private const int OreQueryLod = 1;
        private const int OreSampleMeters = 1 << OreQueryLod;
        private const int OreRefineHaloCells = 2; // 4 m beyond each coarse ore component
        private const int ProgressIntervalMask = 0xFFFF;
        private const byte IsoLevel = 127;
        private AsteroidMap map;
        private volatile bool started;
        private volatile bool workerFinished;
        private volatile bool cancelRequested;
        private Exception workerException;
        private DateTime startedUtc;
        private long processedSamples;
        private long totalSamples;
        private volatile string phase = "idle";
        private Vector3I scanMin;
        private Vector3I scanMax;
        private Vector3I scanSize;
        private long estimatedRawBytes;
        private sealed class RefinementRegion
        {
            public Vector3I Min;
            public Vector3I Max;
        }

        public AsteroidMap Current => map;
        public bool Active => started;
        public string Phase => phase;

        public double Progress
        {
            get
            {
                long total = Interlocked.Read(ref totalSamples);
                return total <= 0 ? 0 : Math.Min(1, (double)Interlocked.Read(ref processedSamples) / total);
            }
        }

        public TimeSpan Elapsed => startedUtc == default(DateTime) ? TimeSpan.Zero : DateTime.UtcNow - startedUtc;

        public TimeSpan EstimatedRemaining
        {
            get
            {
                double progress = Progress;
                if (progress <= 0.000001)
                    return TimeSpan.Zero;
                double seconds = Elapsed.TotalSeconds * (1 - progress) / progress;
                return TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds, Math.Max(0, seconds)));
            }
        }

        public void Begin(MyVoxelBase voxel)
        {
            if (started)
                throw new InvalidOperationException("A voxel scan is already active.");
            map = new AsteroidMap
            {
                Voxel = voxel,
                EntityId = voxel.EntityId,
                EntityBox = voxel.PositionComp.WorldAABB
            };
            Vector3I min = voxel.StorageMin >> QueryLod;
            Vector3I max = (voxel.StorageMax >> QueryLod) - 1;
            long countX = Math.Max(0, (long)max.X - min.X + 1);
            long countY = Math.Max(0, (long)max.Y - min.Y + 1);
            long countZ = Math.Max(0, (long)max.Z - min.Z + 1);
            totalSamples = countX * countY * countZ;
            scanMin = min;
            scanMax = max;
            scanSize = new Vector3I((int)countX, (int)countY, (int)countZ);
            estimatedRawBytes = totalSamples > long.MaxValue / 2 ? long.MaxValue : totalSamples * 2;
            processedSamples = 0;
            map.TotalSamples = totalSamples;
            map.ProcessedSamples = 0;
            startedUtc = DateTime.UtcNow;
            workerException = null;
            workerFinished = false;
            cancelRequested = false;
            phase = "queued";
            started = true;
            Emma3Log.Diagnostic("Scanning asteroid " + map.EntityId
                + " with background voxel query plus local ore refinement: size=" + scanSize
                + " samples=" + totalSamples
                + " rawArrays~=" + FormatBytes(estimatedRawBytes)
                + " coarseResolution=" + SampleMeters
                + "m oreResolution=" + OreSampleMeters
                + "m range=" + min
                + ".." + max
                + ".");
            MyAPIGateway.Parallel.StartBackground(() => ScanInternal(voxel, min, max));
        }

        // Completion is consumed on the game thread so the controller never reads
        // map collections while the worker is still modifying them.
        public bool Step()
        {
            if (!started)
                return true;
            if (!workerFinished)
                return false;
            started = false;
            if (workerException != null)
                throw new InvalidOperationException("Background voxel scan failed.", workerException);
            return true;
        }

        public void Cancel()
        {
            cancelRequested = true;
        }

        public void ClearCurrent()
        {
            if (!started)
                map = null;
        }

        private void ScanInternal(MyVoxelBase voxel, Vector3I min, Vector3I max)
        {
            try
            {
                if (totalSamples <= 0)
                    throw new InvalidOperationException("Asteroid voxel storage has an empty LOD2 range.");
                phase = "allocating storage";
                var storage = new MyStorageData();
                storage.Resize(min, max);
                phase = "reading content and material";
                voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.ContentAndMaterial, QueryLod, min, max);
                phase = "processing raw arrays";
                byte[] content = storage[MyStorageDataTypeEnum.Content];
                byte[] material = storage[MyStorageDataTypeEnum.Material];
                Vector3I size = storage.Size3D;
                long occupied = 0;
                long surface = 0;
                long ore = 0;
                int plane = size.X * size.Y;
                MatrixD voxelWorld = voxel.PositionComp.WorldMatrixRef;
                Vector3D storageHalf = voxel.SizeInMetresHalf;
                var coarseOreCells = new List<Vector3I>();
                for (int index = 0; index < content.Length; index++)
                {
                    if ((index & ProgressIntervalMask) == 0)
                    {
                        if (cancelRequested)
                            throw new OperationCanceledException("Voxel scan cancelled.");
                        Interlocked.Exchange(ref processedSamples, index);
                        Interlocked.Exchange(ref map.ProcessedSamples, index);
                    }

                    if (content[index] <= IsoLevel)
                        continue;
                    occupied++;
                    int x = index % size.X;
                    int y = (index / size.X) % size.Y;
                    int z = index / plane;
                    Vector3D storagePosition = (Vector3D)((min + new Vector3I(x, y, z)) * SampleMeters) + new Vector3D(SampleMeters * 0.5);
                    Vector3D worldPosition = Vector3D.Transform(storagePosition - storageHalf, voxelWorld);
                    map.IncludeOccupied(worldPosition);
                    MyVoxelMaterialDefinition definition = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material[index]);
                    string materialName = definition == null ? "Unknown" : definition.MaterialTypeName;
                    string minedOre = definition == null ? "" : definition.MinedOre;
                    if (string.IsNullOrWhiteSpace(minedOre))
                        minedOre = materialName;
                    bool stone = IsStone(materialName, minedOre);
                    bool mineableOre = definition != null && definition.CanBeHarvested && definition.IsRare && !stone && !string.IsNullOrWhiteSpace(definition.MinedOre);
                    MaterialSample sample = null;
                    if (mineableOre)
                    {
                        sample = CreateSample(worldPosition, materialName, minedOre, stone, true);
                        map.AddOre(sample);
                        coarseOreCells.Add(min + new Vector3I(x, y, z));
                        ore++;
                    }

                    if (IsSurface(content, size, plane, index, x, y, z))
                    {
                        map.AddSurface(sample ?? CreateSample(worldPosition, materialName, minedOre, stone, false));
                        surface++;
                    }
                }

                // Refine only neighborhoods around detected coarse ore components.
                // The 4 m surface map remains untouched for conservative collision
                // checks, while route planning receives 2 m ore coordinates.
                long coarseTested = content.LongLength;
                long fineTested = 0;
                long fineOccupied = 0;
                long refinedOre = 0;
                List<RefinementRegion> regions = BuildRefinementRegions(voxel, coarseOreCells);
                long fineTotal = 0;
                for (int i = 0; i < regions.Count; i++)
                    fineTotal += RegionVolume(regions[i]);
                Interlocked.Exchange(ref totalSamples, coarseTested + fineTotal);
                map.TotalSamples = coarseTested + fineTotal;
                if (regions.Count > 0)
                {
                    phase = "refining ore at 2 m";
                    var refinedSamples = new List<MaterialSample>();
                    for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
                    {
                        if (cancelRequested)
                            throw new OperationCanceledException("Voxel scan cancelled.");
                        RefinementRegion region = regions[regionIndex];
                        var fineStorage = new MyStorageData();
                        fineStorage.Resize(region.Min, region.Max);
                        voxel.Storage.ReadRange(fineStorage, MyStorageDataTypeFlags.ContentAndMaterial, OreQueryLod, region.Min, region.Max);
                        byte[] fineContent = fineStorage[MyStorageDataTypeEnum.Content];
                        byte[] fineMaterial = fineStorage[MyStorageDataTypeEnum.Material];
                        Vector3I fineSize = fineStorage.Size3D;
                        int finePlane = fineSize.X * fineSize.Y;
                        for (int index = 0; index < fineContent.Length; index++)
                        {
                            if ((index & ProgressIntervalMask) == 0)
                            {
                                if (cancelRequested)
                                    throw new OperationCanceledException("Voxel scan cancelled.");
                                long progress = coarseTested + fineTested + index;
                                Interlocked.Exchange(ref processedSamples, progress);
                                Interlocked.Exchange(ref map.ProcessedSamples, progress);
                            }

                            if (fineContent[index] <= IsoLevel)
                                continue;
                            fineOccupied++;
                            MyVoxelMaterialDefinition definition = MyDefinitionManager.Static.GetVoxelMaterialDefinition(fineMaterial[index]);
                            string materialName = definition == null ? "Unknown" : definition.MaterialTypeName;
                            string minedOre = definition == null ? "" : definition.MinedOre;
                            if (string.IsNullOrWhiteSpace(minedOre))
                                minedOre = materialName;
                            bool stone = IsStone(materialName, minedOre);
                            bool mineableOre = definition != null && definition.CanBeHarvested && definition.IsRare && !stone && !string.IsNullOrWhiteSpace(definition.MinedOre);
                            if (!mineableOre)
                                continue;
                            int x = index % fineSize.X;
                            int y = (index / fineSize.X) % fineSize.Y;
                            int z = index / finePlane;
                            Vector3D storagePosition = (Vector3D)((region.Min + new Vector3I(x, y, z)) * OreSampleMeters) + new Vector3D(OreSampleMeters * 0.5);
                            Vector3D worldPosition = Vector3D.Transform(storagePosition - storageHalf, voxelWorld);
                            refinedSamples.Add(CreateSample(worldPosition, materialName, minedOre, stone, true));
                            refinedOre++;
                        }

                        fineTested += fineContent.LongLength;
                    }

                    // Retain the coarse result as a fail-safe if an engine/mod LOD
                    // mismatch unexpectedly produces no fine ore samples.
                    if (refinedSamples.Count > 0)
                    {
                        map.OreSamples.Clear();
                        for (int i = 0; i < refinedSamples.Count; i++)
                            map.AddOre(refinedSamples[i]);
                    }
                }

                phase = "finalizing map";
                if (map.Samples.Count == 0)
                    throw new InvalidOperationException("Asteroid scan found no occupied surface samples.");
                map.OccupiedBox.Inflate(SampleMeters * 0.5);
                map.HighestOccupiedY += SampleMeters * 0.5;
                map.TestedSamples = coarseTested + fineTested;
                map.SkippedEmptySamples = (coarseTested - occupied) + (fineTested - fineOccupied);
                map.NonEmptyChunks = 1 + regions.Count;
                map.EmptyChunks = 0;
                map.ProcessedSamples = coarseTested + fineTested;
                map.Complete = true;
                Interlocked.Exchange(ref processedSamples, coarseTested + fineTested);
                phase = "complete";
                Emma3Log.Diagnostic("Scan complete asteroid=" + map.EntityId
                    + " coarseSamples=" + coarseTested
                    + " refineRegions=" + regions.Count
                    + " fineSamples=" + fineTested
                    + " empty=" + map.SkippedEmptySamples
                    + " occupied=" + occupied
                    + "+" + fineOccupied
                    + " surface=" + surface
                    + " coarseOre=" + ore
                    + " refinedOre=" + refinedOre
                    + " detected=" + FormatOreCounts(map)
                    + " elapsed=" + FormatDuration(Elapsed)
                    + " topY=" + map.HighestOccupiedY.ToString("0.0")
                    + ".");
            }
            catch (OperationCanceledException)
            {
                long asteroidId = map == null ? 0 : map.EntityId;
                map = null;
                workerException = null;
                phase = "cancelled";
                Emma3Log.Diagnostic("Discarded cancelled asteroid scan " + asteroidId + ".");
            }
            catch (Exception ex)
            {
                workerException = ex;
                string failedPhase = phase;
                phase = "failed: " + failedPhase;
                Emma3Log.Error("Background scan failed: asteroid=" + (map == null ? 0 : map.EntityId)
                    + " phase=" + failedPhase
                    + " size=" + scanSize
                    + " samples=" + totalSamples
                    + " rawArrays~=" + FormatBytes(estimatedRawBytes)
                    + " range=" + scanMin
                    + ".." + scanMax
                    + " elapsed=" + FormatDuration(Elapsed)
                    + "; " + ex);
            }
            finally
            {
                workerFinished = true;
            }
        }

        private static List<RefinementRegion> BuildRefinementRegions(MyVoxelBase voxel, List<Vector3I> coarseOreCells)
        {
            var result = new List<RefinementRegion>();
            if (coarseOreCells == null || coarseOreCells.Count == 0)
                return result;
            var remaining = new HashSet<Vector3I>(coarseOreCells);
            var queue = new Queue<Vector3I>();
            Vector3I fineStorageMin = voxel.StorageMin >> OreQueryLod;
            Vector3I fineStorageMax = (voxel.StorageMax >> OreQueryLod) - 1;
            while (remaining.Count > 0)
            {
                Vector3I root = Vector3I.Zero;
                foreach (Vector3I value in remaining)
                {
                    root = value;
                    break;
                }

                remaining.Remove(root);
                queue.Enqueue(root);
                Vector3I componentMin = root;
                Vector3I componentMax = root;
                while (queue.Count > 0)
                {
                    Vector3I current = queue.Dequeue();
                    componentMin = Vector3I.Min(componentMin, current);
                    componentMax = Vector3I.Max(componentMax, current);
                    for (int dz = -2; dz <= 2; dz++)
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                if (dx * dx + dy * dy + dz * dz > 4)
                                    continue; // within the existing 8 m join distance
                                Vector3I neighbor = current + new Vector3I(dx, dy, dz);
                                if (remaining.Remove(neighbor))
                                    queue.Enqueue(neighbor);
                            }
                }

                Vector3I halo = new Vector3I(OreRefineHaloCells);
                Vector3I fineMin = componentMin * 2 - halo;
                Vector3I fineMax = componentMax * 2 + Vector3I.One + halo;
                fineMin = Vector3I.Max(fineMin, fineStorageMin);
                fineMax = Vector3I.Min(fineMax, fineStorageMax);
                result.Add(new RefinementRegion { Min = fineMin, Max = fineMax });
            }

            // Halo boxes from distinct coarse components can still overlap
            // diagonally. Merge them so fine voxels are never counted twice.
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < result.Count && !merged; i++)
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        if (!RegionsOverlap(result[i], result[j]))
                            continue;
                        result[i].Min = Vector3I.Min(result[i].Min, result[j].Min);
                        result[i].Max = Vector3I.Max(result[i].Max, result[j].Max);
                        result.RemoveAt(j);
                        merged = true;
                        break;
                    }
            }
            while (merged);
            return result;
        }

        private static bool RegionsOverlap(RefinementRegion a, RefinementRegion b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        private static long RegionVolume(RefinementRegion region)
        {
            long x = Math.Max(0, (long)region.Max.X - region.Min.X + 1);
            long y = Math.Max(0, (long)region.Max.Y - region.Min.Y + 1);
            long z = Math.Max(0, (long)region.Max.Z - region.Min.Z + 1);
            return x * y * z;
        }

        private static bool IsSurface(byte[] content, Vector3I size, int plane, int index, int x, int y, int z)
        {
            return x == 0 || x == size.X - 1 || y == 0 || y == size.Y - 1 || z == 0 || z == size.Z - 1 || content[index - 1] <= IsoLevel || content[index + 1] <= IsoLevel || content[index - size.X] <= IsoLevel || content[index + size.X] <= IsoLevel || content[index - plane] <= IsoLevel || content[index + plane] <= IsoLevel;
        }

        private static MaterialSample CreateSample(Vector3D position, string material, string ore, bool stone, bool mineableOre)
        {
            return new MaterialSample
            {
                Position = position,
                Material = material ?? "",
                Ore = OreSelection.Normalize(ore),
                IsStone = stone,
                IsMineableOre = mineableOre
            };
        }

        private static bool IsStone(string material, string ore)
        {
            string a = material ?? "", b = ore ?? "";
            return a.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0 || b.Equals("Stone", StringComparison.OrdinalIgnoreCase) || b.Equals("Rock", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasContent(MyVoxelBase voxel, Vector3D point, double sampleSize)
        {
            double half = Math.Max(0.45, sampleSize * 0.35);
            BoundingBoxD query = new BoundingBoxD(point - new Vector3D(half), point + new Vector3D(half));
            return voxel.GetVoxelRangeTypeInBoundingBox(query) != MyVoxelContentConstitution.Empty;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1)
                return value.TotalHours.ToString("0.0") + "h";
            if (value.TotalMinutes >= 1)
                return value.TotalMinutes.ToString("0.0") + "m";
            return value.TotalSeconds.ToString("0.0") + "s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GiB";
            if (bytes >= 1024L * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.0") + " MiB";
            if (bytes >= 1024L)
                return (bytes / 1024.0).ToString("0.0") + " KiB";
            return bytes + " B";
        }

        private static string FormatOreCounts(AsteroidMap asteroid)
        {
            if (asteroid == null || asteroid.OreSamples.Count == 0)
                return "none";
            var values = new System.Collections.Generic.List<string>();
            foreach (var pair in asteroid.OreSamples)
                values.Add(pair.Key + "=" + pair.Value.Count);
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", values);
        }
    }
}
