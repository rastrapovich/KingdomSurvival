using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;
using static Chapter01PlaythroughWalker;

// ПР-06Б: причинное пополнение (семья Тихона) вместо платного найма и
// выход на берег у старого брода, где состав отряда меняет способ и цену.
public sealed class Chapter01FisherFamilyTests
{
    private static GameState GameAfterRepairDecision()
    {
        GameState gameState = NewGame(20260925);
        NarrativeStateData state = gameState.Narrative;
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        return gameState;
    }

    private static List<string> CardIds(GameState gameState)
    {
        return Chapter01HomeActivities.GetAvailable(gameState).Select(card => card.DialogueId).ToList();
    }

    private static void Accept(GameState gameState)
    {
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FisherFamilyAccepted);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);
    }

    private static int CountFamily(GameState gameState)
    {
        return HomePeopleService.All(gameState).Count(r => r.HouseholdId == Chapter01FisherFamily.HouseholdId);
    }

    // --- Предложение ---

    [Test]
    public void Offer_AppearsAfterRepairDecision_NotBefore()
    {
        GameState gameState = NewGame(20260925);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FloodHappened);
        Assert.IsFalse(Chapter01FisherFamily.IsOffered(gameState), "До решения N05 семья не приходит.");

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.RepairNew);
        Assert.IsTrue(Chapter01FisherFamily.IsOffered(gameState));
        CollectionAssert.Contains(CardIds(gameState), Chapter01Ids.Dialogues.GateFamily);
    }

    [Test]
    public void Offer_WaitsAfterChapterExpedition_AndNeedsHeroAtHome()
    {
        GameState gameState = GameAfterRepairDecision();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.Completed);
        Assert.IsTrue(Chapter01FisherFamily.IsOffered(gameState), "Предложение ждёт и после похода главы.");

        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        Assert.IsFalse(Chapter01FisherFamily.IsOffered(gameState), "Пока герой в пути, ответить некому.");
    }

    [Test]
    public void OfferSummary_NamesFamilyAndFoodCost()
    {
        GameState gameState = GameAfterRepairDecision();
        string summary = Chapter01FisherFamily.BuildOfferSummary(gameState);
        StringAssert.Contains("4 человека", summary);
        StringAssert.Contains("Тихон", summary);
        StringAssert.Contains("Варвара", summary);
        StringAssert.Contains("24 → 28", summary);
    }

    // --- Принятие ---

    [Test]
    public void Accept_AddsWholeFamily_Once()
    {
        GameState gameState = GameAfterRepairDecision();
        int fightersBefore = gameState.Fighters.Count;
        int consumptionBefore = gameState.DailyFoodConsumption;

        Accept(gameState);
        Accept(gameState);
        Assert.IsFalse(Chapter01FisherFamily.TryAccept(gameState, out string message), message);

        Assert.AreEqual(4, CountFamily(gameState), "Ровно четыре записи семьи.");
        Assert.AreEqual(28, gameState.Population);
        Assert.AreEqual(consumptionBefore + 4, gameState.DailyFoodConsumption);
        Assert.AreEqual(fightersBefore + 1, gameState.Fighters.Count, "Тихон — доступный боец.");
        Assert.AreEqual("militia", gameState.Fighters.Last().UnitTypeId);

        HouseholdState household = gameState.People.Households.Single(h => h.HouseholdId == Chapter01FisherFamily.HouseholdId);
        CollectionAssert.AreEquivalent(
            new[] { Chapter01FisherFamily.TikhonId, Chapter01FisherFamily.VarvaraId, Chapter01FisherFamily.AnyaId, Chapter01FisherFamily.FedyaId },
            household.MemberIds);
        Assert.AreEqual(ResidentAgeGroup.Child, HomePeopleService.Find(gameState, Chapter01FisherFamily.AnyaId).AgeGroup);
        Assert.AreEqual(ResidentTravelRole.None, HomePeopleService.Find(gameState, Chapter01FisherFamily.VarvaraId).TravelRole);

        Assert.IsFalse(Chapter01FisherFamily.IsOffered(gameState));
        CollectionAssert.DoesNotContain(CardIds(gameState), Chapter01Ids.Dialogues.GateFamily);
    }

    [Test]
    public void Decline_AddsNobody_AndClosesOffer()
    {
        GameState gameState = GameAfterRepairDecision();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FisherFamilyDeclined);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);

        Assert.AreEqual(0, CountFamily(gameState));
        Assert.AreEqual(24, gameState.Population);
        Assert.IsFalse(Chapter01FisherFamily.IsOffered(gameState));
    }

    [Test]
    public void TikhonInExpedition_FamilyEatsAtHome_TikhonOnTheRoad()
    {
        GameState gameState = GameAfterRepairDecision();
        Accept(gameState);
        int homeBefore = gameState.DailyFoodConsumption;

        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, new List<string> { Chapter01FisherFamily.TikhonId }, out string message), message);
        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.Advance(gameState, 5f, false);

        Assert.IsTrue(HomePeopleService.IsInExpedition(gameState, Chapter01FisherFamily.TikhonId));
        Assert.IsTrue(HomePeopleService.IsHomePresent(gameState, HomePeopleService.Find(gameState, Chapter01FisherFamily.VarvaraId)));
        Assert.AreEqual(homeBefore - 2, gameState.DailyFoodConsumption, "Дома не едят герой и Тихон.");
    }

    [Test]
    public void TikhonDies_FamilyStaysInHome()
    {
        GameState gameState = GameAfterRepairDecision();
        Accept(gameState);

        HomePeopleService.MarkDead(gameState, Chapter01FisherFamily.TikhonId, "test");

        Assert.AreEqual(4, CountFamily(gameState), "Запись Тихона остаётся — погибшим.");
        Assert.IsFalse(HomePeopleService.Find(gameState, Chapter01FisherFamily.TikhonId).IsAlive);
        Assert.IsTrue(HomePeopleService.Find(gameState, Chapter01FisherFamily.VarvaraId).IsHomeMember);
        Assert.AreEqual(27, gameState.Population);
    }

    [Test]
    public void SaveLoad_AfterAccept_KeepsFamily_AndDoesNotRepeat()
    {
        GameState gameState = GameAfterRepairDecision();
        Accept(gameState);

        CampaignSaveData data = CampaignSaveService.ExportCampaign(gameState);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data));
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);

        Assert.AreEqual(4, CountFamily(restored));
        Assert.AreEqual(28, restored.Population);
        Assert.IsTrue(restored.Fighters.Any(f => f.Id == Chapter01FisherFamily.TikhonId));

        Chapter01StoryDirector.HandleDialogueCompleted(restored, Chapter01Ids.Dialogues.GateFamily);
        Assert.AreEqual(4, CountFamily(restored), "Повторное завершение после загрузки ничего не добавляет.");
        Assert.AreEqual(restored.Fighters.Count, restored.Fighters.Select(f => f.Id).Distinct().Count());
    }

    // --- Сцена «Люди у ворот» против настоящей базы ---

    [Test]
    public void GateScene_AcceptPath_AddsFamily()
    {
        GameState gameState = NewGame(20260925);
        Walk walk = PlayUntil(gameState, new Options(),
            g => g.Narrative.HasFlag(Chapter01Ids.Flags.FisherFamilyAccepted) && CountFamily(g) == 4);

        CollectionAssert.Contains(walk.Scenes, Chapter01Ids.Dialogues.GateFamily);
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.gate_family_tikhon");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.gate_family.varvara_line");
        Assert.AreEqual(28, gameState.Population);
    }

    [Test]
    public void GateScene_DeclinePath_AddsNobody()
    {
        GameState gameState = NewGame(20260925);
        PlayUntil(gameState, new Options { DeclineFamily = true },
            g => g.Narrative.HasFlag(Chapter01Ids.Flags.FisherFamilyDeclined));

        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FisherFamilyAccepted));
        Assert.AreEqual(0, CountFamily(gameState));
        Assert.AreEqual(24, gameState.Population);
    }

    // --- Выход на берег у брода ---

    private static List<string> FordChoices(GameState gameState, IEnumerable<string> companions)
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(
            database, Chapter01Ids.Dialogues.D12B, gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            gameState.Narrative, out NarrativeDialogueView view, out string error,
            companions.ToList(), new List<string>(), gameState.WorldSeed, 1, gameState), error);

        // Реплики идут по очереди: листаем до вариантов действия.
        for (int i = 0; i < 10 && view.AvailableChoices.Count == 1 && view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
            view = session.SelectChoice(view.AvailableChoices[0].ChoiceId).View;
        return view.AvailableChoices.Select(choice => choice.ChoiceId).ToList();
    }

    [Test]
    public void FordScene_ChoicesDependOnWhoIsPresent()
    {
        GameState gameState = NewGame(20260925);

        List<string> alone = FordChoices(gameState, new string[0]);
        CollectionAssert.AreEqual(new[] { "chapter01.node.12b_bypass" }, alone, "Обход доступен всегда и один.");

        List<string> withLada = FordChoices(gameState, new[] { HomePeopleService.LadaId });
        CollectionAssert.Contains(withLada, "chapter01.node.12b_lada");
        CollectionAssert.DoesNotContain(withLada, "chapter01.node.12b_ostafiy");

        List<string> withTikhon = FordChoices(gameState, new[] { Chapter01FisherFamily.TikhonId });
        CollectionAssert.Contains(withTikhon, "chapter01.node.12b_tikhon");
    }

    private static GameState GameAtFord()
    {
        GameState gameState = NewGame(20260925);
        PlayUntil(gameState, new Options(), g => Chapter01StoryDirector.GetPendingFordAccessDialogueId(g) != null);
        return gameState;
    }

    [Test]
    public void FordScene_OpensOnTheWayToDownstream_Bypass_CostsTwoHoursOnce()
    {
        GameState gameState = GameAtFord();
        Assert.AreEqual(Chapter01Ids.Dialogues.D12B, Chapter01StoryDirector.GetPendingFordAccessDialogueId(gameState));

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessResolved);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessBypass);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12B);

        Assert.IsTrue(gameState.ActiveExpedition.HasTimedActivity);
        Assert.AreEqual(2.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 1e-9);
        Assert.IsNull(Chapter01StoryDirector.GetPendingFordAccessDialogueId(gameState), "Сцена не повторяется.");

        gameState.ActiveExpedition.ActiveActivity = null;
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12B);
        Assert.IsFalse(gameState.ActiveExpedition.HasTimedActivity, "Время снимается один раз.");
    }

    [Test]
    public void FordScene_LadaPresent_CostsOneHour()
    {
        GameState gameState = GameAtFord();
        gameState.ActiveExpedition.RetinueIds.Add(HomePeopleService.LadaId);

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessResolved);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessBracedSupport);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12B);

        Assert.AreEqual(1.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 1e-9);
    }

    [Test]
    public void FordScene_HelperGoneBeforeApply_FallsBackToBypass()
    {
        GameState gameState = GameAtFord();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessResolved);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessOldDescent);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D12B);

        Assert.AreEqual(2.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 1e-9, "Остафия в отряде нет — час за него не списывается.");
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FordAccessBypass));
    }

    // --- ПР-07А-1: команда домашней работы проверяет условие главы ---

    [Test]
    public void YardDeckCommand_RejectedBeforeFlood_AcceptedAfterRepairDecision()
    {
        GameState before = NewGame(20260925);
        before.Gold = 100;
        Assert.IsFalse(Chapter01HomeActivities.TryStartYardDeck(before, out string message));
        Assert.AreEqual(100, before.Gold, message);

        GameState after = GameAfterRepairDecision();
        after.Gold = 100;
        Assert.IsTrue(Chapter01HomeActivities.TryStartYardDeck(after, out message), message);
        Assert.AreEqual(100 - HomeLife.YardDeckGoldCost, after.Gold);
    }
}
