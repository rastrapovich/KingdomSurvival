using System;
using System.Collections.Generic;

// ПР-12А (канон v1.45 §6.0): общие модели экрана Дома и «Дел» и реестр
// содержания режима кампании. Ядро не знает ни главу «Дома на чужой воде»,
// ни свободную игру: каждый модуль регистрирует свой источник по ID режима
// из CampaignConfiguration.CrisisId, UI читает только этот вход.

// Объект Дома на экране: что видим → почему это известно → что можно
// сделать (PR07_HOME_SPEC §5). StateKey — стабильный ключ состояния для
// слоя образа Дома (класс USS и имя рисунка художника).
public sealed class HomeObjectView
{
    public string Id;
    public string Title;
    public string StateKey;
    // Короткая подпись слоя образа Дома: «мутная», «стоит», «цела».
    public string Short;
    public string State;
    public string Source;
    public string ActionLabel;
    public string ActionDialogueId;
}

public enum HomeCareAction
{
    None,
    StartYardDeck,
    OpenDialogue
}

// Забота: одна проблема — одна карточка (PR07_HOME_SPEC §7.1).
public sealed class HomeCareView
{
    public string Id;
    public string Title;
    public string Cause;
    public string Status;
    public string Detail;
    public int Priority;
    public bool Urgent;
    public string ActionLabel;
    public HomeCareAction Action;
    public string DialogueId;
    public bool ActionEnabled;
}

public enum JournalGoalCategory
{
    Main,
    Optional
}

public enum JournalGoalState
{
    Hidden,
    Active,
    Completed,
    Failed
}

public sealed class JournalGoalViewData
{
    public string Id;
    public string Title;
    public string Description;
    public string CurrentStep;
    public string RevisionId;
    public JournalGoalCategory Category;
    public JournalGoalState State;
}

public sealed class CampaignContentProvider
{
    public Func<GameState, IReadOnlyList<JournalGoalViewData>> Goals;
    public Func<GameState, IReadOnlyList<HomeObjectView>> HomeObjects;
    public Func<GameState, IReadOnlyList<HomeCareView>> HomeCares;
    // Однократная настройка только что созданной кампании этого режима.
    public Action<GameState> OnNewCampaign;
}

public static class CampaignContent
{
    private static readonly Dictionary<string, CampaignContentProvider> Providers =
        new Dictionary<string, CampaignContentProvider>();

    public static void Register(string modeId, CampaignContentProvider provider)
    {
        if (!string.IsNullOrEmpty(modeId) && provider != null)
            Providers[modeId] = provider;
    }

    public static bool IsRegistered(string modeId)
    {
        return !string.IsNullOrEmpty(modeId) && Providers.ContainsKey(modeId);
    }

    // Режим партии. Старые сохранения без конфигурации — сюжетная кампания.
    public static string ModeId(GameState state)
    {
        CampaignConfiguration configuration = state?.Configuration;
        return configuration != null && !string.IsNullOrEmpty(configuration.CrisisId)
            ? configuration.CrisisId
            : CampaignStartOptions.HomeOnForeignWaterCrisisId;
    }

    public static bool IsFreePlay(GameState state)
    {
        return state != null && ModeId(state) == CampaignStartOptions.FreePlayId;
    }

    public static IReadOnlyList<JournalGoalViewData> BuildGoals(GameState state)
    {
        CampaignContentProvider provider = Find(state);
        return provider?.Goals != null ? provider.Goals(state) : new List<JournalGoalViewData>();
    }

    public static IReadOnlyList<HomeObjectView> DescribeHomeObjects(GameState state)
    {
        CampaignContentProvider provider = Find(state);
        return provider?.HomeObjects != null ? provider.HomeObjects(state) : new List<HomeObjectView>();
    }

    public static IReadOnlyList<HomeCareView> DescribeHomeCares(GameState state)
    {
        CampaignContentProvider provider = Find(state);
        return provider?.HomeCares != null ? provider.HomeCares(state) : HomeCares.DescribeCommon(state);
    }

    public static void InitializeNewCampaign(GameState state)
    {
        Find(state)?.OnNewCampaign?.Invoke(state);
    }

    private static CampaignContentProvider Find(GameState state)
    {
        if (state == null)
            return null;
        return Providers.TryGetValue(ModeId(state), out CampaignContentProvider provider) ? provider : null;
    }
}

// Общие заботы любого Дома: нехватка еды и уход за ранеными. Модуль режима
// добавляет к ним свои (работы, функции конкретных людей).
public static class HomeCares
{
    // Порядок забот (PR07_HOME_SPEC §7.1): сюжетное решение → нехватка/
    // остановленная необходимая функция → доступные работы → идущие работы.
    public const int PriorityStory = 0;
    public const int PriorityNeed = 1;
    public const int PriorityOptional = 2;
    public const int PriorityRunning = 3;

    public static List<HomeCareView> DescribeCommon(GameState gameState)
    {
        List<HomeCareView> cares = new List<HomeCareView>();
        if (gameState == null)
            return cares;

        HomeFoodForecast food = HomeOverview.ForecastFood(gameState);
        if (food.HasCurrentShortage || food.Outlook == HomeFoodOutlook.ShortageAtNextMidnight)
        {
            cares.Add(new HomeCareView
            {
                Id = "home.care.food",
                Title = "Нехватка еды",
                Cause = "Дома едят " + food.DailyConsumption + " в сутки, поступает " + food.DailyIncome + ".",
                Status = HomeOverview.DescribeFood(food),
                Priority = PriorityNeed,
                Urgent = true
            });
        }

        AddPatients(gameState, cares);
        return cares;
    }

    private static void AddPatients(GameState gameState, List<HomeCareView> cares)
    {
        List<ResidentState> patients = new List<ResidentState>();
        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            if (resident.NeedsCare && HomePeopleService.IsHomePresent(gameState, resident))
                patients.Add(resident);
        }

        if (patients.Count == 0)
            return;

        HomeFunctionReport care = HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.CareId);
        List<string> lines = new List<string>();
        foreach (ResidentState patient in patients)
        {
            string line = patient.DisplayName;
            if (patient.HasCombatState)
                line += " — " + patient.CurrentHitPoints + "/" + patient.MaxHitPoints + " HP";
            if (care.Rate > 0.0)
            {
                double hours = (1.0 - patient.RecoveryProgress) * HomeLife.FullCareCycleHours / care.Rate;
                line += ", около " + Math.Max(1, (int)Math.Ceiling(hours)) + " ч.";
            }
            else
            {
                line += ", уход остановлен";
            }
            lines.Add(line);
        }

        cares.Add(new HomeCareView
        {
            Id = "home.care.patients",
            Title = "Уход за ранеными",
            Cause = patients.Count == 1 ? "Один человек ранен." : "Раненых: " + patients.Count + ".",
            Status = StatusLine(care),
            Detail = string.Join("\n", lines),
            Priority = care.Status == HomeFunctionStatus.Stopped ? PriorityNeed : PriorityRunning,
            Urgent = care.Status == HomeFunctionStatus.Stopped
        });
    }

    public static string StatusLine(HomeFunctionReport report)
    {
        switch (report.Status)
        {
            case HomeFunctionStatus.Working:
                return "Работает — " + report.ExecutorName + ".";
            case HomeFunctionStatus.Limited:
                return "Медленнее: " + report.Reason + ".";
            default:
                return "Остановлено: " + report.Reason + ".";
        }
    }
}
