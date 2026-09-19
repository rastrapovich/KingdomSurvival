using System;
using System.Collections.Generic;

// WM-T05 ("автоматический выбор быстрейшего маршрута"): единственное место,
// где сравниваются прямой путь и путь через авторскую дорожную сеть, и где
// живёт маленький граф Roads (НЕ A* по скрытой сетке 104×64 — сетка
// остаётся техническим фундаментом измерения расстояния/стоимости и не
// становится дорожным графом, раздел 2 задачи). Pure C#, без UnityEngine —
// собирается в KingdomSurvival.Core (noEngineReferences=true) и тестируется
// напрямую в EditMode.
//
// Единственный публичный вход игры остаётся WorldMapNavigation.FindPath —
// он делегирует сюда, поэтому все существующие потребители (GameState,
// ContinuousExpeditionCommands, WorldMapPopulationService, UI-превью
// маршрута) получают новое поведение автоматически, без собственных правок.
public static class WorldMapRoutePlanner
{
    // Раздел 14 задачи: при почти равной стоимости выбираем прямой путь —
    // герой не должен делать визуально бессмысленный крюк ради
    // микроскопической экономии. Явная, маленькая, не настраиваемая
    // пользователем константа (раздел 14 — новую настройку не вводить).
    private const double RoadPreferenceEpsilonHours = 0.01;

    // Раздел 7 задачи: две дороги считаются соединёнными только через общую
    // authored-точку в пределах этого технического допуска (в логических
    // клетках, та же метрика, что и весь остальной planner) — визуальное
    // пересечение линий БЕЗ общей точки НЕ становится перекрёстком.
    public const double JunctionMergeToleranceCells = 0.05;

