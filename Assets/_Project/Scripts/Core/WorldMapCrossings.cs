using System;
using System.Collections.Generic;

// 22.11.2026: переправы плана для навигации. Клетки воды, через которые
// прошла дорога, в разметке — Road и проходимы целиком; без этого набора путь
// пересекал бы реку где угодно в пределах клетки. Здесь по пролётам
// «берег → берег» (WorldMapDefinitionData.BridgeSpans) строятся: клетки
// переправы (непроходимы для обычных шагов и сглаживания) и рёбра
// «клетка входа ↔ клетка выхода» для поиска пути. Строится один раз на
// определение карты (кэш — WorldMapNavigation.ActiveCrossings).
public sealed class WorldMapCrossings
{
    // Концы пролёта — центры клеток суши, в пикселях полотна.
    public readonly struct Span
    {
        public readonly int EntryCell;
        public readonly int ExitCell;
        public readonly double AX, AY, BX, BY;

        public Span(int entryCell, int exitCell, double ax, double ay, double bx, double by)
        {
            EntryCell = entryCell;
            ExitCell = exitCell;
            AX = ax;
            AY = ay;
            BX = bx;
            BY = by;
        }
    }

    public readonly struct Edge
    {
        public readonly int To;
        public readonly int SpanIndex;

        public Edge(int to, int spanIndex)
        {
            To = to;
            SpanIndex = spanIndex;
        }
    }

    // Клетка переправы — не дальше этого от оси пролёта (в клетках пути) и не
    // ближе этого к его концам вдоль оси: так в набор попадают клетки воды,
    // по которым шла дорога, даже если ломаная сетки отклонилась от оси, но
    // не береговая суша рядом с концом.
    private const double ReachHexes = 0.8;
    private const double EndMarginHexes = 0.6;
    private const double AxisSampleFraction = 0.15;

    private readonly WorldMapHexGrid grid;
    private readonly List<Span> spans = new List<Span>();
    // Клетка → номер переправы (первой, если их несколько); -1 — не переправа.
    private readonly int[] crossingOf;
    private readonly Dictionary<long, int> spanByCells = new Dictionary<long, int>();
    private readonly Dictionary<int, List<Edge>> edges = new Dictionary<int, List<Edge>>();

    public IReadOnlyList<Span> Spans => spans;

    public int Count => spans.Count;

    public WorldMapCrossings(WorldMapTerrainLayer layer, IList<MapPointData> spanPoints)
    {
        layer = layer ?? WorldMapTerrainLayer.CreateDefault();
        grid = layer.Grid;
        crossingOf = new int[grid.CellCount];
        for (int i = 0; i < crossingOf.Length; i++)
            crossingOf[i] = -1;
        if (spanPoints == null)
            return;

        HashSet<int> ends = new HashSet<int>();
        for (int i = 0; i + 1 < spanPoints.Count; i += 2)
        {
            MapPointData a = spanPoints[i], b = spanPoints[i + 1];
            double ax = grid.PercentToPixelX(a.XPercent), ay = grid.PercentToPixelY(a.YPercent);
            double bx = grid.PercentToPixelX(b.XPercent), by = grid.PercentToPixelY(b.YPercent);
            WorldMapHexCell aCell = grid.CellAtPixel(ax, ay);
            WorldMapHexCell bCell = grid.CellAtPixel(bx, by);
            if (aCell.DistanceTo(bCell) < 2)
                continue;
            int aIndex = grid.IndexOf(aCell), bIndex = grid.IndexOf(bCell);
            // Концы — по центрам клеток, как их задал план.
            grid.CellCenter(aCell, out ax, out ay);
            grid.CellCenter(bCell, out bx, out by);
            int id = spans.Count;
            spans.Add(new Span(aIndex, bIndex, ax, ay, bx, by));
            ends.Add(aIndex);
            ends.Add(bIndex);
            spanByCells[PairKey(aIndex, bIndex)] = id;
            spanByCells[PairKey(bIndex, aIndex)] = id;
            AddEdge(aIndex, new Edge(bIndex, id));
            AddEdge(bIndex, new Edge(aIndex, id));
        }

        for (int id = 0; id < spans.Count; id++)
            MarkCells(layer, id, ends);
    }

    private static long PairKey(int from, int to) => ((long)from << 32) | (uint)to;

    private void AddEdge(int cell, Edge edge)
    {
        if (!edges.TryGetValue(cell, out List<Edge> list))
            edges[cell] = list = new List<Edge>();
        list.Add(edge);
    }

