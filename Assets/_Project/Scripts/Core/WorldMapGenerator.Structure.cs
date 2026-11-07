using System;
using System.Collections.Generic;
using System.Linq;

// 12У-D: структура региона в генераторе (мастер-бриф перехода §8, гайд
// исследования MapLab, дополнение об устройстве пространства). Стадии
// встроены в прежний конвейер, у каждой — свой поток случайности:
//   приметные места — бюджет открытий по площади и насыщенности,
//     обязательные места с содержанием — ограниченные попытки восстановления;
//   дороги — число петель по связанности, затем причины деревень и их
//     семейства, хозяйственные тропы (ответвления с результатом), обходы
//     через места (петли);
//   готово — районы, основания обнаружить места, паспорт, проверки, отпечаток.
public static partial class WorldMapGenerator
{
    private static int defaultCellCount;

    private static int DefaultCellCount
    {
        get
        {
            if (defaultCellCount == 0)
                defaultCellCount = new WorldMapHexGrid(WorldMapHexGrid.DefaultCanvasWidth, WorldMapHexGrid.DefaultCanvasHeight, WorldMapHexGrid.DefaultHexesAcross).CellCount;
            return defaultCellCount;
        }
    }

    private sealed partial class Builder
    {
        public WorldMapRegionKind RegionKind = WorldMapRegionKind.Mixed;
        // Место → клетка дороги, к которой подходит его тропа.
        private readonly Dictionary<GeneratedMapSite, int> landmarkAttach = new Dictionary<GeneratedMapSite, int>();
        private const int MaxRepairAttempts = 8;

        // ------------------------------------------------------------------
        // Бюджет открытий
        // ------------------------------------------------------------------

        // Число мест правила: насыщенность × площадь относительно стандартной
        // сетки. При нейтрали на стандартной сетке — ровно Count, без лишнего
        // обращения к случайности (прежний поток мест не сдвигается).
        private int EffectiveCount(WorldMapLandmarkRule rule, Random random)
        {
            float factor = WorldMapRegionKinds.DiscoveryFactor(settings.Discoveries) * count / (float)Math.Max(1, DefaultCellCount);
            if (Math.Abs(factor - 1f) < 0.0001f)
                return rule.Count;
            float value = rule.Count * factor;
            int n = (int)Math.Floor(value);
            float fraction = value - n;
            if (fraction > 0.0001f && random.NextDouble() < fraction)
                n++;
            if (!string.IsNullOrWhiteSpace(rule.LocationId) && rule.Count > 0)
                n = Math.Max(1, n);
            return Math.Min(40, n);
        }

        // Обязательное место: сначала ослабляется расстояние, затем местность,
        // затем близость к воде и деревне. Всё — в пределах MaxRepairAttempts.
        private void RepairRequiredLandmark(WorldMapLandmarkRule rule, Random random, List<int> taken, List<int> villageCells)
        {
            bool[] reachable = Reachable();
            for (int attempt = 1; attempt <= MaxRepairAttempts; attempt++)
            {
                float minDistance = rule.MinDistanceHexes * (1f - attempt / (float)MaxRepairAttempts);
                bool anyTerrain = attempt >= 3;
                bool anyWater = attempt >= 5;
                bool anyVillage = attempt >= 6;
                List<int> candidates = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    byte t = terrain[i];
                    if (t == Water || t == Cliffs || protectedStart[i] || NearEdge(i, 1) || !reachable[i])
                        continue;
                    if (!anyTerrain && rule.Terrains.Count > 0 && !rule.Terrains.Contains((WorldMapGameplayTerrainType)t))
                        continue;
                    if (rule.NearWater && !anyWater && waterDistance[i] > 2)
                        continue;
                    int villageDistance = NearestDistance(i, villageCells);
                    if (rule.NearVillage && !anyVillage ? villageDistance > 4 || villageDistance < 1 : villageDistance < Math.Min(minDistance, 3f))
                        continue;
                    if (IsNear(i, taken, Math.Max(2, (int)(minDistance * 0.6f))))
                        continue;
                    candidates.Add(i);
                }
                if (candidates.Count == 0)
                    continue;
                int cell = candidates[random.Next(candidates.Count)];
                taken.Add(cell);
                GeneratedMapSite site = Site(rule, cell, random);
                result.Landmarks.Add(site);
                result.Report.RepairAttempts += attempt;
                List<string> relaxed = new List<string> { "расстояние до других мест ×" + (1f - attempt / (float)MaxRepairAttempts).ToString("0.##") };
                if (anyTerrain && rule.Terrains.Count > 0) relaxed.Add("местность");
                if (anyWater && rule.NearWater) relaxed.Add("близость к воде");
                if (anyVillage && rule.NearVillage) relaxed.Add("близость к деревне");
                result.Report.Issues.Add(new GeneratedRegionIssue
                {
                    Code = "R06",
                    Error = false,
                    Message = $"Место «{rule.Name}» (содержание «{rule.LocationId}») не встало по правилу; поставлено с попытки {attempt} из {MaxRepairAttempts}, ослаблено: {string.Join(", ", relaxed)}.",
                    XPercent = site.XPercent,
                    YPercent = site.YPercent
                });
                return;
            }
            result.Report.RepairAttempts += MaxRepairAttempts;
        }

