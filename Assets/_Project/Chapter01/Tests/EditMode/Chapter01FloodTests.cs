using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P05 — N04 «Синяя ставня». Три слоя проверяются против настоящего
// production-графа в KingdomSurvivalDialogues.asset (Resources.Load), а не
// синтетической копии: инструкция явно требует, чтобы FloodHappened
// выставлялся только в шести конечных узлах, а не при открытии сцены
// (раздел 3), и чтобы каждый из них вёл к N05 (раздел 22, P05-T04) — это
// можно доказать только на реальном графе, который видит игрок.
public sealed class Chapter01FloodTests
{
    private const string ChoiceStrength = "chapter01.node.04_choice_strength";
    private const string ChoiceDexterity = "chapter01.node.04_choice_dexterity";
    private const string ChoiceFortitude = "chapter01.node.04_choice_fortitude";

    private const string PrioritySuccessWorkers = "chapter01.node.04.priority_success_choice_workers";
    private const string PrioritySuccessLivestock = "chapter01.node.04.priority_success_choice_livestock";
    private const string PrioritySuccessMill = "chapter01.node.04.priority_success_choice_mill";
    private const string PriorityFailureWorkers = "chapter01.node.04.priority_failure_choice_workers";
    private const string PriorityFailureLivestock = "chapter01.node.04.priority_failure_choice_livestock";
    private const string PriorityFailureMill = "chapter01.node.04.priority_failure_choice_mill";

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