    private void MarkCells(WorldMapTerrainLayer layer, int id, HashSet<int> ends)
    {
        Span span = spans[id];
        double dx = span.BX - span.AX, dy = span.BY - span.AY;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-9)
            return;
        double ux = dx / length, uy = dy / length;
        double hexWidth = grid.HexWidth;

        // Клетки под осью пролёта, кроме концов, — переправа в любом случае.
        int axisSamples = Math.Max(2, (int)Math.Ceiling(length / (grid.HexRadius * AxisSampleFraction)));
        for (int s = 0; s <= axisSamples; s++)
        {
            double t = s / (double)axisSamples;
            int cell = grid.IndexOf(grid.CellAtPixel(span.AX + dx * t, span.AY + dy * t));
            if (!ends.Contains(cell))
                Mark(cell, id);
        }

        // Дорожные клетки воды рядом с осью (ломаная сетки отклоняется от оси).
        double pad = (ReachHexes + 1.0) * hexWidth;
        WorldMapHexCell lo = grid.CellAtPixel(Math.Min(span.AX, span.BX) - pad, Math.Min(span.AY, span.BY) - pad);
        WorldMapHexCell hi = grid.CellAtPixel(Math.Max(span.AX, span.BX) + pad, Math.Max(span.AY, span.BY) + pad);
        for (int row = Math.Max(0, lo.Row - 1); row <= Math.Min(grid.Rows - 1, hi.Row + 1); row++)
        {
            for (int column = Math.Max(0, lo.Column - 1); column <= Math.Min(grid.Columns - 1, hi.Column + 1); column++)
            {
                WorldMapHexCell c = new WorldMapHexCell(column, row);
                int index = grid.IndexOf(c);
                if (ends.Contains(index) || layer.Get(c) != WorldMapGameplayTerrainType.Road)
                    continue;
                grid.CellCenter(c, out double cx, out double cy);
                double along = ((cx - span.AX) * ux + (cy - span.AY) * uy) / hexWidth;
                double across = Math.Abs(-(cx - span.AX) * uy + (cy - span.AY) * ux) / hexWidth;
                if (across <= ReachHexes && along >= EndMarginHexes && along <= length / hexWidth - EndMarginHexes)
                    Mark(index, id);
            }
        }
    }

    private void Mark(int cell, int id)
    {
        if (crossingOf[cell] < 0)
            crossingOf[cell] = id;
    }

    // Клетка переправы. Клетки той переправы, на которой стоит старт
    // (герой на мосту в старом сохранении), для этого пути проходимы.
    public bool IsBlocked(int cellIndex, int startCellIndex)
    {
        if (cellIndex < 0 || cellIndex >= crossingOf.Length)
            return false;
        int id = crossingOf[cellIndex];
        if (id < 0)
            return false;
        return startCellIndex < 0 || startCellIndex >= crossingOf.Length || crossingOf[startCellIndex] != id;
    }

    public bool IsBlockedCell(int cellIndex) => IsBlocked(cellIndex, -1);

    public bool IsBlockedAtPercent(float xPercent, float yPercent) =>
        IsBlockedCell(grid.IndexOf(grid.CellAtPercent(xPercent, yPercent)));

    // Номер переправы, которой принадлежит клетка (-1 — никакой).
    public int SpanOfCell(int cellIndex) =>
        cellIndex >= 0 && cellIndex < crossingOf.Length ? crossingOf[cellIndex] : -1;

    // Рёбра «вход ↔ выход» из клетки суши (null — переправ у клетки нет).
    public List<Edge> EdgesFrom(int cellIndex) =>
        edges.TryGetValue(cellIndex, out List<Edge> list) ? list : null;

    // Пара соседних точек пути — это переход по пролёту? forward — от входа к выходу.
    public bool TryGetSpan(int fromCell, int toCell, out int spanIndex, out bool forward)
    {
        forward = true;
        if (spans.Count == 0 || !spanByCells.TryGetValue(PairKey(fromCell, toCell), out spanIndex))
        {
            spanIndex = -1;
            return false;
        }
        forward = spans[spanIndex].EntryCell == fromCell;
        return true;
    }

    // Сколько клеток от клетки переправы до ближайшего конца её пролёта: цель
    // приказа на мосту считается достигнутой с берега.
    public int DistanceToNearestEnd(int cellIndex)
    {
        int id = SpanOfCell(cellIndex);
        if (id < 0)
            return 0;
        WorldMapHexCell cell = grid.CellAt(cellIndex);
        Span span = spans[id];
        return Math.Min(cell.DistanceTo(grid.CellAt(span.EntryCell)), cell.DistanceTo(grid.CellAt(span.ExitCell)));
    }
}
