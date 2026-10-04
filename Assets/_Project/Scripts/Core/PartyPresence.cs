using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.9): кто физически находится там, где сейчас
// командир. Три разных состава:
//   1. походный — все, кто ушёл из Дома (герой, бойцы, свита);
//   2. присутствующие — кто здесь, рядом с командиром;
//   3. кандидаты боя — присутствующие, живые и боеспособные бойцы.
// Это единственный источник присутствия для условий сцены, особенностей и
// боя. Без разделения отряда (лагеря у поселения) присутствуют все живые
// участники похода — прежнее поведение.
public static class PartyPresence
{
    public const string ReasonInCamp = "В лагере";
    public const string ReasonWounded = "Ранен — не сражается";
    public const string ReasonRetinue = "Свита — не сражается";
    public const string ReasonDead = "Погиб";

    // Весь походный состав (порядок: герой, бойцы, свита).
    public static List<string> ExpeditionIds(GameState state)
    {
        return state != null ? CampRest.PartyIds(state) : new List<string>();
    }

    public static List<string> PresentIds(GameState state)
    {
        List<string> present = new List<string>();
        foreach (string personId in ExpeditionIds(state))
        {
            if (IsAlive(state, personId) && !IsWaitingInCamp(state, personId) && !present.Contains(personId))
                present.Add(personId);
        }
        return present;
    }

    public static bool IsPresent(GameState state, string personId)
    {
        return !string.IsNullOrEmpty(personId) && PresentIds(state).Contains(personId);
    }

    // Присутствующие без командира — для «спутник рядом» и размера группы.
    public static List<string> PresentCompanionIds(GameState state)
    {
        List<string> ids = PresentIds(state);
        CommanderData hero = state?.GetSelectedCommander();
        if (hero != null)
            ids.Remove(hero.Id);
        return ids;
    }

    // Кто может вступить в бой здесь: командир и присутствующие бойцы, живые
    // и не тяжелораненые. Свита в бой не вступает.
    public static List<string> BattleCandidateIds(GameState state)
    {
        List<string> ids = new List<string>();
        if (state == null)
            return ids;
        CommanderData hero = state.GetSelectedCommander();
        if (hero != null && IsAlive(state, hero.Id))
            ids.Add(hero.Id);
        if (!state.HasActiveExpedition || state.ActiveExpedition.FighterIds == null)
            return ids;
        foreach (string fighterId in state.ActiveExpedition.FighterIds)
        {
            if (string.IsNullOrEmpty(BattleExclusionReason(state, fighterId)) && !ids.Contains(fighterId))
                ids.Add(fighterId);
        }
        return ids;
    }

    // Почему этот участник похода не вступит в бой здесь; пусто — вступит.
    public static string BattleExclusionReason(GameState state, string personId)
    {
        if (!IsAlive(state, personId))
            return ReasonDead;
        if (IsWaitingInCamp(state, personId))
            return ReasonInCamp;
        if (state.HasActiveExpedition && state.ActiveExpedition.RetinueIds != null &&
            state.ActiveExpedition.RetinueIds.Contains(personId))
            return ReasonRetinue;
        // ПР-10: тяжелораненый идёт с отрядом, но в бой не вступает.
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident != null && resident.Injury == ResidentInjury.Recovering)
            return ReasonWounded;
        return string.Empty;
    }

    // Оставлен ждать в лагере у поселения, пока командир внутри.
    public static bool IsWaitingInCamp(GameState state, string personId)
    {
        return SettlementCampService.IsWaiting(state, personId);
    }

    private static bool IsAlive(GameState state, string personId)
    {
        if (state == null || string.IsNullOrEmpty(personId))
            return false;
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident == null || resident.IsAlive;
    }
}