    public readonly struct PointF
    {
        public readonly float X;
        public readonly float Y;

        public PointF(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    // ------------------------------------------------------------------
    // Публичные точки входа
    // ------------------------------------------------------------------

    // Раздел 3/14 задачи: прямой кандидат + дорожный кандидат (если сеть
    // есть и пригодна) → сравнение по игровому времени → лучший маршрут,
    // ресэмплированный в нормальный movement route (раздел 5 — critical:
    // graph node != movement segment).
    public static List<MapPointData> FindFastestRoute(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent,
        WorldMapDefinitionData definition)
    {
        float startX = WorldMapNavigation.ClampMapX(startXPercent);
        float startY = WorldMapNavigation.ClampMapY(startYPercent);
        float targetX = WorldMapNavigation.ClampMapX(targetXPercent);
        float targetY = WorldMapNavigation.ClampMapY(targetYPercent);

        if (DistanceCellsBetween(startX, startY, targetX, targetY) <= 0.0001)
            return new List<MapPointData> { new MapPointData(startXPercent, startYPercent) };

        List<PointF> directPolyline = new List<PointF> { new PointF(startX, startY), new PointF(targetX, targetY) };
        double directHours = EstimateGeometricPathHours(directPolyline, definition);

        List<PointF> chosen = directPolyline;

        List<PointF> roadPolyline = TryFindRoadPolyline(startX, startY, targetX, targetY, definition, out double roadHours);
        if (roadPolyline != null && roadHours + RoadPreferenceEpsilonHours < directHours)
            chosen = roadPolyline;

        return FinishRoute(chosen, startXPercent, startYPercent, targetXPercent, targetYPercent);
    }

    // Раздел 4 задачи: старая прямая логика — сохранена как отдельный
    // helper, используется и как fallback без дорог, и как off-road участок
    // до/после дороги (через BuildMovementRouteAlongPolyline ниже).
    public static List<MapPointData> BuildDirectPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent)
    {
        float startX = WorldMapNavigation.ClampMapX(startXPercent);
        float startY = WorldMapNavigation.ClampMapY(startYPercent);
        float targetX = WorldMapNavigation.ClampMapX(targetXPercent);
        float targetY = WorldMapNavigation.ClampMapY(targetYPercent);

        if (DistanceCellsBetween(startX, startY, targetX, targetY) <= 0.0001)
            return new List<MapPointData> { new MapPointData(startXPercent, startYPercent) };

        List<PointF> polyline = new List<PointF> { new PointF(startX, startY), new PointF(targetX, targetY) };
        return FinishRoute(polyline, startXPercent, startYPercent, targetXPercent, targetYPercent);
    }

    private static List<MapPointData> FinishRoute(
        List<PointF> polyline,
        float startXPercent, float startYPercent,
        float targetXPercent, float targetYPercent)
    {
        List<MapPointData> route = BuildMovementRouteAlongPolyline(polyline);
        route[0].XPercent = startXPercent;
        route[0].YPercent = startYPercent;
        route[route.Count - 1].XPercent = targetXPercent;
        route[route.Count - 1].YPercent = targetYPercent;
        return route;
    }

    // Раздел 5 задачи (критично): P0→P1→P2→... режется ТАК ЖЕ, как всегда
    // резал старый FindPath один сегмент — distanceCells → ceil → базовые
    // segments → terrain cost subdivisions — применительно к КАЖДОМУ
    // геометрическому отрезку входной полилинии. Так дорожное ребро длиной
    // 20 логических клеток никогда не станет одним movement-сегментом.
    public static List<MapPointData> BuildMovementRouteAlongPolyline(IReadOnlyList<PointF> polyline)
    {
        List<MapPointData> route = new List<MapPointData>();
        if (polyline == null || polyline.Count == 0)
            return route;

        route.Add(new MapPointData(polyline[0].X, polyline[0].Y));

        for (int i = 1; i < polyline.Count; i++)
        {
            PointF a = polyline[i - 1];
            PointF b = polyline[i];
            double distanceCells = DistanceCellsBetween(a.X, a.Y, b.X, b.Y);
            if (distanceCells <= 0.0001)
                continue;

            int baseSegments = Math.Max(1, (int)Math.Ceiling(distanceCells));
            for (int segment = 1; segment <= baseSegments; segment++)
            {
                float fromT = (segment - 1f) / baseSegments;
                float toT = segment / (float)baseSegments;
                float midpointT = (fromT + toT) * 0.5f;
                float midpointX = Lerp(a.X, b.X, midpointT);
                float midpointY = Lerp(a.Y, b.Y, midpointT);
                int terrainCost = WorldMapNavigation.GetTerrainTravelCost(
                    WorldMapNavigation.GetTerrainAtPercent(midpointX, midpointY));

                for (int part = 1; part <= terrainCost; part++)
                {
                    float localT = part / (float)terrainCost;
                    float t = fromT + (toT - fromT) * localT;
                    route.Add(new MapPointData(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t)));
                }
            }
        }

        // Вырожденный случай (вся полилиния — совпадающие точки).
        if (route.Count == 1)
            route.Add(new MapPointData(polyline[polyline.Count - 1].X, polyline[polyline.Count - 1].Y));

        return route;
    }

    // ------------------------------------------------------------------
    // Метрика расстояния и стоимости (раздел 9/10 задачи)
    // ------------------------------------------------------------------

    // Единый helper — тот же, что раньше был продублирован в FindPath и
    // CalculateGeometricDistanceCells (раздел 9 задачи: одна формула).
    public static double DistanceCellsBetween(float x1, float y1, float x2, float y2)
    {
        double dxCells = (x2 - x1) * (WorldMapNavigation.GridWidth - 1) / 100.0;
        double dyCells = (y2 - y1) * (WorldMapNavigation.GridHeight - 1) / 100.0;
        return Math.Sqrt(dxCells * dxCells + dyCells * dyCells);
    }

    public static float GetBaseTravelHoursPerCell(WorldMapDefinitionData definition)
    {
        float hoursPerCell = definition != null
            ? definition.BaseTravelHoursPerCell
            : ContinuousSimulationSystem.DefaultBaseTravelHoursPerCell;
        return hoursPerCell > 0f ? hoursPerCell : ContinuousSimulationSystem.DefaultBaseTravelHoursPerCell;
    }

