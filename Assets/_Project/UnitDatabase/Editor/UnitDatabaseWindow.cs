using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.AnimationDatabase.Editor;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    public sealed class UnitDatabaseWindow : EditorWindow
    {
        private const string AssetPath =
            "Assets/_Project/UnitDatabase/Resources/UnitDatabase/KingdomSurvivalUnits.asset";

        [SerializeField] private int selectedUnitIndex = -1;

        private UnitDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private SerializedProperty unitsProperty;
        private SerializedProperty tagsProperty;
        private readonly List<int> filteredUnitIndices = new List<int>();
        private readonly List<string> categoryChoices = new List<string>
        {
            "Все категории",
            "Бойцы",
            "Существа",
            "Командиры",
            "Прочие"
        };

        private TextField searchField;
        private PopupField<string> categoryField;
        private PopupField<string> tagFilterField;
        private ListView unitList;
        private VisualElement detailPane;
        private UnitPortraitElement portraitPreview;
        private Image battlefieldPreview;
        private Label selectionHint;
        private Label validationLabel;
        private bool portraitDragging;
        private int portraitDragPointerId = -1;
        private Vector2 portraitDragStartPointer;
        private Vector2 portraitDragStartOffset;

        // ПР-12З: предпросмотр набора анимаций в карточке существа.
        private CreatureAnimationDatabaseAsset animationDatabase;
        private CreatureAnimationPreviewElement animationPreview;
        private VisualElement animationSetRow;

        [MenuItem("Kingdom Survival/База существ")]
        public static void OpenWindow()
        {
            UnitDatabaseWindow window = GetWindow<UnitDatabaseWindow>();
            window.titleContent = new GUIContent("База существ");
            window.minSize = new Vector2(960f, 620f);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            CreatureAnimationEditorData.DataChanged += OnAnimationDataChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            CreatureAnimationEditorData.DataChanged -= OnAnimationDataChanged;
        }

        // «Открыть в базе существ» из Базы анимаций выбирает нужное существо.
        private void OnFocus()
        {
            ApplyPendingSelection();
        }

        private void ApplyPendingSelection()
        {
            string pending = SessionState.GetString(CreatureAnimationDatabaseWindow.PendingUnitSelectionKey, string.Empty);
            if (string.IsNullOrEmpty(pending) || database == null || unitList == null)
                return;
            SessionState.EraseString(CreatureAnimationDatabaseWindow.PendingUnitSelectionKey);
            for (int i = 0; i < database.Units.Count; i++)
            {
                if (database.Units[i] == null || database.Units[i].Id != pending)
                    continue;
                if (searchField != null)
                    searchField.SetValueWithoutNotify(string.Empty);
                if (categoryField != null)
                    categoryField.index = 0;
                if (tagFilterField != null)
                    tagFilterField.index = 0;
                selectedUnitIndex = i;
                RefreshUnitList();
                RestoreSelection();
                return;
            }
        }

        // После Undo карточка перестраивается: русские списки не привязаны
        // к полям напрямую и иначе показали бы прежнее значение.
        private void OnUndoRedo()
        {
            if (serializedDatabase == null || detailPane == null)
                return;
            serializedDatabase.Update();
            unitList?.RefreshItems();
            ShowSelectedUnit();
        }

        private void OnAnimationDataChanged()
        {
            if (serializedDatabase == null || detailPane == null)
                return;
            serializedDatabase.Update();
            RefreshAnimationCard();
        }

        public void CreateGUI()
        {
            database = AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(AssetPath);
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;

            if (database == null)
            {
                HelpBox missing = new HelpBox(
                    "Не найден файл базы существ: " + AssetPath,
                    HelpBoxMessageType.Error);
                rootVisualElement.Add(missing);
                Button selectFolder = new Button(() => EditorGUIUtility.PingObject(
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                        "Assets/_Project/UnitDatabase")))
                {
                    text = "ПОКАЗАТЬ ПАПКУ"
                };
                rootVisualElement.Add(selectFolder);
                return;
            }

            if (database.MigrateIfNeeded())
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }

            serializedDatabase = new SerializedObject(database);
            unitsProperty = serializedDatabase.FindProperty("units");
            tagsProperty = serializedDatabase.FindProperty("tags");

            BuildToolbar();

            TwoPaneSplitView mainSplit = new TwoPaneSplitView(
                0,
                ListPaneWidth,
                TwoPaneSplitViewOrientation.Horizontal);
            mainSplit.style.flexGrow = 1f;
            rootVisualElement.Add(mainSplit);

            mainSplit.Add(BuildListPane());
            mainSplit.Add(BuildRightPane());

            RefreshTagFilter();
            RefreshUnitList();
            RestoreSelection();
            ApplyPendingSelection();
        }

        private void BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 42f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            toolbar.style.borderBottomWidth = 1f;
            toolbar.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f, 1f);

            Button add = new Button(AddUnit) { text = "+ ДОБАВИТЬ" };
            Button duplicate = new Button(DuplicateSelectedUnit) { text = "ДУБЛИРОВАТЬ" };
            Button remove = new Button(DeleteSelectedUnit) { text = "УДАЛИТЬ" };
            Button addTag = new Button(AddTag) { text = "+ ТЕГ" };
            Button validate = new Button(ValidateDatabase) { text = "ПРОВЕРИТЬ БАЗУ" };

            // Экран героя показывает существа этой базы как заглушки в пустых
            // местах состава, поэтому из базы полезно открывать его разметку.
            // Вызов идёт через пункт меню, чтобы не заводить зависимость
            // KingdomSurvival.UnitDatabase.Editor → KingdomSurvival.UILayout.Editor.
            Button heroScreen = new Button(OpenHeroScreenLayout)
            {
                text = "ЭКРАН ГЕРОЯ",
                tooltip = "Открыть разметку экрана героя в UI Конструкторе. " +
                          "Существа этой базы показываются там как заглушки состава."
            };

            Button cleanArt = new Button(CleanUnusedArt)
            {
                text = "ОЧИСТИТЬ КАРТИНКИ",
                tooltip = "Убрать в корзину загруженные портреты и миниатюры, которые остались от прежних замен и никому не нужны."
            };

            foreach (Button button in new[] { add, duplicate, remove, addTag, validate, heroScreen, cleanArt })
            {
                button.style.height = 26f;
                button.style.marginRight = 6f;
                toolbar.Add(button);
            }

            validationLabel = new Label();
            validationLabel.style.marginLeft = 8f;
            validationLabel.style.flexGrow = 1f;
            validationLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(validationLabel);
            rootVisualElement.Add(toolbar);
        }

        private static void OpenHeroScreenLayout()
        {
            if (!EditorApplication.ExecuteMenuItem("Kingdom Survival/UI Конструктор"))
            {
                Debug.LogWarning(
                    "Не удалось открыть UI Конструктор: пункт меню недоступен.");
                return;
            }

            Debug.Log(
                "UI Конструктор открыт. Выберите экран «Экран героя», " +
                "чтобы настроить разметку блоков состава, характеристик и снаряжения.");
        }

        // Узкая левая панель: фильтры без боковых подписей во всю ширину —
        // при сужении панели они не наезжают друг на друга.
        private const float ListPaneWidth = 190f;
        private const float ThumbnailScale = 0.56f;

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.minWidth = 150f;
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;
            pane.style.paddingBottom = 6f;

            searchField = new TextField { tooltip = "Поиск по названию" };
            searchField.textEdition.placeholder = "Поиск…";
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(searchField);

            categoryField = new PopupField<string>(categoryChoices, 0) { tooltip = "Категория" };
            categoryField.style.marginLeft = 0f;
            categoryField.style.marginRight = 0f;
            categoryField.style.marginTop = 4f;
            categoryField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(categoryField);

            tagFilterField = new PopupField<string>(new List<string> { "Все теги" }, 0) { tooltip = "Тег" };
            tagFilterField.style.marginLeft = 0f;
            tagFilterField.style.marginRight = 0f;
            tagFilterField.style.marginTop = 4f;
            tagFilterField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(tagFilterField);

            unitList = new ListView();
            unitList.style.flexGrow = 1f;
            unitList.style.marginTop = 8f;
            unitList.fixedItemHeight = PortraitSizeTable.Get(PortraitSize.XS).Height * ThumbnailScale + 8f;
            unitList.selectionType = SelectionType.Single;
            unitList.makeItem = CreateUnitListItem;
            unitList.bindItem = BindUnitListItem;
            unitList.selectionChanged += _ => SelectVisibleUnit(unitList.selectedIndex);
            pane.Add(unitList);
            return pane;
        }

        private VisualElement BuildRightPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;

            selectionHint = new Label("Выберите тип существа слева.");
            selectionHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            selectionHint.style.flexGrow = 1f;
            pane.Add(selectionHint);

            detailPane = new ScrollView(ScrollViewMode.Vertical);
            detailPane.style.display = DisplayStyle.None;
            detailPane.style.flexGrow = 1f;
            detailPane.style.paddingLeft = 16f;
            detailPane.style.paddingRight = 16f;
            detailPane.style.paddingTop = 12f;
            detailPane.style.paddingBottom = 18f;
            detailPane.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                unitList.RefreshItems();
                RefreshPreviews();
                RefreshTagFilter();
                RefreshAnimationPreviewData();
            });
            pane.Add(detailPane);
            return pane;
        }

        private static VisualElement CreateUnitListItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 5f;
            row.style.paddingRight = 5f;

            VisualElement thumbnailFrame = new VisualElement { name = "thumbnail-frame" };
            PortraitSizeDefinition thumbnailPreset = PortraitSizeTable.Get(PortraitSize.XS);
            // XS 100x140 в масштабе 56% (было 40%): миниатюра списка крупнее и
            // сохраняет каноническое отношение рамки 5:7.
            thumbnailFrame.style.width = thumbnailPreset.Width * ThumbnailScale;
            thumbnailFrame.style.height = thumbnailPreset.Height * ThumbnailScale;
            thumbnailFrame.style.flexShrink = 0f;
            thumbnailFrame.style.marginRight = 8f;
            thumbnailFrame.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 1f);
            thumbnailFrame.style.overflow = Overflow.Hidden;

            UnitPortraitElement thumbnail = new UnitPortraitElement
            {
                name = "thumbnail",
                pickingMode = PickingMode.Ignore
            };
            thumbnail.style.position = Position.Absolute;
            thumbnail.style.left = 0f;
            thumbnail.style.right = 0f;
            thumbnail.style.top = 0f;
            thumbnail.style.bottom = 0f;
            thumbnailFrame.Add(thumbnail);
            row.Add(thumbnailFrame);

            VisualElement text = new VisualElement();
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            text.style.minWidth = 0f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            Label info = new Label { name = "info" };
            info.style.fontSize = 10f;
            info.style.color = new Color(0.60f, 0.60f, 0.60f, 1f);
            text.Add(title);
            text.Add(info);
            row.Add(text);
            return row;
        }

        private void BindUnitListItem(VisualElement element, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= filteredUnitIndices.Count)
                return;

            UnitDefinitionData unit = database.Units[filteredUnitIndices[visibleIndex]];
            element.Q<Label>("title").text = string.IsNullOrWhiteSpace(unit.DisplayLabel)
                ? "БЕЗ НАЗВАНИЯ ТИПА"
                : unit.DisplayLabel;
            // Без английского ID: категория и, если нужно, что ждёт рисунка.
            string waiting = unit.Portrait == null && unit.BattlefieldSprite == null
                ? " · ждёт рисунка"
                : unit.Portrait == null ? " · нет портрета" : string.Empty;
            element.Q<Label>("info").text = GetCategoryLabel(unit.Category) + waiting;

            UnitPortraitElement thumbnail = element.Q<UnitPortraitElement>("thumbnail");
            thumbnail.SetPortrait(unit);
        }

        private void RefreshUnitList()
        {
            if (database == null || unitList == null)
                return;

            serializedDatabase.Update();
            filteredUnitIndices.Clear();
            string query = searchField != null ? searchField.value.Trim() : string.Empty;
            int categoryIndex = categoryField != null ? categoryField.index : 0;
            string selectedTagId = GetSelectedTagId();

            for (int i = 0; i < database.Units.Count; i++)
            {
                UnitDefinitionData unit = database.Units[i];
                if (unit == null)
                    continue;
                if (!MatchesSearch(unit, query))
                    continue;
                if (categoryIndex > 0 && (int)unit.Category != categoryIndex - 1)
                    continue;
                if (!string.IsNullOrEmpty(selectedTagId) && !unit.HasTag(selectedTagId))
                    continue;
                filteredUnitIndices.Add(i);
            }

            unitList.itemsSource = filteredUnitIndices;
            unitList.Rebuild();

            int visibleSelection = filteredUnitIndices.IndexOf(selectedUnitIndex);
            if (visibleSelection >= 0)
                unitList.SetSelectionWithoutNotify(new[] { visibleSelection });
            else
                unitList.ClearSelection();
        }

        private void RefreshTagFilter()
        {
            if (tagFilterField == null || database == null)
                return;

            string previousId = GetSelectedTagId();
            List<string> choices = new List<string> { "Все теги" };
            for (int i = 0; i < database.Tags.Count; i++)
            {
                UnitTagDefinition tag = database.Tags[i];
                choices.Add(tag.DisplayLabel + "  [" + tag.Id + "]");
            }

            tagFilterField.choices = choices;
            int newIndex = 0;
            if (!string.IsNullOrEmpty(previousId))
            {
                for (int i = 0; i < database.Tags.Count; i++)
                {
                    if (database.Tags[i].Id == previousId)
                    {
                        newIndex = i + 1;
                        break;
                    }
                }
            }
            tagFilterField.index = Mathf.Clamp(newIndex, 0, choices.Count - 1);
        }

        private void RestoreSelection()
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
                selectedUnitIndex = database.Units.Count > 0 ? 0 : -1;

            if (selectedUnitIndex >= 0)
            {
                int visibleIndex = filteredUnitIndices.IndexOf(selectedUnitIndex);
                if (visibleIndex >= 0)
                    unitList.SetSelection(visibleIndex);
                ShowSelectedUnit();
            }
        }

        private void SelectVisibleUnit(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= filteredUnitIndices.Count)
                return;
            selectedUnitIndex = filteredUnitIndices[visibleIndex];
            ShowSelectedUnit();
        }

        // ------------------------------------------------------------------
        // Карточка существа — блоки фиксированной ширины, которые переносятся
        // по ширине окна: тип, характеристики, теги, портрет, миниатюра,
        // анимация, способности. Числа — ползунками с полем ввода, настройки
        // картинки — под самой картинкой.
        // ------------------------------------------------------------------

        private static readonly Color CardColor = new Color(0.13f, 0.14f, 0.16f, 1f);
        private static readonly Color CardBorder = new Color(0.22f, 0.23f, 0.25f, 1f);
        private static readonly Color HeaderColor = new Color(0.86f, 0.70f, 0.38f, 1f);
        private static readonly Color HintColor = new Color(0.60f, 0.60f, 0.60f, 1f);

        private const float FieldViewportWidth = 250f;
        private const float FieldViewportHeight = 290f;
        private const float FieldBaseBoxSize = 190f;
        private const float FieldAnchorY = 0.72f;

        private VisualElement battlefieldViewport;
        private VisualElement battlefieldAnchorMarker;

        private void ShowSelectedUnit()
        {
            portraitDragging = false;
            portraitDragPointerId = -1;

            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
            {
                detailPane.style.display = DisplayStyle.None;
                selectionHint.style.display = DisplayStyle.Flex;
                return;
            }

            selectionHint.style.display = DisplayStyle.None;
            detailPane.style.display = DisplayStyle.Flex;
            detailPane.Clear();

            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            UnitDefinitionData data = database.Units[selectedUnitIndex];

            VisualElement titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.FlexEnd;
            titleRow.style.marginBottom = 10f;
            Label name = new Label(string.IsNullOrWhiteSpace(data.DisplayLabel) ? "Без названия" : data.DisplayLabel);
            name.style.fontSize = 20f;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = HeaderColor;
            titleRow.Add(name);
            Label category = new Label(GetCategoryLabel(data.Category));
            category.style.marginLeft = 10f;
            category.style.marginBottom = 3f;
            category.style.color = HintColor;
            titleRow.Add(category);
            detailPane.Add(titleRow);

            VisualElement grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            detailPane.Add(grid);

            VisualElement typeCard = Card("ТИП", 320f);
            AddField(typeCard, unit, "displayLabel", "Название");
            AddField(typeCard, unit, "id", "ID типа");
            typeCard.Add(EnumPopup(unit, "category", "Категория", CategoryNames));
            typeCard.Add(EnumPopup(unit, "combatRole", "Боевая роль", RoleNames));
            typeCard.Add(EnumPopup(unit, "size", "Размер", SizeNames));
            grid.Add(typeCard);

            VisualElement statsCard = Card("БОЕВЫЕ ХАРАКТЕРИСТИКИ", 320f);
            statsCard.Add(IntSlider(unit, "maxHitPoints", "HP", 1, 100, "Здоровье."));
            statsCard.Add(IntSlider(unit, "attack", "Атака", 0, 20, "Сравнивается с Защитой цели и меняет урон."));
            statsCard.Add(IntSlider(unit, "defense", "Защита", 0, 20, "Снижает урон от атак."));
            statsCard.Add(IntSlider(unit, "damage", "Урон", 1, 30, "Базовый урон удара."));
            statsCard.Add(IntSlider(unit, "movement", "Ход", 1, 30, "Очки перемещения за активацию."));
            statsCard.Add(IntSlider(unit, "initiative", "Инициатива", 0, 20, "Порядок ходов в раунде."));
            statsCard.Add(IntSlider(unit, "attackRange", "Дальность", 1, 10, "1 — ближний бой, больше — стрелок."));
            grid.Add(statsCard);

            VisualElement tagsCard = Card("ТЕГИ", 320f);
            BuildTagToggles(tagsCard, unit.FindPropertyRelative("tagIds"));
            grid.Add(tagsCard);

            grid.Add(BuildPortraitCard(unit));
            grid.Add(BuildBattlefieldCard(unit, data));
            BuildAnimationCard(grid);

            // ПР-12Ж: способности со статусом «ждёт механики» не действуют,
            // пока в ПР-16 нет их общего кирпича.
            VisualElement abilitiesCard = Card("СПОСОБНОСТИ", 660f);
            abilitiesCard.style.flexGrow = 1f;
            abilitiesCard.style.maxWidth = 980f;
            abilitiesCard.Add(new PropertyField(unit.FindPropertyRelative("abilities"), "Способности"));
            grid.Add(abilitiesCard);

            Foldout tagEditor = new Foldout { text = "Справочник тегов базы", value = false };
            tagEditor.style.marginTop = 4f;
            tagEditor.Add(new PropertyField(tagsProperty, "Теги базы"));
            detailPane.Add(tagEditor);

            detailPane.Bind(serializedDatabase);
            RefreshPreviews();
        }

        private static VisualElement Card(string title, float width)
        {
            VisualElement card = new VisualElement();
            card.style.width = width;
            card.style.marginRight = 12f;
            card.style.marginBottom = 12f;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.paddingTop = 8f;
            card.style.paddingBottom = 10f;
            card.style.backgroundColor = CardColor;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftColor = CardBorder;
            card.style.borderRightColor = CardBorder;
            card.style.borderTopColor = CardBorder;
            card.style.borderBottomColor = CardBorder;
            card.style.borderTopLeftRadius = 6f;
            card.style.borderTopRightRadius = 6f;
            card.style.borderBottomLeftRadius = 6f;
            card.style.borderBottomRightRadius = 6f;

            Label header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11f;
            header.style.letterSpacing = 1f;
            header.style.color = HeaderColor;
            header.style.marginBottom = 6f;
            card.Add(header);
            return card;
        }

        // Русские подписи значений в порядке объявления перечислений.
        private static readonly string[] CategoryNames = { "Боец", "Существо", "Командир", "Прочее" };
        private static readonly string[] RoleNames = { "Защитник", "Лучник", "Лекарь", "Копейщик", "Разведчик", "Ополченец", "Существо", "Другая" };
        private static readonly string[] SizeNames = { "Средний", "Малый", "Крупный" };
        private static readonly string[] FitNames = { "Заполнить рамку (обрезать)", "Вписать целиком" };

        // Выбор значения перечисления с русскими подписями; запись через
        // SerializedProperty, поэтому Undo работает как у остальных полей.
        private VisualElement EnumPopup(SerializedProperty owner, string propertyName, string label, string[] names)
        {
            SerializedProperty property = owner.FindPropertyRelative(propertyName);
            string path = property.propertyPath;
            List<int> choices = Enumerable.Range(0, names.Length).ToList();
            int current = Mathf.Clamp(property.enumValueIndex, 0, names.Length - 1);
            PopupField<int> field = new PopupField<int>(label, choices, current, index => names[index], index => names[index]);
            field.RegisterValueChangedCallback(evt =>
            {
                serializedDatabase.Update();
                serializedDatabase.FindProperty(path).enumValueIndex = evt.newValue;
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                SchedulePreviewRefresh();
            });
            return field;
        }

        // Ссылка на картинку без заголовков-атрибутов поля.
        private static ObjectField SpriteField(SerializedProperty owner, string propertyName)
        {
            ObjectField field = new ObjectField("Файл")
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                tooltip = "Sprite из проекта. Загрузить с диска — кнопкой выше или перетаскиванием на картинку."
            };
            field.labelElement.style.minWidth = 70f;
            field.labelElement.style.width = 70f;
            field.BindProperty(owner.FindPropertyRelative(propertyName));
            return field;
        }

        private static void WidenSliderInput(VisualElement slider, float width)
        {
            VisualElement input = slider.Q(className: "unity-base-slider__text-field");
            if (input != null)
            {
                input.style.width = width;
                input.style.minWidth = width;
            }
        }

        private static void AddField(VisualElement parent, SerializedProperty owner, string propertyName, string label)
        {
            PropertyField field = new PropertyField(owner.FindPropertyRelative(propertyName), label);
            parent.Add(field);
        }

        private static Label Hint(string text)
        {
            Label hint = new Label(text);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = HintColor;
            hint.style.marginTop = 4f;
            return hint;
        }

        // Ползунок с полем ввода, привязанный к полю базы: Undo и сохранение
        // работают как у остальных полей.
        private SliderInt IntSlider(SerializedProperty owner, string propertyName, string label, int low, int high, string tooltip)
        {
            SliderInt slider = new SliderInt(label, low, high) { showInputField = true, tooltip = tooltip };
            slider.labelElement.style.minWidth = 86f;
            slider.labelElement.style.width = 86f;
            slider.BindProperty(owner.FindPropertyRelative(propertyName));
            slider.RegisterValueChangedCallback(_ => SchedulePreviewRefresh());
            WidenSliderInput(slider, 48f);
            return slider;
        }

        private Slider FloatSlider(SerializedProperty property, string label, float low, float high, string tooltip)
        {
            Slider slider = new Slider(label, low, high) { showInputField = true, tooltip = tooltip };
            slider.labelElement.style.minWidth = 70f;
            slider.labelElement.style.width = 70f;
            slider.BindProperty(property);
            slider.RegisterValueChangedCallback(_ => SchedulePreviewRefresh());
            WidenSliderInput(slider, 62f);
            return slider;
        }

        // Привязка пишет значение в базу чуть позже события ползунка —
        // предпросмотр обновляется на следующем кадре.
        private void SchedulePreviewRefresh()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                RefreshPreviews();
                unitList?.RefreshItems();
            });
        }

        private VisualElement BuildPortraitCard(SerializedProperty unit)
        {
            PortraitSizeDefinition preset = PortraitSizeTable.Get(PortraitSize.S);
            VisualElement card = Card("ПОРТРЕТ", 270f);

            VisualElement frame = new VisualElement();
            frame.style.alignSelf = Align.Center;
            frame.style.marginBottom = 6f;
            portraitPreview = new UnitPortraitElement
            {
                name = "portrait-preview",
                focusable = true,
                tooltip = "ЛКМ + перетаскивание — сдвинуть портрет в рамке 5:7. Сюда же можно перетащить PNG/JPG или Sprite."
            };
            portraitPreview.style.width = preset.Width;
            portraitPreview.style.height = preset.Height;
            portraitPreview.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            portraitPreview.RegisterCallback<PointerDownEvent>(BeginPortraitDrag);
            portraitPreview.RegisterCallback<PointerMoveEvent>(ContinuePortraitDrag);
            portraitPreview.RegisterCallback<PointerUpEvent>(EndPortraitDrag);
            portraitPreview.RegisterCallback<PointerCaptureOutEvent>(_ => CancelPortraitDrag());
            frame.Add(portraitPreview);
            card.Add(frame);
            RegisterArtDrop(card, UnitArtKind.Portrait);

            card.Add(BuildArtButtons(UnitArtKind.Portrait));
            card.Add(SpriteField(unit, "portrait"));
            card.Add(EnumPopup(unit, "portraitFitMode", "Вписывание", FitNames));
            SerializedProperty offset = unit.FindPropertyRelative("portraitOffsetNormalized");
            card.Add(FloatSlider(unit.FindPropertyRelative("portraitScale"), "Масштаб", 0.2f, 4f, "Масштаб портрета в рамке."));
            card.Add(FloatSlider(offset.FindPropertyRelative("x"), "Сдвиг X", -1f, 1f, "Сдвиг по горизонтали, доля рамки."));
            card.Add(FloatSlider(offset.FindPropertyRelative("y"), "Сдвиг Y", -1f, 1f, "Сдвиг по вертикали, доля рамки."));
            AddField(card, unit, "portraitFlipX", "Отразить");

            Button reset = new Button(ResetPortraitFraming) { text = "СБРОСИТЬ КАДРИРОВАНИЕ" };
            reset.style.marginTop = 4f;
            card.Add(reset);
            card.Add(Hint("Рамка 5:7, как в игре. Тяните портрет мышью прямо в рамке."));
            return card;
        }

        private VisualElement BuildBattlefieldCard(SerializedProperty unit, UnitDefinitionData data)
        {
            VisualElement card = Card("ПОЛЕВАЯ МИНИАТЮРА", 290f);

            battlefieldViewport = new VisualElement
            {
                tooltip = "Красная точка — центр гекса. Сюда можно перетащить PNG/JPG или Sprite."
            };
            battlefieldViewport.style.alignSelf = Align.Center;
            battlefieldViewport.style.width = FieldViewportWidth;
            battlefieldViewport.style.height = FieldViewportHeight;
            battlefieldViewport.style.marginBottom = 6f;
            battlefieldViewport.style.overflow = Overflow.Hidden;
            battlefieldViewport.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            battlefieldViewport.generateVisualContent += DrawFieldGround;

            battlefieldPreview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            battlefieldPreview.style.position = Position.Absolute;
            battlefieldViewport.Add(battlefieldPreview);

            battlefieldAnchorMarker = new VisualElement { pickingMode = PickingMode.Ignore };
            battlefieldAnchorMarker.style.position = Position.Absolute;
            battlefieldAnchorMarker.style.width = 10f;
            battlefieldAnchorMarker.style.height = 10f;
            battlefieldAnchorMarker.style.backgroundColor = new Color(0.95f, 0.08f, 0.06f, 1f);
            battlefieldAnchorMarker.style.borderTopLeftRadius = 5f;
            battlefieldAnchorMarker.style.borderTopRightRadius = 5f;
            battlefieldAnchorMarker.style.borderBottomLeftRadius = 5f;
            battlefieldAnchorMarker.style.borderBottomRightRadius = 5f;
            battlefieldAnchorMarker.style.left = FieldViewportWidth * 0.5f - 5f;
            battlefieldAnchorMarker.style.top = FieldViewportHeight * FieldAnchorY - 5f;
            battlefieldViewport.Add(battlefieldAnchorMarker);
            card.Add(battlefieldViewport);
            RegisterArtDrop(card, UnitArtKind.Battlefield);

            card.Add(BuildArtButtons(UnitArtKind.Battlefield));
            card.Add(SpriteField(unit, "battlefieldSprite"));
            SerializedProperty offset = unit.FindPropertyRelative("battlefieldOffset");
            card.Add(FloatSlider(unit.FindPropertyRelative("battlefieldScale"), "Масштаб", 0.1f, 4f, "Размер миниатюры на поле."));
            card.Add(FloatSlider(offset.FindPropertyRelative("x"), "Сдвиг X", -200f, 200f, "Сдвиг по горизонтали, пиксели поля."));
            card.Add(FloatSlider(offset.FindPropertyRelative("y"), "Сдвиг Y", -200f, 200f, "Сдвиг по вертикали, пиксели поля."));
            card.Add(Hint(!string.IsNullOrEmpty(data.AnimationSetId)
                ? "У существа есть набор анимаций: в бою размер и опору задаёт набор, а эта миниатюра — запасная."
                : "Центр гекса на 15% выше нижнего края рамки. У противника миниатюра в бою отражена."));
            return card;
        }

        // Земля и гекс в предпросмотре миниатюры: где центр гекса и где «пол».
        private void DrawFieldGround(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            Vector2 center = new Vector2(FieldViewportWidth * 0.5f, FieldViewportHeight * FieldAnchorY);
            painter.strokeColor = new Color(0.95f, 0.85f, 0.45f, 0.45f);
            painter.lineWidth = 1.2f;
            painter.BeginPath();
            float radius = FieldBaseBoxSize / 1.35f;
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i - 30f);
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.75f);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void BuildTagToggles(VisualElement parent, SerializedProperty selectedTags)
        {
            VisualElement container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;

            for (int i = 0; i < database.Tags.Count; i++)
            {
                UnitTagDefinition tag = database.Tags[i];
                Toggle toggle = new Toggle(tag.DisplayLabel);
                toggle.tooltip = tag.Description;
                toggle.value = SerializedListContains(selectedTags, tag.Id);
                toggle.style.width = 145f;
                toggle.style.marginBottom = 4f;
                toggle.labelElement.style.minWidth = 0f;
                toggle.labelElement.style.width = 112f;
                toggle.labelElement.style.whiteSpace = WhiteSpace.Normal;
                string capturedId = tag.Id;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    serializedDatabase.Update();
                    SerializedProperty currentUnit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
                    SerializedProperty currentTags = currentUnit.FindPropertyRelative("tagIds");
                    SetSerializedListValue(currentTags, capturedId, evt.newValue);
                    serializedDatabase.ApplyModifiedProperties();
                    EditorUtility.SetDirty(database);
                    RefreshUnitList();
                });
                container.Add(toggle);
            }

            parent.Add(container);
        }

        private void RefreshPreviews()
        {
            if (portraitPreview == null || battlefieldPreview == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }

            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            portraitPreview.SetPortrait(unit);

            // Как на поле: квадратная рамка × масштаб, центр гекса на 15% выше
            // её нижнего края, затем смещение.
            battlefieldPreview.sprite = unit.BattlefieldSprite;
            float box = FieldBaseBoxSize * Mathf.Max(0.1f, unit.BattlefieldScale);
            Vector2 anchor = new Vector2(FieldViewportWidth * 0.5f, FieldViewportHeight * FieldAnchorY);
            battlefieldPreview.style.width = box;
            battlefieldPreview.style.height = box;
            battlefieldPreview.style.left = anchor.x - box * 0.5f + unit.BattlefieldOffset.x;
            battlefieldPreview.style.top = anchor.y - box * 0.85f + unit.BattlefieldOffset.y;
            battlefieldAnchorMarker?.BringToFront();
        }

        // ПР-12З: тот же предпросмотр, что в Базе анимаций. Клипы правятся
        // в Базе анимаций; карточка сразу показывает результат.
        private void BuildAnimationCard(VisualElement parent)
        {
            animationDatabase = CreatureAnimationEditorData.LoadOrCreate();

            VisualElement card = Card("АНИМАЦИЯ НА ПОЛЕ", 330f);
            animationSetRow = new VisualElement();
            card.Add(animationSetRow);

            animationPreview = new CreatureAnimationPreviewElement(250f) { AllowPivotEditing = false };
            animationPreview.style.flexGrow = 0f;
            card.Add(animationPreview);
            parent.Add(card);
            RefreshAnimationCard();
        }

        // ------------------------------------------------------------------
        // Загрузка и замена портрета и полевой миниатюры (как в Базе анимаций).
        // ------------------------------------------------------------------

        private static string ArtProperty(UnitArtKind kind)
        {
            return kind == UnitArtKind.Portrait ? "portrait" : "battlefieldSprite";
        }

        private VisualElement BuildArtButtons(UnitArtKind kind)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 6f;
            row.style.marginLeft = 3f;
            Button load = new Button(() => LoadArtFromDialog(kind))
            {
                text = kind == UnitArtKind.Portrait ? "ЗАГРУЗИТЬ ПОРТРЕТ…" : "ЗАГРУЗИТЬ МИНИАТЮРУ…",
                tooltip = "PNG или JPG с диска: файл копируется в проект и импортируется как Sprite. " +
                          "Можно и просто перетащить файл на карточку предпросмотра ниже."
            };
            Button clear = new Button(() => AssignArt(kind, null))
            {
                text = "УБРАТЬ",
                tooltip = "Убрать картинку у существа. Файл остаётся в проекте; отменить можно через Undo."
            };
            row.Add(load);
            row.Add(clear);
            return row;
        }

        private void LoadArtFromDialog(UnitArtKind kind)
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                kind == UnitArtKind.Portrait ? "Портрет существа" : "Миниатюра на поле",
                SessionState.GetString("KS.UnitDatabase.LastArtFolder", string.Empty),
                new[] { "Изображения", "png,jpg,jpeg" });
            if (string.IsNullOrEmpty(path))
                return;
            SessionState.SetString("KS.UnitDatabase.LastArtFolder", Path.GetDirectoryName(path) ?? string.Empty);
            ImportArtFile(kind, path);
        }

        private void ImportArtFile(UnitArtKind kind, string path)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
                return;
            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            if (!ConfirmReplace(kind, unit))
                return;
            Sprite sprite = UnitArtImporter.ImportExternal(path, unit.Id, kind, out string error);
            if (sprite == null)
            {
                EditorUtility.DisplayDialog("Картинка не загружена", error ?? "Неизвестная ошибка.", "Закрыть");
                return;
            }
            AssignArt(kind, sprite);
        }

        private bool ConfirmReplace(UnitArtKind kind, UnitDefinitionData unit)
        {
            Sprite current = kind == UnitArtKind.Portrait ? unit.Portrait : unit.BattlefieldSprite;
            return current == null || EditorUtility.DisplayDialog(
                "Заменить " + UnitArtImporter.KindTitle(kind) + "?",
                (string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.Id : unit.DisplayLabel) + ": сейчас «" + current.name +
                "». Прежний файл останется в проекте; отменить можно через Undo.",
                "Заменить",
                "Отмена");
        }

        // Назначение через SerializedProperty: Undo, сохранение и привязанные
        // поля работают как у остальных полей базы.
        private void AssignArt(UnitArtKind kind, Sprite sprite)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;
            serializedDatabase.Update();
            SerializedProperty property = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex)
                .FindPropertyRelative(ArtProperty(kind));
            if (property.objectReferenceValue == sprite)
                return;
            property.objectReferenceValue = sprite;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
            unitList.RefreshItems();
            RefreshPreviews();
            RefreshAnimationPreviewData();
        }

        private void RegisterArtDrop(VisualElement card, UnitArtKind kind)
        {
            void Highlight(bool on)
            {
                Color color = on ? new Color(0.88f, 0.71f, 0.38f, 1f) : new Color(0f, 0f, 0f, 0f);
                float width = on ? 2f : 0f;
                card.style.borderLeftWidth = width;
                card.style.borderRightWidth = width;
                card.style.borderTopWidth = width;
                card.style.borderBottomWidth = width;
                card.style.borderLeftColor = color;
                card.style.borderRightColor = color;
                card.style.borderTopColor = color;
                card.style.borderBottomColor = color;
            }

            card.tooltip = "Перетащите сюда PNG/JPG или Sprite, чтобы загрузить " + UnitArtImporter.KindTitle(kind) + ".";
            card.RegisterCallback<DragEnterEvent>(_ => Highlight(HasDroppableArt()));
            card.RegisterCallback<DragLeaveEvent>(_ => Highlight(false));
            card.RegisterCallback<DragExitedEvent>(_ => Highlight(false));
            card.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = HasDroppableArt() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            card.RegisterCallback<DragPerformEvent>(evt =>
            {
                Highlight(false);
                if (!HasDroppableArt())
                    return;
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();

                Sprite projectSprite = (DragAndDrop.objectReferences ?? Array.Empty<UnityEngine.Object>())
                    .Select(UnitArtImporter.FromProjectObject)
                    .FirstOrDefault(sprite => sprite != null);
                if (projectSprite != null)
                {
                    if (selectedUnitIndex >= 0 && ConfirmReplace(kind, database.Units[selectedUnitIndex]))
                        AssignArt(kind, projectSprite);
                    return;
                }
                string file = (DragAndDrop.paths ?? Array.Empty<string>()).FirstOrDefault(UnitArtImporter.IsImageFile);
                if (file != null)
                    ImportArtFile(kind, Path.IsPathRooted(file) ? file : UnitArtImporter.ToAbsolute(file));
            });
        }

        private static bool HasDroppableArt()
        {
            if ((DragAndDrop.objectReferences ?? Array.Empty<UnityEngine.Object>()).Any(item => item is Sprite || item is Texture2D))
                return true;
            return (DragAndDrop.paths ?? Array.Empty<string>()).Any(UnitArtImporter.IsImageFile);
        }

        private void CleanUnusedArt()
        {
            List<string> unused = UnitArtImporter.FindUnused(database);
            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Картинки существ", "Неиспользуемых загруженных картинок нет.", "Хорошо");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Убрать неиспользуемые картинки?",
                    "Эти портреты и миниатюры остались от прежних загрузок и не нужны ни одному существу:\n" +
                    string.Join("\n", unused.Take(12).Select(Path.GetFileName)) +
                    (unused.Count > 12 ? "\n…и ещё " + (unused.Count - 12) : string.Empty) +
                    "\n\nФайлы уйдут в корзину. После этого Undo прежних замен не вернёт эти картинки.",
                    "В корзину",
                    "Отмена"))
            {
                return;
            }
            List<string> failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(unused.ToArray(), failed);
            validationLabel.text = "Убрано картинок: " + (unused.Count - failed.Count);
        }

        private void RefreshAnimationCard()
        {
            if (animationSetRow == null || database == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }

            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            CreatureAnimationSetData set = animationDatabase != null ? animationDatabase.FindSet(unit.AnimationSetId) : null;
            animationSetRow.Clear();

            List<string> setIds = new List<string> { string.Empty };
            if (animationDatabase != null)
                setIds.AddRange(animationDatabase.Sets.Where(item => item != null).Select(item => item.Id));
            if (!setIds.Contains(unit.AnimationSetId))
                setIds.Add(unit.AnimationSetId);
            PopupField<string> setField = new PopupField<string>(
                "Набор анимаций",
                setIds,
                unit.AnimationSetId,
                DescribeAnimationSet,
                DescribeAnimationSet);
            setField.tooltip = "Набор из Базы анимаций. Один набор можно дать нескольким существам.";
            setField.RegisterValueChangedCallback(evt => AssignAnimationSet(evt.newValue));
            animationSetRow.Add(setField);

            Label note = new Label(set != null
                ? "Используют: " + CreatureAnimationEditorData.DescribeUsers(database, set.Id) +
                  ". Размер и опору на поле задаёт набор; «Масштаб миниатюры» и «Смещение миниатюры» для этого существа не применяются."
                : string.IsNullOrEmpty(unit.AnimationSetId)
                    ? "Набора нет: в бою статичная миниатюра, без неё — жетон."
                    : "Набор «" + unit.AnimationSetId + "» не найден в Базе анимаций: в бою статичная миниатюра.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 10f;
            note.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            animationSetRow.Add(note);

            Button open = new Button(() => CreatureAnimationDatabaseWindow.Open(unit.Id))
            {
                text = "ОТКРЫТЬ В БАЗЕ АНИМАЦИЙ"
            };
            open.style.marginTop = 4f;
            animationSetRow.Add(open);

            RefreshAnimationPreviewData();
        }

        private void RefreshAnimationPreviewData()
        {
            if (animationPreview == null || database == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }
            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            CreatureAnimationSetData set = animationDatabase != null ? animationDatabase.FindSet(unit.AnimationSetId) : null;
            animationPreview.SetData(animationDatabase, set, unit.BattlefieldSprite);
        }

        private string DescribeAnimationSet(string setId)
        {
            if (string.IsNullOrEmpty(setId))
                return "— нет —";
            CreatureAnimationSetData set = animationDatabase != null ? animationDatabase.FindSet(setId) : null;
            return set == null
                ? setId + " (не найден)"
                : set.DisplayName + " [" + set.Id + "] · " + CreatureAnimationLabels.StatusTitle(set.Status);
        }

        private void AssignAnimationSet(string setId)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;
            serializedDatabase.Update();
            unitsProperty.GetArrayElementAtIndex(selectedUnitIndex)
                .FindPropertyRelative("animationSetId").stringValue = setId ?? string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            CreatureAnimationEditorData.NotifyChanged();
            RefreshAnimationCard();
        }

        private void BeginPortraitDrag(PointerDownEvent evt)
        {
            if (evt.button != 0 || portraitPreview == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
            {
                return;
            }

            serializedDatabase.Update();
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            portraitDragStartOffset = unit
                .FindPropertyRelative("portraitOffsetNormalized")
                .vector2Value;
            portraitDragStartPointer = new Vector2(evt.position.x, evt.position.y);
            portraitDragPointerId = evt.pointerId;
            portraitDragging = true;

            Undo.RecordObject(database, "Pan Unit Portrait");
            portraitPreview.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void ContinuePortraitDrag(PointerMoveEvent evt)
        {
            if (!portraitDragging || portraitPreview == null ||
                evt.pointerId != portraitDragPointerId ||
                selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
            {
                return;
            }

            float width = Mathf.Max(1f, portraitPreview.contentRect.width);
            float height = Mathf.Max(1f, portraitPreview.contentRect.height);
            Vector2 pointer = new Vector2(evt.position.x, evt.position.y);
            Vector2 delta = pointer - portraitDragStartPointer;
            Vector2 normalized = portraitDragStartOffset + new Vector2(
                delta.x / width,
                delta.y / height);

            serializedDatabase.Update();
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            unit.FindPropertyRelative("portraitOffsetNormalized").vector2Value = normalized;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshPreviews();
            unitList.RefreshItems();
            evt.StopPropagation();
        }

        private void EndPortraitDrag(PointerUpEvent evt)
        {
            if (!portraitDragging || evt.pointerId != portraitDragPointerId)
                return;

            if (portraitPreview != null && portraitPreview.HasPointerCapture(evt.pointerId))
                portraitPreview.ReleasePointer(evt.pointerId);
            CancelPortraitDrag();
            evt.StopPropagation();
        }

        private void CancelPortraitDrag()
        {
            portraitDragging = false;
            portraitDragPointerId = -1;
        }

        private void ResetPortraitFraming()
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;

            serializedDatabase.Update();
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            unit.FindPropertyRelative("portraitFitMode").enumValueIndex = (int)PortraitFitMode.Cover;
            unit.FindPropertyRelative("portraitScale").floatValue = 1f;
            unit.FindPropertyRelative("portraitOffsetNormalized").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("portraitFlipX").boolValue = false;
            unit.FindPropertyRelative("portraitOffset").vector2Value = Vector2.zero;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            ShowSelectedUnit();
            unitList.RefreshItems();
        }

        private void AddUnit()
        {
            serializedDatabase.Update();
            int index = unitsProperty.arraySize;
            unitsProperty.arraySize++;
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(index);
            unit.FindPropertyRelative("id").stringValue = MakeUniqueUnitId("new_unit");
            unit.FindPropertyRelative("displayLabel").stringValue = "Новый тип";
            unit.FindPropertyRelative("category").enumValueIndex = (int)UnitCategory.Fighter;
            unit.FindPropertyRelative("combatRole").enumValueIndex = (int)UnitCombatRole.Custom;
            unit.FindPropertyRelative("maxHitPoints").intValue = 100;
            unit.FindPropertyRelative("attack").intValue = 1;
            unit.FindPropertyRelative("defense").intValue = 1;
            unit.FindPropertyRelative("damage").intValue = 10;
            unit.FindPropertyRelative("movement").intValue = 3;
            unit.FindPropertyRelative("initiative").intValue = 1;
            unit.FindPropertyRelative("attackRange").intValue = 1;
            unit.FindPropertyRelative("portrait").objectReferenceValue = null;
            unit.FindPropertyRelative("portraitFitMode").enumValueIndex = (int)PortraitFitMode.Cover;
            unit.FindPropertyRelative("portraitScale").floatValue = 1f;
            unit.FindPropertyRelative("portraitOffsetNormalized").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("portraitFlipX").boolValue = false;
            unit.FindPropertyRelative("portraitOffset").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("battlefieldSprite").objectReferenceValue = null;
            unit.FindPropertyRelative("battlefieldScale").floatValue = 1f;
            unit.FindPropertyRelative("battlefieldOffset").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("animationSetId").stringValue = string.Empty;
            unit.FindPropertyRelative("tagIds").ClearArray();
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedUnitIndex = index;
            RefreshUnitList();
            RestoreSelection();
        }

        private void DuplicateSelectedUnit()
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;

            serializedDatabase.Update();
            unitsProperty.InsertArrayElementAtIndex(selectedUnitIndex);
            int duplicateIndex = selectedUnitIndex + 1;
            SerializedProperty duplicate = unitsProperty.GetArrayElementAtIndex(duplicateIndex);
            string sourceId = duplicate.FindPropertyRelative("id").stringValue;
            duplicate.FindPropertyRelative("id").stringValue = MakeUniqueUnitId(sourceId + "_copy");
            duplicate.FindPropertyRelative("displayLabel").stringValue += " — копия";
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedUnitIndex = duplicateIndex;
            RefreshUnitList();
            RestoreSelection();
        }

        private void DeleteSelectedUnit()
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;

            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            if (!EditorUtility.DisplayDialog(
                    "Удалить тип существа?",
                    "Будет удалён тип " + unit.DisplayLabel + " [" + unit.Id + "].",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }

            serializedDatabase.Update();
            unitsProperty.DeleteArrayElementAtIndex(selectedUnitIndex);
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedUnitIndex = Mathf.Clamp(selectedUnitIndex - 1, -1, unitsProperty.arraySize - 1);
            RefreshUnitList();
            RestoreSelection();
        }

        private void AddTag()
        {
            serializedDatabase.Update();
            int index = tagsProperty.arraySize;
            tagsProperty.arraySize++;
            SerializedProperty tag = tagsProperty.GetArrayElementAtIndex(index);
            tag.FindPropertyRelative("id").stringValue = MakeUniqueTagId("new.tag");
            tag.FindPropertyRelative("displayLabel").stringValue = "Новый тег";
            tag.FindPropertyRelative("category").stringValue = "Прочее";
            tag.FindPropertyRelative("color").colorValue = Color.gray;
            tag.FindPropertyRelative("description").stringValue = string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshTagFilter();
            if (selectedUnitIndex >= 0)
                ShowSelectedUnit();
        }

        private void ValidateDatabase()
        {
            serializedDatabase.ApplyModifiedProperties();
            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            List<string> artGaps = new List<string>();
            database.CollectArtGaps(artGaps);
            string artLine = artGaps.Count == 0
                ? string.Empty
                : "\n\nЖдут рисунка (бой рисует жетон): " + artGaps.Count + ".";
            if (issues.Count == 0)
            {
                validationLabel.text = artGaps.Count == 0
                    ? "Ошибок не найдено"
                    : "Ошибок нет · ждут рисунка: " + artGaps.Count;
                validationLabel.style.color = new Color(0.35f, 0.72f, 0.40f, 1f);
                EditorUtility.DisplayDialog("Проверка базы", "Ошибок не найдено." + artLine, "Хорошо");
                return;
            }

            validationLabel.text = "Замечаний: " + issues.Count;
            validationLabel.style.color = new Color(0.90f, 0.48f, 0.28f, 1f);
            EditorUtility.DisplayDialog(
                "Проверка базы",
                string.Join("\n", issues.Take(18)) +
                (issues.Count > 18 ? "\n…и ещё " + (issues.Count - 18) : string.Empty),
                "Закрыть");
        }

        private string MakeUniqueUnitId(string seed)
        {
            string candidate = seed;
            int suffix = 2;
            while (database.FindById(candidate) != null)
                candidate = seed + "_" + suffix++;
            return candidate;
        }

        private string MakeUniqueTagId(string seed)
        {
            string candidate = seed;
            int suffix = 2;
            while (database.FindTag(candidate) != null)
                candidate = seed + "_" + suffix++;
            return candidate;
        }

        private string GetSelectedTagId()
        {
            if (tagFilterField == null || tagFilterField.index <= 0 ||
                tagFilterField.index - 1 >= database.Tags.Count)
            {
                return string.Empty;
            }
            return database.Tags[tagFilterField.index - 1].Id;
        }

        private static bool MatchesSearch(UnitDefinitionData unit, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return (!string.IsNullOrEmpty(unit.Id) &&
                    unit.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(unit.DisplayLabel) &&
                    unit.DisplayLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetCategoryLabel(UnitCategory category)
        {
            switch (category)
            {
                case UnitCategory.Fighter: return "Боец";
                case UnitCategory.Creature: return "Существо";
                case UnitCategory.Commander: return "Командир";
                default: return "Прочее";
            }
        }

        private static bool SerializedListContains(SerializedProperty list, string value)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).stringValue == value)
                    return true;
            }
            return false;
        }

        private static void SetSerializedListValue(
            SerializedProperty list,
            string value,
            bool enabled)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).stringValue != value)
                    continue;
                if (!enabled)
                    list.DeleteArrayElementAtIndex(i);
                return;
            }

            if (!enabled)
                return;
            int index = list.arraySize;
            list.arraySize++;
            list.GetArrayElementAtIndex(index).stringValue = value;
        }
    }
}
