using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация: задачи этапа ПР-06 заменяются задачами двух
    // поставок — ПР-06А (люди как одна личность) и ПР-06Б (пополнение) — по
    // ProjectDocs/PR06_PEOPLE_SPEC.md (утверждена 24.09.2026 с правками §0).
    // Прежние две общие задачи ПР-06 не начинались, прогресс не теряется.
    [InitializeOnLoad]
    public static class DevelopmentPlanPr06SplitSync
    {
        private const string Marker = "[PR06_SPLIT_V1_SYNCED]";
        private const string PhaseId = "PR06_PEOPLE";
        private const string SpecDoc = "ProjectDocs/PR06_PEOPLE_SPEC.md";

        static DevelopmentPlanPr06SplitSync()
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

        // Чистая часть — для EditMode-тестов. False, если этапа ПР-06 нет.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData phase = plan.FindPhase(PhaseId);
            if (phase == null)
                return false;

            phase.title = "ПР-06. Люди, семьи и функции Дома (06А + 06Б)";
            phase.purpose = "Один человек — одна запись для боевой, бытовой и сюжетной роли. Две поставки с ручной проверкой между ними; спецификация — " + SpecDoc + ".";
            phase.tasks = BuildTasks();
            return true;
        }

        public static List<DevelopmentTaskData> BuildTasks()
        {
            List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
            {
                Task("PR06A-T01", "06А · Реестр жителей и домохозяйств", DevelopmentTaskCategory.Code,
                    "24 жителя новой партии: Командир, 5 бойцов, 4 сюжетных жителя, 14 фоновых в 4 семьях. Население — производное.",
                    new[] { "Новая партия — 24 живых человека с уникальными ID", "Один человек не существует в двух копиях" }),
                Task("PR06A-T02", "06А · Фактический выход и возврат", DevelopmentTaskCategory.Code,
                    "Подготовленный состав не выключает функции; реальный выход — выключает, возврат — восстанавливает.",
                    new[] { "Подготовка не перемещает людей", "Возврат восстанавливает функции один раз" }),
                Task("PR06A-T03", "06А · Настил и уход с замещением", DevelopmentTaskCategory.Code,
                    "Лада — настил (Остафий/Торвин частично); Марта — уход (Ульяна частично). Работает / ограничено / остановлено с причиной.",
                    new[] { "Отправление Лады/Марты меняет состояние функции", "Два частичных заместителя не дают полную скорость" }),
                Task("PR06A-T04", "06А · Свита 0–1", DevelopmentTaskCategory.Code,
                    "Остафий или Лада; прогноз функций Дома до выхода; дети, погибшие, ушедшие недоступны.",
                    new[] { "Свита не занимает место бойца и не идёт в бой", "Недопустимый состав отклоняется с причиной" }),
                Task("PR06A-T05", "06А · Раздельный расход и стартовое обеспечение", DevelopmentTaskCategory.Code,
                    "Полночь: дома едят дома, в походе — из припасов похода. Приток 24 для нового старта. Голод не убирает людей.",
                    new[] { "24 и поход 6 — расход 18 + 6", "Голод не уменьшает население анонимно" }),
                Task("PR06A-T06", "06А · HP в бою по человеку", DevelopmentTaskCategory.Code,
                    "HP переносятся в BattleSandbox и обратно по PersonId; павший боец — Dead в реестре.",
                    new[] { "Повреждённые HP переживают переход в бой и обратно", "Погибший остаётся в реестре и не выбирается" }),
                Task("PR06A-T07", "06А · Сохранение формата 2 и UI людей", DevelopmentTaskCategory.Code,
                    "Реестр, работы и уход в сохранении; формат 1 честно отклоняется. Блок «Люди Дома», слот свиты.",
                    new[] { "Состояние людей переживает save/load", "Формат 1 отклоняется с причиной, файл не тронут" }),
                Task("PR06B-T01", "06Б · Семья Тихона вместо платного найма", DevelopmentTaskCategory.Content,
                    "Сцена «Люди у ворот»: принять всех четверых или отказать; платная очередь бойцов убирается.",
                    new[] { "Принятие добавляет ровно четыре записи один раз", "Платного найма бойцов больше нет" }),
                Task("PR06B-T02", "06Б · Сцена брода", DevelopmentTaskCategory.Content,
                    "Обход всегда; Лада, Остафий, Тихон открывают свои способы.",
                    new[] { "Без свиты брод проходится обходом", "Специалист открывает свой способ" }),
                Task("PR06B-T03", "06Б · Карточка семьи и паспорта в NARRATIVE", DevelopmentTaskCategory.Documentation,
                    "Карточка семьи в UI; паспорта пяти бойцов, семья Тихона и сцена брода — в NARRATIVE.md как [РАБОЧЕЕ].",
                    new[] { "Паспорта записаны с источниками и статусом" })
            };

            for (int i = 0; i < tasks.Count; i++)
                tasks[i].order = i;
            return tasks;
        }

        private static DevelopmentTaskData Task(string id, string title, DevelopmentTaskCategory category, string details, string[] criteria)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                category = category,
                details = details,
                priority = id.StartsWith("PR06A") ? DevelopmentTaskPriority.Now : DevelopmentTaskPriority.Next,
                verificationType = category == DevelopmentTaskCategory.Documentation
                    ? DevelopmentVerificationType.Manual
                    : DevelopmentVerificationType.EditMode
            };

            task.fileReferences.Add(SpecDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            if (category == DevelopmentTaskCategory.Code)
                task.manualChecks.Add("Проверить в игре по ручному сценарию §17.5 спецификации");
            return task;
        }
    }
}
