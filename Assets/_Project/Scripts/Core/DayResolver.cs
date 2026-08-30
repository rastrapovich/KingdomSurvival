using System;
using System.Collections.Generic;

public class DayResolutionResult
{
    public List<string> Messages = new List<string>();
    public List<ExpeditionIncidentOccurrence> NewExpeditionIncidents =
        new List<ExpeditionIncidentOccurrence>();
}

public static class DayResolver
{
    // Временные значения прототипа, не финальный баланс.
    private const int MoodLossPerShortageDay = 1;
    private const int PopulationLossPerStarvationDay = 1;
    private const int MoodOnlyShortageDays = 3;

    public static DayResolutionResult ResolveDay(GameState state)
    {
        DayResolutionResult result = new DayResolutionResult();
        int finishedDay = state.Day;

        ResolveGoldIncome(state, result);
        ResolveFoodIncome(state, result);
        ResolveCityFood(state, result);
        ResolveExpedition(state, result);

        // Фоновые происшествия возникают после обычного дневного
        // продвижения экспедиции и применяют последствия сразу.
        ExpeditionIncidentSystem.ResolveForDay(
            state,
            finishedDay,
            result);

        state.Day++;

        result.Messages.Insert(0, "День " + finishedDay + " завершён.");
        return result;
    }

    private static void ResolveGoldIncome(GameState state, DayResolutionResult result)
    {
        state.Gold += state.DailyGoldIncome;
        result.Messages.Add("Казна получила " + state.DailyGoldIncome + " золота.");
    }

    private static void ResolveFoodIncome(GameState state, DayResolutionResult result)
    {
        state.Food += state.DailyFoodIncome;
        result.Messages.Add("Город получил " + state.DailyFoodIncome + " пищи.");
    }

    private static void ResolveCityFood(GameState state, DayResolutionResult result)
    {
        int requiredFood = state.DailyFoodConsumption;
        int availableFood = state.Food;

        if (availableFood >= requiredFood)
        {
            state.Food -= requiredFood;

            result.Messages.Add(
                "Город израсходовал " + requiredFood +
                " пищи для " + state.Population + " жителей.");

            if (state.ConsecutiveFoodShortageDays > 0)
                result.Messages.Add("Нехватка пищи прекратилась.");

            state.ConsecutiveFoodShortageDays = 0;
            return;
        }

        int shortage = requiredFood - availableFood;
        state.Food = 0;
        state.ConsecutiveFoodShortageDays++;

        result.Messages.Add(
            "Городу не хватило " + shortage + " пищи. " +
            "День нехватки подряд: " + state.ConsecutiveFoodShortageDays + ".");

        if (state.ConsecutiveFoodShortageDays <= MoodOnlyShortageDays)
        {
            int previousMood = state.Mood;
            state.Mood = Math.Max(0, state.Mood - MoodLossPerShortageDay);
            int moodLost = previousMood - state.Mood;

            result.Messages.Add(
                "Из-за нехватки пищи настроение снизилось на " + moodLost + ".");
        }
        else
        {
            int previousPopulation = state.Population;
            state.Population = Math.Max(
                0,
                state.Population - PopulationLossPerStarvationDay);
            int populationLost = previousPopulation - state.Population;

            result.Messages.Add(
                "Голод затянулся: население уменьшилось на " + populationLost + ".");
        }
    }

    private static void ResolveExpedition(GameState state, DayResolutionResult result)
    {
        if (!state.HasActiveExpedition)
            return;

        ExpeditionData expedition = state.ActiveExpedition;
        CommanderData commander = state.FindCommander(expedition.CommanderId);
        LocationData location = state.FindLocation(expedition.LocationId);

        if (commander == null || location == null)
        {
            result.Messages.Add("Ошибка данных активной экспедиции.");
            return;
        }

        ResolveExpeditionSupply(state, expedition, result);

        if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            expedition.DaysRemaining = Math.Max(0, expedition.DaysRemaining - 1);

            if (expedition.DaysRemaining > 0)
            {
                result.Messages.Add(
                    "Экспедиция движется к локации «" + location.Name +
                    "». Осталось дней пути: " + expedition.DaysRemaining + ".");
            }
            else
            {
                expedition.Phase = CommanderState.AtLocation;
                commander.State = CommanderState.AtLocation;

                result.Messages.Add(
                    commander.Name + " прибыл в локацию «" +
                    location.Name + "» и начал исследование.");
            }

            return;
        }

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            expedition.DaysRemaining = Math.Max(0, expedition.DaysRemaining - 1);

            if (expedition.DaysRemaining > 0)
            {
                result.Messages.Add(
                    commander.Name + " возвращается в столицу. " +
                    "Осталось дней пути: " + expedition.DaysRemaining + ".");
            }
            else
            {
                commander.State = CommanderState.InCastle;
                expedition.IsActive = false;

                result.Messages.Add(
                    commander.Name + " и " + expedition.FighterIds.Count +
                    " воинов вернулись в столицу. Армия снова защищает город.");
            }
        }
    }

    private static void ResolveExpeditionSupply(
        GameState state,
        ExpeditionData expedition,
        DayResolutionResult result)
    {
        int requiredSupply = expedition.FighterIds.Count + 1;
        int availableSupply = state.ArmySupply;

        if (availableSupply >= requiredSupply)
        {
            state.ArmySupply -= requiredSupply;

            result.Messages.Add(
                "Экспедиция израсходовала " + requiredSupply +
                " снабжения. Осталось: " + state.ArmySupply + ".");

            return;
        }

        int shortage = requiredSupply - availableSupply;
        state.ArmySupply = 0;

        result.Messages.Add(
            "<color=#D57E72>Экспедиции не хватило " + shortage +
            " единиц снабжения. Израсходованы последние " +
            availableSupply +
            ". Штраф за голод экспедиции пока не применяется.</color>");
    }
}
