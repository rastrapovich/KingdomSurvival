using System;
using System.Collections.Generic;

// ПР-03: черновой мост кампания → BattleSandbox → кампания. Запрос несёт
// тех же постоянных людей (герой и бойцы текущего похода) по их ID; итог
// возвращает исход и павших по тем же ID и применяется к кампании ровно
// один раз (ID операции в NarrativeState). ПР-06А: HP человека переносятся
// в бой и обратно по PersonId, павший помечается погибшим в реестре. Ранений,
// вещей и наград здесь намеренно нет — это ПР-10.
//
// Временное правило исхода (утверждено пользователем 24.09.2026, до ПР-10):
// павшие бойцы погибают насовсем и уходят из отряда; если пал герой —
// «Отряд разбит»: кампания заканчивается, игрок загружает сохранение или
// начинает заново. Скрытого бессмертия у героя нет.
[Serializable]
public sealed class CampaignBattleParticipant
{
    public string PersonId;
    public string DisplayName;
    public string UnitTypeId;
    public bool IsHero;

    // ПР-06А: текущие HP человека; 0 — боевое состояние ещё не заведено,
    // сцена боя берёт полный запас из шаблона.
    public int CurrentHitPoints;
    public int MaxHitPoints;

    // ПР-08: боевые числа уже собраны (шаблон + качества героя + вещи +
    // состояния, CombatStatsAssembler) — экран героя и бой показывают одно.
    public bool HasAssembledStats;
    public int Attack;
    public int Defense;
    public int Damage;
    public int Movement;
    public int Initiative;
    public int AttackRange;
}

[Serializable]
public sealed class CampaignBattleSurvivor
{
    public string PersonId;
    public int HitPoints;
}

[Serializable]
public sealed class CampaignBattleEnemy
{
    public string UnitTypeId;
    public int Count = 1;
}

[Serializable]
public sealed class CampaignBattleRequest
{
    public string BattleId;
    public string LocationId;
    public List<CampaignBattleParticipant> Participants = new List<CampaignBattleParticipant>();

    // ПР-10: источник боя, враги (шаблоны UnitDatabase), поле и отход.
    // Пустой список врагов — стандартная засада BattleSandbox.
    public string SourceId = string.Empty;
    public List<CampaignBattleEnemy> Enemies = new List<CampaignBattleEnemy>();
    public int Seed;
    public bool AllowRetreat;
}

public enum CampaignBattleOutcome
{
    Victory,
    Defeat,
    // ПР-10: предусмотренный отход из написанного боя.
    Retreat
}

[Serializable]
public sealed class CampaignBattleResult
{
    public string BattleId;
    public CampaignBattleOutcome Outcome;
    public List<string> FallenPersonIds = new List<string>();
    // ПР-06А: HP выживших по PersonId — возвращаются в запись человека.
    public List<CampaignBattleSurvivor> Survivors = new List<CampaignBattleSurvivor>();

    // ПР-10: сколько раундов шёл бой — для изнеможения после долгого боя.
    public int Rounds;
}

public enum CampaignBattleApplyStatus
{
    AlreadyApplied,
    SquadSurvived,
    HeroFell
}

public static class CampaignBattleBridge
{
    // Временная боевая основа героя: в UnitDatabase нет записи командира,
    // а его боевой профиль ещё не утверждён (ПР-08). До тех пор герой
    // сражается как ополченец под своим именем.
    public const string HeroFallbackUnitTypeId = "militia";

    public const string AppliedEffectPrefix = "campaign.battle.result.";

    public static CampaignBattleRequest CreateRequest(GameState state, string battleId)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("ID боя не может быть пустым.", nameof(battleId));

        CampaignBattleRequest request = new CampaignBattleRequest
        {
            BattleId = battleId,
            LocationId = state.HasActiveExpedition ? state.ActiveExpedition.LocationId : string.Empty
        };

        CommanderData hero = state.GetSelectedCommander();
        if (hero != null)
        {
            request.Participants.Add(new CampaignBattleParticipant
            {
                PersonId = hero.Id,
                DisplayName = hero.Name,
                UnitTypeId = string.IsNullOrWhiteSpace(hero.UnitTypeId) ? HeroFallbackUnitTypeId : hero.UnitTypeId,
                IsHero = true
            });
        }

