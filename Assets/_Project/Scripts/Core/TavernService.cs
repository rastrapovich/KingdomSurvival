using System;
using System.Collections.Generic;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §11–§12): корчма. Люди и их разговоры —
// существующие диалоги; здесь — то, что требует денег и проверок: платные
// сведения (оплата и знание — одной операцией, повторно не продаются),
// принятие поручения и оплаченный ночлег поверх общего ночлега лагеря.
public sealed class TavernOfferView
{
    public TavernOfferDefinition Offer;
    public bool Enabled;
    public string Reason = string.Empty;
    public string SourceName = string.Empty;
}

public static class TavernService
{
    public static TavernDefinition Tavern(GameState state, string locationId)
    {
        TavernDefinition tavern = SettlementService.Profile(state, locationId)?.Tavern;
        return tavern != null && tavern.Enabled ? tavern : null;
    }

    private static TavernOfferStateData OfferState(GameState state, string locationId, string offerId, bool create)
    {
        SettlementInstanceData instance = SettlementService.FindInstance(state, locationId);
        if (instance == null)
            return null;
        TavernOfferStateData record = instance.Offers.Find(o => o != null && o.OfferId == offerId);
        if (record == null && create)
        {
            record = new TavernOfferStateData { OfferId = offerId };
            instance.Offers.Add(record);
        }
        return record;
    }

    // Видимые предложения: условия выполнены; однократное взятое — скрыто.
    public static List<TavernOfferView> Offers(GameState state, string locationId, string personId = null)
    {
        List<TavernOfferView> result = new List<TavernOfferView>();
        TavernDefinition tavern = Tavern(state, locationId);
        SettlementProfileDefinition profile = SettlementService.Profile(state, locationId);
        if (tavern?.Offers == null)
            return result;
        NarrativeEvaluationContext context = SettlementService.Context(state);
        foreach (TavernOfferDefinition offer in tavern.Offers)
        {
            if (offer == null || string.IsNullOrEmpty(offer.Id))
                continue;
            if (personId != null && offer.PersonId != personId)
                continue;
            if (!(offer.Conditions?.Evaluate(context) ?? true))
                continue;
            TavernOfferStateData record = OfferState(state, locationId, offer.Id, false);
            if (offer.Once && record != null && record.TimesTaken > 0)
                continue;
            TavernOfferView view = new TavernOfferView { Offer = offer };
            SettlementPersonDefinition person = profile?.FindPerson(offer.PersonId);
            view.SourceName = person != null ? person.DisplayName : string.Empty;
            view.Enabled = CanTake(state, locationId, offer, out view.Reason);
            result.Add(view);
        }
        return result;
    }

