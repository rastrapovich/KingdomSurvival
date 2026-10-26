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
    public void DefaultCatalog_CoversLorePlaceTypes_WithSources()
    {
        List<WorldMapLandmarkRule> rules = WorldMapGenerationSettings.DefaultLandmarks();
        string[] expected = { "cave", "grove", "barrow", "hillfort", "abandoned-village", "abandoned-yard", "sanctuary", "crossroads", "lair", "spring", "hunters-hut", "apiary", "mine", "ruins", "watermill" };
        foreach (string id in expected)
        {
            WorldMapLandmarkRule rule = rules.Find(item => item.Id == id);
            Assert.That(rule, Is.Not.Null, id);
            Assert.That(rule.Source, Is.Not.Empty, $"У типа места '{id}' должен быть указан источник.");
            Assert.That(rule.Description, Is.Not.Empty, id);
        }
        Assert.That(rules.ConvertAll(rule => rule.Id), Is.Unique);
    }

    [TestCase(4)]
    [TestCase(21)]
    public void Crossroads_StandOnRoadJunctions(int seed)
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        WorldMapHexGrid grid = layer.Grid;
        foreach (GeneratedMapSite site in map.Landmarks.FindAll(item => item.Kind == "crossroads"))
        {
            WorldMapHexCell cell = grid.CellAt(site.Cell);
            Assert.That(layer.Get(cell), Is.EqualTo(WorldMapGameplayTerrainType.Road));
            int roads = 0;
            for (int d = 0; d < 6; d++)
            {
                WorldMapHexCell next = cell.Neighbor(d);
                if (grid.IsInside(next) && layer.Get(next) == WorldMapGameplayTerrainType.Road)
                    roads++;
            }
            Assert.That(roads, Is.GreaterThanOrEqualTo(3));
        }
    }

    [Test]
    public void FreePlayCampaign_TurnsVillagesAndLandmarksIntoPlaces()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 31);
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId }.CreateCampaign(null, null, map);

        List<LocationData> generated = state.Locations.FindAll(location => location.Id.StartsWith(GeneratedMapPlaces.IdPrefix));
        Assert.That(generated.ConvertAll(location => location.Id), Is.Unique);
        Assert.That(generated.FindAll(location => location.PlaceKind == GeneratedMapPlaces.VillageKind).Count,
            Is.EqualTo(map.Villages.Count - 1), "Каждая деревня, кроме стоянки, — место карты.");

        foreach (GeneratedMapSite site in map.Landmarks)
        {
            Assert.That(state.Locations.Exists(location => location.PlaceKind == site.Kind), Is.True, $"У знака '{site.Kind}' нет места.");
        }

        // Шахта из Базы карты встаёт у знака шахты и не дублируется местом генератора.
        LocationData mine = state.FindLocation("mine");
        Assert.That(mine, Is.Not.Null);
        Assert.That(mine.PlaceKind, Is.EqualTo("mine"));
        int mineSites = map.Landmarks.FindAll(site => site.Kind == "mine").Count;
        Assert.That(generated.FindAll(location => location.PlaceKind == "mine").Count, Is.EqualTo(mineSites - 1));

        LocationData cave = generated.Find(location => location.PlaceKind == "cave");
        if (cave != null)
        {
            Assert.That(cave.IsVisibleOnMap, Is.False, "Пещера скрыта, пока её не найдут.");
            Assert.That(cave.ExplorationHours, Is.GreaterThan(0.0));
            Assert.That(cave.InteractionDescription, Is.Not.Empty);
        }

        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state)));
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        Assert.That(restored.Locations.FindAll(location => location.Id.StartsWith(GeneratedMapPlaces.IdPrefix)).Count, Is.EqualTo(generated.Count));
        Assert.That(restored.FindLocation(generated[0].Id).PlaceKind, Is.EqualTo(generated[0].PlaceKind));
    }

    [Test]
    public void OldGeneratorAsset_GetsTheLoreCatalog_WithoutLosingAuthorEdits()
    {
        WorldMapGeneratorAsset asset = ScriptableObject.CreateInstance<WorldMapGeneratorAsset>();
        try
        {
            WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
            settings.HexesAcross = 122;
            settings.Landmarks = new List<WorldMapLandmarkRule>
            {
                new WorldMapLandmarkRule { Id = "barrow", Name = "Курган", Count = 5 }
            };
            asset.Settings = settings;
            typeof(WorldMapGeneratorAsset)
                .GetField("landmarkCatalogVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(asset, 0);

            Assert.That(asset.UpgradeLandmarkCatalog(), Is.True);
            Assert.That(asset.Settings.HexesAcross, Is.EqualTo(122));
            WorldMapLandmarkRule barrow = asset.Settings.Landmarks.Find(rule => rule.Id == "barrow");
            Assert.That(barrow.Count, Is.EqualTo(5), "Число, выбранное автором, остаётся.");
            Assert.That(barrow.Description, Is.Not.Empty);
            Assert.That(asset.Settings.Landmarks.Exists(rule => rule.Id == "cave"), Is.True);
            Assert.That(asset.UpgradeLandmarkCatalog(), Is.False, "Повторно не обновляется.");
        }
        finally
        {
            Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void LandmarkArt_PicksVariantsStably()
    {
        Texture2D texture = new Texture2D(4, 4);
        Sprite a = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
        Sprite b = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
        try
        {
            WorldMapLandmarkArt art = new WorldMapLandmarkArt { Sprite = a, Variants = new List<Sprite> { null, b } };
            Assert.That(art.PickSprite(0), Is.SameAs(a));
            Assert.That(art.PickSprite(1), Is.SameAs(b));
            Assert.That(art.PickSprite(7), Is.SameAs(art.PickSprite(7)));
            Assert.That(new WorldMapLandmarkArt().PickSprite(3), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void ArtPools_TakeOldSingleSprites_AndHoldAtMostTen()
    {
        Texture2D texture = new Texture2D(4, 4);
        List<Sprite> created = new List<Sprite>();
        Sprite Make()
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            created.Add(sprite);
            return sprite;
        }
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            Sprite bridge = Make();
            Sprite icon = Make();
            style.BridgeSprite = bridge;
            style.Landmarks.Add(new WorldMapLandmarkArt { RuleId = "cave", Icon = icon, Sprite = Make() });
            for (int i = 0; i < WorldMapArtStyle.MaxPoolSize + 4; i++)
                style.ConiferSprites.Add(Make());

            Assert.That(style.NormalizePools(), Is.True);
            Assert.That(style.BridgeSprite, Is.Null);
            Assert.That(style.BridgeSprites, Does.Contain(bridge));
            Assert.That(style.ConiferSprites.Count, Is.EqualTo(WorldMapArtStyle.MaxPoolSize));
            WorldMapLandmarkArt cave = style.FindLandmark("cave");
            Assert.That(cave.Icons, Does.Contain(icon));
            Assert.That(cave.Variants.Count, Is.EqualTo(1));
            Assert.That(style.NormalizePools(), Is.False, "Повторно ничего не меняется.");
        }
        finally
        {
            foreach (Sprite sprite in created)
                Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void PickStable_GivesTheSameIconForTheSamePlace_AndUsesThePool()
    {
        Texture2D texture = new Texture2D(4, 4);
        List<Sprite> pool = new List<Sprite>();
        try
        {
            for (int i = 0; i < 10; i++)
                pool.Add(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero));
            pool.Insert(3, null);

            Assert.That(WorldMapArtStyle.PickStable(pool, "gen-cave-1"), Is.SameAs(WorldMapArtStyle.PickStable(pool, "gen-cave-1")));
            HashSet<Sprite> used = new HashSet<Sprite>();
            for (int i = 0; i < 200; i++)
                used.Add(WorldMapArtStyle.PickStable(pool, "gen-village-" + i));
            Assert.That(used, Does.Not.Contain(null));
            Assert.That(used.Count, Is.GreaterThanOrEqualTo(8), "Разные места получают разные иконки из пула.");
            Assert.That(WorldMapArtStyle.PickStable(new List<Sprite>(), "x"), Is.Null);
        }
        finally
        {
            foreach (Sprite sprite in pool)
            {
                if (sprite != null)
                    Object.DestroyImmediate(sprite);
            }
            Object.DestroyImmediate(texture);
        }
    }

    // Все слои выключены — на карте только ровный цвет основы.
    private static WorldMapArtStyle AllLayersOff()
    {
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        style.DrawBase = style.DrawTerrain = style.DrawWater = style.DrawInk = false;
        style.DrawTrees = style.DrawReeds = style.DrawHills = style.DrawMountains = style.DrawCliffs = false;
        style.DrawRoads = style.DrawTrails = style.DrawBridges = false;
        style.DrawVillages = style.DrawLandmarks = style.MarkStart = false;
        style.DrawPost = false;
        return style;
    }

    [Test]
    public void AllLayersOff_LeavesOnlyTheFlatBaseColor()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 8);
        WorldMapArtStyle style = AllLayersOff();
        Texture2D texture = WorldMapPainter.Paint(map, style, 0.1f, true);
        try
        {
            Color32 expected = style.BaseColor;
            foreach (Color32 pixel in texture.GetPixels32())
            {
                Assert.That(Mathf.Abs(pixel.r - expected.r), Is.LessThanOrEqualTo(1));
                Assert.That(Mathf.Abs(pixel.g - expected.g), Is.LessThanOrEqualTo(1));
                Assert.That(Mathf.Abs(pixel.b - expected.b), Is.LessThanOrEqualTo(1));
            }
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void EachLayerSwitch_ChangesThePicture()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 8);
        Color32[] Paint(WorldMapArtStyle style)
        {
            Texture2D texture = WorldMapPainter.Paint(map, style, 0.12f, true);
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }
        Color32[] flat = Paint(AllLayersOff());

        (string Name, System.Action<WorldMapArtStyle> TurnOn)[] layers =
        {
            ("Местность", s => s.DrawTerrain = true),
            ("Вода", s => s.DrawWater = true),
            ("Деревья", s => s.DrawTrees = true),
            ("Холмы", s => s.DrawHills = true),
            ("Горы", s => s.DrawMountains = true),
            ("Дороги", s => s.DrawRoads = true),
            ("Деревни", s => s.DrawVillages = true),
            ("Места", s => s.DrawLandmarks = true),
            ("Обработка", s => s.DrawPost = true)
        };
        foreach ((string name, System.Action<WorldMapArtStyle> turnOn) in layers)
        {
            WorldMapArtStyle style = AllLayersOff();
            turnOn(style);
            Color32[] pixels = Paint(style);
            int changed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!pixels[i].Equals(flat[i]))
                    changed++;
            }
            Assert.That(changed, Is.GreaterThan(0), $"Слой «{name}» не появился, хотя его включили одного.");
        }
    }

    [Test]
    public void PoolSwitches_TurnCustomArtOff_WithoutLosingIt()
    {
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        Assert.That(WorldMapArtStyle.MaxPoolSize, Is.EqualTo(20));
        Assert.That(style.Pool(style.ConiferSprites, style.UseConiferSprites), Is.SameAs(style.ConiferSprites));
        style.UseConiferSprites = false;
        Assert.That(style.Pool(style.ConiferSprites, style.UseConiferSprites), Is.Null);
        style.UseConiferSprites = true;
        style.UseCustomArt = false;
        Assert.That(style.Pool(style.HillSprites, style.UseHillSprites), Is.Null, "Общий выключатель гасит все пулы.");
    }

    // Рисунок 32×64: верхняя половина красная, нижняя синяя.
    private static Sprite TwoToneSprite(out Texture2D texture)
    {
        texture = new Texture2D(32, 64, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[32 * 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = y >= 32 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 32, 64), new Vector2(0.5f, 0f));
    }

    [Test]
    public void SignsOverMap_KeepTheLoadedPicture_UprightInTheAtlas()
    {
        Sprite sprite = TwoToneSprite(out Texture2D texture);
        WorldMapSigns signs = null;
        Texture2D map = null;
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.ConiferSprites.Add(sprite);
            style.LeafSprites.Add(sprite);
            GeneratedWorldMap generated = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
            map = WorldMapPainter.Paint(generated, style, 0.1f, false, out signs);

            Assert.That(signs.Items.Count, Is.GreaterThan(0), "Деревья своими рисунками ушли в слой поверх карты.");
            Assert.That(signs.Atlas, Is.Not.Null);
            WorldMapSign sign = signs.Items[0];
            Assert.That(sign.Aspect, Is.EqualTo(0.5f).Within(0.01f));
            Color top = signs.Atlas.GetPixelBilinear(sign.Uv.center.x, sign.Uv.yMax - sign.Uv.height * 0.1f);
            Color bottom = signs.Atlas.GetPixelBilinear(sign.Uv.center.x, sign.Uv.yMin + sign.Uv.height * 0.1f);
            Assert.That(top.r, Is.GreaterThan(0.8f), "Верх рисунка в атласе — верх (красный).");
            Assert.That(bottom.b, Is.GreaterThan(0.8f), "Низ рисунка в атласе — низ (синий).");
            Assert.That(signs.Atlas.mipmapCount, Is.GreaterThan(1), "У атласа есть mip-карты.");
        }
        finally
        {
            signs?.Destroy();
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void SignsBakedIntoTheMap_WhenOverlayIsOff()
    {
        Sprite sprite = TwoToneSprite(out Texture2D texture);
        WorldMapSigns signs = null;
        Texture2D map = null;
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.ConiferSprites.Add(sprite);
            style.SignsOverMap = false;
            map = WorldMapPainter.Paint(WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12), style, 0.1f, false, out signs);
            Assert.That(signs.Items, Is.Empty);
            Assert.That(signs.Atlas, Is.Null);
        }
        finally
        {
            signs?.Destroy();
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(sprite);
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
