# Kingdom Survival — UI Architecture

> Технический стандарт инженерии интерфейса. **Не канон игрового дизайна** — этот документ не входит в иерархию `LORE.md → NARRATIVE.md → BESTIARY.md` и не описывает мир игры. Он фиксирует, как устроен код и данные runtime-экранов, и подчиняется тем же правилам git/коммитов, что и остальной техдолг проекта.

Последнее обновление: 2026-09-11.

---

## 0. Проблема, которую решает этот документ

Сейчас в проекте одновременно существуют **три разных подхода** к построению одного и того же экрана:

1. Экран целиком описан в UXML и применяется через `UILayoutScreenBinder` (`capital-screen`, `expeditions-screen` — частично; `incident-modal`, `game-over` — почти полностью).
2. Экран уже описан в `UILayoutDatabaseAsset` (есть запись в `UI Конструкторе`), но его реальное дерево `VisualElement` всё равно строит C# (`Hero Screen`: `BuildHeroScreen()` при уже существующей записи `hero-screen-overlay` в базе).
3. Экран не описан нигде, кроме кода: `BuildJournalScreen()`, `BuildCampScreen()` (до миграции по этому документу), `EnsureNarrativeDialogueUi()` + `ApplyNarrativeLayout()`.

Из-за этого источник вёрстки раздваивается: визуально кажется, что экран управляется `UI Конструктором`, а на деле правки в конструкторе ни на что не влияют, потому что реальную структуру каждый раз пересобирает код. Именно так уже произошло с Camp.

Этот документ — единственное официальное правило, как это исправлять, и как проектировать новые runtime-экраны, чтобы то же самое не повторилось.

---

## 1. Главное правило

> **C# больше не является инструментом рисования интерфейса.**

Итоговая модель слоёв:

```text
UXML
↓ существует постоянная структура экрана

UXML Templates
↓ структура повторяемых карточек / строк / слотов

UI Конструктор
↓ композиция экрана: позиции, размеры, изображения, фон, текстовое оформление

USS
↓ общий стиль компонентов: кнопки, карточки, рамки, состояния hover/disabled/selected

Controller
↓ только данные, события и состояние UI

Provider / Game System
↓ решает, какие данные и действия доступны

Dialogue System
↓ проигрывает нарративные сцены
```

Формула для быстрой сверки:

> **Static Structure in UXML → Composition in UI Layout → Reusable appearance in USS/Templates → Runtime data and behaviour in Controller → gameplay truth in Systems/Providers.**

---

## 2. Что статично, а что динамично

Не нужно заранее класть в UXML каждый возможный элемент игры.

### Статические элементы — обязаны существовать в UXML

Экран; фон; главные панели; колонки; заголовки; кнопки навигации; контейнеры списков; область изображения; панели деталей; footer/header; слоты фиксированного количества; постоянные кнопки.

### Динамические элементы — могут создаваться во время игры

Строки журнала; карточки локаций/построек неизвестного заранее количества; записи хроники; варианты диалога; текстовые блоки диалога; уведомления; эффекты; теги; способности; предметы.

Но их нельзя собирать из сотни `new Label(...)` / `new VisualElement(...)` / `style.width = ...` / `style.backgroundColor = ...`. Для них — **UXML-шаблоны** (`Assets/_Project/UI/Templates/`), которые Controller клонирует и наполняет данными.

---

## 3. Правило для Controller

Разрешён хороший код:

```csharp
heroName.text = hero.Name;
campScreen.style.display = DisplayStyle.Flex;
button.SetEnabled(canUse);
journalList.Clear();
journalList.Add(CreateGoalRow(goal)); // CreateGoalRow клонирует UXML-template
```

Запрещён для постоянного интерфейса код вида:

```csharp
var panel = new VisualElement();
panel.style.width = 300;
panel.style.height = 450;
panel.style.backgroundColor = ...;
panel.style.marginLeft = ...;
```

---

## 4. Контракт runtime-экрана

Каждый крупный экран (`camp-screen`, `journal-overlay`, `hero-screen-overlay`, `narrative-dialogue-overlay`, `capital-screen`, `expeditions-screen`) обязан иметь:

```text
Screen Root
Initialize / Bind
Open
Close
Refresh
Provider/System
```

`Initialize` ничего не строит. Он только находит уже существующие в UXML элементы и регистрирует callbacks:

```csharp
root = interfaceRoot.Q<VisualElement>("...");
title = root.Q<Label>("...");
closeButton = root.Q<Button>("...");
```

### Правило открытия экранов

```text
OpenXxxScreen()
```

