using System.Collections.Generic;
using System.Linq;
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
        GameState state = new CampaignSetup { WorldSeed = 20261029 }.CreateCampaign();
        state.Narrative ??= new NarrativeStateData();
        state.ArmySupply = 30;
        // Руин в новой игре нет (бриф правок п. 15) — торговым селом делаем
        // первое место карты и показываем его.
        post = state.Locations.First(location => !location.IsWaypoint);
        post.IsVisibleOnMap = true;
        post.IsDiscovered = true;
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
        Assert.IsFalse(state.FindLocation(FreePlayCacheStory.LocationId).IsVisibleOnMap, "Обысканное место ушло с карты.");
        CollectionAssert.IsEmpty(WorldMapSearchAreas.Active(state), "Области поиска больше нет.");
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

    // 12У-D, бриф распределения историй: на сгенерированной карте тайник
    // закрепляется за местом у реки на проверенном пути от торгового села,
    // один раз на партию; загрузка и повторное обновление не перевыбирают.
    [Test]
    public void GeneratedMap_BindsCacheToRiverbank_OncePerGame()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 41);
        try
        {
            WorldMapNavigation.ConfigureFromDefinition(map.ToDefinitionData());
            GameState state = new CampaignSetup { WorldSeed = 41 }.CreateCampaign(null, null, map);
            state.Narrative ??= new NarrativeStateData();
            LocationData post = FreePlayCacheStory.FindTradingPost(state);
            Assert.IsNotNull(post, "Обязательное торговое село назначено деревне карты.");

            FreePlayContent.RefreshWithReports(state);
            StoryRoleBindingData binding = WorldMapStoryBinder.Find(state, "freeplay.cache", "cache");
            Assert.IsNotNull(binding, "Роль закреплена до того, как игрок узнал о тайнике.");
            Assert.IsTrue(binding.Bound, binding.Reason);
            Assert.That(binding.TravelHours, Is.InRange(FreePlayCacheStory.CacheRole.MinTravelHours, FreePlayCacheStory.CacheRole.MaxTravelHours));
            WorldMapHexGrid grid = map.CreateGrid();
            WorldMapHexCell cell = grid.CellAt(binding.Cell);
            Assert.IsTrue(map.RiverCells.Any(r => grid.CellAt(r).DistanceTo(cell) <= 2), "Старое русло — у реки: " + binding.Reason);
            Assert.IsTrue(map.Villages.All(v => grid.CellAt(v.Cell).DistanceTo(cell) >= 2), "Не в деревне.");

            state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
            FreePlayContent.RefreshWithReports(state);
            LocationData cache = state.FindLocation(FreePlayCacheStory.LocationId);
            Assert.AreEqual(binding.XPercent, cache.MapXPercent, 0.001f);
            Assert.AreEqual(binding.YPercent, cache.MapYPercent, 0.001f);

            GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
                JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
            FreePlayContent.RefreshWithReports(loaded);
            StoryRoleBindingData again = WorldMapStoryBinder.Find(loaded, "freeplay.cache", "cache");
            Assert.AreEqual(binding.Cell, again.Cell, "Загрузка не перевыбирает место.");
            Assert.AreEqual(1, loaded.Settlements.StoryBindings.Count(b => b.StoryId == "freeplay.cache"));

            StoryRoleBindingData fresh = WorldMapStoryBinder.Choose(map, 41, FreePlayCacheStory.CacheRole, post.MapXPercent, post.MapYPercent);
            Assert.AreEqual(binding.Cell, fresh.Cell, "Тот же ключ партии — то же место.");
        }
        finally
        {
            WorldMapNavigation.ConfigureDefaultTerrain();
        }
    }

    // ---------- Бриф распределения историй §23.2: сценарии корректности ----------

    // §23.2.2 «Ранний вход»: береста торговца дала сведения без поручения —
    // герой нашёл тайник раньше, чем взял дело у Ждана. Находка не теряется,
    // нож не выдаётся второй раз, позднее поручение закрывается тем же ножом.
    [Test]
    public void EarlyFind_BeforeErrand_KeepsProgress_AndClosesLaterErrandOnce()
    {
        GameState state = NewFreePlayWithPost(out _);
        state.Narrative.SetFlag(ItemCatalog.OldBedCacheStageFlag);
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNotNull(state.FindLocation(FreePlayCacheStory.LocationId), "Береста — тоже источник сведений.");
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.Found);
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(1, state.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Нож найден до поручения.");
        Assert.IsFalse(FreePlayContent.BuildGoals(state).Any(g => g.Id == "errand.settlement.errand.carter_knife" && g.State == JournalGoalState.Completed),
            "Поручение не закрылось само: его ещё не брали.");

        state.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandAccepted);
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(1, state.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Взятое позже поручение не выдаёт второй нож.");
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.TokenReturned);
        state.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandDone);
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(0, state.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Отдан тот самый нож.");
        Assert.AreEqual(JournalGoalState.Completed,
            FreePlayContent.BuildGoals(state).Single(g => g.Id == "errand.settlement.errand.carter_knife").State);
    }

    // §23.2.3–4 «Разный порядок и другой источник»: рассказ возчика и береста
    // торговца в любом порядке дают один тайник, одну область поиска и одно дело.
    [Test]
    public void TwoSources_AnyOrder_GiveOneCacheOneAreaOneErrand()
    {
        foreach (bool birchFirst in new[] { true, false })
        {
            GameState state = NewFreePlayWithPost(out _);
            if (birchFirst)
                state.Narrative.SetFlag(ItemCatalog.OldBedCacheStageFlag);
            else
                state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
            FreePlayContent.RefreshWithReports(state);
            LocationData first = state.FindLocation(FreePlayCacheStory.LocationId);
            if (birchFirst)
                state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
            else
                state.Narrative.SetFlag(ItemCatalog.OldBedCacheStageFlag);
            state.Narrative.SetFlag(FreePlayCacheStory.Flags.ErrandAccepted);
            FreePlayContent.RefreshWithReports(state);
            FreePlayContent.RefreshWithReports(state);
            Assert.AreEqual(1, state.Locations.Count(l => l.Id == FreePlayCacheStory.LocationId), "Один тайник (береста первой: " + birchFirst + ").");
            Assert.AreEqual(first.MapXPercent, state.FindLocation(FreePlayCacheStory.LocationId).MapXPercent, 0.0001f, "Второй источник место не двигает.");
            Assert.AreEqual(1, WorldMapSearchAreas.Active(state).Count, "Одна область поиска.");
            Assert.AreEqual(1, FreePlayContent.BuildGoals(state).Count(g => g.Id == "errand.settlement.errand.carter_knife"), "Одно дело в журнале.");
        }
    }

    // §23.2.12 «Загрузка в значимых точках»: сохранение после сведений и до
    // находки — тайник на том же месте, область поиска на месте, ничего не выдано.
    [Test]
    public void SaveAfterKnowledge_BeforeFind_KeepsPlaceAndArea_NoReward()
    {
        GameState state = NewFreePlayWithPost(out _);
        state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
        FreePlayContent.RefreshWithReports(state);
        LocationData cache = state.FindLocation(FreePlayCacheStory.LocationId);
        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        FreePlayContent.RefreshWithReports(loaded);
        LocationData again = loaded.FindLocation(FreePlayCacheStory.LocationId);
        Assert.IsNotNull(again);
        Assert.AreEqual(cache.MapXPercent, again.MapXPercent, 0.0001f);
        Assert.AreEqual(cache.MapYPercent, again.MapYPercent, 0.0001f);
        Assert.IsFalse(again.IsVisibleOnMap, "После загрузки точного маркера нет.");
        Assert.AreEqual(1, WorldMapSearchAreas.Active(loaded).Count);
        Assert.AreEqual(0, loaded.Inventory.Items.Count(i => i.ItemId == ItemCatalog.CarterTokenKnife), "Загрузка не выдала находку.");
    }

    // §23.2.9–10 «Нет допустимого места»: невыполнимая роль не подменяется
    // молча — привязка помечена неудачной с причиной, без исключения.
    [Test]
    public void ImpossibleRole_FailsHonestly_WithReason()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 41);
        try
        {
            WorldMapNavigation.ConfigureFromDefinition(map.ToDefinitionData());
            StoryRoleSpec impossible = new StoryRoleSpec
            {
                StoryId = "test.story", RoleId = "far", MinTravelHours = 5000.0, MaxTravelHours = 6000.0, TargetTravelHours = 5500.0,
                Terrains = new List<WorldMapGameplayTerrainType> { WorldMapGameplayTerrainType.OpenGround }
            };
            GeneratedMapSite village = map.Villages[0];
            StoryRoleBindingData result = WorldMapStoryBinder.Choose(map, 41, impossible, village.XPercent, village.YPercent);
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Bound, "Невыполнимая роль не привязана.");
            Assert.IsNotEmpty(result.Reason, "Причина отказа видна автору.");
        }
        finally
        {
            WorldMapNavigation.ConfigureDefaultTerrain();
        }
    }
}
