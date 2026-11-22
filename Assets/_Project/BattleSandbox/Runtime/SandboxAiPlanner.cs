using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public enum SandboxAiActionKind
    {
        None,
        Ability,
        Attack,
        ShootAndRetreat,
        Context,
        Wait
    }

    // Решение ИИ на одну активацию: сначала (необязательно) движение, затем
    // одно действие. Исполняет его тот же код, что и команды игрока.
    public sealed class SandboxAiDecision
    {
        public HexCoord? MoveTo { get; set; }
        public SandboxAiActionKind Action { get; set; }
        public SandboxAbilityCommand Command { get; set; }
        public string TargetId { get; set; }
        public HexCoord? RetreatTo { get; set; }
        public SandboxContextAction Context { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    // ПР-16: планировщик ИИ существ. Сначала отсекает недопустимое, затем
    // сравнивает небольшое число реальных кандидатов. Бойцы без способностей
    // и профилей выбирают цель прежним правилом (регрессия поведения).
    public static class SandboxAiPlanner
    {
        public static SandboxAiDecision Plan(SandboxBattle battle, SandboxUnitState unit, bool allowMove)
        {
            SandboxAiDecision none = new SandboxAiDecision { Action = SandboxAiActionKind.None, Reason = "Нет допустимого действия." };
            if (battle == null || unit == null || battle.CurrentUnit != unit || battle.Phase != SandboxBattlePhase.InProgress ||
                unit.ActionPoints <= 0 || unit.HasAttacked)
                return none;

            // Скрытый ждёт в засаде, пока цель не окажется в досягаемости.
            if (unit.IsHidden)
            {
                SandboxUnitState prey = BestAttackTarget(battle, unit, allowMove, out HexCoord? approach, out _, true);
                if (prey == null)
                    return new SandboxAiDecision { Action = SandboxAiActionKind.Wait, Reason = "Ждёт в засаде." };
                return new SandboxAiDecision { Action = SandboxAiActionKind.Attack, TargetId = prey.Id, MoveTo = approach };
            }

            double bestScore = double.MinValue;
            SandboxAiDecision best = null;

            // Способности.
            foreach (SandboxAbilityState state in battle.GetAbilityStates(unit.Id).Where(item => item.Available))
            {
                foreach (SandboxBattle.AbilityCandidate candidate in battle.GetAbilityCandidates(unit.Id, state.Ability.Id))
                {
                    if (!allowMove && candidate.Command.ApproachCell.HasValue && candidate.Command.ApproachCell.Value != unit.Position)
                        continue;
                    double score = ScoreAbility(battle, unit, state.Ability, candidate);
                    if (score > bestScore || (Math.Abs(score - bestScore) < 1e-9 && best != null && best.Action != SandboxAiActionKind.Ability))
                    {
                        bestScore = score;
                        best = new SandboxAiDecision { Action = SandboxAiActionKind.Ability, Command = candidate.Command, Reason = state.Ability.Title };
                    }
                }
            }

            // Освобождение от захвата/паразита; очищение себя или соседа
            // (пакет состояний): сильный контроль, взрыв, оставшиеся тики.
            foreach (SandboxContextAction context in battle.GetContextActions(unit.Id))
            {
                double score = context.Kind == SandboxContextActionKind.BreakFree || context.Kind == SandboxContextActionKind.RemoveParasite
                    ? 3.5
                    : context.Kind == SandboxContextActionKind.PullFree ? 3.0
                    : context.Kind == SandboxContextActionKind.CleanseStatus ? battle.CleanseValue(context)
                    // Этап 16: спасти лежащего «При смерти» союзника, разбить кокон врага.
                    : context.Kind == SandboxContextActionKind.Rescue ? 7.0
                    : context.Kind == SandboxContextActionKind.AttackObject && battle.IsEnemyCocoon(unit, context.ObjectId) ? 4.0 : -1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new SandboxAiDecision { Action = SandboxAiActionKind.Context, Context = context, Reason = context.Label };
                }
            }

            // Обычная атака.
            SandboxUnitState target = BestAttackTarget(battle, unit, allowMove, out HexCoord? attackFrom, out double attackScore);
            if (target != null && attackScore > bestScore)
            {
                bestScore = attackScore;
                // Бриф «Поверхности арены»: стоит в огне/яде — ударить ту же
                // цель из безопасной клетки, если она есть.
                if (!attackFrom.HasValue && allowMove && battle.HazardAt(unit, unit.Position) > 0)
                {
                    HexCoord? safer = unit.AttackRange > 1 ? battle.BestShotCell(unit, target) : battle.SaferMeleeCell(unit, target);
                    if (safer.HasValue && safer.Value != unit.Position && battle.HazardAt(unit, safer.Value) == 0)
                        attackFrom = safer;
                }
                best = new SandboxAiDecision { Action = SandboxAiActionKind.Attack, TargetId = target.Id, MoveTo = attackFrom };
                SandboxAbilityDefinition kite = unit.Definition.FindAbility(SandboxAbilityKind.KiteAfterShot);
                if (kite != null && unit.AttackRange > 1)
                {
                    best.Action = SandboxAiActionKind.ShootAndRetreat;
                    best.RetreatTo = null; // ищется после подхода, из реального остатка движения
                }
            }

            if (best != null && bestScore > 0)
                return best;

            if (!allowMove || unit.RemainingMovement <= 0)
                return none;

            // Сближение (с учётом профиля «держит дистанцию»).
            HexCoord? destination = MoveDestination(battle, unit);
            if (destination.HasValue && destination.Value != unit.Position)
                return new SandboxAiDecision { Action = SandboxAiActionKind.None, MoveTo = destination, Reason = "Сближение." };
            return none;
        }

        // ---------- Обычная атака ----------

        private static SandboxUnitState BestAttackTarget(SandboxBattle battle, SandboxUnitState unit, bool allowMove,
            out HexCoord? moveTo, out double score, bool searchReachable = false)
        {
            moveTo = null;
            score = double.MinValue;
            bool plain = !searchReachable && !unit.HasTag(SandboxCombatTagRules.Pack) &&
                         !unit.Definition.HasAbility(SandboxAbilityKind.FeedOnNumbed) &&
                         !unit.Definition.HasAbility(SandboxAbilityKind.KeepDistance) &&
                         !unit.Definition.HasAbility(SandboxAbilityKind.KiteAfterShot);

            // Сначала — цели с текущей позиции (прежнее правило).
            List<SandboxUnitState> inPlace = battle.Units
                .Where(other => (!other.IsDefeated || other.IsDying) && other.Team != unit.Team && battle.PreviewAttack(unit.Id, other.Id).IsValid)
                .ToList();
            if (plain && inPlace.Count > 0)
            {
                SandboxUnitState chosen = inPlace
                    .OrderBy(other => other.HitPoints)
                    .ThenByDescending(other => battle.PreviewAttack(unit.Id, other.Id).HpDamage)
                    .ThenBy(other => other.Id, StringComparer.Ordinal)
                    .First();
                score = AttackValue(battle, unit, chosen, unit.Position, battle.PreviewAttack(unit.Id, chosen.Id).HpDamage);
                return chosen;
            }
            // Прежнее правило: без цели на месте — сближение, затем удар.
            if (plain)
                return null;

            SandboxUnitState best = null;
            foreach (SandboxUnitState other in battle.Units.Where(item => (!item.IsDefeated || item.IsDying) && item.Team != unit.Team &&
                                                                          !item.IsUnderground && !item.AwaitingPlacement)
                         .OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                HexCoord from;
                int cost;
                if (inPlace.Contains(other))
                {
                    from = unit.Position;
                    cost = 0;
                }
                else if (!allowMove || !battle.TryFindAttackPosition(unit.Id, other.Id, out from, out cost))
                {
                    continue;
                }
                from = PreferredShotPosition(battle, unit, other, from);
                SandboxAttackPreview preview = battle.PreviewReachableAttack(unit.Id, other.Id);
                // Пакет состояний: по фактическому урону HP (щит/неуязвимость не «урон»).
                int damage = preview.IsValid ? preview.HpDamage : 1;
                double value = AttackValue(battle, unit, other, from, damage) - cost * 0.05;
                if (best == null || value > score + 1e-9)
                {
                    best = other;
                    score = value;
                    moveTo = from != unit.Position ? from : (HexCoord?)null;
                }
            }
            return best;
        }

        private static double AttackValue(SandboxBattle battle, SandboxUnitState unit, SandboxUnitState target, HexCoord from, int damage)
        {
            double value = 1.0 + damage + (damage >= target.HitPoints ? 6 : 0) + (target.MaxHitPoints - target.HitPoints) * 0.05;
            // Этап 16: удар по подопечному примет защитник — ценность ниже.
            if (battle.PreviewInterceptor(unit, target) != target)
                value -= 2;
            // Добить лежащего «При смерти» врага, пока его не спасли.
            if (target.IsDying)
                value += 3;
            if (unit.HasTag(SandboxCombatTagRules.Pack))
            {
                if (battle.HasPackBonus(unit, target, from))
                    value += 3;
                value += battle.Units.Count(other => other != unit && other.Team == unit.Team && !other.IsDefeated &&
                                                    other.HasTag(SandboxCombatTagRules.Pack) &&
                                                    other.Position.DistanceTo(target.Position) <= 2) * 0.5;
            }
            if (unit.Definition.HasAbility(SandboxAbilityKind.FeedOnNumbed) && target.IsNumbed)
                value += 5;
            return value;
        }

        // Стрелок с профилем дистанции выбирает позицию дальше от угрозы.
        private static HexCoord PreferredShotPosition(SandboxBattle battle, SandboxUnitState unit, SandboxUnitState target, HexCoord fallback)
        {
            SandboxAbilityDefinition profile = unit.Definition.FindAbility(SandboxAbilityKind.KeepDistance) ??
                                               unit.Definition.FindAbility(SandboxAbilityKind.KiteAfterShot);
            if (profile == null || unit.AttackRange <= 1)
                return fallback;
            int desiredMin = Math.Min(profile.RangeMin, unit.AttackRange);
            int desiredMax = Math.Min(profile.RangeMax, unit.AttackRange);
            IEnumerable<HexCoord> options = battle.GetReachable(unit.Id)
                .Where(pair => pair.Value < unit.RemainingMovement)
                .Select(pair => pair.Key)
                .Concat(new[] { unit.Position })
                .Where(cell => cell.DistanceTo(target.Position) <= unit.AttackRange && battle.CanShootFrom(cell, target.Position));
            HexCoord best = fallback;
            double bestValue = double.MinValue;
            foreach (HexCoord cell in options.OrderBy(cell => cell))
            {
                int distance = cell.DistanceTo(target.Position);
                double value = distance >= desiredMin && distance <= desiredMax ? 2 : 0;
                value += NearestThreat(battle, unit, cell) * 0.5;
                // Бриф «Поверхности арены»: холм, укрытие, без опасности.
                value += battle.ShotCellValue(unit, cell);
                if (cell == unit.Position)
                    value += 0.6; // уже может стрелять — не подходить без нужды
                if (value > bestValue)
                {
                    bestValue = value;
                    best = cell;
                }
            }
            return best;
        }

        private static int NearestThreat(SandboxBattle battle, SandboxUnitState unit, HexCoord cell)
        {
            List<SandboxUnitState> threats = battle.Units.Where(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard).ToList();
            return threats.Count == 0 ? 10 : threats.Min(other => other.Position.DistanceTo(cell));
        }

        // Шиповик: отход на 1–2 гекса после выстрела, в пределах остатка движения.
        public static HexCoord? FindKiteRetreat(SandboxBattle battle, SandboxUnitState unit, int movementBudget)
        {
            SandboxAbilityDefinition kite = unit.Definition.FindAbility(SandboxAbilityKind.KiteAfterShot);
            if (kite == null || movementBudget <= 0)
                return null;
            int here = NearestThreat(battle, unit, unit.Position);
            HexCoord? best = null;
            double bestValue = 0;
            foreach (KeyValuePair<HexCoord, int> pair in battle.GetReachableWithBudget(unit.Id, movementBudget).OrderBy(pair => pair.Key))
            {
                int steps = pair.Key.DistanceTo(unit.Position);
                if (steps < Math.Max(1, kite.PathMin) || steps > Math.Max(1, kite.PathMax))
                    continue;
                if (battle.IsMovementStopCell(unit, pair.Key))
                    continue;
                int threat = NearestThreat(battle, unit, pair.Key);
                if (threat <= here)
                    continue;
                double value = threat + (threat <= unit.AttackRange ? 1 : 0) - pair.Value * 0.1;
                if (value > bestValue)
                {
                    bestValue = value;
                    best = pair.Key;
                }
            }
            return best;
        }

        private static HexCoord? MoveDestination(SandboxBattle battle, SandboxUnitState unit)
        {
            if (unit.IsOffBoard)
                return null;
            SandboxAbilityDefinition keep = unit.Definition.FindAbility(SandboxAbilityKind.KeepDistance);
            if (keep != null && unit.AttackRange > 1 && NearestThreat(battle, unit, unit.Position) < Math.Min(keep.RangeMin, unit.AttackRange))
            {
                // Под угрозой — отойти, оставив движение на выстрел.
                HexCoord? retreat = battle.GetReachable(unit.Id)
                    .Where(pair => pair.Value < unit.RemainingMovement)
                    // Этап 16: маршрут без удара за выход из соседства — раньше.
                    .OrderBy(pair => battle.LeaveReactorsOnPath(unit.Id, pair.Key).Count)
                    .ThenByDescending(pair => NearestThreat(battle, unit, pair.Key))
                    .ThenBy(pair => pair.Value)
                    .ThenBy(pair => pair.Key)
                    .Select(pair => (HexCoord?)pair.Key)
                    .FirstOrDefault();
                if (retreat.HasValue && NearestThreat(battle, unit, retreat.Value) > NearestThreat(battle, unit, unit.Position))
                    return retreat;
            }
            SandboxUnitState closest = battle.FindClosestOpponent(unit);
            if (closest == null)
                return null;
            // Бриф «Поверхности арены»: стрелок подходит на позицию, откуда
            // реально стреляет (линия огня), предпочитая холм и укрытие.
            if (unit.AttackRange > 1)
            {
                HexCoord? shot = battle.Units
                    .Where(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard && !other.IsUnderground)
                    .OrderBy(other => other.Position.DistanceTo(unit.Position)).ThenBy(other => other.Id, StringComparer.Ordinal)
                    .Select(other => battle.BestShotCell(unit, other))
                    .FirstOrDefault(cell => cell.HasValue);
                if (shot.HasValue)
                    return shot;
            }
            return battle.FindBestMoveToward(unit, closest.Position);
        }

        // ---------- Оценка способностей ----------

        private static double ScoreAbility(SandboxBattle battle, SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxBattle.AbilityCandidate candidate)
        {
            SandboxAbilityCommand command = candidate.Command;
            SandboxAbilityPreview preview = candidate.Preview;
            SandboxUnitState target = !string.IsNullOrEmpty(command.TargetUnitId) ? battle.GetUnit(command.TargetUnitId) : null;
            double kill = target != null && !target.IsDefeated && preview.Damage >= target.HitPoints ? 6 : 0;
            double damage = 1.0 + preview.Damage + kill;
            switch (ability.Kind)
            {
                // Бриф завершения, этап 16: новые действия по фактическому результату.
                case SandboxAbilityKind.Heal:
                {
                    if (target == null) return -1;
                    int missing = target.MaxHitPoints - target.HitPoints;
                    int actual = Math.Min(missing, Math.Max(0, ability.Amount));
                    if (actual <= 0) return -1; // не лечить полное HP ради пустого события
                    double urgency = target.HitPoints * 2 <= target.MaxHitPoints ? 2.0 : 0.0;
                    return actual * 1.1 + urgency + battle.UpcomingTickDamage(target) * 0.5;
                }
                case SandboxAbilityKind.Spell:
                {
                    double payload = !string.IsNullOrEmpty(ability.StatusPayloadId) && target != null &&
                                     battle.PreviewStatusRejection(ability.StatusPayloadId, target, unit) == null ? 1.5 : 0;
                    return damage + payload;
                }
                case SandboxAbilityKind.SpellArea:
                {
                    if (!command.TargetCell.HasValue) return -1;
                    double value = 0;
                    foreach (SandboxUnitState hit in battle.Units.Where(u => !u.IsDefeated && !u.IsOffBoard && u != unit &&
                                                                             u.Position.DistanceTo(command.TargetCell.Value) <= Math.Max(1, ability.Push)))
                        value += hit.Team == unit.Team ? -2.5 : 1.0 + Math.Max(1, unit.Damage);
                    return value;
                }
                case SandboxAbilityKind.Dispel:
                    return preview.IsValid ? 2.0 : -1;
                case SandboxAbilityKind.Pounce:
                case SandboxAbilityKind.Charge:
                    return damage + 0.6 + (preview.ForcedCell.HasValue ? 0.5 : 0);
                case SandboxAbilityKind.BreakGuard:
                    return damage + (target != null && target.IsGuarding ? 1.5 : 0.1);
                case SandboxAbilityKind.Shove:
                    return damage + (preview.ForcedCell.HasValue ? 0.6 : 0.05);
                case SandboxAbilityKind.Attach:
                case SandboxAbilityKind.Engulf:
                    return kill > 0 ? damage : damage + 3.5;
                case SandboxAbilityKind.StunSpit:
                    // Пакет состояний: не тратить приём на невосприимчивую или уже ошеломлённую цель.
                    return target != null && (battle.PreviewStatusRejection(SandboxStatusCatalog.Dazed, target, unit) != null || !target.CanRetaliate) ? damage - 0.5 : damage + 2.5;
                case SandboxAbilityKind.NumbBite:
                    return target != null && !target.IsGuarding && target.NumbImmunity == 0 &&
                           battle.PreviewStatusRejection(SandboxStatusCatalog.Numbed, target, unit) == null ? damage + 3 : damage;
                case SandboxAbilityKind.VineGrab:
                case SandboxAbilityKind.TendrilGrab:
                    if (command.TargetObjectId == "pull")
                        return preview.ForcedCell.HasValue ? 2.0 : -1;
                    return damage + 2.5;
                case SandboxAbilityKind.ChimeMark:
                    if (command.TargetObjectId == "toll")
                        return 10;
                    return 2.5 + (string.IsNullOrEmpty(command.SecondTargetUnitId) ? 0 : 2.5);
                case SandboxAbilityKind.Burrow:
                    if (command.TargetObjectId == "strike")
                        return damage + 1;
                    if (battle.Units.Any(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard &&
                                                  battle.PreviewReachableAttack(unit.Id, other.Id).IsValid))
                        return -1; // уже можно бить — не прятаться
                    SandboxUnitState nearest = battle.FindClosestOpponent(unit);
                    if (nearest == null || !command.TargetCell.HasValue)
                        return -1;
                    return 4.0 - command.TargetCell.Value.DistanceTo(nearest.Position);
                case SandboxAbilityKind.Call:
                    if (target == null || target.IsGuarding || !preview.ForcedCell.HasValue)
                        return -1;
                    int allies = battle.Units.Count(other => other.Team == unit.Team && !other.IsDefeated && !other.IsOffBoard &&
                                                             other.Position.DistanceTo(preview.ForcedCell.Value) == 1);
                    return 1.5 + allies * 1.5;
                case SandboxAbilityKind.ConsumeCorpse:
                    return Math.Min(unit.MaxHitPoints - unit.HitPoints, Math.Max(1, unit.MaxHitPoints * ability.Amount / 100)) * 0.6;
                case SandboxAbilityKind.WeaveThread:
                {
                    int value = 0;
                    foreach (HexCoord cell in preview.DangerCells)
                    {
                        foreach (SandboxUnitState foe in battle.Units.Where(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard))
                        {
                            if (cell.DistanceTo(foe.Position) <= 2 && cell.DistanceTo(unit.Position) < foe.Position.DistanceTo(unit.Position))
                                value++;
                        }
                    }
                    return value == 0 ? -1 : 1.0 + Math.Min(4, value) * 0.6;
                }
                case SandboxAbilityKind.PitCycle:
                    if (unit.PitPhase == 1)
                        return 9;
                    // На поверхности: сперва ударить, если есть кого.
                    return battle.Units.Any(other => !other.IsDefeated && other.Team != unit.Team && battle.PreviewAttack(unit.Id, other.Id).IsValid) ? 0.5 : 5;
                case SandboxAbilityKind.LayEgg:
                    return battle.Units.Any(other => !other.IsDefeated && other.Team != unit.Team && battle.PreviewAttack(unit.Id, other.Id).IsValid) ? 2.0 : 4.0;
                default:
                    return 0;
            }
        }
    }
}
