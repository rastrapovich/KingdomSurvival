using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// 12У-D (мастер-бриф перехода на MapLab §8, гайд исследования MapLab):
// структура региона поверх разметки генератора — паспорт, смысловые районы,
// причины размещения деревень и их планировочные семейства, хозяйственные
// площадки, основания обнаружить места (сигналы), бюджет открытий,
// связанность маршрутов и проверки корректности (H — ошибки, W —
// предупреждения) с отчётом по конкретным объектам. Это рабочие правила
// генератора, не факты мира: тип региона, районы и площадки не утверждают
// географию, людей или сюжет.

// Тип региона (мастер-бриф §5 №1, рецепты гайда §20). Mixed — прежняя
// смесь без поправок; BySeed — тип выбирается по seed партии.
public enum WorldMapRegionKind
{
    Mixed = 0,
    BySeed = 1,
    RiverValley = 2,
    ForestLake = 3,
    HillAndLedge = 4,
    SettledLand = 5
}

// Насыщенность открытиями (мастер-бриф §5 №8): бюджет мест относительно
// площади, а не число иконок.
public enum WorldMapDiscoveryRate
{
    Rare = 0,
    Normal = 1,
    Rich = 2
}

[Serializable]
public sealed class GeneratedRegionPassport
{
    public string Kind = string.Empty;
    public string KindTitle = string.Empty;
    public string Character = string.Empty;
    public string Settledness = string.Empty;
    public List<string> Links = new List<string>();
    public List<string> Content = new List<string>();
    public List<string> Excluded = new List<string>();
    // Компактный отпечаток структуры (гайд §22): тип, районы, петли,
    // ответвления, переправы, семейства деревень — грубыми корзинами.
    public string Fingerprint = string.Empty;
}

// Смысловой район: связная область одного характера.
[Serializable]
public sealed class GeneratedMapDistrict
{
    public const string Settled = "settled";
    public const string Upland = "upland";
    public const string DeepForest = "deep-forest";
    public const string ForestEdge = "forest-edge";
    public const string Waterside = "waterside";
    public const string Open = "open";
    public const string Water = "water";

    public string Id = string.Empty;
    public string Kind = Open;
    public int CellCount;
    public float XPercent;
    public float YPercent;
    public List<int> Neighbors = new List<int>();

    public static string Title(string kind)
    {
        switch (kind)
        {
            case Settled: return "Обжитая земля";
            case Upland: return "Возвышенность";
            case DeepForest: return "Лесная глубина";
            case ForestEdge: return "Опушка";
            case Waterside: return "Берег";
            case Water: return "Вода";
            default: return "Открытая земля";
        }
    }
}

[Serializable]
public sealed class GeneratedRegionIssue
{
    public string Code = string.Empty;
    public bool Error;
    public string Message = string.Empty;
    public float XPercent;
    public float YPercent;
}

[Serializable]
public sealed class GeneratedRegionReport
{
    public List<GeneratedRegionIssue> Issues = new List<GeneratedRegionIssue>();
    // Места с содержанием (LocationId правил), которые обязаны стоять на карте.
    public List<string> RequiredLocations = new List<string>();
    public int RepairAttempts;
    public int Loops;
    public int Branches;
    public int WorkTrails;
    public int Crossings;
    public int EdgeExits;
    public float RequiredReachable = 1f;

    public int ErrorCount => Issues?.Count(i => i != null && i.Error) ?? 0;
    public int WarningCount => Issues?.Count(i => i != null && !i.Error) ?? 0;
}

public static class WorldMapRegionKinds
{
    public static readonly WorldMapRegionKind[] Concrete =
    {
        WorldMapRegionKind.RiverValley,
        WorldMapRegionKind.ForestLake,
        WorldMapRegionKind.HillAndLedge,
        WorldMapRegionKind.SettledLand
    };

    public static string Id(WorldMapRegionKind kind)
    {
        switch (kind)
        {
            case WorldMapRegionKind.RiverValley: return "river-valley";
            case WorldMapRegionKind.ForestLake: return "forest-lake";
            case WorldMapRegionKind.HillAndLedge: return "hill-ledge";
            case WorldMapRegionKind.SettledLand: return "settled-land";
            default: return "mixed";
        }
    }

