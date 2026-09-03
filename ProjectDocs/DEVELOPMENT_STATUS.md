# Kingdom Survival — Development Status

Последнее обновление: 2026-09-03

> Технический журнал фактически реализованного состояния Unity-проекта.
> Актуальный канон основной игры: `KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_14.md`.

## 1. Текущий этап

Выполнен технический stabilization-pass основной непрерывной симуляции и тактического `BattleSandbox`. Новые игровые механики и канон не добавлялись: исправлены ошибки планирования AI, детерминизма seed, точности автопаузы, жизненного цикла runtime-state, подключения фона поля и состава Git-репозитория.

## 2. Тактический AI

- AI сначала проверяет прямую атаку;
- если прямой атаки нет, он ищет противника, которого можно атаковать после достижимого перемещения в этой же активации;
- `FindBestMoveToward` при наличии достижимой атаки возвращает именно позицию, с которой атака останется допустимой;
- дальний боец останавливается на минимально необходимой дистанции для выстрела и не обязан расходовать весь запас движения;
- ближний боец может совместно спланировать подход и удар;
- при нескольких достижимых целях приоритет: меньше HP → больше прогнозируемого урона → меньше стоимость перемещения → стабильный ID;
- формула урона, 1 ОД, правила движения и ответного удара не менялись.

## 3. Seed новой партии

`GameState.CreateNewGame(seed)` теперь вызывает `WorldMapNavigation.ConfigureTerrain(WorldSeed)` сразу после установки `WorldSeed` и до первого `FindPath`.

Это устраняет зависимость генерации маршрутов/рельефа новой партии от ранее настроенного глобального seed.

## 4. Непрерывное время и автопауза

- продвижение экспедиции теперь возвращает фактически использованное игровое время;
- при прибытии или обнаружении скрытой локации большой simulation-step обрезается в реальный момент события;
- часы, восстановление и `EventHour` больше не должны перескакивать вперёд за момент обязательной автопаузы;
- задержки маршрута учитываются отдельно от фактического движения;
- завершение исследования уже использовало точное `elapsedHours` и сохранено без изменения правил.

## 5. Жизненный цикл continuous simulation

Хранилище runtime-состояний заменено с `Dictionary<GameState, RuntimeState>` на `ConditionalWeakTable<GameState, RuntimeState>`.

- runtime-state больше не удерживает старую партию сильной ссылкой только из-за присутствия в глобальной таблице;
- `Reset(state)` удаляет прежнюю запись конкретной партии и добавляет новую;
- публичный интерфейс часов и скоростей не менялся.

## 6. Фон BattleSandbox

`BattlefieldDatabaseBootstrap` больше не изменяет `private static readonly` поля `HexBoardElement` через reflection.

- reflection и `FieldInfo.SetValue` удалены;
- IL2CPP больше не зависит от мутации readonly-полей;
- fallback, который вставлял фон внутрь board поверх отрисованной сетки, удалён;
- композиция теперь однозначная: `battlefield-background` добавляется первым, `battle-sandbox-board` — вторым;
- фон всегда находится под интерактивной сеткой, курсорами, HP и миниатюрами.

## 7. Очистка репозитория

Удалены подтверждённые сгенерированные/осиротевшие файлы:

- `Assets/_Project/Prefabs.meta` — соответствующей папки `Prefabs` нет;
- `Assets/_Project/Scripts/Prototype.meta` — соответствующей папки `Scripts/Prototype` нет;
- `KingdomSurvival.slnx`;
- весь сгенерированный каталог `/UIElementsSchema/` с UI Toolkit `.xsd`.

В `.gitignore` добавлены:

- `*.slnx`;
- `/UIElementsSchema/`.

Остальные Unity `.meta` не удалялись.

## 8. Текущее поле и визуал sandbox

Без изменений этого stabilization-pass:

- 58 активных гексов: `7 / 8 / 9 / 10 / 9 / 8 / 7`;
- контейнер `10 × 7`;
- `GridVerticalScale = 0.75`;
- viewport около 80% ширины сцены;
- активная маска центрирована по крайним клеткам;
- полевая миниатюра использует anchor на 15% высоты изображения выше нижнего края;
- `BattlefieldOffset` остаётся индивидуальной тонкой подстройкой;
- в базе существ расширенный preview полевой миниатюры с красной точкой центра гекса.

