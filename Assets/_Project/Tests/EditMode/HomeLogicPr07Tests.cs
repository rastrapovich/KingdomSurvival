using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-07А-1 (ProjectDocs/PR07_HOME_SPEC.md §14.1): логика Дома без нового
// экрана — единая подготовка состава и её команды, запасы и защита словами,
// отключённое настроение, домашние команды только из Дома.
public sealed class HomeLogicPr07Tests
{
    private static GameState NewGame()
    {
        GameState state = new CampaignSetup { WorldSeed = 20260925 }.CreateCampaign();
        state.ArmySupply = 100;
        return state;
    }

    private static List<string> Fighters(GameState state)
    {
        return ExpeditionPreparation.GetFighterIds(state).ToList();
    }

    private static void Depart(GameState state)
    {
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, Fighters(state), out string message,
            ExpeditionPreparation.GetRetinueId(state)), message);
        state.ActiveExpedition.RouteIndex = 1;
    }

    // --- Подготовка состава (A05–A07, A10, A13, A14) ---

    [Test]
    public void Fighters_AddReplaceMoveRemove_KeepOrder_NoDuplicates()
    {
        GameState state = NewGame();
        string message;

        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "torvin", out message), message);
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out message), message);
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out message), "Повтор — без изменений.");
        CollectionAssert.AreEqual(new[] { "torvin", "garrick" }, Fighters(state), "Порядок задаёт игрок.");

        Assert.IsTrue(ExpeditionPreparation.TryPlaceFighter(state, "agnessa", 0, out message), message);
        CollectionAssert.AreEqual(new[] { "agnessa", "garrick" }, Fighters(state), "Замена занятого места.");
        Assert.IsTrue(HomePeopleService.Find(state, "torvin").IsHomeMember, "Снятый остаётся дома.");

        Assert.IsTrue(ExpeditionPreparation.TryMoveFighter(state, 0, 1, out message), message);
        CollectionAssert.AreEqual(new[] { "garrick", "agnessa" }, Fighters(state));

        Assert.IsTrue(ExpeditionPreparation.TryRemove(state, "garrick", out message), message);
        CollectionAssert.AreEqual(new[] { "agnessa" }, Fighters(state));
    }

    [Test]
    public void FifthFighter_Rejected_RosterIntact()
    {
        GameState state = NewGame();
        foreach (string id in new[] { "garrick", "edric", "marta", "torvin" })
            Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, id, out _));

        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, "agnessa", out string message));
        StringAssert.Contains("заменить", message);
        CollectionAssert.AreEqual(new[] { "garrick", "edric", "marta", "torvin" }, Fighters(state));
    }

    [Test]
    public void WrongRoles_ChildCommanderAndRecovering_AreRejectedWithReason()
    {
        GameState state = NewGame();
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out _));
        string message;

        Assert.IsFalse(ExpeditionPreparation.TrySetRetinue(state, "edric", out message));
        Assert.AreEqual("Это место для специалиста.", message);

        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, "lada", out message));
        Assert.AreEqual("Этот человек не участвует в бою.", message);

        ResidentState child = HomePeopleService.All(state).First(r => r.AgeGroup == ResidentAgeGroup.Child);
        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, child.PersonId, out message));
        StringAssert.Contains("ребёнок", message);

        string commanderId = state.GetSelectedCommander().Id;
        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, commanderId, out message));
        Assert.AreEqual("Командир ведёт этот поход.", message);
        Assert.IsFalse(ExpeditionPreparation.TryRemove(state, commanderId, out message));

        HomePeopleService.Find(state, "marta").Injury = ResidentInjury.Recovering;
        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, "marta", out message));
        StringAssert.Contains("восстанавливается", message);

        CollectionAssert.AreEqual(new[] { "garrick" }, Fighters(state), "Отказы не меняют состав.");
    }

    [Test]
    public void Retinue_SelectReplaceRemove_ZeroOrOne()
    {
        GameState state = NewGame();
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out _));

        Assert.IsTrue(ExpeditionPreparation.TrySetRetinue(state, "lada", out _));
        Assert.AreEqual("lada", ExpeditionPreparation.GetRetinueId(state));
        Assert.IsTrue(ExpeditionPreparation.TrySetRetinue(state, "ostafiy", out _));
        Assert.AreEqual("ostafiy", ExpeditionPreparation.GetRetinueId(state));
        Assert.IsTrue(ExpeditionPreparation.TryRemove(state, "ostafiy", out _));
        Assert.IsNull(ExpeditionPreparation.GetRetinueId(state));
        CollectionAssert.AreEqual(new[] { "garrick" }, Fighters(state), "Боевые места не затронуты.");
    }

    [Test]
    public void Preparation_DoesNotMovePeople_UntilDeparture()
    {
        GameState state = NewGame();
        int homeBefore = state.DailyFoodConsumption;
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "marta", out _));
        Assert.IsTrue(ExpeditionPreparation.TrySetRetinue(state, "lada", out _));

        Assert.AreEqual(homeBefore, state.DailyFoodConsumption);
        Assert.IsTrue(HomePeopleService.CanWorkAtHome(state, HomePeopleService.Find(state, "lada")));
        Assert.AreEqual(HomeFunctionStatus.Working,
            HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Status);
    }

    [Test]
    public void PreparedExpedition_NotMoved_EditsApplyToExpedition_ThenLockAfterDeparture()
    {
        GameState state = NewGame();
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out _));
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, Fighters(state), out string message), message);

        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "edric", out message), message);
        CollectionAssert.AreEqual(new[] { "garrick", "edric" }, state.ActiveExpedition.FighterIds);

        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsFalse(ExpeditionPreparation.CanEdit(state));
        Assert.IsFalse(ExpeditionPreparation.TryAddFighter(state, "agnessa", out message));
        Assert.AreEqual("Состав закреплён: поход начался.", message);
        Assert.IsFalse(ExpeditionPreparation.TrySetRetinue(state, "lada", out message));
        CollectionAssert.AreEqual(new[] { "garrick", "edric" }, state.ActiveExpedition.FighterIds);
    }

    [Test]
    public void ExpeditionStart_ValidatesFightersAndRetinueTogether()
    {
        GameState state = NewGame();
        LocationData target = state.Locations.First(location => !location.IsWaypoint);

        Assert.IsFalse(state.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message, "miron"));
        StringAssert.Contains("свиту", message);
        Assert.IsFalse(state.HasActiveExpedition, "Ошибка свиты не отправляет бойцов без неё.");

        Assert.IsFalse(state.TryStartExpedition(target.Id, new List<string> { "lada" }, out message, "lada"));
        Assert.IsFalse(state.HasActiveExpedition);

        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "torvin", "garrick" }, out message, "lada"), message);
        CollectionAssert.AreEqual(new[] { "torvin", "garrick" }, state.ActiveExpedition.FighterIds, "Порядок мест сохранён.");
        CollectionAssert.AreEqual(new[] { "lada" }, state.ActiveExpedition.RetinueIds);
    }

    [Test]
    public void AfterExpedition_LastRosterStays_DeadDropOut()
    {
        GameState state = NewGame();
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "garrick", out _));
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, "edric", out _));
        Depart(state);

        HomePeopleService.MarkDead(state, "edric", "test");
        state.ActiveExpedition.IsActive = false;
        state.ActiveExpedition = null;

        CollectionAssert.AreEqual(new[] { "garrick" }, Fighters(state), "Погибшие не воскресают в подготовке.");
    }

    [Test]
    public void OldV2Save_WithExpeditionAndNoPreparation_UsesRealRoster()
    {
        GameState state = NewGame();
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "agnessa" }, out string message), message);
        state.Preparation = null;

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.IsNotNull(restored.Preparation);
        CollectionAssert.AreEqual(new[] { "agnessa" }, Fighters(restored));
    }

    // --- Запасы и защита (A17) ---

    [TestCase(10, 20, 24, HomeFoodOutlook.DaysCovered, 2)]
    [TestCase(0, 24, 24, HomeFoodOutlook.IncomeCovers, 0)]
    [TestCase(0, 10, 24, HomeFoodOutlook.ShortageAtNextMidnight, 0)]
    [TestCase(50, 0, 0, HomeFoodOutlook.NoConsumption, 0)]
    [TestCase(48, 0, 24, HomeFoodOutlook.DaysCovered, 2)]
    public void FoodForecast_FollowsMidnightOrder(int food, int income, int consumption, HomeFoodOutlook outlook, int days)
    {
        HomeFoodForecast forecast = HomeOverview.ForecastFood(food, income, consumption, false);
        Assert.AreEqual(outlook, forecast.Outlook);
        Assert.AreEqual(days, forecast.CoveredMidnights);
    }

    [Test]
    public void FoodForecast_Texts_NoInfinityOrDivisionByZero()
    {
        StringAssert.Contains("не хватит", HomeOverview.DescribeFood(HomeOverview.ForecastFood(0, 10, 24, false)));
        StringAssert.Contains("недостанет 14", HomeOverview.DescribeFood(HomeOverview.ForecastFood(0, 10, 24, false)));
        Assert.AreEqual("Сейчас расхода нет.", HomeOverview.DescribeFood(HomeOverview.ForecastFood(0, 0, 0, false)));
        Assert.AreEqual("Поступления покрывают расход.", HomeOverview.DescribeFood(HomeOverview.ForecastFood(0, 24, 24, false)));
        StringAssert.Contains("2 полных дня", HomeOverview.DescribeFood(HomeOverview.ForecastFood(48, 0, 24, false)));
        StringAssert.StartsWith("Есть нехватка еды.", HomeOverview.DescribeFood(HomeOverview.ForecastFood(0, 24, 24, true)));
        StringAssert.DoesNotContain("∞", HomeOverview.DescribeFood(HomeOverview.ForecastFood(100000, 23, 24, false)));
    }

    [Test]
    public void Defense_CountsCombatPeoplePhysicallyHome()
    {
        GameState state = NewGame();
        StringAssert.StartsWith("Есть кому держать защиту: 6", HomeOverview.DescribeDefense(state));

        foreach (string id in new[] { "garrick", "edric", "marta", "torvin" })
            Assert.IsTrue(ExpeditionPreparation.TryAddFighter(state, id, out _));
        Assert.AreEqual(6, HomeOverview.GetHomeDefenders(state).Count, "Подготовка не уводит защитников.");

        Depart(state);
        List<ResidentState> defenders = HomeOverview.GetHomeDefenders(state);
        Assert.AreEqual(1, defenders.Count);
        Assert.AreEqual("agnessa", defenders[0].PersonId);
        Assert.AreEqual("Мало защитников: Агнесса.", HomeOverview.DescribeDefense(state));
    }

    [Test]
    public void Defense_NoFightersHome_SaysSo()
    {
        GameState state = NewGame();
        foreach (ResidentState resident in HomePeopleService.All(state))
        {
            if (resident.TravelRole == ResidentTravelRole.Combatant || resident.TravelRole == ResidentTravelRole.Commander)
                resident.Injury = ResidentInjury.Recovering;
        }
        StringAssert.StartsWith("Некому держать защиту", HomeOverview.DescribeDefense(state));
    }

    // --- Настроение (A15) ---

    [Test]
    public void Hunger_DoesNotTouchMood_AndCareStopMessageMatchesRule()
    {
        GameState state = NewGame();
        state.Mood = 0;
        state.Food = 0;
        state.BaseDailyFoodIncome = 1;

        List<string> messages = new List<string>();
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        for (int i = 0; i < 40 && state.ConsecutiveFoodShortageDays < HomeFunctionResolver.CareStopsAfterShortageDays; i++)
        {
            ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(state, 1f, false);
            messages.AddRange(batch.Result.Messages);
        }

        Assert.AreEqual(HomeFunctionResolver.CareStopsAfterShortageDays, state.ConsecutiveFoodShortageDays);
        Assert.AreEqual(0, state.Mood, "Голод не меняет настроение, настроение ничего не решает.");
        Assert.IsFalse(messages.Any(m => m.Contains("Настроение")));
        Assert.IsTrue(messages.Any(m => m.Contains("уход за ранеными остановлен")),
            "Сообщение об остановке ухода — с той же полуночи, что и правило.");
        Assert.IsFalse(messages.Any(m => m.Contains("Город")));
    }

    // --- Домашние команды (A19) ---

    [Test]
    public void YardDeck_FromDepartedExpedition_IsRejectedByCommand()
    {
        GameState state = NewGame();
        state.Gold = 100;
        Depart(state);

        Assert.IsFalse(HomeLife.TryStartYardDeck(state, out string message));
        Assert.AreEqual("Нужно вернуться в Дом.", message);
        Assert.AreEqual(100, state.Gold);
    }
}
