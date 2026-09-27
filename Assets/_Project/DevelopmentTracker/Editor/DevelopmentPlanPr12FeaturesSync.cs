using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (27.09.2026): в ПР-12 добавляется поставка
    // 12Е «Особенности, приёмы и приказы по каталогу» — шаги 12Е-1…12Е-10 из
    // PROTOTYPE_DEVELOPMENT_PLAN.md. Существующие задачи не трогаются.
    [InitializeOnLoad]
    public static class DevelopmentPlanPr12FeaturesSync
    {
        private const string Marker = "[PR12E_FEATURES_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string CatalogDoc = "ProjectDocs/FEATURES_REFERENCES_ADAPTATION.md";

        static DevelopmentPlanPr12FeaturesSync()
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
                Task("PR12E-T01", "12Е-1 · Карточки особенностей, приёмов и приказов в Базе развития", DevelopmentTaskCategory.Data,
                    "Модель карточки §0.1 каталога; статусы кандидат / заглушка / активна; весь каталог в базе и в значениях ядра; страница с фильтрами; проверка базы.",
                    new[] { "Все записи каталога видны и правятся в окне базы", "Проверка базы без ошибок", "Значения ядра по умолчанию совпадают с базой" }),
                Task("PR12E-T02", "12Е-2 · Особенности у каждого человека", DevelopmentTaskCategory.Code,
                    "Владение с рангом и источником; save/load; перенос прежних особенностей Командира; показ в карточке человека; выдача вне уровня одной командой.",
                    new[] { "Особенность, выданная историей, переживает save/load", "Видна в карточке человека" }),
                Task("PR12E-T03", "12Е-3 · Предложение выбора 3 → 35", DevelopmentTaskCategory.Code,
                    "Число вариантов растёт с каждым показом; кандидаты по требованиям, владельцу, рангам и взаимоисключениям; только активные особенности; интерфейс длинного списка.",
                    new[] { "У Командира и бойца разные подходящие варианты", "Заглушки и кандидаты без кода не предлагаются" }),
                Task("PR12E-T04", "12Е-4 · Диспетчер событий и лимиты", DevelopmentTaskCategory.Code,
                    "Единые события (проверка, бой, урон, ночёвка, вход в место, дорожная встреча, возвращение); лимиты ход/бой/встреча/поход/до лагеря в сохранении; переброс без сейв-скама.",
                    new[] { "Лимит «1/поход» не сбрасывается загрузкой", "Переброс после загрузки даёт тот же результат" }),
                Task("PR12E-T05", "12Е-5 · Первая партия особенностей вне боя", DevelopmentTaskCategory.Code,
                    "12 особенностей §6.1 каталога на проверках, пути, лагере и обучении.",
                    new[] { "Каждая срабатывает минимум в двух реальных точках игры" }),
                Task("PR12E-T06", "12Е-6 · Первая партия боевых особенностей", DevelopmentTaskCategory.Code,
                    "8 особенностей §6.1 каталога через сборку боевых чисел и BattleSandbox.",
                    new[] { "Экран человека и бой показывают одно и то же «из чего сложилось»" }),
                Task("PR12E-T07", "12Е-7 · Сработавшая особенность видна", DevelopmentTaskCategory.UI,
                    "Подсветка в диалоге и итогах: что персонаж узнал или изменил; цвет не подсказывает правильный ответ.",
                    new[] { "Игрок видит, какая особенность сработала и что дала" }),
                Task("PR12E-T08", "12Е-8 · Следы развития", DevelopmentTaskCategory.Code,
                    "Первые следы на собираемых данных; «нет → замечено → характерно» с устойчивыми ID; след открывает кандидата в предложении.",
                    new[] { "Повтор той же ситуации не продвигает след" }),
                Task("PR12E-T09", "12Е-9 · Функции присутствия", DevelopmentTaskCategory.Code,
                    "Функции людей как данные; подготовка похода показывает, что уходит вместе с человеком.",
                    new[] { "Без специалиста в походе его функция недоступна и это видно заранее" }),
                Task("PR12E-T10", "12Е-10 · Включение заглушек по мере механик", DevelopmentTaskCategory.Decision,
                    "Теговые особенности — после словаря тегов; приёмы и приказы в бою, реакции, боезапас, повреждения вещей — отдельными решениями (вопросы 17–19 каталога).",
                    new[] { "Каждое включение — отдельное решение пользователя" })
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
                priority = id == "PR12E-T01" || id == "PR12E-T02" || id == "PR12E-T03"
                    ? DevelopmentTaskPriority.Now
                    : id == "PR12E-T10" ? DevelopmentTaskPriority.Later : DevelopmentTaskPriority.Next,
                verificationType = category == DevelopmentTaskCategory.Code || category == DevelopmentTaskCategory.Data
                    ? DevelopmentVerificationType.EditMode
                    : DevelopmentVerificationType.Manual
            };

            task.fileReferences.Add(PlanDoc);
            task.fileReferences.Add(CatalogDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            if (category != DevelopmentTaskCategory.Decision)
                task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