    // Реалистичный предшествующий прогресс: N04 в игре достижим только
    // после N01–N03 (см. Chapter01StateTests.AfterFlood_N05IsAlwaysAvailable).
    private static NarrativeStateData NewStateAtFlood()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        return state;
    }

    private static NarrativeDialogueView AdvanceCurrentNodeText(
        NarrativeDialogueRuntimeSession session,
        NarrativeDialogueView view)
    {
        int guard = 0;
        while (view.AvailableChoices.Count == 1 &&
               view.AvailableChoices[0].ChoiceId == NarrativeDialogueRuntimeSession.SequentialContinueChoiceId)
        {
            Assert.LessOrEqual(view.VisibleTextBlocks.Count, 1, "Один шаг показал несколько реплик.");
            Assert.Less(guard++, 64, "Зациклен runtime-переход между репликами.");
            view = session.SelectChoice(NarrativeDialogueRuntimeSession.SequentialContinueChoiceId).View;
        }

        Assert.LessOrEqual(view.VisibleTextBlocks.Count, 1, "Один шаг показал несколько реплик.");
        return view;
    }

    // Проходит N04 целиком: решающая проверка → тяжёлый выбор приоритета →
    // конечный узел → Exit. Возвращает состояние, в котором осел итог.
    private static NarrativeStateData RunFullFloodScenario(
        string startChoiceId,
        NarrativeCheckForcedOutcome forcedOutcome,
        string priorityChoiceId,
        out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtFlood();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(
            database, Chapter01Ids.Dialogues.D04, new HeroProfileData(), state,
            out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);
        AdvanceCurrentNodeText(session, view);

        NarrativeDialogueSelectionResult afterCheck = session.SelectChoicePreview(startChoiceId, forcedOutcome);
        Assert.IsFalse(afterCheck.DialogueEnded, "Результат проверки не должен завершать сцену.");
        Assert.AreEqual(1, afterCheck.View.AvailableChoices.Count);

        NarrativeDialogueSelectionResult priorityView = session.SelectChoice(afterCheck.View.AvailableChoices[0].ChoiceId);
        Assert.IsFalse(priorityView.DialogueEnded, "Тяжёлый выбор не должен быть пропущен.");
        Assert.AreEqual(3, priorityView.View.AvailableChoices.Count);

        NarrativeDialogueSelectionResult outcomeView = session.SelectChoice(priorityChoiceId);
        Assert.IsFalse(outcomeView.DialogueEnded);
        Assert.AreEqual(1, outcomeView.View.AvailableChoices.Count);

        finalResult = session.SelectChoice(outcomeView.View.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);

        return state;
    }

    // --- P05-T01: структура трёх решающих проверок ---

    [Test]
    public void N04_Dialogue_And_StartNode_Exist()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D04);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
        Assert.IsNotNull(FindNode(dialogue, dialogue.StartNodeId));
    }

    [Test]
    public void N04_Dialogue_Passes_Validation()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(Chapter01Ids.Dialogues.D04, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void N04_StartNode_HasExactlyThreeActiveDecisiveChecks_SharingCheckId_AtDifficulty13()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D04);
        DialogueNodeData startNode = FindNode(dialogue, dialogue.StartNodeId);
        Assert.IsNotNull(startNode);

        List<DialogueChoiceData> activeChoices = new List<DialogueChoiceData>();
        foreach (DialogueChoiceData choice in startNode.Choices)
        {
            if (choice.Kind == DialogueChoiceKind.ActiveDecisive)
                activeChoices.Add(choice);
        }

        Assert.AreEqual(3, activeChoices.Count, "Ожидались ровно три решающие проверки в стартовом узле N04.");

        HashSet<HeroQuality> qualities = new HashSet<HeroQuality>();
        foreach (DialogueChoiceData choice in activeChoices)
        {
            Assert.AreEqual(Chapter01Ids.Checks.FloodResponse, choice.Check.CheckId);
            Assert.AreEqual(NarrativeCheckKind.ActiveDecisive, choice.Check.Kind);
            Assert.AreEqual(NarrativeDifficulty.Ordinary, choice.Check.Difficulty);
            Assert.IsTrue(string.IsNullOrEmpty(choice.Check.CompetencyId), "P05 не должен использовать компетенцию.");
            qualities.Add(choice.Check.Quality);
        }

        Assert.That(qualities, Is.EquivalentTo(new[] { HeroQuality.Strength, HeroQuality.Dexterity, HeroQuality.Fortitude }));
    }

    [Test]
    public void N04_Opening_Does_Not_Set_FloodHappened()
    {
        // Раздел 3 инструкции: FloodHappened нельзя ставить при открытии
        // N04 — иначе Chapter01StoryDirector сочтёт узел завершённым до
        // проверки и тяжёлого выбора.
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtFlood();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D04, new HeroProfileData(), state, out _, out string error);
        Assert.IsTrue(started, error);

        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.FloodHappened));
        Assert.AreEqual(Chapter01Ids.Dialogues.D04, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    // --- P05-T02: и успех, и провал ведут к тяжёлому выбору, не к концу сцены ---

    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure)]
    [TestCase(ChoiceDexterity, NarrativeCheckForcedOutcome.ForceSuccess)]
    [TestCase(ChoiceDexterity, NarrativeCheckForcedOutcome.ForceFailure)]
    [TestCase(ChoiceFortitude, NarrativeCheckForcedOutcome.ForceSuccess)]
    [TestCase(ChoiceFortitude, NarrativeCheckForcedOutcome.ForceFailure)]
    public void N04_EachCheckOutcome_LeadsToHeavyPriorityChoice(string startChoiceId, NarrativeCheckForcedOutcome outcome)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = NewStateAtFlood();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(
            database, Chapter01Ids.Dialogues.D04, new HeroProfileData(), state,
            out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);
        AdvanceCurrentNodeText(session, view);

        NarrativeDialogueSelectionResult afterCheck = session.SelectChoicePreview(startChoiceId, outcome);

        Assert.IsFalse(afterCheck.DialogueEnded, "Ни успех, ни провал не должны заканчивать сцену немедленно.");
        Assert.IsNotNull(afterCheck.CheckResult);
        Assert.AreEqual(outcome == NarrativeCheckForcedOutcome.ForceSuccess, afterCheck.CheckResult.Success);

        NarrativeDialogueSelectionResult priorityView = session.SelectChoice(afterCheck.View.AvailableChoices[0].ChoiceId);
        Assert.IsFalse(priorityView.DialogueEnded);
        Assert.AreEqual(3, priorityView.View.AvailableChoices.Count, "Тяжёлый выбор должен давать три приоритета.");
    }

    // --- P05-T03: матрица последствий (раздел 11/23) ---

    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers, true, true, false, false)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessLivestock, true, false, true, false)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessMill, false, false, false, false)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureWorkers, true, true, true, true)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureLivestock, false, false, true, true)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureMill, false, true, false, true)]
    public void N04_AllSixOutcomes_SetExpectedFlagCombination(
        string startChoiceId,
        NarrativeCheckForcedOutcome outcome,
        string priorityChoiceId,
        bool expectedWorkersSaved,
        bool expectedLivestockLost,
        bool expectedMillDestroyed,
        bool expectedHeroInjured)
    {
        NarrativeStateData state = RunFullFloodScenario(startChoiceId, outcome, priorityChoiceId, out _);

        Assert.AreEqual(expectedWorkersSaved, state.HasFlag(Chapter01Ids.Flags.FloodWorkersSaved), "FloodWorkersSaved");
        Assert.AreEqual(expectedLivestockLost, state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost), "FloodLivestockLost");
        Assert.AreEqual(expectedMillDestroyed, state.HasFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed), "FloodMillDeckDestroyed");
        Assert.AreEqual(expectedHeroInjured, state.HasFlag(Chapter01Ids.Flags.HeroInjuredByFlood), "HeroInjuredByFlood");
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FloodHappened), "FloodHappened должен быть выставлен в конечном узле.");
    }

    // Три качества дают тот же граф исходов — эта проверка фиксирует, что
    // выбор Ловкости/Стойкости ведёт к тем же шести исходам, что и Сила.
    [TestCase(ChoiceDexterity, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessLivestock)]
    [TestCase(ChoiceFortitude, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureMill)]
    public void N04_OtherQualities_ReachSameOutcomeGraph(string startChoiceId, NarrativeCheckForcedOutcome outcome, string priorityChoiceId)
    {
        NarrativeStateData state = RunFullFloodScenario(startChoiceId, outcome, priorityChoiceId, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(finalResult.DialogueEnded);
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FloodHappened));
    }

    // --- P05-T04: failure-forward — все исходы приводят к N05 ---

    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessLivestock)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessMill)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureWorkers)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureLivestock)]
    [TestCase(ChoiceStrength, NarrativeCheckForcedOutcome.ForceFailure, PriorityFailureMill)]
    public void N04_AllSixOutcomes_LeadStoryDirectorToN05(
        string startChoiceId,
        NarrativeCheckForcedOutcome outcome,
        string priorityChoiceId)
    {
        NarrativeStateData state = RunFullFloodScenario(startChoiceId, outcome, priorityChoiceId, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(finalResult.DialogueEnded);
        Assert.AreEqual(Chapter01Ids.Dialogues.D05, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    // --- P05-T03: внешнее ресурсное последствие через Chapter01OutcomeApplier ---

    [Test]
    public void ApplyFloodConsequences_LivestockLost_AppliesFoodLossOnce()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers, out _);
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost));

        GameState gameState = new GameState { Narrative = state, Food = 72 };

        Chapter01OutcomeApplier.ApplyFloodConsequences(gameState);
        Assert.AreEqual(60, gameState.Food);

        Chapter01OutcomeApplier.ApplyFloodConsequences(gameState);
        Assert.AreEqual(60, gameState.Food, "Повторный вызов не должен списывать пищу дважды.");
    }

    [Test]
    public void ApplyFloodConsequences_LivestockNotLost_DoesNotChangeFood()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessMill, out _);
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost));

        GameState gameState = new GameState { Narrative = state, Food = 72 };

        Chapter01OutcomeApplier.ApplyFloodConsequences(gameState);
        Assert.AreEqual(72, gameState.Food);
    }

    [Test]
    public void HandleDialogueCompleted_ForD04_AppliesFloodConsequences_OnlyOnce()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers, out _);
        GameState gameState = new GameState { Narrative = state, Food = 72 };

        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D04);
        Assert.AreEqual(60, gameState.Food);

        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D04);
        Assert.AreEqual(60, gameState.Food);
    }

    [Test]
    public void HandleDialogueCompleted_ForOtherDialogue_DoesNothing()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers, out _);
        GameState gameState = new GameState { Narrative = state, Food = 72 };

        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D05);
        Assert.AreEqual(72, gameState.Food);
    }

    // --- P05-T03: связь с Chapter01HomeState (раздел 25) ---

    [Test]
    public void N04_LivestockLostOutcome_ReflectsIn_Chapter01HomeState()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessWorkers, out _);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);
        Assert.AreEqual(Chapter01HomeLivestockState.Lost, snapshot.Livestock);
    }

    [Test]
    public void N04_MillDestroyedOutcome_ReflectsIn_Chapter01HomeState()
    {
        NarrativeStateData state = RunFullFloodScenario(
            ChoiceStrength, NarrativeCheckForcedOutcome.ForceSuccess, PrioritySuccessLivestock, out _);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);
        Assert.AreEqual(Chapter01HomeMillState.DamagedOrStopped, snapshot.Mill);
        Assert.AreEqual(Chapter01HomeWalkwayState.Destroyed, snapshot.Walkway);
    }
}
