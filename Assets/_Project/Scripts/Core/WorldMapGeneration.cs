using System;
using System.Collections.Generic;

// Канон v1.58 §9.9: карта «Свободной игры» собирается генератором заново в
// каждой новой партии по её WorldSeed. Здесь — только чистые данные без
// UnityEngine: настройки генератора, стадии и результат (разметка клеток,
// реки, дороги, поселения, приметные места, слоты появления). Рисунок карты
// строит отдельный рисовальщик модуля WorldMapVisual из этого результата;
// разметка и рисунок происходят из одного источника и не расходятся.
//
// Результат сохраняется вместе с партией (GameState.GeneratedMap): загрузка
// не генерирует мир заново и не зависит от того, менялся ли генератор.

// Стадии генерации — порядок сборки и шаги пошагового показа в редакторе.
// Каждая стадия берёт свой поток случайности, поэтому карта «до стадии N»
// всегда совпадает с началом полной карты того же seed.
public enum WorldMapGenerationStage
{
    Relief = 0,
    Water = 1,
    Vegetation = 2,
    Settlements = 3,
    Landmarks = 4,
    Roads = 5,
    Fields = 6,
    Complete = 7
}

public static class WorldMapGenerationStages
{
    public static readonly WorldMapGenerationStage[] All =
    {
        WorldMapGenerationStage.Relief,
        WorldMapGenerationStage.Water,
        WorldMapGenerationStage.Vegetation,
        WorldMapGenerationStage.Settlements,
        WorldMapGenerationStage.Landmarks,
        WorldMapGenerationStage.Roads,
        WorldMapGenerationStage.Fields,
        WorldMapGenerationStage.Complete
    };

    public static string Title(WorldMapGenerationStage stage)
    {
        switch (stage)
        {
            case WorldMapGenerationStage.Relief: return "Рельеф";
            case WorldMapGenerationStage.Water: return "Озёра и реки";
            case WorldMapGenerationStage.Vegetation: return "Лес и болота";
            case WorldMapGenerationStage.Settlements: return "Деревни";
            case WorldMapGenerationStage.Landmarks: return "Приметные места";
            case WorldMapGenerationStage.Roads: return "Дороги и тропы";
            case WorldMapGenerationStage.Fields: return "Поля";
            default: return "Готово";
        }
    }
}

// Правило приметного места (руины, шахта, курган...). Примеры по умолчанию —
// рабочие настройки, не факты мира. LocationId связывает правило с местом
// из Базы карты: слот этого места получает тег "place:<LocationId>", и
// место встаёт именно сюда (WorldMapPopulationService).
[Serializable]
public sealed class WorldMapLandmarkRule
{
    public const string PlaceTagPrefix = "place:";

    public string Id = "landmark";
    public string Name = "Приметное место";
    public bool Enabled = true;
    public int Count = 1;
    // Где можно стоять. Пусто — любая суша.
    public List<WorldMapGameplayTerrainType> Terrains = new List<WorldMapGameplayTerrainType>();
    public bool NearWater;
    public bool NearVillage;
    // Не ближе к деревням (если не NearVillage) и к другим местам.
    public float MinDistanceHexes = 8f;
    public string LocationId = string.Empty;
    // Теги слота появления через запятую, например "Ruins, Hills".
    public string SlotTags = string.Empty;
    // Ставить ли знак на рисунок карты (место может быть и без знака).
    public bool DrawMark = true;

    public WorldMapLandmarkRule Clone()
    {
        WorldMapLandmarkRule copy = (WorldMapLandmarkRule)MemberwiseClone();
        copy.Terrains = new List<WorldMapGameplayTerrainType>(Terrains ?? new List<WorldMapGameplayTerrainType>());
        return copy;
    }
}

// Все числа генератора — рабочие настройки Базы карты, не канон.
[Serializable]
public sealed class WorldMapGenerationSettings
{
    // Масштаб: полотно (пропорции и единица координат) и число клеток по
    // ширине — сколько «мира» помещается на карту.
    public float CanvasWidth = WorldMapHexGrid.DefaultCanvasWidth;
    public float CanvasHeight = WorldMapHexGrid.DefaultCanvasHeight;
    public int HexesAcross = WorldMapHexGrid.DefaultHexesAcross;

