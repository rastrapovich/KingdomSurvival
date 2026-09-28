using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// 12Е-6 плана: первая партия боевых особенностей (каталог §6.1) со стороны
// кампании — что уходит в бой, что видно в карточке и как особенности
// меняют итог боя. Варианты утверждены автором 28.09.2026. Правила внутри
// боя — SandboxPerkTests.
public sealed class FeatureCombatBatchTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 40, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private static readonly string[] Batch =
    {
        CombatFeatureIds.BasicDefense, CombatFeatureIds.Stubborn, CombatFeatureIds.StillStanding,
        CombatFeatureIds.Counterstrike, CombatFeatureIds.FirstStrike, CombatFeatureIds.ColdEye,
        CombatFeatureIds.Ambush, CombatFeatureIds.LeavesNoOne
    };

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        ProgressionRules.Current = ProgressionRules.CreateDefault();
        ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        ProgressionRules.Current = ProgressionRules.CreateDefault();
        ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260928 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    private static void Give(GameState state, string personId, string featureId, int rank = 1)
    {
        Assert.IsTrue(CharacterFeatureService.Grant(state, personId, featureId, rank, FeatureSource.Story, null, out string message), message);
    }

    private static CampaignBattleResult Result(string battleId, CampaignBattleOutcome outcome,
        Dictionary<string, int> survivors, params string[] fallen)
    {
        CampaignBattleResult result = new CampaignBattleResult { BattleId = battleId, Outcome = outcome, Rounds = 2 };
        result.FallenPersonIds.AddRange(fallen);
        foreach (KeyValuePair<string, int> pair in survivors)
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = pair.Key, HitPoints = pair.Value });
        return result;
    }

    [Test]
    public void CombatBatch_IsActive_OnlyImplementedRanks()
    {
        foreach (string id in Batch)
        {
            Assert.IsTrue(ProgressionFeatureImplementations.IsImplemented(id), id);
            Assert.AreEqual(FeatureStatus.Active, ProgressionCatalog.Current.FindTrait(id).Status, id);
        }
        Assert.AreEqual(1, ProgressionFeatureImplementations.ImplementedRankCount(CombatFeatureIds.BasicDefense), "«Крепкая защита» ещё без кода.");
        Assert.AreEqual(1, ProgressionFeatureImplementations.ImplementedRankCount(CombatFeatureIds.ColdEye), "«Ищет щель» ждёт «Прицелиться».");
        Assert.AreEqual(2, ProgressionFeatureImplementations.ImplementedRankCount(CombatFeatureIds.Counterstrike));
        Assert.AreEqual(FeatureLimit.Expedition, ProgressionCatalog.Current.FindTrait(CombatFeatureIds.LeavesNoOne).Limit);
    }

    [Test]
    public void BattleRules_GoIntoRequest_AndIntoBreakdown()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", CombatFeatureIds.BasicDefense);
        Give(state, "garrick", CombatFeatureIds.Counterstrike, 2);
        Give(state, Hero(state), CombatFeatureIds.StillStanding);

        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "test.perks");
        CampaignBattleParticipant garrick = request.Participants.First(p => p.PersonId == "garrick");
        CollectionAssert.AreEquivalent(
            new[] { CombatPerkIds.BasicDefense, CombatPerkIds.Counterstrike, CombatPerkIds.CounterstrikeDamage },
            garrick.PerkIds);
        CollectionAssert.AreEqual(new[] { CombatPerkIds.StillStanding },
            request.Participants.First(p => p.PersonId == Hero(state)).PerkIds);

        List<string> sources = CombatStatsAssembler.Compute(state, "garrick").Sources;
        Assert.IsTrue(sources.Any(line => line.StartsWith("Основная защита:")));
        Assert.IsTrue(sources.Any(line => line.StartsWith("Контрудар:") && line.Contains("+1 Урон")));
    }

    [Test]
    public void Stubborn_HeavyWoundOnlyAtEighth()
    {
        GameState state = OnTheRoad("garrick", "edric");
        Give(state, "garrick", CombatFeatureIds.Stubborn);
        int garrickMax = HomePeopleService.Find(state, "garrick").MaxHitPoints;
        int edricMax = HomePeopleService.Find(state, "edric").MaxHitPoints;
        Assert.Greater(garrickMax / 4, garrickMax / 8, "Между порогами есть зазор.");

        CampaignBattleBridge.ApplyResult(state, Result("test.stubborn", CampaignBattleOutcome.Victory,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", garrickMax / 4 }, { "edric", edricMax / 4 } }), new List<string>());

        Assert.AreEqual(ResidentInjury.None, HomePeopleService.Find(state, "garrick").Injury, "Упрямец: четверть — ещё не тяжёлая рана.");
        Assert.AreEqual(ResidentInjury.Recovering, HomePeopleService.Find(state, "edric").Injury);
    }

    [Test]
    public void StillStanding_ForcesHeavyWound_NoteAndActivation()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", CombatFeatureIds.StillStanding);
        CampaignBattleResult result = Result("test.standing", CampaignBattleOutcome.Victory,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", 30 } });
        result.ForcedHeavyWoundIds.Add("garrick");
        List<string> notes = new List<string>();

        CampaignBattleBridge.ApplyResult(state, result, new List<string>(), notes);

        Assert.AreEqual(ResidentInjury.Recovering, HomePeopleService.Find(state, "garrick").Injury, "После боя — обязательная тяжёлая рана.");
        Assert.IsTrue(notes.Any(n => n.Contains("«Ещё на ногах»")));
        Assert.IsTrue(CharacterProgressionService.EnsureState(state).RecentFeatureActivations
            .Any(a => a.PersonId == "garrick" && a.FeatureId == CombatFeatureIds.StillStanding));
    }

    [Test]
    public void StillStanding_RecordedEvenWithoutNotes()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", CombatFeatureIds.StillStanding);
        CampaignBattleResult result = Result("test.standing_quiet", CampaignBattleOutcome.Victory,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", 30 } });
        result.ForcedHeavyWoundIds.Add("garrick");

        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        Assert.IsTrue(CharacterProgressionService.EnsureState(state).RecentFeatureActivations
            .Any(a => a.PersonId == "garrick" && a.FeatureId == CombatFeatureIds.StillStanding));
    }

    [Test]
    public void LeavesNoOne_OnRetreat_CarriesFallenFighterAlive_OncePerExpedition()
    {
        GameState state = OnTheRoad("garrick", "edric");
        Give(state, Hero(state), CombatFeatureIds.LeavesNoOne);
        List<string> notes = new List<string>();

        CampaignBattleBridge.ApplyResult(state, Result("test.retreat1", CampaignBattleOutcome.Retreat,
            new Dictionary<string, int> { { Hero(state), 20 }, { "edric", 20 } }, "garrick"), new List<string>(), notes);

        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        Assert.IsTrue(garrick.IsAlive, "Вынесен из боя живым.");
        Assert.AreEqual(1, garrick.CurrentHitPoints);
        Assert.AreEqual(ResidentInjury.Recovering, garrick.Injury);
        Assert.IsTrue(notes.Any(n => n.Contains("«Не бросает своих»")));

        CampaignBattleBridge.ApplyResult(state, Result("test.retreat2", CampaignBattleOutcome.Retreat,
            new Dictionary<string, int> { { Hero(state), 20 }, { "garrick", 1 } }, "edric"), new List<string>());
        Assert.IsFalse(HomePeopleService.Find(state, "edric").IsAlive, "Раз за поход.");
    }

    [Test]
    public void LeavesNoOne_OnlyOnRetreat()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, Hero(state), CombatFeatureIds.LeavesNoOne);

        CampaignBattleBridge.ApplyResult(state, Result("test.victory", CampaignBattleOutcome.Victory,
            new Dictionary<string, int> { { Hero(state), 20 } }, "garrick"), new List<string>());

        Assert.IsFalse(HomePeopleService.Find(state, "garrick").IsAlive, "Победа — павший не выносится.");
    }

    [Test]
    public void Ambush_PreparedStart_GivesSquadFirstRoundInitiative()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, Hero(state), CombatFeatureIds.Ambush);

        CampaignBattleRequest open = CampaignBattleBridge.CreateRequest(state, "test.open");
        FeatureCombatBatch.ApplyPreparedStart(state, open);
        Assert.AreEqual(0, open.PlayerFirstRoundInitiativeBonus, "Бой не подготовлен — без прибавки.");

        CampaignBattleRequest prepared = CampaignBattleBridge.CreateRequest(state, "test.prepared");
        prepared.PreparedStart = true;
        FeatureCombatBatch.ApplyPreparedStart(state, prepared);
        Assert.AreEqual(FeatureCombatBatch.AmbushInitiativeBonus, prepared.PlayerFirstRoundInitiativeBonus);
        Assert.IsTrue(prepared.Notes.Any(n => n.Contains("«Засада»")));
    }
}
