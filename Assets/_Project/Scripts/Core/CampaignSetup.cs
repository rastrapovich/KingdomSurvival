using System;
using System.Collections.Generic;

// ПР-05: конфигурация кампании. Выбор кризиса, командира и начального
// состояния делается до старта (CampaignSetup), проверяется и фиксируется
// в GameState.Configuration — она сохраняется вместе с кампанией, и загрузка
// восстанавливает ту же композицию, а не собирает мир заново.
//
// Сейчас в каждом списке одна запись; каталоги и мастер выбора — ПР-12.
// Тексты описаний — рабочие, не канон: личность героя не утверждена, и
// текст её не придумывает; завязка описана без разгадки.
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
    public const string HomeOnForeignWaterCrisisId = "crisis.home_on_foreign_water";
    public const string PlaceholderCommanderId = "commander.placeholder";
    public const string BaseHomeStartId = "start.base_home";

    // Без разгадки: только исходная беда и тон (план ПР-05, §4).
    public static readonly IReadOnlyList<CampaignOptionDefinition> Crises = new[]
    {
        new CampaignOptionDefinition(
            HomeOnForeignWaterCrisisId,
            "Дом на чужой воде",
            "Весенний паводок бьёт по плотине, на которой держатся мельница и весь уклад Дома. " +
            "Её придётся чинить — и решить, как именно.")
    };

    public static readonly IReadOnlyList<CampaignOptionDefinition> Commanders = new[]
    {
        new CampaignOptionDefinition(
            PlaceholderCommanderId,
            "Командир",
            "Тот, на ком теперь решения Дома. Может уйти в поход один или взять с собой до четырёх бойцов.")
    };

    public static readonly IReadOnlyList<CampaignOptionDefinition> StartingConditions = new[]
    {
        new CampaignOptionDefinition(
            BaseHomeStartId,
            "Обычная жизнь",
            "Мельница работает, люди заняты своим делом, запасов хватает. Всё, что случится дальше, начнётся с этой нормы.")
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
    public const int BaseHomeDailyFoodIncome = 24;

    public string CrisisId = CampaignStartOptions.HomeOnForeignWaterCrisisId;
    public string CommanderProfileId = CampaignStartOptions.PlaceholderCommanderId;
    public string StartingConditionId = CampaignStartOptions.BaseHomeStartId;
    public int? WorldSeed;

    public bool Validate(out string reason)
    {
        reason = string.Empty;
        if (CampaignStartOptions.Find(CampaignStartOptions.Crises, CrisisId) == null)
        {
            reason = "неизвестный кризис '" + CrisisId + "'";
            return false;
        }
        if (CampaignStartOptions.Find(CampaignStartOptions.Commanders, CommanderProfileId) == null)
        {
            reason = "неизвестный командир '" + CommanderProfileId + "'";
            return false;
        }
        if (CampaignStartOptions.Find(CampaignStartOptions.StartingConditions, StartingConditionId) == null)
        {
            reason = "неизвестное начальное состояние '" + StartingConditionId + "'";
            return false;
        }
        return true;
    }

    // Создаёт новую кампанию и фиксирует её конфигурацию.
    public GameState CreateCampaign(
        IReadOnlyList<WorldMapLocationTemplateData> locationTemplates = null,
        WorldMapDefinitionData worldDefinition = null)
    {
        if (!Validate(out string reason))
            throw new InvalidOperationException("Нельзя начать кампанию: " + reason + ".");

        GameState state = new GameState();
        state.CreateNewGame(WorldSeed, locationTemplates, worldDefinition);

        // ПР-06А: «Обычная жизнь» — Дом кормит себя. Приток 24 при 24
        // жителях — фиксированная настройка пресета, не подгонка под
        // текущее население: новые люди действительно увеличивают расход.
        if (StartingConditionId == CampaignStartOptions.BaseHomeStartId)
            state.BaseDailyFoodIncome = BaseHomeDailyFoodIncome;
        state.Configuration = new CampaignConfiguration
        {
            CampaignId = Guid.NewGuid().ToString("N"),
            CrisisId = CrisisId,
            CommanderProfileId = CommanderProfileId,
            StartingConditionId = StartingConditionId,
            ContentVersion = CampaignConfiguration.CurrentContentVersion,
            WorldSeed = state.WorldSeed
        };
        return state;
    }
}