        // ------------------------------------------------------------------
        // Причины деревень и планировочные семейства
        // ------------------------------------------------------------------

        private void AssignSettlementReasons(HashSet<int> bridgeCells)
        {
            foreach (GeneratedMapSite village in result.Villages)
            {
                int total = 0, forest = 0, stone = 0, open = 0;
                bool crossing = false, junction = false, lakeShore = false, riverShore = false;
                foreach (int i in CellsWithin(village.Cell, 4))
                {
                    byte t = terrain[i];
                    int d = Distance(i, village.Cell);
                    if (t != Water)
                    {
                        total++;
                        if (t == Forest || t == Swamp) forest++;
                        else if (t == Hills || t == Mountains || t == Cliffs) stone++;
                        else if (t == OpenGround || t == Field) open++;
                    }
                    if (d <= 2)
                    {
                        if (bridgeCells.Contains(i) || (riverId[i] >= 0 && (t == Road || t == Trail)))
                            crossing = true;
                        if (t == Road && RoadDegree(i) >= 3)
                            junction = true;
                        if (lake[i]) lakeShore = true;
                        if (riverId[i] >= 0) riverShore = true;
                    }
                }
                float share(int n) => total == 0 ? 0f : n / (float)total;
                List<(string Id, float Score, string Text)> reasons = new List<(string, float, string)>
                {
                    ("crossing", crossing ? 1.25f : 0f, "Переправа: дорога переходит реку рядом"),
                    ("water", lakeShore || riverShore ? 0.9f : 0f, lakeShore ? "Вода: берег озера, рыба и водопой" : "Вода: берег реки, рыба и водопой"),
                    ("forest", share(forest) * 1.6f, "Лесной промысел: лес подходит вплотную"),
                    ("stone", share(stone) * 1.8f, "Камень и склоны: выходы породы, сухое место"),
                    ("road", junction ? 0.8f : 0f, "Дорога: здесь сходятся пути"),
                    ("fertile", share(open) * 1.2f, "Пашня: ровная открытая земля")
                };
                (string id, float score, string text) = reasons.OrderByDescending(r => r.Score).First();
                if (score < 0.3f)
                {
                    village.Reason = string.Empty;
                    village.ReasonText = string.Empty;
                }
                else
                {
                    village.Reason = id;
                    village.ReasonText = text;
                }

                bool rough = share(stone) > 0.4f;
                string family;
                switch (village.Reason)
                {
                    case "crossing": family = GeneratedMapSite.FamilyCrossing; break;
                    case "forest":
                    case "stone": family = GeneratedMapSite.FamilyCraft; break;
                    case "water": family = village.Size <= 1 ? GeneratedMapSite.FamilyCraft : GeneratedMapSite.FamilyAlongRoad; break;
                    case "road": family = GeneratedMapSite.FamilyAlongRoad; break;
                    case "fertile": family = village.Size >= 2 ? GeneratedMapSite.FamilyAlongRoad : GeneratedMapSite.FamilyScattered; break;
                    default: family = GeneratedMapSite.FamilyScattered; break;
                }
                if (rough && family != GeneratedMapSite.FamilyCrossing)
                    family = GeneratedMapSite.FamilyScattered;
                village.Family = family;
            }
        }

