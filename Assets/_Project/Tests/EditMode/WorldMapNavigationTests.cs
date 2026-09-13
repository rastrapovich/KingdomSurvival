using System;
using System.Collections.Generic;
using NUnit.Framework;

// AM-07.5 (канон v1.35, §9.9): процедурная генерация рельефа удалена —
// тесты, проверявшие её конкретное поведение (стабильность по seed, защита
// окрестности столицы, кластеризация Hills/Mountains), больше не имеют
// объекта проверки и удалены вместе с самой генерацией. Оставшиеся тесты
// используют ConfigureDefaultTerrain() — безопасную сплошную Plains, а не
// (не существующий более) процедурный генератор.
public class WorldMapNavigationTests
{
    [Test]
    public void FindPath_UsesStraightLineAndPreservesExactEndpoints()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
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
    public void ConfigureDefaultTerrain_IsEntirelyPlainsEverywhere()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();

        for (int y = 0; y < WorldMapNavigation.GridHeight; y += 7)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x += 7)
            {
                Assert.That(
                    WorldMapNavigation.GetTerrainAtGridCell(x, y),
                    Is.EqualTo(WorldMapTerrainType.Plains),
                    $"Без авторского мира география должна быть безопасной сплошной Plains, " +
                    "а не скрытым остатком процедурной генерации.");
            }
        }
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

    private static GameState CreateTravellingState(int seed)
    {
        GameState state = new GameState();
        state.CreateNewGame(seed);
        WorldMapNavigation.ConfigureDefaultTerrain();

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            90f,
            20f,
            null,
            false,
            new List<string> { "garrick", "edric", "marta", "torvin" },
            out message);

        Assert.That(started, Is.True, message);
        Assert.That(state.ActiveExpedition.FighterIds.Count, Is.EqualTo(4));
        Assert.That(state.ActiveExpedition.Route.Count, Is.GreaterThan(5));
        return state;
    }
}
