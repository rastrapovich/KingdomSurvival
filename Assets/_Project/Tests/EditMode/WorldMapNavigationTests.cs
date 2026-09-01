using System;
using System.Collections.Generic;
using NUnit.Framework;

public class WorldMapNavigationTests
{
    [Test]
    public void FindPath_NeverCutsDiagonalAcrossBlockedCorner()
    {
        List<MapPointData> route = WorldMapNavigation.FindPath(
            22f,
            18f,
            48f,
            12f);

        Assert.That(route.Count, Is.GreaterThan(1));
        AssertRouteHasNoBlockedDiagonalCorners(route);
    }

    [Test]
    public void ContinuousReturn_StartsAtExactCurrentPosition()
    {
        GameState state = CreateTravellingState(12345);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        // Half a cell at x1: the army is deliberately between grid nodes.
        ContinuousSimulationSystem.Advance(state, 1f, false);

        float currentX = state.ActiveExpedition.CurrentMapXPercent;
        float currentY = state.ActiveExpedition.CurrentMapYPercent;

        string message;
        bool returning =
            ContinuousExpeditionCommands.TryOrderReturn(state, out message);

        Assert.That(returning, Is.True, message);
        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.ReturningToCastle));
        Assert.That(state.ActiveExpedition.Route.Count, Is.GreaterThan(1));

        MapPointData routeStart = state.ActiveExpedition.Route[0];
        Assert.That(routeStart.XPercent, Is.EqualTo(currentX).Within(0.001f));
        Assert.That(routeStart.YPercent, Is.EqualTo(currentY).Within(0.001f));
    }

    [Test]
    public void ContinuousRouteChange_PreservesMidCellPosition()
    {
        GameState state = CreateTravellingState(54321);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 1f, false);
        float currentX = state.ActiveExpedition.CurrentMapXPercent;
        float currentY = state.ActiveExpedition.CurrentMapYPercent;

        string message;
        Assert.That(
            state.TryChangeExpeditionRoute(
                15f,
                60f,
                null,
                out message),
            Is.True,
            message);

        ContinuousSimulationSystem.NotifyRouteChanged(state);

        MapPointData routeStart = state.ActiveExpedition.Route[0];
        Assert.That(routeStart.XPercent, Is.EqualTo(currentX).Within(0.001f));
        Assert.That(routeStart.YPercent, Is.EqualTo(currentY).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.CurrentMapXPercent,
            Is.EqualTo(currentX).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.CurrentMapYPercent,
            Is.EqualTo(currentY).Within(0.001f));
    }

    [TestCase(10f, 20f, "Западные земли")]
    [TestCase(50f, 10f, "Северные земли")]
    [TestCase(90f, 20f, "Восточные земли")]
    [TestCase(50f, 70f, "Центральные земли")]
    public void RegionName_UsesFourExpectedMapAreas(
        float x,
        float y,
        string expected)
    {
        Assert.That(
            GameState.GetRegionName(x, y),
            Is.EqualTo(expected));
    }

    private static GameState CreateTravellingState(int seed)
    {
        GameState state = new GameState();
        state.CreateNewGame(seed);

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

    private static void AssertRouteHasNoBlockedDiagonalCorners(
        List<MapPointData> route)
    {
        for (int i = 1; i < route.Count; i++)
        {
            int previousX =
                WorldMapNavigation.GridXFromPercent(route[i - 1].XPercent);
            int previousY =
                WorldMapNavigation.GridYFromPercent(route[i - 1].YPercent);
            int currentX =
                WorldMapNavigation.GridXFromPercent(route[i].XPercent);
            int currentY =
                WorldMapNavigation.GridYFromPercent(route[i].YPercent);

            int deltaX = currentX - previousX;
            int deltaY = currentY - previousY;

            if (Math.Abs(deltaX) != 1 || Math.Abs(deltaY) != 1)
                continue;

            Assert.That(
                WorldMapNavigation.IsBlockedGridCell(
                    previousX + deltaX,
                    previousY),
                Is.False,
                "Диагональ пересекает заблокированный горизонтальный угол.");
            Assert.That(
                WorldMapNavigation.IsBlockedGridCell(
                    previousX,
                    previousY + deltaY),
                Is.False,
                "Диагональ пересекает заблокированный вертикальный угол.");
        }
    }
}
