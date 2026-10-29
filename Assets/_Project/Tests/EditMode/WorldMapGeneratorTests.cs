using System.Collections.Generic;
using System.Linq;
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

        // Бриф правок п. 15: типы без содержания (только «Исследовать») точками
        // карты не становятся; остальные знаки — места.
        foreach (GeneratedMapSite site in map.Landmarks)
        {
            bool expected = !GeneratedMapPlaces.KindsWithoutContent.Contains(site.Kind);
            Assert.That(state.Locations.Exists(location => location.PlaceKind == site.Kind), Is.EqualTo(expected), $"Знак '{site.Kind}'.");
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
        WorldMapLayers layers = null;
        Texture2D map = null;
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.ConiferSprites.Add(sprite);
            style.LeafSprites.Add(sprite);
            GeneratedWorldMap generated = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
            map = WorldMapPainter.Paint(generated, style, 0.1f, false, out layers);

            Assert.That(layers.Signs.Items.Count, Is.GreaterThan(0), "Деревья своими рисунками ушли в слой поверх карты.");
            Assert.That(layers.Signs.Atlas, Is.Not.Null);
            WorldMapSign sign = layers.Signs.Items[0];
            Assert.That(sign.Aspect, Is.EqualTo(0.5f).Within(0.01f));
            Color top = layers.Signs.Atlas.GetPixelBilinear(sign.Uv.center.x, sign.Uv.yMax - sign.Uv.height * 0.1f);
            Color bottom = layers.Signs.Atlas.GetPixelBilinear(sign.Uv.center.x, sign.Uv.yMin + sign.Uv.height * 0.1f);
            Assert.That(top.r, Is.GreaterThan(0.8f), "Верх рисунка в атласе — верх (красный).");
            Assert.That(bottom.b, Is.GreaterThan(0.8f), "Низ рисунка в атласе — низ (синий).");
            Assert.That(layers.Signs.Atlas.mipmapCount, Is.GreaterThan(1), "У атласа есть mip-карты.");
        }
        finally
        {
            layers?.Destroy();
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void TexturesOverMap_KeepTheirOwnResolution_AsTiledLayers()
    {
        Texture2D pattern = new Texture2D(96, 96, TextureFormat.RGBA32, false);
        WorldMapLayers layers = null;
        Texture2D ground = null;
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.BasePattern = pattern;
            style.FindTerrain(WorldMapGameplayTerrainType.Forest).Pattern = pattern;
            GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
            ground = WorldMapPainter.Paint(map, style, 0.1f, false, out layers);

            Assert.That(ground.width, Is.EqualTo(4), "Рисунок карты при слоях — однотонный фон.");
            WorldMapTextureLayer basePattern = layers.Layers.Find(layer => layer.Name == "Узор подложки");
            Assert.That(basePattern, Is.Not.Null);
            Assert.That(basePattern.Tiled, Is.True);
            Assert.That(basePattern.Texture.width, Is.EqualTo(96), "Текстура в собственном разрешении, а не в разрешении карты.");
            Assert.That(basePattern.Texture.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(basePattern.Texture.mipmapCount, Is.GreaterThan(1));

            string forestName = WorldMapTerrainLabels.Name(WorldMapGameplayTerrainType.Forest);
            WorldMapTextureLayer forest = layers.Layers.Find(layer => layer.Name == "Узор: " + forestName);
            Assert.That(forest, Is.Not.Null, "Узор леса — отдельный слой.");
            Assert.That(forest.Triangles, Is.Not.Null.And.Not.Empty, "Узор леса лежит в рваной границе леса, а не растворяется.");
            Assert.That(forest.Triangles.Length % 6, Is.EqualTo(0));

            WorldMapTextureLayer forestTint = layers.Layers.Find(layer => layer.Name == "Местность: " + forestName);
            Assert.That(forestTint?.Triangles, Is.Not.Null.And.Not.Empty, "Подкраска леса — заливка треугольниками в рваной границе.");
            Assert.That(forestTint.Texture, Is.Null);
            for (int i = 0; i < forestTint.Triangles.Length; i += 2)
            {
                Assert.That(forestTint.Triangles[i], Is.InRange(0f, forestTint.SourceWidth));
                Assert.That(forestTint.Triangles[i + 1], Is.InRange(0f, forestTint.SourceHeight));
            }

            int baseIndex = layers.Layers.IndexOf(basePattern);
            int tintIndex = layers.Layers.IndexOf(forestTint);
            int waterIndex = layers.Layers.FindIndex(layer => layer.Name == "Вода");
            int shoreInkIndex = layers.Layers.FindIndex(layer => layer.Name == "Тушь берега");
            int roadsIndex = layers.Layers.FindIndex(layer => layer.Name == "Дороги и тропы");
            Assert.That(baseIndex, Is.LessThan(tintIndex), "Подложка под подкраской местности.");
            Assert.That(tintIndex, Is.LessThan(layers.Layers.IndexOf(forest)));
            Assert.That(layers.Layers.IndexOf(forest), Is.LessThan(waterIndex), "Узоры под водой.");
            Assert.That(waterIndex, Is.LessThan(shoreInkIndex), "Тушь берега поверх воды.");
            Assert.That(shoreInkIndex, Is.LessThan(roadsIndex), "Дороги поверх туши берега.");
        }
        finally
        {
            layers?.Destroy();
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(pattern);
        }
    }

    // Толщина мазка-кольца по точкам: расстояние между внешним и
    // внутренним краем.
    private static List<float> RingWidths(WorldMapLayers layers, string name)
    {
        List<float> widths = new List<float>();
        WorldMapTextureLayer layer = layers.Layers.Find(item => item.Name == name);
        foreach (WorldMapVectorShape shape in layer.Shapes)
        {
            if (shape.Contours.Count != 2 || shape.Contours[0].Length != shape.Contours[1].Length)
                continue;
            for (int i = 0; i < shape.Contours[0].Length; i++)
                widths.Add(Vector2.Distance(shape.Contours[0][i], shape.Contours[1][i]));
        }
        return widths;
    }

    [Test]
    public void RaggedTerrainEdges_MeetWithoutGapsOrOverlaps()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        foreach (float raggedness in new[] { 0f, 1f })
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.EdgeRaggedness = raggedness;
            Texture2D ground = WorldMapPainter.Paint(map, style, 0.25f, false, out WorldMapLayers layers);
            try
            {
                double area = 0.0;
                float canvas = 0f;
                int fills = 0;
                foreach (WorldMapTextureLayer layer in layers.Layers)
                {
                    if (!layer.Name.StartsWith("Местность: "))
                        continue;
                    fills++;
                    canvas = layer.SourceWidth * layer.SourceHeight;
                    float[] t = layer.Triangles;
                    for (int i = 0; i < t.Length; i += 6)
                        area += Mathf.Abs((t[i + 2] - t[i]) * (t[i + 5] - t[i + 1]) - (t[i + 4] - t[i]) * (t[i + 3] - t[i + 1])) * 0.5;
                }
                Assert.That(fills, Is.GreaterThan(2));
                // Сумма заливок — вся карта без озёр: соседние типы делят край.
                Assert.That(area, Is.LessThanOrEqualTo(canvas * 1.002), "Рваные края типов не налезают друг на друга (рваность " + raggedness + ").");
                Assert.That(area, Is.GreaterThan(canvas * 0.9), "Между типами нет щелей (рваность " + raggedness + ").");
            }
            finally
            {
                layers.Destroy();
                Object.DestroyImmediate(ground);
            }
        }
    }

    private static bool InsideTriangles(float[] t, Vector2 p)
    {
        for (int i = 0; i < t.Length; i += 6)
        {
            float d1 = (p.x - t[i + 2]) * (t[i + 1] - t[i + 3]) - (t[i] - t[i + 2]) * (p.y - t[i + 3]);
            float d2 = (p.x - t[i + 4]) * (t[i + 3] - t[i + 5]) - (t[i + 2] - t[i + 4]) * (p.y - t[i + 5]);
            float d3 = (p.x - t[i]) * (t[i + 5] - t[i + 1]) - (t[i + 4] - t[i]) * (p.y - t[i + 1]);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(negative && positive))
                return true;
        }
        return false;
    }

    // Доля точек линии туши, по разные стороны которой (на δ пикселей)
    // заливка есть с одной стороны и нет с другой: край заливки — на линии.
    private static float ShareOnInk(WorldMapLayers layers, string inkLayer, string fillLayer, float delta)
    {
        WorldMapTextureLayer ink = layers.Layers.Find(item => item.Name == inkLayer);
        WorldMapTextureLayer fill = layers.Layers.Find(item => item.Name == fillLayer);
        int samples = 0, onLine = 0;
        foreach (WorldMapVectorShape shape in ink.Shapes)
        {
            if (shape.Contours.Count != 2 || shape.Contours[0].Length != shape.Contours[1].Length)
                continue;
            Vector2[] outer = shape.Contours[0], inner = shape.Contours[1];
            for (int i = 0; i < outer.Length && samples < 160; i += Mathf.Max(1, outer.Length / 6))
            {
                Vector2 middle = (outer[i] + inner[i]) * 0.5f;
                Vector2 across = (outer[i] - inner[i]).normalized;
                samples++;
                if (InsideTriangles(fill.Triangles, middle + across * delta) != InsideTriangles(fill.Triangles, middle - across * delta))
                    onLine++;
            }
        }
        Assert.That(samples, Is.GreaterThan(40), "Есть кольца туши для проверки (" + inkLayer + ").");
        return onLine / (float)samples;
    }

    [Test]
    public void FillsStopExactlyOnTheInkLine()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        style.Ink.Shore.Gaps = 0f;
        style.Ink.ForestEdge.Gaps = 0f;
        style.Ink.Shore.WobbleHexes = 0.06f;
        Texture2D ground = WorldMapPainter.Paint(map, style, 0.5f, false, out WorldMapLayers layers);
        try
        {
            string forest = "Местность: " + WorldMapTerrainLabels.Name(WorldMapGameplayTerrainType.Forest);
            float shore = ShareOnInk(layers, "Тушь берега", "Вода", 0.6f);
            float edge = ShareOnInk(layers, "Кромка леса", forest, 0.6f);
            Assert.That(shore, Is.GreaterThanOrEqualTo(0.97f), "Край воды лежит на линии туши берега.");
            Assert.That(edge, Is.GreaterThanOrEqualTo(0.97f), "Заливка леса обрывается по кромке.");
        }
        finally
        {
            layers.Destroy();
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void EdgeBlends_OverlapNeighbours_WithoutGaps()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        foreach (WorldMapEdgeBlend blend in new[] { WorldMapEdgeBlend.Tongues, WorldMapEdgeBlend.Spots, WorldMapEdgeBlend.Streaks, WorldMapEdgeBlend.Wash })
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.EdgeBlend = blend;
            style.EdgeBlendWidthHexes = 0.6f;
            style.EdgeBlendScaleHexes = 0.8f;
            Texture2D ground = WorldMapPainter.Paint(map, style, 0.25f, false, out WorldMapLayers layers);
            try
            {
                double area = 0.0;
                float canvas = 0f;
                bool melting = false;
                foreach (WorldMapTextureLayer layer in layers.Layers)
                {
                    if (!layer.Name.StartsWith("Местность: "))
                        continue;
                    canvas = layer.SourceWidth * layer.SourceHeight;
                    float[] t = layer.Triangles;
                    for (int i = 0; i < t.Length; i += 6)
                        area += Mathf.Abs((t[i + 2] - t[i]) * (t[i + 5] - t[i + 1]) - (t[i + 4] - t[i]) * (t[i + 3] - t[i + 1])) * 0.5;
                    melting |= layer.VertexAlpha != null && layer.VertexAlpha.Any(alpha => alpha < 0.5f);
                }
                Assert.That(area, Is.GreaterThan(canvas * 1.01), "Заливки заходят друг на друга (" + blend + ").");
                Assert.That(area, Is.GreaterThan(canvas * 0.9), "Щелей нет (" + blend + ").");
                if (blend == WorldMapEdgeBlend.Wash)
                    Assert.That(melting, Is.True, "Размывка тает к соседней заливке.");
            }
            finally
            {
                layers.Destroy();
                Object.DestroyImmediate(ground);
            }
        }
    }

    [Test]
    public void InkBrush_Bristles_Strokes_AndGaps_ChangeTheStroke()
    {
        WorldMapInkBrush brush = new WorldMapInkBrush(new WorldMapNoise(5), 20f);
        List<Vector2> line = new List<Vector2>();
        for (int i = 0; i <= 40; i++)
            line.Add(new Vector2(i * 10f, Mathf.Sin(i * 0.3f) * 20f));
        int Shapes(System.Action<WorldMapStrokeStyle> setup)
        {
            WorldMapStrokeStyle style = new WorldMapStrokeStyle { Gaps = 0f };
            setup(style);
            List<WorldMapVectorShape> shapes = new List<WorldMapVectorShape>();
            brush.Stroke(shapes, line, false, style, 3f, Color.black, 1);
            return shapes.Count;
        }
        Assert.That(Shapes(_ => { }), Is.EqualTo(1), "Сплошная линия — один мазок.");
        Assert.That(Shapes(s => s.Bristles = 4), Is.EqualTo(4), "Сухая кисть — мазок на каждый волосок.");
        Assert.That(Shapes(s => s.StrokeLengthHexes = 1f), Is.GreaterThan(5), "Штрихи внахлёст — много коротких мазков.");
        Assert.That(Shapes(s => { s.Gaps = 0.5f; s.GapScaleHexes = 1f; }), Is.GreaterThan(1), "Обрывы делят линию.");
        Assert.That(Shapes(s => s.Enabled = false), Is.EqualTo(0));

        List<WorldMapVectorShape> blunt = new List<WorldMapVectorShape>();
        brush.Stroke(blunt, line, false, new WorldMapStrokeStyle { Gaps = 0f, Ends = WorldMapStrokeEnd.Blunt }, 3f, Color.black, 1);
        List<WorldMapVectorShape> pointed = new List<WorldMapVectorShape>();
        brush.Stroke(pointed, line, false, new WorldMapStrokeStyle { Gaps = 0f, Ends = WorldMapStrokeEnd.Pointed }, 3f, Color.black, 1);
        Assert.That(blunt[0].Contours[0].Length, Is.GreaterThan(pointed[0].Contours[0].Length), "Тупые концы — со скруглением.");
    }

    [Test]
    public void OldInkSettings_MoveIntoLineProfiles()
    {
        WorldMapArtStyle style = new WorldMapArtStyle { InkVariation = 0.2f, InkGaps = 0.3f, ForestOutline = 0.4f, RoadInk = 0.7f };
        Assert.That(style.Ink.Version, Is.EqualTo(0));
        Assert.That(style.UpgradeInk(), Is.True);
        Assert.That(style.Ink.Shore.Pressure, Is.EqualTo(0.2f).Within(1e-4f));
        Assert.That(style.Ink.Shore.Gaps, Is.EqualTo(0.3f).Within(1e-4f));
        Assert.That(style.Ink.ForestEdge.Opacity, Is.EqualTo(0.4f).Within(1e-4f));
        Assert.That(style.Ink.RoadEdge.Opacity, Is.EqualTo(0.7f).Within(1e-4f));
        Assert.That(style.UpgradeInk(), Is.False, "Перенос — один раз.");
        WorldMapArtStyle copy = style.Clone();
        copy.Ink.Shore.Pressure = 0.9f;
        Assert.That(style.Ink.Shore.Pressure, Is.EqualTo(0.2f).Within(1e-4f), "Копия стиля не делит профили туши с оригиналом.");
    }

    [Test]
    public void InkOnlyRepaint_RebuildsLines_AndKeepsFills()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        style.Ink.Shore.Gaps = 0f;
        Texture2D ground = WorldMapPainter.Paint(map, style, 0.25f, false, out WorldMapLayers layers);
        try
        {
            string forestName = "Местность: " + WorldMapTerrainLabels.Name(WorldMapGameplayTerrainType.Forest);
            float[] forestBefore = layers.Layers.Find(layer => layer.Name == forestName).Triangles;
            Assert.That(RingWidths(layers, "Тушь берега").Max() / RingWidths(layers, "Тушь берега").Min(), Is.GreaterThan(1.8f));

            style.Ink.Shore.Pressure = 0f;
            style.Ink.Shore.Nib = 0f;
            Assert.That(WorldMapPainter.RepaintInk(layers, out bool structureChanged), Is.True);
            Assert.That(structureChanged, Is.False);
            List<float> even = RingWidths(layers, "Тушь берега");
            Assert.That(even.Max() / even.Min(), Is.LessThan(1.25f), "Тушь берега перерисована по новым настройкам.");
            Assert.That(layers.Layers.Find(layer => layer.Name == forestName).Triangles, Is.SameAs(forestBefore), "Заливки не перестраивались.");

            style.Ink.ShoreEchoCount = 2;
            Assert.That(WorldMapPainter.RepaintInk(layers, out structureChanged), Is.True);
            Assert.That(structureChanged, Is.True, "Появился слой линий воды.");
            int echo = layers.Layers.FindIndex(layer => layer.Name == "Линии воды у берега");
            Assert.That(echo, Is.GreaterThan(layers.Layers.FindIndex(layer => layer.Name == "Вода")));
            Assert.That(echo, Is.LessThan(layers.Layers.FindIndex(layer => layer.Name == "Тушь берега")));

            Assert.That(WorldMapPainter.RepaintInk(new WorldMapLayers(), out _), Is.False, "Чужой рисунок — только полной перерисовкой.");
        }
        finally
        {
            layers.Destroy();
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void ShoreEchoLines_AreDrawnInsideWater()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
        style.Ink.ShoreEchoCount = 2;
        Texture2D ground = WorldMapPainter.Paint(map, style, 0.25f, false, out WorldMapLayers layers);
        try
        {
            int echo = layers.Layers.FindIndex(layer => layer.Name == "Линии воды у берега");
            Assert.That(echo, Is.GreaterThan(layers.Layers.FindIndex(layer => layer.Name == "Вода")));
            Assert.That(layers.Layers[echo].Shapes, Is.Not.Empty);
        }
        finally
        {
            layers.Destroy();
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void InkStrokes_AreThickerAndThinner_LikeABrush()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12);
        WorldMapArtStyle brush = WorldMapArtStyle.CreateDefault();
        brush.Ink.Shore.Gaps = 0f;
        WorldMapArtStyle even = WorldMapArtStyle.CreateDefault();
        even.Ink.Shore.Gaps = 0f;
        even.Ink.Shore.Pressure = 0f;
        even.Ink.Shore.Nib = 0f;
        Texture2D a = WorldMapPainter.Paint(map, brush, 0.25f, false, out WorldMapLayers brushLayers);
        Texture2D b = WorldMapPainter.Paint(map, even, 0.25f, false, out WorldMapLayers evenLayers);
        try
        {
            List<float> brushWidths = RingWidths(brushLayers, "Тушь берега");
            List<float> evenWidths = RingWidths(evenLayers, "Тушь берега");
            Assert.That(brushWidths.Count, Is.GreaterThan(20), "Берега обведены мазками-кольцами.");
            Assert.That(brushWidths.Max() / brushWidths.Min(), Is.GreaterThan(1.8f), "Мазок то тоньше, то толще.");
            Assert.That(evenWidths.Max() / evenWidths.Min(), Is.LessThan(1.25f), "Без разнотолщинности — ровная линия.");
        }
        finally
        {
            brushLayers.Destroy();
            evenLayers.Destroy();
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void SignsBakedIntoTheMap_WhenOverlayIsOff()
    {
        Sprite sprite = TwoToneSprite(out Texture2D texture);
        WorldMapLayers layers = null;
        Texture2D map = null;
        try
        {
            WorldMapArtStyle style = WorldMapArtStyle.CreateDefault();
            style.ConiferSprites.Add(sprite);
            style.SignsOverMap = false;
            map = WorldMapPainter.Paint(WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 12), style, 0.1f, false, out layers);
            Assert.That(layers.Signs.Items, Is.Empty);
            Assert.That(layers.Signs.Atlas, Is.Null);
        }
        finally
        {
            layers?.Destroy();
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
