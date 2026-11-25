using System;
using System.Collections.Generic;

// 12Ф-3 «Дороги» (бриф WORLD_MAP_EDITOR_BRIEF_RU.md §4.3): автор ставит
// только опорные точки, путь между ними прокладывается сам по цене местности
// и рельефу. Концы прилипают к деревням, лагерю и другим дорогам
// (перекрёсток), пересечение реки даёт мост. Клетки Road/Trail выставляются
// сами при расчёте раскладки — руками дорогу в клетках не рисуют.

[Serializable]
public sealed class AuthoredRoad
{
    public List<AuthoredPoint> Points = new List<AuthoredPoint>();
    // Тропа — уже, рисуется тоньше, проходимость по типу клетки «Тропа».
    public bool Trail;
    // 0 — по рельефу (обходит холмы, лес, болото), 1 — почти по прямой.
    public float Straightness = 0.3f;
    // Не пересекать реки мостом: только обход или брод.
    public bool AvoidWater;
}

public static class AuthoredWorldMapRoads
{
    public const float DefaultSnapHexes = 2f;
    // Цена клетки реки под мостом; озёра и скалы дорога не пересекает.
    public const float BridgeCost = 8f;

    // Цена шага в клетку (без учёта прямоты). Бесконечность — нельзя.
    public static float TerrainCost(WorldMapGameplayTerrainType t, bool trail)
    {
        switch (t)
        {
            case WorldMapGameplayTerrainType.Road: return trail ? 0.5f : 0.35f;
            case WorldMapGameplayTerrainType.Trail: return trail ? 0.6f : 0.8f;
            case WorldMapGameplayTerrainType.Field: return 1f;
            case WorldMapGameplayTerrainType.Forest: return trail ? 1.2f : 1.8f;
            case WorldMapGameplayTerrainType.Swamp: return trail ? 1.8f : 3f;
            case WorldMapGameplayTerrainType.Hills: return trail ? 1.4f : 1.8f;
            case WorldMapGameplayTerrainType.Mountains: return trail ? 5f : 8f;
            case WorldMapGameplayTerrainType.Cliffs: return float.PositiveInfinity;
            case WorldMapGameplayTerrainType.Water: return float.PositiveInfinity;
            default: return 1f;
        }
    }

    // A* от клетки к клетке по раскладке. river — клетки рек (мост),
    // existing — уже проложенные дороги (дешевле: дороги сливаются).
    public static List<int> FindPath(
        WorldMapHexGrid g, byte[] terrain, float[] heights, HashSet<int> river, HashSet<int> existing,
        int from, int to, AuthoredRoad road)
    {
        List<int> path = new List<int>();
        int count = g.CellCount;
        if (from < 0 || to < 0 || from >= count || to >= count)
            return path;
        if (from == to)
        {
            path.Add(from);
            return path;
        }
        float s = Math.Max(0f, Math.Min(1f, road.Straightness));
        float[] cost = new float[count];
        int[] parent = new int[count];
        bool[] closed = new bool[count];
        for (int i = 0; i < count; i++)
        {
            cost[i] = float.PositiveInfinity;
            parent[i] = -1;
        }
        g.CellCenter(g.CellAt(to), out double tx, out double ty);
        double hexWidth = g.HexWidth;
        // Нижняя оценка шага: дорога по дороге при полной прямоте не дешевле 0,35.
        float minStep = 0.3f;
        SortedSet<(float F, int Cell)> open = new SortedSet<(float, int)>();
        cost[from] = 0f;
        open.Add((Heuristic(g, from, tx, ty, hexWidth) * minStep, from));
        while (open.Count > 0)
        {
            (float _, int current) = open.Min;
            open.Remove(open.Min);
            if (closed[current])
                continue;
            closed[current] = true;
            if (current == to)
                break;
            WorldMapHexCell cell = g.CellAt(current);
            for (int dir = 0; dir < 6; dir++)
            {
                WorldMapHexCell n = cell.Neighbor(dir);
                if (!g.IsInside(n))
                    continue;
                int ni = g.IndexOf(n);
                if (closed[ni])
                    continue;
                float step = StepCost(terrain, heights, river, existing, current, ni, road, s);
                if (float.IsInfinity(step))
                    continue;
                float next = cost[current] + step;
                if (next >= cost[ni])
                    continue;
                cost[ni] = next;
                parent[ni] = current;
                open.Add((next + Heuristic(g, ni, tx, ty, hexWidth) * minStep, ni));
            }
        }
        if (parent[to] < 0)
            return path;
        for (int c = to; c >= 0; c = parent[c])
        {
            path.Add(c);
            if (c == from)
                break;
        }
        path.Reverse();
        return path;
    }

