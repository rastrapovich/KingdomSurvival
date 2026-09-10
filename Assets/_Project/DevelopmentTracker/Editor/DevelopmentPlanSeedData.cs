using System;
using System.Collections.Generic;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Начальное содержимое плана — сводка инструкции «Kingdom Survival:
    // единая инструкция по реализации Главы 01 и экрана «Этапы разработки»».
    // Это рабочий каркас производственного контроля, а не нарративный канон:
    // формулировки задач опираются на утверждённые разделы 1–25 инструкции,
    // но сами по себе не утверждают факты мира (см. раздел 3 инструкции).
    public static class DevelopmentPlanSeedData
    {
        public static void Populate(DevelopmentPlanAsset plan)
        {
            if (plan == null)
                return;

            plan.schemaVersion = DevelopmentPlanAsset.CurrentSchemaVersion;
            plan.projectTitle = "Kingdom Survival — Глава 01 «Дом на чужой воде»";
            plan.currentMilestoneId = "P02_BASELINE";
            plan.planUpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            plan.projectNotes =
                "План сгенерирован DevelopmentPlanSeedData по своду инструкции. " +
                "ID этапов и задач стабильны и используются другими задачами как " +
                "зависимости — не переименовывать их вручную без переноса ссылок.";

            plan.phases = new List<DevelopmentPhaseData>
            {
                BuildP00Decisions(),
                BuildP01Tracker(),
                BuildP02Baseline(),
                BuildP03Scaffold(),
                BuildP04Morning(),
                BuildP05Flood(),
                BuildP06RepairChoice(),
                BuildP07Investigation(),
                BuildP08Departure(),
                BuildP09Road(),
                BuildP10Ford(),
                BuildP11Agreement(),
                BuildP12Return(),
                BuildP13Council(),
                BuildP14Encounters(),
                BuildP15BattleBridge(),
                BuildP16QaDocs()
            };
        }

        private static DevelopmentPhaseData BuildP00Decisions()
        {
            return Phase(
                "P00_DECISIONS", "Утверждение открытых решений",
                "Зафиксировать ответы DEC-01…DEC-12 и внести утверждённое в LORE.md/NARRATIVE.md/BESTIARY.md, прежде чем рабочие имена и правила станут игровыми данными как окончательная истина.",
                0, true,
                "У каждого решения есть статус, формулировка ответа и ссылка на канонический документ; ни одно рабочее имя не попало в игровые данные как факт без решения.",
                null,

                Task("P00-T01", "DEC-03 — Устройство водной системы",
                    "Взять рабочую модель семи затворов, бокового русла и нижнего канала как основу для мельницы, брода, рыбы и людей ниже по течению.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 1,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в LORE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял рекомендацию инструкции без изменений. Модель (семь затворов, боковое русло, нижний канал) внесена в LORE.md §6.1.1 как [УТВЕРЖДЕНО]."),

                Task("P00-T02", "DEC-04 — Два способа ремонта",
                    "Старый ремонт возвращает работу всех затворов и бокового сброса; новый перекрывает седьмое русло и стабилизирует мельницу.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 2,
                    dependencies: new[] { "P00-T01" },
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в LORE.md/NARRATIVE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял рекомендацию без изменений. Качественное направление внесено в NARRATIVE.md §26.3 (пп. 2–3) и в открытые списки §27/«Что намеренно остаётся открытым»; точные сценарные формулировки и сложности проверок остаются производственной работой P06."),

                Task("P00-T03", "DEC-10 — Люди ниже по течению и условия соглашения",
                    "Утвердить, кто они, что именно поддерживали и какую цену платили обе стороны — человеческое ядро развязки.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 3,
                    dependencies: new[] { "P00-T01" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.downstream_people", "chapter01.knowledge.old_agreement" },
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в LORE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял предложенный вариант: нижние — малое поселение на нижнем канале/броде (рыболовство + поле на стоке через боковое русло); нижняя смена чистила канал/берега брода в обмен на регулярный сток через семь затворов по сигналу условных стуков; второй хлеб — рацион нижней смене. Внесено в LORE.md §6.1.2. Имя/география поселения остаются рабочими до отдельного решения о месте."),

                Task("P00-T04", "DEC-05 — Второй хлеб",
                    "Утвердить как рацион работнику нижней смены, позже искажённый до подношения.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 4,
                    dependencies: new[] { "P00-T01" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.second_loaf_is_ration" },
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в LORE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Подтверждена арка: второй хлеб — исторически рацион нижней смены (уже утверждено DEC-10); к N01 практический смысл забыт, жители Дома относятся к нему как к суеверию/подношению затвору; практическое объяснение открывается позже через DEC-10. Внесено в LORE.md §6.1.2."),

                Task("P00-T05", "DEC-06 — Старый предмет",
                    "Утвердить семизубую железную пластину как измеритель/калибр затворов.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 5,
                    dependencies: new[] { "P00-T01" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.seven_tooth_object" },
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в LORE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял рекомендацию без изменений: семь зубцов = калибр семи затворов; к N01 предмет используется как гребень для собаки, назначение раскрывается расследованием. Внесено в LORE.md §6.1.3."),

                Task("P00-T06", "DEC-07 — Утонувшая женщина",
                    "Имя «Мила» оставить рабочим до заполнения полного паспорта существа и биографии смерти по правилам BESTIARY.md.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 6,
                    dependencies: new[] { "P00-T05" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.drowned_woman_story" },
                    acceptanceCriteria: new[]
                    {
                        "Зафиксирован явный ответ пользователя (согласие отложить)",
                        "Решение об отсрочке зафиксировано; полный паспорт в BESTIARY.md — отдельная будущая задача (см. P10-T01/P13-T02), не блокирующая P00"
                    },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь согласился с рекомендацией процесса: имя «Мила» остаётся рабочим, полный паспорт существа (личность, привязка к месту, долг, последствия — по правилам BESTIARY.md) сознательно отложен до сцен P10/P13, где он реально понадобится. BESTIARY.md не менялся — фактов о существе пока не утверждено."),

                Task("P00-T07", "DEC-12 — Доля сверхъестественного",
                    "Сохранить неоднозначность: практическая причина доказуема, проявление памяти воды допустимо, но не обязано иметь одно объяснение.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 7,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в NARRATIVE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял рекомендацию без изменений: неоднозначность сохранена как сознательное решение (не как открытый вопрос). Согласуется с общим каноном LORE.md §11.1. Внесено в LORE.md §6.1.2 и NARRATIVE.md §26.3/§27."),

                Task("P00-T08", "DEC-01 — Минимальная конкретика героя",
                    "Утвердить роль, исходную позицию в Доме и голос героя до финального написания реплик.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 8,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в NARRATIVE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь выбрал направление «Наследник/преемник»: герой готовится принять хозяйственно-распорядительную ответственность за Дом; доверие — по праву позиции, но ещё не доказано делом; паводок и ремонт — первое настоящее испытание. Голос: практичный, наблюдательный, чувствует вес решений сразу. Пол/возраст/имя/личная причина остаются открытыми. Внесено в NARRATIVE.md §26.1.1."),

                Task("P00-T09", "DEC-02 — Повторяющиеся жители Дома",
                    "Сначала утвердить функции (хранитель старого порядка, молодой мастер, мельник, семейный голос), затем имена — Остафий/Лада/Мирон/Ульяна пока рабочие.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 9,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в NARRATIVE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял рекомендацию без изменений: 4 функции утверждены (хранитель старого порядка, молодой мастер, мельник, семейный голос). Имена Остафий/Лада/Мирон/Ульяна остаются рабочими для черновиков. Внесено в NARRATIVE.md §26.1.2."),

                Task("P00-T10", "DEC-11 — Финальный набор решений",
                    "Сохранить три направления развязки, но точные цены определить после DEC-03 и DEC-10.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 10,
                    dependencies: new[] { "P00-T01", "P00-T02", "P00-T03" },
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя", "При принятии — внесено в NARRATIVE.md" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь принял предложенные цены без изменений: (1) старый порядок — постоянный труд/ресурсы, публичное признание зависимости; (2) новый порядок — новое явное обязательство взамен забытого; (3) вода Дому — нижние окончательно теряют сток, испорченные отношения, возможный долг/месть позже. Внесено в NARRATIVE.md §26.3 п.9."),

                Task("P00-T11", "DEC-08 — Навык «Ремесло»",
                    "Пока не добавлять; использовать Суждение, Следопытство, знания и контекст. Вернуться, если появятся минимум три регулярных применения.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 11,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя (в т. ч. явное решение отложить)" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь ОТСТУПИЛ от рекомендации инструкции: компетенция «Ремесло» добавляется в Главу 01, а не откладывается. Регистрация в HeroProfile.cs сознательно отложена до P03 (см. P03-T07) — в этой сессии код не менялся. Зафиксировано в DEVELOPMENT_STATUS.md §2 как явное отступление от §1.4/§6.0 сводной инструкции."),

                Task("P00-T12", "DEC-09 — Трейт «Читает воду»",
                    "В первой главе сначала дать знание/вывод. Трейт выдавать только при подтверждённом применении в следующих главах.",
                    DevelopmentTaskCategory.Decision, DevelopmentTaskStatus.Completed, required: true, order: 12,
                    acceptanceCriteria: new[] { "Зафиксирован явный ответ пользователя (в т. ч. явное решение отложить)" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Пользователь подтвердил рекомендацию без изменений: в Главе 01 трейт не выдаётся, только знание/вывод; трейт возможен не раньше следующих глав при подтверждённом применении.")
            );
        }

        private static DevelopmentPhaseData BuildP01Tracker()
        {
            return Phase(
                "P01_TRACKER", "Экран «Этапы разработки»",
                "Дать личный производственный инструмент: список этапов, галочки, dropdown, поиск/фильтры, progress bar, карточка задачи, validator, переходы в другие окна.",
                1, true,
                "Критерии готовности раздела 4.11 выполнены и пользователь подтвердил работу окна в Unity.",
                null,

                Task("P01-T01", "Модель данных плана",
                    "DevelopmentPlanAsset описывает DevelopmentPhaseData/DevelopmentTaskData/AcceptanceCriterionData, статусы и категории по разделу 4.3, миграцию schemaVersion.",
                    DevelopmentTaskCategory.Data, DevelopmentTaskStatus.Completed, required: true, order: 1,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanAsset.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Сериализуются все поля раздела 4.3 (id/title/details/category/status/required/order/dependencies/acceptanceCriteria/manualChecks/fileReferences/relatedDialogueIds/relatedFlagIds/relatedKnowledgeIds/blockerNote/implementationNote/completedAt/completedCommit/optionalAutoCheckId)",
                        "MigrateIfNeeded поднимает schemaVersion и не падает на null-полях"
                    },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    manualChecks: new[] { "Открыть asset в инспекторе Unity и убедиться, что нет ошибок сериализации" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanAsset.cs",
                    implementationNote: "Подтверждено реальным Unity Test Runner: компиляция чистая, DevelopmentPlanAssetTests (8/8, включая MigrateIfNeeded_FixesNullListsAndBumpsSchemaVersion) зелёные."),

                Task("P01-T02", "Расчёт прогресса",
                    "DevelopmentPlanProgress считает обязательный прогресс отдельно от опционального и исключает Отложено (раздел 4.5).",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Completed, required: true, order: 2,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanProgress.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Обязательный прогресс считается только по required-задачам и не включает Отложено",
                        "Опциональные задачи считаются отдельно и не уменьшают обязательный процент",
                        "«Следующая рекомендуемая задача» учитывает зависимости"
                    },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    manualChecks: new[] { "Прогнать DevelopmentTrackerProgressTests в Test Runner" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanProgress.cs",
                    implementationNote: "Подтверждено реальным Unity Test Runner: DevelopmentTrackerProgressTests 6/6 зелёные."),

                Task("P01-T03", "Validator плана",
                    "DevelopmentPlanValidator проверяет структуру плана по 10 пунктам раздела 4.10.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Completed, required: true, order: 3,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanValidator.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Находит дубликаты ID, отсутствующую зависимость и цикл (прямой и косвенный)",
                        "Требует blockerNote при статусе «Заблокировано»",
                        "Требует критерий приёмки у обязательной задачи",
                        "Предупреждает о выполненных задачах с незакрытыми критериями"
                    },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    manualChecks: new[] { "Прогнать DevelopmentPlanValidatorTests в Test Runner" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanValidator.cs",
                    implementationNote: "Подтверждено реальным Unity Test Runner: DevelopmentPlanValidatorTests 9/9 зелёные (дубликаты, циклы, зависимости, blockerNote и т.д.)."),

                Task("P01-T04", "Автоматические проверки",
                    "DevelopmentPlanAutoChecks подтверждает только объективные факты: существование file:/asset: по optionalAutoCheckId (раздел 4.9).",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanAutoChecks.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Автопроверка file: подтверждает существование файла/папки",
                        "Автопроверка asset: подтверждает существование asset через AssetDatabase",
                        "Результат показывается как доказательство рядом с задачей, но не заменяет ручную галочку"
                    },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanAutoChecks.cs"),

                Task("P01-T05", "Окно: список этапов, поиск и фильтры",
                    "DevelopmentTrackerWindow строит foldout на каждый этап со строками задач внутри, поиск, фильтр статуса/категории, переключатели «Только незавершённые/обязательные/заблокированные».",
                    DevelopmentTaskCategory.UI, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 5,
                    dependencies: new[] { "P01-T01", "P01-T02" },
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentTrackerWindow.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Foldout на каждый этап, строки задач внутри, раскрытие не меняет данные",
                        "Поиск и оба фильтра сужают список без ошибок",
                        "Progress bar и счётчики (заблокировано/нужна проверка/опционально) обновляются сразу"
                    },
                    manualChecks: new[] { "Открыть Kingdom Survival → Этапы разработки и вручную проверить фильтры" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentTrackerWindow.cs"),

                Task("P01-T06", "Галочка, статус-dropdown и карточка задачи",
                    "Галочка переключает Completed ⇄ InProgress по правилу 4.4; статус доступен отдельным dropdown; критерии приёмки — локальные чекбоксы; поля ссылок/blockerNote/implementationNote/коммита редактируемы.",
                    DevelopmentTaskCategory.UI, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 6,
                    dependencies: new[] { "P01-T05" },
                    acceptanceCriteria: new[]
                    {
                        "Установка галочки переводит задачу в «Выполнено», записывает дату и предлагает указать коммит",
                        "Снятие галочки переводит задачу в «В работе», а не в «Не начато»",
                        "Незавершённая обязательная зависимость при попытке завершить задачу показывает предупреждение"
                    },
                    manualChecks: new[] { "Вручную отметить и снять несколько задач, проверить сохранение после перезапуска Unity" }),

                Task("P01-T07", "Диагностическая панель",
                    "Нижняя панель окна показывает ошибки/предупреждения validator отдельно и список задач «Нужна проверка в Unity».",
                    DevelopmentTaskCategory.UI, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 7,
                    dependencies: new[] { "P01-T03" },
                    acceptanceCriteria: new[]
                    {
                        "Кнопка «Проверить план» запускает validator и показывает результат",
                        "Отдельно виден список задач со статусом «Нужна проверка в Unity»"
                    }),

                Task("P01-T08", "Переходы в другие окна",
                    "DevelopmentTrackerMenuCommands открывает «База диалогов», «База существ» и «UI Конструктор» через EditorApplication.ExecuteMenuItem без прямых asmdef-ссылок.",
                    DevelopmentTaskCategory.Integration, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 8,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/DevelopmentTrackerMenuCommands.cs" },
                    acceptanceCriteria: new[] { "Каждая кнопка открывает целевое окно; DevelopmentTracker.Editor компилируется без ссылок на чужие Editor-сборки" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentTrackerMenuCommands.cs"),

                Task("P01-T09", "Editor-only asmdef",
                    "KingdomSurvival.DevelopmentTracker.Editor.asmdef ссылается только на UnityEditor/UI Toolkit; рантайм-билд не получает ссылку на модуль.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Completed, required: true, order: 9,
                    fileReferences: new[] { "Assets/_Project/DevelopmentTracker/Editor/KingdomSurvival.DevelopmentTracker.Editor.asmdef" },
                    acceptanceCriteria: new[] { "includePlatforms = [Editor]", "Нет циклической ссылки с Dialogue/Unit Database" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/KingdomSurvival.DevelopmentTracker.Editor.asmdef",
                    implementationNote: "Подтверждено: чистая Unity-компиляция, никаких ошибок circular reference."),

                Task("P01-T10", "Начальное заполнение плана",
                    "DevelopmentPlanSeedData строит P00–P16 по инструкции; DevelopmentPlanBootstrap создаёт и заполняет asset при первом Unity-запуске после pull, если его ещё нет.",
                    DevelopmentTaskCategory.Data, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 10,
                    fileReferences: new[]
                    {
                        "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanSeedData.cs",
                        "Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanBootstrap.cs"
                    },
                    acceptanceCriteria: new[]
                    {
                        "После первого успешного compile в Unity asset существует и содержит все этапы P00–P16",
                        "Повторный запуск бутстрапа не перезаписывает существующий план",
                        "Есть ручной пункт меню для осознанного пересоздания плана с подтверждением"
                    },
                    manualChecks: new[] { "Удалить asset, перезапустить Unity, убедиться что план создался автоматически" },
                    optionalAutoCheckId: "file:Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanSeedData.cs"),

                Task("P01-T11", "EditMode-тесты экрана",
                    "Тесты сериализации, расчёта прогресса и validator по разделу 19.1.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.Completed, required: true, order: 11,
                    dependencies: new[] { "P01-T01", "P01-T02", "P01-T03" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/Tests/EditMode/DevelopmentPlanAssetTests.cs",
                        "Assets/_Project/Tests/EditMode/DevelopmentPlanValidatorTests.cs",
                        "Assets/_Project/Tests/EditMode/DevelopmentTrackerProgressTests.cs"
                    },
                    acceptanceCriteria: new[] { "Все тесты раздела 19.1 присутствуют и зелёные в Test Runner" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    manualChecks: new[] { "Запустить Window → General → Test Runner → EditMode" },
                    optionalAutoCheckId: "file:Assets/_Project/Tests/EditMode/DevelopmentPlanAssetTests.cs",
                    implementationNote: "Пользователь прислал TestResults.xml реального Unity Test Runner: все 23 теста (8+9+6) зелёные, 0 падений."),

                Task("P01-T12", "UI-настройки в EditorPrefs",
                    "Раскрытые foldout, ширины колонок и активные фильтры хранятся в EditorPrefs, а не сериализуются в asset (раздел 4.8).",
                    DevelopmentTaskCategory.UI, DevelopmentTaskStatus.NeedsUnityCheck, required: false, order: 12,
                    dependencies: new[] { "P01-T05" },
                    acceptanceCriteria: new[] { "Состояние foldout переживает перезапуск окна через EditorPrefs" })
            );
        }

        private static DevelopmentPhaseData BuildP02Baseline()
        {
            return Phase(
                "P02_BASELINE", "Проверка текущего технического фундамента",
                "После pull дождаться чистой компиляции, прогнать все EditMode tests и вручную проверить карту, время, экспедицию, Hero Screen, базы данных, prototype_miller и Road Predator, прежде чем строить Главу 01 поверх них.",
                2, true,
                "Нет ошибок компиляции; тесты зелёные; ручной smoke test записан в DEVELOPMENT_STATUS.md; все задачи P02 либо выполнены, либо имеют конкретный blockerNote.",
                new[] { "P01_TRACKER" },

                Task("P02-T01", "Чистая компиляция после pull",
                    "Открыть проект в Unity 6000.5.10f1 и дождаться Domain Reload без ошибок в Console.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.Completed, required: true, order: 1,
                    acceptanceCriteria: new[] { "Console не содержит ошибок компиляции" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "Подтверждено пользователем: TestResults.xml от реального Unity Test Runner содержит 213 тест-кейсов — компиляция чистая (иначе Test Runner не собрал бы сборки)."),

                Task("P02-T02", "Все EditMode tests зелёные",
                    "Test Runner → EditMode должен пройти без красных тестов на текущем main.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    fileReferences: new[] { "Assets/_Project/Tests/EditMode" },
                    acceptanceCriteria: new[] { "Test Runner → EditMode: все тесты проходят" },
                    implementationNote: "211/213 зелёные (пользователь прислал TestResults.xml), оба падения были в тестах, а не в проверяемом коде — production-код не менялся. " +
                        "(1) BattleSandboxTests.GuardAddsFiftyPercentDefenseUntilNextActivation: Expected 16, was 18. Формула боя (SandboxCombatTagRules.GetEffectiveDefense/BuildAttackPreview) даёт 18 и это совпадает с пятью другими проходящими тестами того же файла (CombatTagModifierTests — Guard_UsesFiftyPercentDefenseBonusRoundedDown и соседние, тот же паттерн floor(defense*0.5) с минимумом 1), т.е. production-формула самосогласована и верна. Сам тест содержал ошибочно посчитанное ожидание — 16 недостижимо этой формулой ни при каком целочисленном floor. Исправлено ожидание на 18 с комментарием, объясняющим расчёт. " +
                        "(2) DialogueDatabaseCheckSystemTests.RegenerateNarrativeIdentifiers_Creates_New_Ids_And_Remaps_Unlock_Reference: NullReferenceException. Стек-трейс вёл на строку самого теста (сравнение originalChoice.Check.CheckId/copyChoice.Check.CheckId), а не в RegenerateNarrativeIdentifiers. Причина: тест искал продублированный выбор по старому ChoiceId ('c_returnable'), но RegenerateNarrativeIdentifiers намеренно регенерирует и ChoiceId тоже (см. §13 в комментарии к методу) — после регенерации выбора с таким ChoiceId в копии больше нет, FindChoiceById возвращал null, а обращение к null.Check роняло NRE. Метод в DialogueDatabaseWindow.Editing.cs не трогался. Тест переписан: копия ищется по позиции (индекс узла + индекс выбора в узле), которая не меняется при дублировании, вместо старого ChoiceId. " +
                        "Обе правки — только в тестовых файлах, без Unity здесь прогнать нельзя: нужно подтверждение пользователя реальным Test Runner."),

                Task("P02-T03", "Карта, время, экспедиция в Prototype_Main",
                    "Открыть сцену, выйти в экспедицию и вернуться без ошибок и рассинхронизации состояния.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    fileReferences: new[] { "Assets/_Project/Scenes/Prototype_Main.unity", "Assets/_Project/Scripts/Core/GameState.cs" },
                    acceptanceCriteria: new[] { "Экспедиция запускается и завершается без ошибок консоли" }),

                Task("P02-T04", "Экран героя, база существ, база диалогов",
                    "Открыть все три редакторских/игровых экрана и убедиться, что они показывают текущие данные.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    fileReferences: new[]
                    {
                        "Assets/_Project/UI/PrototypeUIController.HeroScreen.cs",
                        "Assets/_Project/UnitDatabase/Editor/UnitDatabaseWindow.cs",
                        "Assets/_Project/DialogueDatabase/Editor/DialogueDatabaseWindow.cs"
                    },
                    acceptanceCriteria: new[] { "Все три окна открываются и работают без ошибок" }),

                Task("P02-T05", "prototype_miller: пассивная/возвратимая/решающая проверки",
                    "Пройти техническую витрину и убедиться, что все три типа проверки отрабатывают корректно.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 5,
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Runtime/DialogueDatabaseAsset.cs" },
                    acceptanceCriteria: new[] { "Пассивная, активная возвратимая и активная решающая проверки проходят без ошибок" }),

                Task("P02-T06", "Road Predator Encounter",
                    "Проверить дорожную встречу-хищника через ExpeditionIncidentSystem.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.Completed, required: true, order: 6,
                    fileReferences: new[] { "Assets/_Project/Scripts/Core/ExpeditionIncidentSystem.cs" },
                    acceptanceCriteria: new[] { "Encounter запускается и завершается без ошибок" },
                    acceptanceCriteriaDone: true,
                    completedAt: "2026-09-09",
                    implementationNote: "RoadPredatorEncounterTests 5/5 зелёные в реальном Unity Test Runner."),

                Task("P02-T07", "Записать smoke test в DEVELOPMENT_STATUS.md",
                    "Зафиксировать дату и результат ручной проверки P02 в техническом журнале.",
                    DevelopmentTaskCategory.Documentation, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 7,
                    dependencies: new[] { "P02-T01", "P02-T02", "P02-T03", "P02-T04", "P02-T05", "P02-T06" },
                    fileReferences: new[] { "ProjectDocs/DEVELOPMENT_STATUS.md" },
                    acceptanceCriteria: new[] { "В журнале есть запись с датой и результатом ручной проверки P02" },
                    implementationNote: "Частичная запись внесена (компиляция + результаты Test Runner из TestResults.xml). Полное закрытие ждёт P02-T02 (решение по 2 предсуществующим падениям) и ручной проверки P02-T03/T04/T05 (карта/время/экспедиция, три окна, ручной проход prototype_miller — автотесты этого не подтверждают напрямую).")
            );
        }

        private static DevelopmentPhaseData BuildP03Scaffold()
        {
            return Phase(
                "P03_CHAPTER01_SCAFFOLD", "Каркас Главы 01 и реестр ID",
                "Создать Chapter01Ids, узкий Chapter01StoryDirector (без универсального QuestManager), определить переходы N01–N17 и связать их с TryOpenNarrativeDialogueById, обеспечить идемпотентность.",
                3, true,
                "Пустые/черновые диалоги позволяют пройти весь граф от N01 до N17 в обеих ветках ремонта без тупика и без телепортации.",
                new[] { "P02_BASELINE" },

                Task("P03-T01", "Chapter01Ids — реестр стабильных ID",
                    "Единый реестр ID по шаблонам раздела 7 (узел/диалог/флаг/знание/проверка/встреча/предмет/эффект) с валидатором пустых и дублирующихся ID.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 1,
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01Ids.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Валидатор запрещает пустые и дублирующиеся ID",
                        "Все шаблоны раздела 7 представлены константами/билдерами"
                    },
                    manualChecks: new[] { "Прогнать Chapter01StateTests в Unity" },
                    implementationNote: "Реализовано: Nodes/Dialogues/Flags/Knowledge/Checks/Items/Effects + ValidateRegistry(). Плюс 3 технических флага завершения узла сверх минимального списка (HousePeopleMet/LongRoadStarted/ReturnRoadTraveled — у N02/N11/N15 иначе нет отдельного флага для StoryDirector). Компиляция и тесты не запускались."),

                Task("P03-T02", "Chapter01StoryDirector",
                    "Узкий контроллер: читает NarrativeState/GameState, не хранит вторую копию прогресса, не телепортирует отряд, гарантирует идемпотентность узла.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    dependencies: new[] { "P03-T01" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Повторный запуск не повторяет завершённый узел",
                        "Определяет доступный следующий узел по флагам NarrativeState"
                    },
                    manualChecks: new[] { "Пройти граф вручную дважды подряд без перезапуска и без дублирования эффектов" },
                    implementationNote: "Реализовано как черновой линейный каркас (GetNextDialogueId/GetNextNodeId/GetRepairChoice/TryAdvance) поверх статической таблицы из 20 шагов; свободный порядок N07A/B/C и осмысленные комбинации знаний для N09 намеренно оставлены P07-T05/P08-T02, не меняя публичный контракт. TryAdvance принимает делегат-открыватель, не ссылается на PrototypeUIController напрямую — реальная точка вызова из живого UI ещё не подключена. Проверено симуляцией на Python (полный обход графа реальных диалогов N01→N17 в обеих ветках ремонта, без тупиков), но не реальными EditMode-тестами в Unity."),

                Task("P03-T03", "Chapter01OutcomeApplier",
                    "Одноразовые внешние эффекты (ресурсы/время/повреждения/открытия карты) со стабильным execution ID.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    dependencies: new[] { "P03-T01" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01OutcomeApplier.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Каждый внешний эффект применяется не более одного раза по execution ID",
                        "Повторный показ диалога не удваивает эффект"
                    },
                    manualChecks: new[] { "Сохраниться/загрузиться после применения эффекта и убедиться, что он не повторился" },
                    implementationNote: "Реализовано поверх уже существующего механизма NarrativeStateData.AppliedEffectExecutionIds (не отдельная система идемпотентности). ApplyResourceDelta/ApplyTimeAdvance/GrantItem/MarkHeroInjured — травма героя как флаг, без нового поля HP (раздел 6.4). Открытие карты/маршрута не реализовано — понадобится не раньше P09."),

                Task("P03-T04", "Chapter01ContextBuilder",
                    "Передаёт реальный состав экспедиции, предметы и контекстные модификаторы; отсутствие спутника не блокирует обязательный переход.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    dependencies: new[] { "P03-T01" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01ContextBuilder.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Передаёт реальный состав отряда и предметы из GameState",
                        "Отсутствие конкретного спутника не блокирует обязательный переход"
                    },
                    implementationNote: "Build/GetPresentCompanionIds/GetPresentItemIds/GetPartySize. Потребовалась точечная правка вне Chapter01: NarrativeStateData получил минимальный список Items (HasItem/GrantItem/RemoveItem), а TryOpenNarrativeDialogueById (PrototypeUIController.Narrative.cs) стал передавать его вместо жёстко зашитого null — раньше ItemPresent в реальной игре никогда не мог быть true."),

                Task("P03-T05", "Черновой граф N01–N17",
                    "Черновые диалоги/переходы для всех 17 узлов, связанные через TryOpenNarrativeDialogueById.",
                    DevelopmentTaskCategory.Data, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 5,
                    dependencies: new[] { "P03-T02" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    acceptanceCriteria: new[]
                    {
                        "Черновой граф проходим от N01 до N17 в обеих ветках ремонта",
                        "Нет тупиков и телепортации по физической карте"
                    },
                    manualChecks: new[] { "Открыть Kingdom Survival → База диалогов, проверить все 20 chapter01_dialogue_* в Графе/Preview, прогнать встроенную валидацию" },
                    implementationNote: "20 диалогов (N01–N17, включая N07A/B/C и N14½) добавлены в KingdomSurvivalDialogues.asset вместе с 7 новыми говорящими (Остафий/Лада/Мирон/Ульяна/рассказчик/человек с низовья/свидетель у брода — рабочие имена DEC-02/DEC-10). Каждый узел — один textBlock с onRevealEffects (SetFlag/AddKnowledge) и один Exit-выбор; branch-диалоги N05 и N14½ — два дочерних узла с эффектами на конкретную ветку. Effects на самом выборе (successEffects), а не на textBlock, НЕ сработали бы для Normal/Exit choice — в runtime они применяются только для ActiveCheck choice (SelectChoiceInternal), это не сразу очевидно из схемы. Текст — короткий черновик, не полированная проза (раздел 22 инструкции: полировка позже). Полный обход N01→N17 в обеих ветках ремонта и гарантированная выдача shared_water_system на N14 подтверждены Python-симуляцией самого YAML (не Unity Preview)."),

                Task("P03-T06", "Тесты переходов ремонта",
                    "EditMode-тесты для инвариантов раздела 19.2: repair_old/repair_new не одновременно, обе ветки ведут к N07.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 6,
                    dependencies: new[] { "P03-T02", "P03-T05" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Tests/EditMode/Chapter01StateTests.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Тест подтверждает взаимоисключение repair_old/repair_new",
                        "Тест подтверждает, что обе ветки доходят до N07"
                    },
                    implementationNote: "17 тестов: реестр ID, идемпотентность/продвижение GetNextDialogueId, обе ветки ремонта ведут к N07A, взаимоисключение repair_old/repair_new (GetRepairChoice бросает исключение при одновременной установке), TryAdvance, ContextBuilder без спутников, идемпотентность всех методов OutcomeApplier. Отдельный asmdef KingdomSurvival.Chapter01.EditModeTests рядом с модулем (доп. ссылка в общий Core.EditModeTests не добавлялась)."),

                Task("P03-T07", "Зарегистрировать компетенцию «Ремесло»",
                    "По решению DEC-08 (отступление от рекомендации инструкции, зафиксировано в DEVELOPMENT_STATUS.md §2): зарегистрировать новую нарративную компетенцию «Ремесло» рядом с существующим Следопытством, без изменения других качеств/компетенций/трейтов.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 7,
                    dependencies: new[] { "P00-T11" },
                    fileReferences: new[] { "Assets/_Project/Scripts/Core/HeroProfile.cs" },
                    acceptanceCriteria: new[]
                    {
                        "Компетенция «Ремесло» зарегистрирована с диапазоном 0–5, как Следопытство",
                        "Существующие качества, компетенции и трейты не изменены",
                        "N08 («Семь зубцов», P07-T04) может использовать «Ремесло» как одну из проверок толкования предмета"
                    },
                    manualChecks: new[] { "Прогнать HeroCombatStatsBuilderTests и связанные EditMode-тесты в Unity" },
                    implementationNote: "NarrativeCompetencyIds.Craft = \"craft\" добавлена в HeroProfile.cs (Known-список), подпись «Ремесло» — в NarrativeDisplayLabels.cs. Диапазон 0–5 общий для всех компетенций (NarrativeCheckMath.ClampCompetency), отдельно не настраивается. N08 (P07-T04) обновлена: указывает «Ремесло» в тексте задачи и зависит от P03-T07.")
            );
        }

        private static DevelopmentPhaseData BuildP04Morning()
        {
            return Phase(
                "P04_MORNING", "Обычное утро и люди Дома",
                "Реализовать N01–N03: норму Дома, 3–4 повторяющихся жителя через дело и противоречие, малый выбор с локальным следом, посев мотива второго хлеба и воды без объясняющей лекции.",
                4, true,
                "Игрок понимает, что именно является нормой, кого он знает и что может потерять.",
                new[] { "P03_CHAPTER01_SCAFFOLD" },

                Task("P04-T01", "N01 «Обычное утро / Лишний хлеб»",
                    "Intro, Narration, бытовой выбор; пассивное Суждение 11 может дать раннее знание о втором хлебе.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    relatedFlagIds: new[] { "chapter01.flag.started", "chapter01.flag.home_intro_seen" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.second_loaf_is_ration" },
                    acceptanceCriteria: new[] { "Игрок узнаёт норму Дома и мотив второго хлеба без объясняющей лекции" }),

                Task("P04-T02", "N02 «Люди Дома»",
                    "Представить 3–4 повторяющихся человека через дела и противоречия, не через справку.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P00-T09" },
                    acceptanceCriteria: new[] { "Каждый персонаж имеет функцию, желание и несогласие, а не только справку" }),

                Task("P04-T03", "N03 «Первое давление»",
                    "Малый сбой (уровень/шум/задержка воды/бытовой конфликт) как наблюдение и маленький выбор с поздним эхом.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    relatedFlagIds: new[] { "chapter01.flag.first_pressure_seen" },
                    acceptanceCriteria: new[] { "Тревога нарастает из знакомой нормы, не из внешнего объявления" }),

                Task("P04-T04", "Зафиксировать визуальное состояние Дома для рифмы N16",
                    "Сохранить исходное состояние сцены (звук, вода, мельница, скот, настил), чтобы N16 могло показать минимум три изменения.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    manualChecks: new[] { "Сверить список зафиксированных мотивов со списком раздела 18" },
                    acceptanceCriteria: new[] { "Состояние Дома на входе в N01 зафиксировано и доступно для сравнения в N16" },
                    implementationNote:
                        "Создан Chapter01HomeState.cs (Assets/_Project/Chapter01/Runtime/) — типизированный resolver пяти мотивов " +
                        "(Water/Mill/Livestock/Walkway/Sound), вычисляемых из уже существующих Chapter01Ids.Flags " +
                        "(FloodHappened/FloodLivestockLost/FloodMillDeckDestroyed/RepairOld/RepairNew/RepairCompleted/WaterWrongActive). " +
                        "Отдельная settlement simulation не введена: NarrativeStateData остаётся единственным источником истины, " +
                        "Chapter01HomeState только интерпретирует флаги по запросу (ResolveCurrent), ничего не мутирует. " +
                        "Добавлен один новый persistent-флаг chapter01.flag.home_baseline_captured (Chapter01Ids.Flags), " +
                        "выставляется onRevealEffect на первом обязательном блоке N01 независимо от исхода пассивного Суждения 11. " +
                        "В N01 добавлен один короткий Observation-блок (спокойный скот у водопоя, привычная вода) — без изменения " +
                        "существующей драматургии Ульяны/Остафия/Лады/Мирона. Тесты: Chapter01HomeStateTests.cs (14 тестов — baseline, " +
                        "приоритеты Wrong/Lost, обе ветви ремонта различимы и не равны RunningNormally/OldIntact, JSON-сериализация " +
                        "NarrativeStateData через JsonUtility, registry, >=3 изменённых мотива для обеих веток ремонта) плюс три новых " +
                        "теста в Chapter01FourResidentsTests.cs на фиксацию baseline при открытии N01 независимо от успеха/провала " +
                        "проверки. Unity Editor и Test Runner в среде недоступны — тесты не прогонялись реально."),

                Task("P04-T05", "Функции жителей Дома в диалогах",
                    "Хранитель старого порядка, молодой мастер, мельник и бытовой голос — у каждого выгода/страх/правда/слепое место (раздел 16.1).",
                    DevelopmentTaskCategory.Narrative, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 5,
                    dependencies: new[] { "P00-T09" },
                    acceptanceCriteria: new[] { "У каждой функции описаны выгода от воды, страх после паводка, правда и слепое место" },
                    implementationNote:
                        "Паспорта Остафия/Лады/Мирона/Ульяны — NARRATIVE.md §26.1.3 (выгода/страх/правда/слепое место/отношения " +
                        "друг к другу для каждого, таблица распределения знаний). Production-текст N01–N03 заменил черновые " +
                        "заглушки P03 в KingdomSurvivalDialogues.asset. EditMode-тесты — Chapter01FourResidentsTests.cs. " +
                        "Unity Editor и Test Runner в среде недоступны — тесты не прогонялись реально.")
            );
        }

        private static DevelopmentPhaseData BuildP05Flood()
        {
            return Phase(
                "P05_FLOOD", "Паводок",
                "Реализовать N04 как срочную игровую сцену: решающая проверка Силы/Ловкости/Стойкости, гарантированный тяжёлый выбор после неё, одноразовые последствия, продолжение при любом провале.",
                5, true,
                "Минимум четыре различимых исхода видимо меняют ближайшие сцены, но каждый приводит к осмотру плотины (N05).",
                new[] { "P04_MORNING" },

                Task("P05-T01", "N04 «Синяя ставня» — решающее действие",
                    "Сила, Ловкость или Стойкость определяют способ действия, сложность 13; все исходы ведут к N05.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 1,
                    relatedFlagIds: new[] { "chapter01.flag.flood_happened" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_04_flood" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "Открыть N04 в Preview/Debug Narrative, пройти Силу/Ловкость/Стойкость на успех и на провал" },
                    acceptanceCriteria: new[] { "Проверка сложности 13 на Силу/Ловкость/Стойкость реализована", "Любой исход ведёт к N05" },
                    implementationNote: "Стартовый узел N04 содержит три ActiveDecisive-проверки chapter01.check.flood_response (Сила/Ловкость/Стойкость, сложность 13), FloodHappened больше не ставится при открытии сцены — только в шести конечных узлах. Проверено локально Python-парсером структуры YAML (граф без orphan-узлов, все узлы достигают Exit) — настоящий Unity Test Runner в этой среде недоступен."),

                Task("P05-T02", "Гарантированный тяжёлый выбор после проверки",
                    "Успех проверки определяет доступную позицию, а не спасает всё; отдельный выбор приоритета (кого/что спасать).",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    dependencies: new[] { "P05-T01" },
                    manualChecks: new[] { "Убедиться, что после успеха и после провала виден один и тот же тяжёлый выбор (люди/скот/мельница)" },
                    acceptanceCriteria: new[] { "Успех проверки не отменяет цену катастрофы: есть отдельный обязательный выбор" },
                    implementationNote: "И успех, и провал каждой из трёх проверок ведут к отдельному узлу тяжёлого выбора (priority_success/priority_failure) с тремя одинаковыми по смыслу приоритетами — люди/скот/мельница; эффекты применяются только в конечных узлах через onRevealEffects, а не на самом выборе."),

                Task("P05-T03", "Применение последствий паводка",
                    "Chapter01OutcomeApplier применяет ресурсы/состояние мельницы/скот/флаг травмы по итогам выбора.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    dependencies: new[] { "P03-T03", "P05-T02" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/Chapter01/Runtime/Chapter01OutcomeApplier.cs",
                        "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs",
                        "Assets/_Project/UI/PrototypeUIController.Narrative.cs"
                    },
                    relatedFlagIds: new[]
                    {
                        "chapter01.flag.flood_workers_saved", "chapter01.flag.flood_livestock_lost",
                        "chapter01.flag.flood_mill_deck_destroyed", "chapter01.flag.hero_injured_by_flood"
                    },
                    manualChecks: new[] { "Пройти все шесть исходов вручную и проверить, что ресурсы/флаги применяются один раз" },
                    acceptanceCriteria: new[] { "Минимум четыре различимых исхода видимо меняют ближайшие сцены" },
                    implementationNote: "Chapter01OutcomeApplier.ApplyFloodConsequences читает FloodLivestockLost и списывает Food на 12 через существующий защищённый-от-повтора ApplyResourceDelta (chapter01.effect.flood_resource_loss). Травма героя и повреждение настила остаются нарративными флагами без отдельного системного эффекта. Chapter01StoryDirector.HandleDialogueCompleted(gameState, dialogueId) — узкий hook, вызывающий ApplyFloodConsequences только для D04; подключён в PrototypeUIController.Narrative.cs перед CloseNarrativeDialogue()."),

                Task("P05-T04", "Failure-forward для всех провалов",
                    "Проверить, что каждый вариант провала продолжает сюжет с ценой, а не блокирует его.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    dependencies: new[] { "P05-T03" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Tests/EditMode/Chapter01FloodTests.cs" },
                    manualChecks: new[] { "Запустить Test Runner → EditMode → Run All" },
                    acceptanceCriteria: new[] { "Каждый вариант провала N04 всё равно приводит к N05" },
                    implementationNote: "Chapter01FloodTests.cs добавлен: структура трёх решающих проверок, все шесть исходов (успех/провал × люди/скот/мельница) проверяют точную комбинацию флагов и то, что Chapter01StoryDirector.GetNextDialogueId после каждого исхода возвращает D05, плюс идемпотентность ApplyFloodConsequences и связь с Chapter01HomeState. Тесты написаны против настоящего KingdomSurvivalDialogues.asset (Resources.Load), не синтетической копии. Не запускались настоящим Unity Test Runner в этой среде — компиляция и Test Runner здесь недоступны.")
            );
        }

        private static DevelopmentPhaseData BuildP06RepairChoice()
        {
            return Phase(
                "P06_REPAIR_CHOICE", "Осмотр плотины и выбор ремонта",
                "Реализовать N05 «Мокрый чертёж» (выбор ремонта без проверки, ровно один взаимоисключающий флаг) и N06 (матрица первых симптомов по ветке).",
                6, true,
                "Оба ремонта действительно устраняют местную аварию, но создают разные наблюдаемые последствия.",
                new[] { "P05_FLOOD", "P00_DECISIONS" },

                Task("P06-T01", "N05 «Мокрый чертёж»",
                    "Старое и новое решение показаны через позиции людей и физическую схему; выбор без проверки; ровно один флаг ремонта.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 1,
                    dependencies: new[] { "P00-T01", "P00-T02" },
                    relatedFlagIds: new[] { "chapter01.flag.dam_inspected", "chapter01.flag.repair_old", "chapter01.flag.repair_new" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_05_wet_plan" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "Открыть N05 в Preview, пройти старую и новую ветку, проверить, что нет проверок и ставится ровно один флаг ремонта" },
                    acceptanceCriteria: new[]
                    {
                        "Ровно один из repair_old/repair_new записывается",
                        "Выбор не сопровождается броском проверки"
                    },
                    implementationNote: "N05 переписан с P03-каркаса на production-текст: осмотр после паводка (DamInspected ставится на первом блоке — осмотр уже произошёл сам по себе), условные эхо-блоки P05 (FloodMillDeckDestroyed/FloodLivestockLost/HeroInjuredByFlood), позиции Мирона/Остафия/Лады/Ульяны, два обычных (не Active) выбора. RepairOld/RepairNew ставятся на первом блоке соответствующей итоговой ветки; RepairCompleted в N05 не ставится — это граница с N06. Позже переструктурирован на presentation-правило «одна реплика = один шаг» (30 узлов, новый DialogueChoiceKind.Continue между ними) — раздел 16 DEVELOPMENT_STATUS.md."),

                Task("P06-T02", "N06 — матрица первых симптомов",
                    "Симптомы различаются по ветке ремонта согласно матрице раздела 6 инструкции P06.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    dependencies: new[] { "P06-T01" },
                    relatedFlagIds: new[] { "chapter01.flag.water_wrong_active", "chapter01.flag.repair_completed" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.water_flow_is_wrong" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_06_wrong_water" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "В Preview N06 подготовить RepairOld и RepairNew по отдельности, убедиться, что видна только своя ветка симптомов" },
                    acceptanceCriteria: new[] { "Обе ветки локально успешны, но дают наблюдаемо разные симптомы", "Обе ветки ведут к расследованию N07" },
                    implementationNote: "Один диалог D06 (не два), одна общая вступительная реплика — старая ветка: ночные толчки мельничного колеса без видимой причины; новая ветка: высохший седьмой рукав, встревоженный скот, рыба в лужах. RepairCompleted ставится на первом узле выбранной ветки, WaterWrongActive и знание water_flow_is_wrong — на последнем. Знания расследования P07 (mill_moves_at_wrong_time и т.д.) сознательно не выдаются. Переструктурирован на presentation-правило «одна реплика = один шаг» (23 узла): ветвление RepairOld/RepairNew решается двумя условными DialogueChoiceKind.Continue-выборами на стартовом узле (Negate противоположного флага защищает от конфликтного состояния), а не условными textBlocks внутри одного узла — раздел 16 DEVELOPMENT_STATUS.md."),

                Task("P06-T03", "Тест взаимоисключения ремонта",
                    "EditMode-тест: repair_old и repair_new не могут быть установлены одновременно.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    dependencies: new[] { "P06-T01" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/Chapter01/Tests/EditMode/Chapter01StateTests.cs",
                        "Assets/_Project/Chapter01/Tests/EditMode/Chapter01P06Tests.cs"
                    },
                    manualChecks: new[] { "Запустить Test Runner → EditMode → Run All" },
                    acceptanceCriteria: new[] { "Тест подтверждает взаимоисключение флагов ремонта" },
                    implementationNote: "Chapter01P06Tests.cs добавлен: структура и валидация D05/D06, отсутствие активных проверок в D05, ровно один флаг ремонта на реальном пути через обе ветки D05, видимость только своей ветки симптомов в D06 (по BlockId), progression D05→D06→D07A через Chapter01StoryDirector, и GetRepairChoice на всех четырёх состояниях (Old/New/None/оба флага → InvalidOperationException). Не запускались настоящим Unity Test Runner в этой среде.")
            );
        }

        private static DevelopmentPhaseData BuildP07Investigation()
        {
            return Phase(
                "P07_INVESTIGATION", "Последствия ремонта и расследование",
                "Реализовать N07A/N07B/N07C в свободном порядке и N08 как материальный след старой системы; каждый обязательный вывод — минимум два источника; без счётчика улик.",
                7, true,
                "Любой допустимый порядок расследования приводит к обоснованному решению идти дальше, дополнительные ветви меняют реплики и подготовку.",
                new[] { "P06_REPAIR_CHOICE" },

                Task("P07-T01", "N07A «Колесо, которое не спит»",
                    "Мельница как человеческое свидетельство наблюдения Мирона — материальный факт (колесо движется без причины), не пассивная проверка.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 1,
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.mill_moves_at_wrong_time", "chapter01.knowledge.old_seventh_channel" },
                    relatedFlagIds: new[] { "chapter01.flag.investigated_mill" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_07a_mill" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "Открыть N07A в Preview, пройти цепочку Continue до конца" },
                    acceptanceCriteria: new[] { "Знание доступно однозначно — сцена сама показывает факт, без проверки на провал" },
                    implementationNote: "N07A переписан с P03-каркаса на 8 узлов по правилу «одна реплика = один шаг» (DialogueChoiceKind.Continue). InvestigatedMill ставится на наблюдении за колесом, OldSeventhChannel и MillMovesAtWrongTime — на двух отдельных финальных репликах Мирона. Без passive-проверки и без «ученика» — реализовано как прямое материальное наблюдение, не как риск провала."),

                Task("P07-T02", "N07B — скот",
                    "Поведение животных как физический индикатор старого русла; Ульяна ведёт сцену как голос бытовой цены.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.cattle_avoid_old_branch" },
                    relatedFlagIds: new[] { "chapter01.flag.investigated_cattle" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_07b_cattle" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "Открыть N07B в Preview, пройти цепочку Continue до конца" },
                    acceptanceCriteria: new[] { "Ветка даёт самостоятельный вывод, не дублирует мельницу" },
                    implementationNote: "N07B переписан на 8 узлов по правилу «одна реплика = один шаг». InvestigatedCattle ставится на наблюдении за поведением скота у старого водопоя, CattleAvoidOldBranch — на финальной реплике Ульяны."),

                Task("P07-T03", "N07C — река и рыба",
                    "Изменившаяся гидрология и след ниже по течению; реактивные варианты реплик по ветке ремонта (старый/новый), без отдельного диалога на ветку.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.fish_pattern_changed" },
                    relatedFlagIds: new[] { "chapter01.flag.investigated_river" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_07c_river" },
                    fileReferences: new[] { "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset" },
                    manualChecks: new[] { "В Preview подготовить RepairOld и RepairNew по отдельности, убедиться, что видна только своя ветка реплик" },
                    acceptanceCriteria: new[] { "Игрок получает направление без квестовой стрелки из воздуха" },
                    implementationNote: "N07C — 7 узлов; единственное осознанное исключение из правила «одна реплика = один шаг» в P07: три узла (branch/fish/lada) несут по два взаимоисключающих textBlock для старой/новой ветки ремонта вместо отдельных Continue-узлов на ветку — сохраняет один диалог вместо двух. InvestigatedRiver ставится на отдельном узле до ветвления, чтобы не задваивать EffectExecutionId между old/new. chapter01.knowledge.old_ford сюда не относится (используется позже, не в river-расследовании) — убран из связанных знаний как устаревшая ссылка исходного планирования."),

                Task("P07-T04", "N08 «Семь зубцов»",
                    "Материальная связь нынешнего сбоя с прежней системой через найденный предмет; Суждение + «Ремесло» (DEC-08).",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    dependencies: new[] { "P00-T05", "P03-T07" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.seven_tooth_object", "chapter01.knowledge.old_custom" },
                    relatedFlagIds: new[] { "chapter01.flag.old_trace_found" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_08_seven_teeth" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset",
                        "Assets/_Project/Chapter01/Runtime/Chapter01OutcomeApplier.cs",
                        "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs"
                    },
                    manualChecks: new[] { "В Preview пройти успех и провал проверки chapter01.check.seven_tooth_object, оба обязательных вывода" },
                    acceptanceCriteria: new[]
                    {
                        "Предмет имеет практическую функцию и допускает ошибочное толкование",
                        "Позднее переосмысление не стирает раннюю реакцию"
                    },
                    implementationNote: "N08 переписан на 18 узлов. Первая в игре настоящая активная проверка Craft (chapter01.check.seven_tooth_object, ActiveDecisive, Суждение+Ремесло, сложность 13): успех даёт knowledge.seven_tooth_object, провал не блокирует историю и не отменяет находку — сам gauge (chapter01.item.seven_tooth_gauge) выдаётся отдельно через Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences по chapter01.flag.old_trace_found, независимо от исхода проверки. Два обязательных вывода — обычные Normal choice с Conditions на двух независимых источниках каждый (не счётчик улик): вывод A требует OldSeventhChannel+OldTraceFound, вывод B — CattleAvoidOldBranch+FishPatternChanged. Открытие N08 после всех трёх расследований обеспечивает Chapter01StoryDirector (P07-T05), не сам диалог."),

                Task("P07-T05", "Свободный порядок без счётчика улик",
                    "N07A/B/C доступны в любом порядке; N08 открывается только когда все три завершены — через конкретные флаги, не через число.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 5,
                    dependencies: new[] { "P07-T01", "P07-T02", "P07-T03" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs" },
                    manualChecks: new[] { "Пройти все допустимые порядки A→B→C/B→C→A/C→A→B и убедиться, что D08 открывается только после всех трёх" },
                    acceptanceCriteria: new[]
                    {
                        "Любой порядок N07A/B/C доступен и не создаёт тупик",
                        "Нет видимого или скрытого универсального счётчика улик"
                    },
                    implementationNote: "Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state) возвращает конкретные ещё не пройденные D07A/B/C по InvestigatedMill/Cattle/River (пустой список — либо фаза ещё не началась, либо все три завершены). NarrativeStateData не получил числового поля-счётчика. GetNextDialogueId не менялся: существующая линейная последовательность N01-N17 уже корректно пропускает завершённые шаги независимо от порядка их прохождения — этого достаточно для TryAdvance как разумной единственной подсказки; настоящий свободный выбор получает список через новый метод. Готовый launcher-экран для игрока пока не построен — открытый вопрос, явно вынесенный отдельно, не блокирует сами сцены.")
            );
        }

        private static DevelopmentPhaseData BuildP08Departure()
        {
            return Phase(
                "P08_DEPARTURE", "Старый след и решение идти дальше",
                "Реализовать N09 (совет: знания превращаются в цель, одна основная и максимум две опциональные дальние цели) и N10 (выбор 0–4 бойцов из существующего состава).",
                8, true,
                "Состав группы физически и текстово соответствует GameState; отсутствие конкретного спутника не блокирует обязательный сюжет.",
                new[] { "P07_INVESTIGATION" },

                Task("P08-T01", "N09 — совет: знания → цель",
                    "Открыть ровно одну основную дальнюю цель и максимум одну-две дополнительные, не засоряя карту равноправными маркерами.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 1,
                    relatedKnowledgeIds: new[]
                    {
                        "chapter01.knowledge.second_loaf_is_ration", "chapter01.knowledge.old_custom",
                        "chapter01.knowledge.seven_tooth_object"
                    },
                    relatedFlagIds: new[] { "chapter01.flag.far_route_unlocked" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_09_council_departure" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset",
                        "Assets/_Project/Chapter01/Runtime/Chapter01OutcomeApplier.cs"
                    },
                    manualChecks: new[] { "В Preview пройти D09 без опциональных знаний и с обеими — убедиться, что видно 0/1/2 доп. реплики" },
                    acceptanceCriteria: new[] { "Открыта ровно одна основная дальняя цель и максимум 1–2 опциональные" },
                    implementationNote: "N09 переписан на 9 узлов по правилу «одна реплика = один шаг». FarRouteUnlocked ставится не на входе в сцену, а на узле departure_decided — после единственного Normal-выбора «Проследить старый ход воды» (синтез в узле synthesis). Две опциональные цели — не map marker и не счётчик, а два блока (goal_loaf/goal_tooth) с Conditions по уже существующим знаниям P07 (SecondLoafIsRation+OldCustom; SevenToothObject): если знание не получено, блок не виден и Continue ведёт дальше без него. Раскрытие области поиска на карте (chapter01.location.old_water_search, «След старого русла») — Chapter01OutcomeApplier.ApplyDepartureConsequences, вызванный Chapter01StoryDirector.HandleDialogueCompleted по факту завершения D09, а не эффект самого диалога."),

                Task("P08-T02", "Осмысленные комбинации знаний открывают N09",
                    "Реализовать варианты A/B/C раздела 14 как условия открытия узла.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 2,
                    dependencies: new[] { "P07-T05" },
                    fileReferences: new[] { "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs" },
                    manualChecks: new[] { "Проверить, что при OldTraceFound без ни одной комбинации GetNextDialogueId не открывает D09 и не перескакивает на D10" },
                    acceptanceCriteria: new[] { "Хотя бы одна из трёх причинных комбинаций знаний открывает переход к N09" },
                    implementationNote: "Chapter01StoryDirector.CanOpenDepartureCouncil(state) = OldTraceFound && (A || B || C), где A = OldSeventhChannel+OldCustom, B = CattleAvoidOldBranch+FishPatternChanged, C = OldSeventhChannel+SevenToothObject — буквальный текст исходного раздела 14 в репозитории не сохранился, три комбинации формализованы производственным решением P08 поверх уже реализованных знаний P07 (не новые сущности). NodeStep получил необязательный ReadyGate (Func<NarrativeStateData,bool>), задан только для N09: GetNextDialogueId/GetNextNodeId при OldTraceFound без выполненной комбинации возвращают null, а не пропускают шаг и не открывают D10 — заблокированный N09 не обходится."),

                Task("P08-T03", "N10 — сбор отряда 0–4",
                    "Выбор бойцов идёт из реального состава GameState; любой состав от 0 до 4 позволяет продолжить.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 3,
                    relatedFlagIds: new[] { "chapter01.flag.expedition_started" },
                    relatedDialogueIds: new[] { "chapter01_dialogue_10_gather_party" },
                    fileReferences: new[]
                    {
                        "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset",
                        "Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs",
                        "Assets/_Project/UI/PrototypeUIController.HeroScreen.cs",
                        "Assets/_Project/UI/PrototypeUIController.Narrative.cs",
                        "Assets/_Project/Scripts/Core/GameState.cs"
                    },
                    manualChecks: new[]
                    {
                        "Пройти D10 до конца, убедиться, что Экран героя открывается автоматически",
                        "В picker'е «СОСТАВ ПОХОДА» набрать 0, 1 и 4 бойцов, нажать «ПОДТВЕРДИТЬ СОСТАВ», проверить ActiveExpedition.FighterIds",
                        "Убедиться, что кнопка «ПОДТВЕРДИТЬ СОСТАВ» скрыта вне контекста Главы 01 и после старта похода"
                    },
                    acceptanceCriteria: new[] { "Выбор бойцов физически совпадает с GameState", "0, 1 и 4 бойца — все допустимы" },
                    implementationNote: "N10 переписан на 5 узлов, заканчивается действием «Выбрать состав похода» без единого эффекта — реальный набор 0–4 бойцов происходит в Экране героя, не в диалоге. Панель «СОСТАВ ПОХОДА» перестала быть презентацией (раньше GetHeroScreenParty() жёстко брала первые 4 из GameState.Fighters, а пустые слоты заполняла существами из UnitDatabase как заглушки) — теперь это реальный picker: слоты читают selectedFighterIds, левый клик по занятому слоту убирает бойца, левый клик по строке «ДОСТУПНЫ В ДОМЕ» (тоже из GameState.Fighters) добавляет, кнопка «ПОДТВЕРДИТЬ СОСТАВ» видна только когда FarRouteUnlocked уже true и ExpeditionStarted ещё false. Подтверждение вызывает GameState.TryStartExpedition к раскрытой P08-T01 области поиска (chapter01.location.old_water_search) и, только при успехе, Chapter01StoryDirector.HandleStoryExpeditionStarted — флаг expedition_started убран из onRevealEffects узла открытия сцены (раньше ставился при простом просмотре текста, до какого-либо реального выбора)."),

                Task("P08-T04", "Исправить тексты о «всегда четырёх бойцах»",
                    "Найти и исправить любые реплики/описания, ошибочно утверждающие фиксированный состав из 4 бойцов.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NeedsUnityCheck, required: true, order: 4,
                    fileReferences: new[] { "Assets/_Project/Scripts/Core/GameState.cs" },
                    acceptanceCriteria: new[] { "Не осталось текста, утверждающего фиксированный состав из 4 бойцов" },
                    implementationNote: "Единственное найденное реальное нарушение — GameState.TryStartExpeditionToMapPoint формировал resultMessage с жёстко зашитым «и четыре выбранных бойца» независимо от фактического состава. Заменено на формулировку по настоящему expedition.FighterIds.Count (с корректным русским склонением боец/бойца/бойцов, отдельная ветка для похода героя в одиночку). Поиск по остальному коду (.cs) и активным (не ProjectDocs/Archive) документам ничего больше не нашёл — оставшиеся упоминания «четыре» либо про допустимые 0–4/четыре слота (корректно), либо не про состав похода вовсе.")
            );
        }

        private static DevelopmentPhaseData BuildP09Road()
        {
            return Phase(
                "P09_ROAD", "Сбор отряда и первая дальняя дорога",
                "Реализовать N11 на существующей физической карте: одна обязательная и одна необязательная дорожная сцена, пассивная проверка пути на Инстинкт+Следопытство, опциональная лагерная сцена.",
                9, true,
                "Дорога выполняет сюжетную функцию, цена пути сохраняется в GameState.",
                new[] { "P08_DEPARTURE" },

                Task("P09-T01", "N11 — дальняя дорога на физической карте",
                    "Без телепортации, на текущей карте Prototype_Main.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    acceptanceCriteria: new[] { "Дорога проходится физически на существующей карте" }),

                Task("P09-T02", "Обязательная дорожная встреча",
                    "Ровно одна обязательная встреча по пути к броду.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P09-T01" },
                    acceptanceCriteria: new[] { "Реализована ровно одна обязательная дорожная встреча" }),

                Task("P09-T03", "Необязательная дорожная встреча",
                    "Ровно одна опциональная встреча по пути.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P09-T01" },
                    acceptanceCriteria: new[] { "Реализована ровно одна необязательная дорожная встреча" }),

                Task("P09-T04", "Пассивная проверка пути",
                    "6 + Инстинкт + Следопытство + контекст, сложность 13; успех — подготовленность, провал — цена времени/припасов.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted, required: true, order: 4,
                    dependencies: new[] { "P09-T01" },
                    acceptanceCriteria: new[] { "Успех даёт подготовленность", "Провал тратит время/припасы, но не блокирует путь (failure-forward)" }),

                Task("P09-T05", "Опциональная лагерная сцена синтеза гипотез",
                    "Только если дорога достаточно длинная и есть что синтезировать; без отдельного Camp Manager.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: false, order: 5,
                    dependencies: new[] { "P09-T01" },
                    acceptanceCriteria: new[] { "Сцена собирает минимум две гипотезы и меняется от состава группы, иначе не добавляется" })
            );
        }

        private static DevelopmentPhaseData BuildP10Ford()
        {
            return Phase(
                "P10_FORD", "Старый брод и люди ниже по течению",
                "Реализовать N12 «Женщина у брода» и N13 «У всех есть дом» с учётом ремонта, предмета, знаний и состава отряда; решающая проверка Характера только там, где меняет цену отношений.",
                10, true,
                "Игрок впервые видит живых людей, которые несут цену решения Дома, и не может свести их к функции «выдать экспозицию».",
                new[] { "P09_ROAD" },

                Task("P10-T01", "N12 «Женщина у брода»",
                    "Материальный факт брода (колья/метки) плюс человеческая история свидетеля; без окончательной лекции.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    dependencies: new[] { "P00-T06" },
                    relatedKnowledgeIds: new[] { "chapter01.knowledge.old_ford", "chapter01.knowledge.drowned_woman_story" },
                    relatedFlagIds: new[] { "chapter01.flag.old_ford_found" },
                    acceptanceCriteria: new[] { "Есть материальный факт брода и человеческая история, но не единственная авторская трактовка" }),

                Task("P10-T02", "N13 «У всех есть дом»",
                    "Реакции учитывают ремонт, предмет, размер группы, оружие и предыдущую помощь; провал не скрывает обязательную истину навсегда.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P00-T03", "P10-T01" },
                    relatedFlagIds: new[] { "chapter01.flag.downstream_contact" },
                    acceptanceCriteria: new[]
                    {
                        "Реакции меняются от ремонта, предмета, состава и предыдущей помощи",
                        "Провал приводит к долгу/напряжению/травме, но не блокирует обязательную истину"
                    }),

                Task("P10-T03", "Решающая проверка Характера (первый контакт)",
                    "Сложность 13; успех улучшает форму контакта, провал не скрывает истину навсегда.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P10-T02" },
                    acceptanceCriteria: new[] { "Проверка сложности 13 на Характер реализована и влияет только на форму контакта" }),

                Task("P10-T04", "PartySizeAtLeast/PartySizeAtMost",
                    "Добавить целое PartySize и условия PartySizeAtLeast/PartySizeAtMost в контекст оценки (раздел 13.1) вместо постоянных флагов party_size_0…4.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted, required: true, order: 4,
                    fileReferences: new[] { "Assets/_Project/Scripts/Core/NarrativeState.cs" },
                    manualChecks: new[] { "Прогнать тесты условий размера группы в Unity" },
                    acceptanceCriteria: new[] { "Условия читают текущий контекстный размер группы, а не сохранённый вечный флаг" })
            );
        }

        private static DevelopmentPhaseData BuildP11Agreement()
        {
            return Phase(
                "P11_AGREEMENT", "Раскрытие соглашения и решение о возвращении",
                "Реализовать N14 (сборка физического/человеческого/мифического свидетельства, гарантированное shared_water_system) и N14½ (идти дальше или возвращаться).",
                11, true,
                "Игрок понимает причинную истину достаточно для выбора, но мир остаётся шире единственного объяснения.",
                new[] { "P10_FORD" },

                Task("P11-T01", "N14 — сборка свидетельства",
                    "Гарантированно выдать chapter01.knowledge.shared_water_system до необратимого решения; объяснить практическую функцию затворов/хлеба/предмета, если утверждено.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    dependencies: new[] { "P00-T03", "P00-T04", "P00-T05" },
                    relatedKnowledgeIds: new[]
                    {
                        "chapter01.knowledge.shared_water_system", "chapter01.knowledge.home_was_not_self_sufficient",
                        "chapter01.knowledge.old_agreement", "chapter01.knowledge.downstream_people"
                    },
                    relatedFlagIds: new[] { "chapter01.flag.agreement_revealed" },
                    acceptanceCriteria: new[] { "chapter01.knowledge.shared_water_system гарантированно выдаётся до N17" }),

                Task("P11-T02", "Сохранить сверхъестественную неоднозначность",
                    "Практическая причина доказуема; проявление памяти воды остаётся неоднозначным, без единого закрывающего монолога.",
                    DevelopmentTaskCategory.Narrative, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P00-T07", "P11-T01" },
                    acceptanceCriteria: new[] { "Ни одна сцена не закрывает сверхъестественную неоднозначность единственным монологом" }),

                Task("P11-T03", "N14½ — идти дальше или возвращаться",
                    "Явный выбор с учётом расстояния, времени и припасов, влияющий на обратную дорогу.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P11-T01" },
                    relatedFlagIds: new[] { "chapter01.flag.return_started" },
                    acceptanceCriteria: new[] { "Решение влияет на маршрут и состояние обратной дороги" })
            );
        }

        private static DevelopmentPhaseData BuildP12Return()
        {
            return Phase(
                "P12_RETURN", "Изменившаяся обратная дорога и Дом",
                "Реализовать N15 (изменённая обратная дорога) и N16 (серьёзное возвращение домой): Дом встречает игрока последствиями, а не только докладом.",
                12, true,
                "Возвращение доказывает память мира минимум тремя видимыми изменениями.",
                new[] { "P11_AGREEMENT" },

                Task("P12-T01", "N15 — изменившаяся обратная дорога",
                    "Варианты по времени, ремонту, отношению и знаниям; возврат не повторяет исходную дорогу дословно.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    acceptanceCriteria: new[] { "Обратная дорога физически проходится и не является дословным повтором" }),

                Task("P12-T02", "N16 — серьёзное возвращение домой",
                    "Минимум три ранних мотива (раздел 18) возвращаются изменёнными.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P04-T04", "P12-T01" },
                    relatedFlagIds: new[] { "chapter01.flag.returned_home" },
                    acceptanceCriteria: new[] { "Минимум три ранних мотива возвращаются в видимо изменённом состоянии" }),

                Task("P12-T03", "Таблица эха по флагам паводка и ремонта",
                    "Каждый флаг паводка/ремонта проявляется минимум один раз до N10 и один раз в N16 (раздел 11).",
                    DevelopmentTaskCategory.Data, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P05-T03", "P06-T01", "P12-T02" },
                    acceptanceCriteria: new[] { "Ни один флаг паводка/ремонта не остаётся нигде не увиденным" })
            );
        }

        private static DevelopmentPhaseData BuildP13Council()
        {
            return Phase(
                "P13_COUNCIL", "Совет Дома и итог главы",
                "Реализовать N17: три направления решения после гарантированного знания, устойчивые флаги результата и отношений, ни одного варианта без цены.",
                13, true,
                "Есть 2–3 устойчиво различимых состояния мира, понятные игроку и пригодные для будущего эха.",
                new[] { "P12_RETURN" },

                Task("P13-T01", "N17 — совет Дома, три направления",
                    "Восстановить старый порядок / создать новый порядок / оставить воду Дому — все три с ценой.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    dependencies: new[] { "P00-T10", "P11-T01" },
                    relatedFlagIds: new[] { "chapter01.flag.council_completed", "chapter01.flag.completed" },
                    acceptanceCriteria: new[] { "Реализованы все три направления", "Ни один вариант не является бесплатно идеальным" }),

                Task("P13-T02", "Решение о памяти Милы",
                    "Назвать / скрыть / превратить в обряд / вернуть человеческую историю — только после утверждения DEC-07.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: false, order: 2,
                    dependencies: new[] { "P00-T06" },
                    acceptanceCriteria: new[] { "Реализовано только после утверждения DEC-07" }),

                Task("P13-T03", "Устойчивые флаги результата",
                    "Зафиксировать флаги результата, отношений и будущих долгов для эха в следующих главах.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P13-T01" },
                    acceptanceCriteria: new[] { "2–3 устойчиво различимых состояния мира зафиксированы флагами" })
            );
        }

        private static DevelopmentPhaseData BuildP14Encounters()
        {
            return Phase(
                "P14_ENCOUNTERS", "Пул региональных встреч и эхо решений",
                "Добавить 6–10 сильных региональных сцен вокруг готового позвоночника через существующий ExpeditionIncidentSystem; расширять к 50–70 только пакетами после плейтестов.",
                14, true,
                "Повторное прохождение показывает заметную вариативность без потери причинной линии.",
                new[] { "P13_COUNCIL" },

                Task("P14-T01", "Адаптер ExpeditionIncidentSystem → авторские сцены",
                    "Использовать существующую систему встреч с небольшим адаптером к базе диалогов; отдельную Encounter Database не создавать раньше срока.",
                    DevelopmentTaskCategory.Integration, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    fileReferences: new[] { "Assets/_Project/Scripts/Core/ExpeditionIncidentSystem.cs" },
                    acceptanceCriteria: new[] { "Система выбирает только допустимые по условиям сцены" }),

                Task("P14-T02", "6–10 региональных встреч",
                    "Каждая встреча: stable ID, стадия, место/маршрут, условия, запреты, вес, repeat policy, dialogue ID, последствия, echo tags.",
                    DevelopmentTaskCategory.Content, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P14-T01" },
                    acceptanceCriteria: new[] { "Минимум 6 встреч заполнены по минимальным полям раздела 17" }),

                Task("P14-T03", "Критическое знание не только в редкой встрече",
                    "Ни одно обязательное знание не имеет единственным источником неповторяемую случайную встречу.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    dependencies: new[] { "P14-T02" },
                    acceptanceCriteria: new[] { "Валидация контента подтверждает наличие альтернативного источника у каждого обязательного знания" })
            );
        }

        private static DevelopmentPhaseData BuildP15BattleBridge()
        {
            return Phase(
                "P15_BATTLE_BRIDGE", "Боевой мост",
                "Условный этап: реализуется только если Глава 01 потребует обязательного боя. Если требуется — запускать исключительно существующий BattleSandbox, без второй боевой системы.",
                15, false,
                "Этап остаётся отложенным, пока глава не требует обязательного боя.",
                null,

                Task("P15-T01", "Мост к BattleSandbox",
                    "Передавать состав экспедиции в BattleSandbox, возвращать результат в GameState/NarrativeState, обеспечивать продолжение при поражении, если это не сознательная финальная смерть.",
                    DevelopmentTaskCategory.Code, DevelopmentTaskStatus.Deferred, required: false, order: 1,
                    fileReferences: new[] { "Assets/_Project/BattleSandbox/Runtime" },
                    acceptanceCriteria: new[] { "Запускается только существующий BattleSandbox, без второй боевой системы" },
                    blockerNote: "Отложено: Глава 01 пока не требует обязательного боя (раздел 1.4, П15).")
            );
        }

        private static DevelopmentPhaseData BuildP16QaDocs()
        {
            return Phase(
                "P16_QA_DOCS", "QA, сохранение, регрессии и документация",
                "Пройти обязательные ветки, попарное покрытие факторов (раздел 19.4), сохранение/загрузку вокруг необратимых выборов, все валидаторы и обновить документацию.",
                16, true,
                "Глава проходится от начала до совета во всех обязательных ветках без тупика, потери состояния и противоречий текста.",
                new[] { "P13_COUNCIL" },

                Task("P16-T01", "QA-матрица QA-01…QA-08",
                    "Пройти восемь сценариев раздела 19.4 (ремонт × состав × провалы/успехи × порядок расследования × сохранение).",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted, required: true, order: 1,
                    dependencies: new[] { "P14_ENCOUNTERS" },
                    acceptanceCriteria: new[] { "Все восемь сценариев QA-01…QA-08 пройдены и запротоколированы" }),

                Task("P16-T02", "Шесть комбинаций «ремонт × финал»",
                    "Пройти все 6 комбинаций (2 ремонта × 3 финальных направления).",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted, required: true, order: 2,
                    dependencies: new[] { "P13-T01" },
                    acceptanceCriteria: new[] { "Все 6 комбинаций ремонт×финал пройдены" }),

                Task("P16-T03", "Save/Load вокруг необратимых выборов",
                    "Сохранение и загрузка перед/после N04, N05, N13, N17 не дублирует эффекты.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted, required: true, order: 3,
                    acceptanceCriteria: new[] { "Нет повторного применения ресурсов/времени/эффектов после загрузки" }),

                Task("P16-T04", "Validator баз диалогов/существ/плана/ID",
                    "Запустить все валидаторы проекта и убедиться, что они проходят без ошибок.",
                    DevelopmentTaskCategory.Test, DevelopmentTaskStatus.NotStarted, required: true, order: 4,
                    acceptanceCriteria: new[] { "Все валидаторы (диалоги, существа, план, Chapter01Ids) проходят без ошибок" }),

                Task("P16-T05", "Обновить DEVELOPMENT_STATUS.md и нарративные документы",
                    "Журнал отражает фактическое состояние Главы 01 после прохождения QA-матрицы.",
                    DevelopmentTaskCategory.Documentation, DevelopmentTaskStatus.NotStarted, required: true, order: 5,
                    dependencies: new[] { "P16-T01", "P16-T02", "P16-T03", "P16-T04" },
                    fileReferences: new[] { "ProjectDocs/DEVELOPMENT_STATUS.md" },
                    acceptanceCriteria: new[] { "DEVELOPMENT_STATUS.md обновлён по факту прохождения QA" })
            );
        }

        private static DevelopmentPhaseData Phase(
            string id, string title, string purpose, int order, bool required,
            string acceptanceSummary, string[] dependencies,
            params DevelopmentTaskData[] tasks)
        {
            DevelopmentPhaseData phase = new DevelopmentPhaseData
            {
                id = id,
                title = title,
                purpose = purpose ?? string.Empty,
                order = order,
                required = required,
                acceptanceSummary = acceptanceSummary ?? string.Empty
            };

            if (dependencies != null)
                phase.dependencies.AddRange(dependencies);
            if (tasks != null)
                phase.tasks.AddRange(tasks);

            return phase;
        }

        private static DevelopmentTaskData Task(
            string id, string title, string details,
            DevelopmentTaskCategory category, DevelopmentTaskStatus status,
            bool required, int order,
            string[] dependencies = null,
            string[] acceptanceCriteria = null,
            string[] manualChecks = null,
            string[] fileReferences = null,
            string[] relatedDialogueIds = null,
            string[] relatedFlagIds = null,
            string[] relatedKnowledgeIds = null,
            string blockerNote = null,
            string optionalAutoCheckId = null,
            string completedAt = null,
            string implementationNote = null,
            bool acceptanceCriteriaDone = false)
        {
            DevelopmentTaskData task = new DevelopmentTaskData
            {
                id = id,
                title = title,
                details = details ?? string.Empty,
                category = category,
                status = status,
                required = required,
                order = order,
                blockerNote = blockerNote ?? string.Empty,
                optionalAutoCheckId = optionalAutoCheckId ?? string.Empty,
                completedAt = completedAt ?? string.Empty,
                implementationNote = implementationNote ?? string.Empty
            };

            if (dependencies != null)
                task.dependencies.AddRange(dependencies);
            if (fileReferences != null)
                task.fileReferences.AddRange(fileReferences);
            if (relatedDialogueIds != null)
                task.relatedDialogueIds.AddRange(relatedDialogueIds);
            if (relatedFlagIds != null)
                task.relatedFlagIds.AddRange(relatedFlagIds);
            if (relatedKnowledgeIds != null)
                task.relatedKnowledgeIds.AddRange(relatedKnowledgeIds);
            if (manualChecks != null)
                task.manualChecks.AddRange(manualChecks);

            if (acceptanceCriteria != null)
            {
                foreach (string criterionText in acceptanceCriteria)
                    task.acceptanceCriteria.Add(new AcceptanceCriterionData { text = criterionText, done = acceptanceCriteriaDone });
            }

            return task;
        }
    }
}
