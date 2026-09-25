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

        // Текущий SerializedObject кадра — для команд из выпадающих меню,
        // которые выполняются после отрисовки (ApplyEdit).
        private SerializedObject currentSerializedDatabase;

        private void DrawDialoguesTab()
        {
            SerializedObject serializedDatabase = new SerializedObject(database);
            serializedDatabase.Update();
            currentSerializedDatabase = serializedDatabase;

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

        // Узел в панели схемы (inInspector) или в режиме «Текстом» (со сворачиванием).
        // Карточки реплик и ответов показывают главное; подробные настройки —
        // по «⚙» у карточки или везде сразу при «⚙ Производство».
        private void DrawNode(SerializedProperty dialogue, SerializedProperty nodes, int nodeIndex, bool inInspector = false)
        {
            SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
            string nodeId = node.FindPropertyRelative("id").stringValue;

            EditorGUILayout.BeginVertical(inInspector ? GUIStyle.none : "box");
            if (!inInspector)
            {
                EditorGUILayout.BeginHorizontal();
                node.isExpanded = EditorGUILayout.Foldout(
                    node.isExpanded,
                    string.IsNullOrWhiteSpace(nodeId) ? "Узел без ID" : NodeLabel(nodeId),
                    true);
                GUILayout.FlexibleSpace();
                GUI.enabled = nodes.arraySize > 1;
                if (GUILayout.Button(new GUIContent("×", "Удалить узел"), EditorStyles.miniButton, GUILayout.Width(22f)))
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
            }

            if (inInspector || node.isExpanded)
            {
                DrawNodeHeaderRow(node, inInspector);

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

                DrawCompactSectionTitle("Реплики", textBlocks.arraySize, SemanticCategory.Text,
                    () => ShowAddTextBlockMenu(textBlocks), "＋ реплика ▾");
                string nodeSpeakerId = node.FindPropertyRelative("speakerId").stringValue;
                for (int blockIndex = 0; blockIndex < textBlocks.arraySize; blockIndex++)
                {
                    if (DrawTextBlock(textBlocks, blockIndex, nodeSpeakerId))
                        break;
                }

                SerializedProperty choices = node.FindPropertyRelative("choices");
                DrawCompactSectionTitle("Ответы игрока", choices.arraySize, SemanticCategory.Choice,
                    () => ShowAddChoiceMenu(choices), "＋ ответ ▾");
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    if (DrawChoice(nodes, choices, choiceIndex))
                        break;
                }
            }

            EditorGUILayout.EndVertical();
        }

        // Одна строка шапки: портрет, кто говорит, подпись узла (и ID при «⚙ Производство»).
        private void DrawNodeHeaderRow(SerializedProperty node, bool withPortrait)
        {
            SerializedProperty speakerId = node.FindPropertyRelative("speakerId");
            DialogueSpeakerData speaker = database.FindSpeaker(speakerId.stringValue);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (withPortrait)
            {
                const float portraitSize = 30f;
                Rect portraitRect = GUILayoutUtility.GetRect(portraitSize, portraitSize,
                    GUILayout.Width(portraitSize), GUILayout.Height(portraitSize));
                if (speaker != null && speaker.Portrait != null)
                {
                    Texture2D preview = AssetPreview.GetAssetPreview(speaker.Portrait);
                    if (preview == null)
                        preview = speaker.Portrait.texture;
                    GUI.DrawTexture(portraitRect, preview, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    EditorGUI.DrawRect(portraitRect,
                        EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f));
                    GUI.Label(portraitRect, speaker != null && speaker.DisplayName.Length > 0 ? speaker.DisplayName.Substring(0, 1) : "?",
                        new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
                }
                GUILayout.Space(4f);
            }

            EditorGUILayout.BeginVertical();
            GUILayout.Space(withPortrait ? 5f : 0f);
            EditorGUILayout.BeginHorizontal();
            DrawSpeakerPopup(speakerId, false);
            string nodeId = node.FindPropertyRelative("id").stringValue;
            if (showProduction)
            {
                SerializedProperty id = node.FindPropertyRelative("id");
                id.stringValue = EditorGUILayout.TextField(new GUIContent(string.Empty, "ID узла"), id.stringValue, GUILayout.Width(120f));
            }
            else if (withPortrait)
            {
                GUILayout.Label(NodeLabel(nodeId), EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(false));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawCompactSectionTitle(string title, int count, SemanticCategory category, Action onAdd, string addLabel)
        {
            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = GetSemanticAccentColor(category);
            GUILayout.Label(title + (count > 0 ? " · " + count : string.Empty), style);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(addLabel, EditorStyles.miniButton, GUILayout.Width(86f)))
                onAdd();
            EditorGUILayout.EndHorizontal();
        }

        private void ShowAddTextBlockMenu(SerializedProperty textBlocks)
        {
            GenericMenu menu = new GenericMenu();
            foreach (DialogueTextBlockKind kind in new[]
                     {
                         DialogueTextBlockKind.MainLine, DialogueTextBlockKind.Narration, DialogueTextBlockKind.HeroThought,
                         DialogueTextBlockKind.CompanionLine, DialogueTextBlockKind.Observation, DialogueTextBlockKind.Memory
                     })
            {
                DialogueTextBlockKind captured = kind;
                menu.AddItem(new GUIContent(TextBlockKindLabel(kind)), false, () => ApplyEdit(() => AddTextBlock(textBlocks, captured)));
            }
            menu.ShowAsContext();
        }

        private void ShowAddChoiceMenu(SerializedProperty choices)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Обычный ответ"), false, () => ApplyEdit(() => AddChoice(choices)));
            menu.AddItem(new GUIContent("«…» — читать дальше"), false, () => ApplyEdit(() => AddContinueChoice(choices)));
            menu.AddItem(new GUIContent("Проверка (можно повторить)"), false,
                () => ApplyEdit(() => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveReturnable)));
            menu.AddItem(new GUIContent("Проверка (решающая, один раз)"), false,
                () => ApplyEdit(() => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveDecisive)));
            menu.AddItem(new GUIContent("Выход из разговора"), false, () => ApplyEdit(() => AddExitChoice(choices)));
            menu.ShowAsContext();
        }

        // Команды из меню выполняются после кадра — изменения нужно применить явно.
        private void ApplyEdit(Action edit)
        {
            Undo.RecordObject(database, "Dialogue Edit");
            edit();
            if (currentSerializedDatabase != null)
                currentSerializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            Repaint();
        }

        // ------------------------------------------------------------------
        // Реплика
        // ------------------------------------------------------------------

        // Возвращает true, если реплика удалена (индексы сдвинулись).
        private bool DrawTextBlock(SerializedProperty textBlocks, int blockIndex, string nodeSpeakerId)
        {
            SerializedProperty block = textBlocks.GetArrayElementAtIndex(blockIndex);
            SerializedProperty kind = block.FindPropertyRelative("kind");
            bool expanded = IsCardExpanded(block);

            BeginSemanticCard(null, SemanticCategory.Text);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((blockIndex + 1) + ".", EditorStyles.miniBoldLabel, GUILayout.Width(18f));
            DrawTextBlockKindPopup(kind, GUILayout.MinWidth(90f));
            DrawSpeakerOverridePopup(block.FindPropertyRelative("speakerIdOverride"), nodeSpeakerId);
            DrawCardGear(block);
            if (GUILayout.Button(new GUIContent("×", "Удалить реплику"), EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                textBlocks.DeleteArrayElementAtIndex(blockIndex);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Text);
                return true;
            }
            EditorGUILayout.EndHorizontal();

            DrawAutoHeightNarrativeText(block.FindPropertyRelative("text"), null);

            SerializedProperty conditionGroup = block.FindPropertyRelative("conditions");
            SerializedProperty conditions = conditionGroup.FindPropertyRelative("Conditions");
            SerializedProperty hasPassiveCheck = block.FindPropertyRelative("hasPassiveCheck");
            SerializedProperty passiveCheck = block.FindPropertyRelative("passiveCheck");
            SerializedProperty effects = block.FindPropertyRelative("onRevealEffects");

            if (expanded && showProduction)
                DrawLongIdField(block.FindPropertyRelative("blockId"), "ID реплики");

            if (conditions.arraySize > 0)
            {
                if (expanded)
                    DrawConditionsEditor(conditionGroup, "Показывается, если");
                else
                    DrawSummaryLine("Показывается, если: " + DescribeConditions(conditionGroup));
            }

            if (hasPassiveCheck.boolValue)
            {
                if (DrawInlineCheck(passiveCheck, "◈ видна при проверке", null, true))
                    hasPassiveCheck.boolValue = false;
                else if (expanded)
                    DrawCheckExtras(passiveCheck);
            }

            if (effects.arraySize > 0)
            {
                if (expanded)
                    DrawEffectsEditor(effects, "После показа", SemanticCategory.Effect);
                else
                    DrawSummaryLine("↳ после показа: " + DescribeEffects(effects));
            }

            DrawAddRow(block,
                ("+ условие", "Показывать реплику только при условии", () => AddCondition(conditions)),
                hasPassiveCheck.boolValue ? default : ("+ проверка", "Реплика видна только при успехе пассивной проверки",
                    (Action)(() => EnablePassiveCheck(hasPassiveCheck, passiveCheck))),
                ("+ последствие", "Что меняется в мире после этой реплики", () => AddEffect(effects)));

            EndSemanticCard(SemanticCategory.Text);
            return false;
        }

        private static void EnablePassiveCheck(SerializedProperty hasPassiveCheck, SerializedProperty passiveCheck)
        {
            hasPassiveCheck.boolValue = true;
            passiveCheck.FindPropertyRelative("Kind").enumValueIndex = (int)NarrativeCheckKind.Passive;
            if (string.IsNullOrWhiteSpace(passiveCheck.FindPropertyRelative("CheckId").stringValue))
                passiveCheck.FindPropertyRelative("CheckId").stringValue = Guid.NewGuid().ToString("N");
            if (passiveCheck.FindPropertyRelative("Difficulty").intValue <= 0)
                passiveCheck.FindPropertyRelative("Difficulty").intValue = NarrativeDifficulty.Ordinary;
        }

        // ------------------------------------------------------------------
        // Ответ
        // ------------------------------------------------------------------

        private static readonly DialogueChoiceKind[] ChoiceKindOrder =
        {
            DialogueChoiceKind.Normal, DialogueChoiceKind.Continue, DialogueChoiceKind.ActiveReturnable,
            DialogueChoiceKind.ActiveDecisive, DialogueChoiceKind.Exit
        };

        // Возвращает true, если ответ удалён.
        private bool DrawChoice(SerializedProperty nodes, SerializedProperty choices, int choiceIndex)
        {
            SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
            SerializedProperty kind = choice.FindPropertyRelative("kind");
            SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");
            SerializedProperty check = choice.FindPropertyRelative("check");
            DialogueChoiceKind choiceKind = (DialogueChoiceKind)kind.enumValueIndex;
            bool expanded = IsCardExpanded(choice);

            BeginSemanticCard(null, SemanticCategory.Choice);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((choiceIndex + 1) + ".", EditorStyles.miniBoldLabel, GUILayout.Width(18f));
            DialogueChoiceKind nextKind = DrawChoiceKindPopup(choiceKind);
            if (nextKind != choiceKind)
            {
                ChangeChoiceKind(choice, nextKind);
                choiceKind = nextKind;
            }
            GUILayout.FlexibleSpace();
            DrawCardGear(choice);
            if (GUILayout.Button(new GUIContent("×", "Удалить ответ"), EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                choices.DeleteArrayElementAtIndex(choiceIndex);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Choice);
                return true;
            }
            EditorGUILayout.EndHorizontal();

            if (choiceKind == DialogueChoiceKind.Continue)
                DrawSummaryLine("В игре — кнопка «…»: читать дальше, не слова героя.");
            else
                DrawAutoHeightNarrativeText(choice.FindPropertyRelative("text"), null, 20f);

            if (expanded && showProduction)
                DrawLongIdField(choice.FindPropertyRelative("choiceId"), "ID ответа");

            SerializedProperty conditionGroup = choice.FindPropertyRelative("conditions");
            SerializedProperty conditions = conditionGroup.FindPropertyRelative("Conditions");
            if (conditions.arraySize > 0)
            {
                if (expanded)
                {
                    DrawConditionsEditor(conditionGroup, "Доступен, если");
                    DrawUnavailablePresentationPopup(choice.FindPropertyRelative("unavailablePresentation"));
                }
                else
                {
                    DrawSummaryLine("Доступен, если: " + DescribeConditions(conditionGroup) +
                                    (choice.FindPropertyRelative("unavailablePresentation").enumValueIndex ==
                                     (int)DialogueChoiceUnavailablePresentation.Hidden ? " (иначе скрыт)" : " (иначе неактивен)"));
                }
            }

            SerializedProperty successEffects = choice.FindPropertyRelative("successEffects");
            SerializedProperty failureEffects = choice.FindPropertyRelative("failureEffects");
            bool isCheck = choiceKind == DialogueChoiceKind.ActiveReturnable || choiceKind == DialogueChoiceKind.ActiveDecisive;

            switch (choiceKind)
            {
                case DialogueChoiceKind.Exit:
                    DrawSummaryLine("→ конец разговора");
                    break;

                case DialogueChoiceKind.Continue:
                    DrawTargetRow("→", nextNodeId, nodes, false, null);
                    break;

                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    DrawInlineCheck(check, "◆", null, false);
                    if (expanded)
                        DrawCheckExtras(check);
                    EditorGUILayout.BeginHorizontal();
                    DrawTargetRow("✓ успех", choice.FindPropertyRelative("successNodeId"), nodes, false, null, true);
                    DrawTargetRow("✕ провал", choice.FindPropertyRelative("failureNodeId"), nodes, false, null, true);
                    EditorGUILayout.EndHorizontal();
                    break;

                default:
                    DrawTargetRow("→", nextNodeId, nodes, true, endsDialogue);
                    break;
            }

            if (successEffects.arraySize > 0)
            {
                string title = isCheck ? "После успеха" : "После выбора";
                if (expanded)
                    DrawEffectsEditor(successEffects, title, SemanticCategory.Effect);
                else
                    DrawSummaryLine("↳ " + title.ToLowerInvariant() + ": " + DescribeEffects(successEffects));
            }
            if (failureEffects.arraySize > 0)
            {
                if (expanded)
                    DrawEffectsEditor(failureEffects, "После провала", SemanticCategory.Error);
                else
                    DrawSummaryLine("↳ после провала: " + DescribeEffects(failureEffects));
            }

            if (isCheck)
            {
                DrawAddRow(choice,
                    ("+ условие", "Ответ доступен только при условии", () => AddCondition(conditions)),
                    ("+ после успеха", "Что меняется при успехе", () => AddEffect(successEffects)),
                    ("+ после провала", "Что меняется при провале", () => AddEffect(failureEffects)));
            }
            else if (choiceKind != DialogueChoiceKind.Continue)
            {
                DrawAddRow(choice,
                    ("+ условие", "Ответ доступен только при условии", () => AddCondition(conditions)),
                    default,
                    ("+ последствие", "Что меняется после этого ответа", () => AddEffect(successEffects)));
            }

            EndSemanticCard(SemanticCategory.Choice);
            return false;
        }

        private static DialogueChoiceKind DrawChoiceKindPopup(DialogueChoiceKind current)
        {
            string[] labels = new string[ChoiceKindOrder.Length];
            int selected = 0;
            for (int i = 0; i < ChoiceKindOrder.Length; i++)
            {
                labels[i] = ChoiceKindLabel(ChoiceKindOrder[i]);
                if (ChoiceKindOrder[i] == current)
                    selected = i;
            }
            int next = EditorGUILayout.Popup(selected, labels, EditorStyles.miniPullDown, GUILayout.Width(170f));
            return ChoiceKindOrder[Mathf.Clamp(next, 0, ChoiceKindOrder.Length - 1)];
        }

        private static void ChangeChoiceKind(SerializedProperty choice, DialogueChoiceKind kind)
        {
            choice.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");
            switch (kind)
            {
                case DialogueChoiceKind.Exit:
                    endsDialogue.boolValue = true;
                    choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
                    break;
                case DialogueChoiceKind.Continue:
                    endsDialogue.boolValue = false;
                    break;
                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    endsDialogue.boolValue = false;
                    SerializedProperty check = choice.FindPropertyRelative("check");
                    check.FindPropertyRelative("Kind").enumValueIndex = (int)kind; // номера совпадают с NarrativeCheckKind
                    if (string.IsNullOrWhiteSpace(check.FindPropertyRelative("CheckId").stringValue))
                        check.FindPropertyRelative("CheckId").stringValue = Guid.NewGuid().ToString("N");
                    if (check.FindPropertyRelative("Difficulty").intValue <= 0)
                        check.FindPropertyRelative("Difficulty").intValue = NarrativeDifficulty.Ordinary;
                    if (string.IsNullOrWhiteSpace(choice.FindPropertyRelative("choiceId").stringValue))
                        choice.FindPropertyRelative("choiceId").stringValue = Guid.NewGuid().ToString("N");
                    break;
            }
        }

        // «→ [Узел 2 · «…» ▾]». У обычного ответа первый пункт — конец разговора.
        private void DrawTargetRow(string prefix, SerializedProperty target, SerializedProperty nodes,
            bool allowEnd, SerializedProperty endsDialogue, bool inline = false)
        {
            string[] ids = GetNodeIds(nodes);
            string[] nodeLabels = BuildNodeLabels(ids, nodes);
            int offset = allowEnd ? 1 : 0;
            string[] labels = new string[ids.Length + offset];
            if (allowEnd)
                labels[0] = "■ конец разговора";
            Array.Copy(nodeLabels, 0, labels, offset, nodeLabels.Length);

            int selected;
            if (allowEnd && endsDialogue != null && endsDialogue.boolValue)
                selected = 0;
            else
            {
                selected = Array.IndexOf(ids, target.stringValue);
                selected = selected < 0 ? (allowEnd ? 0 : -1) : selected + offset;
            }

            if (!inline)
                EditorGUILayout.BeginHorizontal();
            GUILayout.Label(prefix, EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(false));
            if (selected < 0)
            {
                labels = new[] { "<не выбран>" };
                Array.Resize(ref labels, ids.Length + 1);
                Array.Copy(nodeLabels, 0, labels, 1, nodeLabels.Length);
                int picked = EditorGUILayout.Popup(0, labels, EditorStyles.miniPullDown);
                if (picked > 0)
                    target.stringValue = ids[picked - 1];
            }
            else
            {
                int next = EditorGUILayout.Popup(selected, labels, EditorStyles.miniPullDown);
                if (next != selected)
                {
                    if (allowEnd && next == 0)
                    {
                        if (endsDialogue != null)
                            endsDialogue.boolValue = true;
                        target.stringValue = string.Empty;
                    }
                    else
                    {
                        if (endsDialogue != null)
                            endsDialogue.boolValue = false;
                        target.stringValue = ids[next - offset];
                    }
                }
            }
            if (!inline)
                EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------
        // Проверка одной строкой: [вид] [качество] [+ компетенция] сложность [13]
        // ------------------------------------------------------------------

        private static readonly HeroQuality[] QualityValues = (HeroQuality[])Enum.GetValues(typeof(HeroQuality));

        // Возвращает true, если нажато «убрать проверку» (только для пассивной).
        private bool DrawInlineCheck(SerializedProperty check, string prefix, string unused, bool removable)
        {
            bool remove = false;
            EditorGUILayout.BeginHorizontal();
            GUIStyle prefixStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            prefixStyle.normal.textColor = GetSemanticAccentColor(SemanticCategory.Check);
            GUILayout.Label(prefix, prefixStyle, GUILayout.ExpandWidth(false));

            SerializedProperty quality = check.FindPropertyRelative("Quality");
            string[] qualityLabels = new string[QualityValues.Length];
            int qualitySelected = 0;
            for (int i = 0; i < QualityValues.Length; i++)
            {
                qualityLabels[i] = NarrativeQualityLabels.GetLabel(QualityValues[i]);
                if (quality.enumValueIndex == (int)QualityValues[i])
                    qualitySelected = i;
            }
            int nextQuality = EditorGUILayout.Popup(qualitySelected, qualityLabels, EditorStyles.miniPullDown, GUILayout.MinWidth(70f));
            quality.enumValueIndex = (int)QualityValues[Mathf.Clamp(nextQuality, 0, QualityValues.Length - 1)];

            SerializedProperty competency = check.FindPropertyRelative("CompetencyId");
            IReadOnlyList<string> known = NarrativeCompetencyIds.Known;
            string[] competencyLabels = new string[known.Count + 1];
            competencyLabels[0] = "без навыка";
            int competencySelected = 0;
            for (int i = 0; i < known.Count; i++)
            {
                competencyLabels[i + 1] = "+ " + NarrativeCompetencyLabels.GetLabel(known[i]);
                if (string.Equals(known[i], competency.stringValue, StringComparison.Ordinal))
                    competencySelected = i + 1;
            }
            int nextCompetency = EditorGUILayout.Popup(competencySelected, competencyLabels, EditorStyles.miniPullDown, GUILayout.MinWidth(80f));
            if (nextCompetency != competencySelected)
                competency.stringValue = nextCompetency == 0 ? string.Empty : known[nextCompetency - 1];

            SerializedProperty difficulty = check.FindPropertyRelative("Difficulty");
            GUILayout.Label(new GUIContent("слож.", "Сложность проверки (9–23)"), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            difficulty.intValue = Mathf.Clamp(EditorGUILayout.IntField(difficulty.intValue, GUILayout.Width(28f)), 1, 40);

            if (removable && GUILayout.Button(new GUIContent("×", "Убрать проверку"), EditorStyles.miniButton, GUILayout.Width(22f)))
                remove = true;
            EditorGUILayout.EndHorizontal();

            DrawSummaryLine(NarrativeDifficultyLabels.Describe(difficulty.intValue));
            return remove;
        }

        // Подробности проверки (при «⚙»): ID и контекстные модификаторы.
        private void DrawCheckExtras(SerializedProperty check)
        {
            EditorGUI.indentLevel++;
            if (showProduction)
                DrawLongIdField(check.FindPropertyRelative("CheckId"), "ID проверки");
            SerializedProperty modifierRules = check.FindPropertyRelative("ModifierRules");
            for (int i = 0; i < modifierRules.arraySize; i++)
            {
                if (DrawModifierRule(modifierRules, i))
                    break;
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("+ модификатор", "Бонус или штраф к проверке при условии"), EditorStyles.miniButton, GUILayout.Width(110f)))
                AddModifierRule(modifierRules);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        // ------------------------------------------------------------------
        // Условия и последствия: строка-сводка или редактор
        // ------------------------------------------------------------------

        private void DrawConditionsEditor(SerializedProperty group, string title)
        {
            DrawSummaryLine(title + ":", GetSemanticAccentColor(SemanticCategory.Condition));
            EditorGUI.indentLevel++;
            SerializedProperty conditions = group.FindPropertyRelative("Conditions");
            if (conditions.arraySize > 1)
                DrawConditionCombinatorPopup(group.FindPropertyRelative("Combinator"));
            for (int i = 0; i < conditions.arraySize; i++)
            {
                if (DrawCondition(conditions, i))
                    break;
            }
            EditorGUI.indentLevel--;
        }

        private void DrawEffectsEditor(SerializedProperty effects, string title, SemanticCategory category)
        {
            DrawSummaryLine(title + ":", GetSemanticAccentColor(category));
            EditorGUI.indentLevel++;
            for (int i = 0; i < effects.arraySize; i++)
            {
                if (DrawEffect(effects, i, category))
                    break;
            }
            EditorGUI.indentLevel--;
        }

        private static string DescribeConditions(SerializedProperty group)
        {
            NarrativeConditionGroup value = (NarrativeConditionGroup)group.boxedValue;
            if (value?.Conditions == null || value.Conditions.Count == 0)
                return "—";
            List<string> parts = new List<string>();
            foreach (NarrativeCondition condition in value.Conditions)
            {
                if (condition != null)
                    parts.Add(DescribeCondition(condition));
            }
            string joiner = value.Combinator == NarrativeConditionCombinator.Any ? " или " : " и ";
            return string.Join(joiner, parts);
        }

        private static string DescribeCondition(NarrativeCondition condition)
        {
            string not = condition.Negate ? "НЕ " : string.Empty;
            switch (condition.Type)
            {
                case NarrativeConditionType.QualityAtLeast:
                    return not + NarrativeQualityLabels.GetLabel(condition.QualityParam) + " ≥ " + condition.IntParam;
                case NarrativeConditionType.CompetencyAtLeast:
                    return not + NarrativeCompetencyLabels.GetLabel(condition.StringParam) + " ≥ " + condition.IntParam;
                case NarrativeConditionType.RelationAtLeast:
                    return not + "отношение «" + condition.StringParam + "» ≥ " + condition.IntParam;
                case NarrativeConditionType.RelationAtMost:
                    return not + "отношение «" + condition.StringParam + "» ≤ " + condition.IntParam;
                case NarrativeConditionType.PartySizeAtLeast:
                    return not + "в отряде ≥ " + condition.IntParam;
                case NarrativeConditionType.PartySizeAtMost:
                    return not + "в отряде ≤ " + condition.IntParam;
                case NarrativeConditionType.CompanionPresent:
                    return not + "рядом «" + condition.StringParam + "»";
                default:
                    return not + ConditionTypeLabel(condition.Type).ToLowerInvariant() + " «" + condition.StringParam + "»";
            }
        }

        private static string DescribeEffects(SerializedProperty effects)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < effects.arraySize; i++)
            {
                NarrativeEffect effect = (NarrativeEffect)effects.GetArrayElementAtIndex(i).boxedValue;
                if (effect == null)
                    continue;
                string text = EffectTypeLabel(effect.Type).ToLowerInvariant();
                switch (effect.Type)
                {
                    case NarrativeEffectType.ChangeFood:
                    case NarrativeEffectType.ChangeSupplies:
                    case NarrativeEffectType.ChangeRelation:
                        text += (string.IsNullOrEmpty(effect.StringParam) ? string.Empty : " «" + effect.StringParam + "»") +
                                " " + (effect.IntParam >= 0 ? "+" : string.Empty) + effect.IntParam;
                        break;
                    case NarrativeEffectType.ShortcutRouteCells:
                        text += " " + effect.IntParam;
                        break;
                    default:
                        text += " «" + effect.StringParam + "»";
                        break;
                }
                parts.Add(text);
            }
            return string.Join("; ", parts);
        }

        // ------------------------------------------------------------------
        // Мелкие элементы карточки
        // ------------------------------------------------------------------

        private readonly HashSet<string> expandedCards = new HashSet<string>(StringComparer.Ordinal);

        private bool IsCardExpanded(SerializedProperty card)
        {
            return showProduction || expandedCards.Contains(card.propertyPath);
        }

        private void DrawCardGear(SerializedProperty card)
        {
            if (showProduction)
                return;
            bool open = expandedCards.Contains(card.propertyPath);
            bool next = GUILayout.Toggle(open, new GUIContent("⚙", open ? "Скрыть подробные настройки" : "Подробные настройки: условия, последствия, модификаторы"),
                EditorStyles.miniButton, GUILayout.Width(24f));
            if (next == open)
                return;
            if (next)
                expandedCards.Add(card.propertyPath);
            else
                expandedCards.Remove(card.propertyPath);
            GUI.FocusControl(null);
        }

        // «+ условие · + проверка · + последствие» — пустые секции не рисуются.
        private void DrawAddRow(SerializedProperty card,
            (string label, string tooltip, Action add) first,
            (string label, string tooltip, Action add) second,
            (string label, string tooltip, Action add) third)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            foreach ((string label, string tooltip, Action add) item in new[] { first, second, third })
            {
                if (item.add == null)
                    continue;
                if (GUILayout.Button(new GUIContent(item.label, item.tooltip), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    item.add();
                    expandedCards.Add(card.propertyPath);
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSummaryLine(string text, Color? color = null)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            style.normal.textColor = color ?? (EditorGUIUtility.isProSkin
                ? new Color(0.66f, 0.66f, 0.63f, 1f)
                : new Color(0.38f, 0.38f, 0.36f, 1f));
            EditorGUILayout.LabelField(text, style);
        }

        // Говорящий отдельной реплики: по умолчанию — говорящий узла.
        private void DrawSpeakerOverridePopup(SerializedProperty speakerIdOverride, string nodeSpeakerId)
        {
            IReadOnlyList<DialogueSpeakerData> speakers = database.Speakers;
            DialogueSpeakerData nodeSpeaker = database.FindSpeaker(nodeSpeakerId);
            string[] labels = new string[speakers.Count + 1];
            labels[0] = nodeSpeaker != null ? nodeSpeaker.DisplayName : "как в узле";
            int selected = 0;
            for (int i = 0; i < speakers.Count; i++)
            {
                labels[i + 1] = speakers[i].DisplayName + (showProduction ? "  [" + speakers[i].Id + "]" : string.Empty);
                if (!string.IsNullOrEmpty(speakerIdOverride.stringValue) &&
                    string.Equals(speakers[i].Id, speakerIdOverride.stringValue, StringComparison.Ordinal))
                    selected = i + 1;
            }

            int next = EditorGUILayout.Popup(selected, labels, GUILayout.MinWidth(90f));
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

            if (showProduction)
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
            added.FindPropertyRelative("EffectExecutionId").stringValue = Guid.NewGuid().ToString("N");
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
            DrawSpeakerPopup(speakerId, true);
        }

        private void DrawSpeakerPopup(SerializedProperty speakerId, bool withLabel)
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

            int next = withLabel
                ? EditorGUILayout.Popup("Говорит", selected, labels)
                : EditorGUILayout.Popup(new GUIContent(string.Empty, "Кто говорит в этом узле"), selected,
                    Array.ConvertAll(labels, label => new GUIContent(label)));
            if (next >= 0 && next < speakers.Count)
                speakerId.stringValue = speakers[next].Id;
        }
    }
}