## 9. Regression-тесты

Добавлены 7 новых EditMode-тестов:

### BattleSandbox

1. дальний AI проходит только необходимое расстояние и атакует в той же активации;
2. ближний AI проходит и атакует в той же активации.

### Core

3. `CreateNewGame` устанавливает запрошенный terrain seed до расчёта маршрутов;
4. одинаковый seed новой игры не зависит от ранее настроенного terrain seed;
5. прибытие останавливает часы в точный момент события;
6. обнаружение скрытой локации останавливает часы в точный момент события;
7. runtime-state continuous simulation структурно использует weak-key storage.

До этапа в проекте было заявлено 82 EditMode-теста. После добавления 7 regression-тестов ожидаемое структурное количество — **89**.

**Важно:** в подключённом окружении отсутствуют Unity Editor и C#-компилятор/Test Runner. Поэтому эти 89 тестов здесь **не запускались** и не заявляются как успешно пройденные. Выполнена только статическая проверка кода и структуры файлов.

## 10. Изменённые файлы этапа

- `.gitignore`;
- `Assets/_Project/BattleSandbox/Runtime/SandboxBattle.cs`;
- `Assets/_Project/BattleSandbox/Tests/EditMode/EnemyPlanningRegressionTests.cs`;
- `Assets/_Project/BattleSandbox/Tests/EditMode/EnemyPlanningRegressionTests.cs.meta`;
- `Assets/_Project/BattlefieldDatabase/Runtime/BattlefieldDatabaseBootstrap.cs`;
- `Assets/_Project/Scripts/Core/ContinuousSimulationActivities.cs`;
- `Assets/_Project/Scripts/Core/ContinuousSimulationClock.cs`;
- `Assets/_Project/Scripts/Core/GameState.cs`;
- `Assets/_Project/Tests/EditMode/StabilityRegressionTests.cs`;
- `Assets/_Project/Tests/EditMode/StabilityRegressionTests.cs.meta`;
- `ProjectDocs/DEVELOPMENT_STATUS.md`.

Удалены:

- `Assets/_Project/Prefabs.meta`;
- `Assets/_Project/Scripts/Prototype.meta`;
- `KingdomSurvival.slnx`;
- `/UIElementsSchema/`.

## 11. Что проверено статически

- AI использует существующие `TryFindAttackPosition`, `PreviewReachableAttack`, `TryMove` и `TryAttack`, поэтому новые параллельные правила дальности/урона не введены;
- дальняя достижимая позиция по-прежнему требует оставить движение для последующей атаки;
- seed конфигурируется до первого маршрута, созданного `CreateNewGame`;
- continuous movement при `RequestAutoPause` возвращает использованное время как `route delay + реально пройденные клетки / CellsPerGameHour`;
- `ContinuousSimulationSystem.Advance` уже использует возвращаемое `advancedHours` для часов и восстановления;
- `ConditionalWeakTable` устраняет глобальное сильное удержание старых `GameState`;
- `BattlefieldDatabaseBootstrap` больше не содержит `System.Reflection`, `FieldInfo`, `SetValue` или overlay-fallback;
- фон добавляется до board в одном surface;
- `.gitignore` предотвращает повторное добавление `.slnx` и `UIElementsSchema`;
- два удалённых `.meta` действительно не имели соответствующих asset/folder в дереве `main`.

## 12. Что проверить после pull

1. Дождаться Unity-компиляции без ошибок.
2. Открыть `BattleSandbox.unity`: проверить дальнего и ближнего врага — оба должны уметь переместиться и атаковать за одну активацию, лучник не должен бессмысленно проходить до упора.
3. Проверить визуал фона поля: фон должен оставаться строго под сеткой и юнитами.
4. Создать несколько новых партий с одним и тем же seed после партий с другими seed и убедиться в воспроизводимости карты/маршрутов.
5. На глобальной карте проверить прибытие и обнаружение локации на ускоренной скорости: часы должны останавливаться в момент события без скачка вперёд.
6. Запустить EditMode Test Runner. Ожидаемое структурное количество после этапа: `89`.
7. Если Unity автоматически снова создаст `.slnx` или `UIElementsSchema`, убедиться, что Git их больше не предлагает к commit.
