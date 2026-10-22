using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEngine;

// Канон v1.58 §9.9: генератор карты «Свободной игры» — воспроизводимость,
// стадии, проходимость, привязка мест к слотам, сохранение карты в партии и
// рисунок.
public class WorldMapGeneratorTests
{
    [SetUp]
    public void SetUp() => WorldMapNavigation.ConfigureDefaultTerrain();

    [TearDown]
    public void TearDown() => WorldMapNavigation.ConfigureDefaultTerrain();

    [Test]
    public void SameSeedAndSettings_GiveTheSameMap()
    {
        WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        GeneratedWorldMap a = WorldMapGenerator.Generate(settings, 42);
        GeneratedWorldMap b = WorldMapGenerator.Generate(settings, 42);

        Assert.That(b.TerrainCells, Is.EqualTo(a.TerrainCells));
        Assert.That(b.Rivers.Count, Is.EqualTo(a.Rivers.Count));
        Assert.That(b.Roads.Count, Is.EqualTo(a.Roads.Count));
        Assert.That(b.Villages.Count, Is.EqualTo(a.Villages.Count));
        Assert.That(b.SpawnSlots.Count, Is.EqualTo(a.SpawnSlots.Count));
    }

    [Test]
    public void DifferentSeeds_GiveDifferentMaps()
    {
        WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        Assert.That(WorldMapGenerator.Generate(settings, 2).TerrainCells,
            Is.Not.EqualTo(WorldMapGenerator.Generate(settings, 3).TerrainCells));
    }

