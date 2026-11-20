using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Пакет «Пассивные теги»: исполнение правил SandboxPassiveCatalog поверх
    // общего потока боя. Постоянные бонусы считаются отдельно от предела
    // временных состояний (±2), как броня и холмы. Эффекты тегов —
    // фиксированный урон/лечение/состояния: они не запускают on-hit,
    // ответы и вампиризм; цепочка смертей ограничена SandboxStatusRules.MaxEventChain.
    public sealed partial class SandboxBattle
    {
        private int passiveDepth;
        private readonly Dictionary<string, string> lastDirectStriker = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---------- Лимиты ----------

        private bool LimitReady(SandboxUnitState bearer, SandboxPassiveRule rule, string targetId)
        {
            SandboxPassiveRuntime rt = bearer.passive;
            switch (rule.Limit)
            {
                case SandboxPassiveLimit.PerOwnTurn: return !rt.LastTurn.TryGetValue(rule.Key, out int turn) || turn != bearer.ActivationCount;
                case SandboxPassiveLimit.PerRound: return !rt.LastRound.TryGetValue(rule.Key, out int round) || round != Round;
                case SandboxPassiveLimit.PerBattle: return !rt.UsedInBattle.Contains(rule.Key);
                case SandboxPassiveLimit.PerTargetPerBattle:
                    return targetId == null || !rt.UsedOnTargets.TryGetValue(rule.Key, out HashSet<string> set) || !set.Contains(targetId);
                default: return true;
            }
        }

        private void MarkUsed(SandboxUnitState bearer, SandboxPassiveRule rule, string targetId)
        {
            SandboxPassiveRuntime rt = bearer.passive;
            switch (rule.Limit)
            {
                case SandboxPassiveLimit.PerOwnTurn: rt.LastTurn[rule.Key] = bearer.ActivationCount; break;
                case SandboxPassiveLimit.PerRound: rt.LastRound[rule.Key] = Round; break;
                case SandboxPassiveLimit.PerBattle: rt.UsedInBattle.Add(rule.Key); break;
                case SandboxPassiveLimit.PerTargetPerBattle:
                    if (targetId == null) break;
                    if (!rt.UsedOnTargets.TryGetValue(rule.Key, out HashSet<string> set))
                        rt.UsedOnTargets[rule.Key] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(targetId);
                    break;
            }
        }

        // Каждое N-е срабатывание: счётчик виден в карточке и сбрасывается.
        private bool EveryNthFires(SandboxUnitState bearer, SandboxPassiveRule rule)
        {
            int count = bearer.passive.Counter(rule.Key) + 1;
            if (count >= Math.Max(1, rule.LimitN))
            {
                bearer.passive.Counters[rule.Key] = 0;
                return true;
            }
            bearer.passive.Counters[rule.Key] = count;
            return false;
        }

        private static IEnumerable<(SandboxPassiveDefinition Def, SandboxPassiveRule Rule)> Rules(SandboxUnitState unit, SandboxPassiveKind kind)
        {
            foreach (SandboxPassiveDefinition def in SandboxPassiveCatalog.Of(unit))
            {
                foreach (SandboxPassiveRule rule in def.Rules)
                {
                    if (rule.Kind == kind)
                        yield return (def, rule);
                }
            }
        }

        internal static bool HasPassive(SandboxUnitState unit, SandboxPassiveKind kind) => Rules(unit, kind).Any();

        // ---------- Условия ----------

        private bool PassiveCondition(SandboxPassiveCondition condition, SandboxPassiveRule rule, SandboxUnitState self, SandboxUnitState other,
            bool ranged, int damage = 0)
        {
            int baseMax = self.Definition.MaxHitPoints;
            switch (condition)
            {
                case SandboxPassiveCondition.None: return true;
                case SandboxPassiveCondition.Melee: return !ranged;
                case SandboxPassiveCondition.Ranged: return ranged;
                case SandboxPassiveCondition.TargetIsolated:
                    return other != null && !units.Any(u => u != other && !u.IsDefeated && !u.IsOffBoard && u.Team == other.Team && u.Position.DistanceTo(other.Position) == 1);
                case SandboxPassiveCondition.TargetHpHalf: return other != null && other.HitPoints * 2 <= other.MaxHitPoints;
                case SandboxPassiveCondition.TargetHp30: return other != null && other.HitPoints * 10 <= other.MaxHitPoints * 3;
                case SandboxPassiveCondition.NoLivingAllies: return !units.Any(u => u != self && !u.IsDefeated && u.Team == self.Team);
                case SandboxPassiveCondition.AlliesWithin2AtLeast3:
                    return units.Count(u => u != self && !u.IsDefeated && !u.IsOffBoard && u.Team == self.Team && u.Position.DistanceTo(self.Position) <= 2) >= 3;
                case SandboxPassiveCondition.NoAlliesWithin2:
                    return !units.Any(u => u != self && !u.IsDefeated && !u.IsOffBoard && u.Team == self.Team && u.Position.DistanceTo(self.Position) <= 2);
                case SandboxPassiveCondition.TargetMaxHpHalfOfMine: return other != null && other.MaxHitPoints * 2 <= self.MaxHitPoints;
                case SandboxPassiveCondition.TargetTag: return other != null && other.HasTag(rule.ConditionParam);
                case SandboxPassiveCondition.AttackerTag: return other != null && other.HasTag(rule.ConditionParam);
                case SandboxPassiveCondition.SelfHpHalf: return self.HitPoints * 2 <= baseMax;
                case SandboxPassiveCondition.SelfHpQuarter: return self.HitPoints * 4 < baseMax;
                case SandboxPassiveCondition.SelfHp20: return self.HitPoints * 5 <= baseMax;
                case SandboxPassiveCondition.SelfFullHp: return self.HitPoints >= self.MaxHitPoints;
                case SandboxPassiveCondition.FirstRound: return Round <= 1;
                case SandboxPassiveCondition.TwoEnemiesAdjacentToSelf:
                    return units.Count(u => !u.IsDefeated && !u.IsOffBoard && u.Team != self.Team && u.Position.DistanceTo(self.Position) == 1) >= 2;
                case SandboxPassiveCondition.TwoOtherAlliesNearTarget:
                    return other != null && units.Count(u => u != self && !u.IsDefeated && !u.IsOffBoard && u.Team == self.Team && u.Position.DistanceTo(other.Position) == 1) >= 2;
                case SandboxPassiveCondition.AllyOppositeTarget:
                    if (other == null) return false;
                    int dir = other.Position.GetNeighborIndex(self.Position);
                    if (dir < 0) return false;
                    HexCoord opposite = SandboxHexLine.Step(other.Position, (dir + 3) % 6);
                    SandboxUnitState ally = GetUnitAt(opposite);
                    return ally != null && ally != self && ally.Team == self.Team;
                case SandboxPassiveCondition.TargetHasStatus: return other != null && rule.StatusIds.Skip(rule.Kind == SandboxPassiveKind.OnHitApplyStatus ? 1 : 0).Any(other.HasStatus);
                case SandboxPassiveCondition.TargetArmored: return other != null && other.HasTag(SandboxCombatTagRules.Armored);
                case SandboxPassiveCondition.NotArmoredSelf: return !self.HasTag(SandboxCombatTagRules.Armored);
                case SandboxPassiveCondition.SelfOnHill: return SandboxTerrainRules.IsOnHill(self);
                case SandboxPassiveCondition.SelfOnShallowWater: return !self.IsOffBoard && HasShallowWater(self.Position);
                case SandboxPassiveCondition.TargetNotActedThisRound:
                    return other != null && turnOrderIds.IndexOf(other.Id) > currentTurnIndex;
                case SandboxPassiveCondition.TargetLiving: return other != null && !other.HasTag("nature.returned");
                case SandboxPassiveCondition.NotAttackedSinceMove: return !self.passive.AttackedSinceMove;
                case SandboxPassiveCondition.TargetHeldBySelfVine:
                    return other != null && links.Any(link => link.Kind == SandboxLinkKind.Vine && link.SourceId == self.Id && link.TargetId == other.Id);
                case SandboxPassiveCondition.DamageAtLeastFifthOfTargetMax:
                    return other != null && damage * 5 >= other.Definition.MaxHitPoints;
                case SandboxPassiveCondition.RememberedAttacker: return other != null && self.passive.RememberedAttackerId == other.Id;
                case SandboxPassiveCondition.AvengedKiller: return other != null && self.passive.KillersOfAllies.Contains(other.Id);
                case SandboxPassiveCondition.FirstAttackOnTarget: return other != null && !self.passive.AttackedTargets.Contains(other.Id);
                default: return false;
            }
        }

        private bool Both(SandboxPassiveRule rule, SandboxUnitState self, SandboxUnitState other, bool ranged, int damage = 0)
        {
            return PassiveCondition(rule.Condition, rule, self, other, ranged, damage) &&
                   PassiveCondition(rule.Condition2, rule, self, other, ranged, damage);
        }

        private static bool StrongControlled(SandboxUnitState unit) => unit.statuses.Any(item => item.Definition.StrongControl);

        // ---------- Расчёт атаки ----------

        internal int PassiveAttackMod(SandboxUnitState attacker, SandboxUnitState target, bool ranged, HexCoord attackPosition, bool consume = false)
        {
            if (attacker == null)
                return 0;
            int sum = 0;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.AttackMod))
            {
                if (!LimitReady(attacker, rule, target?.Id) || !Both(rule, attacker, target, ranged))
                    continue;
                int value = rule.Value;
                // «Работает в окружении» со «Стаей» — берётся больший бонус.
                if (def.TagId == "passive.20261119.077" && HasPackBonus(attacker, target, attackPosition))
                    value -= SandboxCombatTagRules.PackAttackBonus;
                sum += value;
                if (consume)
                    MarkUsed(attacker, rule, target?.Id);
            }
            SandboxPassiveRuntime rt = attacker.passive;
            sum += rt.GrantedAttack + rt.Stack("attack.permanent") + rt.Stack("attack.miss");
            if (rt.GrantedAttackUntilActivation >= attacker.ActivationCount)
                sum += rt.Stack("attack.untilTurn");
            if (rt.ChargedAttack > 0 && rt.ChargedAttackExpires >= attacker.ActivationCount)
            {
                sum += rt.ChargedAttack;
                if (consume)
                    rt.ChargedAttack = 0;
            }
            // Ауры: семья не складывается сама с собой.
            if (AuraFrom(attacker, SandboxPassiveKind.AuraAllyAttack, allies: true, out int allyAura))
                sum += allyAura;
            if (AuraFrom(attacker, SandboxPassiveKind.AuraEnemyAttack, allies: false, out int enemyAura))
                sum += enemyAura;
            if (target != null)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.IncomingAttackMod))
                {
                    if (!LimitReady(target, rule, attacker.Id) || !Both(rule, target, attacker, ranged))
                        continue;
                    sum += rule.Value;
                    if (consume)
                        MarkUsed(target, rule, attacker.Id);
                }
            }
            return sum;
        }

        // Соседний носитель ауры (для врагов — соседний враг); сильнейшее значение одной семьи.
        private bool AuraFrom(SandboxUnitState unit, SandboxPassiveKind kind, bool allies, out int value)
        {
            value = 0;
            bool found = false;
            foreach (SandboxUnitState bearer in units)
            {
                if (bearer == unit || bearer.IsDefeated || bearer.IsOffBoard || (bearer.Team == unit.Team) != allies ||
                    bearer.Position.DistanceTo(unit.Position) != 1 || StrongControlled(bearer))
                    continue;
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(bearer, kind))
                {
                    if (kind == SandboxPassiveKind.AuraEnemyDefense && rule.StatusIds.Any(id => IsPassiveImmune(unit, id)))
                        continue;
                    if (!found || Math.Abs(rule.Value) > Math.Abs(value))
                        value = rule.Value;
                    found = true;
                }
            }
            return found;
        }

        internal int PassiveDefenseMod(SandboxUnitState target, SandboxUnitState attacker, bool ranged, bool consume = false)
        {
            if (target == null)
                return 0;
            int sum = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.DefenseMod))
            {
                if (Both(rule, target, attacker, ranged))
                    sum += rule.Value;
            }
            SandboxPassiveRuntime rt = target.passive;
            sum += rt.GrantedDefense + rt.Stack("defense.hits");
            if (!ranged)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.FirstMeleeHitPerRoundDefense))
                {
                    if (rt.LastRound.TryGetValue(rule.Key, out int round) && round == Round)
                        continue;
                    sum += rule.Value;
                    if (consume)
                        rt.LastRound[rule.Key] = Round;
                }
            }
            if (AuraFrom(target, SandboxPassiveKind.AuraEnemyDefense, allies: false, out int dread))
                sum += dread;
            return sum;
        }

        internal int PassiveIgnoreDefense(SandboxUnitState attacker, SandboxUnitState target, bool ranged)
        {
            int ignore = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.IgnoreDefense))
            {
                if (Both(rule, attacker, target, ranged))
                    ignore = Math.Max(ignore, rule.Value);
            }
            return ignore;
        }

        // Итоговый урон после состояний: доплаты, сокращения и проценты тегов, минимум 1.
        internal int PassiveDamage(SandboxUnitState attacker, SandboxUnitState target, bool ranged, int damage, bool consume)
        {
            int flat = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.OutgoingFlat))
            {
                if (!LimitReady(attacker, rule, target?.Id) || !Both(rule, attacker, target, ranged))
                    continue;
                flat += rule.Value;
                if (consume)
                    MarkUsed(attacker, rule, target?.Id);
            }
            SandboxPassiveRuntime art = attacker.passive;
            flat += art.Stack("outgoing.attacks");
            if (art.OutgoingNextTurnActivation == attacker.ActivationCount)
                flat += art.OutgoingNextTurnValue;
            if (target != null)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.IncomingFlat))
                {
                    if (Both(rule, target, attacker, ranged))
                        flat += rule.Value;
                }
                flat -= target.passive.Stack("reduction.hits");
            }
            int result = damage + flat;
            if (target != null)
            {
                int percent = 0;
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.IncomingPercent))
                {
                    if (Both(rule, target, attacker, ranged))
                        percent = Math.Max(percent, rule.Value);
                }
                if (percent > 0)
                    result = result * (100 - percent) / 100;
            }
            return Math.Max(1, result);
        }

        // Сокращение от «Не подпускает палача»: одно на раунд, общее с прочими плоскими сокращениями.
        internal bool GuardAuraReduction(SandboxUnitState attacker, SandboxUnitState target, bool consume)
        {
            if (attacker == null || target == null || attacker.Team == target.Team || !StrongControlled(target))
                return false;
            foreach (SandboxUnitState bearer in units)
            {
                if (bearer == target || bearer.IsDefeated || bearer.IsOffBoard || bearer.Team != target.Team ||
                    bearer.Position.DistanceTo(target.Position) != 1 || StrongControlled(bearer))
                    continue;
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(bearer, SandboxPassiveKind.AuraAllyGuardStrongControlled))
                {
                    if (!rule.StatusIds.Any(target.HasStatus) || !LimitReady(bearer, rule, null))
                        continue;
                    if (consume)
                        MarkUsed(bearer, rule, null);
                    return true;
                }
            }
            return false;
        }

        internal int PassiveInitiativeMod(SandboxUnitState unit)
        {
            int sum = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.InitiativeMod))
            {
                if (Both(rule, unit, null, false))
                    sum += rule.Value;
            }
            // «Гнетущее присутствие»: враг в радиусе 2 с бо́льшим MaxHP, одна семья.
            foreach (SandboxUnitState bearer in units)
            {
                if (bearer.IsDefeated || bearer.Team == unit.Team || bearer.Position.DistanceTo(unit.Position) > 2 || bearer.MaxHitPoints <= unit.MaxHitPoints)
                    continue;
                if (Rules(bearer, SandboxPassiveKind.AuraEnemyInitiative).Any())
                {
                    sum += Rules(bearer, SandboxPassiveKind.AuraEnemyInitiative).First().Rule.Value;
                    break;
                }
            }
            return sum;
        }

        internal static int PassiveRetaliations(SandboxUnitState unit)
        {
            int best = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.Retaliations))
                best = Math.Max(best, rule.Value);
            return best;
        }

        internal static bool PassiveBlocksRetaliation(SandboxUnitState unit)
        {
            return (unit.MovedThisActivation && HasPassive(unit, SandboxPassiveKind.NoMeleeRetaliationAfterMove)) ||
                   (unit.passive.ShotThisTurn && HasPassive(unit, SandboxPassiveKind.NoMeleeRetaliationAfterShot));
        }

        // ---------- Состояния ----------

        private static bool IsPassiveImmune(SandboxUnitState unit, string statusId)
        {
            return Rules(unit, SandboxPassiveKind.StatusImmunity).Any(pair => pair.Rule.StatusIds.Contains(statusId));
        }

        // До наложения: невосприимчивость, разовые отмены, сроки профиля.
        private string PassiveStatusGate(string statusId, SandboxUnitState target, ref SandboxStatusProfile profile)
        {
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusImmunity))
            {
                if (rule.StatusIds.Contains(statusId))
                    return target.DisplayLabel + " невосприимчив: «" + def.Name + "»";
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusCancelOnce))
            {
                if (rule.StatusIds.Contains(statusId) && LimitReady(target, rule, null))
                {
                    MarkUsed(target, rule, null);
                    return "«" + def.Name + "» отменяет наложение";
                }
            }
            SandboxStatusDefinition status = SandboxStatusCatalog.Get(statusId);
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusMaxDuration))
            {
                if (!rule.StatusIds.Contains(statusId))
                    continue;
                profile = Clone(profile);
                int current = profile.Duration > 0 ? profile.Duration : status.Duration;
                profile.Duration = Math.Min(current, rule.Value);
            }
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StatusDurationDelta))
            {
                if (!rule.StatusIds.Contains(statusId) || !LimitReady(target, rule, null))
                    continue;
                profile = Clone(profile);
                int current = profile.Duration > 0 ? profile.Duration : status.Duration;
                int changed = current + rule.Value;
                profile.Duration = rule.Value2 > 0 ? Math.Min(rule.Value2, changed) : Math.Max(1, changed);
                if (rule.Value > 0)
                    profile.MaxCharges = 1;
                MarkUsed(target, rule, null);
            }
            return null;
        }

        private static SandboxStatusProfile Clone(SandboxStatusProfile profile)
        {
            return profile == null
                ? new SandboxStatusProfile()
                : new SandboxStatusProfile
                {
                    ProfileId = profile.ProfileId, Duration = profile.Duration, Tick = profile.Tick, Supernatural = profile.Supernatural,
                    Value = profile.Value, RefreshSameSource = profile.RefreshSameSource, UntilBattleEnd = profile.UntilBattleEnd,
                    MaxCharges = profile.MaxCharges, Mode = profile.Mode
                };
        }

        // «Старящий удар»: −10% базового MaxHP (вниз, минимум 1) до конца боя.
        private static SandboxStatusProfile ResolveProfile(SandboxStatusProfile profile, SandboxUnitState target)
        {
            if (profile == null || profile.Mode != "aging")
                return profile;
            SandboxStatusProfile copy = Clone(profile);
            copy.Value = -Math.Max(1, target.Definition.MaxHitPoints / 10);
            return copy;
        }

        // ---------- События удара ----------

        private bool IsOwnAttack(SandboxHitKind kind) => kind == SandboxHitKind.Attack || kind == SandboxHitKind.Ability;

        // Смертельный удар: «Живучий» / «Девять жизней» оставляют 1 HP один раз за бой.
        private int PassiveCheatDeath(SandboxHitKind kind, SandboxUnitState target, int toHp)
        {
            if (!IsDirectHit(kind) || toHp < target.HitPoints || target.HitPoints <= 0)
                return toHp;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.CheatDeathOnce))
            {
                if (target.passive.UsedInBattle.Contains(rule.Key))
                    continue;
                target.passive.UsedInBattle.Add(rule.Key);
                foreach (string statusId in rule.StatusIds)
                    RemoveStatus(target, statusId, "«" + def.Name + "»");
                Log(target.DisplayLabel + " выдерживает смертельный удар («" + def.Name + "») — остаётся 1 HP.");
                return target.HitPoints - 1;
            }
            return toHp;
        }

        private void OnPassiveDirectHit(SandboxHitKind kind, SandboxUnitState striker, SandboxUnitState target, int hpBefore, int hpTaken)
        {
            if (striker == null || target == null || passiveDepth > SandboxStatusRules.MaxEventChain)
                return;
            passiveDepth++;
            try
            {
                bool ranged = striker.Position.DistanceTo(target.Position) > 1;
                bool enemies = striker.Team != target.Team;
                if (enemies && IsDirectHit(kind))
                    lastDirectStriker[target.Id] = striker.Id;

                // --- атакующий ---
                if (IsOwnAttack(kind) && enemies)
                {
                    SandboxPassiveRuntime rt = striker.passive;
                    if (CurrentUnit == striker)
                        rt.AttackedThisTurn = true;
                    rt.AttackedSinceMove = true;
                    rt.AttackedTargets.Add(target.Id);
                    if (ranged)
                        rt.ShotThisTurn = true;
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.StackOutgoingPerAttack))
                        rt.Stacks["outgoing.attacks"] = Math.Min(rule.Value2, rt.Stack("outgoing.attacks") + rule.Value);
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.StackAttackAfterMiss))
                        rt.Stacks["attack.miss"] = hpTaken > 0 ? 0 : Math.Min(rule.Value2, rt.Stack("attack.miss") + rule.Value);

                    if (hpTaken > 0 && !target.IsDefeated)
                    {
                        foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.OnHitApplyStatus).ToList())
                        {
                            if (!Both(rule, striker, target, ranged, hpTaken) || !LimitReady(striker, rule, target.Id))
                                continue;
                            if (rule.Limit == SandboxPassiveLimit.EveryNth && !EveryNthFires(striker, rule))
                                continue;
                            MarkUsed(striker, rule, target.Id);
                            SandboxStatusApplyResult applied = ApplyStatus(rule.StatusIds[0], target, striker, ResolveProfile(rule.Profile, target));
                            if (applied.Applied)
                                Log(striker.DisplayLabel + ": «" + def.Name + "».");
                        }
                    }
                    if (hpTaken > 0)
                        PassiveSplash(striker, target, ranged, hpBefore - target.HitPoints >= 0 ? hpTaken : 0);
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.OnHitHealSelf))
                    {
                        if (hpTaken <= 0 || striker.IsDefeated || !Both(rule, striker, target, ranged) || !LimitReady(striker, rule, target.Id))
                            continue;
                        MarkUsed(striker, rule, target.Id);
                        int before = striker.HitPoints;
                        striker.Heal(Math.Min(rule.Value, hpTaken / 2));
                        if (striker.HitPoints > before)
                            Log(striker.DisplayLabel + ": «" + def.Name + "» +" + (striker.HitPoints - before) + " HP.");
                    }
                    if (target.IsDefeated)
                        OnPassiveKill(striker, target, ranged);
                }

                // --- цель ---
                if (enemies && IsDirectHit(kind))
                {
                    SandboxPassiveRuntime trt = target.passive;
                    if (string.IsNullOrEmpty(trt.RememberedAttackerId))
                        trt.RememberedAttackerId = striker.Id;
                    if (hpTaken > 0)
                    {
                        trt.TookDamageThisTurn = true;
                        foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StackDefensePerHitTaken))
                            trt.Stacks["defense.hits"] = Math.Min(rule.Value2, trt.Stack("defense.hits") + rule.Value);
                        foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.StackReductionPerDamageTaken))
                            trt.Stacks["reduction.hits"] = Math.Min(rule.Value2, trt.Stack("reduction.hits") + rule.Value);
                        foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.ChargeAttackAfterDamageTaken))
                        {
                            trt.ChargedAttack = rule.Value;
                            trt.ChargedAttackExpires = target.ActivationCount + rule.Value2;
                        }
                    }
                    if (!target.IsDefeated && !ranged)
                    {
                        foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.OnHitTakenApplyStatusToAttacker).ToList())
                        {
                            if (striker.IsDefeated || hpTaken <= 0 || !LimitReady(target, rule, striker.Id))
                                continue;
                            MarkUsed(target, rule, striker.Id);
                            SandboxStatusApplyResult applied = ApplyStatus(rule.StatusIds[0], striker, target, rule.Profile);
                            if (applied.Applied)
                                Log(target.DisplayLabel + ": «" + def.Name + "».");
                        }
                    }
                    if (!target.IsDefeated)
                        PassiveBelowHalf(target, hpBefore);
                }
            }
            finally
            {
                passiveDepth--;
            }
        }

        private void PassiveSplash(SandboxUnitState striker, SandboxUnitState target, bool ranged, int hpTaken)
        {
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.OnHitSplashAdjacentToTarget))
            {
                if (!Both(rule, striker, target, ranged))
                    continue;
                foreach (SandboxUnitState near in units.Where(u => u != target && !u.IsDefeated && !u.IsOffBoard && u.Team != striker.Team &&
                                                                   u.Position.DistanceTo(target.Position) == 1).OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
                    DealStatusDamage(near, rule.Value, striker, "«" + def.Name + "»", SandboxDamageType.Physical);
            }
            int half = Math.Max(1, hpTaken / 2);
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.OnHitSplashFlank))
            {
                if (!Both(rule, striker, target, ranged))
                    continue;
                foreach (SandboxUnitState near in units.Where(u => u != target && !u.IsDefeated && !u.IsOffBoard && u.Team != striker.Team &&
                                                                   u.Position.DistanceTo(target.Position) == 1 && u.Position.DistanceTo(striker.Position) == 1)
                             .OrderBy(u => u.Id, StringComparer.Ordinal).Take(rule.Value).ToList())
                    DealStatusDamage(near, half, striker, "«" + def.Name + "»", SandboxDamageType.Physical);
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.OnHitSplashBehindTarget))
            {
                int dir = striker.Position.DistanceTo(target.Position) == 1 ? striker.Position.GetNeighborIndex(target.Position) : -1;
                if (dir < 0)
                    continue;
                SandboxUnitState behind = GetUnitAt(SandboxHexLine.Step(target.Position, dir));
                if (behind != null && behind != striker)
                    DealStatusDamage(behind, half, striker, "«" + def.Name + "»", SandboxDamageType.Physical);
            }
        }

        private void PassiveBelowHalf(SandboxUnitState unit, int hpBefore)
        {
            int baseMax = unit.Definition.MaxHitPoints;
            if (unit.passive.BelowHalfDone || hpBefore * 2 <= baseMax || unit.HitPoints * 2 > baseMax)
                return;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.BelowHalfOnce))
            {
                unit.passive.BelowHalfDone = true;
                int before = unit.HitPoints;
                unit.Heal(rule.Value);
                unit.passive.Stacks["attack.permanent"] = unit.passive.Stack("attack.permanent") + rule.Value2;
                Log(unit.DisplayLabel + ": «" + def.Name + "» +" + (unit.HitPoints - before) + " HP, +" + rule.Value2 + " Атака до конца боя.");
            }
        }

        private void OnPassiveKill(SandboxUnitState killer, SandboxUnitState victim, bool ranged)
        {
            SandboxPassiveRuntime rt = killer.passive;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(killer, SandboxPassiveKind.OnKillOutgoingNextTurn))
            {
                rt.OutgoingNextTurnActivation = killer.ActivationCount + 1;
                rt.OutgoingNextTurnValue = rule.Value;
                Log(killer.DisplayLabel + ": «" + def.Name + "» — +" + rule.Value + " урона в следующий ход.");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(killer, SandboxPassiveKind.OnKillMaxHitPoints))
            {
                if (!Both(rule, killer, victim, ranged) || rt.BonusMaxHitPoints >= rule.Value2)
                    continue;
                int add = Math.Min(rule.Value, rule.Value2 - rt.BonusMaxHitPoints);
                rt.BonusMaxHitPoints += add;
                killer.Heal(add);
                Log(killer.DisplayLabel + ": «" + def.Name + "» — MaxHP и HP +" + add + ".");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(killer, SandboxPassiveKind.OnKillApplyStatusNearestEnemy))
            {
                if (!LimitReady(killer, rule, null))
                    continue;
                SandboxUnitState nearest = units.Where(u => !u.IsDefeated && !u.IsOffBoard && u.Team != killer.Team && u.Position.DistanceTo(killer.Position) <= rule.Value)
                    .OrderBy(u => u.Position.DistanceTo(killer.Position)).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                if (nearest == null)
                    continue;
                MarkUsed(killer, rule, null);
                ApplyStatus(rule.StatusIds[0], nearest, killer);
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(killer, SandboxPassiveKind.OnKillStepIntoCell))
            {
                if (ranged || CurrentUnit != killer || killer.IsDefeated)
                    continue;
                HexCoord cell = victim.Position;
                if (IsFreeCell(cell))
                {
                    SetUnitPosition(killer, cell);
                    Log(killer.DisplayLabel + ": «" + def.Name + "» — занимает " + cell + ".");
                }
            }
        }

        // ---------- Смерть носителя и союзников ----------

        private void OnPassiveUnitDefeated(SandboxUnitState dead)
        {
            if (passiveDepth > SandboxStatusRules.MaxEventChain)
            {
                Log("Ошибка: превышена длина цепочки событий при гибели " + dead.DisplayLabel + ".");
                return;
            }
            passiveDepth++;
            try
            {
                // Мститель и счёт павших у союзников.
                lastDirectStriker.TryGetValue(dead.Id, out string killerId);
                foreach (SandboxUnitState ally in units.Where(u => u != dead && !u.IsDefeated && u.Team == dead.Team))
                {
                    if (!string.IsNullOrEmpty(killerId))
                        ally.passive.KillersOfAllies.Add(killerId);
                    if (string.IsNullOrEmpty(dead.SpawnedBySourceId))
                    {
                        foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(ally, SandboxPassiveKind.StackAttackAfterAlliesFell))
                        {
                            int fell = ally.passive.Counter(rule.Key) + 1;
                            ally.passive.Counters[rule.Key] = fell;
                            if (fell == rule.Value2)
                            {
                                ally.passive.Stacks["attack.permanent"] = ally.passive.Stack("attack.permanent") + rule.Value;
                                Log(ally.DisplayLabel + ": «" + def.Name + "» — +" + rule.Value + " Атака до конца боя.");
                            }
                        }
                    }
                }

                List<SandboxUnitState> adjacent = units.Where(u => u != dead && !u.IsDefeated && !u.IsOffBoard && u.Position.DistanceTo(dead.Position) == 1)
                    .OrderBy(u => u.Id, StringComparer.Ordinal).ToList();
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.OnDeathApplyStatus))
                {
                    foreach (SandboxUnitState near in adjacent)
                        ApplyStatus(rule.StatusIds[0], near, null, rule.Profile);
                    Log(dead.DisplayLabel + ": «" + def.Name + "».");
                }
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.OnDeathDamage))
                {
                    foreach (SandboxUnitState near in adjacent)
                        DealStatusDamage(near, rule.Value, null, "«" + def.Name + "» (" + dead.DisplayLabel + ")", SandboxDamageType.Physical);
                }
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.OnDeathHealAllies))
                {
                    foreach (SandboxUnitState near in adjacent.Where(u => u.Team == dead.Team && !u.IsDefeated))
                    {
                        int before = near.HitPoints;
                        near.Heal(rule.Value);
                        if (near.HitPoints > before)
                            Log(near.DisplayLabel + ": «" + def.Name + "» +" + (near.HitPoints - before) + " HP.");
                    }
                }
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.OnDeathDefenseToAlly))
                {
                    SandboxUnitState nearest = units.Where(u => u != dead && !u.IsDefeated && u.Team == dead.Team && u.Position.DistanceTo(dead.Position) <= rule.Value2)
                        .OrderBy(u => u.Position.DistanceTo(dead.Position)).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (nearest == null)
                        continue;
                    nearest.passive.GrantedDefense += rule.Value;
                    Log(nearest.DisplayLabel + ": «" + def.Name + "» — +" + rule.Value + " Защита до конца боя.");
                }
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.OnDeathAttackToAlly))
                {
                    SandboxUnitState weakest = adjacent.Where(u => u.Team == dead.Team && !u.IsDefeated)
                        .OrderBy(u => u.HitPoints).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (weakest == null)
                        continue;
                    weakest.passive.Stacks["attack.untilTurn"] = rule.Value;
                    weakest.passive.GrantedAttackUntilActivation = weakest.ActivationCount + 1;
                    Log(weakest.DisplayLabel + ": «" + def.Name + "» — +" + rule.Value + " Атака на следующий ход.");
                }
            }
            finally
            {
                passiveDepth--;
            }
        }

        // ---------- Ход ----------

        private void ProcessPassiveActivationStart(SandboxUnitState unit)
        {
            SandboxPassiveRuntime rt = unit.passive;
            bool lastTurnNoAttack = rt.Counter("turn.noattack") > 0;
            rt.TookDamageThisTurn = false;
            rt.AttackedThisTurn = false;
            rt.ShotThisTurn = false;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationStartHeal))
            {
                if (!Both(rule, unit, null, false) || unit.HitPoints >= unit.MaxHitPoints)
                    continue;
                int before = unit.HitPoints;
                unit.Heal(rule.Value);
                Log(unit.DisplayLabel + ": «" + def.Name + "» +" + (unit.HitPoints - before) + " HP.");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationStartHealAlly))
            {
                SandboxUnitState wounded = units.Where(u => u != unit && !u.IsDefeated && !u.IsOffBoard && u.Team == unit.Team && u.HitPoints < u.MaxHitPoints &&
                                                           u.Position.DistanceTo(unit.Position) == 1)
                    .OrderBy(u => u.HitPoints * 1000 / Math.Max(1, u.MaxHitPoints)).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                if (wounded == null)
                    continue;
                int before = wounded.HitPoints;
                wounded.Heal(rule.Value);
                Log(unit.DisplayLabel + ": «" + def.Name + "» — " + wounded.DisplayLabel + " +" + (wounded.HitPoints - before) + " HP.");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationStartStatusDecay))
            {
                SandboxStatusInstance frost = FindStatus(unit, rule.StatusIds[0]);
                if (frost != null && frost.charges.Count > 0)
                {
                    frost.charges.RemoveAt(0);
                    StatusRevision++;
                    if (frost.charges.Count == 0)
                        RemoveStatusInstance(unit, frost, "«" + def.Name + "»");
                    else
                        Log(unit.DisplayLabel + ": «" + def.Name + "» снимает заряд инея.");
                }
                else
                {
                    RemoveStatus(unit, rule.StatusIds[1], "«" + def.Name + "»");
                }
            }
            if (rt.GrantedAttackUntilActivation >= 0 && rt.GrantedAttackUntilActivation < unit.ActivationCount)
                rt.Stacks["attack.untilTurn"] = 0;
            rt.Counters["turn.noattack.prev"] = lastTurnNoAttack ? 1 : 0;
        }

        // Добавка к движению в начале хода (до удержаний и NoVoluntaryMove).
        private int PassiveMovementStart(SandboxUnitState unit)
        {
            int bonus = 0;
            if (unit.passive.Counter("turn.noattack.prev") > 0)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.MovementBonusAfterNoAttack))
                    bonus = Math.Max(bonus, rule.Value);
            }
            // «Вожак ловчих»: союзник со «Стаей» в радиусе 2 от вожака.
            if (unit.HasTag(SandboxCombatTagRules.Pack))
            {
                foreach (SandboxUnitState bearer in units)
                {
                    if (bearer == unit || bearer.IsDefeated || bearer.Team != unit.Team || bearer.Position.DistanceTo(unit.Position) > 2)
                        continue;
                    if (Rules(bearer, SandboxPassiveKind.AuraAllyMovementStart).Any())
                    {
                        bonus += 1;
                        break;
                    }
                }
            }
            return bonus;
        }

        private void ProcessPassiveActivationEnd(SandboxUnitState unit)
        {
            if (unit.IsDefeated)
                return;
            SandboxPassiveRuntime rt = unit.passive;
            bool rested = !rt.AttackedThisTurn && !rt.TookDamageThisTurn;
            bool still = !rt.AttackedThisTurn && !unit.MovedThisActivation;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationEndHealPercent))
                HealLog(unit, Math.Max(1, unit.MaxHitPoints * rule.Value / 100), def.Name);
            if (rested)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationEndRestHeal))
                    HealLog(unit, rule.Value, def.Name);
            }
            if (still)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationEndRestSleep))
                {
                    HealLog(unit, rule.Value, def.Name);
                    ApplyStatus("status.030", unit, unit);
                }
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ActivationEndRestCleanse))
                {
                    if (!LimitReady(unit, rule, null))
                        continue;
                    string removed = rule.StatusIds.FirstOrDefault(unit.HasStatus);
                    if (removed == null)
                        continue;
                    MarkUsed(unit, rule, null);
                    RemoveStatus(unit, removed, "«" + def.Name + "»");
                }
            }
            rt.Counters["turn.noattack"] = rt.AttackedThisTurn ? 0 : 1;
        }

        private void HealLog(SandboxUnitState unit, int amount, string name)
        {
            if (unit.HitPoints >= unit.MaxHitPoints)
                return;
            int before = unit.HitPoints;
            unit.Heal(amount);
            Log(unit.DisplayLabel + ": «" + name + "» +" + (unit.HitPoints - before) + " HP.");
        }

        // ---------- Движение ----------

        internal int GetExtraStepCost(SandboxUnitState unit, HexCoord from, HexCoord to) => PassiveExtraStepCost(unit, from, to);

        // Добавочная цена шага: «Мешает пройти» и «липкий отход» (status.047 sticky_exit).
        private int PassiveExtraStepCost(SandboxUnitState unit, HexCoord from, HexCoord to)
        {
            if (unit == null)
                return 0;
            int extra = 0;
            foreach (SandboxUnitState holder in units)
            {
                if (holder.IsDefeated || holder.IsOffBoard || holder.Team == unit.Team || holder.Position.DistanceTo(from) != 1)
                    continue;
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(holder, SandboxPassiveKind.LeaveAdjacentCost))
                    extra = Math.Max(extra, rule.Value);
            }
            if (from == unit.Position)
            {
                foreach (SandboxStatusInstance sticky in unit.statuses.Where(item => item.Mode == "sticky_exit"))
                {
                    SandboxUnitState source = GetUnit(sticky.SourceUnitId);
                    if (source != null && !source.IsDefeated && source.Position.DistanceTo(from) == 1 && source.Position.DistanceTo(to) > 1)
                        extra = Math.Max(extra, 1);
                }
            }
            return extra;
        }

        private void AfterPassiveStep(SandboxUnitState unit, HexCoord previous, HexCoord cell)
        {
            foreach (SandboxUnitState spiny in units.Where(u => !u.IsDefeated && !u.IsOffBoard && u.Team != unit.Team &&
                                                                u.Position.DistanceTo(cell) == 1 && u.Position.DistanceTo(previous) != 1).ToList())
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(spiny, SandboxPassiveKind.EnterAdjacentDamage))
                {
                    if (!unit.IsDefeated)
                        DealStatusDamage(unit, rule.Value, spiny, "«" + def.Name + "» (" + spiny.DisplayLabel + ")", SandboxDamageType.Physical);
                }
            }
        }

        private void AfterPassiveVoluntaryMove(SandboxUnitState unit, HexCoord origin)
        {
            unit.passive.AttackedSinceMove = false;
            foreach (SandboxStatusInstance sticky in unit.statuses.Where(item => item.Mode == "sticky_exit").ToList())
            {
                SandboxUnitState source = GetUnit(sticky.SourceUnitId);
                if (source == null || (source.Position.DistanceTo(origin) == 1 && source.Position.DistanceTo(unit.Position) > 1))
                    RemoveStatusInstance(unit, sticky, "липкий отход оплачен");
            }
        }

        internal static bool PassiveResistsPush(SandboxUnitState unit) => HasPassive(unit, SandboxPassiveKind.PushReduction);

        // Предел движения «вдвое» (status.041 half_speed): не выше половины базового, вверх.
        private static void ApplyHalfSpeed(SandboxUnitState unit)
        {
            if (unit.statuses.Any(item => item.Mode == "half_speed"))
                unit.RemainingMovement = Math.Min(unit.RemainingMovement, (unit.Movement + 1) / 2);
        }
    }
}
