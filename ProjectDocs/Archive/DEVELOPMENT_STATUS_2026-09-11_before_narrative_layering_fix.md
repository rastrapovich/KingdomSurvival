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

Полный журнал состояния непосредственно перед исправлением обновления кнопки Лагеря сохранён без изменений в `ProjectDocs/Archive/DEVELOPMENT_STATUS_2026-09-11_before_p09_camp_refresh_fix.md`.

Более ранние снимки журнала остаются в `ProjectDocs/Archive`, включая состояния до внедрения индивидуальной кадрировки портретов и до Condition Showcase. Текущий файл снова сокращён до реально актуального рабочего состояния; подробная история P00–P08, Dialogue Database/Graph/Inspector, портретных проходов и предыдущих технических решений не потеряна и находится в архивных копиях.

## 1. Текущий этап

Проект находится на этапе **«Нарративный фундамент / production-проход первой главы»**.

Главный производственный приоритет:

`герой → нормальная жизнь Дома → паводок/ремонт → причинные симптомы → расследование → дальняя дорога → нижние люди и старое соглашение → возвращение → последствия → главная тайна → антагонистическая сила → первый регион → первый большой квест`.

Сохраняется технический фундамент карты, непрерывного почасового времени, экспедиций, Hero Screen, Unit Database, BattleSandbox, Dialogue Database, нарративных проверок, Journal и личного экрана «Этапы разработки».

Новые крупные универсальные системы до доказательства первого региона не являются приоритетом.

## 2. Подтверждённый baseline и ограничения проверки

Последний подтверждённый пользователем большой прогон до текущих P09/UI-изменений: Unity компилировался и основной EditMode-набор запускался вручную. После портретных изменений ранее был зафиксирован прогон **330 тестов: 328 passed / 2 failed**; оба падения относились к ошибочной ссылке N01, которая затем была исправлена. Более новые изменения требуют нового полного `Run All` и не считаются подтверждённым зелёным baseline, пока пользователь не выполнит его в Unity.

В подключённой среде Unity Editor, компилятор и Test Runner недоступны. Поэтому все изменения после удалённого code review должны отдельно подтверждаться в живом Unity-проекте.

## 3. Актуальный UI / портретный фундамент

Диалоговый UI использует общую систему UILayout и индивидуальную кадрировку говорящих. База существ также переведена на общий контракт портретов `5:7` с Cover/Contain, Scale, нормализованным Offset и Flip X; полевая миниатюра BattleSandbox сохраняет отдельную настройку. Подробности всех портретных проходов сохранены в архивном журнале из §0.

Канон v1.30 добавляет шестой стандартный portrait preset `ML 250×350` между `M 200×280` и `L 300×420`. Диалоговый портрет больше не закреплён за `L`: любой выбранный канонический preset `XS/S/M/ML/L/XL` допустим, если фактическая рамка соответствует таблице `PortraitSizeTable`.

## 4. P09 — первая дальняя дорога и Лагерь

Реализованный production-каркас P09:

- физическое движение по дальней дороге и непрерывное время;
- обязательная N11 «Дорога, которой нет» примерно на 30% маршрута;
- пассивная RoadReading-проверка и реальное разветвление маршрута;
- гарантированная встреча D11B «Трое под телегой» примерно на 60% маршрута;
- четыре взаимоисключающих исхода D11B, цена помощи во времени и `CampUnlocked` после любого исхода;
- `Camp Screen v1` как отдельный fullscreen UI-слой без CampManager;
- `Chapter01CampSceneProvider` и первая лагерная сцена D11C «После телеги» как отложенное эхо решения у телеги;
- открытие Лагеря должно останавливать время, не сбрасывая маршрут, а «ПРОДОЛЖИТЬ ПУТЬ» — возвращать прежнее состояние движения/остановки.

Текущая ручная проверка пользователя: P09-T01, P09-T02 и P09-T03 считаются пройденными. P09-T04/P09-T05/P09-T06 остаются **«Нужна проверка в Unity»** до повторной проверки после исправления ниже.

## 5. P09 hotfix — кнопка Лагеря оставалась disabled после снятия блокировки

### 5.1. Наблюдавшийся симптом

После разблокировки Лагеря кнопка `Лагерь` появлялась в нижней навигации, но оставалась неактивной. D11C «После телеги» при этом можно было запустить вручную из DEBUG-меню, то есть сама база диалогов и runtime-вход в сцену существовали.

### 5.2. Причина по коду

`RefreshCampNavButtonState()` правильно вычисляет доступность Лагеря из живого состояния: нужен `CampUnlocked`, активная экспедиция и отсутствие другого блокирующего диалога/обязательной модалки.

Проблема была в синхронизации UI: после добавления Camp Screen его refresh был подключён в старый общий `RefreshInterface()`, но не был подключён в два более новых постоянных пути обновления интерфейса:

- `RefreshStableUiAfterStateChange()` — основной refresh после действий/закрытия окон Stable UI;
- `RefreshContinuousTimeUi(true)` — периодический presentation-refresh непрерывного времени и состояния экспедиции.

Из-за этого кнопка могла получить `disabled` в момент, когда диалог или обязательное окно ещё блокировали интерфейс, а после исчезновения блокировки продолжать визуально хранить старое состояние до случайного будущего вызова `RefreshInterface()`.

### 5.3. Исправление

В `PrototypeUIController.StableUI.cs` Camp теперь обновляется вместе с Journal и остальным persistent UI после каждого state-changing refresh:

- вызывается `RefreshCampNavButtonState()`;
- если Camp уже открыт — вызывается `RefreshCampScreen()`.