    public static string Title(WorldMapRegionKind kind)
    {
        switch (kind)
        {
            case WorldMapRegionKind.RiverValley: return "Речная долина";
            case WorldMapRegionKind.ForestLake: return "Лесное озеро";
            case WorldMapRegionKind.HillAndLedge: return "Холм и уступ";
            case WorldMapRegionKind.SettledLand: return "Обжитая земля";
            case WorldMapRegionKind.BySeed: return "По seed";
            default: return "Смешанный";
        }
    }

    // Выбор типа по seed (свой поток случайности).
    public static WorldMapRegionKind Resolve(WorldMapRegionKind kind, int seed)
    {
        if (kind != WorldMapRegionKind.BySeed)
            return kind;
        Random random = new Random(WorldMapGenerator.StreamSeed(seed, "region-kind"));
        return Concrete[random.Next(Concrete.Length)];
    }

    // Поправки настроек под тип региона с разбросом по seed: один тип даёт
    // разные схемы (число рек, доля леса, холмов, деревень), а не одну
    // композицию со смещением объектов. Mixed не меняет ничего.
    public static void Apply(WorldMapGenerationSettings s, WorldMapRegionKind kind, int seed)
    {
        if (kind == WorldMapRegionKind.Mixed || kind == WorldMapRegionKind.BySeed)
            return;
        Random random = new Random(WorldMapGenerator.StreamSeed(seed, "region-jitter"));
        float J(float spread) => 1f + ((float)random.NextDouble() * 2f - 1f) * spread;
        switch (kind)
        {
            case WorldMapRegionKind.RiverValley:
                s.RiverCount = Math.Max(1, s.RiverCount + random.Next(0, 3) - 0);
                s.RiverWidthHexes *= 1.25f * J(0.15f);
                s.RiverMinLengthHexes = Math.Max(2, (int)(s.RiverMinLengthHexes * 1.3f));
                s.LakesShare *= 0.45f * J(0.4f);
                s.VillageNearWater *= 1.5f;
                s.HillsShare *= 0.85f * J(0.2f);
                s.ForestShare *= J(0.2f);
                break;
            case WorldMapRegionKind.ForestLake:
                s.LakesShare = Math.Max(s.LakesShare, 0.02f) * 2.4f * J(0.3f);
                s.ForestShare = Math.Min(0.7f, s.ForestShare * 1.35f * J(0.12f));
                s.RiverCount = Math.Max(1, s.RiverCount - 1 - random.Next(0, 2));
                s.VillageCount = Math.Max(2, (int)Math.Round(s.VillageCount * 0.8f * J(0.15f)));
                s.SwampShare *= 1.4f;
                break;
            case WorldMapRegionKind.HillAndLedge:
                s.HillsShare = Math.Min(0.5f, s.HillsShare * 1.6f * J(0.2f));
                s.MountainsShare = Math.Min(0.25f, s.MountainsShare * 1.6f * J(0.3f));
                s.CliffsShare = Math.Min(1f, s.CliffsShare * 1.5f);
                s.MountainRidges = Math.Min(1f, s.MountainRidges + 0.15f);
                s.RiverCount = Math.Max(1, s.RiverCount - random.Next(0, 2));
                s.ForestOnHills = Math.Min(1f, s.ForestOnHills * J(0.3f));
                break;
            case WorldMapRegionKind.SettledLand:
                s.ForestShare *= 0.6f * J(0.2f);
                s.VillageCount = Math.Max(2, (int)Math.Round(s.VillageCount * 1.3f * J(0.12f)));
                s.VillageMinDistanceHexes = Math.Max(2, (int)Math.Round(s.VillageMinDistanceHexes * 0.85f));
                s.FieldDensity = Math.Min(1f, s.FieldDensity * 1.3f);
                s.FieldRadiusHexes = Math.Min(10f, s.FieldRadiusHexes + 1f);
                s.HillsShare *= 0.75f * J(0.2f);
                s.SwampShare *= 0.5f;
                break;
        }
        s.Sanitize();
    }

    public static float DiscoveryFactor(WorldMapDiscoveryRate rate)
    {
        switch (rate)
        {
            case WorldMapDiscoveryRate.Rare: return 0.6f;
            case WorldMapDiscoveryRate.Rich: return 1.5f;
            default: return 1f;
        }
    }

    // Множитель вокруг нейтрали: 0 → low, 0.5 → 1, 1 → high.
    public static float Around(float m, float low, float high)
    {
        m = Math.Max(0f, Math.Min(1f, float.IsNaN(m) ? 0.5f : m));
        return m < 0.5f ? low + (1f - low) * m * 2f : 1f + (high - 1f) * (m - 0.5f) * 2f;
    }
}

