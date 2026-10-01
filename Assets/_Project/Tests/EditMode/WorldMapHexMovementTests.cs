using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEngine;

// 12И (канон v1.50 §9, §9.6, §9.9): прямое управление героем на карте —
// шестиугольная сетка, разметка, обход препятствий, непрерывное движение и
// часы, идущие в темпе бега.
public class WorldMapHexMovementTests
{
    [SetUp]
    public void SetUp()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    [TearDown]
    public void TearDown()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    // ------------------------------------------------------------------
    // Сетка
    // ------------------------------------------------------------------

    [Test]
    public void Grid_CellCenterMapsBackToSameCell()
    {
        WorldMapHexGrid grid = WorldMapHexGrid.CreateDefault();
        for (int row = 0; row < grid.Rows; row += 3)
        {
            for (int column = 0; column < grid.Columns; column += 7)
            {
                WorldMapHexCell cell = new WorldMapHexCell(column, row);
                grid.CellCenter(cell, out double x, out double y);
                Assert.That(grid.CellAtPixel(x, y), Is.EqualTo(cell), cell.ToString());
            }
        }
    }

    [Test]
    public void Grid_NeighborsAreOneStepAwayAndOneHexWidthApart()
    {
        WorldMapHexGrid grid = WorldMapHexGrid.CreateDefault();
        WorldMapHexCell[] centers = { new WorldMapHexCell(10, 10), new WorldMapHexCell(11, 11) };
        foreach (WorldMapHexCell center in centers)
        {
            grid.CellCenter(center, out double cx, out double cy);
            for (int direction = 0; direction < 6; direction++)
            {
                WorldMapHexCell neighbor = center.Neighbor(direction);
                Assert.That(center.DistanceTo(neighbor), Is.EqualTo(1));
                grid.CellCenter(neighbor, out double nx, out double ny);
                double distance = Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy));
                Assert.That(distance, Is.EqualTo(grid.HexWidth).Within(0.001));
            }
        }
    }

    [Test]
    public void Grid_DefaultSizeMakesOneHexOnePercentOfWidth()
    {
        WorldMapHexGrid grid = WorldMapHexGrid.CreateDefault();
        Assert.That(grid.HexesAcross, Is.EqualTo(WorldMapHexGrid.DefaultHexesAcross));
        Assert.That(grid.DistanceHexes(10f, 50f, 11f, 50f), Is.EqualTo(1.0).Within(0.0001));
        Assert.That(grid.Columns * grid.HexWidth, Is.GreaterThanOrEqualTo(grid.CanvasWidth));
        Assert.That((grid.Rows - 1) * grid.RowStep, Is.GreaterThanOrEqualTo(grid.CanvasHeight));
    }

    // ------------------------------------------------------------------
    // Разметка
    // ------------------------------------------------------------------

    [Test]
    public void TerrainLayer_EncodeDecodeRoundTrip()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        layer.Set(new WorldMapHexCell(3, 4), WorldMapGameplayTerrainType.Water);
        layer.Set(new WorldMapHexCell(4, 4), WorldMapGameplayTerrainType.Water);
        layer.Set(new WorldMapHexCell(50, 30), WorldMapGameplayTerrainType.Road);
        layer.Set(new WorldMapHexCell(70, 60), WorldMapGameplayTerrainType.Cliffs);

        string encoded = layer.Encode();
        WorldMapTerrainLayer decoded = WorldMapTerrainLayer.Decode(layer.Grid, encoded);

        Assert.That(decoded.CopyCells(), Is.EqualTo(layer.CopyCells()));
        Assert.That(encoded.Length, Is.LessThan(100), "Повторы сжимаются в «число*тип».");
        Assert.That(WorldMapTerrainLayer.Decode(layer.Grid, "мусор,,3*x").IsEmpty, Is.True);
    }

    [Test]
    public void TerrainLayer_ResampleKeepsMarkupUnderCellCenters()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 40f, 20f, 60f, 80f, WorldMapGameplayTerrainType.Water);

        WorldMapHexGrid finer = new WorldMapHexGrid(layer.Grid.CanvasWidth, layer.Grid.CanvasHeight, 200);
        WorldMapTerrainLayer resampled = layer.ResampleTo(finer);

        Assert.That(resampled.GetAtPercent(50f, 50f), Is.EqualTo(WorldMapGameplayTerrainType.Water));
        Assert.That(resampled.GetAtPercent(20f, 50f), Is.EqualTo(WorldMapGameplayTerrainType.OpenGround));
    }

    // ------------------------------------------------------------------
    // Поиск пути
    // ------------------------------------------------------------------

    [Test]
    public void FindPath_OpenMapIsStraightLine()
    {
        List<MapPointData> path = WorldMapNavigation.FindPath(20f, 30f, 70f, 60f);

        Assert.That(path.Count, Is.EqualTo(2));
        Assert.That(path[1].XPercent, Is.EqualTo(70f).Within(0.001f));
        Assert.That(path[1].YPercent, Is.EqualTo(60f).Within(0.001f));
    }

    [Test]
    public void FindPath_GoesAroundWaterWallThroughGap()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        // Вертикальная стена воды на x=50% с проходом внизу (y 80..90%).
        PaintRect(layer, 49f, 0f, 51f, 80f, WorldMapGameplayTerrainType.Water);
        PaintRect(layer, 49f, 90f, 51f, 100f, WorldMapGameplayTerrainType.Water);
        WorldMapNavigation.ConfigureTerrainLayer(layer);

        List<MapPointData> path = WorldMapNavigation.FindPath(30f, 40f, 70f, 40f);

        Assert.That(path.Count, Is.GreaterThan(2), "Прямо через воду нельзя — путь с поворотами.");
        Assert.That(path[path.Count - 1].XPercent, Is.EqualTo(70f).Within(0.001f));
        AssertPathAvoids(path, WorldMapGameplayTerrainType.Water);

        float lowest = 0f;
        foreach (MapPointData point in path)
            lowest = Math.Max(lowest, point.YPercent);
        Assert.That(lowest, Is.GreaterThan(78f), "Путь проходит через проход внизу стены.");
    }

    [Test]
    public void FindPath_TargetInWaterEndsAtNearestShore()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 60f, 30f, 80f, 70f, WorldMapGameplayTerrainType.Water);
        WorldMapNavigation.ConfigureTerrainLayer(layer);

        List<MapPointData> path = WorldMapNavigation.FindPath(30f, 50f, 70f, 50f);
        MapPointData end = path[path.Count - 1];

        Assert.That(WorldMapNavigation.GetTerrainAtPercent(end.XPercent, end.YPercent),
            Is.Not.EqualTo(WorldMapGameplayTerrainType.Water));
        Assert.That(end.XPercent, Is.LessThan(61f).And.GreaterThan(57f), "Берег ближе всего к цели.");
        AssertPathAvoids(path, WorldMapGameplayTerrainType.Water);
    }

    [Test]
    public void FindPath_PrefersRoadOverForestWhenFaster()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        // Прямая — через болото; рядом дорога чуть в стороне.
        PaintRect(layer, 25f, 40f, 75f, 60f, WorldMapGameplayTerrainType.Swamp);
        PaintRect(layer, 20f, 62f, 80f, 66f, WorldMapGameplayTerrainType.Road);
        PaintRect(layer, 20f, 50f, 25f, 66f, WorldMapGameplayTerrainType.Road);
        PaintRect(layer, 75f, 50f, 80f, 66f, WorldMapGameplayTerrainType.Road);
        WorldMapNavigation.ConfigureTerrainLayer(layer);

        List<MapPointData> path = WorldMapNavigation.FindPath(22f, 50f, 78f, 50f);
        double hours = WorldMapNavigation.EstimateTravelHours(path);
        List<MapPointData> straight = new List<MapPointData>
        {
            new MapPointData(22f, 50f),
            new MapPointData(78f, 50f)
        };

        Assert.That(hours, Is.LessThan(WorldMapNavigation.EstimateTravelHours(straight)),
            "Найденный путь быстрее прямой через болото.");
        bool usesRoad = false;
        foreach (MapPointData point in path)
            usesRoad |= point.YPercent > 61f;
        Assert.That(usesRoad, Is.True, "Путь уходит на дорогу.");
    }

    [Test]
    public void EstimateTravelHours_ForestTakesLongerThanOpenGround()
    {
        List<MapPointData> path = new List<MapPointData>
        {
            new MapPointData(10f, 50f),
            new MapPointData(20f, 50f)
        };
        double open = WorldMapNavigation.EstimateTravelHours(path);

        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 0f, 40f, 30f, 60f, WorldMapGameplayTerrainType.Forest);
        WorldMapNavigation.ConfigureTerrainLayer(layer);
        double forest = WorldMapNavigation.EstimateTravelHours(path);

        Assert.That(open, Is.EqualTo(10.0 * WorldMapMovementRules.DefaultTravelHoursPerHex).Within(0.01));
        Assert.That(forest, Is.EqualTo(open / 0.7).Within(0.5));
    }

    // ------------------------------------------------------------------
    // Движение и время
    // ------------------------------------------------------------------

    [Test]
    public void Running_ClockFollowsRunSpeedAndIgnoresSpeedButtons()
    {
        GameState state = CreateRunningState(new MapPointData(20f, 50f), new MapPointData(40f, 50f));
        ContinuousSimulationSystem.ToggleSpeed(state);
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        float oneHexSeconds = 1f / WorldMapMovementRules.DefaultHeroRunSpeedHexesPerSecond;
        ContinuousSimulationSystem.Advance(state, oneHexSeconds, false);

        Assert.That(state.ActiveExpedition.CurrentMapXPercent, Is.EqualTo(21f).Within(0.01f),
            "За 1/скорость секунды бега — ровно одна клетка, кнопка ускорения не влияет.");
        Assert.That(ContinuousSimulationSystem.GetClock(state).HourOfDay - startHour,
            Is.EqualTo(WorldMapMovementRules.DefaultTravelHoursPerHex).Within(0.01));
    }

    [Test]
    public void Running_ForestSlowsRunAndCostsMoreHoursPerHex()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 0f, 40f, 100f, 60f, WorldMapGameplayTerrainType.Forest);
        WorldMapMovementRules rules = WorldMapMovementRules.Current;

        // Новая партия без мира сбрасывает разметку — лес кладётся после неё.
        GameState state = CreateRunningState(new MapPointData(20f, 50f), new MapPointData(60f, 50f));
        WorldMapNavigation.ConfigureTerrainLayer(layer);
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;
        ContinuousSimulationSystem.Advance(state, 1f, false);

        double expectedHexes = rules.SafeRunSpeedHexesPerSecond *
                               rules.RunSpeedMultiplier(WorldMapGameplayTerrainType.Forest);
        Assert.That(state.ActiveExpedition.CurrentMapXPercent - 20f, Is.EqualTo(expectedHexes).Within(0.02));
        Assert.That(ContinuousSimulationSystem.GetClock(state).HourOfDay - startHour,
            Is.EqualTo(expectedHexes * rules.HoursPerHex(WorldMapGameplayTerrainType.Forest)).Within(0.05));
    }

    [Test]
    public void FreePointArrival_IsSilentAndStopsTime()
    {
        GameState state = CreateRunningState(new MapPointData(20f, 50f), new MapPointData(22f, 50f));

        ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(state, 5f, false);
        double hourAfterArrival = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        Assert.That(state.ActiveExpedition.Phase, Is.EqualTo(CommanderState.AtLocation));
        Assert.That(batch.Result.Messages, Is.Empty, "Остановка в свободной точке — без донесения.");
        Assert.That(batch.MandatoryNotice, Is.Null);
        Assert.That(hourAfterArrival - ContinuousSimulationSystem.StartHour,
            Is.EqualTo(2.0 * WorldMapMovementRules.DefaultTravelHoursPerHex).Within(0.01),
            "Остаток кадра после остановки стоя не тратится.");
        Assert.That(ContinuousSimulationSystem.HasMovementOrActivityInProgress(state), Is.False,
            "Стоящий в поле отряд время не тратит.");
    }

    [Test]
    public void Expedition_RunsAroundWaterToTarget()
    {
        WorldMapTerrainLayer layer = WorldMapTerrainLayer.CreateDefault();
        PaintRect(layer, 55f, 50f, 58f, 95f, WorldMapGameplayTerrainType.Water);
        WorldMapNavigation.ConfigureTerrainLayer(layer);

        GameState state = new GameState();
        state.CreateNewGame(20261001);
        WorldMapNavigation.ConfigureTerrainLayer(layer);
        state.ArmySupply = 100;
        foreach (LocationData location in state.Locations)
            location.IsVisibleOnMap = true;
        Assert.That(state.TryStartExpeditionToMapPoint(
            65f, 81f, null, false, new List<string>(), out string message), Is.True, message);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        for (int frame = 0; frame < 2000 && state.ActiveExpedition.Phase == CommanderState.TravellingToLocation; frame++)
        {
            ContinuousSimulationSystem.Advance(state, 0.05f, false);
            Assert.That(WorldMapNavigation.GetTerrainAtPercent(
                    state.ActiveExpedition.CurrentMapXPercent,
                    state.ActiveExpedition.CurrentMapYPercent),
                Is.Not.EqualTo(WorldMapGameplayTerrainType.Water), "Герой не заходит в воду.");
        }

        Assert.That(state.ActiveExpedition.Phase, Is.EqualTo(CommanderState.AtLocation));
        Assert.That(state.ActiveExpedition.CurrentMapXPercent, Is.EqualTo(65f).Within(0.01f));
        Assert.That(state.ActiveExpedition.CurrentMapYPercent, Is.EqualTo(81f).Within(0.01f));
    }

    [Test]
    public void AdvanceRouteByCells_MovesAlongPathByDistance()
    {
        ExpeditionData expedition = new ExpeditionData
        {
            IsActive = true,
            Phase = CommanderState.TravellingToLocation,
            CurrentMapXPercent = 10f,
            CurrentMapYPercent = 50f,
            Route = new List<MapPointData>
            {
                new MapPointData(10f, 50f),
                new MapPointData(13f, 50f),
                new MapPointData(13f, 70f)
            }
        };

        WorldMapNavigation.AdvanceRouteByCells(expedition, 4);

        Assert.That(expedition.CurrentMapXPercent, Is.EqualTo(13f).Within(0.01f));
        Assert.That(WorldMapNavigation.DistanceHexes(13f, 50f, 13f, expedition.CurrentMapYPercent),
            Is.EqualTo(1.0).Within(0.01), "3 клетки до поворота и ещё 1 после.");
        Assert.That(expedition.Route[0].XPercent, Is.EqualTo(expedition.CurrentMapXPercent).Within(0.001f));
        Assert.That(expedition.LastTravelPoints.Count, Is.GreaterThan(0));
    }

    [Test]
    public void SaveLoad_MidRun_RestoresPositionAndKeepsRunning()
    {
        GameState state = new GameState();
        state.CreateNewGame(20261002);
        state.ArmySupply = 100;
        foreach (LocationData location in state.Locations)
            location.IsVisibleOnMap = true;
        Assert.That(state.TryStartExpeditionToMapPoint(
            70f, 50f, null, false, new List<string>(), out string message), Is.True, message);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, 1f, false);
        float x = state.ActiveExpedition.CurrentMapXPercent;
        float y = state.ActiveExpedition.CurrentMapYPercent;

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.That(restored.ActiveExpedition.Phase, Is.EqualTo(CommanderState.TravellingToLocation));
        Assert.That(restored.ActiveExpedition.CurrentMapXPercent, Is.EqualTo(x).Within(0.001f));
        Assert.That(restored.ActiveExpedition.CurrentMapYPercent, Is.EqualTo(y).Within(0.001f));

        ContinuousSimulationSystem.SetPaused(restored, false);
        ContinuousSimulationSystem.Advance(restored, 0.5f, false);
        Assert.That(WorldMapNavigation.DistanceHexes(x, y,
                restored.ActiveExpedition.CurrentMapXPercent, restored.ActiveExpedition.CurrentMapYPercent),
            Is.EqualTo(1.0).Within(0.01), "После загрузки отряд бежит дальше от того же места.");
    }

    // ------------------------------------------------------------------
    // Настройки
    // ------------------------------------------------------------------

    [Test]
    public void MovementSettingsAsset_IsLoadedAndCoversEveryTerrain()
    {
        WorldMapVisualRuntime.ClearCache();
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        Assert.That(settings, Is.Not.Null, "Ассет настроек перемещения лежит в Resources.");

        WorldMapMovementRules rules = settings.ToRules();
        foreach (WorldMapGameplayTerrainType terrain in WorldMapTerrainLabels.All)
            Assert.That(settings.FindTerrain(terrain), Is.Not.Null, terrain.ToString());
        Assert.That(rules.IsTraversable(WorldMapGameplayTerrainType.Water), Is.False);
        Assert.That(rules.TravelHoursPerHex, Is.EqualTo(settings.TravelHoursPerHex));
        Assert.That(settings.HeroAnimationSetId, Is.Not.Empty);
    }

    [Test]
    public void WorldDefinitionAsset_HasNoLegacyMarkupAfterMigration()
    {
        WorldMapVisualRuntime.ClearCache();
        WorldMapDatabaseAsset database = WorldMapVisualRuntime.LoadDatabase();
        Assert.That(database, Is.Not.Null);
        Assert.That(database.ActiveWorld, Is.Not.Null);
        Assert.That(database.ActiveWorld.HasLegacyMarkup, Is.False,
            "Старые зоны и дороги перенесены в разметку клеток.");
        Assert.That(database.ActiveWorld.ToData().IsValid, Is.True);
    }

    // ------------------------------------------------------------------

    private static GameState CreateRunningState(MapPointData from, MapPointData to)
    {
        GameState state = new GameState();
        state.CreateNewGame(424242);
        CommanderData commander = state.GetSelectedCommander();
        LocationData waypoint = new LocationData("test-free-point", "Точка", 0, "—")
        {
            IsWaypoint = true,
            MapXPercent = to.XPercent,
            MapYPercent = to.YPercent
        };
        state.Locations.Add(waypoint);
        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(from.XPercent, from.YPercent),
            new MapPointData(to.XPercent, to.YPercent)
        };
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = waypoint.Id,
            Phase = CommanderState.TravellingToLocation,
            CurrentMapXPercent = from.XPercent,
            CurrentMapYPercent = from.YPercent,
            TargetMapXPercent = to.XPercent,
            TargetMapYPercent = to.YPercent,
            Route = route,
            RouteLengthCells = WorldMapNavigation.CalculateRouteCells(route),
            RemainingRouteCells = WorldMapNavigation.CalculateRouteCells(route)
        };
        commander.State = CommanderState.TravellingToLocation;
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        return state;
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
            {
                layer.Set(cell, terrain);
            }
        }
    }

    private static void AssertPathAvoids(List<MapPointData> path, WorldMapGameplayTerrainType terrain)
    {
        for (int i = 1; i < path.Count; i++)
        {
            for (int s = 0; s <= 40; s++)
            {
                float t = s / 40f;
                float x = Mathf.Lerp(path[i - 1].XPercent, path[i].XPercent, t);
                float y = Mathf.Lerp(path[i - 1].YPercent, path[i].YPercent, t);
                Assert.That(WorldMapNavigation.GetTerrainAtPercent(x, y), Is.Not.EqualTo(terrain),
                    "Отрезок " + i + " задевает " + terrain + " в точке (" + x + "; " + y + ").");
            }
        }
    }
}
