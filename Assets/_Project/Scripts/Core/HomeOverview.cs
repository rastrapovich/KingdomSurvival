using System;
using System.Collections.Generic;

public enum HomeFoodOutlook
{
    // Сейчас никто дома не ест — «Сейчас расхода нет».
    NoConsumption,
    // Поступления покрывают суточный расход, ближайшая полночь обеспечена.
    IncomeCovers,
    // Обеспечено N следующих полночей, затем нехватка.
    DaysCovered,
    // Уже ближайшая полночь даст нехватку.
    ShortageAtNextMidnight
}

public readonly struct HomeFoodForecast
{
    public readonly HomeFoodOutlook Outlook;
    public readonly int CoveredMidnights;
    public readonly int ShortageAtFirstMidnight;
    public readonly int DailyIncome;
    public readonly int DailyConsumption;
    public readonly bool HasCurrentShortage;

    public HomeFoodForecast(HomeFoodOutlook outlook, int coveredMidnights, int shortage, int income, int consumption, bool currentShortage)
    {
        Outlook = outlook;
        CoveredMidnights = coveredMidnights;
        ShortageAtFirstMidnight = shortage;
        DailyIncome = income;
        DailyConsumption = consumption;
        HasCurrentShortage = currentShortage;
    }
}

// ПР-07А-1 (PR07_HOME_SPEC §6): сводка Дома — чистые расчёты без изменения
// GameState. Запасы — прогноз до первой полуночи с нехваткой тем же
// порядком, что у симуляции (поступление, затем расход); защита — кто из
// боеспособных людей физически дома.
public static class HomeOverview
{
    // Горизонт прогноза: дальше него «хватит надолго» не уточняется.
    public const int ForecastHorizonDays = 365;

    public static HomeFoodForecast ForecastFood(GameState state)
    {
        int income = BuildingSystem.GetDailyFoodIncome(state);
        int consumption = state.DailyFoodConsumption;
        return ForecastFood(state.Food, income, consumption, state.ConsecutiveFoodShortageDays > 0);
    }

    public static HomeFoodForecast ForecastFood(int food, int income, int consumption, bool currentShortage)
    {
        if (consumption <= 0)
            return new HomeFoodForecast(HomeFoodOutlook.NoConsumption, 0, 0, income, consumption, currentShortage);

        int stock = Math.Max(0, food);
        if (stock + income < consumption)
        {
            return new HomeFoodForecast(HomeFoodOutlook.ShortageAtNextMidnight, 0,
                consumption - (stock + income), income, consumption, currentShortage);
        }

        if (income >= consumption)
            return new HomeFoodForecast(HomeFoodOutlook.IncomeCovers, 0, 0, income, consumption, currentShortage);

        // Постоянный суточный дефицит: полночь d обеспечена, пока
        // stock + income - (d-1)·deficit ≥ consumption.
        int covered = 0;
        for (int day = 0; day < ForecastHorizonDays; day++)
        {
            stock += income;
            if (stock < consumption)
                break;
            stock -= consumption;
            covered++;
        }

        return new HomeFoodForecast(HomeFoodOutlook.DaysCovered, covered, 0, income, consumption, currentShortage);
    }

    public static string DescribeFood(GameState state)
    {
        return DescribeFood(ForecastFood(state));
    }

    public static string DescribeFood(HomeFoodForecast forecast)
    {
        string text;
        switch (forecast.Outlook)
        {
            case HomeFoodOutlook.NoConsumption:
                text = "Сейчас расхода нет.";
                break;
            case HomeFoodOutlook.IncomeCovers:
                text = "Поступления покрывают расход.";
                break;
            case HomeFoodOutlook.ShortageAtNextMidnight:
                text = "До ближайшей ночи запасов не хватит: недостанет " + forecast.ShortageAtFirstMidnight + ".";
                break;
            default:
                text = forecast.CoveredMidnights >= ForecastHorizonDays
                    ? "Хватит надолго при нынешних условиях."
                    : "Хватит на " + forecast.CoveredMidnights + " " + DayWord(forecast.CoveredMidnights) +
                      " при нынешних условиях.";
                break;
        }

        if (forecast.HasCurrentShortage)
            text = "Есть нехватка еды. " + text;
        return text;
    }

    // ------------------------------------------------------------------
    // Защита: кто из боеспособных людей физически дома.
    // ------------------------------------------------------------------

    public static List<ResidentState> GetHomeDefenders(GameState state)
    {
        List<ResidentState> defenders = new List<ResidentState>();
        foreach (ResidentState resident in HomePeopleService.All(state))
        {
            if (!resident.IsHomeMember ||
                (resident.TravelRole != ResidentTravelRole.Commander && resident.TravelRole != ResidentTravelRole.Combatant) ||
                !HomePeopleService.IsHomePresent(state, resident) ||
                resident.Injury == ResidentInjury.Recovering ||
                (resident.HasCombatState && resident.CurrentHitPoints <= 0))
            {
                continue;
            }
            defenders.Add(resident);
        }
        return defenders;
    }

