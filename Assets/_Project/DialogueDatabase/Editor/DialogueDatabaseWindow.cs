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
        private NarrativeCheckPresentationData previewLastCheckPresentation;
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

        // §16 инструкции "новое отображение пассивных наблюдений и проверок":
        // авторский просмотр упущенного текста провалившихся пассивных
        // проверок. По умолчанию OFF — Preview без этого переключателя
        // показывает ровно то же, что видит игрок (§15). Доступно только в
        // Preview редактора, никогда в игровом runtime.
        private bool previewRevealHiddenTextForAuthor;

        [MenuItem("Kingdom Survival/База диалогов")]
        private static void Open()
        {
            GetWindow<DialogueDatabaseWindow>("База диалогов");
        }

        private void OnEnable()
        {
            if (database == null)
                database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);

            LoadGraphDetailMode();
            LoadGraphInspectorWidth();
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
                DrawSectionHeader("ОСНОВНОЕ");
                DrawLongIdField(node.FindPropertyRelative("id"), "ID узла");
                DrawSpeakerPopup(node.FindPropertyRelative("speakerId"));

                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                SerializedProperty legacyText = node.FindPropertyRelative("text");

                // §18 инструкции "полноценное редактирование нод": legacy
                // node.Text редактируется в основной части Inspector'а
                // только для действительно legacy-узла (без textBlocks).
                // Если textBlocks уже есть, legacy-поле игроку не
                // показывается вообще — не нужно занимать им место; если
                // оно всё же непусто, прячем его в сворачиваемую секцию
                // с явным предупреждением, а не молча.
                if (textBlocks.arraySize == 0)
                {
                    DrawAutoHeightNarrativeText(legacyText, "Реплика (legacy)");
                }
                else if (!string.IsNullOrEmpty(legacyText.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "⚠ Узел содержит legacy-текст, но использует textBlocks — это поле игроку не показывается.",
                        MessageType.Warning);
                    legacyTextFoldout = EditorGUILayout.Foldout(legacyTextFoldout, "УСТАРЕВШИЕ ДАННЫЕ", true);
                    if (legacyTextFoldout)
                        DrawAutoHeightNarrativeText(legacyText, "Реплика (legacy)");
                }

                DrawSectionHeader("ТЕКСТОВЫЕ БЛОКИ (" + textBlocks.arraySize + ")", SemanticCategory.Text);
                for (int blockIndex = 0; blockIndex < textBlocks.arraySize; blockIndex++)
                    DrawTextBlock(textBlocks, blockIndex);

                // §29: ряд из 6 кнопок неизбежно требует ширины — сжимаем в
                // одно компактное dropdown-меню.
                if (GUILayout.Button("＋ Добавить текстовый блок ▾", EditorStyles.miniButton))
                    ShowAddTextBlockMenu(textBlocks);

                SerializedProperty choices = node.FindPropertyRelative("choices");
                DrawSectionHeader("ОТВЕТЫ (" + choices.arraySize + ")", SemanticCategory.Choice);
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                    DrawChoice(nodes, choices, choiceIndex);

                if (GUILayout.Button("＋ Добавить ответ ▾", EditorStyles.miniButton))
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
            menu.AddItem(new GUIContent("Обычный"), false, () => AddChoice(choices));
            menu.AddItem(new GUIContent("Возвратная проверка"), false,
                () => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveReturnable));
            menu.AddItem(new GUIContent("Решающая проверка"), false,
                () => AddActiveCheckChoice(choices, DialogueChoiceKind.ActiveDecisive));
            menu.AddItem(new GUIContent("Выход"), false, () => AddExitChoice(choices));
            menu.ShowAsContext();
        }

        private void DrawTextBlock(SerializedProperty textBlocks, int blockIndex)
        {
            SerializedProperty block = textBlocks.GetArrayElementAtIndex(blockIndex);
            SerializedProperty kind = block.FindPropertyRelative("kind");
            SerializedProperty blockId = block.FindPropertyRelative("blockId");
            string label = "[" + TextBlockKindLabel((DialogueTextBlockKind)kind.enumValueIndex) + "] " +
                            (string.IsNullOrWhiteSpace(blockId.stringValue) ? "<без ID>" : blockId.stringValue);

            BeginSemanticCard(null, SemanticCategory.Text);
            EditorGUILayout.BeginHorizontal();
            block.isExpanded = EditorGUILayout.Foldout(block.isExpanded, label, true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f)))
            {
                textBlocks.DeleteArrayElementAtIndex(blockIndex);
                EditorGUILayout.EndHorizontal();
                EndSemanticCard(SemanticCategory.Text);
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (block.isExpanded)
            {
                DrawLongIdField(blockId, "ID блока");
                DrawTextBlockKindPopup(kind);
                DrawLongIdField(block.FindPropertyRelative("speakerIdOverride"), "Говорящий (переопределение)");
                DrawAutoHeightNarrativeText(block.FindPropertyRelative("text"), "Текст");
                DrawConditionGroup(block.FindPropertyRelative("conditions"), "Условия показа");

                SerializedProperty hasPassiveCheck = block.FindPropertyRelative("hasPassiveCheck");
                EditorGUILayout.PropertyField(hasPassiveCheck, new GUIContent("Есть пассивная проверка"));
                if (hasPassiveCheck.boolValue)
                    DrawCheckSpec(block.FindPropertyRelative("passiveCheck"), "Пассивная проверка");

                DrawEffectsList(block.FindPropertyRelative("onRevealEffects"), "Эффекты после показа");
            }
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

            DrawAutoHeightNarrativeText(text, null, 32f);
            DrawConditionGroup(choice.FindPropertyRelative("conditions"), "Условия показа");
            if (choiceKind != DialogueChoiceKind.Normal || choice.FindPropertyRelative("conditions").FindPropertyRelative("Conditions").arraySize > 0)
                DrawUnavailablePresentationPopup(choice.FindPropertyRelative("unavailablePresentation"));

            switch (choiceKind)
            {
                case DialogueChoiceKind.Exit:
                    EditorGUILayout.LabelField("Завершает разговор (ВЫХОД).", EditorStyles.miniLabel);
                    break;

                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    DrawCheckSpec(choice.FindPropertyRelative("check"), "Проверка");
                    DrawNodeTargetPopup(choice.FindPropertyRelative("successNodeId"), nodes, "Узел при успехе");
                    DrawNodeTargetPopup(choice.FindPropertyRelative("failureNodeId"), nodes, "Узел при провале");
                    // §25: "зелёный/красный успех-провал" — те же категории,
                    // что и у обычных эффектов (Effect) и ошибок (Error), а не
                    // отдельная восьмая категория ради двух вызовов.
                    DrawEffectsList(choice.FindPropertyRelative("successEffects"), "Эффекты успеха", SemanticCategory.Effect);
                    DrawEffectsList(choice.FindPropertyRelative("failureEffects"), "Эффекты провала", SemanticCategory.Error);
                    break;

                default:
                    endsDialogue.boolValue = EditorGUILayout.ToggleLeft("Завершает разговор (ВЫХОД)", endsDialogue.boolValue);
                    if (endsDialogue.boolValue)
                        nextNodeId.stringValue = string.Empty;
                    else
                        DrawNodeTargetPopup(nextNodeId, nodes, "Переход");
                    break;
            }

            EndSemanticCard(SemanticCategory.Choice);
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

        private static void DrawTextBlockKindPopup(SerializedProperty kindProperty)
        {
            string[] labels = new string[TextBlockKindValues.Length];
            int selected = 0;
            for (int i = 0; i < TextBlockKindValues.Length; i++)
            {
                labels[i] = TextBlockKindLabel(TextBlockKindValues[i]);
                if (kindProperty.enumValueIndex == (int)TextBlockKindValues[i])
                    selected = i;
            }

            int next = EditorGUILayout.Popup("Тип", selected, labels);
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
            if (kind == NarrativeEffectType.ChangeRelation)
                EditorGUILayout.PropertyField(intParam, new GUIContent("Изменение (дельта)"));

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
