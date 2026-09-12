using System.Collections.Generic;

// WM-06 (раздел 9.9 канона): регион — данные, а не захардкоженный if/else.
// Прямоугольная зона в процентных координатах карты (0..100) — простейшее
// представление; сознательно не полигон/маска, пока нет доказанной
// потребности в сложном редакторе границ ("не нужен сложный редактор
// полигонов на первом этапе"). Имена ниже — те же самые рабочие названия,
// что уже были в GameState.GetRegionName до этого рефакторинга; это не факт
// лора и не финальная география — конкретные имена регионов утверждаются
// отдельно через ProjectDocs/LORE.md.
public sealed class WorldMapRegionDefinition
{
    public string Id;
    public string Name;
    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;

    public bool Contains(float xPercent, float yPercent) =>
        xPercent >= MinXPercent && xPercent < MaxXPercent &&
        yPercent >= MinYPercent && yPercent < MaxYPercent;
}

public static class WorldMapRegionRegistry
{
    // Порядок важен: проверяются по очереди, побеждает первое совпадение —
    // это в точности повторяет старый приоритет (запад/восток проверялись
    // раньше севера) из GameState.GetRegionName. Единственное отличие от
    // прежней реализации: восточная граница здесь x >= 66 (было строго
    // x > 66) — на практике сгенерированные координаты никогда не попадают
    // ровно в 66.0, так что для реального поведения игры разницы нет.
    public static readonly IReadOnlyList<WorldMapRegionDefinition> Regions =
        new List<WorldMapRegionDefinition>
        {
            new WorldMapRegionDefinition
            {
                Id = "west",
                Name = "Западные земли",
                MinXPercent = float.MinValue,
                MaxXPercent = 34f,
                MinYPercent = float.MinValue,
                MaxYPercent = float.MaxValue
            },
            new WorldMapRegionDefinition
            {
                Id = "east",
                Name = "Восточные земли",
                MinXPercent = 66f,
                MaxXPercent = float.MaxValue,
                MinYPercent = float.MinValue,
                MaxYPercent = float.MaxValue
            },
            new WorldMapRegionDefinition
            {
                Id = "north",
                Name = "Северные земли",
                MinXPercent = float.MinValue,
                MaxXPercent = float.MaxValue,
                MinYPercent = float.MinValue,
                MaxYPercent = 40f
            },
            new WorldMapRegionDefinition
            {
                Id = "center",
                Name = "Центральные земли",
                MinXPercent = float.MinValue,
                MaxXPercent = float.MaxValue,
                MinYPercent = float.MinValue,
                MaxYPercent = float.MaxValue
            }
        };

    public static WorldMapRegionDefinition FindRegion(float xPercent, float yPercent)
    {
        foreach (WorldMapRegionDefinition region in Regions)
        {
            if (region.Contains(xPercent, yPercent))
                return region;
        }

        return Regions[Regions.Count - 1];
    }
}
