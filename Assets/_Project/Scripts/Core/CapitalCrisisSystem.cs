using System;

public static class CapitalCrisisSystem
{
    private static readonly Random Random = new Random();
    private static int nextOccurrenceId = 1;

    // Временная вероятность для тестового прототипа, не финальный баланс.
    private const double CrisisChancePerDay = 0.25;

    // Временные последствия первого кризиса прототипа.
    private const int MoodLossWithArmyHome = 1;
    private const int FoodLossWithPartialGarrison = 3;
    private const int MoodLossWithPartialGarrison = 2;
    private const int FoodLossWithArmyAway = 6;
    private const int MoodLossWithArmyAway = 3;

    public static void ResolveForDay(
        GameState state,
        int finishedDay,
        DayResolutionResult result)
    {
        if (Random.NextDouble() >= CrisisChancePerDay)
            return;

        int garrisonPower = state.GarrisonDefensePower;
        int totalArmyPower = state.TotalArmyDefensePower;
        bool garrisonEmpty = garrisonPower <= 0;
        bool garrisonPartial = garrisonPower > 0 && garrisonPower < totalArmyPower;

        string description;
        string consequenceText;

        if (garrisonEmpty)
        {
            int actualFoodLoss = Math.Min(state.Food, FoodLossWithArmyAway);
            int previousMood = state.Mood;

            state.Food -= actualFoodLoss;
            state.Mood = Math.Max(0, state.Mood - MoodLossWithArmyAway);

            int actualMoodLoss = previousMood - state.Mood;

            description =
                "Стража попыталась остановить толпу самостоятельно. " +
                "Через некоторое время она тоже решила, что неплохо бы " +
                "что-нибудь взять из амбара. Армия всё ещё далеко от столицы.";

            consequenceText =
                FormatFoodLoss(actualFoodLoss) + " " +
                FormatMoodLoss(actualMoodLoss) +
                " Гарнизон пуст.";
        }
        else if (garrisonPartial)
        {
            int actualFoodLoss =
                Math.Min(state.Food, FoodLossWithPartialGarrison);
            int previousMood = state.Mood;

            state.Food -= actualFoodLoss;
            state.Mood =
                Math.Max(0, state.Mood - MoodLossWithPartialGarrison);

            int actualMoodLoss = previousMood - state.Mood;

            description =
                "Оставшийся гарнизон удержал толпу от полного разграбления " +
                "амбара, но людей не хватило, чтобы быстро восстановить порядок. " +
                "Часть единственной армии всё ещё находится в экспедиции.";

            consequenceText =
                FormatFoodLoss(actualFoodLoss) + " " +
                FormatMoodLoss(actualMoodLoss) +
                " Сила гарнизона: " + garrisonPower +
                "/" + totalArmyPower + ".";
        }
        else
        {
            int previousMood = state.Mood;
            state.Mood = Math.Max(0, state.Mood - MoodLossWithArmyHome);
            int actualMoodLoss = previousMood - state.Mood;

            description =
                "Толпа попыталась прорваться к амбару, но появление " +
                "вооружённых солдат быстро вернуло всем уважение к очереди. " +
                "Армия находится в столице.";

            consequenceText =
                FormatMoodLoss(actualMoodLoss) +
                " Армия подавила беспорядки до серьёзного ущерба.";
        }

        ExpeditionIncidentOccurrence occurrence =
            new ExpeditionIncidentOccurrence
            {
                // Отрицательные ID не пересекаются с ID походных происшествий.
                // DTO временно общий для уведомлений экспедиции и столицы.
                Id = -nextOccurrenceId++,
                Day = finishedDay,
                Title = "Беспорядки у городского амбара",
                Description = description,
                ConsequenceText = consequenceText,
                Tone = ExpeditionIncidentTone.Negative
            };

        result.NewExpeditionIncidents.Add(occurrence);
        result.HadNotableOccurrence = true;

        result.Messages.Add(
            "Столица: " + occurrence.Title + " — " +
            occurrence.ConsequenceText);
    }

    private static string FormatFoodLoss(int actualLoss)
    {
        if (actualLoss > 0)
            return "Пища -" + actualLoss + ".";

        return "Пища не уменьшилась: городской запас уже пуст.";
    }

    private static string FormatMoodLoss(int actualLoss)
    {
        if (actualLoss > 0)
            return "Настроение -" + actualLoss + ".";

        return "Настроение не уменьшилось: оно уже на минимуме.";
    }
}
