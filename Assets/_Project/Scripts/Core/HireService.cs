using System;
using System.Collections.Generic;

// Бриф правок (19 пунктов) пп. 10, 18: наёмники корчмы. В корчме поселения
// иногда сидят один-два человека, готовых пойти с отрядом (бывает, что
// никого). Кандидаты заводятся один раз на экземпляр поселения и живут в
// сохранении — повторный вход не плодит копий, нанятый больше не предлагается.
// Найм — через разговор: до согласия видны роль, польза и личная плата в
// монетах за игровые сутки; плата хранится в договоре нанятого. Нанятый —
// тот же человек модели (FighterData + ResidentState, внешний участник) и
// занимает свободное место бойца. Плата списывается раз в игровые сутки
// (в полночь) — ни реальная пауза, ни перерисовка, ни темп ×2/×3 её не
// меняют. Рабочие решения (не утверждены): первая плата — в первую полночь
// после найма; не хватает золота — плата не списывается, пишется донесение,
// без долгов и ухода. Имена, роли и суммы — [РАБОЧЕЕ].
[Serializable]
public sealed class HireCandidateData
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string UnitTypeId = string.Empty;
    public string RoleLabel = string.Empty;
    public string Benefit = string.Empty;
    public int DailyWage;
    public bool Hired;
}

[Serializable]
public sealed class HireContractData
{
    public string PersonId = string.Empty;
    public string LocationId = string.Empty;
    public int DailyWage;
    public int LastPaidDay;
}

[Serializable]
public sealed class HireTavernData
{
    public string LocationId = string.Empty;
    public List<HireCandidateData> Candidates = new List<HireCandidateData>();
}

public static class HireService
{
    // Рабочие шаблоны кандидатов: существующие типы бойцов Базы существ.
    private static readonly (string UnitTypeId, string Role, string Benefit, int Wage)[] Templates =
    {
        ("guard", "щитник", "держит строй и прикрывает слабых", 5),
        ("spearman", "копейщик", "встречает зверя на длину древка", 4),
        ("archer", "лучник", "бьёт издали, пока враг не подошёл", 5),
        ("scout", "следопыт", "читает следы и находит тропы", 3)
    };

    private static readonly string[] Names = { "Радим", "Милош", "Тихомир", "Вышеслав", "Будимир", "Ярополк", "Любомир", "Станимир" };

    // Шанс, что в корчме вообще кто-то есть, и что их двое (рабочие).
    public const int PresentPercent = 60;
    public const int SecondPercent = 35;

    public static List<HireCandidateData> Candidates(GameState state, string locationId)
    {
        HireTavernData tavern = EnsureTavern(state, locationId);
        List<HireCandidateData> free = new List<HireCandidateData>();
        if (tavern == null)
            return free;
        foreach (HireCandidateData candidate in tavern.Candidates)
        {
            if (candidate != null && !candidate.Hired)
                free.Add(candidate);
        }
        return free;
    }

    private static HireTavernData EnsureTavern(GameState state, string locationId)
    {
        if (state == null || string.IsNullOrEmpty(locationId) || TavernService.Tavern(state, locationId) == null)
            return null;
        SettlementService.EnsureState(state);
        HireTavernData tavern = state.Settlements.HireTaverns.Find(t => t != null && t.LocationId == locationId);
        if (tavern != null)
            return tavern;
        tavern = new HireTavernData { LocationId = locationId };
        Random random = new Random(unchecked(state.WorldSeed * 31 + StableHash(locationId)));
        int count = random.Next(100) < PresentPercent ? (random.Next(100) < SecondPercent ? 2 : 1) : 0;
        for (int i = 0; i < count; i++)
        {
            (string unitTypeId, string role, string benefit, int wage) = Templates[random.Next(Templates.Length)];
            string name = Names[random.Next(Names.Length)];
            tavern.Candidates.Add(new HireCandidateData
            {
                Id = "hire." + locationId + "." + i,
                Name = name,
                UnitTypeId = unitTypeId,
                RoleLabel = role,
                Benefit = benefit,
                DailyWage = wage
            });
        }
        state.Settlements.HireTaverns.Add(tavern);
        return tavern;
    }

