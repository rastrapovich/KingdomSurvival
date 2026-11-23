using System;
using System.Collections.Generic;

[Serializable]
public class MapPointData
{
    public float XPercent;
    public float YPercent;

    public MapPointData(float xPercent, float yPercent)
    {
        XPercent = xPercent;
        YPercent = yPercent;
    }
}

// 12И (канон v1.50 §9, §9.9): единственная точка, через которую игра
// спрашивает карту — где клетка, какая там местность, как туда пройти и
// сколько это займёт. Под капотом — шестиугольная сетка активного мира и
// WorldMapPathfinder. Путь экспедиции (ExpeditionData.Route) — сглаженная
// ломаная от фактической позиции героя; игроку она не показывается.
public static class WorldMapNavigation
{
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

    // Шаг оценки времени и сдвига по пути: местность меняется не чаще клетки.
    private const double SampleStepHexes = 0.25;

    private static WorldMapDefinitionData activeDefinition;
    private static WorldMapTerrainLayer activeLayer;

    // Без авторского мира — сплошная открытая местность, а не скрытый откат
    // к процедурной генерации (канон §9.9).
    public static void ConfigureDefaultTerrain()
    {
        activeDefinition = null;
        activeLayer = WorldMapTerrainLayer.CreateDefault();
    }

    public static void ConfigureFromDefinition(WorldMapDefinitionData definition)
    {
        if (definition == null || !definition.IsValid)
        {
            ConfigureDefaultTerrain();
            return;
        }

        if (activeLayer != null && ReferenceEquals(activeDefinition, definition))
            return;

        activeDefinition = definition;
        activeLayer = definition.CreateTerrainLayer();
    }

    // Тесты и редактор: подставить разметку без авторского мира.
    public static void ConfigureTerrainLayer(WorldMapTerrainLayer layer)
    {
        activeDefinition = null;
        activeLayer = layer ?? WorldMapTerrainLayer.CreateDefault();
    }

    public static bool HasActiveDefinition => activeDefinition != null;

    public static WorldMapDefinitionData ActiveDefinition => activeDefinition;

    public static WorldMapTerrainLayer ActiveLayer
    {
        get
        {
            if (activeLayer == null)
                ConfigureDefaultTerrain();
            return activeLayer;
        }
    }

    public static WorldMapHexGrid Grid => ActiveLayer.Grid;

