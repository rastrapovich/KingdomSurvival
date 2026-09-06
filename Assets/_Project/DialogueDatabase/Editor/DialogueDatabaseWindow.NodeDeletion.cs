using System;
using UnityEditor;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow
    {
        private bool TryDeleteDialogueNode(
            SerializedProperty dialogue,
            int nodeIndex,
            bool askForConfirmation = true)
        {
            if (dialogue == null)
                return false;

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            if (nodes == null || nodes.arraySize <= 1 || nodeIndex < 0 || nodeIndex >= nodes.arraySize)
                return false;

            SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
            string nodeId = node.FindPropertyRelative("id").stringValue;
            string nodeText = node.FindPropertyRelative("text").stringValue;
            string preview = string.IsNullOrWhiteSpace(nodeText)
                ? "<пустая реплика>"
                : nodeText.Trim();
            if (preview.Length > 90)
                preview = preview.Substring(0, 87) + "...";

            if (askForConfirmation && !EditorUtility.DisplayDialog(
                    "Удалить реплику?",
                    "Реплика '" + (string.IsNullOrWhiteSpace(nodeId) ? "<без ID>" : nodeId) + "' будет удалена.\n\n" +
                    preview + "\n\n" +
                    "Все ответы, которые вели к этой реплике, будут переведены в EXIT.",
                    "Удалить",
                    "Отмена"))
            {
                return false;
            }

            string fallbackStartId = string.Empty;
            for (int i = 0; i < nodes.arraySize; i++)
            {
                if (i == nodeIndex)
                    continue;

                fallbackStartId = nodes.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("id").stringValue;
                break;
            }

            Undo.RecordObject(database, "Delete Dialogue Node");

            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                for (int sourceIndex = 0; sourceIndex < nodes.arraySize; sourceIndex++)
                {
                    if (sourceIndex == nodeIndex)
                        continue;

                    SerializedProperty sourceNode = nodes.GetArrayElementAtIndex(sourceIndex);
                    SerializedProperty choices = sourceNode.FindPropertyRelative("choices");
                    for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                    {
                        SerializedProperty choice = choices.GetArrayElementAtIndex(choiceIndex);
                        SerializedProperty endsDialogue = choice.FindPropertyRelative("endsDialogue");
                        SerializedProperty nextNodeId = choice.FindPropertyRelative("nextNodeId");
                        if (!endsDialogue.boolValue &&
                            string.Equals(nextNodeId.stringValue, nodeId, StringComparison.Ordinal))
                        {
                            endsDialogue.boolValue = true;
                            nextNodeId.stringValue = string.Empty;
                        }
                    }
                }
            }

            SerializedProperty startNodeId = dialogue.FindPropertyRelative("startNodeId");
            if (string.Equals(startNodeId.stringValue, nodeId, StringComparison.Ordinal))
                startNodeId.stringValue = fallbackStartId;

            nodes.DeleteArrayElementAtIndex(nodeIndex);
            dialogue.serializedObject.ApplyModifiedProperties();

            graphSelectedNodeIndex = -1;
            graphDraggedNodeIndex = -1;
            graphConnectingNodeIndex = -1;
            graphConnectingChoiceIndex = -1;
            graphNeedsCenter = true;
            EditorUtility.SetDirty(database);
            ResetPreview();
            return true;
        }
    }
}
