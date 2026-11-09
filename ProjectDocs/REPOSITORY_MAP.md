# Kingdom Survival — карта репозитория

Сюжетная основа 04.11.2026 — [актуальный канон v1.66](../KINGDOM_SURVIVAL_GAME_CONCEPT_CANON_RU_v1_66.md) §6, [NARRATIVE.md](NARRATIVE.md) §26.4 и [LORE.md](LORE.md) §17.2. Дом — [архивное направление под вопросом](Archive/HOME_DESIGN_ON_HOLD_2026-11-04.md); его старый лор и прежняя обязательная тема отменены.

Проверено по локальному checkout 02.11.2026. Это справочник путей, а не новый канон. Рабочие правила — [AGENTS.md](../AGENTS.md). Пути в таблицах ниже указаны от корня репозитория; Markdown-ссылки разрешаются из ProjectDocs. При изменении структуры обновляй карту.

## Карта кода и точек входа

Все пути ниже относительно корня; каталоги модулей находятся в `Assets/_Project/`, если не указано иное.

| Задача | Где начинать |
|---|---|
| Запуск основной игры | [Prototype_Main.unity](../Assets/_Project/Scenes/Prototype_Main.unity), [PrototypeUIController.cs](../Assets/_Project/UI/PrototypeUIController.cs) — MonoBehaviour с UIDocument и тематическими partial-файлами |
| Состояние и переходы между сценами | [GameState.cs](../Assets/_Project/Scripts/Core/GameState.cs), [CampaignSession.cs](../Assets/_Project/Scripts/Core/CampaignSession.cs), [CampaignContent.cs](../Assets/_Project/Scripts/Core/CampaignContent.cs) |
| Время, люди, вещи, торговля | [Scripts/Core/](../Assets/_Project/Scripts/Core/): `ContinuousSimulation*.cs`, `PeopleService.cs`, `Item*Service.cs`, `TradeService.cs`, `SettlementService.cs`, `TavernService.cs`, `NoticeBoardService.cs`, `ErrandService.cs`, `HireService.cs` |
| Свободная игра и конкретные истории | [FreePlay/Runtime/](../Assets/_Project/FreePlay/Runtime/): `FreePlayContent.cs`, `FreePlay*Story.cs`, `FreePlayMineLocal.cs`; авторские начальные данные — `FreePlay/Editor/` |
| Глобальная карта: правила | `Scripts/Core/WorldMap*.cs`: генератор, навигация, поиск пути, туман; результат — `GameState.GeneratedMap` |
| Глобальная карта: рисунок и настройки | [WorldMapVisual/](../Assets/_Project/WorldMapVisual/), `Runtime/WorldMapPainter*.cs`; UI — `UI/PrototypeUIController.WorldMap*.cs` |
| Локальное исследование | `Scripts/Core/LocalExploration*.cs`; [LocalHexMover.cs](../Assets/_Project/BattlefieldDatabase/Runtime/LocalHexMover.cs); `UI/PrototypeUIController.Local*.cs` |
| Бой | [BattleSandbox/](../Assets/_Project/BattleSandbox/) — `Runtime/SandboxBattle.cs`; сцена [BattleSandbox.unity](../Assets/_Project/Scenes/BattleSandbox.unity), связь с партией — `UI/PrototypeUIController.CampaignBattle.cs` |
| Локации и поля боя | [BattlefieldDatabase/](../Assets/_Project/BattlefieldDatabase/) — данные и База локаций; отдельное окно Базы полей боя не восстанавливать |
| Общий рендер мест, земля, свет, тени | [LocationRendering/](../Assets/_Project/LocationRendering/) — `Runtime/LocationWorldRenderer*.cs`, импорт земли — `Editor/GroundImporter.cs` |
| Диалоги и встречи | [DialogueDatabase/](../Assets/_Project/DialogueDatabase/), [Encounters/](../Assets/_Project/Encounters/); ядро — `Scripts/Core/Narrative*.cs`, подача — `UI/PrototypeUIController.Narrative*.cs`, `UI/NarrativeDialogueRendering.cs` |
| Предметы / поселения / развитие / существа | [ItemDatabase/](../Assets/_Project/ItemDatabase/), [SettlementDatabase/](../Assets/_Project/SettlementDatabase/), [ProgressionDatabase/](../Assets/_Project/ProgressionDatabase/), [UnitDatabase/](../Assets/_Project/UnitDatabase/) |
| Графические ассеты / анимации | [ArtAssetDatabase/](../Assets/_Project/ArtAssetDatabase/), [AnimationDatabase/](../Assets/_Project/AnimationDatabase/); локальные рисунки — `Art/` |
| UI | [UI/](../Assets/_Project/UI/): `PrototypeUIController.<тема>.cs`, `Prototype/Prototype_Main.uxml`, тематические USS; повторяемые элементы — `Templates/Resources/Templates/`; композиция — [UILayout/](../Assets/_Project/UILayout/) |
| Сохранения | `Scripts/Core/CampaignSaveData.cs`, `CampaignSaveService.cs`, `CampaignSaveStore.cs`; Unity-адаптер — [PrototypeUIController.CampaignSave.cs](../Assets/_Project/UI/PrototypeUIController.CampaignSave.cs), путь — `Application.persistentDataPath` |
| Планирование квестов и перенос | Локальная `QuestWorkshop/` (не в Git); Unity-мост — [QuestWorkshopBridge/](../Assets/_Project/QuestWorkshopBridge/) |
| Трекер разработки | [DevelopmentTracker/Editor/](../Assets/_Project/DevelopmentTracker/Editor/); фактический журнал — [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) |
| Рабочая HTML-карта до релиза | [KingdomSurvival__Roadmap_RU.html](KingdomSurvival__Roadmap_RU.html) — основная копия для дальнейшего ведения пунктов; дополняет технический план ПР, не заменяет канон. Статусы и заметки хранятся внутри HTML; изменения через браузер нужно экспортировать кнопкой «Скачать HTML» и перенести в этот файл, поскольку localStorage сам файл не обновляет. |
| Эксперимент MapLab | [Assets/Experiments/MapLab/README.md](../Assets/Experiments/MapLab/README.md): `Runtime/`, `Editor/`, `Shaders/`, `Tests/EditMode/`; сцена `Scenes/MapLab_Painterly_Test.unity`, выгрузки — `MapLabExports/` (не в Git) |
| Карта, окружение и стилизация 3D-анимационных рендеров | [MAP_STYLE_GUIDE.md](MAP_STYLE_GUIDE.md): тушь/акварель, формы 70/25/5; §1.1 — точное сохранение всех пропорций, поз, силуэтов и альфа-каналов; конвейер — [ART_PIPELINE_GUIDE.md](ART_PIPELINE_GUIDE.md) |
| Берестяной маскот, его мимика, позы и костюмы | [BIRCH_MASCOT_REFERENCE_RU.md](BIRCH_MASCOT_REFERENCE_RU.md) — выбранная база 00 и ссылка на иллюстрированный паспорт с шестью PNG; смотреть изображения перед продолжением |
| Концепты персонажей/существ, портреты и отдельные изображения | [ART_STYLE_GUIDE.md](ART_STYLE_GUIDE.md) и [иллюстрированная книга](KingdomSurvival_Art_Style_Canon_v1_1.html); старые растения в книге — исторические примеры |
| Blender: KS Ground Renderer | [Tools/Blender/ks_ground_renderer/README_RU.md](../Tools/Blender/ks_ground_renderer/README_RU.md); исходники рядом, проверки — `Tools/Blender/tests/` |

