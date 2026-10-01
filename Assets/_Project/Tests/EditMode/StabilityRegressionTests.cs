using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

public class StabilityRegressionTests
{
    // 12И (канон v1.50 §9.6): пока отряд бежит, часы идут в темпе бега —
    // клетка пути занимает 1/скорость реальных секунд. Запас ×1.5, чтобы
    // гарантированно пройти одну клетку; остановка обрывает кадр точно.
    private static readonly float OneCellAdvanceSeconds;

    static StabilityRegressionTests()
    {
        OneCellAdvanceSeconds = (float)(1.5 / WorldMapMovementRules.CreateDefault().SafeRunSpeedHexesPerSecond);
    }

    private static double HoursPerOpenHex =>
        WorldMapMovementRules.Current.HoursPerHex(WorldMapGameplayTerrainType.OpenGround);

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

        // Цель — ровно в одной клетке от Дома: прибытие через одну клетку
        // игрового времени после старта.
        double expectedArrivalHour =
            (ContinuousSimulationSystem.StartHour + HoursPerOpenHex) % 24.0;
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
            MapXPercent = WorldMapNavigation.CapitalXPercent + 6f,
            MapYPercent = WorldMapNavigation.CapitalYPercent
        };
        // 12И: скрытое место в двух клетках по пути — замечается, как только
        // отряд подходит на радиус обнаружения.
        hidden.MapXPercent = WorldMapNavigation.CapitalXPercent + 2f;
        hidden.MapYPercent = WorldMapNavigation.CapitalYPercent;
        state.Locations.Add(waypoint);

        CommanderData commander = state.GetSelectedCommander();
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = commander.Id,
            LocationId = waypoint.Id,
            Phase = CommanderState.TravellingToLocation,
            RemainingRouteCells = 6,
            RouteLengthCells = 6,
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

        double discoveryHexes = 2.0 - WorldMapMovementRules.Current.DiscoveryRadiusHexes;
        double expectedDiscoveryHour =
            (ContinuousSimulationSystem.StartHour + discoveryHexes * HoursPerOpenHex) % 24.0;
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
        target.MapXPercent = WorldMapNavigation.CapitalXPercent + 1f;
        target.MapYPercent = WorldMapNavigation.CapitalYPercent;
        // Известное место: проверяется прибытие, а не находка по пути.
        target.IsVisibleOnMap = true;
        target.IsDiscovered = true;
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
