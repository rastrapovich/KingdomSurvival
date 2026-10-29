using System;
using System.Collections.Generic;

// Канон v1.58 §9.9: генератор карты «Свободной игры». Работает на той же
// шестиугольной сетке, что и навигация (WorldMapHexGrid), и выдаёт
// GeneratedWorldMap — разметку клеток и линии/точки для рисунка.
//
// Порядок стадий: рельеф (высоты → холмы, горы, скалы) → вода (озёра в
// низинах; реки стекают от возвышенностей к краю карты или к озеру по
// «залитому» рельефу, поэтому никогда не застревают в ямах) → лес и болота
// (доли от суши) → деревни (у воды, на ровном, с минимальным расстоянием) →
// приметные места по правилам → дороги (минимальное остовное дерево между
// деревнями + петли + выходы к краю, поиск пути с ценой местности; мост или
// брод там, где дорога пересекает реку) и тропы к местам → поля вокруг
// деревень → слоты появления мест.
public static class WorldMapGenerator
{
    private const byte OpenGround = (byte)WorldMapGameplayTerrainType.OpenGround;
    private const byte Road = (byte)WorldMapGameplayTerrainType.Road;
    private const byte Field = (byte)WorldMapGameplayTerrainType.Field;
    private const byte Forest = (byte)WorldMapGameplayTerrainType.Forest;
    private const byte Water = (byte)WorldMapGameplayTerrainType.Water;
    private const byte Trail = (byte)WorldMapGameplayTerrainType.Trail;
    private const byte Swamp = (byte)WorldMapGameplayTerrainType.Swamp;
    private const byte Hills = (byte)WorldMapGameplayTerrainType.Hills;
    private const byte Mountains = (byte)WorldMapGameplayTerrainType.Mountains;
    private const byte Cliffs = (byte)WorldMapGameplayTerrainType.Cliffs;

    public static GeneratedWorldMap Generate(
        WorldMapGenerationSettings settings,
        int seed,
        WorldMapGenerationStage upTo = WorldMapGenerationStage.Complete)
    {
        WorldMapGenerationSettings safe = (settings ?? WorldMapGenerationSettings.CreateDefault()).Clone();
        safe.Sanitize();
        Builder builder = new Builder(safe, seed);
        return builder.Run(upTo);
    }

