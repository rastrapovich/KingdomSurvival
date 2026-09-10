using System.Collections.Generic;
using NUnit.Framework;

// Модель "движение = течение времени" (производственная инструкция про
// глобальную карту и время): стратегическое время идёт ровно тогда, когда
// герой физически движется или занят явным времязатратным действием, и
// стоит во всех остальных случаях. ContinuousSimulationSystem.
// HasMovementOrActivityInProgress — единственный источник истины для этого
// факта; PrototypeUIController.RefreshAutoTimeState (не тестируется здесь
// напрямую — приватный метод MonoBehaviour) лишь читает его каждый кадр и
// вызывает SetPaused. Тесты проверяют сам предикат и его согласованность
// с Advance()/GameState — тем же способом, каким уже написаны
// TimedExpeditionActivityTests.cs/ContinuousSimulationTests.cs.
public sealed class ContinuousMovementTimeTests
{
    private static GameState CreateIdleState()
    {
        GameState state = new GameState();
        state.CreateNewGame(701);
        return state;
    }

    private static GameState CreateTravellingState()
    {
        GameState state = new GameState();
        state.CreateNewGame(702);

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

    private static GameState CreateArmyAtRuinsWithSupply()
    {
        GameState state = new GameState();
        state.CreateNewGame(703);
        LocationData ruins = state.FindLocation("ruins");

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                ruins.MapXPercent,
                ruins.MapYPercent,
                ruins.Id,
                false,
                new List<string> { "garrick" },
                out message),
            Is.True,
            message);

        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.CurrentMapXPercent = ruins.MapXPercent;
        state.ActiveExpedition.CurrentMapYPercent = ruins.MapYPercent;
        state.ActiveExpedition.RemainingRouteCells = 0;
        state.FindCommander(state.ActiveExpedition.CommanderId).State = CommanderState.AtLocation;
        state.ArmySupply = 100;
        return state;
    }

    private static double DistanceSquared(float x1, float y1, float x2, float y2)
    {
        double dx = x1 - x2;
        double dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    // --- HasMovementOrActivityInProgress: матрица состояний ---

    [Test]
    public void HasMovementOrActivityInProgress_NoExpedition_False()
    {
        GameState state = CreateIdleState();
        Assert.IsFalse(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state));
    }

    [Test]
    public void HasMovementOrActivityInProgress_TravellingToLocation_True()
    {
        GameState state = CreateTravellingState();
        Assert.IsTrue(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state));
    }

    [Test]
    public void HasMovementOrActivityInProgress_AtLocation_NoActivity_False()
    {
        GameState state = CreateArmyAtRuinsWithSupply();
        Assert.IsFalse(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state));
    }

    [Test]
    public void HasMovementOrActivityInProgress_ReturningToCastle_True()
    {
        GameState state = CreateArmyAtRuinsWithSupply();

        string message;
        Assert.That(state.TryOrderReturn(out message), Is.True, message);

        Assert.IsTrue(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state));
    }

    [Test]
    public void HasMovementOrActivityInProgress_TimedActivityAtLocation_True()
    {
        GameState state = CreateArmyAtRuinsWithSupply();

        string message;
        Assert.That(state.TryStartLocationResearch(out message), Is.True, message);

        Assert.IsTrue(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state));
    }

    // --- Герой стоит => время стоит ---

    [Test]
    public void IdleHero_WorldTimeDoesNotAdvance_WhenPausedByPredicate()
    {
        GameState state = CreateIdleState();
        ContinuousSimulationSystem.Reset(state);

        bool shouldRun = ContinuousSimulationSystem.HasMovementOrActivityInProgress(state);
        Assert.IsFalse(shouldRun);
        ContinuousSimulationSystem.SetPaused(state, !shouldRun);

        ContinuousClockSnapshot before = ContinuousSimulationSystem.GetClock(state);
        ContinuousSimulationSystem.Advance(state, 5f, false);
        ContinuousClockSnapshot after = ContinuousSimulationSystem.GetClock(state);

        Assert.AreEqual(before.Day, after.Day);
        Assert.AreEqual(before.HourOfDay, after.HourOfDay);
    }

    // --- Герой движется => время и позиция идут синхронно ---

    [Test]
    public void MovingHero_ClockAndPositionAdvanceTogether_WhenResumedByPredicate()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        bool shouldRun = ContinuousSimulationSystem.HasMovementOrActivityInProgress(state);
        Assert.IsTrue(shouldRun);
        ContinuousSimulationSystem.SetPaused(state, !shouldRun);

        float startX = state.ActiveExpedition.CurrentMapXPercent;
        float startY = state.ActiveExpedition.CurrentMapYPercent;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;
        int startDay = ContinuousSimulationSystem.GetClock(state).Day;

        ContinuousSimulationSystem.Advance(state, 2f, false);

        ContinuousClockSnapshot after = ContinuousSimulationSystem.GetClock(state);
        bool clockAdvanced = after.Day != startDay || after.HourOfDay > startHour;
        Assert.IsTrue(clockAdvanced, "Часы должны продвинуться вместе с движением.");

        bool positionOrRouteAdvanced =
            !state.HasActiveExpedition ||
            state.ActiveExpedition.CurrentMapXPercent != startX ||
            state.ActiveExpedition.CurrentMapYPercent != startY ||
            state.ActiveExpedition.RouteIndex > 0;
        Assert.IsTrue(positionOrRouteAdvanced, "Позиция/маршрут должны продвинуться синхронно с часами.");
    }

    // --- Прибытие останавливает и движение, и время ---

    [Test]
    public void ArrivalStopsMovement_PredicateBecomesFalse_ClockFreezesAfterward()
    {
        GameState state = new GameState();
        state.CreateNewGame(704);
        LocationData ruins = state.FindLocation("ruins");

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                ruins.MapXPercent, ruins.MapYPercent, ruins.Id, false,
                new List<string> { "garrick" }, out message),
            Is.True, message);

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        for (int i = 0;
             i < 500 && state.HasActiveExpedition &&
             state.ActiveExpedition.Phase == CommanderState.TravellingToLocation;
             i++)
        {
            ContinuousSimulationSystem.Advance(state, 5f, false);
        }

        Assert.IsTrue(state.HasActiveExpedition);
        Assert.AreEqual(CommanderState.AtLocation, state.ActiveExpedition.Phase);

        bool shouldRunAfterArrival = ContinuousSimulationSystem.HasMovementOrActivityInProgress(state);
        Assert.IsFalse(shouldRunAfterArrival);
        ContinuousSimulationSystem.SetPaused(state, !shouldRunAfterArrival);

        double hourAtArrival = ContinuousSimulationSystem.GetClock(state).HourOfDay;
        int dayAtArrival = ContinuousSimulationSystem.GetClock(state).Day;

        ContinuousSimulationSystem.Advance(state, 5f, false);

        ContinuousClockSnapshot after = ContinuousSimulationSystem.GetClock(state);
        Assert.AreEqual(dayAtArrival, after.Day);
        Assert.AreEqual(hourAtArrival, after.HourOfDay);
    }

    // --- Смена маршрута во время движения начинается с текущей позиции ---

    [Test]
    public void RerouteWhileMoving_StartsCloserToCurrentPosition_ThanToCapital()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 8f, false);

        Assert.IsTrue(state.HasActiveExpedition);
        Assert.AreEqual(
            CommanderState.TravellingToLocation,
            state.ActiveExpedition.Phase,
            "Тест рассчитан на середину пути — при необходимости скорректировать seed/цель.");

        float midX = state.ActiveExpedition.CurrentMapXPercent;
        float midY = state.ActiveExpedition.CurrentMapYPercent;

        string message;
        bool changed = state.TryChangeExpeditionRoute(10f, 85f, null, out message);
        Assert.IsTrue(changed, message);

        MapPointData routeStart = state.ActiveExpedition.Route[0];
        double distanceToMid = DistanceSquared(routeStart.XPercent, routeStart.YPercent, midX, midY);
        double distanceToCapital = DistanceSquared(
            routeStart.XPercent, routeStart.YPercent,
            WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent);

        Assert.Less(
            distanceToMid,
            distanceToCapital,
            "Новый маршрут должен начинаться от текущей позиции отряда, а не от Дома.");
    }
}