    [Test]
    public void DefaultSettings_ProduceAllLayers()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 7);
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        HashSet<WorldMapGameplayTerrainType> seen = new HashSet<WorldMapGameplayTerrainType>();
        for (int i = 0; i < layer.Grid.CellCount; i++)
            seen.Add(layer.GetAtIndex(i));

        Assert.That(map.CompletedStage, Is.EqualTo(WorldMapGenerationStage.Complete));
        Assert.That(seen, Does.Contain(WorldMapGameplayTerrainType.Forest));
        Assert.That(seen, Does.Contain(WorldMapGameplayTerrainType.Water));
        Assert.That(seen, Does.Contain(WorldMapGameplayTerrainType.Road));
        Assert.That(seen, Does.Contain(WorldMapGameplayTerrainType.Hills));
        Assert.That(map.Rivers.Count, Is.GreaterThan(0));
        Assert.That(map.Villages.Count, Is.GreaterThan(1));
        Assert.That(map.Landmarks.Count, Is.GreaterThan(0));
        Assert.That(map.Villages[0].IsStart, Is.True);
    }

    [Test]
    public void EarlierStage_IsThePrefixOfTheFullMap()
    {
        WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        GeneratedWorldMap water = WorldMapGenerator.Generate(settings, 11, WorldMapGenerationStage.Water);
        GeneratedWorldMap full = WorldMapGenerator.Generate(settings, 11);

        Assert.That(water.CompletedStage, Is.EqualTo(WorldMapGenerationStage.Water));
        Assert.That(water.Villages, Is.Empty);
        Assert.That(water.Roads, Is.Empty);
        Assert.That(water.Rivers.Count, Is.EqualTo(full.Rivers.Count));
        for (int r = 0; r < water.Rivers.Count; r++)
            Assert.That(water.Rivers[r].Cells, Is.EqualTo(full.Rivers[r].Cells));
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(17)]
    [TestCase(123)]
    [TestCase(9001)]
    public void StartIsClearAndEveryVillageIsReachable(int seed)
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        WorldMapHexGrid grid = layer.Grid;
        WorldMapHexCell start = grid.CellAtPercent(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent);
        WorldMapMovementRules rules = WorldMapMovementRules.CreateDefault();

        Assert.That(rules.IsTraversable(layer.Get(start)), Is.True, "Стоянка не может оказаться в воде или скалах.");

        bool[] reached = new bool[grid.CellCount];
        Queue<WorldMapHexCell> queue = new Queue<WorldMapHexCell>();
        reached[grid.IndexOf(start)] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            WorldMapHexCell cell = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                WorldMapHexCell next = cell.Neighbor(d);
                if (!grid.IsInside(next) || reached[grid.IndexOf(next)] || !rules.IsTraversable(layer.Get(next)))
                    continue;
                reached[grid.IndexOf(next)] = true;
                queue.Enqueue(next);
            }
        }

        foreach (GeneratedMapSite village in map.Villages)
            Assert.That(reached[village.Cell], Is.True, "Деревня отрезана: дороги через реки должны давать брод или мост.");
        foreach (GeneratedMapSite landmark in map.Landmarks)
            Assert.That(reached[landmark.Cell], Is.True, $"Место '{landmark.Kind}' недостижимо: к нему должна вести тропа.");
    }

    [Test]
    public void LandmarkWithLocationId_HostsThatLocation()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 9);
        WorldMapDefinitionData definition = map.ToDefinitionData();
        WorldMapNavigation.ConfigureFromDefinition(definition);

        GeneratedSpawnSlot mineSlot = map.SpawnSlots.Find(slot => slot.Tags.Contains(WorldMapLandmarkRule.PlaceTagPrefix + "mine"));
        Assert.That(mineSlot, Is.Not.Null, "У правила «Старая шахта» есть место 'mine' — у его слота должен быть тег place:mine.");

        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData { Id = "mine", Name = "Шахта", Mode = WorldMapPlacementMode.Anchored },
            new WorldMapLocationTemplateData { Id = "ruins", Name = "Руины", Mode = WorldMapPlacementMode.Anchored }
        };
        List<LocationData> placed = WorldMapPopulationService.Populate(9, templates, definition.SpawnSlots);
        LocationData mine = placed.Find(location => location.Id == "mine");

        WorldMapHexGrid grid = map.CreateGrid();
        double distance = grid.DistanceHexes(
            (mineSlot.MinXPercent + mineSlot.MaxXPercent) * 0.5f, (mineSlot.MinYPercent + mineSlot.MaxYPercent) * 0.5f,
            mine.MapXPercent, mine.MapYPercent);
        Assert.That(distance, Is.LessThan(2.0), "Шахта должна встать у знака шахты на карте.");
    }

    [Test]
    public void ExtremeSettings_DoNotBreakTheGenerator()
    {
        WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        settings.HexesAcross = 5;
        settings.RiverCount = 99;
        settings.ForestShare = 5f;
        settings.VillageCount = 0;
        settings.CanvasHeight = -10f;
        settings.Landmarks = null;

        GeneratedWorldMap map = null;
        Assert.DoesNotThrow(() => map = WorldMapGenerator.Generate(settings, 1));
        Assert.That(map.IsValid, Is.True);
        Assert.That(map.Villages.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FreePlayCampaign_KeepsItsGeneratedMapThroughSaveAndLoad()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 77);
        CampaignSetup setup = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId };
        GameState state = setup.CreateCampaign(null, null, map);

        Assert.That(state.WorldSeed, Is.EqualTo(77));
        Assert.That(state.HasGeneratedMap, Is.True);
        Assert.That(WorldMapNavigation.ActiveDefinition.WorldDefinitionId, Is.EqualTo(GeneratedWorldMap.WorldDefinitionId));

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state, "authored-world", 3);
        Assert.That(data.WorldDefinitionId, Is.EqualTo(GeneratedWorldMap.WorldDefinitionId));

        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data));
        Assert.That(CampaignSaveService.IsLoadable(loaded, "some-other-world", 9, out string reason), Is.True, reason);

        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        Assert.That(restored.HasGeneratedMap, Is.True);
        Assert.That(restored.GeneratedMap.TerrainCells, Is.EqualTo(map.TerrainCells));
        Assert.That(restored.GeneratedMap.Rivers.Count, Is.EqualTo(map.Rivers.Count));
        Assert.That(restored.GeneratedMap.Villages.Count, Is.EqualTo(map.Villages.Count));
    }

    [Test]
    public void AuthoredCampaign_HasNoGeneratedMapAfterLoad()
    {
        GameState state = new CampaignSetup().CreateCampaign();
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state)));

        Assert.That(loaded.State.GeneratedMap, Is.Not.Null, "JsonUtility подставляет пустой объект на месте null.");
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        Assert.That(restored.GeneratedMap, Is.Null);
        Assert.That(restored.HasGeneratedMap, Is.False);
    }

    [Test]
    public void Painter_DrawsEveryStageAtTheRequestedSize()
    {
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        foreach (WorldMapGenerationStage stage in WorldMapGenerationStages.All)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 3, stage);
            Texture2D texture = WorldMapPainter.Paint(map, style, 0.1f);
            Vector2Int size = WorldMapPainter.TextureSize(map, 0.1f);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(size.x));
            Assert.That(texture.height, Is.EqualTo(size.y));
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void ArtStyle_HasAnEntryForEveryTerrain_AndClonesDeep()
    {
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
            Assert.That(style.FindTerrain(type), Is.Not.Null);

        WorldMapArtStyle copy = style.Clone();
        copy.FindTerrain(WorldMapGameplayTerrainType.Forest).Opacity = 0.01f;
        Assert.That(style.FindTerrain(WorldMapGameplayTerrainType.Forest).Opacity, Is.Not.EqualTo(0.01f));
    }
}
