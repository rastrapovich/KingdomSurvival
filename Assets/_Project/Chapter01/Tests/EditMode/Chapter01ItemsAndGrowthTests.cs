using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using static Chapter01PlaythroughWalker;

// ПР-08 (ТЗ §8): вещи первой главы и один выбор развития героя.
public sealed class Chapter01ItemsAndGrowthTests
{
    private static GameState AfterRepairDecision()
    {
        GameState gameState = NewGame(20260925);
        NarrativeStateData state = gameState.Narrative;
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        return gameState;
    }

    [Test]
    public void AcceptedTikhon_GetsAxeAndCoat()
    {
        GameState gameState = AfterRepairDecision();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FisherFamilyAccepted);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);

        Assert.AreEqual(ItemCatalog.Axe, ItemService.Equipped(gameState, HomePeopleService.TikhonId, ItemSlot.Weapon)?.ItemId);
        Assert.AreEqual(ItemCatalog.FisherCoat, ItemService.Equipped(gameState, HomePeopleService.TikhonId, ItemSlot.Protection)?.ItemId);
    }

    [Test]
    public void YardDeckDone_GivesRope_RopeShortensFordBypass()
    {
        GameState gameState = AfterRepairDecision();
        gameState.Gold = 100;
        Assert.IsTrue(Chapter01HomeActivities.TryStartYardDeck(gameState, out string message), message);
        HomeLife.Advance(gameState, 24.0, new List<string>());
        ItemInstanceData rope = ItemService.FindFirst(gameState, ItemCatalog.RopeWithHooks);
        Assert.IsNotNull(rope, "Лада сплела верёвку из остатков настила.");
        Assert.IsTrue(ItemService.TryTakeToPack(gameState, rope.InstanceId, out message), message);

        gameState.ArmySupply = 100;
        LocationData target = gameState.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(gameState.TryStartExpedition(target.Id, new List<string>(), out message), message);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessResolved);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FordAccessBypass);
        Chapter01OutcomeApplier.ApplyFordAccessConsequences(gameState);

        Assert.AreEqual(1.0, gameState.ActiveExpedition.ActiveActivity.TotalHours, 1e-9, "С верёвкой обход занимает час.");
    }

    [Test]
    public void ChapterExpeditionStart_UlyanaGivesHerbsOnce()
    {
        GameState gameState = AfterRepairDecision();
        Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
        Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);

        List<ItemInstanceData> herbs = gameState.Inventory.Items.Where(i => i.ItemId == ItemCatalog.UlyanaHerbs).ToList();
        Assert.AreEqual(1, herbs.Count);
        Assert.AreEqual(gameState.GetSelectedCommander().Id, herbs[0].OwnerPersonId);
        Assert.AreEqual(2, herbs[0].UsesLeft);
    }

    [Test]
    public void FishingCatch_Of24_GivesDriedFish()
    {
        GameState gameState = AfterRepairDecision();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FisherFamilyAccepted);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.GateFamily);

        gameState.People.FishingEarned = 24.0;
        ContinuousSimulationSystem.SetPaused(gameState, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(gameState, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        int day = gameState.Day;
        for (int i = 0; i < 40 && gameState.Day == day; i++)
            ContinuousSimulationSystem.Advance(gameState, 1f, false);

        ItemInstanceData fish = ItemService.FindFirst(gameState, ItemCatalog.DriedFish);
        Assert.IsNotNull(fish);
        Assert.AreEqual(string.Empty, fish.OwnerPersonId, "Связка лежит в кладовой.");
    }

    [Test]
    public void RoadGrowth_OpensAfterReturn_AppliesOnce()
    {
        GameState gameState = NewGame(20260925);
        NarrativeStateData state = gameState.Narrative;
        Assert.IsFalse(Chapter01StoryDirector.CanOpenRoadGrowth(state));

        state.SetFlag(Chapter01Ids.Flags.ReturnedHome);
        Assert.IsTrue(Chapter01StoryDirector.CanOpenRoadGrowth(state));

        HeroProfileData profile = gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData();
        gameState.GetSelectedCommander().HeroProfile = profile;
        int fortitude = profile.GetQuality(HeroQuality.Fortitude);

        state.SetFlag(Chapter01Ids.Flags.RoadGrowthChosen);
        state.SetFlag(Chapter01Ids.Flags.RoadGrowthFortitude);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D16B);
        Chapter01StoryDirector.HandleDialogueCompleted(gameState, Chapter01Ids.Dialogues.D16B);

        Assert.AreEqual(fortitude + 1, profile.GetQuality(HeroQuality.Fortitude), "+1 ровно один раз.");
        Assert.IsFalse(Chapter01StoryDirector.CanOpenRoadGrowth(state));
        StringAssert.Contains("Стойкость +1", Chapter01OutcomeApplier.DescribeRoadGrowth(state));
    }

    [Test]
    public void WholeChapter_ChoosesRoadGrowthBeforeCouncil()
    {
        GameState gameState = NewGame(20260925);
        Walk walk = PlayWholeChapter(gameState, new Options());

        int growth = walk.Scenes.IndexOf(Chapter01Ids.Dialogues.D16B);
        Assert.Greater(growth, walk.Scenes.IndexOf(Chapter01Ids.Dialogues.D16), walk.Path);
        Assert.Less(growth, walk.Scenes.IndexOf(Chapter01Ids.Dialogues.D17), walk.Path);
        Assert.IsTrue(gameState.Narrative.HasFlag(Chapter01Ids.Flags.RoadGrowthFortitude), "Первый ответ — Стойкость.");
    }
}
