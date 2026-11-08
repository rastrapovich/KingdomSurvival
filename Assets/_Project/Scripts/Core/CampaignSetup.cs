using System;
using System.Collections.Generic;

// ПР-05: конфигурация кампании. Командир выбирается до старта
// (CampaignSetup), проверяется и фиксируется в GameState.Configuration — она
// сохраняется вместе с кампанией, и загрузка восстанавливает ту же
// композицию, а не собирает мир заново. Канон v1.59: режим один — свободная
// игра; выбора кризиса и начального состояния нет.
// Тексты описаний — рабочие, не канон: личность героя не утверждена.
[Serializable]
public sealed class CampaignConfiguration
{
    public const int CurrentContentVersion = 1;

    public string CampaignId = string.Empty;
    public string CrisisId = string.Empty;
    public string CommanderProfileId = string.Empty;
    public string StartingConditionId = string.Empty;
    public int ContentVersion = CurrentContentVersion;
    public int WorldSeed;
    // 12У-H: выбор карты при старте (размер, местность, враги...). Старые
    // сохранения без поля получают значения по умолчанию («обычно»).
    public CampaignMapOptions MapOptions = new CampaignMapOptions();
}

// 12У-H (запрос пользователя 08.11.2026): основные параметры карты новой
// партии — уровни «мало / обычно / много» (0/1/2) поверх настроек ассета
// генератора. Множители — те же, что у регуляторов окна «Генератор карты».
// Это рабочие настройки, не канон.
[Serializable]
public sealed class CampaignMapOptions
{
    public const int Low = 0, Normal = 1, High = 2;

    public WorldMapRegionKind RegionKind = WorldMapRegionKind.BySeed;
    public int Relief = Normal;
    public int Water = Normal;
    public int Forest = Normal;
    public int Settlements = Normal;
    public int Discoveries = Normal;
    public int Enemies = Normal;

    public static float Level(int level, float low, float high) => level <= Low ? low : level >= High ? high : 1f;

    public CampaignMapOptions Clone() => (CampaignMapOptions)MemberwiseClone();

    // Применить к копии настроек генератора (ассет не меняется).
    public void ApplyTo(WorldMapGenerationSettings p, int hexesAcross)
    {
        if (p == null)
            return;
        if (hexesAcross > 0)
            p.HexesAcross = hexesAcross;
        p.RegionKind = RegionKind;
        float relief = Level(Relief, 0.4f, 1.8f);
        p.HillsShare *= relief;
        p.MountainsShare *= relief;
        p.CliffsShare *= Level(Relief, 0.5f, 1.5f);
        p.LakesShare *= Level(Water, 0.35f, 2f);
        p.RiverCount = (int)Math.Round(p.RiverCount * Level(Water, 0.34f, 1.67f));
        p.ForestShare *= Level(Forest, 0.3f, 1.9f);
        // Деревень больше на большой карте: на 100 клетках — как в ассете.
        double area = Math.Max(1.0, Math.Pow(p.HexesAcross / 100.0, 1.5));
        p.VillageCount = Math.Max(2, (int)Math.Round(p.VillageCount * area * Level(Settlements, 0.5f, 1.6f)));
        p.FieldDensity *= Level(Settlements, 0.5f, 1.5f);
        p.Discoveries = (WorldMapDiscoveryRate)Math.Max(0, Math.Min(2, Discoveries));
        // Враги: ватаги на дорогах и звериные логова (мест «логово» и групп
        // «ватага» меньше или больше; на большой карте — по площади).
        double enemies = Level(Enemies, 0.4f, 2f);
        double areaLinear = Math.Max(1.0, p.HexesAcross / 100.0);
        if (p.Groups != null)
            foreach (WorldMapGroupRule g in p.Groups)
                if (g != null && g.Role == WorldMapGroupRule.Band)
                    g.Count = Math.Max(Enemies <= Low ? 0 : 1, (int)Math.Round(g.Count * enemies * areaLinear));
        if (p.Landmarks != null)
            foreach (WorldMapLandmarkRule rule in p.Landmarks)
                if (rule != null && rule.Id == "lair")
                    rule.Count = Math.Max(Enemies <= Low ? 0 : 1, (int)Math.Round(rule.Count * enemies));
        p.Sanitize();
    }

    // Доля дней, в которые дорога может предложить встречу (враги «мало» —
    // через день в среднем; «обычно» и «много» — каждый день, как раньше).
    public double RoadEncounterShare => Enemies <= Low ? 0.5 : 1.0;

    public static readonly string[] LevelTitles = { "Мало", "Обычно", "Много" };
}

public sealed class CampaignOptionDefinition
{
    public string Id { get; }
    public string Title { get; }
    public string Summary { get; }

    public CampaignOptionDefinition(string id, string title, string summary)
    {
        Id = id;
        Title = title;
        Summary = summary;
    }
}

public static class CampaignStartOptions
{
    // Канон v1.59 §6.0: «Свободная игра» — единственный режим. ID хранится в
    // CampaignConfiguration.CrisisId (имя поля сохранено ради старых
    // сохранений) и не переиспользуется для другого содержания.
    public const string FreePlayId = "freeplay.home_region";
    public const string PlaceholderCommanderId = "commander.placeholder";
    // Начальное состояние новой партии — стоянка у дороги. Прежний ID
    // «start.base_home» (исходный Дом) не переиспользуется.
    public const string RoadCampStartId = "start.road_camp";

