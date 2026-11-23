using System;
using System.Collections.Generic;

// Стены скальных обрывов живой карты (проценты карты). Рисунок обрыва
// (MapLab) точнее клетки плана: цепочка клеток Cliffs шире стены и не давала
// подойти к кромке сверху и к подножию снизу, а на проходимых концах и в
// просвете уступа стена оказывалась на проходимой земле. Стена — набор
// выпуклых четырёхугольников по линии обрыва (от подножия до края площадки
// с запасом); путь героя и групп её не пересекает, а клетки уступа
// (ledgeCells) проходимы вне стены: подойти к краю можно вплотную. Список
// задаёт рисунок карты (мост MapLab) после сборки; без него — прежние
// непроходимые клетки Cliffs.
public readonly struct WorldMapWall
{
    public readonly float X0, Y0, X1, Y1, X2, Y2, X3, Y3;

    public WorldMapWall(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; X3 = x3; Y3 = y3;
    }

    public float XMin => Math.Min(Math.Min(X0, X1), Math.Min(X2, X3));
    public float XMax => Math.Max(Math.Max(X0, X1), Math.Max(X2, X3));
    public float YMin => Math.Min(Math.Min(Y0, Y1), Math.Min(Y2, Y3));
    public float YMax => Math.Max(Math.Max(Y0, Y1), Math.Max(Y2, Y3));

    // Точка внутри выпуклого четырёхугольника (любой обход).
    public bool Contains(float x, float y)
    {
        float a = Side(X0, Y0, X1, Y1, x, y);
        float b = Side(X1, Y1, X2, Y2, x, y);
        float c = Side(X2, Y2, X3, Y3, x, y);
        float d = Side(X3, Y3, X0, Y0, x, y);
        bool anyNegative = a < 0f || b < 0f || c < 0f || d < 0f;
        bool anyPositive = a > 0f || b > 0f || c > 0f || d > 0f;
        return !(anyNegative && anyPositive);
    }

    private static float Side(float ax, float ay, float bx, float by, float px, float py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);
}

public static class WorldMapWalls
{
    private const float Bucket = 2f;
    // Зазор, на который точка выталкивается за стену, % карты.
    private const float PushGap = 0.02f;

    private static readonly List<WorldMapWall> items = new List<WorldMapWall>();
    private static readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
    private static readonly HashSet<int> ledgeCells = new HashSet<int>();

    public static int Count => items.Count;

    public static IReadOnlyList<WorldMapWall> Items => items;

    public static void Set(IEnumerable<WorldMapWall> walls, IEnumerable<int> cells)
    {
        items.Clear();
        buckets.Clear();
        ledgeCells.Clear();
        if (walls == null)
            return;
        foreach (WorldMapWall wall in walls)
        {
            int id = items.Count;
            items.Add(wall);
            int x0 = (int)Math.Floor(wall.XMin / Bucket), x1 = (int)Math.Floor(wall.XMax / Bucket);
            int y0 = (int)Math.Floor(wall.YMin / Bucket), y1 = (int)Math.Floor(wall.YMax / Bucket);
            for (int bx = x0; bx <= x1; bx++)
                for (int by = y0; by <= y1; by++)
                {
                    long key = ((long)bx << 32) ^ (uint)by;
                    if (!buckets.TryGetValue(key, out List<int> list))
                        buckets[key] = list = new List<int>();
                    list.Add(id);
                }
        }
        if (items.Count > 0 && cells != null)
            foreach (int cell in cells)
                ledgeCells.Add(cell);
    }

    public static void Clear() => Set(null, null);

    // Клетка — часть уступа плана: непроходима для клеточного поиска пути, но
    // пройти можно вне стены (точный путь решают стены).
    public static bool IsLedgeCell(int cellIndex) => items.Count > 0 && ledgeCells.Contains(cellIndex);

    // Точка лежит на стене обрыва.
    public static bool Blocks(float xPercent, float yPercent)
    {
        if (items.Count == 0)
            return false;
        long key = ((long)(int)Math.Floor(xPercent / Bucket) << 32) ^ (uint)(int)Math.Floor(yPercent / Bucket);
        if (!buckets.TryGetValue(key, out List<int> list))
            return false;
        foreach (int id in list)
            if (items[id].Contains(xPercent, yPercent))
                return true;
        return false;
    }

