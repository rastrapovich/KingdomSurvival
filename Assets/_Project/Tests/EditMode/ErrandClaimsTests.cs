using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Бриф правок (22 пункта) пп. 16, 21, 22: два торговых села с одним профилем.
// Поручение закрепляется за первым поселением, где появилось; в другом нет
// ни объявления, ни предложения, ни заказчика; итог применяется только в
// поселении выдачи (даже прямым вызовом эффектов); выполненное исчезает из
// всех источников; привязка переживает сохранение и загрузку.
public sealed class ErrandClaimsTests
{
    private const string Post = SettlementTradeDevFixture.TradingPostProfileId;
    private const string A = SettlementCampDevFixture.SettlementId;
    private const string B = "test.post.b";
    private const string ErrandId = "test.errand";
    private const string Accepted = "test.errand.accepted";
    private const string Done = "test.errand.done";

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
        CampaignSession.Reset();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
        ItemCatalog.ResetToDefaults();
        SettlementProfileDefinition post = new SettlementProfileDefinition { Id = Post, Name = "Торговое село (тест)", Kind = SettlementKind.TradingPost };
        post.Tavern = new TavernDefinition { Enabled = true, Id = "test.tavern", Name = "Корчма" };
        post.Tavern.Visitors.Add(new SettlementPersonDefinition { Id = "carter", DisplayName = "Ждан" });
        post.Tavern.Visitors.Add(new SettlementPersonDefinition { Id = "peddler", DisplayName = "Гостята" });
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.errand", Label = "Взяться", PersonId = "carter", ErrandId = ErrandId });
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.info", Label = "Где тайник", PersonId = "carter", KnowledgeId = "test.k" });
        post.Board = new NoticeBoardDefinition { Enabled = true, Name = "Доска" };
        post.Board.Notices.Add(new NoticeDefinition { Id = "notice.request", Title = "Просьба", Text = "…", ErrandId = ErrandId, ContactPersonId = "carter" });
        post.Board.Notices.Add(new NoticeDefinition { Id = "notice.news", Title = "Новость", Text = "…", Type = NoticeType.LocalNews });
        ErrandDefinition errand = new ErrandDefinition
        {
            Id = ErrandId, Title = "Нож деда", Summary = "Найти и принести.", AcceptedFlag = Accepted, CompletedFlag = Done
        };
        SettlementCatalog.Current = new SettlementCatalogData(new[] { post }, new[] { errand });
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
        ItemCatalog.ResetToDefaults();
        SettlementCatalog.Reset();
    }

    // Отряд у технического села A и второе село B с тем же профилем.
    private static GameState TwoPosts()
    {
        GameState state = new CampaignSetup { WorldSeed = 20261030 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(SettlementTradeDevFixture.Apply(state, Post, out string report), report);
        state.ActiveExpedition.FighterIds.Remove(SettlementCampDevFixture.CreatureId);
        LocationData a = state.FindLocation(A);
        state.Locations.Add(new LocationData(B, "Второе торговое село", 0.0, "низкая")
        {
            RegionId = "dev", RegionName = "Проверка", MapXPercent = a.MapXPercent, MapYPercent = a.MapYPercent,
            IsDiscovered = true, IsVisibleOnMap = true, IsSettlement = true
        });
        state.Settlements.Instances.Add(new SettlementInstanceData { LocationId = B, ProfileId = Post });
        return state;
    }

    private static void EnterAt(GameState state, string locationId)
    {
        SettlementCampService.ReturnToCamp(state);
        state.SettlementCamp = null;
        state.ActiveExpedition.LocationId = locationId;
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        Assert.IsTrue(SettlementCampService.Enter(state, SettlementCampService.Companions(state), out string reason), reason);
        Assert.AreEqual(locationId, SettlementCampService.InsideLocationId(state));
    }

    private static NarrativeEvaluationContext Context(GameState state) =>
        new NarrativeEvaluationContext(state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(), state.Narrative, gameState: state);

    private static List<NarrativeEffect> TurnIn() => new List<NarrativeEffect>
    {
        new NarrativeEffect { EffectExecutionId = "test.turnin.flag", Type = NarrativeEffectType.SetFlag, StringParam = Done },
        new NarrativeEffect { EffectExecutionId = "test.turnin.gold", Type = NarrativeEffectType.ChangeArmyGold, IntParam = 10 }
    };

    private static List<string> People(GameState state, string locationId) =>
        SettlementService.VisiblePeople(state, SettlementService.Profile(state, locationId).Tavern.Visitors, locationId).Select(p => p.Id).ToList();

    [Test]
    public void FirstSettlementClaimsErrand_OtherCopiesShowNoOfferNoticeOrGiver()
    {
        GameState state = TwoPosts();
        EnterAt(state, A);
        ErrandClaimData claim = ErrandClaims.Find(state, ErrandId);
        Assert.IsNotNull(claim, "Первый вход закрепил поручение.");
        Assert.AreEqual(A, claim.LocationId);
        Assert.AreEqual("carter", claim.GiverPersonId);
        Assert.IsTrue(NoticeBoardService.Notices(state, A).Any(n => n.Notice.Id == "notice.request"));
        Assert.IsTrue(TavernService.Offers(state, A).Any(o => o.Offer.Id == "offer.errand"));

        EnterAt(state, B);
        Assert.AreEqual(A, ErrandClaims.Find(state, ErrandId).LocationId, "Второй вход привязку не меняет.");
        Assert.IsFalse(NoticeBoardService.Notices(state, B).Any(n => n.Notice.Id == "notice.request"), "Объявление не дублируется.");
        Assert.IsTrue(NoticeBoardService.Notices(state, B).Any(n => n.Notice.Id == "notice.news"), "Прочее на доске остаётся.");
        Assert.IsFalse(TavernService.Offers(state, B).Any(o => o.Offer.Id == "offer.errand"), "Предложение не дублируется.");
        Assert.IsFalse(TavernService.Offers(state, B).Any(o => o.Offer.PersonId == "carter"), "Заказчика в чужом селе нет — и его предложений.");
        CollectionAssert.DoesNotContain(People(state, B), "carter", "Копия заказчика в другом селе не появляется.");
        CollectionAssert.Contains(People(state, B), "peddler", "Остальные люди на месте.");
    }

    [Test]
    public void TurnIn_OnlyAtSourceSettlement_EvenByDirectEffects_AndOnce()
    {
        GameState state = TwoPosts();
        EnterAt(state, A);
        Assert.IsTrue(ErrandService.TryAccept(state, ErrandId, "доска", out string message), message);
        int gold = state.ArmyGold;

        EnterAt(state, B);
        NarrativeEffectApplier.ApplyAll(TurnIn(), Context(state));
        Assert.IsFalse(state.Narrative.HasFlag(Done), "В чужом селе сдать нельзя.");
        Assert.AreEqual(gold, state.ArmyGold, "Награда в чужом селе не выдана.");

        EnterAt(state, A);
        NarrativeEffectApplier.ApplyAll(TurnIn(), Context(state));
        Assert.IsTrue(state.Narrative.HasFlag(Done), "В селе выдачи сдача работает.");
        Assert.AreEqual(gold + 10, state.ArmyGold);
        StringAssert.Contains("Вернуться: Ждан, Техническое поселение", Chronicle.Find(state, "errand." + ErrandId + ".accepted").Text,
            "Хроника говорит, к кому и куда вернуться.");
    }

    [Test]
    public void CompletedErrand_DisappearsFromAllSources_GiverStays()
    {
        GameState state = TwoPosts();
        EnterAt(state, A);
        Assert.IsTrue(ErrandService.TryAccept(state, ErrandId, "доска", out string message), message);
        NarrativeEffectApplier.ApplyAll(TurnIn(), Context(state));
        ErrandService.Refresh(state);

        Assert.IsFalse(NoticeBoardService.Notices(state, A).Any(n => n.Notice.Id == "notice.request"), "Объявление исчезло.");
        Assert.IsFalse(TavernService.Offers(state, A).Any(o => o.Offer.Id == "offer.errand"), "Предложение исчезло, а не сереет.");
        CollectionAssert.Contains(People(state, A), "carter", "Сам заказчик и прочие его разговоры остаются.");
        Assert.AreEqual(JournalGoalState.Completed, ErrandService.BuildGoals(state).Single().State, "Запись о выполнении есть.");
    }

    [Test]
    public void Claims_SurviveSaveAndLoad()
    {
        GameState state = TwoPosts();
        EnterAt(state, A);
        Assert.IsTrue(ErrandService.TryAccept(state, ErrandId, "доска", out string message), message);
        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.AreEqual(A, ErrandClaims.Find(loaded, ErrandId).LocationId);
        EnterAt(loaded, B);
        CollectionAssert.DoesNotContain(People(loaded, B), "carter");
        int gold = loaded.ArmyGold;
        NarrativeEffectApplier.ApplyAll(TurnIn(), Context(loaded));
        Assert.AreEqual(gold, loaded.ArmyGold, "После загрузки сдать в чужом селе по-прежнему нельзя.");
    }

    // Старое сохранение: поручение взято до привязки, а сёл с ним два —
    // однозначно восстановить нельзя; привязка пустая, сдача — как раньше.
    [Test]
    public void OldSave_AmbiguousAcceptedErrand_StaysUnbound()
    {
        GameState state = TwoPosts();
        state.Narrative.SetFlag(Accepted);
        EnterAt(state, B);
        ErrandClaimData claim = ErrandClaims.Find(state, ErrandId);
        Assert.IsNotNull(claim);
        Assert.AreEqual(string.Empty, claim.LocationId, "Деревня не назначается наугад.");
        NarrativeEffectApplier.ApplyAll(TurnIn(), Context(state));
        Assert.IsTrue(state.Narrative.HasFlag(Done), "Прогресс старого сохранения не теряется.");
    }
}