    public static bool CanTake(GameState state, string locationId, TavernOfferDefinition offer, out string reason)
    {
        if (!SettlementService.CanActInside(state, locationId, out reason))
            return false;
        if (offer == null)
        {
            reason = "Такого предложения нет.";
            return false;
        }
        TavernOfferStateData record = OfferState(state, locationId, offer.Id, false);
        if (record != null && record.TimesTaken > 0)
        {
            if (offer.Once)
            {
                reason = "Уже договорились.";
                return false;
            }
            double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
            if (offer.CooldownHours > 0 && now < record.LastTakenHour + offer.CooldownHours)
            {
                reason = "Не сейчас — позже.";
                return false;
            }
        }
        if (!string.IsNullOrEmpty(offer.KnowledgeId) && state.Narrative != null && state.Narrative.HasKnowledge(offer.KnowledgeId))
        {
            reason = "Это вы уже знаете.";
            return false;
        }
        if (!string.IsNullOrEmpty(offer.ErrandId))
        {
            ErrandDefinition errand = SettlementCatalog.Current.FindErrand(offer.ErrandId);
            ErrandStatus status = ErrandService.Status(state, errand);
            if (errand == null)
            {
                reason = "Поручение не найдено.";
                return false;
            }
            if (status != ErrandStatus.NotTaken)
            {
                reason = "Поручение уже " + ErrandService.StatusLabel(status) + ".";
                return false;
            }
        }
        if (offer.Price > 0 && state.ArmyGold < offer.Price)
        {
            reason = "Не хватает " + (offer.Price - state.ArmyGold) + " зол.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    // Оплатить и получить: всё проверено до списания, затем знание, флаги,
    // поручение и запись источника — разом. dialogueId — сцена рассказа.
    public static bool TryTakeOffer(GameState state, string locationId, string offerId, out string message, out string dialogueId)
    {
        dialogueId = string.Empty;
        TavernDefinition tavern = Tavern(state, locationId);
        TavernOfferDefinition offer = tavern?.Offers?.Find(o => o != null && o.Id == offerId);
        if (offer == null)
        {
            message = "Такого предложения нет.";
            return false;
        }
        if (!SettlementService.Check(state, offer.Conditions))
        {
            message = "Сейчас это не предлагают.";
            return false;
        }
        if (!CanTake(state, locationId, offer, out message))
            return false;

        SettlementProfileDefinition profile = SettlementService.Profile(state, locationId);
        LocationData location = state.FindLocation(locationId);
        SettlementPersonDefinition person = profile?.FindPerson(offer.PersonId);
        string source = (person != null ? person.DisplayName : "человек в корчме") +
                        (location != null ? ", " + location.Name : string.Empty);

        state.ArmyGold -= Math.Max(0, offer.Price);
        TavernOfferStateData record = OfferState(state, locationId, offer.Id, true);
        record.TimesTaken++;
        record.LastTakenHour = ContinuousSimulationSystem.GetAbsoluteHours(state);
        state.Narrative ??= new NarrativeStateData();

        List<string> parts = new List<string>();
        if (!string.IsNullOrEmpty(offer.KnowledgeId))
        {
            CommanderData commander = state.GetSelectedCommander();
            if (commander != null)
            {
                commander.HeroProfile ??= new HeroProfileData();
                new NarrativeEffect
                {
                    EffectExecutionId = "tavern.offer." + locationId + "." + offer.Id,
                    Type = NarrativeEffectType.AddKnowledge,
                    StringParam = offer.KnowledgeId
                }.Apply(new NarrativeEvaluationContext(commander.HeroProfile, state.Narrative, gameState: state));
            }
            state.Narrative.AddKnowledge(offer.KnowledgeId);
            // Что сказал, кто, где и когда — одна запись Хроники на знание.
            Chronicle.Record(state, "knowledge.source." + offer.KnowledgeId, "Сведения: " + offer.Label,
                (string.IsNullOrWhiteSpace(offer.KnowledgeText) ? offer.Description : offer.KnowledgeText) + " (" + source + ")",
                null);
            parts.Add(string.IsNullOrWhiteSpace(offer.KnowledgeText) ? "новые сведения" : offer.KnowledgeText);
        }
        if (!string.IsNullOrEmpty(offer.StageFlagId))
            state.Narrative.SetFlag(offer.StageFlagId);
        if (!string.IsNullOrEmpty(offer.SetFlagId))
            state.Narrative.SetFlag(offer.SetFlagId);
        if (!string.IsNullOrEmpty(offer.ErrandId) && ErrandService.TryAccept(state, offer.ErrandId, source, out string errandMessage))
            parts.Add(errandMessage);

        dialogueId = offer.DialogueId ?? string.Empty;
        message = offer.Label + (offer.Price > 0 ? " (−" + offer.Price + " зол.)" : string.Empty) +
                  (parts.Count > 0 ? ": " + string.Join(" ", parts) : ".");
        return true;
    }

    // ------------------------------------------------------------------
    // Ночлег
    // ------------------------------------------------------------------

    public static bool IsLodging(GameState state)
    {
        return state?.Settlements != null && !string.IsNullOrEmpty(state.Settlements.LodgingLocationId) &&
               CampRest.IsResting(state);
    }

    public static bool CanLodge(GameState state, string locationId, out string reason)
    {
        TavernDefinition tavern = Tavern(state, locationId);
        if (tavern?.Lodging == null || !tavern.Lodging.Enabled)
        {
            reason = "Здесь не ночуют.";
            return false;
        }
        if (!SettlementService.CanActInside(state, locationId, out reason))
            return false;
        if (tavern.Lodging.Price > 0 && state.ArmyGold < tavern.Lodging.Price)
        {
            reason = "Не хватает " + (tavern.Lodging.Price - state.ArmyGold) + " зол.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    // Что даст ночлег — показывается до оплаты.
    public static string DescribeLodging(GameState state, string locationId)
    {
        TavernDefinition tavern = Tavern(state, locationId);
        TavernLodgingDefinition lodging = tavern?.Lodging;
        if (lodging == null || !lodging.Enabled)
            return string.Empty;
        List<string> parts = new List<string>
        {
            ContinuousExpeditionCommands.FormatHours(lodging.Hours),
            lodging.Price > 0 ? lodging.Price + " зол." : "бесплатно",
            "вошедшие отоспятся" + (lodging.HealHitPoints > 0 ? " и вернут до " + lodging.HealHitPoints + " HP" : string.Empty)
        };
        if (SettlementCampService.HasCamp(state))
            parts.Add("оставшиеся в лагере спят у костра");
        parts.Add("тяжёлые раны так не лечат; прерванный ночлег не возвращают");
        return string.Join(" · ", parts);
    }

    // Оплатить один раз и встать на ночлег: та же остановка, что у лагеря
    // (время, припасы, обязательные события идут общим порядком).
    public static bool TryStartLodging(GameState state, string locationId, out string message)
    {
        if (!CanLodge(state, locationId, out message))
            return false;
        TavernDefinition tavern = Tavern(state, locationId);
        if (!CampRest.TryStartRestUnchecked(state, tavern.Lodging.Hours, "НОЧЛЕГ В КОРЧМЕ", out message))
            return false;

        state.ArmyGold -= Math.Max(0, tavern.Lodging.Price);
        SettlementsStateData data = SettlementService.EnsureState(state);
        data.LodgingLocationId = locationId;
        data.LodgingName = tavern.Name ?? string.Empty;
        data.LodgingHealHitPoints = Math.Max(0, tavern.Lodging.HealHitPoints);
        data.LodgingServedIds = PartyPresence.PresentIds(state);
        message = "Ночлег в «" + tavern.Name + "» на " + ContinuousExpeditionCommands.FormatHours(tavern.Lodging.Hours) +
                  (tavern.Lodging.Price > 0 ? " (−" + tavern.Lodging.Price + " зол.)." : ".");
        return true;
    }

    // Утро ночлега в корчме (вызывает CampRest.CompleteRest). Улучшенный
    // отдых — только тем, кто был внутри.
    public static string CompleteLodging(GameState state)
    {
        SettlementsStateData data = state?.Settlements;
        if (data == null || string.IsNullOrEmpty(data.LodgingLocationId))
            return null;
        List<string> healed = new List<string>();
        // Отряд ушёл от корчмы (ночлег прерван и начат заново в другом
        // месте) — улучшенного отдыха нет, оплата не возвращается.
        bool stillThere = state.HasActiveExpedition && state.ActiveExpedition.LocationId == data.LodgingLocationId;
        if (stillThere && data.LodgingHealHitPoints > 0)
        {
            foreach (string id in data.LodgingServedIds)
            {
                ResidentState resident = HomePeopleService.Find(state, id);
                if (resident == null || !resident.IsAlive || !resident.HasCombatState ||
                    resident.CurrentHitPoints >= resident.MaxHitPoints)
                    continue;
                int gain = Math.Min(data.LodgingHealHitPoints, resident.MaxHitPoints - resident.CurrentHitPoints);
                HomePeopleService.SetHitPoints(resident, resident.CurrentHitPoints + gain);
                healed.Add(resident.DisplayName + " +" + gain + " HP");
            }
        }
        string name = data.LodgingName;
        data.LodgingLocationId = string.Empty;
        data.LodgingName = string.Empty;
        data.LodgingHealHitPoints = 0;
        data.LodgingServedIds = new List<string>();
        return "ночь под крышей «" + name + "»" + (healed.Count > 0 ? ": " + string.Join(", ", healed) : string.Empty);
    }
}
