using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using KingdomSurvival.Encounters;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
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
                string label = (string.IsNullOrWhiteSpace(name) ? "<без имени>" : name) + (showProduction ? "\n" + id : string.Empty);
                GUIStyle style = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    fixedHeight = showProduction ? 40f : 24f
                };
                if (GUILayout.Toggle(selectedSpeakerIndex == i, label, style))
                    selectedSpeakerIndex = i;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Персонаж"))
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
                EditorGUILayout.HelpBox("Добавьте первого персонажа.", MessageType.Info);
            }
            else
            {
                selectedSpeakerIndex = Mathf.Clamp(selectedSpeakerIndex, 0, speakers.arraySize - 1);
                SerializedProperty speaker = speakers.GetArrayElementAtIndex(selectedSpeakerIndex);
                EditorGUILayout.LabelField("ПЕРСОНАЖ", EditorStyles.boldLabel);
                if (showProduction)
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
                ResetPreview();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(validationMessage))
                EditorGUILayout.LabelField(validationMessage, EditorStyles.miniLabel);

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
            string id = dialogues.GetArrayElementAtIndex(selectedDialogueIndex).FindPropertyRelative("id").stringValue;
            EncounterDatabaseAsset encounters = AssetDatabase.LoadAssetAtPath<EncounterDatabaseAsset>(
                "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset");
            if (encounters != null)
            {
                List<string> owners = new List<string>();
                foreach (EncounterDefinition encounter in encounters.Encounters)
                    if (encounter != null && encounter.DialogueId == id)
                        owners.Add(encounter.EncounterId);
                if (owners.Count > 0)
                {
                    EditorUtility.DisplayDialog("Диалог используется",
                        "Сначала удалите или перепривяжите энкаунтеры: " +
                        string.Join(", ", owners) + ".", "Понятно");
                    return;
                }
            }
            List<string> external = new List<string>();
            string dialoguePath = AssetDatabase.GetAssetPath(database);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/_Project/", StringComparison.Ordinal) ||
                    path == dialoguePath || path ==
                    "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset")
                    continue;
                string ext = Path.GetExtension(path);
                if (ext != ".asset" && ext != ".prefab" && ext != ".unity" &&
                    ext != ".cs" && ext != ".uxml" && ext != ".json")
                    continue;
                if (File.Exists(path) && File.ReadAllText(path).IndexOf(id, StringComparison.Ordinal) >= 0)
                    external.Add(path);
            }
            if (external.Count > 0)
            {
                EditorUtility.DisplayDialog("Диалог используется",
                    "ID найден в других файлах:\n" + string.Join("\n", external) +
                    "\nСначала уберите ссылки.", "Понятно");
                return;
            }
            if (!EditorUtility.DisplayDialog("Удалить диалог?", "Диалог «" + id +
                "» будет удалён из базы.", "Удалить", "Отмена"))
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
