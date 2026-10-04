using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (04.10.2026): в ПР-12 добавляется поставка
    // 12К «Исследуемые места, бой на месте и физическое присутствие»
    // (канон v1.53 §28.3, §28.9–28.10, план). Существующие задачи не трогаются.
    [InitializeOnLoad]
    public static class DevelopmentPlanLocalExplorationSync
    {
        private const string Marker = "[PR12K_LOCAL_EXPLORATION_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string GuideDoc = "ProjectDocs/LOCAL_EXPLORATION_GUIDE.md";

        static DevelopmentPlanLocalExplorationSync()
        {
            EditorApplication.delayCall += Sync;
        }

        public static void Sync()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);
            if (plan == null || (plan.projectNotes ?? string.Empty).Contains(Marker))
                return;

            if (!Apply(plan))
                return;

            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes)
                ? Marker
                : plan.projectNotes + "\n" + Marker;
            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        // Чистая часть — для EditMode-тестов. False, если ПР-12 ещё нет.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData freePlay = plan.FindPhase(DevelopmentPlanPr12FreePlaySync.FreePlayPhaseId);
            if (freePlay == null)
                return false;

            int order = 0;
            foreach (DevelopmentTaskData existing in freePlay.tasks)
                order = System.Math.Max(order, existing.order + 1);
            foreach (DevelopmentTaskData task in BuildTasks())
            {
                if (freePlay.tasks.Exists(existing => existing != null && existing.id == task.id))
                    continue;
                task.order = order++;
                freePlay.tasks.Add(task);
            }
            return true;
        }

        public static List<DevelopmentTaskData> BuildTasks()
        {
            return new List<DevelopmentTaskData>
            {
                Task("PR12K-T01", "12К-1 · Боевой фундамент: бой из явной геометрии", DevelopmentTaskCategory.Code,
                    "Бой из заданных позиций, местности и отключённых клеток без случайной перегенерации; запрос знает источник, поле, место, участников и точку отхода; союзное существо из общего каталога; показ боя без загрузки другой сцены.",
                    new[] { "Стены не исчезают при нехватке места", "Позиции заданы до первого хода", "Старый полигон и дорожный бой работают как раньше" }),
                Task("PR12K-T02", "12К-2 · Старая шахта на локальной карте", DevelopmentTaskCategory.Code,
                    "Конфигурация места и её проверка; командир ходит кликом, спутники следуют; отвалы, клеймо, признаки опасности, бой со зверями на месте, отход, выход и повторный вход.",
                    new[] { "Бой идёт на том же поле", "После выхода и повторного входа звери и железо не возвращаются", "Герой может пройти шахту один" }),
                Task("PR12K-T03", "12К-3 · Физическое присутствие и лагерь у поселения", DevelopmentTaskCategory.Code,
                    "Единый источник присутствия для диалогов, свиты, особенностей и боя; лагерь у поселения с входящей группой, ожидающими и сбором; технический наёмник и существо-союзник.",
                    new[] { "Оставленный в лагере не вступает в бой и не отвечает в поселении", "Существо остаётся в лагере и участвует в дорожном бою", "Сбор возвращает всех живых" }),
                Task("PR12K-T04", "12К-4 · Сохранение места и лагеря", DevelopmentTaskCategory.Code,
                    "Формат 2 с добавочными полями и маркерами наличия; старые сохранения загружаются как раньше; повтор восстановления не повторяет итогов.",
                    new[] { "Сохранение в шахте восстанавливает слой и позиции", "Старое сохранение без новых полей загружается" }),
                Task("PR12K-T05", "12К-5 · Приёмка исследуемых мест", DevelopmentTaskCategory.Test,
                    "EditMode-проверки, сквозной PlayMode-сценарий шахты, прогон главы, инструкция для следующего места и внешнего участника.",
                    new[] { "Все тесты зелёные", "Ручной проход шахты без Debug" })
            };
        }

        private static DevelopmentTaskData Task(string id, string title, DevelopmentTaskCategory category, string details, string[] criteria)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                category = category,
                details = details,
                priority = DevelopmentTaskPriority.Now,
                verificationType = category == DevelopmentTaskCategory.Code
                    ? DevelopmentVerificationType.EditMode
                    : DevelopmentVerificationType.Manual
            };

            task.fileReferences.Add(PlanDoc);
            task.fileReferences.Add(GuideDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