    // Отдельный поток случайности на стадию: правка одной стадии не сдвигает
    // результат других.
    public static int StreamSeed(int seed, string stream)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in stream)
                hash = (hash ^ c) * 16777619;
            return hash ^ (seed * 31 + 17);
        }
    }

    private sealed class Builder
    {
        private readonly WorldMapGenerationSettings settings;
        private readonly int seed;
        private readonly WorldMapHexGrid grid;
        private readonly int count;
        private readonly int[] neighbors;
        private readonly bool[] boundary;
        private readonly byte[] terrain;
        private readonly float[] elevation;
        private readonly float[] moisture;
        private readonly bool[] lake;
        private readonly int[] riverId;
        // Пролёты мостов по клеткам: суша до воды → суша после неё.
        private readonly List<(int Entry, int Exit)> bridgeSpans = new List<(int, int)>();
        private readonly bool[] protectedStart;
        private readonly GeneratedWorldMap result;
        private readonly int startCell;
        private float hillsThreshold;
        private int[] waterDistance;
        private float[] roadWander;

        public Builder(WorldMapGenerationSettings settings, int seed)
        {
            this.settings = settings;
            this.seed = seed;
            grid = new WorldMapHexGrid(settings.CanvasWidth, settings.CanvasHeight, settings.HexesAcross);
            count = grid.CellCount;
            neighbors = new int[count * 6];
            boundary = new bool[count];
            terrain = new byte[count];
            elevation = new float[count];
            moisture = new float[count];
            lake = new bool[count];
            riverId = new int[count];
            protectedStart = new bool[count];
            for (int i = 0; i < count; i++)
            {
                riverId[i] = -1;
                WorldMapHexCell cell = grid.CellAt(i);
                for (int d = 0; d < 6; d++)
                {
                    WorldMapHexCell next = cell.Neighbor(d);
                    int index = grid.IsInside(next) ? grid.IndexOf(next) : -1;
                    neighbors[i * 6 + d] = index;
                    if (index < 0)
                        boundary[i] = true;
                }
            }

            startCell = grid.IndexOf(grid.CellAtPercent(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent));
            WorldMapHexCell start = grid.CellAt(startCell);
            for (int i = 0; i < count; i++)
                protectedStart[i] = grid.CellAt(i).DistanceTo(start) <= settings.StartClearRadiusHexes;

            result = new GeneratedWorldMap
            {
                Seed = seed,
                CanvasWidth = grid.CanvasWidth,
                CanvasHeight = grid.CanvasHeight,
                HexesAcross = grid.HexesAcross,
                StartXPercent = WorldMapNavigation.CapitalXPercent,
                StartYPercent = WorldMapNavigation.CapitalYPercent,
                Elevation = elevation,
                Moisture = moisture
            };
        }

        public GeneratedWorldMap Run(WorldMapGenerationStage upTo)
        {
            BuildRelief();
            if (upTo > WorldMapGenerationStage.Relief)
                BuildWater();
            if (upTo > WorldMapGenerationStage.Water)
                BuildVegetation();
            if (upTo > WorldMapGenerationStage.Vegetation)
                BuildSettlements();
            if (upTo > WorldMapGenerationStage.Settlements)
                BuildLandmarks();
            if (upTo > WorldMapGenerationStage.Landmarks)
                BuildRoads();
            if (upTo > WorldMapGenerationStage.Roads)
                BuildFields();
            if (upTo > WorldMapGenerationStage.Fields)
                BuildSpawnSlots();

            result.CompletedStage = upTo;
            result.TerrainCells = new WorldMapTerrainLayer(grid, (byte[])terrain.Clone()).Encode();
            return result;
        }

        // ------------------------------------------------------------------
        // Рельеф
        // ------------------------------------------------------------------

        private void BuildRelief()
        {
            WorldMapNoise reliefNoise = new WorldMapNoise(StreamSeed(seed, "relief"));
            WorldMapNoise moistureNoise = new WorldMapNoise(StreamSeed(seed, "moisture"));
            double hexWidth = grid.HexWidth;
            double reliefScale = settings.ReliefScaleHexes * hexWidth;
            double moistureScale = settings.MoistureScaleHexes * hexWidth;

            for (int i = 0; i < count; i++)
            {
                grid.CellCenter(grid.CellAt(i), out double x, out double y);
                double smooth = reliefNoise.Fbm(x / reliefScale, y / reliefScale, settings.ReliefDetail, settings.ReliefRoughness) * 0.5 + 0.5;
                double ridged = reliefNoise.Ridged(x / reliefScale + 31.7, y / reliefScale - 12.3, settings.ReliefDetail, settings.ReliefRoughness);
                elevation[i] = (float)(smooth * (1.0 - settings.MountainRidges) + ridged * settings.MountainRidges);
                moisture[i] = (float)(moistureNoise.Fbm(x / moistureScale, y / moistureScale, 3, 0.5) * 0.5 + 0.5);
            }

            Normalize(elevation);
            Normalize(moisture);

            // Старт — на ровной земле: высоты вокруг него мягко тянутся к середине.
            WorldMapHexCell start = grid.CellAt(startCell);
            int falloff = settings.StartClearRadiusHexes + 3;
            for (int i = 0; i < count; i++)
            {
                int distance = grid.CellAt(i).DistanceTo(start);
                if (distance > falloff)
                    continue;
                float pull = distance <= settings.StartClearRadiusHexes ? 1f : 1f - (distance - settings.StartClearRadiusHexes) / (float)(falloff - settings.StartClearRadiusHexes + 1);
                elevation[i] = elevation[i] + (0.42f - elevation[i]) * pull;
            }

            float mountainThreshold = TopThreshold(elevation, null, settings.MountainsShare);
            hillsThreshold = TopThreshold(elevation, null, settings.MountainsShare + settings.HillsShare);

            List<int> mountainCells = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (protectedStart[i])
                    terrain[i] = OpenGround;
                else if (settings.MountainsShare > 0f && elevation[i] >= mountainThreshold)
                {
                    terrain[i] = Mountains;
                    mountainCells.Add(i);
                }
                else if (settings.HillsShare > 0f && elevation[i] >= hillsThreshold)
                    terrain[i] = Hills;
                else
                    terrain[i] = OpenGround;
            }

            // Скалы — самые крутые горные клетки.
            if (mountainCells.Count > 0 && settings.CliffsShare > 0f)
            {
                mountainCells.Sort((a, b) => Slope(b).CompareTo(Slope(a)));
                int cliffs = (int)Math.Round(mountainCells.Count * settings.CliffsShare);
                for (int i = 0; i < cliffs && i < mountainCells.Count; i++)
                    terrain[mountainCells[i]] = Cliffs;
            }
        }

        private float Slope(int cell)
        {
            float slope = 0f;
            for (int d = 0; d < 6; d++)
            {
                int next = neighbors[cell * 6 + d];
                if (next >= 0)
                    slope = Math.Max(slope, Math.Abs(elevation[cell] - elevation[next]));
            }
            return slope;
        }

        // ------------------------------------------------------------------
        // Вода: озёра и реки
        // ------------------------------------------------------------------

        private void BuildWater()
        {
            WorldMapNoise lakeNoise = new WorldMapNoise(StreamSeed(seed, "lakes"));
            double lakeScale = Math.Max(4.0, settings.ReliefScaleHexes * 0.45) * grid.HexWidth;
            float[] lakeScore = new float[count];
            bool[] eligible = new bool[count];
            for (int i = 0; i < count; i++)
            {
                eligible[i] = !protectedStart[i] && terrain[i] == OpenGround;
                grid.CellCenter(grid.CellAt(i), out double x, out double y);
                lakeScore[i] = (float)((1.0 - elevation[i]) * 0.65 + (lakeNoise.Fbm(x / lakeScale, y / lakeScale, 3, 0.5) * 0.5 + 0.5) * 0.35);
            }

            if (settings.LakesShare > 0f)
            {
                float threshold = TopThreshold(lakeScore, eligible, settings.LakesShare);
                for (int i = 0; i < count; i++)
                    lake[i] = eligible[i] && lakeScore[i] >= threshold;
                RemoveSmallComponents(lake, 3);
                for (int i = 0; i < count; i++)
                {
                    if (lake[i])
                        terrain[i] = Water;
                }
            }

            BuildRivers();
            waterDistance = DistanceField(i => terrain[i] == Water, 8);
        }

        private void BuildRivers()
        {
            if (settings.RiverCount <= 0)
                return;

            Random random = new Random(StreamSeed(seed, "rivers"));

            // «Залитый» рельеф: от краёв карты и озёр вверх; у каждой клетки
            // есть сосед строго ниже, и путь вниз всегда доходит до выхода.
            float[] filled = new float[count];
            bool[] done = new bool[count];
            MinHeap heap = new MinHeap(count);
            for (int i = 0; i < count; i++)
            {
                filled[i] = float.MaxValue;
                if (protectedStart[i] || terrain[i] == Cliffs)
                    continue;
                if (boundary[i] || lake[i])
                {
                    filled[i] = elevation[i];
                    heap.Push(i, filled[i]);
                }
            }

            while (heap.Count > 0)
            {
                int cell = heap.Pop();
                if (done[cell])
                    continue;
                done[cell] = true;
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[cell * 6 + d];
                    if (next < 0 || done[next] || protectedStart[next] || terrain[next] == Cliffs)
                        continue;
                    float value = Math.Max(elevation[next], filled[cell] + 0.0001f);
                    if (value < filled[next])
                    {
                        filled[next] = value;
                        heap.Push(next, value);
                    }
                }
            }

            List<int> sources = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (done[i] && !lake[i] && !boundary[i] && (terrain[i] == Hills || terrain[i] == Mountains || elevation[i] >= hillsThreshold * 0.92f))
                    sources.Add(i);
            }
            Shuffle(sources, random);
            sources.Sort((a, b) => elevation[b].CompareTo(elevation[a]));
            // Не только самые высокие — чуть перемешиваем верхнюю часть.
            int head = Math.Min(sources.Count, Math.Max(settings.RiverCount * 8, 16));
            List<int> pool = sources.GetRange(0, head);
            Shuffle(pool, random);

            int minSourceDistance = Math.Max(5, settings.RiverMinLengthHexes / 2);
            List<int> acceptedSources = new List<int>();
            int maxLength = 1;
            List<List<int>> paths = new List<List<int>>();
            List<int> merges = new List<int>();

            foreach (int source in pool)
            {
                if (paths.Count >= settings.RiverCount)
                    break;
                if (riverId[source] >= 0 || IsNear(source, acceptedSources, minSourceDistance))
                    continue;

                List<int> path = TraceRiver(source, filled, random, out bool merged);
                if (path.Count < settings.RiverMinLengthHexes)
                    continue;

                int id = paths.Count;
                foreach (int cell in path)
                {
                    if (riverId[cell] < 0)
                        riverId[cell] = id;
                    if (!lake[cell])
                        terrain[cell] = Water;
                }
                acceptedSources.Add(source);
                paths.Add(path);
                merges.Add(merged ? 1 : 0);
                maxLength = Math.Max(maxLength, path.Count);
            }

            for (int r = 0; r < paths.Count; r++)
            {
                List<int> path = paths[r];
                float lengthFactor = Math.Min(1f, path.Count / (float)Math.Max(1, maxLength));
                GeneratedMapLine line = new GeneratedMapLine
                {
                    Kind = GeneratedMapLine.RiverKind,
                    StartWidthHexes = settings.RiverWidthHexes * 0.35f,
                    EndWidthHexes = settings.RiverWidthHexes * (merges[r] == 1 ? 0.6f + 0.25f * lengthFactor : 0.7f + 0.3f * lengthFactor),
                    Cells = new List<int>(path)
                };
                List<double[]> points = CellPoints(path);
                ExtendOffMap(points, path);
                points = Chaikin(points, 2);
                points = Meander(points, random, settings.RiverMeander * 0.3 * grid.HexWidth);
                line.Points = ToPercent(points);
                result.Rivers.Add(line);
                foreach (int cell in path)
                {
                    if (!lake[cell])
                        result.RiverCells.Add(cell);
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (lake[i])
                    result.LakeCells.Add(i);
            }
        }

        private List<int> TraceRiver(int source, float[] filled, Random random, out bool merged)
        {
            merged = false;
            List<int> path = new List<int> { source };
            int current = source;
            List<int> lower = new List<int>(6);
            int guard = count;
            while (guard-- > 0)
            {
                if (path.Count > 1 && (lake[current] || boundary[current]))
                    break;
                if (path.Count > 1 && riverId[current] >= 0)
                {
                    merged = true;
                    break;
                }

                lower.Clear();
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[current * 6 + d];
                    if (next >= 0 && filled[next] < filled[current])
                        lower.Add(next);
                }
                if (lower.Count == 0)
                    break;

                lower.Sort((a, b) => filled[a].CompareTo(filled[b]));
                int choice = 0;
                if (lower.Count > 1 && random.NextDouble() < settings.RiverMeander * 0.55)
                    choice = 1 + random.Next(lower.Count - 1);
                current = lower[choice];
                path.Add(current);
            }
            return path;
        }

        // Река у края карты уходит за край, а не обрывается у рамки.
        private void ExtendOffMap(List<double[]> points, List<int> path)
        {
            if (points.Count < 2 || !boundary[path[path.Count - 1]])
                return;
            double[] last = points[points.Count - 1];
            double[] previous = points[points.Count - 2];
            points.Add(new[] { last[0] + (last[0] - previous[0]) * 1.5, last[1] + (last[1] - previous[1]) * 1.5 });
        }

        // ------------------------------------------------------------------
        // Лес и болота
        // ------------------------------------------------------------------

        private void BuildVegetation()
        {
            WorldMapNoise swampNoise = new WorldMapNoise(StreamSeed(seed, "swamp"));
            WorldMapNoise forestNoise = new WorldMapNoise(StreamSeed(seed, "forest"));
            double forestScale = settings.ForestScaleHexes * grid.HexWidth;
            double swampScale = Math.Max(3.0, settings.MoistureScaleHexes * 0.5) * grid.HexWidth;
            int land = 0;
            for (int i = 0; i < count; i++)
            {
                if (terrain[i] != Water)
                    land++;
            }

            if (settings.SwampShare > 0f)
            {
                float[] score = new float[count];
                bool[] eligible = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    eligible[i] = !protectedStart[i] && terrain[i] == OpenGround && elevation[i] < hillsThreshold;
                    grid.CellCenter(grid.CellAt(i), out double x, out double y);
                    float nearWater = waterDistance[i] <= 2 ? 0.2f : 0f;
                    score[i] = moisture[i] * 0.55f + (1f - elevation[i]) * 0.25f + nearWater +
                               (float)(swampNoise.Fbm(x / swampScale, y / swampScale, 2, 0.5) * 0.15);
                }
                SelectTop(score, eligible, (int)(land * settings.SwampShare), Swamp);
            }

            if (settings.ForestShare > 0f)
            {
                float[] score = new float[count];
                bool[] eligible = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    eligible[i] = !protectedStart[i] && (terrain[i] == OpenGround || terrain[i] == Hills);
                    grid.CellCenter(grid.CellAt(i), out double x, out double y);
                    float hillsPenalty = terrain[i] == Hills ? (1f - settings.ForestOnHills) * 0.6f : 0f;
                    score[i] = (float)(forestNoise.Fbm(x / forestScale, y / forestScale, 4, 0.55) * 0.5 + 0.5) +
                               moisture[i] * 0.2f - hillsPenalty;
                }
                SelectTop(score, eligible, (int)(land * settings.ForestShare), Forest);
            }
        }

        // ------------------------------------------------------------------
        // Деревни
        // ------------------------------------------------------------------

        private void BuildSettlements()
        {
            Random random = new Random(StreamSeed(seed, "villages"));
            bool[] reachable = Reachable();
            int edgeMargin = 2;

            List<int> candidates = new List<int>();
            List<float> scores = new List<float>();
            for (int i = 0; i < count; i++)
            {
                byte t = terrain[i];
                if (!reachable[i] || NearEdge(i, edgeMargin) || protectedStart[i])
                    continue;
                if (t != OpenGround && t != Forest && t != Hills && t != Field)
                    continue;
                int water = waterDistance[i];
                float waterBonus = water <= 1 ? 0.6f : water <= 3 ? 1f : water <= 5 ? 0.4f : 0f;
                float terrainBonus = t == OpenGround ? 0.3f : t == Hills ? -0.25f : 0f;
                candidates.Add(i);
                scores.Add((float)random.NextDouble() * 0.6f + settings.VillageNearWater * waterBonus + terrainBonus);
            }

            List<int> order = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                order.Add(i);
            order.Sort((a, b) => scores[b].CompareTo(scores[a]));

            List<int> villages = new List<int> { startCell };
            foreach (int index in order)
            {
                if (villages.Count >= settings.VillageCount)
                    break;
                int cell = candidates[index];
                if (!IsNear(cell, villages, settings.VillageMinDistanceHexes))
                    villages.Add(cell);
            }

            for (int v = 0; v < villages.Count; v++)
            {
                int cell = villages[v];
                ClearAround(cell, 1);
                grid.CellCenter(grid.CellAt(cell), out double x, out double y);
                GeneratedMapSite site = new GeneratedMapSite
                {
                    Kind = GeneratedMapSite.VillageKind,
                    Cell = cell,
                    XPercent = v == 0 ? WorldMapNavigation.CapitalXPercent : grid.PixelToPercentX(x),
                    YPercent = v == 0 ? WorldMapNavigation.CapitalYPercent : grid.PixelToPercentY(y),
                    Size = v == 0 ? 3 : 1 + random.Next(3),
                    Variant = random.Next(1 << 20),
                    IsStart = v == 0
                };
                result.Villages.Add(site);
            }
        }

        private void ClearAround(int cell, int radius)
        {
            foreach (int i in CellsWithin(cell, radius))
            {
                byte t = terrain[i];
                if (t == Forest || t == Swamp || t == Hills)
                    terrain[i] = OpenGround;
            }
            if (terrain[cell] != Water)
                terrain[cell] = OpenGround;
        }

        // ------------------------------------------------------------------
        // Приметные места
        // ------------------------------------------------------------------

        private void BuildLandmarks()
        {
            Random random = new Random(StreamSeed(seed, "landmarks"));
            List<int> villageCells = new List<int>();
            foreach (GeneratedMapSite village in result.Villages)
                villageCells.Add(village.Cell);
            List<int> taken = new List<int>();

            foreach (WorldMapLandmarkRule rule in settings.Landmarks)
            {
                if (rule == null || !rule.Enabled || rule.Count <= 0 || rule.Placement == WorldMapLandmarkPlacement.Crossroads)
                    continue;

                for (int n = 0; n < rule.Count; n++)
                {
                    List<int> candidates = new List<int>();
                    for (int i = 0; i < count; i++)
                    {
                        byte t = terrain[i];
                        if (t == Water || t == Cliffs || protectedStart[i] || NearEdge(i, 1))
                            continue;
                        if (rule.Terrains.Count > 0 && !rule.Terrains.Contains((WorldMapGameplayTerrainType)t))
                            continue;
                        if (rule.NearWater && waterDistance[i] > 2)
                            continue;
                        int villageDistance = NearestDistance(i, villageCells);
                        if (rule.NearVillage ? villageDistance > 4 || villageDistance < 1 : villageDistance < rule.MinDistanceHexes)
                            continue;
                        if (IsNear(i, taken, Math.Max(3, (int)(rule.MinDistanceHexes * 0.6f))))
                            continue;
                        candidates.Add(i);
                    }

                    if (candidates.Count == 0)
                        break;

                    int cell = candidates[random.Next(candidates.Count)];
                    taken.Add(cell);
                    grid.CellCenter(grid.CellAt(cell), out double x, out double y);
                    result.Landmarks.Add(Site(rule, cell, random));
                }
            }
        }

        private GeneratedMapSite Site(WorldMapLandmarkRule rule, int cell, Random random)
        {
            grid.CellCenter(grid.CellAt(cell), out double x, out double y);
            return new GeneratedMapSite
            {
                Kind = rule.Id ?? string.Empty,
                Name = rule.Name ?? string.Empty,
                Cell = cell,
                XPercent = grid.PixelToPercentX(x),
                YPercent = grid.PixelToPercentY(y),
                Variant = random.Next(1 << 20),
                DrawMark = rule.DrawMark,
                LocationId = rule.LocationId ?? string.Empty,
                Hidden = rule.Hidden,
                Description = rule.Description ?? string.Empty,
                Threat = string.IsNullOrWhiteSpace(rule.Threat) ? "неизвестна" : rule.Threat,
                ExplorationHours = Math.Max(0.0, rule.ExplorationHours),
                RewardArmySupply = Math.Max(0, rule.RewardArmySupply),
                RewardArmyGold = Math.Max(0, rule.RewardArmyGold)
            };
        }

        // Перекрёсток — клетка дороги, где сходятся три дороги и больше,
        // в стороне от деревень и других мест.
        private void PlaceCrossroads(Random random)
        {
            List<int> villageCells = new List<int>();
            foreach (GeneratedMapSite village in result.Villages)
                villageCells.Add(village.Cell);

            foreach (WorldMapLandmarkRule rule in settings.Landmarks)
            {
                if (rule == null || !rule.Enabled || rule.Count <= 0 || rule.Placement != WorldMapLandmarkPlacement.Crossroads)
                    continue;

                List<int> candidates = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (terrain[i] != Road || riverId[i] >= 0 || NearEdge(i, 2))
                        continue;
                    int roads = 0;
                    for (int d = 0; d < 6; d++)
                    {
                        int next = neighbors[i * 6 + d];
                        if (next >= 0 && terrain[next] == Road)
                            roads++;
                    }
                    if (roads >= 3 && NearestDistance(i, villageCells) >= 3)
                        candidates.Add(i);
                }
                Shuffle(candidates, random);

                List<int> taken = new List<int>();
                foreach (GeneratedMapSite landmark in result.Landmarks)
                    taken.Add(landmark.Cell);
                int placed = 0;
                foreach (int cell in candidates)
                {
                    if (placed >= rule.Count)
                        break;
                    if (IsNear(cell, taken, Math.Max(3, (int)rule.MinDistanceHexes)))
                        continue;
                    taken.Add(cell);
                    result.Landmarks.Add(Site(rule, cell, random));
                    placed++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Дороги и тропы
        // ------------------------------------------------------------------

        private void BuildRoads()
        {
            Random random = new Random(StreamSeed(seed, "roads"));
            List<GeneratedMapSite> villages = result.Villages;
            HashSet<int> bridgeCells = new HashSet<int>();
            WorldMapNoise wanderNoise = new WorldMapNoise(StreamSeed(seed, "road-wander"));
            double wanderScale = 3.5 * grid.HexWidth;
            roadWander = new float[count];
            for (int i = 0; i < count; i++)
            {
                grid.CellCenter(grid.CellAt(i), out double wx, out double wy);
                roadWander[i] = 1f + settings.RoadWander * (float)(wanderNoise.Fbm(wx / wanderScale, wy / wanderScale, 2, 0.5) * 0.5 + 0.5);
            }

            // Остовное дерево (Прим) по расстоянию в клетках.
            int n = villages.Count;
            List<(int A, int B)> edges = new List<(int, int)>();
            if (n > 1)
            {
                bool[] inTree = new bool[n];
                inTree[0] = true;
                for (int added = 1; added < n; added++)
                {
                    int bestA = -1, bestB = -1, best = int.MaxValue;
                    for (int a = 0; a < n; a++)
                    {
                        if (!inTree[a])
                            continue;
                        for (int b = 0; b < n; b++)
                        {
                            if (inTree[b])
                                continue;
                            int distance = Distance(villages[a].Cell, villages[b].Cell);
                            if (distance < best)
                            {
                                best = distance;
                                bestA = a;
                                bestB = b;
                            }
                        }
                    }
                    inTree[bestB] = true;
                    edges.Add((bestA, bestB));
                }

                // Петли: короткие пары, которых нет в дереве.
                List<(int A, int B, int D)> extra = new List<(int, int, int)>();
                for (int a = 0; a < n; a++)
                {
                    for (int b = a + 1; b < n; b++)
                    {
                        if (edges.Contains((a, b)) || edges.Contains((b, a)))
                            continue;
                        int distance = Distance(villages[a].Cell, villages[b].Cell);
                        if (distance <= settings.VillageMinDistanceHexes * 2.4f)
                            extra.Add((a, b, distance));
                    }
                }
                extra.Sort((x, y) => x.D.CompareTo(y.D));
                int loops = (int)Math.Round(n * settings.ExtraRoadLoops);
                for (int i = 0; i < loops && i < extra.Count; i++)
                    edges.Add((extra[i].A, extra[i].B));
            }

            foreach ((int a, int b) in edges)
            {
                List<int> path = FindPath(villages[a].Cell, new HashSet<int> { villages[b].Cell }, false);
                ApplyPath(path, false, bridgeCells);
            }

            // Выходы к краю карты: дороги «куда-то дальше».
            if (n > 0 && settings.RoadsToEdge > 0)
            {
                List<int> sides = new List<int> { 0, 1, 2, 3 };
                Shuffle(sides, random);
                for (int k = 0; k < settings.RoadsToEdge; k++)
                {
                    int side = sides[k % 4];
                    HashSet<int> targets = EdgeTargets(side, random);
                    if (targets.Count == 0)
                        continue;
                    int from = NearestVillageToSide(side);
                    List<int> path = FindPath(villages[from].Cell, targets, false);
                    ApplyPath(path, false, bridgeCells);
                }
            }

            PlaceCrossroads(random);

            if (settings.TrailsToLandmarks)
            {
                HashSet<int> roadCells = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    if (terrain[i] == Road)
                        roadCells.Add(i);
                }

                foreach (GeneratedMapSite landmark in result.Landmarks)
                {
                    if (roadCells.Count == 0 || roadCells.Contains(landmark.Cell))
                        continue;
                    List<int> path = FindPath(landmark.Cell, roadCells, true);
                    path.Reverse();
                    ApplyPath(path, true, bridgeCells);
                }
            }

            foreach (int cell in bridgeCells)
            {
                grid.CellCenter(grid.CellAt(cell), out double x, out double y);
                result.Bridges.Add(new MapPointData(grid.PixelToPercentX(x), grid.PixelToPercentY(y)));
            }
            foreach ((int entry, int exit) in bridgeSpans)
            {
                grid.CellCenter(grid.CellAt(entry), out double ax, out double ay);
                grid.CellCenter(grid.CellAt(exit), out double bx, out double by);
                result.BridgeSpans.Add(new MapPointData(grid.PixelToPercentX(ax), grid.PixelToPercentY(ay)));
                result.BridgeSpans.Add(new MapPointData(grid.PixelToPercentX(bx), grid.PixelToPercentY(by)));
            }
        }

        // Плавный шум цены пути: дороги петляют, а не идут по осям сетки.
        private float StepCost(int cell, bool trail)
        {
            float cost = BaseStepCost(cell, trail);
            byte t = terrain[cell];
            return roadWander == null || t == Road || t == Trail ? cost : cost * roadWander[cell];
        }

        private float BaseStepCost(int cell, bool trail)
        {
            byte t = terrain[cell];
            if (lake[cell] || t == Cliffs)
                return float.PositiveInfinity;
            if (riverId[cell] >= 0)
                return t == Road || t == Trail ? 0.6f : settings.RiverCrossingCost * (trail ? 0.8f : 1f);
            switch (t)
            {
                case Road: return trail ? 0.5f : 0.35f;
                case Trail: return trail ? 0.6f : 0.8f;
                case Hills: return trail ? 1f + (settings.RoadAvoidHills - 1f) * 0.6f : settings.RoadAvoidHills;
                case Mountains: return trail ? 5f : 8f;
                case Forest: return trail ? 1.2f : settings.RoadAvoidForest;
                case Swamp: return trail ? settings.RoadAvoidSwamp * 0.6f : settings.RoadAvoidSwamp;
                default: return 1f;
            }
        }

        // A* по клеткам до любой из целей.
        private List<int> FindPath(int from, HashSet<int> targets, bool trail)
        {
            List<int> path = new List<int>();
            if (targets.Count == 0)
                return path;
            float[] cost = new float[count];
            int[] parent = new int[count];
            for (int i = 0; i < count; i++)
            {
                cost[i] = float.PositiveInfinity;
                parent[i] = -1;
            }

            List<WorldMapHexCell> targetCells = new List<WorldMapHexCell>();
            foreach (int target in targets)
                targetCells.Add(grid.CellAt(target));
            bool singleTarget = targetCells.Count == 1;
            float minStep = trail ? 0.5f : 0.35f;

            MinHeap heap = new MinHeap(count);
            cost[from] = 0f;
            heap.Push(from, 0f);
            int found = -1;
            while (heap.Count > 0)
            {
                int cell = heap.Pop();
                if (targets.Contains(cell))
                {
                    found = cell;
                    break;
                }
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[cell * 6 + d];
                    if (next < 0)
                        continue;
                    float step = StepCost(next, trail);
                    if (float.IsInfinity(step))
                        continue;
                    float value = cost[cell] + step;
                    if (value < cost[next])
                    {
                        cost[next] = value;
                        parent[next] = cell;
                        float heuristic = singleTarget ? grid.CellAt(next).DistanceTo(targetCells[0]) * minStep : 0f;
                        heap.Push(next, value + heuristic);
                    }
                }
            }

            if (found < 0)
                return path;
            for (int cell = found; cell >= 0; cell = parent[cell])
                path.Add(cell);
            path.Reverse();
            return path;
        }

        private void ApplyPath(List<int> path, bool trail, HashSet<int> bridgeCells)
        {
            if (path.Count < 2)
                return;

            // На рисунок идут только новые участки, продлённые на клетку к
            // уже существующей дороге, чтобы линии смыкались.
            List<int> run = new List<int>();
            for (int i = 0; i < path.Count; i++)
            {
                int cell = path[i];
                bool existing = terrain[cell] == Road || (trail && terrain[cell] == Trail);
                if (!existing)
                {
                    if (run.Count == 0 && i > 0)
                        run.Add(path[i - 1]);
                    run.Add(cell);
                }
                else if (run.Count > 0)
                {
                    run.Add(cell);
                    AddRoadLine(run, trail);
                    run = new List<int>();
                }
            }
            if (run.Count > 0)
                AddRoadLine(run, trail);

            // Бриф правок п. 13: одна переправа — один пролёт от берега до
            // берега (до того, как клетки воды станут дорогой).
            int runStart = -1;
            for (int i = 0; i < path.Count; i++)
            {
                bool water = riverId[path[i]] >= 0 && terrain[path[i]] == Water;
                if (water && runStart < 0)
                    runStart = i;
                else if (!water && runStart >= 0)
                {
                    if (runStart > 0)
                        bridgeSpans.Add((path[runStart - 1], path[i]));
                    runStart = -1;
                }
            }

            foreach (int cell in path)
            {
                if (riverId[cell] >= 0 && terrain[cell] == Water)
                    bridgeCells.Add(cell);
                if (trail)
                {
                    if (terrain[cell] != Road)
                        terrain[cell] = Trail;
                }
                else
                    terrain[cell] = Road;
            }
        }

        private void AddRoadLine(List<int> cells, bool trail)
        {
            if (cells.Count < 2)
                return;
            List<double[]> points = Chaikin(CellPoints(cells), 3);
            points = Meander(points, new Random(StreamSeed(seed, "road-bend") ^ cells[0]), settings.RoadWander * 0.12 * grid.HexWidth);
            result.Roads.Add(new GeneratedMapLine
            {
                Kind = trail ? GeneratedMapLine.TrailKind : GeneratedMapLine.RoadKind,
                StartWidthHexes = trail ? 0.6f : 1f,
                EndWidthHexes = trail ? 0.6f : 1f,
                Cells = new List<int>(cells),
                Points = ToPercent(points)
            });
        }

        // Стороны: 0 — запад, 1 — восток, 2 — север, 3 — юг.
        private HashSet<int> EdgeTargets(int side, Random random)
        {
            List<int> cells = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (!boundary[i] || terrain[i] == Water || terrain[i] == Cliffs || terrain[i] == Mountains)
                    continue;
                WorldMapHexCell cell = grid.CellAt(i);
                bool match = side == 0 ? cell.Column == 0
                    : side == 1 ? cell.Column == grid.Columns - 1
                    : side == 2 ? cell.Row == 0
                    : cell.Row == grid.Rows - 1;
                if (match)
                    cells.Add(i);
            }
            HashSet<int> targets = new HashSet<int>();
            if (cells.Count == 0)
                return targets;
            // Средняя часть стороны, а не угол.
            int pick = cells[(int)(cells.Count * (0.25 + random.NextDouble() * 0.5))];
            targets.Add(pick);
            return targets;
        }

        private int NearestVillageToSide(int side)
        {
            int best = 0;
            double bestValue = double.MaxValue;
            for (int v = 0; v < result.Villages.Count; v++)
            {
                GeneratedMapSite village = result.Villages[v];
                double value = side == 0 ? village.XPercent
                    : side == 1 ? 100.0 - village.XPercent
                    : side == 2 ? village.YPercent
                    : 100.0 - village.YPercent;
                if (value < bestValue)
                {
                    bestValue = value;
                    best = v;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Поля
        // ------------------------------------------------------------------

        private void BuildFields()
        {
            if (settings.FieldRadiusHexes <= 0f || settings.FieldDensity <= 0f)
                return;
            WorldMapNoise fieldNoise = new WorldMapNoise(StreamSeed(seed, "fields"));
            double scale = 2.2 * grid.HexWidth;
            int radius = (int)Math.Ceiling(settings.FieldRadiusHexes);
            foreach (GeneratedMapSite village in result.Villages)
            {
                foreach (int i in CellsWithin(village.Cell, radius))
                {
                    if (i == village.Cell || terrain[i] != OpenGround)
                        continue;
                    if (Distance(i, village.Cell) > settings.FieldRadiusHexes)
                        continue;
                    grid.CellCenter(grid.CellAt(i), out double x, out double y);
                    double value = fieldNoise.Fbm(x / scale, y / scale, 2, 0.5) * 0.5 + 0.5;
                    if (value > 1.0 - settings.FieldDensity)
                        terrain[i] = Field;
                }
            }
        }

        // ------------------------------------------------------------------
        // Слоты появления мест
        // ------------------------------------------------------------------

        private void BuildSpawnSlots()
        {
            Random random = new Random(StreamSeed(seed, "slots"));
            bool[] reachable = Reachable();
            Dictionary<string, int> ruleIndex = new Dictionary<string, int>();
            Dictionary<string, WorldMapLandmarkRule> rules = new Dictionary<string, WorldMapLandmarkRule>();
            foreach (WorldMapLandmarkRule rule in settings.Landmarks)
            {
                if (rule != null && !string.IsNullOrEmpty(rule.Id) && !rules.ContainsKey(rule.Id))
                    rules[rule.Id] = rule;
            }

            // Сначала приметные места: у мест без требований слоты берутся по
            // кругу, и первыми должны идти именно они.
            foreach (GeneratedMapSite landmark in result.Landmarks)
            {
                ruleIndex.TryGetValue(landmark.Kind, out int index);
                ruleIndex[landmark.Kind] = index + 1;
                List<string> tags = new List<string>();
                if (rules.TryGetValue(landmark.Kind, out WorldMapLandmarkRule rule))
                {
                    AddTags(tags, rule.SlotTags);
                    if (!string.IsNullOrWhiteSpace(rule.LocationId))
                        AddTag(tags, WorldMapLandmarkRule.PlaceTagPrefix + rule.LocationId.Trim());
                }
                AddContextTags(tags, landmark.Cell);
                result.SpawnSlots.Add(Slot(landmark.Kind + "-" + (index + 1), landmark.Cell, tags, 0.35f));
            }

            for (int v = 1; v < result.Villages.Count; v++)
            {
                GeneratedMapSite village = result.Villages[v];
                List<string> tags = new List<string> { "Village" };
                AddContextTags(tags, village.Cell);
                result.SpawnSlots.Add(Slot("village-" + v, village.Cell, tags, 0.6f));
            }

            string[] contexts = { "Forest", "Clearing", "Hills", "Shore", "Swamp", "Field", "NearRoad", "Mountains" };
            WorldMapHexCell start = grid.CellAt(startCell);
            foreach (string context in contexts)
            {
                List<int> candidates = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (!reachable[i] || terrain[i] == Water || grid.CellAt(i).DistanceTo(start) < 5)
                        continue;
                    if (HasContext(i, context))
                        candidates.Add(i);
                }
                Shuffle(candidates, random);
                List<int> chosen = new List<int>();
                foreach (int cell in candidates)
                {
                    if (chosen.Count >= 4)
                        break;
                    if (IsNear(cell, chosen, 8))
                        continue;
                    chosen.Add(cell);
                    List<string> tags = new List<string>();
                    AddContextTags(tags, cell);
                    result.SpawnSlots.Add(Slot(context.ToLowerInvariant() + "-" + chosen.Count, cell, tags, 0.5f));
                }
            }
        }

        private bool HasContext(int cell, string context)
        {
            byte t = terrain[cell];
            switch (context)
            {
                case "Forest": return t == Forest;
                case "Clearing": return t == OpenGround;
                case "Hills": return t == Hills;
                case "Mountains": return t == Mountains;
                case "Swamp": return t == Swamp;
                case "Field": return t == Field;
                case "Shore": return t != Water && waterDistance != null && waterDistance[cell] <= 1;
                case "NearRoad": return t != Road && NextTo(cell, Road);
                default: return false;
            }
        }

        private void AddContextTags(List<string> tags, int cell)
        {
            foreach (string context in new[] { "Forest", "Clearing", "Hills", "Mountains", "Swamp", "Field", "Shore", "NearRoad" })
            {
                if (HasContext(cell, context))
                    AddTag(tags, context);
            }
            if (terrain[cell] == Road)
                AddTag(tags, "Road");
        }

        private GeneratedSpawnSlot Slot(string id, int cell, List<string> tags, float radiusHexes)
        {
            grid.CellCenter(grid.CellAt(cell), out double x, out double y);
            double r = radiusHexes * grid.HexWidth;
            return new GeneratedSpawnSlot
            {
                Id = id,
                Tags = tags,
                MinXPercent = Clamp01Percent(grid.PixelToPercentX(x - r)),
                MaxXPercent = Clamp01Percent(grid.PixelToPercentX(x + r)),
                MinYPercent = Clamp01Percent(grid.PixelToPercentY(y - r)),
                MaxYPercent = Clamp01Percent(grid.PixelToPercentY(y + r))
            };
        }

        private static float Clamp01Percent(float value) => Math.Max(0.5f, Math.Min(99.5f, value));

        private static void AddTags(List<string> tags, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            foreach (string part in text.Split(','))
                AddTag(tags, part.Trim());
        }

        private static void AddTag(List<string> tags, string tag)
        {
            if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag))
                tags.Add(tag);
        }

        // ------------------------------------------------------------------
        // Общие помощники
        // ------------------------------------------------------------------

        private bool NextTo(int cell, byte type)
        {
            for (int d = 0; d < 6; d++)
            {
                int next = neighbors[cell * 6 + d];
                if (next >= 0 && terrain[next] == type)
                    return true;
            }
            return false;
        }

        private bool NearEdge(int cell, int margin)
        {
            WorldMapHexCell c = grid.CellAt(cell);
            return c.Column < margin || c.Row < margin || c.Column >= grid.Columns - margin || c.Row >= grid.Rows - margin;
        }

        private int Distance(int a, int b) => grid.CellAt(a).DistanceTo(grid.CellAt(b));

        private bool IsNear(int cell, List<int> others, int distance)
        {
            WorldMapHexCell c = grid.CellAt(cell);
            foreach (int other in others)
            {
                if (c.DistanceTo(grid.CellAt(other)) < distance)
                    return true;
            }
            return false;
        }

        private int NearestDistance(int cell, List<int> others)
        {
            int best = int.MaxValue;
            WorldMapHexCell c = grid.CellAt(cell);
            foreach (int other in others)
                best = Math.Min(best, c.DistanceTo(grid.CellAt(other)));
            return best;
        }

        private List<int> CellsWithin(int cell, int radius)
        {
            List<int> cells = new List<int>();
            WorldMapHexCell center = grid.CellAt(cell);
            for (int row = center.Row - radius; row <= center.Row + radius; row++)
            {
                for (int column = center.Column - radius - 1; column <= center.Column + radius + 1; column++)
                {
                    WorldMapHexCell candidate = new WorldMapHexCell(column, row);
                    if (grid.IsInside(candidate) && candidate.DistanceTo(center) <= radius)
                        cells.Add(grid.IndexOf(candidate));
                }
            }
            return cells;
        }

        // Достижимость от старта по проходимой земле (вода и скалы — стены;
        // дорога и тропа через реку — брод или мост).
        private bool[] Reachable()
        {
            bool[] seen = new bool[count];
            Queue<int> queue = new Queue<int>();
            seen[startCell] = true;
            queue.Enqueue(startCell);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[cell * 6 + d];
                    if (next < 0 || seen[next] || terrain[next] == Water || terrain[next] == Cliffs)
                        continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            return seen;
        }

        // Расстояние в клетках до ближайшей клетки, удовлетворяющей условию
        // (не дальше limit; дальше — limit + 1).
        private int[] DistanceField(Func<int, bool> source, int limit)
        {
            int[] distance = new int[count];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (source(i))
                {
                    distance[i] = 0;
                    queue.Enqueue(i);
                }
                else
                    distance[i] = limit + 1;
            }
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (distance[cell] >= limit)
                    continue;
                for (int d = 0; d < 6; d++)
                {
                    int next = neighbors[cell * 6 + d];
                    if (next >= 0 && distance[next] > distance[cell] + 1)
                    {
                        distance[next] = distance[cell] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return distance;
        }

        private void RemoveSmallComponents(bool[] mask, int minSize)
        {
            bool[] seen = new bool[count];
            List<int> component = new List<int>();
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (!mask[i] || seen[i])
                    continue;
                component.Clear();
                seen[i] = true;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    component.Add(cell);
                    for (int d = 0; d < 6; d++)
                    {
                        int next = neighbors[cell * 6 + d];
                        if (next >= 0 && mask[next] && !seen[next])
                        {
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
                if (component.Count < minSize)
                {
                    foreach (int cell in component)
                        mask[cell] = false;
                }
            }
        }

        private void SelectTop(float[] score, bool[] eligible, int amount, byte type)
        {
            if (amount <= 0)
                return;
            List<int> cells = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (eligible[i])
                    cells.Add(i);
            }
            cells.Sort((a, b) => score[b].CompareTo(score[a]));
            for (int i = 0; i < amount && i < cells.Count; i++)
                terrain[cells[i]] = type;
        }

        // Порог, выше которого лежит доля share значений (среди eligible).
        private float TopThreshold(float[] values, bool[] eligible, float share)
        {
            List<float> list = new List<float>();
            for (int i = 0; i < values.Length; i++)
            {
                if (eligible == null || eligible[i])
                    list.Add(values[i]);
            }
            if (list.Count == 0 || share <= 0f)
                return float.MaxValue;
            list.Sort();
            int index = (int)Math.Floor(list.Count * (1.0 - Math.Min(1f, share)));
            return list[Math.Max(0, Math.Min(list.Count - 1, index))];
        }

        private static void Normalize(float[] values)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (float value in values)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            float range = Math.Max(0.0001f, max - min);
            for (int i = 0; i < values.Length; i++)
                values[i] = (values[i] - min) / range;
        }

        private List<double[]> CellPoints(List<int> cells)
        {
            List<double[]> points = new List<double[]>(cells.Count);
            foreach (int cell in cells)
            {
                grid.CellCenter(grid.CellAt(cell), out double x, out double y);
                points.Add(new[] { x, y });
            }
            return points;
        }

        private List<MapPointData> ToPercent(List<double[]> points)
        {
            List<MapPointData> percent = new List<MapPointData>(points.Count);
            foreach (double[] point in points)
                percent.Add(new MapPointData(grid.PixelToPercentX(point[0]), grid.PixelToPercentY(point[1])));
            return percent;
        }

        // Сглаживание Чайкина: концы остаются на месте.
        private static List<double[]> Chaikin(List<double[]> points, int iterations)
        {
            List<double[]> current = points;
            for (int it = 0; it < iterations && current.Count > 2; it++)
            {
                List<double[]> next = new List<double[]>(current.Count * 2) { current[0] };
                for (int i = 0; i < current.Count - 1; i++)
                {
                    double[] a = current[i];
                    double[] b = current[i + 1];
                    next.Add(new[] { a[0] * 0.75 + b[0] * 0.25, a[1] * 0.75 + b[1] * 0.25 });
                    next.Add(new[] { a[0] * 0.25 + b[0] * 0.75, a[1] * 0.25 + b[1] * 0.75 });
                }
                next.Add(current[current.Count - 1]);
                current = next;
            }
            return current;
        }

        // Плавные изгибы реки поперёк течения; концы не сдвигаются.
        private static List<double[]> Meander(List<double[]> points, Random random, double amplitude)
        {
            if (amplitude <= 0.0 || points.Count < 3)
                return points;
            double phase = random.NextDouble() * Math.PI * 2.0;
            double frequency = 0.35 + random.NextDouble() * 0.25;
            List<double[]> bent = new List<double[]>(points.Count) { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                double dx = points[i + 1][0] - points[i - 1][0];
                double dy = points[i + 1][1] - points[i - 1][1];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 0.0001)
                {
                    bent.Add(points[i]);
                    continue;
                }
                double fade = Math.Min(1.0, Math.Min(i, points.Count - 1 - i) / 4.0);
                double offset = Math.Sin(phase + i * frequency) * amplitude * fade;
                bent.Add(new[] { points[i][0] - dy / length * offset, points[i][1] + dx / length * offset });
            }
            bent.Add(points[points.Count - 1]);
            return bent;
        }

        private static void Shuffle<T>(List<T> list, Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T swap = list[i];
                list[i] = list[j];
                list[j] = swap;
            }
        }
    }

    // Минимальная куча (индекс клетки, приоритет) для заливки и A*.
    private sealed class MinHeap
    {
        private int[] items;
        private float[] priorities;

        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            items = new int[Math.Max(16, capacity)];
            priorities = new float[items.Length];
        }

        public void Push(int item, float priority)
        {
            if (Count == items.Length)
            {
                Array.Resize(ref items, items.Length * 2);
                Array.Resize(ref priorities, priorities.Length * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (priorities[parent] <= priority)
                    break;
                items[i] = items[parent];
                priorities[i] = priorities[parent];
                i = parent;
            }
            items[i] = item;
            priorities[i] = priority;
        }

        public int Pop()
        {
            int top = items[0];
            int lastItem = items[--Count];
            float lastPriority = priorities[Count];
            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= Count)
                    break;
                if (child + 1 < Count && priorities[child + 1] < priorities[child])
                    child++;
                if (priorities[child] >= lastPriority)
                    break;
                items[i] = items[child];
                priorities[i] = priorities[child];
                i = child;
            }
            if (Count > 0)
            {
                items[i] = lastItem;
                priorities[i] = lastPriority;
            }
            return top;
        }
    }
}

