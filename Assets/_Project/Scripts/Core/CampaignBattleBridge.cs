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

// ПР-12К (канон v1.53 §28.3): откуда начался бой. Дорожный — отдельная
// арена с возвратом на глобальную карту (прежнее поведение, значение по
// умолчанию); на месте — то же поле исследуемого места, без смены сцены.
public enum CampaignBattleSourceKind
{
    Road = 0,
    Local = 1
}

// ПР-12К (канон v1.53 §28.10): происхождение участия. Человек Дома —
// основа отряда; наёмник и существо-союзник — редкие внешние участники.
// Происхождение не определяет сторону в бою и допуск в поселение.
public enum CampaignParticipantOrigin
{
    HomePerson = 0,
    Mercenary = 1,
    CreatureAlly = 2
}

[Serializable]
public sealed class CampaignBattleParticipant
{
    public string PersonId;
    public string DisplayName;
    public string UnitTypeId;
    public bool IsHero;

    // ПР-12К: внешний участник может брать боевой шаблон существа из общего
    // каталога, человек Дома — только из отряда.
    public CampaignParticipantOrigin Origin;

    // ПР-12К: клетка поля для боя на месте (HasCell — задана явно).
    public bool HasCell;
    public int CellQ;
    public int CellR;

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

    // 12Е-6: боевые правила особенностей человека (CombatPerkIds).
    public List<string> PerkIds = new List<string>();
}

[Serializable]
public sealed class CampaignBattleSurvivor
{
    public string PersonId;
    public int HitPoints;

    // ПР-12К: где выживший стоял в конце боя на месте.
    public bool HasCell;
    public int CellQ;
    public int CellR;
    // Та же клетка как точка рисунка места (заполняет слой места).
    public bool HasPoint;
    public float PointX;
    public float PointY;
}

// Канон v1.48 §27.3: что участник реально сделал в бою. Полученный урон
// вкладом не считается; последний удар отдельно не вознаграждается.
[Serializable]
public sealed class CampaignBattleContribution
{
    public string PersonId;
    // Урон, действительно снятый с противников (не больше их остатка HP),
    // включая ответные удары.
    public int DamageDealt;
    // Урон, который защитная стойка не пропустила.
    public int DamagePrevented;
    public bool UsedRangedAttack;
    public bool UsedMeleeAttack;

    // 12Е-8: для следов развития — сколько раз был целью атаки, сколько
    // ответных ударов нанёс, стрелял ли, не сходя с места.
    public int TimesAttacked;
    public int Retaliations;
    public bool ShotFromPlace;
}

// Противник, с которым отряд столкнулся, — для банка опыта боя.
[Serializable]
public sealed class CampaignBattleEnemyRecord
{
    public string UnitTypeId;
    public int Level = 1;
    public int MaxHitPoints;
    public int Attack;
    public int Defense;
    public int Damage;
    // ПР-12Ж: для цены противника. 0 — не известно (старый результат).
    public int Movement;
    public int Initiative;
    public int AttackRange;
    public List<string> TagIds = new List<string>();
    public bool Defeated;

    // ПР-12К: конкретный противник места (устойчивый InstanceId), его
    // здоровье и клетка в конце боя. Пусто — безымянный противник дороги.
    public string InstanceId = string.Empty;
    public int HitPoints;
    public bool HasCell;
    public int CellQ;
    public int CellR;
    public bool HasPoint;
    public float PointX;
    public float PointY;
}

[Serializable]
public sealed class CampaignBattleEnemy
{
    public string UnitTypeId;
    public int Count = 1;
    // Уровень противника: прибавки и цена в опыте — из его карты развития.
    public int Level = 1;

    // ПР-12К: конкретный противник места. InstanceId не пуст — это один
    // экземпляр (Count не используется) со своим здоровьем и клеткой;
    // погибший не появляется снова, раненый не исцеляется молча.
    public string InstanceId = string.Empty;
    // 0 — полный запас шаблона.
    public int CurrentHitPoints;
    public bool HasCell;
    public int CellQ;
    public int CellR;
}

// ПР-12К: клетка поля (q, r) в данных запроса.
[Serializable]
public sealed class CampaignBattleCell
{
    public int Q;
    public int R;
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

    // 12Е-6: бой начат подготовленно (отряд заметил угрозу заранее) и
    // прибавка к инициативе отряда в первом раунде («Засада»); строки для
    // журнала боя.
    public bool PreparedStart;
    public int PlayerFirstRoundInitiativeBonus;
    public List<string> Notes = new List<string>();

