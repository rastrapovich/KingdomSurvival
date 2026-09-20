# Kingdom Survival — Development Status

Последнее обновление: 2026-09-20

> Технический журнал фактически реализованного состояния Unity-проекта и зафиксированных проектных решений.
>
> Актуальный общий канон: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_40.md`.
>
> Единая энциклопедия мира: `ProjectDocs/LORE.md`.
>
> Единая нарративная библия: `ProjectDocs/NARRATIVE.md`.
>
> Движок: Unity 6000.5.10f1. UI: UI Toolkit.

## 0. Архив технического журнала

Подробное состояние проекта непосредственно перед реализацией P11/P12 сохранено без изменений в:

`ProjectDocs/Archive/DEVELOPMENT_STATUS_2026-09-12_before_p11_p12.md`.

Там остаются подробные записи P09, UI-layering, P10, Location Interaction и P10-T05. Текущий файл оставляет только актуальный производственный срез и ближайшие проверки.

## 1. Текущий этап

Проект находится на этапе **«Нарративный фундамент / production-проход первой главы»**.

Главный производственный приоритет:

`герой → нормальная жизнь Дома → паводок/ремонт → причинные симптомы → расследование → дальняя дорога → нижние люди и старое соглашение → возвращение → последствия → главная тайна → антагонистическая сила → первый регион → первый большой квест`.

Текущая активная задача — **P13_COUNCIL: серьёзное возвращение в Дом → финальный Совет → одно из трёх устойчивых состояний мира → запись результата в Хронике**.

Новые крупные универсальные системы до доказательства первого региона не являются приоритетом.

## 2. Ограничения проверки

Код P11/P12/P13 подготовлен и снабжён EditMode regression-тестами, но на момент их написания компиляция/`Run All`/Play Mode не запускались напрямую — для P13 использовались доступные статические проверки Unity YAML, связности D17, уникальности ID/эффектов, completion-инвариантов и чистоты diff.

Последний зафиксированный большой прогон на тот момент: **330 тестов: 328 passed / 2 failed**; оба падения относились к ошибочной ссылке N01, которая затем была исправлена.

**С 12.09.2026 подключён MCP-мост к живому Unity Editor пользователя** (`unity-editor-mcp`) — компиляция, `console`, `recompile` и `run_tests` (EditMode/PlayMode) доступны напрямую в рамках сессий, где мост подключён. Полный `Run All` EditMode после WM-04/WM-06 (см. §18): **783 теста: 783 passed / 0 failed**. Это не отменяет ограничение для сессий/участков работы без моста — там по-прежнему нужно честно писать, что компиляция и тесты не запускались.

## 3. P09/P10 baseline

До текущего этапа в репозитории уже присутствуют:

- физическое движение по глобальной карте с непрерывным временем;
- N11 и D11B как дорожные сцены первого похода;
- Camp Screen v1;
- Narrative Dialogue поверх fullscreen-экранов;
- Location Interaction;
- OldWaterSearch → N12 → раскрытие нижнего поселения → физический путь → N13;
- PartySize = герой + бойцы;
- FirstContact как решающая проверка Характера;
- Хроника с этапами дальней дороги.

P10 и связанные UI-изменения по-прежнему требуют полной Unity-проверки после Pull; подробности сохранены в архиве §0.

## 4. P11 — раскрытие соглашения и решение о возвращении

### 4.1. P11-T01 — N14 и гарантированное знание

Dialogue Database уже содержит production-эффекты N14:

- `chapter01.flag.agreement_revealed`;
- `chapter01.knowledge.shared_water_system`;
- `chapter01.knowledge.home_was_not_self_sufficient`;
- `chapter01.knowledge.old_agreement`.

Новый `Chapter01ReturnFlow.EnsureAgreementKnowledge()` закрепляет инвариант: если N14 уже считается раскрытой, обязательные знания присутствуют даже в старом/отладочном сохранении с частично применёнными эффектами. Обязательная причинная истина не может потеряться из-за одного необязательного свидетельства.

### 4.2. P11-T02 — сверхъестественная неоднозначность

В код не добавлялись:

- «истинная причина» мистики;
- шкала сверхъестественного;
- новый дух/монстр;
- флаг, объявляющий рациональное или мифическое объяснение единственно верным.

Практическая связь общей водной системы фиксируется знаниями. Формула этапа сохранена: **факты становятся яснее, причина — нет**.

### 4.3. P11-T03 — N14½

Ветка читается из уже сохраняемых `AppliedEffectExecutionIds` Dialogue Database:

- `chapter01.effect.n14_5_continue_flag1` → идти дальше;
- `chapter01.effect.n14_5_return_flag1` → возвращаться сейчас.

Новых постоянных branch-флагов не добавлено.

После завершения выбора используется штатный `GameState.TryOrderReturn()` — экспедиция реально строит маршрут от текущей позиции до Дома и переходит в `ReturningToCastle`, без телепортации.

Ветка «идти дальше» получает реальную цену: перед обратным движением запускается одноразовое 4-часовое действие `ПРОВЕРКА СВЕЖЕГО СЛЕДА`. Ветка «возвращаться» не получает этой скрытой задержки.

## 5. P12 — изменившаяся обратная дорога и Дом

### 5.1. P12-T01 — N15

N15 больше не является просто следующим линейным текстом. `Chapter01ReturnFlow.IsReturnRoadSceneReady()` разрешает её только когда:

- начато возвращение;
- существует активная экспедиция;
- фаза действительно `ReturningToCastle`;
- нет другого времязатратного действия;
- пройдена часть физического обратного маршрута;
- N15 ещё не завершена.

После N15 применяется одноразовая цена изменившейся дороги через штатный `TryStartRoadActivity`. Длительность зависит от эха ремонта/паводка и ветки N14½. Повторное открытие/Save-Load не должно умножать задержку благодаря существующему реестру `AppliedEffectExecutionIds`.

### 5.2. P12-T02 — N16

N16 разрешается только после фактического завершения физического возвращения: активной экспедиции уже нет, командир снова `InCastle`, N15 завершена, `ReturnedHome` ещё не выставлен.

Для этого этапа добавлен узкий `PrototypeUIController.Chapter01ReturnFlow.cs`, который:

1. связывает N13 → N14 → N14½ без нового QuestManager;
2. переводит выбранную ветку в физическое возвращение;
3. открывает N15 только на обратной дороге;
4. после реального прибытия отдаёт приоритет N16 над техническим модалом `ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ`;
5. не меняет обычные return-notice других походов.

Существующий текст N16 возвращает три ранних мотива: звук мельницы, воду у брода и лица людей. Флаг `ReturnedHome` по-прежнему ставит сама Dialogue Database.

### 5.3. P12-T03 — компактная таблица эха

`Chapter01ReturnFlow.ResolveEcho()` — локальный resolver первого возвращения, а не универсальный consequence engine.

Он читает только реально существующее состояние:

- `RepairOld` / `RepairNew`;
- `FloodMillDeckDestroyed`;
- `FloodLivestockLost`;
- `FloodWorkersSaved`;
- выбор N14½.

Результат определяет:

- вариант дорожного эха;
- одноразовую цену обратной дороги во времени;
- текстовое эхо возвращения в текущем отчёте/Хронике;
- безопасный fallback для старых/неполных сохранений.

`Chapter01JournalProvider` проводит одну цель `Старый след` через стадии `:agreement → :return_decision → :returning → :homeward → :final_council`. После `ReturnedHome` цель остаётся активной до решения Совета, а затем завершается одной из ревизий `:old_order`, `:new_order`, `:water_for_home`. Опциональные записи про второй хлеб и семизубый калибр закрываются только после знания общего соглашения/системы.

## 6. P13 — Совет Дома и итог главы

### 6.1. P13-T01 — production D17

D17 заменён с одноузловой заглушки на сцену из семи узлов:

`возвращение к быту Дома → открытие Совета → пересекающиеся позиции жителей → решение → отдельный aftermath`.

Совет открывается существующим first-chapter poller после N16, без нового lifecycle-метода и без отдельной council-системы. `CanOpenFinalCouncil()` требует `ReturnedHome`, знания `SharedWaterSystem` и `OldAgreement`, а также отсутствие `CouncilCompleted/Completed`.

Старт D17 больше не завершает главу. Решающий узел содержит ровно три обычных выбора без skill check:

- восстановить старый общий порядок — постоянный труд, ресурсы и признанная зависимость от нижних людей;
- создать новый общий порядок — явная компенсация и новое постоянное обязательство;
- оставить воду Дому — потеря прежнего стока нижними и открытый будущий долг.

Каждый выбор ведёт в отдельный aftermath с человеческими реакциями и только там ставит completion-флаги. Conditional text читает обе ветки ремонта, последствия паводка, сохранённый выбор N14½, помощь у брода, FirstContact, семизубую пластину и смысл второго хлеба. Эти сведения меняют реплики, но не доступность трёх решений.

Для чтения N14½ добавлен `NarrativeConditionType.EffectApplied`: он использует уже сохранённый `EffectExecutionId`, поэтому второй branch-флаг не создаётся. Значение добавлено строго в конец enum; редактор Dialogue Database умеет его показывать и редактировать.

### 6.2. P13-T02 — память Милы

Задача оставлена `Deferred` до отдельного утверждения DEC-07. Личность Милы, обстоятельства смерти, отношение к воде и границы сверхъестественного не придуманы и не добавлены в D17. Это не блокирует обязательную часть P13.

### 6.3. P13-T03 — устойчивый результат

Добавлены взаимоисключающие stable flags:

- `chapter01.flag.council_old_order_restored`;
- `chapter01.flag.council_new_order_created`;
- `chapter01.flag.council_water_kept_for_home`.

Ветка «вода Дому» дополнительно ставит `chapter01.flag.downstream_debt_open`; две общие ветки явно очищают его. Узкий `Chapter01CouncilOutcomeResolver` централизованно читает и проверяет итог без нового manager. Все значения сохраняются штатным `NarrativeStateData`; JSON round-trip покрыт тестом.

### 6.4. Канонический hotfix подачи реплик

После P13 обнаружена регрессия presentation: один `NarrativeDialogueView` раскрывал сразу все подходящие `textBlocks` узла, поэтому Совет мог показать готовый блок из фраз нескольких персонажей.

Исправление сделано глобально в `NarrativeDialogueRuntimeSession`, а не только в D17:

- каждый подходящий `TextBlock` становится отдельным presentation-шагом;
- между блоками одного авторского узла runtime создаёт синтетический `Continue` (`…`);
- реальные варианты ответа и disabled-подсказки скрыты до последнего блока;
- `OnRevealEffects` и пассивная проверка конкретной реплики фиксируются в момент её показа и не повторяются от `BuildView`;
- `NarrativeUiHistoryGrouping` больше не склеивает несколько фраз и отклоняет нарушение контракта;
- правило канонизировано в `ProjectDocs/NARRATIVE.md §4.10` без исключений для одного говорящего, разных говорящих или видов текстовых блоков.

Существующие assets не требуют массового разрезания на новые узлы: модульные и условные блоки сохраняются как authoring-структура, а production и Editor Preview получают единую последовательную семантику runtime.

## 7. Development Tracker

Seed переведён на milestone `P13_COUNCIL`: P13-T01 и P13-T03 имеют статус `NeedsUnityCheck`, P13-T02 — `Deferred` с причиной DEC-07.

Одноразовый `DevelopmentPlanP13ProgressSync` обновляет уже существующий plan asset через его фактический `DevelopmentPlanBootstrap.AssetPath`, добавляет ID, файлы, implementation notes и ручные проверки, но не стирает acceptance-галочки и не откатывает `Completed/Blocked/Deferred`. Marker не позволяет повторять миграцию при каждом domain reload.

## 8. Regression-тесты

Добавлен `Assets/_Project/Chapter01/Tests/EditMode/Chapter01P13Tests.cs`. Покрываются gate Совета, отсутствие раннего completion, Unity-валидация D17, ровно три решения, все outcome/debt-флаги, взаимоисключаемость, обе ветки ремонта, запрет повторного открытия, состояния Хроники, чтение сохранённого выбора N14½, Save/Load результата и поочерёдный показ реплик.

Общие EditMode-тесты дополнительно фиксируют runtime-контракт `VisibleTextBlocks.Count <= 1`, синтетический Continue, блокировку преждевременного выбора, отложенный `OnRevealEffects` и защиту UI-группировки. Сценарные тесты D01–D04, D11–D13 и D17 адаптированы к последовательным presentation-шагам.

Файл написан, но в текущей среде не скомпилирован и не запущен.

## 9. Изменённые файлы P13

- `Assets/_Project/Chapter01/Runtime/Chapter01Ids.cs`;
- `Assets/_Project/Chapter01/Runtime/Chapter01StoryDirector.cs`;
- `Assets/_Project/Chapter01/Runtime/Chapter01CouncilOutcome.cs` и `.meta`;
- `Assets/_Project/Chapter01/Runtime/Chapter01JournalProvider.cs`;
- `Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset`;
- `Assets/_Project/UI/PrototypeUIController.Chapter01ReturnFlow.cs`;
- `Assets/_Project/Scripts/Core/NarrativeConditions.cs`;
- `Assets/_Project/DialogueDatabase/Editor/DialogueDatabaseWindow.cs`;
- `Assets/_Project/DialogueDatabase/Editor/DialogueDatabaseWindow.GraphPresentation.cs`;
- `Assets/_Project/Chapter01/Tests/EditMode/Chapter01P13Tests.cs` и `.meta`;
- `Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanSeedData.cs`;
- `Assets/_Project/DevelopmentTracker/Editor/DevelopmentPlanP13ProgressSync.cs` и `.meta`;
- `ProjectDocs/DEVELOPMENT_STATUS.md`.

Сюжетный и лорный канон P13 не менялся. После hotfix в `NARRATIVE.md §4.10` добавлено общее обязательное правило поочерёдной подачи реплик; `LORE.md` и bestiary-файлы не менялись.

## 10. Что проверить после Pull

1. Дождаться чистой Unity-компиляции; Console без C# errors.
2. Убедиться, что Development Tracker показывает milestone P13, P13-T01/T03 как `NeedsUnityCheck`, P13-T02 как `Deferred`, а прежние ручные отметки сохранены.
3. Запустить полный EditMode `Run All`, особенно `Chapter01P13Tests`, `Chapter01P11P12Tests`, `Chapter01P10Tests` и DevelopmentPlan tests.
4. После физического завершения N16 проверить штатное автоматическое открытие N17; при первом показе `CouncilCompleted/Completed` ещё не должны быть установлены.
5. Пройти минимум четыре сценария: старый ремонт → старый порядок; старый ремонт → новый порядок; новый ремонт → новый порядок; новый ремонт → вода Дому.
6. Отдельно пройти Совет после `FloodLivestockLost` или `FloodMillDeckDestroyed` и убедиться, что последствие звучит в репликах.
7. На каждом пути проверить, что каждая фраза Совета появляется отдельным шагом, смена говорящего меняет портрет только на своей реплике, а три немаркированных «добро/зло» решения появляются лишь после последней позиции.
8. Проверить соответствующий итог в Хронике, Save/Load outcome/debt-флагов и невозможность повторно открыть Совет.

До зелёного `Run All` и ручного Play Mode обязательные P13-T01/P13-T03 остаются `NeedsUnityCheck`, а не `Completed`.

## 11. Следующий шаг

После Unity-проверки обязательная часть P13 должна стать 2/2. Опциональный P13-T02 остаётся отложенным до отдельного решения по Миле; дальнейшую работу вести от устойчивого результата главы к первому региону, не превращая локальный resolver в универсальную систему последствий заранее.

## 12. Нарративные reference-документы — 12.09.2026

Добавлен `ProjectDocs/THIS_WAR_OF_MINE_ADAPTATION.md` как `REFERENCE ONLY + утверждённый инструментарий`.

Зафиксированы переносимые принципы исследования компьютерной и настольной `This War of Mine`:

- `решение → время → возврат NPC`;
- продолжение проблемы после отказа/неудачи;
- `Consequence-of-Refusal Encounter`;
- `Echo / Returning Encounter`;
- `Escalation Encounter` как лестница постепенно растущего компромисса;
- `TIME × NEED × RELATIONSHIP` как дополнительные авторские оси к существующей Encounter Grammar;
- положительные визиты и взаимная помощь, чтобы событие не всегда означало потерю;
- несколько авторских состояний одной локации;
- Global Pressure, меняющий смысл существующих действий и мест;
- временная цена как альтернатива чистому броску;
- разные маршруты как разные нарративные экологии;
- потеря человека как возможная причина изменения оставшихся;
- `CHARACTER × STATE × MEMORY × RECENT EVENT → персональная микро-сцена`;
- `Recollection` и `Current Thoughts` из `Memories from the Past` как reference для постоянных спутников;
- информация как действие: `узнал → рассказал/скрыл → мир изменился`;
- культурная/символическая ценность против немедленной утилитарной выгоды;
- правила дедупликации первой и второй редакций настольной игры.

Документ также фиксирует исследовательский корпус: компьютерная база/Final Cut, `The Little Ones`, `Father's Promise`, `The Last Broadcast`, `Fading Embers`, настольная база с 1900+ Script, `Tales from the Ruined City`, `Days of the Siege / Forlorn Hope`, `Expanded Incidents` и `Memories from the Past` с 200+ character-specific Script.

Это **не реализация новой Unity-системы** и не основание сейчас расширять `Chapter01ReturnFlow` в универсальный consequence engine. Конкретные адаптированные события остаются будущей производственной работой; сначала исследуются партиями и проверяются реальными сценами первого региона/Дома.

## 13. Reactive World / расширенная Encounter Grammar — 12.09.2026

Добавлен `ProjectDocs/REACTIVE_WORLD_ENCOUNTER_ADAPTATION.md` как подробный `REFERENCE ONLY + утверждённый инструментарий`.

Документ объединяет полезные принципы из `Frostpunk` (настольная и компьютерная версии), `Robinson Crusoe`, `Earthborne Rangers`, `Tainted Grail`, `Destinies`, `Arkham Horror 3e`, `The 7th Continent`, `Legacy of Dragonholt`, `Kingdom Death: Monster` с уже утверждёнными слоями `Eldritch Horror`, `Dead of Winter`, `This War of Mine`, `Disco Elysium` и собственным синтезом `Kingdom Survival`.

Главные новые утверждённые инструменты:

- **Pending Consequence** — решение может посеять потенциальное будущее последствие, которое проявится только при причинно подходящем контексте;
- **Problem Pressure** — нерешённая проблема может стареть, менять форму или быть решена другими людьми без героя;
- **Micro Reaction** — короткая реакция мира без обязательного Encounter Window, выбора или награды;
- **Location State** — важное знакомое место получает ограниченные авторские состояния и новый смысл при повторном посещении;
- **Unlocked Action** — качество, компетенция, знание, предмет или спутник способны открывать новый способ действия, а не только числовой модификатор;
- **Human Echo** — значимое системное решение позднее материализуется через конкретного человека и его жизнь.

Дополнительно сохранены `Promise Memory`, `Narrative Object`, проникновение активной темы в обычную жизнь, `Recollection / Current Thought`, происхождение значимого ресурса, временная цена вместо обязательного броска, разные нарративные экологии маршрутов и правило физически/социально видимого последствия.

Утверждённая авторская диагностическая схема:

> **`CONTEXT → TRIGGER → HUMAN STATE → RESOLUTION → AFTERMATH → VISIBLE ECHO`.**

Её шесть вопросов: почему история возможна здесь; почему произошла сейчас; почему люди реагируют именно так; что герой реально может сделать; что останется после его ухода; как игрок позднее увидит последствие.

Это **не новая Unity-архитектура**. Не создавать заранее универсальные `PendingConsequenceManager`, `ProblemPressureSystem`, `MicroReactionSystem`, `LocationStateMachine`, `HumanEchoManager` или глобальный Event Engine. Сначала инструменты проверяются реальными сценами `«Дома на чужой воде»`, первой дальней дороги и первого региона; код закрепляет только повторяющиеся доказанные требования.

`NARRATIVE.md` уже содержит канонические родительские правила Narrative State, изменяемых локаций, failure-forward, эха, жизни мира без героя и границы технической реализации; новый документ расширяет **утверждённый инструментарий и словарь**, но не меняет сюжетные факты, `LORE.md` или общий канон v1.30.

## 14. Hotfix — дублированный `LateUpdate` после P11/P12

После Pull Unity обнаружил `CS0111` в `PrototypeUIController.Debug.cs`: partial-класс `PrototypeUIController` содержал два метода `LateUpdate()` с одинаковой сигнатурой — существующий debug lifecycle и добавленный P11/P12 poller.

Исправление локальное:

- отдельный `LateUpdate()` удалён из `PrototypeUIController.Chapter01ReturnFlow.cs`;
- `RefreshChapter01ReturnFlow()` вызывается из уже существующего единственного `PrototypeUIController.LateUpdate()` в `PrototypeUIController.Debug.cs`;
- вызов стоит до раннего выхода `!debugMenuInitialized`, поэтому return-flow работает и когда debug-меню недоступно/не инициализировано;
- порядок остаётся прежним: `Update()` выполняет непрерывную симуляцию, затем `LateUpdate()` видит итог кадра и может открыть N14/N14½/N15/N16.

Канон, Dialogue Database, карта, время, таблица эха и логика P11/P12 не менялись. Unity-компиляцию после hotfix необходимо повторить; затем запустить полный EditMode `Run All`.

## 15. World Map 2.0 — WM-01 (Visual Database) — 12.09.2026

Первый этап технической архитектуры карты из раздела 9.9 канона (`KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_31.md`). Цель WM-01 — отделить визуал карты от кода: местность и иконки локаций теперь читаются из ScriptableObject-темы, а не из захардкоженных USS-цветов/глифов. Логика движения (`WorldMapNavigation`, `GameState`) не менялась.

Новый модуль `Assets/_Project/WorldMapVisual/Runtime/` (asmdef `KingdomSurvival.WorldMapVisual`, ссылается на `KingdomSurvival.Core`, автора-подключаемый):

- `WorldMapTerrainVisualProfile` — цвет клетки + список спрайтов-вариантов (`massVariants`, пока не используются рендером — задел под WM-04) для одного `WorldMapTerrainType`.
- `WorldMapLocationIconEntry` — связь `LocationData.Id` → `Sprite`.
- `WorldMapIconLibrary` (`[CreateAssetMenu]`) — переиспользуемый набор иконок локаций + `DefaultLocationIcon`.
- `WorldMapVisualTheme` (`[CreateAssetMenu]`) — список терраин-профилей + ссылка на `WorldMapIconLibrary`.
- `WorldMapDatabaseAsset` (`[CreateAssetMenu]`) — фасад, хранит `ActiveTheme`; читается из `Resources` по пути `WorldMapVisual/KingdomSurvivalWorldMapDatabase`.
- `WorldMapVisualRuntime` — кэширующий статический загрузчик (`Resources.Load`), по образцу `DialogueDatabaseRuntime`.

Изменения в `Assets/_Project/UI/PrototypeUIController.WorldMap.cs`:

- `DrawBlockedTerrain()` (мёртвый код — `IsBlockedPercent` всегда `false`, местность никогда не рисовалась) заменён на `DrawTerrainCells()`: перебирает внутреннюю сетку 26×16, для клеток с `WorldMapTerrainType != Plains` берёт `CellColor` из активной темы и красит `VisualElement` (класс `world-map-terrain-cell` в `Prototype_Exploration.uss`, цвет больше не хардкожен в USS). Без назначенной темы (Resources-ассет ещё не создан) слой остаётся пустым — поведение как раньше.
- `CreateWorldMapNode` получил `ApplyWorldMapNodeIcon`: если в `WorldMapIconLibrary` для `location.Id` есть спрайт, поверх кнопки добавляется `Image` (класс `world-map-node-icon`) и текстовый глиф (`✓`/`●`) очищается; без темы/иконки — прежнее поведение (только глиф).
- `PrototypeUIController.WorldMapInteractionPolish.cs` и `...WorldMapLocationActions.cs` не тронуты (эти два файла проверяются regression-тестом на отсутствие `new VisualElement/Label/Button`).

Три ассета-экземпляра (`KingdomSurvivalWorldMapIcons.asset`, `KingdomSurvivalWorldMapTheme.asset` с профилями `Hills`/`Mountains`, `KingdomSurvivalWorldMapDatabase.asset`) собраны вручную как Unity YAML (не через Editor `Create Asset Menu`) по образцу уже существующего `KingdomSurvivalBattlefields.asset`, с GUID script-ссылок из `.meta` скриптов и самостоятельно сгенерированными GUID самих ассетов. Лежат в `Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/`.

**Статус проверки:** пользователь подтвердил в своём Unity Editor — компиляция чистая (остались только три предсуществующих предупреждения, не связанных с WM-01: устаревший `FindFirstObjectByType` в `Buildings.cs` и два неиспользуемых поля в `WorldMap.cs`, оставленные ещё до этой сессии), ассеты импортировались корректно, `Icon Library`/`Terrain Profiles` в Inspector не пустые, в Play Mode клетки Hills/Mountains закрашиваются цветом из темы. WM-01 подтверждён рабочим. EditMode `Run All` (`WorldMapNavigationTests` и др.) отдельно не запускался.

## 16. World Map 2.0 — WM-02 (слои рендера) — 12.09.2026

Второй этап из раздела 9.9 канона. Цель WM-02 — явная структура слоёв поверх карты, без изменения текущего поведения.