    // Путь от точной текущей позиции до цели; цель внутри непроходимого
    // места заменяется ближайшей доступной точкой. Всегда хотя бы одна точка —
    // старт; «нечего идти» — одна точка.
    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent) =>
        FindPath(startXPercent, startYPercent, targetXPercent, targetYPercent, true);

    // Галочка «Выгодный путь» на карте (22.11.2026). Выключена — герой идёт
    // строго к точке клика напрямик (обход только непроходимого и построек);
    // включена — прежний поиск: дороги дешевле, путь притягивается к дороге.
    // Выбор игрока; интерфейс выставляет его при запуске и по переключателю.
    public static bool PreferProfitableRoutes;

    // Путь героя по приказу игрока — с учётом галочки «Выгодный путь».
    // Остальные потребители (группы мира, оценки, истории) берут FindPath.
    public static List<MapPointData> FindHeroPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent) =>
        FindPath(startXPercent, startYPercent, targetXPercent, targetYPercent, PreferProfitableRoutes);

    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent,
        bool preferProfitable)
    {
        WorldMapTerrainLayer layer = ActiveLayer;
        WorldMapHexGrid grid = layer.Grid;
        float targetX = ClampMapX(targetXPercent);
        float targetY = ClampMapY(targetYPercent);

        List<MapPointData> route = new List<MapPointData> { new MapPointData(startXPercent, startYPercent) };
        if (grid.DistanceHexes(startXPercent, startYPercent, targetX, targetY) <= 0.0001)
            return route;

        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        WorldMapCrossings crossings = ActiveCrossings;
        List<WorldMapPathfinder.PathPoint> points = WorldMapPathfinder.FindPath(
            layer,
            rules,
            grid.PercentToPixelX(startXPercent),
            grid.PercentToPixelY(startYPercent),
            grid.PercentToPixelX(targetX),
            grid.PercentToPixelY(targetY),
            out _,
            preferProfitable,
            crossings);

        // Переправы: участок «вход → выход» — по настилу моста.
        List<int> decks = null;
        if (crossings != null)
            points = ApplyDecks(points, crossings, layer, rules, out decks);

        for (int i = 1; i < points.Count; i++)
        {
            route.Add(new MapPointData(
                grid.PixelToPercentX(points[i].X),
                grid.PixelToPercentY(points[i].Y)));
        }

        if (route.Count == 2 &&
            grid.DistanceHexes(route[0].XPercent, route[0].YPercent, route[1].XPercent, route[1].YPercent) <= 0.0001)
        {
            route.RemoveAt(1);
            decks = null;
        }
        // 12У-H: основания построек и предметов мест обходятся. Точки
        // настила и всё между ними остаются на месте: подгонка идёт по
        // участкам до и после мостов.
        if (decks == null || decks.Count == 0)
            return ShapeRoute(route, preferProfitable, layer, grid, crossings);

        List<MapPointData> result = new List<MapPointData>();
        int from = 0;
        foreach (int deck in decks)
        {
            result.AddRange(ShapeRoute(route.GetRange(from, deck + 1 - from), preferProfitable, layer, grid, crossings));
            from = deck + 1;
        }
        result.AddRange(ShapeRoute(route.GetRange(from, route.Count - from), preferProfitable, layer, grid, crossings));
        return result;
    }

    private static List<MapPointData> ShapeRoute(
        List<MapPointData> route,
        bool preferProfitable,
        WorldMapTerrainLayer layer,
        WorldMapHexGrid grid,
        WorldMapCrossings crossings)
    {
        List<MapPointData> shaped = preferProfitable ? FollowRoads(route, layer, grid, crossings) : route;
        return WorldMapObstacles.Avoid(shaped, IsTraversableAtPercent);
    }

    // Переправы активного определения; строятся один раз на определение карты
    // (FindPath зовётся часто и для всех групп мира).
    private static WorldMapCrossings cachedCrossings;
    private static WorldMapDefinitionData crossingsDefinition;
    private static WorldMapTerrainLayer crossingsLayer;
    private static int crossingsSpanPoints = -1;

    public static WorldMapCrossings ActiveCrossings
    {
        get
        {
            WorldMapDefinitionData definition = activeDefinition;
            WorldMapTerrainLayer layer = ActiveLayer;
            if (definition == null || definition.BridgeSpans == null || definition.BridgeSpans.Count < 2)
                return null;
            if (!ReferenceEquals(crossingsDefinition, definition) ||
                !ReferenceEquals(crossingsLayer, layer) ||
                crossingsSpanPoints != definition.BridgeSpans.Count)
            {
                crossingsDefinition = definition;
                crossingsLayer = layer;
                crossingsSpanPoints = definition.BridgeSpans.Count;
                cachedCrossings = new WorldMapCrossings(layer, definition.BridgeSpans);
            }
            return cachedCrossings.Count > 0 ? cachedCrossings : null;
        }
    }

    // Настоящий мост рисунка рядом с пролётом плана (середины не дальше
    // полутора клеток) задаёт концы настила; иначе — концы пролёта плана.
    private const double DeckMatchHexes = 1.5;

    private static void DeckEnds(
        WorldMapHexGrid grid,
        WorldMapCrossings.Span span,
        out WorldMapPathfinder.PathPoint entry,
        out WorldMapPathfinder.PathPoint exit)
    {
        entry = new WorldMapPathfinder.PathPoint(span.AX, span.AY);
        exit = new WorldMapPathfinder.PathPoint(span.BX, span.BY);
        double midX = (span.AX + span.BX) * 0.5, midY = (span.AY + span.BY) * 0.5;
        double best = DeckMatchHexes * grid.HexWidth;
        IReadOnlyList<WorldMapBridgeSpan> bridges = WorldMapBridges.Items;
        for (int i = 0; i < bridges.Count; i++)
        {
            WorldMapBridgeSpan bridge = bridges[i];
            double ax = grid.PercentToPixelX(bridge.AXPercent), ay = grid.PercentToPixelY(bridge.AYPercent);
            double bx = grid.PercentToPixelX(bridge.BXPercent), by = grid.PercentToPixelY(bridge.BYPercent);
            double distance = Math.Sqrt(Math.Pow((ax + bx) * 0.5 - midX, 2) + Math.Pow((ay + by) * 0.5 - midY, 2));
            if (distance > best)
                continue;
            best = distance;
            // Концы моста — в порядке концов пролёта плана.
            double straight = Math.Pow(ax - span.AX, 2) + Math.Pow(ay - span.AY, 2) + Math.Pow(bx - span.BX, 2) + Math.Pow(by - span.BY, 2);
            double crossed = Math.Pow(bx - span.AX, 2) + Math.Pow(by - span.AY, 2) + Math.Pow(ax - span.BX, 2) + Math.Pow(ay - span.BY, 2);
            if (straight <= crossed)
            {
                entry = new WorldMapPathfinder.PathPoint(ax, ay);
                exit = new WorldMapPathfinder.PathPoint(bx, by);
            }
            else
            {
                entry = new WorldMapPathfinder.PathPoint(bx, by);
                exit = new WorldMapPathfinder.PathPoint(ax, ay);
            }
        }
    }

    // Пары соседних точек пути, лежащие в концах пролёта, заменяются концами
    // настила; центры клеток входа и выхода убираются, если от соседней
    // точки до конца настила проходимо. deckStarts — индексы первых точек
    // настила в результате (за ними сразу идёт вторая точка).
    private static List<WorldMapPathfinder.PathPoint> ApplyDecks(
        List<WorldMapPathfinder.PathPoint> points,
        WorldMapCrossings crossings,
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        out List<int> deckStarts)
    {
        deckStarts = new List<int>();
        WorldMapHexGrid grid = layer.Grid;
        int n = points.Count;
        bool any = false;
        for (int i = 0; i + 1 < n && !any; i++)
            any = crossings.TryGetSpan(
                grid.IndexOf(grid.CellAtPixel(points[i].X, points[i].Y)),
                grid.IndexOf(grid.CellAtPixel(points[i + 1].X, points[i + 1].Y)),
                out _,
                out _);
        if (!any)
            return points;

        double startX = points[0].X, startY = points[0].Y;
        bool Clear(WorldMapPathfinder.PathPoint a, WorldMapPathfinder.PathPoint b) =>
            WorldMapPathfinder.IsSegmentClear(layer, rules, crossings, a.X, a.Y, b.X, b.Y, startX, startY);

        List<WorldMapPathfinder.PathPoint> result = new List<WorldMapPathfinder.PathPoint>(n + 2);
        bool afterSpan = false;
        int index = 0;
        while (index < n)
        {
            WorldMapPathfinder.PathPoint point = points[index];
            if (index < n - 1 && crossings.TryGetSpan(
                    grid.IndexOf(grid.CellAtPixel(point.X, point.Y)),
                    grid.IndexOf(grid.CellAtPixel(points[index + 1].X, points[index + 1].Y)),
                    out int spanIndex,
                    out bool forward))
            {
                DeckEnds(grid, crossings.Spans[spanIndex], out WorldMapPathfinder.PathPoint a, out WorldMapPathfinder.PathPoint b);
                WorldMapPathfinder.PathPoint first = forward ? a : b;
                WorldMapPathfinder.PathPoint second = forward ? b : a;
                if (index == 0 || !Clear(result[result.Count - 1], first))
                    result.Add(point);
                deckStarts.Add(result.Count);
                result.Add(first);
                result.Add(second);
                afterSpan = true;
                index++;
                continue;
            }
            if (afterSpan && index < n - 1 && Clear(result[result.Count - 1], points[index + 1]))
            {
                afterSpan = false;
                index++;
                continue;
            }
            afterSpan = false;
            result.Add(point);
            index++;
        }
        return result;
    }

    // Взаимодействие только вплотную (22.11.2026): здание, место, группа,
    // цель приказа — на расстоянии не больше радиуса взаимодействия.
    public static double InteractionRadiusHexes => WorldMapMovementRules.Current.SafeInteractionRadiusHexes;

    public static bool IsWithinInteraction(float fromXPercent, float fromYPercent, float toXPercent, float toYPercent) =>
        IsWithinHexes(fromXPercent, fromYPercent, toXPercent, toYPercent, InteractionRadiusHexes);

    // Дошёл ли герой до точки приказа вплотную. Точка на основании постройки
    // засчитывается у её края (путь выталкивает к краю); точка в
    // непроходимой клетке — из соседней с ней клетки.
    public static bool ReachedOrderPoint(float heroXPercent, float heroYPercent, float targetXPercent, float targetYPercent)
    {
        double radius = InteractionRadiusHexes;
        if (DistanceHexes(heroXPercent, heroYPercent, targetXPercent, targetYPercent) <= radius + 0.0001)
            return true;

        int obstacle = WorldMapObstacles.At(targetXPercent, targetYPercent);
        if (obstacle >= 0)
        {
            WorldMapObstacle o = WorldMapObstacles.Items[obstacle];
            float nx = Math.Max(o.XMin, Math.Min(o.XMax, heroXPercent));
            float ny = Math.Max(o.YMin, Math.Min(o.YMax, heroYPercent));
            if (DistanceHexes(heroXPercent, heroYPercent, nx, ny) <= radius + 0.0001)
                return true;
        }

        if (!IsTraversableAtPercent(targetXPercent, targetYPercent))
        {
            WorldMapHexGrid grid = Grid;
            return grid.CellAtPercent(heroXPercent, heroYPercent).DistanceTo(grid.CellAtPercent(targetXPercent, targetYPercent)) <= 1;
        }

        // Цель на мосту: путь выводит героя к ближайшему берегу — оттуда и засчитывается.
        WorldMapCrossings crossings = ActiveCrossings;
        if (crossings != null)
        {
            WorldMapHexGrid grid = Grid;
            WorldMapHexCell targetCell = grid.CellAtPercent(targetXPercent, targetYPercent);
            int targetIndex = grid.IndexOf(targetCell);
            if (crossings.IsBlockedCell(targetIndex))
                return grid.CellAtPercent(heroXPercent, heroYPercent).DistanceTo(targetCell) <=
                    Math.Max(1, crossings.DistanceToNearestEnd(targetIndex));
        }
        return false;
    }

    // 12У-H: на дорожных клетках путь идёт по нарисованной линии дороги —
    // точки пути (через четверть клетки) притягиваются к ближайшей линии
    // дороги или тропы в пределах клетки; вне дорог путь прежний. Начало и
    // конец не двигаются; притяжение не уводит в непроходимую клетку.
    private const double RoadSnapHexes = 0.75;
    private const double RoadStepHexes = 0.25;

    private static List<MapPointData> FollowRoads(
        List<MapPointData> route,
        WorldMapTerrainLayer layer,
        WorldMapHexGrid grid,
        WorldMapCrossings crossings = null)
    {
        List<List<MapPointData>> lines = activeDefinition?.RoadLines;
        if (lines == null || lines.Count == 0 || route.Count < 2)
            return route;
        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        bool OnRoad(float x, float y)
        {
            WorldMapGameplayTerrainType t = layer.GetAtPercent(x, y);
            return t == WorldMapGameplayTerrainType.Road || t == WorldMapGameplayTerrainType.Trail;
        }
        List<MapPointData> result = new List<MapPointData> { route[0] };
        bool changed = false;
        for (int i = 1; i < route.Count; i++)
        {
            MapPointData a = route[i - 1], b = route[i];
            double length = grid.DistanceHexes(a.XPercent, a.YPercent, b.XPercent, b.YPercent);
            int steps = Math.Max(1, (int)Math.Ceiling(length / RoadStepHexes));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                float x = a.XPercent + (b.XPercent - a.XPercent) * t;
                float y = a.YPercent + (b.YPercent - a.YPercent) * t;
                bool last = i == route.Count - 1 && s == steps;
                if (!last && OnRoad(x, y) && NearestOnRoads(lines, grid, x, y, out float sx, out float sy)
                    && rules.IsTraversable(layer.GetAtPercent(sx, sy))
                    && (crossings == null || !crossings.IsBlockedAtPercent(sx, sy)))
                {
                    x = sx;
                    y = sy;
                    changed = true;
                }
                else if (!last && s < steps)
                    continue;
                result.Add(new MapPointData(x, y));
            }
        }
        return changed ? result : route;
    }

    // Отрезки дорог в пикселях, разложенные по корзинам размером в клетку
    // (строятся один раз на определение карты).
    private static object roadIndexOwner;
    private static double roadBucket;
    private static readonly List<double[]> roadSegments = new List<double[]>();
    private static readonly Dictionary<long, List<int>> roadBuckets = new Dictionary<long, List<int>>();

    private static long BucketKey(int bx, int by) => ((long)bx << 32) ^ (uint)by;

    private static void EnsureRoadIndex(List<List<MapPointData>> lines, WorldMapHexGrid grid)
    {
        if (ReferenceEquals(roadIndexOwner, lines))
            return;
        roadIndexOwner = lines;
        roadSegments.Clear();
        roadBuckets.Clear();
        roadBucket = Math.Max(1.0, grid.HexWidth);
        double reach = RoadSnapHexes * grid.HexWidth;
        foreach (List<MapPointData> line in lines)
            for (int k = 1; k < line.Count; k++)
            {
                double ax = grid.PercentToPixelX(line[k - 1].XPercent), ay = grid.PercentToPixelY(line[k - 1].YPercent);
                double bx = grid.PercentToPixelX(line[k].XPercent), by = grid.PercentToPixelY(line[k].YPercent);
                int id = roadSegments.Count;
                roadSegments.Add(new[] { ax, ay, bx, by });
                int x0 = (int)Math.Floor((Math.Min(ax, bx) - reach) / roadBucket), x1 = (int)Math.Floor((Math.Max(ax, bx) + reach) / roadBucket);
                int y0 = (int)Math.Floor((Math.Min(ay, by) - reach) / roadBucket), y1 = (int)Math.Floor((Math.Max(ay, by) + reach) / roadBucket);
                for (int gx = x0; gx <= x1; gx++)
                    for (int gy = y0; gy <= y1; gy++)
                    {
                        long key = BucketKey(gx, gy);
                        if (!roadBuckets.TryGetValue(key, out List<int> list))
                            roadBuckets[key] = list = new List<int>();
                        list.Add(id);
                    }
            }
    }

    private static bool NearestOnRoads(List<List<MapPointData>> lines, WorldMapHexGrid grid, float x, float y, out float sx, out float sy)
    {
        EnsureRoadIndex(lines, grid);
        double px = grid.PercentToPixelX(x), py = grid.PercentToPixelY(y);
        double reach = RoadSnapHexes * grid.HexWidth;
        double best = reach * reach;
        sx = x;
        sy = y;
        bool found = false;
        if (!roadBuckets.TryGetValue(BucketKey((int)Math.Floor(px / roadBucket), (int)Math.Floor(py / roadBucket)), out List<int> candidates))
            return false;
        foreach (int id in candidates)
        {
            {
                double[] seg = roadSegments[id];
                double ax = seg[0], ay = seg[1], bx = seg[2], by = seg[3];
                double dx = bx - ax, dy = by - ay;
                double len2 = dx * dx + dy * dy;
                double u = len2 > 1e-9 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2)) : 0;
                double qx = ax + dx * u, qy = ay + dy * u;
                double d2 = (qx - px) * (qx - px) + (qy - py) * (qy - py);
                if (d2 < best)
                {
                    best = d2;
                    sx = grid.PixelToPercentX(qx);
                    sy = grid.PixelToPercentY(qy);
                    found = true;
                }
            }
        }
        return found;
    }

    // Длина оставшегося пути в клетках, с округлением вверх.
    public static int CalculateRouteCells(List<MapPointData> path, int routeIndex = 0)
    {
        double hexes = PathLengthHexes(path, routeIndex);
        return hexes <= 0.0001 ? 0 : (int)Math.Ceiling(hexes - 0.0001);
    }

    public static double PathLengthHexes(List<MapPointData> path, int fromIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        double total = 0.0;
        for (int i = Math.Max(1, fromIndex + 1); i < path.Count; i++)
            total += DistanceHexes(path[i - 1], path[i]);
        return total;
    }

    public static double CalculateGeometricDistanceCells(List<MapPointData> path) => PathLengthHexes(path);

    public static double DistanceHexes(
        float fromXPercent,
        float fromYPercent,
        float toXPercent,
        float toYPercent) =>
        Grid.DistanceHexes(fromXPercent, fromYPercent, toXPercent, toYPercent);

    public static double DistanceHexes(MapPointData from, MapPointData to) =>
        from == null || to == null ? 0.0 : DistanceHexes(from.XPercent, from.YPercent, to.XPercent, to.YPercent);

    public static bool IsWithinHexes(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent,
        double radiusHexes) =>
        DistanceHexes(firstXPercent, firstYPercent, secondXPercent, secondYPercent) <= radiusHexes + 0.0001;

    public static bool IsWithinDiscoveryRadius(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent) =>
        IsWithinHexes(
            firstXPercent,
            firstYPercent,
            secondXPercent,
            secondYPercent,
            WorldMapMovementRules.Current.DiscoveryRadiusHexes);

    public static WorldMapGameplayTerrainType GetTerrainAtPercent(float xPercent, float yPercent) =>
        ActiveLayer.GetAtPercent(xPercent, yPercent);

    public static bool IsTraversableAtPercent(float xPercent, float yPercent)
    {
        bool traversable = WorldMapMovementRules.Current.IsTraversable(GetTerrainAtPercent(xPercent, yPercent));
        if (WorldMapWalls.Count == 0)
            return traversable;
        // Стена обрыва непроходима; клетка уступа вне стены — проходима.
        if (WorldMapWalls.Blocks(xPercent, yPercent))
            return false;
        return traversable || WorldMapWalls.IsLedgeCell(Grid.IndexOf(Grid.CellAtPercent(xPercent, yPercent)));
    }

    // Игровые часы на оставшийся путь: каждый кусок пути — по местности под ним.
    public static double EstimateTravelHours(
        List<MapPointData> path,
        int routeIndex = 0,
        double segmentProgress = 0.0)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        double hours = 0.0;
        for (int i = Math.Max(0, routeIndex); i < path.Count - 1; i++)
        {
            double startT = i == routeIndex ? Math.Max(0.0, Math.Min(1.0, segmentProgress)) : 0.0;
            hours += SegmentHours(path[i], path[i + 1], startT, rules);
        }
        return hours;
    }

    private static double SegmentHours(MapPointData from, MapPointData to, double startT, WorldMapMovementRules rules)
    {
        double length = DistanceHexes(from, to) * (1.0 - startT);
        if (length <= 0.0)
            return 0.0;

        int samples = Math.Max(1, (int)Math.Ceiling(length / SampleStepHexes));
        double piece = length / samples;
        double hours = 0.0;
        for (int s = 0; s < samples; s++)
        {
            double t = startT + (1.0 - startT) * (s + 0.5) / samples;
            float x = (float)(from.XPercent + (to.XPercent - from.XPercent) * t);
            float y = (float)(from.YPercent + (to.YPercent - from.YPercent) * t);
            hours += piece * rules.HoursPerHex(GetTerrainAtPercent(x, y));
        }
        return hours;
    }

    // Сюжетный или дорожный «короткий путь»: отряд сразу оказывается на
    // cells клеток дальше по своему пути. Пройденные точки попадают в
    // LastTravelPoints, чтобы находки по дороге не терялись.
    public static void AdvanceRouteByCells(ExpeditionData expedition, int cells)
    {
        if (expedition == null || cells <= 0)
            return;

        if (expedition.LastTravelPoints.Count == 0)
        {
            expedition.LastTravelStartedPhase = expedition.Phase;
            expedition.LastTravelTargetLocationId = expedition.LocationId;
            expedition.LastTravelTargetXPercent = expedition.TargetMapXPercent;
            expedition.LastTravelTargetYPercent = expedition.TargetMapYPercent;
        }

        List<MapPointData> route = expedition.Route;
        if (route != null && route.Count > 1)
        {
            float x = expedition.CurrentMapXPercent;
            float y = expedition.CurrentMapYPercent;
            int index = Math.Max(0, Math.Min(route.Count - 1, expedition.RouteIndex));
            double remaining = cells;

            while (remaining > 0.0001 && index < route.Count - 1)
            {
                MapPointData next = route[index + 1];
                double distance = DistanceHexes(x, y, next.XPercent, next.YPercent);
                double step = Math.Min(0.5, Math.Min(remaining, distance));
                if (distance <= 0.0001)
                {
                    index++;
                    continue;
                }

                double t = step / distance;
                x = (float)(x + (next.XPercent - x) * t);
                y = (float)(y + (next.YPercent - y) * t);
                remaining -= step;
                if (step >= distance - 0.0001)
                {
                    x = next.XPercent;
                    y = next.YPercent;
                    index++;
                }
                expedition.LastTravelPoints.Add(new MapPointData(x, y));
            }

            List<MapPointData> rest = new List<MapPointData> { new MapPointData(x, y) };
            for (int i = index + 1; i < route.Count; i++)
                rest.Add(new MapPointData(route[i].XPercent, route[i].YPercent));

            expedition.Route = rest;
            expedition.RouteIndex = 0;
            expedition.CurrentMapXPercent = x;
            expedition.CurrentMapYPercent = y;
        }

        expedition.RemainingRouteCells = CalculateRouteCells(expedition.Route, expedition.RouteIndex);
    }

    public static void AddRouteDelayHours(ExpeditionData expedition, double hours)
    {
        if (expedition == null || hours <= 0.0)
            return;
        expedition.RouteDelayHoursRemaining += hours;
    }

    public static float ClampMapX(float value) =>
        Math.Max(2f, Math.Min(98f, value));

    public static float ClampMapY(float value) =>
        Math.Max(2f, Math.Min(96f, value));
}