### Где лежат авторские данные

Базы устроены как `Runtime/`, `Editor/`, `Resources/<ИмяБазы>/`, иногда `Tests/`. Не создавай второй каталог с тем же смыслом. Точные пути ассетов находи через `git ls-files "Assets/_Project/*/Resources/*.asset"`.

Основные файлы: `KingdomSurvivalItems.asset`, `KingdomSurvivalSettlements.asset`, `KingdomSurvivalProgression.asset`, `KingdomSurvivalUnits.asset`, `KingdomSurvivalDialogues.asset`, `KingdomSurvivalEncounters.asset`, `KingdomSurvivalEncounterFlags.asset`, `KingdomSurvivalLocalLocations.asset`, `KingdomSurvivalBattlefields.asset`, `KingdomSurvivalArtAssets.asset`, `KingdomSurvivalAnimations.asset`, `KingdomSurvivalUILayouts.asset`. Они лежат в Resources одноимённых модулей из карты выше. Настройки карты — `WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalWorldMap*.asset`.

### Важные контракты

- UI: [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md). Статическая структура — UXML; композиция — UI Layout; повторяемый вид — USS/Templates; Controller — данные и события; игровые решения — Systems/Providers. Не создавай вторую верстку статического экрана из C#.
- Локальное перемещение — по гексам боя места; спутники следуют за героем. Общая скорость — `BattlefieldDatabaseAsset.WalkHexesPerSecond`. Бой в месте использует ту же локацию; дорожный бой — отдельную арену.
- MapLab имеет `UNITY_EDITOR` и `autoReferenced: false`, в Build Settings не включён. Не подключай его к партии, сохранениям или общему рендеру без прямой задачи.
- Проверяя сохранения, используй изоляцию тестов (`Tests/PlayMode/PlayModeSaveIsolation.cs`, `CampaignSaveStore.DirectoryOverride`), а не реальные слоты пользователя.

