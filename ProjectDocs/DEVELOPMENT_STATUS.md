# Kingdom Survival — Development Status

Последнее обновление: 2026-09-12

> Технический журнал фактически реализованного состояния Unity-проекта и зафиксированных проектных решений.
>
> Актуальный общий канон: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_31.md`.
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

В подключённой среде Unity Editor, C# compiler, Test Runner и Play Mode недоступны. Поэтому код P11/P12/P13 подготовлен и снабжён EditMode regression-тестами, но **компиляция, `Run All` и фактический Play Mode не запускались**. Для P13 выполнены доступные статические проверки Unity YAML, связности D17, уникальности ID/эффектов, completion-инвариантов и чистоты diff.

Последний ранее зафиксированный большой прогон: **330 тестов: 328 passed / 2 failed**; оба прежних падения относились к ошибочной ссылке N01, которая затем была исправлена. После последующих P09/P10/UI/P11/P12 изменений нужен новый полный `Run All`.

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

Сознательно не делали в этом проходе: не формализовал слоты появления (WM-07) и не менял `GameState.CreateNewGame`/размещение стартовых локаций — это более рискованная правка (затрагивает `GameState`, которую используют минимум 7 EditMode test-файлов, включая travel-time и reward-логику), и её стоит делать отдельным проходом после проверки WM-04/WM-06 в Unity, а не наслаивать непроверенные изменения.

Компиляция/тесты не запускались с моей стороны.
