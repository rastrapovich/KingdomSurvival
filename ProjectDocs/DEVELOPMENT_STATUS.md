# Kingdom Survival — Development Status

Последнее обновление: 2026-09-11

> Технический журнал фактически реализованного состояния Unity-проекта и зафиксированных проектных решений.
>
> Актуальный общий канон: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_30.md`.
>
> Единая энциклопедия мира: `ProjectDocs/LORE.md`.
>
> Единая нарративная библия: `ProjectDocs/NARRATIVE.md`.
>
> Движок: Unity 6000.5.10f1. UI: UI Toolkit.

## 0. Архив технического журнала

Полное состояние журнала непосредственно перед исправлением приоритета Narrative Dialogue сохранено без изменений в:

`ProjectDocs/Archive/DEVELOPMENT_STATUS_2026-09-11_before_narrative_layering_fix.md`.

В нём сохранены подробные записи текущего цикла UI-миграции, включая Camp Screen, Incident/Game Over, Journal, Hero Screen, Narrative Dialogue, portrait preset `ML` и предыдущий P09 hotfix кнопки Лагеря. Более ранние состояния остаются в `ProjectDocs/Archive`.

Текущий файл сокращён до реально актуального рабочего состояния и ближайших проверок; история не удалена.

## 1. Текущий этап

Проект находится на этапе **«Нарративный фундамент / production-проход первой главы»**.

Главный производственный приоритет:

`герой → нормальная жизнь Дома → паводок/ремонт → причинные симптомы → расследование → дальняя дорога → нижние люди и старое соглашение → возвращение → последствия → главная тайна → антагонистическая сила → первый регион → первый большой квест`.

Текущая активная задача — **P09: первая дальняя дорога и Лагерь**. Новые крупные универсальные системы до доказательства первого региона не являются приоритетом.

## 2. Подтверждённый baseline и ограничения проверки

Последний зафиксированный пользователем большой прогон до текущих P09/UI-изменений: **330 тестов: 328 passed / 2 failed**; оба падения относились к ошибочной ссылке N01, которая затем была исправлена.

После последующих UI/P09-изменений новый полный `Run All` ещё не подтверждён.

В подключённой среде Unity Editor, C# compiler, Test Runner и Play Mode недоступны. Поэтому текущий commit проверяется по коду и структурным regression-тестам, а фактическая компиляция и runtime-поведение должны быть подтверждены после Pull в Unity.

## 3. Актуальный UI-фундамент

На общем UXML/UI Toolkit + UI Конструктор фундаменте уже находятся:

- Camp Screen;
- Journal;
- Hero Screen;
- Narrative Dialogue;
- Incident/Game Over;
- общая `UILayoutDatabaseAsset`/`UILayoutScreenBinder` схема.

Narrative Dialogue использует статичный `narrative-dialogue-overlay` из `Prototype_Main.uxml`, а C# отвечает за данные, динамическую историю/варианты ответа, проверки и портреты.

Портретный контракт — `5:7`. Канонические presets: `XS 100×140`, `S 150×210`, `M 200×280`, `ML 250×350`, `L 300×420`, `XL 400×560`. Диалоговый портрет не закреплён за одним размером и может использовать любой канонический preset.

Подробная история UI-M02/UI-M03/UI-M04 и portrait migration сохранена в архиве из §0.

## 4. P09 — первая дальняя дорога и Лагерь

Реализованный production-каркас:

- физическое движение по дальней дороге и непрерывное время;
- обязательная N11 «Дорога, которой нет» примерно на 30% маршрута;
- пассивная RoadReading-проверка и реальное разветвление маршрута;
- гарантированная встреча D11B «Трое под телегой» примерно на 60% маршрута;
- четыре взаимоисключающих исхода D11B;
- цена помощи во времени;
- `CampUnlocked` после любого исхода D11B;
- Camp Screen v1 как отдельный fullscreen UI-слой без CampManager/survival-подсистемы;
- `Chapter01CampSceneProvider`;
- первая лагерная сцена D11C «После телеги» как отложенное эхо решения у телеги;
- открытие Лагеря останавливает время, не сбрасывая маршрут;
- «ПРОДОЛЖИТЬ ПУТЬ» возвращает прежнее состояние движения/остановки.

Ручная проверка пользователя: P09-T01, P09-T02 и P09-T03 считаются пройденными. P09-T04/P09-T05/P09-T06 пока остаются **«Нужна проверка в Unity»**.

## 5. Предыдущий P09 hotfix — обновление кнопки Лагеря

Исправлено состояние, при котором после `CampUnlocked` кнопка `Лагерь` могла появиться, но оставаться disabled после снятия блокировки.

`RefreshCampNavButtonState()` подключён к постоянным путям обновления UI:

- `RefreshStableUiAfterStateChange()`;
- `RefreshContinuousTimeUi(true)`.

Если Camp открыт, в этих же путях обновляется `RefreshCampScreen()`.

Добавлен `CampUiRefreshTests.cs`, фиксирующий эту интеграцию. Само правило доступности Лагеря не ослаблялось: нужен `CampUnlocked`, активная экспедиция и отсутствие другого обязательного блокирующего события.

## 6. Текущий P09 hotfix — Narrative Dialogue всегда поверх fullscreen-экранов

### 6.1. Симптом

Camp Screen открывался корректно, после чего автоматически запускалась D11C «После телеги», но окно диалога визуально оказывалось **под** Camp Screen.

### 6.2. Причина

`OpenCampScreen()` корректно вызывает `campScreen.BringToFront()`.

После UI-M04 `TryOpenNarrativeDialogueById()` показывал `narrativeDialogueOverlay` через:

`narrativeDialogueOverlay.style.display = DisplayStyle.Flex;`

но не возвращал overlay на вершину sibling stack. Поэтому ранее поднятый Camp оставался выше диалога.

Это проблема UI-layering, а не данных D11C, Dialogue Database или `Chapter01CampSceneProvider`.

### 6.3. Исправление

В `PrototypeUIController.Narrative.cs` при каждом успешном открытии диалога сразу после `display = Flex` теперь выполняется:

`narrativeDialogueOverlay.BringToFront();`

Инвариант: **активный Narrative Dialogue — верхний блокирующий gameplay-слой**. Если диалог запускается из Camp/Hero/Journal или другого fullscreen-экрана, он поднимается поверх него в момент открытия.

Camp `BringToFront()` сохранён. Fullscreen coordinator не добавлялся. Архитектура UI, канон, `NARRATIVE.md`, логика сцены D11C, карта, время и боевые системы не менялись.

### 6.4. Regression-проверка

Добавлен:

`Assets/_Project/Tests/EditMode/NarrativeDialogueLayeringTests.cs`.

Тест фиксирует порядок внутри `TryOpenNarrativeDialogueById()`:

`display = Flex → BringToFront() → DisplayNarrativeView(...)`.

Таким образом будущая UI-переработка не должна снова оставить Narrative Dialogue под ранее поднятым fullscreen-экраном.

## 7. Изменённые файлы текущего hotfix

- `Assets/_Project/UI/PrototypeUIController.Narrative.cs`;
- `Assets/_Project/Tests/EditMode/NarrativeDialogueLayeringTests.cs`;
- `Assets/_Project/Tests/EditMode/NarrativeDialogueLayeringTests.cs.meta`;
- `ProjectDocs/DEVELOPMENT_STATUS.md`;
- `ProjectDocs/Archive/DEVELOPMENT_STATUS_2026-09-11_before_narrative_layering_fix.md` — точная архивная копия журнала до этого hotfix.

Несвязанные файлы и пользовательские изменения не входят в hotfix.

## 8. Что проверить после Pull

1. Дождаться чистой Unity-компиляции и убедиться, что Console без C# errors.
2. Запустить полный EditMode `Run All`, включая `CampUiRefreshTests` и новый `NarrativeDialogueLayeringTests`.
3. Пройти P09 обычным игровым путём до D11B «Трое под телегой» и завершить любой исход.
4. Убедиться, что кнопка `Лагерь` появляется и становится активной при продолжающейся экспедиции.
5. Открыть Camp Screen: время/маркер должны остановиться, маршрут не должен сброситься.
6. При первом входе после D11B должна автоматически открыться D11C «После телеги» **поверх Camp Screen**.
7. Завершить D11C: диалог должен исчезнуть, а Camp Screen остаться видимым под ним.
8. Нажать «ПРОДОЛЖИТЬ ПУТЬ» и убедиться, что отряд продолжает тот же маршрут с той же позиции.
9. Отдельно проверить отрицательное условие: дома или без активной экспедиции кнопка Лагеря остаётся недоступной.

После успешного ручного прохода P09-T04/P09-T05 можно отметить выполненными. P09-T06 — после зелёного полного `Run All`.

## 9. Следующий шаг

Сначала подтвердить текущий P09 hotfix в Unity по §8.

После зелёной проверки продолжить production-проход первой главы с P10, не расширяя Camp Screen в отдельную survival-систему раньше сюжетной необходимости.

## 10. P10 — «Старый брод и люди ниже по течению» (реализовано, не проверено)

Код и данные для P10-T01…T04 написаны в этой сессии в удалённой среде без
Unity Editor/C# compiler/Test Runner — **компиляция и `Run All` не
запускались**. Перед тем как отмечать P10-T01…T04 выполненными в
DevelopmentPlanSeedData, обязательно пройти §11 ниже.

**P10-T04 (PartySize = герой + бойцы, 1..5):**
- `NarrativeEvaluationContext` (`Assets/_Project/Scripts/Core/NarrativeState.cs`) получил свойство `PartySize` — новый опциональный параметр конструктора `int? partySize`, по умолчанию `PresentCompanionIds.Count + 1`.
- `Chapter01ContextBuilder.GetPartySize()` (`Assets/_Project/Chapter01/Runtime/Chapter01ContextBuilder.cs`) теперь возвращает `FighterIds.Count + 1` (было `FighterIds.Count`, включая 0 без экспедиции); `Build()` передаёт это значение в контекст явно.
- `NarrativeConditionType.PartySizeAtMost` добавлен в конец enum (индекс 13); `PartySizeAtLeast`/`PartySizeAtMost` в `NarrativeConditions.cs` теперь читают `context.PartySize`, а не `PresentCompanionIds.Count`.
- Мигрированы два условия `PartySizeAtLeast` в D11B («Трое под телегой», `KingdomSurvivalDialogues.asset`): старое `>=1 боец` → `PartySize>=2 AND PartySize<=2`, старое `>=2 бойца` → `PartySize>=3`. Все существующие P09-тесты по составу отряда (`Chapter01P09Tests.cs`) проверены вручную построчно на новую семантику — поведение не меняется, но `Run All` не запускался.
- `DialogueDatabaseWindow.cs` — добавлены русские подписи и поля инспектора для `PartySizeAtLeast`/`PartySizeAtMost` (раньше `PartySizeAtLeast` не имел ни подписи, ни собственного поля).
- `NarrativeDialogueRuntimeSession.Start()` и `PrototypeUIController.Narrative.cs` получили опциональный/явный параметр `partySize`, использующий `Chapter01ContextBuilder.GetPartySize()`.

**P10-T01/T02 (N12/N13):** оба диалога переведены из scaffold (`"черновик"`) в production (`status: 1`, тег `"production"`) в `KingdomSurvivalDialogues.asset`. N12 — линейный ствол (прибытие → материальный факт брода, независимый от характеристик → женщина → бытовой контакт → история утопленницы как человеческое свидетельство, без утверждения сверхъестественной природы → реактивные блоки на RepairOld/RepairNew/SevenToothGauge/OldCustom/OldSeventhChannel/WaterFlowIsWrong/PartySizeAtMost(2)/PartySizeAtLeast(4) → опциональная бытовая помощь женщине → направление дальше). Новый спикер `ford_woman` заменил старый scaffold-спикер `ford_witness` («Ребёнок Дома»). Новый флаг `Chapter01Ids.Flags.FordWomanHelped` — единственное эхо N12 в N13. N13 показывает последствие раньше объяснения, поведенческий выбор «дать понять, что пришли говорить» / «подойти как есть» — только presentation, без отдельного WeaponCondition и без влияния на бросок.

**P10-T03 (FirstContact):** `chapter01.check.first_contact` — `ActiveDecisive`, `Quality: Character`, `CompetencyId: ""`, `Difficulty: 13`, модификаторы `KnowledgeKnown(OldCustom) +1` и `PartySizeAtLeast(4) -1`. Успех/провал ведут в разные узлы (`chapter01.node.13.success`/`.failure`), оба выдают `DownstreamPeople`/`DownstreamContact` — обязательный путь не блокируется провалом. `OldAgreement`/`SharedWaterSystem`/`HomeWasNotSelfSufficient` в P10 не выдаются (материал N14).

**Тесты:** новый `Assets/_Project/Chapter01/Tests/EditMode/Chapter01P10Tests.cs` — структура/валидация D12/D13, реестр ID, PartySize (1..5 по числу бойцов), условия `PartySizeAtLeast`/`PartySizeAtMost`, регрессия D11B, N12 (флаг/знания/помощь/реактивные блоки), N13 (реактивные блоки, FirstContact spec, успех/провал, модификатор OldCustom, невозможность повторного броска, сохранение через `JsonUtility` round-trip). Файл написан, но **не скомпилирован и не запущен** в этой сессии.

## 11. Что проверить после Pull (P10)

1. Дождаться чистой Unity-компиляции, Console без C# errors (особенно из-за сдвинутой сигнатуры `NarrativeEvaluationContext`/`NarrativeDialogueRuntimeSession.Start`/`Chapter01ContextBuilder.GetPartySize`).
2. Запустить полный EditMode `Run All`, включая новый `Chapter01P10Tests.cs` и уже существующий `Chapter01P09Tests.cs` (регрессия D11B).
3. Открыть Dialogue Database Editor, убедиться, что D12/D13 проходят Validation без ошибок, а `PartySizeAtLeast`/`PartySizeAtMost` корректно отображаются в инспекторе условий.
4. Пройти N12/N13 вручную: герой один, герой + 1 боец, герой + 3-4 бойца; с/без семизубой пластины; со старым/новым ремонтом; с помощью женщине и без; с успехом и провалом FirstContact. Проверить, что UI проверки показывает Характер/13/модификаторы и что повторный диалог не предлагает бросок снова.

Только после этого отмечать P10-T01…T04 выполненными в `DevelopmentPlanSeedData.cs`/`KingdomSurvivalDevelopmentPlan.asset` и переходить к P11.

## 12. Location Interaction — замена старого decision-модала прибытия (реализовано, не проверено)

Presentation + wiring: старое окно прибытия («АРМИЯ ПРИБЫЛА»/«ОТРЯД ПРИБЫЛ» → `LocationArrivalDecisionFactory` → `expedition.PendingDecision` → decision-модал «Исследовать/Отменить») заменено на общее системное окно **Location Interaction**, встроенное в тот же VisualElement-оверлей, что Narrative Dialogue (не третий тип fullscreen UI). Формулы исследования, Dialogue Database, P10 `FirstContact`/`PartySize`, Camp/Hero/Journal, маршруты и бой не тронуты.

- Новый файл `Assets/_Project/UI/PrototypeUIController.LocationInteraction.cs`: `TryOpenLocationInteraction(locationId)` — единая точка входа и для автоматического открытия при прибытии, и для ручного входа кнопкой; переиспользует `narrativeDialogueOverlay`/`speaker`/`role`/`portrait`/`history`/`choices`, но не трогает `NarrativeDialogueRuntimeSession`. `ИССЛЕДОВАТЬ` вызывает уже существующий `GameState.TryStartLocationResearch()`; `ОТМЕНИТЬ` только закрывает окно, не создавая Activity.
- `PrototypeUIController.ModalQueue.cs`: `QueueNotice` для «АРМИЯ ПРИБЫЛА»/«ОТРЯД ПРИБЫЛ» теперь вызывает `TryOpenLocationInteraction` вместо `LocationArrivalDecisionFactory.TryCreate` (файл фабрики не изменён и не удалён — оставлен для похожей, но другой механики находки локации по дороге, которую эта правка не трогает). `HasBlockingModalWorkExceptCamp()` включает `IsLocationInteractionActive`. Технический попап «ИССЛЕДОВАНИЕ ЗАВЕРШЕНО» подавляется, если `Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId` готов открыть N12.
- `Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState)` — новый узкий story-gate (та же роль, что `GetPendingRoadEventDialogueId`): возвращает D12, когда `OldWaterSearch` физически достигнут, исследован (`IsExplored`) и `OldFordFound` ещё не выставлен. Опрашивается в `PrototypeUIController.ContinuousTime.cs` (`RefreshAutoTimeState`) тем же кадровым циклом, что дорожные встречи.
- `LocationData.InteractionDescription` (новое поле) + `OldWaterSearch.ExplorationHours = 3.0` (рабочее значение, не канон) заданы в `Chapter01OutcomeApplier.RevealDepartureSearchLocation`.
- Карточка локации на карте: кнопка `world-map-location-inspection-research-button` переиспользована как «ВОЙТИ В ЛОКАЦИЮ» (`WorldMapLocationActions.cs`, `OnWorldMapLocationCardEnterClicked` → `TryOpenLocationInteraction`); прямой запуск исследования из карточки убран.
- Тесты: `Chapter01P10Tests.cs` дополнен блоком про `OldWaterSearch`/`GetPendingLocationNarrativeDialogueId` (Core-уровень, без UI). **UI-обвязка (Location Interaction overlay, ModalQueue, кнопка «ВОЙТИ В ЛОКАЦИЮ») не имеет и не может иметь EditMode-покрытия в этом проекте** — `PrototypeUIController` требует живого `UIDocument`, и в кодовой базе нет прецедента инстанцирования контроллера в EditMode-тестах; проверяется только вручную в Play Mode (см. §13).
- Компиляция/тесты в этой сессии **не запускались** (нет подключённого Unity Editor через Pipeline на момент правки).

## 13. Что проверить после Pull (Location Interaction)

1. Чистая компиляция, Console без ошибок (новый файл `PrototypeUIController.LocationInteraction.cs`, изменённые сигнатуры в `ModalQueue.cs`/`WorldMapLocationActions.cs`/`ContinuousTime.cs`).
2. `Chapter01P10Tests.cs` — новые тесты про `OldWaterSearch`/`GetPendingLocationNarrativeDialogueId` зелёные, полный `Run All` без регрессий (особенно `ContinuousTimePolishTests.cs`, напрямую тестирующий саму `LocationArrivalDecisionFactory`, и `WorldMapLocationCardLayoutTests.cs`/`WorldMapLocationCardStructureRegressionTests.cs`).
3. Play Mode: приехать в обычную локацию (не `OldWaterSearch`) — должно открыться Location Interaction поверх Camp/Hero/Journal, время стоит; `ОТМЕНИТЬ` → окно закрывается, отряд остаётся `AtLocation`, время не идёт, автоматом окно не возвращается; ПКМ по локации → «ВОЙТИ В ЛОКАЦИЮ» → то же окно снова.
4. Play Mode: `ИССЛЕДОВАТЬ` в Location Interaction → окно закрывается, время идёт, исследование доходит до конца обычным способом (награда/лог), повторно начать нельзя.
5. Play Mode: путь `OldWaterSearch` целиком — прибытие → Location Interaction → `ИССЛЕДОВАТЬ` (3 ч) → без промежуточного «ИССЛЕДОВАНИЕ ЗАВЕРШЕНО» сразу открывается N12 → после N12 `OldFordFound` стоит и повторно D12 не предлагается.

Только после этого считать Location Interaction завершённым.
