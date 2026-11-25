using System;
using System.Collections.Generic;

// 12Ф-2 «Вода и уступы» (бриф WORLD_MAP_EDITOR_BRIEF_RU.md §4.1–4.2):
// реки и уступы — линии по опорным точкам автора, броды, озёра «до уровня».
// Нарисованное автором не портится: русла и скалы накладываются при расчёте
// раскладки (AuthoredWorldMapLayout), поэтому реку можно двигать и удалять
// без следов в рельефе.

[Serializable]
public sealed class AuthoredPoint
{
    // Пиксели полотна карты (как у WorldMapHexGrid), y — вниз.
    public float X;
    public float Y;

    public AuthoredPoint()
    {
    }

    public AuthoredPoint(double x, double y)
    {
        X = (float)x;
        Y = (float)y;
    }
}

[Serializable]
public sealed class AuthoredRiver
{
    public string Name = string.Empty;
    // От истока к устью.
    public List<AuthoredPoint> Points = new List<AuthoredPoint>();
    // Ширина в клетках у истока и у устья.
    public float StartWidth = 0.6f;
    public float EndWidth = 1.4f;
}

[Serializable]
public sealed class AuthoredLedge
{
    public List<AuthoredPoint> Points = new List<AuthoredPoint>();
    // Верх обрыва (плато) — слева по ходу линии на экране; выключено — справа.
    public bool UpperLeft = true;
    // Высота обрыва — доля обычной высоты уступа MapLab.
    public float Height = 1f;
    // Концы сходят в склон — по ним можно подняться.
    public bool PassAtEnds = true;
}

public static class AuthoredWorldMapLines
{
    public const float SnapHexes = 1.2f;
    public const float LedgeEndHexes = 1.5f;
    public const float RiverDepth = 0.03f;
    // Река «идёт в гору», если земля под ней поднимается выше уже пройденной
    // низины больше чем на это.
    public const float UphillTolerance = 0.06f;

    public static IEnumerable<AuthoredPoint> AllPoints(AuthoredWorldMapData data)
    {
        if (data.Rivers != null)
            foreach (AuthoredRiver r in data.Rivers)
                if (r?.Points != null)
                    foreach (AuthoredPoint p in r.Points)
                        if (p != null)
                            yield return p;
        if (data.Ledges != null)
            foreach (AuthoredLedge l in data.Ledges)
                if (l?.Points != null)
                    foreach (AuthoredPoint p in l.Points)
                        if (p != null)
                            yield return p;
        if (data.Fords != null)
            foreach (AuthoredPoint p in data.Fords)
                if (p != null)
                    yield return p;
    }

    // Сглаживание Чайкина; концы на месте.
    public static List<AuthoredPoint> Smooth(List<AuthoredPoint> points, int iterations = 2)
    {
        List<AuthoredPoint> current = new List<AuthoredPoint>();
        if (points != null)
            foreach (AuthoredPoint p in points)
                if (p != null)
                    current.Add(new AuthoredPoint(p.X, p.Y));
        for (int it = 0; it < iterations && current.Count >= 3; it++)
        {
            List<AuthoredPoint> next = new List<AuthoredPoint> { current[0] };
            for (int i = 0; i + 1 < current.Count; i++)
            {
                AuthoredPoint a = current[i], b = current[i + 1];
                next.Add(new AuthoredPoint(a.X * 0.75 + b.X * 0.25, a.Y * 0.75 + b.Y * 0.25));
                next.Add(new AuthoredPoint(a.X * 0.25 + b.X * 0.75, a.Y * 0.25 + b.Y * 0.75));
            }
            next.Add(current[current.Count - 1]);
            current = next;
        }
        return current;
    }

    public static double Length(List<AuthoredPoint> points)
    {
        double total = 0.0;
        for (int i = 0; i + 1 < points.Count; i++)
            total += Dist(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y);
        return total;
    }

