using System.Collections.Generic;
using NUnit.Framework;

public class ContinuousSimulationTests
{
    [Test]
    public void Clock_StartsPausedAtDayOneEightHundred()
    {
        GameState state = new GameState();
        state.CreateNewGame(1234);
        ContinuousSimulationSystem.Reset(state);

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        Assert.That(state.Day, Is.EqualTo(1));
        Assert.That(clock.HourOfDay, Is.EqualTo(8.0).Within(0.001));
        Assert.That(clock.IsPaused, Is.True);
        Assert.That(clock.SpeedMultiplier, Is.EqualTo(1));
    }

    // WM-13: явный контрактный тест — число здесь НАМЕРЕННО захардкожено, а
    // не выведено из RealSecondsPerGameDay. Если темп мира когда-нибудь
    // изменится, этот тест должен сломаться и заставить осознанно обновить
    // число здесь, а не молча продолжать проходить вместе с константой.
    [Test]
    public void Clock_SixtyRealSecondsEqualTwentyFourGameHoursAtNormalSpeed()
    {
        GameState state = new GameState();
        state.CreateNewGame(4321);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 60f, false);
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);

        // 08:00 + 24 часа = снова 08:00 следующих суток.
        Assert.That(state.Day, Is.EqualTo(2));
        Assert.That(clock.HourOfDay, Is.EqualTo(8.0).Within(0.01));
    }

    // WM-13: явный контрактный тест — та же намеренная захардкоженная
    // константа (60 секунд), проверяющая независимо второй канонический
    // факт: "1 клетка маршрута = 24 игровых часа" на обычной скорости.
    [Test]
    public void Expedition_OneCellAdvancesExactlyTwentyFourGameHoursAtNormalSpeed()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;
        int startDay = state.Day;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 60f, false);

        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex + 1));
        Assert.That(state.Day, Is.EqualTo(startDay + 1));
        Assert.That(
            ContinuousSimulationSystem.GetClock(state).HourOfDay,
            Is.EqualTo(startHour).Within(0.01),
            "1 клетка маршрута должна занимать ровно 24 часа — часы суток должны вернуться к тому же значению на следующий день.");
    }

    [Test]
    public void Pause_StopsClockAndArmyMovement()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 10f, false);

        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex));
        Assert.That(
            ContinuousSimulationSystem.GetClock(state).HourOfDay,
            Is.EqualTo(startHour).Within(0.001));
    }

    [Test]
    public void FastSpeed_TriplesClockAndMovesOneCellPerGameDay()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.ToggleSpeed(state);

        ExpeditionData expedition = state.ActiveExpedition;
        int startIndex = expedition.RouteIndex;
        int startDay = state.Day;
        double startHour = ContinuousSimulationSystem.GetClock(state).HourOfDay;

        ContinuousSimulationSystem.Advance(state, 2f, false);

        // WM-13: было захардкожено 1.2 под старое RealSecondsPerGameDay —
        // считаем ожидание из констант, чтобы ускорение времени не ломало тест.
        double expectedHours =
            2.0 *
            ContinuousSimulationSystem.FastSpeedMultiplier *
            ContinuousSimulationSystem.GameHoursPerRealSecond;
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(state);
        Assert.That(clock.HourOfDay - startHour, Is.EqualTo(expectedHours).Within(0.02));
        Assert.That(
            expedition.RouteIndex,
            Is.EqualTo(startIndex),
            "при новом темпе ('1 клетка = 1 сутки') 2 реальные секунды ещё не завершают клетку");

        // WM-12: при Fast (×3) та же клетка (1 сутки) проходится за
        // RealSecondsPerGameDay/FastSpeedMultiplier реальных секунд —
        // догоняем остаток времени до полной клетки.
        float remainingSeconds = (float)(
            ContinuousSimulationSystem.RealSecondsPerGameDay /
            ContinuousSimulationSystem.FastSpeedMultiplier) - 2f;
        ContinuousSimulationSystem.Advance(state, remainingSeconds, false);

        Assert.That(expedition.RouteIndex, Is.EqualTo(startIndex + 1));
        Assert.That(state.Day, Is.EqualTo(startDay + 1));
    }

    [Test]
    public void RouteChange_MidCellPreservesExactArmyPosition()
    {
        GameState state = CreateTravellingState();
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        ContinuousSimulationSystem.Advance(state, 1f, false);
        float exactX = state.ActiveExpedition.CurrentMapXPercent;
        float exactY = state.ActiveExpedition.CurrentMapYPercent;

        string message;
        Assert.That(
            state.TryChangeExpeditionRoute(
                12f,
                65f,
                null,
                out message),
            Is.True,
            message);

        ContinuousSimulationSystem.NotifyRouteChanged(state);

        Assert.That(
            state.ActiveExpedition.CurrentMapXPercent,
            Is.EqualTo(exactX).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.CurrentMapYPercent,
            Is.EqualTo(exactY).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.Route[0].XPercent,
            Is.EqualTo(exactX).Within(0.001f));
        Assert.That(
            state.ActiveExpedition.Route[0].YPercent,
            Is.EqualTo(exactY).Within(0.001f));
    }

    [Test]
    public void Midnight_ResolvesDailyEconomyOnce()
    {
        GameState state = new GameState();
        state.CreateNewGame(99);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        int startGold = state.Gold;
        int startFood = state.Food;
        int expectedFood =
            startFood + state.DailyFoodIncome - state.DailyFoodConsumption;

        // WM-13: было захардкожено 80f (16 игровых часов при старом
        // GameHoursPerRealSecond) — считаем из константы, чтобы ускорение
        // времени не заставляло тест случайно пересекать вторую полночь.
        double hoursToMidnight = 24.0 - ContinuousSimulationSystem.StartHour;
        float advanceSeconds = (float)(
            hoursToMidnight / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationSystem.Advance(state, advanceSeconds, false);

        Assert.That(state.Day, Is.EqualTo(2));
        Assert.That(state.Gold, Is.EqualTo(startGold + state.DailyGoldIncome));
        Assert.That(state.Food, Is.EqualTo(expectedFood));
    }

    private static GameState CreateTravellingState()
    {
        GameState state = new GameState();
        state.CreateNewGame(2026);

        string message;
        bool started = state.TryStartExpeditionToMapPoint(
            90f,
            20f,
            null,
            false,
            new List<string> { "garrick", "edric", "marta", "torvin" },
            out message);

        Assert.That(started, Is.True, message);
        Assert.That(state.ActiveExpedition.FighterIds.Count, Is.EqualTo(4));
        Assert.That(state.ActiveExpedition.Route.Count, Is.GreaterThan(5));
        return state;
    }
}
