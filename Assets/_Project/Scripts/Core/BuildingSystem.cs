using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public enum BuildingStatus
{
    Locked,
    Available,
    Constructing,
    Completed
}

[Serializable]
public sealed class BuildingDefinition
{
    public string Id;
    public string DisplayName;
    public string Description;
    public string EffectText;
    public int GoldCost;
    public double ConstructionHours;
    public int DailyGoldIncome;
    public int DailyFoodIncome;
    public int DailyGoldUpkeep;

    public BuildingDefinition(
        string id,
        string displayName,
        string description,
        string effectText,
        int goldCost,
        double constructionHours,
        int dailyGoldIncome = 0,
        int dailyFoodIncome = 0,
        int dailyGoldUpkeep = 0)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        EffectText = effectText;
        GoldCost = goldCost;
        ConstructionHours = constructionHours;
        DailyGoldIncome = dailyGoldIncome;
        DailyFoodIncome = dailyFoodIncome;
        DailyGoldUpkeep = dailyGoldUpkeep;
    }
}

[Serializable]
public sealed class BuildingStateData
{
    public string BuildingId;
    public BuildingStatus Status;
    public double StartedAtGameHour;
    public double CompletesAtGameHour;
}

[Serializable]
public sealed class RecruitmentStateData
{
    public bool IsActive;
    public double StartedAtGameHour;
    public double CompletesAtGameHour;
    public int RecruitNumber;
}

public static class BuildingSystem
{
    public const string FieldsAndGranariesId = "fields_granaries";
    public const string MarketId = "market";
    public const string BarracksId = "barracks";
    public const string CityWallsId = "city_walls";
    public const string MineId = "mine";

    public const int BaseDailyGoldIncome = 3;
    public const int BaseDailyFoodIncome = 7;
    public const int RecruitGoldCost = 35;
    public const double RecruitHours = 8.0;
    public const int PrototypeMaxFighters = 6;

    private sealed class RuntimeState
    {
        public readonly Dictionary<string, BuildingStateData> Buildings =
            new Dictionary<string, BuildingStateData>();
        public readonly Queue<string> Notices = new Queue<string>();
        public RecruitmentStateData Recruitment = new RecruitmentStateData();
        public int NextRecruitNumber = 1;
    }

    private static readonly ConditionalWeakTable<GameState, RuntimeState> RuntimeStates =
        new ConditionalWeakTable<GameState, RuntimeState>();

    private static readonly List<BuildingDefinition> Definitions =
        new List<BuildingDefinition>
        {
            new BuildingDefinition(
                FieldsAndGranariesId,
                "Поля и амбары",
                "Продовольственная основа поселения.",
                "+10 пищи в сутки. Содержание: 1 золото в сутки.",
                60,
                12.0,
                dailyFoodIncome: 10,
                dailyGoldUpkeep: 1),
            new BuildingDefinition(
                MarketId,
                "Рынок",
                "Даёт поселению устойчивый денежный поток.",
                "+5 золота в сутки. Содержание: 1 золото в сутки.",
                80,
                16.0,
                dailyGoldIncome: 5,
                dailyGoldUpkeep: 1),
            new BuildingDefinition(
                BarracksId,
                "Казармы",
                "Открывают подготовку новых постоянных бойцов.",
                "Открывают найм бойцов. Содержание: 2 золота в сутки.",
                100,
                20.0,
                dailyGoldUpkeep: 2),
            new BuildingDefinition(
                CityWallsId,
                "Укрепления",
                "Заготовка оборонительного развития поселения до утверждения правил защиты дома.",
                "Содержание: 1 золото в сутки. Конкретный оборонительный эффект пока не утверждён.",
                140,
                28.0,
                dailyGoldUpkeep: 1),
            new BuildingDefinition(
                MineId,
                "Шахта",
                "Будущая рискованная экономика: добыча должна приносить не только золото, но и связанные проблемы.",
                "Заблокировано в первом слое построек.",
                0,
                0.0)
        };