    // Точки вдоль линии с шагом step: (x, y, пройденная доля 0..1, направление).
    public static List<(double X, double Y, double T, double Dx, double Dy)> Sample(List<AuthoredPoint> points, double step)
    {
        List<(double, double, double, double, double)> result = new List<(double, double, double, double, double)>();
        if (points == null || points.Count == 0)
            return result;
        if (points.Count == 1)
        {
            result.Add((points[0].X, points[0].Y, 0.0, 1.0, 0.0));
            return result;
        }
        double total = Math.Max(1e-6, Length(points));
        double walked = 0.0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            AuthoredPoint a = points[i], b = points[i + 1];
            double len = Dist(a.X, a.Y, b.X, b.Y);
            if (len <= 1e-9)
                continue;
            double dx = (b.X - a.X) / len, dy = (b.Y - a.Y) / len;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int s = 0; s < n; s++)
            {
                double f = s / (double)n;
                result.Add((a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, (walked + len * f) / total, dx, dy));
            }
            walked += len;
        }
        AuthoredPoint last = points[points.Count - 1], prev = points[points.Count - 2];
        double l = Math.Max(1e-9, Dist(prev.X, prev.Y, last.X, last.Y));
        result.Add((last.X, last.Y, 1.0, (last.X - prev.X) / l, (last.Y - prev.Y) / l));
        return result;
    }

    // Ближайшая точка ломаной: расстояние, номер отрезка, сама точка.
    public static double Nearest(List<AuthoredPoint> points, double x, double y, out int segment, out double nx, out double ny)
    {
        segment = -1;
        nx = x;
        ny = y;
        double best = double.MaxValue;
        if (points == null)
            return best;
        if (points.Count == 1)
        {
            segment = 0;
            nx = points[0].X;
            ny = points[0].Y;
            return Dist(x, y, nx, ny);
        }
        for (int i = 0; i + 1 < points.Count; i++)
        {
            AuthoredPoint a = points[i], b = points[i + 1];
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            double t = len2 <= 1e-9 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((x - a.X) * vx + (y - a.Y) * vy) / len2));
            double px = a.X + vx * t, py = a.Y + vy * t;
            double d = Dist(x, y, px, py);
            if (d < best)
            {
                best = d;
                segment = i;
                nx = px;
                ny = py;
            }
        }
        return best;
    }

    // Притоки: точка у другой реки прилипает к её руслу.
    public static bool SnapToRiver(AuthoredWorldMapData data, int exceptRiver, ref double x, ref double y)
    {
        if (data.Rivers == null)
            return false;
        double reach = SnapHexes * data.Grid.HexWidth;
        double best = reach;
        double bx = x, by = y;
        bool found = false;
        for (int r = 0; r < data.Rivers.Count; r++)
        {
            if (r == exceptRiver || data.Rivers[r]?.Points == null || data.Rivers[r].Points.Count < 2)
                continue;
            double d = Nearest(Smooth(data.Rivers[r].Points), x, y, out _, out double nx, out double ny);
            if (d < best)
            {
                best = d;
                bx = nx;
                by = ny;
                found = true;
            }
        }
        if (found)
        {
            x = bx;
            y = by;
        }
        return found;
    }

    public static double Dist(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Озеро «до уровня»: связная низина от клетки, где высота не выше
    // высоты клетки + level, становится водой. Больше maxCells — вода
    // разлилась бы по равнине: ничего не меняется, возвращается -1.
    public static int FillLake(AuthoredWorldMapData data, int startIndex, float level, int maxCells)
    {
        byte[] t = data.Terrain;
        float[] h = data.Heights;
        if (startIndex < 0 || startIndex >= t.Length)
            return 0;
        WorldMapHexGrid g = data.Grid;
        float surface = h[startIndex] + Math.Max(0f, level);
        List<int> cells = new List<int>();
        bool[] seen = new bool[t.Length];
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(startIndex);
        seen[startIndex] = true;
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            cells.Add(index);
            if (cells.Count > maxCells)
                return -1;
            WorldMapHexCell cell = g.CellAt(index);
            for (int dir = 0; dir < 6; dir++)
            {
                WorldMapHexCell n = cell.Neighbor(dir);
                if (!g.IsInside(n))
                    continue;
                int ni = g.IndexOf(n);
                if (seen[ni] || h[ni] > surface)
                    continue;
                seen[ni] = true;
                queue.Enqueue(ni);
            }
        }
        foreach (int index in cells)
            t[index] = (byte)WorldMapGameplayTerrainType.Water;
        return cells.Count;
    }

    // Высушить: связная вода от клетки — снова открытая местность.
    public static int Dry(AuthoredWorldMapData data, int startIndex)
    {
        if (startIndex < 0 || startIndex >= data.Terrain.Length ||
            data.Terrain[startIndex] != (byte)WorldMapGameplayTerrainType.Water)
            return 0;
        return AuthoredWorldMapEditing.Fill(data, startIndex, WorldMapGameplayTerrainType.OpenGround);
    }
}

// Раскладка карты для игры и схемы: нарисованное автором + авто-холмы +
// русла рек + скалы уступов + броды. Считается заново при каждой правке
// (дёшево: линейно по клеткам и длине линий).
public sealed class AuthoredWorldMapLayout
{
    public sealed class RiverInfo
    {
        public AuthoredRiver Source;
        public List<AuthoredPoint> Smoothed;
        // Клетки русла от истока к устью.
        public List<int> Cells = new List<int>();
    }

