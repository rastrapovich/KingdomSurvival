using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Окно «База полей боя»: список полей, большой предпросмотр в кадре боя
    // (16:9, как в игре) и компактная правая колонка с настройками.
    public sealed partial class BattlefieldDatabaseWindow : EditorWindow
    {
        private const string AssetPath = "Assets/_Project/BattlefieldDatabase/Resources/BattlefieldDatabase/KingdomSurvivalBattlefields.asset";
        internal const string BackgroundFolder = "Assets/_Project/Art/Battlefields";
        internal const string HexImageFolder = "Assets/_Project/Art/Battlefields/Hexes";

        private static readonly Color PaneBackground = new Color(0.135f, 0.14f, 0.155f, 1f);
        private static readonly Color CardBackground = new Color(0.20f, 0.205f, 0.225f, 1f);
        private static readonly Color CardBorder = new Color(0.10f, 0.10f, 0.11f, 1f);
        private static readonly Color MutedText = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color FieldAccent = new Color(0.86f, 0.70f, 0.38f, 1f);
        private static readonly Color BackgroundAccent = new Color(0.45f, 0.75f, 0.50f, 1f);
        private static readonly Color GridAccent = new Color(0.40f, 0.62f, 0.90f, 1f);
        private static readonly Color HexAccent = new Color(0.68f, 0.52f, 0.88f, 1f);
        private static readonly Color WarningColor = new Color(0.90f, 0.48f, 0.38f, 1f);
        private static readonly Color OkColor = new Color(0.42f, 0.72f, 0.45f, 1f);

        [SerializeField] private int selectedIndex = -1;
        [SerializeField] private bool showStateSamples;

        private BattlefieldDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private SerializedProperty fieldsProperty;
        private SerializedProperty tagsProperty;
        private SerializedProperty sandboxIdProperty;
        private readonly List<int> visibleIndices = new List<int>();
        private ListView list;
        private VisualElement main;
        private Label emptyHint;
        private Label validation;
        private VisualElement settings;
        private bool refreshScheduled;

        [MenuItem("Kingdom Survival/База полей боя")]
        public static void OpenWindow()
        {
            BattlefieldDatabaseWindow window = GetWindow<BattlefieldDatabaseWindow>();
            window.titleContent = new GUIContent("База полей боя");
            window.minSize = new Vector2(900f, 560f);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            if (serializedDatabase == null || database == null)
                return;
            serializedDatabase.Update();
            RefreshList();
            ShowSelected();
        }

        public void CreateGUI()
        {
            database = AssetDatabase.LoadAssetAtPath<BattlefieldDatabaseAsset>(AssetPath);
            rootVisualElement.Clear();
            if (database == null)
            {
                rootVisualElement.Add(new HelpBox("Не найдена база полей: " + AssetPath, HelpBoxMessageType.Error));
                return;
            }

            serializedDatabase = new SerializedObject(database);
            fieldsProperty = serializedDatabase.FindProperty("battlefields");
            tagsProperty = serializedDatabase.FindProperty("tags");
            sandboxIdProperty = serializedDatabase.FindProperty("sandboxBattlefieldId");
            BuildToolbar();

            TwoPaneSplitView split = new TwoPaneSplitView(0, 250f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(BuildListPane());
            split.Add(BuildMainPane());
            rootVisualElement.Add(split);
            rootVisualElement.TrackSerializedObjectValue(serializedDatabase, _ => ScheduleRefresh());
            RefreshList();
            RestoreSelection();
        }

        private void BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 36f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            toolbar.style.borderBottomWidth = 1f;
            toolbar.style.borderBottomColor = CardBorder;
            AddToolbarButton(toolbar, "+ Поле", () => AddField(null, null));
            AddToolbarButton(toolbar, "Дублировать", DuplicateField);
            AddToolbarButton(toolbar, "Удалить", DeleteField);
            validation = new Label();
            validation.style.flexGrow = 1f;
            validation.style.unityTextAlign = TextAnchor.MiddleRight;
            validation.style.marginRight = 6f;
            toolbar.Add(validation);
            AddToolbarButton(toolbar, "Проверить базу", ValidateDatabase);
            rootVisualElement.Add(toolbar);
        }

        private static void AddToolbarButton(VisualElement parent, string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 24f;
            button.style.marginRight = 4f;
            parent.Add(button);
        }

        // ── Список ─────────────────────────────────────────────────────────

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;
            pane.style.paddingBottom = 6f;

            ToolbarSearchField searchField = new ToolbarSearchField();
            searchField.style.width = StyleKeyword.Auto;
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue ?? string.Empty;
                RefreshList();
            });
            pane.Add(searchField);

            list = new ListView();
            list.style.flexGrow = 1f;
            list.style.marginTop = 6f;
            list.fixedItemHeight = 58f;
            list.selectionType = SelectionType.Single;
            list.makeItem = MakeListItem;
            list.bindItem = BindListItem;
            list.selectionChanged += _ => SelectVisible(list.selectedIndex);
            pane.Add(list);

            Label hint = new Label("Перетащите картинки сюда — появятся новые поля.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = MutedText;
            hint.style.marginTop = 4f;
            pane.Add(hint);

            RegisterImageDrop(pane, BackgroundFolder, true, sprites =>
            {
                foreach (Sprite sprite in sprites)
                    AddField(sprite, sprite.name);
            });
            return pane;
        }

        private string searchText = string.Empty;

        private static VisualElement MakeListItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 2f;
            Image image = new Image { name = "image", scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.style.width = 80f;
            image.style.height = 45f;
            image.style.marginRight = 8f;
            image.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 1f);
            row.Add(image);
            VisualElement labels = new VisualElement();
            labels.style.flexGrow = 1f;
            labels.style.flexShrink = 1f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            Label info = new Label { name = "info" };
            info.style.fontSize = 10f;
            info.style.color = MutedText;
            labels.Add(title);
            labels.Add(info);
            row.Add(labels);
            return row;
        }

        private void BindListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleIndices.Count)
                return;
            BattlefieldDefinitionData field = database.Battlefields[visibleIndices[visibleIndex]];
            bool active = field.Id == database.SandboxBattlefieldId;
            Label title = row.Q<Label>("title");
            title.text = field.DisplayLabel;
            title.style.color = active ? FieldAccent : StyleKeyword.Null;
            row.Q<Label>("info").text = (active ? "● в бою · " : string.Empty) +
                                        BattlefieldFrame.CountActiveCells(field) + " / 58 гексов";
            row.Q<Image>("image").sprite = field.Background;
        }

        private void RefreshList()
        {
            if (database == null || list == null)
                return;
            serializedDatabase.Update();
            visibleIndices.Clear();
            string query = searchText.Trim();
            for (int i = 0; i < database.Battlefields.Count; i++)
            {
                BattlefieldDefinitionData field = database.Battlefields[i];
                if (field == null)
                    continue;
                if (!string.IsNullOrEmpty(query) &&
                    (field.Id == null || field.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (field.DisplayLabel == null || field.DisplayLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                visibleIndices.Add(i);
            }
            list.itemsSource = visibleIndices;
            list.Rebuild();
            int visible = visibleIndices.IndexOf(selectedIndex);
            if (visible >= 0)
                list.SetSelectionWithoutNotify(new[] { visible });
        }

        private void RestoreSelection()
        {
            if (selectedIndex < 0 || selectedIndex >= database.Battlefields.Count)
                selectedIndex = database.Battlefields.Count > 0 ? 0 : -1;
            ShowSelected();
            int visible = visibleIndices.IndexOf(selectedIndex);
            if (visible >= 0)
                list.SetSelectionWithoutNotify(new[] { visible });
        }

        private void SelectVisible(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleIndices.Count)
                return;
            selectedIndex = visibleIndices[visibleIndex];
            ShowSelected();
        }

        private bool HasSelection => database != null && selectedIndex >= 0 && selectedIndex < database.Battlefields.Count;
        private BattlefieldDefinitionData SelectedField => HasSelection ? database.Battlefields[selectedIndex] : null;
        private SerializedProperty SelectedProperty => fieldsProperty.GetArrayElementAtIndex(selectedIndex);

        // ── Основная часть: предпросмотр + настройки ───────────────────────

        private VisualElement BuildMainPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;
            pane.style.backgroundColor = PaneBackground;

            emptyHint = new Label("Выберите поле слева или перетащите картинку в список.");
            emptyHint.style.flexGrow = 1f;
            emptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            pane.Add(emptyHint);

            main = new VisualElement();
            main.style.flexGrow = 1f;
            main.style.flexDirection = FlexDirection.Row;
            main.style.display = DisplayStyle.None;
            main.Add(BuildPreviewColumn());

            ScrollView settingsScroll = new ScrollView(ScrollViewMode.Vertical);
            settingsScroll.style.width = 340f;
            settingsScroll.style.flexShrink = 0f;
            settingsScroll.style.paddingTop = 8f;
            settingsScroll.style.paddingRight = 4f;
            settings = new VisualElement();
            settingsScroll.Add(settings);
            main.Add(settingsScroll);
            pane.Add(main);

            // Любая правка настроек — сразу в предпросмотр.
            settings.RegisterCallback<ChangeEvent<float>>(_ => ScheduleRefresh());
            settings.RegisterCallback<ChangeEvent<Color>>(_ => ScheduleRefresh());
            settings.RegisterCallback<ChangeEvent<UnityEngine.Object>>(_ => ScheduleRefresh());
            settings.RegisterCallback<ChangeEvent<string>>(_ => ScheduleRefresh());
            return pane;
        }

        private void ShowSelected()
        {
            if (!HasSelection)
            {
                emptyHint.style.display = DisplayStyle.Flex;
                main.style.display = DisplayStyle.None;
                return;
            }

            emptyHint.style.display = DisplayStyle.None;
            main.style.display = DisplayStyle.Flex;
            serializedDatabase.Update();
            settings.Unbind();
            settings.Clear();
            SerializedProperty field = SelectedProperty;
            settings.Add(BuildFieldCard(field));
            settings.Add(BuildBackgroundCard(field));
            settings.Add(BuildGridCard(field));
            settings.Add(BuildHexCard(field));
            settings.Bind(serializedDatabase);
            RefreshPreview();
        }

        private void ScheduleRefresh()
        {
            if (refreshScheduled || rootVisualElement == null)
                return;
            refreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                refreshScheduled = false;
                RefreshPreview();
                list?.RefreshItems();
            });
        }

        // ── Карточка «Поле» ────────────────────────────────────────────────

        private VisualElement BuildFieldCard(SerializedProperty field)
        {
            VisualElement card = SectionCard("ПОЛЕ", FieldAccent);

            TextField title = new TextField { bindingPath = field.FindPropertyRelative("displayLabel").propertyPath };
            title.style.fontSize = 14f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginLeft = 0f;
            card.Add(title);

            bool active = SelectedField.Id == database.SandboxBattlefieldId;
            Button use = new Button(MakeSelectedSandboxDefault)
            {
                text = active ? "● Это поле идёт в бой" : "Сделать полем боя"
            };
            use.SetEnabled(!active);
            use.style.marginTop = 4f;
            use.style.marginLeft = 0f;
            use.style.height = 24f;
            card.Add(use);

            Foldout more = new Foldout { text = "ID и теги", value = false };
            more.style.marginTop = 6f;
            TextField id = new TextField("ID") { bindingPath = field.FindPropertyRelative("id").propertyPath, isDelayed = true };
            Compact(id);
            id.RegisterValueChangedCallback(evt =>
            {
                // Поле в бою ссылается на ID — переименование не должно его терять.
                if (evt.previousValue == database.SandboxBattlefieldId && !string.IsNullOrEmpty(evt.newValue))
                {
                    serializedDatabase.Update();
                    sandboxIdProperty.stringValue = evt.newValue;
                    serializedDatabase.ApplyModifiedProperties();
                }
            });
            more.Add(id);
            more.Add(BuildTagChips(field.FindPropertyRelative("tagIds")));

            Foldout tagBook = new Foldout { text = "Справочник тегов", value = false };
            tagBook.Add(new PropertyField(tagsProperty, "Теги базы"));
            Button addTag = new Button(AddTag) { text = "+ Тег" };
            addTag.style.alignSelf = Align.FlexStart;
            tagBook.Add(addTag);
            more.Add(tagBook);
            card.Add(more);
            return card;
        }

        private VisualElement BuildTagChips(SerializedProperty tagIds)
        {
            VisualElement chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 4f;
            foreach (BattlefieldTagDefinition tag in database.Tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Id))
                    continue;
                string tagId = tag.Id;
                bool on = SelectedField.HasTag(tagId);
                Button chip = new Button(() => ToggleTag(tagIds, tagId))
                {
                    text = string.IsNullOrEmpty(tag.DisplayLabel) ? tagId : tag.DisplayLabel,
                    tooltip = tagId + (string.IsNullOrEmpty(tag.Description) ? string.Empty : "\n" + tag.Description)
                };
                chip.style.height = 20f;
                chip.style.marginLeft = 0f;
                chip.style.marginRight = 4f;
                chip.style.marginBottom = 4f;
                chip.style.fontSize = 11f;
                Color color = tag.Color;
                chip.style.backgroundColor = on ? new Color(color.r, color.g, color.b, 0.85f) : new Color(0.16f, 0.16f, 0.17f, 1f);
                chip.style.color = on ? Color.white : MutedText;
                chips.Add(chip);
            }
            return chips;
        }

        private void ToggleTag(SerializedProperty tagIds, string tagId)
        {
            serializedDatabase.Update();
            int index = -1;
            for (int i = 0; i < tagIds.arraySize; i++)
            {
                if (tagIds.GetArrayElementAtIndex(i).stringValue == tagId)
                    index = i;
            }
            if (index >= 0)
                tagIds.DeleteArrayElementAtIndex(index);
            else
            {
                tagIds.arraySize++;
                tagIds.GetArrayElementAtIndex(tagIds.arraySize - 1).stringValue = tagId;
            }
            serializedDatabase.ApplyModifiedProperties();
            ShowSelected();
        }

        // ── Карточка «Фон» ─────────────────────────────────────────────────

        private VisualElement BuildBackgroundCard(SerializedProperty field)
        {
            VisualElement card = SectionCard("ФОН", BackgroundAccent);
            card.Add(ImageSlot("Картинка поля — перетащите сюда или на предпросмотр",
                field.FindPropertyRelative("background"), BackgroundFolder));
            card.Add(BoundSlider("Масштаб", field.FindPropertyRelative("backgroundScale"), 0.5f, 3f));
            SerializedProperty offset = field.FindPropertyRelative("backgroundOffset");
            card.Add(BoundSlider("Сдвиг X", offset.FindPropertyRelative("x"), -0.5f, 0.5f));
            card.Add(BoundSlider("Сдвиг Y", offset.FindPropertyRelative("y"), -0.5f, 0.5f));
            card.Add(SmallButton("Сбросить кадрирование", () => ResetValues(field,
                ("backgroundScale", 1f), ("backgroundOffset.x", 0f), ("backgroundOffset.y", 0f))));
            return card;
        }

        // ── Карточка «Сетка» ───────────────────────────────────────────────

        private VisualElement BuildGridCard(SerializedProperty field)
        {
            VisualElement card = SectionCard("СЕТКА", GridAccent);
            Label hint = new Label("ЛКМ по гексу на предпросмотре — отключить или включить его, протяжка — кистью. " +
                                   "На отключённые гексы нельзя пойти, в бою их нет.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = MutedText;
            hint.style.marginBottom = 4f;
            card.Add(hint);
            card.Add(BoundSlider("Размер", field.FindPropertyRelative("gridScale"), 0.5f, 1.5f));
            SerializedProperty offset = field.FindPropertyRelative("gridOffset");
            card.Add(BoundSlider("Сдвиг X", offset.FindPropertyRelative("x"), -0.3f, 0.3f));
            card.Add(BoundSlider("Сдвиг Y", offset.FindPropertyRelative("y"), -0.3f, 0.3f));

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(SmallButton("Сбросить положение", () => ResetValues(field,
                ("gridScale", 1f), ("gridOffset.x", 0f), ("gridOffset.y", 0f))));
            row.Add(SmallButton("Включить все гексы", EnableAllCells));
            row.Add(SmallButton("Инвертировать", InvertCells));
            card.Add(row);
            return card;
        }

        // ── Карточка «Вид гекса» ───────────────────────────────────────────

        private VisualElement BuildHexCard(SerializedProperty field)
        {
            VisualElement card = SectionCard("ВИД ГЕКСА", HexAccent);
            SerializedProperty useOwn = field.FindPropertyRelative("useOwnHexStyle");
            Toggle own = new Toggle("Свой вид у этого поля") { value = useOwn.boolValue };
            Compact(own);
            own.tooltip = "Выключено — общий вид для всех полей базы. Включено — вид только этого поля " +
                          "(при включении копируется общий).";
            own.RegisterValueChangedCallback(evt => SetOwnHexStyle(evt.newValue));
            card.Add(own);

            Label scope = new Label(useOwn.boolValue
                ? "Правки ниже — только для этого поля."
                : "Правки ниже — для всех полей без своего вида.");
            scope.style.fontSize = 10f;
            scope.style.color = MutedText;
            scope.style.marginBottom = 4f;
            card.Add(scope);

            SerializedProperty style = useOwn.boolValue
                ? field.FindPropertyRelative("hexStyle")
                : serializedDatabase.FindProperty("hexStyle");

            card.Add(ImageSlot("Картинка гекса", style.FindPropertyRelative("hexImage"), HexImageFolder));
            card.Add(BoundColor("Оттенок картинки", style.FindPropertyRelative("hexImageTint")));
            card.Add(ImageSlot("Картинка рамки", style.FindPropertyRelative("frameImage"), HexImageFolder));
            card.Add(BoundColor("Оттенок рамки", style.FindPropertyRelative("frameImageTint")));
            card.Add(BoundSlider("Размер картинок", style.FindPropertyRelative("imageScale"), 0.5f, 1.5f));
            card.Add(BoundColor("Подложка", style.FindPropertyRelative("fillColor")));
            card.Add(BoundColor("Линия", style.FindPropertyRelative("lineColor")));
            card.Add(BoundSlider("Толщина линии", style.FindPropertyRelative("lineWidth"), 0f, 6f));
            card.Add(BoundSlider("Зазор", style.FindPropertyRelative("gap"), 0f, 0.3f));
            card.Add(BoundSlider("Непрозрачность", style.FindPropertyRelative("opacity"), 0f, 1f));

            Foldout states = new Foldout { text = "Цвета состояний в бою", value = false };
            states.Add(BoundColor("Трудный", style.FindPropertyRelative("difficultColor")));
            states.Add(BoundColor("Непроходимый", style.FindPropertyRelative("impassableColor")));
            states.Add(BoundColor("Доступный ход", style.FindPropertyRelative("reachableColor")));
            states.Add(BoundSlider("Толщина хода", style.FindPropertyRelative("reachableLineWidth"), 0.5f, 6f));
            states.Add(BoundColor("Цель", style.FindPropertyRelative("targetColor")));
            states.Add(BoundColor("Наведение: заливка", style.FindPropertyRelative("attackHoverFill")));
            states.Add(BoundColor("Наведение: линия", style.FindPropertyRelative("attackHoverLine")));
            card.Add(states);

            Toggle samples = new Toggle("Показать состояния на предпросмотре") { value = showStateSamples };
            samples.RegisterValueChangedCallback(evt =>
            {
                showStateSamples = evt.newValue;
                RefreshPreview();
            });
            card.Add(samples);

            card.Add(SmallButton("Сбросить вид гекса", ResetHexStyle));
            Label size = new Label("Картинка гекса растягивается на прямоугольник гекса: ширина : высота ≈ 1,15 : 1. " +
                                   "Изображения можно перетаскивать прямо на ячейки.");
            size.style.whiteSpace = WhiteSpace.Normal;
            size.style.fontSize = 10f;
            size.style.color = MutedText;
            size.style.marginTop = 4f;
            card.Add(size);
            return card;
        }

        private BattlefieldHexStyle CurrentHexStyle => database.GetHexStyle(SelectedField);

        private void SetOwnHexStyle(bool own)
        {
            if (!HasSelection)
                return;
            Undo.RecordObject(database, own ? "Свой вид гекса" : "Общий вид гекса");
            BattlefieldDefinitionData field = SelectedField;
            if (own)
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(database.HexStyle), field.OwnHexStyle);
            serializedDatabase.Update();
            SelectedProperty.FindPropertyRelative("useOwnHexStyle").boolValue = own;
            serializedDatabase.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
            ShowSelected();
        }

        private void ResetHexStyle()
        {
            if (!HasSelection)
                return;
            Undo.RecordObject(database, "Сбросить вид гекса");
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(new BattlefieldHexStyle()), CurrentHexStyle);
            EditorUtility.SetDirty(database);
            serializedDatabase.Update();
            ShowSelected();
        }

        // ── Элементы настроек ──────────────────────────────────────────────

        private static VisualElement SectionCard(string title, Color accent)
        {
            VisualElement card = new VisualElement();
            card.style.marginLeft = 4f;
            card.style.marginRight = 6f;
            card.style.marginBottom = 8f;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.paddingTop = 7f;
            card.style.paddingBottom = 9f;
            card.style.backgroundColor = CardBackground;
            card.style.borderTopWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 3f;
            card.style.borderTopColor = CardBorder;
            card.style.borderRightColor = CardBorder;
            card.style.borderBottomColor = CardBorder;
            card.style.borderLeftColor = accent;
            card.style.borderTopLeftRadius = 5f;
            card.style.borderTopRightRadius = 5f;
            card.style.borderBottomLeftRadius = 5f;
            card.style.borderBottomRightRadius = 5f;
            Label header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11f;
            header.style.letterSpacing = 1f;
            header.style.color = accent;
            header.style.marginBottom = 5f;
            card.Add(header);
            return card;
        }

        private static void Compact(VisualElement field)
        {
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            Label label = field.Q<Label>(className: BaseField<int>.labelUssClassName);
            if (label == null)
                return;
            label.style.minWidth = 112f;
            label.style.width = 112f;
        }

        private static Slider BoundSlider(string label, SerializedProperty property, float min, float max)
        {
            Slider slider = new Slider(label, min, max) { showInputField = true, bindingPath = property.propertyPath };
            Compact(slider);
            return slider;
        }

        private static ColorField BoundColor(string label, SerializedProperty property)
        {
            ColorField field = new ColorField(label) { bindingPath = property.propertyPath, showAlpha = true };
            Compact(field);
            return field;
        }

        private static Button SmallButton(string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 20f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 4f;
            button.style.marginTop = 4f;
            button.style.fontSize = 11f;
            button.style.alignSelf = Align.FlexStart;
            return button;
        }

        // Ячейка картинки: миниатюра + поле выбора; принимает перетаскивание
        // спрайтов, текстур проекта и файлов из проводника.
        private VisualElement ImageSlot(string label, SerializedProperty property, string importFolder)
        {
            VisualElement slot = new VisualElement();
            slot.style.flexDirection = FlexDirection.Row;
            slot.style.alignItems = Align.Center;
            slot.style.marginTop = 3f;
            slot.style.marginBottom = 3f;
            slot.style.paddingLeft = 3f;
            slot.style.paddingTop = 3f;
            slot.style.paddingBottom = 3f;
            slot.style.backgroundColor = new Color(0.16f, 0.165f, 0.18f, 1f);
            slot.style.borderTopLeftRadius = 3f;
            slot.style.borderTopRightRadius = 3f;
            slot.style.borderBottomLeftRadius = 3f;
            slot.style.borderBottomRightRadius = 3f;

            Image thumb = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            thumb.style.width = 58f;
            thumb.style.height = 40f;
            thumb.style.marginRight = 6f;
            thumb.style.backgroundColor = new Color(0.08f, 0.085f, 0.09f, 1f);
            thumb.sprite = property.objectReferenceValue as Sprite;
            slot.Add(thumb);

            VisualElement column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.flexShrink = 1f;
            Label caption = new Label(label);
            caption.style.fontSize = 10f;
            caption.style.color = MutedText;
            caption.style.whiteSpace = WhiteSpace.Normal;
            column.Add(caption);
            ObjectField picker = new ObjectField
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                bindingPath = property.propertyPath
            };
            picker.style.marginLeft = 0f;
            picker.RegisterValueChangedCallback(evt => thumb.sprite = evt.newValue as Sprite);
            column.Add(picker);
            slot.Add(column);

            string path = property.propertyPath;
            RegisterImageDrop(slot, importFolder, false, sprites =>
            {
                serializedDatabase.Update();
                serializedDatabase.FindProperty(path).objectReferenceValue = sprites[0];
                serializedDatabase.ApplyModifiedProperties();
                thumb.sprite = sprites[0];
                ScheduleRefresh();
            });
            return slot;
        }

        private void ResetValues(SerializedProperty field, params (string Path, float Value)[] values)
        {
            serializedDatabase.Update();
            foreach ((string path, float value) in values)
                field.FindPropertyRelative(path).floatValue = value;
            serializedDatabase.ApplyModifiedProperties();
            ScheduleRefresh();
        }

        // ── Операции с базой ───────────────────────────────────────────────

        private void MakeSelectedSandboxDefault()
        {
            if (!HasSelection)
                return;
            serializedDatabase.Update();
            sandboxIdProperty.stringValue = SelectedField.Id;
            serializedDatabase.ApplyModifiedProperties();
            RefreshList();
            ShowSelected();
        }

        private void AddField(Sprite background, string label)
        {
            serializedDatabase.Update();
            int index = fieldsProperty.arraySize++;
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(index);
            field.FindPropertyRelative("id").stringValue = MakeUniqueFieldId("battlefield");
            field.FindPropertyRelative("displayLabel").stringValue = string.IsNullOrEmpty(label) ? "Новое поле" : label;
            field.FindPropertyRelative("background").objectReferenceValue = background;
            field.FindPropertyRelative("backgroundScale").floatValue = 1f;
            field.FindPropertyRelative("backgroundOffset").vector2Value = Vector2.zero;
            field.FindPropertyRelative("gridScale").floatValue = 1f;
            field.FindPropertyRelative("gridOffset").vector2Value = Vector2.zero;
            field.FindPropertyRelative("disabledCells").ClearArray();
            field.FindPropertyRelative("useOwnHexStyle").boolValue = false;
            field.FindPropertyRelative("tagIds").ClearArray();
            serializedDatabase.ApplyModifiedProperties();
            selectedIndex = index;
            RefreshList();
            ShowSelected();
        }

        private void DuplicateField()
        {
            if (!HasSelection)
                return;
            serializedDatabase.Update();
            fieldsProperty.InsertArrayElementAtIndex(selectedIndex);
            selectedIndex++;
            SerializedProperty field = fieldsProperty.GetArrayElementAtIndex(selectedIndex);
            string sourceId = field.FindPropertyRelative("id").stringValue;
            field.FindPropertyRelative("id").stringValue = MakeUniqueFieldId(sourceId + "_copy");
            field.FindPropertyRelative("displayLabel").stringValue += " — копия";
            serializedDatabase.ApplyModifiedProperties();
            RefreshList();
            ShowSelected();
        }

        private void DeleteField()
        {
            if (!HasSelection)
                return;
            BattlefieldDefinitionData selected = SelectedField;
            string id = selected.Id;
            if (!EditorUtility.DisplayDialog("Удалить поле боя", "Удалить «" + selected.DisplayLabel + "»?", "Удалить", "Отмена"))
                return;
            serializedDatabase.Update();
            fieldsProperty.DeleteArrayElementAtIndex(selectedIndex);
            if (sandboxIdProperty.stringValue == id)
                sandboxIdProperty.stringValue = fieldsProperty.arraySize > 0
                    ? fieldsProperty.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue
                    : string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            selectedIndex = Mathf.Clamp(selectedIndex - 1, database.Battlefields.Count > 0 ? 0 : -1, database.Battlefields.Count - 1);
            RefreshList();
            ShowSelected();
        }

        private void AddTag()
        {
            serializedDatabase.Update();
            int index = tagsProperty.arraySize++;
            SerializedProperty tag = tagsProperty.GetArrayElementAtIndex(index);
            tag.FindPropertyRelative("id").stringValue = MakeUniqueTagId("new_tag");
            tag.FindPropertyRelative("displayLabel").stringValue = "Новый тег";
            tag.FindPropertyRelative("category").stringValue = "Прочее";
            tag.FindPropertyRelative("color").colorValue = Color.gray;
            tag.FindPropertyRelative("description").stringValue = string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            ShowSelected();
        }

        private void ValidateDatabase()
        {
            serializedDatabase.ApplyModifiedProperties();
            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            validation.text = issues.Count == 0 ? "Ошибок не найдено" : "Ошибок: " + issues.Count + " (подробно — в Console)";
            validation.style.color = issues.Count == 0 ? OkColor : WarningColor;
            if (issues.Count > 0)
                Debug.LogWarning("База полей боя:\n- " + string.Join("\n- ", issues));
        }

        private string MakeUniqueFieldId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindById(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }

        private string MakeUniqueTagId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindTag(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
