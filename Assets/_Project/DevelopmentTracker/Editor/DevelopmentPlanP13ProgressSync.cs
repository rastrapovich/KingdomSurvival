using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая безопасная синхронизация P13 для уже существующего plan
    // asset. Не пересобирает seed и не стирает ручные acceptance-галочки.
    [InitializeOnLoad]
    public static class DevelopmentPlanP13ProgressSync
    {
        private const string Marker = "[P13_COUNCIL_V1_SYNCED]";

        static DevelopmentPlanP13ProgressSync()
        {
            EditorApplication.delayCall += Sync;
        }

        private static void Sync()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);
            if (plan == null || (plan.projectNotes ?? string.Empty).Contains(Marker))
                return;

            DevelopmentTaskData council = plan.FindTask("P13-T01", out _);
            if (council != null)
            {
                MoveToUnityCheck(council);
                SetImplementationNoteIfEmpty(
                    council,
                    "D17 заменён с placeholder на трёхветвевой Совет. CouncilCompleted/Completed выдаются только после финального решения. Три направления имеют видимую цену.");
                AddUnique(council.relatedDialogueIds, "chapter01_dialogue_17_council_of_the_house");
                AddUnique(council.relatedFlagIds, "chapter01.flag.council_completed");
                AddUnique(council.relatedFlagIds, "chapter01.flag.completed");
                AddUnique(council.fileReferences, "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset");
                AddUnique(council.fileReferences, "Assets/_Project/UI/PrototypeUIController.Chapter01ReturnFlow.cs");
                AddUnique(council.manualChecks, "После N16 штатно открывается N17");
                AddUnique(council.manualChecks, "До выбора CouncilCompleted/Completed не установлены");
                AddUnique(council.manualChecks, "У каждого из трёх вариантов цена видна до подтверждения");
            }

            DevelopmentTaskData mila = plan.FindTask("P13-T02", out _);
            if (mila != null)
            {
                if (mila.status == DevelopmentTaskStatus.NotStarted ||
                    mila.status == DevelopmentTaskStatus.InProgress ||
                    mila.status == DevelopmentTaskStatus.NeedsUnityCheck)
                {
                    mila.status = DevelopmentTaskStatus.Deferred;
                }

                if (string.IsNullOrWhiteSpace(mila.blockerNote))
                {
                    mila.blockerNote =
                        "Отложено до отдельного утверждения DEC-07: личность Милы, обстоятельства смерти, " +
                        "отношение к воде и границы сверхъестественного не утверждены. Не блокирует обязательный P13.";
                }
            }

            DevelopmentTaskData outcomes = plan.FindTask("P13-T03", out _);
            if (outcomes != null)
            {
                MoveToUnityCheck(outcomes);
                SetImplementationNoteIfEmpty(
                    outcomes,
                    "Добавлены три взаимоисключающих outcome-флага, future-debt flag для ветки воды Дому, resolver исхода, Хроника и regression tests.");
                AddUnique(outcomes.relatedFlagIds, "chapter01.flag.council_old_order_restored");
                AddUnique(outcomes.relatedFlagIds, "chapter01.flag.council_new_order_created");
                AddUnique(outcomes.relatedFlagIds, "chapter01.flag.council_water_kept_for_home");
                AddUnique(outcomes.relatedFlagIds, "chapter01.flag.downstream_debt_open");
                AddUnique(outcomes.fileReferences, "Assets/_Project/Chapter01/Runtime/Chapter01CouncilOutcome.cs");
                AddUnique(outcomes.fileReferences, "Assets/_Project/Chapter01/Runtime/Chapter01JournalProvider.cs");
                AddUnique(outcomes.fileReferences, "Assets/_Project/Chapter01/Tests/EditMode/Chapter01P13Tests.cs");
                AddUnique(outcomes.manualChecks, "Save/Load сохраняет выбранный исход");
                AddUnique(outcomes.manualChecks, "Совет нельзя открыть повторно");
                AddUnique(outcomes.manualChecks, "Хроника показывает выбранный итог");
            }

            plan.currentMilestoneId = "P13_COUNCIL";
            plan.planUpdatedAt = "2026-09-12";
            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes)
                ? Marker
                : plan.projectNotes + "\n" + Marker;

            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        private static void MoveToUnityCheck(DevelopmentTaskData task)
        {
            if (task.status == DevelopmentTaskStatus.NotStarted ||
                task.status == DevelopmentTaskStatus.InProgress)
            {
                task.status = DevelopmentTaskStatus.NeedsUnityCheck;
            }
        }

        private static void SetImplementationNoteIfEmpty(DevelopmentTaskData task, string note)
        {
            if (string.IsNullOrWhiteSpace(task.implementationNote))
                task.implementationNote = note;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values != null && !values.Contains(value))
                values.Add(value);
        }
    }
}