    public sealed class LedgeInfo
    {
        public AuthoredLedge Source;
        public List<AuthoredPoint> Smoothed;
        public List<int> Cells = new List<int>();
    }

    public sealed class FordInfo
    {
        public AuthoredPoint Source;
        public bool Ok;
        public int Entry = -1;
        public int Exit = -1;
        public List<int> Water = new List<int>();
    }

    public byte[] Terrain;
    public float[] Heights;
    public readonly List<RiverInfo> Rivers = new List<RiverInfo>();
    public readonly List<LedgeInfo> Ledges = new List<LedgeInfo>();
    public readonly List<FordInfo> Fords = new List<FordInfo>();
    public readonly List<int> RiverOrder = new List<int>();
    public readonly HashSet<int> RiverSet = new HashSet<int>();
    public readonly List<int> LakeCells = new List<int>();
    public readonly HashSet<int> CliffSet = new HashSet<int>();
    public readonly HashSet<int> FordSet = new HashSet<int>();
    public readonly List<AuthoredWorldMapValidation.Problem> Problems = new List<AuthoredWorldMapValidation.Problem>();

    public static AuthoredWorldMapLayout Compute(AuthoredWorldMapData data)
    {
        AuthoredWorldMapLayout layout = new AuthoredWorldMapLayout();
        WorldMapHexGrid g = data.Grid;
        int count = g.CellCount;
        layout.Terrain = new byte[count];
        layout.Heights = (float[])data.Heights.Clone();
        for (int i = 0; i < count; i++)
            layout.Terrain[i] = (byte)data.EffectiveTerrain(i);

        layout.ComputeRivers(data, g);
        layout.ComputeLedges(data, g);
        layout.ComputeFords(data, g);

        for (int i = 0; i < count; i++)
            if (layout.Terrain[i] == (byte)WorldMapGameplayTerrainType.Water && !layout.RiverSet.Contains(i))
                layout.LakeCells.Add(i);
        return layout;
    }

    private void Problem(WorldMapHexGrid g, double x, double y, string text, bool error = false)
    {
        WorldMapHexCell cell = g.CellAtPixel(x, y);
        Problems.Add(new AuthoredWorldMapValidation.Problem { IsError = error, Text = text, Column = cell.Column, Row = cell.Row });
    }

    private void ComputeRivers(AuthoredWorldMapData data, WorldMapHexGrid g)
    {
        if (data.Rivers == null)
            return;
        double step = g.HexWidth * 0.25;
        for (int r = 0; r < data.Rivers.Count; r++)
        {
            AuthoredRiver river = data.Rivers[r];
            if (river?.Points == null || river.Points.Count < 2)
                continue;
            string name = string.IsNullOrWhiteSpace(river.Name) ? "Река " + (r + 1) : river.Name;
            RiverInfo info = new RiverInfo { Source = river, Smoothed = AuthoredWorldMapLines.Smooth(river.Points) };
            HashSet<int> mine = new HashSet<int>();
            float running = float.MaxValue;
            bool warnedUphill = false;
            foreach ((double x, double y, double t, double _, double _) in AuthoredWorldMapLines.Sample(info.Smoothed, step))
            {
                double width = river.StartWidth + (river.EndWidth - river.StartWidth) * t;
                int center = g.IndexOf(g.CellAtPixel(x, y));
                // Ровное падение воды: русло не выше уже пройденной низины.
                float ground = data.HeightAt(center);
                if (!warnedUphill && running != float.MaxValue && ground > running + AuthoredWorldMapLines.UphillTolerance)
                {
                    Problem(g, x, y, name + ": течёт в гору — русло прорезано сквозь подъём.");
                    warnedUphill = true;
                }
                running = Math.Min(running, ground);
                float bed = Math.Max(0f, running - AuthoredWorldMapLines.RiverDepth);

                List<int> cells = new List<int> { center };
                float radius = (float)Math.Max(0.0, width * 0.5);
                if (radius > 0.5f)
                    foreach ((int index, float _) in AuthoredWorldMapEditing.CellsInBrush(data, x, y, radius, 0f))
                        cells.Add(index);
                foreach (int index in cells)
                {
                    Terrain[index] = (byte)WorldMapGameplayTerrainType.Water;
                    Heights[index] = Math.Min(Heights[index], bed);
                    if (mine.Add(index))
                        info.Cells.Add(index);
                    if (RiverSet.Add(index))
                        RiverOrder.Add(index);
                }
            }
            Rivers.Add(info);
        }
    }