    public static IReadOnlyList<BuildingDefinition> GetDefinitions()
    {
        return Definitions;
    }

    public static void Reset(GameState state)
    {
        if (state == null)
            return;

        RuntimeStates.Remove(state);
        GetRuntime(state);
    }

    public static void Synchronize(GameState state)
    {
        if (state == null)
            return;

        RuntimeState runtime = GetRuntime(state);
        double now = GetAbsoluteGameHour(state);

        foreach (BuildingStateData building in runtime.Buildings.Values)
        {
            if (building.Status != BuildingStatus.Constructing ||
                now + 0.0001 < building.CompletesAtGameHour)
            {
                continue;
            }

            building.Status = BuildingStatus.Completed;
            BuildingDefinition definition = FindDefinition(building.BuildingId);
            runtime.Notices.Enqueue(
                "Строительство завершено: «" +
                (definition != null ? definition.DisplayName : building.BuildingId) +
                "». Постройка начала действовать.");
        }

        if (runtime.Recruitment.IsActive &&
            now + 0.0001 >= runtime.Recruitment.CompletesAtGameHour)
        {
            CompleteRecruitment(state, runtime);
        }
    }

    public static BuildingStateData GetBuildingState(GameState state, string buildingId)
    {
        Synchronize(state);
        RuntimeState runtime = GetRuntime(state);
        BuildingStateData building;
        return runtime.Buildings.TryGetValue(buildingId, out building)
            ? building
            : null;
    }

    public static bool IsCompleted(GameState state, string buildingId)
    {
        BuildingStateData building = GetBuildingState(state, buildingId);
        return building != null && building.Status == BuildingStatus.Completed;
    }

    public static bool HasActiveConstruction(GameState state)
    {
        Synchronize(state);
        RuntimeState runtime = GetRuntime(state);
        foreach (BuildingStateData building in runtime.Buildings.Values)
        {
            if (building.Status == BuildingStatus.Constructing)
                return true;
        }
        return false;
    }

    public static bool TryStartConstruction(
        GameState state,
        string buildingId,
        out string resultMessage)
    {
        resultMessage = "Строительство сейчас недоступно.";
        if (state == null)
            return false;

        Synchronize(state);
        BuildingDefinition definition = FindDefinition(buildingId);
        BuildingStateData building = GetBuildingState(state, buildingId);

        if (definition == null || building == null ||
            building.Status == BuildingStatus.Locked ||
            building.Status == BuildingStatus.Completed ||
            building.Status == BuildingStatus.Constructing)
        {
            return false;
        }

        if (HasActiveConstruction(state))
        {
            resultMessage = "В поселении уже идёт строительство. Для прототипа одновременно доступна только одна стройка.";
            return false;
        }

        if (state.Gold < definition.GoldCost)
        {
            resultMessage =
                "Для постройки «" + definition.DisplayName + "» требуется " +
                definition.GoldCost + " золота.";
            return false;
        }

        state.Gold -= definition.GoldCost;
        double now = GetAbsoluteGameHour(state);
        building.Status = BuildingStatus.Constructing;
        building.StartedAtGameHour = now;
        building.CompletesAtGameHour = now + definition.ConstructionHours;

        resultMessage =
            "Начато строительство: «" + definition.DisplayName + "». Стоимость: " +
            definition.GoldCost + " золота. Время: " +
            ContinuousExpeditionCommands.FormatHours(definition.ConstructionHours) + ".";
        return true;
    }

    public static double GetConstructionProgress01(GameState state, string buildingId)
    {
        BuildingStateData building = GetBuildingState(state, buildingId);
        if (building == null)
            return 0.0;
        if (building.Status == BuildingStatus.Completed)
            return 1.0;
        if (building.Status != BuildingStatus.Constructing)
            return 0.0;

        double total = building.CompletesAtGameHour - building.StartedAtGameHour;
        if (total <= 0.0)
            return 1.0;
        double elapsed = GetAbsoluteGameHour(state) - building.StartedAtGameHour;
        return Math.Max(0.0, Math.Min(1.0, elapsed / total));
    }

