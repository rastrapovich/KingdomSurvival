using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P13 — финальный Совет Дома: gate после физического возвращения,
// трёхветвевой D17, взаимоисключающие исходы и итоговая Хроника.
public sealed class Chapter01P13Tests
{
    private const string DecisionNodeId = "chapter01.node.17_decision";
    private const string OldOrderChoiceId = "chapter01.node.17_decision_old_order";
    private const string NewOrderChoiceId = "chapter01.node.17_decision_new_order";
    private const string WaterForHomeChoiceId = "chapter01.node.17_decision_water_for_home";

    private static DialogueDatabaseAsset LoadDatabase()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    private static GameState NewGameState(int seed = 913000)
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(seed);
        return gameState;
    }

    private static void MakeCouncilReady(NarrativeStateData state)
    {
        state.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        state.AddKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldAgreement);
    }

    private static NarrativeDialogueRuntimeSession StartCouncil(
        NarrativeStateData state,
        out NarrativeDialogueView view)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            LoadDatabase(),
            Chapter01Ids.Dialogues.D17,
            new HeroProfileData(),
            state,
            out view,
            out string error,
            new List<string>(),
            new List<string>(),
            913000,
            1);
        Assert.IsTrue(started, error);
        return session;
    }

    private static NarrativeDialogueChoiceView FindChoice(
        NarrativeDialogueView view,
        string choiceId)
    {
        for (int i = 0; i < view.AvailableChoices.Count; i++)
        {
            NarrativeDialogueChoiceView choice = view.AvailableChoices[i];
            if (choice.ChoiceId == choiceId)
                return choice;
        }

        return null;
    }

    private static bool HasRevealedBlock(NarrativeDialogueView view, string blockId)
    {
        for (int i = 0; i < view.VisibleTextBlocks.Count; i++)
        {
            NarrativeDialogueVisibleBlock block = view.VisibleTextBlocks[i];
            if (block.BlockId == blockId && block.IsTextRevealed)
                return true;
        }

        return false;
    }

    private static NarrativeDialogueView AdvanceToDecision(
        NarrativeDialogueRuntimeSession session,
        NarrativeDialogueView view)
    {
        string[] continueIds =
        {
            "chapter01.node.17_continue_opening",
            "chapter01.node.17_opening_continue",
            "chapter01.node.17_positions_continue"
        };

        for (int i = 0; i < continueIds.Length; i++)
        {
            NarrativeDialogueChoiceView choice = FindChoice(view, continueIds[i]);
            Assert.IsNotNull(choice, "Не найден переход " + continueIds[i]);
            NarrativeDialogueSelectionResult result = session.SelectChoice(choice.ChoiceId);
            Assert.IsFalse(result.DialogueEnded);
            view = result.View;
        }

        Assert.AreEqual(DecisionNodeId, view.NodeId);
        return view;
    }

    private static NarrativeDialogueView SelectOutcome(
        NarrativeStateData state,
        string choiceId)
    {
        NarrativeDialogueRuntimeSession session = StartCouncil(state, out NarrativeDialogueView view);
        view = AdvanceToDecision(session, view);
        NarrativeDialogueChoiceView choice = FindChoice(view, choiceId);
        Assert.IsNotNull(choice);

        NarrativeDialogueSelectionResult result = session.SelectChoice(choice.ChoiceId);
        Assert.IsFalse(result.DialogueEnded, "Сначала должен быть показан отдельный aftermath-узел.");
        return result.View;
    }

    [Test]
    public void Council_CannotOpenBeforeReturnedHome()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.AddKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldAgreement);

        Assert.IsFalse(Chapter01StoryDirector.CanOpenFinalCouncil(state));
    }

    [Test]
    public void Council_RequiresSharedWaterKnowledge()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldAgreement);

        Assert.IsFalse(Chapter01StoryDirector.CanOpenFinalCouncil(state));

        state.AddKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);
        Assert.IsTrue(Chapter01StoryDirector.CanOpenFinalCouncil(state));
    }

    [Test]
    public void Council_AlsoRequiresOldAgreementKnowledge()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        state.AddKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);

        Assert.IsFalse(Chapter01StoryDirector.CanOpenFinalCouncil(state));
    }

    [Test]
    public void Council_StartDoesNotCompleteChapter()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);

        StartCouncil(state, out NarrativeDialogueView view);

        Assert.AreEqual(Chapter01Ids.Nodes.N17, view.NodeId);
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.CouncilCompleted));
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.Completed));
        Assert.IsFalse(Chapter01CouncilOutcomeResolver.HasOutcome(state));
    }

    [Test]
    public void Council_HasExactlyThreeMainOutcomes()
    {
        DialogueDefinitionData dialogue = LoadDatabase().FindDialogue(Chapter01Ids.Dialogues.D17);
        Assert.IsNotNull(dialogue);

        DialogueNodeData decision = null;
        for (int i = 0; i < dialogue.Nodes.Count; i++)
        {
            if (dialogue.Nodes[i].Id == DecisionNodeId)
            {
                decision = dialogue.Nodes[i];
                break;
            }
        }

        Assert.IsNotNull(decision);
        Assert.AreEqual(3, decision.Choices.Count);
        Assert.AreEqual(OldOrderChoiceId, decision.Choices[0].ChoiceId);
        Assert.AreEqual(NewOrderChoiceId, decision.Choices[1].ChoiceId);
        Assert.AreEqual(WaterForHomeChoiceId, decision.Choices[2].ChoiceId);
        for (int i = 0; i < decision.Choices.Count; i++)
        {
            Assert.AreEqual(DialogueChoiceKind.Normal, decision.Choices[i].Kind);
            Assert.IsFalse(decision.Choices[i].IsActiveCheck);
            Assert.IsFalse(decision.Choices[i].IsExit);
        }
    }

    [Test]
    public void CouncilDialogue_PassesDatabaseValidation()
    {
        List<string> issues = new List<string>();
        LoadDatabase().CollectValidationIssuesForDialogue(Chapter01Ids.Dialogues.D17, issues);

        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void OldOrder_SetsCorrectOutcome()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);

        SelectOutcome(state, OldOrderChoiceId);

        AssertOutcome(state, Chapter01CouncilOutcome.OldOrderRestored, debtOpen: false);
    }

    [Test]
    public void NewOrder_SetsCorrectOutcome()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);

        SelectOutcome(state, NewOrderChoiceId);

        AssertOutcome(state, Chapter01CouncilOutcome.NewOrderCreated, debtOpen: false);
    }

    [Test]
    public void WaterForHome_SetsCorrectOutcomeAndDebt()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);

        SelectOutcome(state, WaterForHomeChoiceId);

        AssertOutcome(state, Chapter01CouncilOutcome.WaterKeptForHome, debtOpen: true);
    }

    [Test]
    public void CouncilOutcomes_AreMutuallyExclusive()
    {
        string[] choices = { OldOrderChoiceId, NewOrderChoiceId, WaterForHomeChoiceId };
        for (int i = 0; i < choices.Length; i++)
        {
            NarrativeStateData state = new NarrativeStateData();
            MakeCouncilReady(state);
            SelectOutcome(state, choices[i]);
            Assert.That(Chapter01CouncilOutcomeResolver.ValidateOutcome(state), Is.Empty);
        }

        NarrativeStateData invalid = new NarrativeStateData();
        invalid.SetFlag(Chapter01Ids.Flags.CouncilOldOrderRestored);
        invalid.SetFlag(Chapter01Ids.Flags.CouncilNewOrderCreated);
        Assert.That(Chapter01CouncilOutcomeResolver.ValidateOutcome(invalid), Is.Not.Empty);
    }

    [TestCase(Chapter01Ids.Flags.RepairOld)]
    [TestCase(Chapter01Ids.Flags.RepairNew)]
    public void PriorRepair_DoesNotRemoveCouncilDirections(string repairFlag)
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);
        state.SetFlag(repairFlag);

        NarrativeDialogueRuntimeSession session = StartCouncil(state, out NarrativeDialogueView view);
        view = AdvanceToDecision(session, view);

        Assert.AreEqual(3, view.AvailableChoices.Count);
        Assert.IsNotNull(FindChoice(view, OldOrderChoiceId));
        Assert.IsNotNull(FindChoice(view, NewOrderChoiceId));
        Assert.IsNotNull(FindChoice(view, WaterForHomeChoiceId));
    }

    [Test]
    public void CompletedCouncil_DoesNotOpenAgain()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);
        SelectOutcome(state, OldOrderChoiceId);

        Assert.IsFalse(Chapter01StoryDirector.CanOpenFinalCouncil(state));
        Assert.IsNull(Chapter01StoryDirector.GetNextDialogueId(state));
    }

    [Test]
    public void Journal_ShowsCouncilAfterHomecoming()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        MakeCouncilReady(gameState.Narrative);

        JournalGoalViewData goal = Chapter01JournalProvider.Build(gameState)[0];

        Assert.AreEqual(JournalGoalState.Active, goal.State);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":final_council", goal.RevisionId);
        StringAssert.Contains("Решить на Совете", goal.CurrentStep);
    }

    [TestCase(Chapter01CouncilOutcome.OldOrderRestored, ":old_order", "восстановить общий порядок")]
    [TestCase(Chapter01CouncilOutcome.NewOrderCreated, ":new_order", "создать новый порядок")]
    [TestCase(Chapter01CouncilOutcome.WaterKeptForHome, ":water_for_home", "оставил воду себе")]
    public void Journal_RecordsSelectedOutcome(
        Chapter01CouncilOutcome outcome,
        string revisionSuffix,
        string expectedText)
    {
        GameState gameState = NewGameState(913100 + (int)outcome);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        MakeCouncilReady(gameState.Narrative);
        SetOutcomeFlagsDirectly(gameState.Narrative, outcome);

        JournalGoalViewData goal = Chapter01JournalProvider.Build(gameState)[0];

        Assert.AreEqual(JournalGoalState.Completed, goal.State);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + revisionSuffix, goal.RevisionId);
        StringAssert.Contains(expectedText, goal.CurrentStep);
    }

    [TestCase(
        Chapter01ReturnFlow.ContinueBranchEffectId,
        "chapter01.node.17_positions_follow_trace",
        "chapter01.node.17_positions_return_now")]
    [TestCase(
        Chapter01ReturnFlow.ReturnNowBranchEffectId,
        "chapter01.node.17_positions_return_now",
        "chapter01.node.17_positions_follow_trace")]
    public void Council_ReadsPersistedN14HalfChoiceWithoutDuplicateFlag(
        string effectId,
        string expectedBlockId,
        string unexpectedBlockId)
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);
        state.MarkEffectApplied(effectId);

        NarrativeDialogueRuntimeSession session = StartCouncil(state, out NarrativeDialogueView view);
        NarrativeDialogueChoiceView first = FindChoice(view, "chapter01.node.17_continue_opening");
        view = session.SelectChoice(first.ChoiceId).View;
        NarrativeDialogueChoiceView second = FindChoice(view, "chapter01.node.17_opening_continue");
        view = session.SelectChoice(second.ChoiceId).View;

        Assert.IsTrue(HasRevealedBlock(view, expectedBlockId));
        Assert.IsFalse(HasRevealedBlock(view, unexpectedBlockId));
    }

    [Test]
    public void CouncilOutcome_SurvivesJsonRoundTrip()
    {
        NarrativeStateData state = new NarrativeStateData();
        MakeCouncilReady(state);
        SelectOutcome(state, WaterForHomeChoiceId);

        string json = JsonUtility.ToJson(state);
        NarrativeStateData restored = JsonUtility.FromJson<NarrativeStateData>(json);

        AssertOutcome(restored, Chapter01CouncilOutcome.WaterKeptForHome, debtOpen: true);
        Assert.IsFalse(Chapter01StoryDirector.CanOpenFinalCouncil(restored));
    }

    private static void AssertOutcome(
        NarrativeStateData state,
        Chapter01CouncilOutcome expected,
        bool debtOpen)
    {
        Assert.AreEqual(expected, Chapter01CouncilOutcomeResolver.GetOutcome(state));
        Assert.AreEqual(expected == Chapter01CouncilOutcome.OldOrderRestored,
            state.HasFlag(Chapter01Ids.Flags.CouncilOldOrderRestored));
        Assert.AreEqual(expected == Chapter01CouncilOutcome.NewOrderCreated,
            state.HasFlag(Chapter01Ids.Flags.CouncilNewOrderCreated));
        Assert.AreEqual(expected == Chapter01CouncilOutcome.WaterKeptForHome,
            state.HasFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome));
        Assert.AreEqual(debtOpen, state.HasFlag(Chapter01Ids.Flags.DownstreamDebtOpen));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.CouncilCompleted));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.Completed));
        Assert.That(Chapter01CouncilOutcomeResolver.ValidateOutcome(state), Is.Empty);
    }

    private static void SetOutcomeFlagsDirectly(
        NarrativeStateData state,
        Chapter01CouncilOutcome outcome)
    {
        if (outcome == Chapter01CouncilOutcome.OldOrderRestored)
            state.SetFlag(Chapter01Ids.Flags.CouncilOldOrderRestored);
        else if (outcome == Chapter01CouncilOutcome.NewOrderCreated)
            state.SetFlag(Chapter01Ids.Flags.CouncilNewOrderCreated);
        else if (outcome == Chapter01CouncilOutcome.WaterKeptForHome)
        {
            state.SetFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome);
            state.SetFlag(Chapter01Ids.Flags.DownstreamDebtOpen);
        }

        state.SetFlag(Chapter01Ids.Flags.CouncilCompleted);
        state.SetFlag(Chapter01Ids.Flags.Completed);
    }
}
