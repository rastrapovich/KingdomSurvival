using System.Collections.Generic;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Одноразовая безопасная синхронизация карты разработки
    // ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md (этапы ПР-00…ПР-13) в
    // существующий plan asset. Добавляет только отсутствующие этапы, не
    // пересобирает seed и не трогает прогресс этапов первой главы (P00–P16).
    // Коды ПР-xx и P-коды первой главы — разные серии, не смешивать.
    [InitializeOnLoad]
    public static class DevelopmentPlanPrototypeRoadmapSync
    {
        private const string Marker = "[PROTOTYPE_ROADMAP_V1_SYNCED]";
        private const string PlanDoc = "ProjectDocs/PROTOTYPE_DEVELOPMENT_PLAN.md";
        private const string StatusDoc = "ProjectDocs/DEVELOPMENT_STATUS.md";
        private const int FirstPhaseOrder = 100;

        public const string FirstMilestoneId = "PR00_BASELINE";

        static DevelopmentPlanPrototypeRoadmapSync()
        {
            EditorApplication.delayCall += Sync;
        }

        // Точка входа и для -executeMethod в batchmode.
        public static void Sync()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);
            if (plan == null || (plan.projectNotes ?? string.Empty).Contains(Marker))
                return;

            Apply(plan);
            plan.projectNotes = string.IsNullOrWhiteSpace(plan.projectNotes)
                ? Marker
                : plan.projectNotes + "\n" + Marker;

            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        // Чистая часть без AssetDatabase — её проверяют EditMode-тесты.
        public static void Apply(DevelopmentPlanAsset plan)
        {
            if (plan.phases == null)
                plan.phases = new List<DevelopmentPhaseData>();

            List<DevelopmentPhaseData> roadmap = BuildPhases();
            for (int i = 0; i < roadmap.Count; i++)
            {
                if (plan.FindPhase(roadmap[i].id) == null)
                    plan.phases.Add(roadmap[i]);
            }

            plan.currentMilestoneId = FirstMilestoneId;
            plan.planUpdatedAt = "2026-09-24";
        }

        public static List<DevelopmentPhaseData> BuildPhases()
        {
            List<DevelopmentPhaseData> phases = new List<DevelopmentPhaseData>
            {
                BuildPr00(),
                Phase("PR01_MENU_SESSION", "ПР-01. Главное меню, владелец сессии и жизненный цикл",
                    "Кампания переживает смену сцен; игра начинается с меню, а не с автоматического StartNewGame.",
                    "Executable открывает меню; начать, выйти в меню и продолжить без второй кампании и двойной подписки часов.",
                    new[] { "PR00_BASELINE" },
                    Task("PR01-T01", "Владелец сессии кампании", DevelopmentTaskCategory.Code,
                        "Объект, держащий GameState через смену сцен (или аддитивная загрузка). Одно решение на меню и бой.",
                        new[] { "GameState больше не живёт только в компоненте сцены Prototype_Main", "Переход меню ↔ игра сохраняет кампанию" },
                        new[] { "Выйти в меню и вернуться: время, место и ресурсы прежние" }),
                    Task("PR01-T02", "Отделить показ UI от StartNewGame", DevelopmentTaskCategory.Code,
                        "OnEnable контроллера больше не создаёт кампанию.",
                        new[] { "Кампания создаётся только по команде «Новая игра»" },
                        new[] { "Повторный вход в игру не создаёт вторую кампанию" }),
                    Task("PR01-T03", "Главное меню и меню паузы", DevelopmentTaskCategory.UI,
                        "Продолжить, Новая игра, Загрузить, Настройки, Выход; Esc — пауза.",
                        new[] { "Все пункты работают или недоступны с понятной причиной", "Повреждённый save не запускает пустой мир молча" },
                        new[] { "Запуск сборки открывает меню" })),
                Phase("PR02_CHAPTER_THROUGH", "ПР-02. Сквозное прохождение первой главы",
                    "Главу можно пройти от меню до Совета без Debug и ручной установки флагов.",
                    "Обе ветки ремонта доходят до трёх исходов Совета; скриптовый прогон зелёный.",
                    new[] { "PR01_MENU_SESSION" },
                    Task("PR02-T01", "Штатный вход в N01–N10", DevelopmentTaskCategory.Integration,
                        "Сейчас узлы открываются только из Debug-панели. Нужен игровой способ через Дом, людей и место.",
                        new[] { "Узлы N01–N10 открываются без Debug-панели", "Решено, обязательны ли все три расследования N07" },
                        new[] { "В development-сборке без Debug дойти от N01 до выхода в поход" }),
                    Task("PR02-T02", "Скриптовый прогон главы", DevelopmentTaskCategory.Test,
                        "EditMode-тест без UI: N01 → Совет по обеим веткам ремонта и трём исходам.",
                        new[] { "Тест проходит по обеим веткам ремонта", "Каждый из трёх исходов Совета достижим" },
                        null, DevelopmentVerificationType.EditMode),
                    Task("PR02-T03", "Темп начала главы", DevelopmentTaskCategory.Narrative,
                        "Первое содержательное решение — в пределах ~10 минут.",
                        new[] { "Замерено время до первого решения; затяжки записаны" },
                        null)),
                Phase("PR03_BATTLE_SPIKE", "ПР-03. Ранняя проверка боя",
                    "Черновой мост кампания → BattleSandbox → кампания до модели людей и вещей.",
                    "Бой реальным составом, победа или поражение, возврат в ту же точку без зависшей паузы и дубля результата.",
                    new[] { "PR01_MENU_SESSION", "PR02_CHAPTER_THROUGH" },
                    Task("PR03-T01", "Черновые BattleRequest / BattleResult", DevelopmentTaskCategory.Code,
                        "Текущий герой и FighterData, одно поле, один набор врагов; результат применяется один раз.",
                        new[] { "Вход в бой из прохождения", "Возврат в ту же точку карты", "Повторное применение результата отвергается" },
                        new[] { "Выиграть и проиграть бой, вернуться в кампанию" }),
                    Task("PR03-T02", "Временное правило падения героя", DevelopmentTaskCategory.Decision,
                        "Экран поражения и возврат к меню или последнему сохранению.",
                        new[] { "Правило утверждено пользователем" },
                        null)),
                Phase("PR04_SAVES_TIME", "ПР-04. Надёжные сохранения, время и навигация",
                    "Save/Load из меню, безопасные точки, версии, единый контракт паузы.",
                    "Выход в пути и возврат с тем же маршрутом, временем и знаниями; Хроника не останавливает время.",
                    new[] { "PR03_BATTLE_SPIKE" },
                    Task("PR04-T01", "Save/Load в меню, autosave, ручные слоты", DevelopmentTaskCategory.Code,
                        "Вывести из Debug; autosave до боя и после результата.",
                        new[] { "Сохранение и загрузка доступны из меню", "Нет дубля награды/встречи после загрузки" },
                        new[] { "Сохраниться в пути, перезапустить, загрузить" }),
                    Task("PR04-T02", "Версии формата, контента и географии", DevelopmentTaskCategory.Code,
                        "Проверка GeographyVersion при загрузке; решение по старым save версии 1.",
                        new[] { "Несовместимый save отклоняется без удаления файла" },
                        new[] { "Загрузить save с другой версией географии" }),
                    Task("PR04-T03", "Единый контракт паузы", DevelopmentTaskCategory.Code,
                        "Меню, бой и обязательный выбор блокируют время; Хроника — нет. Заменить структурный тест паузы журнала поведенческим.",
                        new[] { "Хроника открыта при идущих часах", "Обязательное событие поверх Хроники показывается корректно" },
                        new[] { "Открыть Хронику в пути и дождаться события" })),
                Phase("PR05_CAMPAIGN_CONFIG", "ПР-05. Конфигурация кампании и экран старта",
                    "CampaignSetup и сохраняемая конфигурация; мастер и каталоги — в ПР-12.",
                    "Конфигурация фиксируется при старте, сохраняется и восстанавливается; загрузка не перебрасывает мир.",
                    new[] { "PR04_SAVES_TIME" },
                    Task("PR05-T01", "CampaignSetup и конфигурация в save", DevelopmentTaskCategory.Code,
                        "CampaignId, ID кризиса/командира/старта, версии контента, seed.",
                        new[] { "Конфигурация переживает save/load", "Двойной клик «Начать» не создаёт две кампании" },
                        new[] { "Начать, сохранить, загрузить — конфигурация та же" }),
                    Task("PR05-T02", "Экран итога перед стартом", DevelopmentTaskCategory.UI,
                        "Герой, завязка без спойлеров, состояние Дома; «Начать» / «Назад».",
                        new[] { "Отмена не создаёт сохранение и не тратит время" },
                        null)),
                Phase("PR06_PEOPLE", "ПР-06. Люди, семьи и функции Дома",
                    "Один человек — одна запись для боевой, бытовой и сюжетной роли.",
                    "Специалист меняет и поход, и Дом; семья существует после save/load; перемещение не копирует человека.",
                    new[] { "PR05_CAMPAIGN_CONFIG" },
                    Task("PR06-T01", "Resident / Household и состояния человека", DevelopmentTaskCategory.Code,
                        "Дома/в походе/недоступен; жив/ранен/погиб/ушёл; HP отдельно от шаблона UnitDatabase.",
                        new[] { "Состояние человека переживает save/load", "Погибшего нельзя выбрать в поход" },
                        new[] { "Взять жителя в поход и убедиться, что его функция в Доме остановилась" }),
                    Task("PR06-T02", "Сюжетный источник людей вместо платного найма", DevelopmentTaskCategory.Code,
                        "Убрать очередь найма после появления источника из мира; заменить тест найма.",
                        new[] { "Новый боец появляется из мира, а не из таймера" },
                        new[] { "Проверить, что платной очереди найма больше нет" })),
                Phase("PR07_HOME", "ПР-07. Полноценный экран Дома",
                    "Поселение, люди, функции и заботы вместо временной экономики.",
                    "До похода Дом понятен; после возвращения видимо отличается.",
                    new[] { "PR06_PEOPLE" },
                    Task("PR07-T01", "Экран Дома", DevelopmentTaskCategory.UI,
                        "Изображение поселения, люди, функции, Деньги/Запасы Дома, качественная защита.",
                        new[] { "Состояние мельницы и воды берётся из Chapter01HomeState", "Нет «столицы» и «королевских» формулировок" },
                        null),
                    Task("PR07-T02", "Функция Дома через человека или решение", DevelopmentTaskCategory.Code,
                        "Минимум одна функция открывается через человека, знание или решение в мире.",
                        new[] { "Функция открывается причинно и сохраняется" },
                        new[] { "Открыть функцию через сюжетное действие" })),
                Phase("PR08_HERO_ITEMS", "ПР-08. Экран героя, вещи, ранения и подготовка",
                    "Реальное владение вещами и единый расчёт боевых характеристик.",
                    "Найденная вещь применяется, её эффект виден в проверке/бою/сцене и сохраняется.",
                    new[] { "PR06_PEOPLE" },
                    Task("PR08-T01", "Каталог предметов и экземпляры", DevelopmentTaskCategory.Code,
                        "Надеть / снять / использовать / передать; квестовые вещи не теряются.",
                        new[] { "Эффект предмета одинаков на экране и в BattleSandbox", "Вещи переживают save/load" },
                        new[] { "Надеть, снять и использовать предмет" }),
                    Task("PR08-T02", "HP, ранения, изнеможение, свита", DevelopmentTaskCategory.Code,
                        "Карточки состояний; выбор свиты с предупреждением о домашней функции.",
                        new[] { "Состав закрепляется после выхода" },
                        new[] { "Уйти с ранением и проверить отображение" })),
                Phase("PR09_MAP_CAMP", "ПР-09. Карта, дорога, исследование и лагерь",
                    "Поездка с наблюдением и выбором; лагерь по месту стоянки.",
                    "Игрок осмысленно решает «ещё разведать или домой»; обратный путь идёт с текущей позиции.",
                    new[] { "PR02_CHAPTER_THROUGH", "PR06_PEOPLE", "PR07_HOME", "PR08_HERO_ITEMS" },
                    Task("PR09-T01", "«Исследовать область» как отдельное действие", DevelopmentTaskCategory.Code,
                        "Время и понятный результат; слух ведёт к области, находка — по фактическому пути.",
                        new[] { "Исследование тратит время один раз и даёт результат" },
                        new[] { "Исследовать область и получить находку" }),
                    Task("PR09-T02", "Лагерь по контексту стоянки", DevelopmentTaskCategory.Code,
                        "До двух действий в содержательную ночь; спокойная — быстро.",
                        new[] { "Отдых не дублирует суточный расход Припасов", "Экран лагеря не телепортирует и не тратит ночь" },
                        new[] { "Встать лагерем в двух разных местах" })),
                Phase("PR10_BATTLE", "ПР-10. Полноценный бой в кампании",
                    "Наращивание чернового моста ПР-03: люди, HP, снаряжение, последствия.",
                    "Бой реальным отрядом и возврат с теми же людьми и изменённым состоянием; проверенный исход поражения.",
                    new[] { "PR03_BATTLE_SPIKE", "PR04_SAVES_TIME", "PR06_PEOPLE", "PR08_HERO_ITEMS" },
                    Task("PR10-T01", "Полный BattleRequest / BattleResult", DevelopmentTaskCategory.Code,
                        "Постоянные люди, HP, снаряжение, состояния; результат по стабильным ID.",
                        new[] { "HP и потери переносятся в кампанию", "Награда не выдаётся повторно" },
                        new[] { "Потерять бойца в бою и увидеть это в Доме" }),
                    Task("PR10-T02", "Окончательное правило потерь и падения героя", DevelopmentTaskCategory.Decision,
                        "Обычные бойцы могут погибать; герой без скрытого бессмертия.",
                        new[] { "Правило утверждено пользователем" },
                        null)),
                Phase("PR11_CHRONICLE", "ПР-11. Хроника, сведения и последствия",
                    "Дела, Сведения, История из одного состояния.",
                    "После перерыва игрок за минуту понимает, что происходит и куда идти.",
                    new[] { "PR10_BATTLE", "PR09_MAP_CAMP" },
                    Task("PR11-T01", "Вкладка истории и сохранение Хроники", DevelopmentTaskCategory.Code,
                        "Перестать хранить историю в UI-списке донесений.",
                        new[] { "История переживает save/load", "Слух, наблюдение и факт различаются" },
                        new[] { "Загрузить игру и открыть историю" }),
                    Task("PR11-T02", "Цепь «решение → задержанное последствие»", DevelopmentTaskCategory.Content,
                        "Хотя бы одна полная цепь до следующего решения.",
                        new[] { "Цепь проходит в игре и видна в Хронике" },
                        null)),
                Phase("PR12_REPLAY", "ПР-12. Второй герой, второй старт, каталоги и мастер",
                    "Каталоги командиров и стартов, мастер из трёх шагов, 2 × 2 сочетания.",
                    "Все четыре сочетания завершают главу; повтор даёт другой способ действия.",
                    new[] { "PR11_CHRONICLE" },
                    Task("PR12-T01", "Каталоги CommanderDefinition / StartingConditionDefinition и мастер", DevelopmentTaskCategory.Code,
                        "Первый герой и базовый старт переносятся по уже сохранённым ID.",
                        new[] { "Новая карточка появляется из каталога без правки макета", "Старые save загружаются" },
                        new[] { "Пройти мастер вперёд-назад и отменить" }),
                    Task("PR12-T02", "Второй командир и второй старт", DevelopmentTaskCategory.Decision,
                        "Утверждаются пользователем отдельно.",
                        new[] { "Оба профиля утверждены", "Скриптовый прогон покрывает четыре сочетания" },
                        null)),
                Phase("PR13_RELEASE", "ПР-13. Темп, визуальный результат и готовая сборка",
                    "Сборку можно дать человеку без инструкции разработчика.",
                    "Windows-сборка вне Editor доходит до осмысленного результата без блокирующих заглушек.",
                    new[] { "PR12_REPLAY" },
                    Task("PR13-T01", "Независимая Windows-сборка", DevelopmentTaskCategory.Integration,
                        "Запуск вне Editor, сохранения после перезапуска, настройки, отсутствие потерянных ассетов.",
                        new[] { "Сборка запускается на чистой машине" },
                        new[] { "Пройти главу в сборке" }),
                    Task("PR13-T02", "Тест на игроках", DevelopmentTaskCategory.Test,
                        "3–5 человек, не знающих проект; вопросы из §9 плана.",
                        new[] { "Проведено и записано" },
                        null))
            };

            for (int i = 0; i < phases.Count; i++)
                phases[i].order = FirstPhaseOrder + i;

            return phases;
        }

        private static DevelopmentPhaseData BuildPr00()
        {
            DevelopmentPhaseData phase = Phase(
                FirstMilestoneId,
                "ПР-00. Точка отсчёта",
                "Отличить существующие ошибки от будущих изменений; зафиксировать границу среза.",
                "Проект открывается, тесты прогнаны, падения записаны, N01–N17 пройдены вручную хотя бы по одной ветке.",
                null,
                Task("PR00-T01", "Компиляция и прогон EditMode-тестов", DevelopmentTaskCategory.Test,
                    "Batchmode, Unity 6000.5.10f1: 956 тестов — 942 passed / 0 failed / 14 skipped (нужен GUI-скин редактора).",
                    new[] { "Компиляция без ошибок", "Результат прогона записан в DEVELOPMENT_STATUS" },
                    null, DevelopmentVerificationType.EditMode),
                Task("PR00-T02", "Ревизия структурных тестов", DevelopmentTaskCategory.Test,
                    "Помечены тесты, закрепляющие поведение, которое меняет план (пауза журнала — ПР-04, платный найм — ПР-06).",
                    new[] { "Таблица ревизии записана в DEVELOPMENT_STATUS" },
                    null, DevelopmentVerificationType.Auto),
                Task("PR00-T03", "Таблица N01–N17", DevelopmentTaskCategory.Documentation,
                    "Вход, завершение, последствия, продолжение. Найден блокер: N01–N10 открываются только через Debug.",
                    new[] { "Таблица записана в DEVELOPMENT_STATUS" },
                    null, DevelopmentVerificationType.Auto),
                Task("PR00-T04", "Ручной проход главы в редакторе", DevelopmentTaskCategory.Test,
                    "Debug → «ПРОДОЛЖИТЬ ГЛАВУ 1» открывает следующий узел до N10; дальше глава идёт сама.",
                    new[] { "Пройдена хотя бы одна ветка ремонта до Совета", "Найденные блокеры записаны" },
                    new[] { "Пройти главу через кнопку «ПРОДОЛЖИТЬ ГЛАВУ 1»" }),
                Task("PR00-T05", "Решение: где вести прогресс плана", DevelopmentTaskCategory.Decision,
                    "Решено: в этой панели «Этапы разработки».",
                    new[] { "Решение принято пользователем" },
                    null),
                Task("PR00-T06", "Граница выпуска прототипа", DevelopmentTaskCategory.Decision,
                    "Законченная первая глава с локальным итогом; итоговый объём 2 героя × 1 кризис × 2 старта.",
                    new[] { "Граница подтверждена пользователем" },
                    null));

            SetStatus(phase, "PR00-T01", DevelopmentTaskStatus.Completed);
            SetStatus(phase, "PR00-T02", DevelopmentTaskStatus.Completed);
            SetStatus(phase, "PR00-T03", DevelopmentTaskStatus.Completed);
            SetStatus(phase, "PR00-T05", DevelopmentTaskStatus.Completed);
            SetStatus(phase, "PR00-T04", DevelopmentTaskStatus.InProgress);
            return phase;
        }

        private static DevelopmentPhaseData Phase(
            string id,
            string title,
            string purpose,
            string acceptanceSummary,
            string[] dependencies,
            params DevelopmentTaskData[] tasks)
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData
            {
                id = id,
                title = title,
                purpose = purpose,
                acceptanceSummary = acceptanceSummary
            };

            if (dependencies != null)
                phase.dependencies.AddRange(dependencies);

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i].order = i;
                tasks[i].fileReferences.Add(PlanDoc);
                phase.tasks.Add(tasks[i]);
            }

            return phase;
        }

        private static DevelopmentTaskData Task(
            string id,
            string title,
            DevelopmentTaskCategory category,
            string details,
            string[] criteria,
            string[] manualChecks,
            DevelopmentVerificationType verification = DevelopmentVerificationType.Manual)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                category = category,
                details = details,
                verificationType = verification,
                priority = id.StartsWith("PR00") || id.StartsWith("PR01")
                    ? DevelopmentTaskPriority.Now
                    : DevelopmentTaskPriority.Next
            };

            for (int i = 0; i < criteria.Length; i++)
                task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criteria[i] });

            if (manualChecks != null)
                task.manualChecks.AddRange(manualChecks);

            return task;
        }

        private static void SetStatus(DevelopmentPhaseData phase, string taskId, DevelopmentTaskStatus status)
        {
            for (int i = 0; i < phase.tasks.Count; i++)
            {
                DevelopmentTaskData task = phase.tasks[i];
                if (task.id != taskId)
                    continue;

                task.status = status;
                if (status != DevelopmentTaskStatus.Completed)
                    return;

                task.completedAt = "2026-09-24";
                task.fileReferences.Add(StatusDoc);
                for (int j = 0; j < task.acceptanceCriteria.Count; j++)
                    task.acceptanceCriteria[j].done = true;
                return;
            }
        }
    }
}