// Проверки карты (гайд §21). Работает по готовому результату, поэтому её
// можно повторить и для загруженной карты. Сообщения называют объект и
// причину, а не «плохая карта».
public static class WorldMapRegionValidator
{
    public const int LongRoadWithoutSituation = 18;

    public static List<GeneratedRegionIssue> Validate(GeneratedWorldMap map, WorldMapMovementRules rules = null)
    {
        List<GeneratedRegionIssue> issues = new List<GeneratedRegionIssue>();
        if (map == null || !map.IsValid)
            return issues;
        rules = rules ?? WorldMapMovementRules.CreateDefault();
        WorldMapHexGrid grid = map.CreateGrid();
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        int count = grid.CellCount;
        bool Passable(int i) => rules.IsTraversable(layer.GetAtIndex(i));
        void Add(string code, bool error, string message, float x, float y) =>
            issues.Add(new GeneratedRegionIssue { Code = code, Error = error, Message = message, XPercent = x, YPercent = y });
        string At(float x, float y) => $"({x:0.#}%, {y:0.#}%)";

        // H02: старт и точки действия не в непроходимом.
        WorldMapHexCell startCell = grid.CellAtPercent(map.StartXPercent, map.StartYPercent);
        int start = grid.IndexOf(startCell);
        if (!Passable(start))
            Add("H02", true, $"Старт {At(map.StartXPercent, map.StartYPercent)} стоит на непроходимой клетке «{layer.GetAtIndex(start)}».", map.StartXPercent, map.StartYPercent);

        // H01: достижимость от старта средствами движения (без плавания, лодки, лазания).
        bool[] reached = new bool[count];
        Queue<int> queue = new Queue<int>();
        if (Passable(start))
        {
            reached[start] = true;
            queue.Enqueue(start);
        }
        while (queue.Count > 0)
        {
            WorldMapHexCell cell = grid.CellAt(queue.Dequeue());
            for (int d = 0; d < 6; d++)
            {
                WorldMapHexCell next = cell.Neighbor(d);
                if (!grid.IsInside(next))
                    continue;
                int n = grid.IndexOf(next);
                if (reached[n] || !Passable(n))
                    continue;
                reached[n] = true;
                queue.Enqueue(n);
            }
        }

        int required = 0, requiredReached = 0;
        void CheckSite(GeneratedMapSite site, string title, bool isRequired)
        {
            if (site == null || site.Cell < 0 || site.Cell >= count)
                return;
            if (isRequired)
                required++;
            if (!Passable(site.Cell))
                Add("H02", true, $"{title} {At(site.XPercent, site.YPercent)} стоит на непроходимой клетке «{layer.GetAtIndex(site.Cell)}».", site.XPercent, site.YPercent);
            else if (!reached[site.Cell])
                Add("H01", true, $"{title} {At(site.XPercent, site.YPercent)} недостижим(а) от старта без пересечения воды или скал: нет моста, брода или тропы.", site.XPercent, site.YPercent);
            else if (isRequired)
                requiredReached++;
        }
        if (map.Villages != null)
            for (int v = 0; v < map.Villages.Count; v++)
                CheckSite(map.Villages[v], v == 0 ? "Стартовая деревня" : $"Деревня village-{v}", true);
        if (map.Landmarks != null)
            foreach (GeneratedMapSite site in map.Landmarks)
                CheckSite(site, $"Место «{site.Name}» ({site.Kind})", true);
        if (map.WorkSites != null)
            foreach (GeneratedMapSite site in map.WorkSites)
                CheckSite(site, $"Площадка «{site.Name}»", false);
        if (map.Report != null)
            map.Report.RequiredReachable = required == 0 ? 1f : requiredReached / (float)required;

        // H04: пролёт моста — суша с обеих сторон, на пути дороги.
        if (map.BridgeSpans != null)
        {
            for (int i = 0; i + 1 < map.BridgeSpans.Count; i += 2)
            {
                MapPointData a = map.BridgeSpans[i], b = map.BridgeSpans[i + 1];
                int ca = grid.IndexOf(grid.CellAtPercent(a.XPercent, a.YPercent));
                int cb = grid.IndexOf(grid.CellAtPercent(b.XPercent, b.YPercent));
                if (!Passable(ca) || !Passable(cb))
                    Add("H04", true, $"Мост {At((a.XPercent + b.XPercent) * 0.5f, (a.YPercent + b.YPercent) * 0.5f)}: подход не на проходимой суше.", a.XPercent, a.YPercent);
            }
        }

        // H06: обязательное содержание стоит на карте.
        if (map.Report != null && map.Report.RequiredLocations != null)
        {
            foreach (string id in map.Report.RequiredLocations)
            {
                if (map.Landmarks == null || !map.Landmarks.Any(l => l != null && l.LocationId == id))
                    Add("H06", true, $"Место с содержанием «{id}» не поставлено: ни одна клетка не подошла правилу даже после ослабленных попыток.", 50f, 50f);
            }
        }

        // H09: уникальность ID слотов и районов.
        if (map.SpawnSlots != null)
            foreach (IGrouping<string, GeneratedSpawnSlot> g in map.SpawnSlots.Where(s => s != null && !string.IsNullOrEmpty(s.Id)).GroupBy(s => s.Id).Where(g => g.Count() > 1))
                Add("H09", true, $"Слот «{g.Key}» повторяется {g.Count()} раза.", g.First().MinXPercent, g.First().MinYPercent);
        if (map.Actors != null)
            foreach (IGrouping<string, GeneratedMapActor> g in map.Actors.Where(a => a != null).GroupBy(a => a.ActorId).Where(g => g.Count() > 1))
                Add("H09", true, $"Группа «{g.Key}» повторяется — одна личность не может быть в двух местах.", g.First().HomeXPercent, g.First().HomeYPercent);
        if (map.Districts != null)
            foreach (IGrouping<string, GeneratedMapDistrict> g in map.Districts.GroupBy(d => d.Id).Where(g => g.Count() > 1))
                Add("H09", true, $"Район «{g.Key}» повторяется.", g.First().XPercent, g.First().YPercent);

        if (map.Landmarks != null)
        {
            // W03: важное место с единственным основанием обнаружения.
            foreach (GeneratedMapSite site in map.Landmarks)
            {
                bool important = !string.IsNullOrEmpty(site.LocationId) || site.Hidden;
                int signals = site.Signals?.Count ?? 0;
                if (important && signals <= 1)
                    Add("W03", false, $"Место «{site.Name}» {At(site.XPercent, site.YPercent)}: {(signals == 0 ? "нет ни одного основания" : "одно основание («" + GeneratedMapSite.SignalTitle(site.Signals[0]) + "»)")} обнаружить его.", site.XPercent, site.YPercent);
            }

            // W07: слишком много важного рядом.
            foreach (GeneratedMapSite site in map.Landmarks)
            {
                int near = map.Landmarks.Count(o => o != site && grid.DistanceHexes(site.XPercent, site.YPercent, o.XPercent, o.YPercent) <= 4.0);
                if (near >= 3)
                    Add("W07", false, $"У места «{site.Name}» {At(site.XPercent, site.YPercent)} ещё {near} места в 4 клетках — они спорят за внимание.", site.XPercent, site.YPercent);
            }

            // W12: всё видно сразу.
            if (map.Landmarks.Count > 2 && map.Landmarks.All(l => !l.Hidden))
                Add("W12", false, "Все приметные места видны сразу: новые посещения не меняют понимание района.", 50f, 50f);
        }

        // W10: поселение без причины.
        if (map.Villages != null)
            for (int v = 0; v < map.Villages.Count; v++)
                if (string.IsNullOrEmpty(map.Villages[v].Reason))
                    Add("W10", false, $"Деревня village-{v} {At(map.Villages[v].XPercent, map.Villages[v].YPercent)} не объясняет своего положения: рядом нет воды, переправы, развилки, леса, камня или ровной земли.", map.Villages[v].XPercent, map.Villages[v].YPercent);

        // W08: длинный участок дороги без различимой ситуации.
        if (map.Roads != null)
        {
            List<int> situations = new List<int>();
            void Situation(GeneratedMapSite s) { if (s != null) situations.Add(s.Cell); }
            map.Villages?.ForEach(Situation);
            map.Landmarks?.ForEach(Situation);
            map.WorkSites?.ForEach(Situation);
            HashSet<int> bridges = new HashSet<int>();
            if (map.Bridges != null)
                foreach (MapPointData p in map.Bridges)
                    bridges.Add(grid.IndexOf(grid.CellAtPercent(p.XPercent, p.YPercent)));
            byte[] district = DecodeDistricts(map);
            foreach (GeneratedMapLine line in map.Roads)
            {
                if (line?.Cells == null || line.Kind != GeneratedMapLine.RoadKind)
                    continue;
                int run = 0, best = 0, bestCell = -1;
                int lastDistrict = -1;
                foreach (int c in line.Cells)
                {
                    if (c < 0 || c >= count)
                        continue;
                    bool situation = bridges.Contains(c);
                    int dc = district != null && c < district.Length ? district[c] : -1;
                    if (lastDistrict >= 0 && dc != lastDistrict)
                        situation = true;
                    lastDistrict = dc;
                    if (!situation)
                    {
                        WorldMapHexCell hc = grid.CellAt(c);
                        foreach (int s in situations)
                        {
                            if (hc.DistanceTo(grid.CellAt(s)) <= 2)
                            {
                                situation = true;
                                break;
                            }
                        }
                    }
                    run = situation ? 0 : run + 1;
                    if (run > best)
                    {
                        best = run;
                        bestCell = c;
                    }
                }
                if (best > LongRoadWithoutSituation && bestCell >= 0)
                {
                    grid.CellCenter(grid.CellAt(bestCell), out double x, out double y);
                    float px = grid.PixelToPercentX(x), py = grid.PixelToPercentY(y);
                    Add("W08", false, $"Дорога у {At(px, py)}: {best} клеток подряд без вида, места, переправы или смены района.", px, py);
                }
            }
        }
        return issues;
    }

