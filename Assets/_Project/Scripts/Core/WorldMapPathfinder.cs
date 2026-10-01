using System;
using System.Collections.Generic;

// 12И (канон v1.50 §9): поиск пути героя по шестиугольной сетке.
// A* с ценой клетки из WorldMapMovementRules (дороги дешевле сами по себе),
// обход непроходимых клеток, выход к ближайшей доступной клетке, если цель
// недостижима, и сглаживание: из ломаной по центрам клеток выкидываются
// промежуточные точки, если прямой отрезок проходим и не дороже.
// Результат — точки в пикселях полотна; первая — точный старт.
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
        out bool reachedTarget)
    {
        layer = layer ?? WorldMapTerrainLayer.CreateDefault();
        rules = rules ?? WorldMapMovementRules.Current;
        WorldMapHexGrid grid = layer.Grid;

        startX = Clamp(startX, 0.0, grid.CanvasWidth);
        startY = Clamp(startY, 0.0, grid.CanvasHeight);
        targetX = Clamp(targetX, 0.0, grid.CanvasWidth);
        targetY = Clamp(targetY, 0.0, grid.CanvasHeight);

        WorldMapHexCell start = grid.CellAtPixel(startX, startY);
        WorldMapHexCell target = grid.CellAtPixel(targetX, targetY);
        List<PathPoint> result = new List<PathPoint> { new PathPoint(startX, startY) };

        bool targetPassable = IsPassable(layer, rules, target, start);
        if (start.Equals(target))
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

        List<int> cells = Search(layer, rules, start, target, out reachedTarget);
        if (cells == null || cells.Count < 2)
        {
            reachedTarget = false;
            return result;
        }

        List<PathPoint> raw = new List<PathPoint>(cells.Count + 1) { new PathPoint(startX, startY) };
        for (int i = 1; i < cells.Count; i++)
        {
            grid.CellCenter(grid.CellAt(cells[i]), out double x, out double y);
            raw.Add(new PathPoint(x, y));
        }
        if (reachedTarget && targetPassable)
            raw[raw.Count - 1] = new PathPoint(targetX, targetY);

        return Smooth(layer, rules, raw, start);
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

        double minCost = rules.MinPathCost;
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
                if (closed[nextIndex] || !IsPassable(layer, rules, next, start))
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
        for (int i = 1; i < raw.Count; i++)
            prefix[i] = prefix[i - 1] + SegmentCost(layer, rules, raw[i - 1], raw[i], start, out _);

        List<PathPoint> result = new List<PathPoint> { raw[0] };
        int anchor = 0;
        while (anchor < raw.Count - 1)
        {
            int next = anchor + 1;
            // Сначала самая дальняя точка (частый случай — открытое поле),
            // затем жадно вперёд от якоря.
            if (CanShortcut(layer, rules, raw, prefix, anchor, raw.Count - 1, start))
            {
                next = raw.Count - 1;
            }
            else
            {
                for (int j = anchor + 2; j < raw.Count - 1; j++)
                {
                    if (!CanShortcut(layer, rules, raw, prefix, anchor, j, start))
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
        int from,
        int to,
        WorldMapHexCell start)
    {
        double cost = SegmentCost(layer, rules, raw[from], raw[to], start, out bool clear);
        return clear && cost <= (prefix[to] - prefix[from]) * SmoothingCostTolerance + 1e-9;
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

        double sampleStep = grid.HexRadius * LineSampleFraction;
        int samples = Math.Max(1, (int)Math.Ceiling(length / sampleStep));
        double pieceHexes = length / samples / grid.HexWidth;
        double cost = 0.0;
        for (int i = 0; i < samples; i++)
        {
            double t = (i + 0.5) / samples;
            WorldMapHexCell cell = grid.CellAtPixel(a.X + dx * t, a.Y + dy * t);
            if (!IsPassable(layer, rules, cell, start))
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
        return cell.Equals(start) || rules.IsTraversable(layer.Get(cell));
    }

    private static double CellCost(WorldMapTerrainLayer layer, WorldMapMovementRules rules, WorldMapHexCell cell)
    {
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