    // Оценка стоимости ОДНОГО геометрического отрезка в игровых часах —
    // используется и для сравнения direct/road-кандидатов, и как вес ребра
    // road graph. Terrain cost (Hills×2/Mountains×3) и gameplay multiplier
    // (дороги и т.д.) сэмплируются в середине отрезка — тот же принцип, что
    // BuildMovementRouteAlongPolyline использует для реального движения,
    // поэтому оценка планировщика и фактическое движение не расходятся.
    // Раздел 12 задачи: multiplier используется здесь ТОЛЬКО для сравнения
    // кандидатов — не записывается в Route и не меняет CellsPerGameHour;
    // рантайм применяет его отдельно и живо через WorldMapGameplayTerrainQuery.
    private static double EstimateSegmentHours(PointF a, PointF b, WorldMapDefinitionData definition)
    {
        double distanceCells = DistanceCellsBetween(a.X, a.Y, b.X, b.Y);
        if (distanceCells <= 0.0)
            return 0.0;

        float midX = Lerp(a.X, b.X, 0.5f);
        float midY = Lerp(a.Y, b.Y, 0.5f);
        int terrainCost = WorldMapNavigation.GetTerrainTravelCost(WorldMapNavigation.GetTerrainAtPercent(midX, midY));
        float multiplier = WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, midX, midY);
        float hoursPerCell = GetBaseTravelHoursPerCell(definition);