    public static double GetConstructionHoursRemaining(GameState state, string buildingId)
    {
        BuildingStateData building = GetBuildingState(state, buildingId);
        if (building == null || building.Status != BuildingStatus.Constructing)
            return 0.0;
        return Math.Max(0.0, building.CompletesAtGameHour - GetAbsoluteGameHour(state));
    }

    public static int GetDailyGoldIncome(GameState state)
    {
        Synchronize(state);
        int total = BaseDailyGoldIncome;
        foreach (BuildingDefinition definition in Definitions)
        {
            if (definition.DailyGoldIncome > 0 && IsCompleted(state, definition.Id))
                total += definition.DailyGoldIncome;
        }
        return total;
    }

    public static int GetDailyFoodIncome(GameState state)
    {
        Synchronize(state);
        int total = BaseDailyFoodIncome;
        foreach (BuildingDefinition definition in Definitions)
        {
            if (definition.DailyFoodIncome > 0 && IsCompleted(state, definition.Id))
                total += definition.DailyFoodIncome;
        }
        return total;
    }

    public static int GetDailyGoldUpkeep(GameState state)
    {
        Synchronize(state);
        int total = 0;
        foreach (BuildingDefinition definition in Definitions)
        {
            if (definition.DailyGoldUpkeep > 0 && IsCompleted(state, definition.Id))
                total += definition.DailyGoldUpkeep;
        }
        return total;
    }

    public static int GetNetDailyGoldIncome(GameState state)
    {
        return GetDailyGoldIncome(state) - GetDailyGoldUpkeep(state);
    }

    public static bool CanRecruit(GameState state)
    {
        if (state == null)
            return false;

        Synchronize(state);
        RuntimeState runtime = GetRuntime(state);
        return IsCompleted(state, BarracksId) &&
               !runtime.Recruitment.IsActive &&
               state.Fighters != null &&
               state.Fighters.Count < PrototypeMaxFighters &&
               state.Gold >= RecruitGoldCost;
    }

    public static bool TryStartRecruitment(GameState state, out string resultMessage)
    {
        resultMessage = "Найм сейчас недоступен.";
        if (state == null)
            return false;

        Synchronize(state);
        RuntimeState runtime = GetRuntime(state);

        if (!IsCompleted(state, BarracksId))
        {
            resultMessage = "Сначала постройте Казармы.";
            return false;
        }

        if (runtime.Recruitment.IsActive)
        {
            resultMessage = "В Казармах уже готовят одного бойца.";
            return false;
        }

        if (state.Fighters == null || state.Fighters.Count >= PrototypeMaxFighters)
        {
            resultMessage =
                "Для текущего прототипа общий список ограничен " + PrototypeMaxFighters +
                " живыми бойцами. Это временный технический лимит, а не размер походного отряда.";
            return false;
        }

        if (state.Gold < RecruitGoldCost)
        {
            resultMessage = "Для найма бойца требуется " + RecruitGoldCost + " золота.";
            return false;
        }

        state.Gold -= RecruitGoldCost;
        double now = GetAbsoluteGameHour(state);
        runtime.Recruitment = new RecruitmentStateData
        {
            IsActive = true,
            StartedAtGameHour = now,
            CompletesAtGameHour = now + RecruitHours,
            RecruitNumber = runtime.NextRecruitNumber++
        };

        resultMessage =
            "Казармы начали подготовку нового бойца. Стоимость: " +
            RecruitGoldCost + " золота. Время: " +
            ContinuousExpeditionCommands.FormatHours(RecruitHours) + ".";
        return true;
    }

