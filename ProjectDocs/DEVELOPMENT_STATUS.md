# Kingdom Survival — Development Status

Последнее обновление: 2026-09-13

> Технический журнал фактически реализованного состояния Unity-проекта и зафиксированных проектных решений.
>
> Актуальный общий канон: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_32.md`.
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