        private int RoadDegree(int cell)
        {
            int roads = 0;
            for (int d = 0; d < 6; d++)
            {
                int next = neighbors[cell * 6 + d];
                if (next >= 0 && terrain[next] == Road)
                    roads++;
            }
            return roads;
        }

        // ------------------------------------------------------------------
        // Хозяйственные тропы и обходы
        // ------------------------------------------------------------------

        // Хозяйственная тропа ведёт к работе деревни: делянка у леса,
        // каменоломня на склоне, мостки у воды, выгон в поле. Промысловые
        // слободы получают её всегда; остальные — чаще при низкой
        // связанности (ответвления), реже при высокой (петли).
        private void BuildWorkTrails(HashSet<int> bridgeCells)
        {
            if (!settings.WorkTrails)
                return;
            Random random = new Random(StreamSeed(seed, "work-trails"));
            float chance = 0.35f + 0.6f * (1f - settings.RouteConnectivity);
            List<int> villageCells = result.Villages.Select(v => v.Cell).ToList();
            List<int> occupied = result.Landmarks.Select(l => l.Cell).ToList();
            foreach (GeneratedMapSite village in result.Villages)
            {
                double roll = random.NextDouble();
                bool craft = village.Family == GeneratedMapSite.FamilyCraft;
                if (!craft && roll >= chance)
                    continue;
                string kind, name;
                Func<int, bool> fits;
                int min = 3, max = 7;
                switch (village.Reason)
                {
                    case "forest":
                        kind = "work-forest"; name = "Делянка";
                        fits = i => (terrain[i] == Forest) && HasNeighborNot(i, Forest);
                        break;
                    case "stone":
                        kind = "work-stone"; name = "Каменоломня";
                        fits = i => terrain[i] == Hills || terrain[i] == Mountains;
                        max = 8;
                        break;
                    case "water":
                    case "crossing":
                        kind = "work-shore"; name = "Мостки";
                        fits = i => terrain[i] != Water && terrain[i] != Road && terrain[i] != Cliffs && waterDistance[i] == 1;
                        min = 2; max = 6;
                        break;
                    default:
                        kind = "work-pasture"; name = "Выгон";
                        fits = i => terrain[i] == OpenGround && !NextTo(i, Road);
                        max = 6;
                        break;
                }
                List<int> candidates = new List<int>();
                foreach (int i in CellsWithin(village.Cell, max))
                {
                    int d = Distance(i, village.Cell);
                    if (d < min || NearEdge(i, 1) || !fits(i))
                        continue;
                    if (IsNear(i, occupied, 3) || NearestDistance(i, villageCells) < 2)
                        continue;
                    candidates.Add(i);
                }
                if (candidates.Count == 0)
                    continue;
                candidates.Sort((a, b) => Distance(a, village.Cell).CompareTo(Distance(b, village.Cell)));
                int target = candidates[random.Next(Math.Min(5, candidates.Count))];
                List<int> path = FindPath(village.Cell, new HashSet<int> { target }, true);
                if (path.Count < 2 || path.Count > max * 2 + 2)
                    continue;
                ApplyPath(path, true, bridgeCells);
                occupied.Add(target);
                grid.CellCenter(grid.CellAt(target), out double x, out double y);
                result.WorkSites.Add(new GeneratedMapSite
                {
                    Kind = kind,
                    Name = name,
                    Cell = target,
                    XPercent = grid.PixelToPercentX(x),
                    YPercent = grid.PixelToPercentY(y),
                    Variant = random.Next(1 << 20),
                    DrawMark = false,
                    Description = village.ReasonText
                });
                result.Report.WorkTrails++;
                result.Report.Branches++;
            }
        }

        private bool HasNeighborNot(int cell, byte type)
        {
            for (int d = 0; d < 6; d++)
            {
                int next = neighbors[cell * 6 + d];
                if (next >= 0 && terrain[next] != type)
                    return true;
            }
            return false;
        }