        return distanceCells * hoursPerCell * terrainCost / Math.Max(0.0001f, multiplier);
    }

    public static double EstimateGeometricPathHours(IReadOnlyList<PointF> polyline, WorldMapDefinitionData definition)
    {
        if (polyline == null || polyline.Count < 2)
            return 0.0;

        double total = 0.0;
        for (int i = 1; i < polyline.Count; i++)
            total += EstimateSegmentHours(polyline[i - 1], polyline[i], definition);
        return total;
    }

    // Раздел 20 задачи — единый ETA helper. Route уже разбит на базовые
    // movement-сегменты (~1 клетка каждый, terrain cost уже выражен их
    // количеством — раздел 11: Hills/Mountains не применяются повторно
    // здесь). Домножается только ЖИВОЙ gameplay-multiplier по оставшимся
    // сегментам — то, чего не хватало прежнему GetTravelHoursRemaining.
    // Учитывает частично пройденный текущий сегмент (segmentProgress).
    // RouteDelayHours складывается снаружи, отдельно (как и раньше).
    public static double EstimateRouteTravelHours(
        IReadOnlyList<MapPointData> route,
        int routeIndex,
        double segmentProgress,
        WorldMapDefinitionData definition)
    {
        if (route == null || route.Count <= 1)
            return 0.0;

        float hoursPerCell = GetBaseTravelHoursPerCell(definition);
        double total = 0.0;
        int startIndex = Math.Max(0, routeIndex);

        for (int i = startIndex; i < route.Count - 1; i++)
        {
            MapPointData a = route[i];
            MapPointData b = route[i + 1];
            if (a == null || b == null)
                continue;

            float midX = Lerp(a.XPercent, b.XPercent, 0.5f);
            float midY = Lerp(a.YPercent, b.YPercent, 0.5f);
            float multiplier = WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, midX, midY);
            double segmentHours = hoursPerCell / Math.Max(0.0001f, multiplier);

            double remainingFraction = i == startIndex
                ? Math.Max(0.0, 1.0 - segmentProgress)
                : 1.0;

            total += segmentHours * remainingFraction;
        }

        return total;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ------------------------------------------------------------------
    // Road graph (раздел 6/7/8 задачи)
    // ------------------------------------------------------------------

    private struct GraphEdge
    {
        public int To;
        public double Hours;
    }

    private sealed class RoadGraph
    {
        public readonly List<PointF> NodePoints = new List<PointF>();
        public readonly List<List<GraphEdge>> Adjacency = new List<List<GraphEdge>>();

        public int AddNode(PointF point)
        {
            NodePoints.Add(point);
            Adjacency.Add(new List<GraphEdge>());
            return NodePoints.Count - 1;
        }

        public void AddEdge(int a, int b, double hours)
        {
            Adjacency[a].Add(new GraphEdge { To = b, Hours = hours });
            Adjacency[b].Add(new GraphEdge { To = a, Hours = hours });
        }
    }

    // Раздел 25 задачи: null/NaN/Infinity координаты не должны ронять planner.
    private static bool IsValidPoint(MapPointData p) =>
        p != null &&
        !float.IsNaN(p.XPercent) && !float.IsInfinity(p.XPercent) &&
        !float.IsNaN(p.YPercent) && !float.IsInfinity(p.YPercent);

    // Nodes — авторские Road.Points; edges — соседние точки одной Enabled
    // дороги. Разные дороги соединяются ТОЛЬКО через общую (в пределах
    // JunctionMergeToleranceCells) точку — раздел 7 задачи, никакого
    // автоматического пересечения линий.
    private static RoadGraph BuildRoadGraph(WorldMapDefinitionData definition, out bool hasAnyUsableRoad)
    {
        RoadGraph graph = new RoadGraph();
        hasAnyUsableRoad = false;

        if (definition?.Roads == null)
            return graph;

        foreach (WorldMapRoadDefinition road in definition.Roads)
        {
            if (road == null || !road.Enabled || road.Points == null || road.Points.Count < 2)
                continue;

            int previousNodeId = -1;
            for (int i = 0; i < road.Points.Count; i++)
            {
                MapPointData point = road.Points[i];
                if (!IsValidPoint(point))
                {
                    previousNodeId = -1; // повреждённая точка рвёт дорогу здесь, не ломает planner
                    continue;
                }

                int nodeId = FindOrAddNode(graph, point.XPercent, point.YPercent);

                if (previousNodeId >= 0 && previousNodeId != nodeId)
                {
                    double hours = EstimateSegmentHours(
                        graph.NodePoints[previousNodeId], graph.NodePoints[nodeId], definition);
                    graph.AddEdge(previousNodeId, nodeId, hours);
                    hasAnyUsableRoad = true;
                }

                previousNodeId = nodeId;
            }
        }

        return graph;
    }

    private static int FindOrAddNode(RoadGraph graph, float x, float y)
    {
        for (int i = 0; i < graph.NodePoints.Count; i++)
        {
            if (DistanceCellsBetween(graph.NodePoints[i].X, graph.NodePoints[i].Y, x, y) <=
                JunctionMergeToleranceCells)
            {
                return i;
            }
        }

        return graph.AddNode(new PointF(x, y));
    }

    // Раздел 8 задачи: старт/цель подключаются к дороге через проекцию на
    // ЛЮБУЮ точку ЛЮБОГО пригодного сегмента (не только ближайшую дорогу) —
    // сеть маленькая, маршрут строится редко, полный перебор осознанно
    // предпочтён преждевременной оптимизации. Projection-узлы временные,
    // существуют только для этого расчёта и не пишутся в WorldMapRoadDefinition.
    private static List<PointF> TryFindRoadPolyline(
        float startX, float startY, float targetX, float targetY,
        WorldMapDefinitionData definition, out double roadHours)
    {
        roadHours = double.PositiveInfinity;

        RoadGraph graph = BuildRoadGraph(definition, out bool hasAnyUsableRoad);
        if (!hasAnyUsableRoad || graph.NodePoints.Count == 0)
            return null;

        List<(int A, int B)> roadEdges = new List<(int, int)>();
        for (int a = 0; a < graph.Adjacency.Count; a++)
        {
            foreach (GraphEdge edge in graph.Adjacency[a])
            {
                if (edge.To > a)
                    roadEdges.Add((a, edge.To));
            }
        }

        if (roadEdges.Count == 0)
            return null;

        PointF startPoint = new PointF(startX, startY);
        PointF targetPoint = new PointF(targetX, targetY);
        int startVirtual = graph.AddNode(startPoint);
        int targetVirtual = graph.AddNode(targetPoint);

        foreach ((int a, int b) in roadEdges)
        {
            PointF pointA = graph.NodePoints[a];
            PointF pointB = graph.NodePoints[b];

            PointF entryProjection = ProjectPointOntoSegmentCellSpace(startX, startY, pointA, pointB);
            int entryNode = graph.AddNode(entryProjection);
            graph.AddEdge(entryNode, a, EstimateSegmentHours(entryProjection, pointA, definition));
            graph.AddEdge(entryNode, b, EstimateSegmentHours(entryProjection, pointB, definition));
            graph.AddEdge(startVirtual, entryNode, EstimateSegmentHours(startPoint, entryProjection, definition));

            PointF exitProjection = ProjectPointOntoSegmentCellSpace(targetX, targetY, pointA, pointB);
            int exitNode = graph.AddNode(exitProjection);
            graph.AddEdge(exitNode, a, EstimateSegmentHours(exitProjection, pointA, definition));
            graph.AddEdge(exitNode, b, EstimateSegmentHours(exitProjection, pointB, definition));
            graph.AddEdge(targetVirtual, exitNode, EstimateSegmentHours(exitProjection, targetPoint, definition));

            // Если старт и цель проецируются на ОДИН И ТОТ ЖЕ сегмент, путь
            // не обязан крюком идти через A/B — прямое ребро между
            // проекциями замыкает этот случай корректно.
            graph.AddEdge(entryNode, exitNode, EstimateSegmentHours(entryProjection, exitProjection, definition));
        }

        List<int> path = RunDijkstra(graph, startVirtual, targetVirtual, out double totalHours);
        if (path == null)
            return null;

        roadHours = totalHours;

        List<PointF> polyline = new List<PointF>(path.Count);
        foreach (int nodeId in path)
            polyline.Add(graph.NodePoints[nodeId]);
        return polyline;
    }

    // Проекция в CELL-SPACE, не в raw percent — GridWidth != GridHeight
    // (104×64), поэтому "ближайшая точка отрезка" должна считаться в той же
    // метрике, что и вся остальная стоимость (раздел 9 задачи), иначе
    // проекция была бы геометрически смещена по оси с другим числом клеток.
    private static PointF ProjectPointOntoSegmentCellSpace(float px, float py, PointF a, PointF b)
    {
        double scaleX = (WorldMapNavigation.GridWidth - 1) / 100.0;
        double scaleY = (WorldMapNavigation.GridHeight - 1) / 100.0;

        double axCells = a.X * scaleX, ayCells = a.Y * scaleY;
        double bxCells = b.X * scaleX, byCells = b.Y * scaleY;
        double pxCells = px * scaleX, pyCells = py * scaleY;

        double abx = bxCells - axCells;
        double aby = byCells - ayCells;
        double lengthSquared = abx * abx + aby * aby;

        double t;
        if (lengthSquared <= double.Epsilon)
        {
            t = 0.0;
        }
        else
        {
            double apx = pxCells - axCells;
            double apy = pyCells - ayCells;
            t = (apx * abx + apy * aby) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
        }

        return new PointF(Lerp(a.X, b.X, (float)t), Lerp(a.Y, b.Y, (float)t));
    }

    // O(V^2) Dijkstra без приоритетной очереди — авторская дорожная сеть
    // маленькая (раздел 2/8 задачи), детерминизм важнее асимптотики (раздел
    // 15): узлы перебираются по возрастанию id при равной дистанции, рёбра —
    // в порядке добавления, поэтому результат не зависит от порядка
    // перечисления Dictionary/HashSet, только от порядка Roads/Points в
    // авторских данных.
    private static List<int> RunDijkstra(RoadGraph graph, int source, int target, out double totalHours)
    {
        int n = graph.NodePoints.Count;
        double[] dist = new double[n];
        int[] previous = new int[n];
        bool[] visited = new bool[n];
        for (int i = 0; i < n; i++)
        {
            dist[i] = double.PositiveInfinity;
            previous[i] = -1;
        }
        dist[source] = 0.0;

        for (int iteration = 0; iteration < n; iteration++)
        {
            int current = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (!visited[i] && dist[i] < best)
                {
                    best = dist[i];
                    current = i;
                }
            }

            if (current < 0)
                break;

            visited[current] = true;
            if (current == target)
                break;

            foreach (GraphEdge edge in graph.Adjacency[current])
            {
                double candidate = dist[current] + edge.Hours;
                if (candidate < dist[edge.To] - 1e-9)
                {
                    dist[edge.To] = candidate;
                    previous[edge.To] = current;
                }
            }
        }

        if (double.IsPositiveInfinity(dist[target]))
        {
            totalHours = double.PositiveInfinity;
            return null;
        }

        totalHours = dist[target];
        List<int> path = new List<int>();
        int node = target;
        while (node >= 0)
        {
            path.Add(node);
            node = previous[node];
        }
        path.Reverse();
        return path;
    }
}
