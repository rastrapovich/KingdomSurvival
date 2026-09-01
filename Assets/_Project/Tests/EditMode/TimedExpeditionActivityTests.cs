using System.Collections.Generic;
using NUnit.Framework;

public class TimedExpeditionActivityTests
{
    [Test]
    public void LocationResearch_TakesConfiguredHoursAndRewardsOnCompletion()
    {
        GameState state = CreateArmyAtRuins();
        LocationData ruins = state.FindLocation("ruins");
        state.ArmySupply = 100;
        ContinuousSimulationSystem.Reset(state);

        string message;
        Assert.That(
            state.TryStartLocationResearch(out message),
            Is.True,
            message);
        Assert.That(ruins.ExplorationHours, Is.EqualTo(2.0));

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, 5f, false);

        Assert.That(ruins.IsExplored, Is.False);
        Assert.That(state.ArmyGold, Is.EqualTo(0));
        Assert.That(state.ArmySupply, Is.EqualTo(100));
        Assert.That(
            state.ActiveExpedition.ActiveActivity.RemainingHours,
            Is.EqualTo(1.0).Within(0.01));

        ContinuousSimulationBatch completed =
            ContinuousSimulationSystem.Advance(state, 5f, false);

        Assert.That(ruins.IsExplored, Is.True);
        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmyGold, Is.EqualTo(100));
        Assert.That(state.ArmySupply, Is.EqualTo(300));
        Assert.That(completed.RequestAutoPause, Is.True);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.True);
    }

    [Test]
    public void GatherBerries_StopsForThreeHoursThenRewardsAndResumesRoute()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;
        MakeEveryLocationVisible(state);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        state.ActiveExpedition.PendingDecision =
            new ExpeditionDecisionOccurrence
            {
                DefinitionId = "berry_bushes",
                Title = "Ягодные заросли"
            };

        string message;
        Assert.That(
            ExpeditionDecisionSystem.TryApplyChoice(
                state,
                "gather_berries",
                out message),
            Is.True,
            message);

        int startRouteIndex = state.ActiveExpedition.RouteIndex;
        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(state.ActiveExpedition.IsRoadStopInProgress, Is.True);
        Assert.That(
            state.ActiveExpedition.ActiveActivity.TotalHours,
            Is.EqualTo(3.0));

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, 10f, false);

        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(state.ActiveExpedition.RouteIndex, Is.EqualTo(startRouteIndex));

        ContinuousSimulationBatch completed =
            ContinuousSimulationSystem.Advance(state, 7f, false);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmySupply, Is.EqualTo(13));
        Assert.That(
            state.ActiveExpedition.RouteIndex,
            Is.EqualTo(startRouteIndex + 1));
        Assert.That(completed.RequestAutoPause, Is.False);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.False);
    }

    [Test]
    public void RouteChange_CancelsRoadActivityWithoutReward()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;

        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "berries",
                "СБОР ЯГОД",
                3.0,
                0,
                3,
                out message),
            Is.True,
            message);

        Assert.That(
            state.TryChangeExpeditionRoute(
                12f,
                65f,
                null,
                out message),
            Is.True,
            message);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmySupply, Is.EqualTo(10));
    }

    [Test]
    public void ReturnOrder_CancelsRoadActivityWithoutReward()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;
        MakeEveryLocationVisible(state);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, 2f, false);
        ContinuousSimulationSystem.SetPaused(state, true);

        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "berries",
                "СБОР ЯГОД",
                3.0,
                0,
                3,
                out message),
            Is.True,
            message);

        Assert.That(
            ContinuousExpeditionCommands.TryOrderReturn(state, out message),
            Is.True,
            message);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.ReturningToCastle));
    }

    [Test]
    public void WaypointArrival_StopsWithoutModalOrAutoPause()
    {
        GameState state = new GameState();
        state.CreateNewGame(603);

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                51f,
                80.5f,
                null,
                false,
                new List<string> { "garrick", "edric" },
                out message),
            Is.True,
            message);

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationBatch arrival =
            ContinuousSimulationSystem.Advance(state, 2.1f, false);

        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.AtLocation));
        Assert.That(arrival.MandatoryNotice, Is.Null);
        Assert.That(arrival.RequestAutoPause, Is.False);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.False);
    }

    [Test]
    public void TravelEstimate_UsesContinuousArmySpeed()
    {
        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(0f, 0f),
            new MapPointData(1f, 0f),
            new MapPointData(2f, 0f),
            new MapPointData(3f, 0f),
            new MapPointData(4f, 0f),
            new MapPointData(5f, 0f)
        };

        Assert.That(
            ContinuousSimulationSystem.CellsPerGameHour,
            Is.EqualTo(2.5).Within(0.001));
        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(2.0).Within(0.001));
    }

    private static GameState CreateTravellingState()
    {
        GameState state = new GameState();
        state.CreateNewGame(602);

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
        return state;
    }

    private static GameState CreateArmyAtRuins()
    {
        GameState state = new GameState();
        state.CreateNewGame(601);
        LocationData ruins = state.FindLocation("ruins");

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                ruins.MapXPercent,
                ruins.MapYPercent,
                ruins.Id,
                false,
                new List<string> { "garrick", "edric" },
                out message),
            Is.True,
            message);

        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.CurrentMapXPercent = ruins.MapXPercent;
        state.ActiveExpedition.CurrentMapYPercent = ruins.MapYPercent;
        state.ActiveExpedition.RemainingRouteCells = 0;
        state.FindCommander(state.ActiveExpedition.CommanderId).State =
            CommanderState.AtLocation;
        return state;
    }

    private static void MakeEveryLocationVisible(GameState state)
    {
        foreach (LocationData location in state.Locations)
            location.IsVisibleOnMap = true;
    }
}