В `PrototypeUIController.ContinuousTimePresentation.cs` та же пара вызовов добавлена в `RefreshContinuousTimeUi(true)`. Это является страховочным live-refresh: после исчезновения временного блокиратора доступность кнопки пересчитывается по реальному состоянию максимум на следующем штатном presentation-проходе, а не ждёт несвязанного клика.

Само правило доступности **не ослаблялось**: Лагерь по-прежнему нельзя открыть до `CampUnlocked`, без активной экспедиции или поверх другого обязательного события. Никакого открытия Лагеря дома и никаких новых систем лагеря не добавлено.

### 5.4. Regression-проверка

Добавлен `Assets/_Project/Tests/EditMode/CampUiRefreshTests.cs` с двумя структурными EditMode-проверками: они фиксируют, что `RefreshCampNavButtonState()` остаётся подключён и к `RefreshStableUiAfterStateChange()`, и к `RefreshContinuousTimeUi(bool)`.

Это узкие regression-тесты именно на обнаруженную интеграционную ошибку; они не заменяют Play Mode проверку реального `Button.SetEnabled` в UI Toolkit.

## 6. Изменённые файлы текущего hotfix

- `Assets/_Project/UI/PrototypeUIController.StableUI.cs`;
- `Assets/_Project/UI/PrototypeUIController.ContinuousTimePresentation.cs`;
- `Assets/_Project/Tests/EditMode/CampUiRefreshTests.cs` + `.meta`;
- `ProjectDocs/DEVELOPMENT_STATUS.md`;
- архивная копия предыдущего журнала `ProjectDocs/Archive/DEVELOPMENT_STATUS_2026-09-11_before_p09_camp_refresh_fix.md` указывает на точное состояние файла до этого hotfix.

Нарративные данные D11B/D11C, `Chapter01StoryDirector`, `GameState`, правила экспедиции, карта, боевые системы, `LORE.md` и `NARRATIVE.md` не изменялись.

## 7. Что проверить после Pull

1. дождаться чистой Unity-компиляции и убедиться, что Console без C# errors;
2. запустить полный EditMode `Run All`, включая новые `CampUiRefreshTests`;
3. пройти P09 обычным игровым путём до D11B «Трое под телегой» и завершить любой исход;
4. убедиться, что кнопка `Лагерь` появляется и после закрытия обязательного диалога становится активной, пока экспедиция всё ещё существует;
5. нажать `Лагерь` — должен открыться Camp Screen, время/маркер должны остановиться, маршрут не должен сброситься;
6. при первом входе после D11B должна автоматически открыться D11C «После телеги» поверх Camp Screen;
7. завершить D11C, закрыть Лагерь через «ПРОДОЛЖИТЬ ПУТЬ» и убедиться, что отряд продолжает тот же маршрут с той же позиции;
8. отдельно проверить отрицательное условие: дома или без активной экспедиции кнопка после разблокировки остаётся видимой, но disabled;
9. после успешного ручного прохода P09-T04/P09-T05 можно отметить выполненными; P09-T06 — после зелёного полного `Run All`.

## 8. Следующий шаг

Сначала подтвердить hotfix в Unity по §7. Если кнопка после этого всё ещё disabled, следующий диагностический шаг — посмотреть live-состояние `HasActiveExpedition` и конкретный источник `HasBlockingModalWorkExceptCamp`; ослаблять правило Лагеря без такого подтверждения нельзя.

После зелёной проверки продолжить production-проход первой главы с P10, не расширяя Camp Screen в survival-систему раньше сюжетной необходимости.

## 9. Camp Screen переехал на UXML + UI Конструктор — 11.09.2026

По production-инструкции пользователя: Camp Screen v1 (§4) держал всю визуальную структуру в C# (`PrototypeUIController.Camp.cs` вручную создавал overlay/колонки/панели/кнопки rect-ами и цветом) — визуально править его через Kingdom Survival → UI Конструктор было невозможно. Переработано так же, как уже устроен `expeditions-screen` (единственный до этого прецедент "экран целиком в UXML, C# только ищет по имени").

### 9.1. Что изменилось архитектурно

