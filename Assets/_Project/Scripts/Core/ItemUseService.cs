using System;
using System.Collections.Generic;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §4): использование вещи по эффектам из
// «Базы предметов» — без switch по ItemId. Один resolver и для подписи на
// карточке (Describe), и для превью (Preview), и для применения (TryUse):
// игрок видит ровно то, что произойдёт.
//
// Пакет: сначала проверяются все эффекты и цель, затем применяются только
// полезные и списывается одно использование. Если пользы нет ни от одного
// эффекта (HP полные, изнеможения нет, знание известно) — заряд не тратится,
// причина объясняется.
public sealed class ItemUsePreview
{
    public bool CanUse;
    public string Reason = string.Empty;
    public string TargetPersonId = string.Empty;
    // Что произойдёт («Гаррик: +4 HP»).
    public readonly List<string> Lines = new List<string>();
    // Почему часть эффектов не сработает.
    public readonly List<string> Skipped = new List<string>();
    internal readonly List<ItemUseEffect> Applicable = new List<ItemUseEffect>();
}

public static class ItemUseService
{
    // Подпись механики для карточки, магазина и Базы предметов.
    public static string Describe(ItemDefinition definition)
    {
        if (definition == null)
            return string.Empty;
        List<string> parts = new List<string>();
        string modifier = definition.Modifier.Describe();
        if (modifier.Length > 0)
            parts.Add((definition.Slot != ItemSlot.None ? "пока надето: " : string.Empty) + modifier);
        if (definition.UseEffects != null && definition.IsConsumable)
        {
            foreach (ItemUseEffect effect in definition.UseEffects)
            {
                string line = DescribeEffect(effect);
                if (!string.IsNullOrEmpty(line))
                    parts.Add(line);
            }
            if (definition.Uses > 0 && definition.UseEffects.Count > 0)
                parts.Add(definition.Uses == 1 ? "одно применение" : "применений: " + definition.Uses);
        }
        if (definition.Kind == ItemKind.TradeGood)
            parts.Add("товар");
        if (definition.IsTradeProtected)
            parts.Add("не продаётся");
        return string.Join("; ", parts);
    }

    public static string DescribeEffect(ItemUseEffect effect)
    {
        if (effect == null)
            return string.Empty;
        switch (effect.Type)
        {
            case ItemUseEffectType.RestoreHitPoints: return "вернуть " + Math.Max(0, effect.Amount) + " HP";
            case ItemUseEffectType.RemoveExhaustion: return "снять изнеможение";
            case ItemUseEffectType.AddSupplies: return "в походе: +" + Math.Max(0, effect.Amount) + " припаса";
            case ItemUseEffectType.GrantKnowledge: return "даёт сведения";
            default: return string.Empty;
        }
    }

    public static bool NeedsTarget(ItemDefinition definition)
    {
        if (definition?.UseEffects == null)
            return false;
        foreach (ItemUseEffect effect in definition.UseEffects)
        {
            if (effect != null && effect.NeedsTarget)
                return true;
        }
        return false;
    }

