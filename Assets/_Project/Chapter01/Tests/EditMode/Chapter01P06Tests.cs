using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P06 — N05 «Мокрый чертёж» (осмотр + выбор ремонта без проверки) и N06
// («первые симптомы»). Оба диалога построены по production-правилу "одна
// реплика = один шаг": каждый node несёт не больше одного textBlock,
// шаги соединены DialogueChoiceKind.Continue ("…"), настоящий выбор —
// только там, где герой решает (ulyana_cost2 в N05). Ветвление N06 по
// RepairOld/RepairNew решается условными Continue-выборами на стартовом
// узле, а не условными textBlocks внутри одного узла. Против настоящего
// KingdomSurvivalDialogues.asset через Resources.Load — тот же подход,
// что Chapter01FloodTests.cs.
public sealed class Chapter01P06Tests
{
    private const string ChoiceOld = "chapter01.node.05_choice_old";
    private const string ChoiceNew = "chapter01.node.05_choice_new";
    private const string D05DecisionNodeId = "chapter01.node.05.ulyana_cost2";

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

    // Кликает единственный доступный Continue, пока не дойдёт до узла с
    // настоящим выбором (>1 варианта) или до узла, чей единственный
    // вариант — не Continue (например, Exit). Список посещённых NodeId
    // собирается по пути — нужен, чтобы доказать, что ветка N06 показывает
    // только свой контент и никогда чужой.
    private static NarrativeDialogueView AdvanceThroughContinues(
        NarrativeDialogueRuntimeSession session, NarrativeDialogueView view, List<string> visitedNodeIds = null)
    {
        visitedNodeIds?.Add(view.NodeId);

        while (view.AvailableChoices.Count == 1 && view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue)
        {
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            Assert.IsFalse(result.DialogueEnded, "Continue не должен завершать диалог напрямую.");
            view = result.View;
            visitedNodeIds?.Add(view.NodeId);
        }

        return view;
    }

