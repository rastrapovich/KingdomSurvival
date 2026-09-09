using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum NarrativeCheckKind
{
    Passive,
    ActiveReturnable,
    ActiveDecisive
}

// Именованные значения сложности из §5. Нечётные промежуточные значения
// разрешены для тонкой настройки — это не единственные допустимые числа,
// только удобные ориентиры.
public static class NarrativeDifficulty
{
    public const int Obvious = 9;
    public const int Simple = 11;
    public const int Ordinary = 13;
    public const int Demanding = 15;
    public const int Hard = 17;
    public const int VeryHard = 19;
    public const int Exceptional = 21;
    public const int Legendary = 23;

    public const int MinDifficulty = Obvious;
    public const int MaxDifficulty = Legendary;

    public static bool IsInValidRange(int difficulty)
    {
        return difficulty >= MinDifficulty && difficulty <= MaxDifficulty;
    }
}

public static class NarrativeCheckMath
{
    public const int PassiveBase = 6;
    public const int MinContextModifier = -3;
    public const int MaxContextModifier = 3;

    // Минимальный/максимальный возможный результат 2d6.
    public const int MinDieSum = 2;
    public const int MaxDieSum = 12;

    public static int ClampQuality(int value)
    {
        return Clamp(value, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
    }

    public static int ClampCompetency(int value)
    {
        return Clamp(value, 0, 5);
    }

    public static int ClampContextModifier(int value)
    {
        return Clamp(value, MinContextModifier, MaxContextModifier);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

// Точное распределение суммы 2d6 (36 равновероятных исходов), без
// симуляции. См. §5 инструкции.
public static class NarrativeProbabilityTable
{
    // Количество исходов (из 36) с суммой >= порога, индекс 0 соответствует
    // порогу 2, индекс 11 — порогу 13.
    private static readonly int[] SuccessOutcomeCounts = { 36, 35, 33, 30, 26, 21, 15, 10, 6, 3, 1, 0 };

    public static double GetSuccessProbability(int requiredRoll)
    {
        int clamped = requiredRoll;
        if (clamped < NarrativeCheckMath.MinDieSum)
            clamped = NarrativeCheckMath.MinDieSum;
        if (clamped > NarrativeCheckMath.MinDieSum + SuccessOutcomeCounts.Length - 1)
            clamped = NarrativeCheckMath.MinDieSum + SuccessOutcomeCounts.Length - 1;

        int index = clamped - NarrativeCheckMath.MinDieSum;
        return SuccessOutcomeCounts[index] / 36.0;
    }
}

// Стабильный детерминированный ГПСЧ. Не использует UnityEngine.Random,
// общий System.Random или string.GetHashCode() (нестабилен между
// платформами/запусками) — только FNV-1a по UTF8-байтам и SplitMix64.
public static class NarrativeDeterministicRandom
{
    public static ulong ComputeSeed(int worldSeed, string checkId, int attemptNumber)
    {
        string key =
            worldSeed.ToString(CultureInfo.InvariantCulture) + "|" +
            (checkId ?? string.Empty) + "|" +
            attemptNumber.ToString(CultureInfo.InvariantCulture);
        return Fnv1a64(key);
    }

    public static void RollTwoDice(int worldSeed, string checkId, int attemptNumber, out int dieOne, out int dieTwo)
    {
        ulong state = ComputeSeed(worldSeed, checkId, attemptNumber);
        dieOne = 1 + (int)(NextSplitMix64(ref state) % 6);
        dieTwo = 1 + (int)(NextSplitMix64(ref state) % 6);
    }

    private static ulong Fnv1a64(string text)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }
        return hash;
    }

    private static ulong NextSplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

// Контекстный модификатор с условием применения (§5): применяется, только
// если Condition выполняется в текущем NarrativeEvaluationContext.
[Serializable]
public sealed class NarrativeContextModifierRule
{
    public string SourceId = string.Empty;
    public string Label = string.Empty;
    public int Value;
    public NarrativeConditionGroup Condition = new NarrativeConditionGroup();

    public bool IsApplicable(NarrativeEvaluationContext context)
    {
        return Condition == null || Condition.Evaluate(context);
    }
}

// Неизменяемый снимок одного реально сработавшего контекстного
// модификатора в момент расчёта проверки. Хранится в NarrativeCheckResult
// (не в NarrativeCheckSpec/NarrativeContextModifierRule), потому что
// игровое состояние к моменту показа tooltip могло уже измениться — см.
// §8 инструкции по визуализации проверок ("tooltip обязан показывать
// снимок фактического расчёта, а не пересчитывать его заново").
[Serializable]
public sealed class NarrativeAppliedModifierSnapshot
{
    public string SourceId = string.Empty;
    public string Label = string.Empty;
    public int Value;

    public NarrativeAppliedModifierSnapshot()
    {
    }

    public NarrativeAppliedModifierSnapshot(string sourceId, string label, int value)
    {
        SourceId = sourceId ?? string.Empty;
        Label = label ?? string.Empty;
        Value = value;
    }
}

[Serializable]
public sealed class NarrativeCheckSpec
{
    public string CheckId = string.Empty;
    public NarrativeCheckKind Kind;
    public HeroQuality Quality;

    // Пусто — компетенция не участвует в проверке.
    public string CompetencyId = string.Empty;

    public int Difficulty = NarrativeDifficulty.Ordinary;
    public List<NarrativeContextModifierRule> ModifierRules = new List<NarrativeContextModifierRule>();
}

public readonly struct NarrativeCheckMathBreakdown
{
    public readonly int Quality;
    public readonly int Competency;
    public readonly int RawContextModifier;
    public readonly int AppliedContextModifier;
    public readonly int Bonus;
    public readonly IReadOnlyList<NarrativeContextModifierRule> AppliedModifiers;

    public NarrativeCheckMathBreakdown(
        int quality,
        int competency,
        int rawContextModifier,
        int appliedContextModifier,
        IReadOnlyList<NarrativeContextModifierRule> appliedModifiers)
    {
        Quality = quality;
        Competency = competency;
        RawContextModifier = rawContextModifier;
        AppliedContextModifier = appliedContextModifier;
        Bonus = quality + competency + appliedContextModifier;
        AppliedModifiers = appliedModifiers;
    }
}

public enum NarrativeCheckForcedOutcome
{
    None,
    ForceSuccess,
    ForceFailure
}

public enum NarrativeCheckAttemptOutcome
{
    Resolved,
    Blocked,
    AlreadyDecided
}

public sealed class NarrativeCheckAttempt
{
    public NarrativeCheckAttemptOutcome Outcome;
    public NarrativeCheckResult Result;
    public string BlockReason;
}

// Единственная точка математики и учёта состояния проверок. Условия,
// текст и переходы (диалоги/Encounter) сюда не входят — см. §7-§8.
public static class NarrativeCheckResolver
{
    public static NarrativeCheckMathBreakdown ComputeBreakdown(NarrativeCheckSpec spec, NarrativeEvaluationContext context)
    {
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        int quality = context.Hero.GetQuality(spec.Quality);
        int competency = string.IsNullOrWhiteSpace(spec.CompetencyId)
            ? 0
            : context.Hero.GetCompetency(spec.CompetencyId);

        int rawContext = 0;
        List<NarrativeContextModifierRule> applied = new List<NarrativeContextModifierRule>();
        if (spec.ModifierRules != null)
        {
            for (int i = 0; i < spec.ModifierRules.Count; i++)
            {
                NarrativeContextModifierRule rule = spec.ModifierRules[i];
                if (rule == null || !rule.IsApplicable(context))
                    continue;

                rawContext += rule.Value;
                applied.Add(rule);
            }
        }

        int appliedContext = NarrativeCheckMath.ClampContextModifier(rawContext);
        return new NarrativeCheckMathBreakdown(quality, competency, rawContext, appliedContext, applied);
    }

    public static NarrativeCheckResult ResolvePassive(NarrativeCheckSpec spec, NarrativeEvaluationContext context)
    {
        RequireKind(spec, NarrativeCheckKind.Passive);
        NarrativeCheckMathBreakdown breakdown = ComputeBreakdown(spec, context);
        int total = NarrativeCheckMath.PassiveBase + breakdown.Bonus;
        bool success = total >= spec.Difficulty;

        NarrativeCheckResult result = new NarrativeCheckResult(
            spec.CheckId,
            attemptNumber: 1,
            success,
            dieOne: 0,
            dieTwo: 0,
            breakdown.Quality,
            breakdown.Competency,
            breakdown.RawContextModifier,
            breakdown.AppliedContextModifier,
            total,
            spec.Difficulty,
            appliedModifiers: BuildAppliedModifierSnapshots(breakdown.AppliedModifiers));

        context.State.RecordCheckResult(NarrativeCheckKind.Passive, result);
        return result;
    }

    // Копия реально сработавших правил в момент расчёта — см. каммент
    // NarrativeAppliedModifierSnapshot. Список правил (NarrativeContextModifierRule)
    // сам по себе не сериализуется в результат: только неизменяемый снимок.
    private static List<NarrativeAppliedModifierSnapshot> BuildAppliedModifierSnapshots(
        IReadOnlyList<NarrativeContextModifierRule> appliedModifiers)
    {
        List<NarrativeAppliedModifierSnapshot> snapshots = new List<NarrativeAppliedModifierSnapshot>();
        if (appliedModifiers == null)
            return snapshots;

        for (int i = 0; i < appliedModifiers.Count; i++)
        {
            NarrativeContextModifierRule rule = appliedModifiers[i];
            if (rule == null)
                continue;

            snapshots.Add(new NarrativeAppliedModifierSnapshot(rule.SourceId, rule.Label, rule.Value));
        }

        return snapshots;
    }

    public static double GetActiveSuccessProbability(NarrativeCheckSpec spec, NarrativeEvaluationContext context)
    {
        RequireActiveKind(spec);
        NarrativeCheckMathBreakdown breakdown = ComputeBreakdown(spec, context);
        int requiredRoll = spec.Difficulty - breakdown.Bonus;
        return NarrativeProbabilityTable.GetSuccessProbability(requiredRoll);
    }

    // Проверяет, можно ли прямо сейчас совершить попытку, не тратя её и не
    // трогая состояние. Используется для построения списка доступных/
    // заблокированных вариантов (§12) без побочных эффектов.
    public static NarrativeCheckAttemptOutcome PeekAvailability(
        NarrativeCheckSpec spec,
        NarrativeStateData state,
        out NarrativeCheckResult existingResult,
        out string blockReason)
    {
        RequireActiveKind(spec);
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        existingResult = null;
        blockReason = null;

        NarrativeCheckHistoryEntry history = state.FindHistory(spec.CheckId);

        if (spec.Kind == NarrativeCheckKind.ActiveDecisive && history != null && history.Attempts.Count > 0)
        {
            existingResult = history.LatestResult;
            return NarrativeCheckAttemptOutcome.AlreadyDecided;
        }

        if (spec.Kind == NarrativeCheckKind.ActiveReturnable && history != null && history.IsLocked)
        {
            blockReason = "Проверка заблокирована после провала и ждёт разблокировки.";
            return NarrativeCheckAttemptOutcome.Blocked;
        }

        return NarrativeCheckAttemptOutcome.Resolved;
    }

    public static NarrativeCheckAttempt TryResolveActive(
        NarrativeCheckSpec spec,
        NarrativeEvaluationContext context,
        int worldSeed)
    {
        return ResolveActiveCore(spec, context, worldSeed, NarrativeCheckForcedOutcome.None);
    }

    // Только для авторского Preview в редакторе. Игровой runtime обязан
    // вызывать только TryResolveActive — см. §13/§20 (тест "игровой runtime
    // не содержит режима принудительного результата").
    public static NarrativeCheckAttempt TryResolveActiveForPreview(
        NarrativeCheckSpec spec,
        NarrativeEvaluationContext context,
        int worldSeed,
        NarrativeCheckForcedOutcome forcedOutcome)
    {
        return ResolveActiveCore(spec, context, worldSeed, forcedOutcome);
    }

    private static NarrativeCheckAttempt ResolveActiveCore(
        NarrativeCheckSpec spec,
        NarrativeEvaluationContext context,
        int worldSeed,
        NarrativeCheckForcedOutcome forcedOutcome)
    {
        RequireActiveKind(spec);
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        NarrativeCheckAttemptOutcome peek = PeekAvailability(spec, context.State, out NarrativeCheckResult existingResult, out string blockReason);
        if (peek == NarrativeCheckAttemptOutcome.AlreadyDecided)
            return new NarrativeCheckAttempt { Outcome = peek, Result = existingResult };
        if (peek == NarrativeCheckAttemptOutcome.Blocked)
            return new NarrativeCheckAttempt { Outcome = peek, BlockReason = blockReason };

        NarrativeCheckHistoryEntry history = context.State.FindHistory(spec.CheckId);
        int attemptNumber = (history?.Attempts.Count ?? 0) + 1;
        NarrativeCheckMathBreakdown breakdown = ComputeBreakdown(spec, context);
        List<NarrativeAppliedModifierSnapshot> appliedModifiers = BuildAppliedModifierSnapshots(breakdown.AppliedModifiers);

        NarrativeCheckResult result;
        if (forcedOutcome == NarrativeCheckForcedOutcome.None)
        {
            NarrativeDeterministicRandom.RollTwoDice(worldSeed, spec.CheckId, attemptNumber, out int dieOne, out int dieTwo);
            int total = dieOne + dieTwo + breakdown.Bonus;
            bool success = total >= spec.Difficulty;
            result = new NarrativeCheckResult(
                spec.CheckId,
                attemptNumber,
                success,
                dieOne,
                dieTwo,
                breakdown.Quality,
                breakdown.Competency,
                breakdown.RawContextModifier,
                breakdown.AppliedContextModifier,
                total,
                spec.Difficulty,
                appliedModifiers: appliedModifiers);
        }
        else
        {
            bool success = forcedOutcome == NarrativeCheckForcedOutcome.ForceSuccess;
            result = new NarrativeCheckResult(
                spec.CheckId,
                attemptNumber,
                success,
                dieOne: 0,
                dieTwo: 0,
                breakdown.Quality,
                breakdown.Competency,
                breakdown.RawContextModifier,
                breakdown.AppliedContextModifier,
                total: breakdown.Bonus,
                spec.Difficulty,
                isForcedByPreview: true,
                appliedModifiers: appliedModifiers);
        }

        context.State.RecordCheckResult(spec.Kind, result);
        return new NarrativeCheckAttempt { Outcome = NarrativeCheckAttemptOutcome.Resolved, Result = result };
    }

    private static void RequireActiveKind(NarrativeCheckSpec spec)
    {
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));
        if (spec.Kind != NarrativeCheckKind.ActiveReturnable && spec.Kind != NarrativeCheckKind.ActiveDecisive)
        {
            throw new ArgumentException(
                "Ожидалась активная проверка (ActiveReturnable/ActiveDecisive), получено: " + spec.Kind,
                nameof(spec));
        }
    }

    private static void RequireKind(NarrativeCheckSpec spec, NarrativeCheckKind expected)
    {
        if (spec == null)
            throw new ArgumentNullException(nameof(spec));
        if (spec.Kind != expected)
        {
            throw new ArgumentException(
                "Ожидался тип проверки " + expected + ", получено: " + spec.Kind,
                nameof(spec));
        }
    }
}
