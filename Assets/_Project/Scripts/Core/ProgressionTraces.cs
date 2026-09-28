using System;
using System.Collections.Generic;

// 12Е-8: следы развития (каталог §0.1, §1 п. 5). След — пережитое в разных
// значимых ситуациях: «нет → замечено → характерно». Ситуация — один бой с
// устойчивым ID; за бой каждый след засчитывается не больше одного раза,
// поэтому нафармить его нельзя. След на ступени «характерно» открывает
// кандидата в предложении выбора: особенность становится личным вариантом
// и открывается следом вместо требования компетенции. Решения автора
// 28.09.2026; набор следов, условия и пороги — [РАБОЧЕЕ].

public enum TraceStage
{
    None,
    Noticed,
    Characteristic
}

[Serializable]
public sealed class PersonTraceData
{
    public string TraceId = string.Empty;
    // Засчитанные ситуации («battle:<ID боя>»).
    public List<string> SituationKeys = new List<string>();
}

public sealed class TraceDefinition
{
    public readonly string Id;
    public readonly string Name;
    // Какую особенность открывает на ступени «характерно».
    public readonly string FeatureId;

    public TraceDefinition(string id, string name, string featureId)
    {
        Id = id;
        Name = name;
        FeatureId = featureId;
    }
}

public static class TraceIds
{
    public const string Stance = "trace.stance";
    public const string ManyDefenses = "trace.many_defenses";
    public const string ManyRetaliations = "trace.many_retaliations";
    public const string ShotFromPlace = "trace.shot_from_place";
    public const string FoughtBadlyWounded = "trace.fought_badly_wounded";
    public const string SurvivedHeavyWound = "trace.survived_heavy_wound";
    public const string RetreatForWounded = "trace.retreat_for_wounded";
}

public static class ProgressionTraces
{
    public const string Tag = "[СЛЕД]";

    // Условия внутри боя [РАБОЧЕЕ]: «много защит» — целью не меньше трёх
    // атак, «много ответных ударов» — не меньше двух.
    public const int ManyDefensesAttacks = 3;
    public const int ManyRetaliationsCount = 2;

    public static readonly IReadOnlyList<TraceDefinition> Definitions = new List<TraceDefinition>
    {
        new TraceDefinition(TraceIds.Stance, "защищался в стойке", CombatFeatureIds.FirstStrike),
        new TraceDefinition(TraceIds.ManyDefenses, "много защит", CombatFeatureIds.BasicDefense),
        new TraceDefinition(TraceIds.ManyRetaliations, "много ответных ударов", CombatFeatureIds.Counterstrike),
        new TraceDefinition(TraceIds.ShotFromPlace, "стрелял с места", CombatFeatureIds.ColdEye),
        new TraceDefinition(TraceIds.FoughtBadlyWounded, "сражался тяжелораненым", CombatFeatureIds.Stubborn),
        new TraceDefinition(TraceIds.SurvivedHeavyWound, "пережил тяжёлые раны", CombatFeatureIds.StillStanding),
        new TraceDefinition(TraceIds.RetreatForWounded, "прерывал выгоду ради раненых", CombatFeatureIds.LeavesNoOne)
    };

    public static TraceDefinition Find(string traceId)
    {
        foreach (TraceDefinition definition in Definitions)
        {
            if (definition.Id == traceId)
                return definition;
        }
        return null;
    }

    public static int SituationCount(GameState state, string personId, string traceId)
    {
        PersonTraceData trace = FindData(CharacterProgressionService.Get(state, personId), traceId);
        return trace != null ? trace.SituationKeys.Count : 0;
    }

    public static TraceStage Stage(GameState state, string personId, string traceId)
    {
        return StageFor(SituationCount(state, personId, traceId));
    }

    // Пороги — из «Базы развития» (ProgressionRules).
    public static TraceStage StageFor(int situations)
    {
        ProgressionRules rules = ProgressionRules.Current;
        if (situations >= Math.Max(1, rules.TraceCharacteristicSituations))
            return TraceStage.Characteristic;
        if (situations >= Math.Max(1, rules.TraceNoticedSituations))
            return TraceStage.Noticed;
        return TraceStage.None;
    }

    public static string StageLabel(TraceStage stage)
    {
        switch (stage)
        {
            case TraceStage.Characteristic:
                return "характерно";
            case TraceStage.Noticed:
                return "замечено";
            default:
                return "нет";
        }
    }

    // Засчитать ситуацию. True — ступень выросла (newStage — новая).
    public static bool Note(GameState state, string personId, string traceId, string situationKey, out TraceStage newStage)
    {
        newStage = TraceStage.None;
        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        if (record == null || Find(traceId) == null || string.IsNullOrWhiteSpace(situationKey))
            return false;
        if (record.Traces == null)
            record.Traces = new List<PersonTraceData>();

        PersonTraceData trace = FindData(record, traceId);
        if (trace == null)
        {
            trace = new PersonTraceData { TraceId = traceId };
            record.Traces.Add(trace);
        }
        if (trace.SituationKeys == null)
            trace.SituationKeys = new List<string>();

        TraceStage before = StageFor(trace.SituationKeys.Count);
        if (trace.SituationKeys.Contains(situationKey))
        {
            newStage = before;
            return false;
        }
        trace.SituationKeys.Add(situationKey);
        newStage = StageFor(trace.SituationKeys.Count);
        return newStage > before;
    }