    // ПР-12К (канон v1.53 §28.3): источник боя и его пространство.
    // Дорожный бой: BattlefieldId — арена по местности (пусто — общее поле
    // полигона, отмеченный временный вариант). Бой на месте: поле
    // исследуемого места, явные клетки участников и противников, трудная
    // местность места; стены — отключённые гексы этого поля.
    public CampaignBattleSourceKind SourceKind = CampaignBattleSourceKind.Road;
    public string BattlefieldId = string.Empty;
    public string LocalLocationId = string.Empty;
    // Столкновение места, из которого начат бой.
    public string EncounterId = string.Empty;
    public List<CampaignBattleCell> DifficultCells = new List<CampaignBattleCell>();
    // Клетки, занятые объектами места (кроме стен поля): в бою они так же
    // непроходимы, как при исследовании.
    public List<CampaignBattleCell> BlockedCells = new List<CampaignBattleCell>();
    // ПР-12К (канон v1.54): бой на месте — кадр поля лежит на рисунке места,
    // его стены задаёт разметка места (BlockedCells), а не отключённые гексы
    // собственного рисунка поля.
    public bool IgnoreFieldDisabledCells;
    // ПР-12К: бой на месте идёт на гексах всей локации — прямоугольная сетка
    // GridWidth × GridHeight клеток размера GridHexSize (пиксели рисунка
    // места GridCanvasWidth × GridCanvasHeight). 0 — арена поля 10×7.
    public int GridWidth;
    public int GridHeight;
    public float GridHexSize;
    public float GridCanvasWidth;
    public float GridCanvasHeight;
    // Сдвиг сетки поля (пиксели рисунка места), уже приведённый к периоду сетки.
    public float GridShiftX;
    public float GridShiftY;

    public bool IsLocal => SourceKind == CampaignBattleSourceKind.Local;
    public bool UsesLocationGrid => IsLocal && GridWidth > 0 && GridHeight > 0 && GridHexSize > 0;
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

    // Канон v1.48 §27.2–27.3: участники с их вкладом и противники боя.
    public List<CampaignBattleContribution> Contributions = new List<CampaignBattleContribution>();
    public List<CampaignBattleEnemyRecord> Enemies = new List<CampaignBattleEnemyRecord>();

    // 12Е-6: кто после боя обязательно тяжело ранен («Ещё на ногах»,
    // вынесенный с поля «Не бросает своих»).
    public List<string> ForcedHeavyWoundIds = new List<string>();

    // ПР-12К: откуда был бой — итог боя на месте меняет и само место
    // (противники по InstanceId, позиции выживших) тем же однократным
    // применением, что и кампанию.
    public CampaignBattleSourceKind SourceKind = CampaignBattleSourceKind.Road;
    public string LocalLocationId = string.Empty;
    public string EncounterId = string.Empty;
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