    private void ComputeLedges(AuthoredWorldMapData data, WorldMapHexGrid g)
    {
        if (data.Ledges == null)
            return;
        double step = g.HexWidth * 0.2;
        for (int l = 0; l < data.Ledges.Count; l++)
        {
            AuthoredLedge ledge = data.Ledges[l];
            if (ledge?.Points == null || ledge.Points.Count < 2)
                continue;
            LedgeInfo info = new LedgeInfo { Source = ledge, Smoothed = AuthoredWorldMapLines.Smooth(ledge.Points) };
            double total = AuthoredWorldMapLines.Length(info.Smoothed);
            double margin = ledge.PassAtEnds ? AuthoredWorldMapLines.LedgeEndHexes * g.HexWidth : 0.0;
            HashSet<int> mine = new HashSet<int>();
            foreach ((double x, double y, double t, double _, double _) in AuthoredWorldMapLines.Sample(info.Smoothed, step))
            {
                double along = t * total;
                if (along < margin || along > total - margin)
                    continue;
                int index = g.IndexOf(g.CellAtPixel(x, y));
                if (Terrain[index] == (byte)WorldMapGameplayTerrainType.Water)
                    continue;
                if (mine.Add(index))
                    info.Cells.Add(index);
            }
            if (info.Cells.Count == 0)
            {
                AuthoredPoint p = ledge.Points[0];
                Problem(g, p.X, p.Y, "Уступ " + (l + 1) + ": слишком короткий — скал нет, только склон.");
            }
            foreach (int index in info.Cells)
            {
                Terrain[index] = (byte)WorldMapGameplayTerrainType.Cliffs;
                CliffSet.Add(index);
            }
            Ledges.Add(info);
        }
    }

    // Брод: от точки поперёк ближайшей реки до суши с обеих сторон.
    private void ComputeFords(AuthoredWorldMapData data, WorldMapHexGrid g)
    {
        if (data.Fords == null)
            return;
        for (int f = 0; f < data.Fords.Count; f++)
        {
            AuthoredPoint p = data.Fords[f];
            if (p == null)
                continue;
            FordInfo info = new FordInfo { Source = p };
            Fords.Add(info);
            RiverInfo nearest = null;
            double best = double.MaxValue;
            int segment = -1;
            foreach (RiverInfo river in Rivers)
            {
                double d = AuthoredWorldMapLines.Nearest(river.Smoothed, p.X, p.Y, out int s, out _, out _);
                if (d < best)
                {
                    best = d;
                    nearest = river;
                    segment = s;
                }
            }
            if (nearest == null || best > 2.0 * g.HexWidth || segment < 0 || segment + 1 >= nearest.Smoothed.Count)
            {
                Problem(g, p.X, p.Y, "Брод " + (f + 1) + ": рядом нет реки.");
                continue;
            }
            AuthoredPoint a = nearest.Smoothed[segment], b = nearest.Smoothed[segment + 1];
            double len = Math.Max(1e-9, AuthoredWorldMapLines.Dist(a.X, a.Y, b.X, b.Y));
            double nx = -(b.Y - a.Y) / len, ny = (b.X - a.X) / len;
            int entry = WalkToLand(g, p.X, p.Y, -nx, -ny, info.Water);
            int exit = WalkToLand(g, p.X, p.Y, nx, ny, info.Water);
            if (entry < 0 || exit < 0 || g.CellAt(entry).DistanceTo(g.CellAt(exit)) < 2)
            {
                Problem(g, p.X, p.Y, "Брод " + (f + 1) + ": не дошёл до берега — река слишком широкая.");
                info.Water.Clear();
                continue;
            }
            info.Ok = true;
            info.Entry = entry;
            info.Exit = exit;
            foreach (int index in info.Water)
            {
                Terrain[index] = (byte)WorldMapGameplayTerrainType.Trail;
                FordSet.Add(index);
            }
        }
    }

    private int WalkToLand(WorldMapHexGrid g, double x, double y, double dx, double dy, List<int> water)
    {
        double step = g.HexWidth * 0.2;
        for (int s = 0; s <= 40; s++)
        {
            double px = x + dx * step * s, py = y + dy * step * s;
            if (px < 0 || py < 0 || px > g.CanvasWidth || py > g.CanvasHeight)
                return -1;
            int index = g.IndexOf(g.CellAtPixel(px, py));
            if (Terrain[index] != (byte)WorldMapGameplayTerrainType.Water && Terrain[index] != (byte)WorldMapGameplayTerrainType.Trail)
                return index;
            if (Terrain[index] == (byte)WorldMapGameplayTerrainType.Water && !water.Contains(index))
                water.Add(index);
        }
        return -1;
    }
}
