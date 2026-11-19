using System.Collections.Generic;
using NUnit.Framework;

// Метки карты мира (бриф «Метки мест и доступных квестов»): «?» — известное и
// не исследованное место, «!» — можно взять новое поручение (корчма, доска).
// Скрытое место и скрытое условиями предложение не раскрываются; принятое,
// выполненное и «только сдать» — без «!»; несколько поручений — один «!»;
// «!» важнее «?»; проверка ничего не вывешивает и не меняет.
public sealed class WorldMapMarkerServiceTests
{
    private const string Profile = "test.markers.post";
    private const string Village = "test.markers.village";
    private const string ErrandA = "test.markers.errand.a";
    private const string ErrandB = "test.markers.errand.b";

    private GameState state;
    private SettlementProfileDefinition post;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        post = new SettlementProfileDefinition { Id = Profile, Name = "Село (тест)", Kind = SettlementKind.TradingPost };
        post.Tavern = new TavernDefinition { Enabled = true, Id = "test.markers.tavern", Name = "Корчма" };
        post.Tavern.Visitors.Add(new SettlementPersonDefinition { Id = "carter", DisplayName = "Ждан" });
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.a", Label = "Взяться", PersonId = "carter", ErrandId = ErrandA });
        post.Board = new NoticeBoardDefinition { Enabled = true, Name = "Доска" };
        post.Board.Notices.Add(new NoticeDefinition { Id = "notice.a", Title = "Просьба", Text = "…", ErrandId = ErrandA, LifetimeHours = 48 });
        post.Board.Notices.Add(new NoticeDefinition { Id = "notice.news", Title = "Новость", Text = "…", Type = NoticeType.LocalNews });
        SettlementCatalog.Current = new SettlementCatalogData(new[] { post }, new[]
        {
            new ErrandDefinition { Id = ErrandA, Title = "Нож деда", AcceptedFlag = ErrandA + ".accepted", CompletedFlag = ErrandA + ".done" },
            new ErrandDefinition { Id = ErrandB, Title = "Пропавшая коза", AcceptedFlag = ErrandB + ".accepted", CompletedFlag = ErrandB + ".done" },
        });
        state = new CampaignSetup { WorldSeed = 20261119 }.CreateCampaign();
        state.Locations.Add(new LocationData(Village, "Тестовое село", 2.0, "низкая")
        {
            MapXPercent = 40f, MapYPercent = 40f, IsDiscovered = true, IsVisibleOnMap = true, IsSettlement = true
        });
        SettlementService.EnsureState(state);
        state.Settlements.Instances.Add(new SettlementInstanceData { LocationId = Village, ProfileId = Profile });
    }

    [TearDown]
    public void TearDown()
    {
        SettlementCatalog.Reset();
        CampaignSession.Reset();
    }

    private LocationData Place(string id, bool visible, double hours, bool explored = false)
    {
        LocationData place = new LocationData(id, "Место " + id, 3.0, "низкая", hours)
        {
            MapXPercent = 60f, MapYPercent = 60f, IsDiscovered = visible, IsVisibleOnMap = visible, IsExplored = explored
        };
        state.Locations.Add(place);
        return place;
    }

    private WorldMapMarkerInfo Marker(string id) => WorldMapMarkerService.For(state, state.FindLocation(id));

    [Test]
    public void HiddenPlace_HasNoMarker()
    {
        Place("test.hidden", false, 4.0);
        Assert.AreEqual(WorldMapMarkerKind.None, Marker("test.hidden").Kind, "Скрытая точка не выдаётся.");
    }

    [Test]
    public void FoundUnexploredPlace_IsQuestion_ExploredIsNothing()
    {
        Place("test.found", true, 4.0);
        Assert.AreEqual(WorldMapMarkerKind.UnknownPlace, Marker("test.found").Kind);
        state.FindLocation("test.found").IsExplored = true;
        Assert.AreEqual(WorldMapMarkerKind.None, Marker("test.found").Kind, "Исследованное — без метки.");
        // Место без действия исследования (декорация, точка пути) — без «?».
        Place("test.scenery", true, 0.0);
        Assert.AreEqual(WorldMapMarkerKind.None, Marker("test.scenery").Kind);
    }

    [Test]
    public void SettlementWithOffer_IsExclamation_OverQuestion()
    {
        WorldMapMarkerInfo info = Marker(Village);
        Assert.AreEqual(WorldMapMarkerKind.QuestAvailable, info.Kind, "«!» важнее «?».");
        Assert.IsTrue(info.Unexplored, "Подсказка помнит, что в селе ещё не были.");
        Assert.AreEqual(1, info.QuestOffers, "Корчма и доска с одним поручением — одно предложение.");
        StringAssert.Contains("Можно взять задание", info.Hint);
        StringAssert.Contains("не исследовано", info.Hint);
    }

    [Test]
    public void AcceptedOrCompletedErrand_RemovesExclamation()
    {
        state.Narrative.SetFlag(ErrandA + ".accepted");
        Assert.AreEqual(WorldMapMarkerKind.UnknownPlace, Marker(Village).Kind, "Принятое — без «!», «?» остаётся до посещения.");
        SettlementService.FindInstance(state, Village).Visited = true;
        Assert.AreEqual(WorldMapMarkerKind.None, Marker(Village).Kind, "Посещённое село без новых поручений — без метки.");
        state.Narrative.SetFlag(ErrandA + ".done");
        Assert.AreEqual(WorldMapMarkerKind.None, Marker(Village).Kind, "«Только сдать» или выполнено — не «!».");
    }

    [Test]
    public void SeveralOffers_OneExclamation_WithCount()
    {
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.b", Label = "Коза", PersonId = "carter", ErrandId = ErrandB });
        WorldMapMarkerInfo info = Marker(Village);
        Assert.AreEqual(WorldMapMarkerKind.QuestAvailable, info.Kind);
        Assert.AreEqual(2, info.QuestOffers);
        StringAssert.Contains("(2)", info.Hint);
    }

    // «!» над зданиями на живой карте: корчма и доска считаются по отдельности —
    // метка встаёт только над тем зданием, где поручение действительно есть.
    [Test]
    public void OffersSplitByBuilding_TavernAndBoard()
    {
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.b", Label = "Коза", PersonId = "carter", ErrandId = ErrandB });
        CollectionAssert.AreEquivalent(new[] { ErrandA, ErrandB }, WorldMapMarkerService.TavernQuestErrands(state, Village));
        CollectionAssert.AreEquivalent(new[] { ErrandA }, WorldMapMarkerService.BoardQuestErrands(state, Village));
        post.Board.Notices.RemoveAll(n => n.ErrandId == ErrandA);
        Assert.AreEqual(0, WorldMapMarkerService.BoardQuestErrands(state, Village).Count, "Доска без поручений — без «!» над столбом.");
        post.Tavern.Offers.Clear();
        Assert.AreEqual(0, WorldMapMarkerService.TavernQuestErrands(state, Village).Count, "Корчма без поручений — без «!» над ней.");
        Assert.AreEqual(0, WorldMapMarkerService.AvailableQuestOffers(state, Village));
    }

    [Test]
    public void HiddenByConditionsOffer_IsNotRevealed()
    {
        post.Tavern.Offers.Clear();
        post.Board.Notices.Clear();
        post.Tavern.Offers.Add(new TavernOfferDefinition
        {
            Id = "offer.secret", Label = "Тайное", PersonId = "carter", ErrandId = ErrandB,
            Conditions = new NarrativeConditionGroup
            {
                Combinator = NarrativeConditionCombinator.All,
                Conditions = new List<NarrativeCondition> { new NarrativeCondition { Type = NarrativeConditionType.KnowledgeKnown, StringParam = "test.markers.secret" } }
            }
        });
        Assert.AreEqual(0, Marker(Village).QuestOffers, "Предложение, скрытое условиями, иконкой не раскрывается.");
        Assert.AreEqual(WorldMapMarkerKind.UnknownPlace, Marker(Village).Kind);
    }

    [Test]
    public void Check_DoesNotPostNoticesOrChangeState()
    {
        int before = SettlementService.FindInstance(state, Village).Notices.Count;
        WorldMapMarkerService.For(state, state.FindLocation(Village));
        Assert.AreEqual(before, SettlementService.FindInstance(state, Village).Notices.Count, "Метка ничего не вывешивает на доске.");
        Assert.IsFalse(state.Narrative.HasFlag(ErrandA + ".accepted"), "Метка ничего не принимает.");
        Assert.IsFalse(SettlementService.FindInstance(state, Village).Visited, "Метка не отмечает посещение.");
    }
}
