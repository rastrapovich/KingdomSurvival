using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (01.10.2026): в ПР-12 добавляется поставка
    // 12З «Анимации существ в бою» (PROTOTYPE_DEVELOPMENT_PLAN.md).
    // Существующие задачи не трогаются.
    [InitializeOnLoad]
    public static class DevelopmentPlanCreatureAnimationSync
    {
        private const string Marker = "[PR12Z_CREATURE_ANIMATION_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string GuideDoc = "ProjectDocs/ANIMATION_DATABASE_GUIDE.md";

        static DevelopmentPlanCreatureAnimationSync()
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
                Task("PR12Z-T01", "12З-1 · База анимаций и импорт папки PNG", DevelopmentTaskCategory.Code,
                    "Модуль AnimationDatabase: наборы, действия × 6 ракурсов, скорость, цикл, маркер, опора; ссылка существа на набор по ID; импорт папки PNG с упаковкой в атласы; таблица ракурсов.",
                    new[] { "Папка из KS Sprite Renderer распознаётся без ручной раскладки", "Step, отрицательный старт и последний кадр сортируются верно", "Ошибочный пакет не меняет действующий набор" }),
                Task("PR12Z-T02", "12З-2 · Предпросмотр в Базе существ", DevelopmentTaskCategory.UI,
                    "Тот же компонент предпросмотра в карточке «Полевая миниатюра»; назначение набора; переход в Базу анимаций.",
                    new[] { "Действие, ракурс, воспроизведение и масштаб просмотра работают без Play Mode" }),
                Task("PR12Z-T03", "12З-3 · Проигрывание в бою", DevelopmentTaskCategory.Code,
                    "Записи ударов из модели; поворот, атака/выстрел, маркер, удар, смерть; независимые часы бойцов; стабильная опора; подстановки.",
                    new[] { "Урон, ОД, инициатива и ответные удары не изменились", "Существо без набора воюет как прежде", "Несколько одинаковых существ анимируются независимо" }),
                Task("PR12Z-T04", "12З-4 · Приёмка на настоящих спрайтах", DevelopmentTaskCategory.Art,
                    "Первое существо из KS Sprite Renderer: таблица ракурсов, опора, размер, темп.",
                    new[] { "Существо ходит, атакует, получает удар и умирает в тестовом бою" })
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