## Какие документы читать по задаче

| Тема | Источники |
|---|---|
| Проверка игрового опыта: механики, карта, способности, UI, эксперименты | [GAME_DESIGN_REVIEW_GUIDE.md](GAME_DESIGN_REVIEW_GUIDE.md), §1–2 и выбранные GD01–GD12; обязательная сверка Codex и Claude до значимой работы и при проверке результата |
| Механики, формат и ограничения игры | Текущий корневой канон; [план](PROTOTYPE_DEVELOPMENT_PLAN.md); свежие относящиеся записи [журнала](DEVELOPMENT_STATUS.md) |
| Факты мира / сюжет / существа | [LORE.md](LORE.md) / [NARRATIVE.md](NARRATIVE.md) / [BESTIARY.md](BESTIARY.md), [боевые паспорта](BESTIARY_COMBAT_PASSPORTS.md) |
| Квест, встреча, значимая правка диалога | Правила в конце AGENTS; [правила создания квестов](REACTOR_QUEST_DESIGN_GUIDE.md); [гайд мастерской](QUEST_WORKSHOP_GUIDE.md), локальный `QuestWorkshop/KingdomSurvival_Quest_Guide_RU.md`; подходящие `*_ADAPTATION.md` |
| Поселения, торговля, корчма, доска | [Спецификация](SETTLEMENTS_TRADE_TAVERN_SPEC.md), [руководство автора](SETTLEMENTS_AUTHOR_GUIDE.md) |
| Карта / исследование / база мест | [WORLD_MAP_MOVEMENT_GUIDE.md](WORLD_MAP_MOVEMENT_GUIDE.md), [LOCAL_EXPLORATION_GUIDE.md](LOCAL_EXPLORATION_GUIDE.md), [LOCATION_DATABASE_GUIDE.md](LOCATION_DATABASE_GUIDE.md) |
| UI и диалоговая база | [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md), [UI_LAYOUT_EDITOR_STATUS.md](UI_LAYOUT_EDITOR_STATUS.md), [DIALOGUE_DATABASE_STATUS.md](DIALOGUE_DATABASE_STATUS.md); статус сверять с кодом |
| Арт и его импорт | [ART_STYLE_GUIDE.md](ART_STYLE_GUIDE.md), [ART_PIPELINE_GUIDE.md](ART_PIPELINE_GUIDE.md), [ASSET_DATABASE_GUIDE.md](ASSET_DATABASE_GUIDE.md), [ANIMATION_DATABASE_GUIDE.md](ANIMATION_DATABASE_GUIDE.md) |
| Найденные ранее проблемы | [Аудит 31.10.2026](AUDIT_REPORT_2026-10-31.md) плюс более свежие записи журнала; старый отчёт не доказывает, что ошибка ещё существует |
