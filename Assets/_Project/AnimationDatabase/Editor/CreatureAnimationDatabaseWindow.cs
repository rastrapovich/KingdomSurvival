using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // «База анимаций»: художник выбирает существо, перетаскивает папку PNG
    // из KS Sprite Renderer, проверяет предпросмотр и сохраняет.
    public sealed class CreatureAnimationDatabaseWindow : EditorWindow
    {
        // Ключ для «Открыть в базе существ»: окно существ читает его при фокусе.
        public const string PendingUnitSelectionKey = "KingdomSurvival.UnitDatabase.PendingSelection";

        private static readonly Color ReadyColor = new Color(0.35f, 0.72f, 0.40f, 1f);
        private static readonly Color PartialColor = new Color(0.90f, 0.70f, 0.30f, 1f);
        private static readonly Color EmptyColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color ErrorColor = new Color(0.92f, 0.42f, 0.34f, 1f);
        private static readonly Color HeaderColor = new Color(0.80f, 0.66f, 0.34f, 1f);

        private static readonly List<string> CategoryChoices = new List<string>
        {
            "Все категории", "Бойцы", "Существа", "Командиры", "Прочие"
        };

        private static readonly List<string> StatusChoices = new List<string>
        {
            "Любой статус", "Готово", "Частично", "Без анимаций"
        };

        [SerializeField] private string selectedUnitId;
        [SerializeField] private CreatureAnimationAction selectedAction = CreatureAnimationAction.Idle;
        [SerializeField] private CreatureAnimationDirection selectedDirection = CreatureAnimationDirection.Front;

        private CreatureAnimationDatabaseAsset database;
        private UnitDatabaseAsset units;
        private readonly List<UnitDefinitionData> visibleUnits = new List<UnitDefinitionData>();

        private TextField searchField;
        private PopupField<string> categoryField;
        private PopupField<string> statusField;
        private ListView unitList;
        private Label titleLabel;
        private Label messageLabel;
        private CreatureAnimationPreviewElement preview;
        private VisualElement setPanel;
        private ScrollView detailPanel;
        private VisualElement reviewOverlay;
        private bool advancedOpen;
        private bool directionMapOpen;
        private bool pivotUndoGroupOpen;
        private int undoGroupIndex;
        private Slider pivotXSlider;
        private Slider pivotYSlider;

        private CreatureAnimationImportPackage pendingPackage;
        private CreatureAnimationImportMode pendingMode;

        [MenuItem(CreatureAnimationEditorData.AnimationWindowMenu, priority = 2000)]
        public static void OpenWindow()
        {
            Open(null);
        }

        public static CreatureAnimationDatabaseWindow Open(string unitId)
        {
            CreatureAnimationDatabaseWindow window = GetWindow<CreatureAnimationDatabaseWindow>();
            window.titleContent = new GUIContent("База анимаций");
            window.minSize = new Vector2(1100f, 640f);
            if (!string.IsNullOrWhiteSpace(unitId))
                window.SelectUnit(unitId);
            window.Show();
            window.Focus();
            return window;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnExternalChange;
            CreatureAnimationEditorData.DataChanged += OnExternalChange;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnExternalChange;
            CreatureAnimationEditorData.DataChanged -= OnExternalChange;
            CloseUndoGroup();
        }

        private void OnExternalChange()
        {
            if (rootVisualElement == null || unitList == null)
                return;
            RefreshUnitList();
            ShowSelection();
        }

        public void CreateGUI()
        {
            database = CreatureAnimationEditorData.LoadOrCreate();
            units = CreatureAnimationEditorData.LoadUnits();
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;

            if (units == null)
            {
                rootVisualElement.Add(new HelpBox("Не найдена База существ: " + CreatureAnimationEditorData.UnitDatabasePath, HelpBoxMessageType.Error));
                return;
            }

            BuildToolbar();

            TwoPaneSplitView split = new TwoPaneSplitView(0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            rootVisualElement.Add(split);
            split.Add(BuildListPane());
            split.Add(BuildMainPane());

            rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnWindowDragUpdated);
            rootVisualElement.RegisterCallback<DragPerformEvent>(OnWindowDragPerform);

            RefreshUnitList();
            if (string.IsNullOrEmpty(selectedUnitId) && visibleUnits.Count > 0)
                selectedUnitId = visibleUnits[0].Id;
            SelectUnit(selectedUnitId);
        }

        private void BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.style.height = 30f;
            AddToolbarButton(toolbar, "Загрузить папку", LoadFolder, "Папка существа, действия или ракурса из KS Sprite Renderer. Можно просто перетащить её в окно.");
            AddToolbarButton(toolbar, "Добавить кадры", () => LoadIntoSelectedCell(CreatureAnimationImportMode.Append), "Добавить кадры в конец выбранной ячейки: папка PNG или выделенные Sprite в окне Project.");
            AddToolbarButton(toolbar, "Заменить", () => LoadIntoSelectedCell(CreatureAnimationImportMode.Replace), "Заменить кадры выбранной ячейки: папка PNG или выделенные Sprite в окне Project.");
            AddToolbarButton(toolbar, "Удалить анимацию", DeleteSelectedAnimation, "Убирает назначение из базы. Картинки не удаляются.");
            AddToolbarButton(toolbar, "Сохранить", Save, "Сохранить Базу анимаций и Базу существ.");
            AddToolbarButton(toolbar, "Открыть в базе существ", OpenInUnitDatabase, "Карточка этого существа в Базе существ.");
            AddToolbarButton(toolbar, "Проверить базу", ValidateDatabase, "Ошибки данных и неполнота наборов.");

            ToolbarMenu more = new ToolbarMenu { text = "Ещё" };
            more.menu.AppendAction("Очистить неиспользуемые атласы…", _ => CleanUnusedAtlases());
            more.menu.AppendAction("Создать тестовые кадры…", _ => EditorApplication.ExecuteMenuItem("Kingdom Survival/База анимаций: создать тестовые кадры…"));
            more.menu.AppendAction("Показать папку атласов", _ =>
            {
                UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CreatureAnimationEditorData.ArtRoot);
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
            rootVisualElement.Add(toolbar);
        }

        private static void AddToolbarButton(Toolbar toolbar, string text, Action action, string tooltip)
        {
            ToolbarButton button = new ToolbarButton(action) { text = text, tooltip = tooltip };
            toolbar.Add(button);
        }

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;

            searchField = new TextField { label = "Поиск" };
            searchField.labelElement.style.minWidth = 70f;
            searchField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(searchField);
            categoryField = new PopupField<string>("Категория", CategoryChoices, 0);
            categoryField.labelElement.style.minWidth = 70f;
            categoryField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(categoryField);
            statusField = new PopupField<string>("Статус", StatusChoices, 0);
            statusField.labelElement.style.minWidth = 70f;
            statusField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(statusField);

            unitList = new ListView
            {
                fixedItemHeight = 46f,
                selectionType = SelectionType.Single,
                makeItem = MakeUnitItem,
                bindItem = BindUnitItem
            };
            unitList.style.flexGrow = 1f;
            unitList.style.marginTop = 6f;
            unitList.selectionChanged += _ =>
            {
                int index = unitList.selectedIndex;
                if (index >= 0 && index < visibleUnits.Count)
                    SelectUnit(visibleUnits[index].Id);
            };
            pane.Add(unitList);
            return pane;
        }

        private static VisualElement MakeUnitItem()
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

        // В списке — первый кадр или старая миниатюра: длинный список не
        // проигрывает анимации и не нагружает редактор.
        private void BindUnitItem(VisualElement element, int index)
        {
            if (index < 0 || index >= visibleUnits.Count)
                return;
            UnitDefinitionData unit = visibleUnits[index];
            CreatureAnimationSetData set = CreatureAnimationEditorData.FindSetOf(database, unit);
            CreatureAnimationSetStatus status = set != null ? set.Status : CreatureAnimationSetStatus.Empty;
            element.Q<Label>("title").text = string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.Id : unit.DisplayLabel;
            Label info = element.Q<Label>("info");
            info.text = unit.Id + " · " + CreatureAnimationLabels.StatusTitle(status);
            info.style.color = StatusColor(status);
            Sprite thumbnail = set != null ? set.FindFirstFrame() : null;
            element.Q<Image>("thumbnail").sprite = thumbnail != null ? thumbnail : unit.BattlefieldSprite != null ? unit.BattlefieldSprite : unit.Portrait;
        }

        private static Color StatusColor(CreatureAnimationSetStatus status)
        {
            switch (status)
            {
                case CreatureAnimationSetStatus.Ready: return ReadyColor;
                case CreatureAnimationSetStatus.Partial: return PartialColor;
                default: return EmptyColor;
            }
        }

        private VisualElement BuildMainPane()
        {
            VisualElement main = new VisualElement();
            main.style.flexGrow = 1f;
            main.style.flexDirection = FlexDirection.Row;
            main.style.position = Position.Relative;

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

            preview = new CreatureAnimationPreviewElement(340f) { AllowPivotEditing = true };
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

            reviewOverlay = new VisualElement();
            reviewOverlay.style.position = Position.Absolute;
            reviewOverlay.style.left = 0f;
            reviewOverlay.style.right = 0f;
            reviewOverlay.style.top = 0f;
            reviewOverlay.style.bottom = 0f;
            reviewOverlay.style.backgroundColor = new Color(0.10f, 0.10f, 0.11f, 0.97f);
            reviewOverlay.style.display = DisplayStyle.None;
            main.Add(reviewOverlay);
            return main;
        }

        private void RefreshUnitList()
        {
            if (unitList == null || units == null)
                return;
            string query = searchField != null ? searchField.value.Trim() : string.Empty;
            int category = categoryField != null ? categoryField.index : 0;
            int status = statusField != null ? statusField.index : 0;
            visibleUnits.Clear();
            foreach (UnitDefinitionData unit in units.Units)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                    continue;
                if (query.Length > 0 &&
                    unit.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (unit.DisplayLabel ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (category > 0 && (int)unit.Category != category - 1)
                    continue;
                if (status > 0)
                {
                    CreatureAnimationSetData set = CreatureAnimationEditorData.FindSetOf(database, unit);
                    CreatureAnimationSetStatus unitStatus = set != null ? set.Status : CreatureAnimationSetStatus.Empty;
                    CreatureAnimationSetStatus wanted = status == 1 ? CreatureAnimationSetStatus.Ready
                        : status == 2 ? CreatureAnimationSetStatus.Partial
                        : CreatureAnimationSetStatus.Empty;
                    if (unitStatus != wanted)
                        continue;
                }
                visibleUnits.Add(unit);
            }
            unitList.itemsSource = visibleUnits;
            unitList.Rebuild();
            int selectedIndex = visibleUnits.FindIndex(unit => unit.Id == selectedUnitId);
            if (selectedIndex >= 0)
                unitList.SetSelectionWithoutNotify(new[] { selectedIndex });
            else
                unitList.ClearSelection();
        }

        public void SelectUnit(string unitId)
        {
            if (units == null)
                return;
            if (selectedUnitId != unitId)
                CloseUndoGroup();
            selectedUnitId = unitId;
            if (unitList != null)
            {
                int index = visibleUnits.FindIndex(unit => unit.Id == unitId);
                if (index >= 0)
                {
                    unitList.SetSelectionWithoutNotify(new[] { index });
                    unitList.ScrollToItem(index);
                }
            }
            HideReview();
            ShowSelection();
        }

        private UnitDefinitionData SelectedUnit => units != null ? units.FindById(selectedUnitId) : null;
        private CreatureAnimationSetData SelectedSet => CreatureAnimationEditorData.FindSetOf(database, SelectedUnit);

        private void ShowSelection(bool updatePreviewSelection = true)
        {
            if (preview == null)
                return;
            UnitDefinitionData unit = SelectedUnit;
            CreatureAnimationSetData set = SelectedSet;
            titleLabel.text = unit == null
                ? "Выберите существо слева"
                : (string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.Id : unit.DisplayLabel) +
                  " · " + CreatureAnimationLabels.StatusTitle(set != null ? set.Status : CreatureAnimationSetStatus.Empty);
            preview.SetData(database, set, unit != null ? unit.BattlefieldSprite : null);
            if (updatePreviewSelection)
                preview.Select(selectedAction, selectedDirection);
            BuildSetPanel(unit, set);
            BuildDetailPanel(unit, set);
            unitList?.RefreshItems();
        }

        // Под предпросмотром — основные настройки набора: масштаб на поле и опора.
        private void BuildSetPanel(UnitDefinitionData unit, CreatureAnimationSetData set)
        {
            setPanel.Clear();
            if (unit == null)
                return;

            List<string> setIds = new List<string> { string.Empty };
            setIds.AddRange(database.Sets.Where(item => item != null).Select(item => item.Id));
            PopupField<string> setField = new PopupField<string>(
                "Набор",
                setIds,
                setIds.Contains(unit.AnimationSetId) ? unit.AnimationSetId : string.Empty,
                id => string.IsNullOrEmpty(id) ? "— нет —" : DescribeSet(id),
                id => string.IsNullOrEmpty(id) ? "— нет —" : DescribeSet(id));
            setField.tooltip = "Набор анимаций этого существа. Один набор можно дать нескольким существам.";
            setField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(units, "Назначить набор анимаций");
                CreatureAnimationEditorData.AssignSet(units, unit.Id, evt.newValue);
                ShowSelection();
            });
            setPanel.Add(setField);

            if (set == null)
            {
                setPanel.Add(new HelpBox(
                    "Анимаций нет — в бою статичная миниатюра или жетон. Перетащите сюда папку PNG из KS Sprite Renderer " +
                    "или нажмите «Загрузить папку»: набор создастся сам.",
                    HelpBoxMessageType.Info));
                Button create = new Button(() =>
                {
                    CreatureAnimationEditorData.CreateSetFor(database, units, unit);
                    ShowSelection();
                }) { text = "Создать пустой набор" };
                setPanel.Add(create);
                return;
            }

            Label users = new Label("Используют: " + CreatureAnimationEditorData.DescribeUsers(units, set.Id));
            users.style.whiteSpace = WhiteSpace.Normal;
            users.style.fontSize = 10f;
            users.style.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            setPanel.Add(users);

            // Ползунки меняют набор без перестройки панели: перетаскивание не
            // обрывается, просмотр обновляется сразу, весь жест — одно Undo.
            Slider scale = LiveSlider("Масштаб на поле", 0.1f, 10f, set.FieldScale,
                "Размер существа в бою. 1 — высота кадра равна прежней рамке миниатюры (1,35 гекса). " +
                "Масштаб просмотра колесом мыши сюда не влияет. Старый «Масштаб миниатюры» для существа с набором не применяется.",
                value => SelectedSet?.SetFieldScale(Mathf.Clamp(value, 0.1f, 10f)));
            setPanel.Add(scale);

            pivotXSlider = LiveSlider("Опора X", 0f, 1f, set.Pivot.x,
                "Точка опоры по горизонтали: доля ширины кадра слева. Обычно 0,5 — середина.",
                value => { CreatureAnimationSetData s = SelectedSet; s?.SetPivot(new Vector2(Mathf.Clamp01(value), s.Pivot.y)); });
            pivotYSlider = LiveSlider("Опора Y", 0f, 1f, set.Pivot.y,
                "Точка опоры по вертикали: доля высоты кадра снизу. Ставится на уровень ног — место контакта с землёй. " +
                "Красную точку можно тянуть и в просмотре.",
                value => { CreatureAnimationSetData s = SelectedSet; s?.SetPivot(new Vector2(s.Pivot.x, Mathf.Clamp01(value))); });
            setPanel.Add(pivotXSlider);
            setPanel.Add(pivotYSlider);

            CreatureAnimationSetData template = database.TemplateSet;
            bool isTemplate = template != null && ReferenceEquals(template, set);
            Label templateLabel = new Label(isTemplate
                ? "Образец для новых наборов: этот. Новые существа получат его опору и масштаб, новые действия — его скорость, цикл и «последний кадр»."
                : "Образец для новых наборов: " + (template != null ? "«" + template.DisplayName + "»" : "не выбран") + ".");
            templateLabel.style.whiteSpace = WhiteSpace.Normal;
            templateLabel.style.fontSize = 10f;
            templateLabel.style.marginTop = 4f;
            templateLabel.style.color = isTemplate ? ReadyColor : new Color(0.65f, 0.65f, 0.65f, 1f);
            setPanel.Add(templateLabel);
            if (!isTemplate)
            {
                Button makeTemplate = new Button(() =>
                {
                    Undo.RecordObject(database, "Образец для новых наборов");
                    database.SetTemplateSetId(set.Id);
                    EditorUtility.SetDirty(database);
                    ShowSelection();
                })
                {
                    text = "Сделать образцом для новых наборов",
                    tooltip = "Следующие существа сразу загрузятся с этой опорой и масштабом, а новые действия — с настройками этого набора."
                };
                setPanel.Add(makeTemplate);
            }

            Label canvas = new Label(set.CanvasSize.x > 0
                ? "Холст кадров: " + set.CanvasSize.x + "×" + set.CanvasSize.y + " пикс."
                : "Холст задаст первая загрузка.");
            canvas.style.fontSize = 10f;
            canvas.style.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            setPanel.Add(canvas);
        }

        private Slider LiveSlider(string label, float low, float high, float value, string tooltip, Action<float> apply)
        {
            Slider slider = new Slider(label, low, high) { value = value, showInputField = true, tooltip = tooltip };
            slider.style.marginTop = 2f;
            slider.labelElement.style.minWidth = 110f;
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

        private string DescribeSet(string setId)
        {
            CreatureAnimationSetData set = database.FindSet(setId);
            if (set == null)
                return setId;
            int users = CreatureAnimationEditorData.FindUsers(units, setId).Count;
            return set.DisplayName + " [" + set.Id + "] · " + CreatureAnimationLabels.StatusTitle(set.Status) +
                   (users > 1 ? " · у " + users + " существ" : string.Empty);
        }

        private void BuildDetailPanel(UnitDefinitionData unit, CreatureAnimationSetData set)
        {
            detailPanel.Clear();
            if (unit == null)
                return;

            AddHeader("ДЕЙСТВИЯ И РАКУРСЫ");
            detailPanel.Add(BuildTable(set));
            Label hint = new Label("Ячейка — число кадров. Щёлкните ячейку, чтобы смотреть её; перетащите на неё папку PNG или Sprite, чтобы загрузить кадры прямо туда.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            detailPanel.Add(hint);

            CreatureAnimationClipData clip = set?.FindClip(selectedAction);
            AddHeader(CreatureAnimationLabels.ActionTitle(selectedAction).ToUpperInvariant() + " → " +
                      CreatureAnimationLabels.DirectionTitle(selectedDirection).ToUpperInvariant());
            if (clip == null || !clip.HasAnyFrames)
            {
                detailPanel.Add(new Label("Кадров нет. " + CreatureAnimationEditorData.FallbackNote(selectedAction) + "."));
            }
            else
            {
                BuildClipSettings(set, clip);
            }

            BuildAdvanced(set, clip);
            BuildDiagnostics(unit, set);
            BuildDirectionMap();
        }

        private VisualElement BuildTable(CreatureAnimationSetData set)
        {
            VisualElement table = new VisualElement();
            VisualElement header = TableRow();
            header.Add(TableCellLabel(string.Empty, 118f));
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                Label label = TableCellLabel(
                    CreatureAnimationLabels.FacingArrow(database.GetFacing(direction)) + "\n" + CreatureAnimationLabels.DirectionTitle(direction),
                    70f);
                label.style.fontSize = 9f;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.tooltip = "Папка " + CreatureAnimationLabels.DirectionFolder(direction) + " · на поле смотрит " +
                                CreatureAnimationLabels.FacingTitle(database.GetFacing(direction));
                header.Add(label);
            }
            table.Add(header);

            foreach (CreatureAnimationAction action in CreatureAnimationLabels.Actions)
            {
                VisualElement row = TableRow();
                CreatureAnimationClipData clip = set?.FindClip(action);
                int present = clip != null ? clip.DirectionsWithFrames : 0;
                Label title = TableCellLabel(CreatureAnimationLabels.ActionTitle(action) + (present > 0 ? "  " + present + "/6" : string.Empty), 118f);
                title.style.unityTextAlign = TextAnchor.MiddleLeft;
                title.style.color = present == 6 ? ReadyColor : present > 0 ? PartialColor : EmptyColor;
                if (action == selectedAction)
                    title.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(title);

                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                {
                    CreatureAnimationFrames cell = clip?.FindDirection(direction);
                    int count = cell != null ? cell.FrameCount : 0;
                    bool broken = cell != null && Enumerable.Range(0, count).Any(i => cell.Frames[i] == null);
                    Label cellLabel = TableCellLabel(count > 0 ? count.ToString() : "—", 70f);
                    cellLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    cellLabel.style.backgroundColor = broken ? new Color(0.40f, 0.14f, 0.12f, 1f)
                        : count > 0 ? new Color(0.17f, 0.27f, 0.19f, 1f)
                        : new Color(0.17f, 0.17f, 0.18f, 1f);
                    bool selected = action == selectedAction && direction == selectedDirection;
                    SetBorder(cellLabel, selected ? HeaderColor : new Color(0.25f, 0.25f, 0.26f, 1f), selected ? 2f : 1f);
                    cellLabel.tooltip = CreatureAnimationLabels.ActionTitle(action) + " → " + CreatureAnimationLabels.DirectionTitle(direction) +
                                        (count > 0 ? ": " + count + " кадр(ов)" : ": нет кадров") +
                                        (broken ? "\nЕсть кадры без картинки (атлас удалён или не на этом компьютере)." : string.Empty);
                    CreatureAnimationAction capturedAction = action;
                    CreatureAnimationDirection capturedDirection = direction;
                    cellLabel.RegisterCallback<PointerDownEvent>(_ =>
                    {
                        selectedAction = capturedAction;
                        selectedDirection = capturedDirection;
                        ShowSelection();
                    });
                    cellLabel.RegisterCallback<DragUpdatedEvent>(evt =>
                    {
                        DragAndDrop.visualMode = HasDroppableContent() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                        evt.StopPropagation();
                    });
                    cellLabel.RegisterCallback<DragPerformEvent>(evt =>
                    {
                        DragAndDrop.AcceptDrag();
                        selectedAction = capturedAction;
                        selectedDirection = capturedDirection;
                        DropIntoCell(capturedAction, capturedDirection);
                        evt.StopPropagation();
                    });
                    row.Add(cellLabel);
                }
                table.Add(row);
            }
            return table;
        }

        private void BuildClipSettings(CreatureAnimationSetData set, CreatureAnimationClipData clip)
        {
            FloatField fps = new FloatField("Кадров в секунду") { value = clip.RawFramesPerSecond, isDelayed = true };
            fps.tooltip = "Скорость клипа. 12 — удобное начальное значение, не восстановленный FPS Blender. Меняется без повторной загрузки PNG.";
            fps.RegisterValueChangedCallback(evt =>
                ModifyClip("Скорость анимации", c => c.SetFramesPerSecond(Mathf.Clamp(evt.newValue, CreatureAnimationClipData.MinFramesPerSecond, CreatureAnimationClipData.MaxFramesPerSecond))));
            detailPanel.Add(fps);

            CreatureAnimationClip resolved = CreatureAnimationResolver.ResolveDirection(selectedAction, selectedDirection, clip);
            string duration = resolved != null
                ? "Длительность: " + resolved.Duration.ToString("0.00") + " с · " + resolved.FrameCount + " кадр(ов)" +
                  (resolved.IsDirectionSubstitute ? " (ракурс «" + CreatureAnimationLabels.DirectionTitle(resolved.Direction) + "»)" : string.Empty) +
                  (clip.UseSourceTiming ? " · исходный темп" : string.Empty)
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
            playback.tooltip = "Ожидание и ходьба — цикл; остальные — один раз; смерть держит последний кадр.";
            playback.RegisterValueChangedCallback(evt => ModifyClip("Воспроизведение", c => c.SetPlayback(evt.newValue)));
            detailPanel.Add(playback);

            Toggle skipLast = new Toggle("Не учитывать последний кадр") { value = clip.SkipLastFrame };
            skipLast.tooltip = "Для цикла, у которого последний кадр экспорта повторяет первый (KS Sprite Renderer всегда добавляет последний кадр). " +
                               "Без этого цикл на мгновение «залипает» на стыке. Действует на все ракурсы действия; файлы не меняются.";
            skipLast.RegisterValueChangedCallback(evt => ModifyClip("Последний кадр", c => c.SetSkipLastFrame(evt.newValue)));
            detailPanel.Add(skipLast);
        }

        private void BuildAdvanced(CreatureAnimationSetData set, CreatureAnimationClipData clip)
        {
            Foldout advanced = new Foldout { text = "Дополнительно", value = advancedOpen };
            advanced.RegisterValueChangedCallback(evt => { if (evt.target == advanced) advancedOpen = evt.newValue; });
            advanced.style.marginTop = 8f;
            detailPanel.Add(advanced);
            if (set == null)
            {
                advanced.Add(new Label("Набора нет."));
                return;
            }

            if (clip != null && clip.HasAnyFrames && CreatureAnimationLabels.HasImpactMarker(clip.Action))
            {
                Slider impact = new Slider("Момент удара", 0f, 1f) { value = clip.ImpactTime, showInputField = true };
                impact.tooltip = "Когда в бою показывается попадание (или выпуск снаряда): доля длительности клипа. " +
                                 "Маркер не считает урон — исход уже определён моделью боя.";
                CreatureAnimationClip resolved = CreatureAnimationResolver.ResolveDirection(clip.Action, selectedDirection, clip);
                Label impactFrame = new Label(ImpactFrameText(resolved, clip.ImpactTime));
                impactFrame.style.fontSize = 10f;
                impact.RegisterValueChangedCallback(evt =>
                {
                    impactFrame.text = ImpactFrameText(resolved, evt.newValue);
                    ModifyClip("Момент удара", c => c.SetImpactTime(Mathf.Clamp01(evt.newValue)), false);
                });
                advanced.Add(impact);
                advanced.Add(impactFrame);
            }

            if (clip != null && clip.HasAnyFrames)
            {
                Toggle sourceTiming = new Toggle("Исходный темп Blender") { value = clip.UseSourceTiming };
                sourceTiming.tooltip = "Длительность кадра — по разнице номеров PNG (0001 → 0004 = три исходных кадра), делённой на FPS Blender. " +
                                       "Нужен, чтобы экспорт со Step не ускорял движение. Скорость «Кадров в секунду» тогда не используется.";
                sourceTiming.RegisterValueChangedCallback(evt => ModifyClip("Исходный темп", c => c.SetUseSourceTiming(evt.newValue)));
                advanced.Add(sourceTiming);
                FloatField sourceFps = new FloatField("FPS сцены Blender") { value = clip.RawSourceFramesPerSecond, isDelayed = true };
                sourceFps.SetEnabled(clip.UseSourceTiming);
                sourceFps.RegisterValueChangedCallback(evt => ModifyClip("FPS Blender", c => c.SetSourceFramesPerSecond(Mathf.Clamp(evt.newValue, 1f, 240f))));
                advanced.Add(sourceFps);

                CreatureAnimationFrames cell = clip.FindDirection(selectedDirection);
                if (cell != null)
                {
                    Vector2Field offset = new Vector2Field("Поправка ракурса") { value = cell.Offset };
                    offset.tooltip = "Частное смещение только этого ракурса, доли холста. Обычно не нужно: общая опора задаётся у набора.";
                    offset.RegisterValueChangedCallback(evt => ModifySet("Поправка ракурса", s =>
                        s.FindFrames(selectedAction, selectedDirection)?.SetOffset(evt.newValue)));
                    advanced.Add(offset);
                }
            }

            TextField name = new TextField("Название набора") { value = set.RawDisplayName, isDelayed = true };
            name.RegisterValueChangedCallback(evt => ModifySet("Название набора", s => s.SetDisplayName(evt.newValue)));
            advanced.Add(name);
            Label id = new Label("ID набора: " + set.Id + " (стабильный; существа ссылаются на него, а не на название)");
            id.style.fontSize = 10f;
            id.style.whiteSpace = WhiteSpace.Normal;
            advanced.Add(id);
            Button removeSet = new Button(() => RemoveSet(set)) { text = "Удалить набор из базы" };
            removeSet.tooltip = "Убирает набор и ссылки на него. Атласы на диске остаются.";
            advanced.Add(removeSet);
        }

        private static string ImpactFrameText(CreatureAnimationClip clip, float normalized)
        {
            if (clip == null || clip.FrameCount == 0)
                return string.Empty;
            float seconds = clip.Duration * Mathf.Clamp01(normalized);
            return "≈ кадр " + (clip.GetFrameIndex(seconds) + 1) + " из " + clip.FrameCount + ", " + seconds.ToString("0.00") + " с";
        }

        private void BuildDiagnostics(UnitDefinitionData unit, CreatureAnimationSetData set)
        {
            AddHeader("ДИАГНОСТИКА");
            if (set == null)
            {
                detailPanel.Add(new Label("Набора нет: бой показывает статичную миниатюру или жетон."));
                return;
            }
            List<string> errors = CreatureAnimationEditorData.CollectDataErrors(database, units)
                .Where(error => error.StartsWith(set.DisplayName, StringComparison.Ordinal) || error.StartsWith(unit.Id, StringComparison.Ordinal))
                .ToList();
            foreach (string error in errors)
                detailPanel.Add(DiagnosticLine(error, ErrorColor));
            List<string> warnings = CreatureAnimationEditorData.CollectContentWarnings(set, unit);
            foreach (string warning in warnings)
                detailPanel.Add(DiagnosticLine(warning, PartialColor));
            if (errors.Count == 0 && warnings.Count == 0)
                detailPanel.Add(DiagnosticLine("Набор полный, ошибок нет.", ReadyColor));
        }

        private void BuildDirectionMap()
        {
            Foldout map = new Foldout { text = "Таблица ракурсов (общая для всех наборов)", value = directionMapOpen };
            map.RegisterValueChangedCallback(evt => { if (evt.target == map) directionMapOpen = evt.newValue; });
            map.style.marginTop = 10f;
            Label note = new Label("Камера KS Sprite Renderer повёрнута на 30°: каждый ракурс смотрит на один из шести соседних гексов. " +
                                   "Здесь указано, куда на поле смотрит каждая папка. Проверьте на первом настоящем существе: стрелка в просмотре " +
                                   "должна совпадать с взглядом персонажа.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 10f;
            map.Add(note);
            List<HexFacing> facings = Enum.GetValues(typeof(HexFacing)).Cast<HexFacing>().ToList();
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                PopupField<HexFacing> field = new PopupField<HexFacing>(
                    CreatureAnimationLabels.DirectionFolder(direction),
                    facings,
                    database.GetFacing(direction),
                    facing => CreatureAnimationLabels.FacingArrow(facing) + " " + CreatureAnimationLabels.FacingTitle(facing),
                    facing => CreatureAnimationLabels.FacingArrow(facing) + " " + CreatureAnimationLabels.FacingTitle(facing));
                CreatureAnimationDirection captured = direction;
                field.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(database, "Таблица ракурсов");
                    database.AssignFacing(captured, evt.newValue);
                    EditorUtility.SetDirty(database);
                    ShowSelection();
                });
                map.Add(field);
            }
            Button reset = new Button(() =>
            {
                Undo.RecordObject(database, "Таблица ракурсов по умолчанию");
                database.ResetDirectionMap();
                EditorUtility.SetDirty(database);
                ShowSelection();
            }) { text = "Вернуть таблицу по умолчанию" };
            map.Add(reset);
            detailPanel.Add(map);
        }

        private void ModifySet(string undoName, Action<CreatureAnimationSetData> change)
        {
            CreatureAnimationSetData set = SelectedSet;
            if (set == null)
                return;
            Undo.RecordObject(database, undoName);
            change(set);
            EditorUtility.SetDirty(database);
            ShowSelection();
        }

        private void ModifyClip(string undoName, Action<CreatureAnimationClipData> change, bool rebuildPanel = true)
        {
            CreatureAnimationClipData clip = SelectedSet?.FindClip(selectedAction);
            if (clip == null)
                return;
            Undo.RecordObject(database, undoName);
            change(clip);
            EditorUtility.SetDirty(database);
            if (rebuildPanel)
                ShowSelection();
            else
                preview.Rebuild();
        }

        // Одно перетаскивание опоры — одно действие Undo; ползунки следуют за точкой.
        private void OnPivotDragged(Vector2 pivot, bool finished)
        {
            CreatureAnimationSetData set = SelectedSet;
            if (set == null)
                return;
            OpenUndoGroup("Точка опоры");
            Undo.RecordObject(database, "Точка опоры");
            set.SetPivot(pivot);
            EditorUtility.SetDirty(database);
            pivotXSlider?.SetValueWithoutNotify(pivot.x);
            pivotYSlider?.SetValueWithoutNotify(pivot.y);
            preview.Rebuild();
            if (finished)
                CloseUndoGroup();
        }

        private void OpenUndoGroup(string name)
        {
            if (pivotUndoGroupOpen)
                return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            undoGroupIndex = Undo.GetCurrentGroup();
            pivotUndoGroupOpen = true;
        }

        private void CloseUndoGroup()
        {
            if (!pivotUndoGroupOpen)
                return;
            pivotUndoGroupOpen = false;
            Undo.CollapseUndoOperations(undoGroupIndex);
            Undo.IncrementCurrentGroup();
        }

        // ------------------------------------------------------------------
        // Загрузка
        // ------------------------------------------------------------------

        private void LoadFolder()
        {
            if (SelectedUnit == null)
            {
                ShowMessage("Сначала выберите существо слева.", ErrorColor);
                return;
            }
            string folder = EditorUtility.OpenFolderPanel("Папка PNG из KS Sprite Renderer", SessionState.GetString("KS.AnimationDatabase.LastFolder", string.Empty), string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;
            SessionState.SetString("KS.AnimationDatabase.LastFolder", Path.GetDirectoryName(folder) ?? string.Empty);
            AnalyzeFolder(folder);
        }

        private void AnalyzeFolder(string folder)
        {
            string[] files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(folder, files);
            BeginImport(package, CreatureAnimationImportMode.Replace);
        }

        private void LoadIntoSelectedCell(CreatureAnimationImportMode mode)
        {
            if (SelectedUnit == null)
            {
                ShowMessage("Сначала выберите существо слева.", ErrorColor);
                return;
            }
            List<Sprite> sprites = SpritesFrom(Selection.objects);
            if (sprites.Count > 0)
            {
                ApplySpritesToCell(sprites, mode);
                return;
            }
            string folder = EditorUtility.OpenFolderPanel(
                (mode == CreatureAnimationImportMode.Append ? "Добавить кадры: " : "Заменить кадры: ") +
                CreatureAnimationLabels.ActionTitle(selectedAction) + " → " + CreatureAnimationLabels.DirectionTitle(selectedDirection),
                SessionState.GetString("KS.AnimationDatabase.LastFolder", string.Empty),
                string.Empty);
            if (string.IsNullOrEmpty(folder))
                return;
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.AnalyzeSequence(
                Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly),
                selectedAction,
                selectedDirection);
            BeginImport(package, mode);
        }

        private void ApplySpritesToCell(List<Sprite> sprites, CreatureAnimationImportMode mode)
        {
            CreatureAnimationSetData set = EnsureSet();
            if (set == null)
                return;
            if (mode == CreatureAnimationImportMode.Replace &&
                (set.FindFrames(selectedAction, selectedDirection)?.FrameCount ?? 0) > 0 &&
                !EditorUtility.DisplayDialog("Заменить кадры?",
                    CreatureAnimationLabels.ActionTitle(selectedAction) + " → " + CreatureAnimationLabels.DirectionTitle(selectedDirection) +
                    ": текущие кадры будут заменены " + sprites.Count + " спрайтами из проекта.", "Заменить", "Отмена"))
            {
                return;
            }
            bool applied = CreatureAnimationImporter.ApplySprites(database, set, selectedAction, string.Empty, selectedDirection, sprites, mode, out string message);
            ShowMessage(message, applied ? ReadyColor : ErrorColor);
            ShowSelection();
        }

        private CreatureAnimationSetData EnsureSet()
        {
            UnitDefinitionData unit = SelectedUnit;
            if (unit == null)
                return null;
            return SelectedSet ?? CreatureAnimationEditorData.CreateSetFor(database, units, unit);
        }

        // Новая загрузка без предупреждений и замен применяется сразу;
        // иначе — сводка с подтверждением.
        private void BeginImport(CreatureAnimationImportPackage package, CreatureAnimationImportMode mode)
        {
            pendingPackage = package;
            pendingMode = mode;
            CreatureAnimationSetData set = SelectedSet;
            List<CreatureAnimationImportChange> changes = CreatureAnimationImporter.DescribeChanges(package, set);
            bool quiet = !package.HasErrors && package.Issues.Count == 0 && changes.Count > 0 &&
                         (mode == CreatureAnimationImportMode.Append || changes.All(change => !change.Replaces));
            if (quiet)
                ApplyPending();
            else
                ShowReview();
        }

        private void ApplyPending()
        {
            if (pendingPackage == null)
                return;
            CreatureAnimationSetData current = SelectedSet;
            List<CreatureAnimationImportChange> replaced = CreatureAnimationImporter.DescribeChanges(pendingPackage, current)
                .Where(change => change.Replaces)
                .ToList();
            if (pendingMode == CreatureAnimationImportMode.Replace && replaced.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "Заменить кадры?",
                    "Будут заменены назначения:\n" + string.Join("\n", replaced.Take(14).Select(change => change.Describe(pendingMode))) +
                    (replaced.Count > 14 ? "\n…и ещё " + (replaced.Count - 14) : string.Empty),
                    "Заменить",
                    "Отмена"))
            {
                return;
            }

            CreatureAnimationSetData set = EnsureSet();
            if (set == null)
                return;
            if (CreatureAnimationImporter.Apply(database, set, pendingPackage, pendingMode, out string report))
            {
                pendingPackage = null;
                HideReview();
                ShowMessage(report.Split('\n')[0], ReadyColor);
                Debug.Log("База анимаций · " + report);
                RefreshUnitList();
                ShowSelection();
            }
            else
            {
                ShowReview();
                ShowMessage("Загрузка не применена: есть ошибки. Набор не изменён.", ErrorColor);
            }
        }

        private void ShowReview()
        {
            reviewOverlay.Clear();
            reviewOverlay.style.display = DisplayStyle.Flex;
            ScrollView content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1f;
            content.style.paddingLeft = 16f;
            content.style.paddingRight = 16f;
            content.style.paddingTop = 12f;
            reviewOverlay.Add(content);

            UnitDefinitionData unit = SelectedUnit;
            CreatureAnimationSetData set = SelectedSet;
            Label title = new Label("ЗАГРУЗКА АНИМАЦИЙ · " + (unit != null ? unit.DisplayLabel : "?") +
                                    (set != null ? " · набор «" + set.DisplayName + "»" : " · новый набор"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14f;
            title.style.color = HeaderColor;
            content.Add(title);
            content.Add(new Label("Источник: " + pendingPackage.RootPath));
            content.Add(new Label("Файлов: " + pendingPackage.FileCount + (pendingMode == CreatureAnimationImportMode.Append ? " · режим: добавить в конец" : string.Empty)));

            AddReviewHeader(content, "ДЕЙСТВИЯ");
            List<CreatureAnimationAction?> options = new List<CreatureAnimationAction?> { null };
            options.AddRange(CreatureAnimationLabels.Actions.Select(action => (CreatureAnimationAction?)action));
            foreach (CreatureAnimationImportGroup group in pendingPackage.Groups)
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                string directions = string.Join(", ", group.Cells.Values.Select(cell =>
                    CreatureAnimationLabels.DirectionTitle(cell.Direction) + " " + cell.Frames.Count));
                Label name = new Label("«" + group.RawName + "»" + (string.IsNullOrEmpty(group.ClipKey) ? string.Empty : " [ключ " + group.ClipKey + "]"));
                name.style.width = 170f;
                row.Add(name);
                PopupField<CreatureAnimationAction?> mapping = new PopupField<CreatureAnimationAction?>(
                    options,
                    group.ChosenAction,
                    value => value.HasValue ? CreatureAnimationLabels.ActionTitle(value.Value) : "Не загружать",
                    value => value.HasValue ? CreatureAnimationLabels.ActionTitle(value.Value) : "Не загружать");
                mapping.style.width = 170f;
                mapping.SetEnabled(group.Match != CreatureAnimationActionMatch.Exact);
                CreatureAnimationImportGroup captured = group;
                mapping.RegisterValueChangedCallback(evt =>
                {
                    captured.ChosenAction = evt.newValue;
                    CreatureAnimationImportParser.ValidateDuplicateTargets(pendingPackage);
                    ShowReview();
                });
                row.Add(mapping);
                Label detail = new Label(directions);
                detail.style.fontSize = 10f;
                detail.style.whiteSpace = WhiteSpace.Normal;
                detail.style.flexShrink = 1f;
                detail.style.marginLeft = 8f;
                if (group.Match == CreatureAnimationActionMatch.Suggested)
                    name.style.color = PartialColor;
                else if (group.Match == CreatureAnimationActionMatch.Unknown)
                    name.style.color = ErrorColor;
                row.Add(detail);
                content.Add(row);
            }

            List<CreatureAnimationImportChange> changes = CreatureAnimationImporter.DescribeChanges(pendingPackage, set);
            AddReviewHeader(content, "ЧТО ИЗМЕНИТСЯ");
            if (changes.Count == 0)
                content.Add(new Label("Ничего: ни одна папка не выбрана для загрузки."));
            foreach (CreatureAnimationImportChange change in changes)
            {
                Label line = new Label((change.Replaces ? "↻ " : "+ ") + change.Describe(pendingMode));
                line.style.color = change.Replaces ? PartialColor : ReadyColor;
                content.Add(line);
            }

            if (pendingPackage.Issues.Count > 0)
            {
                AddReviewHeader(content, "ЗАМЕЧАНИЯ");
                foreach (CreatureAnimationImportIssue issue in pendingPackage.Issues)
                {
                    Label line = new Label(issue.ToString());
                    line.style.whiteSpace = WhiteSpace.Normal;
                    line.style.color = issue.Severity == CreatureAnimationImportSeverity.Error ? ErrorColor : PartialColor;
                    content.Add(line);
                }
            }

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 12f;
            buttons.style.marginBottom = 16f;
            Button apply = new Button(ApplyPending) { text = changes.Any(change => change.Replaces) && pendingMode == CreatureAnimationImportMode.Replace ? "Применить с заменой" : "Применить" };
            apply.style.height = 30f;
            apply.style.width = 200f;
            apply.SetEnabled(!pendingPackage.HasErrors && changes.Count > 0);
            Button cancel = new Button(() => { pendingPackage = null; HideReview(); ShowMessage("Загрузка отменена. Набор не изменён.", EmptyColor); }) { text = "Отмена" };
            cancel.style.height = 30f;
            cancel.style.width = 120f;
            buttons.Add(apply);
            buttons.Add(cancel);
            content.Add(buttons);
            if (pendingPackage.HasErrors)
                content.Add(new Label("Есть ошибки: исправьте файлы и загрузите папку снова. Действующий набор не изменён."));
        }

        private static void AddReviewHeader(VisualElement parent, string text)
        {
            Label header = new Label(text);
            header.style.marginTop = 10f;
            header.style.marginBottom = 3f;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = HeaderColor;
            parent.Add(header);
        }

        private void HideReview()
        {
            if (reviewOverlay == null)
                return;
            reviewOverlay.Clear();
            reviewOverlay.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------
        // Перетаскивание
        // ------------------------------------------------------------------

        private static bool HasDroppableContent()
        {
            return (DragAndDrop.paths != null && DragAndDrop.paths.Any(path => Directory.Exists(path) || CreatureAnimationImportParser.IsImageFile(path))) ||
                   SpritesFrom(DragAndDrop.objectReferences).Count > 0;
        }

        private void OnWindowDragUpdated(DragUpdatedEvent evt)
        {
            DragAndDrop.visualMode = SelectedUnit != null && HasDroppableContent()
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
        }

        // Папка, брошенная в окно, разбирается как папка существа.
        private void OnWindowDragPerform(DragPerformEvent evt)
        {
            if (SelectedUnit == null)
                return;
            DragAndDrop.AcceptDrag();
            List<Sprite> sprites = SpritesFrom(DragAndDrop.objectReferences);
            string[] paths = DragAndDrop.paths ?? Array.Empty<string>();
            string externalFolder = paths.FirstOrDefault(Directory.Exists);
            if (externalFolder != null && sprites.Count == 0)
            {
                AnalyzeFolder(ToAbsolutePath(externalFolder));
                return;
            }
            List<string> files = paths.Where(CreatureAnimationImportParser.IsImageFile).Select(ToAbsolutePath).ToList();
            if (sprites.Count == 0 && files.Count > 0)
            {
                string root = Path.GetDirectoryName(files[0]);
                BeginImport(CreatureAnimationImportParser.Analyze(root, files), CreatureAnimationImportMode.Replace);
                return;
            }
            if (sprites.Count > 0)
                ApplySpritesToCell(sprites, CreatureAnimationImportMode.Replace);
        }

        private void DropIntoCell(CreatureAnimationAction action, CreatureAnimationDirection direction)
        {
            List<Sprite> sprites = SpritesFrom(DragAndDrop.objectReferences);
            if (sprites.Count > 0)
            {
                ApplySpritesToCell(sprites, CreatureAnimationImportMode.Replace);
                return;
            }
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
                return;
            BeginImport(CreatureAnimationImportParser.AnalyzeSequence(files, action, direction), CreatureAnimationImportMode.Replace);
        }

        // Пути из окна Project — относительные («Assets/…»).
        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return CreatureAnimationAtlasBuilder.ToAbsolute(path);
        }

        private static List<Sprite> SpritesFrom(IEnumerable<UnityEngine.Object> objects)
        {
            List<Sprite> sprites = new List<Sprite>();
            foreach (UnityEngine.Object item in objects ?? Array.Empty<UnityEngine.Object>())
            {
                if (item is Sprite sprite)
                {
                    sprites.Add(sprite);
                }
                else if (item is Texture2D texture)
                {
                    string path = AssetDatabase.GetAssetPath(texture);
                    sprites.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase));
                }
            }
            return sprites.Distinct().ToList();
        }

        // ------------------------------------------------------------------
        // Прочие команды
        // ------------------------------------------------------------------

        private void DeleteSelectedAnimation()
        {
            CreatureAnimationSetData set = SelectedSet;
            CreatureAnimationClipData clip = set?.FindClip(selectedAction);
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
            Undo.RecordObject(database, "Удалить анимацию");
            if (choice == 0)
            {
                clip.RemoveDirection(selectedDirection);
                if (!clip.HasAnyFrames)
                    set.RemoveClip(selectedAction);
            }
            else
            {
                set.RemoveClip(selectedAction);
            }
            EditorUtility.SetDirty(database);
            ShowMessage("Назначение удалено.", EmptyColor);
            ShowSelection();
        }

        private void RemoveSet(CreatureAnimationSetData set)
        {
            List<UnitDefinitionData> users = CreatureAnimationEditorData.FindUsers(units, set.Id);
            if (!EditorUtility.DisplayDialog(
                    "Удалить набор?",
                    "Набор «" + set.DisplayName + "» будет убран из базы" +
                    (users.Count > 0 ? ", ссылки у существ очищены: " + string.Join(", ", users.Select(u => u.DisplayLabel)) : string.Empty) +
                    ". Атласы на диске останутся.",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }
            Undo.RecordObjects(new UnityEngine.Object[] { database, units }, "Удалить набор анимаций");
            foreach (UnitDefinitionData user in users)
                CreatureAnimationEditorData.AssignSet(units, user.Id, string.Empty);
            database.RemoveSet(set.Id);
            EditorUtility.SetDirty(database);
            RefreshUnitList();
            ShowSelection();
        }

        private void Save()
        {
            AssetDatabase.SaveAssetIfDirty(database);
            if (units != null)
                AssetDatabase.SaveAssetIfDirty(units);
            ShowMessage("Сохранено.", ReadyColor);
        }

        private void OpenInUnitDatabase()
        {
            if (SelectedUnit == null)
                return;
            SessionState.SetString(PendingUnitSelectionKey, SelectedUnit.Id);
            EditorApplication.ExecuteMenuItem(CreatureAnimationEditorData.UnitWindowMenu);
        }

        private void ValidateDatabase()
        {
            List<string> errors = CreatureAnimationEditorData.CollectDataErrors(database, units);
            int partial = 0;
            foreach (UnitDefinitionData unit in units.Units)
            {
                CreatureAnimationSetData set = CreatureAnimationEditorData.FindSetOf(database, unit);
                if (set != null && set.Status == CreatureAnimationSetStatus.Partial)
                    partial++;
            }
            string summary = errors.Count == 0
                ? "Ошибок данных нет." + (partial > 0 ? "\nНаборов с неполным контентом: " + partial + " (бой работает через подстановки)." : string.Empty)
                : string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? "\n…и ещё " + (errors.Count - 20) : string.Empty);
            ShowMessage(errors.Count == 0 ? "Ошибок нет" : "Ошибок: " + errors.Count, errors.Count == 0 ? ReadyColor : ErrorColor);
            EditorUtility.DisplayDialog("Проверка Базы анимаций", summary, "Закрыть");
        }

        private void CleanUnusedAtlases()
        {
            List<string> unused = CreatureAnimationImporter.FindUnusedAtlases(database);
            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Атласы", "Неиспользуемых атласов нет.", "Хорошо");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Очистить неиспользуемые атласы?",
                    "Эти атласы не нужны ни одному набору (остались от прежних загрузок):\n" +
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