делает только: закрывает конфликтующие fullscreen-экраны; показывает root; вызывает `Refresh()`; выводит экран наверх (`BringToFront`); при необходимости ставит паузу времени. Экран не создаёт.

`FullscreenUiCoordinator`, объединяющий `CloseXxxScreen()` разных экранов, — осознанно отложенная идея. Не вводить его, пока не мигрированы хотя бы Camp/Journal/Hero: раньше он станет лишним расширением архитектуры без практической пользы.

---

## 5. Обязательная проверка UXML

Общий helper вместо `NullReferenceException`:

```csharp
BindRequiredElement<T>(root, screenName, elementName)
```

Если элемента нет — понятная ошибка в лог:

```text
Герой: обязательный UXML-элемент 'hero-screen-name' не найден.
```

а не необработанное исключение. Реализация — `PrototypeUIController.UIBinding.cs`. Каждый мигрированный экран использует этот helper при инициализации и обязан явно проверить, что все обязательные элементы найдены, прежде чем регистрировать callbacks.

Схема обязательных элементов экрана также существует на уровне данных: `UILayoutScreenDefinition.RequiredElements` в `UILayoutDatabaseAsset` — конструктор уже умеет подсвечивать отсутствующие элементы, это нужно использовать системно для каждого мигрированного экрана.

---

## 6. Где хранится каждый слой

### UI Конструктор (`UILayoutDatabaseAsset`, `Assets/_Project/UILayout`)

Отвечает за уникальную композицию: X/Y, Width/Height, арт, фон, opacity, текстовое оформление конкретного элемента конкретного экрана. Не должен становиться заменой UI Toolkit: в нём не место условиям, циклам, источникам данных, спискам, сюжетным проверкам, логике кнопок, State Machine, игровым формулам. Он остаётся визуальным layout-редактором.

### USS

Отвечает за общий компонентный стиль: padding, margin, border, hover/disabled/selected, типовые цвета, радиусы, анимации. Разделение по экранам: `Prototype_Shell.uss`, `HeroScreen.uss`/эквивалент, `Journal.uss`, `Prototype_Camp.uss`, `Narrative.uss`, `Expedition.uss`, `BuildingCards.uss`. Существующие файлы можно оставлять как есть, если они уже подходят по смыслу — важен принцип разделения, а не конкретные имена файлов.

### UXML Templates (`Assets/_Project/UI/Templates/`)

Стандартное место для повторяемых карточек/строк/слотов: `BuildingCard.uxml`, `ExpeditionLocationCard.uxml`, `HeroTrait.uxml`, `HeroStat.uxml`, `HeroFighterSlot.uxml`, `JournalGoalRow.uxml`, `JourneySummaryEntry.uxml`, `CampSceneRow.uxml`, `NarrativeChoice.uxml`, `NarrativeHistoryBlock.uxml` и т. п. Не обязательно создавать их все сразу — по мере миграции соответствующего экрана.

### Правило изображений

Любой крупный статический арт (`camp-art`, `capital-background`, `hero-background`, `journal-background`, `expedition-map-frame`, `incident-art`) поддерживает: Sprite, Texture, Cover, Contain, Scale, Offset, Tint, Opacity — это уже есть в `UI Конструкторе`. Если изображение зависит от состояния мира, C# может временно переопределять его, но fallback должен храниться в UI Layout.

---

## 7. Ownership данных

Для каждого экрана — явная цепочка, UI никогда не становится источником игровых данных:

```text
Hero:        GameState / UnitDatabase → Hero Controller → UXML
Journal:     Chapter01JournalProvider → Journal Controller → UXML
Camp:        Chapter01CampSceneProvider → Camp Controller → UXML
Dialogue:    DialogueDatabase → NarrativeDialogueRuntimeSession → Narrative Controller → UXML
Buildings:   BuildingSystem → Building Controller → BuildingCard template
Expeditions: GameState / ContinuousSimulation → Expedition Controller → Expedition UXML/templates
```

---

## 8. Что не трогать этим стандартом

Debug Panel, `UI Конструктор` как инструмент, база диалогов, база существ, экран «Этапы разработки» — это Unity Editor/dev tooling, а не игровые экраны, их не нужно насильно переводить на runtime-UXML архитектуру.

`BattleSandbox` — отдельная сцена и отдельная большая UI-система, в массовый рефакторинг **не включается**, пока не проведён отдельный аудит её UI. Правило для неё в будущем то же (статические панели → UXML; композиция → UI Layout; юниты/гексы/initiative entries → dynamic/template; бой → battle systems/controller), но менять её вместе с остальным UI сейчас — риск, который не оправдан.

Карта экспедиций не переводится в UXML целиком: маршрут, динамические точки, маркер армии, generated terrain, discovery, подсветки — это визуализация данных, а не статичная компоновка UI, и остаётся программной.

