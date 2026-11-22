using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Раздел «Глобальная карта» окна «База анимаций»: фигуры, которые ходят по
    // основной карте (сначала герой). Тот же формат кадров, что у боя, но свои
    // база, атласы, предпросмотр и настройки: размер в клетках карты, шаг
    // ходьбы, опора, таблица ракурсов карты.
    public sealed class WorldMapAnimationSection : VisualElement
    {
        private const string LastFolderKey = "KS.AnimationDatabase.LastFolder";
        private const string SelectedSetKey = "KS.WorldMapAnimation.SetId";

        private static readonly Color ReadyColor = new Color(0.35f, 0.72f, 0.40f, 1f);
        private static readonly Color PartialColor = new Color(0.90f, 0.70f, 0.30f, 1f);
        private static readonly Color EmptyColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color ErrorColor = new Color(0.92f, 0.42f, 0.34f, 1f);
        private static readonly Color HeaderColor = new Color(0.80f, 0.66f, 0.34f, 1f);
        private static readonly Color DropHighlightColor = new Color(0.95f, 0.78f, 0.35f, 1f);
        private static readonly Color HintColor = new Color(0.62f, 0.62f, 0.62f, 1f);

        private const float TableCellWidth = 100f, TableCellHeight = 56f;

        private WorldMapAnimationDatabaseAsset database;
        private string selectedSetId;
        private CreatureAnimationAction selectedAction = CreatureAnimationAction.Walk;
        private CreatureAnimationDirection selectedDirection = CreatureAnimationDirection.Front;
        private readonly List<WorldMapAnimationSetData> visibleSets = new List<WorldMapAnimationSetData>();

        private ListView setList;
        private Label titleLabel;
        private Label messageLabel;
        private WorldMapAnimationPreviewElement preview;
        private VisualElement setPanel;
        private ScrollView detailPanel;
        private Slider pivotXSlider;
        private Slider pivotYSlider;
        private bool directionMapOpen;
        private bool undoGroupOpen;
        private int undoGroupIndex;

        public WorldMapAnimationSection()
        {
            style.flexGrow = 1f;
            database = WorldMapAnimationEditorData.LoadOrCreate();
            selectedSetId = SessionState.GetString(SelectedSetKey, string.Empty);

            BuildToolbar();
            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;
            body.Add(BuildListPane());
            body.Add(BuildMainPane());
            Add(body);

            RegisterCallback<DragUpdatedEvent>(OnSectionDragUpdated);
            RegisterCallback<DragPerformEvent>(OnSectionDragPerform);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += OnExternalChange;
                CreatureAnimationEditorData.DataChanged += OnExternalChange;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= OnExternalChange;
                CreatureAnimationEditorData.DataChanged -= OnExternalChange;
                CloseUndoGroup();
            });

            RefreshSetList();
            if (SelectedSet == null && visibleSets.Count > 0)
                selectedSetId = HeroOrFirst().Id;
            SelectSet(selectedSetId);
        }

        private WorldMapAnimationSetData SelectedSet => database != null ? database.FindSet(selectedSetId) : null;

        private WorldMapAnimationSetData HeroOrFirst()
        {
            return database.FindSet(database.HeroSetId) ?? visibleSets[0];
        }

        private void OnExternalChange()
        {
            if (database == null || setList == null)
                return;
            RefreshSetList();
            ShowSelection();
        }

        // ------------------------------------------------------------------
        // Построение
        // ------------------------------------------------------------------

        private void BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.style.height = 30f;
            AddToolbarButton(toolbar, "Загрузить папку", LoadFolder, "Папка героя (внутри Walk/<ракурс>/*.png) или целого пакета. Можно просто перетащить её в окно.");
            AddToolbarButton(toolbar, "Добавить кадры", () => LoadIntoSelectedCell(CreatureAnimationImportMode.Append), "Добавить кадры в конец выбранной ячейки: папка PNG.");
            AddToolbarButton(toolbar, "Заменить", () => LoadIntoSelectedCell(CreatureAnimationImportMode.Replace), "Заменить кадры выбранной ячейки: папка PNG.");
            AddToolbarButton(toolbar, "Удалить анимацию", DeleteSelectedAnimation, "Убирает назначение из базы карты. Картинки не удаляются.");
            AddToolbarButton(toolbar, "Сохранить", Save, "Сохранить базу анимаций карты.");
            AddToolbarButton(toolbar, "Проверить базу", ValidateDatabase, "Ошибки данных и неполнота наборов карты.");

            ToolbarMenu more = new ToolbarMenu { text = "Ещё" };
            more.menu.AppendAction("Очистить неиспользуемые атласы карты…", _ => CleanUnusedAtlases());
            more.menu.AppendAction("Показать папку атласов карты", _ =>
            {
                UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(WorldMapAnimationEditorData.ArtRoot);
                if (folder != null)
                    EditorGUIUtility.PingObject(folder);
            });
            toolbar.Add(more);

            messageLabel = new Label();
            messageLabel.style.flexGrow = 1f;
            messageLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            messageLabel.style.marginRight = 8f;
            messageLabel.style.overflow = Overflow.Hidden;
            toolbar.Add(messageLabel);
            Add(toolbar);
        }

        private static void AddToolbarButton(Toolbar toolbar, string text, Action action, string tooltip)
        {
            toolbar.Add(new ToolbarButton(action) { text = text, tooltip = tooltip });
        }

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.width = 280f;
            pane.style.minWidth = 220f;
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;

            Label header = new Label("НАБОРЫ КАРТЫ");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = HeaderColor;
            pane.Add(header);

            setList = new ListView
            {
                fixedItemHeight = 46f,
                selectionType = SelectionType.Single,
                makeItem = MakeSetItem,
                bindItem = BindSetItem
            };
            setList.style.flexGrow = 1f;
            setList.style.marginTop = 6f;
            setList.selectionChanged += _ =>
            {
                int index = setList.selectedIndex;
                if (index >= 0 && index < visibleSets.Count)
                    SelectSet(visibleSets[index].Id);
            };
            pane.Add(setList);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 4f;
            buttons.style.marginBottom = 6f;
            buttons.Add(new Button(NewSet) { text = "Новый набор", tooltip = "Пустой набор: затем загрузите в него папку кадров." });
            buttons.Add(new Button(DeleteSet) { text = "Удалить набор", tooltip = "Убирает набор из базы карты. Атласы на диске остаются." });
            pane.Add(buttons);
            return pane;
        }

        private static VisualElement MakeSetItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4f;
            Image thumbnail = new Image { name = "thumbnail", scaleMode = ScaleMode.ScaleToFit };
            thumbnail.style.width = 36f;
            thumbnail.style.height = 40f;
            thumbnail.style.marginRight = 6f;
            row.Add(thumbnail);
            VisualElement text = new VisualElement();
            text.style.flexGrow = 1f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Label info = new Label { name = "info" };
            info.style.fontSize = 10f;
            text.Add(title);
            text.Add(info);
            row.Add(text);
            return row;
        }

        private void BindSetItem(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleSets.Count)
                return;
            WorldMapAnimationSetData set = visibleSets[index];
            WorldMapAnimationEditorData.SetState state = WorldMapAnimationEditorData.GetState(set);
            bool hero = string.Equals(set.Id, database.HeroSetId, StringComparison.Ordinal);
            element.Q<Label>("title").text = set.DisplayName + (hero ? "  ★" : string.Empty);
            Label info = element.Q<Label>("info");
            info.text = set.Id + " · " + WorldMapAnimationEditorData.StateTitle(state) + (hero ? " · герой на карте" : string.Empty);
            info.style.color = StateColor(state);
            element.Q<Image>("thumbnail").sprite = FirstWalkFrame(set);
        }

        private static Sprite FirstWalkFrame(WorldMapAnimationSetData set)
        {
            CreatureAnimationClipData walk = set.Animation.FindClip(CreatureAnimationAction.Walk);
            if (walk != null)
            {
                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                {
                    CreatureAnimationFrames cell = walk.FindDirection(direction);
                    if (cell == null)
                        continue;
                    for (int i = 0; i < cell.FrameCount; i++)
                    {
                        if (cell.Frames[i] != null)
                            return cell.Frames[i];
                    }
                }
            }
            return set.Animation.FindFirstFrame();
        }

        private static Color StateColor(WorldMapAnimationEditorData.SetState state)
        {
            switch (state)
            {
                case WorldMapAnimationEditorData.SetState.Ready: return ReadyColor;
                case WorldMapAnimationEditorData.SetState.Partial: return PartialColor;
                default: return EmptyColor;
            }
        }

        private VisualElement BuildMainPane()
        {
            VisualElement main = new VisualElement();
            main.style.flexGrow = 1f;
            main.style.flexDirection = FlexDirection.Row;

            VisualElement center = new VisualElement();
            center.style.width = 380f;
            center.style.minWidth = 320f;
            center.style.paddingLeft = 8f;
            center.style.paddingRight = 8f;
            center.style.paddingTop = 6f;
            titleLabel = new Label();
            titleLabel.style.fontSize = 15f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = HeaderColor;
            center.Add(titleLabel);

            preview = new WorldMapAnimationPreviewElement(340f) { AllowPivotEditing = true };
            preview.style.flexGrow = 0f;
            preview.SelectionChanged += (action, direction) =>
            {
                selectedAction = action;
                selectedDirection = direction;
                ShowSelection(false);
            };
            preview.PivotDragged += OnPivotDragged;
            center.Add(preview);

            setPanel = new VisualElement();
            setPanel.style.marginTop = 8f;
            center.Add(setPanel);
            main.Add(center);

            detailPanel = new ScrollView(ScrollViewMode.Vertical);
            detailPanel.style.flexGrow = 1f;
            detailPanel.style.paddingLeft = 8f;
            detailPanel.style.paddingRight = 10f;
            detailPanel.style.paddingTop = 6f;
            main.Add(detailPanel);
            return main;
        }

        private void RefreshSetList()
        {
            if (setList == null || database == null)
                return;
            visibleSets.Clear();
            foreach (WorldMapAnimationSetData set in database.Sets)
            {
                if (set != null && !string.IsNullOrWhiteSpace(set.Id))
                    visibleSets.Add(set);
            }
            setList.itemsSource = visibleSets;
            setList.Rebuild();
            int index = visibleSets.FindIndex(set => set.Id == selectedSetId);
            if (index >= 0)
                setList.SetSelectionWithoutNotify(new[] { index });
            else
                setList.ClearSelection();
        }

        private void SelectSet(string setId)
        {
            if (selectedSetId != setId)
                CloseUndoGroup();
            selectedSetId = setId ?? string.Empty;
            SessionState.SetString(SelectedSetKey, selectedSetId);
            if (setList != null)
            {
                int index = visibleSets.FindIndex(set => set.Id == selectedSetId);
                if (index >= 0)
                {
                    setList.SetSelectionWithoutNotify(new[] { index });
                    setList.ScrollToItem(index);
                }
            }
            ShowSelection();
        }

        private void ShowSelection(bool updatePreviewSelection = true)
        {
            if (preview == null)
                return;
            WorldMapAnimationSetData set = SelectedSet;
            titleLabel.text = set == null
                ? "Выберите набор слева"
                : set.DisplayName + " · " + WorldMapAnimationEditorData.StateTitle(WorldMapAnimationEditorData.GetState(set));
            preview.SetData(database, set);
            if (updatePreviewSelection)
                preview.Select(selectedAction, selectedDirection);
            BuildSetPanel(set);
            BuildDetailPanel(set);
            setList?.RefreshItems();
        }

        // ------------------------------------------------------------------
        // Настройки набора
        // ------------------------------------------------------------------

        private void BuildSetPanel(WorldMapAnimationSetData set)
        {
            setPanel.Clear();
            if (set == null)
            {
                setPanel.Add(new HelpBox(
                    "Наборов нет. Нажмите «Новый набор» слева, затем перетащите в окно папку кадров.",
                    HelpBoxMessageType.Info));
                return;
            }

            TextField name = new TextField("Название") { value = set.Animation.RawDisplayName, isDelayed = true };
            name.labelElement.style.minWidth = 110f;
            name.RegisterValueChangedCallback(evt => ModifySet("Название набора", s => s.Animation.SetDisplayName(evt.newValue)));
            setPanel.Add(name);
            Label id = new Label("ID набора: " + set.Id + " (стабильный; игра ссылается на него, а не на название)");
            id.style.fontSize = 10f;
            id.style.whiteSpace = WhiteSpace.Normal;
            id.style.color = HintColor;
            setPanel.Add(id);

            bool hero = string.Equals(set.Id, database.HeroSetId, StringComparison.Ordinal);
            Label heroLabel = new Label(hero
                ? "Это герой на карте: игра берёт для фигуры героя этот набор."
                : "Герой на карте: " + HeroDescription() + ".");
            heroLabel.style.whiteSpace = WhiteSpace.Normal;
            heroLabel.style.fontSize = 10f;
            heroLabel.style.marginTop = 2f;
            heroLabel.style.color = hero ? ReadyColor : HintColor;
            setPanel.Add(heroLabel);
            if (!hero)
            {
                setPanel.Add(new Button(() =>
                {
                    Undo.RecordObject(database, "Герой на карте");
                    database.SetHeroSetId(set.Id);
                    EditorUtility.SetDirty(database);
                    ShowSelection();
                })
                {
                    text = "Это герой на карте",
                    tooltip = "Фигура героя на карте будет ходить по кадрам этого набора (нужны кадры ходьбы)."
                });
            }
            else
            {
                AddHeroMoveSpeed();
            }

            Slider height =LiveSlider("Рост на карте, клеток", WorldMapAnimationSetData.MinHeightInHexes, WorldMapAnimationSetData.MaxHeightInHexes, set.HeightInHexes,
                "Высота холста кадра в ширинах клетки карты. Фигура занимает около 77% холста, поэтому 0,4 даёт рост около трети клетки — как у прежней фишки. " +
                "Масштаб просмотра колесом мыши сюда не влияет.",
                value => SelectedSet?.SetHeightInHexes(value));
            setPanel.Add(height);

            Label speed = new Label(SlideFreeText(set));
            speed.style.whiteSpace = WhiteSpace.Normal;
            speed.style.fontSize = 10f;
            speed.style.color = HintColor;
            Slider stride = LiveSlider("Шаг ходьбы, клеток", WorldMapAnimationSetData.MinStrideHexesPerCycle, 5f, set.StrideHexesPerCycle,
                "Сколько клеток карты фигура проходит за один цикл «Ходьбы». Скорость движения по карте общая («Время и движение» в Генераторе карты), " +
                "а темп цикла подстраивается под неё: темп = скорость × длительность цикла / шаг, поэтому ноги не скользят. " +
                "Если ноги «едут» вперёд — уменьшите шаг, если отстают — увеличьте.",
                value =>
                {
                    WorldMapAnimationSetData s = SelectedSet;
                    s?.SetStrideHexesPerCycle(value);
                    speed.text = SlideFreeText(s);
                });
            // Подгонка темпа под скорость: выключена — «Кадров в секунду» действуют
            // напрямую; включена — темп считается от «Шага ходьбы».
            Toggle sync = new Toggle("Подгонять темп под скорость движения")
            {
                value = set.SyncCadenceToSpeed,
                tooltip = "Выключено: бег идёт ровно с «Кадров в секунду» этого набора, при любой скорости по карте. " +
                          "Включено: темп бега пересчитывается от «Шага ходьбы» и скорости фигуры, чтобы ноги не скользили; «Кадров в секунду» тогда почти не влияют."
            };
            sync.style.marginTop = 4f;
            void ShowStride(bool on)
            {
                stride.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                speed.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            sync.RegisterValueChangedCallback(evt =>
            {
                WorldMapAnimationSetData s = SelectedSet;
                if (s == null)
                    return;
                Undo.RecordObject(database, "Подгонка темпа");
                s.SetSyncCadenceToSpeed(evt.newValue);
                EditorUtility.SetDirty(database);
                ShowStride(evt.newValue);
                preview.Rebuild();
            });
            ShowStride(set.SyncCadenceToSpeed);
            setPanel.Add(sync);
            setPanel.Add(stride);
            setPanel.Add(speed);

            Toggle stand = new Toggle("На стоянке — прежняя фигурка карты")
            {
                value = set.StandWithMarkerFigure,
                tooltip = "Включено: стоящий герой показывается прежней статичной фигуркой фишки карты в ракурсе последнего движения, " +
                          "анимация — только при беге. Выключено: на стоянке «Ожидание» набора, а без него первый кадр «Ходьбы». " +
                          "Сейчас прежняя фигурка есть только у героя; у остальных наборов галочка пока ничего не меняет."
            };
            stand.style.marginTop = 4f;
            stand.RegisterValueChangedCallback(evt =>
            {
                WorldMapAnimationSetData s = SelectedSet;
                if (s == null)
                    return;
                Undo.RecordObject(database, "Стоянка: прежняя фигурка");
                s.SetStandWithMarkerFigure(evt.newValue);
                EditorUtility.SetDirty(database);
                preview.Rebuild();
            });
            setPanel.Add(stand);

            pivotXSlider = LiveSlider("Опора X", 0f, 1f, set.Animation.Pivot.x,
                "Точка опоры по горизонтали: доля ширины кадра слева. Обычно 0,5 — середина.",
                value => { WorldMapAnimationSetData s = SelectedSet; s?.Animation.SetPivot(new Vector2(Mathf.Clamp01(value), s.Animation.Pivot.y)); });
            pivotYSlider = LiveSlider("Опора Y", 0f, 1f, set.Animation.Pivot.y,
                "Точка опоры по вертикали: доля высоты кадра снизу. Ставится на уровень ног — место, которое стоит в центре клетки карты. " +
                "Красную точку можно тянуть и в просмотре.",
                value => { WorldMapAnimationSetData s = SelectedSet; s?.Animation.SetPivot(new Vector2(s.Animation.Pivot.x, Mathf.Clamp01(value))); });
            setPanel.Add(pivotXSlider);
            setPanel.Add(pivotYSlider);

            Label canvas = new Label(set.Animation.CanvasSize.x > 0
                ? "Холст кадров: " + set.Animation.CanvasSize.x + "×" + set.Animation.CanvasSize.y + " пикс."
                : "Холст задаст первая загрузка.");
            canvas.style.fontSize = 10f;
            canvas.style.color = HintColor;
            setPanel.Add(canvas);
        }

        private string HeroDescription()
        {
            WorldMapAnimationSetData hero = database.FindSet(database.HeroSetId);
            return hero != null ? "«" + hero.DisplayName + "»" : "не назначен";
        }

        // Скорость героя по карте — общая настройка движения (ассет
        // KingdomSurvivalWorldMapMovement, «Генератор карты» → «Время и
        // движение»): здесь тот же ползунок, отдельного значения нет. Границы —
        // как у Range поля heroRunSpeedHexesPerSecond.
        private const float MinHeroMoveSpeed = 0.1f;
        private const float MaxHeroMoveSpeed = 12f;

        private void AddHeroMoveSpeed()
        {
            WorldMapMovementSettingsAsset movement = AssetDatabase.LoadAssetAtPath<WorldMapMovementSettingsAsset>(WorldMapMovementSettingsAsset.AssetPath);
            SerializedObject serialized = movement != null ? new SerializedObject(movement) : null;
            SerializedProperty speedProperty = serialized?.FindProperty("heroRunSpeedHexesPerSecond");
            if (speedProperty == null)
            {
                Label missing = new Label("Настройки движения карты не найдены: скорость героя задаётся в «Генераторе карты».");
                missing.style.whiteSpace = WhiteSpace.Normal;
                missing.style.fontSize = 10f;
                missing.style.color = ErrorColor;
                setPanel.Add(missing);
                return;
            }

            const string label = "Скорость по карте, клеток/с";
            Label hint = new Label(HeroMoveSpeedText(movement));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = HintColor;
            Slider slider = new Slider(label, MinHeroMoveSpeed, MaxHeroMoveSpeed)
            {
                value = speedProperty.floatValue,
                showInputField = true,
                tooltip = "Сколько клеток карты в секунду реального времени герой проходит по открытой местности (на ×1). " +
                          "Это общая скорость движения по карте — та же настройка, что «Генератор карты» → «Время и движение»; " +
                          "группы мира идут со своей долей от неё, местность замедляет или ускоряет. В игре применяется сразу."
            };
            slider.style.marginTop = 4f;
            slider.labelElement.style.minWidth = 130f;
            slider.RegisterValueChangedCallback(evt =>
            {
                OpenUndoGroup(label);
                serialized.Update();
                speedProperty.floatValue = Mathf.Clamp(evt.newValue, MinHeroMoveSpeed, MaxHeroMoveSpeed);
                serialized.ApplyModifiedProperties();
                hint.text = HeroMoveSpeedText(movement);
                if (EditorApplication.isPlaying)
                    WorldMapVisualRuntime.ApplyMovementRules();
            });
            slider.RegisterCallback<PointerUpEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<FocusOutEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            setPanel.Add(slider);
            setPanel.Add(hint);
        }

        private static string HeroMoveSpeedText(WorldMapMovementSettingsAsset movement)
        {
            float speed = Mathf.Max(MinHeroMoveSpeed, movement.HeroRunSpeedHexesPerSecond);
            string text = "Общая настройка движения карты (та же, что в «Генераторе карты» → «Время и движение»).";
            if (movement.IndependentTime)
                text += " Клетка открытой местности ≈ " + (movement.WorldGameHoursPerRealSecond / speed).ToString("0.##") + " игровых ч.";
            return text;
        }

        private static string SlideFreeText(WorldMapAnimationSetData set)
        {
            if (set == null)
                return string.Empty;
            float duration = WorldMapAnimationEditorData.WalkCycleSeconds(set);
            if (duration <= 0f)
                return "Ходьбы нет: скорость без скольжения посчитать нельзя.";
            return "Скорость без скольжения ≈ " + WorldMapAnimationEditorData.SlideFreeSpeed(set).ToString("0.00") +
                   " клеток/с (шаг " + set.StrideHexesPerCycle.ToString("0.00") + " / цикл " + duration.ToString("0.00") +
                   " с). При другой скорости движения темп ходьбы ускоряется или замедляется.";
        }

        // Ползунки меняют набор без перестройки панели: перетаскивание не
        // обрывается, просмотр обновляется сразу, весь жест — одно Undo.
        private Slider LiveSlider(string label, float low, float high, float value, string tooltip, Action<float> apply)
        {
            Slider slider = new Slider(label, low, high) { value = value, showInputField = true, tooltip = tooltip };
            slider.style.marginTop = 2f;
            slider.labelElement.style.minWidth = 130f;
            slider.RegisterValueChangedCallback(evt =>
            {
                if (SelectedSet == null)
                    return;
                OpenUndoGroup(label);
                Undo.RecordObject(database, label);
                apply(evt.newValue);
                EditorUtility.SetDirty(database);
                preview.Rebuild();
            });
            // Конец жеста: всё перетаскивание — одна отмена.
            slider.RegisterCallback<PointerUpEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            slider.RegisterCallback<FocusOutEvent>(_ => CloseUndoGroup(), TrickleDown.TrickleDown);
            return slider;
        }

        // Одно перетаскивание опоры — одно действие Undo; ползунки следуют за точкой.
        private void OnPivotDragged(Vector2 pivot, bool finished)
        {
            WorldMapAnimationSetData set = SelectedSet;
            if (set == null)
                return;
            OpenUndoGroup("Точка опоры");
            Undo.RecordObject(database, "Точка опоры");
            set.Animation.SetPivot(pivot);
            EditorUtility.SetDirty(database);
            pivotXSlider?.SetValueWithoutNotify(pivot.x);
            pivotYSlider?.SetValueWithoutNotify(pivot.y);
            preview.Rebuild();
            if (finished)
                CloseUndoGroup();
        }

        private void OpenUndoGroup(string name)
        {
            if (undoGroupOpen)
                return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            undoGroupIndex = Undo.GetCurrentGroup();
            undoGroupOpen = true;
        }

        private void CloseUndoGroup()
        {
            if (!undoGroupOpen)
                return;
            undoGroupOpen = false;
            Undo.CollapseUndoOperations(undoGroupIndex);
            Undo.IncrementCurrentGroup();
        }

        private void ModifySet(string undoName, Action<WorldMapAnimationSetData> change)
        {
            WorldMapAnimationSetData set = SelectedSet;
            if (set == null)
                return;
            Undo.RecordObject(database, undoName);
            change(set);
            EditorUtility.SetDirty(database);
            ShowSelection();
        }

        private void ModifyClip(string undoName, Action<CreatureAnimationClipData> change)
        {
            CreatureAnimationClipData clip = SelectedSet?.Animation.FindClip(selectedAction);
            if (clip == null)
                return;
            Undo.RecordObject(database, undoName);
            change(clip);
            EditorUtility.SetDirty(database);
            ShowSelection();
        }

        // ------------------------------------------------------------------
        // Правая часть: таблица, настройки действия, таблица ракурсов
        // ------------------------------------------------------------------

        private void BuildDetailPanel(WorldMapAnimationSetData set)
        {
            detailPanel.Clear();
            if (set == null)
                return;

            AddHeader("ДЕЙСТВИЯ И РАКУРСЫ");
            detailPanel.Add(BuildTable(set));
            Label hint = new Label("Ячейка: первый кадр и число кадров. Щёлкните ячейку, чтобы смотреть её. Папку PNG — на ячейку (кадры только этой ячейки) " +
                                   "или в окно (всё сразу: Walk/<ракурс>). Нужна «Ходьба» во всех шести ракурсах. «Ожидание» необязательно: если у набора включено " +
                                   "«На стоянке — прежняя фигурка карты» (у героя), стоящая фигура — прежняя фишка, а «Ожидание» не используется; " +
                                   "иначе на стоянке показывается «Ожидание», а без него первый кадр ходьбы.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = HintColor;
            detailPanel.Add(hint);

            CreatureAnimationClipData clip = set.Animation.FindClip(selectedAction);
            AddHeader(CreatureAnimationLabels.ActionTitle(selectedAction).ToUpperInvariant() + " → " +
                      CreatureAnimationLabels.DirectionTitle(selectedDirection).ToUpperInvariant());
            if (clip == null || !clip.HasAnyFrames)
                detailPanel.Add(new Label("Кадров нет. " + FallbackNote(selectedAction)));
            else
                BuildClipSettings(clip);

            BuildDiagnostics(set);
            BuildDirectionMap();
        }

        private static string FallbackNote(CreatureAnimationAction action)
        {
            return action == CreatureAnimationAction.Idle
                ? "На стоянке игра покажет первый кадр ходьбы."
                : "Без кадров ходьбы игра не использует набор.";
        }

        private VisualElement BuildTable(WorldMapAnimationSetData set)
        {
            VisualElement table = new VisualElement();
            VisualElement header = TableRow();
            header.Add(TableCellLabel(string.Empty, 96f));
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                WorldMapFacing facing = database.GetFacing(direction);
                Label label = TableCellLabel(
                    WorldMapFacingLabels.Arrow(facing) + "\n" + CreatureAnimationLabels.DirectionFolder(direction),
                    TableCellWidth);
                label.style.fontSize = 9f;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.tooltip = "Папка " + CreatureAnimationLabels.DirectionFolder(direction) + " · на карте смотрит " +
                                WorldMapFacingLabels.Title(facing);
                header.Add(label);
            }
            table.Add(header);

            foreach (CreatureAnimationAction action in WorldMapAnimationEditorData.Actions)
            {
                VisualElement row = TableRow();
                CreatureAnimationClipData clip = set.Animation.FindClip(action);
                int present = clip != null ? clip.DirectionsWithFrames : 0;
                Label title = TableCellLabel(CreatureAnimationLabels.ActionTitle(action) + (present > 0 ? "  " + present + "/6" : string.Empty) +
                                             (action == CreatureAnimationAction.Idle ? "\n(необязательно)" : string.Empty), 96f);
                title.style.unityTextAlign = TextAnchor.MiddleLeft;
                title.style.minHeight = TableCellHeight;
                title.style.color = present == CreatureAnimationLabels.DirectionCount ? ReadyColor : present > 0 ? PartialColor : EmptyColor;
                if (action == selectedAction)
                    title.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(title);
                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                    row.Add(BuildTableCell(clip?.FindDirection(direction), action, direction));
                table.Add(row);
            }
            return table;
        }

        private VisualElement BuildTableCell(CreatureAnimationFrames cell, CreatureAnimationAction action, CreatureAnimationDirection direction)
        {
            int count = cell != null ? cell.FrameCount : 0;
            bool broken = cell != null && Enumerable.Range(0, count).Any(i => cell.Frames[i] == null);
            bool selected = action == selectedAction && direction == selectedDirection;
            string where = CreatureAnimationLabels.ActionTitle(action) + " → " + CreatureAnimationLabels.DirectionTitle(direction);

            VisualElement box = new VisualElement();
            box.style.width = TableCellWidth;
            box.style.height = TableCellHeight;
            box.style.marginRight = 2f;
            SetBorder(box, selected ? HeaderColor : new Color(0.25f, 0.25f, 0.26f, 1f), selected ? 2f : 1f);
            box.style.backgroundColor = broken ? new Color(0.40f, 0.14f, 0.12f, 1f)
                : count > 0 ? new Color(0.17f, 0.27f, 0.19f, 1f)
                : new Color(0.17f, 0.17f, 0.18f, 1f);
            box.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                selectedAction = action;
                selectedDirection = direction;
                ShowSelection();
            });

            Sprite first = count > 0 ? cell.Frames.FirstOrDefault(sprite => sprite != null) : null;
            if (first != null)
            {
                Image thumb = new Image { sprite = first, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                thumb.style.position = Position.Absolute;
                thumb.style.left = thumb.style.top = thumb.style.right = thumb.style.bottom = 1f;
                box.Add(thumb);
            }
            Label frameCount = new Label(count > 0 ? count.ToString() : "—") { pickingMode = PickingMode.Ignore };
            frameCount.style.position = Position.Absolute;
            frameCount.style.right = 2f;
            frameCount.style.bottom = 0f;
            frameCount.style.fontSize = 11f;
            frameCount.style.unityFontStyleAndWeight = FontStyle.Bold;
            frameCount.style.color = count > 0 ? new Color(0.92f, 0.95f, 0.90f, 1f) : EmptyColor;
            box.Add(frameCount);
            box.tooltip = where + (count > 0 ? ": " + count + " кадр(ов)" : ": нет кадров") +
                          (broken ? "\nЕсть кадры без картинки (атлас удалён или не на этом компьютере)." : string.Empty) +
                          "\nПеретащите сюда папку PNG — кадры этой ячейки.";
            RegisterCellDrop(box, () =>
            {
                selectedAction = action;
                selectedDirection = direction;
                DropIntoCell(action, direction);
            });
            return box;
        }

        // Зона перетаскивания внутри ячейки таблицы: подсветка и приём.
        private static void RegisterCellDrop(VisualElement zone, Action perform)
        {
            void Highlight(bool on) => SetBorderKeepingSelection(zone, on);
            zone.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool droppable = HasDroppableContent();
                DragAndDrop.visualMode = droppable ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                Highlight(droppable);
                evt.StopPropagation();
            });
            zone.RegisterCallback<DragLeaveEvent>(_ => Highlight(false));
            zone.RegisterCallback<DragExitedEvent>(_ => Highlight(false));
            zone.RegisterCallback<DragPerformEvent>(evt =>
            {
                Highlight(false);
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                perform();
            });
        }

        // Подсветка цели сбрасывается к исходной рамке ячейки (она после
        // броска всё равно перестраивается).
        private static void SetBorderKeepingSelection(VisualElement zone, bool on)
        {
            if (on)
            {
                SetBorder(zone, DropHighlightColor, 2f);
                return;
            }
            SetBorder(zone, new Color(0.25f, 0.25f, 0.26f, 1f), 1f);
        }

        private void BuildClipSettings(CreatureAnimationClipData clip)
        {
            FloatField fps = new FloatField("Кадров в секунду") { value = clip.RawFramesPerSecond, isDelayed = true };
            fps.tooltip = "Скорость клипа. 12 — начальное значение. Меняется без повторной загрузки PNG.";
            fps.RegisterValueChangedCallback(evt =>
                ModifyClip("Скорость анимации", c => c.SetFramesPerSecond(Mathf.Clamp(evt.newValue, CreatureAnimationClipData.MinFramesPerSecond, CreatureAnimationClipData.MaxFramesPerSecond))));
            detailPanel.Add(fps);

            CreatureAnimationClip resolved = CreatureAnimationResolver.ResolveDirection(selectedAction, selectedDirection, clip);
            string duration = resolved != null
                ? "Длительность: " + resolved.Duration.ToString("0.00") + " с · " + resolved.FrameCount + " кадр(ов)" +
                  (resolved.IsDirectionSubstitute ? " (ракурс «" + CreatureAnimationLabels.DirectionTitle(resolved.Direction) + "»)" : string.Empty)
                : "Длительность: —";
            Label durationLabel = new Label(duration);
            durationLabel.style.marginLeft = 3f;
            durationLabel.style.marginBottom = 3f;
            detailPanel.Add(durationLabel);

            PopupField<CreatureAnimationPlayback> playback = new PopupField<CreatureAnimationPlayback>(
                "Воспроизведение",
                new List<CreatureAnimationPlayback> { CreatureAnimationPlayback.Loop, CreatureAnimationPlayback.Once, CreatureAnimationPlayback.HoldLastFrame },
                clip.Playback,
                CreatureAnimationLabels.PlaybackTitle,
                CreatureAnimationLabels.PlaybackTitle);
            playback.tooltip = "Ходьба и ожидание на карте — цикл.";
            playback.RegisterValueChangedCallback(evt => ModifyClip("Воспроизведение", c => c.SetPlayback(evt.newValue)));
            detailPanel.Add(playback);

            List<int> skipChoices = Enumerable.Range(0, CreatureAnimationClipData.MaxSkippedLastFrames + 1).ToList();
            PopupField<int> skipLast = new PopupField<int>(
                "Не учитывать последних кадров",
                skipChoices,
                clip.SkipLastFrames,
                SkipChoiceLabel,
                SkipChoiceLabel);
            skipLast.tooltip = "Для цикла, у которого последние кадры экспорта повторяют начало: без этого цикл «залипает» на стыке. " +
                               "Действует на все ракурсы действия; файлы не меняются.";
            skipLast.RegisterValueChangedCallback(evt => ModifyClip("Последние кадры", c => c.SetSkipLastFrames(evt.newValue)));
            detailPanel.Add(skipLast);
        }

        private static string SkipChoiceLabel(int count)
        {
            return count == 0 ? "0 — показывать все" : count.ToString();
        }

        private void BuildDiagnostics(WorldMapAnimationSetData set)
        {
            AddHeader("ДИАГНОСТИКА");
            List<string> errors = WorldMapAnimationEditorData.CollectDataErrors(database)
                .Where(error => error.StartsWith(set.DisplayName, StringComparison.Ordinal) || !error.Contains(" · "))
                .ToList();
            foreach (string error in errors)
                detailPanel.Add(DiagnosticLine(error, ErrorColor));

            List<string> warnings = new List<string>();
            CreatureAnimationClipData walk = set.Animation.FindClip(CreatureAnimationAction.Walk);
            if (walk == null || !walk.HasAnyFrames)
            {
                warnings.Add("Нет «Ходьбы»: без неё игра не использует этот набор.");
            }
            else
            {
                List<string> missing = new List<string>();
                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                {
                    CreatureAnimationFrames cell = walk.FindDirection(direction);
                    if (cell == null || !cell.HasFrames)
                        missing.Add(CreatureAnimationLabels.DirectionFolder(direction));
                }
                if (missing.Count > 0)
                    warnings.Add("«Ходьба» — нет ракурсов: " + string.Join(", ", missing) + ". Для них показывается ближайший имеющийся.");
            }
            CreatureAnimationClipData idle = set.Animation.FindClip(CreatureAnimationAction.Idle);
            if (idle == null || !idle.HasAnyFrames)
                warnings.Add("Нет «Ожидания» (необязательно): на стоянке показывается первый кадр ходьбы.");
            foreach (string warning in warnings)
                detailPanel.Add(DiagnosticLine(warning, PartialColor));
            if (errors.Count == 0 && warnings.Count == 0)
                detailPanel.Add(DiagnosticLine("Набор полный, ошибок нет.", ReadyColor));
        }

        private void BuildDirectionMap()
        {
            Foldout map = new Foldout { text = "Таблица ракурсов карты (общая для всех наборов карты)", value = directionMapOpen };
            map.RegisterValueChangedCallback(evt => { if (evt.target == map) directionMapOpen = evt.newValue; });
            map.style.marginTop = 10f;
            Label note = new Label("Названия ракурсов — как у исходных картинок: Front — к зрителю (бег вниз), Front_Right — вправо-вниз, " +
                                   "Back_Right — вправо-вверх, Back — от зрителя (вверх), Back_Left — влево-вверх, Front_Left — влево-вниз. " +
                                   "Таблица общая для всех наборов карты и не связана с боевой. Проверьте по белой стрелке в просмотре: " +
                                   "она должна совпадать со взглядом фигуры.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 10f;
            map.Add(note);
            List<WorldMapFacing> facings = Enum.GetValues(typeof(WorldMapFacing)).Cast<WorldMapFacing>().ToList();
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                PopupField<WorldMapFacing> field = new PopupField<WorldMapFacing>(
                    CreatureAnimationLabels.DirectionFolder(direction),
                    facings,
                    database.GetFacing(direction),
                    FacingChoiceLabel,
                    FacingChoiceLabel);
                CreatureAnimationDirection captured = direction;
                field.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(database, "Таблица ракурсов карты");
                    database.AssignFacing(captured, evt.newValue);
                    EditorUtility.SetDirty(database);
                    ShowSelection();
                });
                map.Add(field);
            }
            Button reset = new Button(() =>
            {
                Undo.RecordObject(database, "Таблица ракурсов карты по умолчанию");
                database.ResetDirectionMap();
                EditorUtility.SetDirty(database);
                ShowSelection();
            }) { text = "Сбросить по умолчанию" };
            map.Add(reset);
            detailPanel.Add(map);
        }

        private static string FacingChoiceLabel(WorldMapFacing facing)
        {
            return WorldMapFacingLabels.Arrow(facing) + " " + WorldMapFacingLabels.Title(facing);
        }

        // ------------------------------------------------------------------
        // Наборы
        // ------------------------------------------------------------------

        private void NewSet()
        {
            Undo.RecordObject(database, "Новый набор карты");
            WorldMapAnimationSetData set = database.AddSet(database.MakeUniqueSetId("figure"), "Новая фигура");
            EditorUtility.SetDirty(database);
            RefreshSetList();
            SelectSet(set.Id);
            ShowMessage("Создан набор «" + set.DisplayName + "». Переименуйте его и загрузите кадры.", ReadyColor);
        }

        private void DeleteSet()
        {
            WorldMapAnimationSetData set = SelectedSet;
            if (set == null)
            {
                ShowMessage("Сначала выберите набор слева.", ErrorColor);
                return;
            }
            bool hero = string.Equals(set.Id, database.HeroSetId, StringComparison.Ordinal);
            if (!EditorUtility.DisplayDialog(
                    "Удалить набор?",
                    "Набор «" + set.DisplayName + "» будет убран из базы карты" +
                    (hero ? "; герой на карте останется без набора" : string.Empty) +
                    ". Атласы на диске останутся. Отменить можно через Undo.",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }
            Undo.RecordObject(database, "Удалить набор карты");
            database.RemoveSet(set.Id);
            if (hero)
                database.SetHeroSetId(string.Empty);
            EditorUtility.SetDirty(database);
            RefreshSetList();
            selectedSetId = visibleSets.Count > 0 ? visibleSets[0].Id : string.Empty;
            SelectSet(selectedSetId);
        }

        // ------------------------------------------------------------------
        // Загрузка
        // ------------------------------------------------------------------

        private void LoadFolder()
        {
            if (SelectedSet == null)
            {
                ShowMessage("Сначала создайте или выберите набор слева.", ErrorColor);
                return;
            }
            string folder = EditorUtility.OpenFolderPanel("Папка героя / пакета кадров", SessionState.GetString(LastFolderKey, string.Empty), string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;
            SessionState.SetString(LastFolderKey, Path.GetDirectoryName(folder) ?? string.Empty);
            AnalyzeFolder(folder);
        }

        private void AnalyzeFolder(string folder)
        {
            string[] files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            BeginImport(CreatureAnimationImportParser.Analyze(folder, files), CreatureAnimationImportMode.Replace);
        }

        private void LoadIntoSelectedCell(CreatureAnimationImportMode mode)
        {
            if (SelectedSet == null)
            {
                ShowMessage("Сначала создайте или выберите набор слева.", ErrorColor);
                return;
            }
            string folder = EditorUtility.OpenFolderPanel(
                (mode == CreatureAnimationImportMode.Append ? "Добавить кадры: " : "Заменить кадры: ") +
                CreatureAnimationLabels.ActionTitle(selectedAction) + " → " + CreatureAnimationLabels.DirectionTitle(selectedDirection),
                SessionState.GetString(LastFolderKey, string.Empty),
                string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;
            BeginImport(CreatureAnimationImportParser.AnalyzeSequence(
                Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly), selectedAction, selectedDirection), mode);
        }

        // Раздел карты принимает только ходьбу и ожидание, карты нормалей не
        // использует: лишнее отбрасывается с замечанием в сводке.
        public static void PreparePackage(CreatureAnimationImportPackage package)
        {
            bool normalsDropped = false;
            foreach (CreatureAnimationImportGroup group in package.Groups)
            {
                if (group.ChosenAction.HasValue && !WorldMapAnimationEditorData.IsSupportedAction(group.ChosenAction.Value))
                {
                    package.AddWarning("Папка «" + group.RawName + "» («" + CreatureAnimationLabels.ActionTitle(group.ChosenAction.Value) +
                                       "») пропущена: на карте пока нужны только «Ходьба» и «Ожидание».");
                    group.ChosenAction = null;
                    continue;
                }
                foreach (CreatureAnimationImportCell cell in group.Cells.Values)
                {
                    foreach (CreatureAnimationSourceFrame frame in cell.Frames)
                    {
                        if (frame.NormalPath.Length > 0)
                        {
                            frame.NormalPath = string.Empty;
                            normalsDropped = true;
                        }
                    }
                }
            }
            if (normalsDropped)
                package.AddWarning("Карты нормалей на карте не используются — проигнорированы.");
            CreatureAnimationImportParser.ValidateDuplicateTargets(package);
        }

        // Новая загрузка без предупреждений и замен применяется сразу;
        // иначе — сводка с подтверждением.
        private void BeginImport(CreatureAnimationImportPackage package, CreatureAnimationImportMode mode)
        {
            WorldMapAnimationSetData set = SelectedSet;
            if (set == null)
                return;
            PreparePackage(package);
            List<CreatureAnimationImportChange> changes = CreatureAnimationImporter.DescribeChanges(package, set.Animation);
            string summary = Summary(package, changes, mode);

            if (package.HasErrors || changes.Count == 0)
            {
                EditorUtility.DisplayDialog("Загрузка кадров карты", (changes.Count == 0 && !package.HasErrors
                    ? "Нечего загружать: в папке не найдено «Ходьбы» или «Ожидания».\n\n"
                    : "Есть ошибки, набор не изменён.\n\n") + summary, "Закрыть");
                return;
            }

            bool quiet = package.Issues.Count == 0 && (mode == CreatureAnimationImportMode.Append || changes.All(change => !change.Replaces));
            if (!quiet && !EditorUtility.DisplayDialog(
                    "Загрузка кадров карты · «" + set.DisplayName + "»",
                    summary,
                    changes.Any(change => change.Replaces) && mode == CreatureAnimationImportMode.Replace ? "Применить с заменой" : "Применить",
                    "Отмена"))
            {
                ShowMessage("Загрузка отменена. Набор не изменён.", EmptyColor);
                return;
            }

            if (CreatureAnimationImporter.ApplyToWorldMap(database, set, package, mode, out string report))
            {
                ShowMessage(report.Split('\n')[0], ReadyColor);
                Debug.Log("База анимаций · Глобальная карта · " + report);
                RefreshSetList();
                ShowSelection();
            }
            else
            {
                ShowMessage("Загрузка не применена: есть ошибки. Набор не изменён.", ErrorColor);
                EditorUtility.DisplayDialog("Загрузка кадров карты", "Загрузка не применена, набор не изменён.\n\n" +
                                            string.Join("\n", package.Issues.Select(issue => issue.ToString())), "Закрыть");
            }
        }

        private static string Summary(CreatureAnimationImportPackage package, List<CreatureAnimationImportChange> changes, CreatureAnimationImportMode mode)
        {
            List<string> lines = new List<string>
            {
                "Источник: " + package.RootPath,
                "Файлов: " + package.FileCount + (mode == CreatureAnimationImportMode.Append ? " · режим: добавить в конец" : string.Empty),
                string.Empty
            };
            lines.AddRange(changes.Take(14).Select(change => (change.Replaces ? "↻ " : "+ ") + change.Describe(mode)));
            if (changes.Count > 14)
                lines.Add("…и ещё " + (changes.Count - 14));
            if (package.Issues.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(package.Issues.Take(10).Select(issue => issue.ToString()));
                if (package.Issues.Count > 10)
                    lines.Add("…и ещё " + (package.Issues.Count - 10));
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------
        // Перетаскивание
        // ------------------------------------------------------------------

        private static bool HasDroppableContent()
        {
            return DragAndDrop.paths != null && DragAndDrop.paths.Any(path => Directory.Exists(ToAbsolutePath(path)) || CreatureAnimationImportParser.IsImageFile(path));
        }

        private void OnSectionDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = SelectedSet != null && HasDroppableContent()
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
            evt.StopPropagation();
        }

        // Папка, брошенная в окно, разбирается как папка героя / пакета.
        private void OnSectionDragPerform(DragPerformEvent evt)
        {
            evt.StopPropagation();
            if (SelectedSet == null)
                return;
            DragAndDrop.AcceptDrag();
            string[] paths = (DragAndDrop.paths ?? Array.Empty<string>()).Select(ToAbsolutePath).ToArray();
            string folder = paths.FirstOrDefault(Directory.Exists);
            if (folder != null)
            {
                AnalyzeFolder(folder);
                return;
            }
            List<string> files = paths.Where(CreatureAnimationImportParser.IsImageFile).ToList();
            if (files.Count > 0)
                BeginImport(CreatureAnimationImportParser.Analyze(Path.GetDirectoryName(files[0]), files), CreatureAnimationImportMode.Replace);
        }

        private void DropIntoCell(CreatureAnimationAction action, CreatureAnimationDirection direction)
        {
            List<string> files = new List<string>();
            foreach (string path in DragAndDrop.paths ?? Array.Empty<string>())
            {
                string absolute = ToAbsolutePath(path);
                if (Directory.Exists(absolute))
                    files.AddRange(Directory.GetFiles(absolute, "*.png", SearchOption.TopDirectoryOnly));
                else if (CreatureAnimationImportParser.IsImageFile(absolute))
                    files.Add(absolute);
            }
            if (files.Count == 0)
            {
                // Папка с ракурсами или действиями — как папка, брошенная в окно.
                string folder = (DragAndDrop.paths ?? Array.Empty<string>()).Select(ToAbsolutePath).FirstOrDefault(Directory.Exists);
                if (folder != null && Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories).Length > 0)
                    AnalyzeFolder(folder);
                return;
            }
            BeginImport(CreatureAnimationImportParser.AnalyzeSequence(files, action, direction), CreatureAnimationImportMode.Replace);
        }

        // Пути из окна Project — относительные («Assets/…»).
        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return CreatureAnimationAtlasBuilder.ToAbsolute(path);
        }

        // ------------------------------------------------------------------
        // Прочие команды
        // ------------------------------------------------------------------

        private void DeleteSelectedAnimation()
        {
            WorldMapAnimationSetData set = SelectedSet;
            CreatureAnimationClipData clip = set?.Animation.FindClip(selectedAction);
            if (clip == null)
            {
                ShowMessage("У выбранного действия нет кадров.", EmptyColor);
                return;
            }
            string actionTitle = CreatureAnimationLabels.ActionTitle(selectedAction);
            int choice = EditorUtility.DisplayDialogComplex(
                "Удалить анимацию?",
                "Назначение убирается из базы, картинки на диске остаются. Отменить можно через Undo.",
                "Только ракурс «" + CreatureAnimationLabels.DirectionTitle(selectedDirection) + "»",
                "Отмена",
                "Всё действие «" + actionTitle + "»");
            if (choice == 1)
                return;
            Undo.RecordObject(database, "Удалить анимацию карты");
            if (choice == 0)
            {
                clip.RemoveDirection(selectedDirection);
                if (!clip.HasAnyFrames)
                    set.Animation.RemoveClip(selectedAction);
            }
            else
            {
                set.Animation.RemoveClip(selectedAction);
            }
            EditorUtility.SetDirty(database);
            ShowMessage("Назначение удалено.", EmptyColor);
            ShowSelection();
        }

        private void Save()
        {
            AssetDatabase.SaveAssetIfDirty(database);
            ShowMessage("Сохранено.", ReadyColor);
        }

        private void ValidateDatabase()
        {
            List<string> errors = WorldMapAnimationEditorData.CollectDataErrors(database);
            int partial = visibleSets.Count(set => WorldMapAnimationEditorData.GetState(set) == WorldMapAnimationEditorData.SetState.Partial);
            string summary = errors.Count == 0
                ? "Ошибок данных нет." + (partial > 0 ? "\nНаборов с неполной ходьбой: " + partial + " (недостающие ракурсы заменяются ближайшими)." : string.Empty)
                : string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? "\n…и ещё " + (errors.Count - 20) : string.Empty);
            ShowMessage(errors.Count == 0 ? "Ошибок нет" : "Ошибок: " + errors.Count, errors.Count == 0 ? ReadyColor : ErrorColor);
            EditorUtility.DisplayDialog("Проверка базы анимаций карты", summary, "Закрыть");
        }

        private void CleanUnusedAtlases()
        {
            List<string> unused = WorldMapAnimationEditorData.FindUnusedAtlases(database);
            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Атласы карты", "Неиспользуемых атласов нет.", "Хорошо");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Очистить неиспользуемые атласы карты?",
                    "Эти атласы не нужны ни одному набору карты (остались от прежних загрузок):\n" +
                    string.Join("\n", unused.Take(12).Select(Path.GetFileName)) +
                    (unused.Count > 12 ? "\n…и ещё " + (unused.Count - 12) : string.Empty) +
                    "\n\nФайлы уйдут в корзину. После этого Undo старых загрузок не вернёт их кадры.",
                    "В корзину",
                    "Отмена"))
            {
                return;
            }
            List<string> failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(unused.ToArray(), failed);
            ShowMessage("Убрано атласов: " + (unused.Count - failed.Count) + (failed.Count > 0 ? ", не удалось: " + failed.Count : string.Empty), ReadyColor);
        }

        private void ShowMessage(string text, Color color)
        {
            if (messageLabel == null)
                return;
            messageLabel.text = text ?? string.Empty;
            messageLabel.style.color = color;
            messageLabel.tooltip = text ?? string.Empty;
        }

        private void AddHeader(string text)
        {
            Label header = new Label(text);
            header.style.marginTop = 10f;
            header.style.marginBottom = 4f;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = HeaderColor;
            detailPanel.Add(header);
        }

        private static Label DiagnosticLine(string text, Color color)
        {
            Label line = new Label("• " + text);
            line.style.whiteSpace = WhiteSpace.Normal;
            line.style.color = color;
            line.style.fontSize = 11f;
            return line;
        }

        private static VisualElement TableRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2f;
            return row;
        }

        private static Label TableCellLabel(string text, float width)
        {
            Label label = new Label(text);
            label.style.width = width;
            label.style.minHeight = 22f;
            label.style.marginRight = 2f;
            label.style.paddingLeft = 3f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }
    }
}
