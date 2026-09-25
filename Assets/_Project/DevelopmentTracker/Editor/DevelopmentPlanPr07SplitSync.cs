using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация: задачи этапа ПР-07 заменяются задачами трёх
    // поставок — 07А-1 (логика Дома), 07А-2 (экран и перетаскивание),
    // 07Б (рыбная ловля и сведения из похода) — по
    // ProjectDocs/PR07_HOME_SPEC.md (утверждена 25.09.2026 с правками §0).
    // Прежние общие задачи ПР-07 не начинались, прогресс не теряется.
    [InitializeOnLoad]
    public static class DevelopmentPlanPr07SplitSync
    {
        private const string Marker = "[PR07_SPLIT_V1_SYNCED]";
        private const string PhaseId = "PR07_HOME";
        private const string SpecDoc = "ProjectDocs/PR07_HOME_SPEC.md";

        static DevelopmentPlanPr07SplitSync()
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

        // Чистая часть — для EditMode-тестов. False, если этапа ПР-07 нет.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData phase = plan.FindPhase(PhaseId);
            if (phase == null)
                return false;

            phase.title = "ПР-07. Полноценный экран Дома (07А-1 + 07А-2 + 07Б)";
            phase.purpose = "Дом читается как одно место: состояние → причина → нужный человек → действие → последствия ухода. Три поставки с ручной проверкой между ними; спецификация — " + SpecDoc + ".";
            phase.tasks = BuildTasks();
            return true;
        }

        public static List<DevelopmentTaskData> BuildTasks()
        {
            List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
            {
                Task("PR07A1-T01", "07А-1 · Единая подготовка состава", DevelopmentTaskCategory.Code,
                    "Одно сохраняемое состояние: упорядоченные бойцы и свита. Атомарные команды «взять / заменить / переставить / оставить дома»; Дом, экран героя и карта читают один выбор.",
                    new[] { "Недопустимое действие отклоняется с причиной и не меняет состав", "Подготовка переживает смену экрана и save/load" }),
                Task("PR07A1-T02", "07А-1 · Настроение и поражение по нему отключены", DevelopmentTaskCategory.Code,
                    "Шкала, кнопки, снижение от голода и поражение при нуле убраны; поле Mood остаётся в сохранении.",
                    new[] { "Сохранение с Mood = 0 играется без поражения", "Голод не меняет настроение" }),
                Task("PR07A1-T03", "07А-1 · Каталог построек отключён", DevelopmentTaskCategory.Code,
                    "Старые здания не дают доходов и не требуют содержания; команды строительства заблокированы; незавершённая стройка отменяется с однократным возвратом цены.",
                    new[] { "Суточный доход — только базовое хозяйство", "Возврат за стройку выдаётся ровно один раз" }),
                Task("PR07A1-T04", "07А-1 · Запасы и защита словами", DevelopmentTaskCategory.Code,
                    "Прогноз до первой полуночи с нехваткой (без деления на ноль и «∞»); защита — число доступных защитников дома.",
                    new[] { "Прогноз совпадает с порядком полуночной обработки", "Защитники — только боеспособные дома" }),
                Task("PR07A1-T05", "07А-1 · Домашние команды только из Дома", DevelopmentTaskCategory.Code,
                    "Ремонт, изменение состава после выхода — отклоняются командой, а не только кнопкой.",
                    new[] { "Команда из похода отклоняется независимо от UI" }),
                Task("PR07A1-T06", "07А-1 · «Дом» вместо «столицы»", DevelopmentTaskCategory.Code,
                    "Игроковые тексты без «столицы», «королевского», «трона»; имена в коде не трогаются.",
                    new[] { "В игроковых строках нет «столиц» и «трона»" }),
                Task("PR07A2-T01", "07А-2 · Композиция экрана Дома", DevelopmentTaskCategory.UI,
                    "Сводка, образ Дома из слоёв, заботы и дела главы, люди, закреплённая панель подготовки. 1280×720 без горизонтальной прокрутки.",
                    new[] { "Человек и место состава видны одновременно", "Состояние объектов совпадает с данными главы" }),
                Task("PR07A2-T02", "07А-2 · Заботы и карточки людей", DevelopmentTaskCategory.UI,
                    "Одна проблема — одна карточка: причина, исполнитель, цена, время, действие. Карточка человека без перехода на экран героя.",
                    new[] { "Нет служебных ID и канцелярских статусов" }),
                Task("PR07A2-T03", "07А-2 · Перетаскивание состава", DevelopmentTaskCategory.UI,
                    "Перетаскивание кандидатов в места бойцов и свиты, замена, перестановка, «Оставить дома»; то же кнопками.",
                    new[] { "Отмена переноса ничего не меняет", "Кнопки и перетаскивание дают одинаковый результат" }),
                Task("PR07A2-T04", "07А-2 · Прогноз «После выхода»", DevelopmentTaskCategory.UI,
                    "Кто останется, кто защищает, что станет с ремонтом и уходом, расход припасов — до выхода.",
                    new[] { "Прогноз не меняет реальное состояние" }),
                Task("PR07A2-T05", "07А-2 · Перечень рисунков для художника", DevelopmentTaskCategory.Documentation,
                    "Список слоёв образа Дома по состояниям; замена рисунков без изменения кода.",
                    new[] { "Перечень записан" }),
                Task("PR07B-T01", "07Б · Рыбная ловля Тихона", DevelopmentTaskCategory.Code,
                    "8 пищи за 24 часа фактической работы дома; дробный остаток сохраняется; выплата в полночь один раз.",
                    new[] { "24 часа одним шагом и многими — одинаково 8", "Нет улова за дорогу и восстановление" }),
                Task("PR07B-T02", "07Б · Польза семьи до решения", DevelopmentTaskCategory.Code,
                    "Предложение семьи называет ловлю, расход и что будет, если взять Тихона в поход.",
                    new[] { "Польза и расход видны до ответа" }),
                Task("PR07B-T03", "07Б · Дом — последние сведения", DevelopmentTaskCategory.Code,
                    "Снимок при выходе с датой; в походе домашние числа не обновляются; домашние действия недоступны.",
                    new[] { "Снимок переживает save/load", "Старое сохранение в пути без снимка не выдумывает данных" }),
                Task("PR07B-T04", "07Б · «Пока вас не было…»", DevelopmentTaskCategory.Code,
                    "Домашние сообщения в походе копятся в сохраняемой очереди и выдаются при возвращении один раз.",
                    new[] { "В походе домашние сообщения не видны", "Очередь выдаётся ровно один раз" })
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
                priority = id.StartsWith("PR07A1") ? DevelopmentTaskPriority.Now : DevelopmentTaskPriority.Next,
                verificationType = category == DevelopmentTaskCategory.Code
                    ? DevelopmentVerificationType.EditMode
                    : DevelopmentVerificationType.Manual
            };

            task.fileReferences.Add(SpecDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            if (category != DevelopmentTaskCategory.Documentation)
                task.manualChecks.Add("Проверить в игре по сценариям §14 спецификации");
            return task;
        }
    }
}
