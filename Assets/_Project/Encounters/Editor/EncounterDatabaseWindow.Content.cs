using System;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    public sealed partial class EncounterDatabaseWindow
    {
        private const string DialogueAssetPath =
            "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset";

        private static DialogueDatabaseAsset LoadDialogueDatabase()
        {
            return AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(DialogueAssetPath);
        }

        // Сцена встречи: выбор диалога по названию, его начало, «▶ Играть»,
        // переход в базу диалогов и иллюстрация события.
        private void AddDialogueSection(VisualElement parent, SerializedProperty dialogueIdProperty)
        {
            DialogueDatabaseAsset dialogueDatabase = LoadDialogueDatabase();
            string dialogueId = dialogueIdProperty.stringValue;

            if (dialogueDatabase != null)
            {
                List<string> ids = new List<string>();
                Dictionary<string, string> titles = new Dictionary<string, string>();
                foreach (DialogueDefinitionData candidate in dialogueDatabase.Dialogues)
                {
                    if (candidate == null || (candidate.Category != DialogueCategory.RandomEncounter && candidate.Id != dialogueId))
                        continue;
                    ids.Add(candidate.Id);
                    titles[candidate.Id] = string.IsNullOrWhiteSpace(candidate.Title) ? candidate.Id : candidate.Title;
                }
                if (!ids.Contains(dialogueId))
                    ids.Insert(0, dialogueId);
                string Label(string id) => string.IsNullOrEmpty(id) ? "— диалог не выбран —"
                    : titles.TryGetValue(id, out string title) ? (showProduction ? title + "  [" + id + "]" : title)
                    : "? " + id;
                PopupField<string> picker = new PopupField<string>("Диалог", ids, dialogueId, Label, Label);
                picker.tooltip = "Сцена, которую увидит игрок (диалоги категории «Случайные встречи»)";
                picker.RegisterValueChangedCallback(evt =>
                {
                    dialogueIdProperty.stringValue = evt.newValue;
                    CommitAndRebuild();
                });
                parent.Add(picker);
            }
            if (showProduction)
                parent.Add(MakeText(dialogueIdProperty, "ID диалога"));

            DialogueDefinitionData dialogue = dialogueDatabase != null ? dialogueDatabase.FindDialogue(dialogueId) : null;
            if (dialogue == null)
            {
                parent.Add(MakeMutedLabel(string.IsNullOrEmpty(dialogueId)
                    ? "Выберите диалог или создайте его в базе диалогов."
                    : "Диалог «" + dialogueId + "» не найден в базе."));
                return;
            }

            DialogueNodeData start = null;
            foreach (DialogueNodeData node in dialogue.Nodes)
                if (node.Id == dialogue.StartNodeId) { start = node; break; }

            VisualElement preview = new VisualElement();
            preview.style.marginTop = 4f;
            preview.style.paddingLeft = 8f;
            preview.style.borderLeftWidth = 2f;
            preview.style.borderLeftColor = new Color(1f, 1f, 1f, 0.15f);
            if (start != null)
            {
                foreach (DialogueTextBlockData block in start.GetEffectiveTextBlocks())
                {
                    if (string.IsNullOrWhiteSpace(block.Text))
                        continue;
                    Label text = new Label("«" + block.Text + "»");
                    text.style.whiteSpace = WhiteSpace.Normal;
                    text.style.unityFontStyleAndWeight = FontStyle.Italic;
                    preview.Add(text);
                    break;
                }
                foreach (DialogueChoiceData choice in start.Choices)
                    preview.Add(MakeMutedLabel("• " + choice.Text + (choice.IsActiveCheck ? "  [проверка]" : string.Empty)));
            }
            parent.Add(preview);

            VisualElement buttons = Row();
            buttons.style.marginTop = 4f;
            Button play = new Button(() => KingdomSurvival.DialogueDatabase.Editor.DialogueGamePreview.Open(dialogueId))
                { text = "▶ Играть", tooltip = "Пройти сцену в окне как в игре" };
            buttons.Add(play);
            buttons.Add(new Button(() => KingdomSurvival.DialogueDatabase.Editor.DialogueDatabaseWindow.OpenAt(dialogueId))
                { text = "Открыть в базе диалогов" });
            parent.Add(buttons);

            ObjectField illustrationField = new ObjectField("Иллюстрация")
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                value = dialogue.SceneIllustration,
                tooltip = "Картинка на всю сцену вместо портрета говорящего. Пусто — портреты. Кадрирование — в базе диалогов."
            };
            illustrationField.RegisterValueChangedCallback(evt =>
                SetDialogueIllustration(dialogueDatabase, dialogueId, evt.newValue as Sprite));
            parent.Add(illustrationField);
        }

        private static void SetDialogueIllustration(DialogueDatabaseAsset dialogueDatabase,
            string dialogueId, Sprite illustration)
        {
            if (dialogueDatabase == null) return;
            SerializedObject serialized = new SerializedObject(dialogueDatabase);
            serialized.Update();
            SerializedProperty dialogues = serialized.FindProperty("dialogues");
            for (int i = 0; i < dialogues.arraySize; i++)
            {
                SerializedProperty dialogue = dialogues.GetArrayElementAtIndex(i);
                if (dialogue.FindPropertyRelative("id").stringValue != dialogueId) continue;
                Undo.RecordObject(dialogueDatabase, "Change Encounter Illustration");
                dialogue.FindPropertyRelative("sceneIllustration").objectReferenceValue = illustration;
                SerializedProperty scale = dialogue.FindPropertyRelative("sceneIllustrationScale");
                if (illustration != null && scale.floatValue <= 0f)
                    scale.floatValue = 1f;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(dialogueDatabase);
                return;
            }
        }

        private static List<string> FindExternalReferences(string id)
        {
            List<string> paths = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) return paths;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/_Project/", StringComparison.Ordinal) ||
                    path == EncountersAssetPath || path == DialogueAssetPath) continue;
                string ext = Path.GetExtension(path);
                if (ext != ".asset" && ext != ".prefab" && ext != ".unity" &&
                    ext != ".cs" && ext != ".uxml" && ext != ".json") continue;
                try
                {
                    if (File.Exists(path) &&
                        File.ReadAllText(path).IndexOf(id, StringComparison.Ordinal) >= 0)
                        paths.Add(path);
                }
                catch (IOException) { paths.Add(path + " (не удалось проверить)"); }
                catch (UnauthorizedAccessException) { paths.Add(path + " (нет доступа)"); }
            }
            return paths;
        }

        private static bool AssetContainsRecord(string path, string prefix, string id)
        {
            foreach (string line in File.ReadLines(path))
                if (line.Trim() == prefix + id) return true;
            return false;
        }

        private void DeleteEncounterPermanently()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count)
                return;
            serializedDatabase.ApplyModifiedProperties();
            EncounterDefinition encounter = database.Encounters[selectedEncounterIndex];
            string id = encounter.EncounterId;
            string dialogueId = encounter.DialogueId;
            List<string> blockers = FindExternalReferences(id);
            if (blockers.Count > 0)
            {
                EditorUtility.DisplayDialog("Удаление остановлено",
                    "ID энкаунтера используется в:\n" + string.Join("\n", blockers) +
                    "\nСначала уберите ссылки, затем повторите удаление.", "Понятно");
                return;
            }
            DialogueDatabaseAsset dialogueDatabase = LoadDialogueDatabase();
            DialogueDefinitionData dialogue = dialogueDatabase != null
                ? dialogueDatabase.FindDialogue(dialogueId) : null;
            int otherOwners = 0;
            foreach (EncounterDefinition other in database.Encounters)
                if (other != null && other != encounter && other.DialogueId == dialogueId)
                    otherOwners++;
            bool deleteDialogue = dialogue != null && otherOwners == 0 &&
                dialogue.Category == DialogueCategory.RandomEncounter;
            if (deleteDialogue)
            {
                blockers = FindExternalReferences(dialogueId);
                if (blockers.Count > 0)
                {
                    EditorUtility.DisplayDialog("Удаление остановлено",
                        "Диалог используется вне этой пары:\n" + string.Join("\n", blockers) +
                        "\nСначала уберите ссылки, затем повторите удаление.", "Понятно");
                    return;
                }
            }
            string details = deleteDialogue ? "Связанный уникальный диалог тоже будет удалён."
                : dialogue == null ? "Связанного диалога в базе нет."
                : "Связанный диалог используется ещё где-то или принадлежит другой категории; он останется.";
            if (!EditorUtility.DisplayDialog("Удалить из базы?",
                id + "\n" + details + "\nСтарые сохранения автоматически не переписываются.",
                "Удалить полностью", "Отмена"))
                return;

            Undo.RecordObject(database, "Delete Encounter");
            serializedDatabase.Update();
            encountersProperty.DeleteArrayElementAtIndex(selectedEncounterIndex);
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            if (deleteDialogue)
            {
                Undo.RecordObject(dialogueDatabase, "Delete Encounter Dialogue");
                SerializedObject serializedDialogueDatabase = new SerializedObject(dialogueDatabase);
                SerializedProperty dialogues = serializedDialogueDatabase.FindProperty("dialogues");
                for (int i = 0; i < dialogues.arraySize; i++)
                    if (dialogues.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == dialogueId)
                    {
                        dialogues.DeleteArrayElementAtIndex(i);
                        break;
                    }
                serializedDialogueDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(dialogueDatabase);
            }
            AssetDatabase.SaveAssets();
            if (AssetContainsRecord(EncountersAssetPath, "EncounterId: ", id) ||
                (deleteDialogue && AssetContainsRecord(DialogueAssetPath, "id: ", dialogueId)))
                EditorUtility.DisplayDialog("Не удалось записать удаление",
                    "База осталась прежней на диске: файл занят другим процессом. " +
                    "Освободите файл и повторите удаление; после перезапуска Unity запись вернётся.",
                    "Понятно");
            selectedEncounterIndex = Mathf.Min(selectedEncounterIndex, database.Encounters.Count - 1);
            RefreshEncounterList();
            ShowSelectedEncounter();
        }
    }
}
