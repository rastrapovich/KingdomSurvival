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
    public void Choice_CanBranchToDifferentNodes()
    {
        NarrativeDialogueSession session = new NarrativeDialogueSession();
        session.Start(CreateDialogue());

        session.SelectChoice(1);

        Assert.AreEqual("right", session.CurrentNode.Id);
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
