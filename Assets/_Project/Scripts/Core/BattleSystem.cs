using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public enum FighterCombatRole
{
    Unknown,
    Guard,
    Archer,
    Healer,
    Spearman,
    Scout
}

public enum FighterHealthState
{
    Healthy,
    Wounded,
    SeverelyWounded,
    Dead
}

public enum BattleDoctrine
{
    Cautious,
    Balanced,
    Assault
}

public enum BattleKind
{
    ExpeditionLocation,
    CapitalDefense
}

public enum BattleTerrain
{
    Open,
    Forest,
    Ruins,
    City
}

[Flags]
public enum BattleEnemyTag
{
    None = 0,
    Ambush = 1,
    Charge = 2,
    Ranged = 4,
    Beast = 8,
    Hunter = 16
}

public enum BattleOutcome
{
    Victory,
    CostlyVictory,
    Withdrawal,
    Defeat
}

[Serializable]
public sealed class FighterCombatState
{
    public string FighterId;
    public FighterCombatRole RoleCode;
    public int AttackPower;
    public int DefensePower;
    public int MaxHitPoints;
    public double HitPoints;

    public FighterHealthState HealthState =>
        BattleSystem.GetHealthState(HitPoints, MaxHitPoints);
}

[Serializable]
public sealed class BattleContext
{
    public string Id;
    public BattleKind Kind;
    public string SourceId;
    public string Title;
    public string Description;
    public string EnemyName;
    public int EnemyPower;
    public BattleTerrain Terrain;
    public BattleEnemyTag EnemyTags;
    public BattleDoctrine Doctrine;
    public string CommanderId;
    public readonly List<string> FighterIds = new List<string>();
}

[Serializable]
public sealed class BattlePhaseResult
{
    public string Name;
    public int PlayerScore;
    public int EnemyScore;
    public string Explanation;
}

[Serializable]
public sealed class FighterBattleConsequence
{
    public string FighterId;
    public string FighterName;
    public int BeforeHitPoints;
    public int AfterHitPoints;
    public FighterHealthState BeforeState;
    public FighterHealthState AfterState;
    public bool MitigatedByHealer;
    public double RecoveryHours;
}

[Serializable]
public sealed class BattleResult
{
    public string BattleId;
    public BattleKind Kind;
    public string SourceId;
    public string Title;
    public BattleDoctrine Doctrine;
    public BattleOutcome Outcome;
    public readonly List<BattlePhaseResult> Phases =
        new List<BattlePhaseResult>();
    public readonly List<FighterBattleConsequence> FighterConsequences =
        new List<FighterBattleConsequence>();
    public int FoodDelta;
    public int MoodDelta;
    public int ArmyGoldDelta;
    public int ArmySupplyDelta;
    public bool ForceRetreat;
    public bool DeathPossible;
    public bool Applied;
}

[Serializable]
public sealed class PendingBattleData
{
    public BattleContext Context;
    public BattleResult Result;
}

public static class BattleSystem
{
    private sealed class CombatProfile
    {
        public FighterCombatRole Role;
        public int Attack;

        public CombatProfile(FighterCombatRole role, int attack)
        {
            Role = role;
            Attack = attack;
        }
    }

    private sealed class LocationEncounterProfile
    {
        public string Id;
        public string LocationId;
        public string Title;
        public string Description;
        public string EnemyName;
        public int EnemyPower;
        public BattleTerrain Terrain;
        public BattleEnemyTag Tags;

        public LocationEncounterProfile(
            string id,
            string locationId,
            string title,
            string description,
            string enemyName,
            int enemyPower,
            BattleTerrain terrain,
            BattleEnemyTag tags)
        {
            Id = id;
            LocationId = locationId;
            Title = title;
            Description = description;
            EnemyName = enemyName;
            EnemyPower = enemyPower;
            Terrain = terrain;
            Tags = tags;
        }
    }

    private sealed class BattleRuntimeState
    {
        public readonly Dictionary<string, FighterCombatState> Fighters =
            new Dictionary<string, FighterCombatState>();
        public readonly HashSet<string> ResolvedEncounterIds =
            new HashSet<string>();
        public PendingBattleData PendingBattle;
    }

    private const int DefaultMaxHitPoints = 100;
    private const double RecoveryHitPointsPerGameHour = 2.0;

    private static readonly ConditionalWeakTable<GameState, BattleRuntimeState>
        RuntimeStates = new ConditionalWeakTable<GameState, BattleRuntimeState>();

    private static readonly Dictionary<string, CombatProfile> KnownProfiles =
        new Dictionary<string, CombatProfile>
        {
            { "garrick", new CombatProfile(FighterCombatRole.Guard, 3) },
            { "edric", new CombatProfile(FighterCombatRole.Archer, 4) },
            { "marta", new CombatProfile(FighterCombatRole.Healer, 1) },
            { "torvin", new CombatProfile(FighterCombatRole.Spearman, 3) },
            { "agnessa", new CombatProfile(FighterCombatRole.Scout, 2) }
        };

