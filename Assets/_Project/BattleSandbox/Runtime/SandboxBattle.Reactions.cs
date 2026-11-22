using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этап 2: реакции тегов на события состояний.
    // Порядок: запрет/иммунитет → отмена/замена (до изменения модели) →
    // фактическое применение → после-событийные ответы. Одно правило одного
    // владельца не перезапускает себя внутри собственной цепочки; длина
    // цепочки ограничена, превышение — диагностика в журнале.
    public sealed partial class SandboxBattle
    {
        private readonly HashSet<string> activeReactions = new HashSet<string>(StringComparer.Ordinal);
        private int reactionDepth;

        private IEnumerable<SandboxUnitState> ReactionOwners()
        {
            // Стабильный порядок: по ID; погибшие не реагируют.
            return units.Where(u => !u.IsDefeated).OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
        }

        private bool ScopeMatches(SandboxUnitState owner, SandboxPassiveRule rule, SandboxUnitState bearer, SandboxUnitState actor, SandboxStatusInstance instance)
        {
            switch (rule.Scope)
            {
                case SandboxReactionScope.Self: return bearer == owner;
                case SandboxReactionScope.AllyNear:
                    return bearer != null && bearer != owner && bearer.Team == owner.Team && !owner.IsOffBoard &&
                           bearer.Position.DistanceTo(owner.Position) <= Math.Max(1, rule.Value2);
                case SandboxReactionScope.AppliedByOwner: return actor == owner && bearer != owner;
                case SandboxReactionScope.OwnerIsSource: return instance != null && instance.SourceUnitId == owner.Id;
                default: return true;
            }
        }

        private bool OwnerRequirements(SandboxUnitState owner, SandboxPassiveRule rule, SandboxUnitState bearer)
        {
            if (!string.IsNullOrEmpty(rule.OwnerRequires) && !rule.OwnerRequires.Split('|').Any(owner.HasStatus))
                return false;
            if (!string.IsNullOrEmpty(rule.OwnerLacks) && rule.OwnerLacks.Split('|').Any(owner.HasStatus))
                return false;
            if (rule.EventTargetRequiresAny.Length > 0 && (bearer == null || !rule.EventTargetRequiresAny.Any(bearer.HasStatus)))
                return false;
            // У «своих» отмен Value2 — нужное число зарядов требуемого состояния.
            if (rule.Scope == SandboxReactionScope.Self && rule.Value2 > 0 && !string.IsNullOrEmpty(rule.OwnerRequires))
            {
                SandboxStatusInstance required = FindStatus(owner, rule.OwnerRequires.Split('|')[0]);
                if (required == null || required.ChargeCount < rule.Value2)
                    return false;
            }
            return true;
        }

        private static string ReactionKey(SandboxUnitState owner, SandboxPassiveRule rule) => owner.Id + "|" + rule.Key.Split('#')[0];

        private bool EnterReaction(SandboxUnitState owner, SandboxPassiveRule rule)
        {
            // Тег владельца не перезапускает себя в собственной цепочке.
            string key = ReactionKey(owner, rule);
            if (activeReactions.Contains(key))
                return false;
            if (reactionDepth > SandboxStatusRules.MaxEventChain)
            {
                Log("Ошибка: превышена длина цепочки реакций (" + rule.Key + " у " + owner.DisplayLabel + ").");
                return false;
            }
            activeReactions.Add(key);
            reactionDepth++;
            return true;
        }

        private void ExitReaction(SandboxUnitState owner, SandboxPassiveRule rule)
        {
            activeReactions.Remove(ReactionKey(owner, rule));
            reactionDepth--;
        }

        // Попытка наложения: отмена или замена до изменения модели. null —
        // наложение продолжается (возможно, с заменой statusId/профиля).
        private string ReactionAttempt(ref string statusId, SandboxUnitState target, SandboxUnitState source, ref SandboxStatusProfile profile)
        {
            if (target.passive.GrantedImmunities.Contains(statusId) ||
                (source != null && target.passive.GrantedImmunities.Contains(statusId + "@" + source.Id)))
                return target.DisplayLabel + " невосприимчив к «" + SandboxStatusCatalog.Get(statusId)?.Name + "» (опыт этого боя)";
            foreach (SandboxUnitState owner in ReactionOwners())
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(owner, SandboxPassiveKind.StatusReaction).ToList())
                {
                    if (rule.ReactEvent != SandboxReactionEvent.Attempt || !rule.StatusIds.Contains(statusId) ||
                        !ScopeMatches(owner, rule, target, source, null) || !OwnerRequirements(owner, rule, target) ||
                        !LimitReady(owner, rule, target.Id))
                        continue;
                    switch (rule.Effect)
                    {
                        case SandboxReactionEffect.Cancel:
                            MarkUsed(owner, rule, target.Id);
                            if (!string.IsNullOrEmpty(rule.EffectStatus))
                            {
                                SandboxUnitState holder = rule.Value == 2 ? target : owner;
                                foreach (string extra in rule.EffectStatus.Split('|'))
                                {
                                    SandboxStatusInstance found = FindStatus(holder, extra);
                                    if (found == null)
                                        continue;
                                    if (rule.Value == 1)
                                        ShortenInstance(holder, found, 1, def.Name);
                                    else if (extra == SandboxStatusCatalog.Momentum)
                                        RemoveStatusInstance(holder, found, "«" + def.Name + "»", endReason: SandboxStatusEndReason.Consumed);
                                    else
                                        RemoveStatusInstance(holder, found, "«" + def.Name + "»", endReason: SandboxStatusEndReason.CleansedByPassive, actor: owner);
                                }
                            }
                            return "«" + def.Name + "» (" + owner.DisplayLabel + ") отменяет «" + SandboxStatusCatalog.Get(statusId)?.Name + "»";
                        case SandboxReactionEffect.Replace:
                            MarkUsed(owner, rule, target.Id);
                            Log("«" + def.Name + "»: вместо «" + SandboxStatusCatalog.Get(statusId)?.Name + "» — «" + SandboxStatusCatalog.Get(rule.EffectStatus)?.Name + "».");
                            statusId = rule.EffectStatus;
                            profile = rule.EffectProfile;
                            return null;
                        case SandboxReactionEffect.SetDuration:
                            MarkUsed(owner, rule, target.Id);
                            profile = Clone(profile);
                            profile.Duration = rule.Value;
                            if (!string.IsNullOrEmpty(rule.EffectStatus))
                                RemoveStatus(target, rule.EffectStatus, "«" + def.Name + "»");
                            break;
                    }
                }
            }
            return null;
        }

        // После-событийные реакции (наложено, заряд, тик, окончание).
        private void DispatchStatusReaction(SandboxReactionEvent kind, string statusId, SandboxStatusInstance instance, SandboxUnitState bearer,
            SandboxUnitState actor, SandboxStatusEndReason reason = SandboxStatusEndReason.Expired, int amount = 0)
        {
            if (bearer == null || Phase != SandboxBattlePhase.InProgress)
                return;
            foreach (SandboxUnitState owner in ReactionOwners())
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(owner, SandboxPassiveKind.StatusReaction).ToList())
                {
                    if (rule.ReactEvent != kind || !rule.StatusIds.Contains(statusId))
                        continue;
                    if (kind == SandboxReactionEvent.Ended && rule.EndReasons.Length > 0 && !rule.EndReasons.Contains(reason))
                        continue;
                    if (!ScopeMatches(owner, rule, bearer, actor, instance) || !OwnerRequirements(owner, rule, bearer) || !LimitReady(owner, rule, bearer.Id))
                        continue;
                    if (!EnterReaction(owner, rule))
                        continue;
                    try
                    {
                        if (ApplyReactionEffect(owner, def, rule, statusId, instance, bearer, actor, amount))
                        {
                            MarkUsed(owner, rule, bearer.Id);
                            Emit(SandboxCombatEventKind.Reaction, owner, bearer, e => { e.StatusId = statusId; e.Note = def.TagId; });
                        }
                    }
                    finally
                    {
                        ExitReaction(owner, rule);
                    }
                }
            }
        }

        private bool ApplyReactionEffect(SandboxUnitState owner, SandboxPassiveDefinition def, SandboxPassiveRule rule, string statusId,
            SandboxStatusInstance instance, SandboxUnitState bearer, SandboxUnitState actor, int amount)
        {
            string label = "«" + def.Name + "»";
            switch (rule.Effect)
            {
                case SandboxReactionEffect.ApplyToSelf:
                    if (owner.IsDefeated) return false;
                    return ApplyStatus(rule.EffectStatus, owner, owner, rule.EffectProfile).Applied;
                case SandboxReactionEffect.ApplyToEventTarget:
                    if (bearer.IsDefeated) return false;
                    return ApplyStatus(rule.EffectStatus, bearer, owner, rule.EffectProfile).Applied;
                case SandboxReactionEffect.ApplyToEventActor:
                    if (actor == null || actor.IsDefeated) return false;
                    return ApplyStatus(rule.EffectStatus, actor, owner, rule.EffectProfile).Applied;
                case SandboxReactionEffect.ApplyToNearestAlly:
                {
                    SandboxUnitState ally = units.Where(u => u != owner && !u.IsDefeated && !u.IsOffBoard && u.Team == owner.Team &&
                                                             u.Position.DistanceTo(owner.Position) <= Math.Max(1, rule.Value2))
                        .OrderBy(u => u.Position.DistanceTo(owner.Position)).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                    return ally != null && ApplyStatus(rule.EffectStatus, ally, owner, rule.EffectProfile).Applied;
                }
                case SandboxReactionEffect.RemoveFromSelf:
                {
                    string first = rule.EffectStatus.Split('|').FirstOrDefault(owner.HasStatus);
                    return first != null && RemoveStatus(owner, first, label);
                }
                case SandboxReactionEffect.RemoveFromEventTarget:
                {
                    string first = rule.EffectStatus.Split('|').FirstOrDefault(bearer.HasStatus);
                    return first != null && RemoveStatus(bearer, first, label);
                }
                case SandboxReactionEffect.RemoveOneChargeFromSelf:
                {
                    SandboxStatusInstance target = FindStatus(owner, rule.EffectStatus);
                    if (target == null) return false;
                    if (target.charges.Count > 1)
                    {
                        target.charges.RemoveAt(0);
                        StatusRevision++;
                        Log(owner.DisplayLabel + ": " + label + " — снят заряд «" + target.Definition.Name + "».");
                    }
                    else
                        RemoveStatusInstance(owner, target, label, endReason: SandboxStatusEndReason.CleansedByPassive, actor: owner);
                    return true;
                }
                case SandboxReactionEffect.AddCharge:
                {
                    if (bearer.IsDefeated || instance == null || !bearer.statuses.Contains(instance)) return false;
                    allowSecondFrostCharge = true;
                    try { return ApplyStatus(statusId, bearer, owner).Applied; }
                    finally { allowSecondFrostCharge = false; }
                }
                case SandboxReactionEffect.HealSelf:
                {
                    if (owner.IsDefeated || owner.HitPoints >= owner.MaxHitPoints) return false;
                    if (rule.Value2 > 0 && bearer.Position.DistanceTo(owner.Position) > rule.Value2) return false;
                    int before = owner.HitPoints;
                    owner.Heal(rule.Value);
                    Log(owner.DisplayLabel + ": " + label + " +" + (owner.HitPoints - before) + " HP.");
                    Emit(SandboxCombatEventKind.HealingResolved, owner, owner, e => { e.Requested = rule.Value; e.ActualHeal = owner.HitPoints - before; e.Delivery = SandboxDeliveryKind.Heal; e.Note = def.TagId; });
                    return true;
                }
                case SandboxReactionEffect.DamageEventActor:
                    if (actor == null || actor.IsDefeated || actor.Team == owner.Team || actor.Position.DistanceTo(owner.Position) != 1) return false;
                    DealStatusDamage(actor, rule.Value, owner, label, SandboxDamageType.Physical);
                    return true;
                case SandboxReactionEffect.ImmuneSameStatusSameSource:
                    if (instance == null || string.IsNullOrEmpty(instance.SourceUnitId)) return false;
                    owner.passive.GrantedImmunities.Add(statusId + "@" + instance.SourceUnitId);
                    Log(owner.DisplayLabel + ": " + label + " — до конца боя невосприимчив к «" + instance.Definition.Name + "» от этого источника.");
                    return true;
                case SandboxReactionEffect.ImmuneSameStatus:
                    owner.passive.GrantedImmunities.Add(statusId);
                    Log(owner.DisplayLabel + ": " + label + " — до конца боя невосприимчив к «" + SandboxStatusCatalog.Get(statusId)?.Name + "».");
                    return true;
                case SandboxReactionEffect.ShortenSelfStatus:
                {
                    SandboxStatusInstance target = FindStatus(owner, rule.EffectStatus);
                    return target != null && ShortenInstance(owner, target, Math.Max(1, rule.Value), def.Name);
                }
                case SandboxReactionEffect.GrantAttackCharge:
                    owner.passive.ChargedAttack = Math.Max(owner.passive.ChargedAttack, rule.Value);
                    owner.passive.ChargedAttackExpires = owner.ActivationCount + 1;
                    Log(owner.DisplayLabel + ": " + label + " — +" + rule.Value + " Атака к следующему удару.");
                    return true;
                case SandboxReactionEffect.SetDuration:
                {
                    if (instance == null || !bearer.statuses.Contains(instance)) return false;
                    instance.ProfileDuration = rule.Value;
                    StatusRevision++;
                    return true;
                }
                default:
                    return false;
            }
        }

        internal SandboxPassiveRule ReadyRule(SandboxUnitState unit, SandboxPassiveKind kind, string targetId = null, string statusId = null)
        {
            if (unit == null)
                return null;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, kind))
            {
                if (statusId != null && rule.StatusIds.Length > 0 && !rule.StatusIds.Contains(statusId))
                    continue;
                if (LimitReady(unit, rule, targetId))
                    return rule;
            }
            return null;
        }

        internal string RuleName(SandboxPassiveRule rule)
        {
            SandboxPassiveDefinition def = SandboxPassiveCatalog.Get(rule.Key.Split('#')[0]);
            return def != null ? "«" + def.Name + "»" : rule.Key;
        }

        // Сократить оставшийся срок на n ходов (минимум до конца текущей активации).
        private bool ShortenInstance(SandboxUnitState unit, SandboxStatusInstance instance, int turns, string by)
        {
            if (instance.Definition.Clock != SandboxStatusClock.OwnActivations)
                return false;
            foreach (SandboxStatusCharge charge in instance.charges)
                charge.Elapsed = Math.Min(instance.EffectiveDuration, charge.Elapsed + turns);
            instance.Elapsed = instance.charges.Count > 0 ? instance.charges.Max(c => c.Elapsed) : instance.Elapsed;
            StatusRevision++;
            if (instance.charges.All(c => c.Elapsed >= instance.EffectiveDuration))
                RemoveStatusInstance(unit, instance, "«" + by + "» сокращает срок", endReason: SandboxStatusEndReason.CleansedByPassive);
            else
                Log(unit.DisplayLabel + ": «" + by + "» сокращает «" + instance.Definition.Name + "» на " + turns + " х.");
            return true;
        }

        // Продлить состояние на n собственных ходов (не выше max, 0 — без предела).
        private void ExtendInstance(SandboxStatusInstance instance, int turns, int max)
        {
            int limit = max > 0 ? max : int.MaxValue;
            instance.ProfileDuration = Math.Min(limit, instance.EffectiveDuration + turns);
            StatusRevision++;
        }
    }
}
