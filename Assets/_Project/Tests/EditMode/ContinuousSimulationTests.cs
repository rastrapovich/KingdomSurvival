using System.Collections.Generic;
using NUnit.Framework;

public class ContinuousSimulationTests
{
    [Test]
    public void Clock_StartsPausedAtDayOneEightHundred()
    {
        GameState state = new GameState();
        state.CreateNewGame(1234);
        ContinuousSimulationSystem.Reset(state);

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        Assert.That(state.Day, Is.EqualTo(1));
        Assert.That(clock.HourOfDay, Is.EqualTo(8.0).Within(0.001));
        Assert.That(clock.IsPaused, Is.True);
        Assert.That(clock.SpeedMultiplier, Is.EqualTo(1));
    }

    [Test]
    public void Clock_TwoRealMinutesAdvanceOneFullGameDayAtNormalSpeed()
    {
        GameState state = new GameState();
        state.CreateNewGame(4321);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 120f, false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        Assert.That(state.Day, Is.EqualTo(2));
        Assert.That(clock.HourOfDay, Is.EqualTo(8.0).Within(0.01));
    }

    [Test]
    public void Expedition_MovesHalfCellPerRealSecondAtNormalSpeed()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;

        ContinuousSimulationSystem.Advance(state, 2f, false);

        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex + 1));
    }

    [Test]
    public void Pause_StopsClockAndArmyMovement()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 10f, false);

        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex));
        Assert.That(
            ContinuousSimulationSystem.GetClock(state).HourOfDay,
            Is.EqualTo(startHour).Within(0.001));
    }

    [Test]
    public void FastSpeed_TriplesClockAndArmyMovement()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.ToggleSpeed(state);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 2f, false);

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);
        Assert.That(clock.HourOfDay - startHour, Is.EqualTo(1.2).Within(0.02));
        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex + 3));
    }

    [Test]
    public void RouteChange_MidCellPreservesExactArmyPosition()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 1f, false);
        float exactX = state.ActiveExpedition.CurrentMapXPercent;
        float exactY = state.ActiveExpedition.CurrentMapYPercent;

        string message;
        Assert.That(
            state.TryChangeExpeditionRoute(
                12f,
                65f,
                null,
                out message),
            Is.True,
            message);

        ContinuousSimulationSystem.NotifyRouteChanged(state);

        Assert.That(
            state.ActiveExpedition.CurrentMapXPercent,
            Is.EqualTo(exactX).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.CurrentMapYPercent,
            Is.EqualTo(exactY).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.Route[0].XPercent,
            Is.EqualTo(exactX).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.Route[0].YPercent,
            Is.EqualTo(exactY).Within(0.001f));
    }

    [Test]
    public void Midnight_ResolvesDailyEconomyOnce()
    {
        GameState state = new GameState();
        state.CreateNewGame(99);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        int startGold = state.Gold;
        int startFood = state.Food;
        int expectedFood =
            startFood + state.DailyFoodIncome - state.DailyFoodConsumption;

        // С 08:00 до первой полуночи — 16 игровых часов = 80 реальных секунд.
        ContinuousSimulationSystem.Advance(state, 80f, false);

        Assert.That(state.Day, Is.EqualTo(2));
        Assert.That(state.Gold, Is.EqualTo(startGold + state.DailyGoldIncome));
        Assert.That(state.Food, Is.EqualTo(expectedFood));
    }

    private static GameState CreateTravellingState()
    {
        GameState state = new GameState();
        state.CreateNewGame(2026);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            90f,
            20f,
            null,
            false,
            new List<string> { "garrick", "edric" },
            out message);

        Assert.That(started, Is.True, message);
        Assert.That(state.ActiveExpedition.Route.Count, Is.GreaterThan(5));
        return state;
    }
}
