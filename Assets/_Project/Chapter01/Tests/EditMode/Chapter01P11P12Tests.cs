using KingdomSurvival.Chapter01;
using NUnit.Framework;

public sealed class Chapter01P11P12Tests
{
    private static GameState NewState(int seed = 912000)
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(seed);
        return gameState;
    }

    [Test]
    public void AgreementKnowledge_IsGuaranteed_WhenN14FlagExists()
    {
        GameState gameState = NewState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.AgreementRevealed);

        Chapter01ReturnFlow.EnsureAgreementKnowledge(gameState.Narrative);

        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem));
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.HomeWasNotSelfSufficient));
        Assert.IsTrue(gameState.Narrative.HasKnowledge(Chapter01Ids.Knowledge.OldAgreement));
    }

    [Test]
    public void N14Half_Branch_IsDerivedFromPersistedEffectExecutionId()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        state.MarkEffectApplied(Chapter01ReturnFlow.ContinueBranchEffectId);

        Assert.AreEqual(Chapter01ReturnBranch.FollowFreshTrace, Chapter01ReturnFlow.GetBranch(state));

        NarrativeStateData returnState = new NarrativeStateData();
        returnState.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        returnState.MarkEffectApplied(Chapter01ReturnFlow.ReturnNowBranchEffectId);
        Assert.AreEqual(Chapter01ReturnBranch.ReturnNow, Chapter01ReturnFlow.GetBranch(returnState));
    }

    [Test]
    public void ReturnRoadScene_RequiresPhysicalReturnProgress()
    {
        GameState gameState = NewState();
        CommanderData commander = gameState.GetSelectedCommander();
        commander.State = CommanderState.ReturningToCastle;
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        gameState.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            Phase = CommanderState.ReturningToCastle,
            RouteLengthCells = 20,
            RemainingRouteCells = 20
        };

        Assert.IsFalse(Chapter01ReturnFlow.IsReturnRoadSceneReady(gameState));

        gameState.ActiveExpedition.RemainingRouteCells = 14;
        Assert.IsTrue(Chapter01ReturnFlow.IsReturnRoadSceneReady(gameState));

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnRoadTraveled);
        Assert.IsFalse(Chapter01ReturnFlow.IsReturnRoadSceneReady(gameState));
    }

    [Test]
    public void ReturnRoadConsequence_IsIdempotentAndCostsTime()
    {
        GameState gameState = NewState();
        CommanderData commander = gameState.GetSelectedCommander();
        commander.State = CommanderState.ReturningToCastle;
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnRoadTraveled);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        gameState.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            Phase = CommanderState.ReturningToCastle,
            RouteLengthCells = 20,
            RemainingRouteCells = 10
        };

        Assert.IsTrue(Chapter01ReturnFlow.TryApplyReturnRoadConsequence(gameState));
        Assert.IsTrue(gameState.ActiveExpedition.HasTimedActivity);
        double firstDuration = gameState.ActiveExpedition.ActiveActivity.TotalHours;
        Assert.Greater(firstDuration, 0.0);

        gameState.ActiveExpedition.ActiveActivity = null;
        Assert.IsFalse(Chapter01ReturnFlow.TryApplyReturnRoadConsequence(gameState));
        Assert.IsNull(gameState.ActiveExpedition.ActiveActivity);
    }

    [Test]
    public void Homecoming_IsBlockedUntilPhysicalReturnIsComplete()
    {
        GameState gameState = NewState();
        CommanderData commander = gameState.GetSelectedCommander();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnRoadTraveled);
        commander.State = CommanderState.ReturningToCastle;
        gameState.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            Phase = CommanderState.ReturningToCastle
        };

        Assert.IsFalse(Chapter01ReturnFlow.IsHomecomingReady(gameState));

        gameState.ActiveExpedition.IsActive = false;
        commander.State = CommanderState.InCastle;
        Assert.IsTrue(Chapter01ReturnFlow.IsHomecomingReady(gameState));

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        Assert.IsFalse(Chapter01ReturnFlow.IsHomecomingReady(gameState));
    }

    [Test]
    public void EchoResolver_UsesRepairFloodAndN14HalfBranch()
    {
        NarrativeStateData oldRepair = new NarrativeStateData();
        oldRepair.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        oldRepair.SetFlag(Chapter01Ids.Flags.RepairOld);
        oldRepair.MarkEffectApplied(Chapter01ReturnFlow.ReturnNowBranchEffectId);
        Assert.AreEqual(Chapter01ReturnEchoKind.OldRepairReturnNow, Chapter01ReturnFlow.ResolveEcho(oldRepair).Kind);

        NarrativeStateData newRepairFollowed = new NarrativeStateData();
        newRepairFollowed.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        newRepairFollowed.SetFlag(Chapter01Ids.Flags.RepairNew);
        newRepairFollowed.MarkEffectApplied(Chapter01ReturnFlow.ContinueBranchEffectId);
        Assert.AreEqual(Chapter01ReturnEchoKind.NewRepairFollowTrace, Chapter01ReturnFlow.ResolveEcho(newRepairFollowed).Kind);

        newRepairFollowed.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        Assert.AreEqual(Chapter01ReturnEchoKind.FloodDamageFollowTrace, Chapter01ReturnFlow.ResolveEcho(newRepairFollowed).Kind);
    }

    [Test]
    public void Journal_AdvancesFromAgreementThroughCouncilOutcome()
    {
        GameState gameState = NewState();
        NarrativeStateData state = gameState.Narrative;
        state.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        state.SetFlag(Chapter01Ids.Flags.DownstreamContact);

        var goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":agreement", goals[0].RevisionId);

        state.SetFlag(Chapter01Ids.Flags.AgreementRevealed);
        goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":return_decision", goals[0].RevisionId);

        state.SetFlag(Chapter01Ids.Flags.ReturnStarted);
        goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":returning", goals[0].RevisionId);

        state.SetFlag(Chapter01Ids.Flags.ReturnRoadTraveled);
        goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":homeward", goals[0].RevisionId);

        state.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(JournalGoalState.Active, goals[0].State);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":final_council", goals[0].RevisionId);

        state.SetFlag(Chapter01Ids.Flags.CouncilOldOrderRestored);
        state.SetFlag(Chapter01Ids.Flags.CouncilCompleted);
        state.SetFlag(Chapter01Ids.Flags.Completed);
        goals = Chapter01JournalProvider.Build(gameState);
        Assert.AreEqual(JournalGoalState.Completed, goals[0].State);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":old_order", goals[0].RevisionId);
    }
}
