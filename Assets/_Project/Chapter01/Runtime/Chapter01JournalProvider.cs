using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
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

    // Read-only проекция цели похода для UI Журнала (P08J-T01). Не хранится
    // нигде — Chapter01JournalProvider.Build создаёт новый список при каждом
    // вызове из текущего GameState/NarrativeState.
    public sealed class JournalGoalViewData
    {
        public string Id;
        public string Title;
        public string Description;
        public string CurrentStep;

        // Меняется вместе с CurrentStep, когда та же цель переходит к
        // следующему смысловому шагу (например ":prepare" -> ":travel") —
        // UI сравнивает это с уже увиденными ревизиями, чтобы показать
        // НОВОЕ/ОБНОВЛЕНО, не создавая вторую запись под тем же Id.
        public string RevisionId;

        public JournalGoalCategory Category;
        public JournalGoalState State;
    }

    // P08J: журнал ничего не решает и не хранит о сюжетном прогрессе — он
    // только переводит GameState + NarrativeState (Knowledge/Flags) в
    // понятный игроку текст, ровно тем же способом, каким Chapter01StoryDirector
    // уже читает NarrativeState вместо хранения второй копии прогресса. Нет
    // QuestManager/QuestDatabase/quest-флагов/счётчика улик — только чтение.
    public static class Chapter01JournalProvider
    {
        public static IReadOnlyList<JournalGoalViewData> Build(GameState gameState)
        {
            List<JournalGoalViewData> goals = new List<JournalGoalViewData>();

            if (gameState == null || gameState.Narrative == null)
                return goals;

            NarrativeStateData state = gameState.Narrative;

            // Общее правило P08-записей (раздел 9 инструкции): сначала
            // FarRouteUnlocked, потом Journal goals — игрок сначала решает
            // идти дальше в N09, только после этого поход получает
            // формализованные цели. До этого запись не появляется, даже
            // если optional-знания уже выставлены (например через Debug).
            if (!state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked))
                return goals;

            goals.Add(BuildOldWaterTrailGoal(state));

            IReadOnlyList<string> optionalGoals = Chapter01StoryDirector.GetDepartureOptionalGoals(state);
            bool hasSecondLoaf = false;
            bool hasSevenTooth = false;
            for (int i = 0; i < optionalGoals.Count; i++)
            {
                if (string.Equals(optionalGoals[i], Chapter01Ids.Knowledge.SecondLoafIsRation, StringComparison.Ordinal))
                    hasSecondLoaf = true;
                else if (string.Equals(optionalGoals[i], Chapter01Ids.Knowledge.SevenToothObject, StringComparison.Ordinal))
                    hasSevenTooth = true;
            }

            if (hasSecondLoaf)
                goals.Add(BuildSecondLoafGoal());
            if (hasSevenTooth)
                goals.Add(BuildSevenToothGaugeGoal());

            return goals;
        }

        // Одна и та же цель на весь P08/P09 — меняется CurrentStep и
        // RevisionId вслед за ExpeditionStarted, а не создаётся вторая
        // запись "собрать отряд" / "выйти в путь" под другим Id (раздел 6/34
        // инструкции: Goal ID остаётся тем же, Revision и CurrentStep меняются).
        private static JournalGoalViewData BuildOldWaterTrailGoal(NarrativeStateData state)
        {
            bool expeditionStarted = state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted);

            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.OldWaterTrail,
                Title = "Старый след",
                Description = "Проследить старый ход воды и выяснить, куда продолжалась старая система.",
                CurrentStep = expeditionStarted
                    ? "Следовать по старому ходу воды за пределы знакомых дорог."
                    : "Собрать отряд и подготовиться к выходу.",
                RevisionId = Chapter01Ids.JournalGoals.OldWaterTrail + (expeditionStarted ? ":travel" : ":prepare"),
                Category = JournalGoalCategory.Main,
                State = JournalGoalState.Active
            };
        }

        // P10/P11 пока не реализованы — завершение этой цели сознательно не
        // придумывается заранее (раздел 11/36 инструкции). Когда P10/P11
        // дадут игроку реальный ответ, здесь появится Completed-условие по
        // уже существующему знанию/флагу, не по новому quest-completion-флагу.
        private static JournalGoalViewData BuildSecondLoafGoal()
        {
            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.SecondLoaf,
                Title = "Второй хлеб",
                Description = "Выяснить, кому раньше предназначался второй хлеб и почему его несли к воде.",
                CurrentStep = "Спросить об этом у тех, кто связан со старой водной системой.",
                RevisionId = Chapter01Ids.JournalGoals.SecondLoaf + ":discovered",
                Category = JournalGoalCategory.Optional,
                State = JournalGoalState.Active
            };
        }

        private static JournalGoalViewData BuildSevenToothGaugeGoal()
        {
            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.SevenToothGauge,
                Title = "Семь зубцов",
                Description = "Найти другие следы использования семизубого калибра.",
                CurrentStep = "Искать похожие пазы, отметки или устройства вдоль старого водного пути.",
                RevisionId = Chapter01Ids.JournalGoals.SevenToothGauge + ":discovered",
                Category = JournalGoalCategory.Optional,
                State = JournalGoalState.Active
            };
        }
    }
}
