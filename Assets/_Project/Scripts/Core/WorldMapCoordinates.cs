using System;

// AM-01 (канон v1.33, §9.9): единственное место преобразования процентных
// координат карты (0..100) в клетки логической сетки. WorldMapNavigation и
// весь остальной код должны использовать только эти методы, чтобы герой,
// арт и ETA не разошлись при последующих правках. Контракт первой версии
// (см. инструкцию по миграции, раздел 5): диапазон 0..100 покрывает
// GridWidth-1 / GridHeight-1 шагов между крайними узлами — сетка не
// расширяется на этом этапе.
public static class WorldMapCoordinates
{
    public static int PercentToGridX(float xPercent, int gridWidth) =>
        Math.Max(0, Math.Min(
            gridWidth - 1,
            (int)Math.Round(xPercent * (gridWidth - 1) / 100f)));

    public static int PercentToGridY(float yPercent, int gridHeight) =>
        Math.Max(0, Math.Min(
            gridHeight - 1,
            (int)Math.Round(yPercent * (gridHeight - 1) / 100f)));

    public static float GridXToPercent(int gridX, int gridWidth) =>
        gridWidth <= 1 ? 0f : gridX * 100f / (gridWidth - 1);

    public static float GridYToPercent(int gridY, int gridHeight) =>
        gridHeight <= 1 ? 0f : gridY * 100f / (gridHeight - 1);

    public static double GeometricDistanceCells(
        float fromXPercent,
        float fromYPercent,
        float toXPercent,
        float toYPercent,
        int gridWidth,
        int gridHeight)
    {
        double dx = (toXPercent - fromXPercent) * (gridWidth - 1) / 100.0;
        double dy = (toYPercent - fromYPercent) * (gridHeight - 1) / 100.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
