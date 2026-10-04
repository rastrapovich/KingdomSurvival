using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-12К (канон v1.53 §28.9–28.10): лагерь у поселения и физическое
// присутствие. Оставленные ждут снаружи: остаются в походе (Дом их не
// получает обратно), но не вступают в бой и не отвечают внутри; существо-
// союзник не допускается и ждёт; сбор возвращает всех живых; лишних и
// погибших в лагере нет; состояние переживает JsonUtility.
public sealed class SettlementCampTests
{
    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
    }

    private static GameState NewStateAtSettlement(int fighters, bool withRetinue = true)
    {
        GameState state = new GameState();
        state.CreateNewGame(20261005);
        state.ArmySupply = 100;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        List<string> selected = state.Fighters.Take(fighters).Select(f => f.Id).ToList();
        Assert.IsTrue(state.TryStartExpedition(target.Id, selected, out string message), message);
        if (withRetinue)
            state.ActiveExpedition.RetinueIds.Add(HomePeopleService.OstafiyId);
        // Отряд вышел из Дома — домашние функции участников выключены.
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(SettlementCampDevFixture.Apply(state, out string report), report);
        return state;
    }

    [Test]
    public void Fixture_AddsExternalsWithinTheFighterLimit_NotToHomePopulation()
    {
        GameState state = NewStateAtSettlement(2);
        Assert.AreEqual(4, state.ActiveExpedition.FighterIds.Count, "Наёмник и существо заняли места бойцов.");
        Assert.LessOrEqual(state.ActiveExpedition.FighterIds.Count, GameState.ExpeditionFighterSlots);
        ResidentState creature = HomePeopleService.Find(state, SettlementCampDevFixture.CreatureId);
        Assert.AreEqual(ResidentMembership.External, creature.Membership);
        Assert.AreEqual(CampaignParticipantOrigin.CreatureAlly, creature.Origin);
        Assert.IsFalse(creature.IsHomeMember, "Внешний участник не член населения Дома.");
        int population = state.Population;
        HomePeopleService.RecountPopulation(state);
        Assert.AreEqual(population, state.Population);

        GameState full = NewStateAtSettlement(4);
        Assert.AreEqual(GameState.ExpeditionFighterSlots, full.ActiveExpedition.FighterIds.Count, "Сверх лимита никто не добавлен.");
    }

    [Test]
    public void SplitGroup_WaitingAreAbsentInside_ButStayInTheExpedition()
    {
        GameState state = NewStateAtSettlement(2);
        string hero = state.GetSelectedCommander().Id;
        string garrick = state.ActiveExpedition.FighterIds[0];
        string leftBehind = state.ActiveExpedition.FighterIds[1];
        List<string> fightersBefore = state.ActiveExpedition.FighterIds.ToList();
        int supplyUse = state.ExpeditionSupplyConsumption;

        Assert.IsTrue(SettlementCampService.Enter(state, new[] { garrick, SettlementCampDevFixture.MercenaryId }, out string reason), reason);

        List<string> present = PartyPresence.PresentIds(state);
        CollectionAssert.AreEquivalent(new[] { hero, garrick, SettlementCampDevFixture.MercenaryId }, present);
        Assert.IsTrue(PartyPresence.IsWaitingInCamp(state, leftBehind));
        Assert.IsTrue(PartyPresence.IsWaitingInCamp(state, HomePeopleService.OstafiyId), "Свита тоже может ждать.");
        Assert.IsTrue(PartyPresence.IsWaitingInCamp(state, SettlementCampDevFixture.CreatureId));
        Assert.IsFalse(present.Intersect(state.SettlementCamp.WaitingIds).Any(), "Один ID не может быть и внутри, и в лагере.");

        CollectionAssert.AreEqual(fightersBefore, state.ActiveExpedition.FighterIds, "Ожидающие не удалены из похода.");
        Assert.AreEqual(supplyUse, state.ExpeditionSupplyConsumption, "Едят из припасов похода, как раньше.");
        Assert.IsTrue(HomePeopleService.IsInExpedition(state, HomePeopleService.OstafiyId),
            "Остафий ждёт у города — дома его функции нет.");

        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "test.town.battle");
        CollectionAssert.AreEquivalent(new[] { hero, garrick, SettlementCampDevFixture.MercenaryId },
            request.Participants.Select(p => p.PersonId), "В городском бою — только вошедшие.");
        CollectionAssert.DoesNotContain(CharacterProgressionService.PartyPersonIds(state), leftBehind,
            "Опыт и особенности внутри — без ожидающих.");
        Assert.AreEqual(3, PartyPresence.PresentPartySize(state), "Размер группы здесь: командир и двое бойцов.");
        CollectionAssert.DoesNotContain(PartyPresence.PresentCompanionIds(state), HomePeopleService.OstafiyId,
            "Остафий не отвечает изнутри.");
    }

    [Test]
    public void BlockedCreature_CannotEnter_ReasonIsVisible_AndItFightsOnTheRoad()
    {
        GameState state = NewStateAtSettlement(1);
        string block = SettlementCampService.AdmissionBlockReason(state, SettlementCampDevFixture.CreatureId);
        Assert.AreEqual(SettlementCampDevFixture.CreatureAdmissionBlock, block);
        Assert.IsFalse(SettlementCampService.Enter(state, new[] { SettlementCampDevFixture.CreatureId }, out string reason));
        StringAssert.Contains("не допускается", reason);
        Assert.IsFalse(SettlementCampService.HasCamp(state), "Неудачная попытка не разбивает лагерь.");

        // Дорожный бой — без лагеря: существо в бою, шаблон из каталога существ.
        CampaignBattleRequest road = CampaignBattleBridge.CreateRequest(state, "test.road.battle");
        CampaignBattleParticipant creature = road.Participants.Single(p => p.PersonId == SettlementCampDevFixture.CreatureId);
        Assert.AreEqual(CampaignParticipantOrigin.CreatureAlly, creature.Origin);
        Assert.AreEqual(SettlementCampDevFixture.CreatureUnitTypeId, creature.UnitTypeId);

        // Авторское исключение снимает запрет.
        HomePeopleService.Find(state, SettlementCampDevFixture.CreatureId).SettlementAdmissionExceptionFlag = "test.allowed";
        state.Narrative.SetFlag("test.allowed");
        Assert.IsEmpty(SettlementCampService.AdmissionBlockReason(state, SettlementCampDevFixture.CreatureId));
    }

    [Test]
    public void ReturnToCamp_AllTogether_GroupCanChange_GatherEndsTheSplit()
    {
        GameState state = NewStateAtSettlement(2);
        string first = state.ActiveExpedition.FighterIds[0];
        string second = state.ActiveExpedition.FighterIds[1];
        Assert.IsTrue(SettlementCampService.Enter(state, new[] { first }, out _));
        state.Narrative.AddKnowledge("test.knowledge.from.ostafiy");

        SettlementCampService.ReturnToCamp(state);
        Assert.IsTrue(PartyPresence.IsPresent(state, second), "У лагеря все снова рядом.");
        Assert.IsTrue(CampRest.CanRest(state, out string restReason), restReason);

        Assert.IsTrue(SettlementCampService.Enter(state, new[] { second }, out _), "Группу меняют у лагеря.");
        Assert.IsFalse(PartyPresence.IsPresent(state, first));
        Assert.IsFalse(CampRest.CanRest(state, out _), "Ночлег — у лагеря, не порознь.");
        Assert.IsTrue(state.Narrative.HasKnowledge("test.knowledge.from.ostafiy"), "Переданное знание не исчезает с источником.");

        SettlementCampService.ReturnToCamp(state);
        List<string> gathered = SettlementCampService.Gather(state);
        Assert.IsNull(state.SettlementCamp);
        Assert.IsTrue(gathered.Count > 0);
        CollectionAssert.AreEquivalent(PartyPresence.ExpeditionIds(state), PartyPresence.PresentIds(state), "Все живые вместе.");
    }

    [Test]
    public void Normalize_DropsDeadAndUnknown_AndLeavingTakesEveryone()
    {
        GameState state = NewStateAtSettlement(2);
        string fallen = state.ActiveExpedition.FighterIds[1];
        Assert.IsTrue(SettlementCampService.Enter(state, new string[0], out _));
        state.SettlementCamp.WaitingIds.Add("nobody.like.this");
        HomePeopleService.MarkDead(state, fallen, "test");
        int residents = state.Residents().Count();

        List<string> reports = SettlementCampService.Normalize(state);
        CollectionAssert.DoesNotContain(state.SettlementCamp.WaitingIds, "nobody.like.this");
        CollectionAssert.DoesNotContain(state.SettlementCamp.WaitingIds, fallen, "Погибший не ждёт в лагере.");
        Assert.IsTrue(reports.Count > 0);
        Assert.AreEqual(residents, state.Residents().Count(), "Новых людей не создано.");

        state.ActiveExpedition.Phase = CommanderState.TravellingToLocation;
        reports = SettlementCampService.Normalize(state);
        Assert.IsNull(state.SettlementCamp, "Ушли от поселения — забытых у стоянки нет.");
        StringAssert.Contains("выступил вместе", reports.Single());
    }

    [Test]
    public void SaveRoundTrip_KeepsTheSplit()
    {
        GameState state = NewStateAtSettlement(2);
        string inside = state.ActiveExpedition.FighterIds[0];
        Assert.IsTrue(SettlementCampService.Enter(state, new[] { inside }, out _));
        List<string> waiting = state.SettlementCamp.WaitingIds.ToList();

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        Assert.IsTrue(data.HasSettlementCamp);
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data)));

        Assert.IsTrue(SettlementCampService.IsCommanderInside(restored));
        CollectionAssert.AreEqual(waiting, restored.SettlementCamp.WaitingIds);
        Assert.AreEqual(SettlementCampDevFixture.SettlementId, restored.SettlementCamp.SettlementLocationId);
        Assert.IsTrue(restored.FindLocation(SettlementCampDevFixture.SettlementId).IsSettlement);
        Assert.AreEqual(CampaignParticipantOrigin.CreatureAlly,
            HomePeopleService.Find(restored, SettlementCampDevFixture.CreatureId).Origin);
    }
}

internal static class SettlementCampTestExtensions
{
    public static IEnumerable<ResidentState> Residents(this GameState state) => state.People.Residents;
}
