using System;
using System.Collections.Generic;

// 12Ф «Редактор карты» (бриф ProjectDocs/WORLD_MAP_EDITOR_BRIEF_RU.md,
// канон v1.71 §9.9.1): карту свободной игры собирает автор. Здесь — чистые
// данные авторской карты (без UnityEngine), правка кистями и сборка в прежнюю
// структуру GeneratedWorldMap, которую уже читают навигация, места,
// поселения, население мира, сохранения и рисунок MapLab.
//
// Размер карты не ограничен набором: клетка имеет постоянный размер на
// полотне (HexRadiusPixels), карта растёт и уменьшается с любого края
// (Resize) без потери нарисованного.
[Serializable]
public sealed class AuthoredWorldMapData
{
    public const int CurrentVersion = 1;
    // Размер клетки на полотне — как у прежней карты 100 клеток на 4160 px.
    public const float HexRadiusPixels = 24f;
    public const int MinColumns = WorldMapHexGrid.MinHexesAcross;
    public const int MaxColumns = WorldMapHexGrid.MaxHexesAcross;
    public const int MinRows = 6;
    public const int MaxRows = 400;
    public const float DefaultHillsFrom = 0.3f;
    public const float DefaultMountainsFrom = 0.65f;

    public int Version = CurrentVersion;
    public string Name = "Карта свободной игры";
    // Клеток по ширине и рядов по высоте.
    public int Columns = 60;
    public int Rows = 44;
    // Типы клеток (WorldMapTerrainLayer.Encode) — то, что нарисовал автор.
    public string TerrainCells = string.Empty;
    // Высоты клеток 0..1, ushort little-endian в base64.
    public string HeightCells = string.Empty;
    // Холмы и горы на открытой местности — сами по высоте.
    public bool AutoReliefTerrain = true;
    public float HillsFrom = DefaultHillsFrom;
    public float MountainsFrom = DefaultMountainsFrom;
    // Старт отряда (лагерь у дороги) — клетка.
    public int StartColumn = 30;
    public int StartRow = 22;
    // Деревни (поселения): клетка, название автора, размер 1–3.
    public List<AuthoredVillage> Villages = new List<AuthoredVillage>();
    // 12Ф-2: реки и уступы — линии по точкам автора (пиксели полотна), броды.
    public List<AuthoredRiver> Rivers = new List<AuthoredRiver>();
    public List<AuthoredLedge> Ledges = new List<AuthoredLedge>();
    public List<AuthoredPoint> Fords = new List<AuthoredPoint>();
    // 12Ф-3: дороги и тропы — опорные точки, путь прокладывается сам.
    public List<AuthoredRoad> Roads = new List<AuthoredRoad>();

    [NonSerialized] private byte[] terrain;
    [NonSerialized] private float[] heights;
    [NonSerialized] private WorldMapHexGrid grid;

    public static AuthoredWorldMapData CreateDefault(int columns = 60, int rows = 44)
    {
        AuthoredWorldMapData data = new AuthoredWorldMapData
        {
            Columns = ClampColumns(columns),
            Rows = ClampRows(rows)
        };
        data.StartColumn = data.Columns / 2;
        data.StartRow = data.Rows / 2;
        // Умное начало: две деревни недалеко от лагеря, чтобы новая карта
        // сразу была играбельной. Автор двигает или удаляет их.
        data.Villages.Add(new AuthoredVillage { Column = data.StartColumn + 6, Row = data.StartRow - 4, Size = 2 });
        data.Villages.Add(new AuthoredVillage { Column = data.StartColumn - 7, Row = data.StartRow + 6, Size = 1 });
        data.Commit();
        return data;
    }

    public static int ClampColumns(int value) => Math.Max(MinColumns, Math.Min(MaxColumns, value));
    public static int ClampRows(int value) => Math.Max(MinRows, Math.Min(MaxRows, value));

    // Полотно подобрано так, чтобы сетка дала ровно Columns × Rows клеток.
    public float CanvasWidth => (float)(ClampColumns(Columns) * WorldMapHexGrid.Sqrt3 * HexRadiusPixels);
    public float CanvasHeight => (float)((ClampRows(Rows) - 1.5) * 1.5 * HexRadiusPixels);

