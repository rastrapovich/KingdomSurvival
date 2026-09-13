using System.Collections.Generic;
using NUnit.Framework;

// WM-T04 (задача "gameplay-география дорог"): скорость реально меняется,
// пока герой физически находится внутри gameplay-зоны дороги, и
// восстанавливается сразу после выхода — направление/цель маршрута при этом
// не меняются (маршрут строится обычным WorldMapNavigation.FindPath, эта
// задача его не трогает и не подключает A*/снаппинг).
public class WorldMapRoadMovementTests
{
    private const float StartX = 5f;
    private const float StartY = 50f;
    private const float TargetX = 95f;
    private const float TargetY = 50f;

    [TearDown]
    public void ResetGeography()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
    }

    private static WorldMapDefinitionData BuildDefinitionWithRoad(
        string id,
        float roadMinX,
        float roadMaxX)
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData
        {
            WorldDefinitionId = id
        };

        if (roadMaxX > roadMinX)
        {
            WorldMapRoadDefinition road = new WorldMapRoadDefinition
            {
                Id = "road-test",
                Enabled = true,
                Width = 6f
            };
            road.Points.Add(new MapPointData(roadMinX, StartY));
            road.Points.Add(new MapPointData(roadMaxX, StartY));
            definition.Roads.Add(road);
        }

        return definition;
    }

    private static GameState CreateStateTravellingStraightLine(
        WorldMapDefinitionData definition)
    {
        GameState state = new GameState();
        state.CreateNewGame(1, null, definition);
        // Тесты продвигают время достаточно далеко, чтобы уверенно пересечь
        // проверяемые зоны — при ArmySupply=0 (по умолчанию у CreateNewGame)
        // на первой же полночи сработал бы вынужденный возврат экспедиции
        // (не связанная с дорогами логика), исказив измерение.
        state.ArmySupply = 1000000;

        List<MapPointData> route = WorldMapNavigation.FindPath(
            StartX, StartY, TargetX, TargetY);

        CommanderData commander = state.GetSelectedCommander();
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = null,
            Phase = CommanderState.TravellingToLocation,
            CurrentMapXPercent = StartX,
            CurrentMapYPercent = StartY,
            TargetMapXPercent = TargetX,
            TargetMapYPercent = TargetY,
            RouteIndex = 0,
            Route = route
        };
        commander.State = CommanderState.TravellingToLocation;

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.SetPaused(state, false);
        return state;
    }

    [Test]
    public void Expedition_TravelsFartherOnRoadThanOnOpenGroundInSameTime()
    {
        // Дорога покрывает весь путь: за одинаковое игровое время экспедиция
        // на дороге должна пройти в ~1.30 раза больше расстояния. Задача
        // "пересобрать масштаб путешествия" ускорила базовое движение в
        // 6 раз (4ч/клетку вместо 24ч) — advance уменьшен со 150 до 20с,
        // чтобы ни один сценарий не успевал доехать до цели раньше времени
        // (иначе оба измерения упёрлись бы в один и тот же clamp дистанции).
        GameState onRoad = CreateStateTravellingStraightLine(
            BuildDefinitionWithRoad("road-everywhere", 0f, 100f));
        ContinuousSimulationSystem.Advance(onRoad, 20f, false);
        double onRoadDistance = onRoad.ActiveExpedition.CurrentMapXPercent - StartX;

        GameState openGround = CreateStateTravellingStraightLine(
            BuildDefinitionWithRoad("no-road", 0f, 0f));
        ContinuousSimulationSystem.Advance(openGround, 20f, false);
        double openGroundDistance = openGround.ActiveExpedition.CurrentMapXPercent - StartX;

        Assert.That(onRoadDistance, Is.GreaterThan(openGroundDistance),
            "За то же игровое время по дороге (×1.30) должно пройти больше расстояния, чем по земле.");
        Assert.That(onRoadDistance, Is.EqualTo(openGroundDistance * 1.30).Within(0.5),
            "Множитель должен применяться именно ×1.30, не примерно любое ускорение.");
    }

    [Test]
    public void Expedition_SpeedRestoresAfterLeavingRoadZoneAndDirectionUnchanged()
    {
        // Дорога — только первая половина пути (5..50). Достаточно большое
        // продвижение времени гарантированно проносит героя мимо конца
        // дороги и дальше по прямой.
        GameState state = CreateStateTravellingStraightLine(
            BuildDefinitionWithRoad("road-first-half", StartX, 50f));

        ContinuousSimulationSystem.Advance(state, 3000f, false);

        Assert.That(
            state.ActiveExpedition.CurrentMapYPercent,
            Is.EqualTo(StartY).Within(0.01f),
            "Направление маршрута не должно меняться из-за дороги — прямая линия сохраняется.");
        Assert.That(
            state.ActiveExpedition.CurrentMapXPercent,
            Is.GreaterThan(50f),
            "Герой должен пройти дальше конца дороги, а не остановиться/свернуть на ней.");
    }

    [Test]
    public void Expedition_NoRoadsBehavesExactlyAsBeforeThisFeature()
    {
        GameState state = CreateStateTravellingStraightLine(
            BuildDefinitionWithRoad("no-road-baseline", 0f, 0f));

        // См. комментарий в Expedition_TravelsFartherOnRoadThanOnOpenGroundInSameTime
        // про уменьшение advance после ускорения базового движения в 6 раз.
        ContinuousSimulationSystem.Advance(state, 20f, false);

        double expectedCells =
            20f * ContinuousSimulationSystem.MaximumSpeedMultiplier *
            ContinuousSimulationSystem.GameHoursPerRealSecond *
            ContinuousSimulationSystem.CellsPerGameHour;
        double expectedPercent =
            expectedCells * 100.0 / (WorldMapNavigation.GridWidth - 1);

        Assert.That(
            state.ActiveExpedition.CurrentMapXPercent - StartX,
            Is.EqualTo(expectedPercent).Within(0.2),
            "Без дорог (Roads.Count == 0) формула движения должна остаться точно прежней.");
    }
}