`Prototype_Main.uxml`: внутри `world-map` добавлены пять новых пустых `VisualElement`-слоёв (класс `world-map-layer` + собственный класс) в порядке отрисовки между существующими `world-map-terrain`/`world-map-routes`/`world-map-markers`:

`world-map-background → world-map-water → world-map-terrain → world-map-roads → world-map-decoration → world-map-routes → world-map-markers → world-map-fog`.

Пока это только заготовки под будущие этапы — `Background`/`Water`/`Roads` наполнятся вместе с WM-09 (Sprite Shape-реки/дороги), `Decoration` — вместе с WM-04 (`massVariants` из терраин-профилей, уже подготовленные в WM-01), `Fog` — с будущей визуализацией состояний знания (§9.1 канона: Неизвестно/Слух/Обнаружено/Исследовано/Изменено). Сейчас слои пустые, `pickingMode = Ignore`, не перехватывают клики и не влияют на `OnWorldMapPointerDown` (тот по-прежнему сверяет цель клика с `worldMap`/`worldMapTerrain`/`worldMapRoutes`/`worldMapMarkers` — остальные слои в клик-таргет физически попасть не могут).

`PrototypeUIController.WorldMap.cs`: добавлены поля `worldMapBackground/Water/Roads/Decoration/Fog`, находятся в `FindWorldMapElements`, очищаются в `RefreshWorldMapPanel` через `?.Clear()` (нет детей — нет эффекта). Рендер-логика (`DrawTerrainCells`, `CreateWorldMapNode`, `DrawRoute`) не менялась.

**Сознательно не делали в этом проходе:** вынос отрисовки в отдельный класс `WorldMapRenderer` (по архитектуре из канона) — отложен до WM-03, где всё равно понадобится `WorldMapCoordinateTransform`/`WorldMapViewportController` и переработка структуры рендера ради pan/zoom; делать это дважды нет смысла. Сейчас риск регрессии в уже работающей и покрытой тестами карте важнее архитектурной чистоты на пустом месте.

Компиляция/тесты не запускались с моей стороны — ждёт проверки в Unity, как WM-01.

**Статус проверки:** пользователь подтвердил в своём Unity Editor — компиляция без ошибок.

## 17. World Map 2.0 — WM-03 (большая карта + pan/zoom) — 12.09.2026

Третий этап из раздела 9.9 канона. Карта остаётся единым UI Toolkit-полотном (без перехода на Camera/world-space — решение зафиксировано в переписке до WM-01), pan/zoom реализован вручную поверх существующей percent-модели координат.

**Структура:** `Prototype_Main.uxml` — `world-map` (сама карта, все WM-02 слои, капитан, армия) теперь вложена в новый `world-map-viewport` (рамка фиксированного размера, `overflow: hidden`). `world-map-location-inspection-card` вынесена из `world-map` в `world-map-viewport` — это HUD-панель, закреплённая за углом экрана (`right/bottom`), она не должна панорамироваться/зумироваться вместе с картой, в отличие от кнопки столицы и маркера армии (те остаются percent-позиционированы внутри `world-map` и корректно едут вместе с картой).

