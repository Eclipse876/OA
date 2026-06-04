// HexMapGenerator.cs:
// This is *supposed* to make the ocean map look intentional. Right now it
// rolls obstacles/rough water, smooths the spicy bits, and then cheats a little to make sure nothing is trapped.
using System.Collections.Generic;
using UnityEngine;

namespace OA.Simulation.Navigation
{
    // HexMapGenerator is the procedural map maker.
    // Seed goes in, navigable-ish hex soup comes out.
    public sealed class HexMapGenerator
    {
        // Rough water is passable, just expensive enough that paths prefer calmer tiles.
        private const float RoughMoveCost = 3.2f;
        private const int OffshoreDistanceSteps = 12;
        private const int OffshoreDepthStartDistance = 2;
        private const int FullOffshoreDepthDistance = 9;
        private const float AdjacentLandShallowChance = 0.1f;
        private const float NearCoastShallowChance = 0.025f;
        private const float OceanShallowChance = 0.0004f;
        private const float CoastalAroundFeatureChance = 0.92f;
        private const float CoastalShelfChance = 0.62f;
        private const float OffshoreDepthNoiseScale = 22f;
        private const float OffshoreDepthDetailScale = 9f;
        private const float VeryDeepDepthScore = 0.5f;
        private const float AbyssalDepthScore = 0.68f;
        private const float NearLandVeryDeepDepthScore = 0.84f;
        private const float NearLandAbyssalDepthScore = 0.94f;
        private const float ReefNoiseScale = 8f;
        private const float MinimumLandCoverage = 0f;
        private const float MaximumLandCoverage = 0.14f;

        // Reused scratch buffers so reachability checks do not keep allocating tiny garbage.
        private readonly List<Vector2Int> scratchFrontier = new List<Vector2Int>(2048);
        private readonly List<int> scratchIndexFrontier = new List<int>(2048);
        private readonly List<Vector2Int> scratchIslandFrontier = new List<Vector2Int>(128);
        private readonly HashSet<int> scratchVisited = new HashSet<int>();
        private readonly Vector2Int[] neighborBuffer = new Vector2Int[6];

        // Kicks off a deterministic map roll, then clears enough space for spawn/goal.
        public void Generate(
            HexMapRuntime map,
            int seed,
            float obstacleChance,
            float roughWaterChance,
            int smoothingPasses,
            Vector2Int guaranteedStart,
            Vector2Int guaranteedGoal)
        {
            if (map == null)
            {
                return;
            }

            // Clamp the designer-facing knobs before they can make an unusable map.
            System.Random random = new System.Random(seed);
            float clampedObstacleChance = Mathf.Clamp(
                obstacleChance,
                MinimumLandCoverage,
                MaximumLandCoverage);

            float clampedRoughChance = Mathf.Clamp(roughWaterChance, 0f, 0.85f);
            int clampedSmoothingPasses = Mathf.Clamp(smoothingPasses, 0, 8);

            // First pass: reset to ocean and roll rough-water costs from the seed.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    map.SetBlocked(x, y, false);
                    float cost = random.NextDouble() < clampedRoughChance ? RoughMoveCost : 1f;
                    map.SetMoveCost(x, y, cost);
                }
            }

            RollLandClusters(map, random, clampedObstacleChance);

            // Smoothing removes isolated seed dots and fills cramped land holes.
            for (int i = 0; i < clampedSmoothingPasses; i++)
            {
                ApplySmoothingPass(map);
            }

            ApplyTileClasses(map, random);