// Градиентный шум Перлина с собственной перестановкой по seed — не зависит
// от UnityEngine и даёт одинаковый результат на всех платформах.
public sealed class WorldMapNoise
{
    private readonly int[] permutation = new int[512];

    public WorldMapNoise(int seed)
    {
        Random random = new Random(seed);
        int[] p = new int[256];
        for (int i = 0; i < 256; i++)
            p[i] = i;
        for (int i = 255; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int swap = p[i];
            p[i] = p[j];
            p[j] = swap;
        }
        for (int i = 0; i < 512; i++)
            permutation[i] = p[i & 255];
    }

    // Примерно -1..1.
    public double Perlin(double x, double y)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        xi &= 255;
        yi &= 255;
        double u = Fade(xf);
        double v = Fade(yf);
        int aa = permutation[permutation[xi] + yi];
        int ab = permutation[permutation[xi] + yi + 1];
        int ba = permutation[permutation[xi + 1] + yi];
        int bb = permutation[permutation[xi + 1] + yi + 1];
        double x1 = Lerp(Gradient(aa, xf, yf), Gradient(ba, xf - 1, yf), u);
        double x2 = Lerp(Gradient(ab, xf, yf - 1), Gradient(bb, xf - 1, yf - 1), u);
        return Lerp(x1, x2, v) * 1.41;
    }

    public double Fbm(double x, double y, int octaves, double persistence)
    {
        double sum = 0.0, amplitude = 1.0, frequency = 1.0, norm = 0.0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(x * frequency + i * 17.31, y * frequency - i * 9.73) * amplitude;
            norm += amplitude;
            amplitude *= persistence;
            frequency *= 2.0;
        }
        return Math.Max(-1.0, Math.Min(1.0, sum / norm));
    }

    // Хребты: 0..1, гребни там, где шум пересекает ноль.
    public double Ridged(double x, double y, int octaves, double persistence)
    {
        double sum = 0.0, amplitude = 1.0, frequency = 1.0, norm = 0.0;
        for (int i = 0; i < octaves; i++)
        {
            double n = 1.0 - Math.Abs(Perlin(x * frequency + i * 5.17, y * frequency + i * 3.31));
            sum += n * n * amplitude;
            norm += amplitude;
            amplitude *= persistence;
            frequency *= 2.0;
        }
        return sum / norm;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Gradient(int hash, double x, double y)
    {
        switch (hash & 7)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x;
            case 5: return -x;
            case 6: return y;
            default: return -y;
        }
    }
}
