using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// ПР-10 (ProjectDocs/PR10_CAMPAIGN_BATTLE_SPEC.md §3–§5): последствия боя
// для людей — тяжёлая рана, изнеможение, отход; запрос несёт seed и не
// берёт в бой тяжелораненых.
public sealed class BattleAftermathPr10Tests
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

    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260925 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static CampaignBattleResult Result(GameState state, CampaignBattleOutcome outcome, int rounds,
        Dictionary<string, int> hitPoints, params string[] fallen)
    {
        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = "test." + outcome + rounds + hitPoints.Count + fallen.Length,
            Outcome = outcome,
            Rounds = rounds
        };
        result.FallenPersonIds.AddRange(fallen);
        foreach (KeyValuePair<string, int> pair in hitPoints)
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = pair.Key, HitPoints = pair.Value });
        return result;
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    [Test]
    public void HeavyWound_AtQuarterOrBelow_OnlyWoundAbove()
    {
        GameState state = OnTheRoad("garrick", "edric");
        int garrickMax = HomePeopleService.Find(state, "garrick").MaxHitPoints;
        int edricMax = HomePeopleService.Find(state, "edric").MaxHitPoints;
        List<string> notes = new List<string>();

        CampaignBattleBridge.ApplyResult(state, Result(state, CampaignBattleOutcome.Victory, 3,
            new Dictionary<string, int>
            {
                { Hero(state), 20 },
                { "garrick", garrickMax / 4 },
                { "edric", edricMax / 4 + 1 }
            }), new List<string>(), notes);

        Assert.AreEqual(ResidentInjury.Recovering, HomePeopleService.Find(state, "garrick").Injury);
        Assert.AreEqual(ResidentInjury.None, HomePeopleService.Find(state, "edric").Injury, "Выше четверти — просто рана.");
        Assert.IsTrue(notes.Any(n => n.StartsWith("Тяжело ранены: Гаррик")));
        Assert.IsFalse(HomePeopleService.Find(state, "garrick").Exhausted, "Короткий бой без потерь не выматывает.");
    }

    [Test]
    public void Exhaustion_AfterLossesOrLongBattle()
    {
        GameState withLoss = OnTheRoad("garrick", "edric");
        CampaignBattleBridge.ApplyResult(withLoss, Result(withLoss, CampaignBattleOutcome.Victory, 2,
            new Dictionary<string, int> { { Hero(withLoss), 20 }, { "garrick", 20 } }, "edric"), new List<string>());
        Assert.IsTrue(HomePeopleService.Find(withLoss, "garrick").Exhausted);
        Assert.IsTrue(HomePeopleService.Find(withLoss, Hero(withLoss)).Exhausted);

        GameState longFight = OnTheRoad("garrick");
        CampaignBattleBridge.ApplyResult(longFight, Result(longFight, CampaignBattleOutcome.Victory, 7,
            new Dictionary<string, int> { { Hero(longFight), 20 }, { "garrick", 20 } }), new List<string>());
        Assert.IsTrue(HomePeopleService.Find(longFight, "garrick").Exhausted, "Дольше 6 раундов — изнеможение.");
    }

    [Test]
    public void Retreat_LosesSupply_InterruptsRest_ExhaustsAll_Once()
    {
        GameState state = OnTheRoad("garrick");
        Assert.IsTrue(CampRest.TryStartRest(state, out string message), message);
        int supply = state.ArmySupply;
        CampaignBattleResult retreat = Result(state, CampaignBattleOutcome.Retreat, 2,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", 18 } });

        Assert.AreEqual(CampaignBattleApplyStatus.SquadSurvived, CampaignBattleBridge.ApplyResult(state, retreat, new List<string>()));
        Assert.AreEqual(supply - CampaignBattleBridge.RetreatSupplyLoss, state.ArmySupply);
        Assert.IsFalse(CampRest.IsResting(state), "Ночлег прерван.");
        Assert.IsTrue(HomePeopleService.Find(state, "garrick").Exhausted);
        Assert.IsTrue(HomePeopleService.Find(state, Hero(state)).Exhausted);

        Assert.AreEqual(CampaignBattleApplyStatus.AlreadyApplied, CampaignBattleBridge.ApplyResult(state, retreat, new List<string>()));
        Assert.AreEqual(supply - CampaignBattleBridge.RetreatSupplyLoss, state.ArmySupply, "Один раз.");
    }

    [Test]
    public void Victory_DuringRest_RestContinues()
    {
        GameState state = OnTheRoad("garrick");
        Assert.IsTrue(CampRest.TryStartRest(state, out string message), message);
        CampaignBattleBridge.ApplyResult(state, Result(state, CampaignBattleOutcome.Victory, 2,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", 20 } }), new List<string>());
        Assert.IsTrue(CampRest.IsResting(state), "После победы ночлег досыпается.");
    }

    [Test]
    public void HeroFell_NoAftermathApplied()
    {
        GameState state = OnTheRoad("garrick");
        int supply = state.ArmySupply;
        CampaignBattleApplyStatus status = CampaignBattleBridge.ApplyResult(state, Result(state, CampaignBattleOutcome.Defeat, 9,
            new Dictionary<string, int> { { "garrick", 2 } }, Hero(state)), new List<string>());
        Assert.AreEqual(CampaignBattleApplyStatus.HeroFell, status);
        Assert.AreEqual(supply, state.ArmySupply);
    }

    [Test]
    public void Request_SkipsHeavilyWounded_HasSeed()
    {
        GameState state = OnTheRoad("garrick", "edric");
        HomePeopleService.Find(state, "edric").Injury = ResidentInjury.Recovering;
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "test.request");

        CollectionAssert.AreEquivalent(new[] { Hero(state), "garrick" }, request.Participants.Select(p => p.PersonId));
        Assert.AreNotEqual(0, request.Seed);
        Assert.AreEqual(request.Seed, CampaignBattleBridge.CreateRequest(state, "test.request").Seed, "Seed стабилен для одного боя.");
    }
}