    // Рельеф.
    public float ReliefScaleHexes = 26f;
    public int ReliefDetail = 4;
    public float ReliefRoughness = 0.5f;
    public float MountainRidges = 0.45f;
    public float HillsShare = 0.16f;
    public float MountainsShare = 0.05f;
    public float CliffsShare = 0.2f;

    // Вода.
    public float LakesShare = 0.025f;
    public int RiverCount = 3;
    public int RiverMinLengthHexes = 14;
    public float RiverWidthHexes = 0.6f;
    public float RiverMeander = 0.5f;

    // Лес и болота.
    public float ForestShare = 0.34f;
    public float ForestScaleHexes = 12f;
    public float ForestOnHills = 0.5f;
    public float SwampShare = 0.04f;
    public float MoistureScaleHexes = 20f;

    // Деревни и поля.
    public int VillageCount = 8;
    public int VillageMinDistanceHexes = 11;
    public float VillageNearWater = 0.7f;
    public float FieldRadiusHexes = 3f;
    public float FieldDensity = 0.5f;

    // Дороги: связать деревни, добавить петли и выходы к краю карты.
    public float ExtraRoadLoops = 0.25f;
    public int RoadsToEdge = 2;
    public bool TrailsToLandmarks = true;
    public float RoadAvoidHills = 2.5f;
    public float RoadAvoidForest = 1.7f;
    public float RoadAvoidSwamp = 4f;
    public float RiverCrossingCost = 7f;
    // Насколько дороги петляют (0 — кратчайший путь по сетке).
    public float RoadWander = 1.2f;

    // Вокруг точки старта — поляна, на которую не заходят вода и горы.
    public int StartClearRadiusHexes = 2;

    public List<WorldMapLandmarkRule> Landmarks = DefaultLandmarks();

    public static WorldMapGenerationSettings CreateDefault() => new WorldMapGenerationSettings();

    public static List<WorldMapLandmarkRule> DefaultLandmarks()
    {
        return new List<WorldMapLandmarkRule>
        {
            new WorldMapLandmarkRule
            {
                Id = "ruins", Name = "Руины", Count = 2, MinDistanceHexes = 9f, LocationId = "ruins", SlotTags = "Ruins",
                Terrains = new List<WorldMapGameplayTerrainType>
                    { WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Forest }
            },
            new WorldMapLandmarkRule
            {
                Id = "mine", Name = "Старая шахта", Count = 1, MinDistanceHexes = 10f, LocationId = "mine", SlotTags = "Mine, Hills",
                Terrains = new List<WorldMapGameplayTerrainType> { WorldMapGameplayTerrainType.Hills, WorldMapGameplayTerrainType.Mountains }
            },
            new WorldMapLandmarkRule
            {
                Id = "deep-forest", Name = "Глухой лес", Count = 1, MinDistanceHexes = 8f, LocationId = "forest", SlotTags = "Forest",
                DrawMark = false,
                Terrains = new List<WorldMapGameplayTerrainType> { WorldMapGameplayTerrainType.Forest }
            },
            new WorldMapLandmarkRule
            {
                Id = "barrow", Name = "Курган", Count = 2, MinDistanceHexes = 7f, SlotTags = "Barrow",
                Terrains = new List<WorldMapGameplayTerrainType> { WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Hills }
            },
            new WorldMapLandmarkRule
            {
                Id = "watermill", Name = "Водяная мельница", Count = 1, MinDistanceHexes = 3f, SlotTags = "Mill, Shore",
                NearWater = true, NearVillage = true
            }
        };
    }

    public WorldMapGenerationSettings Clone()
    {
        WorldMapGenerationSettings copy = (WorldMapGenerationSettings)MemberwiseClone();
        copy.Landmarks = new List<WorldMapLandmarkRule>();
        if (Landmarks != null)
        {
            foreach (WorldMapLandmarkRule rule in Landmarks)
            {
                if (rule != null)
                    copy.Landmarks.Add(rule.Clone());
            }
        }
        return copy;
    }

