using System;
using System.Collections.Generic;

public sealed class NarrativeDialogueChoice
{
    public string Text { get; }
    public string NextNodeId { get; }
    public bool EndsDialogue { get; }

    public NarrativeDialogueChoice(
        string text,
        string nextNodeId = null,
        bool endsDialogue = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Dialogue choice text cannot be empty.", nameof(text));

        if (endsDialogue && !string.IsNullOrWhiteSpace(nextNodeId))
        {
            throw new ArgumentException(
                "An ending dialogue choice cannot also point to another node.",
                nameof(nextNodeId));
        }

        if (!endsDialogue && string.IsNullOrWhiteSpace(nextNodeId))
        {
            throw new ArgumentException(
                "A non-ending dialogue choice must point to another node.",
                nameof(nextNodeId));
        }

        Text = text;
        NextNodeId = nextNodeId;
        EndsDialogue = endsDialogue;
    }

    public static NarrativeDialogueChoice Exit(string text)
    {
        return new NarrativeDialogueChoice(text, null, true);
    }
}

public sealed class NarrativeDialogueNode
{
    private readonly List<NarrativeDialogueChoice> choices;

    public string Id { get; }
    public string Speaker { get; }
    public string Role { get; }
    public string Text { get; }
    public IReadOnlyList<NarrativeDialogueChoice> Choices => choices;

    public NarrativeDialogueNode(
        string id,
        string speaker,
        string role,
        string text,
        params NarrativeDialogueChoice[] choices)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Dialogue node id cannot be empty.", nameof(id));

        if (string.IsNullOrWhiteSpace(speaker))
            throw new ArgumentException("Dialogue speaker cannot be empty.", nameof(speaker));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Dialogue text cannot be empty.", nameof(text));

        Id = id;
        Speaker = speaker;
        Role = role ?? string.Empty;
        Text = text;
        this.choices = choices != null
            ? new List<NarrativeDialogueChoice>(choices)
            : new List<NarrativeDialogueChoice>();
    }
}

public sealed class NarrativeDialogueDefinition
{
    private readonly Dictionary<string, NarrativeDialogueNode> nodes;

    public string Id { get; }
    public string StartNodeId { get; }

    public NarrativeDialogueDefinition(
        string id,
        string startNodeId,
        params NarrativeDialogueNode[] nodes)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Dialogue id cannot be empty.", nameof(id));

        if (string.IsNullOrWhiteSpace(startNodeId))
            throw new ArgumentException("Dialogue start node id cannot be empty.", nameof(startNodeId));

        Id = id;
        StartNodeId = startNodeId;
        this.nodes = new Dictionary<string, NarrativeDialogueNode>();

        if (nodes == null || nodes.Length == 0)
            throw new ArgumentException("Dialogue must contain at least one node.", nameof(nodes));

        foreach (NarrativeDialogueNode node in nodes)
        {
            if (node == null)
                throw new ArgumentException("Dialogue cannot contain null nodes.", nameof(nodes));

            if (this.nodes.ContainsKey(node.Id))
            {
                throw new ArgumentException(
                    "Duplicate dialogue node id: " + node.Id,
                    nameof(nodes));
            }

            this.nodes.Add(node.Id, node);
        }

        if (!this.nodes.ContainsKey(StartNodeId))
        {
            throw new ArgumentException(
                "Dialogue start node does not exist: " + StartNodeId,
                nameof(startNodeId));
        }

        ValidateTransitions();
    }

    public NarrativeDialogueNode GetNode(string nodeId)
    {
        NarrativeDialogueNode node;
        if (!nodes.TryGetValue(nodeId, out node))
            throw new InvalidOperationException("Dialogue node does not exist: " + nodeId);

        return node;
    }

    private void ValidateTransitions()
    {
        foreach (NarrativeDialogueNode node in nodes.Values)
        {
            foreach (NarrativeDialogueChoice choice in node.Choices)
            {
                if (choice == null)
                    throw new ArgumentException("Dialogue cannot contain null choices.");

                if (choice.EndsDialogue)
                    continue;

                if (!nodes.ContainsKey(choice.NextNodeId))
                {
                    throw new ArgumentException(
                        "Dialogue node '" + node.Id +
                        "' points to missing node '" + choice.NextNodeId + "'.");
                }
            }
        }
    }
}

public sealed class NarrativeDialogueSession
{
    private NarrativeDialogueDefinition definition;

    public bool IsActive { get; private set; }
    public NarrativeDialogueNode CurrentNode { get; private set; }

    public void Start(NarrativeDialogueDefinition dialogue)
    {
        if (dialogue == null)
            throw new ArgumentNullException(nameof(dialogue));

        definition = dialogue;
        CurrentNode = definition.GetNode(definition.StartNodeId);
        IsActive = true;
    }

    public bool SelectChoice(int choiceIndex)
    {
        if (!IsActive || CurrentNode == null)
            throw new InvalidOperationException("No active dialogue session.");

        if (choiceIndex < 0 || choiceIndex >= CurrentNode.Choices.Count)
            throw new ArgumentOutOfRangeException(nameof(choiceIndex));

        NarrativeDialogueChoice choice = CurrentNode.Choices[choiceIndex];
        if (choice.EndsDialogue)
        {
            End();
            return false;
        }

        CurrentNode = definition.GetNode(choice.NextNodeId);
        return true;
    }

    public void End()
    {
        IsActive = false;
        CurrentNode = null;
        definition = null;
    }
}
