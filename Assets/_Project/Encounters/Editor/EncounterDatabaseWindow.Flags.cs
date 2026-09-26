using System;
using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    // Вкладка «Флаги»: память мира о событиях. Слева список (название и
    // «кто ставит / кто проверяет»), справа карточка: что означает флаг,
    // где он ставится, проверяется и снимается (встречи и диалоги, с
    // переходом по клику), заметки. ID — за «⚙ Производство».
    public sealed partial class EncounterDatabaseWindow
    {
        private readonly List<int> visibleFlagIndices = new List<int>();
        private readonly HashSet<EncounterFlagStatus> flagStatusFilter = new HashSet<EncounterFlagStatus>();
        private readonly HashSet<string> flagCategoryFilter = new HashSet<string>(StringComparer.Ordinal);
        private TextField flagSearch;
        private Button flagFilterButton;
        private ListView flagList;
        private ScrollView flagDetails;
        private Label flagEmptyHint;
        private Label flagValidationLabel;
        private VisualElement flagValidationPanel;
        private readonly List<string> flagValidationIssues = new List<string>();

        // Кто и как использует флаг — считается по встречам и диалогам.
        private sealed class FlagUsage
        {
            public readonly List<(string label, Action open)> SetBy = new List<(string, Action)>();
            public readonly List<(string label, Action open)> ReadBy = new List<(string, Action)>();
            public readonly List<(string label, Action open)> ClearedBy = new List<(string, Action)>();
            public bool IsUsed => SetBy.Count + ReadBy.Count + ClearedBy.Count > 0;
        }

        private static string FlagStatusLabel(EncounterFlagStatus status)
        {
            switch (status)
            {
                case EncounterFlagStatus.Active: return "Используется";
                case EncounterFlagStatus.Reserved: return "Закладка на будущее";
                case EncounterFlagStatus.Deprecated: return "Устарел";
                default: return status.ToString();
            }
        }

        private static string FlagStatusDot(EncounterFlagStatus status)
        {
            switch (status)
            {
                case EncounterFlagStatus.Active: return "●";
                case EncounterFlagStatus.Reserved: return "○";
                default: return "✕";
            }
        }

        private void BuildFlagsTab(VisualElement root)
        {
            VisualElement toolbar = Row();
            toolbar.style.height = 30f;
            toolbar.style.paddingLeft = 6f;
            toolbar.style.paddingRight = 6f;
            AddToolbarButton(toolbar, "+ Новый флаг", AddFlag);
            Button more = new Button { text = "⋯", tooltip = "Удалить флаг" };
            more.style.height = 22f;
            more.clicked += () =>
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Удалить флаг…"), false, RemoveFlag);
                menu.DropDown(more.worldBound);
            };
            toolbar.Add(more);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            flagValidationLabel = new Label("Проверить флаги") { tooltip = "Проверить реестр флагов и их использование во встречах" };
            flagValidationLabel.style.color = MutedColor;
            flagValidationLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (flagValidationIssues.Count > 0 && flagValidationPanel.style.display == DisplayStyle.None)
                    flagValidationPanel.style.display = DisplayStyle.Flex;
                else
                    ValidateFlagRegistry();
            });
            toolbar.Add(flagValidationLabel);
            root.Add(toolbar);

            flagValidationPanel = new ScrollView();
            flagValidationPanel.style.display = DisplayStyle.None;
            flagValidationPanel.style.maxHeight = 160f;
            flagValidationPanel.style.paddingLeft = 8f;
            flagValidationPanel.style.paddingRight = 8f;
            root.Add(flagValidationPanel);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 250f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(BuildFlagListPane());
            split.Add(BuildFlagDetailPane());
            root.Add(split);

            RefreshFlagList();
            RestoreFlagSelection();
            ValidateFlagRegistry(false);
        }

        // ---------------------------------------------------------------
        // Список
        // ---------------------------------------------------------------

        private VisualElement BuildFlagListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 6f;
            pane.style.paddingRight = 6f;
            pane.style.paddingTop = 6f;

            VisualElement searchRow = Row();
            flagSearch = new TextField { tooltip = "Поиск по названию, ID, группе и описанию" };
            flagSearch.textEdition.placeholder = "Поиск";
            flagSearch.style.flexGrow = 1f;
            flagSearch.RegisterValueChangedCallback(_ => RefreshFlagList());
            searchRow.Add(flagSearch);
            flagFilterButton = new Button(ShowFlagFilterMenu) { text = "Фильтр ▾" };
            searchRow.Add(flagFilterButton);
            pane.Add(searchRow);

            flagList = new ListView();
            flagList.style.flexGrow = 1f;
            flagList.style.marginTop = 6f;
            flagList.fixedItemHeight = 40f;
            flagList.selectionType = SelectionType.Single;
            flagList.makeItem = MakeEncounterListItem;
            flagList.bindItem = BindFlagListItem;
            flagList.selectionChanged += _ => SelectVisibleFlag(flagList.selectedIndex);
            pane.Add(flagList);
            return pane;
        }

        private void ShowFlagFilterMenu()
        {
            GenericMenu menu = new GenericMenu();
            foreach (EncounterFlagStatus status in Enum.GetValues(typeof(EncounterFlagStatus)))
            {
                EncounterFlagStatus captured = status;
                menu.AddItem(new GUIContent("Статус/" + FlagStatusLabel(status)), flagStatusFilter.Contains(status), () =>
                {
                    if (!flagStatusFilter.Remove(captured))
                        flagStatusFilter.Add(captured);
                    RefreshFlagList();
                });
            }
            foreach (string category in CollectFlagCategories())
            {
                string captured = category;
                menu.AddItem(new GUIContent("Группа/" + category), flagCategoryFilter.Contains(category), () =>
                {
                    if (!flagCategoryFilter.Remove(captured))
                        flagCategoryFilter.Add(captured);
                    RefreshFlagList();
                });
            }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Сбросить фильтр"), false, () =>
            {
                flagStatusFilter.Clear();
                flagCategoryFilter.Clear();
                RefreshFlagList();
            });
            menu.DropDown(flagFilterButton.worldBound);
        }

        private List<string> CollectFlagCategories()
        {
            SortedSet<string> categories = new SortedSet<string>(StringComparer.Ordinal);
            foreach (EncounterFlagDefinition flag in flagRegistry.Flags)
                if (flag != null && !string.IsNullOrWhiteSpace(flag.Category))
                    categories.Add(flag.Category);
            return new List<string>(categories);
        }

        private void BindFlagListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleFlagIndices.Count)
                return;

            EncounterFlagDefinition flag = flagRegistry.Flags[visibleFlagIndices[visibleIndex]];
            Label title = row.Q<Label>("title");
            title.text = FlagStatusDot(flag.Status) + " " + (string.IsNullOrWhiteSpace(flag.DisplayName) ? flag.FlagId : flag.DisplayName);
            title.tooltip = FlagStatusLabel(flag.Status);

            FlagUsage usage = CollectFlagUsage(flag.FlagId);
            string counts = "ставят: " + usage.SetBy.Count + " · проверяют: " + usage.ReadBy.Count +
                            (usage.ClearedBy.Count > 0 ? " · снимают: " + usage.ClearedBy.Count : string.Empty);
            row.Q<Label>("meta").text = showProduction ? flag.FlagId + " · " + counts : counts;
        }

        private void RefreshFlagList()
        {
            if (flagRegistry == null || flagList == null)
                return;

            serializedFlagRegistry.Update();
            visibleFlagIndices.Clear();
            string query = flagSearch != null ? flagSearch.value.Trim() : string.Empty;
            bool Has(string value) => !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            for (int i = 0; i < flagRegistry.Flags.Count; i++)
            {
                EncounterFlagDefinition flag = flagRegistry.Flags[i];
                if (flag == null)
                    continue;
                if (flagStatusFilter.Count > 0 && !flagStatusFilter.Contains(flag.Status))
                    continue;
                if (flagCategoryFilter.Count > 0 && !flagCategoryFilter.Contains(flag.Category ?? string.Empty))
                    continue;
                if (!string.IsNullOrEmpty(query) &&
                    !Has(flag.FlagId) && !Has(flag.DisplayName) && !Has(flag.Category) && !Has(flag.Description))
                    continue;
                visibleFlagIndices.Add(i);
            }

            int filters = flagStatusFilter.Count + flagCategoryFilter.Count;
            if (flagFilterButton != null)
                flagFilterButton.text = filters > 0 ? "Фильтр (" + filters + ") ▾" : "Фильтр ▾";

            flagList.itemsSource = visibleFlagIndices;
            flagList.Rebuild();
            int visible = visibleFlagIndices.IndexOf(selectedFlagIndex);
            if (visible >= 0)
                flagList.SetSelectionWithoutNotify(new[] { visible });
        }

        private void RestoreFlagSelection()
        {
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagRegistry.Flags.Count)
                selectedFlagIndex = flagRegistry.Flags.Count > 0 ? 0 : -1;
            ShowSelectedFlag();
        }

        private void SelectVisibleFlag(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleFlagIndices.Count)
                return;
            selectedFlagIndex = visibleFlagIndices[visibleIndex];
            ShowSelectedFlag();
        }

        // ---------------------------------------------------------------
        // Карточка флага
        // ---------------------------------------------------------------

        private VisualElement BuildFlagDetailPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;

            flagEmptyHint = new Label("Выберите флаг слева.");
            flagEmptyHint.style.flexGrow = 1f;
            flagEmptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            pane.Add(flagEmptyHint);

            flagDetails = new ScrollView();
            flagDetails.style.display = DisplayStyle.None;
            flagDetails.style.flexGrow = 1f;
            flagDetails.style.paddingLeft = 12f;
            flagDetails.style.paddingRight = 12f;
            flagDetails.style.paddingTop = 8f;
            flagDetails.style.paddingBottom = 16f;
            pane.Add(flagDetails);
            return pane;
        }

        private void CommitFlags()
        {
            serializedFlagRegistry.ApplyModifiedProperties();
            EditorUtility.SetDirty(flagRegistry);
            flagList?.RefreshItems();
        }

        private void ShowSelectedFlag()
        {
            if (flagDetails == null)
                return;
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagsProperty.arraySize)
            {
                flagEmptyHint.style.display = DisplayStyle.Flex;
                flagDetails.style.display = DisplayStyle.None;
                return;
            }

            flagEmptyHint.style.display = DisplayStyle.None;
            flagDetails.style.display = DisplayStyle.Flex;
            flagDetails.Clear();
            serializedFlagRegistry.Update();

            SerializedProperty f = flagsProperty.GetArrayElementAtIndex(selectedFlagIndex);
            string flagId = f.FindPropertyRelative("FlagId").stringValue;

            // Название и статус.
            VisualElement header = Row();
            TextField title = new TextField { value = f.FindPropertyRelative("DisplayName").stringValue, tooltip = "Название флага" };
            title.style.flexGrow = 1f;
            title.style.fontSize = 15f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.RegisterValueChangedCallback(evt => { f.FindPropertyRelative("DisplayName").stringValue = evt.newValue; CommitFlags(); });
            header.Add(title);
            SerializedProperty status = f.FindPropertyRelative("Status");
            List<EncounterFlagStatus> statuses = new List<EncounterFlagStatus>((EncounterFlagStatus[])Enum.GetValues(typeof(EncounterFlagStatus)));
            PopupField<EncounterFlagStatus> statusField = new PopupField<EncounterFlagStatus>(statuses,
                (EncounterFlagStatus)status.enumValueIndex, FlagStatusLabel, FlagStatusLabel);
            statusField.style.width = 160f;
            statusField.RegisterValueChangedCallback(evt =>
            {
                status.enumValueIndex = (int)evt.newValue;
                CommitFlags();
                flagDetails.schedule.Execute(ShowSelectedFlag);
            });
            header.Add(statusField);
            flagDetails.Add(header);

            // Группа: свободный текст + выбор из существующих.
            VisualElement groupRow = Row();
            SerializedProperty category = f.FindPropertyRelative("Category");
            TextField group = new TextField("Группа") { value = category.stringValue, tooltip = "К какой истории или месту относится флаг" };
            group.style.flexGrow = 1f;
            group.RegisterValueChangedCallback(evt => { category.stringValue = evt.newValue; CommitFlags(); });
            groupRow.Add(group);
            Button pickGroup = new Button { text = "▾", tooltip = "Выбрать существующую группу" };
            pickGroup.clicked += () =>
            {
                GenericMenu menu = new GenericMenu();
                foreach (string existing in CollectFlagCategories())
                {
                    string captured = existing;
                    menu.AddItem(new GUIContent(existing), existing == category.stringValue, () =>
                    {
                        category.stringValue = captured;
                        CommitFlags();
                        ShowSelectedFlag();
                    });
                }
                menu.DropDown(pickGroup.worldBound);
            };
            groupRow.Add(pickGroup);
            flagDetails.Add(groupRow);

            TextField description = new TextField { value = f.FindPropertyRelative("Description").stringValue, multiline = true, tooltip = "Что означает флаг в мире" };
            description.textEdition.placeholder = "Что означает флаг: какое событие или решение он помнит";
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.minHeight = 36f;
            description.style.marginTop = 4f;
            description.RegisterValueChangedCallback(evt => { f.FindPropertyRelative("Description").stringValue = evt.newValue; CommitFlags(); });
            flagDetails.Add(description);

            // Где используется.
            AddHeader(flagDetails, "ГДЕ ИСПОЛЬЗУЕТСЯ");
            FlagUsage usage = CollectFlagUsage(flagId);
            EncounterFlagStatus statusValue = (EncounterFlagStatus)status.enumValueIndex;
            if (!usage.IsUsed)
            {
                Label hint = MakeMutedLabel(statusValue == EncounterFlagStatus.Reserved
                    ? "Пока нигде — это закладка на будущее, так и задумано."
                    : "Флаг нигде не ставится и не проверяется.");
                if (statusValue == EncounterFlagStatus.Active)
                    hint.style.color = BadColor;
                flagDetails.Add(hint);
            }
            else
            {
                AddUsageRow("Ставится", usage.SetBy);
                AddUsageRow("Проверяется", usage.ReadBy);
                AddUsageRow("Снимается", usage.ClearedBy);
                if (usage.SetBy.Count == 0 && usage.ReadBy.Count > 0)
                    flagDetails.Add(MakeMutedLabel("Флаг проверяют, но нигде не ставят: такие условия никогда не выполнятся."));
            }

            AddHeader(flagDetails, "ЗАМЕТКИ");
            TextField notes = new TextField { value = f.FindPropertyRelative("FutureUseNotes").stringValue, multiline = true };
            notes.textEdition.placeholder = "Зачем флаг пригодится дальше: продолжения, эхо";
            notes.style.whiteSpace = WhiteSpace.Normal;
            notes.style.minHeight = 36f;
            notes.RegisterValueChangedCallback(evt => { f.FindPropertyRelative("FutureUseNotes").stringValue = evt.newValue; CommitFlags(); });
            flagDetails.Add(notes);

            if (showProduction)
            {
                AddHeader(flagDetails, "ПРОИЗВОДСТВО");
                TextField id = new TextField("ID флага") { value = flagId };
                id.isDelayed = true;
                id.RegisterValueChangedCallback(evt =>
                {
                    f.FindPropertyRelative("FlagId").stringValue = evt.newValue;
                    CommitFlags();
                    ShowSelectedFlag();
                });
                flagDetails.Add(id);
                flagDetails.Add(MakeMutedLabel("ID пишется во встречах, диалогах и сохранениях. Переименование здесь ссылки не обновляет."));
            }
        }

        private void AddUsageRow(string title, List<(string label, Action open)> items)
        {
            if (items.Count == 0)
                return;
            VisualElement row = Row();
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 2f;
            Label caption = new Label(title + ":");
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.minWidth = 90f;
            row.Add(caption);
            foreach ((string label, Action open) item in items)
            {
                Button link = new Button(item.open) { text = item.label, tooltip = "Открыть" };
                link.style.height = 18f;
                row.Add(link);
            }
            flagDetails.Add(row);
        }

        // Встречи и диалоги, которые ставят, проверяют и снимают флаг.
        private FlagUsage CollectFlagUsage(string flagId)
        {
            FlagUsage usage = new FlagUsage();
            if (string.IsNullOrWhiteSpace(flagId))
                return usage;

            for (int i = 0; i < database.Encounters.Count; i++)
            {
                EncounterDefinition encounter = database.Encounters[i];
                if (encounter == null)
                    continue;
                int index = i;
                string name = string.IsNullOrWhiteSpace(encounter.DisplayName) ? encounter.EncounterId : encounter.DisplayName;
                void Open()
                {
                    selectedEncounterIndex = index;
                    activeTab = Tab.Encounters;
                    ShowActiveTab();
                }

                if (Contains(encounter.FlagsSetOnStart, flagId))
                    usage.SetBy.Add(("встреча «" + name + "» при начале", Open));
                if (Contains(encounter.FlagsSetOnComplete, flagId))
                    usage.SetBy.Add(("встреча «" + name + "» после завершения", Open));
                if (Contains(encounter.ClearFlagsOnComplete, flagId))
                    usage.ClearedBy.Add(("встреча «" + name + "»", Open));
                if (Contains(encounter.RequiredFlagsAll, flagId) || Contains(encounter.RequiredFlagsAny, flagId) ||
                    ConditionGroupReferencesFlag(encounter.RequiredConditions, flagId))
                    usage.ReadBy.Add(("встреча «" + name + "» (нужен)", Open));
                if (Contains(encounter.ForbiddenFlags, flagId))
                    usage.ReadBy.Add(("встреча «" + name + "» (запрещает)", Open));
            }

            DialogueDatabaseAsset dialogueDatabase = LoadDialogueDatabase();
            if (dialogueDatabase == null)
                return usage;
            foreach (DialogueDefinitionData dialogue in dialogueDatabase.Dialogues)
            {
                if (dialogue == null)
                    continue;
                bool sets = false, clears = false, reads = false;
                foreach (DialogueNodeData node in dialogue.Nodes)
                {
                    foreach (DialogueTextBlockData block in node.GetEffectiveTextBlocks())
                    {
                        ScanEffects(block.OnRevealEffects, flagId, ref sets, ref clears);
                        reads |= ConditionGroupReferencesFlag(block.Conditions, flagId);
                    }
                    foreach (DialogueChoiceData choice in node.Choices)
                    {
                        ScanEffects(choice.SuccessEffects, flagId, ref sets, ref clears);
                        ScanEffects(choice.FailureEffects, flagId, ref sets, ref clears);
                        reads |= ConditionGroupReferencesFlag(choice.Conditions, flagId);
                    }
                }
                string dialogueId = dialogue.Id;
                string title = "диалог «" + (string.IsNullOrWhiteSpace(dialogue.Title) ? dialogue.Id : dialogue.Title) + "»";
                void OpenDialogue() => KingdomSurvival.DialogueDatabase.Editor.DialogueDatabaseWindow.OpenAt(dialogueId);
                if (sets)
                    usage.SetBy.Add((title, OpenDialogue));
                if (clears)
                    usage.ClearedBy.Add((title, OpenDialogue));
                if (reads)
                    usage.ReadBy.Add((title, OpenDialogue));
            }
            return usage;
        }

        private static void ScanEffects(IReadOnlyList<NarrativeEffect> effects, string flagId, ref bool sets, ref bool clears)
        {
            if (effects == null)
                return;
            foreach (NarrativeEffect effect in effects)
            {
                if (effect == null || !string.Equals(effect.StringParam, flagId, StringComparison.Ordinal))
                    continue;
                if (effect.Type == NarrativeEffectType.SetFlag)
                    sets = true;
                else if (effect.Type == NarrativeEffectType.ClearFlag)
                    clears = true;
            }
        }

        private static bool Contains(List<string> flags, string flagId)
        {
            return flags != null && flags.Contains(flagId);
        }

        private static bool ConditionGroupReferencesFlag(NarrativeConditionGroup group, string flagId)
        {
            if (group?.Conditions == null)
                return false;

            foreach (NarrativeCondition condition in group.Conditions)
            {
                if (condition != null &&
                    condition.Type == NarrativeConditionType.FlagSet &&
                    string.Equals(condition.StringParam, flagId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------
        // Команды
        // ---------------------------------------------------------------

        private void AddFlag()
        {
            serializedFlagRegistry.Update();
            int index = flagsProperty.arraySize++;
            SerializedProperty f = flagsProperty.GetArrayElementAtIndex(index);
            f.FindPropertyRelative("FlagId").stringValue = MakeUniqueFlagId("NEW_FLAG");
            f.FindPropertyRelative("DisplayName").stringValue = "Новый флаг";
            f.FindPropertyRelative("Description").stringValue = string.Empty;
            f.FindPropertyRelative("Category").stringValue = string.Empty;
            f.FindPropertyRelative("FutureUseNotes").stringValue = string.Empty;
            f.FindPropertyRelative("Status").enumValueIndex = (int)EncounterFlagStatus.Reserved;
            serializedFlagRegistry.ApplyModifiedProperties();
            EditorUtility.SetDirty(flagRegistry);
            selectedFlagIndex = index;
            RefreshFlagList();
            ShowSelectedFlag();
        }

        // Флаг никогда не удаляется автоматически. Используемый флаг удалить
        // нельзя: сначала уберите его из встреч и диалогов.
        private void RemoveFlag()
        {
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagsProperty.arraySize)
                return;

            EncounterFlagDefinition flag = flagRegistry.Flags[selectedFlagIndex];
            if (CollectFlagUsage(flag.FlagId).IsUsed)
            {
                EditorUtility.DisplayDialog("Флаг используется",
                    "«" + flag.DisplayName + "» ещё ставится или проверяется. Уберите его из встреч и диалогов или пометьте «Устарел».",
                    "Понятно");
                return;
            }
            if (!EditorUtility.DisplayDialog("Удалить флаг", "Удалить «" + flag.DisplayName + "» из реестра?", "Удалить", "Отмена"))
                return;

            serializedFlagRegistry.Update();
            flagsProperty.DeleteArrayElementAtIndex(selectedFlagIndex);
            serializedFlagRegistry.ApplyModifiedProperties();
            EditorUtility.SetDirty(flagRegistry);
            selectedFlagIndex = Mathf.Clamp(selectedFlagIndex - 1, -1, flagRegistry.Flags.Count - 1);
            RefreshFlagList();
            ShowSelectedFlag();
        }

        private void ValidateFlagRegistry()
        {
            ValidateFlagRegistry(true);
        }

        // Кросс-проверка с базой встреч: сам реестр про встречи не знает.
        private void ValidateFlagRegistry(bool openPanel)
        {
            serializedFlagRegistry.ApplyModifiedProperties();
            serializedDatabase.ApplyModifiedProperties();

            flagValidationIssues.Clear();
            flagRegistry.CollectValidationIssues(flagValidationIssues);

            HashSet<string> referencedFlagIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EncounterDefinition encounter in database.Encounters)
            {
                if (encounter == null)
                    continue;
                CollectFlagRefs(encounter.RequiredFlagsAll, referencedFlagIds);
                CollectFlagRefs(encounter.RequiredFlagsAny, referencedFlagIds);
                CollectFlagRefs(encounter.ForbiddenFlags, referencedFlagIds);
                CollectFlagRefs(encounter.FlagsSetOnStart, referencedFlagIds);
                CollectFlagRefs(encounter.FlagsSetOnComplete, referencedFlagIds);
                CollectFlagRefs(encounter.ClearFlagsOnComplete, referencedFlagIds);
            }

            foreach (string flagId in referencedFlagIds)
            {
                if (!flagRegistry.IsKnownFlag(flagId))
                    flagValidationIssues.Add("Флаг " + flagId + " используется во встречах, но не записан в реестр.");
            }

            foreach (EncounterFlagDefinition flag in flagRegistry.Flags)
            {
                if (flag == null || flag.Status != EncounterFlagStatus.Active)
                    continue;
                if (!referencedFlagIds.Contains(flag.FlagId))
                    flagValidationIssues.Add("Флаг «" + flag.DisplayName + "» помечен «Используется», но ни одна встреча его не ставит и не проверяет.");
            }

            flagValidationLabel.text = flagValidationIssues.Count == 0 ? "✓ Ошибок нет" : "⚠ Замечаний: " + flagValidationIssues.Count + " ▾";
            flagValidationLabel.style.color = flagValidationIssues.Count == 0 ? GoodColor : BadColor;
            flagValidationPanel.Clear();
            foreach (string issue in flagValidationIssues)
                flagValidationPanel.Add(new HelpBox(issue, HelpBoxMessageType.Warning));
            flagValidationPanel.style.display = openPanel && flagValidationIssues.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void CollectFlagRefs(List<string> flags, HashSet<string> into)
        {
            if (flags == null)
                return;
            foreach (string flag in flags)
            {
                if (!string.IsNullOrWhiteSpace(flag))
                    into.Add(flag);
            }
        }

        private string MakeUniqueFlagId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (flagRegistry.FindById(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
