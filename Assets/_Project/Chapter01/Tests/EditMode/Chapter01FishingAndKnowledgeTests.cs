using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using UnityEngine;
using static Chapter01PlaythroughWalker;

// ПР-07Б (PR07_HOME_SPEC §10–§12, §14.2): рыбная ловля Тихона за фактически
// отработанные часы и сведения о Доме из похода.
public sealed class Chapter01FishingAndKnowledgeTests
{
    private const double Tolerance = 0.000001;

    private static GameState GameWithFamily()
    {
        GameState gameState = NewGame(20260925);
        NarrativeStateData state = gameState.Narrative;
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.FisherFamilyAccepted);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);
        Assert.IsTrue(HomeFunctionResolver.IsFishingOpen(gameState));
        gameState.ArmySupply = 200;
        return gameState;
    }

    private static double Earned(GameState gameState)
    {
        return gameState.People.FishingEarned;
    }

    private static void Depart(GameState gameState, IEnumerable<string> fighters, string retinue = null)
    {
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, fighters.ToList(), out string message, retinue), message);
        gameState.ActiveExpedition.RouteIndex = 1;
    }

    private static void ReturnHome(GameState gameState)
    {
        gameState.ActiveExpedition.IsActive = false;
        gameState.ActiveExpedition = null;
    }

    // Игровые часы через настоящий владелец времени, кусками по часу.
    private static List<string> AdvanceHours(GameState gameState, double hours)
    {
        List<string> messages = new List<string>();
        ContinuousSimulationSystem.SetSpeedMultiplier(gameState, ContinuousSimulationSystem.NormalSpeedMultiplier);
        double done = 0.0;
        while (done < hours - Tolerance)
        {
            double chunk = System.Math.Min(1.0, hours - done);
            ContinuousSimulationSystem.SetPaused(gameState, false);
            double before = ContinuousSimulationSystem.GetClock(gameState).HourOfDay + gameState.Day * 24.0;
            ContinuousSimulationBatch batch = ContinuousSimulationSystem.Advance(
                gameState, (float)(chunk / ContinuousSimulationSystem.GameHoursPerRealSecond), false);
            messages.AddRange(batch.Result.Messages);
            double after = ContinuousSimulationSystem.GetClock(gameState).HourOfDay + gameState.Day * 24.0;
            Assert.Greater(after, before, "Время не пошло.");
            done += after - before;
        }
        return messages;
    }

    // --- Ловля (B01–B12) ---

    [Test]
    public void FishingOpens_OnlyWithAcceptedFamily()
    {
        GameState withoutFamily = NewGame(20260925);
        Assert.IsFalse(HomeFunctionResolver.IsFishingOpen(withoutFamily));
        Assert.IsNull(Chapter01HomeView.FishingLine(withoutFamily));

        GameState gameState = GameWithFamily();
        StringAssert.StartsWith("Рыбная ловля — Тихон дома", Chapter01HomeView.FishingLine(gameState));
        Assert.IsTrue(HomeKnowledge.TakePendingNews(gameState).Any(n => n.Contains("Открыта рыбная ловля")),
            "Принятие даёт одно короткое сообщение о новой функции.");
    }

    [Test]
    public void FullDay_OneStepOrManySmallSteps_Gives8()
    {
        GameState one = GameWithFamily();
        HomeLife.Advance(one, 24.0, null);
        Assert.AreEqual(8, HomeLife.TakeFishingCatch(one));
        Assert.AreEqual(0.0, Earned(one), Tolerance);

        GameState many = GameWithFamily();
        for (int i = 0; i < 24 * 37; i++)
            HomeLife.Advance(many, 1.0 / 37.0, null);
        Assert.AreEqual(8, HomeLife.TakeFishingCatch(many), "Мелкие шаги не превращают 8 в 7.");
    }

    [Test]
    public void TwelveHoursThenDeparture_Gives4_AndPaysOnce()
    {
        GameState gameState = GameWithFamily();
        HomeLife.Advance(gameState, 12.0, null);
        Depart(gameState, new[] { HomePeopleService.TikhonId });
        HomeLife.Advance(gameState, 12.0, null);

        Assert.AreEqual(4.0, Earned(gameState), Tolerance, "В походе улов не растёт.");
        Assert.AreEqual(4, HomeLife.TakeFishingCatch(gameState));
        Assert.AreEqual(0, HomeLife.TakeFishingCatch(gameState), "Заработанное выплачивается один раз.");
    }

    [Test]
    public void AcceptedAnHourBeforeMidnight_EarnsAThird_KeepsRemainder()
    {
        GameState gameState = NewGame(20260925);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FloodHappened);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        double hour = ContinuousSimulationSystem.GetClock(gameState).HourOfDay;
        AdvanceHours(gameState, 23.0 - hour);

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FisherFamilyAccepted);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);
        int day = gameState.Day;
        int food = gameState.Food;
        int income = BuildingSystem.GetDailyFoodIncome(gameState);
        int consumption = gameState.DailyFoodConsumption;

        AdvanceHours(gameState, 1.5);
        Assert.AreEqual(day + 1, gameState.Day);
        Assert.AreEqual(food + income - consumption, gameState.Food, "Никакой награды +8 за клик.");
        Assert.AreEqual(0.5, Earned(gameState), 0.01, "Треть за час до полуночи и ещё полчаса после.");
    }

    [Test]
    public void LongStep_CrossingRecovery_CountsOnlyWorkedHours()
    {
        GameState gameState = GameWithFamily();
        ResidentState tikhon = HomePeopleService.Find(gameState, HomePeopleService.TikhonId);
        tikhon.Injury = ResidentInjury.Recovering;
        tikhon.RecoveryProgress = 0.5; // Марта долечит за 12 ч.

        int food = gameState.Food;
        int income = BuildingSystem.GetDailyFoodIncome(gameState);
        int consumption = gameState.DailyFoodConsumption;
        int day = gameState.Day;

        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(gameState, ContinuousSimulationSystem.NormalSpeedMultiplier);
        ContinuousSimulationSystem.Advance(gameState, (float)(20.0 / ContinuousSimulationSystem.GameHoursPerRealSecond), false);

        Assert.AreEqual(ResidentInjury.None, tikhon.Injury);
        int midnights = gameState.Day - day;
        int caught = gameState.Food - food - midnights * income + midnights * consumption;
        double total = caught + Earned(gameState);
        Assert.AreEqual(8.0 / 3.0, total, ContinuousSimulationSystem.MaxHomeSubstepHours * HomeLife.FishingFoodPerHour + 0.01,
            "Улов только за 8 часов после выздоровления, с точностью до одного подшага.");
    }

    [Test]
    public void SaveLoad_KeepsFractionalCatch_NoDoublePayout()
    {
        GameState gameState = GameWithFamily();
        HomeLife.Advance(gameState, 7.5, null);
        double earned = Earned(gameState);

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(gameState));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(earned, Earned(restored), Tolerance);
        Assert.AreEqual(2, HomeLife.TakeFishingCatch(restored));
        Assert.AreEqual(0.5, Earned(restored), Tolerance);
    }

    [Test]
    public void PreparedButNotMoved_TikhonKeepsFishing()
    {
        GameState gameState = GameWithFamily();
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(gameState, HomePeopleService.TikhonId, out string message), message);
        HomeLife.Advance(gameState, 6.0, null);
        Assert.AreEqual(2.0, Earned(gameState), Tolerance);

        List<string> forecast = HomeOverview.DescribeDeparture(gameState);
        Assert.IsTrue(forecast.Any(l => l == "Ловля остановится. Полный суточный приток уменьшится на 8."),
            string.Join("\n", forecast));
    }

    [Test]
    public void TikhonDies_FishingStops_EarnedStays_FamilyStays()
    {
        GameState gameState = GameWithFamily();
        HomeLife.Advance(gameState, 3.0, null);
        HomePeopleService.MarkDead(gameState, HomePeopleService.TikhonId, "test");
        HomeLife.Advance(gameState, 10.0, null);

        Assert.AreEqual(1.0, Earned(gameState), Tolerance, "Уже добытое принадлежит Дому.");
        Assert.AreEqual(HomeFunctionStatus.Stopped, HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.FishingId).Status);
        Assert.IsTrue(HomePeopleService.Find(gameState, Chapter01FisherFamily.VarvaraId).IsHomeMember);
        Assert.IsTrue(Chapter01HomeView.DescribeCares(gameState).Any(c => c.Id == "home.care.fishing"));
    }

    [Test]
    public void WrongWater_DoesNotStopFishing()
    {
        GameState gameState = GameWithFamily();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        Assert.AreEqual(HomeFunctionStatus.Working, HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.FishingId).Status);
    }

    [Test]
    public void FullPartyAway_Home22_Supply6_NoNewCatch()
    {
        GameState gameState = GameWithFamily();
        Depart(gameState, new[] { HomePeopleService.TikhonId, "garrick", "edric", "torvin" }, HomePeopleService.LadaId);

        Assert.AreEqual(22, gameState.DailyFoodConsumption);
        Assert.AreEqual(6, gameState.ExpeditionSupplyConsumption);
        HomeLife.Advance(gameState, 10.0, null);
        Assert.AreEqual(0.0, Earned(gameState), Tolerance);
    }

    [Test]
    public void Fishing_DoesNotKeepHomeTimeRunning()
    {
        GameState gameState = GameWithFamily();
        Assert.IsFalse(HomeLife.HasPendingProgress(gameState));
    }

    [Test]
    public void FoodForecast_CountsFishing()
    {
        GameState gameState = GameWithFamily();
        gameState.Food = 0;
        gameState.BaseDailyFoodIncome = 24;
        Assert.AreEqual(HomeFoodOutlook.IncomeCovers, HomeOverview.ForecastFood(gameState).Outlook,
            "24 + 8 при расходе 28 покрывают расход.");

        HomePeopleService.Find(gameState, HomePeopleService.TikhonId).Injury = ResidentInjury.Recovering;
        Assert.AreEqual(HomeFoodOutlook.ShortageAtNextMidnight, HomeOverview.ForecastFood(gameState).Outlook);
    }

    // --- Сведения из похода (B13–B16) ---

    [Test]
    public void Snapshot_IsTakenAtDeparture_AndIsNotLive()
    {
        GameState gameState = GameWithFamily();
        HomeKnowledge.Refresh(gameState, s => new List<string> { "не должно попасть" });

        Depart(gameState, new[] { "garrick" });
        int foodAtDeparture = gameState.Food;
        Assert.IsNull(HomeKnowledge.Refresh(gameState, s => new List<string> { "Вода: мутная после паводка." }));
        Assert.IsTrue(HomeKnowledge.Get(gameState).HasSnapshot);

        gameState.Food += 50;
        HomeKnowledge.Refresh(gameState, s => new List<string> { "не должно попасть" });
        string lastKnown = HomeKnowledge.DescribeLastKnown(gameState);
        StringAssert.Contains("Запасы Дома: " + foodAtDeparture, lastKnown);
        StringAssert.Contains("Вода: мутная после паводка.", lastKnown);
        StringAssert.DoesNotContain("не должно попасть", lastKnown);

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(gameState));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));
        StringAssert.Contains("Запасы Дома: " + foodAtDeparture, HomeKnowledge.DescribeLastKnown(restored));
    }

    [Test]
    public void HomeShortageWhileAway_IsDeferred_ThenReportedOnceOnReturn()
    {
        GameState gameState = GameWithFamily();
        HomeKnowledge.Refresh(gameState, null);
        Depart(gameState, new[] { "garrick" });
        HomeKnowledge.Refresh(gameState, null);
        gameState.Food = 0;
        gameState.BaseDailyFoodIncome = 1;

        List<string> messages = AdvanceHours(gameState, 24.0);
        Assert.IsFalse(messages.Any(m => m.Contains("Дому не хватило")), "В пути домашняя нехватка не видна.");
        Assert.IsFalse(messages.Any(m => m.Contains("В запасы Дома поступило")));
        Assert.IsTrue(HomeKnowledge.Get(gameState).AwayNews.Any(m => m.Contains("Дому не хватило")));

        ReturnHome(gameState);
        string summary = HomeKnowledge.Refresh(gameState, null);
        StringAssert.StartsWith("Пока вас не было…", summary);
        StringAssert.Contains("Дому не хватило", summary);
        StringAssert.Contains("Запасы Дома: было", summary);
        Assert.IsNull(HomeKnowledge.Refresh(gameState, null), "Запись об изменениях — один раз.");
        Assert.IsFalse(HomeKnowledge.Get(gameState).HasSnapshot);
    }

    [Test]
    public void OldSaveAlreadyAway_WithoutSnapshot_DoesNotInventData()
    {
        GameState gameState = GameWithFamily();
        Depart(gameState, new[] { "garrick" });
        gameState.HomeKnowledge = new HomeKnowledgeData();

        HomeKnowledge.Refresh(gameState, s => new List<string> { "выдуманное" });
        Assert.IsFalse(HomeKnowledge.Get(gameState).HasSnapshot);
        StringAssert.StartsWith("Сведения на момент выхода не сохранены", HomeKnowledge.DescribeLastKnown(gameState));
    }

    [Test]
    public void AfterReturn_TikhonFishesAgain()
    {
        GameState gameState = GameWithFamily();
        Depart(gameState, new[] { HomePeopleService.TikhonId });
        HomeLife.Advance(gameState, 5.0, null);
        ReturnHome(gameState);
        HomeLife.Advance(gameState, 3.0, null);
        Assert.AreEqual(1.0, Earned(gameState), Tolerance);
    }
}
