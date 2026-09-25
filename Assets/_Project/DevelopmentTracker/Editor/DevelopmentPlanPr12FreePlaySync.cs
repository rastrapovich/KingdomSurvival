using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая синхронизация (25.09.2026, канон v1.45 §6.0): ПР-12 —
    // «Свободная игра» (поставки 12А–12Д); прежние этапы сдвигаются без
    // потери задач: ПР-13 — внешний тест и самостоятельная сборка (задачи
    // прежнего ПР-13), ПР-14 — второй командир/старт/мастер (задачи прежнего
    // ПР-12), ПР-15 — «Дом на чужой воде» на общей основе (главовая часть
    // прежнего ПР-13). ID существующих этапов не меняются.
    [InitializeOnLoad]
    public static class DevelopmentPlanPr12FreePlaySync
    {
        private const string Marker = "[PR12_FREE_PLAY_V1_SYNCED]";
        public const string FreePlayPhaseId = "PR12_FREE_PLAY";
        public const string ReplayPhaseId = "PR12_REPLAY";
        public const string ReleasePhaseId = "PR13_RELEASE";
        public const string HomeWaterPhaseId = "PR15_HOME_WATER";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";

        static DevelopmentPlanPr12FreePlaySync()
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

        // Чистая часть — для EditMode-тестов. False, если нет прежних ПР-12/ПР-13.
        public static bool Apply(DevelopmentPlanAsset plan)
        {
            DevelopmentPhaseData replay = plan.FindPhase(ReplayPhaseId);
            DevelopmentPhaseData release = plan.FindPhase(ReleasePhaseId);
            if (replay == null || release == null)
                return false;

            if (plan.FindPhase(FreePlayPhaseId) == null)
            {
                DevelopmentPhaseData freePlay = new DevelopmentPhaseData
                {
                    id = FreePlayPhaseId,
                    title = "ПР-12. Свободная игра",
                    purpose = "Базовый режим (канон v1.45 §6.0): Дом → люди и припасы → маршрут → место, встреча или бой → лагерь → возвращение → изменившийся Дом → новый выбор. Без линии N01–N17 и тайны кризиса.",
                    order = 112,
                    acceptanceSummary = "Игроку хочется снова выйти из Дома: он помнит людей, ждёт последствий и видит несколько осмысленных возможностей."
                };
                freePlay.dependencies.Add("PR11_CHRONICLE");
                freePlay.tasks = BuildFreePlayTasks();
                plan.phases.Add(freePlay);
            }

            release.title = "ПР-13. Внешний тест и самостоятельная сборка";
            release.purpose = "Исправления по тесту свободной игры на людях, не знающих проект; Windows-сборка вне Editor.";
            release.order = 113;
            release.dependencies.Clear();
            release.dependencies.Add(FreePlayPhaseId);

            replay.title = "ПР-14. Второй командир, второй старт, каталоги и мастер";
            replay.purpose = "Каталоги командиров и стартов, мастер из трёх шагов — на уже проверенной свободной игре.";
            replay.order = 114;
            replay.dependencies.Clear();
            replay.dependencies.Add(ReleasePhaseId);

            if (plan.FindPhase(HomeWaterPhaseId) == null)
            {
                DevelopmentPhaseData homeWater = new DevelopmentPhaseData
                {
                    id = HomeWaterPhaseId,
                    title = "ПР-15. «Дом на чужой воде» на общей основе",
                    purpose = "Первый кризис поверх свободной игры: продолжение и полировка главы. Задачи главовой части прежнего ПР-13.",
                    order = 115,
                    acceptanceSummary = "Глава проходится на общей основе и завершается понятным итогом."
                };
                homeWater.dependencies.Add(ReplayPhaseId);
                homeWater.tasks = BuildHomeWaterTasks();
                plan.phases.Add(homeWater);
            }

            plan.currentMilestoneId = FreePlayPhaseId;
            return true;
        }

        public static List<DevelopmentTaskData> BuildFreePlayTasks()
        {
            List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
            {
                Task("PR12A-T01", "12А · Выбор «Свободная игра» при создании кампании", DevelopmentTaskCategory.Code,
                    "Режим и seed в конфигурации и сохранении; загрузка восстанавливает режим, не выбирает заново.",
                    new[] { "Режим переживает save/load", "Сюжетная кампания создаётся как раньше" }),
                Task("PR12A-T02", "12А · Изоляция первой главы", DevelopmentTaskCategory.Code,
                    "N01–N17, дела Дома главы, дорожные сцены, бой у стоянки, возвращение, Совет и сведения кризиса не срабатывают в свободной партии.",
                    new[] { "Свободная партия за несколько дней не открывает сцен главы", "Прогон главы остаётся зелёным" }),
                Task("PR12A-T03", "12А · Дела и Дом свободной игры", DevelopmentTaskCategory.Code,
                    "Собственный источник «Дел» и забот Дома; пустая вкладка при доступных действиях недопустима.",
                    new[] { "«Дела» не пусты в новой свободной партии" }),
                Task("PR12B-T01", "12Б · Регион: 6–8 содержательных мест", DevelopmentTaskCategory.Content,
                    "Сначала перечень существующих мест, людей и функций; новые — только по необходимости.",
                    new[] { "Каждое место даёт решение, а не только награду" }),
                Task("PR12B-T02", "12Б · 3–4 авторские истории", DevelopmentTaskCategory.Narrative,
                    "Паспорт до диалогов: состояние, зацепка, способы узнать, действия, цена, исходы, отложенное последствие, эхо, продолжение после неудачи.",
                    new[] { "Решение в походе меняет Дом", "Возвращение к месту или человеку открывает следующую стадию", "Взятый специалист помогает в пути и ощутимо отсутствует дома" }),
                Task("PR12V-T01", "12В · Аудит и пул встреч", DevelopmentTaskCategory.Content,
                    "Аудит 14 записей Encounter-базы; 8–12 пригодных встреч разных типов; ограничения повторов, тишина допустима.",
                    new[] { "Разные seed дают разные подходящие истории", "Save/load не дублирует событие, награду или бой" }),
                Task("PR12V-T02", "12В · Противники и составы боя", DevelopmentTaskCategory.Content,
                    "Небольшой набор противников из UnitDatabase, 2–3 осмысленных состава; хотя бы один конфликт предотвращается или допускает отход.",
                    new[] { "Итог боя переносится в ту же кампанию" }),
                Task("PR12G-T01", "12Г · Цельный интерфейс свободной партии", DevelopmentTaskCategory.UI,
                    "По каждому экрану: понятно ли, что происходит; видно ли действие и цену; отражено ли последствие.",
                    new[] { "Несколько циклов Дом → поход → возвращение без Debug" }),
                Task("PR12D-T01", "12Д · Проверка играбельности", DevelopmentTaskCategory.Test,
                    "Автоматические проверки, ручной полный проход, затем 3–5 внешних игроков без подсказок.",
                    new[] { "Записаны ответы на вопросы §3 инструкции ПР-12" })
            };

            for (int i = 0; i < tasks.Count; i++)
                tasks[i].order = i;
            return tasks;
        }

        public static List<DevelopmentTaskData> BuildHomeWaterTasks()
        {
            List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>
            {
                Task("PR15-T01", "Темп начала главы", DevelopmentTaskCategory.Narrative,
                    "Знакомство через действия и быт, затем давление; первое содержательное решение в пределах ~10 минут.",
                    new[] { "Проверено на новом игроке" }),
                Task("PR15-T02", "Состояния образа Дома главы", DevelopmentTaskCategory.Art,
                    "До паводка, повреждение/ремонт, итог возвращения/Совета — смысловые различия.",
                    new[] { "Состояния совпадают с данными главы" }),
                Task("PR15-T03", "Звук воды, мельницы, Дома, дороги и лагеря", DevelopmentTaskCategory.Art,
                    "Обрыв или изменение привычного звука как последствие.",
                    new[] { "Звук не заменяет обязательную информацию" }),
                Task("PR15-T04", "Итог после Совета и завершение главы", DevelopmentTaskCategory.Code,
                    "Человеческий и хозяйственный итог, затем явное завершение с загрузкой или новой кампанией.",
                    new[] { "Игрок не ждёт несуществующую следующую главу" })
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
                priority = id.StartsWith("PR12A") ? DevelopmentTaskPriority.Now
                    : id.StartsWith("PR12") ? DevelopmentTaskPriority.Next
                    : DevelopmentTaskPriority.Later,
                verificationType = category == DevelopmentTaskCategory.Code
                    ? DevelopmentVerificationType.EditMode
                    : DevelopmentVerificationType.Manual
            };

            task.fileReferences.Add(PlanDoc);
            foreach (string criterion in criteria)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterion });
            if (category != DevelopmentTaskCategory.Documentation)
                task.manualChecks.Add("Проверить в игре");
            return task;
        }
    }
}
