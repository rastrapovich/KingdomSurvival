using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (01.10.2026): в ПР-12 добавляется поставка
    // 12И «Прямое управление героем на карте» (канон v1.50, план).
    // Существующие задачи не трогаются.
    [InitializeOnLoad]
    public static class DevelopmentPlanMapMovementSync
    {
        private const string Marker = "[PR12I_MAP_MOVEMENT_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string GuideDoc = "ProjectDocs/WORLD_MAP_MOVEMENT_GUIDE.md";

        static DevelopmentPlanMapMovementSync()
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
                Task("PR12I-T01", "12И-1 · Ядро движения по шестиугольной сетке", DevelopmentTaskCategory.Code,
                    "Сетка на всю карту в геометрии боя, разметка типами местности, поиск пути с обходом и сглаживанием, непрерывное движение с живой скоростью местности; числа — из настроек.",
                    new[] { "Герой обходит непроходимые клетки", "Клик в воду ведёт к ближайшей доступной точке", "Старые маршрутизатор, дорожный граф и зоны удалены" }),
                Task("PR12I-T02", "12И-2 · Время и команды похода", DevelopmentTaskCategory.Code,
                    "Время в походе идёт только при беге или деле; выход, смена цели, возвращение, находки, прибытие и сохранение на новой модели.",
                    new[] { "Стоящий в поле отряд не тратит время", "Свободная точка — тихая остановка без отчёта", "Сохранение в пути восстанавливает позицию" }),
                Task("PR12I-T03", "12И-3 · Инструмент «Перемещение» в Базе карты", DevelopmentTaskCategory.Data,
                    "Размер клетки, кисть местности, показ сетки, таблица местности, настройки героя, времени и камеры; перенос прежних зон и дорог.",
                    new[] { "Разметку можно нарисовать и стереть без Play Mode", "Смена размера клетки пересчитывает разметку" }),
                Task("PR12I-T04", "12И-4 · Герой на карте", DevelopmentTaskCategory.UI,
                    "Фигура из Базы анимаций, шесть ракурсов, ожидание и бег; клик и бег за зажатой кнопкой; отметка клика без траектории; камера следует.",
                    new[] { "Пунктира нет", "Ракурс совпадает с направлением бега", "Зажатая кнопка ведёт героя за курсором" }),
                Task("PR12I-T05", "12И-5 · Приёмка прямого управления", DevelopmentTaskCategory.Test,
                    "Ручной проход: выход, обход воды, бег за курсором, прибытие, находка, ночлег, возвращение, сохранение в пути.",
                    new[] { "Проход без Debug проходит целиком" })
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
