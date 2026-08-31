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
    public void AdvanceRoute_MovesFourCellsAndRemembersTravelledCells()
    {
        List<MapPointData> route = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent,
            90f,
            20f);

        Assert.That(route.Count, Is.GreaterThan(5));

        ExpeditionData expedition = new ExpeditionData
        {
            IsActive = true,
            Phase = CommanderState.TravellingToLocation,
            LocationId = "target",
            Route = route,
            RouteIndex = 0,
            DaysRemaining = WorldMapNavigation.CalculateDays(route),
            CurrentMapXPercent = route[0].XPercent,
            CurrentMapYPercent = route[0].YPercent,
            TargetMapXPercent = route[route.Count - 1].XPercent,
            TargetMapYPercent = route[route.Count - 1].YPercent
        };

        WorldMapNavigation.AdvanceRoute(expedition, 1);

        Assert.That(
            expedition.RouteIndex,
            Is.EqualTo(Math.Min(
                WorldMapNavigation.CellsPerDay,
                route.Count - 1)));
        Assert.That(
            expedition.LastTravelPoints.Count,
            Is.EqualTo(expedition.RouteIndex));
        Assert.That(
            expedition.CurrentMapXPercent,
            Is.EqualTo(route[expedition.RouteIndex].XPercent).Within(0.001f));
        Assert.That(
            expedition.CurrentMapYPercent,
            Is.EqualTo(route[expedition.RouteIndex].YPercent).Within(0.001f));
    }

    [Test]
    public void OrderReturn_StartsAtActualCurrentPosition()
    {
        GameState state = new GameState();
        state.CreateNewGame(12345);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            88f,
            18f,
            null,
            false,
            new List<string> { "garrick", "edric" },
            out message);

        Assert.That(started, Is.True, message);

        WorldMapNavigation.AdvanceRoute(
            state.ActiveExpedition,
            1);

        float currentX =
            state.ActiveExpedition.CurrentMapXPercent;
        float currentY =
            state.ActiveExpedition.CurrentMapYPercent;

        // После первого завершённого дня приказ уже нельзя просто отменить.
        state.Day = state.ActiveExpedition.StartedOnDay + 1;

        bool returning =
            state.TryOrderReturn(out message);

        Assert.That(returning, Is.True, message);
        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.ReturningToCastle));
        Assert.That(
            state.ActiveExpedition.Route.Count,
            Is.GreaterThan(1));

        MapPointData routeStart =
            state.ActiveExpedition.Route[0];

        Assert.That(
            WorldMapNavigation.GridXFromPercent(routeStart.XPercent),
            Is.EqualTo(
                WorldMapNavigation.GridXFromPercent(currentX)));
        Assert.That(
            WorldMapNavigation.GridYFromPercent(routeStart.YPercent),
            Is.EqualTo(
                WorldMapNavigation.GridYFromPercent(currentY)));
    }

    [Test]
    public void ChangeRoute_RebuildsFromActualCurrentPosition()
    {
        GameState state = new GameState();
        state.CreateNewGame(54321);

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                85f,
                20f,
                null,
                false,
                new List<string> { "garrick" },
                out message),
            Is.True,
            message);

        WorldMapNavigation.AdvanceRoute(
            state.ActiveExpedition,
            1);

        float currentX =
            state.ActiveExpedition.CurrentMapXPercent;
        float currentY =
            state.ActiveExpedition.CurrentMapYPercent;

        Assert.That(
            state.TryChangeExpeditionRoute(
                15f,
                60f,
                null,
                out message),
            Is.True,
            message);

        MapPointData routeStart =
            state.ActiveExpedition.Route[0];

        Assert.That(
            WorldMapNavigation.GridXFromPercent(routeStart.XPercent),
            Is.EqualTo(
                WorldMapNavigation.GridXFromPercent(currentX)));
        Assert.That(
            WorldMapNavigation.GridYFromPercent(routeStart.YPercent),
            Is.EqualTo(
                WorldMapNavigation.GridYFromPercent(currentY)));
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

    private static void AssertRouteHasNoBlockedDiagonalCorners(
        List<MapPointData> route)
    {
        for (int i = 1; i < route.Count; i++)
        {
            int previousX =
                WorldMapNavigation.GridXFromPercent(
                    route[i - 1].XPercent);
            int previousY =
                WorldMapNavigation.GridYFromPercent(
                    route[i - 1].YPercent);
            int currentX =
                WorldMapNavigation.GridXFromPercent(
                    route[i].XPercent);
            int currentY =
                WorldMapNavigation.GridYFromPercent(
                    route[i].YPercent);

            int deltaX = currentX - previousX;
            int deltaY = currentY - previousY;

            if (Math.Abs(deltaX) != 1 ||
                Math.Abs(deltaY) != 1)
            {
                continue;
            }

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
