using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public sealed class SandboxStatusApplyResult
    {
        public bool Applied { get; internal set; }
        public string StatusId { get; internal set; }
        public string Message { get; internal set; } = string.Empty;
        // Причина отказа: иммунитет, защита от повторного контроля, уже действует.
        public string RejectReason { get; internal set; } = string.Empty;
        public SandboxStatusInstance Instance { get; internal set; }
    }

    // Пакет «Боевые состояния»: исполнение временных состояний поверх
    // существующего потока боя. Время — собственные активации (не анимации),
    // порядок начала хода: восстановление → тики (не больше 3 HP новых) →
    // смерть → ограничения. Числа — SandboxStatusRules и определения каталога.
    public sealed partial class SandboxBattle
    {
        private int nextStatusSequence = 1;
        // Растёт при любом изменении состояний: UI пересобирает списки только по нему.
        public int StatusRevision { get; private set; }
        private bool statusesClearedAtEnd;

        // ---------- Запросы ----------

        public IReadOnlyList<SandboxStatusInstance> GetStatuses(string unitId)
        {
            SandboxUnitState unit = GetUnit(unitId);
            return unit != null ? unit.statuses : (IReadOnlyList<SandboxStatusInstance>)Array.Empty<SandboxStatusInstance>();
        }

        private static bool Applies(SandboxStatusInstance instance, SandboxStatusOp op)
        {
            return op.MinCharges <= 0 || instance.ChargeCount >= op.MinCharges;
        }

        private static IEnumerable<(SandboxStatusInstance Instance, SandboxStatusOp Op)> OpsOf(SandboxUnitState unit, SandboxStatusOpKind kind)
        {
            if (unit == null)
                yield break;
            foreach (SandboxStatusInstance instance in unit.statuses)
            {
                foreach (SandboxStatusOp op in instance.Definition.Ops)
                {
                    if (op.Kind == kind && Applies(instance, op))
                        yield return (instance, op);
                }
            }
        }

        // Число операции с учётом профиля источника (замена первого модификатора).
        internal static int V(SandboxStatusInstance instance, SandboxStatusOp op)
        {
            if (instance.ProfileValue == 0)
                return op.Value;
            foreach (SandboxStatusOp first in instance.Definition.Ops)
            {
                if (first.Kind == SandboxStatusOpKind.AttackMod || first.Kind == SandboxStatusOpKind.DefenseMod ||
                    first.Kind == SandboxStatusOpKind.MovementMod || first.Kind == SandboxStatusOpKind.MaxHitPointsMod ||
                    first.Kind == SandboxStatusOpKind.InitiativeMod)
                    return ReferenceEquals(first, op) ? instance.ProfileValue : op.Value;
            }
            return op.Value;
        }

        internal static bool StatusHas(SandboxUnitState unit, SandboxStatusOpKind kind) => OpsOf(unit, kind).Any();

        private static SandboxStatusInstance FindStatus(SandboxUnitState unit, string statusId)
        {
            return unit?.statuses.FirstOrDefault(item => item.StatusId == statusId);
        }

        // Сумма временных модификаторов Атаки от состояний (предел ±2).
        internal int StatusAttackMod(SandboxUnitState attacker, SandboxUnitState target, bool ranged, bool consume = false, bool retaliation = false)
        {
            if (attacker == null)
                return 0;
            int sum = 0;
            foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(attacker, SandboxStatusOpKind.AttackMod))
            {
                if (retaliation && attacker.IsGuarding &&
                    Rules(attacker, SandboxPassiveKind.RetaliationIgnoresStatus).Any(pair => pair.Rule.StatusIds.Contains(instance.StatusId)))
                    continue;
                switch (op.Filter)
                {
                    case SandboxStatusFilter.Melee: if (!ranged) sum += V(instance, op); break;
                    case SandboxStatusFilter.Ranged: if (ranged) sum += V(instance, op); break;
                    case SandboxStatusFilter.VsSource: if (target != null && instance.SourceUnitId == target.Id) sum += V(instance, op); break;
                    case SandboxStatusFilter.WhileSourceNear:
                        if (IsSourceNear(attacker, instance)) sum += V(instance, op);
                        break;
                    case SandboxStatusFilter.WoundThreshold:
                        int full = attacker.Definition.MaxHitPoints;
                        if (attacker.HitPoints * 4 <= full) sum += V(instance, op) + 1;
                        else if (attacker.HitPoints * 2 <= full) sum += V(instance, op);
                        break;
                    default: sum += V(instance, op); break;
                }
            }
            // Вызов на поединок: источник тоже получает +1 против вызванного,
            // а оба −… — вторичный профиль «Цена отвлечения» не включён.
            if (target != null)
            {
                foreach (SandboxStatusInstance duel in target.statuses.Where(item => item.Definition.HasSpecial("DuelMirror") && item.SourceUnitId == attacker.Id))
                    sum += duel.Definition.Ops.First(op => op.Kind == SandboxStatusOpKind.Special).Value;
                // Отмеченная цель: +1 Атаки нападающих врагов, расходует заряд.
                foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(target, SandboxStatusOpKind.IncomingAttackMod).ToList())
                {
                    if (target.Team == attacker.Team || instance.UsesLeft <= 0)
                        continue;
                    sum += V(instance, op);
                    if (consume && op.ConsumesUse)
                        ConsumeUse(target, instance, "использована");
                }
            }
            if (StatusHas(attacker, SandboxStatusOpKind.NoPositiveAttack))
                sum = Math.Min(0, sum);
            return SandboxStatusRules.Clamp(sum, SandboxStatusRules.AttackModCap);
        }

        internal static int StatusDefenseMod(SandboxUnitState target)
        {
            if (target == null)
                return 0;
            // Все существующие прямые удары физические: Окаменение даёт +2 к ним.
            int sum = OpsOf(target, SandboxStatusOpKind.DefenseMod).Sum(pair => V(pair.Instance, pair.Op));
            return SandboxStatusRules.Clamp(sum, SandboxStatusRules.DefenseModCap);
        }

        internal static int StatusMovementMod(SandboxUnitState unit)
        {
            int sum = OpsOf(unit, SandboxStatusOpKind.MovementMod).Where(pair => string.IsNullOrEmpty(pair.Instance.Mode)).Sum(pair => V(pair.Instance, pair.Op));
            SandboxStatusInstance momentum = FindStatus(unit, SandboxStatusCatalog.Momentum);
            if (momentum != null)
                sum += momentum.ChargeCount;
            return SandboxStatusRules.Clamp(sum, SandboxStatusRules.MovementModCap);
        }

        internal static int StatusInitiativeMod(SandboxUnitState unit)
        {
            int sum = OpsOf(unit, SandboxStatusOpKind.InitiativeMod).Sum(pair => V(pair.Instance, pair.Op));
            return SandboxStatusRules.Clamp(sum, SandboxStatusRules.InitiativeModCap);
        }

        internal static int StatusMaxHitPointsMod(SandboxUnitState unit)
        {
            return OpsOf(unit, SandboxStatusOpKind.MaxHitPointsMod).Sum(pair => V(pair.Instance, pair.Op));
        }

        // Базовый Урон до формулы: однотипные уменьшения не складываются.
        internal int StatusBaseDamage(SandboxUnitState attacker, bool ranged)
        {
            if (ReadyRule(attacker, SandboxPassiveKind.BasePenaltyIgnoreOnce) != null && (attacker.HasStatus("status.099") || attacker.HasStatus("status.088")))
                return Math.Max(1, attacker.Damage);
            int strongest = 0;
            foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(attacker, SandboxStatusOpKind.BaseDamageMod))
            {
                if (op.Filter == SandboxStatusFilter.Melee && ranged)
                    continue;
                strongest = Math.Min(strongest, V(instance, op));
            }
            return Math.Max(1, attacker.Damage + strongest);
        }

        // Плоские изменения итогового прямого урона: доплаты суммарно не
        // больше +2, из уменьшений действует сильнейшее (не больше 1).
        internal int StatusFlatDamage(SandboxUnitState attacker, SandboxUnitState target, bool ranged, SandboxDamageType type, bool consume)
        {
            // «Снимает тяжесть удара»: штраф «Ослаблен ударом» один раз не действует.
            bool ignorePenalty = attacker != null && ReadyRule(attacker, SandboxPassiveKind.BasePenaltyIgnoreOnce) != null &&
                                 (attacker.HasStatus("status.088") || attacker.HasStatus("status.099"));
            int bonus = 0;
            int reduction = 0;
            void Count(int value)
            {
                if (value > 0) bonus += value;
                else reduction = Math.Max(reduction, -value);
            }

            foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(attacker, SandboxStatusOpKind.OutgoingFlat).ToList())
            {
                if (op.ConsumesUse && instance.UsesLeft <= 0)
                    continue;
                if (ignorePenalty && instance.StatusId == "status.088")
                    continue;
                Count(op.Value);
                if (consume && op.ConsumesUse)
                    ConsumeUse(attacker, instance, "израсходовано");
            }
            // «Не подпускает палача»: сокращение 1, общее с остальными плоскими (берётся большее).
            if (GuardAuraReduction(attacker, target, consume))
                Count(-1);
            if (target != null && attacker != null && attacker.Team != target.Team)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule vow) in Rules(target, SandboxPassiveKind.VowReduceWhileStatus))
                {
                    SandboxStatusInstance curse = vow.StatusIds.Select(id => FindStatus(target, id)).FirstOrDefault(item => item != null);
                    if (curse == null || target.passive.UsedInBattle.Contains(vow.Key + "@" + curse.InstanceId))
                        continue;
                    Count(-vow.Value);
                    if (consume)
                        target.passive.UsedInBattle.Add(vow.Key + "@" + curse.InstanceId);
                }
            }

            int profileReduction = 0;
            if (target != null)
            {
                foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(target, SandboxStatusOpKind.IncomingFlat).ToList())
                {
                    bool enemy = attacker != null && attacker.Team != target.Team;
                    // Явный разовый профиль покрова (−2 «Перевязи с оберегом») — отдельно от предела.
                    if (instance.ProfileValue < 0 && op.Value < 0)
                    {
                        if (op.Filter == SandboxStatusFilter.Physical && type != SandboxDamageType.Physical)
                            continue;
                        if (instance.UsesLeft <= 0 && instance.Definition.Uses == 0 && instance.ProfileId.Length > 0 && instance.charges.Count == 0)
                            continue;
                        profileReduction = Math.Max(profileReduction, -instance.ProfileValue);
                        if (consume && instance.UsesLeft > 0)
                            ConsumeUse(target, instance, "покров израсходован");
                        continue;
                    }
                    switch (op.Filter)
                    {
                        case SandboxStatusFilter.Physical: if (type != SandboxDamageType.Physical) continue; break;
                        case SandboxStatusFilter.Ranged: if (!ranged) continue; break;
                        case SandboxStatusFilter.Fire: if (type != SandboxDamageType.Fire) continue; break;
                        case SandboxStatusFilter.SourceOncePerSourceTurn:
                            if (attacker == null || instance.SourceUnitId != attacker.Id || instance.LastTriggerActivation == attacker.ActivationCount)
                                continue;
                            if (consume)
                                instance.LastTriggerActivation = attacker.ActivationCount;
                            break;
                        case SandboxStatusFilter.FirstBetweenOwnTurns:
                            if (!enemy || instance.StoredValue > 0)
                                continue;
                            if (consume)
                                instance.StoredValue = 1;
                            SandboxPassiveRule mend = ReadyRule(target, SandboxPassiveKind.StatusExtraDamageReduceOnce, null, instance.StatusId);
                            if (mend != null)
                            {
                                if (consume)
                                    MarkUsed(target, mend, null);
                                continue;
                            }
                            break;
                        default:
                            if (op.Value > 0 && !enemy)
                                continue;
                            break;
                    }
                    if (op.ConsumesUse && instance.UsesLeft <= 0)
                        continue;
                    Count(op.Value);
                    if (consume && op.ConsumesUse)
                        ConsumeUse(target, instance, "израсходовано");
                }
            }
            int totalReduction = Math.Max(Math.Min(SandboxStatusRules.MaxFlatReduction, reduction), profileReduction);
            if (attacker != null)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule pierce) in Rules(attacker, SandboxPassiveKind.PierceFlatReduction))
                    totalReduction = Math.Max(0, totalReduction - pierce.Value);
            }
            return Math.Min(SandboxStatusRules.MaxFlatBonus, bonus) - totalReduction;
        }

        private readonly Dictionary<string, int> markConsumedAtRoot = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> wardBlockedAt = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> frostChargesThisRound = new Dictionary<string, int>(StringComparer.Ordinal);
        // Реакция «Лёд сковывает слабого» вправе добавить второй заряд от источника за раунд.
        internal bool allowSecondFrostCharge;

        private void ConsumeUse(SandboxUnitState owner, SandboxStatusInstance instance, string what)
        {
            // «Клинок находит метку»: несколько попаданий одного действия тратят 1 заряд метки.
            if ((instance.StatusId == "status.098" || instance.StatusId == "status.102") && CurrentUnit != null)
            {
                SandboxPassiveRule once = ReadyRule(CurrentUnit, SandboxPassiveKind.MarkConsumeOncePerAction);
                string key = instance.InstanceId;
                if (once != null && markConsumedAtRoot.TryGetValue(key, out int root) && root == currentRootAction)
                    return;
                if (once != null)
                    markConsumedAtRoot[key] = currentRootAction;
            }
            instance.UsesLeft = Math.Max(0, instance.UsesLeft - 1);
            StatusRevision++;
            if (instance.UsesLeft <= 0)
                RemoveStatusInstance(owner, instance, what);
        }

        private bool IsSourceNear(SandboxUnitState bearer, SandboxStatusInstance instance)
        {
            SandboxUnitState source = GetUnit(instance.SourceUnitId);
            return source != null && !source.IsDefeated && !source.IsOffBoard && !source.IsHidden && !source.HasStatus(SandboxStatusCatalog.Invisible) &&
                   source.Position.DistanceTo(bearer.Position) <= SandboxStatusRules.FearRadius;
        }

        // Общая проверка допустимости вредного действия по цели (атака, способность).
        // Её используют UI, предпросмотр и ИИ: запрещённое не выбирается.
        public string GetStatusTargetBlock(SandboxUnitState attacker, SandboxUnitState target, bool ranged)
        {
            if (attacker == null || target == null)
                return null;
            foreach (SandboxStatusInstance instance in attacker.statuses)
            {
                SandboxStatusDefinition def = instance.Definition;
                if (def.Has(SandboxStatusOpKind.SkipActivation) || def.Has(SandboxStatusOpKind.NoOffense))
                    return "«" + def.Name + "»: нападать нельзя.";
                if (ranged && def.Has(SandboxStatusOpKind.NoRangedAttack))
                    return "«" + def.Name + "»: дальняя атака недоступна.";
                if (ranged && def.Has(SandboxStatusOpKind.RangedAdjacentOnly))
                    return "«" + def.Name + "»: бить можно только соседнюю цель.";
                if (def.Has(SandboxStatusOpKind.CannotTargetSource) && instance.SourceUnitId == target.Id)
                    return "«" + def.Name + "»: нельзя нападать на источник.";
            }
            SandboxStatusInstance provoked = attacker.statuses.FirstOrDefault(item => item.Definition.Has(SandboxStatusOpKind.MustTargetSource));
            if (provoked != null && provoked.SourceUnitId != target.Id)
            {
                SandboxUnitState provoker = GetUnit(provoked.SourceUnitId);
                if (provoker != null && !provoker.IsDefeated && provoker.Team != attacker.Team &&
                    TryFindAttackPosition(attacker.Id, provoker.Id, out _, out _))
                    return "«" + provoked.Definition.Name + "»: нужно нападать на " + provoker.DisplayLabel + ".";
            }
            if (target.Team != attacker.Team && StatusHas(target, SandboxStatusOpKind.CannotBeTargeted))
                return "Цель невидима.";
            return null;
        }

        // ---------- Наложение ----------

        public SandboxStatusApplyResult ApplyStatus(string statusId, SandboxUnitState target, SandboxUnitState source,
            SandboxStatusProfile profile = null, bool supernatural = false)
        {
            return ApplyStatusInternal(statusId, target, source, profile, supernatural, reflected: false);
        }

        private SandboxStatusApplyResult ApplyStatusInternal(string statusId, SandboxUnitState target, SandboxUnitState source,
            SandboxStatusProfile profile, bool supernatural, bool reflected)
        {
            SandboxStatusApplyResult result = new SandboxStatusApplyResult { StatusId = statusId };
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(statusId);
            if (def == null)
                return Reject(result, "неизвестное состояние " + statusId);
            if (target == null || target.IsDefeated)
                return Reject(result, "цели нет");
            if (def.Clock == SandboxStatusClock.LegacyFacade)
                return Reject(result, "«" + def.Name + "» задаётся прежней связью способности");
            if (Phase != SandboxBattlePhase.InProgress && Phase != SandboxBattlePhase.Preparing)
                return Reject(result, "бой завершён");

            SandboxStatusInstance immunity = target.statuses.FirstOrDefault(item => item.Definition.ImmuneTo.Contains(statusId));
            if (immunity != null)
                return Reject(result, target.DisplayLabel + " невосприимчив: «" + immunity.Definition.Name + "»", target);

            bool magical = supernatural || (profile != null && profile.Supernatural) || def.Supernatural;
            bool hostile = def.IsHarmful && source != null && source.Team != target.Team;
            if (hostile && magical && wardBlockedAt.TryGetValue(target.Id, out int blockedRoot) && blockedRoot == currentRootAction)
                return Reject(result, "«Защита от чар» уже отменила это заклинание", target);
            if (hostile && magical && !reflected)
            {
                SandboxStatusInstance mirror = target.statuses.FirstOrDefault(item => item.Definition.HasSpecial("MirrorSupernatural") && item.UsesLeft > 0);
                if (mirror != null && source != null && !source.IsDefeated)
                {
                    ConsumeUse(target, mirror, "отразила чары");
                    Log(target.DisplayLabel + ": «Зеркальная защита» возвращает «" + def.Name + "» в " + source.DisplayLabel + ".");
                    SandboxPassiveRule mirrorGuard = ReadyRule(source, SandboxPassiveKind.ReflectImmunityOnce);
                    if (mirrorGuard != null)
                    {
                        MarkUsed(source, mirrorGuard, null);
                        Log(source.DisplayLabel + ": " + RuleName(mirrorGuard) + " — отражённое состояние не проходит.");
                    }
                    else
                        ApplyStatusInternal(statusId, source, target, profile, supernatural, reflected: true);
                    return Reject(result, "отражено в источник");
                }
                SandboxStatusInstance ward = target.statuses.FirstOrDefault(item => item.Definition.HasSpecial("WardSupernatural") && item.UsesLeft > 0);
                if (ward != null)
                {
                    ConsumeUse(target, ward, "погасила чары");
                    return Reject(result, "«Защита от чар» отменяет «" + def.Name + "»", target);
                }
            }

            string passiveGate = PassiveStatusGate(statusId, target, ref profile);
            if (passiveGate != null)
                return Reject(result, passiveGate, target);

            if (def.StrongControl && (target.StrongControlGuard || target.statuses.Any(item => item.Definition.StrongControl)))
                return Reject(result, target.DisplayLabel + " невосприимчив: защита от повторного сильного контроля", target);

            // Бриф завершения, этап 2: реакции до наложения (отмена/замена/срок).
            string requested = statusId;
            string reactionBlock = ReactionAttempt(ref statusId, target, source, ref profile);
            if (reactionBlock != null)
            {
                Emit(SandboxCombatEventKind.StatusRejected, source, target, e => { e.StatusId = requested; e.Note = reactionBlock; });
                return Reject(result, reactionBlock, target);
            }
            if (statusId != requested)
                return ApplyStatusInternal(statusId, target, source, profile, supernatural, reflected);

            // Взаимная отмена удачи и невезения.
            if (statusId == SandboxStatusCatalog.GoodLuck || statusId == SandboxStatusCatalog.BadLuck)
            {
                string opposite = statusId == SandboxStatusCatalog.GoodLuck ? SandboxStatusCatalog.BadLuck : SandboxStatusCatalog.GoodLuck;
                SandboxStatusInstance other = FindStatus(target, opposite);
                if (other != null)
                {
                    RemoveStatusInstance(target, other, "взаимно отменено");
                    return Reject(result, "«" + def.Name + "» и «" + other.Definition.Name + "» взаимно отменились", target);
                }
            }

            if (statusId == SandboxStatusCatalog.Frost && source != null && (profile == null || profile.Mode != "thin_rime"))
            {
                string frostKey = target.Id + "|" + source.Id + "|" + Round;
                frostChargesThisRound.TryGetValue(frostKey, out int already);
                if (already >= (allowSecondFrostCharge ? 2 : 1))
                    return Reject(result, "иней от этого источника уже лёг в этом раунде", target);
                frostChargesThisRound[frostKey] = already + 1;
            }

            SandboxStatusInstance existing = FindStatus(target, statusId);
            if (existing != null)
            {
                if (profile != null && profile.RefreshSameSource && existing.SourceUnitId == source?.Id && existing.ProfileId == profile.ProfileId)
                {
                    foreach (SandboxStatusCharge charge in existing.charges)
                    {
                        charge.Elapsed = 0;
                        charge.AppliedAtActivation = target.ActivationCount;
                    }
                    existing.Elapsed = 0;
                    StatusRevision++;
                    result.Applied = true;
                    result.Instance = existing;
                    result.Message = target.DisplayLabel + ": «" + def.Name + "» — срок обновлён.";
                    Log(result.Message);
                    Emit(SandboxCombatEventKind.StatusRefreshed, source, target, e => { e.StatusId = statusId; e.StatusInstanceId = existing.InstanceId; e.ProfileId = existing.ProfileId; });
                    return result;
                }
                int maxCharges = existing.ProfileMaxCharges > 0 ? existing.ProfileMaxCharges : def.MaxCharges;
                if (def.Stacking != SandboxStatusStacking.Charges || existing.charges.Count >= maxCharges)
                    return Reject(result, "«" + def.Name + "» уже действует — повтор не усиливает и не продлевает", target);
                existing.charges.Add(new SandboxStatusCharge { AppliedAtActivation = target.ActivationCount });
                StatusRevision++;
                result.Applied = true;
                result.Instance = existing;
                result.Message = target.DisplayLabel + ": «" + def.Name + "» — заряд " + existing.charges.Count + ".";
                Log(result.Message);
                Emit(SandboxCombatEventKind.StatusChargeChanged, source, target, e => { e.StatusId = statusId; e.StatusInstanceId = existing.InstanceId; e.Requested = existing.charges.Count; });
                DispatchStatusReaction(SandboxReactionEvent.ChargeAdded, statusId, existing, target, source);
                if (target.statuses.Contains(existing))
                    AfterChargeAdded(target, existing, source);
                return result;
            }

            SandboxStatusInstance instance = new SandboxStatusInstance
            {
                InstanceId = "st:" + nextStatusSequence,
                Sequence = nextStatusSequence++,
                Definition = def,
                SourceUnitId = source?.Id,
                TargetUnitId = target.Id,
                AppliedRound = Math.Max(1, Round),
                AppliedAtActivation = target.ActivationCount,
                AppliedAtSourceActivation = source != null ? source.ActivationCount : 0,
                UsesLeft = def.Uses,
                ProfileId = profile?.ProfileId ?? string.Empty,
                ProfileDuration = profile != null ? Math.Max(0, profile.Duration) : 0,
                ProfileTick = profile != null ? Math.Max(0, profile.Tick) : 0,
                ProfileValue = profile != null ? profile.Value : 0,
                ProfileRefresh = profile != null && profile.RefreshSameSource,
                UntilBattleEnd = profile != null && profile.UntilBattleEnd,
                ProfileMaxCharges = profile != null ? Math.Max(0, profile.MaxCharges) : 0,
                Mode = profile?.Mode ?? string.Empty
            };
            instance.charges.Add(new SandboxStatusCharge { AppliedAtActivation = target.ActivationCount });
            if (def.Clock == SandboxStatusClock.RoundTrigger)
                instance.TriggerRound = Math.Max(1, Round) + Math.Max(1, def.Duration);

            // Особые правила при наложении.
            if (statusId == SandboxStatusCatalog.Burning)
            {
                SandboxStatusInstance tar = FindStatus(target, SandboxStatusCatalog.Tarred);
                if (tar != null)
                {
                    RemoveStatusInstance(target, tar, "смола вспыхнула");
                    instance.FirstTickBonus = 1;
                }
            }
            if (statusId == SandboxStatusCatalog.Invulnerable)
            {
                SandboxStatusInstance shared = FindStatus(target, SandboxStatusCatalog.SharedWounds);
                if (shared != null)
                    RemoveStatusInstance(target, shared, "неуязвимость рвёт связь ран");
                foreach (SandboxUnitState other in units)
                {
                    foreach (SandboxStatusInstance link in other.statuses.Where(item => item.StatusId == SandboxStatusCatalog.SharedWounds && item.SourceUnitId == target.Id).ToList())
                        RemoveStatusInstance(other, link, "неуязвимость рвёт связь ран");
                }
            }
            foreach (string removed in def.ImmuneTo)
            {
                SandboxStatusInstance old = FindStatus(target, removed);
                if (old != null)
                    RemoveStatusInstance(target, old, "снято «" + def.Name + "»");
            }
            foreach (SandboxStatusOp op in def.Ops.Where(op => op.Kind == SandboxStatusOpKind.TemporaryHitPoints))
                instance.TemporaryHitPoints = Math.Max(instance.TemporaryHitPoints, profile != null && profile.Value > 0 ? profile.Value : op.Value);
            if (profile != null && profile.Uses > 0)
                instance.UsesLeft = profile.Uses;

            target.statuses.Add(instance);
            StatusRevision++;
            if (def.StrongControl)
            {
                target.StrongControlGuard = true;
                target.StrongControlReleaseAtEnd = false;
                CancelPreparedOnControl(target);
            }
            ClampHitPointsToMax(target);

            result.Applied = true;
            result.Instance = instance;
            result.Message = target.DisplayLabel + " получает «" + def.Name + "»" +
                             (source != null && source != target ? " от " + source.DisplayLabel : string.Empty) + ".";
            Log(result.Message);
            Emit(SandboxCombatEventKind.StatusApplied, source, target, e => { e.StatusId = statusId; e.StatusInstanceId = instance.InstanceId; e.ProfileId = instance.ProfileId; });
            if (source != null && statusId == SandboxStatusCatalog.Fear)
            {
                SandboxLink grip = links.FirstOrDefault(link => link.Kind == SandboxLinkKind.Engulf && link.SourceId == target.Id);
                SandboxPassiveRule loosen = grip != null ? ReadyRule(source, SandboxPassiveKind.FearBreaksGrapple) : null;
                if (loosen != null)
                {
                    MarkUsed(source, loosen, null);
                    RemoveLink(grip, RuleName(loosen) + " — страх ослабил хватку.");
                }
            }
            DispatchStatusReaction(SandboxReactionEvent.Applied, statusId, instance, target, source);
            return result;
        }

        private SandboxStatusApplyResult Reject(SandboxStatusApplyResult result, string reason, SandboxUnitState target = null)
        {
            result.Applied = false;
            result.RejectReason = reason;
            result.Message = (target != null ? string.Empty : "Состояние не наложено: ") + reason + ".";
            if (target != null)
                Log(result.Message);
            return result;
        }

        private void AfterChargeAdded(SandboxUnitState target, SandboxStatusInstance instance, SandboxUnitState source)
        {
            if (instance.Definition.HasSpecial("FrostToFreeze") && instance.Mode != "thin_rime" && instance.charges.Count >= 3)
            {
                RemoveStatusInstance(target, instance, "иней сковал льдом");
                ApplyStatusInternal(SandboxStatusCatalog.Frozen, target, source, null, false, false);
            }
        }

        private static void ClampHitPointsToMax(SandboxUnitState unit)
        {
            if (unit.HitPoints > unit.MaxHitPoints)
                unit.HitPoints = unit.MaxHitPoints;
        }

        public bool RemoveStatus(SandboxUnitState unit, string statusId, string reason,
            SandboxStatusEndReason endReason = SandboxStatusEndReason.CleansedByPassive, SandboxUnitState actor = null)
        {
            SandboxStatusInstance instance = FindStatus(unit, statusId);
            if (instance == null)
                return false;
            RemoveStatusInstance(unit, instance, reason, endReason: endReason, actor: actor);
            return true;
        }

        private void RemoveStatusInstance(SandboxUnitState unit, SandboxStatusInstance instance, string reason, bool expired = false,
            SandboxStatusEndReason? endReason = null, SandboxUnitState actor = null)
        {
            if (unit == null || !unit.statuses.Remove(instance))
                return;
            StatusRevision++;
            SandboxStatusEndReason why = expired ? SandboxStatusEndReason.Expired : endReason ?? SandboxStatusEndReason.Consumed;
            Emit(SandboxCombatEventKind.StatusEnded, actor, unit, e =>
            {
                e.StatusId = instance.StatusId; e.StatusInstanceId = instance.InstanceId; e.ProfileId = instance.ProfileId; e.EndReason = why;
            });
            ClampHitPointsToMax(unit);
            if (instance.StatusId == SandboxStatusCatalog.Numbed && expired)
            {
                // Прежнее узкое правило многонога: окно иммунитета на следующий ход.
                unit.NumbImmunity = 1;
                unit.NumbImmunityCounted = false;
            }
            if (!string.IsNullOrEmpty(reason) && !unit.IsDefeated)
                Log(unit.DisplayLabel + ": «" + instance.Definition.Name + "» " + (expired ? "закончилось" : "снято") +
                    (expired || string.IsNullOrEmpty(reason) ? "." : " — " + reason + "."));
            if (why != SandboxStatusEndReason.BattleEnded && !unit.IsDefeated)
                DispatchStatusReaction(SandboxReactionEvent.Ended, instance.StatusId, instance, unit, actor, why);
        }

        // ---------- Урон и лечение ----------

        // Фиксированный урон состояния (тик, взрыв, перенос, ответный огонь):
        // не запускает on-hit, ответы и вампиризм. Временные HP и
        // неуязвимость учитываются. Возвращает реально снятые HP.
        private int DealStatusDamage(SandboxUnitState target, int amount, SandboxUnitState source, string label, SandboxDamageType type)
        {
            if (target == null || target.IsDefeated || amount <= 0)
                return 0;
            bool hostile = source == null || source.Team != target.Team;
            if (hostile && StatusHas(target, SandboxStatusOpKind.Invulnerable))
            {
                Log(target.DisplayLabel + ": «Краткая неуязвимость» — " + label + " не снимает HP.");
                return 0;
            }
            int remaining = AbsorbTemporary(target, amount);
            int before = target.HitPoints;
            target.ReceiveDamage(remaining);
            int taken = before - target.HitPoints;
            hitRecords.Add(new SandboxHitRecord(SandboxHitKind.Status, source != null ? source.Id : target.Id, target.Id,
                source != null ? source.Position : target.Position, target.Position, taken, before, target.HitPoints));
            if (source != null && source.Team != target.Team)
                source.DamageDealt += taken;
            Log(target.DisplayLabel + ": " + label + " −" + taken + " HP" + (amount > remaining ? " (временные HP поглотили " + (amount - remaining) + ")" : string.Empty) +
                (target.IsDefeated ? " — выведен из строя." : "."));
            if (taken > 0)
                AfterHpDamage(target, source, direct: false);
            if (target.IsDefeated)
                OnUnitDefeated(target);
            return taken;
        }

        private static int AbsorbTemporary(SandboxUnitState target, int amount)
        {
            foreach (SandboxStatusInstance pool in target.statuses.Where(item => item.TemporaryHitPoints > 0).OrderBy(item => item.Sequence).ToList())
            {
                int used = Math.Min(pool.TemporaryHitPoints, amount);
                pool.TemporaryHitPoints -= used;
                amount -= used;
                if (pool.TemporaryHitPoints <= 0)
                    target.statuses.Remove(pool);
                if (amount <= 0)
                    break;
            }
            return Math.Max(0, amount);
        }

        // Снятие состояний фактическим HP-уроном (Сон, Невидимость, Заморожен).
        private void AfterHpDamage(SandboxUnitState target, SandboxUnitState source, bool direct)
        {
            foreach (SandboxStatusInstance instance in target.statuses.ToList())
            {
                SandboxStatusDefinition def = instance.Definition;
                if (def.Has(SandboxStatusOpKind.RemoveOnHpDamage))
                    RemoveStatusInstance(target, instance, "фактический урон", endReason: SandboxStatusEndReason.BrokenByDamage, actor: source);
                else if (direct && def.Has(SandboxStatusOpKind.RemoveOnDirectHpHit))
                    RemoveStatusInstance(target, instance, "прямое попадание раскалывает лёд", endReason: SandboxStatusEndReason.BrokenByDamage, actor: source);
                else if (def.Has(SandboxStatusOpKind.RemoveOnDamageFromSourceSide) && source != null)
                {
                    SandboxUnitState origin = GetUnit(instance.SourceUnitId);
                    if (origin != null && origin.Team == source.Team)
                        RemoveStatusInstance(target, instance, "урон от стороны источника", endReason: SandboxStatusEndReason.BrokenByDamage, actor: source);
                }
            }
        }

        private static bool IsDirectHit(SandboxHitKind kind) =>
            kind == SandboxHitKind.Attack || kind == SandboxHitKind.FirstStrike || kind == SandboxHitKind.Retaliation || kind == SandboxHitKind.Ability ||
            kind == SandboxHitKind.Reaction;

        // Вызывается из ApplyHit до снятия HP: щиты, неуязвимость, перенос,
        // расход зарядов. Возвращает урон, который дойдёт до HP цели.
        private int ResolveDirectHitBeforeHp(SandboxHitKind kind, SandboxUnitState striker, SandboxUnitState target, int damage, out int temporaryAbsorbed)
        {
            temporaryAbsorbed = 0;
            if (!IsDirectHit(kind) || striker == null)
            {
                int envRemaining = damage;
                if (damage > 0 && StatusHas(target, SandboxStatusOpKind.Invulnerable))
                    envRemaining = 0;
                int afterTemp = AbsorbTemporary(target, envRemaining);
                temporaryAbsorbed = envRemaining - afterTemp;
                return afterTemp;
            }
            bool ranged = striker.Position.DistanceTo(target.Position) > 1;
            // Своё нападение раскрывает невидимого до разрешения.
            SandboxStatusInstance invisible = FindStatus(striker, SandboxStatusCatalog.Invisible);
            if (invisible != null)
                RemoveStatusInstance(striker, invisible, "нападение раскрывает");
            // Расход зарядов, уже учтённых в расчёте урона.
            StatusAttackMod(striker, target, ranged, consume: true);
            StatusFlatDamage(striker, target, ranged, SandboxDamageType.Physical, consume: true);
            PassiveAttackMod(striker, target, ranged, striker.Position, consume: true);
            SandboxPassiveRule heavy = ReadyRule(striker, SandboxPassiveKind.BasePenaltyIgnoreOnce);
            if (heavy != null && (striker.HasStatus("status.099") || striker.HasStatus("status.088")))
            {
                MarkUsed(striker, heavy, null);
                Log(striker.DisplayLabel + ": " + RuleName(heavy) + " — штраф урона в этом ударе не действует.");
            }
            PassiveDefenseMod(target, striker, ranged, consume: true);
            PassiveDamage(striker, target, ranged, damage, consume: true);

            if (damage <= 0)
                return 0;
            if (striker.Team != target.Team && StatusHas(target, SandboxStatusOpKind.Invulnerable))
            {
                Log(target.DisplayLabel + ": «Краткая неуязвимость» — удар не снимает HP.");
                return 0;
            }
            if (hitMagical && striker.Team != target.Team)
            {
                SandboxStatusInstance ward = target.statuses.FirstOrDefault(item => item.Definition.HasSpecial("WardSupernatural") && item.UsesLeft > 0);
                if (ward != null)
                {
                    ConsumeUse(target, ward, "погасила чары");
                    wardBlockedAt[target.Id] = currentRootAction;
                    Log(target.DisplayLabel + ": «Защита от чар» отменяет заклинание.");
                    return 0;
                }
            }
            SandboxStatusInstance shield = target.statuses.FirstOrDefault(item => item.Definition.Has(SandboxStatusOpKind.AbsorbHit) && item.UsesLeft > 0);
            if (shield != null)
            {
                ConsumeUse(target, shield, "поглотил попадание");
                Log(target.DisplayLabel + ": «" + shield.Definition.Name + "» поглощает " + damage + " урона.");
                return 0;
            }
            // Связанные раны: половина (вниз) уходит партнёру один раз, без новой брони и переноса.
            SandboxStatusInstance shared = target.statuses.FirstOrDefault(item => item.StatusId == SandboxStatusCatalog.SharedWounds);
            if (shared != null)
            {
                SandboxUnitState partner = GetUnit(shared.SourceUnitId);
                if (partner == null || partner.IsDefeated || partner.Position.DistanceTo(target.Position) > SandboxStatusRules.LinkBreakDistance)
                {
                    RemoveStatusInstance(target, shared, "партнёр недоступен — связь прекращена");
                }
                else
                {
                    int moved = damage / 2;
                    if (moved > 0)
                    {
                        damage -= moved;
                        SandboxPassiveRule survive = ReadyRule(partner, SandboxPassiveKind.SharedWoundsSurviveOnce);
                        if (survive != null && moved >= partner.HitPoints)
                        {
                            MarkUsed(partner, survive, null);
                            moved = Math.Max(0, partner.HitPoints - 1);
                            RemoveStatusInstance(target, shared, RuleName(survive) + " — связь прекращена", endReason: SandboxStatusEndReason.CleansedByPassive, actor: partner);
                        }
                        DealStatusDamage(partner, moved, null, "связанные раны (перенос от " + target.DisplayLabel + ")", SandboxDamageType.Physical);
                    }
                }
            }
            bool hadPool = target.HasStatus("status.148");
            int remaining = AbsorbTemporary(target, damage);
            temporaryAbsorbed = damage - remaining;
            if (hadPool && !target.HasStatus("status.148") && striker.Team != target.Team)
            {
                SandboxPassiveRule regrow = ReadyRule(target, SandboxPassiveKind.TempPoolExhaustedRegen);
                if (regrow != null)
                {
                    MarkUsed(target, regrow, null);
                    ApplyStatus(regrow.StatusIds[0], target, target);
                }
            }
            return remaining;
        }

        private void ResolveDirectHitAfterHp(SandboxHitKind kind, SandboxUnitState striker, SandboxUnitState target, int hpTaken, int temporaryAbsorbed)
        {
            if (hpTaken > 0)
                AfterHpDamage(target, striker, IsDirectHit(kind));
            if (!IsDirectHit(kind) || striker == null)
                return;
            bool contact = striker.Position.DistanceTo(target.Position) <= 1;
            // Болевое эхо: запомнить последнее прямое вражеское попадание.
            if (hpTaken > 0 && striker.Team != target.Team)
            {
                foreach (SandboxStatusInstance echo in target.statuses.Where(item => item.Definition.HasSpecial("PainEcho")))
                {
                    int remembered = hpTaken;
                    SandboxPassiveRule dull = ReadyRule(target, SandboxPassiveKind.PainEchoReduceOnce);
                    if (dull != null)
                    {
                        MarkUsed(target, dull, null);
                        remembered = Math.Max(0, remembered - dull.Value);
                    }
                    echo.StoredValue = Math.Min(echo.Definition.Ops.First(op => op.Kind == SandboxStatusOpKind.Special).Value, remembered / 2);
                }
            }
            // Временный вампиризм: половина снятых HP, не больше 2 за собственный ход.
            if (hpTaken > 0 && striker.Team != target.Team && !striker.IsDefeated)
            {
                foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(striker, SandboxStatusOpKind.Vampirism).ToList())
                {
                    if (Rules(striker, SandboxPassiveKind.NoVampirismWhileStatus).Any(pair => pair.Rule.StatusIds.Any(striker.HasStatus)))
                        continue;
                    int allowed = Math.Max(0, op.Value - instance.StoredValue);
                    int heal = Math.Min(allowed, hpTaken / 2);
                    if (heal <= 0)
                        continue;
                    int before = striker.HitPoints;
                    striker.Heal(heal);
                    instance.StoredValue += striker.HitPoints - before;
                    Log(striker.DisplayLabel + ": «" + instance.Definition.Name + "» +" + (striker.HitPoints - before) + " HP.");
                }
            }
            // Огненный покров: фиксированный ответный урон за контактное попадание, без цепочки.
            if (contact && hpTaken + temporaryAbsorbed > 0 && !striker.IsDefeated && striker.Team != target.Team)
            {
                foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(target, SandboxStatusOpKind.ContactRetort).ToList())
                {
                    int retort = op.Value;
                    SandboxPassiveRule shieldRule = ReadyRule(striker, SandboxPassiveKind.RetortReduceOncePerRound);
                    if (shieldRule != null)
                    {
                        MarkUsed(striker, shieldRule, null);
                        retort = Math.Max(0, retort - shieldRule.Value);
                    }
                    if (retort > 0)
                        DealStatusDamage(striker, retort, target, "«" + instance.Definition.Name + "» обжигает", op.DamageType);
                }
            }
        }

        // ---------- Активации ----------

        // Начало собственной активации: true — активация отнята сильным контролем.
        private bool ProcessStatusActivationStart(SandboxUnitState unit)
        {
            int activation = unit.ActivationCount;
            foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.Definition.Clock == SandboxStatusClock.UntilOwnActivationStart &&
                                                                                 item.AppliedAtActivation < activation).ToList())
                RemoveStatusInstance(unit, instance, "срок вышел", expired: true);

            bool skip = unit.statuses.Any(item => item.Definition.Has(SandboxStatusOpKind.SkipActivation) && item.AppliedAtActivation < activation);
            if (!unit.statuses.Any(item => item.Definition.StrongControl) && unit.StrongControlGuard)
                unit.StrongControlReleaseAtEnd = true;
            unit.SupportActions = unit.statuses.Any(item => item.Definition.HasSpecial("ExtraSupportAction") && item.AppliedAtActivation < activation) ? 1 : 0;

            foreach (SandboxStatusInstance instance in unit.statuses)
            {
                if (instance.Definition.Has(SandboxStatusOpKind.Vampirism))
                    instance.StoredValue = 0;
                if (instance.Definition.Ops.Any(op => op.Filter == SandboxStatusFilter.FirstBetweenOwnTurns))
                    instance.StoredValue = 0;
                if (instance.Definition.HasSpecial("PainfulMovement"))
                    instance.StoredValue = 0;
                if (instance.Definition.HasSpecial("GrowingDread") && instance.AppliedAtActivation < activation)
                    instance.Stacks = Math.Min(2, instance.Stacks + 1);
            }

            // 1. Восстановление — до тиков урона; погибшего не поднимает.
            foreach ((SandboxStatusInstance instance, SandboxStatusOp op) in OpsOf(unit, SandboxStatusOpKind.TickHeal).ToList())
            {
                if (instance.AppliedAtActivation >= activation || unit.IsDefeated)
                    continue;
                int before = unit.HitPoints;
                unit.Heal(op.Value);
                if (unit.HitPoints > before)
                    Log(unit.DisplayLabel + ": «" + instance.Definition.Name + "» +" + (unit.HitPoints - before) + " HP.");
            }

            // 2. Очередь тиков: по времени наложения, затем ID; новые не больше 3 HP.
            List<(SandboxStatusInstance Instance, int Amount, SandboxDamageType Type)> queue = new List<(SandboxStatusInstance, int, SandboxDamageType)>();
            foreach (SandboxStatusInstance instance in unit.statuses.OrderBy(item => item.Sequence).ThenBy(item => item.StatusId, StringComparer.Ordinal))
            {
                foreach (SandboxStatusOp op in instance.Definition.Ops)
                {
                    if (op.Kind == SandboxStatusOpKind.TickDamage)
                    {
                        int perCharge = instance.ProfileTick > 0 ? instance.ProfileTick : op.Value;
                        int count = instance.charges.Count(charge => charge.AppliedAtActivation < activation);
                        if (count <= 0)
                            continue;
                        int amount = perCharge * count + instance.FirstTickBonus;
                        instance.FirstTickBonus = 0;
                        queue.Add((instance, amount, op.DamageType));
                    }
                    else if (op.Kind == SandboxStatusOpKind.Special && op.Executor == "PainEcho" &&
                             instance.AppliedAtActivation < activation && instance.StoredValue > 0)
                    {
                        queue.Add((instance, instance.StoredValue, SandboxDamageType.Supernatural));
                    }
                }
            }
            int budget = SandboxStatusRules.MaxTickDamagePerActivationStart;
            foreach ((SandboxStatusInstance instance, int amount, SandboxDamageType type) in queue)
            {
                if (unit.IsDefeated)
                    break;
                int dealt = Math.Min(amount, budget);
                if (dealt <= 0)
                {
                    Log(unit.DisplayLabel + ": «" + instance.Definition.Name + "» — предел " + SandboxStatusRules.MaxTickDamagePerActivationStart + " HP тиков за начало хода исчерпан.");
                    continue;
                }
                budget -= dealt;
                int tickTaken = DealStatusDamage(unit, dealt, GetUnit(instance.SourceUnitId), "«" + instance.Definition.Name + "»", type);
                Emit(SandboxCombatEventKind.StatusTick, GetUnit(instance.SourceUnitId), unit, e =>
                {
                    e.StatusId = instance.StatusId; e.StatusInstanceId = instance.InstanceId; e.Requested = dealt; e.ActualHp = tickTaken;
                    e.Delivery = SandboxDeliveryKind.Tick; e.DamageType = type;
                });
                if (unit.statuses.Contains(instance) || !unit.IsDefeated)
                    DispatchStatusReaction(SandboxReactionEvent.Tick, instance.StatusId, instance, unit, GetUnit(instance.SourceUnitId), amount: tickTaken);
                if (instance.Definition.HasSpecial("PainEcho"))
                    RemoveStatusInstance(unit, instance, "эхо отозвалось");
            }
            if (unit.IsDefeated)
                return true;

            // 3. Паника: обязательный отход от источника до 2 безопасных гексов.
            SandboxStatusInstance panic = unit.statuses.FirstOrDefault(item => item.Definition.HasSpecial("FleeFromSource") && item.AppliedAtActivation < activation);
            if (panic != null && !unit.IsOffBoard)
                ForcePanicRetreat(unit, panic);

            // 4. Набранный темп теряется при удержании.
            SandboxStatusInstance momentum = FindStatus(unit, SandboxStatusCatalog.Momentum);
            if (momentum != null && IsHeld(unit))
                RemoveStatusInstance(unit, momentum, "удержание сбивает темп");
            return skip;
        }

        // Источник состояния начинает свою активацию (Мучительная связь).
        private void ProcessStatusSourceActivationStart(SandboxUnitState source)
        {
            foreach (SandboxUnitState target in units.Where(item => !item.IsDefeated).ToList())
            {
                foreach (SandboxStatusInstance instance in target.statuses.Where(item => item.SourceUnitId == source.Id && item.Definition.HasSpecial("DrainLink")).ToList())
                {
                    if (instance.AppliedAtSourceActivation >= source.ActivationCount)
                        continue;
                    int breakAt = Rules(target, SandboxPassiveKind.DrainLinkShortRange).Any()
                        ? Rules(target, SandboxPassiveKind.DrainLinkShortRange).First().Rule.Value + 1
                        : SandboxStatusRules.DrainLinkBreakDistance;
                    if (source.Position.DistanceTo(target.Position) >= breakAt)
                    {
                        RemoveStatusInstance(target, instance, "пара слишком далеко — связь порвалась", endReason: SandboxStatusEndReason.BrokenByDistance);
                        continue;
                    }
                    int taken = DealStatusDamage(target, 1, source, "«" + instance.Definition.Name + "»", SandboxDamageType.Supernatural);
                    int heal = Math.Min(1, taken);
                    if (heal > 0 && !source.IsDefeated)
                        source.Heal(heal);
                }
            }
        }

        private void ProcessStatusActivationEnd(SandboxUnitState unit)
        {
            StatusRevision++;
            // «Сбивает пламя с товарища»: союзник в стойке и под Горением рядом с носителем.
            if (unit.IsGuarding && unit.HasStatus(SandboxStatusCatalog.Burning))
            {
                foreach (SandboxUnitState helper in units.Where(u => u != unit && !u.IsDefeated && u.Team == unit.Team && u.Position.DistanceTo(unit.Position) == 1)
                             .OrderBy(u => u.Id, StringComparer.Ordinal))
                {
                    SandboxPassiveRule douse = ReadyRule(helper, SandboxPassiveKind.AllyGuardExtinguish);
                    if (douse == null)
                        continue;
                    MarkUsed(helper, douse, null);
                    RemoveStatus(unit, SandboxStatusCatalog.Burning, RuleName(douse) + " (" + helper.DisplayLabel + ")", actor: helper);
                    break;
                }
            }
            // «Щит любит ожидание»: не атаковал со щитом — срок щита +1 раз за бой.
            SandboxStatusInstance waitingShield = FindStatus(unit, "status.132");
            if (waitingShield != null && !unit.passive.AttackedThisTurn)
            {
                SandboxPassiveRule patience = ReadyRule(unit, SandboxPassiveKind.ShieldExtendIfWaited);
                if (patience != null)
                {
                    MarkUsed(unit, patience, null);
                    ExtendInstance(waitingShield, patience.Value, 0);
                    Log(unit.DisplayLabel + ": " + RuleName(patience) + " — щит держится на ход дольше.");
                }
            }
            int activation = unit.ActivationCount;
            foreach (SandboxStatusInstance instance in unit.statuses.ToList())
            {
                SandboxStatusDefinition def = instance.Definition;
                if (def.Clock != SandboxStatusClock.OwnActivations || instance.UntilBattleEnd)
                    continue;
                int duration = instance.EffectiveDuration;
                foreach (SandboxStatusCharge charge in instance.charges.ToList())
                {
                    if (activation <= charge.AppliedAtActivation)
                        continue;
                    charge.Elapsed++;
                    if (charge.Elapsed >= duration)
                        instance.charges.Remove(charge);
                }
                if (instance.charges.Count == 0)
                    RemoveStatusInstance(unit, instance, "срок вышел", expired: true);
                else if (def.Stacking == SandboxStatusStacking.Single)
                    instance.Elapsed = instance.charges[0].Elapsed;
            }
            // Сроки по активациям источника.
            foreach (SandboxUnitState target in units)
            {
                foreach (SandboxStatusInstance instance in target.statuses.Where(item => item.SourceUnitId == unit.Id &&
                                                                                        item.Definition.Clock == SandboxStatusClock.SourceActivations).ToList())
                {
                    if (activation <= instance.AppliedAtSourceActivation)
                        continue;
                    instance.Elapsed++;
                    if (instance.Elapsed >= instance.EffectiveDuration)
                        RemoveStatusInstance(target, instance, "срок вышел", expired: true);
                }
            }
            if (unit.StrongControlReleaseAtEnd)
            {
                unit.StrongControlGuard = false;
                unit.StrongControlReleaseAtEnd = false;
            }
            unit.SupportActions = 0;
        }

        private void ProcessStatusRoundStart()
        {
            frostChargesThisRound.Clear();
            foreach (SandboxUnitState unit in units.Where(item => !item.IsDefeated).ToList())
            {
                foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.Definition.Clock == SandboxStatusClock.RoundTrigger &&
                                                                                     item.TriggerRound <= Round).OrderBy(item => item.Sequence).ToList())
                {
                    if (!unit.statuses.Contains(instance) || unit.IsDefeated)
                        continue;
                    unit.statuses.Remove(instance);
                    SandboxStatusOp op = instance.Definition.Ops.First(item => item.Kind == SandboxStatusOpKind.Special);
                    bool sentence = op.Executor == "DelayedSentence";
                    int blast = op.Value;
                    SandboxPassiveRule ward = ReadyRule(unit, SandboxPassiveKind.DelayedTriggerReduceOnce, null, instance.StatusId);
                    if (ward != null)
                    {
                        MarkUsed(unit, ward, null);
                        blast = Math.Max(1, blast - ward.Value);
                    }
                    DealStatusDamage(unit, blast, GetUnit(instance.SourceUnitId), sentence ? "«Приговор» исполнен" : "«Отложенный взрыв»",
                        sentence ? SandboxDamageType.Supernatural : SandboxDamageType.Fire);
                }
            }
            EvaluateBattleOutcome();
        }

        // Связи состояний по дистанции (после каждого фактического шага).
        private void CheckStatusLinkDistances()
        {
            foreach (SandboxUnitState target in units)
            {
                foreach (SandboxStatusInstance instance in target.statuses.ToList())
                {
                    SandboxStatusDefinition def = instance.Definition;
                    if (!def.HasSpecial("DuelMirror") && !def.HasSpecial("SharedWounds"))
                        continue;
                    SandboxUnitState other = GetUnit(instance.SourceUnitId);
                    if (other == null || other.IsDefeated || other.Position.DistanceTo(target.Position) > SandboxStatusRules.LinkBreakDistance)
                        RemoveStatusInstance(target, instance, "дальше " + SandboxStatusRules.LinkBreakDistance + " гексов — связь прекращена", endReason: SandboxStatusEndReason.BrokenByDistance);
                }
            }
        }

        private void OnStatusUnitDefeated(SandboxUnitState dead)
        {
            foreach (SandboxStatusInstance instance in dead.statuses.ToList())
                dead.statuses.Remove(instance);
            dead.StrongControlGuard = false;
            foreach (SandboxUnitState unit in units.Where(item => item != dead))
            {
                foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.SourceUnitId == dead.Id &&
                                                                                     (item.Definition.RemovedOnSourceDeath ||
                                                                                      item.Definition.Clock == SandboxStatusClock.SourceActivations)).ToList())
                    RemoveStatusInstance(unit, instance, "источник пал", endReason: SandboxStatusEndReason.SourceFinalDeath, actor: dead);
            }
        }

        // Конец боя: временные состояния, заряды и связи сняты; реальные HP не лечатся.
        private void ClearStatusesAtBattleEnd()
        {
            if (statusesClearedAtEnd)
                return;
            statusesClearedAtEnd = true;
            foreach (SandboxUnitState unit in units)
            {
                unit.statuses.Clear();
                unit.StrongControlGuard = false;
                unit.StrongControlReleaseAtEnd = false;
                unit.SupportActions = 0;
                ClampHitPointsToMax(unit);
            }
        }

        // ---------- Движение ----------

        private void ForcePanicRetreat(SandboxUnitState unit, SandboxStatusInstance panic)
        {
            SandboxUnitState source = GetUnit(panic.SourceUnitId);
            if (source == null || source.IsDefeated)
                return;
            HexCoord origin = unit.Position;
            IReadOnlyDictionary<HexCoord, int> reachable = BuildReachable(unit, Math.Max(0, unit.RemainingMovement));
            HexCoord best = origin;
            int bestDistance = origin.DistanceTo(source.Position);
            foreach (KeyValuePair<HexCoord, int> pair in reachable.OrderBy(item => item.Key))
            {
                if (pair.Key.DistanceTo(origin) > SandboxStatusRules.PanicRetreat || HazardAt(unit, pair.Key) > 0)
                    continue;
                int distance = pair.Key.DistanceTo(source.Position);
                if (distance > bestDistance)
                {
                    best = pair.Key;
                    bestDistance = distance;
                }
            }
            if (best != origin)
            {
                SetUnitPosition(unit, best);
                Log(unit.DisplayLabel + " в панике отступает " + origin + " → " + best + ".");
            }
            unit.RemainingMovement = 0;
            unit.ActsWithoutMovement = true;
        }

        // Фильтр добровольного движения: Страх/Ужас не дают приблизиться к источнику.
        private void FilterStatusReachable(SandboxUnitState unit, Dictionary<HexCoord, int> reachable)
        {
            if (reachable.Count == 0)
                return;
            if (StatusHas(unit, SandboxStatusOpKind.NoVoluntaryMove))
            {
                reachable.Clear();
                return;
            }
            foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.Definition.Has(SandboxStatusOpKind.CannotApproachSource)))
            {
                if (!IsSourceNear(unit, instance))
                    continue;
                SandboxUnitState source = GetUnit(instance.SourceUnitId);
                int current = unit.Position.DistanceTo(source.Position);
                foreach (HexCoord cell in reachable.Keys.Where(cell => cell.DistanceTo(source.Position) < current).ToList())
                    reachable.Remove(cell);
            }
        }

        // Шаг по пути: вода гасит горение и смывает смолу, Ранящее перемещение бьёт.
        private void AfterStatusStep(SandboxUnitState unit, HexCoord cell)
        {
            if (HasCellState(cell, SandboxCellState.ShallowWater))
            {
                RemoveStatus(unit, SandboxStatusCatalog.Burning, "мелководье гасит огонь");
                RemoveStatus(unit, SandboxStatusCatalog.Tarred, "мелководье смывает смолу");
            }
            foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.Definition.HasSpecial("PainfulMovement")).ToList())
            {
                int cap = instance.Definition.Ops.First(op => op.Kind == SandboxStatusOpKind.Special).Value;
                if (instance.StoredValue >= cap)
                    continue;
                instance.StoredValue++;
                SandboxPassiveRule mend = ReadyRule(unit, SandboxPassiveKind.StatusExtraDamageReduceOnce, null, instance.StatusId);
                if (mend != null)
                {
                    MarkUsed(unit, mend, null);
                    Log(unit.DisplayLabel + ": " + RuleName(mend) + " — урон перемещения поглощён.");
                    continue;
                }
                DealStatusDamage(unit, 1, GetUnit(instance.SourceUnitId), "«" + instance.Definition.Name + "»", SandboxDamageType.Physical);
            }
        }

        private void AfterStatusVoluntaryMove(SandboxUnitState unit, int steps)
        {
            SandboxStatusInstance momentum = FindStatus(unit, SandboxStatusCatalog.Momentum);
            if (momentum == null || steps < 2 || momentum.LastTriggerActivation == unit.ActivationCount)
                return;
            momentum.LastTriggerActivation = unit.ActivationCount;
            if (momentum.charges.Count < momentum.Definition.MaxCharges)
            {
                momentum.charges.Add(new SandboxStatusCharge { AppliedAtActivation = unit.ActivationCount });
                Log(unit.DisplayLabel + ": «" + momentum.Definition.Name + "» — заряд " + momentum.charges.Count + ".");
            }
        }

        // ---------- Очищение ----------

        private IEnumerable<SandboxContextAction> StatusCleanseActions(SandboxUnitState unit)
        {
            foreach (SandboxStatusInstance instance in unit.statuses.Where(item => item.Definition.IsHarmful &&
                                                                                 (item.Definition.Cleanse & SandboxStatusCleanse.Self) != 0)
                         .OrderBy(item => item.Sequence))
                yield return new SandboxContextAction
                {
                    Kind = SandboxContextActionKind.CleanseStatus, TargetUnitId = unit.Id, StatusId = instance.StatusId,
                    Label = Capitalize(instance.Definition.CleanseVerb) + ": «" + instance.Definition.Name + "»"
                };
            foreach (SandboxUnitState ally in units.Where(other => other != unit && other.Team == unit.Team && !other.IsDefeated && !other.IsOffBoard &&
                                                                 other.Position.DistanceTo(unit.Position) == 1).OrderBy(other => other.Id, StringComparer.Ordinal))
            {
                foreach (SandboxStatusInstance instance in ally.statuses.Where(item => item.Definition.IsHarmful &&
                                                                                     (item.Definition.Cleanse & SandboxStatusCleanse.Ally) != 0)
                             .OrderBy(item => item.Sequence))
                    yield return new SandboxContextAction
                    {
                        Kind = SandboxContextActionKind.CleanseStatus, TargetUnitId = ally.Id, StatusId = instance.StatusId,
                        Label = "Помочь " + ally.DisplayLabel + ": " + instance.Definition.CleanseVerb + " «" + instance.Definition.Name + "»"
                    };
            }
        }

        private string ExecuteCleanse(SandboxUnitState unit, SandboxContextAction action)
        {
            SandboxUnitState target = GetUnit(action.TargetUnitId);
            SandboxStatusInstance instance = FindStatus(target, action.StatusId);
            if (instance == null)
                return "Состояния уже нет.";
            string name = instance.Definition.Name;
            RemoveStatusInstance(target, instance, null, endReason: SandboxStatusEndReason.CleansedByAction, actor: unit);
            return target == unit
                ? unit.DisplayLabel + ": " + instance.Definition.CleanseVerb + " — «" + name + "» снято."
                : unit.DisplayLabel + " помогает " + target.DisplayLabel + ": «" + name + "» снято.";
        }

        private static string Capitalize(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // ---------- Представление ----------

        // Действующие состояния для показа. Врагу невидимый/скрытый/подземный
        // боец не раскрывается своими значками; погибшие значков не имеют.
        public List<SandboxStatusView> GetStatusViews(string unitId, SandboxTeam viewer)
        {
            List<SandboxStatusView> result = new List<SandboxStatusView>();
            SandboxUnitState unit = GetUnit(unitId);
            if (unit == null || unit.IsDefeated)
                return result;
            bool enemyView = viewer != unit.Team;
            if (enemyView && (unit.IsHidden || unit.IsUnderground || unit.AwaitingPlacement || unit.HasStatus(SandboxStatusCatalog.Invisible)))
                return result;

            foreach (SandboxStatusInstance instance in unit.statuses)
                result.Add(BuildView(unit, instance));

            // Фасады прежних механизмов: паразит, корни, захват, стойка.
            SandboxUnitState parasite = GetAttachedParasite(unit);
            if (parasite != null && !parasite.IsDefeated)
                result.Add(FacadeView(SandboxStatusCatalog.Parasite, parasite, "В начале хода паразита −HP; снять — 1 действие себя или соседа."));
            SandboxLink hold = FindHoldingLink(unit);
            if (hold != null && (hold.Kind == SandboxLinkKind.Vine || hold.Kind == SandboxLinkKind.Engulf || hold.Kind == SandboxLinkKind.Tendril))
            {
                string id = hold.Kind == SandboxLinkKind.Engulf ? SandboxStatusCatalog.Grappled : SandboxStatusCatalog.Rooted;
                result.Add(FacadeView(id, GetUnit(hold.SourceId), "Добровольное движение запрещено; атака с места и «Вырваться» доступны."));
            }
            if (Rules(unit, SandboxPassiveKind.HolderImmobilizedView).Any() && links.Any(link => link.Kind == SandboxLinkKind.Engulf && link.SourceId == unit.Id))
                result.Add(FacadeView("status.043", null, "Держит захват — сам не двигается («Удерживает с усилием»)."));
            if (unit.IsGuarding)
            {
                SandboxStatusView guard = FacadeView(SandboxStatusCatalog.Guard, null,
                    "Защита +" + SandboxCombatTagRules.GetGuardBonus(unit) + " (50% вниз, минимум 1) до начала следующего хода.");
                guard.Dependent = false;
                result.Add(guard);
            }
            return result.OrderBy(view => view.Priority).ThenBy(view => view.Sequence).ThenBy(view => view.StatusId, StringComparer.Ordinal).ToList();
        }

        private SandboxStatusView FacadeView(string statusId, SandboxUnitState source, string effect)
        {
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(statusId);
            return new SandboxStatusView
            {
                StatusId = statusId, Name = def.Name, IconId = def.IconId, Polarity = def.Polarity, StrongControl = def.StrongControl,
                Priority = SandboxStatusCatalog.DisplayPriority(def), Sequence = int.MaxValue / 2, TurnsLeft = -1, Charges = 1,
                Dependent = true, SourceLabel = source != null ? source.DisplayLabel : string.Empty, Effect = effect
            };
        }

        private SandboxStatusView BuildView(SandboxUnitState unit, SandboxStatusInstance instance)
        {
            SandboxStatusDefinition def = instance.Definition;
            SandboxUnitState source = GetUnit(instance.SourceUnitId);
            SandboxStatusView view = new SandboxStatusView
            {
                StatusId = def.Id, Name = def.Name, IconId = def.IconId, Polarity = def.Polarity, StrongControl = def.StrongControl,
                Priority = SandboxStatusCatalog.DisplayPriority(def), Sequence = instance.Sequence,
                Charges = def.Stacking == SandboxStatusStacking.Charges ? instance.charges.Count : Math.Max(1, instance.UsesLeft),
                SourceLabel = source != null && source != unit ? source.DisplayLabel : string.Empty,
                Effect = SandboxStatusText.Describe(def, instance),
                Removal = SandboxStatusText.Removal(def)
            };
            switch (def.Clock)
            {
                case SandboxStatusClock.OwnActivations:
                    view.TurnsLeft = instance.charges.Count == 0 ? 0 : instance.charges.Max(charge => instance.EffectiveDuration - charge.Elapsed);
                    if (def.Stacking == SandboxStatusStacking.Charges)
                        view.ChargeTurnsLeft.AddRange(instance.charges.Select(charge => instance.EffectiveDuration - charge.Elapsed));
                    break;
                case SandboxStatusClock.SourceActivations:
                    view.TurnsLeft = Math.Max(0, instance.EffectiveDuration - instance.Elapsed);
                    break;
                case SandboxStatusClock.RoundTrigger:
                    view.TriggerRound = instance.TriggerRound;
                    break;
                case SandboxStatusClock.UntilOwnActivationStart:
                    view.TurnsLeft = 1;
                    break;
                default:
                    view.TurnsLeft = -1;
                    view.Dependent = def.Clock == SandboxStatusClock.UntilRemoved;
                    break;
            }
            if (instance.TemporaryHitPoints > 0)
                view.Charges = instance.TemporaryHitPoints;
            return view;
        }
    }

    // Текст подсказки из операций определения (числа — текущие, не из описания).
    public static class SandboxStatusText
    {
        public static string Describe(SandboxStatusDefinition def, SandboxStatusInstance instance = null)
        {
            List<string> parts = new List<string>();
            foreach (SandboxStatusOp op in def.Ops)
            {
                string when = op.MinCharges > 1 ? " (при " + op.MinCharges + " зарядах)" : string.Empty;
                switch (op.Kind)
                {
                    case SandboxStatusOpKind.AttackMod: parts.Add(Signed(op.Value) + " Атака" + Filter(op.Filter) + when); break;
                    case SandboxStatusOpKind.DefenseMod: parts.Add(Signed(op.Value) + " Защита" + Filter(op.Filter)); break;
                    case SandboxStatusOpKind.MovementMod: parts.Add(Signed(op.Value) + " движение" + when); break;
                    case SandboxStatusOpKind.InitiativeMod: parts.Add(Signed(op.Value) + " Инициатива со следующего раунда"); break;
                    case SandboxStatusOpKind.MaxHitPointsMod: parts.Add("MaxHP " + Signed(op.Value) + " (не урон)"); break;
                    case SandboxStatusOpKind.BaseDamageMod: parts.Add("базовый Урон " + Signed(op.Value) + Filter(op.Filter) + ", минимум 1"); break;
                    case SandboxStatusOpKind.TickDamage:
                        int tick = instance != null && instance.ProfileTick > 0 ? instance.ProfileTick : op.Value;
                        parts.Add(tick + " урона в начале твоего хода" + (def.Stacking == SandboxStatusStacking.Charges ? " за заряд" : string.Empty)); break;
                    case SandboxStatusOpKind.TickHeal: parts.Add("+" + op.Value + " HP в начале твоего хода"); break;
                    case SandboxStatusOpKind.NoVoluntaryMove: parts.Add("нельзя добровольно двигаться; действие с места доступно"); break;
                    case SandboxStatusOpKind.SkipActivation: parts.Add("пропускает движение и действие хода"); break;
                    case SandboxStatusOpKind.NoRetaliation: parts.Add("не отвечает на удары"); break;
                    case SandboxStatusOpKind.NoOffense: parts.Add("нельзя нападать; защита и помощь доступны"); break;
                    case SandboxStatusOpKind.NoRangedAttack: parts.Add("дальняя атака недоступна"); break;
                    case SandboxStatusOpKind.RangedAdjacentOnly: parts.Add("бить можно только соседнюю цель"); break;
                    case SandboxStatusOpKind.CannotTargetSource: parts.Add("нельзя нападать на источник"); break;
                    case SandboxStatusOpKind.CannotApproachSource: parts.Add("нельзя приближаться к источнику в пределах 3 гексов"); break;
                    case SandboxStatusOpKind.MustTargetSource: parts.Add("нападать нужно на источник, если он достижим"); break;
                    case SandboxStatusOpKind.NoPositiveAttack: parts.Add("не получает временных прибавок к Атаке"); break;
                    case SandboxStatusOpKind.CannotBeTargeted: parts.Add("враг не может выбрать прямой целью"); break;
                    case SandboxStatusOpKind.AbsorbHit: parts.Add("поглощает 1 прямое попадание"); break;
                    case SandboxStatusOpKind.TemporaryHitPoints: parts.Add((instance != null ? instance.TemporaryHitPoints : op.Value) + " временных HP — урон сначала расходует их"); break;
                    case SandboxStatusOpKind.Invulnerable: parts.Add("не теряет HP от враждебного урона"); break;
                    case SandboxStatusOpKind.IncomingFlat: parts.Add((op.Value > 0 ? "+" + op.Value + " к урону по носителю" : op.Value + " к урону по носителю, минимум 1") + Filter(op.Filter)); break;
                    case SandboxStatusOpKind.OutgoingFlat: parts.Add(Signed(op.Value) + " к итоговому урону " + (op.ConsumesUse ? "следующей атаки" : "обычной атаки") + (op.Value < 0 ? ", минимум 1" : string.Empty)); break;
                    case SandboxStatusOpKind.IncomingAttackMod: parts.Add("враги получают +" + op.Value + " Атаки против носителя"); break;
                    case SandboxStatusOpKind.Vampirism: parts.Add("лечит половину снятых HP, не больше " + op.Value + " за ход"); break;
                    case SandboxStatusOpKind.ContactRetort: parts.Add("напавший вплотную получает " + op.Value + " огненного урона"); break;
                    case SandboxStatusOpKind.RemoveOnHpDamage: parts.Add("снимается фактическим уроном HP"); break;
                    case SandboxStatusOpKind.RemoveOnDirectHpHit: parts.Add("прямое попадание с уроном снимает"); break;
                    case SandboxStatusOpKind.RemoveOnDamageFromSourceSide: parts.Add("урон от стороны источника снимает"); break;
                    case SandboxStatusOpKind.Special: parts.Add(Special(op, instance)); break;
                }
            }
            if (def.ImmuneTo.Count > 0)
                parts.Add("невосприимчивость: " + string.Join(", ", def.ImmuneTo.Select(id => SandboxStatusCatalog.Get(id)?.Name ?? id)));
            if (def.Uses > 0 && instance != null)
                parts.Add("осталось срабатываний: " + instance.UsesLeft);
            string text = string.Join("; ", parts.Where(part => !string.IsNullOrEmpty(part)));
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) + "." : string.Empty;
        }

        public static string Removal(SandboxStatusDefinition def)
        {
            if (!def.IsHarmful || def.Cleanse == SandboxStatusCleanse.None)
                return string.Empty;
            string who = def.Cleanse == SandboxStatusCleanse.Ally ? "соседний союзник" : def.Cleanse == SandboxStatusCleanse.Self ? "сам" : "сам или соседний союзник";
            return "Снять: " + who + " за 1 действие (" + def.CleanseVerb + ")." + (def.RemovedOnSourceDeath ? " Гибель источника снимает." : string.Empty);
        }

        private static string Special(SandboxStatusOp op, SandboxStatusInstance instance)
        {
            switch (op.Executor)
            {
                case "DrainLink": return "в начале хода источника −1 HP, источник лечится на снятое";
                case "GrowingDread": return "ступень страха в начале хода: −1 Атака за ступень (сейчас " + (instance?.Stacks ?? 0) + ", максимум 2)";
                case "DelayedBurst": return op.Value + " урона носителю после полного раунда предупреждения";
                case "DelayedSentence": return op.Value + " фиксированного сверхъестественного урона после полного раунда";
                case "PainEcho": return "в начале хода — половина урона последнего попадания (вниз, максимум " + op.Value + ")";
                case "FrostToFreeze": return "при 3 зарядах иней становится «Заморожен»";
                case "PainfulMovement": return "каждый пройденный гекс — 1 урон, максимум " + op.Value + " между ходами";
                case "FleeFromSource": return "в начале хода отступает от источника до 2 гексов";
                case "DuelMirror": return "источник тоже получает +" + op.Value + " Атаки против носителя";
                case "SharedWounds": return "половина прямого урона (вниз) уходит связанному союзнику";
                case "WardSupernatural": return "отменяет 1 враждебное сверхъестественное состояние";
                case "MirrorSupernatural": return "отражает 1 враждебное сверхъестественное состояние в источник";
                case "ExtraSupportAction": return "1 дополнительное действие только на помощь, освобождение или очищение";
                case "MomentumMovement": return "+1 движение за заряд; заряд за проход от 2 гексов за ход";
                case "IncubationCocoon": return "при гибели носителя — кокон (пока не подключён)";
                default: return string.Empty;
            }
        }

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        private static string Filter(SandboxStatusFilter filter)
        {
            switch (filter)
            {
                case SandboxStatusFilter.Melee: return " (ближний бой)";
                case SandboxStatusFilter.Ranged: return " (дальние атаки)";
                case SandboxStatusFilter.VsSource: return " против источника";
                case SandboxStatusFilter.Physical: return " (физический урон)";
                case SandboxStatusFilter.Fire: return " (огонь)";
                case SandboxStatusFilter.WhileSourceNear: return ", пока источник видим в 3 гексах";
                case SandboxStatusFilter.WoundThreshold: return " при HP ≤ половины (+2 при ≤ четверти)";
                case SandboxStatusFilter.SourceOncePerSourceTurn: return " (первое попадание источника за его ход)";
                case SandboxStatusFilter.FirstBetweenOwnTurns: return " (первое попадание врага между твоими ходами)";
                default: return string.Empty;
            }
        }
    }
}
