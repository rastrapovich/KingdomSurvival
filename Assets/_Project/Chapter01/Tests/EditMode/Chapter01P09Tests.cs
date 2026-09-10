using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P09 — «Первая дальняя дорога и лагерь». N11 «Дорога, которой нет»
// (обязательная, триггерится физическим прогрессом маршрута, не диалогом
// по клику) и D11B «Трое под телегой» (необязательная, но гарантированная
// встреча первого похода, разблокирует Лагерь). Против настоящего
// KingdomSurvivalDialogues.asset через Resources.Load — тот же подход, что
// Chapter01P08Tests.cs.
public sealed class Chapter01P09Tests
{
    private static DialogueDatabaseAsset LoadDatabase()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    // Готовое к экспедиции состояние (FarRouteUnlocked + область поиска
    // раскрыта), тот же хелпер по духу, что Chapter01P08Tests.
    // NewGameStateReadyForExpedition, но без прямой зависимости от того
    // файла (тесты не должны зависеть друг от друга).
    private static GameState NewGameStateReadyForExpedition(int seed)
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(seed);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        Chapter01OutcomeApplier.ApplyDepartureConsequences(gameState);
        return gameState;
    }

    private static GameState NewGameStateEnRouteToOldWaterSearch(int seed, List<string> fighterIds = null)
    {
        GameState gameState = NewGameStateReadyForExpedition(seed);
        LocationData target = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
        Assert.IsNotNull(target);

        string message;
        bool started = gameState.TryStartExpedition(
            Chapter01Ids.Locations.OldWaterSearch, fighterIds ?? new List<string>(), out message);
        Assert.IsTrue(started, message);
        Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
        return gameState;
    }

    // Прогресс маршрута задаётся напрямую полями ExpeditionData (тот же
    // приём, что ContinuousMovementTimeTests.cs — не гонять реальные тики
    // Advance ради конкретного процента).
    private static void SetRouteProgress(GameState gameState, double fraction)
    {
        ExpeditionData expedition = gameState.ActiveExpedition;
        int total = expedition.RouteLengthCells;
        int traveled = (int)(total * fraction);
        expedition.RemainingRouteCells = Mathf.Max(0, total - traveled);
    }

    // --- Структура и валидация ---

    [TestCase(Chapter01Ids.Dialogues.D11)]
    [TestCase(Chapter01Ids.Dialogues.D11B)]
    [TestCase(Chapter01Ids.Dialogues.D11C)]
    public void Dialogue_And_StartNode_Exist(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
    }

    [TestCase(Chapter01Ids.Dialogues.D11)]
    [TestCase(Chapter01Ids.Dialogues.D11B)]
    [TestCase(Chapter01Ids.Dialogues.D11C)]
    public void Dialogue_Passes_Validation(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(dialogueId, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // --- TRAVEL: триггер дорожных встреч по прогрессу маршрута ---

    [Test]
    public void GetPendingRoadEventDialogueId_NoExpedition_Null()
    {
        GameState gameState = NewGameStateReadyForExpedition(900001);
        Assert.IsNull(Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    [Test]
    public void GetPendingRoadEventDialogueId_BeforeThreshold_Null()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900002);
        SetRouteProgress(gameState, 0.10);
        Assert.IsNull(Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    [Test]
    public void GetPendingRoadEventDialogueId_AtRoadThreshold_ReturnsD11()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900003);
        SetRouteProgress(gameState, 0.35);
        Assert.AreEqual(Chapter01Ids.Dialogues.D11, Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    [Test]
    public void GetPendingRoadEventDialogueId_AfterLongRoadStarted_NotD11Again()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900004);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.LongRoadStarted);
        SetRouteProgress(gameState, 0.90);
        Assert.AreNotEqual(Chapter01Ids.Dialogues.D11, Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    [Test]
    public void GetPendingRoadEventDialogueId_AtCartThreshold_AfterLongRoad_ReturnsD11B()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900005);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.LongRoadStarted);
        SetRouteProgress(gameState, 0.65);
        Assert.AreEqual(Chapter01Ids.Dialogues.D11B, Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    [Test]
    public void GetPendingRoadEventDialogueId_AfterCartResolved_Null()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900006);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.LongRoadStarted);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CartResolved);
        SetRouteProgress(gameState, 0.90);
        Assert.IsNull(Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState));
    }

    // --- N11: пассивная проверка RoadReading ---

    private static NarrativeDialogueRuntimeSession StartD11(GameState gameState)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        CommanderData commander = gameState.GetSelectedCommander();
        Assert.IsNotNull(commander);
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database, Chapter01Ids.Dialogues.D11, commander.HeroProfile, gameState.Narrative,
            out NarrativeDialogueView view, out string error, new List<string>(), new List<string>(), gameState.WorldSeed);
        Assert.IsTrue(started, error);
        return session;
    }

    private static NarrativeDialogueChoiceView FindChoice(NarrativeDialogueView view, DialogueChoiceKind kind)
    {
        foreach (NarrativeDialogueChoiceView choice in view.AvailableChoices)
        {
            if (choice.Kind == kind)
                return choice;
        }
        return null;
    }

    private static NarrativeDialogueChoiceView FindChoiceById(NarrativeDialogueView view, string choiceId)
    {
        foreach (NarrativeDialogueChoiceView choice in view.AvailableChoices)
        {
            if (choice.ChoiceId == choiceId)
                return choice;
        }
        return null;
    }

    // Проводит сессию через две линейные Continue-реплики N11 до узла с
    // пассивной проверкой/реальным выбором маршрута, возвращает итоговый view.
    private static NarrativeDialogueView AdvanceD11ToRoadChoice(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view)
    {
        NarrativeDialogueChoiceView step1 = FindChoice(view, DialogueChoiceKind.Continue);
        Assert.IsNotNull(step1);
        NarrativeDialogueSelectionResult r1 = session.SelectChoice(step1.ChoiceId);
        Assert.IsFalse(r1.DialogueEnded);

        NarrativeDialogueChoiceView step2 = FindChoice(r1.View, DialogueChoiceKind.Continue);
        Assert.IsNotNull(step2);
        NarrativeDialogueSelectionResult r2 = session.SelectChoice(step2.ChoiceId);
        Assert.IsFalse(r2.DialogueEnded);

        return r2.View;
    }

    [Test]
    public void RoadReading_DefaultHero_FailsCheck_NoKnowledgeGranted()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900010);
        NarrativeDialogueRuntimeSession session = StartD11(gameState);
        NarrativeDialogueView view = AdvanceD11ToRoadChoice(session, session.BuildView());

        bool anyRevealed = false;
        foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
        {
            if (block.BlockId == "chapter01.node.11.03_check_success" && block.IsTextRevealed)
                anyRevealed = true;
        }

        Assert.IsFalse(anyRevealed, "6 + Инстинкт(5) + Следопытство(0) = 11 < 13 — по умолчанию провал.");
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.OldRoadAvoidedLowland));
    }

    [Test]
    public void RoadReading_StrongInstinctAndFieldcraft_SucceedsCheck_GrantsKnowledge()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900011);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();
        commander.HeroProfile.SetQuality(HeroQuality.Instinct, 10);
        commander.HeroProfile.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 5);

        NarrativeDialogueRuntimeSession session = StartD11(gameState);
        NarrativeDialogueView view = AdvanceD11ToRoadChoice(session, session.BuildView());

        bool revealed = false;
        foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
        {
            if (block.BlockId == "chapter01.node.11.03_check_success" && block.IsTextRevealed)
                revealed = true;
        }

        Assert.IsTrue(revealed, "6 + 10 + 5 = 21 >= 13 — гарантированный успех.");
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.OldRoadAvoidedLowland));
    }

    [Test]
    public void RoadChoice_FollowOldRoad_SetsFlag_NotCrossedBoundary()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900012);
        NarrativeDialogueRuntimeSession session = StartD11(gameState);
        NarrativeDialogueView view = AdvanceD11ToRoadChoice(session, session.BuildView());

        NarrativeDialogueChoiceView oldRoad = FindChoiceById(view, "chapter01.node.11.03_choice_old_road");
        Assert.IsNotNull(oldRoad);
        session.SelectChoice(oldRoad.ChoiceId);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FollowedOldRoad));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.CrossedOldRoadBoundary));
    }

    [Test]
    public void RoadChoice_Shortcut_SetsFlag_NotFollowedOldRoad()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900013);
        NarrativeDialogueRuntimeSession session = StartD11(gameState);
        NarrativeDialogueView view = AdvanceD11ToRoadChoice(session, session.BuildView());

        NarrativeDialogueChoiceView shortcut = FindChoiceById(view, "chapter01.node.11.03_choice_shortcut");
        Assert.IsNotNull(shortcut);
        session.SelectChoice(shortcut.ChoiceId);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.CrossedOldRoadBoundary));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FollowedOldRoad));
    }

    // --- N11: последствия выбора маршрута (Chapter01OutcomeApplier) ---

    [Test]
    public void ApplyLongRoadRouteConsequences_FollowedOldRoad_BuildsRealDetour()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900020);
        SetRouteProgress(gameState, 0.35);
        int remainingBeforeDetour = gameState.ActiveExpedition.RemainingRouteCells;

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FollowedOldRoad);
        Chapter01OutcomeApplier.ApplyLongRoadRouteConsequences(gameState);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldRoadDetourInProgress));
        Assert.Greater(gameState.ActiveExpedition.RemainingRouteCells, 0);
        // Реальный крюк — не то же самое, что "просто списать часы": маршрут
        // физически пересобран (RemainingRouteCells пересчитан заново).
        Assert.AreNotEqual(remainingBeforeDetour, gameState.ActiveExpedition.RemainingRouteCells);
    }

    [Test]
    public void ApplyLongRoadRouteConsequences_CrossedBoundary_NoDetourFlag()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900021);
        SetRouteProgress(gameState, 0.35);

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CrossedOldRoadBoundary);
        Chapter01OutcomeApplier.ApplyLongRoadRouteConsequences(gameState);

        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldRoadDetourInProgress));
    }

    [Test]
    public void TryContinueOldRoadDetourIfArrived_RedirectsToRealTarget_ClearsFlag()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900022);
        SetRouteProgress(gameState, 0.35);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FollowedOldRoad);
        Chapter01OutcomeApplier.ApplyLongRoadRouteConsequences(gameState);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldRoadDetourInProgress));

        // Симулируем физическое прибытие к временной точке крюка.
        gameState.ActiveExpedition.Phase = CommanderState.AtLocation;
        gameState.ActiveExpedition.RemainingRouteCells = 0;

        bool redirected = Chapter01StoryDirector.TryContinueOldRoadDetourIfArrived(gameState);

        Assert.IsTrue(redirected);
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldRoadDetourInProgress));
        Assert.AreEqual(CommanderState.TravellingToLocation, gameState.ActiveExpedition.Phase);
    }

    // --- CART: доступность "Разделить людей" по составу отряда ---

    private static NarrativeDialogueRuntimeSession StartD11BAtScene7(GameState gameState, List<string> companionIds, out NarrativeDialogueView view)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database, Chapter01Ids.Dialogues.D11B, commander.HeroProfile, gameState.Narrative,
            out view, out string error, companionIds, new List<string>(), gameState.WorldSeed);
        Assert.IsTrue(started, error);

        // "Остановиться." -> scene1 -> ... -> scene7 (шесть Continue-шагов).
        NarrativeDialogueChoiceView stop = FindChoiceById(view, "chapter01.node.11b_stop");
        Assert.IsNotNull(stop);
        NarrativeDialogueSelectionResult result = session.SelectChoice(stop.ChoiceId);
        for (int i = 0; i < 5; i++)
        {
            Assert.IsFalse(result.DialogueEnded);
            NarrativeDialogueChoiceView step = FindChoice(result.View, DialogueChoiceKind.Continue);
            Assert.IsNotNull(step, "шаг " + i);
            result = session.SelectChoice(step.ChoiceId);
        }

        Assert.IsFalse(result.DialogueEnded);
        view = result.View;
        return session;
    }

    [Test]
    public void CartScene_ZeroFighters_SavedBothOptionsNotAvailable()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900030);
        StartD11BAtScene7(gameState, new List<string>(), out NarrativeDialogueView view);

        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_man"));
        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_seed"));
        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_pass"));
    }

    [Test]
    public void CartScene_OneFighter_OnlyThreeHourVariantAvailable()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900031, new List<string> { "garrick" });
        StartD11BAtScene7(gameState, new List<string> { "garrick" }, out NarrativeDialogueView view);

        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
    }

    [Test]
    public void CartScene_TwoFighters_OnlyTwoHourVariantAvailable()
    {
        List<string> fighters = new List<string> { "garrick", "edric" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900032, fighters);
        StartD11BAtScene7(gameState, fighters, out NarrativeDialogueView view);

        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
    }

    // --- CART: ровно один исход, CampUnlocked ставится всегда ---

    [Test]
    public void CartOutcome_PassImmediately_SetsPassedByAndCampUnlocked_NoOtherOutcome()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900040);
        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        session.Start(
            database, Chapter01Ids.Dialogues.D11B, gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            gameState.Narrative, out NarrativeDialogueView view, out string error, new List<string>(), new List<string>(), gameState.WorldSeed);

        NarrativeDialogueChoiceView pass = FindChoiceById(view, "chapter01.node.11b_pass");
        Assert.IsNotNull(pass);
        NarrativeDialogueSelectionResult result = session.SelectChoice(pass.ChoiceId);
        Assert.IsTrue(result.DialogueEnded);

        AssertExactlyOneCartOutcome(gameState.Narrative, Chapter01Ids.Flags.CartOutcomePassedBy);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.CartResolved));
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.CampUnlocked));
    }

    [Test]
    public void CartOutcome_SavedMan_SetsExactlyThatOutcome_ActivityStartsOneHour()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900041);
        NarrativeDialogueRuntimeSession session = StartD11BAtScene7(gameState, new List<string>(), out NarrativeDialogueView view);

        NarrativeDialogueChoiceView savedMan = FindChoiceById(view, "chapter01.node.11b.scene7_saved_man");
        Assert.IsNotNull(savedMan);
        NarrativeDialogueSelectionResult result = session.SelectChoice(savedMan.ChoiceId);
        Assert.IsTrue(result.DialogueEnded);

        AssertExactlyOneCartOutcome(gameState.Narrative, Chapter01Ids.Flags.CartOutcomeSavedMan);

        Chapter01OutcomeApplier.ApplyCartConsequences(gameState);
        Assert.IsTrue(gameState.ActiveExpedition.HasTimedActivity);
        Assert.AreEqual(1.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 0.001);
    }

    [Test]
    public void CartOutcome_SavedBoth_OneFighter_ActivityStartsThreeHours()
    {
        List<string> fighters = new List<string> { "garrick" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900042, fighters);
        NarrativeDialogueRuntimeSession session = StartD11BAtScene7(gameState, fighters, out NarrativeDialogueView view);

        NarrativeDialogueChoiceView savedBoth = FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1");
        Assert.IsNotNull(savedBoth);
        session.SelectChoice(savedBoth.ChoiceId);

        AssertExactlyOneCartOutcome(gameState.Narrative, Chapter01Ids.Flags.CartOutcomeSavedBoth);

        Chapter01OutcomeApplier.ApplyCartConsequences(gameState);
        Assert.AreEqual(3.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 0.001);
    }

    [Test]
    public void CartOutcome_SavedBoth_TwoFighters_ActivityStartsTwoHours()
    {
        List<string> fighters = new List<string> { "garrick", "edric" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900043, fighters);
        NarrativeDialogueRuntimeSession session = StartD11BAtScene7(gameState, fighters, out NarrativeDialogueView view);

        NarrativeDialogueChoiceView savedBoth = FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2");
        Assert.IsNotNull(savedBoth);
        session.SelectChoice(savedBoth.ChoiceId);

        Chapter01OutcomeApplier.ApplyCartConsequences(gameState);
        Assert.AreEqual(2.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 0.001);
    }

    [Test]
    public void CartOutcome_PassedByAfterScene_NoTimedActivity()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900044);
        NarrativeDialogueRuntimeSession session = StartD11BAtScene7(gameState, new List<string>(), out NarrativeDialogueView view);

        NarrativeDialogueChoiceView pass = FindChoiceById(view, "chapter01.node.11b.scene7_pass");
        Assert.IsNotNull(pass);
        session.SelectChoice(pass.ChoiceId);

        AssertExactlyOneCartOutcome(gameState.Narrative, Chapter01Ids.Flags.CartOutcomePassedBy);

        Chapter01OutcomeApplier.ApplyCartConsequences(gameState);
        Assert.IsFalse(gameState.ActiveExpedition.HasTimedActivity);
    }

    private static void AssertExactlyOneCartOutcome(NarrativeStateData state, string expectedFlag)
    {
        string[] all =
        {
            Chapter01Ids.Flags.CartOutcomeSavedBoth,
            Chapter01Ids.Flags.CartOutcomeSavedMan,
            Chapter01Ids.Flags.CartOutcomeSavedSeed,
            Chapter01Ids.Flags.CartOutcomePassedBy,
        };

        int setCount = 0;
        foreach (string flag in all)
        {
            if (state.HasFlag(flag))
                setCount++;
        }

        Assert.AreEqual(1, setCount, "Ровно один исход телеги должен быть выставлен.");
        Assert.IsTrue(state.HasFlag(expectedFlag));
    }

    // --- CAMP: Chapter01CampSceneProvider read-only ---

    [Test]
    public void CampSceneProvider_BeforeCartResolved_ReturnsNull()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900050);
        Assert.IsNull(Chapter01CampSceneProvider.GetAvailableScene(gameState));
    }

    [Test]
    public void CampSceneProvider_AfterCartResolved_ReturnsD11C()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900051);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CartResolved);

        CampSceneViewData? scene = Chapter01CampSceneProvider.GetAvailableScene(gameState);
        Assert.IsTrue(scene.HasValue);
        Assert.AreEqual(Chapter01Ids.Dialogues.D11C, scene.Value.DialogueId);
    }

    [Test]
    public void CampSceneProvider_AfterEchoSeen_ReturnsNull()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900052);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CartResolved);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CartCampEchoSeen);

        Assert.IsNull(Chapter01CampSceneProvider.GetAvailableScene(gameState));
    }

    // --- D11C: ровно одна ветка на исход, эхо не показывается дважды ---

    [TestCase(Chapter01Ids.Flags.CartOutcomeSavedBoth, "chapter01.node.11c.branch_saved_both")]
    [TestCase(Chapter01Ids.Flags.CartOutcomeSavedMan, "chapter01.node.11c.branch_saved_man")]
    [TestCase(Chapter01Ids.Flags.CartOutcomeSavedSeed, "chapter01.node.11c.branch_saved_seed")]
    [TestCase(Chapter01Ids.Flags.CartOutcomePassedBy, "chapter01.node.11c.branch_passed_by")]
    public void D11C_ShowsOnlyMatchingBranch(string outcomeFlag, string expectedBlockId)
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900060);
        gameState.Narrative.SetFlag(outcomeFlag);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.CartResolved);

        DialogueDatabaseAsset database = LoadDatabase();
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        session.Start(
            database, Chapter01Ids.Dialogues.D11C, gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            gameState.Narrative, out NarrativeDialogueView view, out string error, new List<string>(), new List<string>(), gameState.WorldSeed);

        NarrativeDialogueChoiceView continueChoice = FindChoice(view, DialogueChoiceKind.Continue);
        NarrativeDialogueSelectionResult result = session.SelectChoice(continueChoice.ChoiceId);
        Assert.IsFalse(result.DialogueEnded);

        int visibleBranchCount = 0;
        bool sawExpected = false;
        foreach (NarrativeDialogueVisibleBlock block in result.View.VisibleTextBlocks)
        {
            if (!block.BlockId.StartsWith("chapter01.node.11c.branch_"))
                continue;
            visibleBranchCount++;
            if (block.BlockId == expectedBlockId)
                sawExpected = true;
        }

        Assert.AreEqual(1, visibleBranchCount);
        Assert.IsTrue(sawExpected);

        NarrativeDialogueChoiceView exit = FindChoice(result.View, DialogueChoiceKind.Exit);
        Assert.IsNotNull(exit);
        NarrativeDialogueSelectionResult ended = session.SelectChoice(exit.ChoiceId);
        Assert.IsTrue(ended.DialogueEnded);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.CartCampEchoSeen));
    }

    // --- ARRIVAL: физическое прибытие в область поиска ---

    [Test]
    public void RefreshRoadState_ArrivalAtOldWaterSearch_SetsRoadDestinationReached()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900070);
        gameState.ActiveExpedition.Phase = CommanderState.AtLocation;
        gameState.ActiveExpedition.RemainingRouteCells = 0;

        Chapter01StoryDirector.RefreshRoadState(gameState);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.RoadDestinationReached));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldFordFound));
    }

    [Test]
    public void RefreshRoadState_StillTravelling_DoesNotSetRoadDestinationReached()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900071);
        Chapter01StoryDirector.RefreshRoadState(gameState);
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.RoadDestinationReached));
    }

    [Test]
    public void JournalProvider_AfterRoadDestinationReached_ShowsSearchAreaStep()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(900072);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RoadDestinationReached);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);
        JournalGoalViewData trail = null;
        foreach (JournalGoalViewData goal in goals)
        {
            if (goal.Id == Chapter01Ids.JournalGoals.OldWaterTrail)
                trail = goal;
        }

        Assert.IsNotNull(trail);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":search_area", trail.RevisionId);
        StringAssert.Contains("Осмотреть", trail.CurrentStep);
    }
}
