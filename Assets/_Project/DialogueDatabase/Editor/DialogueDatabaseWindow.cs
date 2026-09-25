using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    // База диалогов для автора текста. По умолчанию видно только
    // повествование: кто говорит, реплики, ответы и куда они ведут.
    // Производственная часть (ID, категории, статусы, условия, эффекты,
    // параметры проверок, служебные сводки графа) — за переключателем
    // «⚙ Производство». Превью открывается отдельным окном «как в игре»
    // (UI/Editor/DialogueGamePreviewWindow.cs) кнопкой «▶ Играть».
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private enum WindowTab
        {
            Dialogues,
            Speakers,
            Validation
        }

        private enum DialogueStructureMode
        {
            Graph,
            Table
        }

        private const float LeftWidth = 210f;
        private const string ShowProductionPrefKey = "KingdomSurvival.DialogueDatabase.ShowProduction";
        private const string ListCollapsedPrefKey = "KingdomSurvival.DialogueDatabase.ListCollapsed";

        private DialogueDatabaseAsset database;
        private WindowTab tab;
        private DialogueStructureMode structureMode = DialogueStructureMode.Graph;
        private bool dialogueMetaExpanded = true;
        private int selectedDialogueIndex;
        private int selectedSpeakerIndex;
        private Vector2 leftScroll;
        private Vector2 centerScroll;
        private string search = string.Empty;
        private int categoryFilter;
        private readonly List<string> validationIssues = new List<string>();
        private string validationMessage = string.Empty;

        // Показывать ли производственные поля. Личная настройка автора
        // (EditorPrefs), а не данные диалога.
        private bool showProduction;
        private bool listCollapsed;

        // Номера узлов текущего диалога для подписей «Узел N» без технических ID.
        private readonly Dictionary<string, int> nodeNumberById = new Dictionary<string, int>(StringComparer.Ordinal);
        private string currentStartNodeId = string.Empty;

        [MenuItem("Kingdom Survival/База диалогов")]
        private static void Open()
        {
            GetWindow<DialogueDatabaseWindow>("База диалогов");
        }

        public static void OpenAt(string dialogueId)
        {
            DialogueDatabaseWindow window = GetWindow<DialogueDatabaseWindow>("База диалогов");
            if (window.database == null)
                window.database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
            window.tab = WindowTab.Dialogues;
            window.categoryFilter = 0;
            window.search = dialogueId ?? string.Empty;
            if (window.database != null)
                for (int i = 0; i < window.database.Dialogues.Count; i++)
                    if (window.database.Dialogues[i].Id == dialogueId)
                    {
                        window.selectedDialogueIndex = i;
                        break;
                    }
            window.ResetPreview();
            window.Focus();
            window.Repaint();
        }

        private void OnEnable()
        {
            if (database == null)
                database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);

            showProduction = EditorPrefs.GetBool(ShowProductionPrefKey, false);
            listCollapsed = EditorPrefs.GetBool(ListCollapsedPrefKey, false);
            LoadGraphDetailMode();
            LoadGraphInspectorWidth();
        }

        private void OnGUI()
        {
            DrawHeader();

            if (database == null)
            {
                EditorGUILayout.HelpBox(
                    "Не найдена база диалогов. Назначьте её во включённом режиме «⚙ Производство» или создайте через Create > Kingdom Survival > База диалогов.",
                    MessageType.Warning);
                return;
            }

            switch (tab)
            {
                case WindowTab.Dialogues:
                    DrawDialoguesTab();
                    break;
                case WindowTab.Speakers:
                    DrawSpeakersTab();
                    break;
                case WindowTab.Validation:
                    DrawValidationTab();
                    break;
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (tab == WindowTab.Dialogues &&
                GUILayout.Button(new GUIContent(listCollapsed ? "☰" : "◂", listCollapsed ? "Показать список диалогов" : "Скрыть список диалогов"),
                    EditorStyles.toolbarButton, GUILayout.Width(26f)))
            {
                listCollapsed = !listCollapsed;
                EditorPrefs.SetBool(ListCollapsedPrefKey, listCollapsed);
            }

            if (GUILayout.Toggle(tab == WindowTab.Dialogues, "Диалоги", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                tab = WindowTab.Dialogues;
            if (GUILayout.Toggle(tab == WindowTab.Speakers, "Персонажи", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                tab = WindowTab.Speakers;
            if (GUILayout.Toggle(tab == WindowTab.Validation, "Ошибки", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                tab = WindowTab.Validation;

            if (showProduction)
            {
                GUILayout.Space(8f);
                database = (DialogueDatabaseAsset)EditorGUILayout.ObjectField(
                    database, typeof(DialogueDatabaseAsset), false, GUILayout.Width(200f));
            }

            GUILayout.FlexibleSpace();

            bool nextShowProduction = GUILayout.Toggle(
                showProduction,
                new GUIContent("⚙ Производство", "Показать технические поля: ID, категории, статусы, условия, эффекты, параметры проверок"),
                EditorStyles.toolbarButton,
                GUILayout.Width(110f));
            if (nextShowProduction != showProduction)
            {
                showProduction = nextShowProduction;
                EditorPrefs.SetBool(ShowProductionPrefKey, showProduction);
                GUI.FocusControl(null);
            }

            if (database != null && GUILayout.Button("Сохранить", EditorStyles.toolbarButton, GUILayout.Width(76f)))
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDialoguesTab()
        {
            SerializedObject serializedDatabase = new SerializedObject(database);
            serializedDatabase.Update();

            EditorGUILayout.BeginHorizontal();
            if (!listCollapsed)
                DrawDialogueList(serializedDatabase);
            DrawDialogueEditor(serializedDatabase);
            EditorGUILayout.EndHorizontal();

            if (serializedDatabase.hasModifiedProperties)
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
            }
        }

        private void DrawDialogueList(SerializedObject serializedDatabase)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftWidth), GUILayout.ExpandHeight(true));
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

            if (showProduction)
            {
                string[] categoryNames = new string[Enum.GetValues(typeof(DialogueCategory)).Length + 1];
                categoryNames[0] = "Все категории";
                for (int i = 1; i < categoryNames.Length; i++)
                    categoryNames[i] = CategoryLabel((DialogueCategory)(i - 1));
                categoryFilter = EditorGUILayout.Popup(categoryFilter, categoryNames);
            }

            GUILayout.Space(2f);
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                fixedHeight = showProduction ? 40f : 24f
            };
            for (int i = 0; i < dialogues.arraySize; i++)
            {
                SerializedProperty dialogue = dialogues.GetArrayElementAtIndex(i);
                string id = dialogue.FindPropertyRelative("id").stringValue;
                string title = dialogue.FindPropertyRelative("title").stringValue;
                DialogueCategory category = (DialogueCategory)dialogue.FindPropertyRelative("category").enumValueIndex;
                DialogueProductionStatus status = (DialogueProductionStatus)dialogue.FindPropertyRelative("status").enumValueIndex;

                if (!MatchesDialogueFilter(dialogue, category))
                    continue;

                string label = string.IsNullOrWhiteSpace(title) ? "<без названия>" : title;
                if (showProduction)
                    label += "\n" + id + " · " + StatusLabel(status);
                if (GUILayout.Toggle(selectedDialogueIndex == i, new GUIContent(label, id), buttonStyle))
                {
                    if (selectedDialogueIndex != i)
                    {
                        selectedDialogueIndex = i;
                        ResetPreview();
                        ResetGraphViewState();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Новый"))
                AddDialogue(serializedDatabase);
            GUI.enabled = dialogues.arraySize > 0;
            if (GUILayout.Button("Копия"))
                DuplicateDialogue(serializedDatabase);
            if (GUILayout.Button(new GUIContent("Удалить", "Удалить выбранный диалог")))
                DeleteDialogue(serializedDatabase);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawDialogueEditor(SerializedObject serializedDatabase)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            if (dialogues.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Создайте первый диалог.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            selectedDialogueIndex = Mathf.Clamp(selectedDialogueIndex, 0, dialogues.arraySize - 1);
            SerializedProperty dialogue = dialogues.GetArrayElementAtIndex(selectedDialogueIndex);
            SerializedProperty structureNodes = dialogue.FindPropertyRelative("nodes");
            RefreshNodeNumbers(dialogue);

            // Строка повествования: название и запуск превью «как в игре».
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Название", GUILayout.Width(62f));
            SerializedProperty title = dialogue.FindPropertyRelative("title");
            title.stringValue = EditorGUILayout.TextField(title.stringValue);
            if (GUILayout.Button(new GUIContent("▶ Играть", "Открыть окно диалога как в игре и пройти разговор"), GUILayout.Width(80f)))
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                DialogueGamePreview.Open(dialogue.FindPropertyRelative("id").stringValue);
            }
            EditorGUILayout.EndHorizontal();

            if (showProduction)
                DrawDialogueProductionBlock(serializedDatabase, dialogue, structureNodes);

            bool deleteGraphNode = false;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Toggle(structureMode == DialogueStructureMode.Graph, new GUIContent("Схема", "Узлы и связи на поле"), EditorStyles.toolbarButton, GUILayout.Width(62f)))
                structureMode = DialogueStructureMode.Graph;
            if (GUILayout.Toggle(structureMode == DialogueStructureMode.Table, new GUIContent("Текстом", "Все узлы списком для чтения и правки подряд"), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                structureMode = DialogueStructureMode.Table;
            GUILayout.FlexibleSpace();

            bool canDeleteGraphNode = structureMode == DialogueStructureMode.Graph &&
                                      structureNodes.arraySize > 1 &&
                                      graphSelectedNodeIndex >= 0 &&
                                      graphSelectedNodeIndex < structureNodes.arraySize;
            GUI.enabled = canDeleteGraphNode;
            if (GUILayout.Button("Удалить узел", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                deleteGraphNode = true;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (deleteGraphNode && TryDeleteDialogueNode(dialogue, graphSelectedNodeIndex))
            {
                GUIUtility.ExitGUI();
                return;
            }

            if (structureMode == DialogueStructureMode.Graph)
                DrawDialogueGraph(dialogue);
            else
                DrawDialogueTable(dialogue);

            EditorGUILayout.EndVertical();
        }

        // Производственная часть диалога — только при «⚙ Производство».
        private void DrawDialogueProductionBlock(SerializedObject serializedDatabase, SerializedProperty dialogue, SerializedProperty nodes)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            dialogueMetaExpanded = EditorGUILayout.Foldout(dialogueMetaExpanded, "ПРОИЗВОДСТВО", true);
            if (dialogueMetaExpanded)
            {
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("id"), new GUIContent("ID"));
                DrawLabeledEnumPopup<DialogueCategory>(dialogue.FindPropertyRelative("category"), "Категория", CategoryLabel);
                DrawLabeledEnumPopup<DialogueProductionStatus>(dialogue.FindPropertyRelative("status"), "Статус", StatusLabel);
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("developerComment"), new GUIContent("Комментарий разработчика"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("tags"), new GUIContent("Теги"), true);
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("sceneIllustration"),
                    new GUIContent("Иллюстрация события"));
                if (dialogue.FindPropertyRelative("sceneIllustration").objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("sceneIllustrationFillFrame"),
                        new GUIContent("Заполнить рамку (обрезать края)"));
                    SerializedProperty illustrationScale = dialogue.FindPropertyRelative("sceneIllustrationScale");
                    if (illustrationScale.floatValue <= 0f)
                        illustrationScale.floatValue = 1f;
                    EditorGUILayout.PropertyField(illustrationScale,
                        new GUIContent("Масштаб иллюстрации"));
                    EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("sceneIllustrationOffsetNormalized"),
                        new GUIContent("Смещение в рамке"));
                    EditorGUILayout.HelpBox("Иллюстрация заменяет портрет на протяжении всего диалога. Очистите поле, чтобы вернуть портреты говорящих.", MessageType.None);
                }

                DrawStartNodePopup(dialogue.FindPropertyRelative("startNodeId"), nodes);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Проверить диалог"))
                {
                    serializedDatabase.ApplyModifiedProperties();
                    ValidateSelected(dialogue.FindPropertyRelative("id").stringValue);
                }
                if (GUILayout.Button("Проверить всю базу"))
                {
                    serializedDatabase.ApplyModifiedProperties();
                    ValidateAll();
                }
                SerializedProperty schemaVersionProperty = dialogue.FindPropertyRelative("schemaVersion");
                if (schemaVersionProperty != null && schemaVersionProperty.intValue < DialogueDefinitionData.CurrentSchemaVersion &&
                    GUILayout.Button("Мигрировать на новую схему"))
                {
                    MigrateDialogueSchema(dialogue);
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(validationMessage))
                    EditorGUILayout.LabelField(validationMessage, EditorStyles.miniLabel);
                for (int i = 0; i < validationIssues.Count; i++)
                    EditorGUILayout.HelpBox(validationIssues[i], MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawLabeledEnumPopup<TEnum>(SerializedProperty property, string label, Func<TEnum, string> toLabel)
            where TEnum : Enum
        {
            TEnum[] values = (TEnum[])Enum.GetValues(typeof(TEnum));
            string[] labels = new string[values.Length];
            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                labels[i] = toLabel(values[i]);
                if (Convert.ToInt32(values[i]) == property.enumValueIndex)
                    selected = i;
            }
            int next = EditorGUILayout.Popup(label, selected, labels);
            if (next >= 0 && next < values.Length)
                property.enumValueIndex = Convert.ToInt32(values[next]);
        }

        // «Узел N» / «Начало» вместо технического ID в режиме повествования.
        private void RefreshNodeNumbers(SerializedProperty dialogue)
        {
            nodeNumberById.Clear();
            currentStartNodeId = dialogue.FindPropertyRelative("startNodeId").stringValue ?? string.Empty;
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrEmpty(id) && !nodeNumberById.ContainsKey(id))
                    nodeNumberById[id] = i + 1;
            }
        }

        private string NodeLabel(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return "<нет>";
            if (showProduction)
                return nodeId;
            if (string.Equals(nodeId, currentStartNodeId, StringComparison.Ordinal))
                return "Начало";
            return nodeNumberById.TryGetValue(nodeId, out int number) ? "Узел " + number : "? " + nodeId;
        }

        private void DrawDialogueTable(SerializedProperty dialogue)
        {
            // Таблица не переходит в NarrowInspector-режим: там всегда
            // достаточно места для прежнего горизонтального PropertyField
            // (§41 — режим «Таблица» не должен сломаться).
            inspectorLayoutMode = DialogueEditorLayoutMode.Normal;
            inspectorContentWidth = 700f;

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            centerScroll = EditorGUILayout.BeginScrollView(centerScroll);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("УЗЛЫ И ПЕРЕХОДЫ", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Узел", GUILayout.Width(90f)))
                AddNode(dialogue);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < nodes.arraySize; i++)
                DrawNode(dialogue, nodes, i);

            EditorGUILayout.EndScrollView();
        }

        private void DrawNode(SerializedProperty dialogue, SerializedProperty nodes, int nodeIndex)
        {
            SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
            string nodeId = node.FindPropertyRelative("id").stringValue;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            node.isExpanded = EditorGUILayout.Foldout(
                node.isExpanded,
                string.IsNullOrWhiteSpace(nodeId) ? "Узел без ID" : NodeLabel(nodeId),
                true);
            GUILayout.FlexibleSpace();
            GUI.enabled = nodes.arraySize > 1;
            if (GUILayout.Button(new GUIContent("×", "Удалить узел"), GUILayout.Width(28f)))
            {
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                if (TryDeleteDialogueNode(dialogue, nodeIndex))
                    GUIUtility.ExitGUI();
                return;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (node.isExpanded)
            {
                if (showProduction)
                    DrawLongIdField(node.FindPropertyRelative("id"), "ID узла");
                DrawSpeakerPopup(node.FindPropertyRelative("speakerId"));

                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                SerializedProperty legacyText = node.FindPropertyRelative("text");

                // Устаревший node.Text правится только у старого узла без
                // реплик; если реплики уже есть — поле спрятано и помечено.
                if (textBlocks.arraySize == 0)
                {
                    DrawAutoHeightNarrativeText(legacyText, "Реплика (старый формат)");
                }
                else if (!string.IsNullOrEmpty(legacyText.stringValue) && showProduction)
                {
                    EditorGUILayout.HelpBox(
                        "⚠ У узла есть текст старого формата, но используются реплики — игроку он не показывается.",
                        MessageType.Warning);
                    legacyTextFoldout = EditorGUILayout.Foldout(legacyTextFoldout, "УСТАРЕВШИЕ ДАННЫЕ", true);
                    if (legacyTextFoldout)
                        DrawAutoHeightNarrativeText(legacyText, "Реплика (старый формат)");
                }

                DrawSectionHeader("РЕПЛИКИ (" + textBlocks.arraySize + ")", SemanticCategory.Text);
                for (int blockIndex = 0; blockIndex < textBlocks.arraySize; blockIndex++)
                    DrawTextBlock(textBlocks, blockIndex, node.FindPropertyRelative("speakerId").stringValue);

                if (GUILayout.Button("＋ Реплика ▾", EditorStyles.miniButton))
                    ShowAddTextBlockMenu(textBlocks);

                SerializedProperty choices = node.FindPropertyRelative("choices");
                DrawSectionHeader("ОТВЕТЫ ИГРОКА (" + choices.arraySize + ")", SemanticCategory.Choice);
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                    DrawChoice(nodes, choices, choiceIndex);

                if (GUILayout.Button("＋ Ответ ▾", EditorStyles.miniButton))
                    ShowAddChoiceMenu(choices);
            }

            EditorGUILayout.EndVertical();
        }

        private void ShowAddTextBlockMenu(SerializedProperty textBlocks)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.MainLine)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.MainLine));
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.Observation)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.Observation));
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.Memory)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.Memory));
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.HeroThought)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.HeroThought));
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.CompanionLine)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.CompanionLine));
            menu.AddItem(new GUIContent(TextBlockKindLabel(DialogueTextBlockKind.Narration)), false,
                () => AddTextBlock(textBlocks, DialogueTextBlockKind.Narration));
            menu.ShowAsContext();
        }

        private void ShowAddChoiceMenu(SerializedProperty choices)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Обычный ответ"), false, () => AddChoice(choices));
            menu.AddItem(new GUIContent("«…» — читать дальше"), false, () => AddContinueChoice(choices));
            menu.AddItem(new GUIContent("Проверка (можно повторить)"), false,
                () => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveReturnable));
            menu.AddItem(new GUIContent("Проверка (решающая, один раз)"), false,
                () => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveDecisive));
            menu.AddItem(new GUIContent("Выход из разговора"), false, () => AddExitChoice(choices));
            menu.ShowAsContext();
        }

        private void DrawTextBlock(SerializedProperty textBlocks, int blockIndex, string nodeSpeakerId)
        {
            SerializedProperty block = textBlocks.GetArrayElementAtIndex(blockIndex);
            SerializedProperty kind = block.FindPropertyRelative("kind");

            BeginSemanticCard(null, SemanticCategory.Text);

            // Строка реплики: вид, кто говорит (если не говорящий узла), удалить.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((blockIndex + 1) + ".", EditorStyles.miniBoldLabel, GUILayout.Width(18f));
            DrawTextBlockKindPopup(kind, GUILayout.Width(150f));
            DrawSpeakerOverridePopup(block.FindPropertyRelative("speakerIdOverride"), nodeSpeakerId);
            if (GUILayout.Button(new GUIContent("×", "Удалить реплику"), GUILayout.Width(24f)))
            {
                textBlocks.DeleteArrayElementAtIndex(blockIndex);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Text);
                return;
            }
            EditorGUILayout.EndHorizontal();

            DrawAutoHeightNarrativeText(block.FindPropertyRelative("text"), null);

            SerializedProperty hasPassiveCheck = block.FindPropertyRelative("hasPassiveCheck");
            if (!showProduction)
            {
                // Для автора — одна строка о том, при чём эта реплика видна;
                // подробности правятся в «⚙ Производство».
                List<string> notes = new List<string>();
                if (hasPassiveCheck.boolValue)
                    notes.Add("◈ видна при пассивной проверке: " + DescribeCheck(block.FindPropertyRelative("passiveCheck")));
                int conditionCount = block.FindPropertyRelative("conditions").FindPropertyRelative("Conditions").arraySize;
                if (conditionCount > 0)
                    notes.Add("показ с условием (" + conditionCount + ")");
                int effectCount = block.FindPropertyRelative("onRevealEffects").arraySize;
                if (effectCount > 0)
                    notes.Add("последствий: " + effectCount);
                DrawMutedNote(notes);
                EndSemanticCard(SemanticCategory.Text);
                return;
            }

            DrawLongIdField(block.FindPropertyRelative("blockId"), "ID реплики");
            DrawConditionGroup(block.FindPropertyRelative("conditions"), "Условия показа");
            EditorGUILayout.PropertyField(hasPassiveCheck, new GUIContent("Есть пассивная проверка"));
            if (hasPassiveCheck.boolValue)
                DrawCheckSpec(block.FindPropertyRelative("passiveCheck"), "Пассивная проверка");
            DrawEffectsList(block.FindPropertyRelative("onRevealEffects"), "Последствия после показа");
            EndSemanticCard(SemanticCategory.Text);
        }

        private void DrawChoice(SerializedProperty nodes, SerializedProperty choices, int choiceIndex)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
            SerializedProperty text = choice.FindPropertyRelative("text");
            SerializedProperty kind = choice.FindPropertyRelative("kind");
            SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");
            DialogueChoiceKind choiceKind = (DialogueChoiceKind)kind.enumValueIndex;

            BeginSemanticCard(null, SemanticCategory.Choice);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ОТВЕТ " + (choiceIndex + 1) + " · " + ChoiceKindLabel(choiceKind).ToUpperInvariant(), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("×", "Удалить ответ"), GUILayout.Width(28f)))
            {
                choices.DeleteArrayElementAtIndex(choiceIndex);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Choice);
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (choiceKind == DialogueChoiceKind.Continue)
                EditorGUILayout.LabelField("В игре — кнопка «…»: читать дальше, не слова героя.", EditorStyles.miniLabel);
            else
                DrawAutoHeightNarrativeText(text, null, 32f);

            if (showProduction)
            {
                DrawConditionGroup(choice.FindPropertyRelative("conditions"), "Условия показа");
                if (choiceKind != DialogueChoiceKind.Normal || choice.FindPropertyRelative("conditions").FindPropertyRelative("Conditions").arraySize > 0)
                    DrawUnavailablePresentationPopup(choice.FindPropertyRelative("unavailablePresentation"));
            }

            switch (choiceKind)
            {
                case DialogueChoiceKind.Exit:
                    EditorGUILayout.LabelField("Завершает разговор.", EditorStyles.miniLabel);
                    break;

                case DialogueChoiceKind.Continue:
                    DrawNodeTargetPopup(nextNodeId, nodes, "Дальше");
                    break;

                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    if (showProduction)
                        DrawCheckSpec(choice.FindPropertyRelative("check"), "Проверка");
                    else
                        EditorGUILayout.LabelField("◆ Проверка: " + DescribeCheck(choice.FindPropertyRelative("check")), EditorStyles.miniLabel);
                    DrawNodeTargetPopup(choice.FindPropertyRelative("successNodeId"), nodes, "Если успех");
                    DrawNodeTargetPopup(choice.FindPropertyRelative("failureNodeId"), nodes, "Если провал");
                    if (showProduction)
                    {
                        DrawEffectsList(choice.FindPropertyRelative("successEffects"), "Последствия успеха", SemanticCategory.Effect);
                        DrawEffectsList(choice.FindPropertyRelative("failureEffects"), "Последствия провала", SemanticCategory.Error);
                    }
                    break;

                default:
                    endsDialogue.boolValue = EditorGUILayout.ToggleLeft("Завершает разговор", endsDialogue.boolValue);
                    if (endsDialogue.boolValue)
                        nextNodeId.stringValue = string.Empty;
                    else
                        DrawNodeTargetPopup(nextNodeId, nodes, "Ведёт к");
                    break;
            }

            if (!showProduction)
            {
                List<string> notes = new List<string>();
                int conditionCount = choice.FindPropertyRelative("conditions").FindPropertyRelative("Conditions").arraySize;
                if (conditionCount > 0)
                    notes.Add("доступен при условии (" + conditionCount + ")");
                int effectCount = choice.FindPropertyRelative("successEffects").arraySize + choice.FindPropertyRelative("failureEffects").arraySize;
                if (effectCount > 0)
                    notes.Add("последствий: " + effectCount);
                DrawMutedNote(notes);
            }

            EndSemanticCard(SemanticCategory.Choice);
        }

        // «ЧУТЬЁ + СЛЕДОПЫТСТВО · 13 (непросто)» — параметры проверки одной строкой.
        private static string DescribeCheck(SerializedProperty check)
        {
            if (check == null)
                return "—";
            NarrativeCheckSpec spec = (NarrativeCheckSpec)check.boxedValue;
            if (spec == null)
                return "—";
            return BuildGraphActiveCheckSummary(spec) + " (" + NarrativeDifficultyLabels.Describe(spec.Difficulty) + ")";
        }

        private static void DrawMutedNote(List<string> notes)
        {
            if (notes == null || notes.Count == 0)
                return;
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            style.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.62f, 0.6f, 1f)
                : new Color(0.4f, 0.4f, 0.38f, 1f);
            EditorGUILayout.LabelField(string.Join(" · ", notes), style);
        }

        // Говорящий отдельной реплики: по умолчанию — говорящий узла.
        private void DrawSpeakerOverridePopup(SerializedProperty speakerIdOverride, string nodeSpeakerId)
        {
            IReadOnlyList<DialogueSpeakerData> speakers = database.Speakers;
            DialogueSpeakerData nodeSpeaker = database.FindSpeaker(nodeSpeakerId);
            string[] labels = new string[speakers.Count + 1];
            labels[0] = "говорит: " + (nodeSpeaker != null ? nodeSpeaker.DisplayName : "как в узле");
            int selected = 0;
            for (int i = 0; i < speakers.Count; i++)
            {
                labels[i + 1] = "говорит: " + speakers[i].DisplayName + (showProduction ? "  [" + speakers[i].Id + "]" : string.Empty);
                if (!string.IsNullOrEmpty(speakerIdOverride.stringValue) &&
                    string.Equals(speakers[i].Id, speakerIdOverride.stringValue, StringComparison.Ordinal))
                    selected = i + 1;
            }

            int next = EditorGUILayout.Popup(selected, labels);
            if (next != selected)
                speakerIdOverride.stringValue = next == 0 ? string.Empty : speakers[next - 1].Id;
        }

        private static string ChoiceKindLabel(DialogueChoiceKind kind)
        {
            switch (kind)
            {
                case DialogueChoiceKind.Normal: return "обычный";
                case DialogueChoiceKind.Continue: return "«…» читать дальше";
                case DialogueChoiceKind.ActiveReturnable: return "проверка, можно повторить";
                case DialogueChoiceKind.ActiveDecisive: return "решающая проверка";
                case DialogueChoiceKind.Exit: return "выход";
                default: return kind.ToString();
            }
        }

        private static readonly DialogueTextBlockKind[] TextBlockKindValues =
            (DialogueTextBlockKind[])Enum.GetValues(typeof(DialogueTextBlockKind));

        private static string TextBlockKindLabel(DialogueTextBlockKind kind)
        {
            switch (kind)
            {
                case DialogueTextBlockKind.MainLine: return "Основная реплика";
                case DialogueTextBlockKind.Observation: return "Наблюдение";
                case DialogueTextBlockKind.Memory: return "Воспоминание";
                case DialogueTextBlockKind.HeroThought: return "Мысль героя";
                case DialogueTextBlockKind.CompanionLine: return "Реплика спутника";
                case DialogueTextBlockKind.Narration: return "Повествование";
                default: return kind.ToString();
            }
        }

        private static void DrawTextBlockKindPopup(SerializedProperty kindProperty, params GUILayoutOption[] options)
        {
            string[] labels = new string[TextBlockKindValues.Length];
            int selected = 0;
            for (int i = 0; i < TextBlockKindValues.Length; i++)
            {
                labels[i] = TextBlockKindLabel(TextBlockKindValues[i]);
                if (kindProperty.enumValueIndex == (int)TextBlockKindValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup(selected, labels, options);
            if (next >= 0 && next < TextBlockKindValues.Length)
                kindProperty.enumValueIndex = (int)TextBlockKindValues[next];
        }

        private static readonly DialogueChoiceUnavailablePresentation[] UnavailablePresentationValues =
            (DialogueChoiceUnavailablePresentation[])Enum.GetValues(typeof(DialogueChoiceUnavailablePresentation));

        private static string UnavailablePresentationLabel(DialogueChoiceUnavailablePresentation presentation)
        {
            switch (presentation)
            {
                case DialogueChoiceUnavailablePresentation.Hidden: return "Скрыт";
                case DialogueChoiceUnavailablePresentation.DisabledWithHint: return "Показан неактивным с подсказкой";
                default: return presentation.ToString();
            }
        }

        private static void DrawUnavailablePresentationPopup(SerializedProperty presentationProperty)
        {
            string[] labels = new string[UnavailablePresentationValues.Length];
            int selected = 0;
            for (int i = 0; i < UnavailablePresentationValues.Length; i++)
            {
                labels[i] = UnavailablePresentationLabel(UnavailablePresentationValues[i]);
                if (presentationProperty.enumValueIndex == (int)UnavailablePresentationValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Если недоступен", selected, labels);
            if (next >= 0 && next < UnavailablePresentationValues.Length)
                presentationProperty.enumValueIndex = (int)UnavailablePresentationValues[next];
        }

        // Условия показа (NarrativeConditionGroup/NarrativeCondition, Core)
        // рисовались обычным PropertyField, поэтому Combinator/Type/имена
        // полей и значения HeroQuality выходили на английском (см. запрос
        // о русификации базы диалогов). Этот блок — единственное место, где
        // условия рисуются, и используется и «Таблицей», и «Графом»
        // (DrawChoice/DrawTextBlock общие для обоих режимов).

        private static readonly NarrativeConditionCombinator[] CombinatorValues =
            (NarrativeConditionCombinator[])Enum.GetValues(typeof(NarrativeConditionCombinator));

        private static string CombinatorLabel(NarrativeConditionCombinator combinator)
        {
            switch (combinator)
            {
                case NarrativeConditionCombinator.All: return "Выполнены все условия";
                case NarrativeConditionCombinator.Any: return "Выполнено хотя бы одно";
                default: return combinator.ToString();
            }
        }

        private static void DrawConditionCombinatorPopup(SerializedProperty combinatorProperty)
        {
            string[] labels = new string[CombinatorValues.Length];
            int selected = 0;
            for (int i = 0; i < CombinatorValues.Length; i++)
            {
                labels[i] = CombinatorLabel(CombinatorValues[i]);
                if (combinatorProperty.enumValueIndex == (int)CombinatorValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Как проверять", selected, labels);
            if (next >= 0 && next < CombinatorValues.Length)
                combinatorProperty.enumValueIndex = (int)CombinatorValues[next];
        }

        private static readonly NarrativeConditionType[] ConditionTypeValues =
            (NarrativeConditionType[])Enum.GetValues(typeof(NarrativeConditionType));

        private static string ConditionTypeLabel(NarrativeConditionType type)
        {
            switch (type)
            {
                case NarrativeConditionType.FlagSet: return "Событие / решение уже было";
                case NarrativeConditionType.KnowledgeKnown: return "Герой это знает";
                case NarrativeConditionType.RelationAtLeast: return "Отношение не ниже";
                case NarrativeConditionType.RelationAtMost: return "Отношение не выше";
                case NarrativeConditionType.CompanionPresent: return "Спутник рядом";
                case NarrativeConditionType.ItemPresent: return "Предмет есть у героя";
                case NarrativeConditionType.QualityAtLeast: return "Качество не ниже";
                case NarrativeConditionType.CompetencyAtLeast: return "Компетенция не ниже";
                case NarrativeConditionType.CheckSucceeded: return "Проверка была успешной";
                case NarrativeConditionType.CheckFailed: return "Проверка была провалена";
                case NarrativeConditionType.CheckNotAttempted: return "Проверка ещё не выполнялась";
                case NarrativeConditionType.TraitPresent: return "У героя есть особенность";
                case NarrativeConditionType.PartySizeAtLeast: return "Размер отряда не меньше";
                case NarrativeConditionType.PartySizeAtMost: return "Размер отряда не больше";
                case NarrativeConditionType.EffectApplied: return "Эффект уже применён";
                default: return type.ToString();
            }
        }

        private static void DrawConditionTypePopup(SerializedProperty typeProperty)
        {
            string[] labels = new string[ConditionTypeValues.Length];
            int selected = 0;
            for (int i = 0; i < ConditionTypeValues.Length; i++)
            {
                labels[i] = ConditionTypeLabel(ConditionTypeValues[i]);
                if (typeProperty.enumValueIndex == (int)ConditionTypeValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Тип", selected, labels);
            if (next >= 0 && next < ConditionTypeValues.Length)
                typeProperty.enumValueIndex = (int)ConditionTypeValues[next];
        }

        private static readonly HeroQuality[] HeroQualityValues =
            (HeroQuality[])Enum.GetValues(typeof(HeroQuality));

        private static void DrawHeroQualityPopup(SerializedProperty qualityProperty, string label)
        {
            string[] labels = new string[HeroQualityValues.Length];
            int selected = 0;
            for (int i = 0; i < HeroQualityValues.Length; i++)
            {
                labels[i] = NarrativeQualityLabels.GetLabel(HeroQualityValues[i]);
                if (qualityProperty.enumValueIndex == (int)HeroQualityValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup(label, selected, labels);
            if (next >= 0 && next < HeroQualityValues.Length)
                qualityProperty.enumValueIndex = (int)HeroQualityValues[next];
        }

        private void DrawConditionGroup(SerializedProperty group, string label)
        {
            DrawSectionHeader(label, SemanticCategory.Condition);
            EditorGUI.indentLevel++;

            DrawConditionCombinatorPopup(group.FindPropertyRelative("Combinator"));

            SerializedProperty conditions = group.FindPropertyRelative("Conditions");
            for (int i = 0; i < conditions.arraySize; i++)
            {
                if (DrawCondition(conditions, i))
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Условие", EditorStyles.miniButton, GUILayout.Width(110f)))
                AddCondition(conditions);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        // Возвращает true, если условие было удалено — тогда вызывающий
        // цикл должен остановиться в этом кадре (индексы после удаления
        // сместились).
        private bool DrawCondition(SerializedProperty conditions, int index)
        {
            SerializedProperty condition = conditions.GetArrayElementAtIndex(index);
            SerializedProperty type = condition.FindPropertyRelative("Type");
            SerializedProperty stringParam = condition.FindPropertyRelative("StringParam");
            SerializedProperty qualityParam = condition.FindPropertyRelative("QualityParam");
            SerializedProperty intParam = condition.FindPropertyRelative("IntParam");
            SerializedProperty negate = condition.FindPropertyRelative("Negate");

            BeginSemanticCard(null, SemanticCategory.Condition);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Условие " + (index + 1), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                conditions.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Condition);
                return true;
            }
            EditorGUILayout.EndHorizontal();

            DrawConditionTypePopup(type);
            NarrativeConditionType kind = (NarrativeConditionType)type.enumValueIndex;

            switch (kind)
            {
                case NarrativeConditionType.QualityAtLeast:
                    DrawHeroQualityPopup(qualityParam, "Качество");
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Минимум"));
                    break;

                case NarrativeConditionType.CompetencyAtLeast:
                    DrawLongIdField(stringParam, "Компетенция (ID)");
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Минимум"));
                    break;

                case NarrativeConditionType.RelationAtLeast:
                    DrawLongIdField(stringParam, "С кем считаем отношение (ID)");
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Минимум"));
                    break;

                case NarrativeConditionType.RelationAtMost:
                    DrawLongIdField(stringParam, "С кем считаем отношение (ID)");
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Максимум"));
                    break;

                case NarrativeConditionType.FlagSet:
                    DrawLongIdField(stringParam, "Событие / решение (ID)");
                    break;

                case NarrativeConditionType.KnowledgeKnown:
                    DrawLongIdField(stringParam, "Знание (ID)");
                    break;

                case NarrativeConditionType.CompanionPresent:
                    DrawLongIdField(stringParam, "Спутник (ID)");
                    break;

                case NarrativeConditionType.ItemPresent:
                    DrawLongIdField(stringParam, "Предмет (ID)");
                    break;

                case NarrativeConditionType.CheckSucceeded:
                case NarrativeConditionType.CheckFailed:
                case NarrativeConditionType.CheckNotAttempted:
                    DrawLongIdField(stringParam, "Проверка (ID)");
                    break;

                case NarrativeConditionType.TraitPresent:
                    DrawLongIdField(stringParam, "Особенность (ID)");
                    break;

                case NarrativeConditionType.PartySizeAtLeast:
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Минимум (герой + бойцы)"));
                    break;

                case NarrativeConditionType.PartySizeAtMost:
                    EditorGUILayout.PropertyField(intParam, new GUIContent("Максимум (герой + бойцы)"));
                    break;

                case NarrativeConditionType.EffectApplied:
                    DrawLongIdField(stringParam, "Применённый эффект (ID)");
                    break;
            }

            EditorGUILayout.PropertyField(negate, new GUIContent("Обратить условие"));

            EndSemanticCard(SemanticCategory.Condition);
            return false;
        }

        private static void AddCondition(SerializedProperty conditions)
        {
            conditions.arraySize++;
            SerializedProperty added = conditions.GetArrayElementAtIndex(conditions.arraySize - 1);
            added.FindPropertyRelative("Type").enumValueIndex = 0;
            added.FindPropertyRelative("StringParam").stringValue = string.Empty;
            added.FindPropertyRelative("QualityParam").enumValueIndex = 0;
            added.FindPropertyRelative("IntParam").intValue = 0;
            added.FindPropertyRelative("Negate").boolValue = false;
        }

        // Проверка (NarrativeCheckSpec, Core) и эффекты (NarrativeEffect,
        // Core) — та же проблема и тот же приём, что и с условиями: обычный
        // PropertyField показывал английские имена полей/enum-констант.
        // Общие для «Таблицы» и панели «СВОЙСТВА УЗЛА» в «Графе».

        private static readonly NarrativeCheckKind[] CheckKindValues =
            (NarrativeCheckKind[])Enum.GetValues(typeof(NarrativeCheckKind));

        private static string CheckKindLabel(NarrativeCheckKind kind)
        {
            switch (kind)
            {
                case NarrativeCheckKind.Passive: return "Пассивная";
                case NarrativeCheckKind.ActiveReturnable: return "Активная возвратная";
                case NarrativeCheckKind.ActiveDecisive: return "Активная решающая";
                default: return kind.ToString();
            }
        }

        private static void DrawCheckKindPopup(SerializedProperty kindProperty)
        {
            string[] labels = new string[CheckKindValues.Length];
            int selected = 0;
            for (int i = 0; i < CheckKindValues.Length; i++)
            {
                labels[i] = CheckKindLabel(CheckKindValues[i]);
                if (kindProperty.enumValueIndex == (int)CheckKindValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Вид проверки", selected, labels);
            if (next >= 0 && next < CheckKindValues.Length)
                kindProperty.enumValueIndex = (int)CheckKindValues[next];
        }

        private void DrawCheckSpec(SerializedProperty check, string label)
        {
            BeginSemanticCard(label, SemanticCategory.Check);
            EditorGUI.indentLevel++;

            DrawLongIdField(check.FindPropertyRelative("CheckId"), "ID проверки");
            DrawCheckKindPopup(check.FindPropertyRelative("Kind"));
            DrawHeroQualityPopup(check.FindPropertyRelative("Quality"), "Качество");
            DrawLongIdField(check.FindPropertyRelative("CompetencyId"), "ID компетенции (пусто — не участвует)");
            SerializedProperty difficultyProperty = check.FindPropertyRelative("Difficulty");
            EditorGUILayout.PropertyField(difficultyProperty, new GUIContent("Сложность (9-23)"));
            EditorGUILayout.LabelField("→ " + NarrativeDifficultyLabels.Describe(difficultyProperty.intValue), EditorStyles.miniLabel);

            SerializedProperty modifierRules = check.FindPropertyRelative("ModifierRules");
            EditorGUILayout.LabelField("Контекстные модификаторы", EditorStyles.miniBoldLabel);
            for (int i = 0; i < modifierRules.arraySize; i++)
            {
                if (DrawModifierRule(modifierRules, i))
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Модификатор", EditorStyles.miniButton, GUILayout.Width(130f)))
                AddModifierRule(modifierRules);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EndSemanticCard(SemanticCategory.Check);
        }

        private bool DrawModifierRule(SerializedProperty rules, int index)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Модификатор " + (index + 1), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                rules.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return true;
            }
            EditorGUILayout.EndHorizontal();

            DrawLongIdField(rule.FindPropertyRelative("SourceId"), "ID источника");
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("Label"), new GUIContent("Подпись"));
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("Value"), new GUIContent("Значение"));
            DrawConditionGroup(rule.FindPropertyRelative("Condition"), "Условие применения");

            EditorGUILayout.EndVertical();
            return false;
        }

        private static void AddModifierRule(SerializedProperty rules)
        {
            rules.arraySize++;
            SerializedProperty added = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            added.FindPropertyRelative("SourceId").stringValue = string.Empty;
            added.FindPropertyRelative("Label").stringValue = string.Empty;
            added.FindPropertyRelative("Value").intValue = 0;
            SerializedProperty condition = added.FindPropertyRelative("Condition");
            condition.FindPropertyRelative("Combinator").enumValueIndex = 0;
            condition.FindPropertyRelative("Conditions").ClearArray();
        }

        private static readonly NarrativeEffectType[] EffectTypeValues =
            (NarrativeEffectType[])Enum.GetValues(typeof(NarrativeEffectType));

        private static string EffectTypeLabel(NarrativeEffectType type)
        {
            switch (type)
            {
                case NarrativeEffectType.SetFlag: return "Установить флаг";
                case NarrativeEffectType.ClearFlag: return "Снять флаг";
                case NarrativeEffectType.AddKnowledge: return "Добавить знание";
                case NarrativeEffectType.ChangeRelation: return "Изменить отношение";
                case NarrativeEffectType.UnlockCheck: return "Разблокировать проверку";
                case NarrativeEffectType.GrantTrait: return "Дать особенность";
                case NarrativeEffectType.RemoveTrait: return "Убрать особенность";
                case NarrativeEffectType.GrantItem: return "Дать предмет";
                case NarrativeEffectType.RemoveItem: return "Забрать предмет";
                case NarrativeEffectType.ChangeFood: return "Изменить еду Дома";
                case NarrativeEffectType.ChangeSupplies: return "Изменить припасы похода";
                case NarrativeEffectType.ShortcutRouteCells: return "Сократить путь (клеток)";
                default: return type.ToString();
            }
        }

        private static void DrawEffectTypePopup(SerializedProperty typeProperty)
        {
            string[] labels = new string[EffectTypeValues.Length];
            int selected = 0;
            for (int i = 0; i < EffectTypeValues.Length; i++)
            {
                labels[i] = EffectTypeLabel(EffectTypeValues[i]);
                if (typeProperty.enumValueIndex == (int)EffectTypeValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Тип эффекта", selected, labels);
            if (next >= 0 && next < EffectTypeValues.Length)
                typeProperty.enumValueIndex = (int)EffectTypeValues[next];
        }

        private static string EffectStringParamLabel(NarrativeEffectType type)
        {
            switch (type)
            {
                case NarrativeEffectType.SetFlag:
                case NarrativeEffectType.ClearFlag:
                    return "ID флага";
                case NarrativeEffectType.AddKnowledge:
                    return "ID знания";
                case NarrativeEffectType.ChangeRelation:
                    return "ID субъекта отношения";
                case NarrativeEffectType.UnlockCheck:
                    return "ID блокируемой проверки";
                case NarrativeEffectType.GrantTrait:
                case NarrativeEffectType.RemoveTrait:
                    return "ID особенности";
                case NarrativeEffectType.GrantItem:
                case NarrativeEffectType.RemoveItem:
                    return "ID предмета";
                default:
                    return "Параметр";
            }
        }

        // §25: обычные эффекты (после показа/после разговора) — зелёная
        // категория Effect; эффекты провала внутри ответа с активной
        // проверкой красят вызывающий код в Error (см. DrawChoice) — та же
        // отрисовка, другая акцентная полоса.
        private void DrawEffectsList(SerializedProperty effects, string label)
        {
            DrawEffectsList(effects, label, SemanticCategory.Effect);
        }

        private void DrawEffectsList(SerializedProperty effects, string label, SemanticCategory category)
        {
            DrawSectionHeader(label, category);
            EditorGUI.indentLevel++;

            for (int i = 0; i < effects.arraySize; i++)
            {
                if (DrawEffect(effects, i, category))
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Эффект", EditorStyles.miniButton, GUILayout.Width(110f)))
                AddEffect(effects);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private bool DrawEffect(SerializedProperty effects, int index, SemanticCategory category)
        {
            SerializedProperty effect = effects.GetArrayElementAtIndex(index);
            SerializedProperty type = effect.FindPropertyRelative("Type");
            SerializedProperty stringParam = effect.FindPropertyRelative("StringParam");
            SerializedProperty intParam = effect.FindPropertyRelative("IntParam");
            NarrativeEffectType kind = (NarrativeEffectType)type.enumValueIndex;

            BeginSemanticCard(null, category);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Эффект " + (index + 1) + " — " + EffectTypeLabel(kind), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                effects.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(category);
                return true;
            }
            EditorGUILayout.EndHorizontal();

            DrawLongIdField(effect.FindPropertyRelative("EffectExecutionId"), "ID применения (уникальный)");
            DrawEffectTypePopup(type);
            kind = (NarrativeEffectType)type.enumValueIndex;
            DrawLongIdField(stringParam, EffectStringParamLabel(kind));
            if (kind == NarrativeEffectType.ChangeRelation || kind == NarrativeEffectType.ChangeFood ||
                kind == NarrativeEffectType.ChangeSupplies)
                EditorGUILayout.PropertyField(intParam, new GUIContent("Изменение (+/−)"));
            else if (kind == NarrativeEffectType.ShortcutRouteCells)
                EditorGUILayout.PropertyField(intParam, new GUIContent("Клеток вперёд"));

            EndSemanticCard(category);
            return false;
        }

        private static void AddEffect(SerializedProperty effects)
        {
            effects.arraySize++;
            SerializedProperty added = effects.GetArrayElementAtIndex(effects.arraySize - 1);
            added.FindPropertyRelative("EffectExecutionId").stringValue = string.Empty;
            added.FindPropertyRelative("Type").enumValueIndex = 0;
            added.FindPropertyRelative("StringParam").stringValue = string.Empty;
            added.FindPropertyRelative("IntParam").intValue = 0;
        }

        private void DrawStartNodePopup(SerializedProperty startNodeId, SerializedProperty nodes)
        {
            string[] ids = GetNodeIds(nodes);
            if (ids.Length == 0)
            {
                EditorGUILayout.PropertyField(startNodeId, new GUIContent("Стартовый узел"));
                return;
            }

            int selected = Array.IndexOf(ids, startNodeId.stringValue);
            if (selected < 0)
                selected = 0;
            int next = EditorGUILayout.Popup("Стартовый узел", selected, BuildNodeLabels(ids, nodes));
            if (next >= 0 && next < ids.Length)
                startNodeId.stringValue = ids[next];
        }

        private void DrawNodeTargetPopup(SerializedProperty targetNodeId, SerializedProperty nodes, string label = "Переход")
        {
            string[] ids = GetNodeIds(nodes);
            if (ids.Length == 0)
            {
                EditorGUILayout.PropertyField(targetNodeId, new GUIContent(label));
                return;
            }

            int selected = Array.IndexOf(ids, targetNodeId.stringValue);
            if (selected < 0)
                selected = 0;
            int next = EditorGUILayout.Popup(label, selected, BuildNodeLabels(ids, nodes));
            if (next >= 0 && next < ids.Length)
                targetNodeId.stringValue = ids[next];
        }

        // «Узел 3 · «Под подстилкой оказывается…»» — номер и начало первой
        // реплики; в «⚙ Производство» — технический ID.
        private string[] BuildNodeLabels(string[] ids, SerializedProperty nodes)
        {
            string[] labels = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                if (showProduction)
                {
                    labels[i] = ids[i];
                    continue;
                }
                string preview = string.Empty;
                for (int n = 0; n < nodes.arraySize; n++)
                {
                    SerializedProperty node = nodes.GetArrayElementAtIndex(n);
                    if (node.FindPropertyRelative("id").stringValue != ids[i])
                        continue;
                    SerializedProperty blocks = node.FindPropertyRelative("textBlocks");
                    preview = blocks.arraySize > 0
                        ? blocks.GetArrayElementAtIndex(0).FindPropertyRelative("text").stringValue
                        : node.FindPropertyRelative("text").stringValue;
                    break;
                }
                preview = TruncateForGraphPreview(preview, 36).Replace('/', '∕');
                labels[i] = NodeLabel(ids[i]) + (string.IsNullOrEmpty(preview) ? string.Empty : " · «" + preview + "»");
            }
            return labels;
        }

        private void DrawSpeakerPopup(SerializedProperty speakerId)
        {
            IReadOnlyList<DialogueSpeakerData> speakers = database.Speakers;
            if (speakers.Count == 0)
            {
                EditorGUILayout.PropertyField(speakerId, new GUIContent("Говорящий"));
                EditorGUILayout.HelpBox("Сначала добавьте персонажа во вкладке «Персонажи».", MessageType.Warning);
                return;
            }

            string[] labels = new string[speakers.Count];
            int selected = 0;
            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                labels[i] = showProduction ? speaker.DisplayName + "  [" + speaker.Id + "]" : speaker.DisplayName;
                if (string.Equals(speaker.Id, speakerId.stringValue, StringComparison.Ordinal))
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Говорящий", selected, labels);
            if (next >= 0 && next < speakers.Count)
                speakerId.stringValue = speakers[next].Id;
        }
    }
}
