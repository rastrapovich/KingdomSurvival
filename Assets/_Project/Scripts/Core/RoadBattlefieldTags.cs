using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.3): дорожный бой открывает отдельную арену,
// соответствующую местности, где отряд встретил противника. Здесь — какие
// теги Базы полей боя подходят местности глобальной карты; само поле
// выбирает Unity-слой. Нет подходящего рисунка — отмеченный временный
// вариант (общее поле полигона), а не подмена без следа.
public static class RoadBattlefieldTags
{
    public static IReadOnlyList<string> For(WorldMapGameplayTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapGameplayTerrainType.Forest:
            case WorldMapGameplayTerrainType.Trail:
                return new[] { "biome.forest" };
            case WorldMapGameplayTerrainType.Swamp:
                return new[] { "terrain.swamp" };
            case WorldMapGameplayTerrainType.Hills:
            case WorldMapGameplayTerrainType.Mountains:
            case WorldMapGameplayTerrainType.Cliffs:
                return new[] { "terrain.hills" };
            default:
                return new[] { "terrain.clearing" };
        }
    }
}

// Бриф «Поверхности и окружение боевой арены», этап 4: тема дорожной арены
// из уже известного контекста места — местность под отрядом и (для группы
// мира) её биотоп. Отсутствие данных не выдаётся за географию: обычный
// пресет с подписью источника. Сезона в игре нет — зимняя тема по карте не
// выбирается.
public static class RoadArenaTheme
{
    public const string DryGlade = "dry_glade";
    public const string ForestEdge = "forest_edge";
    public const string BogGlade = "bog_glade";
    public const string Ford = "ford";
    public const string FordBridge = "ford_bridge";
    public const string RockySlope = "rocky_slope";
    public const string RuinedHillfort = "ruined_hillfort";

    public static string For(WorldMapGameplayTerrainType terrain, string biomeHint, int seed, out string source)
    {
        switch (terrain)
        {
            case WorldMapGameplayTerrainType.Forest:
            case WorldMapGameplayTerrainType.Trail:
                source = "местность под отрядом: лес";
                return ForestEdge;
            case WorldMapGameplayTerrainType.Swamp:
                source = "местность под отрядом: болото";
                return BogGlade;
            case WorldMapGameplayTerrainType.Hills:
            case WorldMapGameplayTerrainType.Mountains:
            case WorldMapGameplayTerrainType.Cliffs:
                source = "местность под отрядом: холмы и скалы";
                return RockySlope;
            case WorldMapGameplayTerrainType.Water:
                source = "местность под отрядом: вода; мост — вариант по seed";
                return (seed & 1) == 0 ? Ford : FordBridge;
        }
        switch (biomeHint ?? string.Empty)
        {
            case WorldPopulationBiome.Shore:
                source = "биотоп группы: берег";
                return Ford;
            case WorldPopulationBiome.Swamp:
                source = "биотоп группы: топь";
                return BogGlade;
            case WorldPopulationBiome.Rocky:
                source = "биотоп группы: каменистая суша";
                return RockySlope;
            case WorldPopulationBiome.Ruins:
                source = "биотоп группы: старое место";
                return RuinedHillfort;
            case WorldPopulationBiome.ForestEdge:
            case WorldPopulationBiome.Underbrush:
            case WorldPopulationBiome.DeepForest:
                source = "биотоп группы: лес";
                return ForestEdge;
        }
        source = "обычный пресет: у места (" + terrain + ") нет признаков другой темы";
        return DryGlade;
    }
}
