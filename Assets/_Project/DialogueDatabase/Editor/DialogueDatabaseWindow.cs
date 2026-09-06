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
        private NarrativeDialogueSession previewSession;
        private string previewDialogueId = string.Empty;
        private string previewMessage = string.Empty;
        private readonly List<string> validationIssues = new List<string>();

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
                EditorGUILayout.PropertyField(node.FindPropertyRelative("text"), new GUIContent("Реплика"));

                GUILayout.Space(5f);
                SerializedProperty choices = node.FindPropertyRelative("choices");
                EditorGUILayout.LabelField("Ответы игрока", EditorStyles.boldLabel);
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                    DrawChoice(nodes, choices, choiceIndex);

                if (GUILayout.Button("+ Ответ"))
                    AddChoice(choices);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawChoice(SerializedProperty nodes, SerializedProperty choices, int choiceIndex)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
            SerializedProperty text = choice.FindPropertyRelative("text");
            SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");

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

            endsDialogue.boolValue = EditorGUILayout.ToggleLeft("Завершает разговор (EXIT)", endsDialogue.boolValue);
            if (endsDialogue.boolValue)
            {
                nextNodeId.stringValue = string.Empty;
            }
            else
            {
                DrawNodeTargetPopup(nextNodeId, nodes);
            }
            EditorGUILayout.EndVertical();
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

        private void DrawNodeTargetPopup(SerializedProperty nextNodeId, SerializedProperty nodes)
        {
            string[] ids = GetNodeIds(nodes);
            if (ids.Length == 0)
            {
                EditorGUILayout.PropertyField(nextNodeId, new GUIContent("Переход"));
                return;
            }

            int selected = Array.IndexOf(ids, nextNodeId.stringValue);
            if (selected < 0)
                selected = 0;
            int next = EditorGUILayout.Popup("Переход", selected, ids);
            if (next >= 0 && next < ids.Length)
                nextNodeId.stringValue = ids[next];
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
