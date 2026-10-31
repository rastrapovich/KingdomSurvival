using System.Collections.Generic;

// Решение пользователя 31.10.2026 (аудит A-02, канон §26.4: лечение связано с
// местом, временем, человеком или ресурсом). Тяжёлая рана (ResidentInjury.
// Recovering) заживает за несколько ночей без боя:
//   • ночлег в лагере — 1 очко;
//   • ночь под крышей в поселении (оплаченный ночлег, человек внутри) — 2;
//   • лекарь в отряде на этой ночи — ещё +1;
//   • знахарь в поселении снимает рану сразу за плату (SettlementActionType.Healer).
// Числа — рабочие значения [РАБОЧЕЕ], не утверждённый баланс.
public static class WoundRecovery
{
    public const int PointsToHeal = 3;
    public const int CampNightPoints = 1;
    public const int RoofNightPoints = 2;
    public const int HealerBonusPoints = 1;
    // Лекарь в отряде: Марта (функция перевязки) или любой присутствующий с
    // компетенцией «Врачевание» не ниже этого ранга.
    public const int HealerCompetencyRank = 2;
    public const string HealingCompetencyId = "healing";

    public static bool IsHeavilyWounded(ResidentState resident) =>
        resident != null && resident.IsAlive && resident.Injury == ResidentInjury.Recovering;

    // Новая тяжёлая рана — счёт заживления с нуля.
    public static void Begin(ResidentState resident)
    {
        if (resident == null)
            return;
        resident.Injury = ResidentInjury.Recovering;
        resident.WoundRecoveryPoints = 0;
    }

    public static void Heal(ResidentState resident)
    {
        if (resident == null)
            return;
        resident.Injury = ResidentInjury.None;
        resident.WoundRecoveryPoints = 0;
    }

    // Сколько ночлегов в лагере ещё нужно (для подсказки).
    public static int NightsLeft(ResidentState resident)
    {
        if (!IsHeavilyWounded(resident))
            return 0;
        int missing = PointsToHeal - resident.WoundRecoveryPoints;
        return missing <= 0 ? 0 : (missing + CampNightPoints - 1) / CampNightPoints;
    }

    public static string Describe(ResidentState resident)
    {
        int nights = NightsLeft(resident);
        return "Тяжёлая рана — не сражается. Заживёт за " + nights + " " + NightWord(nights) +
               " без боя; ночь под крышей — за две, лекарь в отряде ускоряет, знахарь в деревне лечит сразу.";
    }

    public static string DescribeShort(ResidentState resident)
    {
        int nights = NightsLeft(resident);
        return "Тяжёлая рана — ещё " + nights + " " + NightWord(nights);
    }

    // Есть ли этой ночью рядом лекарь (кто-то из присутствующих).
    public static string FindHealer(GameState state, IEnumerable<string> presentIds)
    {
        if (state == null || presentIds == null)
            return null;
        foreach (string id in presentIds)
        {
            ResidentState resident = PeopleService.Find(state, id);
            if (resident == null || !resident.IsAlive)
                continue;
            if (id == CampRest.MartaId ||
                CharacterProgressionService.GetCompetencyRank(state, id, HealingCompetencyId) >= HealerCompetencyRank)
                return id;
        }
        return null;
    }

    // Утро ночлега: каждому тяжелораненому из похода — очки заживления.
    // roofIds — кто ночевал под крышей поселения (оплаченный ночлег).
    public static void ApplyNight(GameState state, ICollection<string> roofIds, List<string> results)
    {
        if (state == null || !state.HasActiveExpedition)
            return;
        List<string> present = PartyPresence.PresentIds(state);
        string healer = FindHealer(state, present);
        foreach (string id in CampRest.PartyIds(state))
        {
            ResidentState resident = PeopleService.Find(state, id);
            if (!IsHeavilyWounded(resident))
                continue;
            bool underRoof = roofIds != null && roofIds.Contains(id);
            int points = underRoof ? RoofNightPoints : CampNightPoints;
            // Лекарь помогает тем, кто ночевал рядом с ним.
            if (healer != null && healer != id && present.Contains(id))
                points += HealerBonusPoints;
            resident.WoundRecoveryPoints += points;
            if (resident.WoundRecoveryPoints >= PointsToHeal)
            {
                Heal(resident);
                results?.Add(resident.DisplayName + " оправился от тяжёлой раны");
            }
            else
            {
                results?.Add(resident.DisplayName + ": рана заживает, ещё " + NightsLeft(resident) + " " + NightWord(NightsLeft(resident)));
            }
        }
    }

    // Знахарь в поселении: снимает тяжёлые раны у присутствующих за плату с
    // каждого. Не хватает денег на всех — не лечит никого (сделка целиком).
    public static List<ResidentState> WoundedPresent(GameState state)
    {
        List<ResidentState> wounded = new List<ResidentState>();
        foreach (string id in PartyPresence.PresentIds(state))
        {
            ResidentState resident = PeopleService.Find(state, id);
            if (IsHeavilyWounded(resident))
                wounded.Add(resident);
        }
        return wounded;
    }

    public static bool CanVisitHealer(GameState state, int pricePerPerson, out string reason)
    {
        reason = string.Empty;
        if (state == null || !state.HasActiveExpedition)
        {
            reason = "Знахарь лечит отряд в походе.";
            return false;
        }
        List<ResidentState> wounded = WoundedPresent(state);
        if (wounded.Count == 0)
        {
            reason = "Тяжелораненых рядом нет.";
            return false;
        }
        int total = wounded.Count * System.Math.Max(0, pricePerPerson);
        if (state.ArmyGold < total)
        {
            reason = "Не хватает " + (total - state.ArmyGold) + " зол.: лечение " + total + " зол.";
            return false;
        }
        return true;
    }

    public static bool TryVisitHealer(GameState state, int pricePerPerson, out string message)
    {
        if (!CanVisitHealer(state, pricePerPerson, out message))
            return false;
        List<ResidentState> wounded = WoundedPresent(state);
        int total = wounded.Count * System.Math.Max(0, pricePerPerson);
        state.ArmyGold -= total;
        List<string> names = new List<string>();
        foreach (ResidentState resident in wounded)
        {
            Heal(resident);
            names.Add(resident.DisplayName);
        }
        message = "Знахарка вправила и перевязала: " + string.Join(", ", names) + (total > 0 ? " (−" + total + " зол.)." : ".");
        return true;
    }

    private static string NightWord(int count)
    {
        int lastTwo = count % 100;
        if (lastTwo >= 11 && lastTwo <= 14)
            return "ночлегов";
        switch (count % 10)
        {
            case 1: return "ночлег";
            case 2:
            case 3:
            case 4: return "ночлега";
            default: return "ночлегов";
        }
    }
}