    public static bool IsRecruitmentActive(GameState state)
    {
        Synchronize(state);
        return state != null && GetRuntime(state).Recruitment.IsActive;
    }

    public static double GetRecruitmentProgress01(GameState state)
    {
        Synchronize(state);
        if (state == null)
            return 0.0;

        RecruitmentStateData recruitment = GetRuntime(state).Recruitment;
        if (!recruitment.IsActive)
            return 0.0;

        double total = recruitment.CompletesAtGameHour - recruitment.StartedAtGameHour;
        if (total <= 0.0)
            return 1.0;
        double elapsed = GetAbsoluteGameHour(state) - recruitment.StartedAtGameHour;
        return Math.Max(0.0, Math.Min(1.0, elapsed / total));
    }

    public static double GetRecruitmentHoursRemaining(GameState state)
    {
        Synchronize(state);
        if (state == null)
            return 0.0;

        RecruitmentStateData recruitment = GetRuntime(state).Recruitment;
        return recruitment.IsActive
            ? Math.Max(0.0, recruitment.CompletesAtGameHour - GetAbsoluteGameHour(state))
            : 0.0;
    }

    public static List<string> ConsumeNotices(GameState state)
    {
        Synchronize(state);
        List<string> result = new List<string>();
        if (state == null)
            return result;

        Queue<string> notices = GetRuntime(state).Notices;
        while (notices.Count > 0)
            result.Add(notices.Dequeue());
        return result;
    }

    private static RuntimeState GetRuntime(GameState state)
    {
        return RuntimeStates.GetValue(state, CreateRuntime);
    }

    private static RuntimeState CreateRuntime(GameState state)
    {
        RuntimeState runtime = new RuntimeState();
        foreach (BuildingDefinition definition in Definitions)
        {
            runtime.Buildings[definition.Id] = new BuildingStateData
            {
                BuildingId = definition.Id,
                Status = definition.Id == MineId
                    ? BuildingStatus.Locked
                    : BuildingStatus.Available
            };
        }
        return runtime;
    }

    private static void CompleteRecruitment(GameState state, RuntimeState runtime)
    {
        RecruitmentStateData recruitment = runtime.Recruitment;
        if (!recruitment.IsActive)
            return;

        if (state.Fighters == null)
            state.Fighters = new List<FighterData>();

        if (state.Fighters.Count >= PrototypeMaxFighters)
        {
            runtime.Notices.Enqueue(
                "Подготовка бойца завершилась, но технический лимит списка уже достигнут. Золото возвращено.");
            state.Gold += RecruitGoldCost;
            runtime.Recruitment = new RecruitmentStateData();
            return;
        }

        string fighterId = "recruit_" + recruitment.RecruitNumber;
        string fighterName = GetRecruitName(recruitment.RecruitNumber);
        state.Fighters.Add(
            new FighterData(
                fighterId,
                fighterName,
                "Ополченец",
                1,
                2));

        runtime.Notices.Enqueue(
            "Казармы подготовили нового бойца: " + fighterName +
            ". Он остаётся в поселении до назначения в походный отряд.");
        runtime.Recruitment = new RecruitmentStateData();
    }

    private static string GetRecruitName(int number)
    {
        string[] names =
        {
            "Оскар",
            "Ливена",
            "Рудольф",
            "Хельга",
            "Тео",
            "Ирма",
            "Вольф"
        };
        return names[(Math.Max(1, number) - 1) % names.Length] + " · ополченец";
    }

    private static BuildingDefinition FindDefinition(string buildingId)
    {
        foreach (BuildingDefinition definition in Definitions)
        {
            if (definition.Id == buildingId)
                return definition;
        }
        return null;
    }

    private static double GetAbsoluteGameHour(GameState state)
    {
        if (state == null)
            return 0.0;

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);
        int day = Math.Max(1, state.Day);
        return (day - 1) * 24.0 + clock.HourOfDay;
    }
}
