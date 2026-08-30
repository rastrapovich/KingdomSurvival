using System;
using System.Collections.Generic;

public class DayResolutionResult
{
    public List<string> Messages = new List<string>();
    public List<ExpeditionIncidentOccurrence> NewExpeditionIncidents =
        new List<ExpeditionIncidentOccurrence>();

    // Отмечает события, которые выходят за рамки обычного дохода,
    // потребления и рутинного движения экспедиции.
    public bool HadNotableOccurrence;

    // Во второй подряд день нехватки снабжения отряд занимается
    // аварийным возвращением. В этот день не создаём новые походные
    // происшествия и значимые решения поверх уже понятного кризиса.
    public bool SkipExpeditionOccurrencesForDay;
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

        // Кризис столицы рассчитывается до движения экспедиции.
        // Если армия возвращается в столицу именно в этот день,
        // во время кризиса она ещё считается находящейся вне города.
        CapitalCrisisSystem.ResolveForDay(
            state,
            finishedDay,
            result);

        ResolveExpedition(state, result);

        // Пока экспедиция ждёт значимого приказа, она не продвигается
        // и не получает фоновых происшествий. Мир и расход снабжения идут дальше.
        if (!state.HasPendingExpeditionDecision &&
            !result.SkipExpeditionOccurrencesForDay)
        {
            ExpeditionIncidentSystem.ResolveForDay(
                state,
                finishedDay,
                result);

            if (result.NewExpeditionIncidents.Count > 0)
                result.HadNotableOccurrence = true;
        }

        bool hadPendingDecisionBefore = state.HasPendingExpeditionDecision;

        // После обычного продвижения и фоновых происшествий может возникнуть
        // не более одного значимого события, требующего решения короля.
        if (!result.SkipExpeditionOccurrencesForDay)
        {
            ExpeditionDecisionSystem.ResolveForDay(
                state,
                finishedDay,
                result);
        }

        if (!hadPendingDecisionBefore && state.HasPendingExpeditionDecision)
            result.HadNotableOccurrence = true;

        if (!result.HadNotableOccurrence)
            result.Messages.Add("Ничего особенного не произошло.");

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
            {
                result.Messages.Add("Нехватка пищи прекратилась.");
                result.HadNotableOccurrence = true;
            }

            state.ConsecutiveFoodShortageDays = 0;
            return;
        }

        int shortage = requiredFood - availableFood;
        state.Food = 0;
        state.ConsecutiveFoodShortageDays++;
        result.HadNotableOccurrence = true;

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
            result.HadNotableOccurrence = true;
            return;
        }

        ResolveExpeditionSupply(state, expedition, result);

        if (state.HasPendingExpeditionDecision)
        {
            result.Messages.Add(
                "Экспедиция ждёт приказа по событию «" +
                expedition.PendingDecision.Title +
                "» и сегодня не продвигается.");
            result.HadNotableOccurrence = true;
            return;
        }

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
                result.HadNotableOccurrence = true;

                string arrivalAction = location.ExplorationDays > 0
                    ? " Можно начать исследование или приказать возвращаться."
                    : " Исследование этой локации пока не реализовано.";

                result.Messages.Add(
                    commander.Name + " прибыл в локацию «" +
                    location.Name + "»." + arrivalAction);
            }

            return;
        }

        if (expedition.Phase == CommanderState.AtLocation)
        {
            ResolveLocationResearch(state, expedition, location, result);
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
                int fighterCount = expedition.FighterIds.Count;
                string deliveredResources = state.CompleteExpeditionReturn();
                result.HadNotableOccurrence = true;

                result.Messages.Add(
                    commander.Name + " и " + fighterCount +
                    " воинов вернулись в столицу. Армия снова защищает город. " +
                    deliveredResources);
            }
        }
    }

    private static void ResolveLocationResearch(
        GameState state,
        ExpeditionData expedition,
        LocationData location,
        DayResolutionResult result)
    {
        if (!expedition.IsExplorationInProgress)
            return;

        expedition.ExplorationDaysRemaining =
            Math.Max(0, expedition.ExplorationDaysRemaining - 1);

        if (expedition.ExplorationDaysRemaining > 0)
        {
            int completedDays =
                Math.Max(0, location.ExplorationDays - expedition.ExplorationDaysRemaining);

            result.Messages.Add(
                "Исследование локации «" + location.Name + "» — " +
                completedDays + "/" + location.ExplorationDays +
                ". Награда ещё не получена.");
            return;
        }

        expedition.IsExplorationInProgress = false;
        location.IsExplored = true;

        state.ArmyGold += location.RewardArmyGold;
        state.ArmySupply += location.RewardArmySupply;
        result.HadNotableOccurrence = true;

        List<string> rewardParts = new List<string>();

        if (location.RewardArmyGold > 0)
            rewardParts.Add("золото +" + location.RewardArmyGold);

        if (location.RewardArmySupply > 0)
            rewardParts.Add("снабжение +" + location.RewardArmySupply);

        string rewardText = rewardParts.Count > 0
            ? string.Join(", ", rewardParts)
            : "добычи нет";

        result.Messages.Add(
            "<color=#84B889>Локация «" + location.Name +
            "» исследована. Добыча отряда: " + rewardText +
            ". Ресурсы находятся у отряда и попадут в столицу только после возвращения.</color>");
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
            state.ConsecutiveExpeditionSupplyShortageDays = 0;

            result.Messages.Add(
                "Экспедиция израсходовала " + requiredSupply +
                " снабжения. Осталось: " + state.ArmySupply + ".");

            return;
        }

        int shortage = requiredSupply - availableSupply;
        state.ArmySupply = 0;
        state.ConsecutiveExpeditionSupplyShortageDays++;
        result.HadNotableOccurrence = true;

        if (state.ConsecutiveExpeditionSupplyShortageDays == 1)
        {
            result.Messages.Add(
                "<color=#D57E72>Армии не хватает снабжения. Не хватило " +
                shortage + " единиц; израсходованы последние " +
                availableSupply +
                ". Это первый голодный день подряд: отряд ещё выполняет текущую задачу, " +
                "но следующий такой день сорвёт поход.</color>");
            return;
        }

        result.SkipExpeditionOccurrencesForDay = true;

        string forcedReturnMessage;
        bool returnStarted =
            state.ForceReturnFromSupplyFailure(out forcedReturnMessage);

        if (!returnStarted)
            forcedReturnMessage =
                "Не удалось автоматически определить путь домой; проверьте данные экспедиции.";

        result.Messages.Add(
            "<color=#D57E72>Армии снова не хватило снабжения. Не хватило " +
            shortage + " единиц; израсходованы последние " +
            availableSupply + ". Второй голодный день подряд. " +
            forcedReturnMessage + "</color>");
    }
}
