using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

public class StabilityRegressionTests
{
    [Test]
    public void CreateNewGame_ConfiguresRequestedTerrainSeedBeforeRoutes()
    {
        const int requestedSeed = 24680;
        const int staleSeed = 13579;

        WorldMapNavigation.ConfigureTerrain(requestedSeed);
        string expectedTerrain = BuildTerrainSignature();
        WorldMapNavigation.ConfigureTerrain(staleSeed);

        GameState state = new GameState();
        state.CreateNewGame(requestedSeed);

        Assert.That(BuildTerrainSignature(), Is.EqualTo(expectedTerrain));
    }

    [Test]
    public void CreateNewGame_SameSeedIsIndependentOfPreviouslyConfiguredTerrain()
    {
        const int requestedSeed = 424242;

        WorldMapNavigation.ConfigureTerrain(11);
        GameState first = new GameState();
        first.CreateNewGame(requestedSeed);
        string firstSignature = BuildLocationSignature(first);

        WorldMapNavigation.ConfigureTerrain(999999);
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
            10f,
            false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        double expectedArrivalHour =
            ContinuousSimulationSystem.StartHour +
            1.0 / ContinuousSimulationSystem.CellsPerGameHour;
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
            10f,
            false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        double expectedDiscoveryHour =
            ContinuousSimulationSystem.StartHour +
            1.0 / ContinuousSimulationSystem.CellsPerGameHour;
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

    private static string BuildTerrainSignature()
    {
        StringBuilder builder = new StringBuilder();
        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
                builder.Append((int)WorldMapNavigation.GetTerrainAtGridCell(x, y));
        }
        return builder.ToString();
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
