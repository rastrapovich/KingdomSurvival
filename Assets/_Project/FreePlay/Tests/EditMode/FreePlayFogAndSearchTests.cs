using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// Бриф правок (19 пунктов) пп. 1, 8, 9: туман войны хранит исследованную
// местность отдельно от известных мест, открывается путём героя, переживает
// сохранение и начинается заново в новой партии; область поиска видна только
// у цели, выбранной в журнале, и от выбора не теряет данных; поручение,
// принятое в разговоре, — то же общее состояние, что у корчмы.
public sealed class FreePlayFogAndSearchTests
{
    private const int Seed = 31;

    [SetUp]
    public void SetUp()
    {
        FreePlayContent.Register();
    }

    [TearDown]
    public void TearDown()
    {
        SettlementCatalog.Reset();
        ItemCatalog.ResetToDefaults();
    }

    private static GameState NewGeneratedFreePlay()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), Seed);
        return new CampaignSetup().CreateCampaign(null, null, map);
    }

    private static GameState SaveAndLoad(GameState state) =>
        CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));

    private static LocationData FarHiddenPlace(GameState state)
    {
        GeneratedWorldMap map = state.GeneratedMap;
        return state.Locations
            .Where(location => location.RevealedByFog && !location.IsVisibleOnMap)
            .OrderByDescending(location => WorldMapNavigation.DistanceHexes(
                map.StartXPercent, map.StartYPercent, location.MapXPercent, location.MapYPercent))
            .FirstOrDefault();
    }

    [Test]
    public void Fog_NewGame_RevealsStartOnly_PlacesAppearWithTheirCell_AndSurvivesLoad()
    {
        GameState state = NewGeneratedFreePlay();
        Assert.IsTrue(WorldMapFog.Applies(state));
        GeneratedWorldMap map = state.GeneratedMap;
        Assert.IsTrue(WorldMapFog.IsExploredAtPercent(state, map.StartXPercent, map.StartYPercent), "Округа старта открыта.");
        int startCount = WorldMapFog.ExploredCount(state);
        Assert.Greater(startCount, 0);
        Assert.Less(startCount, WorldMapFog.Grid(state).CellCount / 4, "Открыта только округа, а не вся карта.");

        LocationData village = FarHiddenPlace(state);
        Assert.IsNotNull(village, "Есть место, известное лишь по виду, далеко от старта.");
        Assert.IsFalse(WorldMapFog.IsExploredAtPercent(state, village.MapXPercent, village.MapYPercent));
        Assert.IsFalse(village.IsDiscovered, "Без клетки — ни метки, ни названия.");

        Assert.IsTrue(WorldMapFog.RevealAround(state, village.MapXPercent, village.MapYPercent));
        Assert.IsTrue(village.IsVisibleOnMap, "Открытая клетка делает место известным.");
        Assert.IsTrue(village.IsDiscovered);
        int afterCount = WorldMapFog.ExploredCount(state);
        Assert.Greater(afterCount, startCount);
        Assert.IsFalse(WorldMapFog.RevealAround(state, village.MapXPercent, village.MapYPercent), "Повтор ничего не меняет.");

        GameState loaded = SaveAndLoad(state);
        Assert.AreEqual(afterCount, WorldMapFog.ExploredCount(loaded), "Исследованное сохраняется.");
        Assert.IsTrue(loaded.FindLocation(village.Id).IsVisibleOnMap);
        Assert.IsTrue(WorldMapFog.IsExploredAtPercent(loaded, village.MapXPercent, village.MapYPercent));

        GameState fresh = NewGeneratedFreePlay();
        Assert.AreEqual(startCount, WorldMapFog.ExploredCount(fresh), "Новая партия — туман заново.");
        Assert.IsFalse(fresh.FindLocation(village.Id).IsVisibleOnMap);
    }

    [Test]
    public void Fog_KnownObjectDoesNotRevealTerritory_AndOldSaveGetsHeroSurroundings()
    {
        GameState state = NewGeneratedFreePlay();
        LocationData place = FarHiddenPlace(state);
        // Слух/поручение: место известно, окрестности — нет.
        place.IsVisibleOnMap = true;
        Assert.IsFalse(WorldMapFog.IsExploredAtPercent(state, place.MapXPercent, place.MapYPercent));

        // Старое сохранение без тумана: открыта округа текущей точки героя.
        state.Fog = null;
        GameState loaded = SaveAndLoad(state);
        Assert.Greater(WorldMapFog.ExploredCount(loaded), 0);
        float x = loaded.HasActiveExpedition ? loaded.ActiveExpedition.CurrentMapXPercent : loaded.GeneratedMap.StartXPercent;
        float y = loaded.HasActiveExpedition ? loaded.ActiveExpedition.CurrentMapYPercent : loaded.GeneratedMap.StartYPercent;
        Assert.IsTrue(WorldMapFog.IsExploredAtPercent(loaded, x, y));
        Assert.IsTrue(loaded.FindLocation(place.Id).IsVisibleOnMap, "Известное место остаётся известным.");
    }

    [Test]
    public void Fog_AuthoredCrisisWorld_HasNoFog()
    {
        GameState state = new CampaignSetup().CreateCampaign();
        Assert.IsFalse(WorldMapFog.Applies(state));
        Assert.IsTrue(WorldMapFog.IsExploredAtPercent(state, 90f, 90f), "Без тумана всё «исследовано».");
    }

    [Test]
    public void SearchArea_ShownOnlyForSelectedJournalGoal_DataKept()
    {
        const string errandId = "settlement.errand.carter_knife";
        SettlementCatalog.Current = new SettlementCatalogData(new SettlementProfileDefinition[0], new[]
        {
            new ErrandDefinition
            {
                Id = errandId, Title = "Нож деда", AcceptedFlag = FreePlayCacheStory.Flags.ErrandAccepted,
                CompletedFlag = FreePlayCacheStory.Flags.ErrandDone,
                Stages = new List<ErrandStageDefinition>
                {
                    new ErrandStageDefinition { Flag = ItemCatalog.OldBedCacheStageFlag, Text = "Область отмечена." }
                }
            }
        });
        GameState state = new CampaignSetup { WorldSeed = 20261029 }.CreateCampaign();
        state.Narrative ??= new NarrativeStateData();
        state.Narrative.AddKnowledge(ItemCatalog.OldBedCacheKnowledge);
        FreePlayContent.RefreshWithReports(state);
        state.Narrative.SetFlag(ItemCatalog.OldBedCacheStageFlag);
        Assert.AreEqual(1, WorldMapSearchAreas.Active(state).Count, "Область существует.");

        string goalId = ErrandService.GoalIdPrefix + errandId;
        CollectionAssert.IsEmpty(WorldMapSearchAreas.ForGoal(state, null), "Ничего не выбрано — области нет.");
        CollectionAssert.IsEmpty(WorldMapSearchAreas.ForGoal(state, "freeplay.goal.mine"), "У другой цели области нет.");
        Assert.AreEqual(1, WorldMapSearchAreas.ForGoal(state, goalId).Count, "Выбрана цель — её область.");
        Assert.AreEqual(1, WorldMapSearchAreas.Active(state).Count, "Смена выбора данных не трогает.");
        Assert.IsTrue(state.Narrative.HasFlag(ItemCatalog.OldBedCacheStageFlag));
    }

    [Test]
    public void Errand_AcceptedInDialogue_SharesStateWithTavern_NoDoubleAccept()
    {
        const string errandId = "test.errand";
        const string accepted = "test.errand.accepted";
        const string done = "test.errand.done";
        SettlementProfileDefinition post = new SettlementProfileDefinition
        {
            Id = SettlementTradeDevFixture.TradingPostProfileId, Name = "Торговое село (тест)", Kind = SettlementKind.TradingPost
        };
        post.Tavern = new TavernDefinition { Enabled = true, Id = "test.tavern", Name = "Корчма" };
        post.Tavern.Visitors.Add(new SettlementPersonDefinition { Id = "carter", DisplayName = "Ждан" });
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.errand", Label = "Взяться", PersonId = "carter", ErrandId = errandId });
        SettlementCatalog.Current = new SettlementCatalogData(new[] { post }, new[]
        {
            new ErrandDefinition { Id = errandId, Title = "Нож деда", AcceptedFlag = accepted, CompletedFlag = done }
        });
        GameState state = new CampaignSetup { WorldSeed = 20261031 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(SettlementTradeDevFixture.Apply(state, post.Id, out string report), report);
        string here = SettlementCampDevFixture.SettlementId;
        SettlementService.MarkVisited(state, here);
        Assert.IsTrue(TavernService.Offers(state, here).Any(o => o.Offer.Id == "offer.errand"), "До принятия предложение есть.");

        // Принято в разговоре (эффект SetFlag диалога).
        state.Narrative.SetFlag(accepted);
        ErrandService.Refresh(state);
        Assert.IsFalse(TavernService.Offers(state, here).Any(o => o.Offer.Id == "offer.errand"), "В корчме больше не предлагают.");
        Assert.IsFalse(ErrandService.TryAccept(state, errandId, "корчма", out _), "Второго принятия нет.");
        Assert.AreEqual(1, ErrandService.BuildGoals(state).Count(g => g.Id == ErrandService.GoalIdPrefix + errandId), "Одно дело в журнале.");

        state.Narrative.SetFlag(done);
        ErrandService.Refresh(state);
        Assert.AreEqual(ErrandStatus.Completed, ErrandService.Status(state, SettlementCatalog.Current.FindErrand(errandId)));
        Assert.IsFalse(TavernService.Offers(state, here).Any(o => o.Offer.Id == "offer.errand"), "Выполненное не возвращается.");
    }
}
