using System;
using NUnit.Framework;

public sealed class NarrativeDialogueTests
{
    [Test]
    public void Session_StartsAtConfiguredNode()
    {
        NarrativeDialogueDefinition dialogue = CreateDialogue();
        NarrativeDialogueSession session = new NarrativeDialogueSession();

        session.Start(dialogue);

        Assert.IsTrue(session.IsActive);
        Assert.AreEqual("start", session.CurrentNode.Id);
    }

    [Test]
    public void Session_History_StartsWithOpeningSpeakerLine()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();

        session.Start(CreateDialogue());

        Assert.AreEqual(1, session.History.Count);
        Assert.AreEqual(NarrativeDialogueHistoryEntryKind.Speaker, session.History[0].Kind);
        Assert.AreEqual("Start", session.History[0].Text);
    }

    [Test]
    public void Choice_CanBranchToDifferentNodes()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();
        session.Start(CreateDialogue());

        session.SelectChoice(1);

        Assert.AreEqual("right", session.CurrentNode.Id);
    }

    [Test]
    public void Choice_AppendsPlayerAnswerAndNextSpeakerLineToHistory()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();
        session.Start(CreateDialogue());

        session.SelectChoice(0);

        Assert.AreEqual(3, session.History.Count);
        Assert.AreEqual(NarrativeDialogueHistoryEntryKind.PlayerChoice, session.History[1].Kind);
        Assert.AreEqual("Left", session.History[1].Text);
        Assert.AreEqual(NarrativeDialogueHistoryEntryKind.Speaker, session.History[2].Kind);
        Assert.AreEqual("Left", session.History[2].Text);
    }

    [Test]
    public void DifferentBranches_CanConverge()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();
        session.Start(CreateDialogue());

        session.SelectChoice(0);
        Assert.AreEqual("left", session.CurrentNode.Id);

        session.SelectChoice(0);
        Assert.AreEqual("common", session.CurrentNode.Id);
    }

    [Test]
    public void ExitChoice_EndsDialogue()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();
        session.Start(CreateDialogue());

        bool continues = session.SelectChoice(2);

        Assert.IsFalse(continues);
        Assert.IsFalse(session.IsActive);
        Assert.IsNull(session.CurrentNode);
        Assert.AreEqual(2, session.History.Count);
        Assert.AreEqual("Exit", session.History[1].Text);
    }

    [Test]
    public void Definition_RejectsBrokenTransition()
    {
        Assert.Throws<ArgumentException>(() =>
            new NarrativeDialogueDefinition(
                "broken",
                "start",
                new NarrativeDialogueNode(
                    "start",
                    "Speaker",
                    string.Empty,
                    "Text",
                    new NarrativeDialogueChoice("Broken", "missing"))));
    }

    private static NarrativeDialogueDefinition CreateDialogue()
    {
        return new NarrativeDialogueDefinition(
            "test",
            "start",
            new NarrativeDialogueNode(
                "start",
                "Speaker",
                "Role",
                "Start",
                new NarrativeDialogueChoice("Left", "left"),
                new NarrativeDialogueChoice("Right", "right"),
                NarrativeDialogueChoice.Exit("Exit")),
            new NarrativeDialogueNode(
                "left",
                "Speaker",
                "Role",
                "Left",
                new NarrativeDialogueChoice("Continue", "common")),
            new NarrativeDialogueNode(
                "right",
                "Speaker",
                "Role",
                "Right",
                new NarrativeDialogueChoice("Continue", "common")),
            new NarrativeDialogueNode(
                "common",
                "Speaker",
                "Role",
                "Common",
                NarrativeDialogueChoice.Exit("Done")));
    }
}