Портреты Narrative сохраняют текущую систему (`Sprite` из `Dialogue Database` + `Portrait preset` + `Cover/Contain` + `Scale/Offset/Flip`): UXML хранит рамку, `UI Конструктор` — её расположение и общие параметры, `Dialogue Database` — конкретный портрет персонажа и индивидуальную кадрировку. Не возвращаться к статическому `Sprite` в самом UXML для NPC.

---

## 9. Порядок миграции экранов

| Этап | Экран | Почему |
|---|---|---|
| **UI-M01** | Camp | маленький, первый практический проход — уже выполнено, модель подтверждена |
| **UI-M02** | Journal | автономный fullscreen, простая логика |
| **UI-M03** | Hero Screen | большой, но уже частично описан в `UI Layout` |
| **UI-M04** | Narrative Dialogue | критичный и чувствительный экран; убрать `EnsureNarrativeDialogueUi()`/`ApplyNarrativeLayout()`/`ReparentNarrativeElement()`/`ApplyLayoutElement()`, перевести на `autoApply = true` через `UILayoutScreenBinder.ApplyAutoScreens` |
| **UI-M05** | Buildings | перевести карточки Capital на `BuildingCard.uxml` template вместо `BuildBuildingCards()` |
| **UI-M06** | Expedition popup/cards | убрать layout `quick-expedition-popup` из C# |
| **UI-M07** | Incident + Game Over | в основном добавить/доработать записи `UI Конструктора` — структура уже в UXML |
| **UI-M08** | Main Shell | полный визуальный контроль оболочки (sidebar/workspace/bottom-bar/navigation-bar/resource-bar/commander panel) |
| **UI-M09** | BattleSandbox | отдельный аудит и решение |

Каждый этап — отдельный, самостоятельно проверяемый коммит/проход. Не делать несколько экранов одним коммитом.

---

## 10. Что считается production-ready экраном

```text
1. Создать static UXML.
2. Дать всем важным элементам стабильные names.
3. Создать UXML templates для повторяемого контента.
4. Создать/добавить экран в UI Конструктор.
5. Указать Required Elements.
6. Настроить layout/art.
7. Написать Controller, который только bind'ит элементы.
8. Подключить Provider/System.
9. Добавить callbacks.
10. Добавить Refresh().
11. Добавить structural/EditMode tests.
12. Проверить Play Mode.
```

Только после этого экран считается production-ready.

---

## 11. Проверка каждого мигрированного экрана

Четыре уровня, обязательные для каждого этапа из §9:

**Structure test** — наличие нужных UXML names.

**UILayout test** — screen существует в `UILayoutDatabaseAsset`; `rootName` совпадает; `requiredElements` существуют; `targetName` валиден. См. `UILayoutDatabaseTests.cs` как образец.

**Runtime data test** — Controller правильно подставляет данные из Provider/System.

**Manual Play Mode**: открыть → закрыть → изменить состояние → открыть снова → изменить `UI Constructor` → Play → убедиться, что layout изменился.

### Ключевой regression-тест

> **Production fullscreen screens must not build their static visual tree in C#.**

Структурная проверка: в файлах контроллеров мигрированных экранов не должно быть методов вроде `BuildHeroScreen`, `BuildJournalScreen`, `BuildCampScreen`, `EnsureNarrativeDialogueUi`, создающих root и постоянные панели через `new VisualElement`. Тест добавляется отдельным файлом на каждый мигрированный экран по мере его перехода (не пытаться сразу писать один тест на все четыре экрана — Journal/Hero/Narrative до своей миграции законно продолжают строить дерево в коде).

---

## 12. Статус на момент введения стандарта (2026-09-11)

| Экран | UXML | UI Конструктор | Структура создаётся C# | Состояние |
|---|---:|---:|---:|---|
| Main Shell | ✅ | частично | немного | почти правильно |
| Capital | ✅ | ✅ | карточки построек | частично |
| Expeditions | ✅ | ✅ | popup/cards/dynamic map | частично |
| Camp | ✅ | ✅ | — | мигрирован первым (UI-M01) |
| Hero | ✅ | ✅ | — | мигрирован (UI-M03) |
| Journal | ✅ | ✅ | — | мигрирован (UI-M02) |
| Narrative | ✅ | ✅ (`autoApply=1`) | сегменты истории (легитимно, §2) | мигрирован (UI-M04) |
| Incident | ✅ | ✅ | данные | почти правильно |
| Game Over | ✅ | ✅ | данные | почти правильно |

Эта таблица — технический снимок, не факт мира и не часть канона; обновляется по мере прохождения этапов §9.
