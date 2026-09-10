using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private void DrawPreviewPanel(SerializedObject serializedDatabase)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(RightWidth), GUILayout.ExpandHeight(true));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            EditorGUILayout.LabelField("ПРОВЕРКА И PREVIEW", EditorStyles.boldLabel);

            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            if (dialogues.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Нет диалогов.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            selectedDialogueIndex = Mathf.Clamp(selectedDialogueIndex, 0, dialogues.arraySize - 1);
            SerializedProperty dialogue = dialogues.GetArrayElementAtIndex(selectedDialogueIndex);
            string dialogueId = dialogue.FindPropertyRelative("id").stringValue;

            DrawPreviewAuthoringContext();

            if (GUILayout.Button("Проверить выбранный"))
            {
                serializedDatabase.ApplyModifiedProperties();
                ValidateSelected(dialogueId);
            }

            if (GUILayout.Button("Проверить все диалоги"))
            {
                serializedDatabase.ApplyModifiedProperties();
                ValidateAll();
            }

            if (GUILayout.Button("▶ Запустить / с начала"))
            {
                serializedDatabase.ApplyModifiedProperties();
                StartPreview(dialogueId);
            }

            if (validationIssues.Count > 0)
            {
                GUILayout.Space(6f);
                for (int i = 0; i < validationIssues.Count; i++)
                    EditorGUILayout.HelpBox(validationIssues[i], MessageType.Warning);
            }

            GUILayout.Space(10f);
            DrawPreview(dialogueId);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // §13: тестовый контекст Preview — качества, компетенции, особенности,
        // флаги, знания, отношения, спутники, предметы, принудительный исход.
        // Не связан с реальным игровым сохранением.
        private void DrawPreviewAuthoringContext()
        {
            previewHeroExpanded = EditorGUILayout.Foldout(previewHeroExpanded, "Тестовый герой (Preview)", true);
            if (!previewHeroExpanded)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            previewHero.Strength = EditorGUILayout.IntSlider("Сила", previewHero.Strength, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            previewHero.Dexterity = EditorGUILayout.IntSlider("Сноровка", previewHero.Dexterity, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            previewHero.Fortitude = EditorGUILayout.IntSlider("Стойкость", previewHero.Fortitude, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            previewHero.Instinct = EditorGUILayout.IntSlider("Чутьё", previewHero.Instinct, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            previewHero.Judgment = EditorGUILayout.IntSlider("Суждение", previewHero.Judgment, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            previewHero.Character = EditorGUILayout.IntSlider("Характер", previewHero.Character, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);

            GUILayout.Space(4f);
            int fieldcraft = previewHero.GetCompetency(NarrativeCompetencyIds.Fieldcraft);
            int nextFieldcraft = EditorGUILayout.IntSlider("Следопытство", fieldcraft, 0, 5);
            if (nextFieldcraft != fieldcraft)
                previewHero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, nextFieldcraft);

            GUILayout.Space(4f);
            bool knowsTheWay = previewHero.HasTrait(NarrativeTraitIds.KnowsTheWay);
            if (EditorGUILayout.ToggleLeft("Знающий дорогу", knowsTheWay) != knowsTheWay)
            {
                if (knowsTheWay) previewHero.RemoveTrait(NarrativeTraitIds.KnowsTheWay);
                else previewHero.GrantTrait(NarrativeTraitIds.KnowsTheWay);
            }

            bool naturalist = previewHero.HasTrait(NarrativeTraitIds.Naturalist);
            if (EditorGUILayout.ToggleLeft("Натуралист", naturalist) != naturalist)
            {
                if (naturalist) previewHero.RemoveTrait(NarrativeTraitIds.Naturalist);
                else previewHero.GrantTrait(NarrativeTraitIds.Naturalist);
            }

            GUILayout.Space(4f);
            previewFlagsCsv = EditorGUILayout.TextField("Флаги (через запятую)", previewFlagsCsv);
            previewKnowledgeCsv = EditorGUILayout.TextField("Знания (через запятую)", previewKnowledgeCsv);
            previewRelationsCsv = EditorGUILayout.TextField("Отношения (id:значение,...)", previewRelationsCsv);
            previewCompanionsCsv = EditorGUILayout.TextField("Спутники (через запятую)", previewCompanionsCsv);
            previewItemsCsv = EditorGUILayout.TextField("Предметы (через запятую)", previewItemsCsv);
            previewWorldSeed = EditorGUILayout.IntField("Seed мира", previewWorldSeed);

            GUILayout.Space(4f);
            previewForcedOutcome = (NarrativeCheckForcedOutcome)EditorGUILayout.EnumPopup(
                "Исход активной проверки", previewForcedOutcome);
            EditorGUILayout.HelpBox(
                "Принудительный исход действует только в этом окне Preview. Игровой runtime всегда честно бросает кубики.",
                MessageType.None);

            GUILayout.Space(4f);
            previewRevealHiddenTextForAuthor = EditorGUILayout.ToggleLeft(
                "Показать упущенный текст пассивных проверок [только Preview]",
                previewRevealHiddenTextForAuthor);
            EditorGUILayout.HelpBox(
                "Провал пассивной проверки всегда виден игроку (ИСТОЧНИК: ПРОВАЛ) — это не метагейм, а осознанная presentation-семантика. " +
                "Сам упущенный текст в production невозможен ни при каких условиях; этот переключатель — только авторский просмотр здесь, в Preview.",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview(string dialogueId)
        {
            if (!string.IsNullOrWhiteSpace(previewMessage))
                EditorGUILayout.HelpBox(previewMessage, MessageType.Info);

            if (previewLastCheckPresentation != null)
                DrawCheckPresentationSummary(previewLastCheckPresentation);

            if (previewSession == null || !previewSession.IsActive || previewView == null ||
                !string.Equals(previewDialogueId, dialogueId, StringComparison.Ordinal))
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");

            for (int i = 0; i < previewView.VisibleTextBlocks.Count; i++)
            {
                NarrativeDialogueVisibleBlock block = previewView.VisibleTextBlocks[i];

                // §15/§18 инструкции "новое отображение пассивных наблюдений и
                // проверок": Preview по умолчанию показывает ровно то же, что
                // видит игрок — checked-observation рисуется одной строкой
                // "ИСТОЧНИК: РЕЗУЛЬТАТ [— текст]" без подписи говорящего,
                // провал не раскрывает тело текста.
                bool isCheckedObservation = block.CheckPresentation != null &&
                    (block.Kind == DialogueTextBlockKind.Observation ||
                     block.Kind == DialogueTextBlockKind.Memory ||
                     block.Kind == DialogueTextBlockKind.HeroThought ||
                     block.Kind == DialogueTextBlockKind.Narration);

                if (isCheckedObservation)
                {
                    string source = NarrativeCheckPresentationBuilder.BuildSourceLabel(block.CheckPresentation).ToUpperInvariant() + ":";
                    string result = block.CheckPresentation.Success ? "УСПЕХ" : "ПРОВАЛ";
                    string line = block.IsTextRevealed ? source + " " + result + " — " + block.Text : source + " " + result;
                    EditorGUILayout.LabelField(
                        new GUIContent(line, NarrativeCheckPresentationText.BuildFullBreakdown(block.CheckPresentation)),
                        EditorStyles.wordWrappedLabel);

                    if (!block.IsTextRevealed && !string.IsNullOrEmpty(block.PreviewOnlyHiddenText))
                    {
                        EditorGUILayout.LabelField(
                            "[ТОЛЬКО PREVIEW] " + block.PreviewOnlyHiddenText,
                            EditorStyles.wordWrappedMiniLabel);
                    }

                    GUILayout.Space(6f);
                    continue;
                }

                string headerLabel = block.SpeakerDisplayName + "  [" + block.Kind + "]";
                EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);
                if (!string.IsNullOrWhiteSpace(block.SpeakerRole))
                    EditorGUILayout.LabelField(block.SpeakerRole, EditorStyles.miniLabel);

                if (block.CheckPresentation != null)
                    DrawCheckPresentationSummary(block.CheckPresentation);

                EditorGUILayout.LabelField(block.Text, EditorStyles.wordWrappedLabel);
                GUILayout.Space(6f);
            }

            GUILayout.Space(4f);

            for (int i = 0; i < previewView.AvailableChoices.Count; i++)
            {
                NarrativeDialogueChoiceView choiceView = previewView.AvailableChoices[i];
                string label = choiceView.Text;
                if (choiceView.Kind == DialogueChoiceKind.Exit)
                    label += "  [EXIT]";

                if (GUILayout.Button(label, GUILayout.MinHeight(30f)))
                    ApplyPreviewSelection(choiceView.ChoiceId);

                if (!string.IsNullOrWhiteSpace(choiceView.MechanicalSummary))
                    EditorGUILayout.LabelField(choiceView.MechanicalSummary, EditorStyles.miniLabel);
            }

            if (previewView.DisabledChoices.Count > 0)
            {
                GUILayout.Space(4f);
                for (int i = 0; i < previewView.DisabledChoices.Count; i++)
                {
                    NarrativeDialogueChoiceView disabledView = previewView.DisabledChoices[i];
                    GUI.enabled = false;
                    GUILayout.Button(disabledView.Text, GUILayout.MinHeight(26f));
                    GUI.enabled = true;
                    EditorGUILayout.LabelField(disabledView.DisabledHint, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyPreviewSelection(string choiceId)
        {
            try
            {
                NarrativeDialogueSelectionResult result = previewSession.SelectChoicePreview(choiceId, previewForcedOutcome);
                if (result.DialogueEnded)
                {
                    previewView = null;
                    previewMessage = "Диалог завершён. Нажмите «Запустить / с начала», чтобы пройти его снова.";
                    previewLastCheckPresentation = null;
                }
                else
                {
                    // Preview всегда строится через BuildViewPreview — с
                    // revealHiddenTextForAuthor=false он даёт ровно тот же
                    // результат, что и production BuildView() (§15).
                    previewView = previewSession.BuildViewPreview(previewRevealHiddenTextForAuthor);
                    previewMessage = BuildCheckResultMessage(result.CheckResult);
                    previewLastCheckPresentation = result.CheckPresentation;
                }
            }
            catch (InvalidOperationException exception)
            {
                previewMessage = exception.Message;
            }

            Repaint();
        }

        private static string BuildCheckResultMessage(NarrativeCheckResult result)
        {
            if (result == null)
                return string.Empty;

            string outcome = result.Success ? "Успех" : "Провал";
            string dice = result.HasDice ? " (" + result.DieOne + "+" + result.DieTwo + ")" : string.Empty;
            string forced = result.IsForcedByPreview ? " [принудительно]" : string.Empty;
            return outcome + dice + ": итог " + result.Total + " против сложности " + result.Difficulty + forced + ".";
        }

        // §14/§17: "наведение показывает подробную математику". IMGUI не
        // даёт удобной кастомной панели по hover — используется нативный
        // однострочный (с переносами) tooltip GUIContent поверх краткой
        // строки "КАЧЕСТВО [+ КОМПЕТЕНЦИЯ] — УСПЕХ/ПРОВАЛ".
        private static void DrawCheckPresentationSummary(NarrativeCheckPresentationData data)
        {
            string source = NarrativeCheckPresentationBuilder.BuildSourceLabel(data).ToUpperInvariant();
            string result = data.Success ? "УСПЕХ" : "ПРОВАЛ";
            string summary = source + " — " + result;
            string breakdown = NarrativeCheckPresentationText.BuildFullBreakdown(data);

            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(new GUIContent(summary, breakdown), style);
        }

        private void DrawSpeakersTab()
        {
            SerializedObject serializedDatabase = new SerializedObject(database);
            serializedDatabase.Update();
            SerializedProperty speakers = serializedDatabase.FindProperty("speakers");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftWidth), GUILayout.ExpandHeight(true));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            for (int i = 0; i < speakers.arraySize; i++)
            {
                SerializedProperty speaker = speakers.GetArrayElementAtIndex(i);
                string id = speaker.FindPropertyRelative("id").stringValue;
                string name = speaker.FindPropertyRelative("displayName").stringValue;
                string label = (string.IsNullOrWhiteSpace(name) ? "<без имени>" : name) + "\n" + id;
                GUIStyle style = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    fixedHeight = 42f
                };
                if (GUILayout.Toggle(selectedSpeakerIndex == i, label, style))
                    selectedSpeakerIndex = i;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Говорящий"))
                AddSpeaker(serializedDatabase);
            GUI.enabled = speakers.arraySize > 0;
            if (GUILayout.Button("Удалить"))
                DeleteSpeaker(serializedDatabase);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (speakers.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Добавьте первого говорящего.", MessageType.Info);
            }
            else
            {
                selectedSpeakerIndex = Mathf.Clamp(selectedSpeakerIndex, 0, speakers.arraySize - 1);
                SerializedProperty speaker = speakers.GetArrayElementAtIndex(selectedSpeakerIndex);
                EditorGUILayout.LabelField("ГОВОРЯЩИЙ", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(speaker.FindPropertyRelative("id"), new GUIContent("ID"));
                EditorGUILayout.PropertyField(speaker.FindPropertyRelative("displayName"), new GUIContent("Имя в игре"));
                EditorGUILayout.PropertyField(speaker.FindPropertyRelative("role"), new GUIContent("Подпись / роль"));
                EditorGUILayout.PropertyField(speaker.FindPropertyRelative("portrait"), new GUIContent("Портрет"));
                DrawSpeakerPortraitFraming(speaker);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (serializedDatabase.hasModifiedProperties)
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                ResetPreview();
            }
        }

        private void DrawValidationTab()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Проверить всю базу", GUILayout.Width(180f)))
                ValidateAll();
            if (GUILayout.Button("Очистить", GUILayout.Width(100f)))
                validationIssues.Clear();
            EditorGUILayout.EndHorizontal();

            if (validationIssues.Count == 0)
            {
                EditorGUILayout.HelpBox("Ошибок не показано. Нажмите «Проверить всю базу».", MessageType.Info);
            }
            else
            {
                centerScroll = EditorGUILayout.BeginScrollView(centerScroll);
                for (int i = 0; i < validationIssues.Count; i++)
                    EditorGUILayout.HelpBox(validationIssues[i], MessageType.Warning);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private bool MatchesDialogueFilter(SerializedProperty dialogue, DialogueCategory category)
        {
            if (categoryFilter > 0 && category != (DialogueCategory)(categoryFilter - 1))
                return false;
            if (string.IsNullOrWhiteSpace(search))
                return true;

            string needle = search.Trim();
            string id = dialogue.FindPropertyRelative("id").stringValue;
            string title = dialogue.FindPropertyRelative("title").stringValue;
            if (ContainsIgnoreCase(id, needle) || ContainsIgnoreCase(title, needle))
                return true;

            SerializedProperty tags = dialogue.FindPropertyRelative("tags");
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (ContainsIgnoreCase(tags.GetArrayElementAtIndex(i).stringValue, needle))
                    return true;
            }

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string speakerId = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("speakerId").stringValue;
                if (ContainsIgnoreCase(speakerId, needle))
                    return true;
                DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
                if (speaker != null && (ContainsIgnoreCase(speaker.DisplayName, needle) || ContainsIgnoreCase(speaker.Role, needle)))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddDialogue(SerializedObject serializedDatabase)
        {
            Undo.RecordObject(database, "Add Dialogue");
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            int index = dialogues.arraySize;
            dialogues.arraySize++;
            SerializedProperty dialogue = dialogues.GetArrayElementAtIndex(index);
            string id = MakeUniqueDialogueId("dialogue_new");
            InitializeDialogue(dialogue, id);
            serializedDatabase.ApplyModifiedProperties();
            selectedDialogueIndex = index;
            EditorUtility.SetDirty(database);
            ResetPreview();
        }

        // Использует SerializedProperty.DuplicateCommand(), чтобы глубоко
        // скопировать весь диалог (включая textBlocks/choices/проверки/
        // эффекты произвольной вложенности), а не переписывать копирование
        // вручную поле за полем при каждом расширении схемы.
        private void DuplicateDialogue(SerializedObject serializedDatabase)
        {
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            if (dialogues.arraySize == 0)
                return;

            selectedDialogueIndex = Mathf.Clamp(selectedDialogueIndex, 0, dialogues.arraySize - 1);
            Undo.RecordObject(database, "Duplicate Dialogue");

            SerializedProperty source = dialogues.GetArrayElementAtIndex(selectedDialogueIndex);
            string originalId = source.FindPropertyRelative("id").stringValue;
            string originalTitle = source.FindPropertyRelative("title").stringValue;

            source.DuplicateCommand();
            int newIndex = selectedDialogueIndex + 1;
            SerializedProperty destination = dialogues.GetArrayElementAtIndex(newIndex);

            destination.FindPropertyRelative("id").stringValue = MakeUniqueDialogueId(originalId + "_copy");
            destination.FindPropertyRelative("title").stringValue = originalTitle + " — копия";
            destination.FindPropertyRelative("status").enumValueIndex = (int)DialogueProductionStatus.Working;

            RegenerateNarrativeIdentifiers(destination);

            serializedDatabase.ApplyModifiedProperties();
            selectedDialogueIndex = newIndex;
            EditorUtility.SetDirty(database);
            ResetPreview();
        }

        private void DeleteDialogue(SerializedObject serializedDatabase)
        {
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            if (dialogues.arraySize == 0)
                return;

            selectedDialogueIndex = Mathf.Clamp(selectedDialogueIndex, 0, dialogues.arraySize - 1);
            if (!EditorUtility.DisplayDialog("Удалить диалог?", "Диалог будет удалён из базы.", "Удалить", "Отмена"))
                return;

            Undo.RecordObject(database, "Delete Dialogue");
            dialogues.DeleteArrayElementAtIndex(selectedDialogueIndex);
            serializedDatabase.ApplyModifiedProperties();
            selectedDialogueIndex = Mathf.Max(0, selectedDialogueIndex - 1);
            EditorUtility.SetDirty(database);
            ResetPreview();
        }

        private void AddSpeaker(SerializedObject serializedDatabase)
        {
            Undo.RecordObject(database, "Add Dialogue Speaker");
            SerializedProperty speakers = serializedDatabase.FindProperty("speakers");
            int index = speakers.arraySize;
            speakers.arraySize++;
            SerializedProperty speaker = speakers.GetArrayElementAtIndex(index);
            speaker.FindPropertyRelative("id").stringValue = MakeUniqueSpeakerId("speaker_new");
            speaker.FindPropertyRelative("displayName").stringValue = "Новый персонаж";
            speaker.FindPropertyRelative("role").stringValue = string.Empty;
            speaker.FindPropertyRelative("portrait").objectReferenceValue = null;
            speaker.FindPropertyRelative("overridePortraitFraming").boolValue = false;
            speaker.FindPropertyRelative("portraitScale").floatValue = 1f;
            speaker.FindPropertyRelative("portraitOffsetNormalized").vector2Value = Vector2.zero;
            speaker.FindPropertyRelative("portraitFlipX").boolValue = false;
            serializedDatabase.ApplyModifiedProperties();
            selectedSpeakerIndex = index;
            EditorUtility.SetDirty(database);
        }

        private void DeleteSpeaker(SerializedObject serializedDatabase)
        {
            SerializedProperty speakers = serializedDatabase.FindProperty("speakers");
            if (speakers.arraySize == 0)
                return;

            selectedSpeakerIndex = Mathf.Clamp(selectedSpeakerIndex, 0, speakers.arraySize - 1);
            string id = speakers.GetArrayElementAtIndex(selectedSpeakerIndex).FindPropertyRelative("id").stringValue;
            if (!EditorUtility.DisplayDialog(
                    "Удалить говорящего?",
                    "Ссылки на '" + id + "' останутся в узлах и будут показаны как ошибки валидации.",
                    "Удалить",
                    "Отмена"))
                return;

            Undo.RecordObject(database, "Delete Dialogue Speaker");
            speakers.DeleteArrayElementAtIndex(selectedSpeakerIndex);
            serializedDatabase.ApplyModifiedProperties();
            selectedSpeakerIndex = Mathf.Max(0, selectedSpeakerIndex - 1);
            EditorUtility.SetDirty(database);
            ResetPreview();
        }

    }
}
