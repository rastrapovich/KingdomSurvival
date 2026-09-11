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

    // Read-only проекция Хроники. Сюжетное состояние остаётся только в
    // GameState/NarrativeState; отдельной Quest Database здесь нет.
    public static class Chapter01JournalProvider
    {
        public static IReadOnlyList<JournalGoalViewData> Build(GameState gameState)
        {
            List<JournalGoalViewData> goals = new List<JournalGoalViewData>();
            if (gameState == null || gameState.Narrative == null)
                return goals;

            NarrativeStateData state = gameState.Narrative;
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
                goals.Add(BuildSecondLoafGoal(state));
            if (hasSevenTooth)
                goals.Add(BuildSevenToothGaugeGoal(state));

            return goals;
        }

        private static JournalGoalViewData BuildOldWaterTrailGoal(NarrativeStateData state)
        {
            bool expeditionStarted = state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted);
            bool destinationReached = state.HasFlag(Chapter01Ids.Flags.RoadDestinationReached);
            bool oldFordFound = state.HasFlag(Chapter01Ids.Flags.OldFordFound);
            bool downstreamContact = state.HasFlag(Chapter01Ids.Flags.DownstreamContact);
            bool agreementRevealed = state.HasFlag(Chapter01Ids.Flags.AgreementRevealed);
            bool returnStarted = state.HasFlag(Chapter01Ids.Flags.ReturnStarted);
            bool returnRoadTraveled = state.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled);
            bool returnedHome = state.HasFlag(Chapter01Ids.Flags.ReturnedHome);

            string currentStep;
            string revisionSuffix;
            JournalGoalState goalState = JournalGoalState.Active;

            if (returnedHome)
            {
                currentStep = "Вернуться с найденной правдой в Дом.";
                revisionSuffix = ":home";
                goalState = JournalGoalState.Completed;
            }
            else if (returnRoadTraveled)
            {
                currentStep = "Дойти до Дома и увидеть, что изменилось за время похода.";
                revisionSuffix = ":homeward";
            }
            else if (returnStarted)
            {
                currentStep = Chapter01ReturnFlow.GetBranch(state) == Chapter01ReturnBranch.FollowFreshTrace
                    ? "Закончить проверку свежего следа и возвращаться физическим маршрутом к Дому."
                    : "Возвращаться физическим маршрутом к Дому.";
                revisionSuffix = ":returning";
            }
            else if (agreementRevealed)
            {
                currentStep = "Решить: искать дальше, пока след свежий, или вернуться к людям, которые платят за отсутствие героя.";
                revisionSuffix = ":return_decision";
            }
            else if (downstreamContact)
            {
                currentStep = "Сопоставить следы старой системы со словами людей ниже по течению.";
                revisionSuffix = ":agreement";
            }
            else if (oldFordFound)
            {
                currentStep = "Добраться до людей ниже по течению.";
                revisionSuffix = ":downstream_people";
            }
            else if (destinationReached)
            {
                currentStep = "Осмотреть место, где старый путь снова выходит к воде.";
                revisionSuffix = ":search_area";
            }
            else if (expeditionStarted)
            {
                currentStep = "Следовать по старому ходу воды за пределы знакомых дорог.";
                revisionSuffix = ":travel";
            }
            else
            {
                currentStep = "Собрать отряд и подготовиться к выходу.";
                revisionSuffix = ":prepare";
            }

            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.OldWaterTrail,
                Title = "Старый след",
                Description = "Проследить старый ход воды, понять, с кем Дом делил эту систему, и вернуться с последствиями найденного.",
                CurrentStep = currentStep,
                RevisionId = Chapter01Ids.JournalGoals.OldWaterTrail + revisionSuffix,
                Category = JournalGoalCategory.Main,
                State = goalState
            };
        }

        private static JournalGoalViewData BuildSecondLoafGoal(NarrativeStateData state)
        {
            bool resolved = state.HasKnowledge(Chapter01Ids.Knowledge.OldAgreement) &&
                            state.HasKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);
            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.SecondLoaf,
                Title = "Второй хлеб",
                Description = "Выяснить, кому раньше предназначался второй хлеб и почему его несли к воде.",
                CurrentStep = resolved
                    ? "Смысл старого обычая восстановлен: хлеб был частью общего порядка людей у воды."
                    : "Спросить об этом у тех, кто связан со старой водной системой.",
                RevisionId = Chapter01Ids.JournalGoals.SecondLoaf + (resolved ? ":resolved" : ":discovered"),
                Category = JournalGoalCategory.Optional,
                State = resolved ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }

        private static JournalGoalViewData BuildSevenToothGaugeGoal(NarrativeStateData state)
        {
            bool resolved = state.HasKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem) &&
                            state.HasKnowledge(Chapter01Ids.Knowledge.OldAgreement);
            return new JournalGoalViewData
            {
                Id = Chapter01Ids.JournalGoals.SevenToothGauge,
                Title = "Семь зубцов",
                Description = "Найти другие следы использования семизубого калибра.",
                CurrentStep = resolved
                    ? "Следы предмета связаны с общей водной системой и её старым порядком обслуживания."
                    : "Искать похожие пазы, отметки или устройства вдоль старого водного пути.",
                RevisionId = Chapter01Ids.JournalGoals.SevenToothGauge + (resolved ? ":resolved" : ":discovered"),
                Category = JournalGoalCategory.Optional,
                State = resolved ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }
    }
}
