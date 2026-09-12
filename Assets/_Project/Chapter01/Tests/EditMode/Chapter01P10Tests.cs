using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P10 — «Старый брод и люди ниже по течению». N12 «Женщина у брода» и
// N13 «У всех есть дом» + нормализация PartySize (P10-T04), решающая
// проверка Характера FirstContact (P10-T03) и физическая связка между
// сценами через нижнее поселение (P10-T05). Против настоящего
// KingdomSurvivalDialogues.asset через Resources.Load — тот же подход, что
// Chapter01P09Tests.cs.
public sealed class Chapter01P10Tests
{
    private static DialogueDatabaseAsset LoadDatabase()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

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

    private static bool WasBlockRevealed(
        IReadOnlyList<NarrativeDialogueVisibleBlock> blocks,
        string blockId)
    {
        foreach (NarrativeDialogueVisibleBlock block in blocks)
        {
            if (block.BlockId == blockId && block.IsTextRevealed)
                return true;
        }
        return false;
    }

    private static NarrativeDialogueView AdvanceCurrentNodeText(
        NarrativeDialogueRuntimeSession session,
        NarrativeDialogueView view,
        List<NarrativeDialogueVisibleBlock> observedBlocks = null)
    {
        int guard = 0;
        while (true)
        {
            Assert.LessOrEqual(view.VisibleTextBlocks.Count, 1, "Один шаг показал несколько реплик.");
            if (view.VisibleTextBlocks.Count == 1)
                observedBlocks?.Add(view.VisibleTextBlocks[0]);

            if (view.AvailableChoices.Count != 1 ||
                view.AvailableChoices[0].ChoiceId != NarrativeDialogueRuntimeSession.SequentialContinueChoiceId)
            {
                return view;
            }

            Assert.Less(guard++, 64, "Зациклен runtime-переход между репликами.");
            view = session.SelectChoice(NarrativeDialogueRuntimeSession.SequentialContinueChoiceId).View;
        }
    }

    private static NarrativeDialogueRuntimeSession StartDialogue(
        GameState gameState,
        string dialogueId,
        List<string> companionIds,
        out NarrativeDialogueView view)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        CommanderData commander = gameState.GetSelectedCommander();
        Assert.IsNotNull(commander);
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();

