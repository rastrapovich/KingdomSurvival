using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Пакет состояний, этап 9: предпросмотр удара без побочных эффектов.
    // Прямой урон, урон по HP после щита/неуязвимости/переноса/временных HP,
    // новые состояния удара и причина отказа, ожидаемые тики цели — отдельно,
    // не суммируются в «гарантированный» урон. ИИ читает те же числа.
    public sealed partial class SandboxBattle
    {
        private void EnrichPreview(SandboxAttackPreview preview, SandboxUnitState attacker, SandboxUnitState target, bool ranged)
        {
            if (preview == null || !preview.IsValid || attacker == null || target == null)
                return;
            preview.HpDamage = PredictHpDamage(attacker, target, preview.Damage, preview.Notes);
            preview.TargetHitPointsAfter = Math.Max(0, target.HitPoints - preview.HpDamage);

            if (preview.HpDamage > 0)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.OnHitApplyStatus))
                {
                    if (!Both(rule, attacker, target, ranged, preview.HpDamage) || !LimitReady(attacker, rule, target.Id))
                        continue;
                    if (rule.Limit == SandboxPassiveLimit.EveryNth && attacker.passive.Counter(rule.Key) + 1 < Math.Max(1, rule.LimitN))
                        continue;
                    preview.StatusNotes.Add(StatusPreviewNote(rule.StatusIds[0], target, attacker, rule.Profile, def.Name));
                }
                if (!ranged)
                {
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.OnHitTakenApplyStatusToAttacker))
                    {
                        if (preview.HpDamage >= target.HitPoints || !LimitReady(target, rule, attacker.Id))
                            continue;
                        preview.StatusNotes.Add("в ответ на нападающего: " + StatusPreviewNote(rule.StatusIds[0], attacker, target, rule.Profile, def.Name));
                    }
                }
            }
            preview.UpcomingTicks = UpcomingTickDamage(target);
            // Этап 16: перехват защитником и спасение — видно до удара.
            SandboxUnitState guard = PreviewInterceptor(attacker, target);
            if (guard != target)
                preview.Notes.Add("«Под защитой союзника»: удар примет " + guard.DisplayLabel + ".");
            if (target.IsDying)
                preview.Notes.Add("«При смерти»: удар — окончательная гибель.");
        }

        // Кто примет удар по цели (без изменения состояния).
        public SandboxUnitState PreviewInterceptor(SandboxUnitState attacker, SandboxUnitState target)
        {
            if (attacker == null || target == null || attacker.Team == target.Team)
                return target;
            SandboxStatusInstance cover = target.statuses.FirstOrDefault(s => s.StatusId == GuardedId);
            SandboxUnitState guardian = cover != null ? GetUnit(cover.SourceUnitId) : null;
            return guardian != null && CanGuardNow(guardian, target) ? guardian : target;
        }

        // Кто ударит за выход из соседства на маршруте к клетке (предпросмотр пути и ИИ).
        public IReadOnlyList<SandboxUnitState> LeaveReactorsOnPath(string unitId, HexCoord destination)
        {
            List<SandboxUnitState> result = new List<SandboxUnitState>();
            SandboxUnitState mover = GetUnit(unitId);
            if (mover == null || !SandboxMovementPath.TryBuild(this, unitId, destination, out IReadOnlyList<HexCoord> steps, out _))
                return result;
            for (int i = 1; i < steps.Count; i++)
            {
                foreach (SandboxUnitState reactor in units.Where(u => u.Team != mover.Team && u.Position.DistanceTo(steps[i - 1]) == 1 &&
                                                                      u.Position.DistanceTo(steps[i]) > 1).OrderBy(u => u.Id, StringComparer.Ordinal))
                {
                    if (result.Contains(reactor) || !CanReact(reactor) || ReadyRule(reactor, SandboxPassiveKind.LeaveAdjacencyStrike) == null)
                        continue;
                    if (mover.passive.HitAndRunTargets.Contains(reactor.Id) && HasPassive(mover, SandboxPassiveKind.HitAndRun))
                        continue;
                    result.Add(reactor);
                }
            }
            return result;
        }

        // Объект — кокон стороны противника (его стоит разбить до вылупления).
        public bool IsEnemyCocoon(SandboxUnitState unit, string objectId)
        {
            SandboxBattleObject item = string.IsNullOrEmpty(objectId) ? null : GetObject(objectId);
            return unit != null && item != null && !item.IsDestroyed && item.Kind == SandboxObjectKind.Cocoon && item.SnapshotTeam != unit.Team;
        }

        // Ожидаемый урон по HP без изменения состояния (щиты, неуязвимость,
        // перенос ран, временные HP, «Живучий»).
        internal int PredictHpDamage(SandboxUnitState attacker, SandboxUnitState target, int damage, List<string> notes)
        {
            if (damage <= 0)
                return 0;
            if (attacker.Team != target.Team && StatusHas(target, SandboxStatusOpKind.Invulnerable))
            {
                notes?.Add("«Краткая неуязвимость»: HP не снимается");
                return 0;
            }
            SandboxStatusInstance shield = target.statuses.FirstOrDefault(item => item.Definition.Has(SandboxStatusOpKind.AbsorbHit) && item.UsesLeft > 0);
            if (shield != null)
            {
                notes?.Add("«" + shield.Definition.Name + "» поглотит удар");
                return 0;
            }
            SandboxStatusInstance shared = target.statuses.FirstOrDefault(item => item.StatusId == SandboxStatusCatalog.SharedWounds);
            if (shared != null)
            {
                SandboxUnitState partner = GetUnit(shared.SourceUnitId);
                if (partner != null && !partner.IsDefeated && partner.Position.DistanceTo(target.Position) <= SandboxStatusRules.LinkBreakDistance && damage / 2 > 0)
                {
                    notes?.Add(damage / 2 + " уйдёт " + partner.DisplayLabel + " («Связанные раны»)");
                    damage -= damage / 2;
                }
            }
            int temporary = target.statuses.Sum(item => item.TemporaryHitPoints);
            if (temporary > 0)
            {
                int absorbed = Math.Min(temporary, damage);
                notes?.Add("временные HP поглотят " + absorbed);
                damage -= absorbed;
            }
            if (damage >= target.HitPoints && target.HitPoints > 0 &&
                Rules(target, SandboxPassiveKind.CheatDeathOnce).Any(pair => !target.passive.UsedInBattle.Contains(pair.Rule.Key)))
            {
                notes?.Add("выдержит смертельный удар («" + Rules(target, SandboxPassiveKind.CheatDeathOnce).First().Def.Name + "»)");
                damage = target.HitPoints - 1;
            }
            return Math.Max(0, Math.Min(damage, target.HitPoints));
        }

        // Пройдёт ли наложение (без расхода зарядов): null — да, иначе причина.
        public string PreviewStatusRejection(string statusId, SandboxUnitState target, SandboxUnitState source, SandboxStatusProfile profile = null)
        {
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(statusId);
            if (def == null || target == null || target.IsDefeated)
                return "недоступно";
            SandboxStatusInstance immunity = target.statuses.FirstOrDefault(item => item.Definition.ImmuneTo.Contains(statusId));
            if (immunity != null)
                return "невосприимчив («" + immunity.Definition.Name + "»)";
            foreach ((SandboxPassiveDefinition tag, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusImmunity))
            {
                if (rule.StatusIds.Contains(statusId))
                    return "невосприимчив («" + tag.Name + "»)";
            }
            foreach ((SandboxPassiveDefinition tag, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusCancelOnce))
            {
                if (rule.StatusIds.Contains(statusId) && LimitReady(target, rule, null))
                    return "отменит «" + tag.Name + "»";
            }
            if (def.IsHarmful && source != null && source.Team != target.Team && def.Supernatural &&
                target.statuses.Any(item => (item.Definition.HasSpecial("WardSupernatural") || item.Definition.HasSpecial("MirrorSupernatural")) && item.UsesLeft > 0))
                return "отменит защита от чар";
            if (def.StrongControl && (target.StrongControlGuard || target.statuses.Any(item => item.Definition.StrongControl)))
                return "защита от повторного сильного контроля";
            SandboxStatusInstance existing = FindStatus(target, statusId);
            if (existing != null)
            {
                bool refresh = profile != null && profile.RefreshSameSource && existing.SourceUnitId == source?.Id && existing.ProfileId == profile.ProfileId;
                int max = existing.ProfileMaxCharges > 0 ? existing.ProfileMaxCharges : def.MaxCharges;
                if (!refresh && (def.Stacking != SandboxStatusStacking.Charges || existing.charges.Count >= max))
                    return "уже действует — не продлевается";
            }
            return null;
        }

        private string StatusPreviewNote(string statusId, SandboxUnitState target, SandboxUnitState source, SandboxStatusProfile profile, string reason)
        {
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(statusId);
            string rejection = PreviewStatusRejection(statusId, target, source, profile);
            string numbers = string.Empty;
            if (def != null && def.Has(SandboxStatusOpKind.TickDamage))
            {
                int tick = profile != null && profile.Tick > 0 ? profile.Tick : def.Ops.First(op => op.Kind == SandboxStatusOpKind.TickDamage).Value;
                int turns = profile != null && profile.Duration > 0 ? profile.Duration : def.Duration;
                numbers = " " + tick + "×" + turns;
            }
            return "«" + (def?.Name ?? statusId) + "»" + numbers + " (" + reason + ")" + (rejection != null ? " — не пройдёт: " + rejection : string.Empty);
        }

        // Новые обычные тики, которые цель получит в начале своего хода (предел 3).
        public int UpcomingTickDamage(SandboxUnitState unit)
        {
            if (unit == null || unit.IsDefeated)
                return 0;
            int total = 0;
            foreach (SandboxStatusInstance instance in unit.statuses)
            {
                foreach (SandboxStatusOp op in instance.Definition.Ops)
                {
                    if (op.Kind == SandboxStatusOpKind.TickDamage)
                        total += (instance.ProfileTick > 0 ? instance.ProfileTick : op.Value) * instance.charges.Count + instance.FirstTickBonus;
                    else if (op.Kind == SandboxStatusOpKind.Special && op.Executor == "PainEcho")
                        total += instance.StoredValue;
                }
            }
            return Math.Min(total, SandboxStatusRules.MaxTickDamagePerActivationStart);
        }

        // Ценность очищения для ИИ: сильный контроль полезного союзника,
        // отложенный взрыв/Приговор, оставшийся урон тиков, ослабления.
        public double CleanseValue(SandboxContextAction action)
        {
            if (action == null || action.Kind != SandboxContextActionKind.CleanseStatus)
                return -1;
            SandboxUnitState target = GetUnit(action.TargetUnitId);
            SandboxStatusInstance instance = FindStatus(target, action.StatusId);
            if (instance == null)
                return -1;
            SandboxStatusDefinition def = instance.Definition;
            if (def.Clock == SandboxStatusClock.RoundTrigger)
                return 3.5 + def.Ops.Where(op => op.Kind == SandboxStatusOpKind.Special).Sum(op => op.Value) * 0.5;
            if (def.Has(SandboxStatusOpKind.SkipActivation))
                return 4.0 + target.Damage * 0.5;
            if (def.StrongControl)
                return 3.0 + target.Damage * 0.3;
            double value = 0.8;
            foreach (SandboxStatusOp op in def.Ops.Where(op => op.Kind == SandboxStatusOpKind.TickDamage))
            {
                int left = def.Clock == SandboxStatusClock.OwnActivations
                    ? instance.charges.Sum(charge => Math.Max(0, instance.EffectiveDuration - charge.Elapsed))
                    : 1;
                value = Math.Max(value, (instance.ProfileTick > 0 ? instance.ProfileTick : op.Value) * left * 0.9);
            }
            if (def.Has(SandboxStatusOpKind.AttackMod) || def.Has(SandboxStatusOpKind.DefenseMod) || def.Has(SandboxStatusOpKind.NoVoluntaryMove))
                value = Math.Max(value, 1.2);
            return value;
        }
    }
}
