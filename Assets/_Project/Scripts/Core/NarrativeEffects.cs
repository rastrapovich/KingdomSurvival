using System;
using System.Collections.Generic;

// Первая версия эффектов из §10 инструкции, расширенная минимальным
// Gameplay Effects слоем для Encounter-системы (§63/§106): предметы и
// ресурсы. AdvanceTime сознательно не добавлен — игровые часы живут во
// внутреннем RuntimeState ContinuousSimulationSystem, а не в GameState,
// и трогать этот приватный рантайм-класс вслепую отсюда рискованно;
// изменение локаций/карты/временных состояний по-прежнему подключается
// позже отдельными обработчиками. Новые значения — строго в конец enum,
// существующие уже сериализованы по всей базе диалогов.
public enum NarrativeEffectType
{
    SetFlag,
    ClearFlag,
    AddKnowledge,
    ChangeRelation,
    UnlockCheck,
    GrantTrait,
    RemoveTrait,
    GrantItem,
    RemoveItem,
    ChangeFood,
    ChangeSupplies,

    // Продвигает ExpeditionData.RouteIndex на N клеток вперёд (та же
    // WorldMapNavigation.AdvanceRouteByCells, которой legacy
    // ExpeditionIncidentSystem/ExpeditionDecisionSystem уже пользуются
    // напрямую) — чистая мутация GameState.ActiveExpedition, не трогает
    // внутренний RuntimeState симуляции. Только сокращение пути (IntParam >
    // 0 клеток вперёд); задержка/остановка (Road Stop activity) сюда
    // сознательно не добавлена — это отдельный, более рискованный путь
    // (ActiveActivity пересекается с паузой/модальной очередью).
    ShortcutRouteCells
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
            case NarrativeEffectType.GrantItem:
                context.State.GrantItem(StringParam);
                break;
            case NarrativeEffectType.RemoveItem:
                context.State.RemoveItem(StringParam);
                break;
            case NarrativeEffectType.ChangeFood:
                if (context.GameState != null)
                    context.GameState.Food = Math.Max(0, context.GameState.Food + IntParam);
                break;
            case NarrativeEffectType.ChangeSupplies:
                if (context.GameState != null)
                    context.GameState.ArmySupply = Math.Max(0, context.GameState.ArmySupply + IntParam);
                break;
            case NarrativeEffectType.ShortcutRouteCells:
                if (context.GameState?.ActiveExpedition != null && IntParam > 0)
                    WorldMapNavigation.AdvanceRouteByCells(context.GameState.ActiveExpedition, IntParam);
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
