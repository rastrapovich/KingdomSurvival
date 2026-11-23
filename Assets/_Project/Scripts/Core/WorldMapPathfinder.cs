using System;
using System.Collections.Generic;

// 12И (канон v1.50 §9): поиск пути героя по шестиугольной сетке.
// A* с ценой клетки из WorldMapMovementRules (дороги дешевле сами по себе),
// обход непроходимых клеток, выход к ближайшей доступной клетке, если цель
// недостижима, и сглаживание: из ломаной по центрам клеток выкидываются
// промежуточные точки, если прямой отрезок проходим и не дороже.
// Результат — точки в пикселях полотна; первая — точный старт.
// preferCheapTerrain = false — прямой путь героя без поиска выгоды.
// crossings (22.11.2026) — переправы через реки: их клетки непроходимы для
// обычных шагов, а переход идёт только по ребру «вход → выход» пролёта.
// null — прежнее поведение.
public static class WorldMapPathfinder
{
    public readonly struct PathPoint
    {
        public readonly double X;
        public readonly double Y;

        public PathPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    // Отрезок проверяется с шагом в долю радиуса клетки.
    private const double LineSampleFraction = 0.4;
    // Сглаживание не должно делать путь заметно дороже исходного.
    private const double SmoothingCostTolerance = 1.02;

    public static List<PathPoint> FindPath(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        double startX,
        double startY,
        double targetX,
        double targetY,
        out bool reachedTarget,
        bool preferCheapTerrain = true,
        WorldMapCrossings crossings = null)
    {
        bool previous = uniformCost;
        WorldMapCrossings previousCrossings = activeCrossings;
        uniformCost = !preferCheapTerrain;
        activeCrossings = crossings != null && crossings.Count > 0 ? crossings : null;
        try
        {
            return FindPathCore(layer, rules, startX, startY, targetX, targetY, out reachedTarget);
        }
        finally
        {
            uniformCost = previous;
            activeCrossings = previousCrossings;
        }
    }

    // Проходим ли отрезок (без клеток переправы, кроме старта) — для
    // постобработки маршрута у мостов.
    public static bool IsSegmentClear(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        WorldMapCrossings crossings,
        double ax,
        double ay,
        double bx,
        double by,
        double startX,
        double startY)
    {
        layer = layer ?? WorldMapTerrainLayer.CreateDefault();
        rules = rules ?? WorldMapMovementRules.Current;
        WorldMapCrossings previous = activeCrossings;
        activeCrossings = crossings != null && crossings.Count > 0 ? crossings : null;
        try
        {
            return IsStraightLineClear(layer, rules, ax, ay, bx, by, layer.Grid.CellAtPixel(startX, startY));
        }
        finally
        {
            activeCrossings = previous;
        }
    }

    // Прямой путь героя (22.11.2026): местность не влияет на выбор пути —
    // все проходимые клетки стоят одинаково. Прямая до цели берётся без
    // поиска, иначе — геометрически кратчайший обход непроходимого.
    [ThreadStatic] private static bool uniformCost;
    [ThreadStatic] private static WorldMapCrossings activeCrossings;

    private static List<PathPoint> FindPathCore(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        double startX,
        double startY,
        double targetX,
        double targetY,
        out bool reachedTarget)
    {
        layer = layer ?? WorldMapTerrainLayer.CreateDefault();
        rules = rules ?? WorldMapMovementRules.Current;
        WorldMapHexGrid grid = layer.Grid;

        startX = Clamp(startX, 0.0, grid.CanvasWidth);
        startY = Clamp(startY, 0.0, grid.CanvasHeight);
        targetX = Clamp(targetX, 0.0, grid.CanvasWidth);
        targetY = Clamp(targetY, 0.0, grid.CanvasHeight);

        // Цель на стене обрыва встаёт у её края снаружи.
        if (WallsOn)
        {
            float wallX = grid.PixelToPercentX(targetX), wallY = grid.PixelToPercentY(targetY);
            if (WorldMapWalls.Blocks(wallX, wallY))
            {
                WorldMapWalls.PushOut(ref wallX, ref wallY);
                targetX = Clamp(grid.PercentToPixelX(wallX), 0.0, grid.CanvasWidth);
                targetY = Clamp(grid.PercentToPixelY(wallY), 0.0, grid.CanvasHeight);
            }
        }

        WorldMapHexCell start = grid.CellAtPixel(startX, startY);
        WorldMapHexCell target = grid.CellAtPixel(targetX, targetY);
        List<PathPoint> result = new List<PathPoint> { new PathPoint(startX, startY) };

        // Клетка уступа проходима вне стены: точная цель принимается.
        bool targetPassable = IsPassable(layer, rules, target, start) || (IsLedge(grid, target) && !WallAt(grid, targetX, targetY));
        // Старт и цель в одной клетке: напрямую, если между ними нет стены обрыва.
        if (start.Equals(target) && !LegCrossesWall(grid, startX, startY, targetX, targetY))
        {
            reachedTarget = true;
            if (targetPassable || IsStraightLineClear(layer, rules, startX, startY, targetX, targetY, start))
                result.Add(new PathPoint(targetX, targetY));
            return result;
        }

        // Неразмеченная карта — сплошная открытая местность: прямая без A*.
        if (layer.IsEmpty && rules.IsTraversable(WorldMapGameplayTerrainType.OpenGround))
        {
            reachedTarget = true;
            result.Add(new PathPoint(targetX, targetY));
            return result;
        }

        if (uniformCost && targetPassable && IsStraightLineClear(layer, rules, startX, startY, targetX, targetY, start))
        {
            reachedTarget = true;
            result.Add(new PathPoint(targetX, targetY));
            return result;
        }

        // Стены обрыва: поиск идёт по клеткам вне уступа, а точный старт и цель
        // привязываются к ближайшей видимой (без стены между) клетке поиска.
        WorldMapHexCell searchStart = Anchor(layer, rules, startX, startY, start, true);
        WorldMapHexCell searchTarget = Anchor(layer, rules, targetX, targetY, target, false);
        bool startAnchored = !searchStart.Equals(start);
        bool targetAnchored = !searchTarget.Equals(target);

        List<int> cells = Search(layer, rules, searchStart, searchTarget, out reachedTarget);
        if (cells == null || (cells.Count < 2 && !startAnchored && !targetAnchored && !(WallsOn && reachedTarget && targetPassable)))
        {
            reachedTarget = false;
            return result;
        }

        List<PathPoint> raw = new List<PathPoint>(cells.Count + 2) { new PathPoint(startX, startY) };
        bool exactTarget = reachedTarget && targetPassable;
        // Центр клетки старта остаётся точкой пути, если от точного старта до
        // следующей точки между ними стена.
        bool includeFirst = startAnchored;
        if (!includeFirst && WallsOn)
        {
            if (cells.Count > 1)
            {
                grid.CellCenter(grid.CellAt(cells[1]), out double nx, out double ny);
                includeFirst = LegCrossesWall(grid, startX, startY, nx, ny);
            }
            else if (exactTarget)
                includeFirst = LegCrossesWall(grid, startX, startY, targetX, targetY);
        }
        for (int i = includeFirst ? 0 : 1; i < cells.Count; i++)
        {
            grid.CellCenter(grid.CellAt(cells[i]), out double x, out double y);
            raw.Add(new PathPoint(x, y));
        }
        if (exactTarget)
        {
            // Последняя клетка заменяется точной целью, если между ними нет стены.
            if (targetAnchored || raw.Count < 2 || LegCrossesWall(grid, raw[raw.Count - 2].X, raw[raw.Count - 2].Y, targetX, targetY))
                raw.Add(new PathPoint(targetX, targetY));
            else
                raw[raw.Count - 1] = new PathPoint(targetX, targetY);
        }
        if (raw.Count < 2)
        {
            reachedTarget = false;
            return result;
        }

        return Smooth(layer, rules, raw, start);
    }

    private static bool WallsOn => WorldMapWalls.Count > 0;

    // Отрезок между точками пикселей задевает стену обрыва.
    private static bool LegCrossesWall(WorldMapHexGrid grid, double ax, double ay, double bx, double by) =>
        WallsOn && WorldMapWalls.Crosses(
            grid.PixelToPercentX(ax), grid.PixelToPercentY(ay),
            grid.PixelToPercentX(bx), grid.PixelToPercentY(by));

    // Шаг поиска между центрами соседних клеток задевает стену обрыва.
    private static bool StepCrossesWall(WorldMapHexGrid grid, WorldMapHexCell a, WorldMapHexCell b)
    {
        if (!WallsOn)
            return false;
        grid.CellCenter(a, out double ax, out double ay);
        grid.CellCenter(b, out double bx, out double by);
        return LegCrossesWall(grid, ax, ay, bx, by);
    }

    private static bool WallAt(WorldMapHexGrid grid, double x, double y) =>
        WallsOn && WorldMapWalls.Blocks(grid.PixelToPercentX(x), grid.PixelToPercentY(y));

    private static bool IsLedge(WorldMapHexGrid grid, WorldMapHexCell cell) =>
        WallsOn && WorldMapWalls.IsLedgeCell(grid.IndexOf(cell));

    // Центр клетки виден из точки: ни он, ни отрезок до него не задевают стену.
    private static bool Visible(WorldMapHexGrid grid, double x, double y, WorldMapHexCell cell)
    {
        grid.CellCenter(cell, out double cx, out double cy);
        return !WorldMapWalls.Crosses(
            grid.PixelToPercentX(x), grid.PixelToPercentY(y),
            grid.PixelToPercentX(cx), grid.PixelToPercentY(cy));
    }

    // Клетка поиска для точной точки. Обычно — её клетка. Клетка уступа и
    // клетка, центр которой за стеной от точки, — ближайшая из соседних
    // проходимых клеток вне уступа, центр которой виден из точки. Старт на
    // самой стене (leaving) видимости не требует: герой должен выйти.
    private static WorldMapHexCell Anchor(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        double x,
        double y,
        WorldMapHexCell cell,
        bool leaving)
    {
        if (!WallsOn)
            return cell;
        WorldMapHexGrid grid = layer.Grid;
        bool onWall = leaving && WallAt(grid, x, y);
        if (!IsLedge(grid, cell) && (!rules.IsTraversable(layer.Get(cell)) || onWall || Visible(grid, x, y, cell)))
            return cell;

        WorldMapHexCell best = cell;
        double bestDistance = double.MaxValue;
        for (int pass = 0; pass < 2 && bestDistance == double.MaxValue; pass++)
        {
            bool needVisible = pass == 0 && !onWall;
            for (int direction = 0; direction < 6; direction++)
            {
                WorldMapHexCell next = cell.Neighbor(direction);
                if (!grid.IsInside(next) || IsLedge(grid, next) || !rules.IsTraversable(layer.Get(next)))
                    continue;
                if (activeCrossings != null && activeCrossings.IsBlockedCell(grid.IndexOf(next)))
                    continue;
                if (needVisible && !Visible(grid, x, y, next))
                    continue;
                grid.CellCenter(next, out double cx, out double cy);
                double d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = next;
                }
            }
        }
        return best;
    }

