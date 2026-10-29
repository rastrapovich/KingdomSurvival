using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// 12Т, пилот «Тайник у старого русла»: знание (рассказ возчика или береста)
// создаёт скрытый тайник в достижимой точке у торгового села и область
// поиска — не точку; находка выдаёт нож один раз; отданный Ждану нож уходит
// из сумки один раз, поручение закрывается; всё переживает сохранение.
public sealed class FreePlayCacheStoryTests
{
    private const string PostProfile = "settlement.trading.post";
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        SettlementProfileDefinition post = new SettlementProfileDefinition
        {
            Id = PostProfile, Name = "Торговое село", Kind = SettlementKind.TradingPost,
            AllowGenerated = true, GenerationWeight = 1, GuaranteeInGenerated = true
        };
        SettlementCatalog.Current = new SettlementCatalogData(new[] { post }, new[]
        {
            new ErrandDefinition
            {
                Id = "settlement.errand.carter_knife", Title = "Нож деда", Summary = "Найти и принести.",
                AcceptedFlag = FreePlayCacheStory.Flags.ErrandAccepted, CompletedFlag = FreePlayCacheStory.Flags.ErrandDone
            }
        });
    }

    [TearDown]
    public void TearDown()
    {
        SettlementCatalog.Reset();
        ItemCatalog.ResetToDefaults();
    }

    private static GameState NewFreePlayWithPost(out LocationData post)
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = 20261029 }.CreateCampaign();
        state.Narrative ??= new NarrativeStateData();
        state.ArmySupply = 30;
        post = state.Locations.First(location => !location.IsWaypoint && location.IsVisibleOnMap);
        post.IsSettlement = true;
        if (SettlementService.FindInstance(state, post.Id) == null)
            state.Settlements.Instances.Add(new SettlementInstanceData { LocationId = post.Id, ProfileId = PostProfile });
        else
            SettlementService.FindInstance(state, post.Id).ProfileId = PostProfile;
        return state;
    }

    [Test]
    public void Knowledge_CreatesHiddenReachableCache_AndSearchAreaNotExactPoint()
    {
        GameState state = NewFreePlayWithPost(out LocationData post);
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNull(state.FindLocation(FreePlayCacheStory.LocationId), "Без сведений тайника нет.");
        CollectionAssert.IsEmpty(WorldMapSearchAreas.Active(state));

        state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
        FreePlayContent.RefreshWithReports(state);
        FreePlayContent.RefreshWithReports(state);
        LocationData cache = state.FindLocation(FreePlayCacheStory.LocationId);
        Assert.IsNotNull(cache);
        Assert.AreEqual(1, state.Locations.Count(l => l.Id == FreePlayCacheStory.LocationId), "Создан один раз.");
        Assert.IsFalse(cache.IsVisibleOnMap, "Точного маркера нет.");
        List<MapPointData> route = WorldMapNavigation.FindPath(post.MapXPercent, post.MapYPercent, cache.MapXPercent, cache.MapYPercent);
        Assert.GreaterOrEqual(route.Count, 2, "До тайника есть путь от торгового села.");

        ActiveSearchArea area = WorldMapSearchAreas.Active(state).Single();
        Assert.IsTrue(area.Stage.Contains(cache.MapXPercent, cache.MapYPercent), "Тайник внутри области.");
        cache.IsVisibleOnMap = true;
        CollectionAssert.IsEmpty(WorldMapSearchAreas.Active(state), "Найденное место область убирает.");
    }

    [Test]
    public void Find_GrantsKnifeOnce_ReturnClosesErrandOnce_AndSurvivesSave()
    {
        GameState state = NewFreePlayWithPost(out _);
        state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandAccepted);
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNotNull(FreePlayCacheStory.LocationEntry(state, FreePlayCacheStory.LocationId), "У ивы — сцена находки.");

        state.Narrative.SetFlag(FreePlayCacheStory.Flags.Found);
        FreePlayContent.RefreshWithReports(state);
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(1, state.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Нож выдан один раз.");
        Assert.IsTrue(state.FindLocation(FreePlayCacheStory.LocationId).IsVisibleOnMap);
        Assert.IsNull(FreePlayCacheStory.LocationEntry(state, FreePlayCacheStory.LocationId), "Повторно сцена не открывается.");

        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        FreePlayContent.RefreshWithReports(loaded);
        Assert.AreEqual(1, loaded.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Загрузка не выдаёт второй нож.");

        loaded.Narrative.SetFlag(FreePlayCacheStory.Flags.TokenReturned);
        loaded.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandDone);
        FreePlayContent.RefreshWithReports(loaded);
        FreePlayContent.RefreshWithReports(loaded);
        Assert.AreEqual(0, loaded.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Нож отдан.");
        Assert.AreEqual(JournalGoalState.Completed,
            FreePlayContent.BuildGoals(loaded).Single(g => g.Id == "errand.settlement.errand.carter_knife").State);
    }

    [Test]
    public void SeededScenes_AreValid_WhenInDatabase()
    {
        if (dialogues == null || dialogues.FindDialogue("freeplay_carter") == null)
            Assert.Ignore("Сцены 12Т ещё не засеяны в Базу диалогов (Kingdom Survival → База поселений → «+ Сцены примера»).");
        foreach (string id in new[]
                 {
                     "freeplay_carter", "freeplay_carter_tells", "freeplay_cache_find", "settlement_village_elder",
                     "settlement_village_hostess", "settlement_trade_keeper", "settlement_trade_peddler",
                     "settlement_town_hostess", "settlement_town_milana"
                 })
        {
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
    }

    [Test]
    public void CarterScene_GiveKnife_OnlyWhenKnifeIsInPack()
    {
        if (dialogues == null || dialogues.FindDialogue("freeplay_carter") == null)
            Assert.Ignore("Сцены 12Т ещё не засеяны в Базу диалогов.");
        GameState state = NewFreePlayWithPost(out _);
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandAccepted);
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.Found);
        Assert.IsFalse(Choices(state).Contains("freeplay.carter.give"), "Ножа нет — отдать нечего.");

        ItemService.CreateInstance(state, ItemCatalog.CarterTokenKnife, state.GetSelectedCommander().Id);
        Assert.IsTrue(Choices(state).Contains("freeplay.carter.give"));
    }

    private List<string> Choices(GameState state)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, "freeplay_carter", state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error, new List<string>(), ItemService.GetPresentItemIds(state),
            state.WorldSeed, 1, state), error);
        for (int i = 0; i < 20 && view != null && view.AvailableChoices.Count == 1 &&
                        view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
        {
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view != null ? view.AvailableChoices.Select(c => c.ChoiceId).ToList() : new List<string>();
    }
}
