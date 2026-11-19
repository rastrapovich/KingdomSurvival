using System;
using System.Collections.Generic;

// Облака тумана войны (19.11.2026, бриф «KingdomSurvival_Fog_Clouds_3_Styles»):
// декоративный объём над маской неисследованного. Здесь — только чистая
// раскладка и правила без Unity: стабильные по seed карты опорные точки
// облаков (неровная сетка с разбросом, без «забора»), признак «облако уже
// рассеяно» и расстояние от облака до пройденного отрезка пути героя.
//
// Состояние облаков не хранится: облако рассеяно, если в радиусе запуска
// от его опорной точки есть исследованная клетка (WorldMapFog). Поэтому
// после загрузки и смены вида рассеянные облака не возвращаются, а
// сохранение остаётся прежним. Скрывает местность маска WorldMapFog —
// облака только придают ей объём; без них туман работает как раньше.
public readonly struct WorldMapFogCloudAnchor
{
    public readonly int Id;
    // Центр в пикселях полотна карты (как WorldMapHexGrid.CellCenter).
    public readonly double X;
    public readonly double Y;
    // Стабильные «случайные» доли 0..1 из seed и ID: выбор рисунка,
    // разброс размера, зеркальность, запасное направление отлёта.
    public readonly uint Hash;

    public WorldMapFogCloudAnchor(int id, double x, double y, uint hash)
    {
        Id = id;
        X = x;
        Y = y;
        Hash = hash;
    }

    public float Fraction(int channel) => WorldMapFogClouds.Fraction(Hash, channel);
    public int Variant(int count) => count <= 0 ? 0 : (int)(WorldMapFogClouds.Mix(Hash, 11) % (uint)count);
    public bool Flip => Fraction(3) < 0.5f;
    // Запасное направление отлёта (радианы), если герой стоит прямо на опоре.
    public double FallbackAngle => Fraction(4) * Math.PI * 2.0;
}

public static class WorldMapFogClouds
{
    // Неровная сетка опорных точек на всё полотно: ряды через шаг·0,87,
    // нечётные ряды сдвинуты на полшага, каждая точка смещена на долю шага.
    // Один и тот же seed и сетка — те же облака (загрузка, смена вида).
    public static List<WorldMapFogCloudAnchor> Layout(WorldMapHexGrid grid, int seed, double spacingHexes, double jitter) =>
        Layout(grid, seed, spacingHexes, Math.Max(1.0, spacingHexes) * 0.866, jitter);

    // Шаг по рядам отдельно: облака широкие и низкие, поэтому ряды идут
    // чаще столбцов — плотный покров почти без просветов. Разброс по каждой
    // оси — доля своего шага (вертикальный разброс не раздвигает ряды).
    public static List<WorldMapFogCloudAnchor> Layout(WorldMapHexGrid grid, int seed, double spacingHexes, double rowSpacingHexes, double jitter)
    {
        List<WorldMapFogCloudAnchor> result = new List<WorldMapFogCloudAnchor>();
        if (grid == null || grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0)
            return result;
        double step = Math.Max(1.0, spacingHexes) * grid.HexWidth;
        double rowStep = Math.Max(0.5, rowSpacingHexes) * grid.HexWidth;
        double clamped = Math.Max(0.0, Math.Min(0.9, jitter));
        double spread = clamped * step;
        double spreadY = clamped * rowStep * 0.3;
        int rows = (int)Math.Ceiling(grid.CanvasHeight / rowStep) + 1;
        int columns = (int)Math.Ceiling(grid.CanvasWidth / step) + 1;
        uint seedHash = Mix((uint)seed, 0x5F0C1D);
        for (int row = 0; row < rows; row++)
        {
            double shift = (row & 1) == 1 ? step * 0.5 : 0.0;
            for (int column = 0; column < columns; column++)
            {
                int id = row * columns + column;
                uint hash = Mix(seedHash, (uint)id);
                double x = column * step + shift + (Fraction(hash, 1) * 2.0 - 1.0) * spread;
                double y = row * rowStep + (Fraction(hash, 2) * 2.0 - 1.0) * spreadY;
                // Край полотна тоже под облаками: опора может выйти за край на полшага.
                if (x < -step * 0.5 || y < -rowStep * 0.5 || x > grid.CanvasWidth + step * 0.5 || y > grid.CanvasHeight + rowStep * 0.5)
                    continue;
                result.Add(new WorldMapFogCloudAnchor(id, x, y, hash));
            }
        }
        return result;
    }