    public WorldMapHexGrid Grid
    {
        get
        {
            if (grid == null || grid.HexesAcross != ClampColumns(Columns) || Math.Abs(grid.CanvasHeight - CanvasHeight) > 0.01f)
                grid = new WorldMapHexGrid(CanvasWidth, CanvasHeight, ClampColumns(Columns));
            return grid;
        }
    }

    public int CellCount => Grid.CellCount;

    // Сбросить разобранные массивы (после Undo или правки строк в инспекторе).
    public void Invalidate()
    {
        terrain = null;
        heights = null;
        grid = null;
    }

    public byte[] Terrain
    {
        get
        {
            EnsureDecoded();
            return terrain;
        }
    }

    public float[] Heights
    {
        get
        {
            EnsureDecoded();
            return heights;
        }
    }

    private void EnsureDecoded()
    {
        WorldMapHexGrid g = Grid;
        if (terrain != null && terrain.Length == g.CellCount && heights != null && heights.Length == g.CellCount)
            return;
        terrain = WorldMapTerrainLayer.Decode(g, TerrainCells).CopyCells();
        heights = DecodeHeights(HeightCells, g.CellCount);
    }

    // Записать массивы обратно в сериализуемые строки.
    public void Commit()
    {
        EnsureDecoded();
        TerrainCells = new WorldMapTerrainLayer(Grid, terrain).Encode();
        HeightCells = EncodeHeights(heights);
        StartColumn = Math.Max(0, Math.Min(Grid.Columns - 1, StartColumn));
        StartRow = Math.Max(0, Math.Min(Grid.Rows - 1, StartRow));
    }

    public WorldMapGameplayTerrainType TerrainAt(int index) =>
        index >= 0 && index < Terrain.Length ? (WorldMapGameplayTerrainType)Terrain[index] : WorldMapGameplayTerrainType.OpenGround;

    public float HeightAt(int index) => index >= 0 && index < Heights.Length ? Heights[index] : 0f;

    // Тип клетки для игры: нарисованный автором или, на открытой местности, —
    // холмы и горы по высоте.
    public WorldMapGameplayTerrainType EffectiveTerrain(int index)
    {
        WorldMapGameplayTerrainType painted = TerrainAt(index);
        if (!AutoReliefTerrain || painted != WorldMapGameplayTerrainType.OpenGround)
            return painted;
        float h = HeightAt(index);
        if (h >= MountainsFrom)
            return WorldMapGameplayTerrainType.Mountains;
        if (h >= HillsFrom)
            return WorldMapGameplayTerrainType.Hills;
        return painted;
    }

    public static string EncodeHeights(float[] values)
    {
        if (values == null || values.Length == 0)
            return string.Empty;
        bool any = false;
        byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            float v = float.IsNaN(values[i]) ? 0f : Math.Max(0f, Math.Min(1f, values[i]));
            ushort u = (ushort)Math.Round(v * ushort.MaxValue);
            any |= u != 0;
            bytes[i * 2] = (byte)(u & 0xff);
            bytes[i * 2 + 1] = (byte)(u >> 8);
        }
        return any ? Convert.ToBase64String(bytes) : string.Empty;
    }

    public static float[] DecodeHeights(string encoded, int count)
    {
        float[] values = new float[Math.Max(0, count)];
        if (string.IsNullOrWhiteSpace(encoded))
            return values;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return values;
        }
        int n = Math.Min(values.Length, bytes.Length / 2);
        for (int i = 0; i < n; i++)
            values[i] = (bytes[i * 2] | bytes[i * 2 + 1] << 8) / (float)ushort.MaxValue;
        return values;
    }
}

[Serializable]
public sealed class AuthoredVillage
{
    public string Name = string.Empty;
    public int Column;
    public int Row;
    public int Size = 1;
}

// Кисти и правка размера. Все операции меняют разобранные массивы; Commit —
// отдельно (окно делает его в конце мазка).
public static class AuthoredWorldMapEditing
{
    public enum HeightBrush
    {
        Raise,
        Lower,
        Flatten,
        Smooth,
        Noise
    }

