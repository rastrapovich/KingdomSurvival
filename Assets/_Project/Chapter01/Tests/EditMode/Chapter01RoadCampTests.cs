using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using UnityEngine;
using static Chapter01PlaythroughWalker;

// ПР-09 (ProjectDocs/PR09_ROAD_CAMP_SPEC.md): ночлег, дела стоянки, итог
// исследования, старый брод на обратном пути, замер темпа главы.
public sealed class Chapter01RoadCampTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState GameOnTheRoad(IEnumerable<string> fighters, string retinue = null)
    {
        GameState gameState = NewGame(20260925);
        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, fighters.ToList(), out string message, retinue), message);
        gameState.ActiveExpedition.RouteIndex = 1;
        return gameState;
    }

    private static List<string> AdvanceHours(GameState gameState, double hours)
    {
        List<string> messages = new List<string>();
        ContinuousSimulationSystem.SetSpeedMultiplier(gameState, ContinuousSimulationSystem.NormalSpeedMultiplier);
        double done = 0.0;
        for (int i = 0; i < 400 && done < hours - 0.0001; i++)
        {
            double chunk = System.Math.Min(1.0, hours - done);
            ContinuousSimulationSystem.SetPaused(gameState, false);
            double before = gameState.Day * 24.0 + ContinuousSimulationSystem.GetClock(gameState).HourOfDay;
            ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(
                gameState, (float)(chunk / ContinuousSimulationSystem.GameHoursPerRealSecond), false);
            messages.AddRange(batch.Result.Messages);
            done += gameState.Day * 24.0 + ContinuousSimulationSystem.GetClock(gameState).HourOfDay - before;
        }
        return messages;
    }

    // --- Ночлег ---

    [Test]
    public void Rest_Takes8Hours_ClearsExhaustion_NoExtraSupply_KeepsRoute()
    {
        GameState gameState = GameOnTheRoad(new[] { "garrick" });
        HomePeopleService.Find(gameState, "garrick").Exhausted = true;
        int supply = gameState.ArmySupply;
        List<MapPointData> route = new List<MapPointData>(gameState.ActiveExpedition.Route);
        string target = gameState.ActiveExpedition.LocationId;

        Assert.IsTrue(CampRest.TryStartRest(gameState, out string message), message);
        Assert.IsFalse(CampRest.TryStartRest(gameState, out message), "Второй ночлег поверх первого нельзя.");

        double startHour = ContinuousSimulationSystem.GetClock(gameState).HourOfDay;
        List<string> messages = AdvanceHours(gameState, 7.5);
        Assert.IsTrue(CampRest.IsResting(gameState), "Ночлег ещё идёт.");
        messages.AddRange(AdvanceHours(gameState, 1.0));

        Assert.IsFalse(CampRest.IsResting(gameState));
        Assert.IsFalse(HomePeopleService.Find(gameState, "garrick").Exhausted);
        Assert.IsTrue(messages.Any(m => m.StartsWith("Утро после ночлега")), string.Join("\n", messages));
        Assert.AreEqual(target, gameState.ActiveExpedition.LocationId, "Цель не меняется.");
        Assert.AreEqual(route.Count, gameState.ActiveExpedition.Route.Count, "Маршрут не перестраивается.");

        bool crossedMidnight = startHour + 8.5 >= 24.0;
        if (!crossedMidnight)
            Assert.AreEqual(supply, gameState.ArmySupply, "Ночлег сам припасы не тратит.");
    }

    [Test]
    public void Rest_NotAtHome_NotDuringDecision()
    {
        GameState home = NewGame(20260925);
        Assert.IsFalse(CampRest.TryStartRest(home, out string message));
        StringAssert.Contains("в походе", message);

        GameState road = GameOnTheRoad(new[] { "garrick" });
        road.ActiveExpedition.PendingDecision = new ExpeditionDecisionOccurrence { Id = 1, Title = "test" };
        Assert.IsFalse(CampRest.TryStartRest(road, out message));
    }

    [Test]
    public void CalmNight_OneShortMessage()
    {
        GameState gameState = GameOnTheRoad(new[] { "garrick" });
        List<string> messages = new List<string>();
        CampRest.CompleteRest(gameState, messages);
        CollectionAssert.AreEqual(new[] { "Ночь прошла спокойно. Отряд продолжает путь." }, messages);
    }

    // --- Дела стоянки ---

    [Test]
    public void Actions_AtMostTwo_AndOnlyWhenJustified()
    {
        GameState alone = GameOnTheRoad(new string[0]);
        Assert.IsFalse(CampRest.TryToggleAction(alone, CampActionKind.Bandage, out string message));
        StringAssert.Contains("Марта не в отряде", message);
        Assert.IsFalse(CampRest.TryToggleAction(alone, CampActionKind.Watch, out message));

        GameState party = GameOnTheRoad(new[] { "marta", "agnessa" });
        ResidentState agnessa = HomePeopleService.Find(party, "agnessa");
        HomePeopleService.SetHitPoints(agnessa, agnessa.MaxHitPoints - 8);
        CampRest.GetNight(party).NearWater = true;

        Assert.IsTrue(CampRest.TryToggleAction(party, CampActionKind.Bandage, out message), message);
        Assert.IsTrue(CampRest.TryToggleAction(party, CampActionKind.Inspect, out message), message);
        Assert.IsFalse(CampRest.TryToggleAction(party, CampActionKind.Watch, out message));
        StringAssert.Contains("не больше двух", message);
        Assert.IsTrue(CampRest.TryToggleAction(party, CampActionKind.Inspect, out message), "Повторный выбор снимает.");
        Assert.IsTrue(CampRest.TryToggleAction(party, CampActionKind.Watch, out message), message);
    }

    [Test]
    public void Bandage_WithHealerBag_HealsHalf_WithoutBag_Quarter()
    {
        GameState gameState = GameOnTheRoad(new[] { "marta", "garrick" });
        ResidentState garrick = HomePeopleService.Find(gameState, "garrick");
        HomePeopleService.SetHitPoints(garrick, garrick.MaxHitPoints - 8);

        Assert.IsTrue(CampRest.TryToggleAction(gameState, CampActionKind.Bandage, out string message), message);
        CampRest.CompleteRest(gameState, new List<string>());
        Assert.AreEqual(garrick.MaxHitPoints - 4, garrick.CurrentHitPoints, "С сумкой — половина недостающего.");

        ItemInstanceData bag = ItemService.Equipped(gameState, "marta", ItemSlot.Special);
        Assert.IsTrue(ItemService.TryUnequip(gameState, bag.InstanceId, out message), message);
        HomePeopleService.SetHitPoints(garrick, garrick.MaxHitPoints - 8);
        Assert.IsTrue(CampRest.TryToggleAction(gameState, CampActionKind.Bandage, out message), message);
        CampRest.CompleteRest(gameState, new List<string>());
        Assert.AreEqual(garrick.MaxHitPoints - 6, garrick.CurrentHitPoints, "Без сумки — четверть.");
    }

    [Test]
    public void Inspect_RevealsHiddenPlaceNearby()
    {
        GameState gameState = GameOnTheRoad(new[] { "agnessa" });
        LocationData hidden = gameState.Locations.FirstOrDefault(l => !l.IsWaypoint && !l.IsVisibleOnMap);
        if (hidden == null)
            Assert.Ignore("В этом мире нет скрытых мест.");
        gameState.ActiveExpedition.CurrentMapXPercent = hidden.MapXPercent + 1f;
        gameState.ActiveExpedition.CurrentMapYPercent = hidden.MapYPercent;

        Assert.IsTrue(CampRest.TryToggleAction(gameState, CampActionKind.Inspect, out string message), message);
        List<string> messages = new List<string>();
        CampRest.CompleteRest(gameState, messages);

        Assert.IsTrue(hidden.IsVisibleOnMap);
        StringAssert.Contains(hidden.Name, messages[0]);
    }

    // --- Исследование ---

    [Test]
    public void ResearchResult_TellsWhatWasSeen_ModestRewards()
    {
        IReadOnlyList<WorldMapLocationTemplateData> defaults = WorldMapLocationDefaults.Create();
        WorldMapLocationTemplateData ruins = defaults.Single(t => t.Id == "ruins");
        Assert.AreEqual(6, ruins.RewardArmySupply);
        Assert.AreEqual(20, ruins.RewardArmyGold);
        Assert.IsFalse(string.IsNullOrEmpty(ruins.ResearchResultText));
        Assert.AreEqual(40, defaults.Single(t => t.Id == "mine").RewardArmyGold);
    }

    // --- Старый брод ---

    private static GameState ReturningPastFord(bool braced)
    {
        GameState gameState = NewGame(20260925);
        PlayUntil(gameState, new Options(), g => g.Narrative.HasFlag(Chapter01Ids.Flags.ReturnStarted) &&
                                                  g.HasActiveExpedition &&
                                                  g.ActiveExpedition.Phase == CommanderState.ReturningToCastle);
        NarrativeStateData state = gameState.Narrative;
        state.ClearFlag(Chapter01Ids.Flags.FordReturnHandled);
        state.ClearFlag(Chapter01Ids.Flags.FordAccessBypass);
        state.ClearFlag(Chapter01Ids.Flags.FordAccessBracedSupport);
        state.SetFlag(Chapter01Ids.Flags.FordAccessResolved);
        state.SetFlag(braced ? Chapter01Ids.Flags.FordAccessBracedSupport : Chapter01Ids.Flags.FordAccessBypass);
        gameState.ActiveExpedition.ActiveActivity = null;
        ExpeditionData expedition = gameState.ActiveExpedition;
        expedition.RemainingRouteCells = expedition.RouteLengthCells / 2;
        return gameState;
    }

    [Test]
    public void FordOnReturn_BypassAgain_WhenEdgeWasNotBraced()
    {
        GameState gameState = ReturningPastFord(braced: false);
        string report = Chapter01StoryDirector.TryApplyFordReturnCrossing(gameState);

        StringAssert.Contains("снова обход", report);
        Assert.AreEqual(2.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 1e-9);
        Assert.IsNull(Chapter01StoryDirector.TryApplyFordReturnCrossing(gameState), "Один раз.");
    }

    [Test]
    public void FordOnReturn_NoStop_WhenLadaBracedTheEdge_AndCampByTheWater()
    {
        GameState gameState = ReturningPastFord(braced: true);
        string report = Chapter01StoryDirector.TryApplyFordReturnCrossing(gameState);

        StringAssert.Contains("без остановки", report);
        Assert.IsFalse(gameState.ActiveExpedition.HasTimedActivity);

        LocationData ford = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
        gameState.ActiveExpedition.CurrentMapXPercent = ford.MapXPercent;
        gameState.ActiveExpedition.CurrentMapYPercent = ford.MapYPercent;
        Chapter01StoryDirector.RefreshRoadState(gameState);
        Assert.IsTrue(CampRest.GetNight(gameState).NearWater);
        StringAssert.Contains("у старого брода", Chapter01StoryDirector.DescribeCampPlace(gameState));
    }

    // --- Темп главы (замер для §8 ТЗ, числа не меняются) ---

    [Test]
    public void ChapterPace_Measured()
    {
        GameState gameState = NewGame(20260925);
        double start = 0, arrival = 0, downstream = 0, home = 0;
        int supplyAtStart = 0;
        PlayUntil(gameState, new Options(), g => g.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
        start = Now(gameState);
        supplyAtStart = gameState.ArmySupply;
        PlayUntil(gameState, new Options(), g => g.Narrative.HasFlag(Chapter01Ids.Flags.RoadDestinationReached));
        arrival = Now(gameState);
        PlayUntil(gameState, new Options(), g => g.Narrative.HasFlag(Chapter01Ids.Flags.DownstreamContact));
        downstream = Now(gameState);
        PlayUntil(gameState, new Options(), g => g.Narrative.HasFlag(Chapter01Ids.Flags.ReturnedHome));
        home = Now(gameState);

        string report =
            "Темп главы (часы игры): до области поиска " + (arrival - start).ToString("0.0") +
            "; до нижнего поселения " + (downstream - start).ToString("0.0") +
            "; весь поход " + (home - start).ToString("0.0") +
            " (" + ((home - start) / 24.0).ToString("0.0") + " сут.); припасов на старте у прогона " + supplyAtStart +
            ", расход отряда в сутки " + CampRest.PartyIds(gameState).Count + ".";
        TestContext.WriteLine(report);
        Debug.Log(report);
        Assert.Greater(home, start);
    }

    private static double Now(GameState gameState)
    {
        return gameState.Day * 24.0 + ContinuousSimulationSystem.GetClock(gameState).HourOfDay;
    }
}
