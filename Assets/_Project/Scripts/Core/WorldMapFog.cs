using System;

// Бриф правок (19 пунктов) п. 1: туман войны на сгенерированной карте
// «Свободной игры». Исследованная местность — отдельное знание от известных
// объектов: клетки, мимо которых герой прошёл в радиусе обзора, открываются
// навсегда (битовое поле клеток сетки в сохранении), а известность места
// (LocationData.IsVisibleOnMap — слух, поручение, находка) от тумана не
// зависит и окрестности не открывает. Новая партия начинается с открытой
// округой старта; старое сохранение без поля — с округой текущей точки
// героя. Авторские миры кризисов тумана не имеют. Радиус — рабочее число в
// настройках перемещения (WorldMapMovementRules.FogRevealRadiusHexes), цвет —
// в ассете настроек перемещения на стороне Unity.
[Serializable]
public sealed class WorldMapFogData
{
    public int Columns;
    public int Rows;
    // Битовое поле клеток (index = row * Columns + column) в Base64.
    public string Explored = string.Empty;
}

public static class WorldMapFog
{
    public static bool Applies(GameState state) =>
        state != null && state.HasGeneratedMap && CampaignContent.IsFreePlay(state);

    // Версия открытого — растёт при каждом новом открытии; интерфейс по ней
    // решает, перерисовывать ли текстуру тумана (не каждый кадр).
    public static int Version { get; private set; }

    [ThreadStatic] private static byte[] cachedBits;
    [ThreadStatic] private static GameState cachedState;
    [ThreadStatic] private static string cachedSource;

    [ThreadStatic] private static GeneratedWorldMap cachedGridMap;
    [ThreadStatic] private static WorldMapHexGrid cachedGrid;

    // Сетка карты партии — одна на карту (без выделения памяти на каждый вопрос).
    public static WorldMapHexGrid Grid(GameState state)
    {
        if (state == null || !state.HasGeneratedMap)
            return WorldMapNavigation.Grid;
        if (!ReferenceEquals(cachedGridMap, state.GeneratedMap) || cachedGrid == null)
        {
            cachedGridMap = state.GeneratedMap;
            cachedGrid = state.GeneratedMap.CreateGrid();
        }
        return cachedGrid;
    }

    public static bool IsExplored(GameState state, int cellIndex)
    {
        if (!Applies(state))
            return true;
        byte[] bits = Bits(state);
        if (bits == null || cellIndex < 0 || (cellIndex >> 3) >= bits.Length)
            return false;
        return (bits[cellIndex >> 3] & (1 << (cellIndex & 7))) != 0;
    }

    public static bool IsExploredAtPercent(GameState state, float xPercent, float yPercent)
    {
        if (!Applies(state))
            return true;
        WorldMapHexGrid grid = Grid(state);
        WorldMapHexCell cell = grid.CellAtPixel(grid.PercentToPixelX(xPercent), grid.PercentToPixelY(yPercent));
        return IsExplored(state, grid.IndexOf(cell));
    }

    public static int ExploredCount(GameState state)
    {
        byte[] bits = Applies(state) ? Bits(state) : null;
        if (bits == null)
            return 0;
        int count = 0;
        foreach (byte b in bits)
        {
            int v = b;
            while (v != 0)
            {
                count += v & 1;
                v >>= 1;
            }
        }
        return count;
    }

    // Открыть округу точки в радиусе обзора. Возвращает, открылось ли новое.
    public static bool RevealAround(GameState state, float xPercent, float yPercent) =>
        RevealAround(state, xPercent, yPercent, WorldMapMovementRules.Current.FogRevealRadiusHexes);