    // Облако рассеяно: в радиусе (клетки) от опоры есть исследованная клетка.
    // Граница — как у открытия тумана: центр клетки в радиусе + полклетки.
    public static bool IsCleared(GameState state, WorldMapHexGrid grid, double x, double y, double radiusHexes)
    {
        if (!WorldMapFog.Applies(state) || grid == null)
            return true;
        double radiusPx = Math.Max(0.0, radiusHexes) * grid.HexWidth;
        double limit = radiusPx + grid.HexWidth * 0.5;
        double limitSquared = limit * limit;
        int rowFrom = Math.Max(0, (int)Math.Floor((y - limit) / grid.RowStep) - 1);
        int rowTo = Math.Min(grid.Rows - 1, (int)Math.Ceiling((y + limit) / grid.RowStep) + 1);
        int columnFrom = Math.Max(0, (int)Math.Floor((x - limit) / grid.HexWidth) - 1);
        int columnTo = Math.Min(grid.Columns - 1, (int)Math.Ceiling((x + limit) / grid.HexWidth) + 1);
        for (int row = rowFrom; row <= rowTo; row++)
        {
            for (int column = columnFrom; column <= columnTo; column++)
            {
                WorldMapHexCell cell = new WorldMapHexCell(column, row);
                grid.CellCenter(cell, out double cx, out double cy);
                double dx = cx - x;
                double dy = cy - y;
                if (dx * dx + dy * dy > limitSquared)
                    continue;
                if (WorldMapFog.IsExplored(state, grid.IndexOf(cell)))
                    return true;
            }
        }
        return false;
    }

    // Ближайшая к точке P точка отрезка AB (весь путь между обновлениями,
    // а не только конечная точка — быстрый ход не оставляет пропусков).
    public static void ClosestOnSegment(double px, double py, double ax, double ay, double bx, double by, out double cx, out double cy)
    {
        double vx = bx - ax;
        double vy = by - ay;
        double lengthSquared = vx * vx + vy * vy;
        double t = lengthSquared > 1e-12 ? ((px - ax) * vx + (py - ay) * vy) / lengthSquared : 0.0;
        t = Math.Max(0.0, Math.Min(1.0, t));
        cx = ax + vx * t;
        cy = ay + vy * t;
    }

    // Направление отлёта: от героя (ближайшей точки пути) к центру облака;
    // почти нулевой вектор — стабильное направление из хеша облака.
    public static void DriftDirection(WorldMapFogCloudAnchor anchor, double heroX, double heroY, double minLength, out double dx, out double dy)
    {
        dx = anchor.X - heroX;
        dy = anchor.Y - heroY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= Math.Max(1e-6, minLength))
        {
            double angle = anchor.FallbackAngle;
            dx = Math.Cos(angle);
            dy = Math.Sin(angle);
            return;
        }
        dx /= length;
        dy /= length;
    }

    public static float Fraction(uint hash, int channel) =>
        (Mix(hash, (uint)(channel * 0x9E3779B1)) & 0xFFFFFF) / (float)0x1000000;

    public static uint Mix(uint a, uint b)
    {
        unchecked
        {
            uint h = a ^ (b * 0x85EBCA6B);
            h ^= h >> 16;
            h *= 0x7FEB352D;
            h ^= h >> 15;
            h *= 0x846CA68B;
            h ^= h >> 16;
            return h;
        }
    }
}
