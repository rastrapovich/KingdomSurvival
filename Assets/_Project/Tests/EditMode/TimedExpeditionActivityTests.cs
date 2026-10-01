using System.Collections.Generic;
using NUnit.Framework;

public class TimedExpeditionActivityTests
{
    [Test]
    public void LocationResearch_TakesConfiguredHoursAndRewardsOnCompletion()
    {
        GameState state = CreateArmyAtRuins();
        LocationData ruins = state.FindLocation("ruins");
        state.ArmySupply = 100;
        ContinuousSimulationSystem.Reset(state);

        string message;
        Assert.That(
            state.TryStartLocationResearch(out message),
            Is.True,
            message);
        Assert.That(ruins.ExplorationHours, Is.EqualTo(2.0));

        // WM-13: было захардкожено 5f (1 час активности при старом
        // GameHoursPerRealSecond) — считаем из константы, чтобы ускорение
        // времени не сдвигало момент "прошёл ровно 1 час из 2".
        float oneActivityHourSeconds = (float)(
            1.0 / ContinuousSimulationSystem.GameHoursPerRealSecond);

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, oneActivityHourSeconds, false);

        Assert.That(ruins.IsExplored, Is.False);
        Assert.That(state.ArmyGold, Is.EqualTo(0));
        Assert.That(state.ArmySupply, Is.EqualTo(100));
        Assert.That(
            state.ActiveExpedition.ActiveActivity.RemainingHours,
            Is.EqualTo(1.0).Within(0.01));

        ContinuousSimulationBatch completed =
            ContinuousSimulationSystem.Advance(state, oneActivityHourSeconds, false);