    // Следы человека, у которых есть хоть одна ступень, — для карточки.
    public static List<KeyValuePair<TraceDefinition, TraceStage>> Visible(GameState state, string personId)
    {
        List<KeyValuePair<TraceDefinition, TraceStage>> visible = new List<KeyValuePair<TraceDefinition, TraceStage>>();
        foreach (TraceDefinition definition in Definitions)
        {
            TraceStage stage = Stage(state, personId, definition.Id);
            if (stage != TraceStage.None)
                visible.Add(new KeyValuePair<TraceDefinition, TraceStage>(definition, stage));
        }
        return visible;
    }

    // След на ступени «характерно», который открывает эту особенность.
    public static TraceDefinition OpeningTrace(GameState state, string personId, string featureId)
    {
        foreach (TraceDefinition definition in Definitions)
        {
            if (definition.FeatureId == featureId && Stage(state, personId, definition.Id) == TraceStage.Characteristic)
                return definition;
        }
        return null;
    }

    // «[СЛЕД] Гаррик — «защищался в стойке»: замечено.»
    public static string Line(GameState state, string personId, TraceDefinition definition, TraceStage stage)
    {
        return Tag + " " + CharacterProgressionService.DisplayName(state, personId) + " — «" + definition.Name + "»: " +
               StageLabel(stage) + ".";
    }

    public static bool IsTraceLine(string line)
    {
        return line != null && line.StartsWith(Tag, StringComparison.Ordinal);
    }

    // Отход, при котором кто-то из отряда пал или остался на четверти
    // здоровья и меньше. Считается до применения итога боя.
    public static bool IsRetreatForWounded(GameState state, CampaignBattleResult result)
    {
        if (state == null || result == null || result.Outcome != CampaignBattleOutcome.Retreat)
            return false;
        CommanderData hero = state.GetSelectedCommander();
        foreach (string personId in result.FallenPersonIds ?? new List<string>())
        {
            if (hero == null || personId != hero.Id)
                return true;
        }
        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            ResidentState resident = HomePeopleService.Find(state, survivor.PersonId);
            if (resident != null && resident.MaxHitPoints > 0 && survivor.HitPoints * 4 <= resident.MaxHitPoints)
                return true;
        }
        return false;
    }

    // Следы боя (после применения итога: здоровье и тяжёлые раны уже
    // записаны). Возвращает строки о выросших ступенях.
    public static List<string> RecordBattle(GameState state, CampaignBattleResult result, bool retreatForWounded,
        ICollection<string> woundedBeforeBattle)
    {
        List<string> lines = new List<string>();
        if (state == null || result == null || string.IsNullOrWhiteSpace(result.BattleId))
            return lines;
        string situation = "battle:" + result.BattleId;

        foreach (CampaignBattleContribution contribution in result.Contributions ?? new List<CampaignBattleContribution>())
        {
            if (contribution == null || !IsAlive(state, contribution.PersonId))
                continue;
            if (contribution.DamagePrevented > 0)
                Add(state, contribution.PersonId, TraceIds.Stance, situation, lines);
            if (contribution.TimesAttacked >= ManyDefensesAttacks)
                Add(state, contribution.PersonId, TraceIds.ManyDefenses, situation, lines);
            if (contribution.Retaliations >= ManyRetaliationsCount)
                Add(state, contribution.PersonId, TraceIds.ManyRetaliations, situation, lines);
            if (contribution.ShotFromPlace)
                Add(state, contribution.PersonId, TraceIds.ShotFromPlace, situation, lines);
        }

        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            ResidentState resident = HomePeopleService.Find(state, survivor.PersonId);
            if (resident == null || !resident.IsAlive)
                continue;
            if (resident.MaxHitPoints > 0 && survivor.HitPoints * 4 <= resident.MaxHitPoints)
                Add(state, survivor.PersonId, TraceIds.FoughtBadlyWounded, situation, lines);
            // Тяжёлая рана — полученная в этом бою.
            if (resident.Injury == ResidentInjury.Recovering && (woundedBeforeBattle == null || !woundedBeforeBattle.Contains(survivor.PersonId)))
                Add(state, survivor.PersonId, TraceIds.SurvivedHeavyWound, situation, lines);
        }

        CommanderData hero = state.GetSelectedCommander();
        if (retreatForWounded && hero != null)
            Add(state, hero.Id, TraceIds.RetreatForWounded, situation, lines);
        return lines;
    }

    private static void Add(GameState state, string personId, string traceId, string situation, List<string> lines)
    {
        if (Note(state, personId, traceId, situation, out TraceStage stage))
            lines.Add(Line(state, personId, Find(traceId), stage));
    }

    private static bool IsAlive(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        return resident != null ? resident.IsAlive : CharacterProgressionService.IsProgressing(state, personId);
    }

    private static PersonTraceData FindData(PersonProgressionData record, string traceId)
    {
        if (record?.Traces == null)
            return null;
        foreach (PersonTraceData trace in record.Traces)
        {
            if (trace != null && trace.TraceId == traceId)
                return trace;
        }
        return null;
    }
}
