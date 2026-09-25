using System;
using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    public sealed partial class EncounterDatabaseWindow
    {
        // Фильтр списка: пусто — показывать всё.
        private readonly HashSet<EncounterStatus> statusFilter = new HashSet<EncounterStatus>();
        private readonly HashSet<EncounterCategory> categoryFilter = new HashSet<EncounterCategory>();
        private readonly HashSet<EncounterDurationClass> durationFilter = new HashSet<EncounterDurationClass>();

        private readonly List<int> visibleEncounterIndices = new List<int>();
        private TextField encounterSearch;
        private Button filterButton;
        private ListView encounterList;
        private ScrollView encounterDetails;
        private Label encounterEmptyHint;
        private Label validationLabel;
        private VisualElement validationPanel;
        private readonly List<string> validationIssues = new List<string>();

        // Раскрытые пустые списки карточки (показаны после «+ …»).
        private readonly HashSet<string> openSections = new HashSet<string>(StringComparer.Ordinal);
        private Label whenSentence;
        private VisualElement eligibilityResult;

        private void BuildEncountersTab(VisualElement root)
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 30f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 6f;
            toolbar.style.paddingRight = 6f;
            AddToolbarButton(toolbar, "+ Новая встреча", AddEncounter);
            Button more = new Button { text = "⋯", tooltip = "Дублировать, списать, удалить" };
            more.style.height = 22f;
            more.clicked += () =>
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Дублировать"), false, DuplicateEncounter);
                menu.AddItem(new GUIContent("Списать (черновик — удалить)"), false, DeprecateEncounter);
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Удалить из базы…"), false, DeleteEncounterPermanently);
                menu.DropDown(more.worldBound);
            };
            toolbar.Add(more);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            validationLabel = new Label("Проверить базу");
            validationLabel.tooltip = "Проверить все встречи и связанные диалоги";
            validationLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            validationLabel.style.color = MutedColor;
            validationLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (validationIssues.Count > 0 && validationPanel.style.display == DisplayStyle.None)
                    validationPanel.style.display = DisplayStyle.Flex;
                else
                    ValidateEncounterDatabase();
            });
            toolbar.Add(validationLabel);
            root.Add(toolbar);

            validationPanel = new ScrollView();
            validationPanel.style.display = DisplayStyle.None;
            validationPanel.style.maxHeight = 160f;
            validationPanel.style.paddingLeft = 8f;
            validationPanel.style.paddingRight = 8f;
            root.Add(validationPanel);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 250f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(BuildEncounterListPane());
            split.Add(BuildEncounterDetailPane());
            root.Add(split);

            RefreshEncounterList();
            RestoreEncounterSelection();
            ValidateEncounterDatabase(false);
        }

        // ---------------------------------------------------------------
        // Список
        // ---------------------------------------------------------------

        private VisualElement BuildEncounterListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;

            VisualElement searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            encounterSearch = new TextField { tooltip = "Поиск по названию, ID, пулу и тегам" };
            encounterSearch.textEdition.placeholder = "Поиск";
            encounterSearch.style.flexGrow = 1f;
            encounterSearch.RegisterValueChangedCallback(_ => RefreshEncounterList());
            searchRow.Add(encounterSearch);
            filterButton = new Button(ShowFilterMenu) { text = "Фильтр ▾" };
            searchRow.Add(filterButton);
            pane.Add(searchRow);

            encounterList = new ListView();
            encounterList.style.flexGrow = 1f;
            encounterList.style.marginTop = 6f;
            encounterList.fixedItemHeight = 40f;
            encounterList.selectionType = SelectionType.Single;
            encounterList.makeItem = MakeEncounterListItem;
            encounterList.bindItem = BindEncounterListItem;
            encounterList.selectionChanged += _ => SelectVisibleEncounter(encounterList.selectedIndex);
            pane.Add(encounterList);
            return pane;
        }

        private void ShowFilterMenu()
        {
            GenericMenu menu = new GenericMenu();
            foreach (EncounterStatus status in Enum.GetValues(typeof(EncounterStatus)))
            {
                EncounterStatus captured = status;
                menu.AddItem(new GUIContent("Статус/" + EncounterEditorLabels.Status(status)), statusFilter.Contains(status),
                    () => ToggleFilter(statusFilter, captured));
            }
            foreach (EncounterCategory category in Enum.GetValues(typeof(EncounterCategory)))
            {
                EncounterCategory captured = category;
                menu.AddItem(new GUIContent("Где/" + EncounterEditorLabels.Category(category)), categoryFilter.Contains(category),
                    () => ToggleFilter(categoryFilter, captured));
            }
            foreach (EncounterDurationClass duration in Enum.GetValues(typeof(EncounterDurationClass)))
            {
                EncounterDurationClass captured = duration;
                menu.AddItem(new GUIContent("Длительность/" + EncounterEditorLabels.Duration(duration)), durationFilter.Contains(duration),
                    () => ToggleFilter(durationFilter, captured));
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Сбросить фильтр"), false, () =>
            {
                statusFilter.Clear();
                categoryFilter.Clear();
                durationFilter.Clear();
                RefreshEncounterList();
            });
            menu.DropDown(filterButton.worldBound);
        }

        private void ToggleFilter<T>(HashSet<T> set, T value)
        {
            if (!set.Remove(value))
                set.Add(value);
            RefreshEncounterList();
        }

        private static VisualElement MakeEncounterListItem()
        {
            VisualElement row = new VisualElement();
            row.style.paddingTop = 3f;
            row.style.paddingLeft = 2f;

            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(title);

            Label meta = new Label { name = "meta" };
            meta.style.fontSize = 10f;
            meta.style.color = MutedColor;
            row.Add(meta);
            return row;
        }

        private void BindEncounterListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleEncounterIndices.Count)
                return;

            EncounterDefinition encounter = database.Encounters[visibleEncounterIndices[visibleIndex]];
            Label title = row.Q<Label>("title");
            title.text = StatusDot(encounter.Status) + " " + (string.IsNullOrWhiteSpace(encounter.DisplayName) ? encounter.EncounterId : encounter.DisplayName);
            title.tooltip = EncounterEditorLabels.Status(encounter.Status);
            row.Q<Label>("meta").text = showProduction
                ? encounter.EncounterId + " · " + EncounterEditorLabels.DescribeListLine(encounter)
                : EncounterEditorLabels.DescribeListLine(encounter);
        }

        private static string StatusDot(EncounterStatus status)
        {
            switch (status)
            {
                case EncounterStatus.Production: return "●";
                case EncounterStatus.Draft: return "○";
                case EncounterStatus.Disabled: return "◐";
                case EncounterStatus.Deprecated: return "✕";
                default: return "?";
            }
        }

        private void RefreshEncounterList()
        {
            if (database == null || encounterList == null)
                return;

            serializedDatabase.Update();
            visibleEncounterIndices.Clear();
            string query = encounterSearch != null ? encounterSearch.value.Trim() : string.Empty;

            for (int i = 0; i < database.Encounters.Count; i++)
            {
                EncounterDefinition encounter = database.Encounters[i];
                if (encounter == null)
                    continue;
                if (statusFilter.Count > 0 && !statusFilter.Contains(encounter.Status))
                    continue;
                if (categoryFilter.Count > 0 && !categoryFilter.Contains(encounter.Category))
                    continue;
                if (durationFilter.Count > 0 && !durationFilter.Contains(encounter.DurationClass))
                    continue;
                if (!string.IsNullOrEmpty(query) && !MatchesQuery(encounter, query))
                    continue;
                visibleEncounterIndices.Add(i);
            }

            int filters = statusFilter.Count + categoryFilter.Count + durationFilter.Count;
            if (filterButton != null)
                filterButton.text = filters > 0 ? "Фильтр (" + filters + ") ▾" : "Фильтр ▾";

            encounterList.itemsSource = visibleEncounterIndices;
            encounterList.Rebuild();
            int visible = visibleEncounterIndices.IndexOf(selectedEncounterIndex);
            if (visible >= 0)
                encounterList.SetSelectionWithoutNotify(new[] { visible });
        }

        private static bool MatchesQuery(EncounterDefinition encounter, string query)
        {
            bool Has(string value) => !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has(encounter.EncounterId) || Has(encounter.DisplayName) || Has(encounter.PoolId) || Has(encounter.DialogueId))
                return true;
            return encounter.Tags != null && encounter.Tags.Exists(Has);
        }

        private void RestoreEncounterSelection()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count)
                selectedEncounterIndex = database.Encounters.Count > 0 ? 0 : -1;
            ShowSelectedEncounter();
        }

        private void SelectVisibleEncounter(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleEncounterIndices.Count)
                return;
            selectedEncounterIndex = visibleEncounterIndices[visibleIndex];
            openSections.Clear();
            ShowSelectedEncounter();
        }

        // ---------------------------------------------------------------
        // Карточка встречи
        // ---------------------------------------------------------------

        private VisualElement BuildEncounterDetailPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;

            encounterEmptyHint = new Label("Выберите встречу слева.");
            encounterEmptyHint.style.flexGrow = 1f;
            encounterEmptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            pane.Add(encounterEmptyHint);

            encounterDetails = new ScrollView();
            encounterDetails.style.display = DisplayStyle.None;
            encounterDetails.style.flexGrow = 1f;
            encounterDetails.style.paddingLeft = 12f;
            encounterDetails.style.paddingRight = 12f;
            encounterDetails.style.paddingTop = 8f;
            encounterDetails.style.paddingBottom = 16f;
            pane.Add(encounterDetails);
            return pane;
        }

        private void ShowSelectedEncounter()
        {
            if (encounterDetails == null)
                return;
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
            {
                encounterEmptyHint.style.display = DisplayStyle.Flex;
                encounterDetails.style.display = DisplayStyle.None;
                return;
            }

            Vector2 scroll = encounterDetails.scrollOffset;
            encounterEmptyHint.style.display = DisplayStyle.None;
            encounterDetails.style.display = DisplayStyle.Flex;
            encounterDetails.Clear();
            serializedDatabase.Update();

            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            BuildCardHeader(e);
            BuildSceneSection(e);
            BuildWhenSection(e);
            BuildAfterSection(e);
            BuildNotesSection(e);
            if (showProduction)
                BuildProductionSection(e);

            encounterDetails.schedule.Execute(() => encounterDetails.scrollOffset = scroll);
        }

        private void Commit()
        {
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            encounterList?.RefreshItems();
            if (whenSentence != null && selectedEncounterIndex >= 0 && selectedEncounterIndex < database.Encounters.Count)
                whenSentence.text = EncounterEditorLabels.DescribeWhen(database.Encounters[selectedEncounterIndex]);
        }

        private void CommitAndRebuild()
        {
            Commit();
            encounterDetails.schedule.Execute(ShowSelectedEncounter);
        }

        private string SectionKey(string name)
        {
            return selectedEncounterIndex + ":" + name;
        }

        // Заголовок: название, статус, где · длительность · функции, описание.
        private void BuildCardHeader(SerializedProperty e)
        {
            VisualElement row = Row();
            TextField title = new TextField { value = e.FindPropertyRelative("DisplayName").stringValue, tooltip = "Название встречи" };
            title.style.flexGrow = 1f;
            title.style.fontSize = 15f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.RegisterValueChangedCallback(evt =>
            {
                e.FindPropertyRelative("DisplayName").stringValue = evt.newValue;
                Commit();
            });
            row.Add(title);
            row.Add(MakeEnumPopup<EncounterStatus>(e.FindPropertyRelative("Status"), null, EncounterEditorLabels.Status, 110f));
            encounterDetails.Add(row);

            VisualElement meta = Row();
            meta.style.marginTop = 2f;
            meta.Add(MakeEnumPopup<EncounterCategory>(e.FindPropertyRelative("Category"), null, EncounterEditorLabels.Category, 100f));
            meta.Add(MakeEnumPopup<EncounterDurationClass>(e.FindPropertyRelative("DurationClass"), null, EncounterEditorLabels.Duration, 130f));
            meta.Add(MakeFunctionsButton(e.FindPropertyRelative("Functions")));
            encounterDetails.Add(meta);

            TextField description = new TextField { value = e.FindPropertyRelative("Description").stringValue, multiline = true, tooltip = "Коротко о сцене — для себя и команды" };
            description.textEdition.placeholder = "Коротко о сцене";
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 4f;
            description.RegisterValueChangedCallback(evt =>
            {
                e.FindPropertyRelative("Description").stringValue = evt.newValue;
                Commit();
            });
            encounterDetails.Add(description);
        }

        private VisualElement MakeFunctionsButton(SerializedProperty functions)
        {
            List<string> selected = new List<string>();
            for (int i = 0; i < functions.arraySize; i++)
                selected.Add(EncounterEditorLabels.Function((EncounterFunction)functions.GetArrayElementAtIndex(i).enumValueIndex));
            Button button = new Button { text = (selected.Count > 0 ? string.Join(", ", selected) : "зачем эта сцена") + " ▾" };
            button.tooltip = "Функции встречи: что она даёт игре";
            button.style.flexShrink = 1f;
            button.clicked += () =>
            {
                GenericMenu menu = new GenericMenu();
                foreach (EncounterFunction function in Enum.GetValues(typeof(EncounterFunction)))
                {
                    EncounterFunction captured = function;
                    int index = IndexOfEnum(functions, (int)function);
                    menu.AddItem(new GUIContent(EncounterEditorLabels.Function(function)), index >= 0, () =>
                    {
                        serializedDatabase.Update();
                        int existing = IndexOfEnum(functions, (int)captured);
                        if (existing >= 0)
                            functions.DeleteArrayElementAtIndex(existing);
                        else
                        {
                            functions.arraySize++;
                            functions.GetArrayElementAtIndex(functions.arraySize - 1).enumValueIndex = (int)captured;
                        }
                        CommitAndRebuild();
                    });
                }
                menu.DropDown(button.worldBound);
            };
            return button;
        }

        private static int IndexOfEnum(SerializedProperty list, int value)
        {
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).enumValueIndex == value)
                    return i;
            return -1;
        }

        // СЦЕНА: связанный диалог, его начало, «▶ Играть».
        private void BuildSceneSection(SerializedProperty e)
        {
            AddHeader(encounterDetails, "СЦЕНА");
            AddDialogueSection(encounterDetails, e.FindPropertyRelative("DialogueId"));
        }

        // КОГДА ВЫПАДАЕТ: фраза + правка шанса, повторов, мест, условий, флагов.
        private void BuildWhenSection(SerializedProperty e)
        {
            AddHeader(encounterDetails, "КОГДА ВЫПАДАЕТ");
            whenSentence = new Label(EncounterEditorLabels.DescribeWhen(database.Encounters[selectedEncounterIndex]));
            whenSentence.style.whiteSpace = WhiteSpace.Normal;
            whenSentence.style.marginBottom = 4f;
            encounterDetails.Add(whenSentence);

            VisualElement chanceRow = Row();
            SerializedProperty chance = e.FindPropertyRelative("DiscoveryChancePercent");
            SliderInt chanceSlider = new SliderInt("Шанс, %", 0, 100) { value = chance.intValue, showInputField = true };
            chanceSlider.tooltip = "Шанс встречи попасть в кандидаты, когда пул сработал (не итоговая вероятность)";
            chanceSlider.style.flexGrow = 1f;
            chanceSlider.RegisterValueChangedCallback(evt => { chance.intValue = evt.newValue; Commit(); });
            chanceRow.Add(chanceSlider);
            encounterDetails.Add(chanceRow);

            BuildOccurrencesRow(e);

            BuildStringList(e.FindPropertyRelative("AllowedRegionIds"), "Регионы", "регион", "где угодно");
            BuildStringList(e.FindPropertyRelative("RequiredLocationTags"), "Нужны приметы места", "примета");
            BuildStringList(e.FindPropertyRelative("ForbiddenLocationTags"), "Не в местах с приметами", "примета");
            BuildConditionsList(e.FindPropertyRelative("RequiredConditions"));
            BuildStringList(e.FindPropertyRelative("RequiredFlagsAll"), "Нужны все флаги", "флаг");
            BuildStringList(e.FindPropertyRelative("RequiredFlagsAny"), "Нужен любой из флагов", "флаг");
            BuildStringList(e.FindPropertyRelative("ForbiddenFlags"), "Не выпадает при флагах", "флаг");

            AddAddRow(
                ("+ регион", "AllowedRegionIds"),
                ("+ примета места", "RequiredLocationTags"),
                ("+ условие", "RequiredConditions"),
                ("+ нужен флаг", "RequiredFlagsAll"),
                ("+ запрещён флаг", "ForbiddenFlags"));

            Foldout test = new Foldout { text = "Проверить в тестовых условиях", value = false };
            test.style.marginTop = 6f;
            BuildPreviewContextFields(test);
            Button check = new Button(RunEligibilityPreview) { text = "Выпала бы сейчас?" };
            check.style.marginTop = 4f;
            test.Add(check);
            eligibilityResult = new VisualElement();
            test.Add(eligibilityResult);
            test.Add(MakeMutedLabel("Проверяет правила этой встречи; шанс пула и бросок не учитываются. Весь пул — во вкладке «Проверка пула»."));
            encounterDetails.Add(test);
        }

        // «Сколько раз»: один раз / несколько / без ограничений; перерыв — только при повторах.
        private void BuildOccurrencesRow(SerializedProperty e)
        {
            SerializedProperty unlimited = e.FindPropertyRelative("UnlimitedOccurrences");
            SerializedProperty max = e.FindPropertyRelative("MaxOccurrencesPerGame");
            SerializedProperty cooldown = e.FindPropertyRelative("CooldownHours");

            List<string> options = new List<string> { "один раз за игру", "несколько раз", "без ограничений" };
            int current = unlimited.boolValue ? 2 : max.intValue <= 1 ? 0 : 1;

            VisualElement row = Row();
            PopupField<string> mode = new PopupField<string>("Сколько раз", options, current);
            mode.style.flexGrow = 1f;
            mode.RegisterValueChangedCallback(evt =>
            {
                int picked = options.IndexOf(evt.newValue);
                unlimited.boolValue = picked == 2;
                if (picked == 0)
                    max.intValue = 1;
                else if (picked == 1 && max.intValue <= 1)
                    max.intValue = 2;
                CommitAndRebuild();
            });
            row.Add(mode);

            if (current == 1)
            {
                IntegerField count = new IntegerField { value = max.intValue, tooltip = "Сколько раз за игру" };
                count.style.width = 44f;
                count.RegisterValueChangedCallback(evt => { max.intValue = Mathf.Max(2, evt.newValue); Commit(); });
                row.Add(count);
            }
            encounterDetails.Add(row);

            if (current != 0)
            {
                IntegerField gap = new IntegerField("Перерыв, ч") { value = cooldown.intValue, tooltip = "Не чаще раза в столько часов" };
                gap.RegisterValueChangedCallback(evt => { cooldown.intValue = Mathf.Max(0, evt.newValue); Commit(); });
                encounterDetails.Add(gap);
            }
        }

        // ЧТО ОСТАЁТСЯ ПОСЛЕ: память мира и флаги.
        private void BuildAfterSection(SerializedProperty e)
        {
            AddHeader(encounterDetails, "ЧТО ОСТАЁТСЯ ПОСЛЕ");
            encounterDetails.Add(MakeEnumPopup<EncounterMemoryClass>(e.FindPropertyRelative("MemoryClass"), "Память", EncounterEditorLabels.Memory, 0f));
            BuildStringList(e.FindPropertyRelative("FlagsSetOnStart"), "Флаги при начале", "флаг");
            BuildStringList(e.FindPropertyRelative("FlagsSetOnComplete"), "Флаги после завершения", "флаг");
            BuildStringList(e.FindPropertyRelative("ClearFlagsOnComplete"), "Снять флаги после завершения", "флаг");
            AddAddRow(
                ("+ флаг при начале", "FlagsSetOnStart"),
                ("+ флаг после завершения", "FlagsSetOnComplete"),
                ("+ снять флаг", "ClearFlagsOnComplete"));
        }

        private void BuildNotesSection(SerializedProperty e)
        {
            AddHeader(encounterDetails, "ЗАМЕТКИ");
            encounterDetails.Add(MakeMultilineText(e.FindPropertyRelative("DesignerNotes"), "Заметки автора"));
            encounterDetails.Add(MakeMultilineText(e.FindPropertyRelative("FutureHooksNotes"), "Зацепки на будущее: продолжения, эхо"));
        }

        private void BuildProductionSection(SerializedProperty e)
        {
            AddHeader(encounterDetails, "ПРОИЗВОДСТВО");
            encounterDetails.Add(MakeText(e.FindPropertyRelative("EncounterId"), "ID встречи"));
            encounterDetails.Add(MakeMutedLabel("ID готовой встречи не меняют: его помнят сохранения."));
            encounterDetails.Add(MakeEnumPopup<EncounterSelectionMode>(e.FindPropertyRelative("SelectionMode"), "Как вызывается", EncounterEditorLabels.SelectionMode, 0f));

            List<string> poolIds = new List<string>();
            foreach (EncounterPoolDefinition pool in database.Pools)
                if (pool != null && !string.IsNullOrEmpty(pool.PoolId))
                    poolIds.Add(pool.PoolId);
            SerializedProperty poolId = e.FindPropertyRelative("PoolId");
            if (!poolIds.Contains(poolId.stringValue))
                poolIds.Insert(0, poolId.stringValue);
            PopupField<string> poolField = new PopupField<string>("Пул", poolIds, poolId.stringValue);
            poolField.RegisterValueChangedCallback(evt => { poolId.stringValue = evt.newValue; Commit(); });
            encounterDetails.Add(poolField);

            SerializedProperty weight = e.FindPropertyRelative("SelectionWeight");
            IntegerField weightField = new IntegerField("Вес среди кандидатов") { value = weight.intValue, tooltip = "Чем больше, тем чаще выбирается среди прошедших встреч" };
            weightField.RegisterValueChangedCallback(evt => { weight.intValue = Mathf.Max(0, evt.newValue); Commit(); });
            encounterDetails.Add(weightField);

            BuildStringList(e.FindPropertyRelative("Tags"), "Теги (для поиска)", "тег", "нет", true);
        }

        // ---------------------------------------------------------------
        // Элементы карточки
        // ---------------------------------------------------------------

        private static VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private TextField MakeText(SerializedProperty property, string label)
        {
            TextField field = new TextField(label) { value = property.stringValue };
            field.RegisterValueChangedCallback(evt => { property.stringValue = evt.newValue; Commit(); });
            return field;
        }

        private TextField MakeMultilineText(SerializedProperty property, string placeholder)
        {
            TextField field = new TextField { value = property.stringValue, multiline = true, tooltip = placeholder };
            field.textEdition.placeholder = placeholder;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.minHeight = 36f;
            field.style.marginBottom = 4f;
            field.RegisterValueChangedCallback(evt => { property.stringValue = evt.newValue; Commit(); });
            return field;
        }

        private PopupField<TEnum> MakeEnumPopup<TEnum>(SerializedProperty property, string label, Func<TEnum, string> toLabel, float width)
            where TEnum : Enum
        {
            List<TEnum> values = new List<TEnum>((TEnum[])Enum.GetValues(typeof(TEnum)));
            TEnum current = values.Find(v => Convert.ToInt32(v) == property.enumValueIndex);
            PopupField<TEnum> field = new PopupField<TEnum>(label, values, current, toLabel, toLabel);
            if (width > 0f)
                field.style.width = width;
            field.RegisterValueChangedCallback(evt =>
            {
                property.enumValueIndex = Convert.ToInt32(evt.newValue);
                Commit();
            });
            return field;
        }

        // Список строк плашками «значение ×» + поле добавления. Пустой список
        // не рисуется, пока его не открыли кнопкой «+ …».
        private void BuildStringList(SerializedProperty list, string title, string placeholder, string emptyText = null, bool alwaysShow = false)
        {
            string key = SectionKey(list.name);
            if (list.arraySize == 0 && !alwaysShow && !openSections.Contains(key))
                return;

            VisualElement block = new VisualElement();
            block.style.marginTop = 3f;
            block.Add(MakeMutedLabel(title));

            VisualElement chips = Row();
            chips.style.flexWrap = Wrap.Wrap;
            if (list.arraySize == 0 && emptyText != null)
                chips.Add(MakeMutedLabel(emptyText));
            for (int i = 0; i < list.arraySize; i++)
            {
                int index = i;
                VisualElement chip = Row();
                chip.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
                chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius =
                    chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 3f;
                chip.style.paddingLeft = 5f;
                chip.style.marginRight = 4f;
                chip.style.marginBottom = 2f;
                chip.Add(new Label(list.GetArrayElementAtIndex(i).stringValue));
                Button remove = new Button(() =>
                {
                    serializedDatabase.Update();
                    list.DeleteArrayElementAtIndex(index);
                    CommitAndRebuild();
                }) { text = "×", tooltip = "Убрать" };
                remove.style.height = 16f;
                remove.style.paddingLeft = 3f;
                remove.style.paddingRight = 3f;
                chip.Add(remove);
                chips.Add(chip);
            }

            TextField input = new TextField { tooltip = "Enter — добавить" };
            input.textEdition.placeholder = "+ " + placeholder;
            input.style.minWidth = 110f;
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                string value = input.value.Trim();
                if (string.IsNullOrEmpty(value))
                    return;
                serializedDatabase.Update();
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = value;
                openSections.Remove(key);
                CommitAndRebuild();
            }, TrickleDown.TrickleDown);
            chips.Add(input);
            block.Add(chips);
            encounterDetails.Add(block);

            if (list.arraySize == 0 && openSections.Contains(key))
                input.schedule.Execute(() => input.Focus());
        }

        // Условия встречи (NarrativeConditionGroup) — строки по-русски.
        private void BuildConditionsList(SerializedProperty group)
        {
            SerializedProperty conditions = group.FindPropertyRelative("Conditions");
            string key = SectionKey(group.name);
            if (conditions.arraySize == 0 && !openSections.Contains(key))
                return;

            VisualElement block = new VisualElement();
            block.style.marginTop = 3f;
            block.Add(MakeMutedLabel("Условия"));

            if (conditions.arraySize > 1)
            {
                SerializedProperty combinator = group.FindPropertyRelative("Combinator");
                List<string> options = new List<string> { "выполнены все", "выполнено хотя бы одно" };
                PopupField<string> combine = new PopupField<string>("Как проверять", options, Mathf.Clamp(combinator.enumValueIndex, 0, 1));
                combine.RegisterValueChangedCallback(evt => { combinator.enumValueIndex = options.IndexOf(evt.newValue); Commit(); });
                block.Add(combine);
            }

            for (int i = 0; i < conditions.arraySize; i++)
                block.Add(MakeConditionRow(conditions, i));

            Button add = new Button(() =>
            {
                serializedDatabase.Update();
                conditions.arraySize++;
                SerializedProperty added = conditions.GetArrayElementAtIndex(conditions.arraySize - 1);
                added.FindPropertyRelative("Type").enumValueIndex = 0;
                added.FindPropertyRelative("StringParam").stringValue = string.Empty;
                added.FindPropertyRelative("IntParam").intValue = 0;
                added.FindPropertyRelative("Negate").boolValue = false;
                CommitAndRebuild();
            }) { text = "+ ещё условие" };
            add.style.alignSelf = Align.FlexStart;
            block.Add(add);
            encounterDetails.Add(block);
        }

        private VisualElement MakeConditionRow(SerializedProperty conditions, int index)
        {
            SerializedProperty condition = conditions.GetArrayElementAtIndex(index);
            SerializedProperty type = condition.FindPropertyRelative("Type");
            NarrativeConditionType typeValue = (NarrativeConditionType)type.enumValueIndex;

            VisualElement row = Row();
            row.style.flexWrap = Wrap.Wrap;

            SerializedProperty negate = condition.FindPropertyRelative("Negate");
            Toggle not = new Toggle("НЕ") { value = negate.boolValue, tooltip = "Условие должно НЕ выполняться" };
            not.labelElement.style.minWidth = 18f;
            not.RegisterValueChangedCallback(evt => { negate.boolValue = evt.newValue; Commit(); });
            row.Add(not);

            List<NarrativeConditionType> types = new List<NarrativeConditionType>((NarrativeConditionType[])Enum.GetValues(typeof(NarrativeConditionType)));
            PopupField<NarrativeConditionType> typeField = new PopupField<NarrativeConditionType>(types, typeValue,
                EncounterEditorLabels.ConditionType, EncounterEditorLabels.ConditionType);
            typeField.style.minWidth = 170f;
            typeField.RegisterValueChangedCallback(evt => { type.enumValueIndex = (int)evt.newValue; CommitAndRebuild(); });
            row.Add(typeField);

            if (typeValue == NarrativeConditionType.QualityAtLeast)
            {
                SerializedProperty quality = condition.FindPropertyRelative("QualityParam");
                List<HeroQuality> qualities = new List<HeroQuality>((HeroQuality[])Enum.GetValues(typeof(HeroQuality)));
                PopupField<HeroQuality> qualityField = new PopupField<HeroQuality>(qualities, (HeroQuality)quality.enumValueIndex,
                    NarrativeQualityLabels.GetLabel, NarrativeQualityLabels.GetLabel);
                qualityField.RegisterValueChangedCallback(evt => { quality.enumValueIndex = (int)evt.newValue; Commit(); });
                row.Add(qualityField);
            }
            else if (EncounterEditorLabels.ConditionUsesString(typeValue))
            {
                SerializedProperty text = condition.FindPropertyRelative("StringParam");
                TextField param = new TextField { value = text.stringValue, tooltip = "ID флага, знания, спутника, предмета…" };
                param.style.flexGrow = 1f;
                param.style.minWidth = 120f;
                param.RegisterValueChangedCallback(evt => { text.stringValue = evt.newValue; Commit(); });
                row.Add(param);
            }

            if (EncounterEditorLabels.ConditionUsesInt(typeValue))
            {
                SerializedProperty number = condition.FindPropertyRelative("IntParam");
                IntegerField value = new IntegerField { value = number.intValue };
                value.style.width = 40f;
                value.RegisterValueChangedCallback(evt => { number.intValue = evt.newValue; Commit(); });
                row.Add(value);
            }

            Button remove = new Button(() =>
            {
                serializedDatabase.Update();
                conditions.DeleteArrayElementAtIndex(index);
                CommitAndRebuild();
            }) { text = "×", tooltip = "Убрать условие" };
            row.Add(remove);
            return row;
        }

        // Строка «+ регион · + условие · …» — открывает пустой список для ввода.
        private void AddAddRow(params (string label, string property)[] items)
        {
            VisualElement row = Row();
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 3f;
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            foreach ((string label, string property) item in items)
            {
                SerializedProperty list = e.FindPropertyRelative(item.property);
                SerializedProperty sized = item.property == "RequiredConditions" ? list.FindPropertyRelative("Conditions") : list;
                string key = SectionKey(list.name);
                if (sized.arraySize > 0 || openSections.Contains(key))
                    continue;
                (string label, string property) captured = item;
                Button add = new Button(() =>
                {
                    openSections.Add(SectionKey(captured.property));
                    if (captured.property == "RequiredConditions")
                    {
                        serializedDatabase.Update();
                        SerializedProperty conditions = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex)
                            .FindPropertyRelative("RequiredConditions").FindPropertyRelative("Conditions");
                        conditions.arraySize++;
                        SerializedProperty added = conditions.GetArrayElementAtIndex(conditions.arraySize - 1);
                        added.FindPropertyRelative("Type").enumValueIndex = 0;
                        added.FindPropertyRelative("StringParam").stringValue = string.Empty;
                        added.FindPropertyRelative("IntParam").intValue = 0;
                        added.FindPropertyRelative("Negate").boolValue = false;
                        CommitAndRebuild();
                        return;
                    }
                    ShowSelectedEncounter();
                }) { text = captured.label };
                add.style.height = 18f;
                add.style.fontSize = 10f;
                row.Add(add);
            }
            if (row.childCount > 0)
                encounterDetails.Add(row);
        }

        // ---------------------------------------------------------------
        // «Выпала бы сейчас?» — правила этой встречи в тестовых условиях.
        // ---------------------------------------------------------------

        private void RunEligibilityPreview()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count || eligibilityResult == null)
                return;

            serializedDatabase.ApplyModifiedProperties();
            EncounterDefinition encounter = database.Encounters[selectedEncounterIndex];
            EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
                encounter, BuildEvaluationContext(BuildPreviewState()), new EncounterRuntimeStateData(),
                previewContext.Region, SplitCsv(previewContext.LocationTags), previewContext.WorldHour);

            eligibilityResult.Clear();
            Label verdict = new Label(result.Eligible ? "✓ Выпала бы: все правила выполнены." : "✗ Не выпадет:");
            verdict.style.color = result.Eligible ? GoodColor : BadColor;
            verdict.style.unityFontStyleAndWeight = FontStyle.Bold;
            eligibilityResult.Add(verdict);
            foreach (string reason in result.BlockReasons)
            {
                Label reasonLabel = new Label("— " + reason);
                reasonLabel.style.whiteSpace = WhiteSpace.Normal;
                reasonLabel.style.color = BadColor;
                reasonLabel.style.marginLeft = 8f;
                eligibilityResult.Add(reasonLabel);
            }
        }

        // ---------------------------------------------------------------
        // Команды
        // ---------------------------------------------------------------

        private void AddEncounter()
        {
            serializedDatabase.Update();
            int index = encountersProperty.arraySize++;
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("EncounterId").stringValue = MakeUniqueEncounterId("NEW_ENCOUNTER");
            e.FindPropertyRelative("DisplayName").stringValue = "Новая встреча";
            e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Draft;
            e.FindPropertyRelative("SelectionMode").enumValueIndex = (int)EncounterSelectionMode.Pool;
            e.FindPropertyRelative("SelectionWeight").intValue = 1;
            e.FindPropertyRelative("MaxOccurrencesPerGame").intValue = 1;
            if (database.Pools.Count > 0 && database.Pools[0] != null)
                e.FindPropertyRelative("PoolId").stringValue = database.Pools[0].PoolId;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedEncounterIndex = index;
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        private void DuplicateEncounter()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
                return;
            serializedDatabase.Update();
            encountersProperty.InsertArrayElementAtIndex(selectedEncounterIndex);
            selectedEncounterIndex++;
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            string sourceId = e.FindPropertyRelative("EncounterId").stringValue;
            e.FindPropertyRelative("EncounterId").stringValue = MakeUniqueEncounterId(sourceId + "_COPY");
            e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Draft;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        // Готовая встреча не удаляется, а списывается — её ID помнят старые
        // сохранения. Черновик можно удалить сразу.
        private void DeprecateEncounter()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
                return;

            serializedDatabase.Update();
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            EncounterStatus status = (EncounterStatus)e.FindPropertyRelative("Status").enumValueIndex;
            if (status == EncounterStatus.Draft)
            {
                DeleteEncounterPermanently();
                return;
            }

            e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Deprecated;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        private void ValidateEncounterDatabase()
        {
            ValidateEncounterDatabase(true);
        }

        private void ValidateEncounterDatabase(bool openPanel)
        {
            serializedDatabase.ApplyModifiedProperties();
            validationIssues.Clear();
            database.CollectValidationIssues(validationIssues);
            DialogueDatabaseAsset dialogueDatabase = LoadDialogueDatabase();
            foreach (EncounterDefinition encounter in database.Encounters)
            {
                if (encounter == null || encounter.Status != EncounterStatus.Production ||
                    encounter.ResolutionMode != EncounterResolutionMode.DialogueDriven ||
                    string.IsNullOrWhiteSpace(encounter.DialogueId))
                    continue;
                DialogueDefinitionData dialogue = dialogueDatabase != null
                    ? dialogueDatabase.FindDialogue(encounter.DialogueId) : null;
                if (dialogue == null)
                {
                    validationIssues.Add(encounter.EncounterId + ": диалог " + encounter.DialogueId + " не найден.");
                    continue;
                }
                if (dialogue.Status == DialogueProductionStatus.Disabled)
                    validationIssues.Add(encounter.EncounterId + ": связанный диалог отключён.");
                List<string> dialogueIssues = new List<string>();
                dialogueDatabase.CollectValidationIssuesForDialogue(encounter.DialogueId, dialogueIssues);
                foreach (string issue in dialogueIssues)
                    validationIssues.Add(encounter.EncounterId + ": " + issue);
                if (dialogue.Nodes.Count == 1)
                {
                    DialogueNodeData node = dialogue.Nodes[0];
                    if (node != null && node.Choices.Count == 1 && node.Choices[0] != null && node.Choices[0].IsExit)
                        foreach (DialogueTextBlockData block in node.GetEffectiveTextBlocks())
                            if (block.OnRevealEffects.Count > 0)
                            {
                                validationIssues.Add(encounter.EncounterId +
                                    ": ресурс меняется при показе текста, у игрока только выход.");
                                break;
                            }
                }
            }
            if (dialogueDatabase != null)
            {
                HashSet<string> linked = new HashSet<string>(StringComparer.Ordinal);
                foreach (EncounterDefinition encounter in database.Encounters)
                    if (encounter != null) linked.Add(encounter.DialogueId);
                foreach (DialogueDefinitionData dialogue in dialogueDatabase.Dialogues)
                    if (dialogue != null && dialogue.Category == DialogueCategory.RandomEncounter &&
                        dialogue.Id.StartsWith("road_", StringComparison.Ordinal) &&
                        !linked.Contains(dialogue.Id))
                        validationIssues.Add("Дорожный диалог без встречи: " + dialogue.Id + ".");
            }

            validationLabel.text = validationIssues.Count == 0 ? "✓ Ошибок нет" : "⚠ Замечаний: " + validationIssues.Count + " ▾";
            validationLabel.style.color = validationIssues.Count == 0 ? GoodColor : BadColor;
            validationPanel.Clear();
            foreach (string issue in validationIssues)
                validationPanel.Add(new HelpBox(issue, HelpBoxMessageType.Warning));
            validationPanel.style.display = openPanel && validationIssues.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string MakeUniqueEncounterId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindById(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
