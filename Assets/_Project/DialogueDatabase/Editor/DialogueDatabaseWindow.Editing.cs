using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private void AddNode(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            Vector2 position = new Vector2(80f + nodes.arraySize * 36f, 80f + nodes.arraySize * 36f);
            AddNodeAtPosition(dialogue, position);
        }

        private string AddNodeAtPosition(SerializedProperty dialogue, Vector2 position)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            string id = MakeUniqueNodeId(nodes, "node");
            int index = nodes.arraySize;
            nodes.arraySize++;
            SerializedProperty node = nodes.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("id").stringValue = id;
            node.FindPropertyRelative("speakerId").stringValue = database.Speakers.Count > 0 ? database.Speakers[0].Id : string.Empty;
            node.FindPropertyRelative("text").stringValue = "Новая реплика.";
            SerializedProperty choices = node.FindPropertyRelative("choices");
            choices.arraySize = 1;
            SerializedProperty choice = choices.GetArrayElementAtIndex(0);
            choice.FindPropertyRelative("text").stringValue = "Завершить разговор.";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            SetNodeEditorPosition(node, position);
            node.isExpanded = true;
            return id;
        }

        private static void AddChoice(SerializedProperty choices)
        {
            int index = choices.arraySize;
            choices.arraySize++;
            SerializedProperty choice = choices.GetArrayElementAtIndex(index);
            choice.FindPropertyRelative("text").stringValue = "Новый ответ";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
        }

        private static void SetNodeEditorPosition(SerializedProperty node, Vector2 position)
        {
            SerializedProperty editorPosition = node.FindPropertyRelative("editorPosition");
            SerializedProperty hasEditorPosition = node.FindPropertyRelative("hasEditorPosition");
            if (editorPosition != null)
                editorPosition.vector2Value = position;
            if (hasEditorPosition != null)
                hasEditorPosition.boolValue = true;
        }

        private void InitializeDialogue(SerializedProperty dialogue, string id)
        {
            dialogue.FindPropertyRelative("id").stringValue = id;
            dialogue.FindPropertyRelative("title").stringValue = "Новый диалог";
            dialogue.FindPropertyRelative("category").enumValueIndex = (int)DialogueCategory.Test;
            dialogue.FindPropertyRelative("status").enumValueIndex = (int)DialogueProductionStatus.Working;
            dialogue.FindPropertyRelative("developerComment").stringValue = string.Empty;
            dialogue.FindPropertyRelative("startNodeId").stringValue = "start";
            dialogue.FindPropertyRelative("tags").arraySize = 0;

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            nodes.arraySize = 1;
            SerializedProperty node = nodes.GetArrayElementAtIndex(0);
            node.FindPropertyRelative("id").stringValue = "start";
            node.FindPropertyRelative("speakerId").stringValue = database.Speakers.Count > 0 ? database.Speakers[0].Id : string.Empty;
            node.FindPropertyRelative("text").stringValue = "Новая реплика.";
            SerializedProperty choices = node.FindPropertyRelative("choices");
            choices.arraySize = 1;
            SerializedProperty choice = choices.GetArrayElementAtIndex(0);
            choice.FindPropertyRelative("text").stringValue = "Завершить разговор.";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            SetNodeEditorPosition(node, new Vector2(80f, 80f));
        }

        private static void CopyDialogue(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative("id").stringValue = source.FindPropertyRelative("id").stringValue;
            destination.FindPropertyRelative("title").stringValue = source.FindPropertyRelative("title").stringValue;
            destination.FindPropertyRelative("category").enumValueIndex = source.FindPropertyRelative("category").enumValueIndex;
            destination.FindPropertyRelative("status").enumValueIndex = source.FindPropertyRelative("status").enumValueIndex;
            destination.FindPropertyRelative("developerComment").stringValue = source.FindPropertyRelative("developerComment").stringValue;
            destination.FindPropertyRelative("startNodeId").stringValue = source.FindPropertyRelative("startNodeId").stringValue;

            SerializedProperty sourceTags = source.FindPropertyRelative("tags");
            SerializedProperty destinationTags = destination.FindPropertyRelative("tags");
            destinationTags.arraySize = sourceTags.arraySize;
            for (int i = 0; i < sourceTags.arraySize; i++)
                destinationTags.GetArrayElementAtIndex(i).stringValue = sourceTags.GetArrayElementAtIndex(i).stringValue;

            SerializedProperty sourceNodes = source.FindPropertyRelative("nodes");
            SerializedProperty destinationNodes = destination.FindPropertyRelative("nodes");
            destinationNodes.arraySize = sourceNodes.arraySize;
            for (int nodeIndex = 0; nodeIndex < sourceNodes.arraySize; nodeIndex++)
            {
                SerializedProperty sourceNode = sourceNodes.GetArrayElementAtIndex(nodeIndex);
                SerializedProperty destinationNode = destinationNodes.GetArrayElementAtIndex(nodeIndex);
                destinationNode.FindPropertyRelative("id").stringValue = sourceNode.FindPropertyRelative("id").stringValue;
                destinationNode.FindPropertyRelative("speakerId").stringValue = sourceNode.FindPropertyRelative("speakerId").stringValue;
                destinationNode.FindPropertyRelative("text").stringValue = sourceNode.FindPropertyRelative("text").stringValue;

                SerializedProperty sourceChoices = sourceNode.FindPropertyRelative("choices");
                SerializedProperty destinationChoices = destinationNode.FindPropertyRelative("choices");
                destinationChoices.arraySize = sourceChoices.arraySize;
                for (int choiceIndex = 0; choiceIndex < sourceChoices.arraySize; choiceIndex++)
                {
                    SerializedProperty sourceChoice = sourceChoices.GetArrayElementAtIndex(choiceIndex);
                    SerializedProperty destinationChoice = destinationChoices.GetArrayElementAtIndex(choiceIndex);
                    destinationChoice.FindPropertyRelative("text").stringValue = sourceChoice.FindPropertyRelative("text").stringValue;
                    destinationChoice.FindPropertyRelative("nextNodeId").stringValue = sourceChoice.FindPropertyRelative("nextNodeId").stringValue;
                    destinationChoice.FindPropertyRelative("endsDialogue").boolValue = sourceChoice.FindPropertyRelative("endsDialogue").boolValue;
                }

                SerializedProperty sourcePosition = sourceNode.FindPropertyRelative("editorPosition");
                SerializedProperty sourceHasPosition = sourceNode.FindPropertyRelative("hasEditorPosition");
                SerializedProperty destinationPosition = destinationNode.FindPropertyRelative("editorPosition");
                SerializedProperty destinationHasPosition = destinationNode.FindPropertyRelative("hasEditorPosition");
                if (sourcePosition != null && destinationPosition != null)
                    destinationPosition.vector2Value = sourcePosition.vector2Value;
                if (sourceHasPosition != null && destinationHasPosition != null)
                    destinationHasPosition.boolValue = sourceHasPosition.boolValue;
            }
        }

        private void StartPreview(string dialogueId)
        {
            validationIssues.Clear();
            NarrativeDialogueDefinition definition;
            string error;
            if (!database.TryBuildRuntime(dialogueId, out definition, out error))
            {
                previewSession = null;
                previewDialogueId = string.Empty;
                previewMessage = error;
                if (!string.IsNullOrWhiteSpace(error))
                    validationIssues.Add(error);
                return;
            }

            previewSession = new NarrativeDialogueSession();
            previewSession.Start(definition);
            previewDialogueId = dialogueId;
            previewMessage = string.Empty;
        }

        private void ValidateSelected(string dialogueId)
        {
            database.CollectValidationIssuesForDialogue(dialogueId, validationIssues);
            if (validationIssues.Count == 0)
                previewMessage = "Выбранный диалог прошёл проверку.";
        }

        private void ValidateAll()
        {
            database.CollectValidationIssues(validationIssues);
            previewMessage = validationIssues.Count == 0
                ? "База диалогов прошла проверку: ошибок не найдено."
                : "Найдено ошибок: " + validationIssues.Count + ".";
        }

        private void ResetPreview()
        {
            if (previewSession != null && previewSession.IsActive)
                previewSession.End();
            previewSession = null;
            previewDialogueId = string.Empty;
            previewMessage = string.Empty;
            validationIssues.Clear();
        }

        private string MakeUniqueDialogueId(string baseId)
        {
            string clean = string.IsNullOrWhiteSpace(baseId) ? "dialogue" : baseId.Trim();
            string candidate = clean;
            int suffix = 2;
            while (database.FindDialogue(candidate) != null)
                candidate = clean + "_" + suffix++;
            return candidate;
        }

        private string MakeUniqueSpeakerId(string baseId)
        {
            string clean = string.IsNullOrWhiteSpace(baseId) ? "speaker" : baseId.Trim();
            string candidate = clean;
            int suffix = 2;
            while (database.FindSpeaker(candidate) != null)
                candidate = clean + "_" + suffix++;
            return candidate;
        }

        private static string MakeUniqueNodeId(SerializedProperty nodes, string baseId)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.arraySize; i++)
                ids.Add(nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue);

            string candidate = baseId;
            int suffix = 2;
            while (ids.Contains(candidate))
                candidate = baseId + "_" + suffix++;
            return candidate;
        }

        private static string[] GetNodeIds(SerializedProperty nodes)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
            return ids.ToArray();
        }

        private static string CategoryLabel(DialogueCategory category)
        {
            switch (category)
            {
                case DialogueCategory.Test: return "Тестовые";
                case DialogueCategory.MainStory: return "Главный сюжет";
                case DialogueCategory.SideQuest: return "Побочные квесты";
                case DialogueCategory.RandomEncounter: return "Случайные встречи";
                case DialogueCategory.AmbientNpc: return "Обычные NPC";
                case DialogueCategory.Service: return "Служебные";
                default: return category.ToString();
            }
        }

        private static string StatusLabel(DialogueProductionStatus status)
        {
            switch (status)
            {
                case DialogueProductionStatus.Test: return "тестовый";
                case DialogueProductionStatus.Working: return "рабочий";
                case DialogueProductionStatus.Approved: return "утверждённый";
                case DialogueProductionStatus.Disabled: return "отключённый";
                default: return status.ToString();
            }
        }
    }
}
