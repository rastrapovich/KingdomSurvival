using System;
using System.Collections.Generic;
using NUnit.Framework;

public class WorldMapNavigationTests
{
    [Test]
    public void FindPath_UsesStraightLineAndPreservesExactEndpoints()
    {
        WorldMapNavigation.ConfigureTerrain(12345);
        const float startX = 22f;
        const float startY = 18f;
        const float targetX = 83f;
        const float targetY = 67f;

        List<MapPointData> route = WorldMapNavigation.FindPath(
            startX, startY, targetX, targetY);

        Assert.That(route.Count, Is.GreaterThan(1));
        Assert.That(route[0].XPercent, Is.EqualTo(startX).Within(0.001f));
        Assert.That(route[0].YPercent, Is.EqualTo(startY).Within(0.001f));
        Assert.That(route[route.Count - 1].XPercent, Is.EqualTo(targetX).Within(0.001f));
        Assert.That(route[route.Count - 1].YPercent, Is.EqualTo(targetY).Within(0.001f));

        double dx = targetX - startX;
        double dy = targetY - startY;
        foreach (MapPointData point in route)
        {
            double cross = (point.XPercent - startX) * dy -
                           (point.YPercent - startY) * dx;
            Assert.That(Math.Abs(cross), Is.LessThan(0.02),
                "Все подточки маршрута должны оставаться на прямой линии.");
        }
    }

    [Test]
    public void TerrainGeneration_IsStableForSameSeedAndChangesAcrossSeeds()
    {
        int[] first = CaptureTerrain(777);
        int[] same = CaptureTerrain(777);
        int[] other = CaptureTerrain(778);

        CollectionAssert.AreEqual(first, same);
        CollectionAssert.AreNotEqual(first, other);
    }

    [Test]
    public void TerrainGeneration_KeepsCapitalNeighborhoodClear()
    {
        WorldMapNavigation.ConfigureTerrain(999);
        int capitalX = WorldMapNavigation.GridXFromPercent(
            WorldMapNavigation.CapitalXPercent);
        int capitalY = WorldMapNavigation.GridYFromPercent(
            WorldMapNavigation.CapitalYPercent);

        for (int y = capitalY - 2; y <= capitalY + 2; y++)
        {
            for (int x = capitalX - 2; x <= capitalX + 2; x++)
            {
                Assert.That(
                    WorldMapNavigation.GetTerrainAtGridCell(x, y),
                    Is.EqualTo(WorldMapTerrainType.Plains));
            }
        }
    }

    [Test]
    public void TerrainGeneration_CreatesClusteredHillsAndMountains()
    {
        WorldMapNavigation.ConfigureTerrain(424242);
        Assert.That(HasAdjacentPair(WorldMapTerrainType.Hills), Is.True);
        Assert.That(HasAdjacentPair(WorldMapTerrainType.Mountains), Is.True);
    }

    [Test]
    public void TerrainTravelCost_MatchesApprovedMultipliers()
    {
        Assert.That(
            WorldMapNavigation.GetTerrainTravelCost(WorldMapTerrainType.Plains),
            Is.EqualTo(1));
        Assert.That(
            WorldMapNavigation.GetTerrainTravelCost(WorldMapTerrainType.Hills),
            Is.EqualTo(2));
        Assert.That(
            WorldMapNavigation.GetTerrainTravelCost(WorldMapTerrainType.Mountains),
            Is.EqualTo(3));
        Assert.That(
            WorldMapNavigation.GetTerrainSpeedMultiplier(WorldMapTerrainType.Hills),
            Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(
            WorldMapNavigation.GetTerrainSpeedMultiplier(WorldMapTerrainType.Mountains),
            Is.EqualTo(1f / 3f).Within(0.0001f));
    }

    [Test]
    public void ContinuousReturn_StartsAtExactCurrentPosition()
    {
        GameState state = CreateTravellingState(12345);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

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
        MapPointData routeEnd =
            state.ActiveExpedition.Route[state.ActiveExpedition.Route.Count - 1];
        Assert.That(routeStart.XPercent, Is.EqualTo(currentX).Within(0.001f));
        Assert.That(routeStart.YPercent, Is.EqualTo(currentY).Within(0.001f));
        Assert.That(
            routeEnd.XPercent,
            Is.EqualTo(WorldMapNavigation.CapitalXPercent).Within(0.001f));
        Assert.That(
            routeEnd.YPercent,
            Is.EqualTo(WorldMapNavigation.CapitalYPercent).Within(0.001f));
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
            state.TryChangeExpeditionRoute(15f, 60f, null, out message),
            Is.True,
            message);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        MapPointData routeStart = state.ActiveExpedition.Route[0];
        Assert.That(routeStart.XPercent, Is.EqualTo(currentX).Within(0.001f));
        Assert.That(routeStart.YPercent, Is.EqualTo(currentY).Within(0.001f));
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
        Assert.That(GameState.GetRegionName(x, y), Is.EqualTo(expected));
    }

    private static int[] CaptureTerrain(int seed)
    {
        WorldMapNavigation.ConfigureTerrain(seed);
        int[] values = new int[
            WorldMapNavigation.GridWidth * WorldMapNavigation.GridHeight];
        int index = 0;
        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
                values[index++] = (int)WorldMapNavigation.GetTerrainAtGridCell(x, y);
        }
        return values;
    }

    private static bool HasAdjacentPair(WorldMapTerrainType terrain)
    {
        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
            {
                if (WorldMapNavigation.GetTerrainAtGridCell(x, y) != terrain)
                    continue;
                if (x + 1 < WorldMapNavigation.GridWidth &&
                    WorldMapNavigation.GetTerrainAtGridCell(x + 1, y) == terrain)
                    return true;
                if (y + 1 < WorldMapNavigation.GridHeight &&
                    WorldMapNavigation.GetTerrainAtGridCell(x, y + 1) == terrain)
                    return true;
            }
        }
        return false;
    }

    private static GameState CreateTravellingState(int seed)
    {
        GameState state = new GameState();
        state.CreateNewGame(seed);
        WorldMapNavigation.ConfigureTerrain(seed);

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
