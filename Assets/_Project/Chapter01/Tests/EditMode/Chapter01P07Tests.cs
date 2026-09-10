using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P07 — N07A/N07B/N07C (свободный порядок расследования) и N08 (материальный
// след старой системы). Presentation-правило «одна реплика = один шаг»
// (DialogueChoiceKind.Continue) с одним явным исключением в N07C: три узла
// несут по два взаимоисключающих textBlock для старой/новой ветки ремонта
// вместо смешения говорящих. Нет счётчика улик — обязательные выводы N08
// завязаны на HasKnowledge/HasFlag двух независимых источников. Против
// настоящего KingdomSurvivalDialogues.asset через Resources.Load — тот же
// подход, что Chapter01FloodTests.cs/Chapter01P06Tests.cs.
public sealed class Chapter01P07Tests
{
    private const string LadaQuestionCheckChoiceId = "chapter01.node.08.lada_question_check";

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

    // N07A/N07B/N07C не содержат ни одного настоящего выбора — только
    // Continue от старта до Exit.
    private static NarrativeStateData RunLinearInvestigation(
        string dialogueId, NarrativeStateData state, out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, dialogueId, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        NarrativeDialogueView finalView = AdvanceThroughContinues(session, view);
        Assert.AreEqual(1, finalView.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Exit, finalView.AvailableChoices[0].Kind);

        finalResult = session.SelectChoice(finalView.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);
        return state;
    }

    // Реалистичный прогресс: N08 в игре достижим только после N01-N06 и
    // всех трёх расследований (см. Chapter01StateTests-стиль хелперов).
    private static NarrativeStateData NewStateAfterAllInvestigations()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.DamInspected);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.RepairCompleted);
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedRiver);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel);
        state.AddKnowledge(Chapter01Ids.Knowledge.MillMovesAtWrongTime);
        state.AddKnowledge(Chapter01Ids.Knowledge.CattleAvoidOldBranch);
        state.AddKnowledge(Chapter01Ids.Knowledge.FishPatternChanged);
        return state;
    }

    // Проходит N08 целиком: до Craft-проверки, через её принудительный
    // исход, через оба обязательных вывода (обычные Normal choice — не
    // Continue, поэтому их нужно явно выбрать, а не прокликать) и до Exit.
    private static NarrativeDialogueView RunD08ToConclusions(
        NarrativeStateData state, NarrativeCheckForcedOutcome forcedOutcome, List<string> visited,
        out NarrativeDialogueSelectionResult finalResult)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D08, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        view = AdvanceThroughContinues(session, view, visited);
        Assert.AreEqual(LadaQuestionCheckChoiceId, view.AvailableChoices[0].ChoiceId);

        NarrativeDialogueSelectionResult afterCheck = session.SelectChoicePreview(LadaQuestionCheckChoiceId, forcedOutcome);
        Assert.IsFalse(afterCheck.DialogueEnded);
        view = AdvanceThroughContinues(session, afterCheck.View, visited);

        // conclusion_a — единственный доступный обычный (не Continue) choice.
        Assert.AreEqual(1, view.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Normal, view.AvailableChoices[0].Kind);
        NarrativeDialogueSelectionResult afterConclusionA = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
        Assert.IsFalse(afterConclusionA.DialogueEnded);
        view = AdvanceThroughContinues(session, afterConclusionA.View, visited);

        // conclusion_b — тот же принцип.
        Assert.AreEqual(1, view.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Normal, view.AvailableChoices[0].Kind);
        NarrativeDialogueSelectionResult afterConclusionB = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
        Assert.IsFalse(afterConclusionB.DialogueEnded);
        view = AdvanceThroughContinues(session, afterConclusionB.View, visited);

        Assert.AreEqual(1, view.AvailableChoices.Count);
        Assert.AreEqual(DialogueChoiceKind.Exit, view.AvailableChoices[0].Kind);

        finalResult = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
        Assert.IsTrue(finalResult.DialogueEnded);
        return view;
    }

    // --- Структура и валидация ---

    [TestCase(Chapter01Ids.Dialogues.D07A)]
    [TestCase(Chapter01Ids.Dialogues.D07B)]
    [TestCase(Chapter01Ids.Dialogues.D07C)]
    [TestCase(Chapter01Ids.Dialogues.D08)]
    public void Dialogue_And_StartNode_Exist(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
        Assert.IsNotNull(FindNode(dialogue, dialogue.StartNodeId));
    }

    [TestCase(Chapter01Ids.Dialogues.D07A)]
    [TestCase(Chapter01Ids.Dialogues.D07B)]
    [TestCase(Chapter01Ids.Dialogues.D07C)]
    [TestCase(Chapter01Ids.Dialogues.D08)]
    public void Dialogue_Passes_Validation(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(dialogueId, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [TestCase(Chapter01Ids.Dialogues.D07A)]
    [TestCase(Chapter01Ids.Dialogues.D07B)]
    [TestCase(Chapter01Ids.Dialogues.D07C)]
    [TestCase(Chapter01Ids.Dialogues.D08)]
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
                Assert.AreEqual(0, choice.SuccessEffects.Count, where);
                Assert.AreEqual(0, choice.FailureEffects.Count, where);
                Assert.IsTrue(nodeIds.Contains(choice.NextNodeId), where + " -> отсутствующий узел '" + choice.NextNodeId + "'.");
            }
        }
    }

    // --- P07-T01: N07A ---

    [Test]
    public void D07A_SetsInvestigatedMill_AndBothKnowledgeItems()
    {
        NarrativeStateData state = RunLinearInvestigation(Chapter01Ids.Dialogues.D07A, new NarrativeStateData(), out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.InvestigatedMill));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.MillMovesAtWrongTime));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    // --- P07-T02: N07B ---

    [Test]
    public void D07B_SetsInvestigatedCattle_AndKnowledge()
    {
        NarrativeStateData state = RunLinearInvestigation(Chapter01Ids.Dialogues.D07B, new NarrativeStateData(), out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.InvestigatedCattle));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.CattleAvoidOldBranch));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    // --- P07-T03: N07C ---

    [TestCase(true)]
    [TestCase(false)]
    public void D07C_SetsInvestigatedRiver_AndKnowledge(bool repairOld)
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(repairOld ? Chapter01Ids.Flags.RepairOld : Chapter01Ids.Flags.RepairNew);

        RunLinearInvestigation(Chapter01Ids.Dialogues.D07C, state, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.InvestigatedRiver));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.FishPatternChanged));
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    // Три узла N07C (branch/fish/lada) несут по два взаимоисключающих
    // textBlock для старой/новой ветки ремонта вместо отдельных Continue-
    // узлов — единственное осознанное исключение из правила "один узел =
    // один блок" в P07. Этот тест доказывает, что оба блока никогда не
    // видны одновременно и что чужая ветка не просачивается на экран.
    [TestCase(true)]
    [TestCase(false)]
    public void D07C_NeverShowsBothRepairBranchBlocksAtOnce_AndNeverShowsWrongBranch(bool repairOld)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(repairOld ? Chapter01Ids.Flags.RepairOld : Chapter01Ids.Flags.RepairNew);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(database, Chapter01Ids.Dialogues.D07C, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        HashSet<string> allVisibleBlockIds = new HashSet<string>();
        while (true)
        {
            Assert.LessOrEqual(view.VisibleTextBlocks.Count, 1, "Узел " + view.NodeId + " показал больше одного блока одновременно.");
            foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
                allVisibleBlockIds.Add(block.BlockId);

            if (view.AvailableChoices.Count != 1 || view.AvailableChoices[0].Kind != DialogueChoiceKind.Continue)
                break;

            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            Assert.IsFalse(result.DialogueEnded);
            view = result.View;
        }

        string wrongSuffix = repairOld ? "_new" : "_old";
        foreach (string blockId in allVisibleBlockIds)
            Assert.IsFalse(blockId.EndsWith(wrongSuffix), "Показан блок чужой ветки ремонта: " + blockId);
    }

    // --- P07-T05: свободный порядок без счётчика улик ---

    [Test]
    public void GetAvailableInvestigationDialogueIds_EmptyBeforeWaterWrongActive()
    {
        NarrativeStateData state = new NarrativeStateData();
        Assert.IsEmpty(Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state));
    }

    [Test]
    public void GetAvailableInvestigationDialogueIds_AllThree_WhenNoneInvestigatedYet()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);

        IReadOnlyList<string> available = Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state);

        Assert.That(available, Is.EquivalentTo(new[] { Chapter01Ids.Dialogues.D07A, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Dialogues.D07C }));
    }

    // NUnit/C#: массив нельзя передать как аргумент TestCase (CS0182 — он
    // сам является params object[] у атрибута, вложенный array creation
    // внутри него не считается константным выражением атрибута). Порядок
    // прохождения передаём одной строкой вида "ABC" и индексируем по символу.
    [TestCase("ABC")]
    [TestCase("CAB")]
    [TestCase("BCA")]
    public void FreeOrder_AnyPermutation_ShrinksAvailabilityCorrectly_AndEndsEmpty(string order)
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);

        Dictionary<char, string> flagByLetter = new Dictionary<char, string>
        {
            { 'A', Chapter01Ids.Flags.InvestigatedMill },
            { 'B', Chapter01Ids.Flags.InvestigatedCattle },
            { 'C', Chapter01Ids.Flags.InvestigatedRiver },
        };

        Assert.AreEqual(3, Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state).Count);

        for (int i = 0; i < order.Length; i++)
        {
            state.SetFlag(flagByLetter[order[i]]);
            int expectedRemaining = order.Length - (i + 1);
            Assert.AreEqual(expectedRemaining, Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state).Count,
                "После прохождения " + (i + 1) + " из трёх (порядок " + order + ")");
        }

        Assert.IsEmpty(Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state));
        Assert.AreEqual(Chapter01Ids.Dialogues.D08, Chapter01StoryDirector.GetNextDialogueId(BuildFullProgressState(state)));
    }

    // Достраивает состояние до N01-N06, чтобы GetNextDialogueId дошёл до
    // проверки N07A/B/C, а не остановился раньше на более ранних узлах.
    private static NarrativeStateData BuildFullProgressState(NarrativeStateData investigationState)
    {
        investigationState.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        investigationState.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        investigationState.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        investigationState.SetFlag(Chapter01Ids.Flags.FloodHappened);
        investigationState.SetFlag(Chapter01Ids.Flags.RepairOld);
        return investigationState;
    }

    [Test]
    public void GetAvailableInvestigationDialogueIds_NoIntCounter_OnlyConcreteFlags()
    {
        // §2 требования P07: свободный порядок проверяется по конкретным
        // завершённым узлам, а не по количеству. NarrativeStateData не
        // содержит отдельного числового поля "investigation_points" —
        // единственный источник истины уже HasFlag/HasKnowledge.
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);

        Assert.AreEqual(1, Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state).Count);
        Assert.AreEqual(Chapter01Ids.Dialogues.D07C, Chapter01StoryDirector.GetAvailableInvestigationDialogueIds(state)[0]);
    }

    // --- P07-T04: N08 ---

    [Test]
    public void D08_RequiresAllThreeInvestigations_ToBeTheSuggestedNextDialogue()
    {
        NarrativeStateData partial = new NarrativeStateData();
        partial.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        partial.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        partial.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        partial.SetFlag(Chapter01Ids.Flags.FloodHappened);
        partial.SetFlag(Chapter01Ids.Flags.RepairOld);
        partial.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        partial.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        partial.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);

        Assert.AreEqual(Chapter01Ids.Dialogues.D07C, Chapter01StoryDirector.GetNextDialogueId(partial));

        partial.SetFlag(Chapter01Ids.Flags.InvestigatedRiver);
        Assert.AreEqual(Chapter01Ids.Dialogues.D08, Chapter01StoryDirector.GetNextDialogueId(partial));
    }

    [Test]
    public void D08_Success_GrantsSevenToothObjectKnowledge_AndReachesBothConclusions()
    {
        NarrativeStateData state = NewStateAfterAllInvestigations();
        List<string> visited = new List<string>();

        RunD08ToConclusions(state, NarrativeCheckForcedOutcome.ForceSuccess, visited, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.OldTraceFound));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.SevenToothObject));
        Assert.IsTrue(state.HasKnowledge(Chapter01Ids.Knowledge.OldCustom));
        Assert.Contains("chapter01.node.08.conclusion_a", visited);
        Assert.Contains("chapter01.node.08.conclusion_b", visited);
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D08_Failure_DoesNotBlockCompletion_AndDoesNotGrantSevenToothObjectKnowledge()
    {
        NarrativeStateData state = NewStateAfterAllInvestigations();
        List<string> visited = new List<string>();

        RunD08ToConclusions(state, NarrativeCheckForcedOutcome.ForceFailure, visited, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.OldTraceFound), "Провал не должен отменять физическую находку.");
        Assert.IsFalse(state.HasKnowledge(Chapter01Ids.Knowledge.SevenToothObject));
        Assert.Contains("chapter01.node.08.conclusion_a", visited);
        Assert.Contains("chapter01.node.08.conclusion_b", visited);
        Assert.IsTrue(finalResult.DialogueEnded);
    }

    [Test]
    public void D08_ConclusionA_RequiresBothOldSeventhChannel_AndOldTraceFound()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D08);
        DialogueNodeData node = FindNode(dialogue, "chapter01.node.08.conclusion_a");
        Assert.IsNotNull(node);
        Assert.AreEqual(1, node.Choices.Count);
        Assert.AreEqual(2, node.Choices[0].Conditions.Conditions.Count);
    }

    [Test]
    public void D08_ConclusionB_RequiresBothCattleAndFishKnowledge()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D08);
        DialogueNodeData node = FindNode(dialogue, "chapter01.node.08.conclusion_b");
        Assert.IsNotNull(node);
        Assert.AreEqual(1, node.Choices.Count);
        Assert.AreEqual(2, node.Choices[0].Conditions.Conditions.Count);
    }

    // Без реальных независимых источников вывод не должен быть достижим —
    // причинность работает через данные, а не только порядок сцен.
    [Test]
    public void D08_WithoutPrerequisiteKnowledge_ConclusionA_HasNoAvailableChoice()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedRiver);
        // Флаги расследования стоят, но знания сознательно не выданы.

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(database, Chapter01Ids.Dialogues.D08, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);

        view = AdvanceThroughContinues(session, view);
        NarrativeDialogueSelectionResult afterCheck = session.SelectChoicePreview(LadaQuestionCheckChoiceId, NarrativeCheckForcedOutcome.ForceSuccess);
        NarrativeDialogueView conclusionAView = AdvanceThroughContinues(session, afterCheck.View);

        Assert.AreEqual("chapter01.node.08.conclusion_a", conclusionAView.NodeId);
        Assert.IsEmpty(conclusionAView.AvailableChoices, "Без OldSeventhChannel вывод A не должен быть доступен.");
    }

    [Test]
    public void ApplySevenTeethInvestigationConsequences_GrantsItem_OnlyOnce()
    {
        NarrativeStateData state = NewStateAfterAllInvestigations();
        List<string> visited = new List<string>();
        RunD08ToConclusions(state, NarrativeCheckForcedOutcome.ForceSuccess, visited, out _);

        GameState gameState = new GameState { Narrative = state };
        Assert.IsFalse(gameState.Narrative.HasItem(Chapter01Ids.Items.SevenToothGauge));

        Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(gameState);
        Assert.IsTrue(gameState.Narrative.HasItem(Chapter01Ids.Items.SevenToothGauge));

        int countAfterFirst = gameState.Narrative.Items.Count;
        Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(gameState);
        Assert.AreEqual(countAfterFirst, gameState.Narrative.Items.Count, "Повторный вызов не должен выдавать предмет дважды.");
    }

    [Test]
    public void HandleDialogueCompleted_ForD08_GrantsItem_OnlyForD08()
    {
        NarrativeStateData state = NewStateAfterAllInvestigations();
        List<string> visited = new List<string>();
        RunD08ToConclusions(state, NarrativeCheckForcedOutcome.ForceFailure, visited, out _);

        GameState gameState = new GameState { Narrative = state };
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D07A);
        Assert.IsFalse(gameState.Narrative.HasItem(Chapter01Ids.Items.SevenToothGauge));

        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D08);
        Assert.IsTrue(gameState.Narrative.HasItem(Chapter01Ids.Items.SevenToothGauge));
    }

    [Test]
    public void D08_Completion_AdvancesStoryDirectorTo_D09()
    {
        NarrativeStateData state = NewStateAfterAllInvestigations();
        List<string> visited = new List<string>();
        RunD08ToConclusions(state, NarrativeCheckForcedOutcome.ForceSuccess, visited, out NarrativeDialogueSelectionResult finalResult);

        Assert.IsTrue(finalResult.DialogueEnded);
        Assert.AreEqual(Chapter01Ids.Dialogues.D09, Chapter01StoryDirector.GetNextDialogueId(state));
    }
}
