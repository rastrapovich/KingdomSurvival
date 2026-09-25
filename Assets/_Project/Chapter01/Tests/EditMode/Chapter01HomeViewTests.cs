using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using static Chapter01PlaythroughWalker;

// ПР-07А-2 (PR07_HOME_SPEC §5, §7, §8.5): модель экрана Дома — объекты по
// флагам главы, заботы по функциям и работам, прогноз «После выхода».
public sealed class Chapter01HomeViewTests
{
    private static HomeObjectView Find(GameState gameState, string id)
    {
        return Chapter01HomeView.DescribeObjects(gameState).FirstOrDefault(o => o.Id == id);
    }

    private static GameState AfterFlood()
    {
        GameState gameState = NewGame(20260925);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FloodHappened);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        return gameState;
    }

    [Test]
    public void BeforeFlood_EverythingNormal_NoYardDeck()
    {
        GameState gameState = NewGame(20260925);
        Assert.AreEqual("Normal", Find(gameState, Chapter01HomeView.WaterId).StateKey);
        Assert.AreEqual("Intact", Find(gameState, Chapter01HomeView.DamId).StateKey);
        Assert.IsNull(Find(gameState, Chapter01HomeView.YardDeckId));
        Assert.IsTrue(Chapter01HomeView.DescribeObjects(gameState).All(o => !string.IsNullOrEmpty(o.Short)));
    }

    [Test]
    public void AfterFlood_DamDamaged_OpensTheSameInspectionScene()
    {
        GameState gameState = AfterFlood();
        HomeObjectView dam = Find(gameState, Chapter01HomeView.DamId);
        Assert.AreEqual("Damaged", dam.StateKey);
        Assert.AreEqual(Chapter01Ids.Dialogues.D05, dam.ActionDialogueId, "Плотина ведёт к тому же делу главы, а не к новому событию.");
        Assert.AreEqual("DamagedOrStopped", Find(gameState, Chapter01HomeView.MillId).StateKey);
    }

    [Test]
    public void ChosenRepair_IsNotCompletedRepair()
    {
        GameState gameState = AfterFlood();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        HomeObjectView dam = Find(gameState, Chapter01HomeView.DamId);
        Assert.AreEqual("ChosenOld", dam.StateKey);
        StringAssert.Contains("не завершена", dam.State);
        Assert.IsNull(dam.ActionDialogueId);

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairCompleted);
        Assert.AreEqual("RepairedOld", Find(gameState, Chapter01HomeView.DamId).StateKey);
    }

    [Test]
    public void WrongWater_KeepsPriority_EvenAfterRepairAndYardDeck()
    {
        GameState gameState = AfterFlood();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairNew);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairCompleted);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        gameState.Gold = 100;
        Assert.IsTrue(Chapter01HomeActivities.TryStartYardDeck(gameState, out string message), message);
        HomeLife.FindWork(gameState, HomeLife.YardDeckWorkId).Completed = true;

        Assert.AreEqual("Wrong", Find(gameState, Chapter01HomeView.WaterId).StateKey);
        Assert.AreEqual("Restored", Find(gameState, Chapter01HomeView.YardDeckId).StateKey);
        Assert.AreEqual("RepairedNew", Find(gameState, Chapter01HomeView.DamId).StateKey,
            "Двор и плотина — разные ремонты.");
    }

    [Test]
    public void Cares_YardDeckOffer_ShowsCostExecutorAndAction()
    {
        GameState gameState = AfterFlood();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        gameState.Gold = 100;

        HomeCareView deck = Chapter01HomeView.DescribeCares(gameState).Single(c => c.Id == "home.care.yard_deck");
        Assert.AreEqual(HomeCareAction.StartYardDeck, deck.Action);
        Assert.IsTrue(deck.ActionEnabled);
        StringAssert.Contains("Лада", deck.Status);
        StringAssert.Contains(HomeLife.YardDeckGoldCost + " золота", deck.Detail);
    }

    [Test]
    public void Cares_FoodShortage_ComesFirst()
    {
        GameState gameState = AfterFlood();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairOld);
        gameState.Food = 0;
        gameState.BaseDailyFoodIncome = 1;

        IReadOnlyList<HomeCareView> cares = Chapter01HomeView.DescribeCares(gameState);
        Assert.AreEqual("home.care.food", cares[0].Id);
        Assert.IsTrue(cares[0].Urgent);
    }

    [Test]
    public void Cares_NoPatients_NoEmptyCareCard()
    {
        GameState gameState = NewGame(20260925);
        Assert.IsFalse(Chapter01HomeView.DescribeCares(gameState).Any(c => c.Id == "home.care.patients"));
    }

    [Test]
    public void Departure_LadaLeaves_RepairContinuesSlower()
    {
        GameState gameState = NewGame(20260925);
        Assert.IsTrue(ExpeditionPreparation.TrySetRetinue(gameState, HomePeopleService.LadaId, out _));

        List<string> lines = HomeOverview.DescribeDeparture(gameState);
        Assert.IsTrue(lines.Any(l => l.Contains("Ремонт продолжит Остафий — медленнее")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.StartsWith("Уйдут 2")), "Командир уходит тоже.");
        Assert.AreEqual(HomeFunctionStatus.Working,
            HomeFunctionResolver.Resolve(gameState, HomeFunctionResolver.MaintenanceId).Status,
            "Прогноз не меняет реальное состояние.");
    }

    [Test]
    public void Departure_MartaAndTorvinWithLada_ForecastsHonestly()
    {
        GameState gameState = NewGame(20260925);
        HomePeopleService.Find(gameState, HomePeopleService.OstafiyId).Injury = ResidentInjury.Recovering;
        Assert.IsTrue(ExpeditionPreparation.TrySetRetinue(gameState, HomePeopleService.LadaId, out _));
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(gameState, HomePeopleService.TorvinId, out _));
        Assert.IsTrue(ExpeditionPreparation.TryAddFighter(gameState, HomePeopleService.MartaId, out _));

        List<string> lines = HomeOverview.DescribeDeparture(gameState);
        Assert.IsTrue(lines.Any(l => l.StartsWith("Ремонт остановится")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("Уход за ранеными продолжит Ульяна — медленнее")), string.Join("\n", lines));
    }
}