    private static List<int> Search(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        WorldMapHexCell start,
        WorldMapHexCell target,
        out bool reachedTarget)
    {
        WorldMapHexGrid grid = layer.Grid;
        int count = grid.CellCount;
        double[] g = new double[count];
        int[] parent = new int[count];
        bool[] closed = new bool[count];
        for (int i = 0; i < count; i++)
        {
            g[i] = double.MaxValue;
            parent[i] = -1;
        }

        double minCost = uniformCost ? 1.0 : rules.MinPathCost;
        int startIndex = grid.IndexOf(start);
        int targetIndex = grid.IndexOf(target);
        bool targetPassable = IsPassable(layer, rules, target, start);

        MinHeap open = new MinHeap();
        g[startIndex] = 0.0;
        open.Push(startIndex, start.DistanceTo(target) * minCost);

        int best = startIndex;
        int bestDistance = start.DistanceTo(target);
        double bestG = 0.0;

        while (open.Count > 0)
        {
            int current = open.Pop();
            if (closed[current])
                continue;
            closed[current] = true;

            WorldMapHexCell cell = grid.CellAt(current);
            int distance = cell.DistanceTo(target);
            if (distance < bestDistance || (distance == bestDistance && g[current] < bestG))
            {
                best = current;
                bestDistance = distance;
                bestG = g[current];
            }

            if (current == targetIndex)
                break;

            for (int direction = 0; direction < 6; direction++)
            {
                WorldMapHexCell next = cell.Neighbor(direction);
                if (!grid.IsInside(next))
                    continue;
                int nextIndex = grid.IndexOf(next);
                if (closed[nextIndex] || !IsPassable(layer, rules, next, start) || StepCrossesWall(grid, cell, next))
                    continue;

                // Цена шага — среднее двух клеток: граница местности посередине.
                double step = 0.5 * (CellCost(layer, rules, cell) + CellCost(layer, rules, next));
                double candidate = g[current] + step;
                if (candidate >= g[nextIndex])
                    continue;
                g[nextIndex] = candidate;
                parent[nextIndex] = current;
                open.Push(nextIndex, candidate + next.DistanceTo(target) * minCost);
            }

            // Пролёты переправ: вход ↔ выход, цена — длина пролёта в клетках
            // на цену дороги (в прямом пути — просто длина).
            List<WorldMapCrossings.Edge> jumps = activeCrossings?.EdgesFrom(current);
            if (jumps != null)
            {
                foreach (WorldMapCrossings.Edge jump in jumps)
                {
                    WorldMapHexCell next = grid.CellAt(jump.To);
                    if (closed[jump.To] || !IsPassable(layer, rules, next, start))
                        continue;
                    double hexes = cell.DistanceTo(next);
                    double candidate = g[current] + hexes * (uniformCost ? 1.0 : rules.PathCost(WorldMapGameplayTerrainType.Road));
                    if (candidate >= g[jump.To])
                        continue;
                    g[jump.To] = candidate;
                    parent[jump.To] = current;
                    open.Push(jump.To, candidate + next.DistanceTo(target) * minCost);
                }
            }
        }

        int end = closed[targetIndex] && targetPassable ? targetIndex : best;
        reachedTarget = end == targetIndex;

        List<int> path = new List<int>();
        for (int index = end; index >= 0; index = parent[index])
            path.Add(index);
        path.Reverse();
        return path;
    }

