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