    private static NarrativeStateData RunD05(string repairChoiceId, out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtRepairPlan();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D05, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        view = AdvanceThroughContinues(session, view);
        Assert.AreEqual(D05DecisionNodeId, view.NodeId, "Цепочка Continue должна довести ровно до узла настоящего решения.");
        Assert.AreEqual(2, view.AvailableChoices.Count, "У решения о ремонте должно быть ровно два варианта.");

        NarrativeDialogueSelectionResult afterChoice = session.SelectChoice(repairChoiceId);
        Assert.IsFalse(afterChoice.DialogueEnded, "Выбор ремонта должен вести в итоговую цепочку ветки, а не завершать сцену.");

        NarrativeDialogueView outcomeView = AdvanceThroughContinues(session, afterChoice.View);
        Assert.AreEqual(1, outcomeView.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Exit, outcomeView.AvailableChoices[0].Kind);

        finalResult = session.SelectChoice(outcomeView.AvailableChoices[0].ChoiceId);
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

    // Проходит N06 целиком, возвращая список посещённых NodeId (ровно один
    // textBlock на узел, так что NodeId однозначно определяет показанный контент).
    private static List<string> RunD06(NarrativeStateData state, out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D06, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        List<string> visitedNodeIds = new List<string>();
        NarrativeDialogueView finalView = AdvanceThroughContinues(session, view, visitedNodeIds);

        Assert.AreEqual(1, finalView.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Exit, finalView.AvailableChoices[0].Kind);

        finalResult = session.SelectChoice(finalView.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);
        return visitedNodeIds;
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
        // Это вопрос "какой путь выбрать", а не "может ли герой так
        // сделать" — оба настоящих варианта обычные, промежуточные — Continue.
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
    public void D05_RealDecisionNode_HasNoContinueChoices()
    {
        // Перед настоящим решением героя дополнительный Continue не нужен
        // (инструкция по DialogueChoiceKind.Continue, §8): либо это шаг
        // подачи текста, либо решение игрока — не одновременно.
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D05);
        DialogueNodeData decisionNode = FindNode(dialogue, D05DecisionNodeId);
        Assert.IsNotNull(decisionNode);
        Assert.AreEqual(2, decisionNode.Choices.Count);

        foreach (DialogueChoiceData choice in decisionNode.Choices)
        {
            Assert.AreNotEqual(DialogueChoiceKind.Continue, choice.Kind);
            Assert.AreEqual(DialogueChoiceKind.Normal, choice.Kind);
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
    public void D06_OldBranch_VisitsOnlyOldNodes_AndSetsExpectedFlags()
    {
        NarrativeStateData state = NewStateAfterRepairDecision(repairOld: true);

        List<string> visited = RunD06(state, out NarrativeDialogueSelectionResult finalResult);

        Assert.Contains("chapter01.node.06", visited);
        Assert.Contains("chapter01.node.06.old_01", visited);
        Assert.Contains("chapter01.node.06.old_05", visited);
        Assert.Contains("chapter01.node.06.old_10", visited);

        // Old-ветка не должна показывать ни одного узла new-ветки —
        // высохший седьмой рукав / нервный скот (§19 инструкции P06).
        foreach (string nodeId in visited)
            Assert.IsFalse(nodeId.StartsWith("chapter01.node.06.new_"), "Old-ветка зашла на узел new-ветки: " + nodeId);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairCompleted));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.WaterWrongActive));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.WaterFlowIsWrong));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D06_NewBranch_VisitsOnlyNewNodes_AndSetsExpectedFlags()
    {
        NarrativeStateData state = NewStateAfterRepairDecision(repairOld: false);

        List<string> visited = RunD06(state, out NarrativeDialogueSelectionResult finalResult);

        Assert.Contains("chapter01.node.06", visited);
        Assert.Contains("chapter01.node.06.new_01", visited);
        Assert.Contains("chapter01.node.06.new_06", visited);
        Assert.Contains("chapter01.node.06.new_12", visited);

        // New-ветка не должна показывать ни одного узла old-ветки —
        // ночные толчки мельничного колеса (§19 инструкции P06).
        foreach (string nodeId in visited)
            Assert.IsFalse(nodeId.StartsWith("chapter01.node.06.old_"), "New-ветка зашла на узел old-ветки: " + nodeId);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.RepairCompleted));
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.WaterWrongActive));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.WaterFlowIsWrong));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D06_DoesNotGrant_InvestigationKnowledge_Ahead_Of_P07()
    {
        // N06 даёт только "есть проблема" (water_flow_is_wrong), а не
        // улики расследования P07.
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

    // --- Presentation-структура: правило "одна реплика = один шаг" ---

    [TestCase(Chapter01Ids.Dialogues.D05)]
    [TestCase(Chapter01Ids.Dialogues.D06)]
    public void EveryNode_HasAtMostOneTextBlock(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);

        foreach (DialogueNodeData node in dialogue.Nodes)
            Assert.LessOrEqual(node.TextBlocks.Count, 1, dialogueId + "/" + node.Id);
    }

    [TestCase(Chapter01Ids.Dialogues.D05)]
    [TestCase(Chapter01Ids.Dialogues.D06)]
    public void ContinueChoices_AreStructurallySound(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);

        HashSet<string> nodeIds = new HashSet<string>();
        foreach (DialogueNodeData n in dialogue.Nodes)
            nodeIds.Add(n.Id);

        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            foreach (DialogueChoiceData choice in node.Choices)
            {
                if (choice.Kind != DialogueChoiceKind.Continue)
                    continue;

                string where = dialogueId + "/" + node.Id;
                Assert.IsFalse(choice.IsActiveCheck, where);
                Assert.IsFalse(choice.IsExit, where);
                Assert.IsFalse(choice.EndsDialogue, where);
                Assert.AreEqual(0, choice.SuccessEffects.Count, where + ": Continue не должен нести эффекты успеха.");
                Assert.AreEqual(0, choice.FailureEffects.Count, where + ": Continue не должен нести эффекты провала.");
                Assert.IsTrue(nodeIds.Contains(choice.NextNodeId), where + " -> отсутствующий узел '" + choice.NextNodeId + "'.");
            }
        }
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

    [Test]
    public void D06_ConflictingRepairFlags_HasNoAvailableContinue()
    {
        // §9 инструкции P06-T03: конфликтное состояние не должно нормально
        // запускаться — оба Continue на стартовом узле гейтятся Negate
        // противоположного флага, поэтому ни один не станет доступен.
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtRepairPlan();
        state.SetFlag(Chapter01Ids.Flags.DamInspected);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.RepairNew);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(database, Chapter01Ids.Dialogues.D06, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        Assert.IsEmpty(view.AvailableChoices, "Конфликтное состояние не должно давать доступный Continue.");
    }
}