    // Клетки в радиусе (в клетках) вокруг точки полотна с весом мягкого края.
    public static List<(int Index, float Weight)> CellsInBrush(
        AuthoredWorldMapData data, double pixelX, double pixelY, float radiusHexes, float softness)
    {
        List<(int, float)> result = new List<(int, float)>();
        WorldMapHexGrid g = data.Grid;
        double radiusPx = Math.Max(0.5, radiusHexes) * g.HexWidth;
        WorldMapHexCell center = g.CellAtPixel(pixelX, pixelY);
        int reach = (int)Math.Ceiling(radiusHexes) + 1;
        float soft = Math.Max(0f, Math.Min(1f, softness));
        for (int dr = -reach; dr <= reach; dr++)
        {
            for (int dc = -reach - 1; dc <= reach + 1; dc++)
            {
                WorldMapHexCell cell = new WorldMapHexCell(center.Column + dc, center.Row + dr);
                if (!g.IsInside(cell))
                    continue;
                g.CellCenter(cell, out double x, out double y);
                double d = Math.Sqrt((x - pixelX) * (x - pixelX) + (y - pixelY) * (y - pixelY));
                if (d > radiusPx)
                    continue;
                float t = (float)(d / radiusPx);
                float weight = soft <= 0f ? 1f : t < 1f - soft ? 1f : (float)(0.5 + 0.5 * Math.Cos(Math.PI * (t - (1f - soft)) / soft));
                result.Add((g.IndexOf(cell), weight));
            }
        }
        return result;
    }