    public static bool RevealAround(GameState state, float xPercent, float yPercent, double radiusHexes)
    {
        if (!Applies(state))
            return false;
        WorldMapHexGrid grid = Grid(state);
        byte[] bits = Bits(state);
        if (bits == null)
            return false;

        double px = grid.PercentToPixelX(xPercent);
        double py = grid.PercentToPixelY(yPercent);
        double radiusPx = Math.Max(0.0, radiusHexes) * grid.HexWidth;
        int rowFrom = Math.Max(0, (int)Math.Floor((py - radiusPx) / grid.RowStep) - 1);
        int rowTo = Math.Min(grid.Rows - 1, (int)Math.Ceiling((py + radiusPx) / grid.RowStep) + 1);
        int columnFrom = Math.Max(0, (int)Math.Floor((px - radiusPx) / grid.HexWidth) - 1);
        int columnTo = Math.Min(grid.Columns - 1, (int)Math.Ceiling((px + radiusPx) / grid.HexWidth) + 1);
        double limit = radiusPx + grid.HexWidth * 0.5;
        double limitSquared = limit * limit;

        bool changed = false;
        for (int row = rowFrom; row <= rowTo; row++)
        {
            for (int column = columnFrom; column <= columnTo; column++)
            {
                WorldMapHexCell cell = new WorldMapHexCell(column, row);
                grid.CellCenter(cell, out double cx, out double cy);
                double dx = cx - px;
                double dy = cy - py;
                if (dx * dx + dy * dy > limitSquared)
                    continue;
                int index = grid.IndexOf(cell);
                int mask = 1 << (index & 7);
                if ((bits[index >> 3] & mask) != 0)
                    continue;
                bits[index >> 3] = (byte)(bits[index >> 3] | mask);
                changed = true;
            }
        }
        if (changed)
        {
            Store(state, bits);
            RevealPlacesInExplored(state);
        }
        return changed;
    }

    // Новая партия: места, известные лишь по виду, ждут открытия клетки.
    public static void HidePlacesUnderFog(GameState state)
    {
        if (!Applies(state) || state.Locations == null)
            return;
        foreach (LocationData location in state.Locations)
        {
            if (location == null || location.IsWaypoint || !location.IsVisibleOnMap)
                continue;
            location.RevealedByFog = true;
            location.IsVisibleOnMap = false;
            location.IsDiscovered = false;
        }
    }

    private static void RevealPlacesInExplored(GameState state)
    {
        if (state.Locations == null)
            return;
        foreach (LocationData location in state.Locations)
        {
            if (location == null || !location.RevealedByFog || location.IsVisibleOnMap)
                continue;
            if (!IsExploredAtPercent(state, location.MapXPercent, location.MapYPercent))
                continue;
            location.IsVisibleOnMap = true;
            location.IsDiscovered = true;
        }
    }

    // Начальное открытие: новая партия (старт) или старое сохранение без
    // поля (текущая точка героя). Пустое поле — признак «ещё не начинали».
    public static void EnsureInitialized(GameState state)
    {
        if (!Applies(state))
            return;
        WorldMapFogData fog = state.Fog;
        WorldMapHexGrid grid = Grid(state);
        bool valid = fog != null && fog.Columns == grid.Columns && fog.Rows == grid.Rows && !string.IsNullOrEmpty(fog.Explored);
        if (valid)
            return;
        state.Fog = new WorldMapFogData { Columns = grid.Columns, Rows = grid.Rows, Explored = string.Empty };
        cachedState = null;
        float x = state.GeneratedMap.StartXPercent;
        float y = state.GeneratedMap.StartYPercent;
        if (state.HasActiveExpedition)
        {
            x = state.ActiveExpedition.CurrentMapXPercent;
            y = state.ActiveExpedition.CurrentMapYPercent;
        }
        // Поле пустым не остаётся даже при нулевом радиусе: Store пишет его.
        if (!RevealAround(state, x, y))
            Store(state, Bits(state));
    }

    private static byte[] Bits(GameState state)
    {
        WorldMapFogData fog = state.Fog;
        WorldMapHexGrid grid = Grid(state);
        int length = (grid.CellCount + 7) / 8;
        if (fog == null || fog.Columns != grid.Columns || fog.Rows != grid.Rows)
        {
            fog = new WorldMapFogData { Columns = grid.Columns, Rows = grid.Rows, Explored = string.Empty };
            state.Fog = fog;
        }
        if (ReferenceEquals(cachedState, state) && ReferenceEquals(cachedSource, fog.Explored) && cachedBits != null && cachedBits.Length == length)
            return cachedBits;

        byte[] bits = new byte[length];
        if (!string.IsNullOrEmpty(fog.Explored))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(fog.Explored);
                Array.Copy(decoded, bits, Math.Min(decoded.Length, bits.Length));
            }
            catch (FormatException)
            {
                // Испорченное поле — туман заново, без падения загрузки.
            }
        }
        cachedState = state;
        cachedSource = fog.Explored;
        cachedBits = bits;
        return bits;
    }

    private static void Store(GameState state, byte[] bits)
    {
        state.Fog.Explored = Convert.ToBase64String(bits);
        cachedState = state;
        cachedSource = state.Fog.Explored;
        cachedBits = bits;
        Version++;
    }
}