            // Always clear breathing room around the guaranteed endpoints, then carve if needed.
            // The carve intentionally stamps safe deep water for sandbox routing.
            ClearHexRadius(map, guaranteedStart, 2);
            ClearHexRadius(map, guaranteedGoal, 2);
            EnsureGuaranteedPath(map, guaranteedStart, guaranteedGoal);
        }

        // Runs one cellular-automata-ish cleanup pass over the blocked cells.
        private void ApplySmoothingPass(HexMapRuntime map)
        {
            bool[] nextBlocked = new bool[map.Width * map.Height];

            // Decide next blocked states without mutating the map mid-pass.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int blockedNeighbors = CountBlockedNeighbors(map, x, y);
                    bool current = map.IsBlocked(x, y);

                    nextBlocked[map.GetIndex(x, y)] = current
                        ? blockedNeighbors >= 1
                        : blockedNeighbors >= 5;
                }
            }

            // Apply the decided blocked state and keep passable cells at a valid movement cost.
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    bool blocked = nextBlocked[map.GetIndex(x, y)];
                    map.SetBlocked(x, y, blocked);

                    if (blocked)
                    {
                        map.SetMoveCost(x, y, 1f);
                    }
                    else
                    {
                        map.SetMoveCost(x, y, Mathf.Max(1f, map.GetMoveCost(x, y)));
                    }
                }
            }
        }

        private void RollLandClusters(
            HexMapRuntime map,
            System.Random random,
            float landCoverage)
        {
            int targetLandCells = Mathf.RoundToInt(
                map.Width * map.Height * landCoverage);

            if (targetLandCells <= 0)
            {
                return;
            }

            int placed = 0;
            int spacing = CalculateIslandSpacing(landCoverage);
            int attempts = 0;
            int maxAttempts = Mathf.Max(200, targetLandCells * 24);

            while (placed < targetLandCells && attempts < maxAttempts)
            {
                attempts++;

                int x = random.Next(map.Width);
                int y = random.Next(map.Height);

                if (map.IsBlocked(x, y) ||
                    HasBlockedWithinRadius(map, x, y, spacing))
                {
                    continue;
                }

                placed += GrowIslandCluster(
                    map,
                    x,
                    y,
                    Mathf.Min(RollIslandSize(random), targetLandCells - placed),
                    random);
            }

            attempts = 0;
            maxAttempts = Mathf.Max(200, (targetLandCells - placed) * 12);

            while (placed < targetLandCells && attempts < maxAttempts)
            {
                attempts++;

                int x = random.Next(map.Width);
                int y = random.Next(map.Height);

                if (map.IsBlocked(x, y))
                {
                    continue;
                }

                placed += GrowIslandCluster(
                    map,
                    x,
                    y,
                    Mathf.Min(RollIslandSize(random), targetLandCells - placed),
                    random);
            }
        }

        private int GrowIslandCluster(
            HexMapRuntime map,
            int startX,
            int startY,
            int targetCells,
            System.Random random)
        {
            if (targetCells <= 0)
            {
                return 0;
            }

            scratchIslandFrontier.Clear();
            scratchIslandFrontier.Add(new Vector2Int(startX, startY));
            PaintLandCell(map, startX, startY);

            int placed = 1;

            while (placed < targetCells && scratchIslandFrontier.Count > 0)
            {
                int frontierIndex = random.Next(scratchIslandFrontier.Count);
                Vector2Int current = scratchIslandFrontier[frontierIndex];
                int neighborCount = map.GetNeighborCount(
                    current.x,
                    current.y,
                    neighborBuffer);

                bool grew = false;
                float filledFraction = targetCells <= 1
                    ? 1f
                    : placed / (float)targetCells;

                float growChance = Mathf.Lerp(0.88f, 0.42f, filledFraction);
                int startNeighbor = random.Next(Mathf.Max(1, neighborCount));

                for (int i = 0; i < neighborCount && placed < targetCells; i++)
                {
                    Vector2Int next = neighborBuffer[(startNeighbor + i) % neighborCount];

                    if (map.IsBlocked(next.x, next.y) ||
                        random.NextDouble() > growChance)
                    {
                        continue;
                    }

                    PaintLandCell(map, next.x, next.y);
                    scratchIslandFrontier.Add(next);
                    placed++;
                    grew = true;
                }

                if (!grew)
                {
                    int last = scratchIslandFrontier.Count - 1;
                    scratchIslandFrontier[frontierIndex] = scratchIslandFrontier[last];
                    scratchIslandFrontier.RemoveAt(last);
                }
            }

            return placed;
        }

        private static void PaintLandCell(HexMapRuntime map, int x, int y)
        {
            map.SetBlocked(x, y, true);
            map.SetMoveCost(x, y, 1f);
        }

        private static int RollIslandSize(System.Random random)
        {
            double roll = random.NextDouble();

            if (roll < 0.08)
            {
                return 1;
            }

            if (roll < 0.28)
            {
                return random.Next(2, 8);
            }

            if (roll < 0.72)
            {
                return random.Next(8, 30);
            }

            if (roll < 0.94)
            {
                return random.Next(30, 90);
            }

            return random.Next(90, 180);
        }

        private static int CalculateIslandSpacing(float landCoverage)
        {
            float density = Mathf.InverseLerp(
                MinimumLandCoverage,
                MaximumLandCoverage,
                landCoverage);

            return Mathf.RoundToInt(Mathf.Lerp(13f, 5f, density));
        }

        private bool HasBlockedWithinRadius(
            HexMapRuntime map,
            int centerX,
            int centerY,
            int radius)
        {
            if (radius <= 0)
            {
                return false;
            }

            Vector2Int center = new Vector2Int(centerX, centerY);

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = new Vector2Int(centerX + x, centerY + y);
                    if (!map.InBounds(cell.x, cell.y))
                    {
                        continue;
                    }

                    if (HexMapRuntime.HexDistance(center, cell) > radius)
                    {
                        continue;
                    }

                    if (map.IsBlocked(cell.x, cell.y))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void FillNearestLandDistances(
            HexMapRuntime map,
            int[] nearestLandDistance,
            int maxDistance)
        {
            scratchIndexFrontier.Clear();

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);

                    if (map.IsBlocked(x, y))
                    {
                        nearestLandDistance[index] = 0;
                        scratchIndexFrontier.Add(index);
                    }
                    else
                    {
                        nearestLandDistance[index] = maxDistance + 1;
                    }
                }
            }

            int cursor = 0;
            while (cursor < scratchIndexFrontier.Count)
            {
                int index = scratchIndexFrontier[cursor++];
                int distance = nearestLandDistance[index];

                if (distance >= maxDistance)
                {
                    continue;
                }

                int x = index % map.Width;
                int y = index / map.Width;
                int neighborCount = map.GetNeighborCount(x, y, neighborBuffer);

                for (int i = 0; i < neighborCount; i++)
                {
                    Vector2Int n = neighborBuffer[i];
                    int neighborIndex = map.GetIndex(n.x, n.y);
                    int neighborDistance = distance + 1;

                    if (nearestLandDistance[neighborIndex] <= neighborDistance)
                    {
                        continue;
                    }

                    nearestLandDistance[neighborIndex] = neighborDistance;
                    scratchIndexFrontier.Add(neighborIndex);
                }
            }
        }

        private void ApplyTileClasses(HexMapRuntime map, System.Random random)
        {
            WaterDepthClass[] nextDepth = new WaterDepthClass[map.Width * map.Height];
            LandElevationClass[] nextLandElevation =
                new LandElevationClass[map.Width * map.Height];
            int[] nearestLandDistance = new int[map.Width * map.Height];
            float offshoreNoiseX = (float)random.NextDouble() * 4096f;
            float offshoreNoiseY = (float)random.NextDouble() * 4096f;
            float reefNoiseX = (float)random.NextDouble() * 4096f;
            float reefNoiseY = (float)random.NextDouble() * 4096f;
            FillNearestLandDistances(
                map,
                nearestLandDistance,
                OffshoreDistanceSteps);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);
                    nextLandElevation[index] = LandElevationClass.Land;

                    if (map.IsBlocked(x, y))
                    {
                        nextDepth[index] = WaterDepthClass.Shallow;
                        nextLandElevation[index] = RollLandElevation(map, x, y, random);
                        nearestLandDistance[index] = 0;
                    }
                    else 
                    {
                        int landDistance = nearestLandDistance[index];

                        nextDepth[index] = RollShallowWater(
                                landDistance,
                                x,
                                y,
                                reefNoiseX,
                                reefNoiseY,
                                random)
                            ? WaterDepthClass.Shallow
                            : WaterDepthClass.Deep;
                    }
                }
            }

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);

                    if (map.IsBlocked(x, y) ||
                        nextDepth[index] == WaterDepthClass.Shallow)
                    {
                        continue;
                    }

                    int landDistance = nearestLandDistance[index];
                    bool touchesLandOrShallow =
                        HasBlockedNeighbor(map, x, y) ||
                        HasNeighborDepth(map, nextDepth, x, y, WaterDepthClass.Shallow);

                    if (touchesLandOrShallow &&
                        random.NextDouble() < CoastalAroundFeatureChance)
                    {
                        nextDepth[index] = WaterDepthClass.Coastal;
                    }
                    else if (landDistance <= 2 &&
                             random.NextDouble() < CoastalShelfChance)
                    {
                        nextDepth[index] = WaterDepthClass.Coastal;
                    }
                    else if (landDistance > 2)
                    {
                        nextDepth[index] = SampleOffshoreDepth(
                            x,
                            y,
                            landDistance,
                            offshoreNoiseX,
                            offshoreNoiseY);
                    }
                }
            }

            ReduceIsolatedWaterDepths(map, nextDepth);
            SmoothLandElevations(map, nextLandElevation);

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int index = map.GetIndex(x, y);
                    map.SetDepthClass(x, y, nextDepth[index]);
                    map.SetLandElevationClass(x, y, nextLandElevation[index]);
                }
            }
        }

        private static bool RollShallowWater(
            int nearestLandDistance,
            int x,
            int y,
            float reefNoiseX,
            float reefNoiseY,
            System.Random random)
        {
            double roll = random.NextDouble();

            if (nearestLandDistance == 1)
            {
                return roll < AdjacentLandShallowChance;
            }

            if (nearestLandDistance <= 2)
            {
                return roll < NearCoastShallowChance;
            }

            float reefNoise = Mathf.PerlinNoise(
                (x + reefNoiseX) / ReefNoiseScale,
                (y + reefNoiseY) / ReefNoiseScale);

            return reefNoise > 0.88f ||
                   roll < OceanShallowChance;
        }

        private static WaterDepthClass SampleOffshoreDepth(
            int x,
            int y,
            int nearestLandDistance,
            float offshoreNoiseX,
            float offshoreNoiseY)
        {
            float broad = Mathf.PerlinNoise(
                (x + offshoreNoiseX) / OffshoreDepthNoiseScale,
                (y + offshoreNoiseY) / OffshoreDepthNoiseScale);
            float detail = Mathf.PerlinNoise(
                (x + offshoreNoiseX * 0.73f) / OffshoreDepthDetailScale,
                (y + offshoreNoiseY * 0.73f) / OffshoreDepthDetailScale);
            float depthScore = broad * 0.82f + detail * 0.18f;
            float offshoreFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    OffshoreDepthStartDistance,
                    FullOffshoreDepthDistance,
                    nearestLandDistance));
            float abyssalThreshold = Mathf.Lerp(
                NearLandAbyssalDepthScore,
                AbyssalDepthScore,
                offshoreFactor);
            float veryDeepThreshold = Mathf.Lerp(
                NearLandVeryDeepDepthScore,
                VeryDeepDepthScore,
                offshoreFactor);

            if (depthScore >= abyssalThreshold)
            {
                return WaterDepthClass.Abyssal;
            }

            if (depthScore >= veryDeepThreshold)
            {
                return WaterDepthClass.VeryDeep;
            }

            return WaterDepthClass.Deep;
        }

        private LandElevationClass RollLandElevation(
            HexMapRuntime map,
            int x,
            int y,
            System.Random random)
        {
            int blockedNeighbors = CountBlockedNeighbors(map, x, y);
            float interiorFactor = Mathf.InverseLerp(1f, 6f, blockedNeighbors);
            float variation = (float)random.NextDouble();
            float elevationScore = 0.16f + interiorFactor * 0.58f + variation * 0.32f;

            if (blockedNeighbors <= 1)
            {
                elevationScore *= 0.68f;
            }
            else if (blockedNeighbors <= 2)
            {
                elevationScore *= 0.82f;
            }

            if (elevationScore >= 0.93f)
            {
                return LandElevationClass.Peak;
            }

            if (elevationScore >= 0.78f)
            {
                return LandElevationClass.Mountain;
            }

            if (elevationScore >= 0.62f)
            {
                return LandElevationClass.LargeHill;
            }

            return elevationScore >= 0.42f
                ? LandElevationClass.Hill
                : LandElevationClass.Land;
        }

        private void SmoothLandElevations(
            HexMapRuntime map,
            LandElevationClass[] elevationClasses)
        {
            const int passes = 2;

            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        if (!map.IsBlocked(x, y))
                        {
                            continue;
                        }

                        int index = map.GetIndex(x, y);
                        int weightedSum = (int)elevationClasses[index] * 2;
                        int samples = 2;
                        int neighborCount = map.GetNeighborCount(x, y, neighborBuffer);

                        for (int i = 0; i < neighborCount; i++)
                        {
                            Vector2Int n = neighborBuffer[i];
                            if (!map.IsBlocked(n.x, n.y))
                            {
                                continue;
                            }

                            weightedSum += (int)elevationClasses[map.GetIndex(n.x, n.y)];
                            samples++;
                        }

                        int smoothed = Mathf.RoundToInt(weightedSum / (float)samples);
                        elevationClasses[index] = (LandElevationClass)Mathf.Clamp(
                            smoothed,
                            (int)LandElevationClass.Land,
                            (int)LandElevationClass.Peak);
                    }
                }
            }
        }

        private void ReduceIsolatedWaterDepths(
            HexMapRuntime map,
            WaterDepthClass[] depthClasses)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.IsBlocked(x, y))
                    {
                        continue;
                    }

                    int index = map.GetIndex(x, y);
                    WaterDepthClass depthClass = depthClasses[index];
                    bool reefFeature = depthClass == WaterDepthClass.Shallow;
                    bool deepFeature = depthClass == WaterDepthClass.VeryDeep ||
                                       depthClass == WaterDepthClass.Abyssal;

                    if (!reefFeature && !deepFeature)
                    {
                        continue;
                    }

                    int matchingNeighbors = CountNeighborDepth(
                        map,
                        depthClasses,
                        x,
                        y,
                        depthClass);

                    if (reefFeature &&
                        (matchingNeighbors > 0 || HasBlockedNeighbor(map, x, y)))
                    {
                        continue;
                    }

                    if (deepFeature && matchingNeighbors > 1)
                    {
                        continue;
                    }

                    WaterDepthClass replacement = GetDominantNeighborWaterDepth(
                        map,
                        depthClasses,
                        x,
                        y);

                    if (replacement == WaterDepthClass.Shallow &&
                        !HasBlockedNeighbor(map, x, y))
                    {
                        replacement = WaterDepthClass.Deep;
                    }

                    depthClasses[index] = replacement == depthClass
                        ? WaterDepthClass.Deep
                        : replacement;
                }
            }
        }

        // Counts blocked neighbors around one hex so smoothing knows whether it should flip.
        private int CountBlockedNeighbors(HexMapRuntime map, int centerX, int centerY)
        {
            int count = 0;
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (map.IsBlocked(n.x, n.y))
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasBlockedNeighbor(HexMapRuntime map, int centerX, int centerY)
        {
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);
        
            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (map.IsBlocked(n.x, n.y))
                {
                    return true;
                }
            }
            
            return false;
        }

        private bool HasNeighborDepth(
            HexMapRuntime map,
            WaterDepthClass[] depthClasses,
            int centerX,
            int centerY,
            WaterDepthClass depthClass)
        {
            return CountNeighborDepth(
                map,
                depthClasses,
                centerX,
                centerY,
                depthClass) > 0;
        }

        private int CountNeighborDepth(
            HexMapRuntime map,
            WaterDepthClass[] depthClasses,
            int centerX,
            int centerY,
            WaterDepthClass depthClass)
        {
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);
            int count = 0;

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (depthClasses[map.GetIndex(n.x, n.y)] == depthClass)
                {
                    count++;
                }
            }

            return count;
        }

        private WaterDepthClass GetDominantNeighborWaterDepth(
            HexMapRuntime map,
            WaterDepthClass[] depthClasses,
            int centerX,
            int centerY)
        {
            int shallowCount = 0;
            int deepCount = 0;
            int coastalCount = 0;
            int veryDeepCount = 0;
            int abyssalCount = 0;
            int neighborCount = map.GetNeighborCount(centerX, centerY, neighborBuffer);

            for (int i = 0; i < neighborCount; i++)
            {
                Vector2Int n = neighborBuffer[i];
                if (map.IsBlocked(n.x, n.y))
                {
                    continue;
                }

                switch (depthClasses[map.GetIndex(n.x, n.y)])
                {
                    case WaterDepthClass.Shallow:
                        shallowCount++;
                        break;
                    case WaterDepthClass.Coastal:
                        coastalCount++;
                        break;
                    case WaterDepthClass.VeryDeep:
                        veryDeepCount++;
                        break;
                    case WaterDepthClass.Abyssal:
                        abyssalCount++;
                        break;
                    default:
                        deepCount++;
                        break;
                }
            }

            WaterDepthClass bestDepth = WaterDepthClass.Deep;
            int bestCount = deepCount;

            SelectDominantDepth(WaterDepthClass.Shallow, shallowCount, ref bestDepth, ref bestCount);
            SelectDominantDepth(WaterDepthClass.Coastal, coastalCount, ref bestDepth, ref bestCount);
            SelectDominantDepth(WaterDepthClass.VeryDeep, veryDeepCount, ref bestDepth, ref bestCount);
            SelectDominantDepth(WaterDepthClass.Abyssal, abyssalCount, ref bestDepth, ref bestCount);

            return bestDepth;
        }

        private static void SelectDominantDepth(
            WaterDepthClass candidate,
            int candidateCount,
            ref WaterDepthClass bestDepth,
            ref int bestCount)
        {
            if (candidateCount > bestCount)
            {
                bestDepth = candidate;
                bestCount = candidateCount;
            }
        }

        // Clears a small hex-shaped patch around important cells like spawn and goal.
        private static void ClearHexRadius(HexMapRuntime map, Vector2Int center, int radius)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = new Vector2Int(center.x + x, center.y + y);
                    if (!map.InBounds(cell.x, cell.y))
                    {
                        continue;
                    }

                    if (HexMapRuntime.HexDistance(center, cell) > radius)
                    {
                        continue;
                    }

                    map.SetBlocked(cell.x, cell.y, false);
                    map.SetMoveCost(cell.x, cell.y, 1f);
                    map.SetDepthClass(cell.x, cell.y, WaterDepthClass.Deep);
                    map.SetLandElevationClass(cell.x, cell.y, LandElevationClass.Land);
                }
            }
        }

        // Makes sure generation did not accidentally split start and goal into separate oceans.
        private void EnsureGuaranteedPath(HexMapRuntime map, Vector2Int start, Vector2Int goal)
        {
            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
            {
                return;
            }

            if (IsReachable(map, start, goal))
            {
                return;
            }

            // If BFS says the map is split, carve a simple hex-line corridor between endpoints.
            Vector2Int? previous = null;

            foreach (Vector2Int cell in HexLine(start, goal))
            {
                if (previous.HasValue && previous.Value == cell)
                {
                    continue;
                }

                previous = cell;

                if (!map.InBounds(cell.x, cell.y))
                {
                    continue;
                }

                map.SetBlocked(cell.x, cell.y, false);
                map.SetMoveCost(cell.x, cell.y, 1f);
                ClearHexRadius(map, cell, 1);
            }
        }

        // Breadth-first search used as a quick "can these two cells connect?" check.
        private bool IsReachable(HexMapRuntime map, Vector2Int start, Vector2Int goal)
        {
            scratchFrontier.Clear();
            scratchVisited.Clear();

            scratchFrontier.Add(start);
            scratchVisited.Add(map.GetIndex(start.x, start.y));

            // Cursor-style BFS over a list keeps the queue simple and allocation-light.
            int cursor = 0;
            while (cursor < scratchFrontier.Count)
            {
                Vector2Int current = scratchFrontier[cursor++];

                if (current == goal)
                {
                    return true;
                }

                int neighbors = map.GetNeighborCount(current.x, current.y, neighborBuffer);
                for (int i = 0; i < neighbors; i++)
                {
                    Vector2Int next = neighborBuffer[i];

                    if (!map.IsWalkable(next.x, next.y))
                    {
                        continue;
                    }

                    int index = map.GetIndex(next.x, next.y);
                    if (!scratchVisited.Add(index))
                    {
                        continue;
                    }

                    scratchFrontier.Add(next);
                }
            }

            return false;
        }

        // Walks a straight-ish line between two hex cells using cube-coordinate interpolation.
        private static IEnumerable<Vector2Int> HexLine(Vector2Int a, Vector2Int b)
        {
            HexMapRuntime.ToCube(a, out int ax, out int ay, out int az);
            HexMapRuntime.ToCube(b, out int bx, out int by, out int bz);

            int steps = Mathf.Max(1, HexMapRuntime.HexDistance(a, b));

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = Mathf.Lerp(ax, bx, t);
                float y = Mathf.Lerp(ay, by, t);
                float z = Mathf.Lerp(az, bz, t);

                CubeRound(x, y, z, out int rx, out int ry, out int rz);
                yield return HexMapRuntime.CubeToOffset(rx, rz);
            }
        }

        // Rounds cube coordinates while repairing the axis with the biggest rounding error.
        private static void CubeRound(float x, float y, float z, out int rx, out int ry, out int rz)
        {
            rx = Mathf.RoundToInt(x);
            ry = Mathf.RoundToInt(y);
            rz = Mathf.RoundToInt(z);

            float xDiff = Mathf.Abs(rx - x);
            float yDiff = Mathf.Abs(ry - y);
            float zDiff = Mathf.Abs(rz - z);

            if (xDiff > yDiff && xDiff > zDiff)
            {
                rx = -ry - rz;
            }
            else if (yDiff > zDiff)
            {
                ry = -rx - rz;
            }
            else
            {
                rz = -rx - ry;
            }
        }
    }
}
