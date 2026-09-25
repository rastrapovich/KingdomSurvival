using System;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
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

        private void AddDialoguePreview(VisualElement parent, string dialogueId)
        {
            DialogueDatabaseAsset dialogueDatabase = LoadDialogueDatabase();
            DialogueDefinitionData dialogue = dialogueDatabase != null
                ? dialogueDatabase.FindDialogue(dialogueId) : null;
            if (dialogue == null)
            {
                parent.Add(MakeMutedLabel("Связанный диалог не найден. Проверьте ID и базу."));
                return;
            }
            int checks = 0;
            foreach (DialogueNodeData node in dialogue.Nodes)
            {
                foreach (DialogueTextBlockData block in node.GetEffectiveTextBlocks())
                    if (block.HasPassiveCheck) checks++;
                foreach (DialogueChoiceData choice in node.Choices)
                    if (choice.IsActiveCheck) checks++;
            }
            AddHeader(parent, "СЦЕНА: " + dialogue.Title);
            parent.Add(MakeMutedLabel(
                dialogue.Nodes.Count + " узлов · " + checks + " проверок · " + dialogue.Status));
            DialogueNodeData start = null;
            foreach (DialogueNodeData node in dialogue.Nodes)
                if (node.Id == dialogue.StartNodeId) { start = node; break; }
            if (start != null)
            {
                foreach (DialogueTextBlockData block in start.GetEffectiveTextBlocks())
                {
                    if (!string.IsNullOrWhiteSpace(block.Text))
                    {
                        parent.Add(MakeMutedLabel(block.Text));
                        break;
                    }
                }
                foreach (DialogueChoiceData choice in start.Choices)
                    parent.Add(MakeMutedLabel("• " + choice.Text +
                        (choice.IsActiveCheck ? " [проверка]" : string.Empty)));
            }
            parent.Add(new Button(() =>
            {
                KingdomSurvival.DialogueDatabase.Editor.DialogueDatabaseWindow.OpenAt(dialogueId);
            }) { text = "Открыть связанный диалог" });
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