    private static float StepCost(byte[] terrain, float[] heights, HashSet<int> river, HashSet<int> existing,
        int from, int to, AuthoredRoad road, float straightness)
    {
        WorldMapGameplayTerrainType t = (WorldMapGameplayTerrainType)terrain[to];
        float baseCost;
        if (river.Contains(to) && t == WorldMapGameplayTerrainType.Water)
        {
            if (road.AvoidWater)
                return float.PositiveInfinity;
            baseCost = BridgeCost * (road.Trail ? 0.8f : 1f);
        }
        else if (existing.Contains(to))
            baseCost = TerrainCost(WorldMapGameplayTerrainType.Road, road.Trail);
        else
            baseCost = TerrainCost(t, road.Trail);
        if (float.IsInfinity(baseCost))
            return baseCost;
        float slope = Math.Abs(heights[to] - heights[from]) * 25f;
        float shaped = baseCost + slope;
        // Прямая дорога почти не смотрит на местность (мосты и скалы — всегда).
        return 1f + (shaped - 1f) * (1f - straightness * 0.9f);
    }

    private static float Heuristic(WorldMapHexGrid g, int cell, double tx, double ty, double hexWidth)
    {
        g.CellCenter(g.CellAt(cell), out double x, out double y);
        return (float)(AuthoredWorldMapLines.Dist(x, y, tx, ty) / hexWidth);
    }

    // Прилипание: к деревне или лагерю, иначе к клетке другой дороги.
    public static bool Snap(AuthoredWorldMapData data, AuthoredWorldMapLayout layout, int exceptRoad,
        float radiusHexes, ref double x, ref double y)
    {
        WorldMapHexGrid g = data.Grid;
        double reach = Math.Max(0.1, radiusHexes) * g.HexWidth;
        double best = reach;
        double qx = x, qy = y;
        double bx = x, by = y;
        bool found = false;
        void Try(double cx, double cy)
        {
            double d = AuthoredWorldMapLines.Dist(qx, qy, cx, cy);
            if (d < best)
            {
                best = d;
                bx = cx;
                by = cy;
                found = true;
            }
        }
        foreach ((double cx, double cy) in NodeCenters(data))
            Try(cx, cy);
        if (!found && layout != null)
        {
            for (int r = 0; r < layout.Roads.Count; r++)
            {
                if (r == exceptRoad)
                    continue;
                foreach (int cell in layout.Roads[r].Cells)
                {
                    g.CellCenter(g.CellAt(cell), out double cx, out double cy);
                    Try(cx, cy);
                }
            }
        }
        if (found)
        {
            x = bx;
            y = by;
        }
        return found;
    }

    // Лагерь и деревни — центры их клеток (лагерь первым).
    public static List<(double X, double Y)> NodeCenters(AuthoredWorldMapData data)
    {
        WorldMapHexGrid g = data.Grid;
        List<(double, double)> nodes = new List<(double, double)>();
        g.CellCenter(new WorldMapHexCell(data.StartColumn, data.StartRow), out double sx, out double sy);
        nodes.Add((sx, sy));
        if (data.Villages != null)
            foreach (AuthoredVillage v in data.Villages)
                if (v != null)
                {
                    g.CellCenter(new WorldMapHexCell(v.Column, v.Row), out double vx, out double vy);
                    nodes.Add((vx, vy));
                }
        return nodes;
    }