        // Обход через место: вторая тропа от места к другой части дорожной
        // сети. Только при связанности выше нейтрали и если обход не длиннее
        // разумного (иначе это не выбор, а крюк).
        private void BuildLandmarkLoops(HashSet<int> bridgeCells)
        {
            float share = (settings.RouteConnectivity - 0.5f) * 2f;
            if (share <= 0f)
                return;
            Random random = new Random(StreamSeed(seed, "route-loops"));
            foreach (GeneratedMapSite landmark in result.Landmarks)
            {
                if (random.NextDouble() >= share * 0.6f)
                    continue;
                if (!landmarkAttach.TryGetValue(landmark, out int attach))
                    continue;
                int firstLength = Distance(landmark.Cell, attach) + 1;
                HashSet<int> targets = new HashSet<int>();
                for (int i = 0; i < count; i++)
                    if (terrain[i] == Road && Distance(i, attach) >= 6)
                        targets.Add(i);
                if (targets.Count == 0)
                    continue;
                List<int> path = FindPath(landmark.Cell, targets, true);
                if (path.Count < 3 || path.Count > Math.Max(6, firstLength * 2.2f))
                    continue;
                path.Reverse();
                ApplyPath(path, true, bridgeCells);
                result.Report.Loops++;
            }
        }

        // ------------------------------------------------------------------
        // Детали мест и условия соседства
        // ------------------------------------------------------------------

        public static readonly string[] KnownNear = { "river", "water", "road", "forest-edge", "fields", "slope", "high", "village", "far" };

        // Условия соседства правила (мельница — у реки, пристань — у воды и
        // деревни...). Для мест (до дорог) «road» и «fields» ещё не выполнимы.
        private bool MatchesNear(WorldMapLandmarkRule rule, int cell)
        {
            if (rule.Near == null || rule.Near.Count == 0)
                return true;
            foreach (string token in rule.Near)
            {
                if (!NearHolds(token, cell))
                    return false;
            }
            return true;
        }

        private bool NearHolds(string token, int cell)
        {
            byte t = terrain[cell];
            switch (token)
            {
                case "river":
                    for (int d = 0; d < 6; d++)
                    {
                        int n = neighbors[cell * 6 + d];
                        if (n >= 0 && riverId[n] >= 0 && terrain[n] == Water)
                            return true;
                    }
                    return false;
                case "water":
                    return waterDistance != null && waterDistance[cell] <= 1;
                case "road":
                    foreach (int i in CellsWithin(cell, 2))
                        if (terrain[i] == Road || terrain[i] == Trail)
                            return true;
                    return false;
                case "forest-edge":
                    return t == Forest ? HasNeighborNot(cell, Forest) : t == OpenGround && NextTo(cell, Forest);
                case "fields":
                    if (t == Field)
                        return false;
                    foreach (int i in CellsWithin(cell, 2))
                        if (terrain[i] == Field)
                            return true;
                    return false;
                case "slope":
                    foreach (int i in CellsWithin(cell, 1))
                        if (terrain[i] == Hills || terrain[i] == Mountains || terrain[i] == Cliffs)
                            return true;
                    return false;
                case "high":
                    return t == Hills || t == Mountains;
                case "village":
                {
                    int d = result.Villages.Count == 0 ? int.MaxValue : result.Villages.Min(v => Distance(v.Cell, cell));
                    return d >= 2 && d <= 4;
                }
                case "far":
                    return result.Villages.Count == 0 || result.Villages.Min(v => Distance(v.Cell, cell)) >= 6;
                default:
                    return false;
            }
        }

        private void WarnUnknownNear(WorldMapLandmarkRule rule)
        {
            foreach (string token in rule.Near ?? new List<string>())
            {
                if (Array.IndexOf(KnownNear, token) < 0)
                    result.Report.Issues.Add(new GeneratedRegionIssue
                    {
                        Code = "W13", Error = false, XPercent = 50f, YPercent = 50f,
                        Message = $"Правило «{rule.Name}» ({rule.Id}): неизвестное условие соседства «{token}» — правило не ставится (известны: {string.Join(", ", KnownNear)})."
                    });
            }
        }