- **`Prototype_Main.uxml`** — добавлен постоянный `camp-screen` (сиблинг `incident-modal-overlay`/`game-over-overlay`, скрыт по умолчанию через `.camp-screen { display: none; }`), с именованными узлами по production-таблице: `camp-header`/`camp-title`/`camp-time-label`/`camp-location-label`/`camp-close-button`, `camp-body` → `camp-party-panel` (`camp-commander-slot` + `camp-fighter-slot-1..4`, каждый с именованными name/status-лейблами), `camp-art`, `camp-scenes-panel`/`camp-scene-list`, `camp-footer` → `camp-status-panel`/`camp-status-text`, `camp-actions-panel`/`camp-actions-placeholder` (статичная заглушка "Пока нет доступных действий."), `camp-continue-button`.
- **`Prototype_Camp.uss`** (новый файл, подключён в `Prototype_Main.uxml`) — вся геометрия/цвет/отступы палитры Hero Screen (rgb-эквиваленты `HeroScreenBackdrop/Panel/PanelDeep/Border/Gold/Text/Muted`), а не инлайн-стили в C#.
- **`KingdomSurvivalUILayouts.asset`** — новый экран `id: camp` (`autoApply: 1`, `rootName: camp-screen`, `requiredElements` — все 9 узлов из раздела 25 инструкции, `elements` — все 32 узла экрана с `targetName`, совпадающим с именами в UXML). Override-флаги везде выключены по умолчанию (как и во всей остальной базе — `camp-art` уже `Kind: Image`, дизайнеру достаточно включить `overrideBackground` и назначить Sprite, без единой правки C#).
- **`PrototypeUIController.Camp.cs`** — переписан на query-only: `InitializeCampUi()` только ищет узлы через `campScreen.Q<T>(name)`, `RefreshCampScreen()` подставляет только текст/видимость (день/время, место, состав отряда по слотам, статус похода, список доступных сцен). Ни одного `.style.width/height/left/top/backgroundColor` для основной компоновки не осталось.
- **Nav-бар** — `.shell-navigation-bar` расширена с 396px до 500px под пять кнопок, добавлен `.shell-nav-button.nav-camp`, добавлен общий `.shell-nav-button:disabled { opacity: 0.4; }` (раздел 2: цвет не должен быть единственным признаком disabled). Кнопка «Лагерь» теперь также получает `nav-button-active` через уже существующий `SetNavigationButtonActive` (тот же класс, каким подсвечиваются Столица/Экспедиции), пока открыта.
- Более конкретные tooltip на кнопке «Лагерь» вместо одной общей фразы (раздел 18): "Сначала завершите разговор." / "Сначала примите обязательное решение." / "Лагерь доступен только во время похода." / "Остановиться лагерем." / "Лагерь открыт."

### 9.2. Что не изменилось (сознательно)

`CanOpenCampScreen`/`HasBlockingModalWorkExceptCamp`/пауза времени/`Chapter01CampSceneProvider`/порядок "сначала экран, потом сцена" (раздел 14) — вся логика раздела §5 (hotfix кнопки) осталась как есть; RoadStop-остановка на дороге по-прежнему не блокирует Лагерь (раздел 19 — это уже было верно, `HasBlockingModalWorkExceptCamp` никогда не учитывал `ActiveActivity`). Никакого CampManager, никакой survival-механики (сон/готовка/дозор/крафт) не добавлено — `camp-actions-panel` остаётся статичной заглушкой.

### 9.3. Тесты

`Assets/_Project/Tests/EditMode/CampScreenLayoutTests.cs` (новый файл): экран `camp` зарегистрирован и `autoApply`; все `requiredElements` резолвятся с ожидаемым родителем; `camp-art` — `Kind.Image` с валидным `TargetName`; полная `CollectValidationIssues` базы по-прежнему пуста; **каждый `targetName` экрана camp реально встречается как `name="..."` в `Prototype_Main.uxml`** (текстовый скан файла — тот же приём, что уже использует `CampUiRefreshTests.cs` для проверки интеграции без Play Mode). `Chapter01P09Tests.AssertExactlyOneCartOutcome` усилен: теперь для ЛЮБОГО из четырёх исходов телеги отдельно проверяется `CampUnlocked == true` (раздел 29), а не только для одного из пяти существующих тестов-путей.

### 9.4. Что не проверено (честно, без Unity)

Unity Editor, компилятор, Test Runner и Play Mode недоступны — не запускались. Структурная корректность (уникальность/связность узлов UILayout, соответствие `targetName` ↔ `name` в UXML, баланс скобок C#) проверена Python-скриптами вручную. Не проверено ни разу вживую:

1. что `UILayoutScreenBinder.ApplyAutoScreens` реально накладывает geometry на `camp-screen` без ошибок при старте (override-флаги сейчас везде выключены, поэтому визуально ничего двигаться не должно — но это предположение, не наблюдение);
2. что открытие Kingdom Survival → UI Конструктор → «Лагерь» показывает все элементы и позволяет их двигать/масштабировать так же, как уже работает для «Экспедиции»;
3. что назначение тестового Sprite в `camp-art` (`overrideBackground` + Sprite) реально показывает картинку в Play Mode;
4. фактический внешний вид nav-бара на 500px и видимость всех пяти кнопок без схлопывания;
5. визуальные состояния кнопки «Лагерь» (скрыта/disabled/enabled/активна) вживую.

После Pull обязательно: чистая компиляция, полный EditMode `Run All` (включая `CampScreenLayoutTests`), затем ручной проход по пп. 1–5 выше.

## 10. UI-M07 — Incident + Game Over: закрыт проверочный проход по ProjectDocs/UI_ARCHITECTURE.md

По плану миграции runtime-экранов (`ProjectDocs/UI_ARCHITECTURE.md` §9, порядок UI-M01…UI-M09) Incident и Game Over были ближе всего к целевой модели ещё до этого прохода: структура уже в `Prototype_Main.uxml`, `PrototypeUIController.cs` только биндит `Q<T>(name)`, ни одного `Build*`/`Ensure*` метода для этих экранов не было (проверено вручную). Поэтому это не миграция, а проверка + закрытие пробела в покрытии тестами:

- в `KingdomSurvivalUILayouts.asset` у экранов `incident-modal` и `game-over` `requiredElements` были пустыми (`[]`) — заполнены явной схемой (для `incident-modal`: `overlay`/`incident-modal-title`/`incident-modal-description`/`incident-modal-consequence`/`incident-understood-button`; для `game-over`: `overlay`/`game-over-days-label`/`restart-game-button`), все с корректным `expectedParentId`, аналогично `narrative-dialogue`/`camp`;
- добавлен `Assets/_Project/Tests/EditMode/IncidentAndGameOverScreenLayoutTests.cs` (по образцу `CampScreenLayoutTests.cs`): регистрация+`autoApply`, `requiredElements` резолвятся с ожидаемым родителем, полная `CollectValidationIssues` пуста, каждый `targetName` встречается в `Prototype_Main.uxml`.
- Изменений в `PrototypeUIController.cs` и `Prototype_Main.uxml` не потребовалось — оба экрана уже соответствовали модели.

Компиляция и Test Runner в этой сессии недоступны — новый тест не запускался; проверить вместе со следующим полным `Run All`.

Следующий шаг по плану — **UI-M02 Journal**: первый экран, где заводятся `BindRequiredElement<T>` и первый UXML-template.

## 11. UI-M02 — Journal переехал на UXML + UI Конструктор

По тому же плану (`ProjectDocs/UI_ARCHITECTURE.md` §9) — второй экран после Camp, и первый, где вводится инфраструктура, которой у Camp нет: явный helper обязательной привязки и настоящий UXML-template для повторяемого контента.

### 11.1. Что изменилось архитектурно

- **`PrototypeUIController.UIBinding.cs`** (новый файл) — `BindRequiredElement<T>(root, screenName, elementName)`: явный `Debug.LogError` вместо `NullReferenceException`, если элемента нет в UXML (§5 доктрины). Journal — первый потребитель.
- **`Prototype_SharedPanels.uss`** (новый файл) — общие классы `.ks-panel-backdrop/.ks-panel/.ks-panel-title/.ks-panel-deep/.ks-border/.ks-gold-text/.ks-text/.ks-muted-text/.ks-slot-empty/.ks-button`, 1:1 копирующие цвета и поведение приватных констант/хелперов `PrototypeUIController.HeroScreen.cs` (`HeroScreenGold` и т. д., `CreateHeroScreenPanel`, `StyleHeroScreenButton`). Journal — первый экран на этих классах; раньше он напрямую дёргал приватные хелперы Hero Screen, теперь зависит только от USS. Hero Screen (UI-M03) при своей миграции перейдёт на эти же классы и удалит исходные C#-хелперы.
- **`Journal.uss`** (новый файл) — вся раскладка `journal-overlay`: паддинги/ширины/flex колонок и секций, шрифты detail-панели, стиль строки цели (`.journal-goal-row*`).
- **`Prototype_Main.uxml`** — добавлен постоянный `journal-overlay` (сиблинг `camp-screen`/`incident-modal-overlay`/`game-over-overlay`, скрыт по умолчанию): шапка, вкладки (Цели активна, Хроника — задизейблена прямо в UXML вместе с tooltip), две колонки — список целей (3 фиксированные секции main/optional/completed, каждая со своим пустым списком-контейнером) и панель деталей.
- **`Assets/_Project/UI/Templates/Resources/Templates/JournalGoalRow.uxml`** (новый шаблон, первый в проекте) — строка цели: заголовок, бейдж «НОВОЕ» (скрывается через `style.display`, не пересоздаётся), подзаголовок. Клонируется через `VisualTreeAsset.Instantiate()`, загружается `Resources.Load<VisualTreeAsset>("Templates/JournalGoalRow")` — путь без ручной привязки в Inspector, специально выбран для сред без Unity Editor (см. §11.4).
- **`KingdomSurvivalUILayouts.asset`** — новый экран `id: journal` (`autoApply: 1`, `rootName: journal-overlay`, 27 элементов, 14 `requiredElements` — ровно те узлы, которые `InitializeJournalUi()` реально биндит через `BindRequiredElement`, а не все узлы подряд). Override-флаги выключены по умолчанию, как и везде в базе.
- **`PrototypeUIController.Journal.cs`** — переписан на bind-only: `InitializeJournalUi()` только находит узлы и один раз подписывает обработчики; `RefreshJournal()`/`RefreshJournalDetailPanel()`/`RefreshJournalNotificationState()` только текст/видимость/классы; `CreateJournalGoalRow` клонирует шаблон вместо `new VisualElement`/`new Label`. `seenJournalRevisionIds` (сессионная пометка «НОВОЕ») не изменилась — это не данные Provider.

### 11.2. Что не изменилось (сознательно)

`Chapter01JournalProvider.Build(gameState)` не трогался — единственная точка входа данных та же. Правило «Journal/Hero Screen/Camp — взаимоисключающие fullscreen-слои» (`CloseHeroScreen()`/`CloseCampScreen()` при открытии) осталось как есть. Отметка «прочитано» по-прежнему только по клику на конкретную запись, не при открытии журнала.

### 11.3. Тесты

- `Assets/_Project/Tests/EditMode/JournalScreenLayoutTests.cs` (по образцу `CampScreenLayoutTests.cs`): регистрация+`autoApply`, `requiredElements` резолвятся с ожидаемым родителем, полная `CollectValidationIssues` пуста, каждый `targetName` встречается в `Prototype_Main.uxml`, шаблон `JournalGoalRow.uxml` содержит ожидаемые именованные узлы.
- `Assets/_Project/Tests/EditMode/JournalScreenStructureRegressionTests.cs` (ключевой regression-тест §11/§30 доктрины, приём как в `CampUiRefreshTests.cs`): в `PrototypeUIController.Journal.cs` нет ни одного из прежних `Build*`-методов, нет `new VisualElement(...)`/`new Label(...)`, есть хотя бы один `Instantiate()`, и — отдельная проверка — нет больше ссылок на приватные хелперы/цвета Hero Screen (страховка на будущее удаление этих хелперов в UI-M03).

### 11.4. Что не проверено (честно, без Unity)

Unity Editor, компилятор, Test Runner и Play Mode недоступны в этой сессии. Структурная корректность (YAML базы, XML UXML/шаблона, парность скобок C#, отсутствие текстовых коллизий имён) проверена вручную Python-скриптами. Не проверено ни разу вживую:

1. что `Resources.Load<VisualTreeAsset>("Templates/JournalGoalRow")` реально находит и клонирует шаблон в Play Mode (сам путь и структура папок `Assets/_Project/UI/Templates/Resources/Templates/` проверены только текстово);
2. что `UILayoutScreenBinder.ApplyAutoScreens` накладывается на `journal-overlay` без ошибок при старте;
3. открытие Kingdom Survival → UI Конструктор → «Журнал» показывает все элементы и позволяет их двигать;
4. визуально: бейдж «НОВОЕ», выделение выбранной строки, задизейбленная вкладка «Хроника» с tooltip, скролл списка целей и панели деталей.

После Pull обязательно: чистая компиляция, полный EditMode `Run All` (включая `JournalScreenLayoutTests`/`JournalScreenStructureRegressionTests`), затем ручной проход по пп. 1–4 выше.

Следующий шаг по плану — **UI-M03 Hero Screen**: самый большой оставшийся экран, зависит от `.ks-*` классов из этого прохода (см. `/root/.claude/plans/precious-wiggling-bumblebee.md`).

## 12. UI-M03 — Hero Screen переехал на UXML + UI Конструктор

Самый большой и рискованный этап плана: ~1740 строк `PrototypeUIController.HeroScreen.cs` строили весь экран программно (панели, сетки характеристик, 5 карточек состава, модалку карточки существа, два плавающих тултипа).

### 12.1. Что изменилось архитектурно

- **Переосмысление «динамическое vs статичное»**: при ближайшем чтении кода оказалось, что качества (6), боевые характеристики (7 + инициатива), способности (3), снаряжение (6), инвентарь (12), свита (3) и состав похода (командир + 4 бойца) — всюду **фиксированное количество**, не переменное. По §2 доктрины («слоты фиксированного количества» — статика) все они стали именованными статичными узлами UXML, а не шаблонами/циклами. Реально динамические (неизвестное число) остались только: теги существа, особенности героя, доступные для набора бойцы, строка компетенции — для них заведены шаблоны.
- **`Prototype_SharedPanels.uss`** (заведён в UI-M02) стал реальным общим домом стиля: приватные `HeroScreenGold/Text/Muted/Panel/PanelDeep/Border/SlotEmpty/Backdrop` и хелперы `CreateHeroScreenPanel/StyleHeroScreenButton/SetHeroScreenBorder/SetHeroScreenRadius` **удалены из C# без остатка** (Journal их не использовал уже с UI-M02 — сначала это подтверждено, потом удаление).
- **`HeroScreen.uss`** (новый файл) — вся вёрстка, специфичная для экрана героя: сетки характеристик, карточки состава, модалка, тултипы. HP на карточках/в модалке всегда «MaxHitPoints/MaxHitPoints» (системы урона ещё нет) — полоса здоровья поэтому статична в CSS (100%, зелёный цвет), C# только переключает её видимость.
- **Четыре новых шаблона**: `HeroChip.uxml` (теги/особенности/ролевой чип/фаза командира — везде один Label с динамическим текстом/цветом фона), `HeroStatRow.uxml` (строка компетенции), `HeroRosterAvailableChip.uxml` (доступный для набора боец), `HeroHint.uxml` (пустые состояния — переиспользован и в `PrototypeUIController.JourneySummary.cs`). Плюс `JourneySummaryEntry.uxml` — сводка похода (физически на этом экране) тоже переведена с `new VisualElement/new Label` на шаблон.
- **`Prototype_Main.uxml`** — статичный `hero-screen-overlay` (213 именованных узлов): шапка, три колонки, ростер-бар (5 фиксированных карточек с портретом/именем/ролью/HP/состоянием/полосой здоровья/ролевым чипом/подсказкой у каждой), панель свиты, `journey-summary-block` (перенесён как есть), модалка карточки существа с 7 статичными строками характеристик, два тултипа.
- **`UnitPortraitElement`** (рендер-компонент портрета, не имеет UXML-фабрики) создаётся один раз при инициализации в уже существующий статичный слот — тот же принцип, что уже защищает портретный конвейер Narrative (§8/§22 доктрины): рамка/позиция — UXML, конкретный Sprite — решает `ApplyHeroScreenPortrait` на каждый Refresh.
- **`KingdomSurvivalUILayouts.asset`** — запись `id: hero-screen` (ранее ~57 элементов, частично устаревшая) **полностью пересобрана**: 213 элементов и 120 `requiredElements`, сгенерированы напрямую из фактического дерева `Prototype_Main.uxml` (Python-скрипт парсит UXML и строит `parentId`-цепочки автоматически — исключает ручные ошибки в такой большой структуре). `requiredElements` — ровно то, что реально биндит `InitializeHeroScreenUi()`, а не все узлы подряд.
- **`PrototypeUIController.HeroScreen.cs`** переписан на bind-only: `InitializeHeroScreenUi()` только находит элементы (через `BindRequiredElement<T>`) и один раз подписывает обработчики; `Refresh*`-методы меняют text/display/классы фиксированных узлов; для динамических списков — `Add*`-методы, клонирующие шаблон.

### 12.2. Что не изменилось (сознательно)

Вся игровая логика: picker состава похода (`gameState.Fighters`/`ActiveExpedition.FighterIds`/`selectedFighterIds`), подтверждение состава и его последствия, разрешение юнита по роли (`ResolveHeroScreenUnit`), формула инициативы (Сноровка → модификатор), взаимоисключение Hero Screen/Journal/Camp. Ручное позиционирование тултипов (`ShowHeroScreenTooltip` с `WorldToLocal`/`Clamp`) и модалка карточки существа (открытие/закрытие, доступ по ПКМ) оставлены в C# как легитимное поведение, не структура (§2 доктрины) — UI Toolkit не показывает встроенный `tooltip` в билде.

### 12.3. Тесты

- `HeroScreenLayoutTests.cs` — регистрация/`autoApply`, `requiredElements` резолвятся с ожидаемым родителем, полная `CollectValidationIssues` пуста, каждый `targetName` встречается в UXML, отдельные проверки на все 5 карточек состава и на 6+7 слотов качеств/характеристик.
- `HeroScreenStructureRegressionTests.cs` — отсутствие всего семейства `BuildHeroScreen*`, отсутствие возврата приватных хелперов/цветов, отсутствие `new VisualElement/Label/Button`, отсутствие `RefreshHeroScreenAbilities/Slots/Retinue` (эти три набора теперь не пересобираются каждый Refresh).
- `JournalScreenStructureRegressionTests.cs` (из UI-M02) успешно служит страховкой: он уже фиксировал, что Journal не ссылается на хелперы Hero Screen — то есть их удаление здесь не могло сломать Journal незаметно.

### 12.4. Что не проверено (честно, без Unity)

Unity Editor, компилятор, Test Runner и Play Mode недоступны в этой сессии. Сделано вручную и Python-скриптами: полная сверка «что биндит контроллер» ↔ «что есть в UXML» (100% совпадение после одного найденного и исправленного расхождения — `hp` vs `health` в узлах модалки), валидация всей базы `UILayoutDatabaseAsset` (уникальность id, разрешимость `parentId`, отсутствие циклов, соответствие `requiredElements`, положительные width/height/fontSize, override-флаги везде выключены), баланс скобок C#. Не проверено ни разу вживую:

1. компиляция C# (стилевые хелперы/поля/шаблонные `Instantiate()` вызовы — синтаксически выверены вручную, но не собраны компилятором);
2. что `Resources.Load<VisualTreeAsset>` реально находит и клонирует все четыре новых шаблона в Play Mode;
3. фактическая раскладка экрана (213 узлов, override-флаги выключены — экран должен выглядеть так же, как раньше, если UXML/USS воспроизводят вёрстку верно, но это не подтверждено визуально);
4. работоспособность обеих подсказок (характеристики/теги), модалки карточки существа (ПКМ, dimmer-клик, Escape), выбора/снятия бойцов в составе, кнопки «ПОДТВЕРДИТЬ СОСТАВ»;
5. что открытие Kingdom Survival → UI Конструктор → «Экран героя» показывает все 213 элементов корректно и без ошибок разрешения.

После Pull обязательно: чистая компиляция, полный EditMode `Run All` (включая оба новых набора тестов), затем ручной проход по пп. 2–5 выше — это самый большой и наименее подтверждённый кусок работы из всех пройденных этапов, проверять в первую очередь.

## 13. UI-M04 — Narrative Dialogue переехал на UXML + UI Конструктор

Самый чувствительный этап плана: экран диалога — единственный, где раньше был третий, собственный способ применения `UILayoutDatabaseAsset` в обход `UILayoutScreenBinder` (`autoApply: 0`), и где рядом живёт защищённый портретный конвейер.

### 13.1. Что изменилось архитектурно

- **`EnsureNarrativeDialogueUi()` / `ApplyNarrativeLayout()` / `ReparentNarrativeElement()` / `ApplyLayoutElement()` удалены целиком.** Постоянная структура (`narrative-dialogue-overlay` → `panel`[`text`-ScrollView→`history`, `choices`] + `portrait`[+заглушка] + `speaker`/`role` + `narrative-check-tooltip`) — теперь статичный узел `Prototype_Main.uxml`, подключённый стиль — новый `Narrative.uss`. Родительство узлов (`speaker`/`role`/`portrait` — дети `overlay`, а не `panel`; `text`/`choices` — дети `panel`) прописано в UXML напрямую и совпадает с уже существовавшей схемой `requiredElements` в базе — раньше именно этим занимался `ReparentNarrativeElement()` в рантайме, теперь это просто порядок тегов.
- **Экран `narrative-dialogue` переведён на `autoApply: 1`**, `rootName: narrative-dialogue-overlay` — теперь проходит через общий `UILayoutScreenBinder.ApplyAutoScreens` наравне с Camp/Journal/Hero. Override-флаги (`overrideRect`/`overrideBackground`/`overrideText`) у всех элементов кроме `portrait` по-прежнему выключены (инвариант `Default_Database_Does_Not_Enable_Legacy_Override_Flags`) — позиции `panel`/`speaker`/`role`/`text`/`choices` держит статичный CSS в `Narrative.uss`, пересчитанный из тех же Rect, что раньше применял `ApplyLayoutElement()` в обход флагов. `portrait` — исключение по конструкции: `Kind.Portrait` всегда получает `Rect`/`Background` от байндера (`ShouldApplyRect`/`ShouldApplyBackground`), поэтому в CSS у него нет фиксированной позиции — её всегда ставит сам байндер.
- **Текстовые стили и dimming overlay по-прежнему считает `PrototypeUIController.NarrativePresentation.cs` (`ApplyNarrativeLayoutPresentation`)** на каждое обновление истории — этот файл не тронут вообще, он и раньше не зависел от того, кто построил дерево. CSS в `Narrative.uss` — только fallback-позиционирование до первого такого прохода и визуальный стиль, не управляемый УИ Конструктором (цвета/шрифты/hover), перенесённый 1:1 из старого `Resources/Prototype_Narrative.uss` (файл удалён — больше не загружается рантаймом).
- **Три новых шаблона** (`NarrativeChoiceButton.uxml`, `NarrativeChoiceSecondary.uxml`, `NarrativeHistoryPlayerLine.uxml`) заменили `new Button`/`new Label` для кнопок вариантов ответа, их вторичной строки (механика/подсказка недоступности) и реплики игрока в истории. **Единственное намеренное отклонение от идиома шаблонов, принятого в Journal/Hero Screen**: там в дерево добавляется сам `TemplateContainer` (`parent.Add(instance)`), здесь — извлечённый из него корневой элемент (`InstantiateFlatTemplate<T>`, с явным комментарием-обоснованием в коде). Причина: `ApplyNarrativeLayoutPresentation()` проверяет CSS-классы через `narrativeChoicesContainer`/`narrativeHistoryContainer` `.ElementAt(i).ClassListContains(...)` напрямую по **прямым** детям контейнера — обёртка `TemplateContainer` не несёт этих классов и сломала бы проверку (вторичные строки получили бы основной текстовый стиль вместо мелкого, реплика игрока не получала бы `ApplyTextStyle`).
- **Сборка `ResponseGroup` (`BuildNarrativeHistoryGroupElement`, заголовки проверки в `PrototypeUIController.NarrativeCheckPresentation.cs`) сознательно НЕ шаблонизирована и не тронута.** Три формы сегмента (`CheckResult`/`CheckedObservation`/обычная реплика с 0..N абзацами) — переменная по форме композиция без единого inline `style.width`/`style.backgroundColor` (только текст и CSS-классы), это explicitly разрешённая доктриной (§2) динамика, а не постоянная структура экрана; принудительная унификация в один шаблон (пробовался вариант с общим `check-slot`/`speaker`/`body`) только увеличивала бы риск на самом чувствительном этапе плана без выигрыша — решение отражено прямо в комментарии над классом в файле.
- **`narrative-check-tooltip`** (плавающий tooltip с разбором проверки при наведении на «УСПЕХ»/«ПРОВАЛ») стал постоянным узлом `Prototype_Main.uxml`, привязывается в `InitializeNarrativeDialogueUi()` через `BindRequiredElement`; `EnsureNarrativeCheckTooltip()` удалён — `ShowNarrativeCheckTooltip` теперь просто проверяет `narrativeCheckTooltip == null`. Его собственное содержимое (`BuildNarrativeCheckTooltipContent` и хелперы `AddTooltip*Row`) не входит в перенос — та же логика "переменная композиция без inline-стилей", что и у `ResponseGroup`.
- **`PrototypeUIController.cs`**: добавлен вызов `InitializeNarrativeDialogueUi()` в общую цепочку `OnEnable()` рядом с Hero/Journal/Camp; `TryOpenNarrativeDialogueById` теперь проверяет `narrativeUiBound` вместо вызова `EnsureNarrativeDialogueUi()`.
- **`KingdomSurvivalUILayoutDatabaseAsset`/`UILayoutEditorWindow.cs`**: убраны все следы специального статуса диалога — тултип «применяется собственным кодом» и исключение диалога из предупреждения «переопределения включены, но autoApply выключен» в Editor-окне УИ Конструктора; комментарии в `UILayoutDatabaseAsset.cs`/`UILayoutScreenBinder.cs` про «свой код применения» убраны как устаревшие. Заодно исправлен `kind` у `text`/`choices` с `Text`(1) на `Container`(4) — они не одиночные Label, а контейнеры (`ScrollView`/список кнопок); на рантайм не влияет (ни один код не читал их `kind` для принятия решений), только на корректность подписи типа в УИ Конструкторе.
- **Портретный конвейер (`ApplyNarrativeSpeakerPortrait`, `UILayoutRuntimeApplier.ApplyDynamicImage`, `OverridePortraitFraming`/`PortraitScale`/`PortraitOffsetNormalized`/`PortraitFlipX`) не тронут ни единой строкой** — защищён по §8 доктрины.

### 13.2. Что не изменилось (сознательно)

Вся игровая логика диалога: `NarrativeDialogueRuntimeSession`, разрешение активных/пассивных проверок, `NarrativeUiHistoryGrouping.BuildSegments`, переход между узлами диалога, завершение диалога и его последствия (`Chapter01StoryDirector.HandleDialogueCompleted`, открытие Экрана героя после D10), пауза времени на время диалога, debug-триггер диалогов в `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. `narrativeDivider` (в старом коде — постоянно `display: none`, ничего не показывал) убран как мёртвый код, а не перенесён.

### 13.3. Тесты

- `NarrativeDialogueScreenLayoutTests.cs` — регистрация/`autoApply`/`rootName`, `requiredElements` резолвятся с ожидаемым родителем, отдельная явная проверка родительства `speaker`/`role`/`portrait` (дети `overlay`, не `panel`) — единственный экран, где это стоило проверить отдельным тестом, полная `CollectValidationIssues` пуста, каждый `targetName` встречается в UXML, узел `narrative-check-tooltip` существует, три шаблона существуют с ожидаемым корневым узлом.
- `NarrativeDialogueScreenStructureRegressionTests.cs` — отсутствие всех пяти удалённых методов (с открывающей скобкой в строке поиска, чтобы не ловить легитимный `ApplyNarrativeLayoutPresentation(...)`), отсутствие загрузки старого `Resources/Prototype_Narrative.uss`, использование `Instantiate()`/трёх новых шаблонов для кнопок/строки истории игрока, `narrative-check-tooltip` не создаётся через `new VisualElement`.
- Обновлён существующий `UILayoutDatabaseTests.cs::Narrative_Screen_Is_Excluded_From_Generic_Auto_Apply` → переименован в `Narrative_Screen_Is_AutoApplied_By_Generic_Binder`, утверждение инвертировано (`AutoApply == true`, добавлена проверка `RootName`).

### 13.4. Что не проверено (честно, без Unity)

Unity Editor, компилятор, Test Runner и Play Mode недоступны в этой сессии. Сделано вручную и Python-скриптами: полная сверка «что биндит контроллер» ↔ «что есть в UXML» (100% совпадение), сверка каждого `targetName` базы с UXML, YAML-парс всей записи `narrative-dialogue` (родители/`kind`/`autoApply`/`rootName` — всё соответствует ожиданиям), XML well-formedness всех тронутых `.uxml`, grep-сверка форбидден-методов из regression-теста против фактического кода. Не проверено ни разу вживую:

1. компиляция C# (в частности — использование `narrativeCheckTooltip` как общего поля между `PrototypeUIController.Narrative.cs` (bind) и `PrototypeUIController.NarrativeCheckPresentation.cs` (owner/использование) — синтаксически корректно, но не собрано компилятором);
2. что три новых шаблона реально клонируются в Play Mode и `InstantiateFlatTemplate<T>` действительно достаёт нужный тип (`Button`/`Label`) из `TemplateContainer` без ошибок;
3. фактическая раскладка диалога (позиции `panel`/`speaker`/`role`/`text`/`choices`, пересчитанные в статичный CSS из Rect базы, — должны визуально совпасть со старым поведением, но это не подтверждено на экране);
4. портретный конвейер при **нескольких разных говорящих** с разной индивидуальной кадрировкой (`OverridePortraitFraming`/`Scale`/`Offset`/`FlipX`) — самая рискованная точка регресса всего этапа, явно указанная ещё в плане;
5. hover/focus tooltip разбора проверки (`narrative-check-tooltip`) — что он действительно позиционируется и не обрезается границами overlay после перехода на bind-only;
6. что `narrativeChoicesContainer`/`narrativeHistoryContainer` после `Clear()`+заполнения шаблонами по-прежнему дают `ApplyNarrativeLayoutPresentation()` ожидаемые прямые дети без обёртки `TemplateContainer` (логика перепроверена по коду `InstantiateFlatTemplate`, но не по факту в рантайме).

После Pull обязательно: чистая компиляция, полный EditMode `Run All` (включая оба новых набора тестов и обновлённый `UILayoutDatabaseTests.cs`), затем ручной проход диалога минимум с 2-3 разными говорящими (разная кадрировка портрета — пп. 4 выше) и наведение на «УСПЕХ»/«ПРОВАЛ» хотя бы один раз (п. 5) — это критичный экран, проверять внимательнее прочих.

Следующий шаг по плану — **UI-M04 Narrative Dialogue**: не зависит от Hero Screen, но по чувствительности (защищённый портретный конвейер) требует особой аккуратности.

## 14. Portrait preset ML и свободный размер диалогового портрета — 11.09.2026

### 14.1. Что изменено

- В `PortraitSize` добавлен новый канонический preset `ML = 250×350`, математически ровно между `M 200×280` и `L 300×420`, с тем же отношением сторон `5:7`.
- Сохранена Unity-совместимость сериализации: `XS=0`, `S=1`, `M=2`, `L=3`, `XL=4` не сдвигались; новый `ML` получил значение `5`.
- `PortraitSizeTable` теперь хранит логический порядок `XS → S → M → ML → L → XL` и ищет definition по значению enum, а не индексирует массив числовым значением enum.
- Добавлен `PortraitSizeTable.IsDefined`, чтобы regression-тест диалогового портрета проверял именно принадлежность к канонической таблице.
- Правило теста больше не фиксирует `L`: диалоговый портрет может использовать любой выбранный канонический preset `XS/S/M/ML/L/XL`.
- Текущий default `narrative-dialogue/portrait` мигрируется один раз из `M 200×280` в `ML 250×350` через версию схемы `UILayoutDatabaseAsset`; центр рамки и настройки изображения сохраняются. После миграции пользователь может выбрать любой другой preset — повторно выбор не переписывается.
- Большой `KingdomSurvivalUILayouts.asset` после недавней UI-M04 переработки намеренно не перезаписывался удалённо целиком: миграция применяется при загрузке базы; после открытия проекта и сохранения asset Unity сериализует новую версию схемы и выбранный `ML` в YAML.

### 14.2. Проверено

- Git-diff системной части проверен отдельно: изменяются только `PortraitSize.cs`, `UILayoutDatabaseAsset.cs` и `UILayoutDatabaseTests.cs` до добавления документации.
- Добавлены regression-проверки размеров `ML`, неизменности старых serialized enum-значений и логического порядка таблицы.
- Legacy-тест свободной рамки обновлён: его исходный Rect теперь корректно выбирает ближайший `ML`, а не `L`.
- Unity Editor, C# compiler и Test Runner в подключённой среде недоступны; фактическая компиляция и EditMode `Run All` не выполнялись.

### 14.3. Что проверить после Pull

1. дождаться чистой Unity-компиляции;
2. запустить полный EditMode `Run All`;
3. открыть `Kingdom Survival → UI Конструктор → Диалог → Портрет` и убедиться, что доступен `ML 250×350`;
4. убедиться, что текущий диалоговый portrait после загрузки имеет `ML 250×350` и сохраняет прежний центр;
5. переключить `ML → M → L → ML` и проверить, что меняется только размер рамки, а `Scale/Offset/Flip X` не сбрасываются;
6. запустить диалог с 2–3 разными говорящими и проверить совпадение Editor-preview/runtime кадрирования;
7. сохранить `KingdomSurvivalUILayouts.asset`, чтобы одноразовая миграция и `portraitPresetSchemaVersion` были записаны в YAML.