    // Что произойдёт, если использовать вещь на человека (пусто — на героя).
    public static ItemUsePreview Preview(GameState state, ItemInstanceData item, string targetPersonId)
    {
        ItemUsePreview preview = new ItemUsePreview();
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (state == null || definition == null || !definition.HasUse || item.UsesLeft <= 0)
        {
            preview.Reason = "Это нельзя использовать.";
            return preview;
        }
        if (!ItemService.IsReachable(state, item))
        {
            preview.Reason = "Эта вещь сейчас не рядом.";
            return preview;
        }

        CommanderData commander = state.GetSelectedCommander();
        string targetId = string.IsNullOrEmpty(targetPersonId) ? commander?.Id : targetPersonId;
        preview.TargetPersonId = targetId ?? string.Empty;
        ResidentState target = string.IsNullOrEmpty(targetId) ? null : PeopleService.Find(state, targetId);
        bool targetHere = target != null && target.IsAlive && ItemService.IsWithHeroPublic(state, targetId);

        foreach (ItemUseEffect effect in definition.UseEffects)
        {
            if (effect == null)
                continue;
            switch (effect.Type)
            {
                case ItemUseEffectType.RestoreHitPoints:
                    if (!targetHere)
                        preview.Skipped.Add("Это нужно дать тому, кто рядом.");
                    else if (!target.HasCombatState || target.CurrentHitPoints >= target.MaxHitPoints)
                        preview.Skipped.Add(target.DisplayName + " цел — лечить нечего.");
                    else if (effect.Amount <= 0)
                        preview.Skipped.Add("Лечение не задано.");
                    else
                    {
                        int gain = Math.Min(effect.Amount, target.MaxHitPoints - target.CurrentHitPoints);
                        preview.Applicable.Add(effect);
                        preview.Lines.Add(target.DisplayName + ": +" + gain + " HP");
                    }
                    break;

                case ItemUseEffectType.RemoveExhaustion:
                    if (!targetHere)
                        preview.Skipped.Add("Это нужно дать тому, кто рядом.");
                    else if (!target.Exhausted)
                        preview.Skipped.Add(target.DisplayName + " не изнеможён — это не нужно.");
                    else
                    {
                        preview.Applicable.Add(effect);
                        preview.Lines.Add(target.DisplayName + ": изнеможение снято");
                    }
                    break;

                case ItemUseEffectType.AddSupplies:
                    if (!state.HasActiveExpedition)
                        preview.Skipped.Add("Припасы пополняют в походе.");
                    else if (effect.Amount <= 0)
                        preview.Skipped.Add("Количество припасов не задано.");
                    else
                    {
                        preview.Applicable.Add(effect);
                        preview.Lines.Add("+" + effect.Amount + " припаса отряду");
                    }
                    break;

                case ItemUseEffectType.GrantKnowledge:
                    if (string.IsNullOrWhiteSpace(effect.KnowledgeId))
                        preview.Skipped.Add("Сведения не заданы.");
                    else if (state.Narrative != null && state.Narrative.HasKnowledge(effect.KnowledgeId))
                        preview.Skipped.Add("Это уже известно.");
                    else
                    {
                        preview.Applicable.Add(effect);
                        preview.Lines.Add(string.IsNullOrWhiteSpace(effect.KnowledgeText) ? "новые сведения" : effect.KnowledgeText);
                    }
                    break;
            }
        }

        preview.CanUse = preview.Applicable.Count > 0;
        if (!preview.CanUse)
            preview.Reason = preview.Skipped.Count > 0 ? string.Join(" ", preview.Skipped) : "Это нельзя использовать.";
        return preview;
    }

    // Применить пакет полезных эффектов и списать одно использование.
    public static bool TryUse(GameState state, string instanceId, string targetPersonId, out string message)
    {
        ItemInstanceData item = ItemService.Find(state, instanceId);
        ItemUsePreview preview = Preview(state, item, targetPersonId);
        if (!preview.CanUse)
        {
            message = preview.Reason;
            return false;
        }

        ItemDefinition definition = ItemCatalog.Find(item.ItemId);
        ResidentState target = PeopleService.Find(state, preview.TargetPersonId);
        foreach (ItemUseEffect effect in preview.Applicable)
        {
            switch (effect.Type)
            {
                case ItemUseEffectType.RestoreHitPoints:
                    PeopleService.SetHitPoints(target, Math.Min(target.MaxHitPoints, target.CurrentHitPoints + effect.Amount));
                    break;
                case ItemUseEffectType.RemoveExhaustion:
                    target.Exhausted = false;
                    break;
                case ItemUseEffectType.AddSupplies:
                    state.ArmySupply += effect.Amount;
                    break;
                case ItemUseEffectType.GrantKnowledge:
                    GrantKnowledge(state, definition, effect);
                    break;
            }
        }

        item.UsesLeft--;
        if (item.UsesLeft <= 0)
            ItemService.Get(state).Items.Remove(item);
        message = definition.Name + ": " + string.Join("; ", preview.Lines) + ".";
        return true;
    }

    // Знание — тем же эффектом диалогов (особенности «новое сведение»
    // срабатывают), флаг этапа поиска — отдельно, запись в Хронику — одна.
    private static void GrantKnowledge(GameState state, ItemDefinition definition, ItemUseEffect effect)
    {
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        CommanderData commander = state.GetSelectedCommander();
        if (commander != null)
        {
            commander.HeroProfile ??= new HeroProfileData();
            new NarrativeEffect
            {
                EffectExecutionId = "item.knowledge." + definition.Id + "." + effect.KnowledgeId,
                Type = NarrativeEffectType.AddKnowledge,
                StringParam = effect.KnowledgeId
            }.Apply(new NarrativeEvaluationContext(commander.HeroProfile, state.Narrative, gameState: state));
        }
        state.Narrative.AddKnowledge(effect.KnowledgeId);
        if (!string.IsNullOrWhiteSpace(effect.StageFlagId))
            state.Narrative.SetFlag(effect.StageFlagId);
        Chronicle.Record(state, "knowledge.source." + effect.KnowledgeId, "Сведения: " + definition.Name,
            string.IsNullOrWhiteSpace(effect.KnowledgeText) ? definition.Description : effect.KnowledgeText);
    }
}