    private static readonly List<LocationEncounterProfile> LocationEncounters =
        new List<LocationEncounterProfile>
        {
            new LocationEncounterProfile(
                "black_forest_ambush",
                "forest",
                "Засада в Чёрном лесу",
                "Между деревьями отряд окружает стая лесных тварей. " +
                "Отступить без столкновения уже нельзя: сначала нужно выбрать доктрину боя.",
                "лесные твари",
                16,
                BattleTerrain.Forest,
                BattleEnemyTag.Ambush | BattleEnemyTag.Beast)
        };

    public static bool HasPendingBattle(GameState state)
    {
        return state != null && GetRuntime(state).PendingBattle != null;
    }

    public static PendingBattleData GetPendingBattle(GameState state)
    {
        return state != null ? GetRuntime(state).PendingBattle : null;
    }

    public static FighterCombatState GetFighterCombatState(
        GameState state,
        string fighterId)
    {
        if (state == null || string.IsNullOrEmpty(fighterId))
            return null;

        BattleRuntimeState runtime = GetRuntime(state);
        SyncFighters(state, runtime);

        FighterCombatState fighter;
        return runtime.Fighters.TryGetValue(fighterId, out fighter)
            ? fighter
            : null;
    }

    public static bool HasUnresolvedLocationEncounter(
        GameState state,
        string locationId)
    {
        if (state == null || string.IsNullOrEmpty(locationId))
            return false;

        LocationEncounterProfile encounter = FindLocationEncounter(locationId);
        return encounter != null &&
               !GetRuntime(state).ResolvedEncounterIds.Contains(encounter.Id);
    }

    public static bool TryPrepareLocationBattle(
        GameState state,
        string locationId,
        out string resultMessage)
    {
        resultMessage = "Бой в этой локации сейчас недоступен.";

        if (state == null || !state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision)
        {
            return false;
        }

        if (HasPendingBattle(state))
        {
            resultMessage = "Сначала завершите уже подготовленный бой.";
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.Phase != CommanderState.AtLocation)
            return false;

        LocationEncounterProfile encounter = FindLocationEncounter(locationId);
        if (encounter == null ||
            GetRuntime(state).ResolvedEncounterIds.Contains(encounter.Id))
        {
            return false;
        }

        BattleContext context = new BattleContext
        {
            Id = encounter.Id,
            Kind = BattleKind.ExpeditionLocation,
            SourceId = locationId,
            Title = encounter.Title,
            Description = encounter.Description,
            EnemyName = encounter.EnemyName,
            EnemyPower = encounter.EnemyPower,
            Terrain = encounter.Terrain,
            EnemyTags = encounter.Tags,
            Doctrine = BattleDoctrine.Balanced,
            CommanderId = expedition.CommanderId
        };

        foreach (string fighterId in expedition.FighterIds)
            context.FighterIds.Add(fighterId);

        PendingBattleData pending = new PendingBattleData
        {
            Context = context,
            Result = Resolve(state, context)
        };
        GetRuntime(state).PendingBattle = pending;

        resultMessage =
            "Подготовлен бой «" + encounter.Title +
            "». Выберите доктрину и подтвердите точный прогноз.";
        return true;
    }

    public static bool TryPrepareCurrentLocationBattle(
        GameState state,
        out string resultMessage)
    {
        resultMessage = "Опасного столкновения в текущей локации нет.";

        if (state == null || !state.HasActiveExpedition)
            return false;

        return TryPrepareLocationBattle(
            state,
            state.ActiveExpedition.LocationId,
            out resultMessage);
    }

    public static bool TryPrepareCapitalBattle(
        GameState state,
        out string resultMessage)
    {
        resultMessage = "Нападение на столицу сейчас не может быть рассчитано.";

        if (state == null || HasPendingBattle(state))
            return false;

        BattleContext context = new BattleContext
        {
            Id = "capital_granary_raid_day_" + state.Day,
            Kind = BattleKind.CapitalDefense,
            SourceId = "capital_granary",
            Title = "Нападение на городской амбар",
            Description =
                "Вооружённая толпа пытается прорваться к городскому амбару. " +
                "В бой вступают только бойцы, которые фактически находятся в столице.",
            EnemyName = "вооружённая толпа",
            EnemyPower = 14,
            Terrain = BattleTerrain.City,
            EnemyTags = BattleEnemyTag.Charge,
            Doctrine = BattleDoctrine.Balanced,
            CommanderId = GetCapitalCommanderId(state)
        };

        foreach (FighterData fighter in state.Fighters)
        {
            if (!state.IsFighterInActiveExpedition(fighter.Id))
                context.FighterIds.Add(fighter.Id);
        }

        PendingBattleData pending = new PendingBattleData
        {
            Context = context,
            Result = Resolve(state, context)
        };
        GetRuntime(state).PendingBattle = pending;

        resultMessage =
            "Нападение на столицу началось. Выберите доктрину обороны и " +
            "подтвердите сохранённый прогноз.";
        return true;
    }

