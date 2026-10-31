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

// Выбор до старта. Validate — до создания GameState; ошибка не оставляет
// полусозданной кампании.
public sealed class CampaignSetup
{
    public string CommanderProfileId = CampaignStartOptions.PlaceholderCommanderId;
    public int? WorldSeed;

    public bool Validate(out string reason)
    {
        reason = string.Empty;
        if (CampaignStartOptions.Find(CampaignStartOptions.Commanders, CommanderProfileId) == null)
        {
            reason = "неизвестный командир '" + CommanderProfileId + "'";
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
            WorldSeed = state.WorldSeed
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
