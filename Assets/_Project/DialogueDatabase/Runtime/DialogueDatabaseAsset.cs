using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase
{
    public enum DialogueCategory
    {
        Test,
        MainStory,
        SideQuest,
        RandomEncounter,
        AmbientNpc,
        Service
    }

    public enum DialogueProductionStatus
    {
        Test,
        Working,
        Approved,
        Disabled
    }

    [Serializable]
    public sealed class DialogueSpeakerData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string role = string.Empty;
        [SerializeField] private Sprite portrait;

        public string Id => id;
        public string DisplayName => displayName;
        public string Role => role;
        public Sprite Portrait => portrait;
    }

    [Serializable]
    public sealed class DialogueChoiceData
    {
        [SerializeField, TextArea(1, 3)] private string text = string.Empty;
        [SerializeField] private string nextNodeId = string.Empty;
        [SerializeField] private bool endsDialogue;

        public string Text => text;
        public string NextNodeId => nextNodeId;
        public bool EndsDialogue => endsDialogue;
    }

    [Serializable]
    public sealed class DialogueNodeData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string speakerId = string.Empty;
        [SerializeField, TextArea(3, 8)] private string text = string.Empty;
        [SerializeField] private List<DialogueChoiceData> choices = new List<DialogueChoiceData>();
        [SerializeField, HideInInspector] private Vector2 editorPosition = Vector2.zero;
        [SerializeField, HideInInspector] private bool hasEditorPosition;

        public string Id => id;
        public string SpeakerId => speakerId;
        public string Text => text;
        public IReadOnlyList<DialogueChoiceData> Choices => choices ?? (IReadOnlyList<DialogueChoiceData>)Array.Empty<DialogueChoiceData>();
        public Vector2 EditorPosition => editorPosition;
        public bool HasEditorPosition => hasEditorPosition;
    }

    [Serializable]
    public sealed class DialogueDefinitionData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField] private DialogueCategory category = DialogueCategory.Test;
        [SerializeField] private DialogueProductionStatus status = DialogueProductionStatus.Working;
        [SerializeField, TextArea(2, 5)] private string developerComment = string.Empty;
        [SerializeField] private string startNodeId = string.Empty;
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeField] private List<DialogueNodeData> nodes = new List<DialogueNodeData>();

        public string Id => id;
        public string Title => title;
        public DialogueCategory Category => category;
        public DialogueProductionStatus Status => status;
        public string DeveloperComment => developerComment;
        public string StartNodeId => startNodeId;
        public IReadOnlyList<string> Tags => tags ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<DialogueNodeData> Nodes => nodes ?? (IReadOnlyList<DialogueNodeData>)Array.Empty<DialogueNodeData>();
    }

    [CreateAssetMenu(
        fileName = "KingdomSurvivalDialogues",
        menuName = "Kingdom Survival/База диалогов")]
    public sealed class DialogueDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "DialogueDatabase/KingdomSurvivalDialogues";

        [SerializeField] private List<DialogueSpeakerData> speakers = new List<DialogueSpeakerData>();
        [SerializeField] private List<DialogueDefinitionData> dialogues = new List<DialogueDefinitionData>();

        public IReadOnlyList<DialogueSpeakerData> Speakers => speakers ?? (IReadOnlyList<DialogueSpeakerData>)Array.Empty<DialogueSpeakerData>();
        public IReadOnlyList<DialogueDefinitionData> Dialogues => dialogues ?? (IReadOnlyList<DialogueDefinitionData>)Array.Empty<DialogueDefinitionData>();

        public DialogueSpeakerData FindSpeaker(string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId) || speakers == null)
                return null;

            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                if (speaker != null && string.Equals(speaker.Id, speakerId, StringComparison.Ordinal))
                    return speaker;
            }

            return null;
        }

        public DialogueDefinitionData FindDialogue(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId) || dialogues == null)
                return null;

            for (int i = 0; i < dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = dialogues[i];
                if (dialogue != null && string.Equals(dialogue.Id, dialogueId, StringComparison.Ordinal))
                    return dialogue;
            }

            return null;
        }

        public bool TryBuildRuntime(
            string dialogueId,
            out NarrativeDialogueDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;

            DialogueDefinitionData dialogue = FindDialogue(dialogueId);
            if (dialogue == null)
            {
                error = "Диалог не найден: " + dialogueId;
                return false;
            }

            List<string> issues = new List<string>();
            CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
            if (issues.Count > 0)
            {
                error = string.Join("\n", issues);
                return false;
            }

            NarrativeDialogueNode[] runtimeNodes = new NarrativeDialogueNode[dialogue.Nodes.Count];
            for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
            {
                DialogueNodeData node = dialogue.Nodes[nodeIndex];
                DialogueSpeakerData speaker = FindSpeaker(node.SpeakerId);
                NarrativeDialogueChoice[] runtimeChoices = new NarrativeDialogueChoice[node.Choices.Count];

                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    DialogueChoiceData choice = node.Choices[choiceIndex];
                    runtimeChoices[choiceIndex] = choice.EndsDialogue
                        ? NarrativeDialogueChoice.Exit(choice.Text)
                        : new NarrativeDialogueChoice(choice.Text, choice.NextNodeId);
                }

                runtimeNodes[nodeIndex] = new NarrativeDialogueNode(
                    node.Id,
                    speaker.Id,
                    speaker.DisplayName,
                    speaker.Role,
                    node.Text,
                    runtimeChoices);
            }

            definition = new NarrativeDialogueDefinition(
                dialogue.Id,
                dialogue.StartNodeId,
                runtimeNodes);
            return true;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();

            HashSet<string> speakerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                if (speaker == null)
                {
                    issues.Add("Говорящий #" + (i + 1) + ": пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(speaker.Id))
                    issues.Add("Говорящий #" + (i + 1) + ": отсутствует ID.");
                else if (!speakerIds.Add(speaker.Id))
                    issues.Add("Повторяющийся ID говорящего: " + speaker.Id + ".");

                if (string.IsNullOrWhiteSpace(speaker.DisplayName))
                    issues.Add("Говорящий '" + speaker.Id + "': отсутствует отображаемое имя.");
            }

            HashSet<string> dialogueIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = dialogues[i];
                if (dialogue == null)
                {
                    issues.Add("Диалог #" + (i + 1) + ": пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dialogue.Id))
                    issues.Add("Диалог #" + (i + 1) + ": отсутствует ID.");
                else if (!dialogueIds.Add(dialogue.Id))
                    issues.Add("Повторяющийся ID диалога: " + dialogue.Id + ".");

                CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
            }
        }

        public void CollectValidationIssuesForDialogue(string dialogueId, List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();
            DialogueDefinitionData dialogue = FindDialogue(dialogueId);
            if (dialogue == null)
            {
                issues.Add("Диалог не найден: " + dialogueId + ".");
                return;
            }

            CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
        }

        private void CollectDialogueValidationIssues(
            DialogueDefinitionData dialogue,
            List<string> issues,
            bool includeReachability)
        {
            string prefix = string.IsNullOrWhiteSpace(dialogue.Id)
                ? "Диалог без ID"
                : "Диалог '" + dialogue.Id + "'";

            if (string.IsNullOrWhiteSpace(dialogue.Title))
                issues.Add(prefix + ": отсутствует название.");
            if (string.IsNullOrWhiteSpace(dialogue.StartNodeId))
                issues.Add(prefix + ": не назначен стартовый узел.");
            if (dialogue.Nodes == null || dialogue.Nodes.Count == 0)
            {
                issues.Add(prefix + ": нет узлов.");
                return;
            }

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, DialogueNodeData> nodesById = new Dictionary<string, DialogueNodeData>(StringComparer.Ordinal);
            for (int i = 0; i < dialogue.Nodes.Count; i++)
            {
                DialogueNodeData node = dialogue.Nodes[i];
                if (node == null)
                {
                    issues.Add(prefix + ": узел #" + (i + 1) + " пуст.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    issues.Add(prefix + ": узел #" + (i + 1) + " без ID.");
                    continue;
                }

                if (!nodeIds.Add(node.Id))
                    issues.Add(prefix + ": повторяющийся Node ID '" + node.Id + "'.");
                else
                    nodesById.Add(node.Id, node);

                if (string.IsNullOrWhiteSpace(node.SpeakerId))
                    issues.Add(prefix + " / " + node.Id + ": не назначен говорящий.");
                else if (FindSpeaker(node.SpeakerId) == null)
                    issues.Add(prefix + " / " + node.Id + ": неизвестный говорящий '" + node.SpeakerId + "'.");

                if (string.IsNullOrWhiteSpace(node.Text))
                    issues.Add(prefix + " / " + node.Id + ": пустая реплика.");
                if (node.Choices == null || node.Choices.Count == 0)
                    issues.Add(prefix + " / " + node.Id + ": нет ни одного варианта ответа.");
            }

            if (!string.IsNullOrWhiteSpace(dialogue.StartNodeId) && !nodesById.ContainsKey(dialogue.StartNodeId))
                issues.Add(prefix + ": стартовый узел '" + dialogue.StartNodeId + "' не существует.");

            foreach (KeyValuePair<string, DialogueNodeData> pair in nodesById)
            {
                DialogueNodeData node = pair.Value;
                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    DialogueChoiceData choice = node.Choices[choiceIndex];
                    string choicePrefix = prefix + " / " + node.Id + " / ответ #" + (choiceIndex + 1);
                    if (choice == null)
                    {
                        issues.Add(choicePrefix + ": пустая запись.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(choice.Text))
                        issues.Add(choicePrefix + ": пустой текст ответа.");

                    if (choice.EndsDialogue)
                    {
                        if (!string.IsNullOrWhiteSpace(choice.NextNodeId))
                            issues.Add(choicePrefix + ": EXIT-ответ не должен иметь переход.");
                    }
                    else if (string.IsNullOrWhiteSpace(choice.NextNodeId))
                    {
                        issues.Add(choicePrefix + ": не указан следующий узел.");
                    }
                    else if (!nodesById.ContainsKey(choice.NextNodeId))
                    {
                        issues.Add(choicePrefix + ": переход ведёт в отсутствующий узел '" + choice.NextNodeId + "'.");
                    }
                }
            }

            if (!includeReachability || !nodesById.ContainsKey(dialogue.StartNodeId))
                return;

            HashSet<string> reachable = CollectReachableNodes(dialogue.StartNodeId, nodesById);
            foreach (string nodeId in nodesById.Keys)
            {
                if (!reachable.Contains(nodeId))
                    issues.Add(prefix + ": узел '" + nodeId + "' недостижим из старта.");
            }

            Dictionary<string, bool> memo = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (!CanReachExit(dialogue.StartNodeId, nodesById, new HashSet<string>(StringComparer.Ordinal), memo))
                issues.Add(prefix + ": из стартового узла невозможно завершить разговор.");
        }

        private static HashSet<string> CollectReachableNodes(
            string startNodeId,
            Dictionary<string, DialogueNodeData> nodesById)
        {
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(startNodeId);

            while (queue.Count > 0)
            {
                string nodeId = queue.Dequeue();
                if (!reachable.Add(nodeId))
                    continue;

                DialogueNodeData node;
                if (!nodesById.TryGetValue(nodeId, out node) || node == null)
                    continue;

                for (int i = 0; i < node.Choices.Count; i++)
                {
                    DialogueChoiceData choice = node.Choices[i];
                    if (choice == null || choice.EndsDialogue || string.IsNullOrWhiteSpace(choice.NextNodeId))
                        continue;
                    if (nodesById.ContainsKey(choice.NextNodeId))
                        queue.Enqueue(choice.NextNodeId);
                }
            }

            return reachable;
        }

        private static bool CanReachExit(
            string nodeId,
            Dictionary<string, DialogueNodeData> nodesById,
            HashSet<string> visiting,
            Dictionary<string, bool> memo)
        {
            bool cached;
            if (memo.TryGetValue(nodeId, out cached))
                return cached;
            if (!visiting.Add(nodeId))
                return false;

            DialogueNodeData node;
            if (!nodesById.TryGetValue(nodeId, out node) || node == null)
            {
                visiting.Remove(nodeId);
                memo[nodeId] = false;
                return false;
            }

            for (int i = 0; i < node.Choices.Count; i++)
            {
                DialogueChoiceData choice = node.Choices[i];
                if (choice == null)
                    continue;
                if (choice.EndsDialogue)
                {
                    visiting.Remove(nodeId);
                    memo[nodeId] = true;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(choice.NextNodeId) &&
                    nodesById.ContainsKey(choice.NextNodeId) &&
                    CanReachExit(choice.NextNodeId, nodesById, visiting, memo))
                {
                    visiting.Remove(nodeId);
                    memo[nodeId] = true;
                    return true;
                }
            }

            visiting.Remove(nodeId);
            memo[nodeId] = false;
            return false;
        }
    }
}