    private static List<PathPoint> Smooth(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        List<PathPoint> raw,
        WorldMapHexCell start)
    {
        if (raw.Count <= 2)
            return raw;

        double[] prefix = new double[raw.Count];
        // Шаги по пролёту переправы сглаживание не трогает: считаем их
        // отдельно и не даём срезать углы через них.
        int[] spanSteps = new int[raw.Count];
        for (int i = 1; i < raw.Count; i++)
        {
            bool jump = IsSpanStep(layer.Grid, raw[i - 1], raw[i]);
            spanSteps[i] = spanSteps[i - 1] + (jump ? 1 : 0);
            prefix[i] = prefix[i - 1] + (jump ? 0.0 : SegmentCost(layer, rules, raw[i - 1], raw[i], start, out _));
        }

        List<PathPoint> result = new List<PathPoint> { raw[0] };
        int anchor = 0;
        while (anchor < raw.Count - 1)
        {
            int next = anchor + 1;
            // Сначала самая дальняя точка (частый случай — открытое поле),
            // затем жадно вперёд от якоря.
            if (CanShortcut(layer, rules, raw, prefix, spanSteps, anchor, raw.Count - 1, start))
            {
                next = raw.Count - 1;
            }
            else
            {
                for (int j = anchor + 2; j < raw.Count - 1; j++)
                {
                    if (!CanShortcut(layer, rules, raw, prefix, spanSteps, anchor, j, start))
                        break;
                    next = j;
                }
            }
            result.Add(raw[next]);
            anchor = next;
        }
        return result;
    }

