using System;
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
    public void RuntimeSession_Starts_Prototype_And_Preserves_Speaker()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        NarrativeDialogueView view;
        string error;
        bool success = session.Start(
            database,
            "prototype_miller",
            new HeroProfileData(),
            new NarrativeStateData(),
            out view,
            out error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(view);
        Assert.AreEqual("opening", view.NodeId);
        Assert.That(view.VisibleTextBlocks, Is.Not.Empty);
        Assert.AreEqual("miller", view.VisibleTextBlocks[0].SpeakerId);
        Assert.AreEqual("Мельник", view.VisibleTextBlocks[0].SpeakerDisplayName);
    }

    [Test]
    public void DefaultPrototype_Demonstrates_Every_Condition_Type_And_Group_Logic()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        DialogueDefinitionData dialogue = database.FindDialogue("prototype_miller");
        Assert.IsNotNull(dialogue);

        HashSet<NarrativeConditionType> foundTypes = new HashSet<NarrativeConditionType>();
        bool hasAllGroupWithSeveralConditions = false;
        bool hasAnyGroupWithSeveralConditions = false;
        bool hasNegatedCondition = false;

        for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
        {
            DialogueNodeData node = dialogue.Nodes[nodeIndex];
            for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
            {
                NarrativeConditionGroup group = node.Choices[choiceIndex].Conditions;
                if (group.Conditions.Count > 1 && group.Combinator == NarrativeConditionCombinator.All)
                    hasAllGroupWithSeveralConditions = true;
                if (group.Conditions.Count > 1 && group.Combinator == NarrativeConditionCombinator.Any)
                    hasAnyGroupWithSeveralConditions = true;

                for (int conditionIndex = 0; conditionIndex < group.Conditions.Count; conditionIndex++)
                {
                    NarrativeCondition condition = group.Conditions[conditionIndex];
                    foundTypes.Add(condition.Type);
                    if (condition.Negate)
                        hasNegatedCondition = true;
                }
            }
        }

        NarrativeConditionType[] everyType =
            (NarrativeConditionType[])Enum.GetValues(typeof(NarrativeConditionType));

        Assert.That(foundTypes, Is.EquivalentTo(everyType));
        Assert.IsTrue(hasAllGroupWithSeveralConditions);
        Assert.IsTrue(hasAnyGroupWithSeveralConditions);
        Assert.IsTrue(hasNegatedCondition);
    }

    [Test]
    public void DefaultPrototype_Has_Saved_Graph_Positions()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database);

        DialogueDefinitionData dialogue = database.FindDialogue("prototype_miller");
        Assert.IsNotNull(dialogue);
        Assert.That(dialogue.Nodes.Count, Is.GreaterThan(1));

        HashSet<Vector2> positions = new HashSet<Vector2>();
        for (int i = 0; i < dialogue.Nodes.Count; i++)
        {
            DialogueNodeData node = dialogue.Nodes[i];
            Assert.IsTrue(node.HasEditorPosition, "Нет editor-позиции у узла " + node.Id);
            positions.Add(node.EditorPosition);
        }

        Assert.That(positions.Count, Is.EqualTo(dialogue.Nodes.Count));
    }
}
