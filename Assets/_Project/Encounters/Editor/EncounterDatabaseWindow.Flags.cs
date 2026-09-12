using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    public sealed partial class EncounterDatabaseWindow
    {
        private readonly List<int> visibleFlagIndices = new List<int>();
        private TextField flagSearch;
        private ListView flagList;
        private ScrollView flagDetails;
        private ScrollView flagReferencesPane;
        private Label flagEmptyHint;
        private Label flagValidationLabel;

        private void BuildFlagsTab(VisualElement root)
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 42f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            AddToolbarButton(toolbar, "+ НОВЫЙ ФЛАГ", AddFlag);
            AddToolbarButton(toolbar, "УДАЛИТЬ", RemoveFlag);
            AddToolbarButton(toolbar, "ПРОВЕРИТЬ", ValidateFlagRegistry);
            flagValidationLabel = new Label();
            flagValidationLabel.style.flexGrow = 1f;
            flagValidationLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(flagValidationLabel);
            root.Add(toolbar);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 320f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(BuildFlagListPane());

            TwoPaneSplitView innerSplit = new TwoPaneSplitView(0, 480f, TwoPaneSplitViewOrientation.Horizontal);
            innerSplit.Add(BuildFlagDetailPane());
            innerSplit.Add(BuildFlagReferencesPane());
            split.Add(innerSplit);

            root.Add(split);

            RefreshFlagList();
            RestoreFlagSelection();
        }

        // ---------------------------------------------------------------
        // Список
        // ---------------------------------------------------------------

        private VisualElement BuildFlagListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 8f;
            pane.style.paddingRight = 8f;
            pane.style.paddingTop = 8f;
            pane.style.paddingBottom = 8f;

            flagSearch = new TextField("Поиск");
            flagSearch.RegisterValueChangedCallback(_ => RefreshFlagList());
            pane.Add(flagSearch);

            flagList = new ListView();
            flagList.style.flexGrow = 1f;
            flagList.style.marginTop = 8f;
            flagList.fixedItemHeight = 40f;
            flagList.selectionType = SelectionType.Single;
            flagList.makeItem = MakeFlagListItem;
            flagList.bindItem = BindFlagListItem;
            flagList.selectionChanged += _ => SelectVisibleFlag(flagList.selectedIndex);
            pane.Add(flagList);

            return pane;
        }

        private static VisualElement MakeFlagListItem()
        {
            VisualElement row = new VisualElement();
            row.style.paddingTop = 4f;

            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(title);

            Label id = new Label { name = "id" };
            id.style.fontSize = 10f;
            id.style.color = MutedColor;
            row.Add(id);

            return row;
        }

        private void BindFlagListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleFlagIndices.Count)
                return;

            EncounterFlagDefinition flag = flagRegistry.Flags[visibleFlagIndices[visibleIndex]];
            string statusTag = flag.Status switch
            {
                EncounterFlagStatus.Active => "[Активен]",
                EncounterFlagStatus.Reserved => "[Резерв]",
                EncounterFlagStatus.Deprecated => "[Устарел]",
                _ => string.Empty
            };

            row.Q<Label>("title").text = statusTag + " " + flag.DisplayName;
            row.Q<Label>("id").text = flag.FlagId;
        }

        private void RefreshFlagList()
        {
            if (flagRegistry == null || flagList == null)
                return;

            serializedFlagRegistry.Update();
            visibleFlagIndices.Clear();

            string query = flagSearch != null ? flagSearch.value.Trim() : string.Empty;
            for (int i = 0; i < flagRegistry.Flags.Count; i++)
            {
                EncounterFlagDefinition flag = flagRegistry.Flags[i];
                if (flag == null)
                    continue;

                if (!string.IsNullOrEmpty(query) &&
                    (flag.FlagId == null || flag.FlagId.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (flag.DisplayName == null || flag.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                visibleFlagIndices.Add(i);
            }

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
        // Инспектор
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
            flagDetails.style.paddingLeft = 16f;
            flagDetails.style.paddingRight = 16f;
            flagDetails.style.paddingTop = 12f;
            flagDetails.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedFlagRegistry.ApplyModifiedProperties();
                EditorUtility.SetDirty(flagRegistry);
                flagList.RefreshItems();
                RefreshFlagReferences();
            });
            pane.Add(flagDetails);
            return pane;
        }

        private void ShowSelectedFlag()
        {
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagsProperty.arraySize)
            {
                flagEmptyHint.style.display = DisplayStyle.Flex;
                flagDetails.style.display = DisplayStyle.None;
                flagReferencesPane?.Clear();
                return;
            }

            flagEmptyHint.style.display = DisplayStyle.None;
            flagDetails.style.display = DisplayStyle.Flex;
            flagDetails.Clear();

            SerializedProperty f = flagsProperty.GetArrayElementAtIndex(selectedFlagIndex);
            AddHeader(flagDetails, "ФЛАГ");
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("FlagId"), "ID флага"));
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("DisplayName"), "Название"));
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("Description"), "Описание"));
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("Category"), "Категория"));
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("Status"), "Статус"));
            flagDetails.Add(MakeMutedLabel(
                "Reserved — намеренная закладка без потребителя сейчас (§30), это нормально. " +
                "Active без потребителя нигде — предупреждение при ПРОВЕРИТЬ (§31)."));
            flagDetails.Add(new PropertyField(f.FindPropertyRelative("FutureUseNotes"), "Заметки о будущем использовании"));

            flagDetails.Bind(serializedFlagRegistry);
            RefreshFlagReferences();
        }

        // ---------------------------------------------------------------
        // Dependency viewer (§33, §72-75) — Created By / Read By / Cleared By.
        // ---------------------------------------------------------------

        private VisualElement BuildFlagReferencesPane()
        {
            flagReferencesPane = new ScrollView();
            flagReferencesPane.style.flexGrow = 1f;
            flagReferencesPane.style.paddingLeft = 12f;
            flagReferencesPane.style.paddingRight = 12f;
            flagReferencesPane.style.paddingTop = 12f;
            return flagReferencesPane;
        }

        private void RefreshFlagReferences()
        {
            flagReferencesPane.Clear();
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagRegistry.Flags.Count)
                return;

            string flagId = flagRegistry.Flags[selectedFlagIndex].FlagId;
            if (string.IsNullOrWhiteSpace(flagId))
                return;

            List<string> createdBy = new List<string>();
            List<string> readBy = new List<string>();
            List<string> clearedBy = new List<string>();

            foreach (EncounterDefinition encounter in database.Encounters)
            {
                if (encounter == null)
                    continue;

                if (Contains(encounter.FlagsSetOnStart, flagId) || Contains(encounter.FlagsSetOnComplete, flagId))
                    createdBy.Add(encounter.EncounterId);

                if (Contains(encounter.ClearFlagsOnComplete, flagId))
                    clearedBy.Add(encounter.EncounterId);

                if (Contains(encounter.RequiredFlagsAll, flagId) ||
                    Contains(encounter.RequiredFlagsAny, flagId) ||
                    Contains(encounter.ForbiddenFlags, flagId) ||
                    ConditionGroupReferencesFlag(encounter.RequiredConditions, flagId))
                {
                    readBy.Add(encounter.EncounterId);
                }
            }

            AddHeader(flagReferencesPane, "ЗАВИСИМОСТИ");
            AddReferenceList("Создаётся в", createdBy);
            AddReferenceList("Читается в", readBy);
            AddReferenceList("Очищается в", clearedBy);
        }

        private void AddReferenceList(string title, List<string> encounterIds)
        {
            Label header = new Label(title + ":");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 6f;
            flagReferencesPane.Add(header);

            if (encounterIds.Count == 0)
            {
                flagReferencesPane.Add(MakeMutedLabel("никем"));
                return;
            }

            foreach (string id in encounterIds)
                flagReferencesPane.Add(new Label("    " + id));
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
        // Toolbar actions
        // ---------------------------------------------------------------

        private void AddFlag()
        {
            serializedFlagRegistry.Update();
            int index = flagsProperty.arraySize++;
            SerializedProperty f = flagsProperty.GetArrayElementAtIndex(index);
            f.FindPropertyRelative("FlagId").stringValue = MakeUniqueFlagId("NEW_FLAG");
            f.FindPropertyRelative("DisplayName").stringValue = "Новый флаг";
            f.FindPropertyRelative("Status").enumValueIndex = (int)EncounterFlagStatus.Reserved;
            serializedFlagRegistry.ApplyModifiedProperties();
            EditorUtility.SetDirty(flagRegistry);
            selectedFlagIndex = index;
            RefreshFlagList();
            ShowSelectedFlag();
        }

        // §34: флаг никогда не удаляется автоматически. Ручное удаление —
        // только явным подтверждением, и только когда он нигде не
        // используется (иначе это почти наверняка ошибка пользователя).
        private void RemoveFlag()
        {
            if (selectedFlagIndex < 0 || selectedFlagIndex >= flagsProperty.arraySize)
                return;

            string id = flagRegistry.Flags[selectedFlagIndex].FlagId;
            if (!EditorUtility.DisplayDialog("Удалить флаг", "Удалить «" + id + "» из реестра?", "Удалить", "Отмена"))
                return;

            serializedFlagRegistry.Update();
            flagsProperty.DeleteArrayElementAtIndex(selectedFlagIndex);
            serializedFlagRegistry.ApplyModifiedProperties();
            EditorUtility.SetDirty(flagRegistry);
            selectedFlagIndex = Mathf.Clamp(selectedFlagIndex - 1, -1, flagRegistry.Flags.Count - 1);
            RefreshFlagList();
            ShowSelectedFlag();
        }

        // Кросс-валидация с Encounter Database (§31, §32, §69-71) — здесь, а
        // не в EncounterFlagRegistryAsset.CollectValidationIssues, потому что
        // сам реестр не знает про базу энкаунтеров.
        private void ValidateFlagRegistry()
        {
            serializedFlagRegistry.ApplyModifiedProperties();
            serializedDatabase.ApplyModifiedProperties();

            List<string> issues = new List<string>();
            flagRegistry.CollectValidationIssues(issues);

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

            // ERROR: флаг используется в базе энкаунтеров, но не зарегистрирован.
            foreach (string flagId in referencedFlagIds)
            {
                if (!flagRegistry.IsKnownFlag(flagId))
                    issues.Add("ОШИБКА: флаг " + flagId + " используется в базе энкаунтеров, но не зарегистрирован в реестре флагов.");
            }

            // WARNING: Active-флаг без единого producer/consumer среди энкаунтеров.
            foreach (EncounterFlagDefinition flag in flagRegistry.Flags)
            {
                if (flag == null || flag.Status != EncounterFlagStatus.Active)
                    continue;
                if (!referencedFlagIds.Contains(flag.FlagId))
                    issues.Add("ПРЕДУПРЕЖДЕНИЕ: Active-флаг " + flag.FlagId + " не используется ни одним энкаунтером (§31).");
            }

            flagValidationLabel.text = issues.Count == 0 ? "Ошибок не найдено" : "Проблем: " + issues.Count;
            flagValidationLabel.style.color = issues.Count == 0 ? GoodColor : BadColor;
            if (issues.Count > 0)
                Debug.LogWarning("Реестр флагов:\n- " + string.Join("\n- ", issues));
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
