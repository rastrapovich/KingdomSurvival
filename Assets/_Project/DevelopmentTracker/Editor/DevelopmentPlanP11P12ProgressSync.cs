using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация уже существующего DevelopmentPlanAsset после
    // реализации P11/P12. Не пересобирает план из seed и не стирает ручные
    // галочки: Completed/Blocked/Deferred сохраняются как есть.
    [InitializeOnLoad]
    public static class DevelopmentPlanP11P12ProgressSync
    {
        private const string PlanPath = "Assets/_Project/Resources/Development/KingdomSurvivalDevelopmentPlan.asset";
        private const string Marker = "[P11P12_RUNTIME_V1_SYNCED]";

        static DevelopmentPlanP11P12ProgressSync()
        {
            EditorApplication.delayCall += Sync;
        }

        private static void Sync()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(PlanPath);
            if (plan == null || (plan.projectNotes ?? string.Empty).Contains(Marker))
                return;

            string[] taskIds =
            {
                "P11-T01", "P11-T02", "P11-T03",
                "P12-T01", "P12-T02", "P12-T03"
            };

            for (int i = 0; i < taskIds.Length; i++)
            {
                DevelopmentTaskData task = plan.FindTask(taskIds[i], out _);
                if (task == null)
                    continue;

                if (task.status == DevelopmentTaskStatus.NotStarted ||
                    task.status == DevelopmentTaskStatus.InProgress)
                {
                    task.status = DevelopmentTaskStatus.NeedsUnityCheck;
                }

                string note = GetImplementationNote(task.id);
                if (!string.IsNullOrEmpty(note) && string.IsNullOrEmpty(task.implementationNote))
                    task.implementationNote = note;
            }

            plan.currentMilestoneId = "P11_AGREEMENT";
            plan.planUpdatedAt = "2026-09-12";
            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes)
                ? Marker
                : plan.projectNotes + "\n" + Marker;

            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        private static string GetImplementationNote(string taskId)
        {
            switch (taskId)
            {
                case "P11-T01":
                    return "N14 уже выдаёт SharedWaterSystem/OldAgreement/HomeWasNotSelfSufficient; Chapter01ReturnFlow дополнительно гарантирует знания для старых/отладочных сохранений.";
                case "P11-T02":
                    return "Практическая причинная связь фиксируется знаниями, сверхъестественная причина не кодируется отдельным истинным флагом/шкалой.";
                case "P11-T03":
                    return "Ветка N14½ читается из сохранённых EffectExecutionId. «Идти дальше» тратит 4 ч; обе ветки затем используют физический TryOrderReturn.";
                case "P12-T01":
                    return "N15 открывается только после физического прогресса обратного маршрута; после сцены применяется одноразовая временная цена обратной дороги.";
                case "P12-T02":
                    return "N16 открывается только после фактического завершения ReturningToCastle. Технический попап возвращения уступает место сюжетной сцене.";
                case "P12-T03":
                    return "Chapter01ReturnFlow.ResolveEcho использует RepairOld/RepairNew, последствия паводка и ветку N14½; Хроника обновлена по стадиям возвращения.";
                default:
                    return string.Empty;
            }
        }
    }
}
