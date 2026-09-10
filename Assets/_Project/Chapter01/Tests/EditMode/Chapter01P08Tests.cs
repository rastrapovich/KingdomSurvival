using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P08 — N09 «Совет: знания -> цель» и N10 «Сбор отряда». Presentation-правило
// «одна реплика = один шаг» (DialogueChoiceKind.Continue), N09 открывается не
// по факту прохождения N08 самого по себе, а по Chapter01StoryDirector.
// CanOpenDepartureCouncil (осмысленное сочетание знаний P07, не счётчик).
// N10 не создаёт состав сам — реальная экспедиция и ExpeditionStarted
// появляются только через GameState.TryStartExpedition +
// Chapter01StoryDirector.HandleStoryExpeditionStarted. Против настоящего
// KingdomSurvivalDialogues.asset через Resources.Load — тот же подход, что
// Chapter01FloodTests.cs/Chapter01P06Tests.cs/Chapter01P07Tests.cs.
public sealed class Chapter01P08Tests
{
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

    // Достраивает состояние до момента, когда N08 уже пройден (OldTraceFound),
    // но без знаний из комбинаций A/B/C — сам CanOpenDepartureCouncil тесты
    // добавляют нужные знания поверх этого явно, по одному сочетанию за раз.
    private static NarrativeStateData NewStateWithOldTraceFound()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);
        state.SetFlag(Chapter01Ids.Flags.InvestigatedRiver);
        state.SetFlag(Chapter01Ids.Flags.OldTraceFound);
        return state;
    }

    // Комбинация B (скот + рыба) не пересекается ни с одним из знаний,
    // нужных опциональным целям похода (второй хлеб, калибр) — удобная
    // независимая база для теста "0-2 опциональные цели".
    private static NarrativeStateData NewStateReadyForN09ViaCombinationB()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        state.AddKnowledge(Chapter01Ids.Knowledge.CattleAvoidOldBranch);
        state.AddKnowledge(Chapter01Ids.Knowledge.FishPatternChanged);
        return state;
    }

    // Проходит D09 от старта до Exit целиком, собирая ID всех реально
    // показанных блоков — так тест доказывает видимость опциональных целей
    // по факту работы BuildView, а не по чтению самих Conditions из данных.
    // Каждый узел D09 несёт ровно один доступный выбор (Continue, Normal в
    // synthesis или Exit в goal_tooth), поэтому "выбрать единственный
    // доступный вариант, пока диалог не завершится" — корректный полный
    // обход без отдельной ветки под Continue.
    private static List<string> RunD09_CollectVisibleBlockIds(NarrativeStateData state, out bool farRouteUnlockedAtOpen)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D09, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);
        Assert.IsTrue(started, error);
        farRouteUnlockedAtOpen = state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked);

        List<string> visibleBlockIds = new List<string>();
        while (true)
        {
            foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
                visibleBlockIds.Add(block.BlockId);

            Assert.AreEqual(1, view.AvailableChoices.Count, view.NodeId + " ожидался ровно один доступный выбор.");

            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            if (result.DialogueEnded)
                break;

            view = result.View;
        }

        return visibleBlockIds;
    }

    private static GameState NewGameStateReadyForExpedition()
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(20260908);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        Chapter01OutcomeApplier.ApplyDepartureConsequences(gameState);
        return gameState;
    }

    private static void AssertNoFlagEffect(IReadOnlyList<NarrativeEffect> effects, string where)
    {
        foreach (NarrativeEffect effect in effects)
        {
            if (effect.Type != NarrativeEffectType.SetFlag)
                continue;

            Assert.AreNotEqual(Chapter01Ids.Flags.ExpeditionStarted, effect.StringParam, where);
            Assert.AreNotEqual(Chapter01Ids.Flags.FarRouteUnlocked, effect.StringParam, where);
        }
    }

    // --- Структура и валидация ---

    [TestCase(Chapter01Ids.Dialogues.D09)]
    [TestCase(Chapter01Ids.Dialogues.D10)]
    public void Dialogue_And_StartNode_Exist(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
        Assert.IsNotNull(FindNode(dialogue, dialogue.StartNodeId));
    }

    [TestCase(Chapter01Ids.Dialogues.D09)]
    [TestCase(Chapter01Ids.Dialogues.D10)]
    public void Dialogue_Passes_Validation(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(dialogueId, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [TestCase(Chapter01Ids.Dialogues.D09)]
    [TestCase(Chapter01Ids.Dialogues.D10)]
    public void EveryNode_HasAtMostOneTextBlock(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);

        foreach (DialogueNodeData node in dialogue.Nodes)
            Assert.LessOrEqual(node.TextBlocks.Count, 1, dialogueId + "/" + node.Id);
    }

    [TestCase(Chapter01Ids.Dialogues.D09)]
    [TestCase(Chapter01Ids.Dialogues.D10)]
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

    // Все узлы D09/D10, кроме трёх настоящих точек решения (synthesis —
    // "Проследить..."; goal_tooth — Exit; party_choice — "Выбрать состав
    // похода"), обязаны иметь ровно один Continue-переход и ничего больше.
    [TestCase(Chapter01Ids.Dialogues.D09)]
    [TestCase(Chapter01Ids.Dialogues.D10)]
    public void OnlyContinueBetweenLinearNodes(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);

        HashSet<string> decisionOrExitNodeIds = new HashSet<string>
        {
            "chapter01.node.09.synthesis",
            "chapter01.node.09.goal_tooth",
            "chapter01.node.10.party_choice",
        };

        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            if (decisionOrExitNodeIds.Contains(node.Id))
                continue;

            Assert.AreEqual(1, node.Choices.Count, node.Id);
            Assert.AreEqual(DialogueChoiceKind.Continue, node.Choices[0].Kind, node.Id);
        }
    }

    // --- P08-T02: CanOpenDepartureCouncil ---

    [Test]
    public void CanOpenDepartureCouncil_False_WithoutOldTraceFound()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.AddKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        Assert.IsFalse(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void CanOpenDepartureCouncil_False_OldTraceFoundAlone_NoCombination()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        Assert.IsFalse(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void CanOpenDepartureCouncil_True_CombinationA_ChannelAndCustom()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        state.AddKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel);
        state.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        Assert.IsTrue(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void CanOpenDepartureCouncil_True_CombinationB_CattleAndFish()
    {
        NarrativeStateData state = NewStateReadyForN09ViaCombinationB();
        Assert.IsTrue(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void CanOpenDepartureCouncil_True_CombinationC_ChannelAndTooth()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        state.AddKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel);
        state.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);
        Assert.IsTrue(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void CanOpenDepartureCouncil_False_UnrelatedKnowledgePair()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        state.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        state.AddKnowledge(Chapter01Ids.Knowledge.DrownedWomanStory);
        Assert.IsFalse(Chapter01StoryDirector.CanOpenDepartureCouncil(state));
    }

    [Test]
    public void GetNextDialogueId_ReturnsNull_WhenOldTraceFoundButNoCombination()
    {
        NarrativeStateData state = NewStateWithOldTraceFound();
        Assert.IsNull(Chapter01StoryDirector.GetNextDialogueId(state));
        Assert.IsNull(Chapter01StoryDirector.GetNextNodeId(state));
    }

    [Test]
    public void GetNextDialogueId_ReturnsD09_NotD10_WhenCombinationSatisfied()
    {
        NarrativeStateData state = NewStateReadyForN09ViaCombinationB();
        Assert.AreEqual(Chapter01Ids.Dialogues.D09, Chapter01StoryDirector.GetNextDialogueId(state));
        Assert.AreEqual(Chapter01Ids.Nodes.N09, Chapter01StoryDirector.GetNextNodeId(state));
    }

    // --- P08-T01: N09 ---

    [Test]
    public void D09_FarRouteUnlocked_FalseAtOpen_TrueAfterDecision()
    {
        NarrativeStateData state = NewStateReadyForN09ViaCombinationB();
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked));

        RunD09_CollectVisibleBlockIds(state, out bool falseAtOpen);

        Assert.IsFalse(falseAtOpen);
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked));
    }

    [Test]
    public void D09_FarRouteUnlockedEffect_OnlyOnDepartureDecidedNode()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D09);

        int count = 0;
        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            foreach (DialogueTextBlockData block in node.TextBlocks)
            {
                foreach (NarrativeEffect effect in block.OnRevealEffects)
                {
                    if (effect.Type == NarrativeEffectType.SetFlag &&
                        effect.StringParam == Chapter01Ids.Flags.FarRouteUnlocked)
                    {
                        count++;
                        Assert.AreEqual("chapter01.node.09.departure_decided", node.Id);
                    }
                }
            }
        }
        Assert.AreEqual(1, count);
    }

    [Test]
    public void D09_Synthesis_HasExactlyOneMandatoryDecisionChoice()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D09);
        DialogueNodeData node = FindNode(dialogue, "chapter01.node.09.synthesis");

        Assert.AreEqual(1, node.Choices.Count);
        Assert.AreEqual(DialogueChoiceKind.Normal, node.Choices[0].Kind);
    }

    [TestCase(false, false, 0)]
    [TestCase(true, false, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, true, 2)]
    public void D09_OptionalGoalBlocks_VisibleExactlyWhenKnowledgeAllows(bool loaf, bool tooth, int expectedCount)
    {
        NarrativeStateData state = NewStateReadyForN09ViaCombinationB();
        if (loaf)
        {
            state.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
            state.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        }
        if (tooth)
            state.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        List<string> blockIds = RunD09_CollectVisibleBlockIds(state, out _);

        Assert.AreEqual(loaf, blockIds.Contains("chapter01.node.09.goal_loaf_main"));
        Assert.AreEqual(tooth, blockIds.Contains("chapter01.node.09.goal_tooth_main"));

        int actualCount = (blockIds.Contains("chapter01.node.09.goal_loaf_main") ? 1 : 0) +
                           (blockIds.Contains("chapter01.node.09.goal_tooth_main") ? 1 : 0);
        Assert.AreEqual(expectedCount, actualCount);
    }

    // GetDepartureOptionalGoals — та же логика для внешних потребителей
    // (тесты, будущий UI похода), не влияет на сам диалог.
    [TestCase(false, false, 0)]
    [TestCase(true, false, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, true, 2)]
    public void GetDepartureOptionalGoals_MatchesKnowledgeState(bool loaf, bool tooth, int expectedCount)
    {
        NarrativeStateData state = new NarrativeStateData();
        if (loaf)
        {
            state.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
            state.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        }
        if (tooth)
            state.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        Assert.AreEqual(expectedCount, Chapter01StoryDirector.GetDepartureOptionalGoals(state).Count);
    }

    // --- P08-T03: N10 и реальный состав похода ---

    [Test]
    public void D10_Start_DoesNotSetExpeditionStarted()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeStateData state = new NarrativeStateData();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();

        bool started = session.Start(database, Chapter01Ids.Dialogues.D10, new HeroProfileData(), state, out NarrativeDialogueView view, out string error);

        Assert.IsTrue(started, error);
        Assert.IsFalse(state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
    }

    [Test]
    public void D10_NeverSetsExpeditionStartedOrFarRouteUnlocked_ViaDialogueEffects()
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(Chapter01Ids.Dialogues.D10);

        foreach (DialogueNodeData node in dialogue.Nodes)
        {
            foreach (DialogueTextBlockData block in node.TextBlocks)
                AssertNoFlagEffect(block.OnRevealEffects, node.Id);

            foreach (DialogueChoiceData choice in node.Choices)
            {
                AssertNoFlagEffect(choice.SuccessEffects, node.Id);
                AssertNoFlagEffect(choice.FailureEffects, node.Id);
            }
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    public void PartyComposition_ZeroToFour_IsAccepted(int fighterCount)
    {
        GameState gameState = NewGameStateReadyForExpedition();
        List<string> selected = new List<string>();
        for (int i = 0; i < fighterCount; i++)
            selected.Add(gameState.Fighters[i].Id);

        bool started = gameState.TryStartExpedition(Chapter01Ids.Locations.OldWaterSearch, selected, out string message);

        Assert.IsTrue(started, message);
        Assert.IsNotNull(gameState.ActiveExpedition);
        CollectionAssert.AreEquivalent(selected, gameState.ActiveExpedition.FighterIds);
    }

    [Test]
    public void PartyComposition_FiveFighters_IsRejected()
    {
        GameState gameState = NewGameStateReadyForExpedition();
        List<string> selected = new List<string>();
        foreach (FighterData fighter in gameState.Fighters)
            selected.Add(fighter.Id);
        Assert.GreaterOrEqual(selected.Count, 5, "Тест рассчитан на стартовый ростер из пяти бойцов.");

        bool started = gameState.TryStartExpedition(Chapter01Ids.Locations.OldWaterSearch, selected, out string message);

        Assert.IsFalse(started);
        Assert.IsNull(gameState.ActiveExpedition);
    }

    [Test]
    public void PartyComposition_UnknownFighterId_IsRejected()
    {
        GameState gameState = NewGameStateReadyForExpedition();

        bool started = gameState.TryStartExpedition(
            Chapter01Ids.Locations.OldWaterSearch, new List<string> { "no-such-fighter" }, out string message);

        Assert.IsFalse(started);
        Assert.IsNull(gameState.ActiveExpedition);
    }

    [Test]
    public void ExpeditionStarted_FalseAfterRealExpeditionCreated_TrueOnlyAfterHandleStoryExpeditionStarted()
    {
        GameState gameState = NewGameStateReadyForExpedition();
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));

        List<string> selected = new List<string> { gameState.Fighters[0].Id };
        bool started = gameState.TryStartExpedition(Chapter01Ids.Locations.OldWaterSearch, selected, out string message);
        Assert.IsTrue(started, message);

        // Само по себе создание экспедиции не ставит флаг — он должен
        // означать совершившееся действие, а не просмотр сцены сбора.
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));

        Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
    }

    // Отсутствие любого конкретного бойца не должно блокировать сюжет —
    // N10 читает состав Дома, а не список зашитых имён.
    [Test]
    public void MissingSpecificFighter_DoesNotBlockExpedition()
    {
        GameState gameState = NewGameStateReadyForExpedition();
        gameState.Fighters.RemoveAll(fighter => fighter.Id == "garrick");

        bool started = gameState.TryStartExpedition(Chapter01Ids.Locations.OldWaterSearch, new List<string>(), out string message);

        Assert.IsTrue(started, message);
        Assert.AreEqual(0, gameState.ActiveExpedition.FighterIds.Count);

        Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
    }

    [Test]
    public void AfterExpeditionStarted_NextDialogueAndNodeAreN11()
    {
        NarrativeStateData state = NewStateReadyForN09ViaCombinationB();
        state.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        state.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);

        Assert.AreEqual(Chapter01Ids.Dialogues.D11, Chapter01StoryDirector.GetNextDialogueId(state));
        Assert.AreEqual(Chapter01Ids.Nodes.N11, Chapter01StoryDirector.GetNextNodeId(state));
    }

    // --- Карта/время: подтверждение состава больше не двигает героя ---

    // "Движение = течение времени" (производственная инструкция про карту и
    // время): подтверждение состава в Hero Screen больше не создаёт
    // ActiveExpedition — герой остаётся у Дома до клика по карте, поэтому
    // здесь не может быть никакого движения/времязатратного действия.
    [Test]
    public void PartyConfirmed_WithoutRealExpedition_NoMovementOrActivityInProgress()
    {
        GameState gameState = NewGameStateReadyForExpedition();

        Assert.IsFalse(ContinuousSimulationSystem.HasMovementOrActivityInProgress(gameState));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
    }

    // Клик по карте (GameState.TryStartExpedition) сразу переводит Phase в
    // TravellingToLocation — ровно этот факт PrototypeUIController.
    // RefreshAutoTimeState использует как триггер для
    // Chapter01StoryDirector.HandleStoryExpeditionStarted, без ожидания
    // первого фактического смещения маркера по карте.
    [Test]
    public void FirstRealMovement_MakesHasMovementOrActivityInProgress_TrueImmediately()
    {
        GameState gameState = NewGameStateReadyForExpedition();
        List<string> selected = new List<string> { gameState.Fighters[0].Id };

        bool started = gameState.TryStartExpedition(Chapter01Ids.Locations.OldWaterSearch, selected, out string message);

        Assert.IsTrue(started, message);
        Assert.IsTrue(ContinuousSimulationSystem.HasMovementOrActivityInProgress(gameState));
    }
}