`Prototype_Exploration.uss`: `.world-map-viewport` получила прежние размеры/рамку/фон `.world-map` (350px по умолчанию, растягивается в fullscreen через C#, как раньше). `.world-map` стала `position: absolute; left:0; top:0; width:100%; height:100%; transform-origin: 0% 0%;` — заполняет viewport при zoom=1, паном/зумом управляет код, а не layout.

**Новый файл `PrototypeUIController.WorldMapViewport.cs`:**

- Зум — колесо мыши, шаг 0.15, диапазон `[0.75; 2.5]`, привязан к точке под курсором (формула: конвертировать точку курсора в локальные координаты канваса до смены масштаба, затем пересчитать `panOffset` так, чтобы та же точка канваса осталась под курсором после смены).
- Панорама — **средняя кнопка мыши** (не левая — там мгновенный приказ похода; не правая — та занята осмотром локации по ПКМ, UI-M08), через `PointerDown/Move/Up` + `CapturePointer`.
- `ClampWorldMapPan()` не даёt карте уехать за рамку viewport; при zoom ≤ 1 карта центрируется.
- Технически зум/пан реализованы как `worldMap.style.scale` + `worldMap.style.left/top` (не Camera) — `WorldToLocal`, используемый в `OnWorldMapPointerDown` для перевода клика в проценты карты, уже учитывает полную трансформацию сам, поэтому клик-навигация не потребовала отдельной математики (`WorldMapCoordinateTransform` как отдельный класс не понадобился — UI Toolkit уже даёт это через `WorldToLocal`).

**Правки существующих файлов:** `PrototypeUIController.WorldMap.cs` — `ConfigureWorldMapFullscreenLayout()` теперь переставляет/растягивает `worldMapViewport` вместо `worldMap`; `WorldMapElementsExist()` и `Register/UnregisterWorldMapCallbacks()` учитывают viewport. `PrototypeUIController.WorldMapInteractionPolish.cs` — удалена задублированная подгонка `flexGrow/width/height` самого `world-map` в `RefreshWorldMapPresentation()` (стала бессмысленной, раз `world-map` теперь `position: absolute` со статичными 100% в USS; сама функция и её вызов `ConfigureWorldMapFullscreenLayout()` не убирались, файл по-прежнему не содержит `new VisualElement/Label/Button`/`RemoveFromHierarchy`, что требует regression-тест).

**Не менялось:** `DrawTerrainCells`, `CreateWorldMapNode`, `DrawRoute`, вся percent-модель координат (`XPercent/YPercent`), `WorldMapNavigation`, `GameState`. При zoom=1 без панорамирования (состояние по умолчанию при открытии экрана) карта выглядит и ведёт себя как до WM-03.

**Статус проверки:** пользователь подтвердил в Unity — колесо мыши зумит, средняя кнопка тащит карту в границах рамки, левый клик по-прежнему сразу отдаёт приказ похода.

## 18. World Map 2.0 — WM-04 (массы вместо клеток) и WM-06 (регионы как данные) — 12.09.2026

### WM-04 — местность массами, не сеткой

`DrawTerrainCells()` в `PrototypeUIController.WorldMap.cs` разделена на диспетчер `DrawTerrainForType` + два режима: `DrawTerrainFlatCells` (старое поведение WM-01 — плоская заливка по клетке) и новый `DrawTerrainMassClusters` в **`PrototypeUIController.WorldMapTerrainMasses.cs`**.

Логика: связные (4-directional flood fill) кластеры клеток одного типа местности → на каждый кластер 1–6 крупных спрайтов-"масс" (`cluster.Count / 4`, clamp 1..6), выбранных из `WorldMapTerrainVisualProfile.MassVariants`, со случайным (в пределах bounding box кластера) положением/масштабом (0.85–1.2)/поворотом (±8°). Сид детерминирован от `gameState.WorldSeed` + тип местности + индекс кластера — расстановка не "дрожит" между вызовами `RefreshWorldMapPanel` в течение одной партии.

**Включается автоматически, когда художник заполнит `MassVariants` в `KingdomSurvivalWorldMapTheme.asset`** (Editor, drag&drop, без кода) — пока список пуст (как сейчас), `DrawTerrainForType` использует старый `DrawTerrainFlatCells`, поведение идентично WM-01/WM-03. Новый USS-класс `world-map-terrain-mass` (`Prototype_Exploration.uss`) — размер ~2.5 клетки сетки, центрируется через отрицательные margin.

### WM-06 — регионы как данные

Новый файл **`Assets/_Project/Scripts/Core/WorldMapRegionDefinition.cs`** (чистый C#, без UnityEngine — `KingdomSurvival.Core` собран с `noEngineReferences: true`, так что визуальные ScriptableObject-типы туда пойти не могут): `WorldMapRegionDefinition` (Id/Name/прямоугольная зона в процентах) + статический `WorldMapRegionRegistry.Regions` — те же 4 региона с теми же именами и порядком проверки, что были захардкожены в `GameState.GetRegionName` (`x<34 → Запад; x>66 → Восток; y<40 → Север; иначе Центр`).

`GameState.GetRegionName(x, y)` теперь однострочник, делегирующий в `WorldMapRegionRegistry.FindRegion(x, y).Name` — **поведение не изменилось** для всех практических координат (единственная теоретическая разница — восточная граница здесь `x >= 66` вместо строгого `x > 66f`; сгенерированные позиции никогда не попадают ровно в 66.0, так что для реального EditMode-теста `RegionName_UsesFourExpectedMapAreas` разницы нет — прогнал все 4 тест-кейса вручную по новой логике, совпадают).

Сознательно не делали в этом проходе: не формализовал слоты появления (WM-07) и не менял `GameState.CreateNewGame`/размещение стартовых локаций.

**Статус проверки (задним числом, после подключения `unity-editor-mcp`):** полный `Run All` EditMode — **783/783 passed**, включая все 17 тестов по карте (`WorldMapNavigationTests`, `WorldMapLocationCardLayoutTests`, `WorldMapLocationCardStructureRegressionTests`) и все 4 кейса `RegionName_UsesFourExpectedMapAreas`. WM-04/WM-06 подтверждены рабочими без регрессии.

## 19. World Map 2.0 — WM-07 (Spawn Slots) и WM-08 (раздельные потоки WorldSeed) — 12.09.2026

С этого прохода доступен MCP-мост к живому Unity Editor пользователя (`unity-editor-mcp`) — компиляция, `Run All` и произвольный C# через `eval` проверялись напрямую, не "на честном слове" как раньше.

### WM-07 — Spawn Slots

Новый файл **`Assets/_Project/Scripts/Core/WorldMapSpawnSlotDefinition.cs`** (чистый C#, без UnityEngine — как и `WorldMapRegionDefinition`): `WorldMapSpawnSlotDefinition` (прямоугольная зона в процентах + `PickXPercent/PickYPercent`) и `WorldMapSpawnSlotRegistry.StartingLocationSlots` — три зоны (`slot-west/-north/-east`), примерно там же, где раньше были точки `candidatePositions`, но теперь это область, а не точка ± джиттер `±4/±3`.

`GameState.CreateNewGame`: вместо `candidatePositions[i] + Random.Next(-4,5)/(-3,4)` каждая локация получает случайную точку внутри своего слота (`slot.PickXPercent/PickYPercent`), затем снэпится к концу пути от столицы — снэп-логика не менялась.

### WM-08 — раздельные потоки WorldSeed

Добавлен `GameState.DeriveStreamSeed(worldSeed, tag)` — детерминированно производит сид под конкретный поток случайности. `ShuffleLocations` и выбор точки в слоте теперь используют `new Random(DeriveStreamSeed(WorldSeed, "location"))` вместо `new Random(WorldSeed)` напрямую — будущие источники случайности (декорации и т.п.) не будут делить последовательность с размещением локаций и случайно сдвигать его. Сохранение фактических координат в `Save` отдельно решать не пришлось: `LocationData.MapXPercent/MapYPercent` и так обычные сериализуемые поля, вычисляются один раз в `CreateNewGame` и хранятся как значения, а не пересчитываются из seed при загрузке — будущие изменения алгоритма генерации старые сохранения не тронут.

**Важно:** сид `WorldSeed` тот же самый, но конкретные координаты локаций для заданного `WorldSeed` теперь ОТЛИЧАЮТСЯ от значений до WM-07/08 (другой генератор случайности для того же входного числа) — это не регрессия (ни один тест/код не полагается на конкретные числа, см. разведку перед реализацией), но стоит знать, если кто-то сравнивал сохранения/логи до и после этого коммита.

### Проверено напрямую в Unity (не только `Run All`)

```
seed=1 ruins  x=13.72 y=24.05 (Западные земли) travelH=7.20
seed=1 mine   x=84.16 y=23.92 (Восточные земли) travelH=7.60
seed=1 forest x=48.53 y=6.58  (Северные земли)  travelH=5.20
seed=2 ruins  x=6.41  y=31.12 (Западные земли)
seed=2 mine   x=42.11 y=19.64 (Северные земли)
seed=2 forest x=89.84 y=18.55 (Восточные земли)
seed=3 ruins  x=20.98 y=12.22 (Западные земли)
seed=3 mine   x=53.79 y=5.85  (Северные земли)
seed=3 forest x=71.61 y=29.51 (Восточные земли)
```

Подтверждено через `eval`: (1) один и тот же seed даёт идентичный результат при повторном `CreateNewGame` — детерминированность есть; (2) координаты реально расходятся по всей площади слота между разными seed (например `ruins` по X — от 6.4 до 21.0 в пределах `slot-west` `[6,22]`), а не крутятся вокруг одной точки, как раньше; (3) `RegionName` (WM-06) корректно считается по новым координатам.

Полный `Run All` после WM-07/08: **783/783 passed, 0 failed**. Console: 0 errors, только уже известные предсуществующие warnings (obsolete API в BattleSandbox/UILayout, два CS0414-поля в WorldMap.cs) — ничего нового.

## 20. World Map 2.0 — WM-09 (река через ленту UI Toolkit-сегментов) — 12.09.2026

### Архитектурное отклонение от раздела 9.9 — согласовано с пользователем

Раздел 9.9 канона называл `Sprite Shape` способом рисовать реки. При реализации выяснилось: `SpriteShapeController`/`SpriteShapeRenderer` — GameObject-компоненты, рендерятся Camera; карта же целиком на UI Toolkit (`VisualElement`), решение WM-03. Настоящий `Sprite Shape` внутри `VisualElement`-дерева напрямую не работает. Пользователю предложены два варианта (лента из спрайтов в UI Toolkit vs `Sprite Shape` в offscreen-сцене → текстура для UI); выбран первый — карта остаётся полностью в одном технологическом стеке. **Канон обновлён**: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_32.md` (было v1.31 → архивирован в `ProjectDocs/Archive/`), пункт 9.9 переформулирован под реальную реализацию.

### Генерация — `Assets/_Project/Scripts/Core/WorldMapNavigation.cs`

Река — не влияющая на `GetTerrainTravelCost`/проходимость отдельная геометрия (геймплейный эффект — отдельное решение, не входит в WM-09). `GenerateRiver(worldSeed)`: случайное блуждание от точки на одном краю сетки 26×16 к противоположному, с общим сносом к центру и уклонением от защищённой зоны столицы (пропуск шага, не обрыв пути). Независимый XOR-сид от местности (`worldSeed ^ 0x5249564D`), кэшируется вместе с `terrainGrid` в `ConfigureTerrain`. Публично: `GetRiverPath()` (упорядоченная цепочка `(X,Y)`), `IsRiverAtGridCell(x,y)`.

### Визуал — новый модуль + новый файл контроллера

- `WorldMapWaterVisualProfile.cs` (`WorldMapVisual`, чистые данные: `SegmentSprite`, `FallbackColor`, `WidthPixels`) + поле `Water` в `WorldMapVisualTheme`. Без спрайта — заливка `FallbackColor` (та же деградация без арта, что и everywhere в WM-01/04).
- `PrototypeUIController.WorldMapRiver.cs`: `DrawRiver()` — на каждую пару соседних точек пути один повёрнутый `VisualElement` в `world-map-water` (слой из WM-02). Вызывается из `RefreshWorldMapPanel` после `DrawTerrainCells()`.

### Два реальных бага, найденных и исправленных живым Unity-мостом (не в отчёте "не проверено")

1. **Неверный угол/разрыв в стыках сегментов.** Первая версия считала угол/длину сегмента через `sqrt(dx%²+dy%²)`/`atan2(dy%,dx%)` напрямую в процентах. Проценты X и Y считаются от разных осей контейнера (ширина/высота) — на не квадратном viewport это даёт геометрически неверный угол. Обнаружено визуально (скриншот показал реку как два параллельных обрывка с разрывом ~35px вместо одной линии), подтверждено разбором конкретных чисел сегментов через `eval`. **Исправление**: разница переводится в реальные пиксели контейнера (`resolvedStyle.width/height`) перед `atan2`/`sqrt`, сегмент получил ширину в пикселях вместо процентов.
2. **NaN на первом кадре.** `resolvedStyle.width/height` до первого layout-прохода — не `0`, а `NaN`; `Mathf.Max(1f, NaN)` не защищает (сравнение с `NaN` всегда `false`, `Mathf.Max` возвращает именно `NaN`). Результат — сегменты нулевой длины с `rotate=NaN`, невидимая река. **Исправление**: явная проверка `float.IsNaN`, при неготовой геометрии — тихий выход и одноразовая подписка на `GeometryChangedEvent`, который очищает `world-map-water` и перерисовывает реку, как только реальный размер станет известен.

### Как проверялось (в этой сессии впервые доступен `unity-editor-mcp` — реальный Unity Editor, не "черновая честность")

- `GetRiverPath()` через `eval`: путь связный (все шаги соседние), детерминированный по seed, разной длины на разных seed (10–16 клеток в выборке).
- После обоих исправлений — `editor_play` → драйв контроллера через reflection (`OpenScreen(Expeditions)`) → `capture_game_view(source:"screen")`: река видна как один непрерывный отрезок с реалистичным изгибом, без разрывов и NaN.
- Полный `Run All` после каждого из трёх проходов (генерация / первая версия рендера / фикс геометрии) — **783/783 passed, 0 failed** каждый раз.
- Побочная находка (не баг, а особенность автоматизации): при вызове публичных методов контроллера через reflection сразу после `editor_play`, `OnEnable` иногда успевает отработать раньше, чем `UIDocument` заполнит `rootVisualElement` из `visualTreeAsset`, из-за чего закэшированные ссылки на элементы оказываются `null`. В реальной игре (без ручного driving через `eval`) это не проявляется — `OnEnable` и заполнение дерева синхронны в обычном порядке инициализации.

## 21. World Map 2.0 — WM-10 (минимальный редактор Database) — 12.09.2026

Последний пункт исходного плана WM-01…WM-10. Сознательно **не** делал: редактор полигонов/масок регионов, редактор Spawn Slots, интеграцию с Rule Tile — ни одно из этого не доказано нужным ("не нужен сложный редактор... на первом этапе", решение зафиксировано ещё при обсуждении архитектуры). Сделан минимум, который реально экономит время художника: посмотреть результат без Play Mode и поймать явные ошибки конфигурации до того, как они всплывут в игре.

Новый модуль `Assets/_Project/WorldMapVisual/Editor/` (`KingdomSurvival.WorldMapVisual.Editor`, платформа `Editor`, ссылается на `KingdomSurvival.Core` и `KingdomSurvival.WorldMapVisual`):

- **`WorldMapDatabaseWindow.cs`** — `Kingdom Survival → Карта → World Map Database`. Два раздела:
  - **Validate** — `CollectIssues(database)`: нет темы/иконотеки, дублирующийся профиль местности, у Hills/Mountains нет ни `MassVariants`, ни видимого `CellColor` (значит не отобразятся), у воды нет ни спрайта, ни видимого `FallbackColor`, в Icon Library — пустой/дублирующийся `LocationId` или запись без спрайта.
  - **Preview** — поле Seed + `WorldMapNavigation.ConfigureTerrain(seed)` + схематичный IMGUI-грид 26×16: цвет клетки берётся из назначенной темы (или дефолтный, если темы нет), река — поверх тем же способом, что и `FallbackColor` в рантайме. Не финальный визуал (это не UI Toolkit рендер игры, а быстрый инструмент подбора цветов/seed), но не требует Play Mode.

**Проверено напрямую в Editor** (`unity-editor-mcp`): открыл окно через `menu` (`Kingdom Survival/Карта/World Map Database`) — 0 новых ошибок в консоли (проверил `groundTruth.consoleErrors` до/после — старые ошибки в буфере от более раннего reflection-driven Play Mode тестирования WM-09, не от этого окна). Вызвал `CollectIssues` напрямую через `eval`: на нашем реальном `KingdomSurvivalWorldMapDatabase.asset` (из WM-01) — `0 issues` (конфигурация чистая); на `null` — корректно возвращает `"Не выбран World Map Database."`. Финальный `Run All`: **783/783 passed, 0 failed**.

### Итог World Map 2.0 (WM-01…WM-10)

Все десять этапов из раздела 9.9 канона реализованы и проверены живым Unity Editor в этой сессии: Visual Database (WM-01), слои рендера (WM-02), pan/zoom (WM-03), местность массами (WM-04), полный арт — на стороне художника, не код (WM-05), регионы как данные (WM-06), Spawn Slots (WM-07), раздельные потоки WorldSeed (WM-08), река через UI Toolkit-ленту (WM-09), минимальный Database-редактор (WM-10). Карта всё ещё не имеет реального арта (WM-05 не начат) — визуально она выглядит так же, как до этой серии проходов, пока в тему не добавят текстуры/спрайты; вся инфраструктура под них готова и не требует правок кода при подключении арта.

## 22. World Map 2.0 — WM-11 (карта ×16 по площади, старт с макс. зумом, редактируемая тема) — 12.09.2026

По запросу пользователя поверх готовой WM-01…WM-10: карта в 4 раза больше (по каждой оси, т.е. ×16 по площади), старт с максимальным зумом у столицы, редактирование спрайтов темы прямо в `World Map Database` вместо Inspector.

### Сетка ×4 по каждой оси — `WorldMapNavigation.cs`

`GridWidth 26→104`, `GridHeight 16→64`. Пропорционально (×4 линейно) масштабированы константы генерации, чтобы плотность местности не поредела: `HillClusterCount 8→32`, `MountainClusterCount 5→20`, длины кластеров холмов `4-8→16-32`, гор `3-6→12-24`, `ProtectedCapitalRadiusCells 2→8`. `WorldMapRegionDefinition`/`WorldMapSpawnSlotDefinition` не тронуты — они в процентах, от сетки не зависят.

**Найдена и исправлена системная проблема, не замеченная на этапе планирования:** время экспедиции в пути считается как `CalculateRouteCells(route) / CellsPerGameHour` (`ContinuousSimulationClock.cs`), а `CalculateRouteCells` — это количество точек маршрута, которое линейно зависит от разрешения сетки (`WorldMapNavigation.FindPath` строит точку на каждую клетку дистанции). Без компенсации то же процентное расстояние на карте стало бы отнимать в ~4 раза больше игрового времени на путешествие — тихая порча баланса, не только визуальный эффект. Исправлено масштабированием `ContinuousSimulationSystem.ArmyCellsPerRealSecond: 0.5 → 2.0` (×4, компенсирует ×4 клеток на тот же маршрут). Из-за этого 4 существующих EditMode-теста, буквально проверявших старое числовое значение константы (`ContinuousSimulationTests.Expedition_MovesHalfCellPerRealSecondAtNormalSpeed` → переименован в `...MovesTwoCellsPerRealSecondAtNormalSpeed`, `FastSpeed_TriplesClockAndArmyMovement`, `TimedExpeditionActivityTests.GatherBerries_...`, `TravelEstimate_UsesContinuousArmySpeed`), обновлены на новые ожидаемые числа (все ×4 от старых) — это ожидаемое сопровождение задокументированной смены константы, не сокрытие регрессии.

**Вторая найденная и исправленная проблема — производительность.** `DrawTerrainFlatCells` (fallback без арта, WM-01) создавала один `VisualElement` на каждую не-Plains клетку. На старой сетке — до ~416 клеток, на новой — уже 1768 клеток за один `RefreshWorldMapPanel`. Переписано на объединение клеток в строке в один прямоугольник (run-length по X) — `AddTerrainRunElement` в `PrototypeUIController.WorldMap.cs`. Заодно убран захардкоженный размер клетки в CSS (`4.5%/6.8%`, подобранный под старую сетку 26×16) — размер теперь считается из реального `GridWidth/GridHeight` в коде.

`PrototypeUIController.WorldMapTerrainMasses.cs`: `MaxMassesPerCluster 6→24` (×4) — кластеры на большей сетке крупнее в клетках, без этого потолок масс на кластер срабатывал бы значительно раньше и плотность казалась бы ниже (актуально только когда появится арт с `MassVariants`).

### Старт с максимальным зумом — `PrototypeUIController.WorldMapViewport.cs`

`worldMapZoom` по умолчанию — `WorldMapMaxZoom` (2.5) вместо `1f`.

**Найдена и исправлена ещё одна проблема, не покрытая изначальным планом:** при zoom > 1 канвас крупнее viewport, а `panOffset` по умолчанию `(0,0)` (левый верхний угол) — `ClampWorldMapPan` только не даёт краю уехать за рамку, но не центрирует ни на чём. Столица находится у (50%, 81%) — почти внизу карты, вне видимой при zoom=2.5 области по умолчанию. Результат без фикса: игрок при старте видит пустой угол карты, а не окрестности столицы. Добавлен `CenterWorldMapOn(CapitalXPercent, CapitalYPercent)`, вызывается один раз (`worldMapInitialFocusApplied`) при первом известном реальном размере viewport (`OnWorldMapViewportGeometryChanged`), до `ClampWorldMapPan`.

### Редактирование темы прямо в окне — `WorldMapDatabaseWindow.cs`

Окно WM-10 (было: только Validate + Preview) переведено на вкладки (`WindowTab.Theme/Validate/Preview`, тот же паттерн, что в `DialogueDatabaseWindow`). Новая вкладка **Тема**, через `SerializedObject`/`SerializedProperty` (Undo + корректная пометка ассета dirty — не прямая мутация полей):

- **Местность** — по секции на каждый `WorldMapTerrainVisualProfile`: `CellColor`, список `MassVariants` (добавить/удалить спрайт), кнопка "Удалить профиль", кнопки "+ Добавить профиль {Plains/Hills/Mountains}" для отсутствующих типов, поясняющий текст под секцией (CellColor — fallback, MassVariants — органичные массы WM-04).
- **Вода** — `SegmentSprite`, `FallbackColor`, `WidthPixels` + поясняющий текст.
- **Иконки локаций** — редактирует отдельный ассет `WorldMapIconLibrary` (своя `SerializedObject`, т.к. это отдельный ScriptableObject, не встроенное поле темы): `DefaultLocationIcon`, список записей (`LocationId` + `Sprite`, добавить/удалить), кнопка "Добавить недостающие id" по известному сейчас списку id локаций (`ruins/mine/forest`, из `GameState.CreateNewGame` — если состав локаций изменится, список в окне устареет и нужно поправить вручную).

Validate/Preview — без изменений логики, кроме динамического текста размера сетки в Preview (был захардкожен "26×16").

### Как проверялось

- Полный `Run All` после каждого шага (константы сетки/масс → 1 фейл в `TimedExpeditionActivityTests`; после компенсации скорости → 4 фейла с точным множителем ×4 у всех; после обновления тестов → **783/783 passed** — три чистых прогона, показывающих причинно-следственную цепь, а не один финальный "зелёный" без истории).
- `eval`: сетка `104×64`, река связная/детерминированная на новом размере (тот же метод проверки, что и в WM-09), `worldMap.style.scale=2.5` подтверждён напрямую из `resolvedStyle` в живой сессии Play Mode, `RefreshWorldMapPanel` — 10мс (не тормозит после run-length фикса).
- `SerializedObject`-редактирование через вкладку "Тема" проверено по-настоящему: добавил тестовый спрайт в `MassVariants` через тот же код, что использует окно, сохранил (`AssetDatabase.SaveAssets`), перечитал ассет **заново с диска** (не из кэша) — подтвердил, что сохранилось; откатил обратно, реальный ассет остался чистым.
- Console: 0 errors на всех этапах (кроме одного независимого от этой задачи эпизода зависания Play Mode из-за потока исключений в `RefreshDebugMenu` — известный артефакт reflection-driven автоматизации, уже отмеченный в §20 WM-09, не связан с этими изменениями).
- Визуальный Play Mode-скриншот карты целиком (с новой сеткой и стартовым зумом) не удалось получить в конце сессии — Play Mode переставал продвигать кадры при автоматизированном/расфокусированном запуске в этой конкретной сессии (воспроизводилось независимо от моих правок, включая уже наблюдавшееся ранее в WM-09 поведение). Логика подтверждена всеми методами выше; рекомендуется визуально проверить самостоятельно в Unity.

## 23. Encounter System — E01 (MVP + Phase 2) и P14-T01 (принудительный запуск) — 12.09.2026

Новый модуль `Assets/_Project/Encounters/` (Runtime/Editor/Tests, свой asmdef, не тянет Unity-зависимости в `KingdomSurvival.Core`) — данные, чистый Selector, Runtime Service, Editor Database Window, минимальный Gameplay Effects слой поверх существующего `NarrativeEffect`.

### E01 — фундамент

`EncounterDefinition` (Identity/Selection/Location/Conditions/Memory, плюс `DurationClass`/`MemoryClass`/`Functions` из Reactive Encounter Layer), `EncounterPoolDefinition`, `EncounterFlagDefinition`/`EncounterFlagRegistryAsset` (Active/Reserved/Deprecated), `EncounterRuntimeStateData` (встроено полем в `GameState.Encounters` — Scripts/Core, т.к. Core ни на что не реферит и не может ссылаться на модуль Encounters). `EncounterEligibilityEvaluator` переиспользует существующий `NarrativeConditionGroup`, вторая система условий не создавалась. `EncounterSelector` — pure (pool trigger → eligibility → discovery roll → weighted pick), детерминированный seed через `EncounterDeterministicRandom` (FNV-1a, не `UnityEngine.Random` и не `string.GetHashCode()` — оба нестабильны между процессами/сборками). `EncounterRuntimeService` — единственное место побочных эффектов; occurrence и `FlagsSetOnStart` применяются строго после успешного открытия диалога, не на этапе выбора.

### Gameplay Effects слой

`NarrativeEffectType` расширен: `GrantItem`/`RemoveItem` (готовые методы `NarrativeStateData`), `ChangeFood`/`ChangeSupplies` (мутируют `GameState`, клампятся на 0), `ShortcutRouteCells` (тот же `WorldMapNavigation.AdvanceRouteByCells`, которым уже пользовался legacy-код напрямую). Потребовало добавить `GameState GameState { get; }` в `NarrativeEvaluationContext` — опциональный параметр в конец конструктора (тот же приём, что уже был у `PartySize`), прокинут через `NarrativeDialogueRuntimeSession.Start` → `PrototypeUIController.TryOpenNarrativeDialogueById`. `AdvanceTime` сознательно не добавлен — игровые часы живут в приватном `RuntimeState` `ContinuousSimulationSystem`, трогать вслепую не стали.

### Editor Database Window

`Kingdom Survival → Энкаунтеры` — две вкладки (Энкаунтеры/Флаги), список+инспектор+диагностика по образцу `BattlefieldDatabaseWindow` (UI Toolkit, `SerializedObject`/`PropertyField`, не IMGUI). Diagnostics: Eligibility Preview без Play Mode (ручной Preview Context) и Monte Carlo Simulator (N прогонов `EncounterSelector` с одним контекстом, только `OpportunityId` варьируется). Flag Registry: dependency viewer (Created by/Read by/Cleared by) + кросс-валидация «Active-флаг без потребителя» / «флаг используется, но не зарегистрирован». Весь текст интерфейса — на русском.

### Контент

`ROAD_WARM_SHEEP_01` — production vertical slice (18 узлов диалога, 2 активные проверки, 12 флагов, ветвящаяся структура по авторской версии пользователя от 12.09.2026). Плюс перенесены 10 происшествий и 3 решения, ранее захардкоженных в `ExpeditionIncidentSystem`/`ExpeditionDecisionSystem` (`rats/hunt/bad_water/cache/washed_road/short_path/cold_rain/torn_bags/fishing/good_crossing/unmapped_fork/berry_bushes/hungry_travelers` → `ROAD_*_01`) — это перенос, не копия: старые записи удалены из `Definitions` обеих legacy-систем, чтобы контент не дублировался между системами. `road_predator` и `location_discovered` не тронуты — первый завязан на `NarrativeCheckResolver`, второй структурный игровой узел, не декоративная сцена. Положительная задержка маршрута (Road Stop activity с часами — была у `washed_road`/`cold_rain`/`safe_road`/`gather_berries`) не воспроизведена механически: `ExpeditionData.ActiveActivity` пересекается с паузой/модальной очередью, добавлять как общий Gameplay Effect не стали без отдельной проверки — зафиксировано в `FutureHooksNotes` соответствующих Encounter.

**Найденная и исправленная регрессия:** `TimedExpeditionActivityTests.GatherBerries_...`/`SafeRoadDecision_...` полагались на удалённые decision-записи `berry_bushes`/`unmapped_fork`. Тесты по факту проверяли generic-механику Road Stop activity, а не контент — переписаны на прямой вызов `GameState.TryStartRoadActivity` (то же самое, что делал старый код внутри `TryApplyChoice`), тестовое покрытие сохранено.

### P14-T01 — принудительный запуск для тестирования

Добавлена кнопка **«ВЫЗВАТЬ ДОРОЖНЫЙ ENCOUNTER»** в debug-панель (`PrototypeUIController.Debug.cs`) рядом с уже существующими «ВЫЗВАТЬ ФОНОВОЕ ПРОИСШЕСТВИЕ»/«ВЫЗВАТЬ ЗНАЧИМОЕ СОБЫТИЕ». `TryDebugForceRoadEncounter` (`PrototypeUIController.Encounters.cs`) гоняет тот же runtime-путь, что и реальная игра (`EncounterRuntimeService.SelectEncounter → RecordSelectionPacing → TryOpenNarrativeDialogueById → RecordEncounterStarted`), не открывает Narrative UI в обход системы — единственное отличие от настоящего пути в том, что `OpportunityId` синтетический (свежий GUID на попытку), чтобы не упираться в дедупликацию «эта Opportunity уже обработана» при повторных нажатиях в рамках одного дня.

**Важное архитектурное решение, принятое в этой сессии:** production-инструкция для P14-T01 (полученная от пользователя) описывала `AuthoredEncounterDefinition`/`AuthoredEncounterRunner` как новую систему поверх `ExpeditionIncidentSystem`. Реализация этой инструкции буквально создала бы вторую параллельную систему событий — ровно то, что сама инструкция запрещает в своём каноне, — потому что `EncounterDefinition`/`EncounterRuntimeService`/`EncounterSelector` уже реализуют весь описанный контракт (стабильный ID, region binding, eligibility до выбора, once/repeatable, one line at a time, идемпотентные эффекты, persisted outcome через флаги). Из всей инструкции реализован только реально отсутствовавший кусок — Force-кнопка; остальное признано уже существующим, не продублировано.

Save/Load-критерии инструкции (persisted completion/outcome переживает Save→Load) физически не проверялись — **в проекте на данный момент нет системы сохранений вообще** (ни `JsonUtility`, ни `BinaryFormatter`, ни `SaveGame`-подобного кода не найдено). Это не пробел Encounter-системы — это общий пробел всей игры, не закрывался молча в рамках этой задачи.

### Как проверялось

Впервые в этом проекте — **живой MCP-мост к Unity Editor пользователя, настроенный в этой же сессии** (`unity pipeline install` + `unity mcp configure claude-code`), а не только статический анализ. Полный `Run All` EditMode через `unity command run_tests` дважды подряд после всех правок Encounter-системы (включая force-кнопку): **783/783 passed, 0 failed** — оба прогона зафиксированы, между ними Editor на короткое время переставал отвечать на `eval`/`test_status` (зависание главного потока, не связанное с код-изменениями — отошло само, второй прогон подтвердил чистое состояние). Компиляция проверена напрямую (`UnityEditor.EditorUtility.scriptCompilationFailed == false`) после каждого пакета правок, а не только по факту отсутствия ошибок в тестах. `EncounterDatabaseAsset.CollectValidationIssues` прогнан на реальном ассете через `eval` — 0 проблем. Состояние ассетов подтверждено напрямую из живого Editor: 14 энкаунтеров, 1 пул, 13 флагов, 36 диалогов (общая база, не только Encounters) — не голословно, а фактическим `db.Encounters.Count` и т.д. в момент проверки.

## 24. World Map 2.0 — WM-12 (масштаб «1 клетка = 1 сутки», столица/армия ½ клетки, зум до 10×) — 12.09.2026

По запросу пользователя поверх WM-11: скорректирован темп движения, размер маркеров столицы/армии, максимальный зум — всё выведено из единого правила «1 клетка маршрута = 1 сутки игрового времени».

### Темп — `Assets/_Project/Scripts/Core/ContinuousSimulationClock.cs`

`ArmyCellsPerRealSecond` заменён с подобранной компенсации (`2.0`) на прямое определение: `1.0 / RealSecondsPerGameDay` (≈0.008333) — `RealSecondsPerGameDay` (120) реальных секунд как раз и составляют одни игровые сутки, так что клетка проходится ровно за них на обычной скорости. Даёт `CellsPerGameHour ≈ 0.041667` (1/24 — клетка = 24 часа на Plains). Холмы/горы автоматически становятся 2/3 суток на клетку через уже существующий `WorldMapNavigation.GetTerrainTravelCost` (под-точки маршрута в `FindPath`) — не трогался.

**Побочные эффекты, найденные и осознанно принятые (не баги):**
- Любой переход длиной ≥1 клетки теперь обязательно пересекает минимум одну полночь — раньше (при старом на порядки более быстром темпе) это было физически невозможно в пределах теста/короткой игровой сессии. Дневной расход снабжения похода (`GameState.ExpeditionSupplyConsumption` — бойцы+1) и автоматическое принудительное возвращение при нехватке снабжения (раздел 9.7 канона, "Экспедиционный риск без level scaling") теперь реально срабатывают там, где раньше не успевали.
- Дальние маршруты (например до `ruins` или до `downstream_settlement`, ~10-15% карты) на новой шкале занимают недели игрового времени — тесты, которые доходят до таких точек, теперь должны явно выдавать экспедиции запас снабжения (`state.ArmySupply = 1000`), иначе легитимно обрывают поход раньше времени.

### Тесты — 11 упавших после смены константы, разобраны по одному, не скопом

Полный список (все находятся и чинятся по отдельности через `unity-editor-mcp`, не угадыванием чисел): `ContinuousSimulationTests.Expedition_Moves...`/`FastSpeed_...` (длительность `Advance` пересчитана на "сутки", а не старые секунды; вторая — с сохранением исходной проверки утроения тика часов), `TimedExpeditionActivityTests.GatherBerries_.../SafeRoadDecision_.../TravelEstimate_.../WaypointArrival_...`, `StabilityRegressionTests.ContinuousMovement_ArrivalStops.../Discovery...` (обёртка ожидаемого часа по модулю 24 — прежняя формула `StartHour + 1.0/CellsPerGameHour` при новом значении даёт `32.0`, чего `HourOfDay` физически не может показать после `ResolveMidnight`), `Chapter01P10Tests.DownstreamRoute_...`, `ContinuousMovementTimeTests.ArrivalStopsMovement_...`, `ContinuousTimePolishTests.PreparedRoster_...` (порог `HasExpeditionStartedMoving` — `dx²+dy²>0.0001` — требует чуть больше реального времени при новом медленном темпе).

Особенно показательный случай — `GatherBerries_...`: после исправления длительности `Advance` тест всё ещё падал (`ArmySupply` ожидалось `13`, получилось `8`). Разведка через `eval` показала: `10 (старт) + 3 (награда сбора ягод) - 5 (дневной расход снабжения похода из 4 бойцов+командир) = 8` — легитимное следствие пересечения полуночи, которое раньше физически не происходило в окне теста. Ожидание переписано на `8` с объяснением, а не подогнано вслепую.

### Столица и армия — ½ клетки, позиция из реальной константы

Найден и попутно исправлен баг, не связанный напрямую с запросом: CSS-позиция кнопки столицы была захардкожена (`left:37%; top:74%`), а реальная точка столицы для пути и центрирования камеры (WM-11) — `WorldMapNavigation.CapitalXPercent/YPercent = 50/81`. Не совпадали.

`Prototype_Exploration.uss`: `.world-map-capital` и `.world-map-army-marker` лишились статичных `left/top/width/height` (были `26%×48px` и `74px×38px` соответственно — на сетке 104×64 в разы больше клетки-суток); `border-radius: 50%` у столицы (круглая точка, не прямоугольник — текст "СТОЛИЦА" в ½ клетки физически не помещается, перенесён в `tooltip`).

`PrototypeUIController.WorldMap.cs`, `RefreshWorldMapCapital`/`RefreshWorldMapArmyMarker`: размер (`½ × 100/(GridWidth-1)` по ширине, `½ × 100/(GridHeight-1)` по высоте) и позиция (центрирование на `WorldMapNavigation.CapitalXPercent/YPercent` для столицы, на `expedition.CurrentMapXPercent/YPercent` для армии) считаются в коде из реального разрешения сетки, не хардкодятся — тем же приёмом, что местность/река/маски в предыдущих WM-этапах.

### Зум — до 10×, мультипликативный шаг

`PrototypeUIController.WorldMapViewport.cs`: `WorldMapMaxZoom: 2.5 → 10`. Формула шага колеса мыши сменена с аддитивной (`zoom ± 0.15`) на мультипликативную (`zoom *= 1.15^±1`) — на диапазоне `[0.75;10]` (почти в 13 раз шире прежнего `[0.75;2.5]`) аддитивный шаг потребовал бы ~62 нотча колеса до максимума; мультипликативный даёт равномерное ощущение на любом уровне приближения. `WorldMapMinZoom` не менялся.

### Как проверялось

- Полный `Run All` на каждом контрольном шаге (11 упавших → 6 → 3 → 0), а не один финальный прогон — история фактически отражает, что каждая правка была осмысленной, а не подгонкой чисел до зелёного.
- `eval` в живом Editor: `ArmyCellsPerRealSecond`/`CellsPerGameHour` совпадают с расчётными (0.008333/0.041667), 1 клетка = ровно 120 реальных секунд на обычной скорости.
- `eval` (Play Mode): `worldMapCapitalButton` — `width=0.4854369%`, `height=0.7936508%` (ровно половина клетки), `left=49.75728`, `top=80.60317` (ровно центрировано на `CapitalXPercent=50/CapitalYPercent=81`) — не предположение, а фактическое значение `style` у живого элемента. `worldMapArmyMarker` — тот же размер. `worldMapZoom`/`WorldMapMaxZoom` — оба `10`.
- Причину `GatherBerries_...`/`SafeRoadDecision_...`/`WaypointArrival_...`/`Chapter01P10Tests`/`ContinuousMovementTimeTests`-падений (переход в `ReturningToCastle` вместо ожидаемого состояния) нашёл не угадыванием, а прямым воспроизведением сценария через `eval` — увидел, что `GetTravelHoursRemaining` после "прибытия" не ноль, а фаза `ReturningToCastle`: экспедиция реально повернула домой по нехватке снабжения.
- Финальный Play Mode-скриншот снова не удалось снять (та же особенность продвижения кадров при автоматизации, что и в конце WM-11/WM-09-сессии, воспроизводится независимо от кода) — вся проверка выше сделана через `eval`-инспекцию реального состояния живых UI-элементов, а не скриншотом.

### Пост-релизная правка — реальный визуальный баг, найденный пользователем

Проверка через `style.width/left` (см. выше) оказалась недостаточной: она подтверждала, какое значение **запрошено**, а не какое реально **вычислено layout'ом** (`resolvedStyle`). Пользователь прислал скриншот с гигантским кругом столицы (~4 клетки) — `eval`-проверка `resolvedStyle.minWidth` в его живой сессии показала `Auto`, а не `0`: встроенный `min-height`/`min-width` темы Unity `unity-button` побеждал наш маленький `height` из-за порядка применения стилшитов (не специфичности) — USS-правило `min-width: 0` в `.world-map-capital`/`.world-map-army-marker` не срабатывало. Исправлено переносом `min-width`/`min-height: 0` в **inline-стиль в коде** (`RefreshWorldMapCapital`/`RefreshWorldMapArmyMarker`) — inline-стили в UI Toolkit гарантированно старше любого USS-правила независимо от порядка загрузки стилшитов.

Также нашлась и исправлена вторая, независимая проблема: `.world-map-route-dot(-active/-preview)` остались на старых `5-7px`, подобранных под прежний максимум зума `2.5×` — при новом `10×` те же px превращались в точки размером с клетку. Уменьшены до `2-3px`.

После этого пользователь всё ещё видел точки крупнее ожидаемого — по прямому пользовательскому указанию ("нужно в 10 раз меньше") доля клетки для столицы/армии уменьшена **ещё в 10 раз**: `0.5 → 0.05` от клетки, вынесено в единую именованную константу `MapMarkerCellFraction` (`PrototypeUIController.WorldMap.cs`) вместо повторяющегося магического числа в двух местах — дальнейшая подстройка теперь одним числом.

Отдельно проверено через `eval` (не на глаз): `expedition.Route[0]` после `TryStartExpeditionToMapPoint` **точно равен** `WorldMapNavigation.CapitalXPercent/YPercent` — старт похода из столицы подтверждён на уровне данных; жалоба "герой начинает не в столице" на момент проверки не воспроизвелась как баг данных, вероятно относится к уже прошедшему времени пути на скриншоте (при "1 клетка = 1 сутки" смещение от столицы за прошедшее время заметно) либо к кадрированию скриншота.

`Run All` после каждого шага этой правки — стабильно 783/783. Итоговый визуальный результат (после уменьшения ещё в 10 раз) пользователем на момент записи ещё не подтверждён.

## 25. World Map 2.0 — WM-15 (стабильная 1px screen-space сетка при zoom) — 13.09.2026

После WM-14 пользователь подтвердил, что геометрия квадратных клеток работает, но при разных уровнях zoom сетка в UI Toolkit вела себя нестабильно: на одном масштабе были видны обе оси, на другом — только горизонтальные полосы, на третьем линии исчезали полностью.

Причина находилась не в квадратном canvas, а в способе рисования grid: линии были дочерними элементами масштабируемого `world-map`, а их толщина компенсировалась как `1px / zoom`. При больших масштабах UI Toolkit получал фактические размеры элементов `0.5/0.2/0.1px` до transform и нестабильно растрировал их.

Исправление в `Assets/_Project/UI/PrototypeUIController.WorldMapPolish.cs`:

- `world-map-grid-overlay` перенесён из масштабируемого `world-map` непосредственно в `world-map-viewport` как screen-space overlay;
- линии сетки теперь всегда имеют реальную толщину `1px`, без `1/zoom`;
- позиции линий пересчитываются из `worldMapPanOffset + gridIndex * WorldMapBaseCellSizePx * worldMapZoom`;
- координаты линий округляются до целого экранного пикселя;
- невидимые за пределами viewport линии скрываются, а видимые ограничиваются реальной областью canvas;
- пересчёт вызывается через существующий `ApplyWorldMapViewportTransform()` при первом открытии, resize, pan и zoom;
- route markers остаются внутри `world-map` и сохраняют отдельную компенсацию собственного экранного размера.

Не менялись квадратный canvas WM-14, `WorldMapNavigation`, маршрут, логическое положение столицы/армии, `1 клетка = 24 игровых часа`, динамический `maxZoom` и общая UI Toolkit-архитектура карты.

**Проверка в этой сессии:** выполнен статический аудит diff и связей partial-класса; изменение ограничено `PrototypeUIController.WorldMapPolish.cs`. Доступа к живому Unity Editor/MCP в этой сессии нет, поэтому фактическая компиляция, EditMode `Run All` и ручной Play Mode не запускались. После Pull обязательно проверить min/mid/max zoom и pan: обе оси сетки должны оставаться видимыми, клетки — квадратными, толщина — визуально 1px, а route/столица/армия — не смещаться относительно клеток.

## 26. World Map 2.0 — WM-16 (поселение, мелкий пунктир и стабильный герой) — 13.09.2026

После живой проверки WM-15 пользователь подтвердил, что квадратные клетки и screen-space сетка выглядят правильно. Следующий визуальный проход касается только маркеров карты и не меняет навигацию/время.

Изменения:

- размер столицы/поселения отделён от героя и установлен примерно в `0.25` логической клетки (`CapitalMarkerCellFraction = 0.25`);
- крупные route-node точки убраны из визуализации: логические узлы `route[i]` остаются в данных, но игрок видит только мелкий частый пунктир;
- пунктир уменьшен до `1.5px` экранного диаметра и уплотнён до `7` штрихов на клетку, с расчётом плотности по реальной длине сегмента в клетках;
- активный маршрут рисуется начиная с текущего сегмента (`RouteIndex`), поэтому точка героя движется непосредственно по пунктирной траектории;
- маркер героя отделён от размера клетки: `ArmyMarkerScreenDiameter = 8px`, размер компенсируется как `8px / zoom` внутри масштабируемого `world-map`, так что после transform остаётся одинаковым на экране;
- центр героя теперь задаётся точными `CurrentMapXPercent/YPercent`, а центрирование самого кружка выполняется симметричными отрицательными margin, поэтому ширина/высота не зависят от осей карты и не должны растягиваться/сжиматься во время движения;
- `RefreshWorldMapZoomCompensatedVisuals()` теперь вместе с route dots пересчитывает и размер героя при каждом изменении zoom.

Не менялись `WorldMapNavigation`, `GameState`, скорость экспедиции, `1 клетка = 24 игровых часа`, квадратный canvas, pan/zoom, сетка WM-15 и логические точки маршрута.

**Проверка в этой сессии:** статически перечитаны итоговые участки `PrototypeUIController.WorldMap.cs` и `PrototypeUIController.WorldMapPolish.cs`, проверены ссылки partial-класса и удаление зависимости визуала от прежней общей `MapMarkerCellFraction`. Живой Unity Editor/MCP в этой сессии недоступен, поэтому компиляция, EditMode `Run All` и Play Mode не запускались. После Pull проверить: поселение ≈¼ клетки; маршрут выглядит как очень мелкий равномерный пунктир без крупных узлов; герой остаётся круглым и одного размера при движении и min/mid/max zoom; герой идёт по линии маршрута без визуального смещения.

## 27. World Map 2.0 — WM-17 (screen-space герой и пунктир внутри клетки) — 13.09.2026

Живая проверка WM-16 выявила тот же класс субпиксельной проблемы, который раньше был у сетки: при максимальном zoom герой мерцал, а маршрут мог полностью исчезнуть. Причина — `8px / zoom` и `1.5px / zoom` внутри масштабируемого `world-map`: при сильном приближении UI Toolkit получал элементы меньше одного layout-пикселя до transform.

Добавлен `Assets/_Project/UI/PrototypeUIController.WorldMapScreenSpaceTravel.cs` (+ `.meta`) как узкий визуальный слой без изменения навигации/симуляции:

- старый `world-map-routes` остаётся runtime-совместимым для существующих `DrawRoute/FadeNextRoutePoint`, но скрыт визуально;
- старый `worldMapArmyMarker` сохраняется для существующей activity/tooltip-логики, однако фон самой старой точки делается прозрачным;
- поверх `world-map-viewport` создаётся отдельный screen-space route overlay и отдельный screen-space hero marker;
- герой имеет настоящий постоянный размер `8×8px`, без деления на zoom; его экранная позиция вычисляется из `CurrentMapXPercent/YPercent + pan + zoom` и округляется до целого пикселя для устранения shimmer;
- маршрут рисуется точками `2px` с постоянным экранным шагом `8px`, поэтому при максимальном zoom внутри одной логической клетки видны десятки точек вместо прежних фиксированных `7` на клетку;
- первый видимый отрезок строится от **фактической текущей позиции героя**, затем продолжается к `Route[RouteIndex+1]` и по оставшимся сегментам — движение внутри одной клетки читается напрямую;
- визуал обновляется через UI Toolkit scheduler каждые `16ms`; герой обновляется постоянно, а пунктир перестраивается только при изменении маршрута/RouteIndex/pan/zoom или смещении героя минимум на `2px`;
- route overlay и hero marker имеют `PickingMode.Ignore`, поэтому не вмешиваются в левый клик приказа, ПКМ осмотра и панорамирование;
- карточка осмотра локации принудительно остаётся поверх новых screen-space слоёв.

Не менялись `WorldMapNavigation`, `GameState`, `ContinuousSimulationSystem`, правило `1 клетка = 24 игровых часа`, квадратный canvas, поселение `0.25` клетки и screen-space сетка WM-15.

**Проверка в этой сессии:** diff статически проверен; изменение добавляет только новый partial-файл контроллера и его `.meta`, плюс эту запись журнала. Живой Unity Editor/MCP в текущей среде недоступен, поэтому компиляция, `Run All` и Play Mode не запускались. После Pull проверить на max zoom: герой не мерцает и остаётся 8px-кругом; пунктир виден внутри одной клетки и начинается непосредственно от текущей позиции героя; при pan/zoom маршрут остаётся привязан к тем же мировым координатам.

## 28. World Map Database + shell UI (WM-18 / UI-M09) — 13.09.2026

По запросу пользователя объединены три связанных правки: река у стартового поселения, авторская база текстур/локаций и упрощённая постоянная оболочка.

### Река и стартовое поселение

`WorldMapNavigation.GenerateRiver` больше не обходит защищённую окрестность столицы. Для каждого `WorldSeed` река связывает два противоположных края карты и обязательно проходит через клетку-ориентир в диапазоне не дальше двух клеток от `CapitalXPercent/CapitalYPercent`. Путь остаётся связным по 8 соседям. Добавлены EditMode-тесты на 81 seed и на связность/противоположные края.

### World Map Database

`Карта → World Map Database` теперь имеет четыре простые вкладки:

- **Текстуры** — фон карты (спрайт, fallback-цвет, tint), профили равнин/холмов/гор, текстура/цвет/ширина реки и резервная иконка;
- **Локации** — список реальных стартовых `LocationData` с добавлением/удалением, id, названием, описанием, угрозой, временем исследования, наградами, стартовой видимостью/открытием, spawn slot, спрайтом, tint и масштабом иконки;
- **Проверка** — дубли/пустые id, пустые имена, отрицательные значения, неизвестные spawn slots, диапазон масштаба иконок и прежние проверки темы;
- **Предпросмотр** — seed, местность, река, столица и иконки всех стартовых локаций; скрытые в начале игры маркеры показаны полупрозрачными только в Editor Preview.

Новый `WorldMapLocationTemplateData` оставлен в Core без Unity-зависимостей. `WorldMapDatabaseAsset` преобразует свои `WorldMapLocationDefinition` в эти шаблоны, а `PrototypeUIController.StartNewGame` передаёт их в `GameState.CreateNewGame`. Поэтому добавленная в Editor Window локация входит в следующую новую игру, создаёт маркер на карте и карточку в быстром списке. Старая `WorldMapIconLibrary` сохранена как fallback. Добавлены тесты загрузки ассета и передачи произвольной локации в `GameState`.

### Оболочка и пауза

- заголовок ленты переименован в `ДОНЕСЕНИЯ`; надписи `KINGDOM SURVIVAL` больше нет;
- постоянная панель командира удалена из UXML, USS, C#-биндинга и `KingdomSurvivalUILayouts.asset`; донесения занимают её прежнее место внизу слева;
- в нижней панели остались только `Столица`, `Карта`, `Герой`, `Журнал`, `Лагерь` и индикатор дня/времени; старая скрытая кнопка времени сохранена как runtime-служебный узел, но игроку не показывается;
- золото, пища, население, настроение, доходы/расходы и отладочные кнопки перенесены в отдельную панель экрана `Столица`;
- кнопка `ЛОКАЦИИ` на карте сохраняет быстрый список известных/видимых мест;
- `Журнал` и `Герой` теперь вызывают `PauseForBlockingModal` при открытии и `ResumeAfterBlockingModalIfReady` при закрытии. Аудит остальных блокирующих экранов подтвердил ту же политику у лагеря, диалога, взаимодействия с локацией, происшествия и обязательного решения. `Столица`, `Карта` и быстрый список локаций намеренно не ставят время на паузу.

### Проверка в этой сессии

- `git diff --check` — чисто;
- XML-parser успешно прочитал все 12 UXML-файлов, JSON-parser — все asmdef;
- статически проверены структура shell, отсутствие legacy-панели командира/старых заголовков, состав нижней панели и инварианты реки на 2001 seed;
- добавлены EditMode regression-тесты для реки, базы локаций, shell-разметки и паузы Journal/Hero.

## 21. Переход на авторскую карту (World Map 3.0) — AM-01…AM-10 — 13.09.2026

Канон обновлён до **v1.33**: §9.9 переформулирован — глобальная карта переходит с процедурной генерации по `WorldSeed` на авторскую постоянную географию (рельеф, реки, леса, поля, основные дороги, Дом, крупные поселения и ориентиры больше не генерируются при старте партии). v1.32 перенесён в `ProjectDocs/Archive/`. Вариативность остаётся только в размещении допустимых малых локаций/событий. Источник: инструкция по миграции AM-01…AM-10, приложенная пользователем 13.09.2026.

### AM-01 — Контракт мира [СДЕЛАНО И ПРОВЕРЕНО]

- `Scripts/Core/WorldMapCoordinates.cs` — единственное место преобразования процентных координат карты (0..100) в клетки логической сетки (`PercentToGridX/Y`, обратные функции, геометрическое расстояние). `WorldMapNavigation` теперь делегирует сюда вместо собственных приватных `PercentToGridX/Y`.
- `Scripts/Core/WorldMapDefinitionData.cs` — чистый (без UnityEngine) контракт авторского мира: `WorldDefinitionId`, `GeographyVersion`, размер сетки (для валидации — сама сетка размером не меняется, см. ограничение №3 инструкции), `HomeLocationId/HomeXPercent/HomeYPercent`, списки регионов/`WorldMapTerrainAreaData`/точек реки/spawn-слотов. `WorldMapTerrainAreaData` — авторская прямоугольная зона расчётной местности (аналог уже существующих `WorldMapRegionDefinition`/`WorldMapSpawnSlotDefinition`, без полигонов, по решению инструкции).
- `WorldMapNavigation.ConfigureFromDefinition(WorldMapDefinitionData)` — новый вход, строящий рельеф (`BuildAuthoredTerrain`) и реку (`BuildAuthoredRiver`) из авторских данных вместо случайных `GenerateTerrain`/`GenerateRiver`. Старый `ConfigureTerrain(seed)` не менялся и остаётся переходным адаптером для кода, который ещё не подключён к авторскому миру (пока это весь текущий `GameState.CreateNewGame` — подключение к нему запланировано на AM-04).
- `WorldMapVisual/Runtime/WorldMapWorldDefinitionAsset.cs` — ScriptableObject-обёртка (`CreateAssetMenu`: `Kingdom Survival/Карта/World Definition`) с `ToData()`, преобразующим авторские Unity-поля в `WorldMapDefinitionData`. Ссылка `ActiveWorld` добавлена в `WorldMapDatabaseAsset` (может быть `null` до AM-04).
- Новые тесты `WorldMapDefinitionDataTests` (3 шт.): один и тот же `WorldMapDefinitionData` даёт идентичный рельеф независимо от того, каким seed был сконфигурирован рельеф раньше; область вне авторских зон остаётся Plains; река из авторских опорных точек — непрерывная цепочка соседних клеток.

**Сознательно не делали:** размер сетки (`GridWidth`/`GridHeight` = 104×64) остался константой — не стал динамическим, чтобы не трогать все места, которые сейчас на него полагаются как на compile-time значение; `WorldMapNavigation.CapitalXPercent/YPercent` не удалены и не заменены новым `HomeXPercent/YPercent` — 16 файлов кода/тестов их читают напрямую, полная миграция потребителей на единый источник Дома — задача AM-02/AM-04, не AM-01; сам `WorldMapWorldDefinitionAsset` пока ни к чему не подключён (никакой ассет ещё не создан художником, `GameState.CreateNewGame` продолжает работать по-старому).

**Проверено:** `recompile` — чисто на каждом шаге; полный `Run All` EditMode после AM-01 — **795 тестов: 794 passed / 1 failed**; упавший `BuildingSystemTests.BarracksRecruitOneReplacementFighterOverTime` (ожидание 368, получено 369 золота) не относится к карте.

**Побочный фикс (не карта):** `BarracksRecruitOneReplacementFighterOverTime` не был флаком — детерминированный баг, оставшийся от WM-13 (ускорение `RealSecondsPerGameDay` 120→60, т.е. игра стала течь в 2 раза быстрее реального времени). Тест по-прежнему продвигал время на старые 10 реальных секунд, что на новом темпе пересекало **две** полночи вместо одной (28 против ожидаемых ~20 игровых часов), поэтому в кассу дважды падал доход и один раз — содержание казарм, отсюда лишний +1 золота. Поправлено на 7 реальных секунд (28 игровых часов — казармы гарантированно достроены, ровно одна полночь). После фикса: `BuildingSystemTests` — 3/3 passed.

### AM-02 — Авторский тестовый участок [СДЕЛАНО И ПРОВЕРЕНО]

- `GameState.CreateNewGame` получил новый опциональный параметр `worldDefinition` (`WorldMapDefinitionData`). Если он валиден — география идёт через `WorldMapNavigation.ConfigureFromDefinition`, а `WorldSeed` остаётся только для будущего наполнения (AM-04); без него — прежнее процедурное поведение AM-01/до-миграции, ни один существующий вызов `CreateNewGame` не сломан (параметр опционален, по умолчанию `null`).
- `PrototypeUIController.StartNewGame` передаёт `mapDatabase.ActiveWorld?.ToData()` третьим аргументом.
- `WorldMapWorldDefinitionAsset` получил editor-only методы заполнения (`EditorSetWorldId/EditorSetHome/EditorAddTerrainArea/EditorAddRiverPoint` и парные `Clear`) — задел под будущий редактор (AM-03), уже пригодный для скриптового заполнения.
- Создан реальный тестовый ассет `WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalTestWorldDefinition.asset` (`am02-test-world`): Дом на прежних `CapitalXPercent/YPercent = 50/81`, две авторские зоны местности (`test-hills-west` — Hills в x[8,30]×y[40,72], `test-mountains-north` — Mountains в x[35,58]×y[4,24]), река тремя опорными точками через окрестность Дома. Подключён как `ActiveWorld` в живом `KingdomSurvivalWorldMapDatabase.asset` через `unity-editor-mcp eval` (`SerializedObject`, не вручную в Inspector — художнику ещё нечем сделать это самостоятельно, это AM-03).

**Проверено через живой `eval` в Editor:** `GameState.CreateNewGame` с этим миром при seed `111` и при seed `999999` даёт **идентичный** рельеф (`GetTerrainAtGridCell` в тестовой точке) и одинаковую длину пути реки (105 клеток в обоих случаях) — география больше не зависит от `WorldSeed`, как и требует обновлённый канон. Отдельно подтверждено, что клетка внутри `test-hills-west` действительно `Hills`, а внутри `test-mountains-north` — `Mountains`. Полный `Run All` EditMode — **795/795 passed, 0 failed**.

**Сознательно не делали:** это один тестовый участок для проверки контракта, не production-география первого региона — ни местность, ни имена ниоткуда не объявляются лором. Расстановка малых локаций (Anchored/Temporary, `WorldMapPopulationService`) не подключена — `GameState.CreateNewGame` по-прежнему раскладывает `WorldMapLocationTemplateData` по старым `WorldMapSpawnSlotRegistry` (AM-04). Редактора для художника ещё нет — ассет заполнен служебным скриптом через `eval`, а не Inspector-UI (AM-03).

### AM-03 — Редактор: разделы «Мир» и «География» [СДЕЛАНО И ПРОВЕРЕНО В ЖИВОМ EDITOR]

`WorldMapDatabaseWindow` (`Kingdom Survival → Карта → World Map Database`) получил две новые вкладки поверх прежних четырёх (`Текстуры/Локации/Проверка/Предпросмотр`):

- **«Мир»** — назначение/создание `WorldMapWorldDefinitionAsset` как `Active World` прямо из окна (кнопка «+ Создать новый World Definition» через `SaveFilePanelInProject`, без Inspector), редактирование `World Definition Id`/`Geography Version`/`Home Location Id`/координат Дома. Без Active World явно показывает предупреждение о переходном процедурном поведении (AM-01).
- **«География»** — список авторских зон местности (`TerrainAreas`: ID, тип местности, Priority, прямоугольник в процентах, добавление/удаление) и опорных точек реки (`RiverPathPercent`: добавление/удаление/редактирование). Ровно то, что раньше приходилось заполнять через `eval`.
- **«Проверка»** расширена `CollectWorldIssues`: пустой `WorldDefinitionId`, координаты Дома вне 0..100%, пустой/дублирующийся ID зоны, `Min >= Max` по любой оси, **явная проверка пересечения зон с одинаковым Priority** (та самая проверка, которую инструкция требует не оставлять на порядок объектов в списке), одна точка реки вместо минимум двух.
- **«Предпросмотр»** переведён на `ConfigureFromDefinition` при наличии `Active World` вместо безусловного `ConfigureTerrain(seed)` — Seed-поле блокируется и явно помечено как влияющее только на будущее наполнение (AM-04), не на географию. Добавлено предупреждение о том, что общий статический `WorldMapNavigation` всё ещё может временно подменить географию работающей Play Mode-партии, если окно открыто на вкладке «Предпросмотр» одновременно — известное ограничение, снимается только в AM-10 (перенос с статического адаптера).

**Проверено в живом Editor (не только компиляцией):**
- Reflection-вызов `CollectIssues`/`CollectWorldIssues` на реальной базе — 0 проблем на валидных данных; на намеренно сконфликтованном тестовом мире (две зоны с одинаковым Priority и пересечением) — ровно 1 найденная проблема с точным текстом.
- Окно открыто через реальный `menu` (`Kingdom Survival/Карта/World Map Database`), `Repaint()`/`Focus()` вызваны в настоящем цикле событий Unity (не через прямой reflection-вызов `OnGUI`, который закономерно падает вне цикла IMGUI-событий даже на немодифицированном коде) — консоль Editor: **0 errors** после открытия и репейнта.
- Полный `Run All` EditMode после AM-03 — **795/795 passed, 0 failed**.

**Сознательно не делали:** разделы «Локации и ориентиры» (расширение под Fixed/Anchored/Temporary), «Слоты наполнения», «Зоны встреч», «Знание и слухи» из инструкции — они требуют данных, которые появятся только в AM-04 (`WorldMapPopulationService`, зоны Encounter). Полигональный редактор границ не вводился — по решению инструкции достаточно прямоугольников. Не проверено визуально человеком (цвет/выравнивание полей, удобство кликов) — у меня нет глаз на реальный интерфейс, только подтверждение отсутствия исключений и корректности данных.

### AM-04 — WorldMapPopulationService [СДЕЛАНО И ПРОВЕРЕНО]

- Новый чистый сервис [WorldMapPopulationService.cs](../Assets/_Project/Scripts/Core/WorldMapPopulationService.cs) (Core, без UnityEngine): расстановка новой партии вынесена из `GameState.CreateNewGame` — `GameState` по-прежнему создаёт кампанию и вызывает сервис, но не отвечает за то, как выбираются точки.
- `WorldMapLocationTemplateData` получил `WorldMapPlacementMode` (Anchored/Fixed/Temporary) и `FixedXPercent/FixedYPercent`. **Anchored** — прежнее поведение WM-07 (точка внутри авторского слота). **Fixed** — точные координаты, не зависящие от `WorldSeed`. **Temporary** — не участвует в стартовом наполнении (появление по зонам Encounter — AM-08, ещё не реализовано); это явная, задокументированная граница, а не тихий пропуск.
- **Исправлен баг из §3 инструкции:** `RegionId` раньше был `"sector-" + индекс` — техническая метка, не совпадавшая с `GetRegionName`/`WorldMapRegionRegistry`. Теперь `RegionId` — настоящий Id региона (`west`/`east`/`north`/`center`), где физически оказалась точка; UI и будущие зональные фильтры Encounter больше не могут спутать одно место с двумя разными регионами.
- **Исправлена стабильность порядка (§10 инструкции):** расстановка Anchored-локаций сортируется по Id перед перемешиванием, а не зависит от порядка записей в базе художника — добавление/переупорядочивание локаций в редакторе больше не сдвигает расстановку остальных при том же seed.
- `WorldMapLocationDefinition` (редактор) и вкладка «Локации» получили выбор `Placement Mode` и поля `X%/Y%` для Fixed — не нужно больше настраивать это через `eval`.
- 4 новых теста (`WorldMapPopulationServiceTests`): Fixed-локация на точных координатах независимо от seed; `RegionId` совпадает с `WorldMapRegionRegistry`, а не начинается с `"sector-"`; Temporary исключена из стартового пула; расстановка Anchored не зависит от порядка входного списка (только от Id).

**Проверено:** полный `Run All` EditMode — **799/799 passed, 0 failed** (795 + 4 новых). Сквозной сценарий через живой `eval` с реальной базой (`KingdomSurvivalTestWorldDefinition`) — `RegionId` трёх стартовых локаций корректно `west/north/east`, координаты в границах авторских слотов.

**Сознательно не делали:** резервирование пространства (footprint/capacity слотов) не введено — Anchored по-прежнему может теоретически совпасть с другим слотом при неудачном round-robin, это уже существовавшее с WM-07 ограничение, а не новый дефект AM-04. Temporary-локации нигде ещё не создаются (нет зон Encounter — AM-08). `WorldMapRuntimeStateData` как отдельная структура сохранения результата размещения не введена — результат по-прежнему хранится там же, где раньше (`GameState.Locations`), полноценный Save/Load — AM-05.

### AM-05 — Save/Load [СДЕЛАНО И ПРОВЕРЕНО]

Инструкция прямо предупреждала: простая сериализация `GameState` не сохранит скрытое время/поток случайности, а `System.Random` нельзя считать восстановленным только по seed. Проверка эмпирически подтвердила ровно эти риски — и ещё один, не названный явно в инструкции.

**Найденные и исправленные конкретные баги (не гипотезы — воспроизведены через живой `JsonUtility` в Editor):**

1. **`ContinuousSimulationClock`**: час дня/пауза/скорость/прогресс текущего сегмента маршрута и поток случайности (`ScheduleDailyChecks`) живут в приватном `RuntimeState` внутри `ConditionalWeakTable<GameState, RuntimeState>` — простая сериализация `GameState` их не видит.
2. **`BuildingSystem`**: то же самое устройство (свой `ConditionalWeakTable`) для состояния построек (`Dictionary<string, BuildingStateData>`) и найма — тоже вне `GameState`, тоже терялось бы молча.
3. **`System.Random` нельзя восстановить только по seed после использования** — решено не заменой на другой генератор (это изменило бы уже проверенное поведение `TerrainGeneration`/`River`-тестов), а подсчётом количества «шагов» потока (`RandomDrawCount`) и прокруткой свежего `Random(seed)` на то же число шагов при загрузке. Работает только потому, что при явном `seed` в конструкторе `Random` .NET использует алгоритм с гарантированной обратной совместимостью (не Xoshiro) — соответствие проверено тестом, сравнивающим фактическое продолжение после Save/Load с продолжением без него.
4. **Не названный явно в инструкции, но найденный экспериментально баг:** `UnityEngine.JsonUtility` не умеет сериализовать `null` для полей-ссылок — вместо null на выходе оказывается фиктивный default-объект **уже на этапе `ToJson`**, а не только после `FromJson`. Проверено `eval`: `state.ActiveExpedition = null` → после `ToJson`/`FromJson` `restored.ActiveExpedition` **не null**. `HasActiveExpedition` в данном случае спасает флаг `IsActive` внутри объекта — но `ExpeditionData.ActiveActivity == null`/`PendingDecision == null` таким флагом не защищены: без явного маркера `restored.ActiveExpedition.HasTimedActivity` после загрузки становится **true**, хотя реально таймера не было. Это реальный, а не теоретический риск: минимум 3 места в коде (`GameState.cs:1005`, `NarrativeEffects.cs:101`, `ExpeditionIncidentSystem.cs:327`) читают эти поля напрямую.

**Реализация:**

- `ContinuousSimulationSnapshotData` + `ContinuousSimulationSystem.ExportSnapshot/RestoreSnapshot` ([ContinuousSimulationClock.cs](../Assets/_Project/Scripts/Core/ContinuousSimulationClock.cs)).
- `BuildingSystemSnapshotData` + `BuildingSystem.ExportSnapshot/RestoreSnapshot` ([BuildingSystem.cs](../Assets/_Project/Scripts/Core/BuildingSystem.cs)) — `Dictionary` превращён в `List<BuildingStateData>` для снимка (JsonUtility не умеет Dictionary; `BuildingStateData` уже содержит `BuildingId`, ключ не теряется). `Notices` (одноразовые UI-тосты) сознательно не сохраняются.
- `CampaignSaveData`/`CampaignSaveService` ([CampaignSaveData.cs](../Assets/_Project/Scripts/Core/CampaignSaveData.cs), [CampaignSaveService.cs](../Assets/_Project/Scripts/Core/CampaignSaveService.cs)) — чистый C# в Core (без `UnityEngine`/`JsonUtility`), явные маркеры `HasActiveExpedition/HasActiveActivity/HasPendingDecision` для честного восстановления null.
- Unity-слой файла: [PrototypeUIController.CampaignSave.cs](../Assets/_Project/UI/PrototypeUIController.CampaignSave.cs) — `JsonUtility.ToJson/FromJson`, запись во временный файл с `File.Replace` (безопасная замена + `.bak`), проверка версии формата и совпадения `WorldDefinitionId` перед подменой текущей партии, переприменение авторской географии (`WorldMapNavigation.ConfigureFromDefinition`) после восстановления. Единственный слот сохранения (`Application.persistentDataPath/campaign.save.json`) — без менеджера профилей, по решению инструкции ("не нужен для первого рабочего результата").
- Кнопки «СОХРАНИТЬ ПАРТИЮ»/«ЗАГРУЗИТЬ ПАРТИЮ» подключены в существующее debug-меню ([PrototypeUIController.Debug.cs](../Assets/_Project/UI/PrototypeUIController.Debug.cs)). **Отклонение от инструкции, отмеченное явно:** инструкция просила разместить их в «существующем меню паузы» — такого экрана в проекте пока нет вообще (проверено поиском), поэтому кнопки временно в debug-меню (доступно в Editor/Development Build). Перенос в постоянное игровое меню — отдельная задача UI, не часть AM-05.

**Проверено:**
- 5 новых тестов (`CampaignSaveServiceTests`) через настоящий `JsonUtility.ToJson/FromJson` (не имитация): восстановление истинного `null` для `ActiveExpedition`; восстановление истинного `null` для `ActiveActivity`/`PendingDecision` на реальной активной экспедиции; сохранение реальных `ActiveActivity`/`PendingDecision`, когда они есть; **детерминированное продолжение потока случайности** после Save/Load (сравнение с продолжением без Save/Load — оба дают идентичные `ExpeditionIncidentCheckHour`/`ExpeditionDecisionCheckHour`/`RandomDrawCount`); прогресс стройки/найма переживает Save/Load.
- Полный `Run All` EditMode — **804/804 passed, 0 failed**.
- Сквозной прогон через `eval` на реальной файловой системе (`Application.persistentDataPath`): запись, `File.Replace` с созданием `.bak`, чтение, восстановление — золото/день/seed/час совпадают в реальном файле, не только в памяти.

**Сознательно не делали:** полноценный менеджер сохранений/слотов, блокировка сохранения во время обязательного модального окна (задокументированное ограничение первой версии — если окажется проблемой на практике, инструкция явно разрешает такое временное ограничение), перенос старых процедурных сохранений на авторскую карту (конвертер), реальный экран паузы вместо debug-меню.

### AM-06 — Масштабы/LOD и UI (частично) [СДЕЛАНО, ЧАСТИЧНО ПРОВЕРЕНО]

Реализован только конкретный, проверяемый без арта срез раздела 9/7 инструкции — навигация и зум карты. LOD-политика видимости подписей/меток, фильтры (задания/следы/угрозы/лагеря) и «Атлас/Регион/Путешествие/Подробно» как явные уровни детализации **не реализованы** — это отдельная, более крупная задача, требующая уже настоящих локаций разных типов и знания карты (AM-07), а не только зума.

**Сделано:**

- [WorldMapViewport.cs](../Assets/_Project/UI/PrototypeUIController.WorldMapViewport.cs): нижняя граница зума (`worldMapMinZoom`) больше не фиксированная константа 0.75 — считается в `RecalculateWorldMapMinZoom()` как `min(viewportWidth/canvasWidth, viewportHeight/canvasHeight)`, то есть гарантированно "вся карта помещается в viewport" на кнопке «Вся карта», независимо от размера полотна.
- Старт партии — среднее геометрическое между min и max зумом (`sqrt(minZoom·maxZoom)`) вокруг Дома, а не принудительный максимальный зум, как раньше (раздел 9 инструкции: "Нынешний принудительный старт на максимальном приближении заменить"). Дом для нового кода — `ActiveWorld.HomeXPercent/YPercent`, если авторский мир подключён (AM-01/02), иначе прежний `WorldMapNavigation.CapitalXPercent/YPercent`.
- Кнопки «К герою», «К Дому», «Вся карта», `+`/`−` и компактный индикатор масштаба (% от максимального приближения) — новые элементы в [Prototype_Main.uxml](../Assets/_Project/UI/Prototype/Prototype_Main.uxml)/[Prototype_Exploration.uss](../Assets/_Project/UI/Prototype/Prototype_Exploration.uss), логика в `WorldMapViewport.cs`/`WorldMap.cs`. Зум колесом и зум кнопками теперь используют один и тот же метод `ZoomWorldMapAroundScreenPoint` (раньше было только колесо) — кнопки зумируют от центра viewport, колесо — от курсора, как раньше.
- **Исправлен реальный, а не гипотетический баг:** вспомогательная сетка (`world-map-grid-overlay`, WM-15) рисовалась **всегда**, когда была видна на экране — не было ни переключателя, ни состояния "выключено по умолчанию", хотя раздел 9 инструкции прямо требует "на обычном игровом масштабе сетка выключена". Добавлен `Toggle` «Сетка» (по умолчанию `false`) и поле `worldMapGridEnabled`, `RefreshWorldMapGridOverlay` теперь ничего не рисует, пока переключатель не включён явно.

**Проверено:**
- Компиляция чистая, полный `Run All` EditMode — **804/804 passed** (эта часть AM-06 не меняет Core, поэтому число тестов не выросло — сама логика проверяется ниже, не unit-тестами).
- Через живой `eval` (не Play Mode): все новые UXML-элементы (`world-map-nav-controls` и все кнопки/лейбл/тоггл внутри) реально существуют в дереве и находятся по `name` — разметка не сломана; `world-map-grid-toggle.value == false` сразу после загрузки сцены — исправление бага подтверждено на уровне данных.
- **Не проверено визуально в Play Mode.** Была предпринята попытка: `editor_play` → вход в Play Mode, вызов навигации на экран карты и принудительные `QueuePlayerLoopUpdate()`/`RepaintAllViews()` — `world-map-viewport.resolvedStyle.width/height` остались `0` даже после нескольких попыток и пауз. Это то же самое, независимое от конкретных правок ограничение автоматизированного Play Mode в этой среде, что уже задокументировано в §20 (WM-09) и §21 WM-11 ("Play Mode переставал продвигать кадры при автоматизированном/расфокусированном запуске") — не новая проблема этой сессии. Из-за этого не удалось вживую проверить: реальное позиционирование при клике "Вся карта"/"К Дому"/"К герою", визуальную читаемость кнопок, поведение zoom-индикатора при реальном клике. Формулы проверены статическим разбором и повторным использованием уже проверенной (WM-14) математики поворота вокруг точки, но это не замена скриншоту.

**Рекомендация:** при следующей ручной сессии в Unity открыть карту и глазами проверить: kнопки не перекрывают ЛОКАЦИИ (лево vs право), "Вся карта" действительно показывает всю карту без белых полей за краем, индикатор процента совпадает с ощущением приближения.

### AM-07 — Знание и слухи (только слухи, без бумажной маски территории) [СДЕЛАНО И ПРОВЕРЕНО]

Раздел 12 инструкции описывает две разные вещи под одним заголовком: (1) слухи — сужающиеся стадии поиска конкретного места и (2) бумажный слой/маска, скрывающая подробную землю за пределами обследованного пути. Реализована только первая часть — она чистая логика, проверяемая тестами. Вторая часть требует настоящего парного арта (приблизительная география + подробная) и мягкой маски с рендером — авторского арта пока нет, строить рендер под несуществующий контент не стал (тот же принцип, что и в AM-06 с LOD/фильтрами).

**Сделано:**

- `WorldMapSearchAreaDefinition`/`WorldMapSearchStageDefinition` ([WorldMapSearchAreaDefinition.cs](../Assets/_Project/Scripts/Core/WorldMapSearchAreaDefinition.cs)) — авторские сужающиеся прямоугольные стадии поиска, каждая привязана к списку требуемых флагов знания.
- `WorldMapKnowledgeService.GetActiveStage` ([WorldMapKnowledgeService.cs](../Assets/_Project/Scripts/Core/WorldMapKnowledgeService.cs)) — **не вводит новую систему знаний**: переиспользует уже существующий `NarrativeStateData.Flags` (тот самый, что уже сохраняется в Save/Load без дополнительного снимка и уже идемпотентен во всём остальном нарративе). Один resolver, побеждает последняя достигнутая по порядку списка стадия — соответствует требованию раздела 12 "все потребители смотрят в один resolver видимости".
- `WorldMapSearchAreaValidator` ([WorldMapSearchAreaValidator.cs](../Assets/_Project/Scripts/Core/WorldMapSearchAreaValidator.cs)) — реализует прямое требование инструкции: "Валидатор проверяет, что достоверная область включает реальную цель и следующая стадия не расширяется случайно". Прямоугольники, не универсальный геометрический решатель — по решению инструкции.
- 6 тестов (`WorldMapKnowledgeServiceTests`): стадия сужается по мере знания флагов; повторное применение уже известных флагов не меняет результат (идемпотентность); валидатор ловит и "область не включает цель", и "стадия расширяется вместо сужения".

**Проверено:** полный `Run All` EditMode — **810/810 passed, 0 failed**.

**Сознательно не делали (не бумажная отговорка — реальные зависимости):**
- Бумажный слой/маска подробной земли — нужен парный арт (approximate + detailed) и рендер мягкого края, которых нет.
- Раскрытие вдоль реально пройденного отрезка (не всего маршрута) — требует того же слоя.
- UI-рендер активных стадий поиска на карте (`world-map-fog`, пустой слой-заготовка с WM-02) — не подключал, так как ни одна реальная локация ещё не имеет авторского `WorldMapSearchAreaDefinition` (нет контента для показа, только тестовые данные в юнит-тестах) — рисовать рендер под несуществующий контент означало бы непроверяемый мёртвый код.
- Редактор для художника (вкладка «Знание и слухи» в `WorldMapDatabaseWindow`) — тот же принцип: нет данных, которые в нём заполнять.

### Отключение процедурной/авторской реки — 13.09.2026

По прямому указанию пользователя: карта переходит на принцип «нарисованная карта — единственный источник географии». Река, озёра, берега перестают быть кодом (ни процедурная генерация WM-09, ни авторская ломаная точек AM-01) — они будут частью художественного полотна `Background Map Art`, которое нарисует художник. До появления этого арта река в системе вообще не учитывается — ни визуально, ни как gameplay-данные.

**Канон поднят до v1.34** (`KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_34.md`, v1.33 → `ProjectDocs/Archive/`): §9.9 уточнён — река/озёра/берега больше не описываются как «авторская геометрия, реализуемая средствами UI Toolkit» (формулировка v1.33), а как часть вручную нарисованного полотна карты; будущее геймплейное распознавание воды — через невидимую ручную разметку клеток, а не порождаемую кодом геометрию.

**Удалено из runtime:**
- `WorldMapNavigation`: `GenerateRiver`, `AppendRiverLeg`, `BuildAuthoredRiver`, поля `riverPath`/`riverCellLookup`, методы `GetRiverPath()`/`IsRiverAtGridCell()`, вызовы генерации реки из `ConfigureTerrain`/`ConfigureFromDefinition`.
- `WorldMapDefinitionData.RiverPath` — река больше не часть контракта авторского мира.
- `WorldMapWorldDefinitionAsset`: `riverPathPercent`/`RiverPathPercent`, `EditorAddRiverPoint`/`EditorClearRiverPath`, соответствующий кусок `ToData()`.
- `PrototypeUIController.WorldMapRiver.cs` — файл целиком удалён (`DrawRiver()` был единственным содержимым, вызов убран из `RefreshWorldMapPanel`).
- `WorldMapWaterVisualProfile.cs` — файл целиком удалён (использовался только процедурной рекой); поле `WorldMapVisualTheme.water` убрано.
- Редактор (`WorldMapDatabaseWindow.cs`): секция «Вода (река)» на вкладке «Текстуры», раздел «Река — опорные точки» на вкладке «География», рендер реки и `GetPreviewWaterColor()` в Preview, Validate-проверки `theme.Water`/`RiverPathPercent.Count == 1`.
- USS-класс `.world-map-river-segment` (использовался только удалённым `DrawRiver`).
- Тесты, существовавшие только для проверки реки: `WorldMapNavigationTests.River_AlwaysPassesWithinTwoCellsOfStartingSettlement`, `River_ConnectsOppositeEdgesAndRemainsContinuous`, `WorldMapDefinitionDataTests.ConfigureFromDefinition_BuildsRiverConnectingAuthoredPoints`.

**Сознательно НЕ трогали:** движение героя, клетки карты, время перемещения (`GetTerrainTravelCost`/`GetTerrainSpeedMultiplier`), регионы (`WorldMapRegionRegistry`), расположение локаций (`WorldMapPopulationService`), Spawn Slots, pan/zoom, маршрут (`FindPath`/`CalculateRouteCells`), систему координат (`WorldMapCoordinates`) — ничего из этого не читало и не зависело от реки, кроме самого рендера. Слой `world-map-water` (VisualElement, WM-02) оставлен в UXML как пустой контейнер — часть 5-слойной модели канона, годится для будущей невидимой разметки, но сейчас ничего в него не пишется.

**Задел на будущее (по инструкции — не реализовывать сейчас):** после готового арта карты вкладка «Разметка → Вода» в `World Map Database` должна дать возможность вручную отметить клетки как River (не рисуя их), и `IsRiverAtGridCell(x, y)` должен читать эту ручную разметку вместо (не существующего более) генератора. Явно не строил эту систему сейчас — по признанию самой инструкции, это увеличило бы объём задачи без данных, на которых её проверять.

**Проверено:** полный `Run All` EditMode — **807/807 passed, 0 failed** (810 − 3 удалённых теста реки = 807, сходится). Компиляция чистая. Сквозной прогон через `eval`: `CreateNewGame` с реальной базой (`KingdomSurvivalTestWorldDefinition`) по-прежнему создаёт 3 локации, местность в авторских зонах (`Hills`) и координаты Дома (50/81) — без единой ссылки на реку в рантайме.

### AM-07.5 — Авторская география рельефа/местности (промежуточный этап перед AM-08) [СДЕЛАНО И ПРОВЕРЕНО]

По прямому указанию пользователя: художник рисует **весь** постоянный визуал карты вручную (не только реку — холмы, горы, лес, поля, дороги тоже), код хранит только невидимую gameplay-разметку. Канон поднят до **v1.35** (`KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_35.md`, v1.34 → `ProjectDocs/Archive/`): §9.9 доведён до принципа "нарисованная карта = единственный визуальный источник истины" для всей постоянной географии, не только воды.

**Удалено из runtime (было — до этого прохода — переходным авторским/процедурным слоем, теперь полностью убрано, не спрятанным fallback'ом):**
- `WorldMapNavigation.GenerateTerrain`/`PaintClusters`/`PaintCell`/`IsProtectedCapitalCell` и константы кластеров/радиуса столицы — процедурная генерация Hills/Mountains по seed удалена целиком.
- `ConfigureTerrain(int worldSeed)` заменён на `ConfigureDefaultTerrain()` — без параметра, без какой-либо зависимости от seed: без авторского мира география теперь **всегда** сплошная Plains, а не проверенно-другой-но-всё-ещё-процедурный откат. Обновлены все 6 вызовов в продакшн-коде (`GameState.CreateNewGame`, `ContinuousExpeditionCommands` — оказался вовсе избыточным, `FindPath` и так вызывает `EnsureTerrainConfigured`, — и 4 места в UI, сведённые к одному новому хелперу `PrototypeUIController.EnsureWorldMapGeographyConfigured()`).
- **Визуальный рендер рельефа кодом убран целиком**, не только процедурный: `DrawTerrainCells`/`DrawTerrainForType`/`DrawTerrainFlatCells`/`AddTerrainRunElement` (плоские клетки) и весь файл `PrototypeUIController.WorldMapTerrainMasses.cs` (WM-04, разбрасывание спрайтов-"масс" по кластерам клеток) — визуальным источником истины теперь безусловно является `baseMapSprite`, а не что-либо, что код рисует поверх логической сетки (тот же принцип, что уже применён к реке).
- `PrototypeUIController.GeneratedTerrain.cs` — файл целиком удалён (ASCII-плейсхолдер ▲/⌃ поверх карты с 50мс поллингом); `RefreshLocationTravelEstimatesForTerrain`, который он вызывал, оказался ненужным вовсе — с фиксированной авторской географией расстояние от Дома до локации не может протухнуть после размещения, пересчитывать нечего.
- `WorldMapTerrainVisualProfile.cs` — файл целиком удалён (Cell Color + Mass Variants), поле `WorldMapVisualTheme.terrainProfiles`/`FindTerrainProfile` убрано — вкладка «Местность» темы, её Validate-проверки и раскраска в Preview соответственно тоже.
- USS-классы `.world-map-terrain-cell`, `.world-map-terrain-mass`.

**Добавлено (невидимая gameplay-разметка вместо визуала):**
- `WorldMapTerrainAreaData.Tags`/`WorldMapWorldDefinitionAsset.TerrainAreaEntry.Tags` — чисто описательные контекстные теги (Forest/Field/Shore/Road и др.) поверх уже существующих авторских зон местности; не участвуют в `GetTerrainTravelCost` — только для подбора слотов.
- `WorldMapSpawnSlotDefinition.Tags`/`HasAllTags` и `WorldMapLocationTemplateData.RequiredSlotTags` — Anchored-локация с требованием тегов выбирает случайный слот **только** среди слотов, содержащих все требуемые теги (не round-robin по всем слотам); без требований — прежнее поведение без изменений.
- `WorldMapWorldDefinitionAsset.SpawnSlots` (новый `SpawnSlotEntry` с тегами) — авторские слоты мира вместо захардкоженного `WorldMapSpawnSlotRegistry`, когда автор их задаст; `WorldMapPopulationService.Populate` получил опциональный параметр `slots`, `GameState.CreateNewGame` передаёt `worldDefinition.SpawnSlots`. Registry остаётся запасным вариантом, когда авторских слотов нет (сейчас — всегда, ни один реальный мир их ещё не заполнил).
- Редактор: вкладка «География» получила раздел «Слоты появления малых локаций» (по образцу зон местности) и компактное поле тегов через запятую (`DrawTagsField`) — использовано и для зон местности, и для слотов, и для `RequiredSlotTags` на вкладке «Локации». Validate проверяет дубли ID и `Min<Max` слотов (по аналогии с зонами местности).

**Проверено:**
- 3 новых теста: `ConfigureDefaultTerrain_IsEntirelyPlainsEverywhere`, `Populate_AnchoredLocationWithRequiredTagsOnlyUsesCompatibleSlots` (20 разных seed — локация с требованием тега `Forest` ни разу не попадает в слот без него), `Populate_NamedSpawnSlotIdWinsOverRequiredTags`. Удалены 4 теста, проверявшие теперь несуществующее поведение (стабильность процедурной генерации по seed, защита окрестности столицы, кластеризация Hills/Mountains, "заданный seed побеждает протухший").
- Полный `Run All` EditMode — **806/806 passed, 0 failed** (807 − 4 удалённых + 3 новых = 806, сходится).
- Сквозной `eval`: `CreateNewGame` без авторского мира — 0 клеток не-Plains на всей сетке (было бы физически невозможно проверить на процедурной версии — там всегда были кластеры); с реальным тестовым миром — рельеф/локации/Дом работают как раньше.

**Сознательно не делали:** полноценный редактор «Разметка → Вода» для клеточной маски реки (задел из предыдущего прохода, по-прежнему не реализован — не увеличивать объём задачи без реального арта, как явно разрешено инструкцией). Ручная художественная приёмка не считается выполненной — тестовые зоны/слоты доказывают техническую цепочку, а не финальный визуал; ждём загрузки настоящего арта карты.

## WM-T01…WM-T04 — Gameplay-география дорог — 13.09.2026

Отдельная задача от пользователя (не AM-этап): дороги как невидимая gameplay-разметка поверх нарисованной художником карты, с модификатором скорости — без A*/графа дорог/снаппинга/тайлов. Реализованы все 4 этапа (WM-T01…WM-T04); WM-T05 (Road Network) и WM-T06 (Optimal Route) **не реализованы**, как и было условлено.

**Важное архитектурное решение, зафиксированное перед реализацией:** в проекте уже существовал механизм "стоимость местности" (`WorldMapTerrainType` Plains/Hills/Mountains, `GetTerrainTravelCost`), но он работает **не так**, как просил пользователь для дорог — стоимость печётся ОДИН РАЗ в `WorldMapNavigation.FindPath` как дополнительные точки маршрута, а не как живой запрос текущей позиции. Дороги — узкая полоса, которую эта грубая ~1%-сетка almost наверняка не поймала бы. Поэтому дороги реализованы как **независимый, отдельный слой** (`WorldMapGameplayTerrainType`: OpenGround/Road/Field/Forest/Water), не путать с `WorldMapTerrainType` — тот не тронут вообще, продолжает работать как раньше для Hills/Mountains.

**Также в ходе разведки перед реализацией выяснилось: `WorldMapRouteMask`, о котором писал пользователь, не существует и никогда не существовал в этом репозитории** (проверено `grep` по коду и по всей истории git). Не создавалось ничего с таким именем — если пользователь имел в виду другую систему, нужно уточнение.

### WM-T01 — Gameplay Terrain [СДЕЛАНО]

- [WorldMapGameplayTerrain.cs](../Assets/_Project/Scripts/Core/WorldMapGameplayTerrain.cs) (Core): `WorldMapGameplayTerrainType` (OpenGround/Road/Field/Forest/Water), `WorldMapGameplayTerrainSettings` (Traversable + MovementMultiplier), data-driven — не hardcode constants в runtime-коде. Встроенные значения по умолчанию (1.00/1.30/0.90/0.70/1.00×Water-непроходима) используются, только если автор не задал свои — тестовые числа, не канон (раздел 24 задачи, канон не трогал).
- `WorldMapDefinitionData.GameplayTerrainSettings`/`Roads` — новые списки, независимые от существующих `TerrainAreas`/`SpawnSlots`.

### WM-T02 — Road Authoring [СДЕЛАНО]

- `WorldMapRoadDefinition` (Core): Id, DisplayName, Enabled, Width, ordered `Points` (те же процентные координаты карты 0..100, что и везде — не отдельная система координат, не пиксели PNG). Обычные сериализуемые данные, не GameObject сцены.
- `WorldMapWorldDefinitionAsset` (Unity): `RoadEntry`/`GameplayTerrainSettingsEntry` + `ToData()`, editor-методы (`EditorAddRoad`, `EditorEnsureDefaultGameplayTerrainSettings`).
- Редактор — вкладка «География» получила два новых раздела: **«Типы местности»** (Traversable/Скорость на класс, кнопка «Заполнить значениями по умолчанию») и **«Дороги»** (список: добавить/удалить/ID/Название/Активна/Ширина с tooltip «в координатах карты, не пиксели фона», точки числовыми полями, кнопка «Редактировать путь» переключает на вкладку «Предпросмотр»).
- **Честно про интерактивное редактирование (раздел 8 задачи):** полноценный drag существующих точек мышью **не реализован** — это отдельная инженерная работа (свой обработчик `Event.current` с hit-testing и drag-state в `EditorWindow`, не готовая переиспользуемая система). Вместо этого: точки редактируются числовыми полями (как и зоны/слоты до этого) **плюс** клик по карте на вкладке «Предпросмотр» добавляет новую точку в конец пути редактируемой дороги — дёшево, реализовано, покрывает результат "разметил путь без правки кода".

### Preview дорог [СДЕЛАНО]

`DrawPreviewRoads`/`DrawPreviewRoad` в `WorldMapDatabaseWindow.cs`: центральная линия (`Handles.DrawLine`), номерованные точки, полупрозрачная полоса реальной gameplay-ширины (`Handles.DrawAAConvexPolygon`, повёрнутый прямоугольник вдоль каждого сегмента). Только в редакторе — не попадает в игровой экран (не переиспользует `PrototypeUIController`).

### WM-T03 — Terrain Detection [СДЕЛАНО И ПРОВЕРЕНО]

`WorldMapGameplayTerrainQuery.IsInsideRoad`/`DistancePointToSegment` — классическая проекция точки на отрезок с зажимом параметра `t∈[0,1]`, никакой сетки. 16 тестов (`WorldMapGameplayTerrainQueryTests`) закрывают все кейсы из раздела 20 задачи: центр линии, в пределах/за пределами полуширины, начало/конец сегмента, диагональ, острый поворот, сегмент нулевой длины (не NaN/exception), путь из 0/1 точек (игнорируется), выключенная дорога (игнорируется), несколько дорог (попадание в любую активную), нет дорог → OpenGround/1.0, `null`-определение → тот же безопасный запасной вариант, кастомные настройки переопределяют дефолтные.

### WM-T04 — Влияние на скорость [СДЕЛАНО И ПРОВЕРЕНО — ключевая архитектурная правка]

Точка внедрения — **`ContinuousSimulationSystem.AdvanceExpeditionMovement`** (`ContinuousSimulationActivities.cs`), тот самый единственный authoritative расчёт движения/времени (раздел 15 задачи), а не новый параллельный механизм. Бюджет шага переведён из "клеток" в "игровые часы": на каждой итерации внутреннего цикла берётся **живой** запрос `GetLiveMovementSpeedMultiplier` по текущей позиции героя (`expedition.CurrentMapXPercent/YPercent`, уже интерполируемой), и клетки-в-час на этом шаге пересчитываются как `CellsPerGameHour × multiplier`. При multiplier=1.0 (везде, где нет дороги) арифметика математически идентична прежней — подтверждено тестом `Expedition_NoRoadsBehavesExactlyAsBeforeThisFeature`.

**Не тронуто вообще:** `WorldMapNavigation.FindPath` (маршрут строится как раньше, по прямой), направление, `CalculateRouteCells`, Hills/Mountains-логика (свой независимый, ортогональный механизм). Никакого A*, графа, снаппинга, привязки к дороге, изменения конечной точки.

3 теста (`WorldMapRoadMovementTests`): за одинаковое время по дороге проходится в ~1.30 раза больше расстояния (проверено с допуском); после выхода из зоны дороги направление (Y) не меняется и движение продолжается за её пределы; без дорог формула движения даёт тот же результат, что и до этой задачи.

**Побочная находка при отладке тестов (не баг производственного кода):** первая версия тестов использовала `ArmySupply=0` (значение по умолчанию `CreateNewGame`) и огромные интервалы времени — экспедиция автоматически принудительно возвращалась домой по нехватке снабжения (существующая, не связанная с дорогами механика), что маскировало реальный результат. Поправлено в тестах (`ArmySupply` выставлен большим), не в производственном коде.

### Validation [СДЕЛАНО]

`CollectRoadIssues` в `WorldMapDatabaseWindow.cs`, расширяет существующую Validate (не отдельная система): дубли/пустой ID дороги, `Width <= 0`, путь короче 2 точек, точки вне диапазона 0..100%, предупреждение об отсутствии правила Road в «Типы местности» (сработает запасной множитель ×1.30). Выключенная дорога — не ошибка (раздел 17 задачи).

### Обратная совместимость [ПРОВЕРЕНО]

`Roads.Count == 0` (все существующие миры, где дороги ещё не заданы) → `GetTerrainTypeAtPosition` всегда `OpenGround`, множитель 1.0, движение работает бит-в-бит как раньше — подтверждено тестом и полным прогоном existing-тестов без единой регрессии.

**Проверено:** 19 новых тестов (16 детекции + 3 движения), полный `Run All` EditMode — **825/825 passed, 0 failed** (806 + 19). Сквозной прогон через живой `eval` на реальной базе (`KingdomSurvivalWorldMapDatabase`): добавлена тестовая дорога `road_test_01` (точки через Дом), Validate корректно предупредил об отсутствии правила Road → добавлены дефолтные настройки через кнопку → Validate стал чистым; `GetTerrainTypeAtPosition` на координатах Дома вернул `Road` (множитель 1.30), в стороне от дороги — `OpenGround`; полный `CreateNewGame` + движение экспедиции через дорогу — герой прошёл на ~1.02 клетки больше за то же время, направление не изменилось. Редактор открыт через реальный `menu`, все 6 вкладок (включая новые разделы) отрепейнчены в живом Editor-цикле — 0 ошибок в консоли.

**Тестовые данные оставлены в реальном ассете `KingdomSurvivalWorldMapDatabase`** (дорога `road_test_01` через Дом + дефолтные «Типы местности») — специально, чтобы пользователь сразу увидел результат при открытии `World Map Database → География`, не тратя время на раздел 21 задачи (ручная проверка) с нуля. Можно удалить/переименовать/подвинуть точки — это обычные данные, не код.

**Сознательно НЕ реализовано (по границам задачи):** WM-T05 (Road Network/граф), WM-T06 (Optimal Route), любой A*/Dijkstra, автоматический выбор/поиск ближайшей дороги, snapping героя к дороге, drag существующих точек мышью в Preview (только числа + клик-добавление в конец), ETA-оценка (`GetTravelHoursRemaining`) не учитывает будущие дороги на маршруте — показывает прежнюю грубую оценку по клеткам (сама механика движения корректна, только предварительный дисплей времени в пути может быть неточным, если впереди есть дорога — не в рамках этой задачи).

## WM-T04.5 — Preview с артом карты и полноценное редактирование дорог — 13.09.2026

Отдельная задача от пользователя (не AM/WM-этап): вкладка «Предпросмотр» окна `World Map Database` не показывала фоновый арт карты (`WorldMapVisualTheme.BaseMapSprite`), из-за чего было невозможно сверять авторские точки дорог с реально нарисованным художником дорожным полотном. Добавление точки было единственной операцией (клик всегда дописывал точку в конец пути) — не было выбора/drag/удаления существующей точки мышью.

**Причина, почему арт не показывался:** `baseMapSprite` использовался только в коде вкладки «Текстуры» (загрузка/назначение файла) — метод отрисовки `DrawPreviewGrid` рисовал исключительно debug-грид клеток `WorldMapNavigation`, нигде не обращаясь к спрайту. Не баг рендера — отсутствовавшая функциональность.

### Что сделано

- **`WorldMapPreviewMath.cs`** (новый файл, `KingdomSurvival.WorldMapVisual.Editor`, чистая математика без завязки на IMGUI): `ComputeMapRect` — letterbox-вписывание спрайта в область превью с сохранением пропорций; `MapToPreview`/`PreviewToMap`/`PreviewToMapClamped` — единая точка перевода координат карты (проценты 0..100, Y вниз) в экранные координаты Preview и обратно. Используется абсолютно всеми слоями отрисовки (Terrain Areas, Locations, Spawn Slots, Roads) и обработкой кликов — единый `mapRect` на кадр, вычисляемый один раз в `DrawPreviewSection`.
- **Фон карты**: `DrawPreviewBackgroundSprite` рисует существующий `ActiveTheme.BaseMapSprite` через `GUI.DrawTextureWithTexCoords` с UV, посчитанным из `Sprite.rect` (не всей текстуры) — корректно работает при любом разрешении PNG и при упаковке в атлас. Никакого нового поля-дубликата — источник ровно тот же `BaseMapSprite`, что и на вкладке «Текстуры»; Import Settings/Filter Mode не трогаются.
- **Блок «Отображение»**: 5 переключателей (Арт карты — по умолчанию включён, Дороги, Terrain Areas, Locations, Spawn Slots), состояние — `EditorPrefs` (не сериализуется в ассет, чисто редакторское). Когда арт выключен или отсутствует, `mapRect` = вся область превью (прежнее поведение, без леттербокса).
- **Terrain Areas поверх арта**: `Plains` не рисуется вовсе (это «нет авторской зоны», а не свой цвет), Hills/Mountains — с альфой ×0.28, чтобы рисунок оставался виден.
- **Новый слой Spawn Slots**: полупрозрачная заливка + контур прямоугольника (раньше вообще не визуализировался в Preview).
- **Индикация редактируемой дороги**: баннер «Режим редактирования: {Id}» с кнопками «Удалить выбранную точку» (активна только когда точка выбрана) и «Завершить редактирование»; дороги, кроме редактируемой, рисуются тусклее (alpha ×0.4) — клики в любом случае обрабатываются только для `editingRoadIndex`, тусклость — визуальная подсказка, не единственная защита.
- **Полноценное редактирование пути** (`HandleRoadPathEditingInput`, замена старого `HandlePreviewRoadEditingClick`): клик внутри `mapRect` рядом с существующей точкой (фиксированный экранный радиус попадания ~8px, не зависит от gameplay `Width`) — выбор + начало drag; клик по пустому месту — добавление новой точки в конец пути; `MouseDrag` двигает выбранную точку; отпускание кнопки завершает drag. Один `Undo.RecordObject` на `MouseDown` (не на каждый кадр `MouseDrag`) — весь drag одной операцией отмены; отдельные Undo-описания для Add/Move/Delete. Выбранная точка рисуется крупнее, белым, с рамкой. Клик за пределами `mapRect` (по леттербоксу) не создаёт точек ни для какой дороги.
- **Геометрически корректная зона ширины дороги** (`DrawRoadSegmentZone`, замена `DrawThickSegment`): перпендикулярное смещение на половину `Width` считается **в процентах карты** (та же метрика, что использует runtime `WorldMapGameplayTerrainQuery.DistancePointToSegment`), и только получившиеся 4 угла переводятся в экран через `WorldMapPreviewMath.MapToPreview` — устраняет прежнюю ошибку (суммирование раздельных X/Y-полуширин в один экранный скаляр), которая давала визуально неверную толщину при не квадратном `mapRect`/соотношении сторон арта.
- Удаление кнопкой, а не глобальным `Delete`/`Backspace` — сознательный выбор (см. отчёт перед реализацией), чтобы не конфликтовать с редактированием текстовых полей в этом же окне.
- `KingdomSurvival.Core.EditModeTests.asmdef`: добавлена ссылка на `KingdomSurvival.WorldMapVisual.Editor` (по прецеденту уже существующей ссылки на `KingdomSurvival.DialogueDatabase.Editor`) — позволило написать тесты на скомпилированный `WorldMapPreviewMath` напрямую, без reflection.
- **`WorldMapPreviewMathTests.cs`** (9 новых тестов): letterbox для широкого/высокого спрайта, `ComputeMapRect` без арта возвращает всю область, углы/центр `MapToPreview`, round-trip `MapToPreview`→`PreviewToMap`, `PreviewToMapClamped` для точки далеко за пределами карты, инвариантность соотношения экран/проценты при изменении размера области превью (proxy для «resize окна не меняет сериализованные координаты» — сама сериализация координат вообще не завязана на экранные пиксели, поэтому resize физически не может её задеть). IMGUI mouse-drag интеграционные тесты сознательно не писались (не тестируется юнит-тестами в этом проекте, только вручную).

### Не тронуто (гарантированно)

`ContinuousSimulationSystem`, `MovementMultiplier`, `WorldMapGameplayTerrainQuery`/детекция дороги, `WorldMapNavigation.FindPath`, время/ETA, A*/граф — задача полностью в границах Editor/Preview, раздел 15 задачи ("НЕ МЕНЯТЬ GAMEPLAY") соблюдён.

**Проверено:** компиляция чистая (`recompile` — 0 ошибок); полный `Run All` EditMode — **834/834 passed, 0 failed** (825 + 9 новых). Живой `eval`: реальный `KingdomSurvivalWorldMapDatabase` уже содержит назначенный `BaseMapSprite` (`map`, 2048×1260) и тестовую дорогу `road_test_01` (3 точки, сохранена нетронутой); окно открыто через реальный `EditorWindow`, вкладка «Предпросмотр» отрепейнчена дважды в живом Editor-цикле — 0 ошибок в консоли (кроме одной не связанной с задачей ошибки MCP-инструмента `wait_for`, не от игрового кода).

**Требует ручной проверки в живом Unity** (недоступно из текущей headless-сессии): реальный drag точки мышью и визуальное подтверждение, что она двигается плавно без дублирования Undo-шагов на каждый кадр; клик на границе `mapRect` (letterbox) действительно не создаёт точку; переключение вкладок Отображение визуально скрывает/показывает слои; поведение при полностью пустом `BaseMapSprite` (тумблер «Арт карты» в этом случае должен просто не иметь эффекта — код это предусматривает, но не проверялся вручную на реальном пустом поле).

## WM-T04.6 — Map Art Layers — 13.09.2026

Отдельная задача от пользователя: одного Background Sprite ("Base Map") на всю глобальную карту недостаточно для production pipeline — художник рисует карту постепенно, отдельными PNG-фрагментами (сначала домашний регион, потом северный участок, болото и т.д.), и ни один такой фрагмент не должен растягиваться на всю карту.

### Что сделано

- **Base Map сохранён без изменений** — `WorldMapVisualTheme.BaseMapSprite` по-прежнему необязательный фон на всю карту (бумага/общий цвет/старый полноразмерный рисунок), рендерится первым, ниже всех Art Layers.
- **`WorldMapArtLayerEntry`** (новая структура, `WorldMapVisualTheme.cs`): `Id`, `DisplayName`, `Enabled`, `Sprite`, `MinXPercent/MaxXPercent/MinYPercent/MaxYPercent` (Bounds — ТА ЖЕ система координат карты 0..100%, что у Roads/Locations/Spawn Slots/героя, не пиксели PNG), `Order` (меньший — ниже), `Opacity` (0..1), `FitMode` (`PreserveAspect` по умолчанию — letterbox внутри Bounds, либо `Stretch`). Список `artLayers` — новое поле `WorldMapVisualTheme` (не `WorldMapWorldDefinitionAsset`): это чисто визуальный слой темы, gameplay (`WorldMapNavigation`, `WorldMapGameplayTerrainQuery`, Core) о нём не знает вообще.
- **`WorldMapArtLayerUtility.GetOrderedEnabledLayers`** (Runtime, `KingdomSurvival.WorldMapVisual`) — единственный источник правды для фильтрации (Enabled && Sprite != null) и сортировки по `Order` (устойчивая — при равном Order сохраняется порядок списка); используется и Preview, и runtime-рендером карты — порядок слоёв не может разойтись между режимами.
- **`WorldMapPreviewMath.MapBoundsToRect`** (новая чистая функция) — прямоугольник Bounds-слоя в Preview через две точки `MapToPreview`, та же математика, что и весь остальной Preview.
- **Вкладка «Текстуры»**: новая секция «Map Art Layers» под «Основой карты» — список слоёв редактируется как Roads (SerializedProperty, добавление/удаление кнопками), числовые поля Bounds/Order/Opacity/FitMode, кнопка «Показать в Preview» на каждом слое подсвечивает его в Preview тонкой рамкой с подписью ID.
- **Preview**: новый тумблер «Map Art Layers» (по умолчанию включён) и отдельный тумблер «Art Layer Bounds» (debug-рамки, по умолчанию выключен — раздел 13/14 задачи) в блоке «Отображение»; каждый слой рисуется строго внутри своего `MapBoundsToRect`-прямоугольника поверх Base Map и под Terrain Areas/Locations/Roads — `PreserveAspect` вписывает спрайт с сохранением пропорций (letterbox внутри Bounds, переиспользуя `WorldMapPreviewMath.ComputeMapRect`), Opacity слоя умножает alpha через `GUI.color`.
- **Validate**: `WorldMapDatabaseWindow.CollectArtLayerIssues` (публичный static, принимает список слоёв напрямую) — ошибки: пустой/дублирующийся ID, Enabled-слой без Sprite, MinX≥MaxX, MinY≥MaxY; предупреждения: слой полностью вне карты, Opacity≤0, очень маленький Bounds (<0.5%), несколько активных слоёв с одинаковым Order.
- **Runtime**: новый слой `world-map-art-layers` в `Prototype_Main.uxml` (между `world-map-background` и `world-map-water`, класс `.world-map-layer`); `PrototypeUIController.ApplyWorldMapArtLayers()` создаёт по одному `VisualElement` на каждый слой из `GetOrderedEnabledLayers`, позиционируя его через `style.left/top/width/height` **в процентах** — это математически идентично `MapToPreview` (то же независимое по осям линейное отображение, которым уже пользуется существующий обработчик клика по карте, `OnWorldMapPointerDown`), просто выражено через нативный процентный layout UI Toolkit вместо явного умножения. `unity-background-scale-mode` = `ScaleToFit` (PreserveAspect) или `StretchToFill` (Stretch); `style.opacity` = Opacity слоя. Никакого нового GameObject/SpriteRenderer — всё в рамках существующего UI Toolkit-полотна карты (канон §9.9).
- **Обратная совместимость**: `ArtLayers.Count == 0` (все существующие базы, включая реальный `KingdomSurvivalWorldMapDatabase`) → ветки отрисовки Art Layers ничего не делают, карта выглядит ровно как до этой задачи; проверено — реальная база и её тема после задачи всё ещё содержат `ArtLayers.Count == 0`, `BaseMapSprite` не тронут.

### Тесты

18 новых тестов (`WorldMapArtLayerTests.cs`): полный диапазон 0..100 занимает весь `mapRect`; под-регион даёт правильный под-прямоугольник; центрированный Bounds даёт центр `mapRect`; Disabled и Enabled-без-Sprite слои исключаются `GetOrderedEnabledLayers`; сортировка по Order (включая устойчивость при равном Order); Validation ловит Opacity≤0, MinX≥MaxX, отсутствие Sprite, дублирующийся ID, слой полностью вне карты, валидный слой не даёт предупреждений; пустой/`null` список слоёв не падает и возвращает пустой результат (legacy-поведение); точка внутри Bounds попадает внутрь вычисленного прямоугольника слоя (и снаружи — не попадает) — то же преобразование координат, что использует runtime; пересчёт `MapBoundsToRect` для разных по размеру `mapRect` не меняет исходные данные Bounds слоя (resize Preview не трогает координаты). IMGUI-отрисовку и построение `VisualElement` в рантайме юнит-тестами не покрываем (как и раньше в этом проекте) — только ручная проверка.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **852/852 passed, 0 failed** (834 + 18 новых). Живой `eval`: реальная база — `ArtLayers.Count == 0`, `BaseMapSprite` = `map` (не тронут); во временный экземпляр темы (только в памяти, не сохранён, не помечен dirty намеренно и убран после проверки) добавлен тестовый слой с Bounds 10..35/40..65% и существующим спрайтом карты — вкладки «Текстуры», «Предпросмотр» и «Проверка» отрепейнчены в живом Editor-цикле — 0 новых ошибок в консоли; `git status`/`git diff --stat` после проверки подтвердили отсутствие непреднамеренных изменений в .asset-файлах на диске.

**Требует ручной проверки в живом Unity:** реальное добавление нескольких слоёв через UI кнопки «+ ДОБАВИТЬ СЛОЙ» с разными PNG-фрагментами и визуальная проверка, что каждый занимает только свой Bounds; кнопка «Показать в Preview» действительно подсвечивает нужный слой; overlap двух слоёв с разным Order визуально даёт ожидаемый порядок; Play Mode — совпадение положения Art Layer в реальной игровой карте с тем, что показывает Preview (раздел 24 задачи, тестовый workflow целиком); PNG с alpha-каналом корректно не перекрывает нижний слой непрозрачными краями (код умножает Opacity через `style.opacity`, сам alpha PNG обрабатывается стандартным Unity-шейдером UI Toolkit — не проверялось на реальном PNG с прозрачностью).

## WM-T04.7 — Direct Art Layer Manipulation — 13.09.2026

Отдельная задача от пользователя: числовые Bounds (Min/Max X/Y) для Map Art Layers неудобны художнику — добавлено прямое перетаскивание и масштабирование PNG-фрагмента прямо во вкладке «Предпросмотр».

### Что сделано

- **Выбор слоя кликом в Preview**: ЛКМ по видимой области Art Layer выбирает его; при перекрытии слоёв побеждает тот, у кого больше `Order` (при равном `Order` — тот же стабильный порядок, что даёт `WorldMapArtLayerUtility.GetOrderedEnabledLayers`, общий с runtime). Disabled-слой или слой без Sprite не участвует в hit-test вообще — раздел 23/24 задачи (Opacity на hit-test не влияет, alpha-pixel testing не делается).
- **Рамка + 4 угловых resize handle** у выбранного слоя видны всегда (не только при включённом debug-тумблере «Art Layer Bounds» — иначе нечем было бы управлять). Handle — фиксированный экранный размер 10px, не зависит от масштаба PNG/Bounds.
- **Drag тела слоя** — перемещает Bounds без изменения ширины/высоты. **Drag углового handle** — масштабирует относительно противоположного (неподвижного) угла; при `FitMode = PreserveAspect` пропорция берётся из `Sprite.rect.width/height` (не всей Texture — атласы/cropped sprites учтены) и сохраняется; при `Stretch` X/Y меняются независимо.
- **Защита от плавающего дрифта** (раздел 7 задачи): и move, и resize считаются как `originalBounds` (снимок на MouseDown) `+ суммарная дельта/абсолютная позиция курсора`, а не накопительно кадр за кадром — `WorldMapArtLayerBoundsMath.MoveBounds`/`ResizeBoundsFromCorner` в `WorldMapPreviewMath.cs`, чистые тестируемые функции без UnityEditor-типов.
- **Clamp картой**: `ClampBoundsToMap` сдвигает (не обрезает) прямоугольник целиком, если он выходит за 0..100% — применяется и к move, и к resize (в resize координата тянущегося угла сначала клампится к 0..100, что естественно удерживает слой в границах, так как anchor уже был внутри).
- **Минимальный размер**: `MinArtLayerSizePercent = 1f` (константа с комментарием-обоснованием) — resize-handle не даёт схлопнуть слой в точку; числовые поля на вкладке «Текстуры» этим порогом не ограничены (для точной ручной подгонки мелких фрагментов).
- **Undo**: `Undo.RecordObject(database.ActiveTheme, ...)` один раз на `MouseDown` (не на каждый `MouseDrag`) для move и отдельно для resize — Ctrl+Z возвращает слой к состоянию до начала drag целиком. `EditorUtility.SetDirty` на каждом кадре drag — Bounds сохраняются в `WorldMapVisualTheme` как обычные сериализованные данные, отдельного runtime/Editor-only состояния для них нет.
- **Конфликт с Road edit mode устранён структурно, не порядком `current.Use()`**: `DrawPreviewSection` вызывает `HandleRoadPathEditingInput` ТОЛЬКО когда `editingRoadIndex >= 0`, и `HandleArtLayerEditingInput` — ТОЛЬКО когда `editingRoadIndex < 0`. Пока редактируется путь дороги, Art Layer вообще не получает событий мыши — resize/drag не может случайно создать Road Point и наоборот.
- **Selection state**: `selectedArtLayerIndex` — editor-only, не сериализуется. Сбрасывается при удалении выбранного слоя (сдвигается на -1 при удалении слоя ПЕРЕД выбранным, чтобы selection не "перепрыгнул"), и при смене `WorldMapDatabaseAsset`/`ActiveTheme` (раздел 4 задачи).
- **Вкладка «Текстуры»**: секция Bounds переименована в «Точное положение» (внутренние имена полей `MinXPercent` и т.д. не менялись); кнопка «Показать в Preview» теперь сразу переключает вкладку на «Предпросмотр» (раздел 17 задачи).
- Курсор `MouseCursor.ScaleArrow` над resize-handle (раздел 15 задачи, необязательное улучшение — сделано, так как оказалось дёшево через `EditorGUIUtility.AddCursorRect`).
- **Runtime не изменён** — после drag/resize меняются только сериализованные Bounds в `WorldMapVisualTheme`, существующий `PrototypeUIController.ApplyWorldMapArtLayers` их просто читает, как и любые другие Bounds, заданные вручную числом.

### Тесты

17 новых тестов (`WorldMapArtLayerDragTests.cs`): move по X/Y сохраняет размер; clamp у левого/правого края карты; move от `originalBounds + суммарная дельта` даёт тот же результат, что несколько маленьких вызовов подряд (нет накопления дрифта); resize BottomRight увеличивает Max, TopLeft уменьшает Min; PreserveAspect сохраняет соотношение сторон; Stretch меняет X/Y независимо; resize не даёт width/height ⩽ 0 (минимальный размер); resize у самого края карты остаётся в 0..100; `ClampBoundsToMap` не трогает уже валидный Bounds; выбор клика при перекрытии слоёв берёт больший `Order`; клик вне перекрытия попадает только в подходящий слой; Disabled-слой не выбирается; клик вне всех слоёв — `-1`. IMGUI drag и Unity Undo юнит-тестами не покрываются (как и раньше в проекте) — только ручная проверка.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **869/869 passed, 0 failed** (852 + 17 новых). Живой `eval`: во временный слой (только в памяти, добавлен и убран после проверки, не сохранён) выбран и отрисован с рамкой + 4 handle на вкладке «Предпросмотр» — 0 новых ошибок в консоли. При проверке в реальной базе обнаружен уже существующий слой `art-layer-1` (Bounds 50..100/50..100, Sprite `map`), явно оставленный пользователем при собственном исследовании функциональности в живом Editor — **не тронут**, оставлен как есть; `git status` подтвердил отсутствие изменений в `.asset`-файлах на диске от моих проверок.

**Требует ручной проверки в живом Unity** (полный workflow раздела 28 задачи): клик по PNG → рамка+handles; drag тела → перемещение без изменения размера; Ctrl+Z → возврат; drag BottomRight handle → resize (с проверкой PreserveAspect и отдельно Stretch); попытка вытянуть слой за край карты; включение Road edit mode → Art Layer не двигается случайно, выключение → снова можно; закрытие/открытие окна → Bounds сохранены; Play Mode → слой в том же месте, что и Preview.

## WM-T04.8 — Terrain Area Preview Authoring — 13.09.2026

Отдельная задача от пользователя: числовые Min/Max X/Y для Terrain Areas неудобны — добавлено рисование, перемещение и resize зон мышью прямо в Preview, плюс отдельный ползунок прозрачности debug-overlay (арт больше не должен закрываться зонами).

### Что сделано

- **Явный режим редактирования Preview**: новый toolbar «Просмотр / Art Layers / Terrain Areas / Roads» (`PreviewEditMode`). Заменяет прежнюю неявную логику (Art Layer editing был активен всегда, кроме Road mode) — теперь только ОДИН обработчик мыши активен в любой момент, переключение режима явно сбрасывает любой незавершённый drag/resize/armed-create предыдущего режима, чтобы «зависший» MouseUp не применился к чужому объекту. Кнопки «Показать в Preview» (Art Layers), «Редактировать путь» (Roads) и новая «Показать в Preview» (Terrain Areas) теперь выставляют соответствующий режим — раньше только листали вкладку.
- **Preview рисует САМИ прямоугольники `TerrainAreaEntry`**, а не испечённую grid-сетку `WorldMapNavigation`, как раньше — та не позволяла кликнуть по конкретной зоне (клетка не хранит, какая именно зона её раскрасила при разрешении Priority). Цвета — тот же существующий `GetPreviewTerrainColor` (Plains/Hills/Mountains), не переделывался.
- **Select/move/resize/create/delete** зоны в режиме Terrain Areas: ЛКМ по зоне выбирает её (при перекрытии — с наибольшим Priority, при равном — последняя в списке, тот же принцип, что уже описан в подсказке вкладки «География» для gameplay-разрешения); drag тела перемещает без изменения размера; 4 угловых handle меняют размер СВОБОДНО по X/Y (aspect ratio НЕ сохраняется — Terrain Area прямоугольник, не спрайт); кнопка «+ Нарисовать новую зону» переводит в режим ожидания, следующий MouseDown+drag+MouseUp создаёт зону (drag-threshold 5 экранных px, минимальный размер 0.5% map-space — иначе случайный клик не создаёт зону); кнопка «Удалить выбранную зону» (disabled без выбора).
- **Переиспользована математика WM-T04.7 без дублирования** (раздел 26 задачи): `WorldMapArtLayerBoundsMath.MoveBounds`/`ResizeBoundsFromCorner` (с `preserveAspect: false` для свободного resize)/`ClampBoundsToMap` — те же функции, что и для Art Layers. Добавлена только одна новая чистая функция — `NormalizeBoundsFromTwoPoints` (для создания зоны протягиванием, работает в любом направлении drag).
- **Undo**: `Undo.RecordObject(database.ActiveWorld, ...)` один раз на MouseDown для move/resize и один раз при фактическом создании/удалении — не на каждый MouseDrag. `WorldMapWorldDefinitionAsset.EditorRemoveTerrainAreaAt(int)` (новый метод) — точечное удаление одной зоны в дополнение к уже существовавшим `EditorAddTerrainArea`/`EditorClearTerrainAreas`.
- **Прозрачность — ТОЛЬКО настройка Preview**: ползунок 0–100% (`EditorPrefs`, ключ `KingdomSurvival.WorldMapPreview.TerrainAreaOpacity`, default 20%). `Opacity` НЕ добавлен в `TerrainAreaEntry` — это исключительно Editor-only состояние отображения, gameplay-данные не тронуты (подтверждено тестом на отсутствие поля через reflection). При Opacity = 0 заливка полностью прозрачна, но граница зоны остаётся видна, пока включён показ Terrain Areas — иначе зону нечем было бы редактировать.
- **Тумблер видимости Terrain Areas по умолчанию — OFF** (`EditorPrefs.GetBool(..., false)`, было `true`) — Preview в первую очередь показывает арт карты; уже сохранённый выбор существующих пользователей не переопределяется (`GetBool` использует default только при отсутствующем ключе).
- **Числовое редактирование на вкладке «География» не удалено** — Terrain/Tags/Priority/Min/Max X/Y остаются точным способом ввода; добавлена кнопка «Показать в Preview» у каждой зоны.
- **Runtime не изменён** — `WorldMapNavigation`/`WorldMapGameplayTerrainQuery`/движение/маршруты не тронуты; после drag/resize/create/delete меняются только сериализованные `TerrainAreaEntry` в `WorldMapWorldDefinitionAsset`, существующая логика применения зон (`ConfigureFromDefinition` → grid) их просто читает как обычные авторские данные.

### Тесты

16 новых тестов (`WorldMapTerrainAreaDragTests.cs`): нормализация Bounds по двум точкам drag в любом направлении; слишком маленький drag даёт Bounds меньше порога; move сохраняет ширину/высоту; move clamp внутри 0..100; resize TopLeft/BottomRight меняют только соответствующие углы; свободный (не привязанный к aspect) resize независимо по X/Y; resize никогда не даёт MinX≥MaxX или MinY≥MaxY; resize у края карты остаётся в 0..100; выбор при перекрытии берёт больший Priority; при равном Priority — последний в списке; клик вне всех зон — `-1`; `Mathf.Clamp01` для диапазона Opacity; `TerrainAreaEntry` не имеет поля `Opacity` (reflection-проверка, что data model не тронута). IMGUI drag и Unity Undo — только ручная проверка, как и у Art Layers/Roads.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **885/885 passed, 0 failed** (869 + 16 новых). Живой `eval`: все 4 режима Preview (Просмотр/Art Layers/Terrain Areas/Roads) отрепейнчены циклом — 0 новых ошибок в консоли; временная зона (Mountains, добавлена и убрана после проверки, не сохранена) выбрана и отрисована с рамкой + 4 handle на реальных существующих зонах (`test-hills-west`, `test-mountains-north`) — те остались нетронутыми; `git status` подтвердил отсутствие изменений в `.asset`-файлах на диске.

**Требует ручной проверки в живом Unity** (полный workflow раздела 31 задачи): назначить Art Layer с PNG → арт виден при Terrain Areas OFF → включить Terrain Areas на 20% → арт остаётся читаемым → режим Terrain Areas → выбрать Hills → «+ Нарисовать новую зону» → протянуть прямоугольник → зона создалась → drag внутри перемещает → drag угла — resize → Ctrl+Z отменяет → удалить зону → числа на «Географии» синхронны → закрыть/открыть окно — зона сохранена.

## WM-T04.9 — Global Map Aspect & Preview Canvas Navigation — 13.09.2026

Отдельная задача от пользователя: Preview растягивал глобальную карту по ширине при широком окне Unity (аспект брался из `BaseMapSprite` или из размера самого Preview-окна), а сам canvas имел жёсткую высоту 320px с огромной пустой областью под ним. Задача — зафиксировать геометрию мира и превратить Preview в полноразмерный canvas с zoom/pan, не переписывая уже работающие Art Layers/Terrain Areas/Roads.

### Причина растяжения (было)

```csharp
Rect previewArea = GUILayoutUtility.GetRect(position.width - 24f, 320f, GUILayout.ExpandWidth(false));
float spriteAspect = showArtNow ? backgroundSprite.rect.width / backgroundSprite.rect.height : 0f;
Rect mapRect = WorldMapPreviewMath.ComputeMapRect(previewArea, spriteAspect, showArtNow);
```
Без Base Map (`spriteAspect=0`) `ComputeMapRect` возвращал весь `previewArea` без letterbox — а `previewArea` уже был растянут на всю ширину окна. С Base Map аспект карты неверно совпадал с аспектом PNG.

### Что сделано

- **`WorldMapWorldDefinitionAsset`**: новые поля `mapCanvasWidth`/`mapCanvasHeight` (default **4160×2560**, `DefaultMapCanvasWidth`/`DefaultMapCanvasHeight` — публичные константы) и вычисляемое свойство `GlobalMapAspect` с защитой от деления на 0/отрицательных значений (откат на default). Это **reference canvas** — не требование к разрешению какой-либо Texture; gameplay-координаты остаются 0..100. Старые ассеты без этих полей получают default автоматически через инициализатор при десериализации — пересоздавать вручную не нужно (подтверждено живым `eval` на реальном `KingdomSurvivalWorldDefinition`: `GlobalMapAspect=1.625` сразу после загрузки, без миграции).
- **Вкладка «Мир»**: новый блок «Глобальная карта» — Reference Width/Height (редактируемые) + read-only Aspect.
- **`WorldMapPreviewMath`**: `ZoomRectAroundPoint(rect, zoomFactor, anchorScreenPoint)` — общая rect-математика масштабирования вокруг экранной точки (не завязана на map-space); `ApplyZoomPan(fitRect, zoom, panFraction)` — строит displayRect по цепочке Canvas→AspectFit(FitRect)→Zoom→Pan; `ExtractZoomPan` — обратное преобразование (нужно после zoom вокруг курсора, чтобы новый displayRect сохранился как относительные `zoom`/`panFraction`, а не абсолютный пиксельный rect, который бы рассинхронизировался при следующем resize окна). Существующие `ComputeMapRect`/`MapToPreview`/`PreviewToMap`/`MapBoundsToRect` переиспользованы без изменений — `ComputeMapRect` теперь считает FitRect по `GlobalMapAspect` вместо аспекта спрайта, всегда (`showArt: true`), а не по условию.
- **Единый Preview-transform без новой параллельной системы**: все 9 существующих функций отрисовки/ввода (`DrawPreviewArtLayers`, `DrawPreviewTerrainAreas`, `DrawPreviewRoads`, `DrawPreviewLocations`, `DrawPreviewSpawnSlots`, `HandleArtLayerEditingInput`, `HandleTerrainAreaEditingInput`, `HandleRoadPathEditingInput` и хиттесты handle'ов) **не изменены** — они как и раньше получают один `Rect mapRect`; поменялось только то, ЧТО в него теперь подставляется (fit→zoom→pan вместо аспекта спрайта). Это одновременно и адаптация Art Layers/Terrain Areas/Roads под навигацию, и доказательство, что она не сломала их логику.
- **Canvas на всё окно**: `GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))` вместо жёсткой высоты 320px, с минимумом 320px на случай вырожденного layout-прохода. Подтверждено живым `eval`: при окне 1400×700 canvas получил `height=398` (а не фиксированные 320), при этом `fitRect` остался с аспектом ровно 1.625.
- **`GUI.BeginGroup(canvasArea)`** вместо ручного клиппинга — даёт clip (карта при zoom/pan не рисуется поверх toolbar) и бесплатный перевод `Event.current.mousePosition` в локальные координаты canvas для ВСЕХ вложенных элементов, поэтому существующие Handle-методы не нуждались в правках сигнатур.
- **Zoom**: колесо мыши, диапазон **0.1×–8×**, работает только когда курсор над canvas. Zoom вокруг курсора: `ApplyZoomKeepingPointFixed` берёт текущий displayRect, вызывает `ZoomRectAroundPoint` вокруг позиции курсора, затем `ExtractZoomPan` пересчитывает `previewZoom`/`previewPanFraction` относительно текущего `fitRect` — точка карты под курсором остаётся под курсором (подтверждено тестом `ZoomRectAroundPoint_KeepsAnchorPointFixedRelativeToRect`).
- **Pan**: средняя кнопка мыши (MMB) + drag, независимо от `PreviewEditMode` — `button==2`/`ScrollWheel` структурно не пересекаются с `button==0`, которым пользуются все три режима авторинга, поэтому конфликт невозможен без явного gate.
- **«Вписать карту»**: `zoom=1, pan=(0,0)` — это и есть базовое состояние FitRect (не отдельный расчёт).
- **«1×»**: один reference-пиксель `MapCanvasWidth` соответствует одному экранному пикселю (`newZoom = MapCanvasWidth / fitRect.width`, якорь — центр canvas). Семантика выбрана явно и описана в коде — если она когда-нибудь окажется неудобной в IMGUI, требует отдельного пересмотра, не переопределялась молча.
- **Zoom/Pan — editor-only**: `previewZoom`/`previewPanFraction`, персистентны через `EditorPrefs` (не в `WorldMapDatabase`/`WorldMapWorldDefinitionAsset`/SaveGame) — переживают закрытие/открытие окна.
- **Resize окна**: `fitRect` пересчитывается заново каждый кадр из актуального `canvasRect`, а `zoom`/`panFraction` хранятся как относительные величины (доля от размера `fitRect`) — при изменении размера окна аспект и относительный масштаб/сдвиг не ломаются без явного действия пользователя.
- **Handles остаются постоянного экранного размера** — `ArtLayerHandleScreenSize`/аналоги для Terrain Area не масштабируются на zoom (они и раньше не зависели от масштаба mapRect, так как складывались/вычитались в экранных пикселях уже после перевода координат).
- **Runtime проверен, не изменён**: `PrototypeUIController.WorldMapViewport.cs`, `ConfigureWorldMapCanvasSize()` уже задаёт `worldMap.style.width/height` явно в пикселях из `(GridWidth-1)/(GridHeight-1) × WorldMapBaseCellSizePx` (WM-14) — фиксированный аспект, независимый от `BaseMapSprite`/viewport, с собственным корректным zoom/pan (WM-11...WM-17). Runtime уже был правильно нормализован до этой задачи — трогать не потребовалось.

### Тесты

19 новых тестов (`WorldMapPreviewTransformTests.cs`): `GlobalMapAspect` для дефолтного размера и с fallback при невалидных Width/Height; `ComputeMapRect` для широкого/узкого/разных по размеру canvas сохраняет заданный аспект без искажений; углы/центр `MapToPreview`; roundtrip `MapToPreview`↔`PreviewToMap`; `ApplyZoomPan` при zoom=1 совпадает с fitRect, при zoom=2 удваивает размер, pan сдвигает пропорционально; `ZoomRectAroundPoint` сохраняет map-точку под курсором и корректно round-trip'ится через `ExtractZoomPan`→`ApplyZoomPan`; сброс к fit идемпотентен; точка вне canvas корректно определяется `Rect.Contains`; регрессия — `MapBoundsToRect`/`PreviewToMap` после zoom/pan (когда `mapRect` уже сам является displayRect) работают той же формулой без отдельной математики для Art Layer/Terrain/Road.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **904/904 passed, 0 failed** (885 + 19 новых, ни один старый тест не менялся). Живой `eval`: реальный `KingdomSurvivalWorldDefinition` без ручной миграции сразу дал `GlobalMapAspect=1.625`; окно расширено до 1400×700 — canvas вырос до `height=398` (не 320), `fitRect` остался с аспектом ровно 1.625, letterbox — по бокам, не деформация; `Base Map` выключен через рефлексию — аспект не изменился; zoom=3/pan=(0.3,-0.2) выставлены и сброшены обратно — 0 новых ошибок в консоли на всех шагах; `git status` подтвердил отсутствие изменений в `.asset`-файлах на диске.

**Требует ручной проверки в живом Unity** (полный workflow разделов 41-48 задачи): реальное колесо мыши над конкретной точкой Art Layer — визуальное подтверждение, что точка остаётся под курсором; MMB pan при zoom > fit; кнопки «Вписать карту»/«1×»; выбор/move/resize Art Layer, Terrain Area (включая create протягиванием) и Road Point после zoom/pan — что клик действительно попадает в ожидаемую map-точку; Terrain Area opacity 20% при включённом Art Layer — арт остаётся читаемым (эта часть логики не менялась в WM-T04.9, но стоит перепроверить визуально после смены canvas).

## Пересборка масштаба путешествия — базово 1 клетка = 4 игровых часа — 14.09.2026

Отдельная задача от пользователя: сетка глобальной карты `104×64` и максимальный zoom Preview позволяют приблизиться примерно до одной клетки на экран, но симуляция фактически считала «1 клетка маршрута = 24 игровых часа», из-за чего на обычной скорости пересечение одной клетки занимало ~1 минуту, а на ускорении всё равно ощущалось слишком медленно. Канон и `NARRATIVE.md` проверены — утверждённого правила «1 клетка = 1 сутки» там нет, это было только техническое решение кода (комментарий `WM-12`), поэтому менять его вместе с реализацией не требовало отдельного согласования канона.

### Причина старого правила

`ContinuousSimulationSystem.CellsPerGameHour = ArmyCellsPerRealSecond / GameHoursPerRealSecond`, где `ArmyCellsPerRealSecond = 1/RealSecondsPerGameDay` и `GameHoursPerRealSecond = 24/RealSecondsPerGameDay` — `RealSecondsPerGameDay` сокращался в числителе и знаменателе, давая ровно `1/24` НЕЗАВИСИМО от темпа мира. Скорость армии была случайно, но жёстко привязана к длительности игровых суток.

### Что сделано

- **`WorldMapDefinitionData.BaseTravelHoursPerCell`** (новое поле Core, default `4f`) — сколько игровых часов занимает пересечение одной обычной (OpenGround) клетки на обычной скорости. Балансировочная настройка мира, не константа кода.
- **`WorldMapWorldDefinitionAsset`**: сериализуемое поле `baseTravelHoursPerCell` + safe-fallback свойство `BaseTravelHoursPerCell` (тот же паттерн, что уже есть у `GlobalMapAspect` из WM-T04.9 — невалидное/нулевое/отрицательное значение откатывается на default `4`, без деления на 0), проброшено в `ToData()`. Старые ассеты без этого поля получают default через инициализатор при десериализации — подтверждено живым `eval` на реальном `KingdomSurvivalWorldDefinition` (`BaseTravelHoursPerCell=4` сразу после загрузки, без миграции).
- **Новая формула**: `ContinuousSimulationSystem.CellsPerGameHour = 1.0 / hoursPerCell`, где `hoursPerCell` читается из `WorldMapNavigation.ActiveDefinition.BaseTravelHoursPerCell` (fallback `4` без активного мира или при `<=0`). `RealSecondsPerGameDay`/`GameHoursPerRealSecond` (темп течения МИРОВОГО времени) и `NormalSpeedMultiplier`/`FastSpeedMultiplier` — не тронуты; удалён только промежуточный `ArmyCellsPerRealSecond` (был нужен исключительно для старой, теперь несуществующей, формулы).
- **Вкладка «Мир»**: новый блок «Путешествие» — поле «Базовое время на клетку, ч», read-only «Действующее значение, ч» (после fallback), HelpBox с точным текстом из задачи.
- **Terrain cost (Hills/Mountains) и живой множитель дорог не тронуты и не задвоены**: `WorldMapNavigation.FindPath` по-прежнему добавляет 2/3 под-точки маршрута для Hills/Mountains (независимый слой), `WorldMapGameplayTerrainQuery`/`ContinuousSimulationActivities.AdvanceExpeditionMovement` по-прежнему применяют живой множитель местности (дороги) поверх базовой скорости — это ДВА независимых, не пересекающихся механизма, перемножаются один раз. Проверено тестами и вручную: обычная клетка = 4ч, Hills = 8ч, Mountains = 12ч, дорога (×1.3) сокращает время в пути.
- **`GetTravelHoursRemaining`** автоматически использует новую базу (через тот же `CellsPerGameHour`) — отдельно ничего чинить не пришлось. Известное ограничение сохраняется как есть (не расширялось): ETA не учитывает дороги, которые встретятся ВПЕРЕДИ по маршруту — только текущую позицию.
- **Save/Load не затронут**: `BaseTravelHoursPerCell` — данные World Definition (не runtime-состояние), `ExportSnapshot`/`RestoreSnapshot` (`SegmentProgress`/`RouteIndex`/время) продолжают работать без изменений — подтверждено новым тестом.
- **Не тронуто**: `GridWidth=104`/`GridHeight=64`, размер canvas/zoom/арт/координаты локаций, Road/A*/pathfinding архитектура, UI Toolkit карта.

### Обновлены тесты со старым правилом «1 клетка = 24 часа»

- `ContinuousSimulationTests.cs`: `Expedition_OneCellAdvancesExactlyTwentyFourGameHoursAtNormalSpeed` → `Expedition_OneCellAdvancesExactlyBaseTravelHoursAtNormalSpeed` (10 реальных секунд = 4ч по умолчанию, не пересекает полночь); `FastSpeed_TriplesClockAndMovesOneCellPerGameDay` → `FastSpeed_TriplesClockAndMovesOneCellPerBaseTravelHours` (расчёт остатка времени — от новой клетки, не от `RealSecondsPerGameDay`).
- `StabilityRegressionTests.cs`: `OneCellAdvanceSeconds` пересчитан через `CellsPerGameHour`/`GameHoursPerRealSecond` (генерик-формула, а не жёстко от `RealSecondsPerGameDay`), явный сброс географии в статическом конструкторе — иначе значение зависело бы от порядка запуска тестов.
- `TimedExpeditionActivityTests.cs`: `TravelEstimate_UsesContinuousArmySpeed` — жёсткая проверка `1/24`/`120ч` заменена на `1/4`/`20ч` (для 5 клеток); `GatherBerries_StopsForThreeHoursThenRewardsAndResumesRoute` — буфер движения после активности пересчитан под новую (кратно меньшую) длительность клетки (иначе маршрут проскакивал на 9 клеток вместо одной); ArmySupply-ожидание скорректировано — при 4ч/клетку окно теста больше не обязано пересекать полночь, дневной расход снабжения не срабатывает (13, не 8) — это не регрессия, а следствие новой шкалы.
- `WorldMapRoadMovementTests.cs`: `Advance` уменьшен со 150 до 20 секунд в двух тестах — при 6-кратном ускорении базового движения старая длительность заставляла экспедицию доехать до цели раньше, чем нужно было измерить (оба сценария упирались в один и тот же clamp дистанции).

### Новые тесты (`WorldMapTravelScaleTests.cs`, 9 тестов)

OpenGround = 4ч за клетку (fallback без мира); 10 клеток = 40ч; Hills (×2) = 8ч; Mountains (×3) = 12ч (важная деталь при построении: `WorldMapNavigation.ClampMapX/Y` клампит координаты к `[2..98]`, тестовые координаты должны лежать строго внутри); дорога с множителем 1.3 реально сокращает время в пути (не только увеличивает дистанцию — та же связь с другой стороны); смена `BaseTravelHoursPerCell` меняет движение, `RealSecondsPerGameDay` при этом не меняется (структурно не может — не поле World Definition); `BaseTravelHoursPerCell = 0` и `< 0` безопасно откатываются на fallback `4` (без `Infinity`/`NaN`); Save/Load посреди сегмента маршрута сохраняет позицию/прогресс под новым масштабом.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **917/917 passed, 0 failed** (908 + 9 новых, 5 старых тестов обновлены под новую шкалу, остальные не менялись). Живой `eval`: реальный `KingdomSurvivalWorldDefinition` без миграции даёт `BaseTravelHoursPerCell=4`, `CellsPerGameHour=0.25`; вкладка «Мир» с новым блоком «Путешествие» отрепейнчена в живом Editor-цикле — 0 новых ошибок в консоли; `git status` подтвердил отсутствие изменений в `.asset`-файлах на диске.

**Требует ручной проверки в живом Unity (Play Mode):** обычная клетка на normal speed должна проходиться примерно за 10 реальных секунд (4 игровых часа при текущем темпе мира ≈ 1/6 суток), на ×3 — примерно за 3.3с; маршрут в 5–10 клеток должен визуально идти заметно быстрее прежнего, но плавно (не телепортируется) — герой по-прежнему интерполируется через `SegmentProgress`.

## Регулируемый визуальный размер героя и Дома на глобальной карте — 14.09.2026

Дополнение к задаче о масштабе путешествия: герой и Дом на глобальной карте — условные маркеры, а не объекты в физическом масштабе мира (иначе они были бы почти невидимы). Нужен простой способ вручную подбирать соотношение героя/Дома/клетки/арта — но ЧИСТО визуально, не затрагивая скорость, расстояние, координаты, discovery radius, Road Width, Terrain или маршрут.

### Где хранится, в каких единицах

- **`WorldMapVisualTheme`** (Runtime, тот же ассет, где уже живут `BaseMapSprite`/`IconLibrary`/Art Layers — не gameplay terrain settings, не `WorldMapDefinitionData`): новые поля `heroMarkerSizeCells`/`homeMarkerSizeCells` с safe-fallback свойствами `HeroMarkerSizeCells`/`HomeMarkerSizeCells` (тот же паттерн, что `GlobalMapAspect`/`BaseTravelHoursPerCell` — невалидное/нулевое/NaN/Infinity значение откатывается на default, не даёт invisible marker).
- **Единица — доли ЛОГИЧЕСКОЙ клетки карты** (1.0 = визуальный размер одной клетки 104×64), НЕ экранные пиксели — по прямому требованию задачи, чтобы соотношение герой↔Дом↔клетка↔карта было понятным числом, а не пикселями, привязанными к конкретному разрешению.
- **Default**: `0.25` для обоих — подобрано так, чтобы **точно воспроизвести прежний внешний вид**: старый герой был `ArmyMarkerScreenDiameter(8px) / WorldMapBaseCellSizePx(32px) = 0.25` клетки; старый Дом — `CapitalMarkerCellFraction = 0.25` (то же число, случайно совпавшее, но independently подобранное раньше). Старые Visual Theme ассеты получают default через инициализатор поля при десериализации — подтверждено живым `eval` на реальной теме (`HeroMarkerSizeCells=0.25`, `HomeMarkerSizeCells=0.25` сразу после загрузки, без миграции).

### UI

Вкладка «Текстуры» → блок «Визуальный масштаб маркеров» (между «Основа карты» и «Map Art Layers»): слайдер «Размер героя» (0.2–2.0 клетки) и «Размер Дома» (0.1–4.0 клетки — нижняя граница сознательно снижена с предложенных в задаче 0.5, чтобы не блокировать backward-compatible default 0.25), значение сразу подписано в клетках, HelpBox с предупреждением при невалидном значении (структурно недостижимо через сам слайдер, но остаётся защитой на случай ручного/legacy значения через Inspector).

### Как масштабируется с zoom — сознательный отказ от WM-16

Раньше (**WM-16**) маркер героя был явно спроектирован как **константный экранный диаметр**, компенсированный обратно пропорционально zoom (`ArmyMarkerScreenDiameter / zoom` в пикселях) — эта задача **сознательно отменяет то решение** по прямому требованию пользователя: маркер героя теперь задаётся в ПРОЦЕНТАХ карты (как и Дом уже делал), поэтому масштабируется вместе с картой автоматически — `world-map-army-marker`/`world-map-capital-button` оба являются детьми `#world-map`, который получает `transform: scale(zoom)` (WM-11), и процентный размер ребёнка масштабируется вместе с этим transform без ручного пересчёта на каждый кадр zoom. `RefreshWorldMapArmyMarkerScreenSize` переименован в `RefreshWorldMapArmyMarkerSize` и убран из `RefreshWorldMapZoomCompensatedVisuals` (маркер героя больше не нуждается в компенсации — это и была причина этой правки).

- **`RefreshWorldMapCapital`**: `CapitalMarkerCellFraction` заменён на `theme.HomeMarkerSizeCells`.
- **`RefreshWorldMapArmyMarker`/`RefreshWorldMapArmyMarkerSize`**: позиция (`style.left/top`) теперь сама включает смещение на половину ширины/высоты в процентах (как у Дома) — `marginLeft`/`marginTop` (пиксельные, использовались для центрирования старого px-based маркера) обнулены и больше не нужны. Anchor — центр маркера = позиция героя, не изменился.
- Общий хелпер `GetMarkerSizePercent(sizeCells)` — единая точка перевода "доли клетки" → "проценты карты" для обеих осей (`GridWidth`/`GridHeight` раздельные — сетка не квадратная), используется и Домом, и героем.
- **Preview** (`DrawPreviewLocations`): фиксированный квадрат 6×6px заменён на размер, посчитанный из `database.ActiveTheme.HomeMarkerSizeCells` и `mapRect` — тот же источник данных, что runtime, и автоматически масштабируется вместе с zoom/pan Preview (`mapRect` уже включает их). Герой в Preview не отображается (не рисовался и раньше) — по прямому указанию задачи новая большая preview-система под это не строилась.

### Что НЕ затронуто

Map Art Layer Bounds, Base Map, reference canvas 4160×2560, `GridWidth`/`GridHeight`, zoom/pan-математика Preview, discovery radius, Road Width, Terrain, маршрут/скорость/`CellsPerGameHour`, координаты Дома/локаций, collision — размер маркера нигде не участвует ни в одной gameplay-формуле (структурно проверено тестом).

### Тесты (`WorldMapMarkerScaleTests.cs`, 10 тестов)

Hero/Home fallback при 0/отрицательном/NaN/Infinity; валидное кастомное значение используется как есть; перевод клеток в проценты карты корректен (та же формула `100/(GridWidth-1)` и `100/(GridHeight-1)`, что у Bounds/позиций); масштабирование с zoom (через `WorldMapPreviewMath.ApplyZoomPan` — та же математика Preview) даёт пропорциональный рост экранного размера; изменение размера маркера не меняет `WorldMapNavigation.CapitalXPercent/YPercent`; изменение размера маркера не меняет `ContinuousSimulationSystem.CellsPerGameHour`; одно значение размера корректно и без искажения даёт разные (но пропорциональные неквадратной сетке) проценты по X/Y — не независимое растяжение осей.

**Проверено:** компиляция чистая; полный `Run All` EditMode — **927/927 passed, 0 failed** (917 + 10 новых). Живой `eval`: реальная тема без миграции даёт `HeroMarkerSizeCells=0.25`, `HomeMarkerSizeCells=0.25`; вкладки «Текстуры» и «Предпросмотр» отрепейнчены в живом Editor-цикле — 0 новых ошибок в консоли; `git status` подтвердил отсутствие изменений в `.asset`-файлах на диске.

**Требует ручной проверки в Play Mode:** Hero = 0.5/1.0/1.5 клетки и Home = 1.0/2.0/3.0 клетки на нескольких уровнях zoom — позиция не прыгает, герой движется по той же траектории, скорость движения не меняется, Дом не меняет координату, маркер визуально растёт/уменьшается вместе с картой при zoom (не остаётся константного экранного размера, как было до этой задачи).

### AM-08…AM-10 — статус

Не реализованы. Дальнейшие этапы (зональные Encounter — теперь получают Region/Zone/context tags из авторской географии AM-07.5, а не из procedural terrain, как и просил пользователь, — состояния рисунков последствий, LOD/фильтры AM-06 в полном объёме, память/бюджет текстур) по-прежнему требуют либо реальных арт-ассетов, либо длительной ручной Unity-проверки по §19, либо того и другого.

В текущей среде нет Unity Editor и C# toolchain, поэтому компиляция и `Run All` не запускались. После Pull обязательно: дождаться компиляции, запустить EditMode `Run All`, затем в Play Mode проверить паузу/возобновление у Journal/Hero, новую оболочку на разрешениях 1280×720 и 1920×1080, а в `World Map Database` — сохранение текстуры и добавление тестовой локации с появлением в новой игре.

## WM-T05 — автоматический выбор быстрейшего маршрута (Roads) — 14.09.2026

### Старое поведение и его ограничение

`WorldMapNavigation.FindPath` всегда строил геометрически прямой путь между двумя точками карты и ресемплировал его в сегменты движения (`distanceCells → ceil → базовые сегменты → terrain-cost подразделения`). Авторская дорожная сеть (`WorldMapRoadDefinition`, Path+Width) при этом использовалась ТОЛЬКО как gameplay-слой скорости — `WorldMapGameplayTerrainQuery.GetMovementMultiplier` применялся live к текущей позиции героя во время движения, но сам маршрут никогда не строился вдоль дороги специально: если прямая линия не проходила через дорогу, её потенциальное ускорение было недостижимо. Известное следствие (см. раздел «Пересборка масштаба путешествия» выше): `GetTravelHoursRemaining` тоже игнорировал дороги ВПЕРЕДИ по маршруту.

### Новое правило

При построении маршрута система теперь ВСЕГДА сравнивает два кандидата по игровому времени (не по геометрической длине) и выбирает более быстрый:
1. **Прямой путь** (старая логика — сохранена без изменений, только вынесена в переиспользуемый `WorldMapRoutePlanner.BuildDirectPath`).
2. **Путь через дорожную сеть** — если существует маршрут по авторским `Enabled`-дорогам, который быстрее прямого больше чем на небольшой фиксированный эпсилон (`0.01` игрового часа — не новая пользовательская настройка, а защита от нестабильных ничьих).

При отсутствии/поломке/отключении дорог, при их недостижимости или когда дорога не даёт выигрыша — маршрут всегда падает обратно на прямой путь. Единственная публичная точка входа игры, `WorldMapNavigation.FindPath(startX, startY, targetX, targetY)`, не изменилась по сигнатуре — она стала тонким делегатом в новый чистый C#-файл `WorldMapRoutePlanner.cs` (Core, без UnityEngine-зависимостей, `noEngineReferences: true` соблюдён). Все существующие потребители (`GameState.TryStartExpeditionToMapPoint/TryChangeExpeditionRoute/TryOrderReturn/ForceReturnFromSupplyFailure/StopAtDiscoveredLocation/TryResumeInterruptedRoute/GetOrCreateRouteWaypoint/RevealLocationNear`, `WorldMapPopulationService.TravelHoursFromCapital`) получили новое поведение бесплатно — код-ревью подтвердил, что все эти вызовы уже передавали актуальную текущую позицию (не столицу), правки вызывающего кода не потребовались.

### Дорожный граф — почему Dijkstra, а не A*

Скрытая сетка 104×64 остаётся ЧИСТО техническим субстратом измерения расстояния/стоимости — она никогда не становится дорожным графом, и grid A* по ней не строится (прямое требование задачи). Вместо этого строится маленький граф ТОЛЬКО из авторских точек дорог:
- **Узлы** — точки (`Points`) всех `Enabled`-дорог с `Points.Count >= 2`.
- **Рёбра** — соседние точки ОДНОЙ дороги (двунаправленные), стоимость ребра — ожидаемые игровые часы (см. ниже).
- **Junction-правило**: две разные дороги соединяются в один узел графа ТОЛЬКО если их точки совпадают в cell-пространстве в пределах малого допуска `WorldMapRoutePlanner.JunctionMergeToleranceCells = 0.05` клетки. Визуально пересекающиеся, но не имеющие общей авторской точки линии узел НЕ разделяют (проверено тестом `BuildRoadGraph_VisuallyCrossingLinesWithoutSharedPoint_AreNotAutomaticallyConnected` — прямой подсчёт узлов графа через reflection, не косвенное сравнение времени в пути, которое оказалось ненадёжным индикатором). Отдельная база перекрёстков не вводится.

Поиск кратчайшего (по времени) пути — простой O(V²) Dijkstra без приоритетной очереди: граф заведомо маленький и строится нечасто (по требованию задачи — не преждевременная оптимизация), а O(V²)-версия проще сделать детерминированной (см. ниже), чем версия на `Dictionary`/куче с недетерминированным порядком перебора.

### Как старт/цель попадают в граф — виртуальные проекционные узлы

Игрок не обязан кликать точно на авторскую точку дороги, чтобы попасть на неё. Для КАЖДОГО ребра дорожного графа (не только ближайшего — сеть маленькая, перебор всех сегментов не является преждевременной оптимизацией) вычисляется ближайшая проекция старта и отдельно цели на этот сегмент в cell-пространстве (`ProjectPointOntoSegmentCellSpace` — стандартная clamped-t проекция, отдельно масштабирующая X/Y на `(GridWidth-1)/100`/`(GridHeight-1)/100` до проекции, поскольку сетка не квадратная 104×64 — наивная процентная проекция была бы геометрически перекошена). Эти проекции — временные узлы только для текущего расчёта пути; они никогда не записываются обратно в `WorldMapRoadDefinition.Points`. Алгоритм подключает их к виртуальным `startVirtual`/`targetVirtual` узлам и естественно находит лучшую комбинацию точки входа/выхода по всей сети через обычный Dijkstra, без отдельного «найти ближайшую дорогу» шага.

### Формула стоимости

`travelHours = distanceCells × BaseTravelHoursPerCell(мира) × terrainCost(Plains=1/Hills=2/Mountains=3) / gameplayMultiplier` — та же самая формула cost/terrain, что и раньше в `FindPath`, плюс **live**-запрос `WorldMapGameplayTerrainQuery.GetMovementMultiplier` (Road ×1.3 и т.д.) в середине каждого сравниваемого отрезка. `BaseTravelHoursPerCell` берётся из активного `WorldMapDefinitionData` (не хардкод `4`), с тем же fallback-паттерном, что и остальная система travel-scale.

**Road-множитель применяется РОВНО один раз**: planner использует его ТОЛЬКО для сравнения/оценки кандидатов при выборе маршрута — он никогда не запекается в итоговый `Route` (`MapPointData` не хранит скорость). Во время реального движения `ContinuousSimulationActivities.AdvanceExpeditionMovement` (не изменён) продолжает live-запрашивать множитель от ФАКТИЧЕСКОЙ текущей позиции героя на каждом тике — а поскольку выбранный путь геометрически идёт вдоль центральной линии дороги (если она была выбрана), рантайм естественно обнаруживает ускорение под ногами героя без какого-либо ручного протаскивания множителя в данные маршрута. Тест `EstimateSegmentHours_RoadMultiplier_AppliedExactlyOnce` явно проверяет, что множитель не применяется дважды (`4.0/1.3`, а не `4.0/1.3/1.3`). `Road.Width` остаётся исключительно радиусом gameplay-коридора для этого live-запроса — он НЕ используется как стоимость маршрутизации, шаг узлов или визуальный размер.

### Граф ≠ сегмент движения — ресэмплинг

Критическое требование задачи: одно ребро графа (например, 20-клеточный участок дороги) не должно схлопываться в один сегмент движения — иначе герой прошёл бы его за долю положенного времени. Поэтому выбранный путь (прямой ИЛИ через граф) всегда прогоняется через общий `WorldMapRoutePlanner.BuildMovementRouteAlongPolyline` — ту же самую логику `distanceCells → ceil → базовые сегменты → terrain-cost подразделения`, что раньше жила только внутри старого `FindPath` для одного прямого отрезка, теперь обобщённую на произвольную N-точечную ломаную (полилинию) и применяемую к каждому геометрическому ребру выбранного пути отдельно. Тест `BuildMovementRouteAlongPolyline_LongRoadEdge_IsNotSingleMovementSegment` подтверждает это напрямую.

Метрика расстояния — единая шаренная функция `WorldMapRoutePlanner.DistanceCellsBetween` (та же анизотропная cell-space формула `dxCells=dxPercent×(GridWidth-1)/100`, `dyCells=dyPercent×(GridHeight-1)/100`, `distanceCells=sqrt(dxCells²+dyCells²)`), которую теперь использует и planner, и рефакторенный `WorldMapNavigation.CalculateGeometricDistanceCells` — формула больше не дублируется в двух местах.

### Детерминизм

Одинаковые входные данные (World Definition, Roads, старт, цель) всегда дают идентичный маршрут: узлы графа строятся в фиксированном порядке (порядок списка `Roads`, затем порядок `Points` внутри дороги), Dijkstra — O(V²) с перебором id узлов по возрастанию при равенстве расстояний (не через `Dictionary`/хеш-порядок), виртуальные/проекционные узлы добавляются в фиксированном порядке перебора рёбер. Проверено тестом `FindFastestRoute_SameInputs_AlwaysProducesIdenticalRoute`.

### ETA — фикс известного ограничения

Введён единственный чистый helper `WorldMapRoutePlanner.EstimateRouteTravelHours(route, routeIndex, segmentProgress, definition)`, учитывающий `BaseTravelHoursPerCell`, уже запечённые terrain-cost подразделения, LIVE gameplay-множитель на оставшихся сегментах (включая дороги ВПЕРЕДИ по маршруту — то, чего не хватало раньше) и частичный прогресс на текущем сегменте. И `ContinuousSimulationSystem.GetTravelHoursRemaining`, и `CalculateTravelHours` теперь делегируют в этот ОДИН helper (не несколько независимых формул) — `RouteDelayHoursRemaining` по-прежнему прибавляется отдельно поверх. Тест `CalculateTravelHours_MatchesNewRouteCost` подтверждает согласованность обеих точек вызова.

### Return / retarget / прерванный маршрут / discovery / отрисовка

- `TryOrderReturn`/`ForceReturnFromSupplyFailure` автоматически используют ту же самую fastest-route логику — отдельного алгоритма возврата нет и не появилось (оба вызывают тот же `FindPath`).
- Retarget и возобновление прерванного маршрута (`TryResumeInterruptedRoute`) уже вызывали `FindPath` от ФАКТИЧЕСКОЙ текущей позиции (не столицы) — код-ревью подтвердил, правок не потребовалось.
- Discovery вдоль фактического (возможно, изогнутого через дорогу) пути — `LastTravelPoints`/`FindFirstHiddenLocationAlongLastTravel` не менялись и продолжают работать по факту итерации по полному `Route` независимо от его геометрии.
- Отрисовка маршрута (`PrototypeUIController.DrawRoute`/`AddRouteDashes`) уже итерировала по КАЖДОЙ последовательной паре точек полного `Expedition.Route` независимо от геометрии (это было нужно и раньше — для Hills/Mountains подточек) — правок кода не потребовалось. Сама авторская геометрия дороги (`Road.Points`) по-прежнему НЕ рисуется как видимая дорога в обычном геймплее — только в Editor Preview/authoring overlay; нарисованный арт карты остаётся единственным источником истины визуальной географии.

### Save/Load

Граф НЕ сохраняется (он выводится из World Definition при каждом построении маршрута). Существующие снапшот-поля (`Expedition.Route`, `RouteIndex`, `SegmentProgress`, `CurrentMapX/YPercent`, `TargetMapX/YPercent`, `InterruptedTarget...`) остаются достаточными без изменений — сохранение посреди уже построенного через дорогу маршрута корректно продолжается после загрузки без телепортации; новый граф/маршрут пересчитывается только при следующем приказе/перестроении. Подтверждено тестом `SaveLoad_MidRoadRoute_PreservesPositionAndContinuesWithoutTeleport`.

### Устойчивость к некорректным данным

Planner не падает и не бросает исключений на `null`/пустых/отключённых Roads, `road == null` в списке, `Points == null`, `Points.Count < 2`, `null`-точках, дублирующихся точках, нулевой длине сегмента, `Width <= 0`, `NaN`/`Infinity` координатах — все такие дороги/сегменты безопасно пропускаются, прямой маршрут остаётся конечным fallback'ом. Проверено тестами `FindFastestRoute_NullRoadsList_DoesNotThrow`/`_RoadWithNaNPoint_DoesNotThrowAndFallsBackSafely`/`_NullRoadEntryInList_DoesNotThrow`.

### Файлы

- **Новый**: `Assets/_Project/Scripts/Core/WorldMapRoutePlanner.cs` — весь planner (публично: `FindFastestRoute`, `BuildDirectPath`, `BuildMovementRouteAlongPolyline`, `DistanceCellsBetween`, `EstimateGeometricPathHours`, `EstimateRouteTravelHours`, `GetBaseTravelHoursPerCell`; приватно: построение графа, junction-merge, проекция на сегмент в cell-space, O(V²) Dijkstra).
- **Изменён**: `WorldMapNavigation.cs` — `FindPath` теперь тонкий делегат в `WorldMapRoutePlanner.FindFastestRoute`; `CalculateGeometricDistanceCells` рефакторен на общий `DistanceCellsBetween`; удалён более не нужный приватный `Lerp`.
- **Изменён**: `ContinuousSimulationClock.cs` — `GetTravelHoursRemaining`/`CalculateTravelHours` рефакторены на единый `WorldMapRoutePlanner.EstimateRouteTravelHours`.
- **Новый**: `Assets/_Project/Tests/EditMode/WorldMapRoutePlannerTests.cs` — 29 новых тестов (DIRECT/ROAD/GRAPH/DISTANCE-RESAMPLING/CHOICE/RUNTIME CONTRACT/GAMESTATE/SAVE-LOAD/INVALID DATA).

**Проверено:** компиляция чистая; полный `Run All` EditMode — **956/956 passed, 0 failed** (927 базовых без единого изменения/удаления + 29 новых). Ручная проверка через живой `eval` в Unity Editor: (A) большой бесполезный крюк дороги → выбирается прямой путь; (B) небольшое смещение дороги (сдвиг больше половины `Width`, иначе прямой кандидат физически лежит внутри коридора дороги и получает бонус скорости «бесплатно» — этот нюанс стоил одной итерации на тестовом сценарии) с реальным выигрышем по времени → выбирается дорога (`usesRoad=True`); (D) отключение дороги (`Enabled=false`) → маршрут идентичен прямому; (E) `FindPath` от произвольной текущей позиции (не столицы) корректно строит маршрут от неё; (F) возврат домой использует ту же fastest-route логику (структурно гарантировано — `TryOrderReturn` вызывает тот же `FindPath`, отдельно проверено на сценарии с дорогой, ведущей прямо к столице). Save/Load и рендеринг маршрута проверены тестами и код-ревью (не отдельным ручным Play Mode прогоном).

### Известные ограничения / вне рамок WM-T05

Grid A* / Unity NavMesh, ручное построение маршрута игроком, очередь waypoint'ов, известные/неизвестные дороги, мосты/эстакады, односторонние дороги, повреждение/состояние дороги, динамически заблокированные дороги, караваны/трафик, владение дорогами/пошлины, NPC pathfinding AI, процедурные дороги, миникарта, изменения арта карты, изменения размера маркеров Героя/Дома, изменение баланса `BaseTravelHoursPerCell`, новые типы территории, новая система снабжения — всё сознательно не затронуто (см. полную спецификацию задачи).

## Канон v1.37 — производственное упрощение систем — 19.09.2026

По прямому решению пользователя обновлён единственный актуальный канон: v1.36 перенесён в `ProjectDocs/Archive`, в корне создана v1.37.

Зафиксировано:

- обычное путешествие без постоянных режимов `Быстро / Обычно / Исследовать`; разведка — отдельное действие `Исследовать область` с затратой времени;
- один регулярный ресурс экспедиции `Припасы`, без набора отдельных походных шкал;
- изнеможение только дискретными состояниями `Нормально / Измотан / Истощён`, без числовой fatigue-шкалы;
- погода только как авторское состояние/событие, без глобальной метеосимуляции;
- защита Дома как компактная оценка причинных факторов без RTS/гарнизонной мини-игры;
- финальная экономика Дома: деньги, Запасы Дома и конкретные люди; числовое настроение и глубокая ежедневная экономика не являются целевой моделью;
- постоянные бойцы приходят из мира и отношений, а не производятся платной очередью;
- crafting и инвентарь остаются компактными: для обычного бойца ориентир `оружие + защита/одежда + один особый предмет`;
- обычный Encounter ограничен производственно: место/состояние + один значимый контекст; второй дополнительный контекст — только для важных сцен.

`ProjectDocs/NARRATIVE.md` синхронизирован с production-лимитом Encounter и правилом сценарной погоды.

**Важно:** код экономического прототипа, очереди найма, будущей защиты Дома, Припасов/изнеможения и команды исследования в этой задаче не переписывался. Расхождение с новым каноном является отдельной задачей синхронизации после приоритизации первого региона. Несвязанные локальные изменения карты/маршрутизации сохранены.

## Канон лагеря v1.36 — 19.09.2026

По прямому утверждению пользователя обновлён общий канон до v1.36. Зафиксировано: место стоянки наследует контекст глобальной карты и меняет прежде всего события; Camp Screen является экраном текущего отряда в походе; содержательная ночь использует компактный ориентир до двух действий; спокойная ночь проходит быстро без обязательного микроменеджмента; лагерь служит каналом человеческого и отложенного эха дневных решений; ночные события используют общую Encounter Grammar по сочетанию места, региона, прошлых событий, состава, ранений, Narrative State и времени. Отдельная прокачка/строительство лагеря не вводятся.

Изменён только проектный канон и технический журнал; runtime-код и assets не менялись. Компиляция и Unity-тесты для этой документационной фиксации не требовались и не запускались.

## Аудит проекта и сверка канона v1.38 — 19.09.2026

Записи «Канон v1.37» и «Канон лагеря v1.36» выше были сделаны в двух параллельных сессиях от одной и той же v1.35 и независимо друг от друга: ветка v1.37 не видела ещё не существовавших на момент её ответвления правил лагеря, а ветка «лагерь» не видела ещё не существовавшего пакета производственного упрощения. При `git merge` обеих веток (коммит с сообщением «43») оба корневых канон-файла (`v1_36.md` и `v1_37.md`) оказались одновременно в репозитории как «единственный актуальный», а этот файл (`DEVELOPMENT_STATUS.md`) получил незакрытые git-конфликты прямо в тексте (маркеры `<<<<<<< HEAD` / `=======` / `>>>>>>>` вокруг записей WM-T05 и «Канон лагеря v1.36» выше) — то есть сломанный merge был закоммичен и запушен в `main` без ручного разрешения конфликта.

Исправлено при аудите:

- в этом файле убраны конфликт-маркеры, обе исторические записи сохранены без изменений;
- корневой канон сведён к единственному актуальному файлу `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_38.md` — это полный текст бывшей v1.37 (производственное упрощение) плюс возвращённые правила лагеря §24.2.1–24.2.5 из ветки v1.36, которые отсутствовали в v1.37 именно из-за более раннего момента ответвления, а не по решению отменить их;
- бывший `v1_37.md` переименован в `v1_38.md`; бывший корневой `v1_36.md` (ветка «лагерь») перенесён в `ProjectDocs/Archive/KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_36b_camp_branch.md` с поясняющей заметкой — под именем `v1_36.md` в Archive уже лежит другой документ (правки маршрута/дорог), поэтому прямое архивирование под тем же именем создало бы второй конфликт версий;
- новых игровых решений эта сверка не вводит — оба набора решений уже были утверждены пользователем по отдельности, объединены только сами документы.

Изменён только проектный канон и технический журнал; runtime-код и assets не менялись. Компиляция и Unity-тесты для этой документационной правки не требовались и не запускались.

## Терминология «Припасы» и пометки временного слоя — 19.09.2026

Первый шаг закрытия разрыва между каноном v1.38 и кодом. По решению пользователя выбран вариант «пометить и подождать»: меняется только то, что не требует уже принятого решения по первому региону.

### Что оказалось не разрывом

Проверка кода перед правкой показала, что часть «долга» v1.38 — это ненаписанные функции, а не противоречащий код:

- постоянных режимов темпа `Быстро / Обычно / Исследовать` в коде никогда не существовало — канон отменил то, чего не было; отдельная команда `Исследовать область` остаётся будущей функцией;
- числовой шкалы усталости в коде тоже нет — рвать нечего, дискретные состояния `Нормально / Измотан / Истощён` являются будущей функцией;
- единый ресурс экспедиции уже реализован как `GameState.ArmySupply` + `ExpeditionSupplyConsumption` с суточным расходом в полночь и принудительным возвратом при второй подряд нехватке (`ContinuousSimulationDaily.ResolveExpeditionSupplyAtMidnight`), а экран героя уже показывает остаток в днях и суточный расход. Это фактически §9.7.1 — расхождением была только терминология.

### Терминология

Игровые тексты переведены со «снабжения» на каноничные **`Припасы`** (§9.7.1): `ContinuousSimulationActivities`, `ContinuousSimulationDaily`, `ExpeditionDecisionSystem`, `ExpeditionIncidentSystem`, `GameState`, `PrototypeUIController.Camp/Debug/LocationInteraction`, подпись `ПРИПАСЫ` в `Prototype_Main.uxml` и подпись поля в `WorldMapDatabaseWindow`. Изменены только строки для игрока — сериализованные имена полей (`ArmySupply`, `ExpeditionSupplyConsumption`, `RewardArmySupply`, `SupplyDelta`) намеренно НЕ переименованы, чтобы не ломать существующие сохранения и не трогать API ради косметики.

### Пометки временного слоя

Три места, где код прямо противоречит канону, помечены в самом коде со ссылкой на пункт канона, вместо того чтобы молча расходиться с документом:

- `BuildingSystem.BaseDailyGoldIncome/BaseDailyFoodIncome` — §10, целевая экономика Дома это деньги, Запасы Дома и конкретные люди;
- `BuildingSystem.RecruitGoldCost/RecruitHours` — §10.1, бойцы должны приходить из мира и отношений, а не производиться очередью;
- `GameState.TotalArmyDefensePower/GarrisonFighterCount` — §13.1, защита Дома выводится из причинных факторов, а не из суммарного числа силы. Прежний комментарий («не является утверждённой системой защиты») устарел после v1.38 и заменён точной ссылкой.

Очередь найма сознательно НЕ удалена: это единственный способ получить бойца в прототипе, а заменяющий сюжетный источник «человек пришёл из мира» упирается в неутверждённый первый регион. Удаление оставило бы прототип неиграбельным ради буквального соответствия документу.

**Проверка:** Unity Editor и C# toolchain в этой среде недоступны — компиляция и `Run All` не запускались. Правки текстовые и не меняют сигнатур; тесты на переименованные строки не завязаны (проверено grep: «снабжение» встречается в тестах только в комментариях). После Pull дождаться компиляции и прогнать EditMode `Run All`, затем глазами проверить экран героя, лагерь и Хронику на новые формулировки.


## Канон v1.39 — осмысленное ручное действие — 20.09.2026

По прямому решению пользователя добавлено общее производственное правило: игрок не должен вручную обслуживать систему только потому, что подобное действие привычно для RPG.

Зафиксировано:

- ручное действие сохраняется, когда само создаёт выбор, риск, информацию, отношение, характер или заметное последствие;
- повторяемое действие с очевидно правильным ответом автоматизируется, объединяется с более важным решением или удаляется;
- ежедневная раскладка еды, назначение готовки, перенос материалов в служебный слот, повторное распределение бойцов по стенам и отдельные экраны ради малых пассивных бонусов не требуются как самостоятельные механики;
- тот же принцип действует для UI: лишние подтверждения, промежуточные меню и drag-and-drop не добавляются без собственной игровой функции;
- тот же принцип действует для нарратива: если смысл сцены лучше передаётся несколькими репликами, образом и решением, длинный текст не является достоинством сам по себе.

Обновлены общий канон и `ProjectDocs/NARRATIVE.md`. Runtime-код и assets не менялись. Unity-компиляция и тесты для этой документационной канонизации не требовались и не запускались.


## Канон v1.40 — семантическая память и вариативное повторное прохождение — 20.09.2026

По прямому утверждению пользователя зафиксировано направление, в котором одна авторская история должна собираться в разные личные версии прохождения без кратного роста объёма текста.

Утверждено:

- значимое знание может хранить не только факт, но и источник/канал получения, если это меняет будущую реакцию;
- присутствующие люди меняют драматическую функцию сцены, а не только дают числовой бонус;
- не все NPC обязаны говорить в каждой версии, а сюжетная функция может причинно перейти к другому человеку;
- важнее отложенная реактивность будущего разговора, чем одна мгновенная контекстная реплика;
- изменившееся состояние места или человека может сократить, заменить или отменить старую сцену;
- важные диалоги проектируются через смысловые функции, чтобы отличать повтор известного факта от новой интерпретации;
- утверждено направление необязательного режима `Сокращать уже прочитанное`, использующего отдельную межкампанийную память игрока и никогда не переносящего знания герою;
- точные ID, схема данных, правила идентичности блоков и UI режима остаются открытыми до проверки на реальных сценах первого региона;
- все новые правила подчиняются существующему производственному пределу реактивности и не разрешают комбинаторный взрыв диалогов.

Обновлены общий канон и `ProjectDocs/NARRATIVE.md`. Runtime-код и assets не менялись. Unity-компиляция и тесты для документационной канонизации не требовались и не запускались.
