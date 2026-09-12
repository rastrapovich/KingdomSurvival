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

    [TestCase(5)]
    [TestCase(10)]
    public void ExtendedSpeed_ScalesStrategicClock(int multiplier)
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

        // WM-13: раньше 1.0/2.0 были захардкожены под старое
        // GameHoursPerRealSecond — считаем ожидание из константы, чтобы
        // ускорение времени не ломало тест.
        double expectedGameHours =
            multiplier * ContinuousSimulationSystem.GameHoursPerRealSecond;
        Assert.That(actualHours, Is.EqualTo(expectedGameHours).Within(0.01));
        Assert.That(
            ContinuousSimulationSystem.GetSpeedMultiplier(state),
            Is.EqualTo(multiplier));
    }

    [Test]
    public void PreparedRoster_CanChangeBeforeMovement_WhenFourFightersRemainSelected()
    {
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "edric", "marta", "torvin", "agnessa" },
            out message);

        Assert.That(changed, Is.True, message);
        Assert.That(
            state.ActiveExpedition.FighterIds,
            Is.EqualTo(new[] { "edric", "marta", "torvin", "agnessa" }));
    }

    [Test]
    public void PreparedRoster_AcceptsFewerThanFourFighters()
    {
        // Канон v1.25: командир + от 0 до 4 бойцов, герой может уйти один.
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "garrick" },
            out message);

        Assert.That(changed, Is.True, message);
        Assert.That(state.ActiveExpedition.FighterIds, Is.EqualTo(new[] { "garrick" }));
    }

    [Test]
    public void PreparedRoster_RejectsMoreThanFourFighters()
    {
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "garrick", "edric", "marta", "torvin", "agnessa" },
            out message);

        Assert.That(changed, Is.False);
        Assert.That(state.ActiveExpedition.FighterIds.Count, Is.EqualTo(4));
        StringAssert.Contains("не больше 4", message);
    }

    [Test]
    public void PreparedRoster_LocksAfterActualMovementBegins()
    {
        GameState state = CreatePreparedExpedition();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        // WM-12: "1 клетка = 1 сутки" — HasExpeditionStartedMoving сверяет
        // фактический сдвиг позиции с порогом (dx²+dy² > 0.0001); при новом,
        // гораздо более медленном темпе 0.25 реальной секунды сдвигает армию
        // на пренебрежимо малую долю клетки, ниже порога. Берём значение с
        // запасом, но всё ещё много меньше RealSecondsPerGameDay — движение
        // должно быть замечено раньше, чем закончится хотя бы одна клетка.
        ContinuousSimulationSystem.Advance(state, 15f, false);

        Assert.That(
            ContinuousSimulationSystem.HasExpeditionStartedMoving(state),
            Is.True);

        string message;
        bool changed = ContinuousPreparationCommands.TrySetPreparedRoster(
            state,
            new List<string> { "edric", "marta", "torvin", "agnessa" },
            out message);

        Assert.That(changed, Is.False);
        Assert.That(
            state.ActiveExpedition.FighterIds.Count,
            Is.EqualTo(4));
    }

    [Test]
    public void LocationArrival_CancelLeavesArmyAtLocation()
    {
        GameState state = CreateArmyAtRuins();
        ExpeditionDecisionOccurrence decision;

        bool created =
            LocationArrivalDecisionFactory.TryCreate(state, out decision);

        Assert.That(created, Is.True);
        Assert.That(decision.OptionA.Label, Is.EqualTo("Исследовать"));
        Assert.That(decision.OptionB.Label, Is.EqualTo("Отменить"));

        string message;
        bool resolved = ExpeditionDecisionSystem.TryApplyChoice(
            state,
            decision.OptionB.Id,
            out message);

        Assert.That(resolved, Is.True, message);
        Assert.That(state.HasPendingExpeditionDecision, Is.False);
        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.AtLocation));
        Assert.That(
            state.ActiveExpedition.LocationId,
            Is.EqualTo("ruins"));
    }

    [Test]
    public void LocationArrival_InvestigateStartsResearch()
    {
        GameState state = CreateArmyAtRuins();
        state.ArmySupply = 100;

        ExpeditionDecisionOccurrence decision;
        Assert.That(
            LocationArrivalDecisionFactory.TryCreate(state, out decision),
            Is.True);

        string message;
        bool resolved = ExpeditionDecisionSystem.TryApplyChoice(
            state,
            decision.OptionA.Id,
            out message);

        Assert.That(resolved, Is.True, message);
        Assert.That(state.HasPendingExpeditionDecision, Is.False);
        Assert.That(state.ActiveExpedition.IsLocationResearchInProgress, Is.True);
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
            new List<string> { "garrick", "edric", "marta", "torvin" },
            out message);

        Assert.That(started, Is.True, message);
        return state;
    }

    private static GameState CreateArmyAtRuins()
    {
        GameState state = new GameState();
        state.CreateNewGame(888);

        LocationData location = state.FindLocation("ruins");
        Assert.That(location, Is.Not.Null);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            location.MapXPercent,
            location.MapYPercent,
            location.Id,
            false,
            new List<string> { "garrick", "edric", "marta", "torvin" },
            out message);

        Assert.That(started, Is.True, message);

        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.CurrentMapXPercent = location.MapXPercent;
        state.ActiveExpedition.CurrentMapYPercent = location.MapYPercent;
        state.ActiveExpedition.RemainingRouteCells = 0;

        CommanderData commander =
            state.FindCommander(state.ActiveExpedition.CommanderId);
        Assert.That(commander, Is.Not.Null);
        commander.State = CommanderState.AtLocation;

        return state;
    }
}
