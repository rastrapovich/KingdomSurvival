using System;
using System.Collections.Generic;

// 12У-H: препятствия живой карты — основания построек и предметов мест
// (дома, амбары, столбы, колодцы) в процентах карты. Путь героя и групп их
// обходит: точки пути, попавшие на основание, выталкиваются к ближайшей
// стороне с запасом. Лес препятствием не является. Список задаёт рисунок
// карты (мост MapLab) после сборки; без него путь прежний.
public struct WorldMapObstacle
{
    public float XMin, YMin, XMax, YMax;

    public WorldMapObstacle(float xMin, float yMin, float xMax, float yMax)
    {
        XMin = Math.Min(xMin, xMax);
        YMin = Math.Min(yMin, yMax);
        XMax = Math.Max(xMin, xMax);
        YMax = Math.Max(yMin, yMax);
    }

    public bool Contains(float x, float y, float margin) =>
        x > XMin - margin && x < XMax + margin && y > YMin - margin && y < YMax + margin;
}

public static class WorldMapObstacles
{
    // Запас вокруг основания и шаг проверки пути, % карты.
    public const float Margin = 0.06f;
    private const float Step = 0.05f;
    private const float Bucket = 1f;

    private static readonly List<WorldMapObstacle> items = new List<WorldMapObstacle>();
    private static readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();

    public static int Count => items.Count;

    public static IReadOnlyList<WorldMapObstacle> Items => items;

    public static void Set(IEnumerable<WorldMapObstacle> obstacles)
    {
        items.Clear();
        buckets.Clear();
        if (obstacles == null)
            return;
        // Основания, чьи запасы касаются, сливаются в один блок: обход идёт
        // вокруг ряда домов, а не зацикливается между соседями.
        List<WorldMapObstacle> merged = new List<WorldMapObstacle>();
        foreach (WorldMapObstacle o in obstacles)
            if (o.XMax - o.XMin > 0f && o.YMax - o.YMin > 0f)
                merged.Add(o);
        float gap = Margin * 3.2f;
        bool any = true;
        for (int pass = 0; any && pass < 8; pass++)
        {
            any = false;
            for (int i = 0; i < merged.Count; i++)
                for (int j = merged.Count - 1; j > i; j--)
                {
                    WorldMapObstacle a = merged[i], b = merged[j];
                    if (a.XMin - gap > b.XMax || b.XMin - gap > a.XMax || a.YMin - gap > b.YMax || b.YMin - gap > a.YMax)
                        continue;
                    merged[i] = new WorldMapObstacle(Math.Min(a.XMin, b.XMin), Math.Min(a.YMin, b.YMin), Math.Max(a.XMax, b.XMax), Math.Max(a.YMax, b.YMax));
                    merged.RemoveAt(j);
                    any = true;
                }
        }
        foreach (WorldMapObstacle o in merged)
        {
            int id = items.Count;
            items.Add(o);
            int x0 = (int)Math.Floor((o.XMin - Margin) / Bucket), x1 = (int)Math.Floor((o.XMax + Margin) / Bucket);
            int y0 = (int)Math.Floor((o.YMin - Margin) / Bucket), y1 = (int)Math.Floor((o.YMax + Margin) / Bucket);
            for (int bx = x0; bx <= x1; bx++)
                for (int by = y0; by <= y1; by++)
                {
                    long key = ((long)bx << 32) ^ (uint)by;
                    if (!buckets.TryGetValue(key, out List<int> list))
                        buckets[key] = list = new List<int>();
                    list.Add(id);
                }
        }
    }

    public static void Clear() => Set(null);

    // Препятствие, на основании которого стоит точка (или -1).
    public static int At(float x, float y, float margin = Margin)
    {
        if (items.Count == 0)
            return -1;
        long key = ((long)(int)Math.Floor(x / Bucket) << 32) ^ (uint)(int)Math.Floor(y / Bucket);
        if (!buckets.TryGetValue(key, out List<int> list))
            return -1;
        foreach (int id in list)
            if (items[id].Contains(x, y, margin))
                return id;
        return -1;
    }

    // Путь в обход оснований: отрезки проверяются с шагом Step. Участок,
    // идущий по основанию, заменяется обходом по периметру основания (с
    // запасом) — по более короткой стороне. Точка назначения на основании
    // встаёт рядом с ним. Несколько проходов — обход соседних домов.
    public static List<MapPointData> Avoid(List<MapPointData> route, Func<float, float, bool> traversable)
    {
        if (items.Count == 0 || route == null || route.Count < 2)
            return route;
        List<MapPointData> current = route;
        bool changedAny = false;
        for (int pass = 0; pass < 4; pass++)
        {
            List<MapPointData> next = AvoidOnce(current, traversable, out bool changed);
            if (!changed)
                break;
            changedAny = true;
            current = next;
        }
        return changedAny ? current : route;
    }