    public static readonly IReadOnlyList<CampaignOptionDefinition> Commanders = new[]
    {
        new CampaignOptionDefinition(
            PlaceholderCommanderId,
            "Командир",
            "Ведёт отряд. Может уйти в поход один или взять с собой до четырёх бойцов.")
    };

    // Размер карты свободной игры — клеток по ширине (просьба пользователя
    // 01.11.2026). Генератор берёт остальные настройки из своего ассета.
    public const int SmallMapHexesAcross = 50;
    public const int MediumMapHexesAcross = 100;
    public const int LargeMapHexesAcross = 140;
    public const int HugeMapHexesAcross = 180;
    public const int DefaultMapHexesAcross = MediumMapHexesAcross;

    public static readonly IReadOnlyList<MapSizeOption> MapSizes = new[]
    {
        new MapSizeOption(SmallMapHexesAcross, "Маленькая", "50 клеток по ширине: места ближе, дороги короче."),
        new MapSizeOption(MediumMapHexesAcross, "Средняя", "100 клеток по ширине: округа больше, путь между местами дольше."),
        new MapSizeOption(LargeMapHexesAcross, "Большая", "140 клеток по ширине: вдвое больше земли, больше деревень и мест; загрузка дольше."),
        new MapSizeOption(HugeMapHexesAcross, "Очень большая", "180 клеток по ширине: втрое больше земли; первая загрузка заметно дольше.")
    };

    public static MapSizeOption FindMapSize(int hexesAcross)
    {
        foreach (MapSizeOption option in MapSizes)
        {
            if (option.HexesAcross == hexesAcross)
                return option;
        }
        return null;
    }

    public static CampaignOptionDefinition Find(IReadOnlyList<CampaignOptionDefinition> options, string id)
    {
        foreach (CampaignOptionDefinition option in options)
        {
            if (option.Id == id)
                return option;
        }
        return null;
    }
}

public sealed class MapSizeOption
{
    public int HexesAcross { get; }
    public string Title { get; }
    public string Summary { get; }

    public MapSizeOption(int hexesAcross, string title, string summary)
    {
        HexesAcross = hexesAcross;
        Title = title;
        Summary = summary;
    }
}

// Выбор до старта. Validate — до создания GameState; ошибка не оставляет
// полусозданной кампании.
public sealed class CampaignSetup
{
    public string CommanderProfileId = CampaignStartOptions.PlaceholderCommanderId;
    public int? WorldSeed;
    // Клеток по ширине генерируемой карты (CampaignStartOptions.MapSizes).
    public int MapHexesAcross = CampaignStartOptions.DefaultMapHexesAcross;
    // Основные параметры карты (местность, рельеф, вода, лес, деревни, места, враги).
    public CampaignMapOptions MapOptions = new CampaignMapOptions();

    public bool Validate(out string reason)
    {
        reason = string.Empty;
        if (CampaignStartOptions.Find(CampaignStartOptions.Commanders, CommanderProfileId) == null)
        {
            reason = "неизвестный командир '" + CommanderProfileId + "'";
            return false;
        }
        if (CampaignStartOptions.FindMapSize(MapHexesAcross) == null)
        {
            reason = "неизвестный размер карты " + MapHexesAcross;
            return false;
        }
        return true;
    }

    // Создаёт новую кампанию и фиксирует её конфигурацию.
    // Канон v1.58 §9.9: generatedMap — карта, сгенерированная для этой
    // партии (свободная игра); она заменяет авторский мир и хранится в партии.
    public GameState CreateCampaign(
        IReadOnlyList<WorldMapLocationTemplateData> locationTemplates = null,
        WorldMapDefinitionData worldDefinition = null,
        GeneratedWorldMap generatedMap = null)
    {
        if (!Validate(out string reason))
            throw new InvalidOperationException("Нельзя начать кампанию: " + reason + ".");

        bool generated = generatedMap != null && generatedMap.IsValid;
        if (generated)
        {
            WorldSeed ??= generatedMap.Seed;
            worldDefinition = generatedMap.ToDefinitionData();
        }

        GameState state = new GameState();
        state.CreateNewGame(WorldSeed, WorldMapLocationTemplateData.ForMode(locationTemplates, CampaignStartOptions.FreePlayId), worldDefinition);
        if (generated)
        {
            state.GeneratedMap = generatedMap;
            GeneratedMapPlaces.AddTo(state);
        }

        state.Configuration = new CampaignConfiguration
        {
            CampaignId = Guid.NewGuid().ToString("N"),
            CrisisId = CampaignStartOptions.FreePlayId,
            CommanderProfileId = CommanderProfileId,
            StartingConditionId = CampaignStartOptions.RoadCampStartId,
            ContentVersion = CampaignConfiguration.CurrentContentVersion,
            WorldSeed = state.WorldSeed,
            MapOptions = (MapOptions ?? new CampaignMapOptions()).Clone()
        };

        // Бриф правок (19 пунктов) п. 1: места, видимые лишь глазом, прячутся
        // под туман до открытия своей клетки — до настройки режима, чтобы
        // места, о которых режим сообщает сам (слух, поручение), остались
        // известными.
        WorldMapFog.HidePlacesUnderFog(state);

        // ПР-12А: однократная настройка свободной игры (модуль зарегистрирован в CampaignContent).
        CampaignContent.InitializeNewCampaign(state);

        // Бриф правок (19 пунктов) п. 1: новая партия — туман с открытой
        // округой старта.
        WorldMapFog.EnsureInitialized(state);

        // 12Т: поселения партии — профили деревень генератора и явно
        // привязанных мест назначаются один раз и фиксируются в сохранении.
        SettlementService.SyncLocations(state);
        return state;
    }
}
