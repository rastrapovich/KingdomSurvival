using System;
using System.Collections.Generic;

// Первая версия эффектов из §10 инструкции. Изменение снабжения,
// времени пути, предметов, локаций и состояния места подключается позже
// отдельными обработчиками — здесь их сознательно нет.
public enum NarrativeEffectType
{
    SetFlag,
    ClearFlag,
    AddKnowledge,
    ChangeRelation,
    UnlockCheck,
    GrantTrait,
    RemoveTrait
}

[Serializable]
public sealed class NarrativeEffect
{
    // Стабильный ID применения. Обязателен для каждого эффекта: повторный
    // рендер текста не должен применить эффект второй раз (см. §10).
    public string EffectExecutionId = string.Empty;

    public NarrativeEffectType Type;

    // Смысл зависит от Type: ID флага/знания/особенности либо ID субъекта
    // отношения; для UnlockCheck — ID блокируемой проверки.
    public string StringParam = string.Empty;

    // Используется только ChangeRelation (дельта отношения).
    public int IntParam;

    public void Apply(NarrativeEvaluationContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(EffectExecutionId))
            throw new InvalidOperationException("Narrative effect must have a stable EffectExecutionId.");

        if (context.State.HasEffectApplied(EffectExecutionId))
            return;

        switch (Type)
        {
            case NarrativeEffectType.SetFlag:
                context.State.SetFlag(StringParam);
                break;
            case NarrativeEffectType.ClearFlag:
                context.State.ClearFlag(StringParam);
                break;
            case NarrativeEffectType.AddKnowledge:
                context.State.AddKnowledge(StringParam);
                break;
            case NarrativeEffectType.ChangeRelation:
                context.State.ChangeRelation(StringParam, IntParam);
                break;
            case NarrativeEffectType.UnlockCheck:
                context.State.UnlockCheck(StringParam);
                break;
            case NarrativeEffectType.GrantTrait:
                context.Hero.GrantTrait(StringParam);
                break;
            case NarrativeEffectType.RemoveTrait:
                context.Hero.RemoveTrait(StringParam);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Type), Type, null);
        }

        context.State.MarkEffectApplied(EffectExecutionId);
    }
}

public static class NarrativeEffectApplier
{
    public static void ApplyAll(IEnumerable<NarrativeEffect> effects, NarrativeEvaluationContext context)
    {
        if (effects == null)
            return;

        foreach (NarrativeEffect effect in effects)
            effect?.Apply(context);
    }
}
