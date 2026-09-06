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

        private void DrawPreview(string dialogueId)
        {
            if (!string.IsNullOrWhiteSpace(previewMessage))
                EditorGUILayout.HelpBox(previewMessage, MessageType.Info);

            if (previewSession == null || !previewSession.IsActive || !string.Equals(previewDialogueId, dialogueId, StringComparison.Ordinal))
                return;

            NarrativeDialogueNode node = previewSession.CurrentNode;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(node.Speaker, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(node.Role))
                EditorGUILayout.LabelField(node.Role, EditorStyles.miniLabel);
            GUILayout.Space(4f);
            EditorGUILayout.LabelField(node.Text, EditorStyles.wordWrappedLabel);
            GUILayout.Space(8f);

            for (int i = 0; i < node.Choices.Count; i++)
            {
                int choiceIndex = i;
                NarrativeDialogueChoice choice = node.Choices[i];
                string label = choice.EndsDialogue ? choice.Text + "  [EXIT]" : choice.Text;
                if (GUILayout.Button(label, GUILayout.MinHeight(32f)))
                {
                    bool continues = previewSession.SelectChoice(choiceIndex);
                    if (!continues)
                        previewMessage = "Диалог завершён. Нажмите «Запустить / с начала», чтобы пройти его снова.";
                    Repaint();
                }
            }
            EditorGUILayout.EndVertical();
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

                Sprite portrait = speaker.FindPropertyRelative("portrait").objectReferenceValue as Sprite;
                if (portrait != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(180f, 260f, GUILayout.Width(180f), GUILayout.Height(260f));
                    GUI.DrawTexture(rect, portrait.texture, ScaleMode.ScaleToFit, true);
                }
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

        private void DuplicateDialogue(SerializedObject serializedDatabase)
        {
            SerializedProperty dialogues = serializedDatabase.FindProperty("dialogues");
            if (dialogues.arraySize == 0)
                return;

            selectedDialogueIndex = Mathf.Clamp(selectedDialogueIndex, 0, dialogues.arraySize - 1);
            Undo.RecordObject(database, "Duplicate Dialogue");
            SerializedProperty source = dialogues.GetArrayElementAtIndex(selectedDialogueIndex);
            int newIndex = dialogues.arraySize;
            dialogues.arraySize++;
            SerializedProperty destination = dialogues.GetArrayElementAtIndex(newIndex);
            CopyDialogue(source, destination);

            string originalId = source.FindPropertyRelative("id").stringValue;
            string originalTitle = source.FindPropertyRelative("title").stringValue;
            destination.FindPropertyRelative("id").stringValue = MakeUniqueDialogueId(originalId + "_copy");
            destination.FindPropertyRelative("title").stringValue = originalTitle + " — копия";
            destination.FindPropertyRelative("status").enumValueIndex = (int)DialogueProductionStatus.Working;

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
