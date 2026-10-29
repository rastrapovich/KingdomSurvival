using System;
using System.Collections.Generic;

// Бриф правок (22 пункта) пп. 16, 21, 22: одно поручение — одно поселение
// и один заказчик на всю партию. Профиль поселения может повторяться у
// нескольких деревень карты; без привязки «Нож деда» висел на досках всех
// торговых сёл, а Ждан в соседнем селе принимал нож, взятый в другом.
// При первом входе в поселение его поручения (по устойчивому Id поручения,
// а не по названию) закрепляются за ним вместе с заказчиком и источником.
// Дальше в других поселениях нет ни объявления, ни предложения корчмы, ни
// самого заказчика, а итог поручения (флаг выполнения и награда из того же
// выбора диалога) применяется только в поселении выдачи. Привязка живёт в
// сохранении; новая партия начинает свою историю выдачи.
[Serializable]
public sealed class ErrandClaimData
{
    public string ErrandId = string.Empty;
    // Пусто — привязку старого сохранения восстановить однозначно нельзя:
    // поручение ведёт себя по-старому (сдать можно в любом поселении).
    public string LocationId = string.Empty;
    public string GiverPersonId = string.Empty;
    public string Source = string.Empty;
}

public static class ErrandClaims
{
    // Источники поручений профиля: предложения корчмы и объявления доски,
    // у которых указано поручение; заказчик — предлагающий или контакт.
    public static List<(string ErrandId, string GiverPersonId, string Source)> Sources(SettlementProfileDefinition profile)
    {
        List<(string, string, string)> result = new List<(string, string, string)>();
        if (profile == null)
            return result;
        if (profile.Tavern?.Offers != null)
        {
            foreach (TavernOfferDefinition offer in profile.Tavern.Offers)
            {
                if (offer != null && !string.IsNullOrEmpty(offer.ErrandId))
                    result.Add((offer.ErrandId, offer.PersonId ?? string.Empty, "корчма"));
            }
        }
        if (profile.Board?.Notices != null)
        {
            foreach (NoticeDefinition notice in profile.Board.Notices)
            {
                if (notice != null && !string.IsNullOrEmpty(notice.ErrandId))
                    result.Add((notice.ErrandId, notice.ContactPersonId ?? string.Empty, "доска"));
            }
        }
        return result;
    }

    public static ErrandClaimData Find(GameState state, string errandId)
    {
        if (state?.Settlements?.ErrandClaims == null || string.IsNullOrEmpty(errandId))
            return null;
        return state.Settlements.ErrandClaims.Find(c => c != null && c.ErrandId == errandId);
    }

    // Вход в поселение: его ещё ничьи поручения закрепляются за ним. Уже
    // взятое по старому сохранению без привязки закрепляется, только если
    // поселение с таким поручением на карте одно, — иначе привязка пустая.
    public static void ClaimForSettlement(GameState state, string locationId)
    {
        SettlementProfileDefinition profile = SettlementService.Profile(state, locationId);
        if (profile == null)
            return;
        SettlementService.EnsureState(state);
        foreach ((string errandId, string giver, string source) in Sources(profile))
        {
            if (Find(state, errandId) != null || SettlementCatalog.Current.FindErrand(errandId) == null)
                continue;
            ErrandStatus status = ErrandService.Status(state, SettlementCatalog.Current.FindErrand(errandId));
            string claimed = locationId;
            if (status != ErrandStatus.NotTaken && CountSettlementsOffering(state, errandId) > 1)
                claimed = string.Empty;
            state.Settlements.ErrandClaims.Add(new ErrandClaimData
            {
                ErrandId = errandId,
                LocationId = claimed,
                GiverPersonId = giver,
                Source = source
            });
        }
    }

    private static int CountSettlementsOffering(GameState state, string errandId)
    {
        int count = 0;
        foreach (SettlementInstanceData instance in state.Settlements.Instances)
        {
            SettlementProfileDefinition profile = SettlementCatalog.Current.FindProfile(instance?.ProfileId);
            if (Sources(profile).Exists(s => s.ErrandId == errandId))
                count++;
        }
        return count;
    }

    // Источник этого поручения в этом поселении можно показывать.
    public static bool AvailableAt(GameState state, string errandId, string locationId)
    {
        ErrandClaimData claim = Find(state, errandId);
        return claim == null || string.IsNullOrEmpty(claim.LocationId) || claim.LocationId == locationId;
    }

    // Заказчик закреплённого за другим поселением поручения здесь — лишь
    // копия того же шаблона: его в этом поселении нет.
    public static bool IsGiverElsewhere(GameState state, string locationId, string personId)
    {
        if (state?.Settlements?.ErrandClaims == null || string.IsNullOrEmpty(personId))
            return false;
        List<(string ErrandId, string GiverPersonId, string Source)> sources = Sources(SettlementService.Profile(state, locationId));
        foreach (ErrandClaimData claim in state.Settlements.ErrandClaims)
        {
            if (claim == null || claim.GiverPersonId != personId || string.IsNullOrEmpty(claim.LocationId) || claim.LocationId == locationId)
                continue;
            if (sources.Exists(s => s.ErrandId == claim.ErrandId && s.GiverPersonId == personId))
                return true;
        }
        return false;
    }

    // Итог поручения — только у заказчика в поселении выдачи (командир
    // внутри него). Проверяется в ядре при применении эффектов выбора, так
    // что прямой вызов чужого диалога её не обходит.
    public static bool CanCompleteHere(GameState state, ErrandDefinition errand)
    {
        ErrandClaimData claim = Find(state, errand?.Id);
        if (claim == null || string.IsNullOrEmpty(claim.LocationId))
            return true;
        return SettlementCampService.InsideLocationId(state) == claim.LocationId;
    }

    // Пакет эффектов выбора, который завершил бы поручение не там, где его
    // выдали, не применяется целиком: ни флага, ни награды, ни списания вещи.
    public static bool BlocksEffects(GameState state, IEnumerable<NarrativeEffect> effects)
    {
        if (state == null || effects == null)
            return false;
        foreach (NarrativeEffect effect in effects)
        {
            if (effect == null || effect.Type != NarrativeEffectType.SetFlag || string.IsNullOrEmpty(effect.StringParam))
                continue;
            foreach (ErrandDefinition errand in SettlementCatalog.Current.Errands)
            {
                if (errand != null && errand.CompletedFlag == effect.StringParam && !CanCompleteHere(state, errand))
                    return true;
            }
        }
        return false;
    }

    // «Куда вернуться»: заказчик и поселение выдачи словами.
    public static string DescribeReturn(GameState state, ErrandDefinition errand)
    {
        ErrandClaimData claim = Find(state, errand?.Id);
        if (claim == null || string.IsNullOrEmpty(claim.LocationId))
            return string.Empty;
        LocationData location = state.FindLocation(claim.LocationId);
        SettlementPersonDefinition giver = SettlementService.Profile(state, claim.LocationId)?.FindPerson(claim.GiverPersonId);
        string who = giver != null ? giver.DisplayName : errand.GiverName;
        string where = location != null ? location.Name : "поселение выдачи";
        return string.IsNullOrWhiteSpace(who) ? "Вернуться: " + where + "." : "Вернуться: " + who + ", " + where + ".";
    }
}
