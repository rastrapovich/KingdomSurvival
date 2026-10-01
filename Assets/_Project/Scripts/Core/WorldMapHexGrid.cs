using System;

// 12И (канон v1.50 §9.9): невидимая шестиугольная сетка на всю глобальную
// карту. Геометрия та же, что у поля боя (BattleSandbox.HexCoord): острые
// вершины сверху, нечётные ряды сдвинуты на полклетки вправо. Ядро не
// ссылается на модуль боя, поэтому минимальная математика повторена здесь.
//
// Клетка — столбец и ряд; позиция героя — непрерывная точка в пикселях
// полотна карты (MapCanvasWidth × MapCanvasHeight). Игровые координаты
// остального кода остаются процентами 0..100; перевод — только здесь.
public readonly struct WorldMapHexCell : IEquatable<WorldMapHexCell>
{
    private static readonly int[] EvenRowColumnSteps = { 1, 0, -1, -1, -1, 0 };
    private static readonly int[] OddRowColumnSteps = { 1, 1, 0, -1, 0, 1 };
    private static readonly int[] RowSteps = { 0, -1, -1, 0, 1, 1 };

    public readonly int Column;
    public readonly int Row;

    public WorldMapHexCell(int column, int row)
    {
        Column = column;
        Row = row;
    }

    // Порядок соседей совпадает с HexCoord.Neighbors() и HexFacing:
    // вправо, вправо-вверх, влево-вверх, влево, влево-вниз, вправо-вниз.
    public WorldMapHexCell Neighbor(int direction)
    {
        bool odd = (Row & 1) != 0;
        int columnStep = odd ? OddRowColumnSteps[direction] : EvenRowColumnSteps[direction];
        return new WorldMapHexCell(Column + columnStep, Row + RowSteps[direction]);
    }

    public int DistanceTo(WorldMapHexCell other)
    {
        int ax = Column - (Row - (Row & 1)) / 2;
        int az = Row;
        int bx = other.Column - (other.Row - (other.Row & 1)) / 2;
        int bz = other.Row;
        int dx = ax - bx;
        int dz = az - bz;
        int dy = -dx - dz;
        return Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
    }

    public bool Equals(WorldMapHexCell other) => Column == other.Column && Row == other.Row;
    public override bool Equals(object obj) => obj is WorldMapHexCell other && Equals(other);
    public override int GetHashCode() => unchecked(Column * 397 ^ Row);
    public override string ToString() => "(" + Column + ", " + Row + ")";
}

public sealed class WorldMapHexGrid
{
    public const float DefaultCanvasWidth = 4160f;
    public const float DefaultCanvasHeight = 2560f;
    // Размер сетки задаётся числом клеток по ширине полотна: 100 — одна
    // клетка пути равна 1% ширины карты.
    public const int DefaultHexesAcross = 100;
    public const int MinHexesAcross = 10;
    public const int MaxHexesAcross = 600;
    public static readonly double Sqrt3 = Math.Sqrt(3.0);

    public float CanvasWidth { get; }
    public float CanvasHeight { get; }
    public int HexesAcross { get; }
    // Радиус описанной окружности клетки (центр → вершина) в пикселях полотна.
    public float HexRadius { get; }
    public int Columns { get; }
    public int Rows { get; }

    // Расстояние между центрами соседних клеток — единица «одна клетка пути».
    public double HexWidth => Sqrt3 * HexRadius;
    public double RowStep => 1.5 * HexRadius;
    public int CellCount => Columns * Rows;

    public WorldMapHexGrid(float canvasWidth, float canvasHeight, int hexesAcross)
    {
        CanvasWidth = canvasWidth > 1f && !float.IsNaN(canvasWidth) ? canvasWidth : DefaultCanvasWidth;
        CanvasHeight = canvasHeight > 1f && !float.IsNaN(canvasHeight) ? canvasHeight : DefaultCanvasHeight;
        HexesAcross = SanitizeHexesAcross(hexesAcross);
        HexRadius = (float)(CanvasWidth / (HexesAcross * Sqrt3));
        Columns = Math.Max(1, (int)Math.Ceiling(CanvasWidth / HexWidth) + 1);
        Rows = Math.Max(1, (int)Math.Ceiling(CanvasHeight / RowStep) + 1);
    }

    public static WorldMapHexGrid CreateDefault() =>
        new WorldMapHexGrid(DefaultCanvasWidth, DefaultCanvasHeight, DefaultHexesAcross);

    public static int SanitizeHexesAcross(int hexesAcross)
    {
        if (hexesAcross <= 0)
            return DefaultHexesAcross;
        return Math.Max(MinHexesAcross, Math.Min(MaxHexesAcross, hexesAcross));
    }

    public bool IsInside(WorldMapHexCell cell) =>
        cell.Column >= 0 && cell.Column < Columns && cell.Row >= 0 && cell.Row < Rows;

    public int IndexOf(WorldMapHexCell cell) => cell.Row * Columns + cell.Column;

    public WorldMapHexCell CellAt(int index) => new WorldMapHexCell(index % Columns, index / Columns);

    // Центр клетки (0,0) лежит в левом верхнем углу полотна.
    public void CellCenter(WorldMapHexCell cell, out double x, out double y)
    {
        x = HexWidth * (cell.Column + ((cell.Row & 1) != 0 ? 0.5 : 0.0));
        y = RowStep * cell.Row;
    }

    // Точка полотна → клетка (кубическое округление), с прижатием к краю сетки.
    public WorldMapHexCell CellAtPixel(double x, double y)
    {
        double q = (Sqrt3 / 3.0 * x - y / 3.0) / HexRadius;
        double r = 2.0 / 3.0 * y / HexRadius;
        double cx = q;
        double cz = r;
        double cy = -cx - cz;
        double rx = Math.Round(cx);
        double ry = Math.Round(cy);
        double rz = Math.Round(cz);
        double dx = Math.Abs(rx - cx);
        double dy = Math.Abs(ry - cy);
        double dz = Math.Abs(rz - cz);
        if (dx > dy && dx > dz)
            rx = -ry - rz;
        else if (dy <= dz)
            rz = -rx - ry;

        int row = (int)rz;
        int column = (int)rx + (row - (row & 1)) / 2;
        return new WorldMapHexCell(
            Math.Max(0, Math.Min(Columns - 1, column)),
            Math.Max(0, Math.Min(Rows - 1, row)));
    }

    public double PercentToPixelX(float xPercent) => xPercent / 100.0 * CanvasWidth;
    public double PercentToPixelY(float yPercent) => yPercent / 100.0 * CanvasHeight;
    public float PixelToPercentX(double x) => (float)(x / CanvasWidth * 100.0);
    public float PixelToPercentY(double y) => (float)(y / CanvasHeight * 100.0);

    public WorldMapHexCell CellAtPercent(float xPercent, float yPercent) =>
        CellAtPixel(PercentToPixelX(xPercent), PercentToPixelY(yPercent));

    // Непрерывное расстояние в клетках пути (центр-центр соседей = 1).
    public double DistanceHexes(float fromXPercent, float fromYPercent, float toXPercent, float toYPercent)
    {
        double dx = PercentToPixelX(toXPercent) - PercentToPixelX(fromXPercent);
        double dy = PercentToPixelY(toYPercent) - PercentToPixelY(fromYPercent);
        return Math.Sqrt(dx * dx + dy * dy) / HexWidth;
    }
}
