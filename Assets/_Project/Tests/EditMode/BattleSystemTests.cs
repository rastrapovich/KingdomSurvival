using System.Collections.Generic;
using NUnit.Framework;

public class BattleSystemTests
{
    [Test]
    public void CapitalBattle_UsesOnlyFightersLeftInCapital()
    {
        GameState state = new GameState();
        state.CreateNewGame(1001);
        state.ArmySupply = 100;

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                90f,
                20f,
                null,
                false,
                new List<string> { "garrick", "edric" },
                out message),
            Is.True,
            message);

        Assert.That(BattleSystem.TryPrepareCapitalBattle(state, out message), Is.True, message);
        PendingBattleData pending = BattleSystem.GetPendingBattle(state);

        Assert.That(pending.Context.FighterIds, Does.Not.Contain("garrick"));
        Assert.That(pending.Context.FighterIds, Does.Not.Contain("edric"));
        Assert.That(pending.Context.FighterIds.Count, Is.EqualTo(3));
    }

    [Test]
    public void Resolver_IsDeterministicForSameDoctrineAndState()
    {
        GameState state = CreateAtForest(
            "garrick", "edric", "marta", "torvin", "agnessa");

        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);

        BattleResult first =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Balanced);
        BattleResult second =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Balanced);

        Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
        Assert.That(second.Phases.Count, Is.EqualTo(first.Phases.Count));
        Assert.That(second.FighterConsequences.Count, Is.EqualTo(first.FighterConsequences.Count));

        for (int i = 0; i < first.FighterConsequences.Count; i++)
        {
            Assert.That(
                second.FighterConsequences[i].AfterHitPoints,
                Is.EqualTo(first.FighterConsequences[i].AfterHitPoints));
            Assert.That(
                second.FighterConsequences[i].AfterState,
                Is.EqualTo(first.FighterConsequences[i].AfterState));
        }
    }

    [Test]
    public void Apply_UsesStoredPreviewWithoutReroll()
    {
        GameState state = CreateAtForest("garrick", "edric", "torvin");
        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);

        BattleResult preview =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Balanced);
        Dictionary<string, int> predictedHp = new Dictionary<string, int>();
        foreach (FighterBattleConsequence consequence in preview.FighterConsequences)
            predictedHp[consequence.FighterId] = consequence.AfterHitPoints;

        BattleResult applied;
        string report;
        Assert.That(
            BattleSystem.TryApplyPendingBattle(state, out applied, out report),
            Is.True,
            report);
        Assert.That(applied, Is.SameAs(preview));
        Assert.That(BattleSystem.HasPendingBattle(state), Is.False);

        foreach (KeyValuePair<string, int> pair in predictedHp)
        {
            FighterCombatState fighter =
                BattleSystem.GetFighterCombatState(state, pair.Key);

            if (pair.Value <= 0)
                Assert.That(state.FindFighter(pair.Key), Is.Null);
            else
                Assert.That((int)fighter.HitPoints, Is.EqualTo(pair.Value));
        }
    }

    [Test]
    public void HealthyFighter_CannotDieInSingleAssaultEvenOnDefeat()
    {
        GameState state = CreateAtForest("garrick");
        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);

        BattleResult result =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Assault);
        FighterBattleConsequence garrick =
            result.FighterConsequences.Find(item => item.FighterId == "garrick");

        Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Defeat));
        Assert.That(garrick.BeforeState, Is.EqualTo(FighterHealthState.Healthy));
        Assert.That(garrick.AfterState, Is.Not.EqualTo(FighterHealthState.Dead));
    }

    [Test]
    public void SeverelyWoundedFighter_CanDieAndIsRemovedFromRoster()
    {
        GameState state = CreateAtForest("garrick");
        FighterCombatState combat =
            BattleSystem.GetFighterCombatState(state, "garrick");
        combat.HitPoints = 20;

        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);
        BattleResult preview =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Assault);
        FighterBattleConsequence garrick =
            preview.FighterConsequences.Find(item => item.FighterId == "garrick");

        Assert.That(garrick.AfterState, Is.EqualTo(FighterHealthState.Dead));
        Assert.That(preview.DeathPossible, Is.True);

        BattleResult applied;
        string report;
        Assert.That(
            BattleSystem.TryApplyPendingBattle(state, out applied, out report),
            Is.True,
            report);
        Assert.That(state.FindFighter("garrick"), Is.Null);
        Assert.That(state.ActiveExpedition.FighterIds, Does.Not.Contain("garrick"));
    }

    [Test]
    public void Healer_ReducesOneSevereConsequence()
    {
        GameState state = CreateAtForest("garrick", "marta");
        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);

        BattleResult result =
            BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Balanced);
        FighterBattleConsequence garrick =
            result.FighterConsequences.Find(item => item.FighterId == "garrick");

        Assert.That(garrick.MitigatedByHealer, Is.True);
        Assert.That(garrick.AfterState, Is.EqualTo(FighterHealthState.Wounded));
    }

    [Test]
    public void Recovery_HealsOnlyFightersActuallyInCapital()
    {
        GameState state = new GameState();
        state.CreateNewGame(1002);
        state.ArmySupply = 100;

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                90f,
                20f,
                null,
                false,
                new List<string> { "garrick" },
                out message),
            Is.True,
            message);

        FighterCombatState away =
            BattleSystem.GetFighterCombatState(state, "garrick");
        FighterCombatState home =
            BattleSystem.GetFighterCombatState(state, "marta");
        away.HitPoints = 60;
        home.HitPoints = 60;

        BattleSystem.AdvanceCapitalRecovery(state, 10.0);

        Assert.That(away.HitPoints, Is.EqualTo(60).Within(0.001));
        Assert.That(home.HitPoints, Is.EqualTo(80).Within(0.001));
    }

    [Test]
    public void FullCapitalGarrison_WinsTestRaid()
    {
        GameState state = new GameState();
        state.CreateNewGame(1003);

        string message;
        Assert.That(BattleSystem.TryPrepareCapitalBattle(state, out message), Is.True, message);
        BattleResult result = BattleSystem.GetPendingBattle(state).Result;

        Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Victory));
        Assert.That(result.FoodDelta, Is.EqualTo(0));
        Assert.That(result.MoodDelta, Is.EqualTo(0));
    }

    [Test]
    public void WinningForestBattle_ResolvesEncounter()
    {
        GameState state = CreateAtForest(
            "garrick", "edric", "marta", "torvin", "agnessa");
        string message;
        Assert.That(
            BattleSystem.TryPrepareCurrentLocationBattle(state, out message),
            Is.True,
            message);

        BattleSystem.SelectPendingDoctrine(state, BattleDoctrine.Balanced);
        BattleResult applied;
        string report;
        Assert.That(
            BattleSystem.TryApplyPendingBattle(state, out applied, out report),
            Is.True,
            report);

        Assert.That(
            applied.Outcome == BattleOutcome.Victory ||
            applied.Outcome == BattleOutcome.CostlyVictory,
            Is.True);
        Assert.That(
            BattleSystem.HasUnresolvedLocationEncounter(state, "forest"),
            Is.False);
        Assert.That(state.FindLocation("forest").IsExplored, Is.True);
    }

    private static GameState CreateAtForest(params string[] fighterIds)
    {
        GameState state = new GameState();
        state.CreateNewGame(4242);
        state.ArmySupply = 100;

        string message;
        Assert.That(
            state.TryStartExpedition(
                "forest",
                new List<string>(fighterIds),
                out message),
            Is.True,
            message);

        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.LocationId = "forest";
        CommanderData commander =
            state.FindCommander(state.ActiveExpedition.CommanderId);
        commander.State = CommanderState.AtLocation;
        return state;
    }
}