    public static byte[] DecodeDistricts(GeneratedWorldMap map)
    {
        if (map == null || string.IsNullOrEmpty(map.DistrictCells))
            return null;
        try
        {
            return Convert.FromBase64String(map.DistrictCells);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Отпечаток структуры: тип, наборы районов, связи и семейства грубыми
    // корзинами — смена seed с той же топологией даёт тот же отпечаток.
    public static string Fingerprint(GeneratedWorldMap map)
    {
        if (map == null)
            return string.Empty;
        string Bucket(int n) => n <= 0 ? "0" : n == 1 ? "1" : n <= 3 ? "2-3" : "4+";
        StringBuilder b = new StringBuilder();
        b.Append(map.Passport?.Kind ?? "mixed");
        if (map.Districts != null)
        {
            b.Append("|d:");
            foreach (IGrouping<string, GeneratedMapDistrict> g in map.Districts.Where(d => d.Kind != GeneratedMapDistrict.Water).GroupBy(d => d.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
                b.Append(g.Key).Append('=').Append(Bucket(g.Count())).Append(',');
            // Соседство характеров: какие пары районов граничат.
            SortedSet<string> pairs = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < map.Districts.Count; i++)
            {
                foreach (int j in map.Districts[i].Neighbors)
                {
                    if (j <= i || j >= map.Districts.Count)
                        continue;
                    string a = map.Districts[i].Kind, c = map.Districts[j].Kind;
                    if (a == c)
                        continue;
                    pairs.Add(string.CompareOrdinal(a, c) < 0 ? a + "/" + c : c + "/" + a);
                }
            }
            b.Append("|adj:").Append(string.Join(",", pairs));
        }
        GeneratedRegionReport r = map.Report;
        if (r != null)
            b.Append("|L").Append(Bucket(r.Loops)).Append("|B").Append(Bucket(r.Branches)).Append("|X").Append(Bucket(r.Crossings));
        if (map.Villages != null)
        {
            b.Append("|f:");
            foreach (IGrouping<string, GeneratedMapSite> g in map.Villages.GroupBy(v => v.Family ?? string.Empty).OrderBy(g => g.Key, StringComparer.Ordinal))
                b.Append(g.Key).Append('=').Append(Bucket(g.Count())).Append(',');
        }
        return b.ToString();
    }

    // W11: доля различных отпечатков в наборе seed.
    public static float Diversity(IEnumerable<GeneratedWorldMap> maps)
    {
        List<string> prints = maps.Where(m => m != null).Select(m => string.IsNullOrEmpty(m.Passport?.Fingerprint) ? Fingerprint(m) : m.Passport.Fingerprint).ToList();
        return prints.Count == 0 ? 0f : prints.Distinct().Count() / (float)prints.Count;
    }
}
