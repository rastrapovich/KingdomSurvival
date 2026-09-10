using System;
using System.Collections.Generic;

// Минимальный набор условий из §9 инструкции. Никакого языка выражений —
// только типизированные записи с параметрами.
public enum NarrativeConditionType
{
    FlagSet,
    KnowledgeKnown,
    RelationAtLeast,
    RelationAtMost,
    CompanionPresent,
    ItemPresent,
    QualityAtLeast,
    CompetencyAtLeast,
    CheckSucceeded,
    CheckFailed,
    CheckNotAttempted,
    TraitPresent,

    // P09-T03 ("Трое под телегой"): нужно ветвить по РАЗМЕРУ отряда, а не
    // по присутствию конкретного спутника — CompanionPresent для этого не
    // подходит (проверяет один заданный ID). IntParam — минимальный
    // требуемый размер; вместе с Negate этого достаточно и для "меньше N"
    // (не(PartySizeAtLeast(N))), отдельный тип AtMost не нужен. Добавлено
    // в конец enum — существующие числовые значения уже сериализованы по
    // всей базе диалогов и не должны сдвигаться.
    PartySizeAtLeast
}

[Serializable]
public sealed class NarrativeCondition
{
    public NarrativeConditionType Type;

    // Смысл зависит от Type: ID флага/знания/спутника/предмета/компетенции/
    // проверки/особенности либо ID субъекта отношения.
    public string StringParam = string.Empty;

    // Используется только для QualityAtLeast.
    public HeroQuality QualityParam;

    // Используется для RelationAtLeast/RelationAtMost/QualityAtLeast/CompetencyAtLeast.
    public int IntParam;

    // Инвертирует результат — так поддерживается "отсутствует флаг",
    // "проверка ещё не провалена" и т. д. без отдельных типов условий.
    public bool Negate;

    public bool Evaluate(NarrativeEvaluationContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        bool raw = EvaluateRaw(context);
        return Negate ? !raw : raw;
    }

    private bool EvaluateRaw(NarrativeEvaluationContext context)
    {
        switch (Type)
        {
            case NarrativeConditionType.FlagSet:
                return context.State.HasFlag(StringParam);
            case NarrativeConditionType.KnowledgeKnown:
                return context.State.HasKnowledge(StringParam);
            case NarrativeConditionType.RelationAtLeast:
                return context.State.GetRelation(StringParam) >= IntParam;
            case NarrativeConditionType.RelationAtMost:
                return context.State.GetRelation(StringParam) <= IntParam;
            case NarrativeConditionType.CompanionPresent:
                return context.IsCompanionPresent(StringParam);
            case NarrativeConditionType.ItemPresent:
                return context.IsItemPresent(StringParam);
            case NarrativeConditionType.QualityAtLeast:
                return context.Hero.GetQuality(QualityParam) >= IntParam;
            case NarrativeConditionType.CompetencyAtLeast:
                return context.Hero.GetCompetency(StringParam) >= IntParam;
            case NarrativeConditionType.CheckSucceeded:
                return context.State.GetLastOutcome(StringParam) == true;
            case NarrativeConditionType.CheckFailed:
                return context.State.GetLastOutcome(StringParam) == false;
            case NarrativeConditionType.CheckNotAttempted:
                return context.State.GetLastOutcome(StringParam) == null;
            case NarrativeConditionType.TraitPresent:
                return context.Hero.HasTrait(StringParam);
            case NarrativeConditionType.PartySizeAtLeast:
                return context.PresentCompanionIds.Count >= IntParam;
            default:
                throw new ArgumentOutOfRangeException(nameof(Type), Type, null);
        }
    }
}

public enum NarrativeConditionCombinator
{
    All,
    Any
}

// Группа условий. Пустая группа считается выполненной — это осознанное
// поведение "нет условий = ничего не блокирует".
[Serializable]
public sealed class NarrativeConditionGroup
{
    public NarrativeConditionCombinator Combinator = NarrativeConditionCombinator.All;
    public List<NarrativeCondition> Conditions = new List<NarrativeCondition>();

    public bool Evaluate(NarrativeEvaluationContext context)
    {
        if (Conditions == null || Conditions.Count == 0)
            return true;

        if (Combinator == NarrativeConditionCombinator.All)
        {
            for (int i = 0; i < Conditions.Count; i++)
            {
                NarrativeCondition condition = Conditions[i];
                if (condition != null && !condition.Evaluate(context))
                    return false;
            }
            return true;
        }

        for (int i = 0; i < Conditions.Count; i++)
        {
            NarrativeCondition condition = Conditions[i];
            if (condition != null && condition.Evaluate(context))
                return true;
        }
        return false;
    }
}