    public static HireContractData Contract(GameState state, string personId)
    {
        if (state?.Settlements?.HireContracts == null || string.IsNullOrEmpty(personId))
            return null;
        return state.Settlements.HireContracts.Find(c => c != null && c.PersonId == personId);
    }

    public static bool CanHire(GameState state, string locationId, string candidateId, out string reason)
    {
        HireCandidateData candidate = Candidates(state, locationId).Find(c => c.Id == candidateId);
        if (candidate == null)
        {
            reason = "Этого человека здесь уже нет.";
            return false;
        }
        if (!state.HasActiveExpedition)
        {
            reason = "Нанимают в походе.";
            return false;
        }
        if (state.ActiveExpedition.FighterIds.Count >= GameState.ExpeditionFighterSlots)
        {
            reason = "В отряде нет свободного места.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public static bool TryHire(GameState state, string locationId, string candidateId, out string message)
    {
        if (!CanHire(state, locationId, candidateId, out message))
            return false;
        HireCandidateData candidate = Candidates(state, locationId).Find(c => c.Id == candidateId);
        string personId = candidate.Id;
        if (state.FindFighter(personId) == null)
            state.Fighters.Add(new FighterData(personId, candidate.Name, "Наёмник", 1, 0, candidate.UnitTypeId));
        if (PeopleService.Find(state, personId) == null)
        {
            ResidentState resident = new ResidentState
            {
                PersonId = personId,
                DisplayName = candidate.Name,
                RoleLabel = "наёмник, " + candidate.RoleLabel,
                ShortDescription = candidate.Benefit,
                Membership = ResidentMembership.External,
                Origin = CampaignParticipantOrigin.Mercenary,
                TravelRole = ResidentTravelRole.Combatant,
                UnitTypeId = candidate.UnitTypeId
            };
            state.People.Residents.Add(resident);
            PeopleService.EnsureCombatState(resident);
        }
        state.ActiveExpedition.FighterIds.Add(personId);
        candidate.Hired = true;
        state.Settlements.HireContracts.Add(new HireContractData
        {
            PersonId = personId,
            LocationId = locationId,
            DailyWage = candidate.DailyWage,
            LastPaidDay = state.Day
        });
        Chronicle.Record(state, "hire." + personId, "Нанят: " + candidate.Name,
            candidate.Name + ", " + candidate.RoleLabel + ", идёт с отрядом за " + candidate.DailyWage + " зол. в сутки.");
        message = candidate.Name + " идёт с отрядом. Плата — " + candidate.DailyWage + " зол. в сутки.";
        return true;
    }

    // Полночь: плата за прошедшие игровые сутки — один раз за сутки.
    public static void PayDaily(GameState state, List<string> messages)
    {
        if (state?.Settlements?.HireContracts == null)
            return;
        foreach (HireContractData contract in state.Settlements.HireContracts)
        {
            if (contract == null || contract.LastPaidDay >= state.Day)
                continue;
            ResidentState resident = PeopleService.Find(state, contract.PersonId);
            if (resident == null || !resident.IsAlive)
                continue;
            contract.LastPaidDay = state.Day;
            if (state.ArmyGold >= contract.DailyWage)
            {
                state.ArmyGold -= contract.DailyWage;
                messages?.Add("Плата наёмнику " + resident.DisplayName + ": " + contract.DailyWage + " зол.");
            }
            else
            {
                messages?.Add("Не хватило золота заплатить " + resident.DisplayName + " (" + contract.DailyWage + " зол.).");
            }
        }
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in text ?? string.Empty)
                hash = hash * 31 + c;
            return hash;
        }
    }
}
