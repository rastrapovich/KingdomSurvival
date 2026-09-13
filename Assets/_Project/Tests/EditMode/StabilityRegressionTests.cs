using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

public class StabilityRegressionTests
{
    // Задача "пересобрать масштаб путешествия": одна клетка теперь занимает
    // 1.0/CellsPerGameHour игровых часов (балансировочная настройка мира, по
    // умолчанию 4ч — не жёстко "1 сутки"), а не фиксированную долю
    // RealSecondsPerGameDay. Считаем нужные реальные секунды из этих же
    // констант, а не из старого предположения — формула остаётся верной при
    // любом BaseTravelHoursPerCell. Запас ×1.5, чтобы гарантированно
    // пересечь ровно одну клетку маршрута независимо от гранулярности шагов
    // внутри ContinuousSimulationSystem.Advance. Явно сбрасываем географию
    // до default ПЕРЕД вычислением — CellsPerGameHour читает
    // WorldMapNavigation.ActiveDefinition, а это static readonly поле
    // вычисляется один раз при первом обращении к классу, до TearDown любого
    // теста; без явного сброса значение зависело бы от порядка запуска тестов.
    private static readonly float OneCellAdvanceSeconds;

    static StabilityRegressionTests()
    {
        WorldMapNavigation.ConfigureDefaultTerrain();
        OneCellAdvanceSeconds = (float)((1.0 / ContinuousSimulationSystem.CellsPerGameHour) /
            ContinuousSimulationSystem.GameHoursPerRealSecond * 1.5);
    }

    [Test]
    public void CreateNewGame_SameSeedIsIndependentOfPreviouslyConfiguredTerrain()
    {
        const int requestedSeed = 424242;

        WorldMapNavigation.ConfigureDefaultTerrain();
        GameState first = new GameState();
        first.CreateNewGame(requestedSeed);
        string firstSignature = BuildLocationSignature(first);

        WorldMapNavigation.ConfigureDefaultTerrain();
        GameState second = new GameState();
        second.CreateNewGame(requestedSeed);

        Assert.That(BuildLocationSignature(second), Is.EqualTo(firstSignature));
    }

    [Test]
    public void ContinuousMovement_ArrivalStopsClockAtExactArrivalTime()
    {
        GameState state = new GameState();
        state.CreateNewGame(20260903);
        LocationData target = state.Locations[0];
        PrepareOneSegmentExpedition(state, target);

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(
            state,
            OneCellAdvanceSeconds,
            false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        // 1.0/CellsPerGameHour часов после старта (StartHour) — если это
        // пересекает полночь, часы суток оборачиваются по модулю 24
        // (ResolveMidnight), поэтому ожидание тоже нужно свернуть.
        double expectedArrivalHour =
            (ContinuousSimulationSystem.StartHour +
             1.0 / ContinuousSimulationSystem.CellsPerGameHour) % 24.0;
        Assert.That(batch.RequestAutoPause, Is.True);
        Assert.That(clock.IsPaused, Is.True);
        Assert.That(clock.HourOfDay, Is.EqualTo(expectedArrivalHour).Within(0.001));
        Assert.That(state.ActiveExpedition.Phase, Is.EqualTo(CommanderState.AtLocation));
    }

    [Test]
    public void ContinuousMovement_DiscoveryStopsClockAtDiscoveryMoment()
    {
        GameState state = new GameState();
        state.CreateNewGame(20260904);
        LocationData hidden = state.Locations[0];
        Assert.That(hidden.IsVisibleOnMap, Is.False);

        LocationData waypoint = new LocationData("test-waypoint", "Точка", 0, "—")
        {
            IsWaypoint = true,
            IsVisibleOnMap = false,
            MapXPercent = 95f,
            MapYPercent = 5f
        };
        state.Locations.Add(waypoint);

        CommanderData commander = state.GetSelectedCommander();
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = waypoint.Id,
            Phase = CommanderState.TravellingToLocation,
            RemainingRouteCells = 2,
            RouteLengthCells = 2,
            CurrentMapXPercent = WorldMapNavigation.CapitalXPercent,
            CurrentMapYPercent = WorldMapNavigation.CapitalYPercent,
            TargetMapXPercent = waypoint.MapXPercent,
            TargetMapYPercent = waypoint.MapYPercent,
            RouteIndex = 0,
            Route = new List<MapPointData>
            {
                new MapPointData(
                    WorldMapNavigation.CapitalXPercent,
                    WorldMapNavigation.CapitalYPercent),
                new MapPointData(hidden.MapXPercent, hidden.MapYPercent),
                new MapPointData(waypoint.MapXPercent, waypoint.MapYPercent)
            }
        };
        commander.State = CommanderState.TravellingToLocation;

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(
            state,
            OneCellAdvanceSeconds,
            false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        // WM-12: см. комментарий в ContinuousMovement_ArrivalStopsClockAtExactArrivalTime.
        double expectedDiscoveryHour =
            (ContinuousSimulationSystem.StartHour +
             1.0 / ContinuousSimulationSystem.CellsPerGameHour) % 24.0;
        Assert.That(batch.RequestAutoPause, Is.True);
        Assert.That(clock.IsPaused, Is.True);
        Assert.That(clock.HourOfDay, Is.EqualTo(expectedDiscoveryHour).Within(0.001));
        Assert.That(state.HasPendingExpeditionDecision, Is.True);
        Assert.That(state.ActiveExpedition.LocationId, Is.EqualTo(hidden.Id));
    }

    [Test]
    public void ContinuousRuntimeStateStorage_UsesWeakGameStateKeys()
    {
        FieldInfo field = typeof(ContinuousSimulationSystem).GetField(
            "RuntimeStates",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(field, Is.Not.Null);
        Assert.That(field.FieldType.IsGenericType, Is.True);
        Assert.That(
            field.FieldType.GetGenericTypeDefinition(),
            Is.EqualTo(typeof(ConditionalWeakTable<,>)));
    }

    private static void PrepareOneSegmentExpedition(
        GameState state,
        LocationData target)
    {
        CommanderData commander = state.GetSelectedCommander();
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = target.Id,
            Phase = CommanderState.TravellingToLocation,
            RemainingRouteCells = 1,
            RouteLengthCells = 1,
            CurrentMapXPercent = WorldMapNavigation.CapitalXPercent,
            CurrentMapYPercent = WorldMapNavigation.CapitalYPercent,
            TargetMapXPercent = target.MapXPercent,
            TargetMapYPercent = target.MapYPercent,
            RouteIndex = 0,
            Route = new List<MapPointData>
            {
                new MapPointData(
                    WorldMapNavigation.CapitalXPercent,
                    WorldMapNavigation.CapitalYPercent),
                new MapPointData(target.MapXPercent, target.MapYPercent)
            }
        };
        commander.State = CommanderState.TravellingToLocation;
    }

    private static string BuildLocationSignature(GameState state)
    {
        StringBuilder builder = new StringBuilder();
        foreach (LocationData location in state.Locations)
        {
            builder.Append(location.Id).Append('|')
                .Append(location.MapXPercent).Append('|')
                .Append(location.MapYPercent).Append('|')
                .Append(location.TravelHoursFromCapital).Append(';');
        }
        return builder.ToString();
    }
}