    // strength — доля полного подъёма за один шаг мазка (0..1).
    public static void ApplyHeight(
        AuthoredWorldMapData data, HeightBrush brush, double pixelX, double pixelY,
        float radiusHexes, float strength, float softness, float target = 0f,
        bool terraces = false, float terraceStep = 0.1f, int noiseSeed = 0)
    {
        float[] h = data.Heights;
        List<(int Index, float Weight)> cells = CellsInBrush(data, pixelX, pixelY, radiusHexes, softness);
        float s = Math.Max(0f, Math.Min(1f, strength));
        float[] before = brush == HeightBrush.Smooth ? (float[])h.Clone() : null;
        WorldMapHexGrid g = data.Grid;
        foreach ((int index, float weight) in cells)
        {
            float w = weight * s;
            float v = h[index];
            switch (brush)
            {
                case HeightBrush.Raise:
                    v += 0.08f * w;
                    break;
                case HeightBrush.Lower:
                    v -= 0.08f * w;
                    break;
                case HeightBrush.Flatten:
                    v += (target - v) * Math.Min(1f, w * 1.5f);
                    break;
                case HeightBrush.Smooth:
                {
                    WorldMapHexCell cell = g.CellAt(index);
                    float sum = before[index];
                    int count = 1;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        WorldMapHexCell n = cell.Neighbor(dir);
                        if (!g.IsInside(n))
                            continue;
                        sum += before[g.IndexOf(n)];
                        count++;
                    }
                    v += (sum / count - v) * Math.Min(1f, w * 1.5f);
                    break;
                }
                case HeightBrush.Noise:
                    v += (Hash01(index, noiseSeed) - 0.5f) * 0.06f * w;
                    break;
            }
            if (terraces && terraceStep > 0.005f && brush != HeightBrush.Smooth && brush != HeightBrush.Noise)
            {
                float snapped = (float)Math.Round(v / terraceStep) * terraceStep;
                v += (snapped - v) * Math.Min(1f, w * 2f);
            }
            h[index] = Math.Max(0f, Math.Min(1f, v));
        }
    }

    public static void PaintTerrain(
        AuthoredWorldMapData data, WorldMapGameplayTerrainType type, double pixelX, double pixelY,
        float radiusHexes, bool keepWaterAndRoads)
    {
        byte[] t = data.Terrain;
        foreach ((int index, float _) in CellsInBrush(data, pixelX, pixelY, radiusHexes, 0f))
        {
            if (keepWaterAndRoads && IsProtected((WorldMapGameplayTerrainType)t[index]) && !IsProtected(type))
                continue;
            t[index] = (byte)type;
        }
    }

    // Заливка связной области одного типа (как ведро в рисовалке).
    public static int Fill(AuthoredWorldMapData data, int startIndex, WorldMapGameplayTerrainType type)
    {
        byte[] t = data.Terrain;
        if (startIndex < 0 || startIndex >= t.Length || t[startIndex] == (byte)type)
            return 0;
        byte from = t[startIndex];
        WorldMapHexGrid g = data.Grid;
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(startIndex);
        t[startIndex] = (byte)type;
        int filled = 0;
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            filled++;
            WorldMapHexCell cell = g.CellAt(index);
            for (int dir = 0; dir < 6; dir++)
            {
                WorldMapHexCell n = cell.Neighbor(dir);
                if (!g.IsInside(n))
                    continue;
                int ni = g.IndexOf(n);
                if (t[ni] != from)
                    continue;
                t[ni] = (byte)type;
                queue.Enqueue(ni);
            }
        }
        return filled;
    }

    // Изменить размер: добавить (или убрать, если меньше нуля) клетки с
    // каждого края. Нарисованное остаётся на своих местах. Ряды сверху
    // меняются парами, чтобы не сбился сдвиг нечётных рядов сетки.
    public static void Resize(AuthoredWorldMapData data, int addLeft, int addRight, int addTop, int addBottom)
    {
        if ((addTop & 1) != 0)
            addTop += addTop > 0 ? 1 : -1;
        byte[] oldTerrain = data.Terrain;
        float[] oldHeights = data.Heights;
        WorldMapHexGrid oldGrid = data.Grid;

        int columns = AuthoredWorldMapData.ClampColumns(data.Columns + addLeft + addRight);
        int rows = AuthoredWorldMapData.ClampRows(data.Rows + addTop + addBottom);
        data.Columns = columns;
        data.Rows = rows;
        data.Invalidate();
        data.TerrainCells = string.Empty;
        data.HeightCells = string.Empty;
        WorldMapHexGrid newGrid = data.Grid;
        byte[] t = data.Terrain;
        float[] h = data.Heights;
        for (int i = 0; i < oldGrid.CellCount; i++)
        {
            WorldMapHexCell c = oldGrid.CellAt(i);
            WorldMapHexCell moved = new WorldMapHexCell(c.Column + addLeft, c.Row + addTop);
            if (!newGrid.IsInside(moved))
                continue;
            int ni = newGrid.IndexOf(moved);
            t[ni] = oldTerrain[i];
            h[ni] = oldHeights[i];
        }
        data.StartColumn += addLeft;
        data.StartRow += addTop;
        data.Villages ??= new List<AuthoredVillage>();
        for (int i = data.Villages.Count - 1; i >= 0; i--)
        {
            AuthoredVillage v = data.Villages[i];
            if (v == null) { data.Villages.RemoveAt(i); continue; }
            v.Column += addLeft;
            v.Row += addTop;
            if (!newGrid.IsInside(new WorldMapHexCell(v.Column, v.Row)))
                data.Villages.RemoveAt(i);
        }
        // Линии и броды — в пикселях полотна: сдвиг на добавленные клетки.
        float shiftX = (float)(addLeft * oldGrid.HexWidth);
        float shiftY = (float)(addTop * oldGrid.RowStep);
        foreach (AuthoredPoint p in AuthoredWorldMapLines.AllPoints(data))
        {
            p.X += shiftX;
            p.Y += shiftY;
        }
        data.Commit();
    }

    private static bool IsProtected(WorldMapGameplayTerrainType type) =>
        type == WorldMapGameplayTerrainType.Water || type == WorldMapGameplayTerrainType.Road || type == WorldMapGameplayTerrainType.Trail;

    private static float Hash01(int index, int seed)
    {
        unchecked
        {
            uint x = (uint)(index * 374761393 + seed * 668265263);
            x = (x ^ (x >> 13)) * 1274126177u;
            x ^= x >> 16;
            return (x & 0xffffff) / (float)0x1000000;
        }
    }
}

// Сборка авторской карты в карту партии.
public static class AuthoredWorldMapBuilder
{
    public static GeneratedWorldMap Build(AuthoredWorldMapData data, int seed)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        WorldMapHexGrid g = data.Grid;
        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        byte[] effective = layout.Terrain;

        WorldMapHexCell start = new WorldMapHexCell(
            Math.Max(0, Math.Min(g.Columns - 1, data.StartColumn)),
            Math.Max(0, Math.Min(g.Rows - 1, data.StartRow)));
        g.CellCenter(start, out double sx, out double sy);

        GeneratedWorldMap map = new GeneratedWorldMap
        {
            Seed = seed,
            CanvasWidth = data.CanvasWidth,
            CanvasHeight = data.CanvasHeight,
            HexesAcross = AuthoredWorldMapData.ClampColumns(data.Columns),
            TerrainCells = new WorldMapTerrainLayer(g, effective).Encode(),
            Authored = true,
            // Высоты — с прорезанными руслами рек.
            HeightCells = AuthoredWorldMapData.EncodeHeights(layout.Heights),
            LakeCells = new List<int>(layout.LakeCells),
            RiverCells = new List<int>(layout.RiverOrder),
            StartXPercent = Clamp01To100(g.PixelToPercentX(sx)),
            StartYPercent = Clamp01To100(g.PixelToPercentY(sy))
        };
        map.Elevation = (float[])layout.Heights.Clone();

