using System.Collections.Generic;
using NUnit.Framework;

// Задача "пересобрать масштаб путешествия — базово 1 клетка = 4 игровых
// часа вместо 24": ContinuousSimulationSystem.CellsPerGameHour больше не
// выведен из RealSecondsPerGameDay/GameHoursPerRealSecond (темп течения
// мирового времени — отдельная система), а читает
// WorldMapDefinitionData.BaseTravelHoursPerCell активного мира (fallback 4ч
// при отсутствии мира или невалидном значении). Дороги/Hills/Mountains
// изменяют итоговое время поверх этой базы — не заменяют её.
public class WorldMapTravelScaleTests
{
    [TearDown]
    public void ResetGeography()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    private static List<MapPointData> BuildStraightRoute(int cellCount)
    {
        List<MapPointData> route = new List<MapPointData>();
        for (int i = 0; i <= cellCount; i++)
            route.Add(new MapPointData(i, 0f));
        return route;
    }

    // OpenGround = 4 ч за одну клетку (без активного мира — безопасный
    // fallback на дефолт 4).
    [Test]
    public void CalculateTravelHours_OneCellOpenGround_IsFourHoursByDefault()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
        List<MapPointData> route = BuildStraightRoute(1);

        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(4.0).Within(0.0001));
    }

    // 10 клеток = 40 ч.
    [Test]
    public void CalculateTravelHours_TenCellsOpenGround_IsFortyHours()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
        List<MapPointData> route = BuildStraightRoute(10);

        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(40.0).Within(0.001));
    }

    private static WorldMapDefinitionData BuildDefinitionWithTerrainArea(
        WorldMapTerrainType terrain, float baseTravelHoursPerCell = 4f)
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData
        {
            WorldDefinitionId = "travel-scale-test",
            BaseTravelHoursPerCell = baseTravelHoursPerCell
        };

        definition.TerrainAreas.Add(new WorldMapTerrainAreaData
        {
            Id = "area",
            Terrain = terrain,
            MinXPercent = 0f,
            MaxXPercent = 100f,
            MinYPercent = 0f,
            MaxYPercent = 100f,
            Priority = 0
        });

        return definition;
    }

    // Terrain cost ×2 (Hills) = 8 ч за эквивалент обычной клетки — не
    // применяется дважды: WorldMapNavigation.GetTerrainTravelCost (FindPath,
    // под-точки маршрута) и CellsPerGameHour (базовое время) — независимые
    // множители, перемножаются один раз.
    // FindPath делит дистанцию на ceil(distanceCells) базовых сегментов —
    // нужно целиться ровно в одну клетку дистанции (100/(GridWidth-1)%),
    // иначе округление даст 2+ базовых сегмента вместо одного.
    private static float OneCellDeltaPercent =>
        100f / (WorldMapNavigation.GridWidth - 1) * 0.999f;

    [Test]
    public void CalculateTravelHours_OneCellHills_IsEightHours()
    {
        WorldMapNavigation.ConfigureFromDefinition(
            BuildDefinitionWithTerrainArea(WorldMapTerrainType.Hills));

        // Раздел ClampMapX/ClampMapY (WorldMapNavigation): координаты клампятся
        // к [2..98], поэтому старт/цель должны лежать внутри этого диапазона,
        // иначе оба клампятся к одному и тому же краю и дистанция становится 0.
        List<MapPointData> route =
            WorldMapNavigation.FindPath(50f, 50f, 50f + OneCellDeltaPercent, 50f);

        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(8.0).Within(0.5));
    }

    // Terrain cost ×3 (Mountains) = 12 ч за эквивалент обычной клетки.
    [Test]
    public void CalculateTravelHours_OneCellMountains_IsTwelveHours()
    {
        WorldMapNavigation.ConfigureFromDefinition(
            BuildDefinitionWithTerrainArea(WorldMapTerrainType.Mountains));

        List<MapPointData> route =
            WorldMapNavigation.FindPath(50f, 50f, 50f + OneCellDeltaPercent, 50f);

        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(12.0).Within(0.5));
    }

    // Дорога с multiplier 1.3 реально сокращает время (не только увеличивает
    // пройденное расстояние — это то же самое утверждение с другой стороны):
    // за одинаковое реальное время экспедиция на дороге проходит цель раньше.
    [Test]
    public void RoadMultiplier_ReducesActualTravelTimeForSameDistance()
    {
        const float startX = 5f;
        const float startY = 50f;
        const float targetX = 15f;
        const float targetY = 50f;

        WorldMapDefinitionData withRoad = new WorldMapDefinitionData { WorldDefinitionId = "with-road" };
        WorldMapRoadDefinition road = new WorldMapRoadDefinition { Id = "road", Enabled = true, Width = 6f };
        road.Points.Add(new MapPointData(0f, startY));
        road.Points.Add(new MapPointData(100f, startY));
        withRoad.Roads.Add(road);

        GameState onRoad = new GameState();
        onRoad.CreateNewGame(1, null, withRoad);
        onRoad.ArmySupply = 1000000;
        StartStraightLineExpedition(onRoad, startX, startY, targetX, targetY);

        GameState openGround = new GameState();
        openGround.CreateNewGame(1, null, new WorldMapDefinitionData { WorldDefinitionId = "no-road" });
        openGround.ArmySupply = 1000000;
        StartStraightLineExpedition(openGround, startX, startY, targetX, targetY);

        // Продвигаем оба состояния РОВНО на время, за которое OpenGround
        // проходит половину дистанции — на дороге за то же время должно
        // остаться пройти меньше половины (герой ближе к цели).
        double halfDistanceHours = 5.0 * ContinuousSimulationSystem.CellsPerGameHour <= 0
            ? 0
            : 5.0 / ContinuousSimulationSystem.CellsPerGameHour;
        float advanceSeconds = (float)(halfDistanceHours / ContinuousSimulationSystem.GameHoursPerRealSecond);

        WorldMapNavigation.ConfigureFromDefinition(withRoad);
        ContinuousSimulationSystem.Reset(onRoad);
        ContinuousSimulationSystem.NotifyRouteChanged(onRoad);
        ContinuousSimulationSystem.SetPaused(onRoad, false);
        ContinuousSimulationSystem.Advance(onRoad, advanceSeconds, false);

        WorldMapNavigation.ConfigureDefaultTerrain();
        ContinuousSimulationSystem.Reset(openGround);
        ContinuousSimulationSystem.NotifyRouteChanged(openGround);
        ContinuousSimulationSystem.SetPaused(openGround, false);
        ContinuousSimulationSystem.Advance(openGround, advanceSeconds, false);

        double onRoadRemaining = targetX - onRoad.ActiveExpedition.CurrentMapXPercent;
        double openGroundRemaining = targetX - openGround.ActiveExpedition.CurrentMapXPercent;

        Assert.That(
            onRoadRemaining,
            Is.LessThan(openGroundRemaining),
            "За одинаковое время на дороге должно остаться пройти МЕНЬШЕ — множитель дороги сокращает время в пути.");
    }

    private static void StartStraightLineExpedition(
        GameState state, float startX, float startY, float targetX, float targetY)
    {
        CommanderData commander = state.GetSelectedCommander();
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = null,
            Phase = CommanderState.TravellingToLocation,
            CurrentMapXPercent = startX,
            CurrentMapYPercent = startY,
            TargetMapXPercent = targetX,
            TargetMapYPercent = targetY,
            RouteIndex = 0,
            Route = WorldMapNavigation.FindPath(startX, startY, targetX, targetY)
        };
        commander.State = CommanderState.TravellingToLocation;
    }

    // Изменение BaseTravelHoursPerCell меняет движение без изменения
    // RealSecondsPerGameDay (темп мира — независимая система, структурно не
    // может измениться от данных World Definition: RealSecondsPerGameDay —
    // константа кода, не поле WorldMapDefinitionData).
    [Test]
    public void ChangingBaseTravelHoursPerCell_ChangesTravelTime_WithoutTouchingWorldClockConstant()
    {
        double realSecondsPerGameDayBefore = ContinuousSimulationSystem.RealSecondsPerGameDay;

        WorldMapNavigation.ConfigureFromDefinition(
            new WorldMapDefinitionData { WorldDefinitionId = "slow", BaseTravelHoursPerCell = 4f });
        double hoursAtFour = ContinuousSimulationSystem.CalculateTravelHours(BuildStraightRoute(1));

        WorldMapNavigation.ConfigureFromDefinition(
            new WorldMapDefinitionData { WorldDefinitionId = "fast", BaseTravelHoursPerCell = 2f });
        double hoursAtTwo = ContinuousSimulationSystem.CalculateTravelHours(BuildStraightRoute(1));

        Assert.That(hoursAtFour, Is.EqualTo(4.0).Within(0.0001));
        Assert.That(hoursAtTwo, Is.EqualTo(2.0).Within(0.0001));
        Assert.That(
            ContinuousSimulationSystem.RealSecondsPerGameDay,
            Is.EqualTo(realSecondsPerGameDayBefore),
            "Темп течения мирового времени не должен зависеть от масштаба путешествия.");
    }

    // Invalid/zero setting безопасно получает fallback — без деления на 0 и
    // без бесконечной скорости.
    [Test]
    public void CellsPerGameHour_ZeroBaseTravelHoursPerCell_FallsBackSafely()
    {
        WorldMapNavigation.ConfigureFromDefinition(
            new WorldMapDefinitionData { WorldDefinitionId = "zero", BaseTravelHoursPerCell = 0f });

        double cellsPerGameHour = ContinuousSimulationSystem.CellsPerGameHour;

        Assert.That(double.IsInfinity(cellsPerGameHour), Is.False);
        Assert.That(double.IsNaN(cellsPerGameHour), Is.False);
        Assert.That(cellsPerGameHour, Is.EqualTo(1.0 / 4.0).Within(0.0001));
    }

    [Test]
    public void CellsPerGameHour_NegativeBaseTravelHoursPerCell_FallsBackSafely()
    {
        WorldMapNavigation.ConfigureFromDefinition(
            new WorldMapDefinitionData { WorldDefinitionId = "negative", BaseTravelHoursPerCell = -10f });

        double cellsPerGameHour = ContinuousSimulationSystem.CellsPerGameHour;

        Assert.That(double.IsInfinity(cellsPerGameHour), Is.False);
        Assert.That(cellsPerGameHour, Is.EqualTo(1.0 / 4.0).Within(0.0001));
    }

    // Save/Load в середине сегмента не ломается — SegmentProgress/
    // RouteIndex/позиция героя переживают snapshot/restore под новым
    // масштабом точно так же, как и под старым (сам масштаб не влияет на
    // контракт snapshot'а).
    [Test]
    public void SaveLoad_MidSegment_PreservesPositionAndProgressUnderNewScale()
    {
        WorldMapDefinitionData definition =
            new WorldMapDefinitionData { WorldDefinitionId = "save-load-scale", BaseTravelHoursPerCell = 4f };

        GameState original = new GameState();
        original.CreateNewGame(1, null, definition);
        original.ArmySupply = 1000000;
        StartStraightLineExpedition(original, 5f, 50f, 25f, 50f);

        ContinuousSimulationSystem.Reset(original);
        ContinuousSimulationSystem.NotifyRouteChanged(original);
        ContinuousSimulationSystem.SetPaused(original, false);

        // Продвигаем меньше времени, чем требуется на одну полную клетку —
        // гарантированно останавливаемся в середине сегмента.
        float partialCellSeconds = (float)(
            (1.0 / ContinuousSimulationSystem.CellsPerGameHour) * 0.4 /
            ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationSystem.Advance(original, partialCellSeconds, false);

        Assert.That(original.ActiveExpedition.Phase, Is.EqualTo(CommanderState.TravellingToLocation));
        float xBeforeSave = original.ActiveExpedition.CurrentMapXPercent;
        int routeIndexBeforeSave = original.ActiveExpedition.RouteIndex;

        ContinuousSimulationSnapshotData snapshot = ContinuousSimulationSystem.ExportSnapshot(original);

        GameState restored = new GameState();
        restored.CreateNewGame(1, null, definition);
        restored.ArmySupply = 1000000;
        StartStraightLineExpedition(restored, 5f, 50f, 25f, 50f);
        restored.ActiveExpedition.CurrentMapXPercent = xBeforeSave;
        restored.ActiveExpedition.CurrentMapYPercent = original.ActiveExpedition.CurrentMapYPercent;
        restored.ActiveExpedition.RouteIndex = routeIndexBeforeSave;
        ContinuousSimulationSystem.RestoreSnapshot(restored, snapshot);
        ContinuousSimulationSystem.SetPaused(restored, false);

        Assert.That(restored.ActiveExpedition.CurrentMapXPercent, Is.EqualTo(xBeforeSave).Within(0.0001f));
        Assert.That(restored.ActiveExpedition.RouteIndex, Is.EqualTo(routeIndexBeforeSave));

        // Дальнейшее продвижение на восстановленной копии продолжает работать
        // (не роняет исключение, не телепортирует, не сбрасывает прогресс).
        ContinuousSimulationSystem.Advance(restored, partialCellSeconds, false);
        Assert.That(
            restored.ActiveExpedition.CurrentMapXPercent,
            Is.GreaterThan(xBeforeSave),
            "После восстановления движение должно продолжаться вперёд по маршруту.");
    }
}
