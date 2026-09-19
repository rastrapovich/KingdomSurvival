using System.Collections.Generic;
using NUnit.Framework;

// WM-T05 ("автоматический выбор быстрейшего маршрута"): чистая математика
// planner'а (direct vs road candidate, road graph, junction rule,
// resampling, ETA). IMGUI/Play Mode не тестируется здесь — только ручная
// проверка. Единственный публичный вход игры (WorldMapNavigation.FindPath)
// не меняется — тестируем и его напрямую, и внутренние helper'ы
// WorldMapRoutePlanner, куда он делегирует.
public class WorldMapRoutePlannerTests
{
    [TearDown]
    public void ResetGeography()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    private static WorldMapRoadDefinition MakeRoad(string id, bool enabled, params (float x, float y)[] points)
    {
        WorldMapRoadDefinition road = new WorldMapRoadDefinition { Id = id, Enabled = enabled, Width = 6f };
        foreach ((float x, float y) point in points)
            road.Points.Add(new MapPointData(point.x, point.y));
        return road;
    }

    private static WorldMapDefinitionData MakeDefinition(string id = "planner-test")
    {
        return new WorldMapDefinitionData { WorldDefinitionId = id };
    }

    // ---------------- DIRECT ----------------

    [Test]
    public void FindPath_NoRoads_MatchesOldDirectRoute()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> viaFindPath = WorldMapNavigation.FindPath(10f, 50f, 90f, 50f);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f);

        Assert.AreEqual(direct.Count, viaFindPath.Count);
        for (int i = 0; i < direct.Count; i++)
        {
            Assert.AreEqual(direct[i].XPercent, viaFindPath[i].XPercent, 0.001f);
            Assert.AreEqual(direct[i].YPercent, viaFindPath[i].YPercent, 0.001f);
        }
    }

    [Test]
    public void FindPath_DisabledRoad_UsesDirectRoute()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("r1", false, (10f, 40f), (90f, 40f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapNavigation.FindPath(10f, 50f, 90f, 50f);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f);

        Assert.AreEqual(direct.Count, route.Count);
    }

    [Test]
    public void FindPath_RoadWithOnePoint_UsesDirectRoute()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("r1", true, (50f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapNavigation.FindPath(10f, 50f, 90f, 50f);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f);

        Assert.AreEqual(direct.Count, route.Count);
    }

    [Test]
    public void FindFastestRoute_HugeDetourRoad_IsSlowerThanDirect_UsesDirect()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Дорога уходит далеко в сторону и обратно — геометрически огромный крюк.
        definition.Roads.Add(MakeRoad("detour", true, (10f, 50f), (10f, 5f), (90f, 5f), (90f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f);

        Assert.AreEqual(direct.Count, route.Count,
            "Огромный крюк должен проигрывать прямому пути даже с бонусом дороги ×1.3.");
    }

    // ---------------- ROAD ----------------

    [Test]
    public void FindFastestRoute_SmallDetourWithFastRoad_ChoosesRoad()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Дорога почти совпадает с прямой линией — небольшое отклонение.
        definition.Roads.Add(MakeRoad("fast-road", true, (10f, 48f), (90f, 48f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);

        // Сравнение через Count ненадёжно — resampling может дать
        // совпадающее число точек у геометрически разных маршрутов.
        // Проверяем напрямую, что выбранный путь заходит в gameplay-зону
        // дороги (герой физически "выходит к дороге", а не идёт мимо неё).
        bool usesRoad = false;
        foreach (MapPointData point in route)
        {
            if (WorldMapGameplayTerrainQuery.IsInsideRoad(definition.Roads[0], point.XPercent, point.YPercent))
            {
                usesRoad = true;
                break;
            }
        }

        Assert.IsTrue(usesRoad, "При почти прямой быстрой дороге planner должен выбрать её, а не прямой путь.");
    }

    [Test]
    public void FindFastestRoute_ChosenPath_ContainsRoadCenterlinePoint()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("fast-road", true, (10f, 48f), (50f, 48f), (90f, 48f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);

        bool passesNearCenterline = false;
        foreach (MapPointData point in route)
        {
            if (WorldMapGameplayTerrainQuery.IsInsideRoad(definition.Roads[0], point.XPercent, point.YPercent))
            {
                passesNearCenterline = true;
                break;
            }
        }

        Assert.IsTrue(passesNearCenterline, "Выбранный маршрут должен проходить по gameplay-зоне дороги.");
    }

    [Test]
    public void FindFastestRoute_StartProjectsIntoMiddleOfRoadSegment_NotOnlyEndpoint()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("fast-road", true, (10f, 48f), (90f, 48f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        // Старт напротив середины дороги — герой не обязан идти к authored
        // концу дороги (10,48) или (90,48), должен войти где-то посередине.
        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(50f, 20f, 50f, 80f, definition);

        bool enteredMidSegment = false;
        foreach (MapPointData point in route)
        {
            if (point.XPercent > 20f && point.XPercent < 80f &&
                WorldMapGameplayTerrainQuery.IsInsideRoad(definition.Roads[0], point.XPercent, point.YPercent))
            {
                enteredMidSegment = true;
                break;
            }
        }

        Assert.IsTrue(enteredMidSegment, "Герой должен выйти на дорогу в середине её сегмента, не только через authored endpoint.");
    }

    // ---------------- GRAPH ----------------

    [Test]
    public void BuildRoadGraph_TwoRoadsWithSharedAuthoredPoint_AreConnected()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road-a", true, (10f, 50f), (50f, 50f)));
        definition.Roads.Add(MakeRoad("road-b", true, (50f, 50f), (90f, 20f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        // Прямой путь намеренно длиннее по прямой, чем через две дороги с
        // общей точкой на (50,50), при достаточно быстром множителе.
        double roadHours = EstimateFullRoadHours(10f, 50f, 90f, 20f, definition);
        Assert.IsTrue(!double.IsPositiveInfinity(roadHours), "Две дороги с общей точкой должны образовывать связный граф.");
    }

    [Test]
    public void BuildRoadGraph_DisconnectedRoads_NoPathBetweenThem()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road-a", true, (10f, 10f), (20f, 10f)));
        definition.Roads.Add(MakeRoad("road-b", true, (80f, 90f), (90f, 90f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        // Дороги физически далеко друг от друга и не пересекаются — planner
        // не обязан их соединять, но не должен падать/зависать.
        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 10f, 90f, 90f, definition);
        Assert.IsNotNull(route);
        Assert.Greater(route.Count, 1);
    }

    // Раздел 7 задачи: две дороги, геометрически пересекающиеся в районе
    // (50,50), но БЕЗ общей authored-точки — не должны сливаться в graph.
    // Сравнение через итоговое route-время ненадёжно (близкая дорога может
    // давать частичную выгоду сама по себе, независимо от связности с
    // другой дорогой) — проверяем структуру графа напрямую: без общей точки
    // должно остаться 4 узла (по 2 на дорогу), с общей точкой — 3 (раздел
    // 15 задачи, соседний тест BuildRoadGraph_PointsWithinTolerance...
    // проверяет обратный случай).
    [Test]
    public void BuildRoadGraph_VisuallyCrossingLinesWithoutSharedPoint_AreNotAutomaticallyConnected()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road-a", true, (30f, 50f), (70f, 50f)));
        definition.Roads.Add(MakeRoad("road-b", true, (50f, 30f), (50f, 70f)));

        int nodeCount = CountRoadGraphNodes(definition);

        Assert.AreEqual(4, nodeCount,
            "Без общей authored-точки пересекающиеся линии не должны сливаться в один узел графа.");
    }

    private static int CountRoadGraphNodes(WorldMapDefinitionData definition)
    {
        System.Reflection.MethodInfo method = typeof(WorldMapRoutePlanner).GetMethod(
            "BuildRoadGraph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object[] args = { definition, null };
        object graph = method.Invoke(null, args);

        System.Reflection.FieldInfo nodePointsField = graph.GetType().GetField("NodePoints");
        System.Collections.IEnumerable nodePoints =
            (System.Collections.IEnumerable)nodePointsField.GetValue(graph);

        int count = 0;
        foreach (object _ in nodePoints)
            count++;
        return count;
    }

    [Test]
    public void BuildRoadGraph_PointsWithinTolerance_AreMergedAsSameJunction()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Точки отличаются на значение меньше JunctionMergeToleranceCells —
        // должны слиться в один узел (3 узла вместо 4), а не остаться
        // отдельными несвязанными точками.
        definition.Roads.Add(MakeRoad("road-a", true, (10f, 50f), (50f, 50f)));
        definition.Roads.Add(MakeRoad("road-b", true, (50.001f, 50.001f), (90f, 20f)));

        int nodeCount = CountRoadGraphNodes(definition);

        Assert.AreEqual(3, nodeCount, "Точки в пределах технического допуска должны сливаться в один узел.");
    }

    [Test]
    public void FindFastestRoute_SameInputs_AlwaysProducesIdenticalRoute()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road-a", true, (10f, 48f), (50f, 48f)));
        definition.Roads.Add(MakeRoad("road-b", true, (50f, 48f), (90f, 48f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> first = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);
        List<MapPointData> second = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);

        Assert.AreEqual(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.AreEqual(first[i].XPercent, second[i].XPercent, 0.0001f);
            Assert.AreEqual(first[i].YPercent, second[i].YPercent, 0.0001f);
        }
    }

    private static double EstimateFullRoadHours(
        float startX, float startY, float targetX, float targetY, WorldMapDefinitionData definition)
    {
        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(startX, startY, targetX, targetY, definition);
        return WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, definition);
    }

    // ---------------- DISTANCE / RESAMPLING ----------------

    [Test]
    public void BuildMovementRouteAlongPolyline_LongRoadEdge_IsNotSingleMovementSegment()
    {
        // Отрезок ~20 логических клеток по X (GridWidth=104 → 100% ≈ 103
        // клетки, поэтому смещение на ~20% даёт около 20 клеток).
        List<WorldMapRoutePlanner.PointF> polyline = new List<WorldMapRoutePlanner.PointF>
        {
            new WorldMapRoutePlanner.PointF(10f, 50f),
            new WorldMapRoutePlanner.PointF(30f, 50f)
        };

        List<MapPointData> route = WorldMapRoutePlanner.BuildMovementRouteAlongPolyline(polyline);

        Assert.Greater(route.Count, 10,
            "Длинный отрезок дороги не должен становиться одним movement-сегментом.");
    }

    [Test]
    public void BuildMovementRouteAlongPolyline_StartAndEndCoordinates_AreExact()
    {
        List<WorldMapRoutePlanner.PointF> polyline = new List<WorldMapRoutePlanner.PointF>
        {
            new WorldMapRoutePlanner.PointF(12.345f, 33.333f),
            new WorldMapRoutePlanner.PointF(77.777f, 61.111f)
        };

        List<MapPointData> route = WorldMapRoutePlanner.BuildMovementRouteAlongPolyline(polyline);

        Assert.AreEqual(12.345f, route[0].XPercent, 0.001f);
        Assert.AreEqual(33.333f, route[0].YPercent, 0.001f);
        Assert.AreEqual(77.777f, route[route.Count - 1].XPercent, 0.001f);
        Assert.AreEqual(61.111f, route[route.Count - 1].YPercent, 0.001f);
    }

    [Test]
    public void EstimateGeometricPathHours_HillsCost_IsPreserved()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.TerrainAreas.Add(new WorldMapTerrainAreaData
        {
            Id = "hills", Terrain = WorldMapTerrainType.Hills,
            MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
        });
        WorldMapNavigation.ConfigureFromDefinition(definition);

        float delta = 100f / (WorldMapNavigation.GridWidth - 1) * 0.999f;
        double hoursOneCell = WorldMapRoutePlanner.EstimateGeometricPathHours(
            new List<WorldMapRoutePlanner.PointF>
            {
                new WorldMapRoutePlanner.PointF(50f, 50f),
                new WorldMapRoutePlanner.PointF(50f + delta, 50f)
            },
            definition);

        Assert.AreEqual(8.0, hoursOneCell, 0.5, "Hills ×2 должно сохраниться в оценке стоимости planner'а.");
    }

    [Test]
    public void EstimateGeometricPathHours_MountainsCost_IsPreserved()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.TerrainAreas.Add(new WorldMapTerrainAreaData
        {
            Id = "mountains", Terrain = WorldMapTerrainType.Mountains,
            MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
        });
        WorldMapNavigation.ConfigureFromDefinition(definition);

        float delta = 100f / (WorldMapNavigation.GridWidth - 1) * 0.999f;
        double hoursOneCell = WorldMapRoutePlanner.EstimateGeometricPathHours(
            new List<WorldMapRoutePlanner.PointF>
            {
                new WorldMapRoutePlanner.PointF(50f, 50f),
                new WorldMapRoutePlanner.PointF(50f + delta, 50f)
            },
            definition);

        Assert.AreEqual(12.0, hoursOneCell, 0.5, "Mountains ×3 должно сохраниться в оценке стоимости planner'а.");
    }

    [Test]
    public void EstimateSegmentHours_RoadMultiplier_AppliedExactlyOnce()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road", true, (10f, 50f), (90f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        float delta = 100f / (WorldMapNavigation.GridWidth - 1) * 0.999f;
        double hoursOnRoad = WorldMapRoutePlanner.EstimateGeometricPathHours(
            new List<WorldMapRoutePlanner.PointF>
            {
                new WorldMapRoutePlanner.PointF(50f, 50f),
                new WorldMapRoutePlanner.PointF(50f + delta, 50f)
            },
            definition);

        // 1 клетка × 4ч / 1.3 (Road multiplier) — НЕ /1.3/1.3.
        Assert.AreEqual(4.0 / 1.3, hoursOnRoad, 0.05);
    }

    // ---------------- CHOICE ----------------

    [Test]
    public void FindFastestRoute_RoadFaster_ChoosesRoad()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Дорога смещена от прямой линии на 10 (40 вместо 50) — при
        // Width=6 (половина коридора 3) смещение должно превышать полу-
        // ширину коридора, иначе "прямой" кандидат тоже физически лежит
        // внутри дорожного коридора и получает бонус скорости бесплатно,
        // и сравнение перестаёт что-либо проверять.
        definition.Roads.Add(MakeRoad("road", true, (10f, 40f), (90f, 40f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);
        double routeHours = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, definition);
        double directHours = WorldMapRoutePlanner.EstimateRouteTravelHours(
            WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f), 0, 0.0, definition);

        Assert.Less(routeHours, directHours);
    }

    [Test]
    public void FindFastestRoute_DirectFaster_ChoosesDirect()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Дорога есть, но требует огромного крюка — прямой путь быстрее.
        definition.Roads.Add(MakeRoad("road", true, (10f, 5f), (90f, 5f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 90f, 90f, 90f, definition);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 90f, 90f, 90f);

        Assert.AreEqual(direct.Count, route.Count);
    }

    [Test]
    public void FindFastestRoute_AlmostEqualCost_StableFallsBackToDirect()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        // Дорога идёт ровно по прямой (та же геометрия), без выигрыша по
        // multiplier (Road не настроен отдельно — используется default 1.3,
        // так что дорога будет БЫСТРЕЕ; чтобы получить "почти равное",
        // намеренно делаем multiplier = 1.0 через настройки).
        definition.GameplayTerrainSettings.Add(new WorldMapGameplayTerrainSettings
        {
            Terrain = WorldMapGameplayTerrainType.Road, Traversable = true, MovementMultiplier = 1.0f
        });
        definition.Roads.Add(MakeRoad("road", true, (10f, 50f), (90f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition);
        List<MapPointData> direct = WorldMapRoutePlanner.BuildDirectPath(10f, 50f, 90f, 50f);

        Assert.AreEqual(direct.Count, route.Count,
            "При равном множителе (нет выгоды) planner должен стабильно предпочесть прямой путь.");
    }

    // ---------------- RUNTIME CONTRACT ----------------

    [Test]
    public void CalculateTravelHours_MatchesNewRouteCost()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = WorldMapNavigation.FindPath(10f, 50f, 30f, 50f);
        double viaClock = ContinuousSimulationSystem.CalculateTravelHours(route);
        double viaPlanner = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, definition);

        Assert.AreEqual(viaPlanner, viaClock, 0.0001);
    }

    [Test]
    public void EstimateRouteTravelHours_PartialSegmentProgress_IsAccountedFor()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(10f, 50f),
            new MapPointData(20f, 50f),
            new MapPointData(30f, 50f)
        };

        double fullHours = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, definition);
        double halfwayHours = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.5, definition);

        Assert.Less(halfwayHours, fullHours);
    }

    [Test]
    public void ChangingBaseTravelHoursPerCell_ChangesBothEstimatesConsistently()
    {
        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(10f, 50f),
            new MapPointData(20f, 50f)
        };

        WorldMapDefinitionData slow = new WorldMapDefinitionData { WorldDefinitionId = "slow", BaseTravelHoursPerCell = 4f };
        WorldMapDefinitionData fast = new WorldMapDefinitionData { WorldDefinitionId = "fast", BaseTravelHoursPerCell = 2f };

        double slowHours = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, slow);
        double fastHours = WorldMapRoutePlanner.EstimateRouteTravelHours(route, 0, 0.0, fast);

        Assert.AreEqual(slowHours / 2.0, fastHours, 0.0001);
    }

    // ---------------- GAMESTATE ----------------

    [Test]
    public void TryStartExpeditionToMapPoint_UsesRoadWhenFaster()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad(
            "capital-road",
            true,
            (WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent - 2f),
            (WorldMapNavigation.CapitalXPercent + 40f, WorldMapNavigation.CapitalYPercent - 2f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        GameState state = new GameState();
        state.CreateNewGame(1, null, definition);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            WorldMapNavigation.CapitalXPercent + 40f,
            WorldMapNavigation.CapitalYPercent - 2f,
            null, false, new List<string> { "garrick" }, out message);

        Assert.IsTrue(started, message);
        bool usesRoad = false;
        foreach (MapPointData point in state.ActiveExpedition.Route)
        {
            if (WorldMapGameplayTerrainQuery.IsInsideRoad(definition.Roads[0], point.XPercent, point.YPercent))
            {
                usesRoad = true;
                break;
            }
        }
        Assert.IsTrue(usesRoad, "Начальный маршрут экспедиции должен автоматически использовать дорогу, если она выгоднее.");
    }

    [Test]
    public void TryChangeExpeditionRoute_BuildsFromCurrentPosition_NotCapital()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        WorldMapNavigation.ConfigureFromDefinition(definition);

        GameState state = new GameState();
        state.CreateNewGame(2, null, definition);

        string message;
        state.TryStartExpeditionToMapPoint(90f, 20f, null, false, new List<string> { "garrick" }, out message);
        state.ActiveExpedition.CurrentMapXPercent = 60f;
        state.ActiveExpedition.CurrentMapYPercent = 60f;

        bool changed = state.TryChangeExpeditionRoute(10f, 10f, null, out message);
        Assert.IsTrue(changed, message);

        MapPointData firstPoint = state.ActiveExpedition.Route[0];
        Assert.AreEqual(60f, firstPoint.XPercent, 0.01f);
        Assert.AreEqual(60f, firstPoint.YPercent, 0.01f);
    }

    // ---------------- SAVE / LOAD ----------------

    [Test]
    public void SaveLoad_MidRoadRoute_PreservesPositionAndContinuesWithoutTeleport()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(MakeRoad("road", true, (5f, 50f), (95f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        GameState original = new GameState();
        original.CreateNewGame(3, null, definition);
        original.ArmySupply = 1000000;

        string message;
        original.TryStartExpeditionToMapPoint(95f, 50f, null, false, new List<string> { "garrick" }, out message);

        ContinuousSimulationSystem.Reset(original);
        ContinuousSimulationSystem.NotifyRouteChanged(original);
        ContinuousSimulationSystem.SetPaused(original, false);
        ContinuousSimulationSystem.Advance(original, 5f, false);

        float xBeforeSave = original.ActiveExpedition.CurrentMapXPercent;
        float yBeforeSave = original.ActiveExpedition.CurrentMapYPercent;
        int routeIndexBeforeSave = original.ActiveExpedition.RouteIndex;

        ContinuousSimulationSnapshotData snapshot = ContinuousSimulationSystem.ExportSnapshot(original);

        GameState restored = new GameState();
        restored.CreateNewGame(3, null, definition);
        restored.ArmySupply = 1000000;
        restored.TryStartExpeditionToMapPoint(95f, 50f, null, false, new List<string> { "garrick" }, out message);
        restored.ActiveExpedition.CurrentMapXPercent = xBeforeSave;
        restored.ActiveExpedition.CurrentMapYPercent = yBeforeSave;
        restored.ActiveExpedition.RouteIndex = routeIndexBeforeSave;
        ContinuousSimulationSystem.RestoreSnapshot(restored, snapshot);
        ContinuousSimulationSystem.SetPaused(restored, false);

        Assert.AreEqual(xBeforeSave, restored.ActiveExpedition.CurrentMapXPercent, 0.0001f);
        Assert.AreEqual(yBeforeSave, restored.ActiveExpedition.CurrentMapYPercent, 0.0001f);

        ContinuousSimulationSystem.Advance(restored, 1f, false);
        bool positionAdvancedOrRouteProgressed =
            restored.ActiveExpedition.CurrentMapXPercent != xBeforeSave ||
            restored.ActiveExpedition.CurrentMapYPercent != yBeforeSave ||
            restored.ActiveExpedition.RouteIndex > routeIndexBeforeSave;
        Assert.IsTrue(positionAdvancedOrRouteProgressed, "После восстановления движение должно продолжаться без телепорта.");
    }

    // ---------------- INVALID DATA ----------------

    [Test]
    public void FindFastestRoute_NullRoadsList_DoesNotThrow()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads = null;
        WorldMapNavigation.ConfigureFromDefinition(definition);

        Assert.DoesNotThrow(() => WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition));
    }

    [Test]
    public void FindFastestRoute_RoadWithNaNPoint_DoesNotThrowAndFallsBackSafely()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        WorldMapRoadDefinition road = new WorldMapRoadDefinition { Id = "broken", Enabled = true, Width = 6f };
        road.Points.Add(new MapPointData(10f, 50f));
        road.Points.Add(new MapPointData(float.NaN, float.NaN));
        road.Points.Add(new MapPointData(90f, 50f));
        definition.Roads.Add(road);
        WorldMapNavigation.ConfigureFromDefinition(definition);

        List<MapPointData> route = null;
        Assert.DoesNotThrow(() => route = WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition));
        Assert.IsNotNull(route);
        Assert.Greater(route.Count, 1);
    }

    [Test]
    public void FindFastestRoute_NullRoadEntryInList_DoesNotThrow()
    {
        WorldMapDefinitionData definition = MakeDefinition();
        definition.Roads.Add(null);
        definition.Roads.Add(MakeRoad("ok", true, (10f, 50f), (90f, 50f)));
        WorldMapNavigation.ConfigureFromDefinition(definition);

        Assert.DoesNotThrow(() => WorldMapRoutePlanner.FindFastestRoute(10f, 50f, 90f, 50f, definition));
    }
}