    public static string DescribeDefense(GameState state)
    {
        List<ResidentState> defenders = GetHomeDefenders(state);
        if (defenders.Count == 0)
            return "Некому держать защиту — никто из бойцов не остался дома.";

        List<string> names = new List<string>();
        foreach (ResidentState defender in defenders)
            names.Add(defender.DisplayName);

        if (defenders.Count <= 2)
            return "Мало защитников: " + string.Join(", ", names) + ".";
        return "Есть кому держать защиту: " + defenders.Count + " — " + string.Join(", ", names) + ".";
    }

    // ------------------------------------------------------------------
    // ПР-07А-2 (§8.5): прогноз «После выхода» по подготовленному составу.
    // Условно отсутствуют все уходящие, включая Командира. Ничего не меняет.
    // Сначала — то, что изменится; затем — что останется как есть.
    // ------------------------------------------------------------------

    public static List<string> GetLeavingIds(GameState state)
    {
        List<string> leaving = new List<string>();
        CommanderData commander = state.GetSelectedCommander();
        if (commander != null)
            leaving.Add(commander.Id);
        leaving.AddRange(ExpeditionPreparation.GetFighterIds(state));
        string retinue = ExpeditionPreparation.GetRetinueId(state);
        if (!string.IsNullOrEmpty(retinue))
            leaving.Add(retinue);
        return leaving;
    }

    public static List<string> DescribeDeparture(GameState state)
    {
        List<string> changed = new List<string>();
        List<string> same = new List<string>();
        List<string> leaving = GetLeavingIds(state);

        int homeNow = HomePeopleService.CountHomePresent(state);
        int leavingFromHome = 0;
        foreach (string id in leaving)
        {
            ResidentState resident = HomePeopleService.Find(state, id);
            if (resident != null && HomePeopleService.IsHomePresent(state, resident))
                leavingFromHome++;
        }
        int homeAfter = homeNow - leavingFromHome;
        changed.Add("Уйдут " + leaving.Count + ", дома останется " + homeAfter + ".");

        List<string> defenders = new List<string>();
        foreach (ResidentState defender in GetHomeDefenders(state))
        {
            if (!leaving.Contains(defender.PersonId))
                defenders.Add(defender.DisplayName);
        }
        changed.Add(defenders.Count == 0
            ? "Защищать Дом будет некому."
            : defenders.Count <= 2
                ? "Защищать Дом останутся: " + string.Join(", ", defenders) + " — мало."
                : "Защищать Дом останутся " + defenders.Count + ": " + string.Join(", ", defenders) + ".");

        DescribeFunction(state, HomeFunctionResolver.MaintenanceId, "Ремонт", leaving, changed, same);
        DescribeFunction(state, HomeFunctionResolver.CareId, "Уход за ранеными", leaving, changed, same);

        int consumptionAfter = Math.Max(0, state.DailyFoodConsumption - leavingFromHome);
        HomeFoodForecast food = ForecastFood(state.Food, BuildingSystem.GetDailyFoodIncome(state), consumptionAfter,
            state.ConsecutiveFoodShortageDays > 0);
        same.Add("Запасы Дома после выхода: расход " + consumptionAfter + " в сутки. " + DescribeFood(food));

        int supplyPerDay = leaving.Count;
        same.Add("Припасы похода: " + supplyPerDay + " в сутки, есть " + state.ArmySupply +
                 (supplyPerDay > 0 ? " — на " + state.ArmySupply / supplyPerDay + " сут." : "."));

        changed.AddRange(same);
        return changed;
    }

    private static void DescribeFunction(GameState state, string functionId, string title, List<string> leaving,
        List<string> changed, List<string> same)
    {
        HomeFunctionReport now = HomeFunctionResolver.Resolve(state, functionId);
        HomeFunctionReport after = HomeFunctionResolver.Forecast(state, functionId, leaving);

        if (now.Status == after.Status && now.ExecutorId == after.ExecutorId)
        {
            same.Add(title + ": без изменений" +
                     (string.IsNullOrEmpty(after.ExecutorName) ? "." : " — " + after.ExecutorName + "."));
            return;
        }

        string text;
        switch (after.Status)
        {
            case HomeFunctionStatus.Working:
                text = title + " продолжит " + after.ExecutorName + ".";
                break;
            case HomeFunctionStatus.Limited:
                string why = after.Reason ?? string.Empty;
                int cut = why.IndexOf(" — продолжает ", StringComparison.Ordinal);
                if (cut > 0)
                    why = why.Substring(0, cut);
                text = (why.Length > 0 ? char.ToUpperInvariant(why[0]) + why.Substring(1) + ". " : string.Empty) +
                       title + " продолжит " + after.ExecutorName + " — медленнее.";
                break;
            default:
                text = title + " остановится: " + after.Reason + ".";
                break;
        }
        changed.Add(text);
    }

    private static string DayWord(int count)
    {
        int lastTwo = count % 100;
        if (lastTwo >= 11 && lastTwo <= 14)
            return "полных дней";
        switch (count % 10)
        {
            case 1: return "полный день";
            case 2:
            case 3:
            case 4: return "полных дня";
            default: return "полных дней";
        }
    }
}
