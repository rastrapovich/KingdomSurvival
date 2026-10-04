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