    // Безопасные пределы: генератор не падает и не зависает на любых
    // значениях из инспектора.
    public void Sanitize()
    {
        CanvasWidth = CanvasWidth > 64f && !float.IsNaN(CanvasWidth) ? CanvasWidth : WorldMapHexGrid.DefaultCanvasWidth;
        CanvasHeight = CanvasHeight > 64f && !float.IsNaN(CanvasHeight) ? CanvasHeight : WorldMapHexGrid.DefaultCanvasHeight;
        HexesAcross = Math.Max(20, Math.Min(300, HexesAcross));
        ReliefScaleHexes = Clamp(ReliefScaleHexes, 2f, 200f);
        ReliefDetail = Math.Max(1, Math.Min(7, ReliefDetail));
        ReliefRoughness = Clamp(ReliefRoughness, 0.1f, 0.9f);
        MountainRidges = Clamp(MountainRidges, 0f, 1f);
        HillsShare = Clamp(HillsShare, 0f, 0.8f);
        MountainsShare = Clamp(MountainsShare, 0f, 0.5f);
        CliffsShare = Clamp(CliffsShare, 0f, 1f);
        LakesShare = Clamp(LakesShare, 0f, 0.3f);
        RiverCount = Math.Max(0, Math.Min(24, RiverCount));
        RiverMinLengthHexes = Math.Max(2, Math.Min(400, RiverMinLengthHexes));
        RiverWidthHexes = Clamp(RiverWidthHexes, 0.05f, 3f);
        RiverMeander = Clamp(RiverMeander, 0f, 1f);
        ForestShare = Clamp(ForestShare, 0f, 0.9f);
        ForestScaleHexes = Clamp(ForestScaleHexes, 2f, 200f);
        ForestOnHills = Clamp(ForestOnHills, 0f, 1f);
        SwampShare = Clamp(SwampShare, 0f, 0.5f);
        MoistureScaleHexes = Clamp(MoistureScaleHexes, 2f, 200f);
        VillageCount = Math.Max(1, Math.Min(80, VillageCount));
        VillageMinDistanceHexes = Math.Max(2, Math.Min(100, VillageMinDistanceHexes));
        VillageNearWater = Clamp(VillageNearWater, 0f, 2f);
        FieldRadiusHexes = Clamp(FieldRadiusHexes, 0f, 10f);
        FieldDensity = Clamp(FieldDensity, 0f, 1f);
        ExtraRoadLoops = Clamp(ExtraRoadLoops, 0f, 2f);
        RoadsToEdge = Math.Max(0, Math.Min(12, RoadsToEdge));
        RoadAvoidHills = Clamp(RoadAvoidHills, 1f, 20f);
        RoadAvoidForest = Clamp(RoadAvoidForest, 1f, 20f);
        RoadAvoidSwamp = Clamp(RoadAvoidSwamp, 1f, 40f);
        RiverCrossingCost = Clamp(RiverCrossingCost, 1f, 100f);
        RoadWander = Clamp(RoadWander, 0f, 4f);
        StartClearRadiusHexes = Math.Max(0, Math.Min(6, StartClearRadiusHexes));
        if (Landmarks == null)
            Landmarks = new List<WorldMapLandmarkRule>();
        foreach (WorldMapLandmarkRule rule in Landmarks)
        {
            if (rule == null)
                continue;
            rule.Count = Math.Max(0, Math.Min(40, rule.Count));
            rule.MinDistanceHexes = Clamp(rule.MinDistanceHexes, 0f, 100f);
            if (rule.Terrains == null)
                rule.Terrains = new List<WorldMapGameplayTerrainType>();
        }
    }

    private static float Clamp(float value, float min, float max) =>
        float.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
}

// Река, дорога или тропа: клетки разметки и сглаженная линия для рисунка.
// Ширина задаётся в клетках пути и растёт от истока к устью.
[Serializable]
public sealed class GeneratedMapLine
{
    public const string RiverKind = "river";
    public const string RoadKind = "road";
    public const string TrailKind = "trail";

