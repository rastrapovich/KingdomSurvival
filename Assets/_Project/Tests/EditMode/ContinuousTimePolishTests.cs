using System.Collections.Generic;
using NUnit.Framework;

public class ContinuousTimePolishTests
{
    [Test]
    public void PauseToggle_DoesNotAdvanceClockOrCalendarDay()
    {
        GameState state = new GameState();
        state.CreateNewGame(501);
        ContinuousSimulationSystem.Reset(state);

        ContinuousClockSnapshot before =
            ContinuousSimulationSystem.GetClock(state);

        ContinuousSimulationSystem.TogglePause(state);
        ContinuousSimulationSystem.TogglePause(state);

        ContinuousClockSnapshot after =
            ContinuousSimulationSystem.GetClock(state);

        Assert.That(state.Day, Is.EqualTo(1));
        Assert.That(after.HourOfDay, Is.EqualTo(before.HourOfDay).Within(0.0001));
        Assert.That(after.IsPaused, Is.EqualTo(before.IsPaused));
    }

    [TestCase(5, 1.0)]
    [TestCase(10, 2.0)]
    public void ExtendedSpeed_ScalesStrategicClock(
        int multiplier,
        double expectedGameHours)
    {
        GameState state = new GameState();
        state.CreateNewGame(502 + multiplier);
        ContinuousSimulationSystem.Reset(state);

        Assert.That(
            ContinuousSimulationSystem.SetSpeedMultiplier(state, multiplier),
            Is.True);

        ContinuousSimulationSystem.SetPaused(state, false);
        double startHour =
            ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 1f, false);

        double actualHours =
            ContinuousSimulationSystem.GetClock(state).HourOfDay - startHour;

        Assert.That(actualHours, Is.EqualTo(expectedGameHours).Within(0.01));
        Assert.That(
            ContinuousSimulationSystem.GetSpeedMultiplier(state),
            Is.EqualTo(multiplier));
    }

    [Test]
    public void PreparedRoster_CanChangeBeforeMovement()
    {
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "garrick" },
            out message);

        Assert.That(changed, Is.True, message);
        Assert.That(
            state.ActiveExpedition.FighterIds,
            Is.EqualTo(new[] { "garrick" }));
    }

    [Test]
    public void PreparedRoster_LocksAfterActualMovementBegins()
    {
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 0.25f, false);

        Assert.That(
            ContinuousSimulationSystem.HasExpeditionStartedMoving(state),
            Is.True);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "garrick" },
            out message);

        Assert.That(changed, Is.False);
        Assert.That(
            state.ActiveExpedition.FighterIds.Count,
            Is.EqualTo(2));
    }

    private static GameState CreatePreparedExpedition()
    {
        GameState state = new GameState();
        state.CreateNewGame(777);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            90f,
            20f,
            null,
            false,
            new List<string> { "garrick", "edric" },
            out message);

        Assert.That(started, Is.True, message);
        return state;
    }
}
