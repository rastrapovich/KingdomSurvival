using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P06 — N05 «Мокрый чертёж» (осмотр + выбор ремонта без проверки) и N06
// («первые симптомы» — одна ветка на условных textBlocks вместо двух
// диалогов). Против настоящего KingdomSurvivalDialogues.asset через
// Resources.Load — тот же подход, что Chapter01FloodTests.cs.
public sealed class Chapter01P06Tests
{
    private const string ChoiceOld = "chapter01.node.05_choice_old";
    private const string ChoiceNew = "chapter01.node.05_choice_new";

    private static DialogueDatabaseAsset LoadDatabase()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    private static DialogueNodeData FindNode(DialogueDefinitionData dialogue, string nodeId)
    {
        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            if (node.Id == nodeId)
                return node;
        }
        return null;
    }

    // Реалистичный предшествующий прогресс: N05 в игре достижим только
    // после N01–N04 (см. Chapter01StateTests.AfterFlood_N05IsAlwaysAvailable).
    private static NarrativeStateData NewStateAtRepairPlan()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        return state;
    }

    private static NarrativeStateData RunD05(string choiceId, out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtRepairPlan();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D05, new HeroProfileData(), state, out _, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueSelectionResult afterChoice = session.SelectChoice(choiceId);
        Assert.IsFalse(afterChoice.DialogueEnded, "Выбор ремонта должен вести в итоговый узел ветки, а не завершать сцену.");
        Assert.AreEqual(1, afterChoice.View.AvailableChoices.Count);

        finalResult = session.SelectChoice(afterChoice.View.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);
        return state;
    }

    // repairOld=true задаёт состояние "решение — старый ремонт" напрямую
    // флагами (без прохождения N05) — так тесты N06 не зависят от текста N05.
    private static NarrativeStateData NewStateAfterRepairDecision(bool repairOld)
    {
        NarrativeStateData state = NewStateAtRepairPlan();
        state.SetFlag(Chapter01Ids.Flags.DamInspected);
        state.SetFlag(repairOld ? Chapter01Ids.Flags.RepairOld : Chapter01Ids.Flags.RepairNew);
        return state;
    }

    private static NarrativeDialogueView RunD06(NarrativeStateData state, out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D06, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        finalResult = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);
        return view;
    }

    private static bool HasBlock(NarrativeDialogueView view, string blockId)
    {
        foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
        {
            if (block.BlockId == blockId)
                return true;
        }
        return false;
    }

    // --- P06-T01: N05 «Мокрый чертёж» ---

    [Test]
    public void D05_Dialogue_And_StartNode_Exist()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D05);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
        Assert.IsNotNull(FindNode(dialogue, dialogue.StartNodeId));
    }

    [Test]
    public void D05_Dialogue_Passes_Validation()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(Chapter01Ids.Dialogues.D05, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void D05_HasNoActiveChecks()
    {
        // Раздел 4 инструкции P06: это вопрос "какой путь выбрать", а не
        // "может ли герой так сделать" — оба варианта обычные.
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D05);

        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            foreach (DialogueChoiceData choice in node.Choices)
            {
                Assert.IsFalse(choice.IsActiveCheck, dialogue.Id + "/" + node.Id + ": " + choice.Text);
            }
        }
    }

    [Test]
    public void D05_OldChoice_SetsRepairOld_And_DamInspected_NotRepairNew()
    {
        NarrativeStateData state = RunD05(ChoiceOld, out _);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.DamInspected));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairOld));
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.RepairNew));
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.RepairCompleted), "N05 — только решение, ремонт ещё не завершён.");
    }

    [Test]
    public void D05_NewChoice_SetsRepairNew_And_DamInspected_NotRepairOld()
    {
        NarrativeStateData state = RunD05(ChoiceNew, out _);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.DamInspected));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairNew));
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.RepairOld));
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.RepairCompleted));
    }

    [TestCase(ChoiceOld)]
    [TestCase(ChoiceNew)]
    public void D05_AfterEitherBranch_StoryDirector_ReturnsD06(string choiceId)
    {
        NarrativeStateData state = RunD05(choiceId, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(finalResult.DialogueEnded);
        Assert.AreEqual(Chapter01Ids.Dialogues.D06, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    // --- P06-T02: N06 «первые симптомы» ---

    [Test]
    public void D06_Dialogue_And_StartNode_Exist()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D06);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
        Assert.IsNotNull(FindNode(dialogue, dialogue.StartNodeId));
    }

    [Test]
    public void D06_Dialogue_Passes_Validation()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(Chapter01Ids.Dialogues.D06, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void D06_OldBranch_ShowsOnlyOldSymptoms_AndSetsExpectedFlags()
    {
        NarrativeStateData state = NewStateAfterRepairDecision(repairOld: true);

        NarrativeDialogueView view = RunD06(state, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(HasBlock(view, "chapter01.node.06_common_opening"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_old_repaired"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_old_night"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_old_wheel"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_old_conclusion"));

        // Old-ветка не должна показывать симптомы new-ветки (высохший
        // седьмой рукав / нервный скот) — раздел 19 инструкции P06.
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_new_repaired"));
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_new_channel"));
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_new_conclusion"));

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairCompleted));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.WaterWrongActive));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.WaterFlowIsWrong));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D06_NewBranch_ShowsOnlyNewSymptoms_AndSetsExpectedFlags()
    {
        NarrativeStateData state = NewStateAfterRepairDecision(repairOld: false);

        NarrativeDialogueView view = RunD06(state, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(HasBlock(view, "chapter01.node.06_common_opening"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_new_repaired"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_new_channel"));
        Assert.IsTrue(HasBlock(view, "chapter01.node.06_new_conclusion"));

        // New-ветка не должна показывать симптомы old-ветки (ночные толчки
        // мельничного колеса) — раздел 19 инструкции P06.
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_old_repaired"));
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_old_night"));
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_old_wheel"));
        Assert.IsFalse(HasBlock(view, "chapter01.node.06_old_conclusion"));

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairCompleted));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.WaterWrongActive));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.WaterFlowIsWrong));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D06_DoesNotGrant_InvestigationKnowledge_Ahead_Of_P07()
    {
        // Раздел 21 инструкции: N06 даёт только "есть проблема"
        // (water_flow_is_wrong), а не улики расследования P07.
        NarrativeStateData oldState = NewStateAfterRepairDecision(repairOld: true);
        RunD06(oldState, out _);
        AssertNoInvestigationKnowledge(oldState);

        NarrativeStateData newState = NewStateAfterRepairDecision(repairOld: false);
        RunD06(newState, out _);
        AssertNoInvestigationKnowledge(newState);
    }

    private static void AssertNoInvestigationKnowledge(NarrativeStateData state)
    {
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.MillMovesAtWrongTime));
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel));
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.CattleAvoidOldBranch));
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.FishPatternChanged));
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.OldFord));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void D06_AfterEitherBranch_StoryDirector_ReturnsD07A(bool repairOld)
    {
        NarrativeStateData state = NewStateAfterRepairDecision(repairOld);
        RunD06(state, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(finalResult.DialogueEnded);
        Assert.AreEqual(Chapter01Ids.Dialogues.D07A, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    // --- P06-T03: взаимоисключение ремонта ---

    [Test]
    public void RepairOld_And_RepairNew_Are_Never_Both_Set_By_A_Real_D05_Path()
    {
        NarrativeStateData oldPath = RunD05(ChoiceOld, out _);
        Assert.IsFalse(oldPath.HasFlag(Chapter01Ids.Flags.RepairOld) && oldPath.HasFlag(Chapter01Ids.Flags.RepairNew));

        NarrativeStateData newPath = RunD05(ChoiceNew, out _);
        Assert.IsFalse(newPath.HasFlag(Chapter01Ids.Flags.RepairOld) && newPath.HasFlag(Chapter01Ids.Flags.RepairNew));
    }

    [Test]
    public void GetRepairChoice_RecognizesEachBranch_AndRejectsBothFlags()
    {
        NarrativeStateData oldState = new NarrativeStateData();
        oldState.SetFlag(Chapter01Ids.Flags.RepairOld);
        Assert.AreEqual(Chapter01RepairChoice.Old, Chapter01StoryDirector.GetRepairChoice(oldState));

        NarrativeStateData newState = new NarrativeStateData();
        newState.SetFlag(Chapter01Ids.Flags.RepairNew);
        Assert.AreEqual(Chapter01RepairChoice.New, Chapter01StoryDirector.GetRepairChoice(newState));

        NarrativeStateData emptyState = new NarrativeStateData();
        Assert.AreEqual(Chapter01RepairChoice.None, Chapter01StoryDirector.GetRepairChoice(emptyState));

        NarrativeStateData conflictState = new NarrativeStateData();
        conflictState.SetFlag(Chapter01Ids.Flags.RepairOld);
        conflictState.SetFlag(Chapter01Ids.Flags.RepairNew);
        Assert.Throws<System.InvalidOperationException>(() => Chapter01StoryDirector.GetRepairChoice(conflictState));
    }
}