        Assert.That(ruins.IsExplored, Is.True);
        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmyGold, Is.EqualTo(20));
        Assert.That(state.ArmySupply, Is.EqualTo(106));
        Assert.That(completed.RequestAutoPause, Is.True);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.True);
    }

    [Test]
    public void GatherBerries_StopsForThreeHoursThenRewardsAndResumesRoute()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;
        MakeEveryLocationVisible(state);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);

        // ROAD_BERRY_BUSHES_01 перенесён в Encounter Database (§92-93 —
        // Encounters/Editor/EncounterLegacyRoadEventsSeedData.cs) и удалён из
        // ExpeditionDecisionSystem.Definitions. Этот тест — про генерик-
        // механику Road Stop activity, а не про конкретный контент, поэтому
        // запускает её напрямую тем же путём, что раньше делал TryApplyChoice.
        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "berry_bushes:gather_berries",
                "СБОР ЯГОД",
                3.0,
                0,
                3,
                out message),
            Is.True,
            message);

        float startX = state.ActiveExpedition.CurrentMapXPercent;
        float startY = state.ActiveExpedition.CurrentMapYPercent;
        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(state.ActiveExpedition.IsRoadStopInProgress, Is.True);
        Assert.That(
            state.ActiveExpedition.ActiveActivity.TotalHours,
            Is.EqualTo(3.0));

        // WM-13: было захардкожено 10f (2 часа активности при старом
        // GameHoursPerRealSecond) — считаем из константы, чтобы ускорение
        // времени не завершало активность (3ч) уже на этом Advance.
        float twoActivityHoursSeconds = (float)(
            2.0 / ContinuousSimulationSystem.GameHoursPerRealSecond);

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, twoActivityHoursSeconds, false);

        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(MovedHexes(state, startX, startY), Is.EqualTo(0.0).Within(0.0001), "Во время дела отряд стоит.");

        // 12И: остаток дела (1 ч) по часам дела, затем 6 ч бега по открытой
        // местности — ровно полторы клетки пути (4 ч на клетку).
        double oneCellHours = WorldMapMovementRules.Current.HoursPerHex(WorldMapGameplayTerrainType.OpenGround);
        float secondAdvanceSeconds = (float)(
            (1.0 + oneCellHours * 1.5) / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationBatch completed =
            ContinuousSimulationSystem.Advance(state, secondAdvanceSeconds, false);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        // При базовом эталоне (4ч/клетку) это окно теста больше не обязано
        // пересекать полночь (в отличие от прежнего "1 клетка = 1 сутки",
        // где прохождение клетки гарантированно задевало хотя бы одну
        // полночь) — дневной расход снабжения здесь не срабатывает, снабжение
        // отражает только награду за сбор ягод: 10 + 3 = 13.
        Assert.That(state.ArmySupply, Is.EqualTo(13));
        Assert.That(MovedHexes(state, startX, startY), Is.EqualTo(1.5).Within(0.01), "После дела отряд продолжил путь.");
        Assert.That(completed.RequestAutoPause, Is.False);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.False);
    }

    [Test]
    public void SafeRoadDecision_UsesGenericTimedActivityAndVisibleProgressData()
    {
        GameState state = CreateTravellingState();
        MakeEveryLocationVisible(state);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        // ROAD_UNMAPPED_FORK_01 перенесён в Encounter Database (§92-93) и
        // удалён из ExpeditionDecisionSystem.Definitions — см. комментарий в
        // GatherBerries_... выше.
        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "unmapped_fork:safe_road",
                "БЕЗОПАСНЫЙ ОБХОД",
                1.0,
                0,
                0,
                out message),
            Is.True,
            message);

        ExpeditionActivityData activity =
            state.ActiveExpedition.ActiveActivity;
        Assert.That(activity, Is.Not.Null);
        Assert.That(activity.Kind, Is.EqualTo(ExpeditionActivityKind.RoadStop));
        Assert.That(activity.DisplayName, Is.EqualTo("БЕЗОПАСНЫЙ ОБХОД"));
        Assert.That(activity.TotalHours, Is.EqualTo(1.0));
        Assert.That(activity.Progress01, Is.EqualTo(0.0).Within(0.001));
        Assert.That(state.ActiveExpedition.RouteDelayHoursRemaining, Is.Zero);

        // WM-13: было захардкожено 2.5f (0.5 часа активности при старом
        // GameHoursPerRealSecond) — считаем из константы.
        float halfActivityHourSeconds = (float)(
            0.5 / ContinuousSimulationSystem.GameHoursPerRealSecond);

        float startX = state.ActiveExpedition.CurrentMapXPercent;
        float startY = state.ActiveExpedition.CurrentMapYPercent;
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, halfActivityHourSeconds, false);

        Assert.That(activity.Progress01, Is.EqualTo(0.5).Within(0.01));
        Assert.That(MovedHexes(state, startX, startY), Is.EqualTo(0.0).Within(0.0001));

        // После оставшихся 0.5 ч дела отряд снова идёт по пути.
        float secondAdvanceSeconds = (float)(
            30.0 / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationBatch completed =
            ContinuousSimulationSystem.Advance(state, secondAdvanceSeconds, false);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(MovedHexes(state, startX, startY), Is.GreaterThan(1.0));
        Assert.That(completed.RequestAutoPause, Is.False);
    }

    [Test]
    public void ArmySupplyBatchAdjustment_TransfersAvailableAmountOnly()
    {
        GameState state = new GameState();
        state.CreateNewGame(604);
        state.Food = 7;
        state.ArmySupply = 2;

        Assert.That(state.AdjustArmySupply(5), Is.EqualTo(5));
        Assert.That(state.Food, Is.EqualTo(2));
        Assert.That(state.ArmySupply, Is.EqualTo(7));

        Assert.That(state.AdjustArmySupply(10), Is.EqualTo(2));
        Assert.That(state.Food, Is.Zero);
        Assert.That(state.ArmySupply, Is.EqualTo(9));

        Assert.That(state.AdjustArmySupply(-20), Is.EqualTo(-9));
        Assert.That(state.Food, Is.EqualTo(9));
        Assert.That(state.ArmySupply, Is.Zero);
    }

    [Test]
    public void RouteChange_CancelsRoadActivityWithoutReward()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;

        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "berries",
                "СБОР ЯГОД",
                3.0,
                0,
                3,
                out message),
            Is.True,
            message);

        Assert.That(
            state.TryChangeExpeditionRoute(
                12f,
                65f,
                null,
                out message),
            Is.True,
            message);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmySupply, Is.EqualTo(10));
    }

    [Test]
    public void ReturnOrder_CancelsRoadActivityWithoutReward()
    {
        GameState state = CreateTravellingState();
        state.ArmySupply = 10;
        MakeEveryLocationVisible(state);
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        // 12И: секунда бега — две клетки и 8 часов, до полуночи и суточного расхода.
        ContinuousSimulationSystem.Advance(state, 1f, false);
        ContinuousSimulationSystem.SetPaused(state, true);

        string message;
        Assert.That(
            state.TryStartRoadActivity(
                "berries",
                "СБОР ЯГОД",
                3.0,
                0,
                3,
                out message),
            Is.True,
            message);

        Assert.That(
            ContinuousExpeditionCommands.TryOrderReturn(state, out message),
            Is.True,
            message);

        Assert.That(state.ActiveExpedition.ActiveActivity, Is.Null);
        Assert.That(state.ArmySupply, Is.EqualTo(10));
        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.ReturningToCastle));
    }

    [Test]
    public void WaypointArrival_StopsWithoutModalOrAutoPause()
    {
        GameState state = new GameState();
        state.CreateNewGame(603);
        // WM-12: "1 клетка = 1 сутки" — даже короткий переход теперь
        // пересекает минимум одну полночь; ArmySupply=0 (дефолт
        // CreateNewGame) сразу же обрывает поход автоматическим
        // возвращением (раздел 9.7 канона). Тест — про остановку без
        // модалки/автопаузы на прибытии, не про голод.
        state.ArmySupply = 1000;

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                51f,
                80.5f,
                null,
                false,
                new List<string> { "garrick", "edric", "marta", "torvin" },
                out message),
            Is.True,
            message);

        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.NotifyRouteChanged(state);
        ContinuousSimulationSystem.SetPaused(state, false);

        // WM-12: "1 клетка = 1 сутки" — точная цель может быть в 1-2 клетках
        // от столицы в зависимости от геометрии, а этот вид прибытия не
        // ставит RequestAutoPause (проверяется ниже), значит Advance не
        // остановится сам точно на прибытии — важно не взять времени больше,
        // чем нужно, иначе симуляция проедет дальше. Считаем точно из
        // маршрута, с небольшим (не множительным) запасом.
        double remainingHours =
            ContinuousSimulationSystem.GetTravelHoursRemaining(state);
        float advanceSeconds = (float)(
            (remainingHours + 0.5) / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationBatch arrival =
            ContinuousSimulationSystem.Advance(state, advanceSeconds, false);

        Assert.That(
            state.ActiveExpedition.Phase,
            Is.EqualTo(CommanderState.AtLocation));
        Assert.That(arrival.MandatoryNotice, Is.Null);
        Assert.That(arrival.RequestAutoPause, Is.False);
        Assert.That(ContinuousSimulationSystem.IsPaused(state), Is.False);
    }

    [Test]
    public void TravelEstimate_UsesContinuousArmySpeed()
    {
        List<MapPointData> route = new List<MapPointData>
        {
            new MapPointData(0f, 0f),
            new MapPointData(1f, 0f),
            new MapPointData(2f, 0f),
            new MapPointData(3f, 0f),
            new MapPointData(4f, 0f),
            new MapPointData(5f, 0f)
        };

        // 12И: без ассета настроек ядро берёт 4 игровых часа на клетку открытой
        // местности; при сетке по умолчанию 1% ширины карты — ровно одна клетка.
        WorldMapNavigation.ConfigureDefaultTerrain();
        Assert.That(
            (1.0 / WorldMapMovementRules.Current.HoursPerHex(WorldMapGameplayTerrainType.OpenGround)),
            Is.EqualTo(1.0 / 4.0).Within(0.0001));
        Assert.That(
            ContinuousSimulationSystem.CalculateTravelHours(route),
            Is.EqualTo(20.0).Within(0.001));
    }

    private static double MovedHexes(GameState state, float startX, float startY) =>
        WorldMapNavigation.DistanceHexes(
            startX,
            startY,
            state.ActiveExpedition.CurrentMapXPercent,
            state.ActiveExpedition.CurrentMapYPercent);

    private static GameState CreateTravellingState()
    {
        GameState state = new GameState();
        state.CreateNewGame(602);

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                90f,
                20f,
                null,
                false,
                new List<string> { "garrick", "edric", "marta", "torvin" },
                out message),
            Is.True,
            message);
        return state;
    }

    private static GameState CreateArmyAtRuins()
    {
        GameState state = new GameState();
        state.CreateNewGame(601);
        LocationData ruins = state.FindLocation("ruins");

        string message;
        Assert.That(
            state.TryStartExpeditionToMapPoint(
                ruins.MapXPercent,
                ruins.MapYPercent,
                ruins.Id,
                false,
                new List<string> { "garrick", "edric", "marta", "torvin" },
                out message),
            Is.True,
            message);

        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        state.ActiveExpedition.CurrentMapXPercent = ruins.MapXPercent;
        state.ActiveExpedition.CurrentMapYPercent = ruins.MapYPercent;
        state.ActiveExpedition.RemainingRouteCells = 0;
        state.FindCommander(state.ActiveExpedition.CommanderId).State =
            CommanderState.AtLocation;
        return state;
    }

    private static void MakeEveryLocationVisible(GameState state)
    {
        foreach (LocationData location in state.Locations)
            location.IsVisibleOnMap = true;
    }
}
