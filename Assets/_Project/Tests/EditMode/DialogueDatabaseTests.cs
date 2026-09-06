using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

public sealed class DialogueDatabaseTests
{
    [Test]
    public void DefaultDatabase_Loads_PrototypeDialogue()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);

        Assert.IsNotNull(database);
        Assert.IsNotNull(database.FindDialogue("prototype_miller"));
        Assert.IsNotNull(database.FindSpeaker("miller"));
    }

    [Test]
    public void DefaultDatabase_Has_No_Validation_Issues()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        List<string> issues = new List<string>();
        database.CollectValidationIssues(issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void RuntimeFactory_Builds_Prototype_And_Preserves_SpeakerId()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeDialogueDefinition definition;
        string error;
        bool success = database.TryBuildRuntime("prototype_miller", out definition, out error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(definition);
        NarrativeDialogueNode opening = definition.GetNode("opening");
        Assert.AreEqual("miller", opening.SpeakerId);
        Assert.AreEqual("Мельник", opening.Speaker);
        Assert.AreEqual("details", opening.Choices[0].NextNodeId);
    }
}