    public string Kind = RoadKind;
    public float StartWidthHexes;
    public float EndWidthHexes;
    public List<MapPointData> Points = new List<MapPointData>();
    public List<int> Cells = new List<int>();
}

// Деревня или приметное место. Variant — устойчивое случайное число для
// выбора рисунка (какой домик, какой знак), не меняется между загрузками.
[Serializable]
public sealed class GeneratedMapSite
{
    public const string VillageKind = "village";

    public string Kind = VillageKind;
    public string Name = string.Empty;
    public int Cell;
    public float XPercent;
    public float YPercent;
    public int Size = 1;
    public int Variant;
    public bool DrawMark = true;
    public bool IsStart;
}

[Serializable]
public sealed class GeneratedSpawnSlot
{
    public string Id;
    public List<string> Tags = new List<string>();
    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;
}

[Serializable]
public sealed class GeneratedWorldMap
{
    public const int CurrentVersion = 1;
    public const string WorldDefinitionId = "generated";

    // Поддельный объект на месте null (так JsonUtility пишет пустые ссылки)
    // имеет нулевое полотно и честно не проходит IsValid.
    public int Version = CurrentVersion;
    public int Seed;
    public float CanvasWidth;
    public float CanvasHeight;
    public int HexesAcross;
    public string TerrainCells = string.Empty;
    public WorldMapGenerationStage CompletedStage = WorldMapGenerationStage.Complete;

    public List<GeneratedMapLine> Rivers = new List<GeneratedMapLine>();
    public List<GeneratedMapLine> Roads = new List<GeneratedMapLine>();
    public List<int> LakeCells = new List<int>();
    public List<int> RiverCells = new List<int>();
    public List<GeneratedMapSite> Villages = new List<GeneratedMapSite>();
    public List<GeneratedMapSite> Landmarks = new List<GeneratedMapSite>();
    public List<MapPointData> Bridges = new List<MapPointData>();
    public List<GeneratedSpawnSlot> SpawnSlots = new List<GeneratedSpawnSlot>();
    public float StartXPercent = WorldMapNavigation.CapitalXPercent;
    public float StartYPercent = WorldMapNavigation.CapitalYPercent;

    // Только для показа в редакторе (карта высот и влажности); в сохранение
    // не попадают — игре они не нужны.
    [NonSerialized] public float[] Elevation;
    [NonSerialized] public float[] Moisture;

    public bool IsValid => Version > 0 && CanvasWidth > 1f && CanvasHeight > 1f && HexesAcross > 0;

    public WorldMapHexGrid CreateGrid() => new WorldMapHexGrid(CanvasWidth, CanvasHeight, HexesAcross);

    public WorldMapTerrainLayer CreateTerrainLayer() => WorldMapTerrainLayer.Decode(CreateGrid(), TerrainCells);

    // Карта как обычный мир для навигации и расстановки мест: та же
    // разметка клеток, полотно и слоты, что у авторского World Definition.
    public WorldMapDefinitionData ToDefinitionData()
    {
        WorldMapDefinitionData data = new WorldMapDefinitionData
        {
            WorldDefinitionId = WorldDefinitionId,
            GeographyVersion = Version,
            HomeLocationId = "home",
            HomeXPercent = StartXPercent,
            HomeYPercent = StartYPercent,
            CanvasWidth = CanvasWidth,
            CanvasHeight = CanvasHeight,
            HexesAcross = HexesAcross,
            TerrainCells = TerrainCells ?? string.Empty
        };

        if (SpawnSlots != null)
        {
            foreach (GeneratedSpawnSlot slot in SpawnSlots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.Id))
                    continue;
                data.SpawnSlots.Add(new WorldMapSpawnSlotDefinition
                {
                    Id = slot.Id,
                    Tags = new List<string>(slot.Tags ?? new List<string>()),
                    MinXPercent = slot.MinXPercent,
                    MaxXPercent = slot.MaxXPercent,
                    MinYPercent = slot.MinYPercent,
                    MaxYPercent = slot.MaxYPercent
                });
            }
        }

        return data;
    }
}
