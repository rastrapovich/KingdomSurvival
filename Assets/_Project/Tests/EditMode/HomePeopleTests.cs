using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-06А (ProjectDocs/PR06_PEOPLE_SPEC.md §17): люди Дома как одна личность.
// Проверяется поведение через публичные операции и реальную сериализацию.
public sealed class HomePeopleTests
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

    private static GameState NewGame()
    {
        GameState state = new CampaignSetup { WorldSeed = 20260925 }.CreateCampaign();
        state.ArmySupply = 100;
        return state;
    }

    // Готовит поход; departed — отряд уже сдвинулся с места (реально ушёл).
    private static void PrepareExpedition(GameState state, IEnumerable<string> fighters, string retinueId, bool departed)
    {
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        if (retinueId != null)
            Assert.IsTrue(ContinuousPreparationCommands.TrySetPreparedRetinue(state, retinueId, out message), message);
        if (departed)
            state.ActiveExpedition.RouteIndex = 1;
    }

    private static GameState SaveAndLoad(GameState state)
    {
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        return CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));
    }

    // --- Идентичность ---

    [Test]
    public void NewGame_Has24LivingPeople_WithUniqueIds()
    {
        GameState state = NewGame();
        IReadOnlyList<ResidentState> people = HomePeopleService.All(state);

        Assert.AreEqual(24, people.Count);
        Assert.AreEqual(24, people.Select(p => p.PersonId).Distinct().Count());
        Assert.AreEqual(24, HomePeopleService.CountHomeMembers(state));
        Assert.AreEqual(24, state.Population, "Население — производное из реестра.");
        Assert.AreEqual(1, people.Count(p => p.TravelRole == ResidentTravelRole.Commander));
        CollectionAssert.IsSubsetOf(new[] { "garrick", "edric", "marta", "torvin", "agnessa" }, people.Select(p => p.PersonId).ToList());
        CollectionAssert.IsSubsetOf(new[] { "ostafiy", "lada", "miron", "ulyana" }, people.Select(p => p.DialogueSpeakerId).ToList());
        Assert.AreEqual(14, people.Count(p => p.PersonId.StartsWith("home.background.")));
        Assert.AreEqual(4, state.People.Households.Count);
    }

    [Test]
    public void Children_AreNotRetinueOrCombatants()
    {
        GameState state = NewGame();
        foreach (ResidentState child in HomePeopleService.All(state).Where(p => p.AgeGroup == ResidentAgeGroup.Child))
        {
            Assert.AreEqual(ResidentTravelRole.None, child.TravelRole);
            Assert.IsFalse(HomePeopleService.CanJoinAsRetinue(state, child.PersonId, out _));
        }
    }

    // --- Выход, возврат и функции ---

    [Test]
    public void PreparedRoster_DoesNotDisableHomeFunctions_DepartureDoes()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "marta" }, "lada", departed: false);

        Assert.AreEqual(HomeFunctionStatus.Working, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Status,
            "Подготовка — люди ещё дома.");
        Assert.AreEqual(HomeFunctionStatus.Working, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.CareId).Status);

        state.ActiveExpedition.RouteIndex = 1;
        HomeFunctionReport maintenance = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId);
        HomeFunctionReport care = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.CareId);

        Assert.AreEqual(HomeFunctionStatus.Limited, maintenance.Status);
        Assert.AreEqual("ostafiy", maintenance.ExecutorId, "Первым по порядку замещает Остафий.");
        Assert.AreEqual(HomeFunctionStatus.Limited, care.Status);
        Assert.AreEqual("ulyana", care.ExecutorId);
        StringAssert.Contains("в походе", maintenance.Reason);
    }

    [Test]
    public void Maintenance_WithoutAnySubstitute_Stops()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "torvin" }, "lada", departed: true);
        HomePeopleService.Find(state, "ostafiy").Injury = ResidentInjury.Recovering;

        HomeFunctionReport report = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId);
        Assert.AreEqual(HomeFunctionStatus.Stopped, report.Status);
        Assert.AreEqual(0.0, report.Rate);
    }

    [Test]
    public void PartialSubstitute_GivesHalfSpeed_NotFull()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new string[0], "lada", departed: true);
        Assert.AreEqual(0.5, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Rate,
            "Остафий и Торвин дома, но два частичных не складываются в мастера.");
    }

    [Test]
    public void Return_RestoresFunctions()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new string[0], "lada", departed: true);
        Assert.AreEqual(HomeFunctionStatus.Limited, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Status);

        state.ActiveExpedition.IsActive = false;
        Assert.AreEqual(HomeFunctionStatus.Working, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Status);
    }

    // --- Свита ---

    [Test]
    public void Retinue_AcceptsOnlyOstafiyOrLada_AndNotAFighter()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "marta" }, null, departed: false);

        Assert.IsTrue(ContinuousPreparationCommands.TrySetPreparedRetinue(state, "ostafiy", out _));
        Assert.IsFalse(ContinuousPreparationCommands.TrySetPreparedRetinue(state, "miron", out _), "Мирон в ПР-06 домашний специалист.");
        Assert.IsFalse(ContinuousPreparationCommands.TrySetPreparedRetinue(state, "marta", out _), "Боец не может быть и специалистом.");
        Assert.IsFalse(ContinuousPreparationCommands.TrySetPreparedRetinue(state, "home.background.03", out _));
        Assert.IsTrue(ContinuousPreparationCommands.TrySetPreparedRetinue(state, "lada", out _));
        Assert.AreEqual(1, state.ActiveExpedition.RetinueIds.Count, "Свита — не больше одного.");
        Assert.IsTrue(ContinuousPreparationCommands.TrySetPreparedRetinue(state, null, out _));
        Assert.IsEmpty(state.ActiveExpedition.RetinueIds);
    }

    [Test]
    public void RecoveringFighter_CannotStartExpedition()
    {
        GameState state = NewGame();
        HomePeopleService.Find(state, "garrick").Injury = ResidentInjury.Recovering;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsFalse(state.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message));
        StringAssert.Contains("ранен", message);
    }

    // --- Еда ---

    [Test]
    public void Food_24People_Party6_Is18AtHomeAnd6OnTheRoad()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "garrick", "edric", "marta", "torvin" }, "lada", departed: true);

        Assert.AreEqual(6, HomePeopleService.CountExpeditionPresent(state));
        Assert.AreEqual(18, state.DailyFoodConsumption, "Не 24: ушедшие едят из припасов похода.");
        Assert.AreEqual(6, state.ExpeditionSupplyConsumption, "Свита ест из припасов похода.");
    }

    [Test]
    public void BaseHomeStart_FeedsItself()
    {
        GameState state = NewGame();
        Assert.AreEqual(24, BuildingSystem.GetDailyFoodIncome(state));
    }

    [Test]
    public void LongHunger_DoesNotRemovePeople_AndStopsCare()
    {
        GameState state = NewGame();
        state.Food = 0;
        state.BaseDailyFoodIncome = 1;
        ContinuousSimulationSystem.Reset(state);
        ContinuousSimulationSystem.SetPaused(state, false);
        float fiveDays = (float)(24.0 * 5 / ContinuousSimulationSystem.GameHoursPerRealSecond);
        ContinuousSimulationSystem.Advance(state, fiveDays, false);

        Assert.GreaterOrEqual(state.ConsecutiveFoodShortageDays, 4);
        Assert.AreEqual(24, state.Population, "Голод не стирает жителей.");
        Assert.AreEqual(HomeFunctionStatus.Stopped, HomeFunctionResolver.Resolve(state, HomeFunctionResolver.CareId).Status);
    }

    // --- Домашняя работа и уход ---

    [Test]
    public void YardDeck_ChargesOnce_LadaFullSpeed_SubstituteHalf_SurvivesSave()
    {
        GameState state = NewGame();
        int gold = state.Gold;
        Assert.IsTrue(HomeLife.TryStartYardDeck(state, out _));
        Assert.IsFalse(HomeLife.TryStartYardDeck(state, out _));
        Assert.AreEqual(gold - HomeLife.YardDeckGoldCost, state.Gold, "Золото списано один раз.");

        HomeLife.Advance(state, 6.0, null);
        Assert.AreEqual(6.0, HomeLife.FindWork(state, HomeLife.YardDeckWorkId).DoneWork, 0.0001);

        PrepareExpedition(state, new string[0], "lada", departed: true);
        HomeLife.Advance(state, 4.0, null);
        Assert.AreEqual(8.0, HomeLife.FindWork(state, HomeLife.YardDeckWorkId).DoneWork, 0.0001, "Без Лады — половина скорости.");

        GameState restored = SaveAndLoad(state);
        HomeWorkState work = HomeLife.FindWork(restored, HomeLife.YardDeckWorkId);
        Assert.AreEqual(8.0, work.DoneWork, 0.0001);
        Assert.AreEqual(4.0, HomeLife.RemainingWork(work), 0.0001);
    }

    [Test]
    public void YardDeck_CompletesOnce_WithMessage()
    {
        GameState state = NewGame();
        HomeLife.TryStartYardDeck(state, out _);
        List<string> messages = new List<string>();
        HomeLife.Advance(state, 20.0, messages);

        Assert.IsTrue(HomeLife.FindWork(state, HomeLife.YardDeckWorkId).Completed);
        Assert.AreEqual(1, messages.Count(m => m.Contains("настил")));
        HomeLife.Advance(state, 5.0, messages);
        Assert.AreEqual(1, messages.Count(m => m.Contains("настил")), "Завершение не повторяется.");
    }

    [Test]
    public void Care_Marta24h_Ulyana48h()
    {
        GameState state = NewGame();
        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        HomePeopleService.SetHitPoints(garrick, 10);

        HomeLife.Advance(state, 12.0, null);
        Assert.AreEqual(15, garrick.CurrentHitPoints, "Половина цикла Марты — половина недостающего.");
        HomeLife.Advance(state, 12.0, null);
        Assert.AreEqual(20, garrick.CurrentHitPoints);
        Assert.IsFalse(garrick.NeedsCare);

        PrepareExpedition(state, new[] { "marta" }, null, departed: true);
        HomePeopleService.SetHitPoints(garrick, 10);
        HomeLife.Advance(state, 24.0, null);
        Assert.AreEqual(15, garrick.CurrentHitPoints, "У Ульяны цикл вдвое дольше.");
    }

    [Test]
    public void Recovering_ClearsAfterFullCycle()
    {
        GameState state = NewGame();
        ResidentState lada = HomePeopleService.Find(state, "lada");
        lada.Injury = ResidentInjury.Recovering;
        Assert.IsFalse(HomePeopleService.CanWorkAtHome(state, lada));

        HomeLife.Advance(state, 24.0, null);
        Assert.AreEqual(ResidentInjury.None, lada.Injury);
        Assert.IsTrue(HomePeopleService.CanWorkAtHome(state, lada));
    }

    // --- Бой ---

    [Test]
    public void Battle_CarriesPersonHp_AndDeadStaysInRegistry()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "garrick", "edric" }, "ostafiy", departed: true);
        HomePeopleService.SetHitPoints(HomePeopleService.Find(state, "garrick"), 14);

        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "b.hp");
        Assert.AreEqual(3, request.Participants.Count, "Свита в бой не идёт.");
        Assert.AreEqual(14, request.Participants.First(p => p.PersonId == "garrick").CurrentHitPoints);

        CampaignBattleResult result = new CampaignBattleResult { BattleId = "b.hp", Outcome = CampaignBattleOutcome.Victory };
        result.FallenPersonIds.Add("edric");
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = 5 });
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "commander", HitPoints = 20 });
        CampaignBattleBridge.ApplyResult(state, result, null);

        Assert.AreEqual(5, HomePeopleService.Find(state, "garrick").CurrentHitPoints);
        ResidentState edric = HomePeopleService.Find(state, "edric");
        Assert.IsNotNull(edric, "Погибший остаётся в реестре.");
        Assert.AreEqual(ResidentLifeStatus.Dead, edric.LifeStatus);
        Assert.IsNull(state.FindFighter("edric"), "И не выбирается в состав.");
        CollectionAssert.DoesNotContain(state.ActiveExpedition.FighterIds, "edric");
        Assert.AreEqual(23, state.Population);

        GameState restored = SaveAndLoad(state);
        Assert.AreEqual(ResidentLifeStatus.Dead, HomePeopleService.Find(restored, "edric").LifeStatus, "Погибший не воскресает после загрузки.");
        Assert.AreEqual(5, HomePeopleService.Find(restored, "garrick").CurrentHitPoints);
        Assert.AreEqual(23, restored.Population);
    }

    // --- Сохранение ---

    [Test]
    public void Format1Save_IsRejected_WithReason()
    {
        GameState state = NewGame();
        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        data.SaveFormatVersion = 1;

        Assert.IsFalse(CampaignSaveService.IsLoadable(data, string.Empty, 0, out string reason));
        StringAssert.Contains("новую игру", reason);
    }

    [Test]
    public void PeopleAndRetinue_SurviveSaveLoad()
    {
        GameState state = NewGame();
        PrepareExpedition(state, new[] { "marta" }, "lada", departed: true);

        GameState restored = SaveAndLoad(state);
        Assert.AreEqual(24, HomePeopleService.All(restored).Count);
        CollectionAssert.AreEqual(new[] { "lada" }, restored.ActiveExpedition.RetinueIds);
        Assert.IsTrue(HomePeopleService.IsInExpedition(restored, "lada"));
        Assert.AreEqual(HomeFunctionStatus.Limited, HomeFunctionResolver.Resolve(restored, HomeFunctionResolver.CareId).Status);
    }
}