    // «Соединить деревни»: недостающие дороги так, чтобы лагерь и все
    // деревни были связаны (остов по расстоянию), петли — объездные пути,
    // выходы — дороги от ближайших поселений к краям карты. Возвращает
    // число добавленных дорог; это обычные дороги, дальше правятся руками.
    public static int ConnectVillages(AuthoredWorldMapData data, float loops, int exits, bool trail, float straightness)
    {
        WorldMapHexGrid g = data.Grid;
        List<(double X, double Y)> nodes = NodeCenters(data);
        int n = nodes.Count;
        int added = 0;
        AuthoredRoad Make(double ax, double ay, double bx, double by)
        {
            AuthoredRoad road = new AuthoredRoad { Trail = trail, Straightness = straightness };
            road.Points.Add(new AuthoredPoint(ax, ay));
            road.Points.Add(new AuthoredPoint(bx, by));
            data.Roads.Add(road);
            added++;
            return road;
        }

        if (n >= 2)
        {
            // Уже связанные дорогами узлы — одна группа.
            AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
            int[] group = new int[n];
            for (int i = 0; i < n; i++)
                group[i] = i;
            int Find(int a) => group[a] == a ? a : group[a] = Find(group[a]);
            int[] component = RoadComponents(g, layout);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    int ci = ComponentNear(g, component, nodes[i].X, nodes[i].Y);
                    int cj = ComponentNear(g, component, nodes[j].X, nodes[j].Y);
                    if (ci >= 0 && ci == cj)
                        group[Find(i)] = Find(j);
                }

            List<(double D, int A, int B)> pairs = new List<(double, int, int)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    pairs.Add((AuthoredWorldMapLines.Dist(nodes[i].X, nodes[i].Y, nodes[j].X, nodes[j].Y), i, j));
            pairs.Sort((a, b) => a.D.CompareTo(b.D));

            // Остов (Краскал) и граф для оценки объезда.
            List<(int, double)>[] graph = new List<(int, double)>[n];
            for (int i = 0; i < n; i++)
                graph[i] = new List<(int, double)>();
            List<(double D, int A, int B)> rest = new List<(double, int, int)>();
            foreach ((double d, int a, int b) in pairs)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb)
                {
                    rest.Add((d, a, b));
                    continue;
                }
                group[ra] = rb;
                graph[a].Add((b, d));
                graph[b].Add((a, d));
                Make(nodes[a].X, nodes[a].Y, nodes[b].X, nodes[b].Y);
            }

            // Петли: пара, путь между которой по сети намного длиннее прямой.
            int loopBudget = (int)Math.Round(Math.Max(0f, Math.Min(1f, loops)) * n);
            foreach ((double d, int a, int b) in rest)
            {
                if (loopBudget <= 0)
                    break;
                if (NetworkDistance(graph, a, b) <= d * 1.7)
                    continue;
                graph[a].Add((b, d));
                graph[b].Add((a, d));
                Make(nodes[a].X, nodes[a].Y, nodes[b].X, nodes[b].Y);
                loopBudget--;
            }
        }

        // Выходы к краю: слева, справа, сверху, снизу — от ближайшего узла.
        int[] order = { 0, 1, 2, 3 };
        for (int e = 0; e < Math.Min(4, Math.Max(0, exits)) && n > 0; e++)
        {
            int side = order[e];
            int bestNode = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double d = side == 0 ? nodes[i].X : side == 1 ? g.CanvasWidth - nodes[i].X : side == 2 ? nodes[i].Y : g.CanvasHeight - nodes[i].Y;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestNode = i;
                }
            }
            (double x, double y) = nodes[bestNode];
            double margin = g.HexWidth * 0.3;
            double ex = side == 0 ? margin : side == 1 ? g.CanvasWidth - margin : x;
            double ey = side == 2 ? margin : side == 3 ? g.CanvasHeight - margin : y;
            Make(x, y, ex, ey);
        }
        return added;
    }

    // Связные группы клеток дорог (-1 — не дорога).
    private static int[] RoadComponents(WorldMapHexGrid g, AuthoredWorldMapLayout layout)
    {
        int[] component = new int[g.CellCount];
        for (int i = 0; i < component.Length; i++)
            component[i] = -1;
        HashSet<int> roadCells = new HashSet<int>();
        foreach (AuthoredWorldMapLayout.RoadInfo road in layout.Roads)
            foreach (int cell in road.Cells)
                roadCells.Add(cell);
        int next = 0;
        foreach (int seed in roadCells)
        {
            if (component[seed] >= 0)
                continue;
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(seed);
            component[seed] = next;
            while (queue.Count > 0)
            {
                WorldMapHexCell cell = g.CellAt(queue.Dequeue());
                for (int dir = 0; dir < 6; dir++)
                {
                    WorldMapHexCell nb = cell.Neighbor(dir);
                    if (!g.IsInside(nb))
                        continue;
                    int ni = g.IndexOf(nb);
                    if (component[ni] >= 0 || !roadCells.Contains(ni))
                        continue;
                    component[ni] = next;
                    queue.Enqueue(ni);
                }
            }
            next++;
        }
        return component;
    }

    // Группа дороги в клетке узла или рядом (до одной клетки).
    private static int ComponentNear(WorldMapHexGrid g, int[] component, double x, double y)
    {
        WorldMapHexCell cell = g.CellAtPixel(x, y);
        int at = component[g.IndexOf(cell)];
        if (at >= 0)
            return at;
        for (int dir = 0; dir < 6; dir++)
        {
            WorldMapHexCell nb = cell.Neighbor(dir);
            if (g.IsInside(nb) && component[g.IndexOf(nb)] >= 0)
                return component[g.IndexOf(nb)];
        }
        return -1;
    }

    private static double NetworkDistance(List<(int To, double D)>[] graph, int from, int to)
    {
        double[] dist = new double[graph.Length];
        bool[] done = new bool[graph.Length];
        for (int i = 0; i < dist.Length; i++)
            dist[i] = double.MaxValue;
        dist[from] = 0.0;
        for (int it = 0; it < graph.Length; it++)
        {
            int u = -1;
            for (int i = 0; i < dist.Length; i++)
                if (!done[i] && (u < 0 || dist[i] < dist[u]))
                    u = i;
            if (u < 0 || dist[u] == double.MaxValue)
                break;
            if (u == to)
                return dist[u];
            done[u] = true;
            foreach ((int v, double d) in graph[u])
                if (dist[u] + d < dist[v])
                    dist[v] = dist[u] + d;
        }
        return dist[to];
    }
}

