using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using NUnit.Framework;

public sealed class Chapter01StateTests
{
    private static NarrativeStateData NewState()
    {
        return new NarrativeStateData();
    }

    [Test]
    public void Registry_HasNoEmptyOrDuplicateIds()
    {
        IReadOnlyList<string> issues = Chapter01Ids.ValidateRegistry();
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
    }

    [Test]
    public void FreshState_NextDialogue_IsFirstNode()
    {
        NarrativeStateData state = NewState();
        Assert.AreEqual(Chapter01Ids.Dialogues.D01, Chapter01StoryDirector.GetNextDialogueId(state));
        Assert.AreEqual(Chapter01Ids.Nodes.N01, Chapter01StoryDirector.GetNextNodeId(state));
        Assert.IsFalse(Chapter01StoryDirector.IsChapterComplete(state));
    }

    [Test]
    public void CompletingNode_AdvancesToNextNode_AndDoesNotRepeat()
    {
        NarrativeStateData state = NewState();

        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        Assert.AreEqual(Chapter01Ids.Dialogues.D02, Chapter01StoryDirector.GetNextDialogueId(state));

        // Повторный вызов без изменения флагов возвращает тот же узел —
        // это и есть идемпотентность: уже завершённый N01 не предлагается снова.
        Assert.AreEqual(Chapter01Ids.Dialogues.D02, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    [Test]
    public void AfterFlood_N05IsAlwaysAvailable()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);

        Assert.AreEqual(Chapter01Ids.Dialogues.D05, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void BothRepairBranches_LeadToN07(bool repairOld, bool repairNew)
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        if (repairOld) state.SetFlag(Chapter01Ids.Flags.RepairOld);
        if (repairNew) state.SetFlag(Chapter01Ids.Flags.RepairNew);
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);

        Assert.AreEqual(Chapter01Ids.Dialogues.D07A, Chapter01StoryDirector.GetNextDialogueId(state));
    }

    [Test]
    public void RepairOldAndRepairNew_AreMutuallyExclusive()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        Assert.AreEqual(Chapter01RepairChoice.Old, Chapter01StoryDirector.GetRepairChoice(state));

        state.SetFlag(Chapter01Ids.Flags.RepairNew);
        Assert.Throws<System.InvalidOperationException>(() => Chapter01StoryDirector.GetRepairChoice(state));
    }

    [Test]
    public void RepairChoice_NoneByDefault()
    {
        NarrativeStateData state = NewState();
        Assert.AreEqual(Chapter01RepairChoice.None, Chapter01StoryDirector.GetRepairChoice(state));
    }

    [Test]
    public void ChapterComplete_ReflectsCompletedFlag()
    {
        NarrativeStateData state = NewState();
        Assert.IsFalse(Chapter01StoryDirector.IsChapterComplete(state));
        Assert.IsNull(Chapter01StoryDirector.GetNextDialogueId(NewCompletedState()));
    }

    private static NarrativeStateData NewCompletedState()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.Completed);
        return state;
    }

    [Test]
    public void TryAdvance_OpensNextDialogue_AndReturnsOpenerResult()
    {
        GameState gameState = new GameState { Narrative = NewState() };
        string openedId = null;

        bool result = Chapter01StoryDirector.TryAdvance(gameState, id =>
        {
            openedId = id;
            return true;
        });

        Assert.IsTrue(result);
        Assert.AreEqual(Chapter01Ids.Dialogues.D01, openedId);
    }

    [Test]
    public void TryAdvance_ReturnsFalse_WhenChapterComplete()
    {
        GameState gameState = new GameState { Narrative = NewCompletedState() };

        bool result = Chapter01StoryDirector.TryAdvance(gameState, _ => true);

        Assert.IsFalse(result);
    }

    [Test]
    public void ContextBuilder_NoActiveExpedition_ReturnsEmptyCompanionsAndZeroPartySize()
    {
        GameState gameState = new GameState { Narrative = NewState() };

        Assert.IsEmpty(Chapter01ContextBuilder.GetPresentCompanionIds(gameState));
        Assert.AreEqual(0, Chapter01ContextBuilder.GetPartySize(gameState));
    }

    [Test]
    public void ContextBuilder_Build_UsesGameStateNarrativeAndSeed()
    {
        GameState gameState = new GameState { Narrative = NewState(), WorldSeed = 42 };
        gameState.Narrative.GrantItem(Chapter01Ids.Items.SevenToothGauge);
        HeroProfileData hero = new HeroProfileData();

        NarrativeEvaluationContext context = Chapter01ContextBuilder.Build(gameState, hero);

        Assert.AreEqual(42, context.WorldSeed);
        Assert.IsTrue(context.IsItemPresent(Chapter01Ids.Items.SevenToothGauge));
        Assert.AreSame(gameState.Narrative, context.State);
    }

    [Test]
    public void OutcomeApplier_ResourceDelta_AppliesOnlyOnce()
    {
        GameState gameState = new GameState { Narrative = NewState(), Food = 10, Gold = 5 };

        bool firstApply = Chapter01OutcomeApplier.ApplyResourceDelta(
            gameState, Chapter01Ids.Effects.FloodResourceLoss, foodDelta: -3, goldDelta: -2);
        bool secondApply = Chapter01OutcomeApplier.ApplyResourceDelta(
            gameState, Chapter01Ids.Effects.FloodResourceLoss, foodDelta: -3, goldDelta: -2);

        Assert.IsTrue(firstApply);
        Assert.IsFalse(secondApply);
        Assert.AreEqual(7, gameState.Food);
        Assert.AreEqual(3, gameState.Gold);
    }

    [Test]
    public void OutcomeApplier_TimeAdvance_AppliesOnlyOnce()
    {
        GameState gameState = new GameState { Narrative = NewState(), Day = 1 };

        Chapter01OutcomeApplier.ApplyTimeAdvance(gameState, Chapter01Ids.Effects.FloodTimeAdvance, 2);
        Chapter01OutcomeApplier.ApplyTimeAdvance(gameState, Chapter01Ids.Effects.FloodTimeAdvance, 2);

        Assert.AreEqual(3, gameState.Day);
    }

    [Test]
    public void OutcomeApplier_GrantItem_AppliesOnlyOnce()
    {
        GameState gameState = new GameState { Narrative = NewState() };

        Chapter01OutcomeApplier.GrantItem(gameState, Chapter01Ids.Effects.SevenToothGaugeGrant, Chapter01Ids.Items.SevenToothGauge);
        Chapter01OutcomeApplier.GrantItem(gameState, Chapter01Ids.Effects.SevenToothGaugeGrant, Chapter01Ids.Items.SevenToothGauge);

        Assert.IsTrue(gameState.Narrative.HasItem(Chapter01Ids.Items.SevenToothGauge));
        Assert.AreEqual(1, gameState.Narrative.Items.Count);
    }

    [Test]
    public void OutcomeApplier_MarkHeroInjured_SetsFlagOnce()
    {
        GameState gameState = new GameState { Narrative = NewState() };

        bool first = Chapter01OutcomeApplier.MarkHeroInjured(
            gameState, "chapter01.effect.test_injury", Chapter01Ids.Flags.HeroInjuredByFlood);
        bool second = Chapter01OutcomeApplier.MarkHeroInjured(
            gameState, "chapter01.effect.test_injury", Chapter01Ids.Flags.HeroInjuredByFlood);

        Assert.IsTrue(first);
        Assert.IsFalse(second);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.HeroInjuredByFlood));
    }
}
