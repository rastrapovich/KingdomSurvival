using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// P10 — «Старый брод и люди ниже по течению». N12 «Женщина у брода» и
// N13 «У всех есть дом» + нормализация PartySize (P10-T04) и решающая
// проверка Характера FirstContact (P10-T03). Против настоящего
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

    private static bool IsBlockRevealed(NarrativeDialogueView view, string blockId)
    {
        foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
        {
            if (block.BlockId == blockId && block.IsTextRevealed)
                return true;
        }
        return false;
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

    private static NarrativeDialogueView AdvanceN12ToTalk(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view)
    {
        NarrativeDialogueChoiceView toWoman = FindChoiceById(view, "chapter01.node.12_to_woman");
        Assert.IsNotNull(toWoman);
        NarrativeDialogueSelectionResult r1 = session.SelectChoice(toWoman.ChoiceId);
        Assert.IsFalse(r1.DialogueEnded);

        NarrativeDialogueChoiceView toTalk = FindChoiceById(r1.View, "chapter01.node.12.woman_to_talk");
        Assert.IsNotNull(toTalk);
        NarrativeDialogueSelectionResult r2 = session.SelectChoice(toTalk.ChoiceId);
        Assert.IsFalse(r2.DialogueEnded);
        return r2.View;
    }

    [Test]
    public void N12_Talk_GrantsDrownedWomanStory_OnlyAfterReachingStoryBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910101);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, new List<string>(), out NarrativeDialogueView view);

        Assert.IsFalse(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.DrownedWomanStory));

        NarrativeDialogueView talkView = AdvanceN12ToTalk(session, view);

        Assert.IsTrue(IsBlockRevealed(talkView, "chapter01.node.12.talk_story"));
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
        NarrativeDialogueView talkView = AdvanceN12ToTalk(session, view);

        Assert.IsTrue(IsBlockRevealed(talkView, "chapter01.node.12.talk_gauge"));
    }

    [Test]
    public void N12_Woman_LargeParty_RevealsWeaponAwareBlock()
    {
        List<string> fighters = new List<string> { "garrick", "edric", "marta", "torvin" };
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910105, fighters);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D12, fighters, out NarrativeDialogueView view);

        NarrativeDialogueChoiceView toWoman = FindChoiceById(view, "chapter01.node.12_to_woman");
        NarrativeDialogueSelectionResult result = session.SelectChoice(toWoman.ChoiceId);

        Assert.IsTrue(IsBlockRevealed(result.View, "chapter01.node.12.woman_large_party"));
        Assert.IsFalse(IsBlockRevealed(result.View, "chapter01.node.12.woman_small_party"));
    }

    // --- N13: «У всех есть дом» ---

    private static NarrativeDialogueView AdvanceN13ToTensionNode(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view, bool calmApproach)
    {
        NarrativeDialogueChoiceView toApproach = FindChoiceById(view, "chapter01.node.13_to_approach");
        Assert.IsNotNull(toApproach);
        NarrativeDialogueSelectionResult r1 = session.SelectChoice(toApproach.ChoiceId);
        Assert.IsFalse(r1.DialogueEnded);

        string choiceId = calmApproach
            ? "chapter01.node.13.approach_choice_calm"
            : "chapter01.node.13.approach_choice_tense";
        NarrativeDialogueChoiceView approachChoice = FindChoiceById(r1.View, choiceId);
        Assert.IsNotNull(approachChoice);
        NarrativeDialogueSelectionResult r2 = session.SelectChoice(approachChoice.ChoiceId);
        Assert.IsFalse(r2.DialogueEnded);

        NarrativeDialogueChoiceView continue1 = FindChoice(r2.View, DialogueChoiceKind.Normal);
        Assert.IsNotNull(continue1);
        NarrativeDialogueSelectionResult r3 = session.SelectChoice(continue1.ChoiceId);
        Assert.IsFalse(r3.DialogueEnded);
        return r3.View;
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
        StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);

        Assert.IsTrue(IsBlockRevealed(view, "chapter01.node.13_repair_old"));
        Assert.IsFalse(IsBlockRevealed(view, "chapter01.node.13_repair_new"));
    }

    [Test]
    public void N13_Arrival_RepairNew_RevealsNewRepairConsequenceBlock()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910202);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairNew);
        StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);

        Assert.IsTrue(IsBlockRevealed(view, "chapter01.node.13_repair_new"));
        Assert.IsFalse(IsBlockRevealed(view, "chapter01.node.13_repair_old"));
    }

    [Test]
    public void N13_Approach_FordWomanHelped_RevealsSofterFirstReaction()
    {
        GameState gameState = NewGameStateEnRouteToOldWaterSearch(910203);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordWomanHelped);
        NarrativeDialogueRuntimeSession session = StartDialogue(gameState, Chapter01Ids.Dialogues.D13, new List<string>(), out NarrativeDialogueView view);

        NarrativeDialogueChoiceView toApproach = FindChoiceById(view, "chapter01.node.13_to_approach");
        NarrativeDialogueSelectionResult result = session.SelectChoice(toApproach.ChoiceId);

        Assert.IsTrue(IsBlockRevealed(result.View, "chapter01.node.13.approach_helped"));
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
}