public sealed partial class AuthoredWorldMapLayout
{
    public sealed class RoadInfo
    {
        public AuthoredRoad Source;
        // Клетки пути по порядку.
        public List<int> Cells = new List<int>();
        // Линия для рисунка: центры клеток, сглаженные.
        public List<AuthoredPoint> Smoothed = new List<AuthoredPoint>();
        public readonly List<CrossingInfo> Crossings = new List<CrossingInfo>();
        public bool Broken;
    }

    // Мост: последняя суша перед рекой, первая после неё и клетки воды.
    public sealed class CrossingInfo
    {
        public int Entry;
        public int Exit;
        public List<int> Water = new List<int>();
    }

    public readonly List<RoadInfo> Roads = new List<RoadInfo>();
    public readonly HashSet<int> RoadSet = new HashSet<int>();

    private void ComputeRoads(AuthoredWorldMapData data, WorldMapHexGrid g)
    {
        if (data.Roads == null)
            return;
        for (int r = 0; r < data.Roads.Count; r++)
        {
            AuthoredRoad road = data.Roads[r];
            if (road?.Points == null || road.Points.Count < 2)
                continue;
            RoadInfo info = new RoadInfo { Source = road };
            for (int i = 0; i + 1 < road.Points.Count; i++)
            {
                int from = g.IndexOf(g.CellAtPixel(road.Points[i].X, road.Points[i].Y));
                int to = g.IndexOf(g.CellAtPixel(road.Points[i + 1].X, road.Points[i + 1].Y));
                List<int> part = AuthoredWorldMapRoads.FindPath(g, Terrain, Heights, RiverSet, RoadSet, from, to, road);
                if (part.Count == 0)
                {
                    info.Broken = true;
                    AuthoredPoint p = road.Points[i + 1];
                    Problem(g, p.X, p.Y, (road.Trail ? "Тропа " : "Дорога ") + (r + 1) + ": не нашла пути" +
                        (road.AvoidWater ? " без воды (нужен брод или выключите «Не пересекать воду»)." : " (вода, скалы)."), true);
                    continue;
                }
                int start = info.Cells.Count > 0 && info.Cells[info.Cells.Count - 1] == part[0] ? 1 : 0;
                for (int k = start; k < part.Count; k++)
                    info.Cells.Add(part[k]);
            }

            // Клетки пути: суша — дорога или тропа, вода реки — мост.
            WorldMapGameplayTerrainType kind = road.Trail ? WorldMapGameplayTerrainType.Trail : WorldMapGameplayTerrainType.Road;
            for (int k = 0; k < info.Cells.Count; k++)
            {
                int cell = info.Cells[k];
                bool water = Terrain[cell] == (byte)WorldMapGameplayTerrainType.Water && RiverSet.Contains(cell);
                if (water)
                {
                    CrossingInfo crossing = new CrossingInfo { Entry = k > 0 ? info.Cells[k - 1] : cell };
                    while (k < info.Cells.Count && Terrain[info.Cells[k]] == (byte)WorldMapGameplayTerrainType.Water && RiverSet.Contains(info.Cells[k]))
                    {
                        crossing.Water.Add(info.Cells[k]);
                        k++;
                    }
                    crossing.Exit = k < info.Cells.Count ? info.Cells[k] : crossing.Water[crossing.Water.Count - 1];
                    info.Crossings.Add(crossing);
                    foreach (int w in crossing.Water)
                    {
                        Terrain[w] = (byte)WorldMapGameplayTerrainType.Road;
                        BridgeSet.Add(w);
                    }
                    k--;
                    continue;
                }
                // Дорога поверх тропы — дорога; тропа по дороге её не портит.
                if (Terrain[cell] != (byte)WorldMapGameplayTerrainType.Road)
                    Terrain[cell] = (byte)kind;
            }
            foreach (int cell in info.Cells)
                RoadSet.Add(cell);

            List<AuthoredPoint> centers = new List<AuthoredPoint>(info.Cells.Count);
            foreach (int cell in info.Cells)
            {
                g.CellCenter(g.CellAt(cell), out double x, out double y);
                centers.Add(new AuthoredPoint(x, y));
            }
            info.Smoothed = AuthoredWorldMapLines.Smooth(centers, 2);
            Roads.Add(info);
        }

        // Деревня без дороги — предупреждение (когда дороги уже есть).
        if (Roads.Count == 0 || data.Villages == null)
            return;
        for (int i = 0; i < data.Villages.Count; i++)
        {
            AuthoredVillage v = data.Villages[i];
            if (v == null)
                continue;
            WorldMapHexCell c = new WorldMapHexCell(v.Column, v.Row);
            if (!g.IsInside(c))
                continue;
            bool near = RoadSet.Contains(g.IndexOf(c));
            for (int dir = 0; dir < 6 && !near; dir++)
            {
                WorldMapHexCell nb = c.Neighbor(dir);
                near = g.IsInside(nb) && RoadSet.Contains(g.IndexOf(nb));
            }
            if (!near)
            {
                g.CellCenter(c, out double x, out double y);
                Problem(g, x, y, (string.IsNullOrWhiteSpace(v.Name) ? "Деревня " + (i + 1) : v.Name) + ": к ней не ведёт дорога.");
            }
        }
    }

    public readonly HashSet<int> BridgeSet = new HashSet<int>();
}
