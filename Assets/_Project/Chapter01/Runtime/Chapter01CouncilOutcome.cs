using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    public enum Chapter01CouncilOutcome
    {
        None,
        OldOrderRestored,
        NewOrderCreated,
        WaterKeptForHome
    }

    // P13: узкий resolver результата первого Совета. Он не хранит вторую
    // копию состояния и не является Ending/Consequence Manager — только
    // централизованно читает три стабильных взаимоисключающих флага.
    public static class Chapter01CouncilOutcomeResolver
    {
        public static Chapter01CouncilOutcome GetOutcome(NarrativeStateData state)
        {
            if (state == null)
                return Chapter01CouncilOutcome.None;

            int count = CountOutcomes(state, out Chapter01CouncilOutcome outcome);
            return count == 1 ? outcome : Chapter01CouncilOutcome.None;
        }

        public static bool HasOutcome(NarrativeStateData state)
        {
            return CountOutcomes(state, out _) == 1;
        }

        // Пустой список означает корректное состояние. Помимо строгой
        // взаимоисключаемости проверяет главный инвариант P13:
        // завершённый Совет/глава всегда имеют ровно один итог.
        public static IReadOnlyList<string> ValidateOutcome(NarrativeStateData state)
        {
            List<string> issues = new List<string>();
            if (state == null)
            {
                issues.Add("NarrativeState отсутствует.");
                return issues;
            }

            int count = CountOutcomes(state, out _);
            bool councilCompleted = state.HasFlag(Chapter01Ids.Flags.CouncilCompleted);
            bool chapterCompleted = state.HasFlag(Chapter01Ids.Flags.Completed);

            if (count > 1)
                issues.Add("Одновременно установлено несколько исходов Совета.");

            if (councilCompleted != chapterCompleted)
                issues.Add("CouncilCompleted и Completed должны изменяться вместе.");

            if ((councilCompleted || chapterCompleted) && count != 1)
                issues.Add("Завершённый Совет должен иметь ровно один outcome-флаг.");

            if (!councilCompleted && !chapterCompleted && count > 0)
                issues.Add("Outcome-флаг не должен появляться до завершения Совета.");

            return issues;
        }

        private static int CountOutcomes(
            NarrativeStateData state,
            out Chapter01CouncilOutcome outcome)
        {
            outcome = Chapter01CouncilOutcome.None;
            if (state == null)
                return 0;

            int count = 0;
            if (state.HasFlag(Chapter01Ids.Flags.CouncilOldOrderRestored))
            {
                outcome = Chapter01CouncilOutcome.OldOrderRestored;
                count++;
            }

            if (state.HasFlag(Chapter01Ids.Flags.CouncilNewOrderCreated))
            {
                outcome = Chapter01CouncilOutcome.NewOrderCreated;
                count++;
            }

            if (state.HasFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome))
            {
                outcome = Chapter01CouncilOutcome.WaterKeptForHome;
                count++;
            }

            return count;
        }
    }
}
