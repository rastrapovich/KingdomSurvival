using NUnit.Framework;

public class BuildingSystemTests
{
    [Test]
    public void OnlyOneBuildingCanBeConstructedAtATime()
    {
        GameState state = CreateState();

        string firstMessage;
        string secondMessage;
        Assert.IsTrue(BuildingSystem.TryStartConstruction(
            state,
            BuildingSystem.FieldsAndGranariesId,
            out firstMessage));
        Assert.IsFalse(BuildingSystem.TryStartConstruction(
            state,
            BuildingSystem.MarketId,
            out secondMessage));
        Assert.AreEqual(60, state.Gold);
        StringAssert.Contains("уже идёт строительство", secondMessage);
    }

    [Test]
    public void FoodBuildingCompletesOnStrategicClockAndChangesDailyEconomy()
    {
        GameState state = CreateState();
        string message;
        Assert.IsTrue(BuildingSystem.TryStartConstruction(
            state,
            BuildingSystem.FieldsAndGranariesId,
            out message));

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            state,
            ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.Advance(state, 6f, false);

        Assert.IsTrue(BuildingSystem.IsCompleted(
            state,
            BuildingSystem.FieldsAndGranariesId));
        Assert.AreEqual(17, BuildingSystem.GetDailyFoodIncome(state));
        Assert.AreEqual(1, BuildingSystem.GetDailyGoldUpkeep(state));
    }

    [Test]
    public void BarracksRecruitOneReplacementFighterOverTime()
    {
        GameState state = CreateState();
        state.Gold = 500;
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
        Assert.IsTrue(BuildingSystem.IsCompleted(state, BuildingSystem.BarracksId));

        int before = state.Fighters.Count;
        Assert.IsTrue(BuildingSystem.TryStartRecruitment(state, out message));
        Assert.AreEqual(500 - 100 - BuildingSystem.RecruitGoldCost + 3, state.Gold);

        ContinuousSimulationSystem.Advance(state, 4f, false);
        BuildingSystem.Synchronize(state);

        Assert.AreEqual(before + 1, state.Fighters.Count);
        Assert.AreEqual(BuildingSystem.PrototypeMaxFighters, state.Fighters.Count);
        Assert.AreEqual("Ополченец", state.Fighters[state.Fighters.Count - 1].Role);
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
