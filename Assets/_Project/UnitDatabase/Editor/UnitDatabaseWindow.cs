using System;
using System.Collections.Generic;
using System.Linq;
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
        private Image portraitPreview;
        private Image battlefieldPreview;
        private Label selectionHint;
        private Label validationLabel;

        [MenuItem("Kingdom Survival/База существ")]
        public static void OpenWindow()
        {
            UnitDatabaseWindow window = GetWindow<UnitDatabaseWindow>();
            window.titleContent = new GUIContent("База существ");
            window.minSize = new Vector2(960f, 620f);
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

            serializedDatabase = new SerializedObject(database);
            unitsProperty = serializedDatabase.FindProperty("units");
            tagsProperty = serializedDatabase.FindProperty("tags");

            BuildToolbar();

            TwoPaneSplitView mainSplit = new TwoPaneSplitView(
                0,
                310f,
                TwoPaneSplitViewOrientation.Horizontal);
            mainSplit.style.flexGrow = 1f;
            rootVisualElement.Add(mainSplit);

            mainSplit.Add(BuildListPane());
            mainSplit.Add(BuildRightPane());

            RefreshTagFilter();
            RefreshUnitList();
            RestoreSelection();
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

            foreach (Button button in new[] { add, duplicate, remove, addTag, validate })
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

        private VisualElement BuildListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 8f;
            pane.style.paddingRight = 8f;
            pane.style.paddingTop = 8f;
            pane.style.paddingBottom = 8f;

            searchField = new TextField { label = "Поиск" };
            searchField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(searchField);

            categoryField = new PopupField<string>(
                "Категория",
                categoryChoices,
                0);
            categoryField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(categoryField);

            tagFilterField = new PopupField<string>(
                "Тег",
                new List<string> { "Все теги" },
                0);
            tagFilterField.RegisterValueChangedCallback(_ => RefreshUnitList());
            pane.Add(tagFilterField);

            unitList = new ListView();
            unitList.style.flexGrow = 1f;
            unitList.style.marginTop = 8f;
            unitList.fixedItemHeight = 60f;
            unitList.selectionType = SelectionType.Single;
            unitList.makeItem = CreateUnitListItem;
            unitList.bindItem = BindUnitListItem;
            unitList.selectionChanged += _ => SelectVisibleUnit(unitList.selectedIndex);
            pane.Add(unitList);

            Label explanation = new Label(
                "ID определяет тип. Личное имя не хранится. " +
                "Индивидуальное развитие экземпляра будет находиться в сохранении.");
            explanation.style.whiteSpace = WhiteSpace.Normal;
            explanation.style.fontSize = 10f;
            explanation.style.marginTop = 6f;
            explanation.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            pane.Add(explanation);
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
            thumbnailFrame.style.width = 39f;
            thumbnailFrame.style.height = 52f;
            thumbnailFrame.style.flexShrink = 0f;
            thumbnailFrame.style.marginRight = 8f;
            thumbnailFrame.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 1f);
            thumbnailFrame.style.overflow = Overflow.Hidden;

            Image thumbnail = new Image
            {
                name = "thumbnail",
                scaleMode = ScaleMode.ScaleAndCrop,
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
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Label id = new Label { name = "id" };
            id.style.fontSize = 10f;
            id.style.color = new Color(0.60f, 0.60f, 0.60f, 1f);
            text.Add(title);
            text.Add(id);
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
            element.Q<Label>("id").text = unit.Id + " · " + GetCategoryLabel(unit.Category);

            Image thumbnail = element.Q<Image>("thumbnail");
            thumbnail.sprite = unit.Portrait;
            ApplyImageFraming(thumbnail, unit.PortraitScale, unit.PortraitOffset);
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

        private void ShowSelectedUnit()
        {
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
            AddHeader("ИДЕНТИФИКАЦИЯ ТИПА");
            AddField(unit, "id", "ID типа");
            AddField(unit, "displayLabel", "Название типа");
            AddField(unit, "category", "Категория");
            AddField(unit, "combatRole", "Боевая роль");

            AddHeader("БОЕВЫЕ ХАРАКТЕРИСТИКИ");
            AddField(unit, "maxHitPoints", "HP");
            AddField(unit, "attack", "Атака");
            AddField(unit, "defense", "Защита");
            AddField(unit, "damage", "Урон");
            AddField(unit, "movement", "Ход");
            AddField(unit, "initiative", "Инициатива");
            AddField(unit, "attackRange", "Дальность");

            AddHeader("ИЗОБРАЖЕНИЯ");
            AddField(unit, "portrait", "Портрет");
            AddField(unit, "portraitScale", "Масштаб портрета");
            AddField(unit, "portraitOffset", "Смещение портрета X / Y");

            Button resetPortrait = new Button(ResetPortraitFraming)
            {
                text = "СБРОСИТЬ КАДРИРОВАНИЕ ПОРТРЕТА"
            };
            resetPortrait.style.marginTop = 4f;
            resetPortrait.style.marginBottom = 6f;
            detailPane.Add(resetPortrait);

            Label portraitHint = new Label(
                "Портрет заполняет вертикальную рамку 3:4 через ScaleAndCrop. " +
                "Рекомендуемый исходник — 900×1200 или 1200×1600. " +
                "Квадратные изображения тоже поддерживаются и автоматически обрезаются по краям.");
            portraitHint.style.whiteSpace = WhiteSpace.Normal;
            portraitHint.style.fontSize = 10f;
            portraitHint.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            portraitHint.style.marginBottom = 8f;
            detailPane.Add(portraitHint);

            AddField(unit, "battlefieldSprite", "Миниатюра на поле");
            AddField(unit, "battlefieldScale", "Масштаб миниатюры");
            AddField(unit, "battlefieldOffset", "Смещение миниатюры X / Y");
            AddField(unit, "sandboxEncounterCount", "Количество в тестовой засаде");

            AddHeader("ТЕГИ");
            BuildTagToggles(unit.FindPropertyRelative("tagIds"));

            AddHeader("ПРЕДПРОСМОТР");
            VisualElement previews = new VisualElement();
            previews.style.flexDirection = FlexDirection.Row;
            previews.style.height = 250f;

            VisualElement portraitCard = CreatePreview(
                "ПОРТРЕТ · 3:4",
                150f,
                200f,
                ScaleMode.ScaleAndCrop,
                out portraitPreview);
            VisualElement battlefieldCard = CreatePreview(
                "ПОЛЕВАЯ МИНИАТЮРА",
                150f,
                200f,
                ScaleMode.ScaleToFit,
                out battlefieldPreview);
            previews.Add(portraitCard);
            previews.Add(battlefieldCard);
            detailPane.Add(previews);

            Foldout tagEditor = new Foldout { text = "Настройка справочника тегов" };
            PropertyField tagDefinitions = new PropertyField(tagsProperty, "Теги базы");
            tagEditor.Add(tagDefinitions);
            detailPane.Add(tagEditor);

            detailPane.Bind(serializedDatabase);
            RefreshPreviews();
        }

        private void AddHeader(string text)
        {
            Label header = new Label(text);
            header.style.marginTop = 12f;
            header.style.marginBottom = 5f;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new Color(0.80f, 0.66f, 0.34f, 1f);
            detailPane.Add(header);
        }

        private void AddField(SerializedProperty owner, string propertyName, string label)
        {
            SerializedProperty property = owner.FindPropertyRelative(propertyName);
            PropertyField field = new PropertyField(property, label);
            detailPane.Add(field);
        }

        private void BuildTagToggles(SerializedProperty selectedTags)
        {
            VisualElement container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;

            for (int i = 0; i < database.Tags.Count; i++)
            {
                UnitTagDefinition tag = database.Tags[i];
                Toggle toggle = new Toggle(tag.DisplayLabel);
                toggle.tooltip = tag.Id + "\n" + tag.Description;
                toggle.value = SerializedListContains(selectedTags, tag.Id);
                toggle.style.marginRight = 12f;
                toggle.style.marginBottom = 5f;
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

            detailPane.Add(container);
        }

        private static VisualElement CreatePreview(
            string label,
            float frameWidth,
            float frameHeight,
            ScaleMode scaleMode,
            out Image image)
        {
            VisualElement card = new VisualElement();
            card.style.width = Mathf.Max(frameWidth + 24f, 190f);
            card.style.height = frameHeight + 42f;
            card.style.marginRight = 12f;
            card.style.paddingLeft = 8f;
            card.style.paddingRight = 8f;
            card.style.paddingTop = 8f;
            card.style.paddingBottom = 8f;
            card.style.alignItems = Align.Center;
            card.style.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);

            Label title = new Label(label);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(title);

            VisualElement viewport = new VisualElement();
            viewport.style.width = frameWidth;
            viewport.style.height = frameHeight;
            viewport.style.marginTop = 6f;
            viewport.style.position = Position.Relative;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            card.Add(viewport);

            image = new Image
            {
                scaleMode = scaleMode,
                pickingMode = PickingMode.Ignore
            };
            image.style.position = Position.Absolute;
            image.style.left = 0f;
            image.style.right = 0f;
            image.style.top = 0f;
            image.style.bottom = 0f;
            viewport.Add(image);
            return card;
        }

        private void RefreshPreviews()
        {
            if (portraitPreview == null || battlefieldPreview == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }

            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            portraitPreview.sprite = unit.Portrait;
            ApplyImageFraming(portraitPreview, unit.PortraitScale, unit.PortraitOffset);

            battlefieldPreview.sprite = unit.BattlefieldSprite;
            ApplyImageFraming(
                battlefieldPreview,
                unit.BattlefieldScale,
                unit.BattlefieldOffset);
        }

        private static void ApplyImageFraming(Image image, float scale, Vector2 offset)
        {
            if (image == null)
                return;

            float safeScale = Mathf.Max(0.1f, scale);
            image.style.scale = new Scale(new Vector3(safeScale, safeScale, 1f));
            image.transform.position = new Vector3(offset.x, offset.y, 0f);
        }

        private void ResetPortraitFraming()
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;

            serializedDatabase.Update();
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            unit.FindPropertyRelative("portraitScale").floatValue = 1f;
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
            unit.FindPropertyRelative("portraitScale").floatValue = 1f;
            unit.FindPropertyRelative("portraitOffset").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("battlefieldSprite").objectReferenceValue = null;
            unit.FindPropertyRelative("battlefieldScale").floatValue = 1f;
            unit.FindPropertyRelative("battlefieldOffset").vector2Value = Vector2.zero;
            unit.FindPropertyRelative("sandboxEncounterCount").intValue = 0;
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
            if (issues.Count == 0)
            {
                validationLabel.text = "Ошибок не найдено";
                validationLabel.style.color = new Color(0.35f, 0.72f, 0.40f, 1f);
                EditorUtility.DisplayDialog("Проверка базы", "Ошибок не найдено.", "Хорошо");
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
