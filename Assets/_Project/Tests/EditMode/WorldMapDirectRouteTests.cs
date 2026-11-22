using System.Collections.Generic;
using NUnit.Framework;

// 22.11.2026: герой идёт строго к точке клика (без галочки «Выгодный путь»),
// с галочкой — прежний поиск выгодного пути; место открывается, только
// если герой дошёл до него вплотную.
public class WorldMapDirectRouteTests
{
    [SetUp]
    public void SetUp()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
        WorldMapNavigation.PreferProfitableRoutes = false;
        WorldMapObstacles.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
        WorldMapNavigation.PreferProfitableRoutes = false;
        WorldMapObstacles.Clear();
    }

    [Test]
    public void HeroPath_WithoutProfitableRoute_GoesStraightThroughSwamp()
    {
        WorldMapNavigation.ConfigureTerrainLayer(SwampWithRoadAround());

        List<MapPointData> path = WorldMapNavigation.FindHeroPath(22f, 50f, 78f, 50f);

        Assert.That(path.Count, Is.EqualTo(2), "Прямая до цели — без промежуточных точек.");
        Assert.That(path[1].XPercent, Is.EqualTo(78f).Within(0.01f));
        Assert.That(path[1].YPercent, Is.EqualTo(50f).Within(0.01f));
    }

    [Test]
    public void HeroPath_WithProfitableRoute_UsesRoad()
    {
        WorldMapNavigation.ConfigureTerrainLayer(SwampWithRoadAround());
        WorldMapNavigation.PreferProfitableRoutes = true;

        List<MapPointData> path = WorldMapNavigation.FindHeroPath(22f, 50f, 78f, 50f);

        bool usesRoad = false;
        foreach (MapPointData point in path)
            usesRoad |= point.YPercent > 61f;
        Assert.That(usesRoad, Is.True, "С галочкой путь уходит на дорогу.");
    }

    [Test]
    public void HeroPath_WithoutProfitableRoute_RoundsWaterByShortestWay_AndEndsAtClick()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 48f, 30f, 52f, 70f, WorldMapGameplayTerrainType.Water);
        WorldMapNavigation.ConfigureTerrainLayer(layer);

        List<MapPointData> path = WorldMapNavigation.FindHeroPath(30f, 50f, 70f, 50f);

        MapPointData end = path[path.Count - 1];
        Assert.That(end.XPercent, Is.EqualTo(70f).Within(0.01f), "Конец пути — точно точка клика.");
        Assert.That(end.YPercent, Is.EqualTo(50f).Within(0.01f));
        for (int i = 1; i < path.Count; i++)
            for (int s = 0; s <= 20; s++)
            {
                float t = s / 20f;
                float x = path[i - 1].XPercent + (path[i].XPercent - path[i - 1].XPercent) * t;
                float y = path[i - 1].YPercent + (path[i].YPercent - path[i - 1].YPercent) * t;
                Assert.That(WorldMapNavigation.GetTerrainAtPercent(x, y),
                    Is.Not.EqualTo(WorldMapGameplayTerrainType.Water), "Путь не идёт по воде.");
            }
        double straight = WorldMapNavigation.DistanceHexes(30f, 50f, 70f, 50f);
        Assert.That(WorldMapNavigation.PathLengthHexes(path), Is.LessThan(straight * 1.6),
            "Обход — короткий, без странных петель.");
    }

    [Test]
    public void ReachedOrderPoint_OnlyWhenClose()
    {
        double radius = WorldMapNavigation.InteractionRadiusHexes;
        Assert.That(radius, Is.EqualTo(WorldMapMovementRules.DefaultInteractionRadiusHexes).Within(0.0001));
        Assert.That(WorldMapNavigation.ReachedOrderPoint(50f, 50f, 50f, 50f), Is.True);

        // Точка в двух клетках — не вплотную.
        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        float twoHexes = (float)(2.0 * grid.HexWidth / grid.CanvasWidth * 100.0);
        Assert.That(WorldMapNavigation.ReachedOrderPoint(50f, 50f, 50f + twoHexes, 50f), Is.False);
    }

    [Test]
    public void Arrival_AtUnreachableLocation_StopsShortWithoutOpeningIt()
    {
        GameState state = new GameState();
        state.CreateNewGame(20261122);
        // Остров: место окружено водой — дойти вплотную нельзя.
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 60f, 40f, 80f, 60f, WorldMapGameplayTerrainType.Water);
        WorldMapNavigation.ConfigureTerrainLayer(layer);
        state.ArmySupply = 100;
        foreach (LocationData location in state.Locations)
            location.IsVisibleOnMap = true;

        LocationData island = null;
        foreach (LocationData location in state.Locations)
            if (!location.IsWaypoint)
            {
                island = location;
                break;
            }
        Assert.That(island, Is.Not.Null);
        island.MapXPercent = 70f;
        island.MapYPercent = 50f;
        island.IsVisibleOnMap = true;

        Assert.That(state.TryStartExpeditionToMapPoint(
            70f, 50f, island.Id, false, new List<string>(), out string message), Is.True, message);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        for (int frame = 0; frame < 4000 && state.ActiveExpedition.Phase == CommanderState.TravellingToLocation; frame++)
            ContinuousSimulationSystem.Advance(state, 0.05f, false);

        Assert.That(state.ActiveExpedition.Phase, Is.EqualTo(CommanderState.AtLocation));
        Assert.That(state.ActiveExpedition.LocationId, Is.Not.EqualTo(island.Id),
            "До острова не дойти вплотную — место не открывается.");
    }

    private static WorldMapTerrainLayer SwampWithRoadAround()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 25f, 40f, 75f, 60f, WorldMapGameplayTerrainType.Swamp);
        PaintRect(layer, 20f, 62f, 80f, 66f, WorldMapGameplayTerrainType.Road);
        PaintRect(layer, 20f, 50f, 25f, 66f, WorldMapGameplayTerrainType.Road);
        PaintRect(layer, 75f, 50f, 80f, 66f, WorldMapGameplayTerrainType.Road);
        return layer;
    }

    private static void PaintRect(
        WorldMapTerrainLayer layer,
        float minXPercent,
        float minYPercent,
        float maxXPercent,
        float maxYPercent,
        WorldMapGameplayTerrainType terrain)
    {
        WorldMapHexGrid grid = layer.Grid;
        for (int index = 0; index < grid.CellCount; index++)
        {
            WorldMapHexCell cell = grid.CellAt(index);
            grid.CellCenter(cell, out double x, out double y);
            float xPercent = grid.PixelToPercentX(x);
            float yPercent = grid.PixelToPercentY(y);
            if (xPercent >= minXPercent && xPercent <= maxXPercent &&
                yPercent >= minYPercent && yPercent <= maxYPercent)
                layer.Set(cell, terrain);
        }
    }
}