    private static bool CanShortcut(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        List<PathPoint> raw,
        double[] prefix,
        int[] spanSteps,
        int from,
        int to,
        WorldMapHexCell start)
    {
        if (spanSteps[to] != spanSteps[from])
            return false;
        double cost = SegmentCost(layer, rules, raw[from], raw[to], start, out bool clear);
        return clear && cost <= (prefix[to] - prefix[from]) * SmoothingCostTolerance + 1e-9;
    }

    private static bool IsSpanStep(WorldMapHexGrid grid, PathPoint a, PathPoint b)
    {
        return activeCrossings != null &&
            activeCrossings.TryGetSpan(
                grid.IndexOf(grid.CellAtPixel(a.X, a.Y)),
                grid.IndexOf(grid.CellAtPixel(b.X, b.Y)),
                out _,
                out _);
    }

    // Цена отрезка в клетках с учётом местности; clear = все клетки проходимы.
    private static double SegmentCost(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        PathPoint a,
        PathPoint b,
        WorldMapHexCell start,
        out bool clear)
    {
        WorldMapHexGrid grid = layer.Grid;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        clear = true;
        if (length <= 1e-9)
            return 0.0;

        // Стена обрыва: отрезок проверяется мелким шагом (угол стены короче шага клеток).
        if (LegCrossesWall(grid, a.X, a.Y, b.X, b.Y))
        {
            clear = false;
            return double.MaxValue;
        }

        double sampleStep = grid.HexRadius * LineSampleFraction;
        int samples = Math.Max(1, (int)Math.Ceiling(length / sampleStep));
        double pieceHexes = length / samples / grid.HexWidth;
        double cost = 0.0;
        for (int i = 0; i < samples; i++)
        {
            double t = (i + 0.5) / samples;
            double sampleX = a.X + dx * t, sampleY = a.Y + dy * t;
            WorldMapHexCell cell = grid.CellAtPixel(sampleX, sampleY);
            // Клетка уступа проходима вне стены обрыва (сама стена — проверка отрезка выше).
            if (!IsPassable(layer, rules, cell, start) && !IsLedge(grid, cell))
            {
                clear = false;
                return double.MaxValue;
            }
            cost += pieceHexes * CellCost(layer, rules, cell);
        }
        return cost;
    }