        // Детали мест: после дорог и полей; площадка вне дорог, полей, воды,
        // деревень и других мест; рисунок и описание — из правила.
        private void BuildFeatures()
        {
            Random random = new Random(StreamSeed(seed, "features"));
            bool[] reachable = Reachable();
            List<int> taken = result.Landmarks.Select(l => l.Cell).Concat(result.WorkSites.Select(w => w.Cell)).ToList();
            List<int> villageCells = result.Villages.Select(v => v.Cell).ToList();
            foreach (WorldMapLandmarkRule rule in settings.Features)
            {
                if (rule == null || !rule.Enabled || rule.Count <= 0)
                    continue;
                WarnUnknownNear(rule);
                int want = EffectiveCount(rule, random);
                List<int> candidates = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    byte t = terrain[i];
                    if (t == Water || t == Cliffs || t == Road || t == Trail || t == Field || !reachable[i] || protectedStart[i] || NearEdge(i, 1))
                        continue;
                    if (rule.Terrains.Count > 0 && !rule.Terrains.Contains((WorldMapGameplayTerrainType)t))
                        continue;
                    if (villageCells.Count > 0 && NearestDistance(i, villageCells) < 2)
                        continue;
                    if (!MatchesNear(rule, i))
                        continue;
                    candidates.Add(i);
                }
                int placed = 0;
                Shuffle(candidates, random);
                foreach (int cell in candidates)
                {
                    if (placed >= want)
                        break;
                    if (IsNear(cell, taken, Math.Max(2, (int)rule.MinDistanceHexes)))
                        continue;
                    taken.Add(cell);
                    GeneratedMapSite site = Site(rule, cell, random);
                    result.Features.Add(site);
                    placed++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Районы, сигналы, паспорт, проверки
        // ------------------------------------------------------------------

        private void BuildStructure()
        {
            BuildDistricts();
            AssignSignals();
            result.Report.Crossings = bridgeSpans.Count;
            foreach (WorldMapLandmarkRule rule in settings.Landmarks)
            {
                if (rule != null && rule.Enabled && rule.Count > 0 && !string.IsNullOrWhiteSpace(rule.LocationId)
                    && !result.Report.RequiredLocations.Contains(rule.LocationId.Trim()))
                    result.Report.RequiredLocations.Add(rule.LocationId.Trim());
            }
            BuildPassport();
            result.Report.Issues.AddRange(WorldMapRegionValidator.Validate(result));
            result.Passport.Fingerprint = WorldMapRegionValidator.Fingerprint(result);
        }

        private const byte DWater = 1, DSettled = 2, DUpland = 3, DDeep = 4, DEdge = 5, DShore = 6, DOpen = 7;

        private static string DistrictKind(byte code)
        {
            switch (code)
            {
                case DWater: return GeneratedMapDistrict.Water;
                case DSettled: return GeneratedMapDistrict.Settled;
                case DUpland: return GeneratedMapDistrict.Upland;
                case DDeep: return GeneratedMapDistrict.DeepForest;
                case DEdge: return GeneratedMapDistrict.ForestEdge;
                case DShore: return GeneratedMapDistrict.Waterside;
                default: return GeneratedMapDistrict.Open;
            }
        }

        private void BuildDistricts()
        {
            int[] roadDistance = DistanceField(i => terrain[i] == Road || terrain[i] == Trail, 4);
            HashSet<int> villageSet = new HashSet<int>(result.Villages.Select(v => v.Cell));
            int[] villageDistance = DistanceField(i => villageSet.Contains(i), 4);
            byte[] cls = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte t = terrain[i];
                if (t == Water)
                    cls[i] = DWater;
                else if (villageDistance[i] <= 3 || t == Field)
                    cls[i] = DSettled;
                else if (t == Hills || t == Mountains || t == Cliffs)
                    cls[i] = DUpland;
                else if (t == Forest || t == Swamp)
                    cls[i] = roadDistance[i] >= 3 ? DDeep : DEdge;
                else if (waterDistance[i] <= 1)
                    cls[i] = DShore;
                else
                    cls[i] = DOpen;
            }

            // Связные области одного характера.
            int[] comp = new int[count];
            for (int i = 0; i < count; i++)
                comp[i] = -1;
            List<byte> compClass = new List<byte>();
            List<int> compSize = new List<int>();
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (comp[i] >= 0)
                    continue;
                int id = compClass.Count;
                compClass.Add(cls[i]);
                compSize.Add(0);
                comp[i] = id;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    compSize[id]++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = neighbors[c * 6 + d];
                        if (n >= 0 && comp[n] < 0 && cls[n] == cls[i])
                        {
                            comp[n] = id;
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            // Мелкие обрезки сливаются с соседом, с которым граничат сильнее.
            // Вода не сливается с сушей, пока районов не слишком много.
            int threshold = 6;
            for (int pass = 0; pass < 12; pass++)
            {
                bool changed = false;
                int alive = compSize.Count(s => s > 0);
                bool tooMany = alive > 250;
                for (int id = 0; id < compClass.Count; id++)
                {
                    if (compSize[id] == 0 || compSize[id] >= threshold)
                        continue;
                    if (compClass[id] == DWater && !tooMany)
                        continue;
                    Dictionary<int, int> border = new Dictionary<int, int>();
                    for (int i = 0; i < count; i++)
                    {
                        if (comp[i] != id)
                            continue;
                        for (int d = 0; d < 6; d++)
                        {
                            int n = neighbors[i * 6 + d];
                            if (n < 0 || comp[n] == id)
                                continue;
                            if (compClass[comp[n]] == DWater && compClass[id] != DWater && !tooMany)
                                continue;
                            border.TryGetValue(comp[n], out int b);
                            border[comp[n]] = b + 1;
                        }
                    }
                    if (border.Count == 0)
                        continue;
                    int target = border.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                    for (int i = 0; i < count; i++)
                        if (comp[i] == id)
                            comp[i] = target;
                    compSize[target] += compSize[id];
                    compSize[id] = 0;
                    changed = true;
                }
                if (compSize.Count(s => s > 0) <= 250 && !changed)
                    break;
                if (compSize.Count(s => s > 0) > 250)
                    threshold *= 2;
            }

            // Нумерация районов в порядке первой клетки (устойчиво).
            Dictionary<int, int> index = new Dictionary<int, int>();
            List<double> sumX = new List<double>(), sumY = new List<double>();
            byte[] packed = new byte[count];
            for (int i = 0; i < count; i++)
            {
                int c = comp[i];
                if (!index.TryGetValue(c, out int k))
                {
                    if (index.Count >= 255)
                        continue;
                    k = index.Count;
                    index[c] = k;
                    result.Districts.Add(new GeneratedMapDistrict { Id = "district-" + k, Kind = DistrictKind(compClass[c]) });
                    sumX.Add(0);
                    sumY.Add(0);
                }
                grid.CellCenter(grid.CellAt(i), out double x, out double y);
                sumX[k] += x;
                sumY[k] += y;
                result.Districts[k].CellCount++;
                packed[i] = (byte)(k + 1);
            }
            for (int k = 0; k < result.Districts.Count; k++)
            {
                GeneratedMapDistrict district = result.Districts[k];
                district.XPercent = grid.PixelToPercentX(sumX[k] / Math.Max(1, district.CellCount));
                district.YPercent = grid.PixelToPercentY(sumY[k] / Math.Max(1, district.CellCount));
            }
            for (int i = 0; i < count; i++)
            {
                if (packed[i] == 0)
                    continue;
                int a = packed[i] - 1;
                for (int d = 0; d < 6; d++)
                {
                    int n = neighbors[i * 6 + d];
                    if (n < 0 || packed[n] == 0 || packed[n] == packed[i])
                        continue;
                    int b = packed[n] - 1;
                    if (!result.Districts[a].Neighbors.Contains(b))
                        result.Districts[a].Neighbors.Add(b);
                }
            }
            foreach (GeneratedMapDistrict district in result.Districts)
                district.Neighbors.Sort();
            result.DistrictCells = Convert.ToBase64String(packed);

            void Tag(GeneratedMapSite site)
            {
                if (site != null && site.Cell >= 0 && site.Cell < count && packed[site.Cell] > 0)
                    site.DistrictId = result.Districts[packed[site.Cell] - 1].Id;
            }
            result.Villages.ForEach(Tag);
            result.Landmarks.ForEach(Tag);
            result.WorkSites.ForEach(Tag);
            result.Features.ForEach(Tag);
        }

        // Основания обнаружить место (гайд §13–14): тропа, вид с дороги,
        // слух в ближней деревне, путь по воде, соседнее место.
        private void AssignSignals()
        {
            int[] roadDistance = DistanceField(i => terrain[i] == Road, 6);
            List<int> villageCells = result.Villages.Select(v => v.Cell).ToList();
            foreach (GeneratedMapSite site in result.Landmarks)
            {
                site.Signals = new List<string>();
                int cell = site.Cell;
                byte t = terrain[cell];
                if (t == Trail || t == Road || NextTo(cell, Trail) || NextTo(cell, Road))
                    site.Signals.Add("trail");
                bool open = t == OpenGround || t == Field || t == Road || t == Trail;
                bool high = t == Hills || t == Mountains;
                if ((open && roadDistance[cell] <= 3) || (high && roadDistance[cell] <= 6))
                    site.Signals.Add("view");
                if (villageCells.Count > 0 && NearestDistance(cell, villageCells) <= 9)
                    site.Signals.Add("rumor");
                bool river = false;
                foreach (int i in CellsWithin(cell, 2))
                {
                    if (riverId[i] >= 0)
                    {
                        river = true;
                        break;
                    }
                }
                if (river)
                    site.Signals.Add("water");
                if (result.Landmarks.Any(o => o != site && Distance(o.Cell, cell) <= 6))
                    site.Signals.Add("neighbor");
            }
        }

        private void BuildPassport()
        {
            GeneratedRegionPassport p = result.Passport;
            p.Kind = WorldMapRegionKinds.Id(RegionKind);
            p.KindTitle = WorldMapRegionKinds.Title(RegionKind);
            int land = 0, forest = 0, upland = 0, fields = 0, water = 0;
            for (int i = 0; i < count; i++)
            {
                byte t = terrain[i];
                if (t == Water) { water++; continue; }
                land++;
                if (t == Forest || t == Swamp) forest++;
                else if (t == Hills || t == Mountains || t == Cliffs) upland++;
                else if (t == Field) fields++;
            }
            float Share(int n) => land == 0 ? 0f : n * 100f / land;
            string kinds = string.Join(", ", result.Districts.Where(d => d.Kind != GeneratedMapDistrict.Water)
                .GroupBy(d => d.Kind).OrderByDescending(g => g.Sum(d => d.CellCount))
                .Select(g => GeneratedMapDistrict.Title(g.Key).ToLowerInvariant() + " ×" + g.Count()));
            p.Character = $"{p.KindTitle}: рек {result.Rivers.Count}, вода {water * 100f / Math.Max(1, count):0}% карты; лес {Share(forest):0}% суши, холмы и горы {Share(upland):0}%, поля {Share(fields):0}%. Районы: {kinds}.";
            string level = result.Villages.Count >= 10 || Share(fields) > 6f ? "обжитая земля"
                : result.Villages.Count <= 3 && Share(fields) < 2f ? "дикая земля" : "редко обжитая земля";
            string families = string.Join(", ", result.Villages.GroupBy(v => v.Family).OrderByDescending(g => g.Count())
                .Select(g => GeneratedMapSite.FamilyTitle(g.Key).ToLowerInvariant() + " ×" + g.Count()));
            p.Settledness = $"{level}: деревень {result.Villages.Count} ({families}).";
            p.Links.Clear();
            int roads = result.Roads.Count(r => r.Kind == GeneratedMapLine.RoadKind), trails = result.Roads.Count(r => r.Kind == GeneratedMapLine.TrailKind);
            p.Links.Add($"Линий дорог {roads}, троп {trails}; переправ {bridgeSpans.Count}.");
            p.Links.Add($"Петель и обходов {result.Report.Loops}, завершённых ответвлений {result.Report.Branches} (из них хозяйственных троп {result.Report.WorkTrails}), выходов к краю {result.Report.EdgeExits}.");
            if (result.Features.Count > 0)
                p.Links.Add("Детали мест: " + string.Join(", ", result.Features.GroupBy(f => f.Name).Select(g => g.Count() > 1 ? g.Key + " ×" + g.Count() : g.Key)) + ".");
            p.Content.Clear();
            foreach (GeneratedMapSite site in result.Landmarks.Where(l => !string.IsNullOrEmpty(l.LocationId)))
                p.Content.Add($"{site.Name} → место «{site.LocationId}»");
            int plain = result.Landmarks.Count(l => string.IsNullOrEmpty(l.LocationId));
            if (plain > 0)
                p.Content.Add($"Мест без подключённого содержания: {plain} — площадки для будущего содержания, не готовые истории.");
            p.Excluded.Clear();
            p.Excluded.AddRange(new[] { "плавание", "лодки и паромы", "скалолазание", "экономическая симуляция", "новые квестовые системы" });
        }
    }
}
