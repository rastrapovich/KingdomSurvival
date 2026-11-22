using System.Collections.Generic;

// 22.11.2026: точные мосты рисунка живой карты — концы настила на берегах,
// в процентах карты (X — от ширины полотна, Y — от высоты) и ширина настила.
// Реку герой и группы мира переходят только по пролёту переправы; если
// рисунок отдал настоящий мост рядом с пролётом плана, путь идёт по его
// концам (WorldMapNavigation.FindPath). Реестр заполняет мост MapLab после
// сборки; без него (тесты, нет MapLab) работают пролёты плана.
public struct WorldMapBridgeSpan
{
    public float AXPercent, AYPercent, BXPercent, BYPercent; // концы настила на берегах, % карты
    public float WidthPercent; // ширина настила, % ширины полотна

    public WorldMapBridgeSpan(float axPercent, float ayPercent, float bxPercent, float byPercent, float widthPercent)
    {
        AXPercent = axPercent;
        AYPercent = ayPercent;
        BXPercent = bxPercent;
        BYPercent = byPercent;
        WidthPercent = widthPercent;
    }
}

public static class WorldMapBridges
{
    private static readonly List<WorldMapBridgeSpan> items = new List<WorldMapBridgeSpan>();

    public static IReadOnlyList<WorldMapBridgeSpan> Items => items;

    public static int Count => items.Count;

    // null — очистить.
    public static void Set(IEnumerable<WorldMapBridgeSpan> spans)
    {
        items.Clear();
        if (spans != null)
            items.AddRange(spans);
    }

    public static void Clear() => Set(null);
}
