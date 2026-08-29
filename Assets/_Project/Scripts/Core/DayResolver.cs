using System;
using System.Collections.Generic;

// ============================================================
// РЕЗУЛЬТАТ РАСЧЁТА ДНЯ
// Сообщения отсюда попадут в Королевские донесения.
// ============================================================

public class DayResolutionResult
{
    public List<string> Messages = new List<string>();
}

// ============================================================
// РАСЧЁТ ЗАВЕРШЕНИЯ ДНЯ
// Один клик по кнопке = один пакет расчётов.
// ============================================================

public static class DayResolver
{
    public static DayResolutionResult ResolveDay(GameState state)
    {
        DayResolutionResult result = new DayResolutionResult();

        int finishedDay = state.Day;

        ResolveCityFood(state, result);
        ResolveExpedition(state, result);

        // Новый день начинается только после всех расчётов.
        state.Day++;

        result.Messages.Insert(
            0,
            "День " + finishedDay + " завершён.");

        return result;
    }

    // --------------------------------------------------------
    // ПИЩА ГОРОДА
    // Население × 1 пища.
    // --------------------------------------------------------

    private static void ResolveCityFood(
        GameState state,
        DayResolutionResult result)
    {
        int requiredFood = state.DailyFoodConsumption;
        int availableFood = state.Food;

        if (availableFood >= requiredFood)
        {
            state.Food -= requiredFood;

            result.Messages.Add(
                "Город израсходовал " +
                requiredFood +
                " пищи для " +
                state.Population +
                " жителей.");
        }
        else
        {
            int shortage = requiredFood - availableFood;

            state.Food = 0;

            result.Messages.Add(
                "Городу не хватило " +
                shortage +
                " пищи.");
        }

        // Последствия голода пока не добавляем:
        // их формула ещё не утверждена.
    }

    // --------------------------------------------------------
    // ПРОДВИЖЕНИЕ ЭКСПЕДИЦИИ
    // --------------------------------------------------------

    private static void ResolveExpedition(
        GameState state,
        DayResolutionResult result)
    {
        if (!state.HasActiveExpedition)
            return;

        ExpeditionData expedition =
            state.ActiveExpedition;

        CommanderData commander =
            state.FindCommander(expedition.CommanderId);

        LocationData location =
            state.FindLocation(expedition.LocationId);

        if (commander == null || location == null)
        {
            result.Messages.Add(
                "Ошибка данных активной экспедиции.");

            return;
        }

        // ====================================================
        // ПУТЬ К ЦЕЛИ
        // ====================================================

        if (expedition.Phase ==
            CommanderState.TravellingToLocation)
        {
            expedition.DaysRemaining =
                Math.Max(0, expedition.DaysRemaining - 1);

            if (expedition.DaysRemaining > 0)
            {
                result.Messages.Add(
                    "Экспедиция движется к локации «" +
                    location.Name +
                    "». Осталось дней пути: " +
                    expedition.DaysRemaining +
                    ".");
            }
            else
            {
                expedition.Phase =
                    CommanderState.AtLocation;

                commander.State =
                    CommanderState.AtLocation;

                result.Messages.Add(
                    commander.Name +
                    " прибыл в локацию «" +
                    location.Name +
                    "» и начал исследование.");
            }

            return;
        }

        // ====================================================
        // ВОЗВРАЩЕНИЕ В СТОЛИЦУ
        // ====================================================

        if (expedition.Phase ==
            CommanderState.ReturningToCastle)
        {
            expedition.DaysRemaining =
                Math.Max(0, expedition.DaysRemaining - 1);

            if (expedition.DaysRemaining > 0)
            {
                result.Messages.Add(
                    commander.Name +
                    " возвращается в столицу. " +
                    "Осталось дней пути: " +
                    expedition.DaysRemaining +
                    ".");
            }
            else
            {
                commander.State =
                    CommanderState.InCastle;

                expedition.IsActive = false;

                result.Messages.Add(
                    commander.Name +
                    " и " +
                    expedition.FighterIds.Count +
                    " воинов вернулись в столицу. " +
                    "Армия снова защищает город.");
            }
        }
    }
}