    public static BattleResult SelectPendingDoctrine(
        GameState state,
        BattleDoctrine doctrine)
    {
        PendingBattleData pending = GetPendingBattle(state);
        if (pending == null)
            return null;

        pending.Context.Doctrine = doctrine;
        pending.Result = Resolve(state, pending.Context);
        return pending.Result;
    }

    public static bool TryApplyPendingBattle(
        GameState state,
        out BattleResult appliedResult,
        out string reportText)
    {
        appliedResult = null;
        reportText = "Нет подготовленного боя.";

        PendingBattleData pending = GetPendingBattle(state);
        if (state == null || pending == null || pending.Result == null)
            return false;

        BattleRuntimeState runtime = GetRuntime(state);
        BattleResult result = pending.Result;

        // Никакого повторного расчёта здесь нет: применяем ровно тот BattleResult,
        // который был показан игроку после выбора доктрины.
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            FighterCombatState combatant;
            if (!runtime.Fighters.TryGetValue(consequence.FighterId, out combatant))
                continue;

            combatant.HitPoints = consequence.AfterHitPoints;
        }

        List<string> deadIds = new List<string>();
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            if (consequence.AfterState == FighterHealthState.Dead)
                deadIds.Add(consequence.FighterId);
        }

        if (deadIds.Count > 0)
        {
            if (state.ActiveExpedition != null)
            {
                state.ActiveExpedition.FighterIds.RemoveAll(
                    fighterId => deadIds.Contains(fighterId));
            }

            state.Fighters.RemoveAll(fighter => deadIds.Contains(fighter.Id));
        }

        if (result.Kind == BattleKind.CapitalDefense)
        {
            state.Food = Math.Max(0, state.Food + result.FoodDelta);
            state.Mood = Math.Max(0, Math.Min(100, state.Mood + result.MoodDelta));
        }
        else
        {
            state.ArmyGold = Math.Max(0, state.ArmyGold + result.ArmyGoldDelta);
            state.ArmySupply = Math.Max(0, state.ArmySupply + result.ArmySupplyDelta);

            if (result.Outcome == BattleOutcome.Victory ||
                result.Outcome == BattleOutcome.CostlyVictory)
            {
                runtime.ResolvedEncounterIds.Add(result.BattleId);
                LocationData location = state.FindLocation(result.SourceId);
                if (location != null && !location.IsWaypoint)
                    location.IsExplored = true;
            }
        }

        result.Applied = true;
        runtime.PendingBattle = null;
        appliedResult = result;

        string returnMessage = string.Empty;
        if (result.Kind == BattleKind.ExpeditionLocation && result.ForceRetreat &&
            state.HasActiveExpedition)
        {
            state.TryOrderReturn(out returnMessage);
        }

        reportText = BuildBattleReport(result);
        if (!string.IsNullOrWhiteSpace(returnMessage))
            reportText += "\n" + returnMessage;

        return true;
    }

    public static void AdvanceCapitalRecovery(GameState state, double gameHours)
    {
        if (state == null || gameHours <= 0.0)
            return;

        BattleRuntimeState runtime = GetRuntime(state);
        SyncFighters(state, runtime);

        foreach (FighterData fighter in state.Fighters)
        {
            if (state.IsFighterInActiveExpedition(fighter.Id))
                continue;

            FighterCombatState combatant;
            if (!runtime.Fighters.TryGetValue(fighter.Id, out combatant) ||
                combatant.HealthState == FighterHealthState.Dead ||
                combatant.HitPoints >= combatant.MaxHitPoints)
            {
                continue;
            }

            combatant.HitPoints = Math.Min(
                combatant.MaxHitPoints,
                combatant.HitPoints + RecoveryHitPointsPerGameHour * gameHours);
        }
    }

    public static FighterHealthState GetHealthState(
        double hitPoints,
        int maxHitPoints)
    {
        if (maxHitPoints <= 0 || hitPoints <= 0.0)
            return FighterHealthState.Dead;

        double ratio = hitPoints / maxHitPoints;
        if (ratio >= 0.75)
            return FighterHealthState.Healthy;
        if (ratio >= 0.40)
            return FighterHealthState.Wounded;
        return FighterHealthState.SeverelyWounded;
    }

    public static string GetHealthLabel(FighterHealthState state)
    {
        switch (state)
        {
            case FighterHealthState.Healthy:
                return "здоров";
            case FighterHealthState.Wounded:
                return "ранен";
            case FighterHealthState.SeverelyWounded:
                return "тяжело ранен";
            case FighterHealthState.Dead:
                return "погиб";
            default:
                return "неизвестно";
        }
    }

    public static string GetRoleLabel(FighterCombatRole role)
    {
        switch (role)
        {
            case FighterCombatRole.Guard:
                return "гвардеец";
            case FighterCombatRole.Archer:
                return "лучник";
            case FighterCombatRole.Healer:
                return "лекарь";
            case FighterCombatRole.Spearman:
                return "копейщик";
            case FighterCombatRole.Scout:
                return "разведчик";
            default:
                return "боец";
        }
    }

    public static string GetDoctrineLabel(BattleDoctrine doctrine)
    {
        switch (doctrine)
        {
            case BattleDoctrine.Cautious:
                return "Осторожная";
            case BattleDoctrine.Assault:
                return "Натиск";
            default:
                return "Сбалансированная";
        }
    }

    public static string GetOutcomeLabel(BattleOutcome outcome)
    {
        switch (outcome)
        {
            case BattleOutcome.Victory:
                return "ПОБЕДА";
            case BattleOutcome.CostlyVictory:
                return "ТЯЖЁЛАЯ ПОБЕДА";
            case BattleOutcome.Withdrawal:
                return "ОРГАНИЗОВАННЫЙ ОТХОД";
            default:
                return "РАЗГРОМ";
        }
    }

    public static string BuildBattlePreview(PendingBattleData pending)
    {
        if (pending == null || pending.Result == null)
            return "Прогноз недоступен.";

        BattleResult result = pending.Result;
        List<string> lines = new List<string>
        {
            "Доктрина: " + GetDoctrineLabel(result.Doctrine) + ".",
            "Точный прогноз: " + GetOutcomeLabel(result.Outcome) + "."
        };

        foreach (BattlePhaseResult phase in result.Phases)
        {
            lines.Add(
                phase.Name + ": " + phase.PlayerScore +
                " против " + phase.EnemyScore + ". " + phase.Explanation);
        }

        List<string> losses = BuildConsequenceLines(result);
        lines.Add("Потери: " +
            (losses.Count > 0 ? string.Join("; ", losses) : "ранений не ожидается"));
        lines.Add(result.DeathPossible
            ? "Гибель: возможна и уже указана по конкретному бойцу."
            : "Гибель: невозможна в показанном расчёте.");

        if (result.FoodDelta != 0 || result.MoodDelta != 0)
        {
            lines.Add(
                "Последствия для столицы: пища " + FormatSigned(result.FoodDelta) +
                ", настроение " + FormatSigned(result.MoodDelta) + ".");
        }

        if (result.ForceRetreat)
            lines.Add("После боя отряд будет вынужден возвращаться в столицу.");

        lines.Add("После подтверждения будет применён именно этот сохранённый расчёт.");
        return string.Join("\n", lines);
    }

    public static string BuildCompactPreview(BattleResult result)
    {
        if (result == null)
            return "Точный прогноз пока недоступен.";

        List<string> losses = BuildConsequenceLines(result);
        return
            "Прогноз: " + GetOutcomeLabel(result.Outcome) + ". " +
            (losses.Count > 0
                ? "Ожидаемые потери: " + string.Join("; ", losses) + "."
                : "Ранений не ожидается.") +
            " Выберите доктрину перед подтверждением.";
    }

    public static string BuildBattleReport(BattleResult result)
    {
        if (result == null)
            return "Боевой отчёт отсутствует.";

        List<string> lines = new List<string>
        {
            "Бой: «" + result.Title + "». Доктрина: " +
            GetDoctrineLabel(result.Doctrine) + ".",
            "Итог: " + GetOutcomeLabel(result.Outcome) + "."
        };

        foreach (BattlePhaseResult phase in result.Phases)
        {
            lines.Add(
                phase.Name + ": " + phase.PlayerScore +
                "/" + phase.EnemyScore + " — " + phase.Explanation);
        }

        List<string> losses = BuildConsequenceLines(result);
        lines.Add("Бойцы: " +
            (losses.Count > 0 ? string.Join("; ", losses) : "без новых ранений"));

        if (result.FoodDelta != 0 || result.MoodDelta != 0)
        {
            lines.Add(
                "Столица: пища " + FormatSigned(result.FoodDelta) +
                ", настроение " + FormatSigned(result.MoodDelta) + ".");
        }

        return string.Join("\n", lines);
    }

    private static BattleResult Resolve(GameState state, BattleContext context)
    {
        BattleRuntimeState runtime = GetRuntime(state);
        SyncFighters(state, runtime);

        List<FighterCombatState> fighters = new List<FighterCombatState>();
        foreach (string fighterId in context.FighterIds)
        {
            FighterCombatState combatant;
            if (runtime.Fighters.TryGetValue(fighterId, out combatant) &&
                combatant.HealthState != FighterHealthState.Dead)
            {
                fighters.Add(combatant);
            }
        }

        int supplyPenalty = CalculateSupplyPenalty(state, context);
        int approach = 0;
        int clash = 0;
        int hold = 0;
        List<string> approachNotes = new List<string>();
        List<string> clashNotes = new List<string>();
        List<string> holdNotes = new List<string>();

        foreach (FighterCombatState fighter in fighters)
        {
            double healthFactor = GetHealthCombatFactor(fighter.HealthState);
            approach += (int)Math.Round(fighter.AttackPower * healthFactor);
            clash += (int)Math.Round(
                (fighter.AttackPower + fighter.DefensePower) * 0.5 * healthFactor);
            hold += (int)Math.Round(fighter.DefensePower * healthFactor);

            switch (fighter.RoleCode)
            {
                case FighterCombatRole.Scout:
                    int scoutBonus = 3;
                    if ((context.EnemyTags & BattleEnemyTag.Ambush) != 0 ||
                        context.Terrain == BattleTerrain.Forest)
                    {
                        scoutBonus += 2;
                    }
                    approach += scoutBonus;
                    approachNotes.Add("разведчик сорвал внезапность +" + scoutBonus);
                    break;

                case FighterCombatRole.Archer:
                    approach += 3;
                    approachNotes.Add("лучник ослабил противника до схватки +3");
                    break;

                case FighterCombatRole.Guard:
                    clash += 2;
                    hold += 3;
                    clashNotes.Add("гвардеец удержал линию +2");
                    holdNotes.Add("гвардеец прикрыл строй +3");
                    break;

                case FighterCombatRole.Spearman:
                    int spearBonus =
                        ((context.EnemyTags & BattleEnemyTag.Charge) != 0 ||
                         (context.EnemyTags & BattleEnemyTag.Beast) != 0)
                            ? 5
                            : 2;
                    clash += spearBonus;
                    hold += 1;
                    clashNotes.Add("копейщик остановил натиск +" + spearBonus);
                    break;

                case FighterCombatRole.Healer:
                    hold += 1;
                    holdNotes.Add("лекарь стабилизировал строй +1");
                    break;
            }
        }

        int commanderLeadership = GetCommanderLeadership(context.CommanderId);
        if (commanderLeadership > 0)
        {
            int leadershipBonus = commanderLeadership * 2;
            hold += leadershipBonus;
            holdNotes.Add("командование +" + leadershipBonus);
        }

        if (context.Terrain == BattleTerrain.City)
        {
            hold += 4;
            holdNotes.Add("городские укрепления +4");
        }
        else if (context.Terrain == BattleTerrain.Ruins)
        {
            clash += 2;
            hold += 2;
            clashNotes.Add("укрытия руин +2");
        }

        ApplyDoctrineModifiers(
            context.Doctrine,
            ref approach,
            ref clash,
            ref hold,
            approachNotes,
            clashNotes,
            holdNotes);

        if (supplyPenalty > 0)
        {
            approach = Math.Max(0, approach - supplyPenalty);
            clash = Math.Max(0, clash - supplyPenalty);
            hold = Math.Max(0, hold - supplyPenalty);
            approachNotes.Add("нехватка снабжения -" + supplyPenalty);
        }

        int enemyApproach = context.EnemyPower;
        int enemyClash = context.EnemyPower;
        int enemyHold = context.EnemyPower;

        if ((context.EnemyTags & BattleEnemyTag.Ambush) != 0)
            enemyApproach += 3;
        if ((context.EnemyTags & BattleEnemyTag.Ranged) != 0)
            enemyApproach += 2;
        if ((context.EnemyTags & BattleEnemyTag.Charge) != 0)
            enemyClash += 3;
        if ((context.EnemyTags & BattleEnemyTag.Beast) != 0)
            enemyClash += 2;

        int playerTotal = approach + clash + hold;
        int enemyTotal = enemyApproach + enemyClash + enemyHold;
        int margin = playerTotal - enemyTotal;
        BattleOutcome outcome = DetermineOutcome(margin, context.Doctrine);

        BattleResult result = new BattleResult
        {
            BattleId = context.Id,
            Kind = context.Kind,
            SourceId = context.SourceId,
            Title = context.Title,
            Doctrine = context.Doctrine,
            Outcome = outcome,
            ForceRetreat =
                context.Kind == BattleKind.ExpeditionLocation &&
                (outcome == BattleOutcome.Withdrawal || outcome == BattleOutcome.Defeat)
        };

        result.Phases.Add(new BattlePhaseResult
        {
            Name = "Сближение",
            PlayerScore = approach,
            EnemyScore = enemyApproach,
            Explanation = BuildPhaseExplanation(approachNotes)
        });
        result.Phases.Add(new BattlePhaseResult
        {
            Name = "Столкновение",
            PlayerScore = clash,
            EnemyScore = enemyClash,
            Explanation = BuildPhaseExplanation(clashNotes)
        });
        result.Phases.Add(new BattlePhaseResult
        {
            Name = "Удержание/отход",
            PlayerScore = hold,
            EnemyScore = enemyHold,
            Explanation = BuildPhaseExplanation(holdNotes)
        });

        BuildFighterConsequences(
            state,
            context,
            fighters,
            margin,
            outcome,
            result);
        ApplyStrategicConsequences(state, result);
        return result;
    }

    private static void BuildFighterConsequences(
        GameState state,
        BattleContext context,
        List<FighterCombatState> fighters,
        int margin,
        BattleOutcome outcome,
        BattleResult result)
    {
        List<FighterCombatState> riskOrder =
            new List<FighterCombatState>(fighters);
        riskOrder.Sort((a, b) =>
        {
            int riskComparison =
                GetRiskScore(b, context).CompareTo(GetRiskScore(a, context));
            return riskComparison != 0
                ? riskComparison
                : string.CompareOrdinal(a.FighterId, b.FighterId);
        });

        int injurySlots = GetInjurySlots(outcome, margin, context.Doctrine, fighters.Count);
        int firstSeverity = GetFirstInjurySeverity(outcome, context.Doctrine);

        for (int i = 0; i < riskOrder.Count; i++)
        {
            FighterCombatState combatant = riskOrder[i];
            FighterData display = state.FindFighter(combatant.FighterId);
            FighterHealthState beforeState = combatant.HealthState;
            int beforeHp = (int)Math.Ceiling(combatant.HitPoints);
            FighterHealthState afterState = beforeState;

            if (i < injurySlots)
            {
                int severity = i == 0 ? firstSeverity : 1;
                bool lethalAllowed =
                    context.Doctrine == BattleDoctrine.Assault ||
                    outcome == BattleOutcome.Defeat ||
                    beforeState == FighterHealthState.SeverelyWounded;
                afterState = AdvanceHealthState(beforeState, severity, lethalAllowed);

                // Даже разгром или натиск не убивает полностью здорового бойца
                // за один бой. Такой риск становится тяжёлым ранением и виден заранее.
                if (beforeState == FighterHealthState.Healthy &&
                    afterState == FighterHealthState.Dead)
                {
                    afterState = FighterHealthState.SeverelyWounded;
                }
            }

            int afterHp = GetTargetHitPoints(combatant, beforeState, afterState);
            result.FighterConsequences.Add(new FighterBattleConsequence
            {
                FighterId = combatant.FighterId,
                FighterName = display != null ? display.Name : combatant.FighterId,
                BeforeHitPoints = beforeHp,
                AfterHitPoints = afterHp,
                BeforeState = beforeState,
                AfterState = afterState,
                RecoveryHours = CalculateRecoveryHours(afterHp, combatant.MaxHitPoints)
            });
        }

        MitigateOneConsequenceWithHealer(fighters, result);
        result.DeathPossible = result.FighterConsequences.Exists(
            consequence => consequence.AfterState == FighterHealthState.Dead);
    }

    private static void MitigateOneConsequenceWithHealer(
        List<FighterCombatState> fighters,
        BattleResult result)
    {
        bool hasHealer = fighters.Exists(
            fighter => fighter.RoleCode == FighterCombatRole.Healer &&
                       fighter.HealthState != FighterHealthState.Dead);
        if (!hasHealer)
            return;

        FighterBattleConsequence target = null;
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            if ((int)consequence.AfterState <= (int)consequence.BeforeState)
                continue;

            if (target == null || (int)consequence.AfterState > (int)target.AfterState)
                target = consequence;
        }

        if (target == null)
            return;

        FighterHealthState mitigated = PreviousHealthState(target.AfterState);
        if ((int)mitigated < (int)target.BeforeState)
            mitigated = target.BeforeState;

        target.AfterState = mitigated;
        FighterCombatState template = new FighterCombatState
        {
            MaxHitPoints = DefaultMaxHitPoints,
            HitPoints = target.BeforeHitPoints
        };
        target.AfterHitPoints = GetTargetHitPoints(
            template,
            target.BeforeState,
            mitigated);
        target.RecoveryHours =
            CalculateRecoveryHours(target.AfterHitPoints, DefaultMaxHitPoints);
        target.MitigatedByHealer = true;

        BattlePhaseResult hold = result.Phases.Count >= 3 ? result.Phases[2] : null;
        if (hold != null)
        {
            hold.Explanation +=
                (hold.Explanation.EndsWith(".") ? " " : ". ") +
                "Лекарь снизил тяжесть одной травмы.";
        }
    }

    private static void ApplyStrategicConsequences(
        GameState state,
        BattleResult result)
    {
        if (result.Kind != BattleKind.CapitalDefense)
            return;

        int foodLoss;
        int moodLoss;

        switch (result.Outcome)
        {
            case BattleOutcome.Victory:
                foodLoss = 0;
                moodLoss = 0;
                break;
            case BattleOutcome.CostlyVictory:
                foodLoss = 2;
                moodLoss = 1;
                break;
            case BattleOutcome.Withdrawal:
                foodLoss = 4;
                moodLoss = 2;
                break;
            default:
                foodLoss = 6;
                moodLoss = 3;
                break;
        }

        result.FoodDelta = -Math.Min(state.Food, foodLoss);
        result.MoodDelta = -Math.Min(state.Mood, moodLoss);
    }

    private static BattleOutcome DetermineOutcome(
        int margin,
        BattleDoctrine doctrine)
    {
        if (margin >= 10)
            return BattleOutcome.Victory;
        if (margin >= 0)
            return BattleOutcome.CostlyVictory;
        if (margin >= -10 && doctrine != BattleDoctrine.Assault)
            return BattleOutcome.Withdrawal;
        return BattleOutcome.Defeat;
    }

    private static int GetInjurySlots(
        BattleOutcome outcome,
        int margin,
        BattleDoctrine doctrine,
        int fighterCount)
    {
        if (fighterCount <= 0)
            return 0;

        int slots;
        switch (outcome)
        {
            case BattleOutcome.Victory:
                slots = margin >= 18 ? 0 : 1;
                break;
            case BattleOutcome.CostlyVictory:
                slots = 1;
                break;
            case BattleOutcome.Withdrawal:
                slots = 1;
                break;
            default:
                slots = Math.Min(3, Math.Max(1, (fighterCount + 1) / 2));
                break;
        }

        if (doctrine == BattleDoctrine.Cautious)
            slots = Math.Max(0, slots - 1);
        else if (doctrine == BattleDoctrine.Assault)
            slots = Math.Min(fighterCount, slots + 1);

        return slots;
    }

    private static int GetFirstInjurySeverity(
        BattleOutcome outcome,
        BattleDoctrine doctrine)
    {
        int severity = outcome == BattleOutcome.Defeat ? 2 : 1;
        if (doctrine == BattleDoctrine.Assault)
            severity++;
        if (doctrine == BattleDoctrine.Cautious)
            severity = Math.Max(1, severity - 1);
        return severity;
    }

    private static FighterHealthState AdvanceHealthState(
        FighterHealthState state,
        int steps,
        bool lethalAllowed)
    {
        FighterHealthState current = state;
        for (int i = 0; i < steps; i++)
        {
            if (current == FighterHealthState.Healthy)
                current = FighterHealthState.Wounded;
            else if (current == FighterHealthState.Wounded)
                current = FighterHealthState.SeverelyWounded;
            else if (current == FighterHealthState.SeverelyWounded)
            {
                if (lethalAllowed)
                    current = FighterHealthState.Dead;
                break;
            }
            else
                break;
        }
        return current;
    }

    private static FighterHealthState PreviousHealthState(FighterHealthState state)
    {
        switch (state)
        {
            case FighterHealthState.Dead:
                return FighterHealthState.SeverelyWounded;
            case FighterHealthState.SeverelyWounded:
                return FighterHealthState.Wounded;
            case FighterHealthState.Wounded:
                return FighterHealthState.Healthy;
            default:
                return FighterHealthState.Healthy;
        }
    }

    private static int GetTargetHitPoints(
        FighterCombatState combatant,
        FighterHealthState beforeState,
        FighterHealthState afterState)
    {
        int current = (int)Math.Ceiling(combatant.HitPoints);
        if (afterState == beforeState)
            return current;

        switch (afterState)
        {
            case FighterHealthState.Healthy:
                return Math.Max(current, (int)Math.Ceiling(combatant.MaxHitPoints * 0.75));
            case FighterHealthState.Wounded:
                return Math.Min(current, (int)Math.Ceiling(combatant.MaxHitPoints * 0.60));
            case FighterHealthState.SeverelyWounded:
                return Math.Min(current, (int)Math.Ceiling(combatant.MaxHitPoints * 0.25));
            case FighterHealthState.Dead:
                return 0;
            default:
                return current;
        }
    }

    private static int GetRiskScore(
        FighterCombatState fighter,
        BattleContext context)
    {
        int risk = 0;
        switch (fighter.RoleCode)
        {
            case FighterCombatRole.Guard:
                risk += 35;
                break;
            case FighterCombatRole.Spearman:
                risk += 30;
                break;
            case FighterCombatRole.Scout:
                risk += 15;
                if ((context.EnemyTags & BattleEnemyTag.Ambush) != 0)
                    risk += 15;
                break;
            case FighterCombatRole.Archer:
                risk += 8;
                break;
            case FighterCombatRole.Healer:
                if ((context.EnemyTags & BattleEnemyTag.Hunter) != 0)
                    risk += 35;
                break;
        }

        if (fighter.HealthState == FighterHealthState.Wounded)
            risk += 10;
        else if (fighter.HealthState == FighterHealthState.SeverelyWounded)
            risk += 25;

        return risk;
    }

    private static void ApplyDoctrineModifiers(
        BattleDoctrine doctrine,
        ref int approach,
        ref int clash,
        ref int hold,
        List<string> approachNotes,
        List<string> clashNotes,
        List<string> holdNotes)
    {
        if (doctrine == BattleDoctrine.Cautious)
        {
            approach = Math.Max(0, approach - 1);
            clash = (int)Math.Floor(clash * 0.90);
            hold += 3;
            holdNotes.Add("осторожная доктрина +3 к сохранению строя");
        }
        else if (doctrine == BattleDoctrine.Assault)
        {
            approach += 2;
            clash = (int)Math.Ceiling(clash * 1.15);
            hold = Math.Max(0, hold - 2);
            approachNotes.Add("натиск +2");
            clashNotes.Add("натиск усилил столкновение");
            holdNotes.Add("натиск затруднил отход -2");
        }
    }

    private static int CalculateSupplyPenalty(GameState state, BattleContext context)
    {
        if (context.Kind != BattleKind.ExpeditionLocation || !state.HasActiveExpedition)
            return 0;

        int penalty = 0;
        if (state.ArmySupply < state.ExpeditionSupplyConsumption)
            penalty += 2;
        penalty += Math.Min(4, state.ConsecutiveExpeditionSupplyShortageDays * 2);
        return penalty;
    }

    private static int GetCommanderLeadership(string commanderId)
    {
        switch (commanderId)
        {
            case "mirena":
                return 4;
            case "alric":
            case "bran":
                return 3;
            default:
                return string.IsNullOrEmpty(commanderId) ? 0 : 2;
        }
    }

    private static string GetCapitalCommanderId(GameState state)
    {
        CommanderData commander = state.GetSelectedCommander();
        return commander != null && commander.State == CommanderState.InCastle
            ? commander.Id
            : null;
    }

    private static double GetHealthCombatFactor(FighterHealthState state)
    {
        switch (state)
        {
            case FighterHealthState.Wounded:
                return 0.80;
            case FighterHealthState.SeverelyWounded:
                return 0.55;
            case FighterHealthState.Dead:
                return 0.0;
            default:
                return 1.0;
        }
    }

    private static double CalculateRecoveryHours(int hitPoints, int maxHitPoints)
    {
        if (hitPoints <= 0 || hitPoints >= maxHitPoints)
            return 0.0;
        return (maxHitPoints - hitPoints) / RecoveryHitPointsPerGameHour;
    }

    private static string BuildPhaseExplanation(List<string> notes)
    {
        return notes.Count > 0
            ? string.Join(", ", notes) + "."
            : "без специальных модификаторов.";
    }

    private static List<string> BuildConsequenceLines(BattleResult result)
    {
        List<string> lines = new List<string>();
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            if (consequence.AfterState == consequence.BeforeState &&
                consequence.AfterHitPoints == consequence.BeforeHitPoints)
            {
                continue;
            }

            string text =
                consequence.FighterName + ": " +
                consequence.BeforeHitPoints + "→" + consequence.AfterHitPoints +
                " HP, " + GetHealthLabel(consequence.AfterState);
            if (consequence.MitigatedByHealer)
                text += " (лекарь смягчил травму)";
            if (consequence.AfterState != FighterHealthState.Dead &&
                consequence.RecoveryHours > 0.0)
            {
                text += ", восстановление ≈" +
                    Math.Ceiling(consequence.RecoveryHours) + " ч.";
            }
            lines.Add(text);
        }
        return lines;
    }

    private static string FormatSigned(int value)
    {
        return value > 0 ? "+" + value : value.ToString();
    }

    private static LocationEncounterProfile FindLocationEncounter(string locationId)
    {
        foreach (LocationEncounterProfile encounter in LocationEncounters)
        {
            if (encounter.LocationId == locationId)
                return encounter;
        }
        return null;
    }

    private static BattleRuntimeState GetRuntime(GameState state)
    {
        return RuntimeStates.GetValue(state, CreateRuntime);
    }

    private static BattleRuntimeState CreateRuntime(GameState state)
    {
        BattleRuntimeState runtime = new BattleRuntimeState();
        SyncFighters(state, runtime);
        return runtime;
    }

    private static void SyncFighters(GameState state, BattleRuntimeState runtime)
    {
        if (state == null || state.Fighters == null)
            return;

        foreach (FighterData fighter in state.Fighters)
        {
            if (runtime.Fighters.ContainsKey(fighter.Id))
                continue;

            CombatProfile profile;
            if (!KnownProfiles.TryGetValue(fighter.Id, out profile))
            {
                profile = new CombatProfile(
                    FighterCombatRole.Unknown,
                    Math.Max(1, fighter.DefensePower));
            }

            runtime.Fighters[fighter.Id] = new FighterCombatState
            {
                FighterId = fighter.Id,
                RoleCode = profile.Role,
                AttackPower = profile.Attack,
                DefensePower = Math.Max(1, fighter.DefensePower),
                MaxHitPoints = DefaultMaxHitPoints,
                HitPoints = DefaultMaxHitPoints
            };
        }
    }
}