    // Прежний вход: все, кто может сражаться здесь (PartyPresence). Без
    // разделения отряда это герой и бойцы похода без тяжелораненых — как до
    // ПР-12К.
    public static CampaignBattleRequest CreateRequest(GameState state, string battleId)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        return CreateRequestFor(state, battleId, PartyPresence.BattleCandidateIds(state));
    }

    // ПР-12К: явная сборка запроса из выбранных участников (по ID, в этом
    // порядке). Берутся только те, кто реально может вступить в этот бой:
    // присутствует, жив, боеспособен, не свита. Событие начала боя видит уже
    // окончательный состав — особенности не срабатывают за отсутствующих.
    public static CampaignBattleRequest CreateRequestFor(
        GameState state,
        string battleId,
        IEnumerable<string> participantIds,
        CampaignBattleSourceKind sourceKind = CampaignBattleSourceKind.Road)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("ID боя не может быть пустым.", nameof(battleId));

        CampaignBattleRequest request = new CampaignBattleRequest
        {
            BattleId = battleId,
            LocationId = state.HasActiveExpedition ? state.ActiveExpedition.LocationId : string.Empty,
            SourceKind = sourceKind
        };
        request.Seed = unchecked(state.WorldSeed * 31 + battleId.GetHashCode());

        CommanderData hero = state.GetSelectedCommander();
        List<string> allowed = PartyPresence.BattleCandidateIds(state);
        foreach (string personId in participantIds ?? new List<string>())
        {
            if (string.IsNullOrEmpty(personId) || !allowed.Contains(personId) ||
                request.Participants.Exists(existing => existing.PersonId == personId))
                continue;

            CampaignBattleParticipant participant;
            if (hero != null && personId == hero.Id)
            {
                participant = new CampaignBattleParticipant
                {
                    PersonId = hero.Id,
                    DisplayName = hero.Name,
                    UnitTypeId = string.IsNullOrWhiteSpace(hero.UnitTypeId) ? HeroFallbackUnitTypeId : hero.UnitTypeId,
                    IsHero = true
                };
            }
            else
            {
                FighterData fighter = FindFighter(state, personId);
                if (fighter == null || string.IsNullOrWhiteSpace(fighter.UnitTypeId))
                    continue;
                participant = new CampaignBattleParticipant
                {
                    PersonId = fighter.Id,
                    DisplayName = fighter.Name,
                    UnitTypeId = fighter.UnitTypeId,
                    IsHero = false
                };
            }

            ResidentState resident = HomePeopleService.Find(state, personId);
            participant.Origin = resident != null ? resident.Origin : CampaignParticipantOrigin.HomePerson;
            FillHitPoints(state, participant);
            request.Participants.Add(participant);
        }

        List<string> roster = new List<string>();
        foreach (CampaignBattleParticipant participant in request.Participants)
            roster.Add(participant.PersonId);
        FeatureDispatcher.Raise(new FeatureEvent
        {
            Trigger = FeatureTrigger.BattleStarted,
            State = state,
            BattleId = battleId,
            EventKey = "battle-start:" + battleId,
            LocationId = request.LocationId,
            ParticipantIds = roster
        });

        return request;
    }

    // ПР-06А: текущие HP берутся из записи человека, а не из шаблона.
    private static void FillHitPoints(GameState state, CampaignBattleParticipant participant)
    {
        // 12Е-6: боевые правила особенностей человека.
        participant.PerkIds = FeatureCombatBatch.PerksFor(state, participant.PersonId);

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

        if (result.ForcedHeavyWoundIds == null)
            result.ForcedHeavyWoundIds = new List<string>();

        // 12Е-8: для следов — каким бой был до применения итога (кто пал и
        // кто уже был тяжело ранен).
        bool retreatForWounded = ProgressionTraces.IsRetreatForWounded(state, result);
        HashSet<string> woundedBeforeBattle = new HashSet<string>();
        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            ResidentState resident = HomePeopleService.Find(state, survivor.PersonId);
            if (resident != null && resident.Injury == ResidentInjury.Recovering)
                woundedBeforeBattle.Add(survivor.PersonId);
        }

        // 12Е-6: «Ещё на ногах» и «Не бросает своих» — до того, как павшие
        // уйдут из отряда.
        FeatureCombatBatch.RecordStillStanding(state, result, notes);
        FeatureCombatBatch.CarryFallenOnRetreat(state, result, notes);

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

            // Канон v1.48 §27.2: общий опыт из единого банка боя и практика.
            List<string> experience = BattleExperience.Apply(state, result);
            notes?.AddRange(experience);

            // 12Е-8: следы развития этого боя (записываются и без списка строк).
            List<string> traces = ProgressionTraces.RecordBattle(state, result, retreatForWounded, woundedBeforeBattle);
            notes?.AddRange(traces);

            // ПР-12К: бой на месте меняет и само место — тем же однократным
            // применением (противники по InstanceId, позиции выживших).
            LocalExplorationService.ApplyBattle(state, result);
        }

        List<string> battleRoster = new List<string>();
        foreach (CampaignBattleContribution contribution in result.Contributions ?? new List<CampaignBattleContribution>())
            battleRoster.Add(contribution.PersonId);
        List<FeatureActivation> activations = FeatureDispatcher.Raise(new FeatureEvent
        {
            Trigger = FeatureTrigger.BattleEnded,
            State = state,
            BattleId = result.BattleId,
            BattleResult = result,
            EventKey = "battle-end:" + result.BattleId,
            // ПР-12К: только те, кто был в бою; старый итог без вклада —
            // как раньше, весь присутствующий отряд.
            ParticipantIds = battleRoster.Count > 0 ? battleRoster : null
        });
        notes?.AddRange(FeaturePresentation.Lines(state, activations));

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

            bool forced = result.ForcedHeavyWoundIds != null && result.ForcedHeavyWoundIds.Contains(survivor.PersonId);
            if ((forced || resident.CurrentHitPoints * FeatureCombatBatch.HeavyWoundDivisor(state, survivor.PersonId) <= resident.MaxHitPoints) &&
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
            notes?.Add(result.SourceKind == CampaignBattleSourceKind.Local
                ? "Отряд отошёл, бросив часть поклажи: потеряно " + RetreatSupplyLoss + " припаса."
                : "Отряд бросил стоянку: потеряно " + RetreatSupplyLoss + " припаса, ночлег прерван.");
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
