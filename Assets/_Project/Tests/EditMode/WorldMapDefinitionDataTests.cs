using NUnit.Framework;

// AM-01 (канон v1.33, §9.9): контракт авторского постоянного мира —
// ConfigureFromDefinition должен давать одинаковый рельеф независимо от
// того, какой WorldSeed использовался раньше, и не зависеть от seed вовсе.
// Река больше не часть этого контракта — карта переходит на вручную
// нарисованное полотно, где вода уже присутствует как арт.
public class WorldMapDefinitionDataTests
{
    [Test]
    public void ConfigureFromDefinition_ProducesIdenticalTerrainRegardlessOfPriorSeed()
    {
        WorldMapDefinitionData definition = BuildTestDefinition();

        WorldMapNavigation.ConfigureTerrain(1);
        WorldMapNavigation.ConfigureFromDefinition(definition);
        WorldMapTerrainType afterSeedOne = WorldMapNavigation.GetTerrainAtGridCell(5, 5);

        WorldMapNavigation.ConfigureTerrain(999999);
        WorldMapNavigation.ConfigureFromDefinition(definition);
        WorldMapTerrainType afterSeedTwo = WorldMapNavigation.GetTerrainAtGridCell(5, 5);

        Assert.That(afterSeedTwo, Is.EqualTo(afterSeedOne));
        Assert.That(afterSeedOne, Is.EqualTo(WorldMapTerrainType.Mountains));
    }

    [Test]
    public void ConfigureFromDefinition_LeavesAreaOutsideAuthoredZonesAsPlains()
    {
        WorldMapDefinitionData definition = BuildTestDefinition();
        WorldMapNavigation.ConfigureFromDefinition(definition);

        Assert.That(
            WorldMapNavigation.GetTerrainAtGridCell(
                WorldMapNavigation.GridWidth - 1,
                WorldMapNavigation.GridHeight - 1),
            Is.EqualTo(WorldMapTerrainType.Plains));
    }

    [TearDown]
    public void ResetToDefaultProcedure()
    {
        // Не оставлять авторский мир активным для тестов, идущих следом в
        // том же прогоне (WorldMapNavigation хранит состояние статически).
        WorldMapNavigation.ConfigureTerrain(0);
    }

    private static WorldMapDefinitionData BuildTestDefinition()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData
        {
            WorldDefinitionId = "am01-test-world"
        };

        definition.TerrainAreas.Add(new WorldMapTerrainAreaData
        {
            Id = "test-mountains",
            Terrain = WorldMapTerrainType.Mountains,
            MinXPercent = 0f,
            MaxXPercent = 20f,
            MinYPercent = 0f,
            MaxYPercent = 20f,
            Priority = 0
        });

        return definition;
    }
}
