using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
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

        private const float LeftWidth = 250f;
        private const float RightWidth = 330f;

        private DialogueDatabaseAsset database;
        private WindowTab tab;
        private DialogueStructureMode structureMode = DialogueStructureMode.Graph;
        private bool dialogueMetaExpanded = true;
        private int selectedDialogueIndex;
        private int selectedSpeakerIndex;
        private Vector2 leftScroll;
        private Vector2 centerScroll;
        private Vector2 rightScroll;
        private string search = string.Empty;
        private int categoryFilter;
        private NarrativeDialogueRuntimeSession previewSession;
        private NarrativeDialogueView previewView;
        private string previewDialogueId = string.Empty;
        private string previewMessage = string.Empty;
        private readonly List<string> validationIssues = new List<string>();

        // Авторский Preview-контекст (§13): не связан с реальным сохранением
        // игры, существует только пока открыто окно редактора.
        private HeroProfileData previewHero = new HeroProfileData();
        private NarrativeStateData previewState = new NarrativeStateData();
        private bool previewHeroExpanded;
        private string previewCompanionsCsv = string.Empty;
        private string previewItemsCsv = string.Empty;
        private string previewFlagsCsv = string.Empty;
        private string previewKnowledgeCsv = string.Empty;
        private string previewRelationsCsv = string.Empty;
        private int previewWorldSeed = 12345;
        private NarrativeCheckForcedOutcome previewForcedOutcome = NarrativeCheckForcedOutcome.None;

        [MenuItem("Kingdom Survival/База диалогов")]
        private static void Open()
        {
            GetWindow<DialogueDatabaseWindow>("База диалогов");
        }

        private void OnEnable()
        {
            if (database == null)
                database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        }

        private void OnGUI()
        {
            DrawHeader();

            if (database == null)
            {
                EditorGUILayout.HelpBox(
                    "Не найдена база диалогов. Назначьте DialogueDatabaseAsset или создайте её через Create > Kingdom Survival > База диалогов.",
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
            database = (DialogueDatabaseAsset)EditorGUILayout.ObjectField(
                database,
                typeof(DialogueDatabaseAsset),
                false,
                GUILayout.Width(260f));

            GUILayout.Space(8f);
            if (GUILayout.Toggle(tab == WindowTab.Dialogues, "Диалоги", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                tab = WindowTab.Dialogues;
            if (GUILayout.Toggle(tab == WindowTab.Speakers, "Говорящие", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                tab = WindowTab.Speakers;
            if (GUILayout.Toggle(tab == WindowTab.Validation, "Проверка", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                tab = WindowTab.Validation;

            GUILayout.FlexibleSpace();
            if (database != null && GUILayout.Button("Сохранить Asset", EditorStyles.toolbarButton, GUILayout.Width(110f)))
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
            DrawDialogueList(serializedDatabase);
            DrawDialogueEditor(serializedDatabase);
            DrawPreviewPanel(serializedDatabase);
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
            search = EditorGUILayout.TextField("Поиск", search);

            string[] categoryNames = new string[Enum.GetValues(typeof(DialogueCategory)).Length + 1];
            categoryNames[0] = "Все категории";
            for (int i = 1; i < categoryNames.Length; i++)
                categoryNames[i] = CategoryLabel((DialogueCategory)(i - 1));
            categoryFilter = EditorGUILayout.Popup(categoryFilter, categoryNames);

            GUILayout.Space(4f);
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
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
                label += "\n" + id + " · " + StatusLabel(status);
                GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    fixedHeight = 44f
                };
                if (GUILayout.Toggle(selectedDialogueIndex == i, label, buttonStyle))
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
            if (GUILayout.Button("Дубль"))
                DuplicateDialogue(serializedDatabase);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUI.enabled = dialogues.arraySize > 0;
            if (GUILayout.Button("Удалить выбранный"))
                DeleteDialogue(serializedDatabase);
            GUI.enabled = true;
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            dialogueMetaExpanded = EditorGUILayout.Foldout(dialogueMetaExpanded, "ДИАЛОГ", true);
            if (dialogueMetaExpanded)
            {
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("id"), new GUIContent("ID"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("title"), new GUIContent("Название"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("category"), new GUIContent("Категория"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("status"), new GUIContent("Статус"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("developerComment"), new GUIContent("Комментарий разработчика"));
                EditorGUILayout.PropertyField(dialogue.FindPropertyRelative("tags"), new GUIContent("Теги"), true);

                SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
                DrawStartNodePopup(dialogue.FindPropertyRelative("startNodeId"), nodes);
            }
            EditorGUILayout.EndVertical();

            SerializedProperty structureNodes = dialogue.FindPropertyRelative("nodes");
            bool deleteGraphNode = false;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("СТРУКТУРА", EditorStyles.boldLabel, GUILayout.Width(90f));
            if (GUILayout.Toggle(structureMode == DialogueStructureMode.Graph, "Граф", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                structureMode = DialogueStructureMode.Graph;
            if (GUILayout.Toggle(structureMode == DialogueStructureMode.Table, "Таблица", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                structureMode = DialogueStructureMode.Table;
            GUILayout.FlexibleSpace();

            bool canDeleteGraphNode = structureMode == DialogueStructureMode.Graph &&
                                      structureNodes.arraySize > 1 &&
                                      graphSelectedNodeIndex >= 0 &&
                                      graphSelectedNodeIndex < structureNodes.arraySize;
            GUI.enabled = canDeleteGraphNode;
            if (GUILayout.Button("Удалить реплику", EditorStyles.toolbarButton, GUILayout.Width(115f)))
                deleteGraphNode = true;
            GUI.enabled = true;

            SerializedProperty schemaVersionProperty = dialogue.FindPropertyRelative("schemaVersion");
            if (schemaVersionProperty != null && schemaVersionProperty.intValue < DialogueDefinitionData.CurrentSchemaVersion)
            {
                if (GUILayout.Button("Мигрировать на новую схему", EditorStyles.toolbarButton, GUILayout.Width(190f)))
                    MigrateDialogueSchema(dialogue);
            }
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

        private void DrawDialogueTable(SerializedProperty dialogue)
        {
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
                string.IsNullOrWhiteSpace(nodeId) ? "Узел без ID" : nodeId,
                true);
            GUILayout.FlexibleSpace();
            GUI.enabled = nodes.arraySize > 1;
            if (GUILayout.Button("×", GUILayout.Width(28f)))
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
                EditorGUILayout.PropertyField(node.FindPropertyRelative("id"), new GUIContent("Node ID"));
                DrawSpeakerPopup(node.FindPropertyRelative("speakerId"));
                EditorGUILayout.PropertyField(node.FindPropertyRelative("text"), new GUIContent("Реплика (legacy)"));

                GUILayout.Space(5f);
                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                EditorGUILayout.LabelField("Текстовые блоки", EditorStyles.boldLabel);
                for (int blockIndex = 0; blockIndex < textBlocks.arraySize; blockIndex++)
                    DrawTextBlock(textBlocks, blockIndex);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Основная реплика", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.MainLine);
                if (GUILayout.Button("+ Наблюдение", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.Observation);
                if (GUILayout.Button("+ Воспоминание", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.Memory);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Мысль героя", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.HeroThought);
                if (GUILayout.Button("+ Реплика спутника", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.CompanionLine);
                if (GUILayout.Button("+ Повествование", EditorStyles.miniButton))
                    AddTextBlock(textBlocks, DialogueTextBlockKind.Narration);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(8f);
                SerializedProperty choices = node.FindPropertyRelative("choices");
                EditorGUILayout.LabelField("Ответы игрока", EditorStyles.boldLabel);
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                    DrawChoice(nodes, choices, choiceIndex);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Обычный ответ", EditorStyles.miniButton))
                    AddChoice(choices);
                if (GUILayout.Button("+ Возвратная проверка", EditorStyles.miniButton))
                    AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveReturnable);
                if (GUILayout.Button("+ Решающая проверка", EditorStyles.miniButton))
                    AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveDecisive);
                if (GUILayout.Button("+ Завершить разговор", EditorStyles.miniButton))
                    AddExitChoice(choices);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTextBlock(SerializedProperty textBlocks, int blockIndex)
        {
            SerializedProperty block = textBlocks.GetArrayElementAtIndex(blockIndex);
            SerializedProperty kind = block.FindPropertyRelative("kind");
            SerializedProperty blockId = block.FindPropertyRelative("blockId");
            string label = "[" + (DialogueTextBlockKind)kind.enumValueIndex + "] " +
                            (string.IsNullOrWhiteSpace(blockId.stringValue) ? "<без ID>" : blockId.stringValue);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            block.isExpanded = EditorGUILayout.Foldout(block.isExpanded, label, true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                textBlocks.DeleteArrayElementAtIndex(blockIndex);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (block.isExpanded)
            {
                EditorGUILayout.PropertyField(blockId, new GUIContent("Block ID"));
                EditorGUILayout.PropertyField(kind, new GUIContent("Тип"));
                EditorGUILayout.PropertyField(block.FindPropertyRelative("speakerIdOverride"), new GUIContent("Говорящий (переопределение)"));
                EditorGUILayout.PropertyField(block.FindPropertyRelative("text"), new GUIContent("Текст"));
                EditorGUILayout.PropertyField(block.FindPropertyRelative("conditions"), new GUIContent("Условия показа"), true);

                SerializedProperty hasPassiveCheck = block.FindPropertyRelative("hasPassiveCheck");
                EditorGUILayout.PropertyField(hasPassiveCheck, new GUIContent("Есть пассивная проверка"));
                if (hasPassiveCheck.boolValue)
                    EditorGUILayout.PropertyField(block.FindPropertyRelative("passiveCheck"), new GUIContent("Пассивная проверка"), true);

                EditorGUILayout.PropertyField(block.FindPropertyRelative("onRevealEffects"), new GUIContent("Эффекты при показе"), true);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawChoice(SerializedProperty nodes, SerializedProperty choices, int choiceIndex)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
            SerializedProperty text = choice.FindPropertyRelative("text");
            SerializedProperty kind = choice.FindPropertyRelative("kind");
            SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");
            DialogueChoiceKind choiceKind = (DialogueChoiceKind)kind.enumValueIndex;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(text, GUIContent.none);
            if (GUILayout.Button("×", GUILayout.Width(28f)))
            {
                choices.DeleteArrayElementAtIndex(choiceIndex);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Вид: " + ChoiceKindLabel(choiceKind), EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(choice.FindPropertyRelative("conditions"), new GUIContent("Условия показа"), true);
            if (choiceKind != DialogueChoiceKind.Normal || choice.FindPropertyRelative("conditions").FindPropertyRelative("Conditions").arraySize > 0)
                EditorGUILayout.PropertyField(choice.FindPropertyRelative("unavailablePresentation"), new GUIContent("Если недоступен"));

            switch (choiceKind)
            {
                case DialogueChoiceKind.Exit:
                    EditorGUILayout.LabelField("Завершает разговор (EXIT).", EditorStyles.miniLabel);
                    break;

                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    EditorGUILayout.PropertyField(choice.FindPropertyRelative("check"), new GUIContent("Проверка"), true);
                    DrawNodeTargetPopup(choice.FindPropertyRelative("successNodeId"), nodes, "Узел при успехе");
                    DrawNodeTargetPopup(choice.FindPropertyRelative("failureNodeId"), nodes, "Узел при провале");
                    EditorGUILayout.PropertyField(choice.FindPropertyRelative("successEffects"), new GUIContent("Эффекты успеха"), true);
                    EditorGUILayout.PropertyField(choice.FindPropertyRelative("failureEffects"), new GUIContent("Эффекты провала"), true);
                    break;

                default:
                    endsDialogue.boolValue = EditorGUILayout.ToggleLeft("Завершает разговор (EXIT)", endsDialogue.boolValue);
                    if (endsDialogue.boolValue)
                        nextNodeId.stringValue = string.Empty;
                    else
                        DrawNodeTargetPopup(nextNodeId, nodes, "Переход");
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private static string ChoiceKindLabel(DialogueChoiceKind kind)
        {
            switch (kind)
            {
                case DialogueChoiceKind.Normal: return "обычный";
                case DialogueChoiceKind.ActiveReturnable: return "возвратная проверка";
                case DialogueChoiceKind.ActiveDecisive: return "решающая проверка";
                case DialogueChoiceKind.Exit: return "завершение";
                default: return kind.ToString();
            }
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
            int next = EditorGUILayout.Popup("Стартовый узел", selected, ids);
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
            int next = EditorGUILayout.Popup(label, selected, ids);
            if (next >= 0 && next < ids.Length)
                targetNodeId.stringValue = ids[next];
        }

        private void DrawSpeakerPopup(SerializedProperty speakerId)
        {
            IReadOnlyList<DialogueSpeakerData> speakers = database.Speakers;
            if (speakers.Count == 0)
            {
                EditorGUILayout.PropertyField(speakerId, new GUIContent("Говорящий"));
                EditorGUILayout.HelpBox("Сначала добавьте говорящего во вкладке «Говорящие».", MessageType.Warning);
                return;
            }

            string[] labels = new string[speakers.Count];
            int selected = 0;
            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                labels[i] = speaker.DisplayName + "  [" + speaker.Id + "]";
                if (string.Equals(speaker.Id, speakerId.stringValue, StringComparison.Ordinal))
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Говорящий", selected, labels);
            if (next >= 0 && next < speakers.Count)
                speakerId.stringValue = speakers[next].Id;
        }
    }
}