    private static List<MapPointData> AvoidOnce(List<MapPointData> route, Func<float, float, bool> traversable, out bool changed)
    {
        changed = false;
        List<MapPointData> dense = new List<MapPointData> { route[0] };
        List<bool> vertex = new List<bool> { true };
        for (int i = 1; i < route.Count; i++)
        {
            MapPointData a = route[i - 1], b = route[i];
            float dx = b.XPercent - a.XPercent, dy = b.YPercent - a.YPercent;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / Step));
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                dense.Add(new MapPointData(a.XPercent + dx * t, a.YPercent + dy * t));
                vertex.Add(s == steps);
            }
        }
        List<MapPointData> result = new List<MapPointData>();
        int k = 0;
        while (k < dense.Count)
        {
            int id = At(dense[k].XPercent, dense[k].YPercent);
            if (id < 0)
            {
                if (vertex[k])
                    result.Add(dense[k]);
                k++;
                continue;
            }
            // Участок по основанию id: от k до end включительно.
            int end = k;
            while (end + 1 < dense.Count && At(dense[end + 1].XPercent, dense[end + 1].YPercent) == id)
                end++;
            WorldMapObstacle o = items[id];
            changed = true;
            if (k == 0 || end == dense.Count - 1)
            {
                // Начало или конец пути на основании — встать рядом.
                MapPointData p = PushOut(o, dense[k == 0 ? 0 : end]);
                if (k == 0)
                    result.Add(p);
                else
                {
                    if (k > 0)
                        result.Add(dense[k - 1]);
                    if (traversable == null || traversable(p.XPercent, p.YPercent))
                        result.Add(p);
                }
                k = end + 1;
                continue;
            }
            MapPointData entry = dense[k - 1], exit = dense[end + 1];
            if (result.Count == 0 || !Same(result[result.Count - 1], entry))
                result.Add(entry);
            foreach (MapPointData corner in Detour(o, entry, exit))
                if (traversable == null || traversable(corner.XPercent, corner.YPercent))
                    result.Add(corner);
            k = end + 1;
            result.Add(dense[k]);
            k++;
        }
        return result;
    }

    private static bool Same(MapPointData a, MapPointData b) =>
        Math.Abs(a.XPercent - b.XPercent) < 1e-5f && Math.Abs(a.YPercent - b.YPercent) < 1e-5f;

    // Обход прямоугольника (с запасом) от проекции entry до проекции exit
    // по периметру — в ту сторону, где путь короче.
    private static List<MapPointData> Detour(WorldMapObstacle o, MapPointData entry, MapPointData exit)
    {
        float m = Margin * 1.5f;
        float x0 = o.XMin - m, y0 = o.YMin - m, x1 = o.XMax + m, y1 = o.YMax + m;
        float w = x1 - x0, h = y1 - y0, perimeter = 2f * (w + h);
        float tEntry = Param(x0, y0, x1, y1, entry), tExit = Param(x0, y0, x1, y1, exit);
        float forward = tExit - tEntry;
        if (forward < 0f)
            forward += perimeter;
        bool clockwise = forward <= perimeter - forward;
        float[] corners = { 0f, w, w + h, 2f * w + h };
        List<MapPointData> points = new List<MapPointData> { Point(x0, y0, x1, y1, tEntry) };
        float distance = clockwise ? forward : perimeter - forward;
        // Углы между точками входа и выхода по выбранному направлению.
        List<(float d, float t)> passed = new List<(float, float)>();
        foreach (float c in corners)
        {
            float d = clockwise ? c - tEntry : tEntry - c;
            if (d < 0f)
                d += perimeter;
            if (d > 1e-4f && d < distance - 1e-4f)
                passed.Add((d, c));
        }
        passed.Sort((a, b) => a.d.CompareTo(b.d));
        foreach ((float _, float t) in passed)
            points.Add(Point(x0, y0, x1, y1, t));
        points.Add(Point(x0, y0, x1, y1, tExit));
        return points;
    }

    // Параметр ближайшей точки периметра: верх (y0) слева направо, правая
    // сторона вниз, низ справа налево, левая сторона вверх.
    private static float Param(float x0, float y0, float x1, float y1, MapPointData p)
    {
        float x = Math.Max(x0, Math.Min(x1, p.XPercent)), y = Math.Max(y0, Math.Min(y1, p.YPercent));
        float w = x1 - x0, h = y1 - y0;
        float dTop = Math.Abs(y - y0), dRight = Math.Abs(x1 - x), dBottom = Math.Abs(y1 - y), dLeft = Math.Abs(x - x0);
        float best = Math.Min(Math.Min(dTop, dRight), Math.Min(dBottom, dLeft));
        if (best == dTop)
            return x - x0;
        if (best == dRight)
            return w + (y - y0);
        if (best == dBottom)
            return w + h + (x1 - x);
        return 2f * w + h + (y1 - y);
    }

    private static MapPointData Point(float x0, float y0, float x1, float y1, float t)
    {
        float w = x1 - x0, h = y1 - y0, perimeter = 2f * (w + h);
        t = ((t % perimeter) + perimeter) % perimeter;
        if (t <= w)
            return new MapPointData(x0 + t, y0);
        if (t <= w + h)
            return new MapPointData(x1, y0 + (t - w));
        if (t <= 2f * w + h)
            return new MapPointData(x1 - (t - w - h), y1);
        return new MapPointData(x0, y1 - (t - 2f * w - h));
    }

    private static MapPointData PushOut(WorldMapObstacle o, MapPointData p)
    {
        float m = Margin * 1.5f;
        float left = p.XPercent - (o.XMin - m);
        float right = (o.XMax + m) - p.XPercent;
        float up = p.YPercent - (o.YMin - m);
        float down = (o.YMax + m) - p.YPercent;
        float best = Math.Min(Math.Min(left, right), Math.Min(up, down));
        if (best == left)
            return new MapPointData(o.XMin - m, p.YPercent);
        if (best == right)
            return new MapPointData(o.XMax + m, p.YPercent);
        if (best == up)
            return new MapPointData(p.XPercent, o.YMin - m);
        return new MapPointData(p.XPercent, o.YMax + m);
    }
}