    private static bool IsStraightLineClear(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        double ax,
        double ay,
        double bx,
        double by,
        WorldMapHexCell start)
    {
        SegmentCost(layer, rules, new PathPoint(ax, ay), new PathPoint(bx, by), start, out bool clear);
        return clear;
    }

    // Стартовая клетка всегда проходима: герой, оказавшийся в воде после
    // правки разметки, должен иметь возможность выйти на берег.
    private static bool IsPassable(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        WorldMapHexCell cell,
        WorldMapHexCell start)
    {
        if (cell.Equals(start))
            return true;
        if (!rules.IsTraversable(layer.Get(cell)))
            return false;
        // Клетки переправы — только по пролёту; со старта на мосту — свободно.
        return activeCrossings == null ||
            !activeCrossings.IsBlocked(layer.Grid.IndexOf(cell), layer.Grid.IndexOf(start));
    }

    private static double CellCost(WorldMapTerrainLayer layer, WorldMapMovementRules rules, WorldMapHexCell cell)
    {
        if (uniformCost)
            return 1.0;
        WorldMapGameplayTerrainType terrain = layer.Get(cell);
        return rules.IsTraversable(terrain) ? rules.PathCost(terrain) : 1.0;
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    // Двоичная куча (индекс клетки, приоритет) без внешних зависимостей.
    private sealed class MinHeap
    {
        private readonly List<int> items = new List<int>();
        private readonly List<double> priorities = new List<double>();

        public int Count => items.Count;

        public void Push(int item, double priority)
        {
            items.Add(item);
            priorities.Add(priority);
            int child = items.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (priorities[parent] <= priorities[child])
                    break;
                Swap(parent, child);
                child = parent;
            }
        }

        public int Pop()
        {
            int top = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            priorities[0] = priorities[last];
            items.RemoveAt(last);
            priorities.RemoveAt(last);

            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= items.Count)
                    break;
                int right = left + 1;
                int smallest = right < items.Count && priorities[right] < priorities[left] ? right : left;
                if (priorities[parent] <= priorities[smallest])
                    break;
                Swap(parent, smallest);
                parent = smallest;
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            int item = items[a];
            items[a] = items[b];
            items[b] = item;
            double priority = priorities[a];
            priorities[a] = priorities[b];
            priorities[b] = priority;
        }
    }
}
