using NUnit.Framework;

// Бриф «Поверхности и окружение боевой арены», этап 4: тема дорожной арены
// берётся из известного контекста места; без признаков — подписанный обычный
// пресет, а не вымышленная география.
public sealed class RoadArenaThemeTests
{
    [Test]
    public void TerrainUnderParty_PicksMatchingTheme_WithSource()
    {
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Forest, null, 1, out string forest), Is.EqualTo(RoadArenaTheme.ForestEdge));
        Assert.That(forest, Does.Contain("лес"));
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Swamp, null, 1, out _), Is.EqualTo(RoadArenaTheme.BogGlade));
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Hills, null, 1, out _), Is.EqualTo(RoadArenaTheme.RockySlope));
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Water, null, 2, out _), Is.EqualTo(RoadArenaTheme.Ford));
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Water, null, 3, out _), Is.EqualTo(RoadArenaTheme.FordBridge));
    }

    [Test]
    public void GenericTerrain_UsesGroupBiome_OrSignedDefault()
    {
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Road, WorldPopulationBiome.Ruins, 1, out string ruins), Is.EqualTo(RoadArenaTheme.RuinedHillfort));
        Assert.That(ruins, Does.Contain("биотоп"));
        Assert.That(RoadArenaTheme.For(WorldMapGameplayTerrainType.Field, string.Empty, 1, out string plain), Is.EqualTo(RoadArenaTheme.DryGlade));
        Assert.That(plain, Does.Contain("обычный пресет"));
    }
}
