using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этапы 3–4: лечение, адресное и площадное заклинание,
    // развеивание; тип урона удара (огонь, холод, яд, чары) как контекст
    // одного расчёта для предпросмотра и факта. Маны и школ нет: цена —
    // 1 действие и КД; «чародейский запас» существует, только если задан
    // бойцу в Базе существ (MagicReserve > 0).
    public sealed partial class SandboxBattle
    {
        private SandboxDamageType hitDamageType = SandboxDamageType.Physical;
        private bool hitMagical;

        public SandboxDamageType CurrentHitDamageType => hitDamageType;
        public bool CurrentHitMagical => hitMagical;

        private sealed class HitScopeHandle : IDisposable
        {
            private readonly SandboxBattle battle;
            private readonly SandboxDamageType type;
            private readonly bool magical;

            public HitScopeHandle(SandboxBattle battle, SandboxDamageType newType, bool newMagical)
            {
                this.battle = battle;
                type = battle.hitDamageType;
                magical = battle.hitMagical;
                battle.hitDamageType = newType;
                battle.hitMagical = newMagical;
            }

            public void Dispose()
            {
                battle.hitDamageType = type;
                battle.hitMagical = magical;
            }
        }

        // Удар способности с её типом урона: предпросмотр и факт в одном контексте.
        internal IDisposable HitScope(SandboxAbilityDefinition ability)
        {
            return new HitScopeHandle(this, ability != null ? ability.DamageType : SandboxDamageType.Physical, ability != null && ability.IsMagical);
        }

        // ---------- Чародейский запас ----------

        public int MagicLeft(SandboxUnitState unit) => unit == null ? 0 : Math.Max(0, unit.Definition.MagicReserve - unit.passive.MagicSpent);

        private string MagicGate(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            if (!ability.IsMagical || unit.Definition.MagicReserve <= 0)
                return null;
            return MagicLeft(unit) > 0 ? null : "Чародейский запас исчерпан.";
        }

        private void SpendMagic(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            if (ability.IsMagical && unit.Definition.MagicReserve > 0)
                unit.passive.MagicSpent++;
        }

        // ---------- Кандидаты ----------

        private IEnumerable<SandboxAbilityCommand> EnumerateExtended(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            string id = ability.Id;
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Heal:
                    foreach (SandboxUnitState ally in units.Where(u => !u.IsDefeated && !u.IsOffBoard && u.Team == unit.Team &&
                                                                       u.Position.DistanceTo(unit.Position) <= ability.RangeMax &&
                                                                       u.Position.DistanceTo(unit.Position) >= ability.RangeMin)
                                 .OrderBy(u => u.Id, StringComparer.Ordinal))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = ally.Id, KeyCell = ally.Position };
                    break;
                case SandboxAbilityKind.Spell:
                case SandboxAbilityKind.Dispel:
                    foreach (SandboxUnitState target in Opponents(unit))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, KeyCell = target.Position };
                    break;
                case SandboxAbilityKind.SpellArea:
                    foreach (SandboxUnitState target in Opponents(unit))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetCell = target.Position, KeyCell = target.Position };
                    break;
            }
        }

        // ---------- Предпросмотр ----------

        private SandboxAbilityPreview EvaluateExtended(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxAbilityCommand command, SandboxUnitState target)
        {
            SandboxAbilityPreview preview = new SandboxAbilityPreview { IsValid = true, Multiplier = ability.DamageMultiplier };
            string magic = MagicGate(unit, ability);
            if (magic != null)
                return SandboxAbilityPreview.Invalid(magic);
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Heal:
                {
                    if (target == null || target.IsDefeated || target.Team != unit.Team)
                        return SandboxAbilityPreview.Invalid("Лечить можно себя или союзника.");
                    int distance = unit.Position.DistanceTo(target.Position);
                    if (distance > ability.RangeMax || distance < ability.RangeMin)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности лечения.");
                    if (distance > 1 && !HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    int requested = HealAmount(unit, target, ability.Amount, consume: false);
                    int actual = Math.Max(0, Math.Min(requested, target.MaxHitPoints - target.HitPoints));
                    List<string> extras = HealPayloadPreview(unit, target, actual);
                    if (actual <= 0 && extras.Count == 0)
                        return SandboxAbilityPreview.Invalid(target.DisplayLabel + " цел — лечить нечего.");
                    preview.Damage = 0;
                    preview.Summary = "+" + actual + " HP" + (requested > actual ? " (избыток " + (requested - actual) + ")" : string.Empty) +
                                      (extras.Count > 0 ? " · " + string.Join(" · ", extras) : string.Empty);
                    return preview;
                }
                case SandboxAbilityKind.Spell:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    int distance = unit.Position.DistanceTo(target.Position);
                    if (distance < ability.RangeMin || distance > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMin + "–" + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    using (HitScope(ability))
                        FillStrike(preview, unit, target, unit.Position, ability.DamageMultiplier);
                    preview.Summary = "Заклинание (" + SandboxAbilityDefinition.DamageTypeLabel(ability.DamageType) + ") " + DamageFormula(preview) +
                                      PayloadNote(ability, target, unit);
                    return preview;
                }
                case SandboxAbilityKind.SpellArea:
                {
                    if (!command.TargetCell.HasValue) return SandboxAbilityPreview.Invalid("Нужна клетка.");
                    HexCoord center = command.TargetCell.Value;
                    int distance = unit.Position.DistanceTo(center);
                    if (distance < ability.RangeMin || distance > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Клетка вне дальности " + ability.RangeMin + "–" + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, center))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    List<SandboxUnitState> hit = AreaTargets(unit, ability, center);
                    if (hit.Count == 0) return SandboxAbilityPreview.Invalid("В области никого нет.");
                    preview.DangerCells.AddRange(AreaCells(center, ability));
                    using (HitScope(ability))
                        preview.Summary = "Площадь (" + SandboxAbilityDefinition.DamageTypeLabel(ability.DamageType) + "): " +
                                          string.Join(", ", hit.Select(item => item.DisplayLabel + (item.Team == unit.Team ? " (свой!)" : string.Empty) + " " +
                                                                                BuildAttackPreview(unit, item, unit.Position, false, ability.DamageMultiplier).HpDamage));
                    return preview;
                }
                case SandboxAbilityKind.Dispel:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    if (unit.Position.DistanceTo(target.Position) > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    SandboxStatusInstance buff = DispelCandidate(target);
                    if (buff == null) return SandboxAbilityPreview.Invalid("На цели нет полезных состояний.");
                    preview.Summary = "Снимет «" + buff.Definition.Name + "»";
                    return preview;
                }
            }
            return SandboxAbilityPreview.Invalid("Способность недоступна.");
        }

        private string PayloadNote(SandboxAbilityDefinition ability, SandboxUnitState target, SandboxUnitState source)
        {
            if (string.IsNullOrEmpty(ability.StatusPayloadId))
                return string.Empty;
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(ability.StatusPayloadId);
            string rejection = PreviewStatusRejection(ability.StatusPayloadId, target, source);
            return ", затем «" + (def?.Name ?? ability.StatusPayloadId) + "»" + (rejection != null ? " — не пройдёт: " + rejection : string.Empty);
        }

        private IEnumerable<HexCoord> AreaCells(HexCoord center, SandboxAbilityDefinition ability)
        {
            int radius = Math.Max(1, ability.Push);
            for (int q = center.Q - radius; q <= center.Q + radius; q++)
            {
                for (int r = center.R - radius; r <= center.R + radius; r++)
                {
                    HexCoord cell = new HexCoord(q, r);
                    if (IsInside(cell) && cell.DistanceTo(center) <= radius)
                        yield return cell;
                }
            }
        }

        private List<SandboxUnitState> AreaTargets(SandboxUnitState caster, SandboxAbilityDefinition ability, HexCoord center)
        {
            bool sparesAllies = HasPassive(caster, SandboxPassiveKind.AreaSpellSparesAllies);
            HashSet<HexCoord> cells = new HashSet<HexCoord>(AreaCells(center, ability));
            return units.Where(u => !u.IsDefeated && !u.IsOffBoard && u != caster && cells.Contains(u.Position) &&
                                    !(sparesAllies && u.Team == caster.Team))
                .OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
        }

        private static SandboxStatusInstance DispelCandidate(SandboxUnitState target)
        {
            return target.statuses.Where(item => !item.Definition.IsHarmful && item.Definition.Clock != SandboxStatusClock.LegacyFacade)
                .OrderByDescending(item => item.Definition.Supernatural).ThenBy(item => item.Sequence).FirstOrDefault();
        }

        // ---------- Исполнение ----------

        private string ExecuteExtended(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxAbilityCommand command, SandboxUnitState target,
            SandboxAbilityPreview preview)
        {
            SpendMagic(unit, ability);
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Heal:
                    return " → " + target.DisplayLabel + ": " + PerformHeal(unit, target, ability.Amount, ability.Title);
                case SandboxAbilityKind.Spell:
                {
                    string body;
                    int before = target.HitPoints;
                    using (HitScope(ability))
                        body = " → " + target.DisplayLabel + ": " + Strike(unit, target, ability.DamageMultiplier, false, preview);
                    body += ApplyAbilityPayload(unit, target, ability, before - target.HitPoints);
                    AfterSpellCast(unit, target, before - target.HitPoints);
                    return body;
                }
                case SandboxAbilityKind.SpellArea:
                {
                    string body = string.Empty;
                    HexCoord center = command.TargetCell.Value;
                    foreach (SandboxUnitState hit in AreaTargets(unit, ability, center))
                    {
                        int before = hit.HitPoints;
                        using (HitScope(ability))
                        {
                            int damage = BuildAttackPreview(unit, hit, unit.Position, false, ability.DamageMultiplier).Damage;
                            RecordHit(unit, hit, damage, unit.Position);
                            ApplyHit(SandboxHitKind.Ability, unit, hit, damage);
                        }
                        body += " " + hit.DisplayLabel + " −" + (before - hit.HitPoints) + (hit.IsDefeated ? " (выведен)" : string.Empty) + ";";
                        body += ApplyAbilityPayload(unit, hit, ability, before - hit.HitPoints);
                    }
                    AfterSpellCast(unit, null, 0);
                    return ": площадь " + center + ":" + body;
                }
                case SandboxAbilityKind.Dispel:
                {
                    SandboxStatusInstance buff = DispelCandidate(target);
                    if (buff == null)
                        return ": снимать нечего.";
                    string name = buff.Definition.Name;
                    bool magical = buff.Definition.Supernatural;
                    RemoveStatusInstance(target, buff, null, endReason: SandboxStatusEndReason.CleansedByAction, actor: unit);
                    string note = " → " + target.DisplayLabel + ": «" + name + "» развеяно.";
                    // «Сифон чужой силы»: первый снятый полезный магический эффект лечит носителя.
                    SandboxPassiveRule siphon = magical ? ReadyRule(unit, SandboxPassiveKind.HealOnDispelMagical) : null;
                    if (siphon != null)
                    {
                        MarkUsed(unit, siphon, null);
                        note += " " + PerformHeal(unit, unit, siphon.Value, RuleName(siphon), isAbilityHeal: false);
                    }
                    return note;
                }
            }
            return string.Empty;
        }

        // После заклинания: «Оберег ближних чар», «Крадёт запас сил».
        private void AfterSpellCast(SandboxUnitState caster, SandboxUnitState target, int hpTaken)
        {
            SandboxPassiveRule ward = ReadyRule(caster, SandboxPassiveKind.SpellCastSelfStatus);
            if (ward != null && units.Any(u => !u.IsDefeated && u.Team != caster.Team && u.Position.DistanceTo(caster.Position) == 1))
            {
                MarkUsed(caster, ward, null);
                ApplyStatus(ward.StatusIds[0], caster, caster, ward.Profile);
            }
        }

        // ---------- Лечение ----------

        // Запрошенное лечение с бонусами носителя и цели (без учёта максимума).
        private int HealAmount(SandboxUnitState healer, SandboxUnitState target, int amount, bool consume)
        {
            int total = amount;
            if (healer != target)
            {
                SandboxPassiveRule hands = ReadyRule(healer, SandboxPassiveKind.HealOtherBonus);
                if (hands != null)
                {
                    total += hands.Value;
                    if (consume) MarkUsed(healer, hands, null);
                }
            }
            SandboxPassiveRule patient = ReadyRule(target, SandboxPassiveKind.HealReceivedBonus);
            if (patient != null)
            {
                total += patient.Value;
                if (consume) MarkUsed(target, patient, null);
            }
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule sick) in Rules(target, SandboxPassiveKind.HealReceivedPenaltyWhileStatus))
            {
                if (!sick.StatusIds.Any(target.HasStatus) || !LimitReady(target, sick, null))
                    continue;
                total = Math.Max(amount > 0 ? 1 : 0, total - sick.Value);
                if (consume) MarkUsed(target, sick, null);
            }
            return Math.Max(0, total);
        }

        private List<string> HealPayloadPreview(SandboxUnitState healer, SandboxUnitState target, int actual)
        {
            List<string> extras = new List<string>();
            if (healer == target || actual <= 0)
                return extras;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(healer, SandboxPassiveKind.OnHealOther))
            {
                if (!LimitReady(healer, rule, target.Id))
                    continue;
                if (rule.Effect == SandboxReactionEffect.RemoveFromEventTarget && !rule.EffectStatus.Split('|').Any(target.HasStatus))
                    continue;
                extras.Add("«" + def.Name + "»");
            }
            return extras;
        }

        // Лечение как боевой результат: фактическое и избыточное, одно событие.
        // Не поднимает выведенных; само по себе не снимает яд и кровотечение.
        public string PerformHeal(SandboxUnitState healer, SandboxUnitState target, int amount, string label, bool isAbilityHeal = true)
        {
            if (target == null || target.IsDefeated)
                return "лечить некого.";
            int requested = HealAmount(healer, target, amount, consume: true);
            int before = target.HitPoints;
            target.Heal(requested);
            int actual = target.HitPoints - before;
            int overheal = Math.Max(0, requested - actual);
            Emit(SandboxCombatEventKind.HealingResolved, healer, target, e =>
            {
                e.Requested = requested; e.ActualHeal = actual; e.Overheal = overheal; e.Delivery = SandboxDeliveryKind.Heal; e.Note = label;
            });
            string result = "+" + actual + " HP" + (overheal > 0 ? " (избыток " + overheal + ")" : string.Empty) + ".";
            if (isAbilityHeal && actual > 0 && healer != target)
                result += HealOtherReactions(healer, target);
            return result;
        }

        private string HealOtherReactions(SandboxUnitState healer, SandboxUnitState target)
        {
            string note = string.Empty;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(healer, SandboxPassiveKind.OnHealOther).ToList())
            {
                if (!LimitReady(healer, rule, target.Id))
                    continue;
                if (rule.Value2 > 0 && healer.Position.DistanceTo(target.Position) > rule.Value2)
                    continue;
                bool done;
                switch (rule.Effect)
                {
                    case SandboxReactionEffect.RemoveFromEventTarget:
                    {
                        string first = rule.EffectStatus.Split('|').FirstOrDefault(target.HasStatus);
                        if (first == null) continue;
                        SandboxStatusInstance found = FindStatus(target, first);
                        if (found != null && found.charges.Count > 1)
                        {
                            found.charges.RemoveAt(0);
                            StatusRevision++;
                        }
                        else
                            RemoveStatus(target, first, "«" + def.Name + "»", SandboxStatusEndReason.CleansedByPassive, healer);
                        done = true;
                        break;
                    }
                    case SandboxReactionEffect.HealSelf:
                    {
                        int before = healer.HitPoints;
                        healer.Heal(rule.Value);
                        done = healer.HitPoints > before;
                        break;
                    }
                    case SandboxReactionEffect.GrantAttackCharge:
                        target.passive.Stacks["attack.untilTurn"] = Math.Max(target.passive.Stack("attack.untilTurn"), rule.Value);
                        target.passive.GrantedAttackUntilActivation = target.ActivationCount + 1;
                        done = true;
                        break;
                    case SandboxReactionEffect.ApplyToEventTarget:
                        done = ApplyStatus(rule.EffectStatus, target, healer, rule.EffectProfile).Applied;
                        break;
                    default:
                        done = false;
                        break;
                }
                if (!done)
                    continue;
                MarkUsed(healer, rule, target.Id);
                note += " «" + def.Name + "».";
            }
            return note;
        }

        // ---------- Огонь и холод на попадании ----------

        // После фактического прямого попадания с типом урона.
        private void AfterTypedHit(SandboxUnitState striker, SandboxUnitState target, int hpTaken, SandboxDamageType type)
        {
            if (target == null || target.IsDefeated)
                return;
            if (type == SandboxDamageType.Fire && hpTaken > 0)
            {
                // Огонь снимает лёд, иней, паутину и озноб после своего урона.
                foreach (string melted in new[] { SandboxStatusCatalog.Frozen, SandboxStatusCatalog.Frost, "status.022", "status.014" })
                    RemoveStatus(target, melted, "огонь растопил/сжёг", SandboxStatusEndReason.BrokenByDamage, striker);
            }
            if (type == SandboxDamageType.Cold && striker != null)
            {
                // «От удара стынет земля»: иней на гексе цели до конца следующего раунда.
                SandboxPassiveRule rime = ReadyRule(striker, SandboxPassiveKind.ColdHitRimeCell);
                if (rime != null)
                {
                    MarkUsed(striker, rime, null);
                    AddEffect(target.Position, SandboxEffectKind.Rime, 1, 0, striker.Id);
                }
                // «Промораживает насквозь»: следующий холодный удар по этой цели +1.
                SandboxPassiveRule chill = ReadyRule(striker, SandboxPassiveKind.ColdHitMarkNext);
                if (chill != null && striker.passive.ColdMarkTargetId != target.Id)
                {
                    MarkUsed(striker, chill, null);
                    striker.passive.ColdMarkTargetId = target.Id;
                    striker.passive.ColdMarkExpires = target.ActivationCount + 2;
                    striker.passive.ColdMarkValue = chill.Value;
                }
            }
        }
    }
}