        // 12Ф-2: реки — сглаженная линия автора и клетки русла; ширина
        // растёт от истока к устью.
        for (int r = 0; r < layout.Rivers.Count; r++)
        {
            AuthoredWorldMapLayout.RiverInfo info = layout.Rivers[r];
            map.Rivers.Add(new GeneratedMapLine
            {
                Kind = GeneratedMapLine.RiverKind,
                StartWidthHexes = info.Source.StartWidth,
                EndWidthHexes = info.Source.EndWidth,
                Points = ToPercent(g, info.Smoothed),
                Cells = new List<int>(info.Cells)
            });
        }
        // Уступы: линия обрыва и клетки скал (без проходимых концов).
        foreach (AuthoredWorldMapLayout.LedgeInfo info in layout.Ledges)
        {
            map.Ledges.Add(new GeneratedMapLedge
            {
                Points = ToPercent(g, info.Smoothed),
                Cells = new List<int>(info.Cells),
                UpperSide = info.Source.UpperLeft ? 1f : -1f,
                HeightScale = Math.Max(0.3f, Math.Min(2.5f, info.Source.Height))
            });
        }
        // 12Ф-3: дороги и тропы — линия по клеткам пути; мосты — пролёт
        // «берег → берег» для навигации и клетки моста для рисунка.
        foreach (AuthoredWorldMapLayout.RoadInfo road in layout.Roads)
        {
            if (road.Cells.Count < 2)
                continue;
            map.Roads.Add(new GeneratedMapLine
            {
                Kind = road.Source.Trail ? GeneratedMapLine.TrailKind : GeneratedMapLine.RoadKind,
                Points = ToPercent(g, road.Smoothed),
                Cells = new List<int>(road.Cells)
            });
            foreach (AuthoredWorldMapLayout.CrossingInfo crossing in road.Crossings)
            {
                g.CellCenter(g.CellAt(crossing.Entry), out double ex, out double ey);
                g.CellCenter(g.CellAt(crossing.Exit), out double xx, out double xy);
                map.BridgeSpans.Add(new MapPointData(g.PixelToPercentX(ex), g.PixelToPercentY(ey)));
                map.BridgeSpans.Add(new MapPointData(g.PixelToPercentX(xx), g.PixelToPercentY(xy)));
                foreach (int w in crossing.Water)
                {
                    g.CellCenter(g.CellAt(w), out double wx, out double wy);
                    map.Bridges.Add(new MapPointData(g.PixelToPercentX(wx), g.PixelToPercentY(wy)));
                }
            }
        }
        // Броды: пролёт «берег → берег» для навигации (мост не рисуется).
        foreach (AuthoredWorldMapLayout.FordInfo ford in layout.Fords)
        {
            if (!ford.Ok)
                continue;
            g.CellCenter(g.CellAt(ford.Entry), out double ax, out double ay);
            g.CellCenter(g.CellAt(ford.Exit), out double bx, out double by);
            map.BridgeSpans.Add(new MapPointData(g.PixelToPercentX(ax), g.PixelToPercentY(ay)));
            map.BridgeSpans.Add(new MapPointData(g.PixelToPercentX(bx), g.PixelToPercentY(by)));
        }

