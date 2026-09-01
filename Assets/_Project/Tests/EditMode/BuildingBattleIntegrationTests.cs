using NUnit.Framework;

public class BuildingBattleIntegrationTests
{
    [Test]
    public void CapitalBuildingsReduceEffectiveEnemyAttackAndKeepEnemyRoster()
    {
        GameState state = CreateState();
        state.Gold = 1000;

        string message;
        Assert.IsTrue(BuildingSystem.TryStartConstruction(
            state,
            BuildingSystem.BarracksId,
            out message));

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            state,
            ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.Advance(state, 10f, false);
        Assert.IsTrue(BuildingSystem.IsCompleted(
            state,
            BuildingSystem.BarracksId));

        Assert.IsTrue(BuildingSystem.TryStartConstruction(
            state,
            BuildingSystem.CityWallsId,
            out message));
        ContinuousSimulationSystem.Advance(state, 14f, false);
        Assert.IsTrue(BuildingSystem.IsCompleted(
            state,
            BuildingSystem.CityWallsId));

        Assert.IsTrue(BattleSystem.TryPrepareCapitalBattle(state, out message));
        PendingBattleData pending = BattleSystem.GetPendingBattle(state);
        Assert.IsNotNull(pending);
        Assert.AreEqual(4, pending.Context.Enemies.Count);

        int attackBefore = SumEnemyAttack(pending);
        int approachBefore = pending.Result.Phases[0].EnemyScore;

        string defenseMessage =
            BuildingBattleIntegration.ApplyCapitalDefenseToPendingBattle(state);

        Assert.IsNotEmpty(defenseMessage);
        Assert.AreEqual(4, pending.Context.Enemies.Count);
        Assert.Less(SumEnemyAttack(pending), attackBefore);
        Assert.Less(pending.Result.Phases[0].EnemyScore, approachBefore);
        StringAssert.Contains(
            "Оборона построек",
            pending.Context.Description);
    }

    private static int SumEnemyAttack(PendingBattleData pending)
    {
        int total = 0;
        foreach (BattleEnemyUnit enemy in pending.Context.Enemies)
            total += enemy.AttackPower;
        return total;
    }

    private static GameState CreateState()
    {
        GameState state = new GameState();
        state.CreateNewGame(12345);
        ContinuousSimulationSystem.Reset(state);
        BuildingSystem.Reset(state);
        return state;
    }
}