        List<string> presentItemIds = new List<string>();
        if (gameState.Narrative?.Items != null)
            presentItemIds.AddRange(gameState.Narrative.Items);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database, dialogueId, commander.HeroProfile, gameState.Narrative,
            out view, out string error, companionIds ?? new List<string>(), presentItemIds, gameState.WorldSeed);
        Assert.IsTrue(started, error);
        return session;
    }

    // --- Структура и валидация ---

    [TestCase(Chapter01Ids.Dialogues.D12)]
    [TestCase(Chapter01Ids.Dialogues.D13)]
    public void Dialogue_And_StartNode_Exist(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        DialogueDefinitionData dialogue = database.FindDialogue(dialogueId);
        Assert.IsNotNull(dialogue);
        Assert.IsNotEmpty(dialogue.StartNodeId);
    }

    [TestCase(Chapter01Ids.Dialogues.D12)]
    [TestCase(Chapter01Ids.Dialogues.D13)]
    public void Dialogue_Passes_Validation(string dialogueId)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> issues = new List<string>();
        database.CollectValidationIssuesForDialogue(dialogueId, issues);
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void IdRegistry_ValidatesCleanly()
    {
        List<string> issues = new List<string>(Chapter01Ids.ValidateRegistry());
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    // --- P10-T04: PartySize = герой + бойцы, 1..5 ---

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 3)]
    [TestCase(3, 4)]
    [TestCase(4, 5)]
    public void ContextBuilder_GetPartySize_HeroPlusFighters(int fighterCount, int expectedPartySize)
    {
        // Реальные ID из стартового ростера GameState (garrick/edric/marta/
        // torvin/agnessa) — TryStartExpedition валидирует ID бойцов, поэтому
        // произвольные "fighter_N" отклоняются как несуществующие.
        string[] roster = { "garrick", "edric", "marta", "torvin", "agnessa" };
        List<string> fighters = new List<string>();
        for (int i = 0; i < fighterCount; i++)
            fighters.Add(roster[i]);

        GameState gameState = fighterCount == 0
            ? NewGameStateEnRouteToOldWaterSearch(910000)
            : NewGameStateEnRouteToOldWaterSearch(910000 + fighterCount, fighters);

        Assert.AreEqual(expectedPartySize, Chapter01ContextBuilder.GetPartySize(gameState));
    }

    [Test]
    public void ContextBuilder_Build_PartySizeMatchesGetPartySize()
    {
        List<string> fighters = new List<string> { "garrick", "edric" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910010, fighters);

        NarrativeEvaluationContext context = Chapter01ContextBuilder.Build(gameState, new HeroProfileData());

        Assert.AreEqual(3, context.PartySize);
        Assert.AreEqual(Chapter01ContextBuilder.GetPartySize(gameState), context.PartySize);
    }

    [TestCase(1, 2, true)]
    [TestCase(2, 2, true)]
    [TestCase(3, 2, false)]
    [TestCase(1, 1, true)]
    [TestCase(0, 1, true)]
    public void PartySizeAtMost_ReadsContextPartySize(int partySize, int intParam, bool expected)
    {
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(
            new HeroProfileData(), new NarrativeStateData(), partySize: partySize);
        NarrativeCondition condition = new NarrativeCondition
        {
            Type = NarrativeConditionType.PartySizeAtMost,
            IntParam = intParam
        };

        Assert.AreEqual(expected, condition.Evaluate(context));
    }

    [TestCase(4, 4, true)]
    [TestCase(5, 4, true)]
    [TestCase(3, 4, false)]
    public void PartySizeAtLeast_ReadsContextPartySize(int partySize, int intParam, bool expected)
    {
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(
            new HeroProfileData(), new NarrativeStateData(), partySize: partySize);
        NarrativeCondition condition = new NarrativeCondition
        {
            Type = NarrativeConditionType.PartySizeAtLeast,
            IntParam = intParam
        };

        Assert.AreEqual(expected, condition.Evaluate(context));
    }

    // --- Регрессия P09: D11B ("Трое под телегой") после миграции IntParam ---

    private static NarrativeDialogueRuntimeSession StartD11BAtScene7(GameState gameState, List<string> companionIds, out NarrativeDialogueView view)
    {
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D11B, companionIds, out view);

        NarrativeDialogueChoiceView stop = FindChoiceById(view, "chapter01.node.11b_stop");
        Assert.IsNotNull(stop);
        NarrativeDialogueSelectionResult result = session.SelectChoice(stop.ChoiceId);
        for (int i = 0; i < 6; i++)
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
    public void D11BRegression_ZeroFighters_SavedBothOptionsNotAvailable()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910030);
        StartD11BAtScene7(gameState, new List<string>(), out NarrativeDialogueView view);

        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
    }

    [Test]
    public void D11BRegression_OneFighter_OnlyThreeHourVariantAvailable()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910031, new List<string> { "garrick" });
        StartD11BAtScene7(gameState, new List<string> { "garrick" }, out NarrativeDialogueView view);

        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
    }

    [Test]
    public void D11BRegression_TwoFighters_OnlyTwoHourVariantAvailable()
    {
        List<string> fighters = new List<string> { "garrick", "edric" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910032, fighters);
        StartD11BAtScene7(gameState, fighters, out NarrativeDialogueView view);

        Assert.IsNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_1"));
        Assert.IsNotNull(FindChoiceById(view, "chapter01.node.11b.scene7_saved_both_2"));
    }

    // --- N12: «Женщина у брода» ---

    [Test]
    public void N12_Arrival_GrantsOldFordFlagAndKnowledge_Immediately()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910100);
        StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out _);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldFordFound));
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.OldFord));
        // Материальный факт не зависит от знаний героя об истории утопленницы.
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DrownedWomanStory));
    }

    private static NarrativeDialogueView AdvanceN12ToTalk(
        NarrativeDialogueRuntimeSession session,
        NarrativeDialogueView view,
        List<NarrativeDialogueVisibleBlock> talkBlocks = null)
    {
        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueChoiceView toWoman = FindChoiceById(view, "chapter01.node.12_to_woman");
        Assert.IsNotNull(toWoman);
        NarrativeDialogueSelectionResult r1 = session.SelectChoice(toWoman.ChoiceId);
        Assert.IsFalse(r1.DialogueEnded);

        NarrativeDialogueView womanView = AdvanceCurrentNodeText(session, r1.View);
        NarrativeDialogueChoiceView toTalk = FindChoiceById(womanView, "chapter01.node.12.woman_to_talk");
        Assert.IsNotNull(toTalk);
        NarrativeDialogueSelectionResult r2 = session.SelectChoice(toTalk.ChoiceId);
        Assert.IsFalse(r2.DialogueEnded);
        return AdvanceCurrentNodeText(session, r2.View, talkBlocks);
    }

    [Test]
    public void N12_Talk_GrantsDrownedWomanStory_OnlyAfterReachingStoryBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910101);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out NarrativeDialogueView view);

        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DrownedWomanStory));

        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceN12ToTalk(session, view, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.12.talk_story"));
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DrownedWomanStory));
        // N12 не выдаёт полную разгадку соглашения — это материал N14.
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.OldAgreement));
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem));
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.HomeWasNotSelfSufficient));
    }

    [Test]
    public void N12_Help_SetsFordWomanHelpedFlag()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910102);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView talkView = AdvanceN12ToTalk(session, view);

        NarrativeDialogueChoiceView help = FindChoiceById(talkView, "chapter01.node.12.talk_help");
        Assert.IsNotNull(help);
        NarrativeDialogueSelectionResult result = session.SelectChoice(help.ChoiceId);
        Assert.IsFalse(result.DialogueEnded);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FordWomanHelped));
    }

    [Test]
    public void N12_SkipHelp_DoesNotSetFordWomanHelpedFlag()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910103);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView talkView = AdvanceN12ToTalk(session, view);

        NarrativeDialogueChoiceView direction = FindChoiceById(talkView, "chapter01.node.12.talk_direction");
        Assert.IsNotNull(direction);
        session.SelectChoice(direction.ChoiceId);

        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FordWomanHelped));
    }

    [Test]
    public void N12_Talk_ItemGauge_RevealsRecognitionBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910104);
        gameState.Narrative.GrantItem(Chapter01Ids.Items.SevenToothGauge);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out NarrativeDialogueView view);
        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceN12ToTalk(session, view, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.12.talk_gauge"));
    }

    [Test]
    public void N12_Woman_LargeParty_RevealsWeaponAwareBlock()
    {
        List<string> fighters = new List<string> { "garrick", "edric", "marta", "torvin" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910105, fighters);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, fighters, out NarrativeDialogueView view);

        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueChoiceView toWoman = FindChoiceById(view, "chapter01.node.12_to_woman");
        NarrativeDialogueSelectionResult result = session.SelectChoice(toWoman.ChoiceId);
        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, result.View, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.12.woman_large_party"));
        Assert.IsFalse(WasBlockRevealed(observed, "chapter01.node.12.woman_small_party"));
    }

    // --- N13: «У всех есть дом» ---

    private static NarrativeDialogueView AdvanceN13ToTensionNode(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view, bool calmApproach)
    {
        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueChoiceView toApproach = FindChoiceById(view, "chapter01.node.13_to_approach");
        Assert.IsNotNull(toApproach);
        NarrativeDialogueSelectionResult r1 = session.SelectChoice(toApproach.ChoiceId);
        Assert.IsFalse(r1.DialogueEnded);

        NarrativeDialogueView approachView = AdvanceCurrentNodeText(session, r1.View);

        string choiceId = calmApproach
            ? "chapter01.node.13.approach_choice_calm"
            : "chapter01.node.13.approach_choice_tense";
        NarrativeDialogueChoiceView approachChoice = FindChoiceById(approachView, choiceId);
        Assert.IsNotNull(approachChoice);
        NarrativeDialogueSelectionResult r2 = session.SelectChoice(approachChoice.ChoiceId);
        Assert.IsFalse(r2.DialogueEnded);

        NarrativeDialogueView responseView = AdvanceCurrentNodeText(session, r2.View);
        NarrativeDialogueChoiceView continue1 = FindChoice(responseView, DialogueChoiceKind.Normal);
        Assert.IsNotNull(continue1);
        NarrativeDialogueSelectionResult r3 = session.SelectChoice(continue1.ChoiceId);
        Assert.IsFalse(r3.DialogueEnded);
        return AdvanceCurrentNodeText(session, r3.View);
    }

    [Test]
    public void N13_Arrival_DoesNotGrantDownstreamContact_BeforeFirstContact()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910200);
        StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out _);

        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DownstreamPeople));
    }

    [Test]
    public void N13_Arrival_RepairOld_RevealsOldRepairConsequenceBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910201);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        NarrativeDialogueRuntimeSession session = StartDialogue(
            gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, view, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.13_repair_old"));
        Assert.IsFalse(WasBlockRevealed(observed, "chapter01.node.13_repair_new"));
    }

    [Test]
    public void N13_Arrival_RepairNew_RevealsNewRepairConsequenceBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910202);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairNew);
        NarrativeDialogueRuntimeSession session = StartDialogue(
            gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, view, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.13_repair_new"));
        Assert.IsFalse(WasBlockRevealed(observed, "chapter01.node.13_repair_old"));
    }

    [Test]
    public void N13_Approach_FordWomanHelped_RevealsSofterFirstReaction()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910203);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordWomanHelped);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);

        view = AdvanceCurrentNodeText(session, view);
        NarrativeDialogueChoiceView toApproach = FindChoiceById(view, "chapter01.node.13_to_approach");
        NarrativeDialogueSelectionResult result = session.SelectChoice(toApproach.ChoiceId);
        List<NarrativeDialogueVisibleBlock> observed = new List<NarrativeDialogueVisibleBlock>();
        AdvanceCurrentNodeText(session, result.View, observed);

        Assert.IsTrue(WasBlockRevealed(observed, "chapter01.node.13.approach_helped"));
    }

    [Test]
    public void FirstContact_Spec_IsCharacterDifficulty13ActiveDecisive()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910300);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView tensionView = AdvanceN13ToTensionNode(session, view, calmApproach: true);

        NarrativeDialogueChoiceView firstContact = FindChoiceById(tensionView, "chapter01.node.13.tension_first_contact");
        Assert.IsNotNull(firstContact);
        Assert.AreEqual(DialogueChoiceKind.ActiveDecisive, firstContact.Kind);
    }

    [Test]
    public void FirstContact_Success_GrantsDownstreamKnowledgeAndFlag_LocksCheck()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910301);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();
        // Character = 15 гарантирует успех независимо от костей (2d6, 2..12).
        commander.HeroProfile.SetQuality(HeroQuality.Character, 15);

        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView tensionView = AdvanceN13ToTensionNode(session, view, calmApproach: true);

        NarrativeDialogueChoiceView firstContact = FindChoiceById(tensionView, "chapter01.node.13.tension_first_contact");
        NarrativeDialogueSelectionResult result = session.SelectChoice(firstContact.ChoiceId);

        Assert.IsFalse(result.DialogueEnded);
        Assert.IsNotNull(result.CheckResult);
        Assert.IsTrue(result.CheckResult.Success);

        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DownstreamPeople));
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.IsTrue(gameState.Narrative.IsCheckLocked(Chapter01Ids.Checks.FirstContact));
        Assert.AreEqual(true, gameState.Narrative.GetLastOutcome(Chapter01Ids.Checks.FirstContact));

        NarrativeDialogueChoiceView exit = FindChoice(result.View, DialogueChoiceKind.Exit);
        Assert.IsNotNull(exit);
        NarrativeDialogueSelectionResult exitResult = session.SelectChoice(exit.ChoiceId);
        Assert.IsTrue(exitResult.DialogueEnded);
    }

    [Test]
    public void FirstContact_Failure_StillGrantsDownstreamKnowledgeAndFlag_MandatoryPathContinues()
    {
        List<string> fighters = new List<string> { "garrick", "edric", "marta", "torvin" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910302, fighters);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();
        // Character = 0 + большой отряд (-1) гарантирует провал: 2d6 (max 12) - 1 = 11 < 13.
        commander.HeroProfile.SetQuality(HeroQuality.Character, 0);

        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, fighters, out NarrativeDialogueView view);
        NarrativeDialogueView tensionView = AdvanceN13ToTensionNode(session, view, calmApproach: false);

        NarrativeDialogueChoiceView firstContact = FindChoiceById(tensionView, "chapter01.node.13.tension_first_contact");
        NarrativeDialogueSelectionResult result = session.SelectChoice(firstContact.ChoiceId);

        Assert.IsNotNull(result.CheckResult);
        Assert.IsFalse(result.CheckResult.Success);

        // Провал не скрывает обязательную истину навсегда — знание и флаг всё равно есть.
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DownstreamPeople));
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.AreEqual(false, gameState.Narrative.GetLastOutcome(Chapter01Ids.Checks.FirstContact));

        NarrativeDialogueChoiceView exit = FindChoice(result.View, DialogueChoiceKind.Exit);
        Assert.IsNotNull(exit);
        NarrativeDialogueSelectionResult exitResult = session.SelectChoice(exit.ChoiceId);
        Assert.IsTrue(exitResult.DialogueEnded);
    }

    [Test]
    public void FirstContact_KnowledgeOldCustom_AppliesPlusOneModifier()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910303);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();

        NarrativeEvaluationContext context = Chapter01ContextBuilder.Build(gameState, commander.HeroProfile);
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = Chapter01Ids.Checks.FirstContact,
            Kind = NarrativeCheckKind.ActiveDecisive,
            Quality = HeroQuality.Character,
            CompetencyId = string.Empty,
            Difficulty = 13,
            ModifierRules = new List<NarrativeContextModifierRule>
            {
                new NarrativeContextModifierRule
                {
                    SourceId = "old_custom",
                    Value = 1,
                    Condition = new NarrativeConditionGroup
                    {
                        Conditions = new List<NarrativeCondition>
                        {
                            new NarrativeCondition
                            {
                                Type = NarrativeConditionType.KnowledgeKnown,
                                StringParam = Chapter01Ids.Knowledge.OldCustom
                            }
                        }
                    }
                }
            }
        };

        NarrativeCheckMathBreakdown breakdown = NarrativeCheckResolver.ComputeBreakdown(spec, context);
        Assert.AreEqual(1, breakdown.AppliedContextModifier);
    }

    [Test]
    public void FirstContact_CannotBeAttemptedTwice_ReenteringReplaysStoredOutcome()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910304);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();
        commander.HeroProfile.SetQuality(HeroQuality.Character, 15);

        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView tensionView = AdvanceN13ToTensionNode(session, view, calmApproach: true);
        NarrativeDialogueChoiceView firstContact = FindChoiceById(tensionView, "chapter01.node.13.tension_first_contact");
        session.SelectChoice(firstContact.ChoiceId);

        Assert.IsTrue(gameState.Narrative.IsCheckLocked(Chapter01Ids.Checks.FirstContact));

        // Открываем диалог заново (симулируем повторный вход) — второй бросок
        // не должен создаваться: PeekAvailability должен вернуть AlreadyDecided,
        // а повторное разрешение проверки — переиграть уже сохранённый исход.
        NarrativeDialogueRuntimeSession secondSession = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView secondView);
        NarrativeDialogueView secondTensionView = AdvanceN13ToTensionNode(secondSession, secondView, calmApproach: true);
        NarrativeDialogueChoiceView secondFirstContact = FindChoiceById(secondTensionView, "chapter01.node.13.tension_first_contact");
        Assert.IsNotNull(secondFirstContact);

        int attemptsBefore = gameState.Narrative.FindHistory(Chapter01Ids.Checks.FirstContact).Attempts.Count;
        NarrativeDialogueSelectionResult replay = secondSession.SelectChoice(secondFirstContact.ChoiceId);
        int attemptsAfter = gameState.Narrative.FindHistory(Chapter01Ids.Checks.FirstContact).Attempts.Count;

        Assert.AreEqual(attemptsBefore, attemptsAfter, "Повторный вход не должен добавлять новую попытку.");
        Assert.IsTrue(replay.CheckResult.Success);
    }

    // --- Сохранение/восстановление состояния FirstContact ---

    [Test]
    public void FirstContact_StateSurvives_JsonRoundTrip()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910305);
        CommanderData commander = gameState.GetSelectedCommander();
        commander.HeroProfile = new HeroProfileData();
        commander.HeroProfile.SetQuality(HeroQuality.Character, 15);

        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);
        NarrativeDialogueView tensionView = AdvanceN13ToTensionNode(session, view, calmApproach: true);
        NarrativeDialogueChoiceView firstContact = FindChoiceById(tensionView, "chapter01.node.13.tension_first_contact");
        session.SelectChoice(firstContact.ChoiceId);

        string json = JsonUtility.ToJson(gameState.Narrative);
        NarrativeStateData restored = JsonUtility.FromJson<NarrativeStateData>(json);

        Assert.IsTrue(restored.IsCheckLocked(Chapter01Ids.Checks.FirstContact));
        Assert.AreEqual(true, restored.GetLastOutcome(Chapter01Ids.Checks.FirstContact));
        Assert.IsTrue(restored.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.IsTrue(restored.HasKnowledge(Chapter01Ids.Knowledge.DownstreamPeople));
    }

    // --- P10-LocInt: OldWaterSearch как реальное Location Research +
    // story-gate для автоматического N12. UI-обвязка (Location Interaction
    // overlay, ModalQueue, кнопка "ВОЙТИ В ЛОКАЦИЮ") в этом проекте не имеет
    // EditMode-покрытия — PrototypeUIController требует живого UIDocument и
    // проверяется только вручную в Play Mode (см. DEVELOPMENT_STATUS.md);
    // здесь проверяется вся Core/Chapter01-логика, от которой она зависит.

    private static void ArriveAtOldWaterSearch(GameState gameState)
    {
        PlaceExpeditionAtLocation(gameState, Chapter01Ids.Locations.OldWaterSearch);
        Chapter01StoryDirector.RefreshRoadState(gameState);
    }

    private static void PlaceExpeditionAtLocation(GameState gameState, string locationId)
    {
        LocationData location = gameState.FindLocation(locationId);
        Assert.IsNotNull(location);
        Assert.IsTrue(gameState.HasActiveExpedition);

        ExpeditionData expedition = gameState.ActiveExpedition;
        expedition.LocationId = locationId;
        expedition.Phase = CommanderState.AtLocation;
        expedition.RemainingRouteCells = 0;
        expedition.RouteLengthCells = 0;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0.0;
        expedition.CurrentMapXPercent = location.MapXPercent;
        expedition.CurrentMapYPercent = location.MapYPercent;
        expedition.TargetMapXPercent = location.MapXPercent;
        expedition.TargetMapYPercent = location.MapYPercent;
        expedition.Route = new List<MapPointData>
        {
            new MapPointData(location.MapXPercent, location.MapYPercent)
        };
        expedition.LastTravelPoints = new List<MapPointData>();
        expedition.ActiveActivity = null;
        expedition.PendingDecision = null;

        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        Assert.IsNotNull(commander);
        commander.State = CommanderState.AtLocation;
    }

    private static GameState NewGameStateAfterN12(int seed)
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(seed);
        ArriveAtOldWaterSearch(gameState);
        gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch).IsExplored = true;
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.OldFordFound);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12);
        return gameState;
    }

    private static JournalGoalViewData FindJournalGoal(
        IReadOnlyList<JournalGoalViewData> goals,
        string goalId)
    {
        for (int i = 0; i < goals.Count; i++)
        {
            if (goals[i].Id == goalId)
                return goals[i];
        }

        return null;
    }

    private static void CompleteOldWaterSearchResearch(GameState gameState)
    {
        LocationData location =
            gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
        Assert.IsNotNull(location);

        // Advance принимает реальные секунды, а ExplorationHours хранится
        // в игровых часах. 4f давало лишь 0,8 игрового часа и потому три
        // теста ниже проверяли состояние до завершения исследования.
        float realSecondsToComplete = (float)(
            location.ExplorationHours /
            ContinuousSimulationSystem.GameHoursPerRealSecond + 1.0);

        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.Advance(
            gameState,
            realSecondsToComplete,
            false);
    }

    [Test]
    public void OldWaterSearch_HasNonZeroExplorationHours_AndInteractionDescription()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910400);
        LocationData location = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);

        Assert.IsNotNull(location);
        Assert.Greater(location.ExplorationHours, 0.0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(location.InteractionDescription));
    }

    [Test]
    public void GetPendingLocationNarrativeDialogueId_Null_BeforeArrival()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910401);
        Assert.IsNull(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState));
    }

    [Test]
    public void GetPendingLocationNarrativeDialogueId_Null_ArrivedButNotExploredYet()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910402);
        ArriveAtOldWaterSearch(gameState);

        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.RoadDestinationReached));
        Assert.IsNull(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState));
    }

    [Test]
    public void LocationResearch_Completion_SetsIsExplored_ButNotOldFordFound()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910403);
        ArriveAtOldWaterSearch(gameState);
        gameState.ArmySupply = 100;

        string message;
        Assert.IsTrue(gameState.TryStartLocationResearch(out message), message);
        CompleteOldWaterSearchResearch(gameState);

        LocationData location = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
        Assert.IsTrue(location.IsExplored);
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldFordFound));
    }

    [Test]
    public void GetPendingLocationNarrativeDialogueId_ReturnsD12_AfterResearchCompletes()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910404);
        ArriveAtOldWaterSearch(gameState);
        gameState.ArmySupply = 100;

        string message;
        Assert.IsTrue(gameState.TryStartLocationResearch(out message), message);
        CompleteOldWaterSearchResearch(gameState);

        Assert.AreEqual(Chapter01Ids.Dialogues.D12, Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState));
    }

    [Test]
    public void GetPendingLocationNarrativeDialogueId_Null_AfterOldFordFound()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910405);
        ArriveAtOldWaterSearch(gameState);
        gameState.ArmySupply = 100;

        string message;
        Assert.IsTrue(gameState.TryStartLocationResearch(out message), message);
        CompleteOldWaterSearchResearch(gameState);
        Assert.AreEqual(Chapter01Ids.Dialogues.D12, Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState));

        // N12 сам выставляет OldFordFound (не завершение исследования) —
        // после этого story-gate не должен снова предлагать D12.
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.OldFordFound);
        Assert.IsNull(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState));
    }

    // --- P10-T05: N12 → физический путь к людям → ручной вход в N13 ---

    [Test]
    public void DownstreamSettlement_DoesNotExistBeforeN12Completes()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910500);
        ArriveAtOldWaterSearch(gameState);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.OldFordFound);

        Assert.IsNull(gameState.FindLocation(Chapter01Ids.Locations.DownstreamSettlement));
        Assert.IsFalse(gameState.Narrative.HasEffectApplied(
            Chapter01Ids.Effects.DownstreamLocationReveal));
    }

    [Test]
    public void D12Completion_RevealsDownstreamSettlementOnce_WithoutTeleportOrContact()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910501);
        ArriveAtOldWaterSearch(gameState);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.OldFordFound);

        ExpeditionData expedition = gameState.ActiveExpedition;
        string locationBefore = expedition.LocationId;
        CommanderState phaseBefore = expedition.Phase;
        float xBefore = expedition.CurrentMapXPercent;
        float yBefore = expedition.CurrentMapYPercent;

        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12);

        int revealCount = gameState.Locations.FindAll(location =>
            location.Id == Chapter01Ids.Locations.DownstreamSettlement).Count;
        Assert.AreEqual(1, revealCount);
        Assert.IsTrue(gameState.Narrative.HasEffectApplied(
            Chapter01Ids.Effects.DownstreamLocationReveal));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.AreEqual(locationBefore, expedition.LocationId);
        Assert.AreEqual(phaseBefore, expedition.Phase);
        Assert.AreEqual(xBefore, expedition.CurrentMapXPercent);
        Assert.AreEqual(yBefore, expedition.CurrentMapYPercent);
    }

    [Test]
    public void DownstreamSettlement_ContinuesCapitalToFordVector_ByTwelvePercent()
    {
        GameState gameState = NewGameStateAfterN12(910502);
        LocationData oldWater = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
        LocationData downstream = gameState.FindLocation(Chapter01Ids.Locations.DownstreamSettlement);

        Assert.IsNotNull(downstream);
        Assert.AreEqual("Люди ниже по течению", downstream.Name);
        Assert.AreEqual(0.0, downstream.ExplorationHours);
        Assert.IsFalse(string.IsNullOrWhiteSpace(downstream.InteractionDescription));
        Assert.That(downstream.MapXPercent, Is.InRange(0f, 100f));
        Assert.That(downstream.MapYPercent, Is.InRange(0f, 100f));

        Vector2 capitalToFord = new Vector2(
            oldWater.MapXPercent - WorldMapNavigation.CapitalXPercent,
            oldWater.MapYPercent - WorldMapNavigation.CapitalYPercent);
        Vector2 fordToDownstream = new Vector2(
            downstream.MapXPercent - oldWater.MapXPercent,
            downstream.MapYPercent - oldWater.MapYPercent);

        Assert.That(fordToDownstream.magnitude, Is.InRange(10f, 15f));
        Assert.Greater(Vector2.Dot(capitalToFord.normalized, fordToDownstream.normalized), 0.999f);
    }

    [Test]
    public void DownstreamRoute_UsesOrdinaryTime_AndN13WaitsForExplicitLocationAction()
    {
        GameState gameState = NewGameStateAfterN12(910503);
        LocationData downstream = gameState.FindLocation(Chapter01Ids.Locations.DownstreamSettlement);

        Assert.IsNull(Chapter01StoryDirector.GetLocationEntryDialogueId(
            gameState, Chapter01Ids.Locations.DownstreamSettlement));

        string message;
        Assert.IsTrue(gameState.TryChangeExpeditionRoute(
            downstream.MapXPercent,
            downstream.MapYPercent,
            downstream.Id,
            out message), message);
        Assert.AreEqual(CommanderState.TravellingToLocation, gameState.ActiveExpedition.Phase);

        float startX = gameState.ActiveExpedition.CurrentMapXPercent;
        float startY = gameState.ActiveExpedition.CurrentMapYPercent;
        ContinuousClockSnapshot clockBefore = ContinuousSimulationSystem.GetClock(gameState);
        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.Advance(gameState, 1f, false);
        ContinuousClockSnapshot clockAfter = ContinuousSimulationSystem.GetClock(gameState);

        Assert.Greater(clockAfter.HourOfDay, clockBefore.HourOfDay);
        Assert.Greater(Vector2.Distance(
            new Vector2(startX, startY),
            new Vector2(
                gameState.ActiveExpedition.CurrentMapXPercent,
                gameState.ActiveExpedition.CurrentMapYPercent)), 0f);
        Assert.IsNull(Chapter01StoryDirector.GetLocationEntryDialogueId(
            gameState, downstream.Id));

        // WM-12: "1 клетка = 1 сутки" — путь до downstream (~10-15% карты,
        // см. DownstreamSettlement_ContinuesCapitalToFordVector_ByTwelvePercent)
        // занимает больше недели игрового времени. Без запаса снабжения
        // экспедиция с ArmySupply=0 (дефолт CreateNewGame) по дороге
        // автоматически поворачивает домой (легитимный игровой механизм
        // "Экспедиционный риск" — раздел 9.7 канона), что и произошло при
        // первой проверке через eval. Тест — про нарративный гейтинг во
        // время обычного перехода, а не про голод, поэтому даём запас
        // снабжения, как это уже делают другие тесты в проекте (например
        // LocationResearch_TakesConfiguredHoursAndRewardsOnCompletion).
        gameState.ArmySupply = 1000;

        // 60 реальных секунд больше не гарантируют прибытие на новой (гораздо
        // более медленной) шкале. Считаем реальное время до прибытия из
        // самого маршрута, с небольшим (не множительным) запасом — Advance
        // должен сам остановиться точно на прибытии (RequestAutoPause),
        // большой запас рискует "проскочить" мимо него в последующую логику.
        double remainingHours =
            ContinuousSimulationSystem.GetTravelHoursRemaining(gameState);
        float arrivalAdvanceSeconds = (float)(
            (remainingHours + 1.0) / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationBatch arrival =
            ContinuousSimulationSystem.Advance(gameState, arrivalAdvanceSeconds, false);

        Assert.AreEqual(CommanderState.AtLocation, gameState.ActiveExpedition.Phase);
        Assert.AreEqual(downstream.Id, gameState.ActiveExpedition.LocationId);
        Assert.IsNotNull(arrival.MandatoryNotice);
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        Assert.IsNull(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState),
            "N13 нельзя добавлять в автоматический story-gate прибытия.");
        Assert.AreEqual(Chapter01Ids.Dialogues.D13,
            Chapter01StoryDirector.GetLocationEntryDialogueId(gameState, downstream.Id));

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.DownstreamContact);
        Assert.IsNull(Chapter01StoryDirector.GetLocationEntryDialogueId(gameState, downstream.Id));
    }

    [Test]
    public void N12Completion_UpdatesExistingJournalGoal_ToDownstreamPeopleStep()
    {
        GameState gameState = NewGameStateAfterN12(910504);

        JournalGoalViewData trail = FindJournalGoal(
            Chapter01JournalProvider.Build(gameState),
            Chapter01Ids.JournalGoals.OldWaterTrail);

        Assert.IsNotNull(trail);
        Assert.AreEqual("Добраться до людей ниже по течению.", trail.CurrentStep);
        Assert.AreEqual(
            Chapter01Ids.JournalGoals.OldWaterTrail + ":downstream_people",
            trail.RevisionId);
    }

    [Test]
    public void SaveLoad_DuringDownstreamTravel_PreservesLocationRouteAndJournalStep()
    {
        GameState gameState = NewGameStateAfterN12(910505);
        LocationData downstream = gameState.FindLocation(Chapter01Ids.Locations.DownstreamSettlement);
        Assert.IsTrue(gameState.TryChangeExpeditionRoute(
            downstream.MapXPercent,
            downstream.MapYPercent,
            downstream.Id,
            out string message), message);

        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.Advance(gameState, 1f, false);
        string json = JsonUtility.ToJson(gameState);
        GameState restored = JsonUtility.FromJson<GameState>(json);

        LocationData restoredDownstream =
            restored.FindLocation(Chapter01Ids.Locations.DownstreamSettlement);
        Assert.IsNotNull(restoredDownstream);
        Assert.AreEqual(CommanderState.TravellingToLocation, restored.ActiveExpedition.Phase);
        Assert.AreEqual(Chapter01Ids.Locations.DownstreamSettlement,
            restored.ActiveExpedition.LocationId);
        Assert.Greater(restored.ActiveExpedition.RemainingRouteCells, 0);

        JournalGoalViewData trail = FindJournalGoal(
            Chapter01JournalProvider.Build(restored),
            Chapter01Ids.JournalGoals.OldWaterTrail);
        Assert.IsNotNull(trail);
        Assert.AreEqual("Добраться до людей ниже по течению.", trail.CurrentStep);
        Assert.AreEqual(
            Chapter01Ids.JournalGoals.OldWaterTrail + ":downstream_people",
            trail.RevisionId);
    }
}