        // Как у генератора: первая «деревня» — стоянка отряда (лагерь у
        // дороги, не поселение), затем поселения автора. Номера мест игры
        // (gen-village-N) совпадают с номерами в рисунке MapLab.
        map.Villages.Add(new GeneratedMapSite
        {
            Kind = GeneratedMapSite.VillageKind,
            Cell = g.IndexOf(start),
            XPercent = map.StartXPercent,
            YPercent = map.StartYPercent,
            IsStart = true,
            DrawMark = false
        });
        if (data.Villages != null)
        {
            foreach (AuthoredVillage village in data.Villages)
            {
                if (village == null)
                    continue;
                WorldMapHexCell cell = new WorldMapHexCell(
                    Math.Max(0, Math.Min(g.Columns - 1, village.Column)),
                    Math.Max(0, Math.Min(g.Rows - 1, village.Row)));
                g.CellCenter(cell, out double vx, out double vy);
                int index = g.IndexOf(cell);
                map.Villages.Add(new GeneratedMapSite
                {
                    Kind = GeneratedMapSite.VillageKind,
                    Name = village.Name ?? string.Empty,
                    Cell = index,
                    XPercent = Clamp01To100(g.PixelToPercentX(vx)),
                    YPercent = Clamp01To100(g.PixelToPercentY(vy)),
                    Size = Math.Max(1, Math.Min(3, village.Size)),
                    Variant = unchecked(index * 7919 + seed),
                    Family = GeneratedMapSite.FamilyAlongRoad
                });
            }
        }
        return map;
    }

    private static float Clamp01To100(float value) => Math.Max(0f, Math.Min(100f, value));

    private static List<MapPointData> ToPercent(WorldMapHexGrid g, List<AuthoredPoint> points)
    {
        List<MapPointData> result = new List<MapPointData>(points.Count);
        foreach (AuthoredPoint p in points)
            result.Add(new MapPointData(g.PixelToPercentX(p.X), g.PixelToPercentY(p.Y)));
        return result;
    }
}

// Живая проверка авторской карты (бриф §4.9): что помешает играть.
public static class AuthoredWorldMapValidation
{
    public sealed class Problem
    {
        public bool IsError;
        public string Text = string.Empty;
        public int Column;
        public int Row;
    }

    public static List<Problem> Check(AuthoredWorldMapData data, WorldMapMovementRules rules = null, AuthoredWorldMapLayout layout = null)
    {
        rules ??= WorldMapMovementRules.Current;
        WorldMapHexGrid g = data.Grid;
        layout ??= AuthoredWorldMapLayout.Compute(data);
        List<Problem> problems = new List<Problem>(layout.Problems);
        bool Passable(int index) => rules.IsTraversable((WorldMapGameplayTerrainType)layout.Terrain[index]);

        WorldMapHexCell start = new WorldMapHexCell(data.StartColumn, data.StartRow);
        int startIndex = g.IsInside(start) ? g.IndexOf(start) : -1;
        if (startIndex < 0 || !Passable(startIndex))
        {
            problems.Add(new Problem { IsError = true, Text = "Лагерь стоит на непроходимой клетке.", Column = data.StartColumn, Row = data.StartRow });
            return problems;
        }

        bool[] reached = new bool[g.CellCount];
        Queue<int> queue = new Queue<int>();
        reached[startIndex] = true;
        queue.Enqueue(startIndex);
        while (queue.Count > 0)
        {
            WorldMapHexCell cell = g.CellAt(queue.Dequeue());
            for (int dir = 0; dir < 6; dir++)
            {
                WorldMapHexCell n = cell.Neighbor(dir);
                if (!g.IsInside(n))
                    continue;
                int ni = g.IndexOf(n);
                if (reached[ni] || !Passable(ni))
                    continue;
                reached[ni] = true;
                queue.Enqueue(ni);
            }
        }

        if (data.Villages == null || data.Villages.Count == 0)
            problems.Add(new Problem { Text = "На карте нет ни одной деревни.", Column = data.StartColumn, Row = data.StartRow });
        else
        {
            for (int i = 0; i < data.Villages.Count; i++)
            {
                AuthoredVillage v = data.Villages[i];
                if (v == null)
                    continue;
                string name = string.IsNullOrWhiteSpace(v.Name) ? "Деревня " + (i + 1) : v.Name;
                WorldMapHexCell cell = new WorldMapHexCell(v.Column, v.Row);
                int index = g.IsInside(cell) ? g.IndexOf(cell) : -1;
                if (index < 0 || !Passable(index))
                    problems.Add(new Problem { IsError = true, Text = name + ": стоит на непроходимой клетке.", Column = v.Column, Row = v.Row });
                else if (!reached[index])
                    problems.Add(new Problem { IsError = true, Text = name + ": от лагеря до неё не дойти.", Column = v.Column, Row = v.Row });
                if (string.IsNullOrWhiteSpace(v.Name))
                    problems.Add(new Problem { Text = name + ": нет названия (в игре — «Деревня»).", Column = v.Column, Row = v.Row });
            }
        }
        return problems;
    }
}