    // Отрезок задевает стену: точное пересечение с каждым четырёхугольником
    // на пути отрезка (выборка точек пропускала угол стены).
    public static bool Crosses(float ax, float ay, float bx, float by)
    {
        if (items.Count == 0)
            return false;
        int x0 = (int)Math.Floor(Math.Min(ax, bx) / Bucket), x1 = (int)Math.Floor(Math.Max(ax, bx) / Bucket);
        int y0 = (int)Math.Floor(Math.Min(ay, by) / Bucket), y1 = (int)Math.Floor(Math.Max(ay, by) / Bucket);
        for (int cx = x0; cx <= x1; cx++)
            for (int cy = y0; cy <= y1; cy++)
            {
                long key = ((long)cx << 32) ^ (uint)cy;
                if (!buckets.TryGetValue(key, out List<int> list))
                    continue;
                foreach (int id in list)
                    if (SegmentHits(items[id], ax, ay, bx, by))
                        return true;
            }
        return false;
    }

    private static bool SegmentHits(WorldMapWall w, float ax, float ay, float bx, float by)
    {
        if (w.Contains(ax, ay) || w.Contains(bx, by))
            return true;
        return SegmentsIntersect(ax, ay, bx, by, w.X0, w.Y0, w.X1, w.Y1)
            || SegmentsIntersect(ax, ay, bx, by, w.X1, w.Y1, w.X2, w.Y2)
            || SegmentsIntersect(ax, ay, bx, by, w.X2, w.Y2, w.X3, w.Y3)
            || SegmentsIntersect(ax, ay, bx, by, w.X3, w.Y3, w.X0, w.Y0);
    }

    // Отрезки пересекаются (общий случай; совпадающие прямые покрыты проверкой концов).
    private static bool SegmentsIntersect(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
    {
        float d1 = Orient(cx, cy, dx, dy, ax, ay), d2 = Orient(cx, cy, dx, dy, bx, by);
        float d3 = Orient(ax, ay, bx, by, cx, cy), d4 = Orient(ax, ay, bx, by, dx, dy);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Orient(float ax, float ay, float bx, float by, float px, float py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    // Точка на стене → ближайшая точка рядом со стеной снаружи (иначе без
    // изменений): вдоль поперечника (подножие ↔ край площадки) части стены
    // шагами, пока точка не выйдет из стены целиком — соседние части
    // перекрываются, ближайшее ребро одной лежит внутри другой.
    public static void PushOut(ref float x, ref float y)
    {
        if (!Blocks(x, y))
            return;
        long key = ((long)(int)Math.Floor(x / Bucket) << 32) ^ (uint)(int)Math.Floor(y / Bucket);
        if (!buckets.TryGetValue(key, out List<int> list))
            return;
        float bestX = x, bestY = y, bestDistance = float.MaxValue;
        foreach (int id in list)
        {
            WorldMapWall w = items[id];
            if (!w.Contains(x, y))
                continue;
            // Поперечник: от стороны площадки (вершины 2, 3) к подножию (0, 1).
            float ax = (w.X0 + w.X1) * 0.5f - (w.X2 + w.X3) * 0.5f, ay = (w.Y0 + w.Y1) * 0.5f - (w.Y2 + w.Y3) * 0.5f;
            float length = (float)Math.Sqrt(ax * ax + ay * ay);
            if (length < 1e-6f)
                continue;
            ax /= length;
            ay /= length;
            for (int sign = -1; sign <= 1; sign += 2)
                for (float r = PushGap; r <= length * 1.2f; r += PushGap)
                {
                    float px = x + ax * r * sign, py = y + ay * r * sign;
                    if (Blocks(px, py))
                        continue;
                    if (r < bestDistance)
                    {
                        bestDistance = r;
                        bestX = px;
                        bestY = py;
                    }
                    break;
                }
        }
        x = bestX;
        y = bestY;
    }
}