        foreach (CampaignBattleParticipant participant in request.Participants)
            FillHitPoints(state, participant);
        request.Seed = unchecked(state.WorldSeed * 31 + battleId.GetHashCode());

        if (state.HasActiveExpedition && state.ActiveExpedition.FighterIds != null)
        {
            foreach (string fighterId in state.ActiveExpedition.FighterIds)
            {
                FighterData fighter = FindFighter(state, fighterId);
                if (fighter == null || string.IsNullOrWhiteSpace(fighter.UnitTypeId))
                    continue;

                // ПР-10: тяжелораненый идёт с отрядом, но в бой не вступает.
                ResidentState wounded = HomePeopleService.Find(state, fighterId);
                if (wounded != null && wounded.Injury == ResidentInjury.Recovering)
                    continue;

                request.Participants.Add(new CampaignBattleParticipant
                {
                    PersonId = fighter.Id,
                    DisplayName = fighter.Name,
                    UnitTypeId = fighter.UnitTypeId,
                    IsHero = false
                });
                FillHitPoints(state, request.Participants[request.Participants.Count - 1]);
            }
        }

        return request;
    }

    // ПР-06А: текущие HP берутся из записи человека, а не из шаблона.
    private static void FillHitPoints(GameState state, CampaignBattleParticipant participant)
    {
        ResidentState resident = HomePeopleService.Find(state, participant.PersonId);
        if (resident == null)
            return;

        HomePeopleService.EnsureCombatState(resident);
        if (!resident.HasCombatState)
            return;
        ItemService.RefreshMaxHitPoints(state, resident.PersonId);

        participant.CurrentHitPoints = resident.CurrentHitPoints;
        participant.MaxHitPoints = resident.MaxHitPoints;

        AssembledCombatStats assembled = CombatStatsAssembler.Compute(state, participant.PersonId);
        if (!assembled.HasTemplate)
            return;
        participant.HasAssembledStats = true;
        participant.MaxHitPoints = assembled.Final.MaxHitPoints;
        participant.CurrentHitPoints = Math.Min(resident.CurrentHitPoints, assembled.Final.MaxHitPoints);
        participant.Attack = assembled.Final.Attack;
        participant.Defense = assembled.Final.Defense;
        participant.Damage = assembled.Final.Damage;
        participant.Movement = assembled.Final.Movement;
        participant.Initiative = assembled.Final.Initiative;
        participant.AttackRange = assembled.Final.AttackRange;
    }

    public static bool IsApplied(GameState state, string battleId)
    {
        return state?.Narrative != null &&
               state.Narrative.HasEffectApplied(AppliedEffectPrefix + battleId);
    }

    // Применяет итог к кампании один раз. Павшие бойцы уходят из отряда и
    // из похода; пал герой — HeroFell (кампанию заканчивает вызывающий UI).
    // ПР-10 (ТЗ §4–§5), рабочие числа [РАБОЧЕЕ]: тяжёлая рана — выжил с HP
    // не выше четверти максимума; изнеможение — если в бою кто-то пал или
    // бой шёл дольше 6 раундов; отход — все изнеможены, −2 припаса, ночлег
    // прерван.
    public const int HeavyWoundHitPointsDivisor = 4;
    public const int LongBattleRounds = 6;
    public const int RetreatSupplyLoss = 2;

    public static CampaignBattleApplyStatus ApplyResult(
        GameState state,
        CampaignBattleResult result,
        List<string> fallenNames,
        List<string> notes = null)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (result == null || string.IsNullOrWhiteSpace(result.BattleId))
            throw new ArgumentException("Итог боя без ID.", nameof(result));

        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        if (IsApplied(state, result.BattleId))
            return CampaignBattleApplyStatus.AlreadyApplied;

        state.Narrative.MarkEffectApplied(AppliedEffectPrefix + result.BattleId);

        CommanderData hero = state.GetSelectedCommander();
        bool heroFell = false;
        foreach (string personId in result.FallenPersonIds ?? new List<string>())
        {
            if (hero != null && personId == hero.Id)
            {
                heroFell = true;
                continue;
            }

            FighterData fighter = FindFighter(state, personId);
            if (fighter == null)
                continue;

            fallenNames?.Add(fighter.Name);
            if (HomePeopleService.Find(state, personId) != null)
            {
                // ПР-06А: погибший остаётся в реестре и семье, уходит из состава.
                HomePeopleService.MarkDead(state, personId, "battle." + result.BattleId);
            }
            else
            {
                state.Fighters.Remove(fighter);
                if (state.HasActiveExpedition && state.ActiveExpedition.FighterIds != null)
                    state.ActiveExpedition.FighterIds.Remove(personId);
            }
        }

        // ПР-06А: HP выживших возвращаются в запись человека; шаблон
        // UnitDatabase не меняется. Потерянное здоровье лечит уход дома.
        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            ResidentState resident = HomePeopleService.Find(state, survivor.PersonId);
            if (resident == null || !resident.IsAlive)
                continue;

            HomePeopleService.EnsureCombatState(resident);
            HomePeopleService.SetHitPoints(resident, survivor.HitPoints);
        }

        if (!heroFell)
        {
            List<string> aftermath = new List<string>();
            ApplyBattleAftermath(state, result, aftermath);
            notes?.AddRange(aftermath);

            // ПР-11: бой — запись истории (один раз на бой).
            List<string> fallenHere = new List<string>();
            foreach (string personId in result.FallenPersonIds ?? new List<string>())
            {
                ResidentState fallen = HomePeopleService.Find(state, personId);
                if (fallen != null)
                    fallenHere.Add(fallen.DisplayName);
            }
            string outcomeText = result.Outcome == CampaignBattleOutcome.Victory ? "Победа."
                : result.Outcome == CampaignBattleOutcome.Retreat ? "Отряд отступил." : "Бой проигран.";
            Chronicle.Record(state, "battle." + result.BattleId, "Бой",
                outcomeText + (fallenHere.Count > 0 ? " Погибли: " + string.Join(", ", fallenHere) + "." : " Все живы.") +
                (aftermath.Count > 0 ? " " + string.Join(" ", aftermath) : string.Empty),
                state.HasActiveExpedition ? state.ActiveExpedition.LocationId : null);
        }

        return heroFell ? CampaignBattleApplyStatus.HeroFell : CampaignBattleApplyStatus.SquadSurvived;
    }

    private static void ApplyBattleAftermath(GameState state, CampaignBattleResult result, List<string> notes)
    {
        bool anyFallen = result.FallenPersonIds != null && result.FallenPersonIds.Count > 0;
        bool retreat = result.Outcome == CampaignBattleOutcome.Retreat;
        bool exhausting = retreat || anyFallen || result.Rounds > LongBattleRounds;

        List<string> heavy = new List<string>();
        List<string> tired = new List<string>();
        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            ResidentState resident = HomePeopleService.Find(state, survivor.PersonId);
            if (resident == null || !resident.IsAlive || !resident.HasCombatState)
                continue;

            if (resident.CurrentHitPoints * HeavyWoundHitPointsDivisor <= resident.MaxHitPoints &&
                resident.Injury != ResidentInjury.Recovering)
            {
                resident.Injury = ResidentInjury.Recovering;
                heavy.Add(resident.DisplayName);
            }

            if (exhausting && !resident.Exhausted)
            {
                resident.Exhausted = true;
                tired.Add(resident.DisplayName);
            }
        }

        if (retreat)
        {
            state.ArmySupply = Math.Max(0, state.ArmySupply - RetreatSupplyLoss);
            if (CampRest.IsResting(state))
            {
                state.ActiveExpedition.ActiveActivity = null;
                CampRest.GetNight(state).ChosenActions.Clear();
            }
            notes?.Add("Отряд бросил стоянку: потеряно " + RetreatSupplyLoss + " припаса, ночлег прерван.");
        }

        if (heavy.Count > 0)
            notes?.Add("Тяжело ранены: " + string.Join(", ", heavy) + " — в следующий поход только после лечения дома.");
        if (tired.Count > 0)
            notes?.Add("Изнеможены: " + string.Join(", ", tired) + ".");
    }

    private static FighterData FindFighter(GameState state, string fighterId)
    {
        if (state.Fighters == null || string.IsNullOrWhiteSpace(fighterId))
            return null;

        foreach (FighterData fighter in state.Fighters)
        {
            if (fighter != null && fighter.Id == fighterId)
                return fighter;
        }

        return null;
    }
}
