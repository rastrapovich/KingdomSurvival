using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public enum SandboxTeam
    {
        Player,
        Enemy
    }

    public enum SandboxUnitRole
    {
        Guard,
        Archer,
        Healer,
        Spearman,
        Scout,
        Militia,
        Beast
    }

    public enum SandboxTerrain
    {
        Normal,
        Difficult,
        Impassable
    }

    public enum SandboxBattlePhase
    {
        Preparing,
        InProgress,
        PlayerVictory,
        EnemyVictory
    }

    public sealed class SandboxUnitDefinition
    {
        public string Id { get; }
        public string RoleLabel { get; }
        public SandboxUnitRole Role { get; }
        public int MaxHitPoints { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int Damage { get; }
        public int Movement { get; }
        public int Initiative { get; }
        public int AttackRange { get; }

        public SandboxUnitDefinition(
            string id,
            string roleLabel,
            SandboxUnitRole role,
            int maxHitPoints,
            int attack,
            int defense,
            int damage,
            int movement,
            int initiative,
            int attackRange)
        {
            Id = id;
            RoleLabel = roleLabel;
            Role = role;
            MaxHitPoints = Math.Max(1, maxHitPoints);
            Attack = Math.Max(0, attack);
            Defense = Math.Max(0, defense);
            Damage = Math.Max(1, damage);
            Movement = Math.Max(1, movement);
            Initiative = Math.Max(0, initiative);
            AttackRange = Math.Max(1, attackRange);
        }
    }

    public sealed class SandboxUnitState
    {
        public const int ActionsPerActivation = 1;

        public SandboxUnitDefinition Definition { get; }
        public SandboxTeam Team { get; }
        public HexCoord Position { get; internal set; }
        public int HitPoints { get; internal set; }
        public int ActionPoints { get; internal set; }
        public int RemainingMovement { get; internal set; }
        public bool HasAttacked { get; internal set; }
        public bool IsGuarding { get; internal set; }

        public string Id => Definition.Id;
        public string DisplayLabel => Definition.RoleLabel;
        public SandboxUnitRole Role => Definition.Role;
        public int MaxHitPoints => Definition.MaxHitPoints;
        public int Attack => Definition.Attack;
        public int Defense => Definition.Defense;
        public int Damage => Definition.Damage;
        public int Movement => Definition.Movement;
        public int Initiative => Definition.Initiative;
        public int AttackRange => Definition.AttackRange;
        public bool IsDefeated => HitPoints <= 0;
        public int DamageTaken => Math.Max(0, MaxHitPoints - HitPoints);
        public bool IsDamaged => DamageTaken > 0;

        public SandboxUnitState(
            SandboxUnitDefinition definition,
            SandboxTeam team,
            HexCoord position)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Team = team;
            Position = position;
            HitPoints = definition.MaxHitPoints;
        }

        internal void BeginActivation()
        {
            ActionPoints = ActionsPerActivation;
            RemainingMovement = Movement;
            HasAttacked = false;
            IsGuarding = false;
        }

        internal void ReceiveDamage(int damage)
        {
            HitPoints = Math.Max(0, HitPoints - Math.Max(0, damage));
            if (IsDefeated)
            {
                ActionPoints = 0;
                RemainingMovement = 0;
                IsGuarding = false;
            }
        }
    }

    public sealed class SandboxAttackPreview
    {
        public static SandboxAttackPreview Invalid(string reason)
        {
            return new SandboxAttackPreview(false, reason, 0, 0);
        }

        public bool IsValid { get; }
        public string Reason { get; }
        public int Damage { get; }
        public int TargetHitPointsAfter { get; }

        public SandboxAttackPreview(
            bool isValid,
            string reason,
            int damage,
            int targetHitPointsAfter)
        {
            IsValid = isValid;
            Reason = reason;
            Damage = damage;
            TargetHitPointsAfter = targetHitPointsAfter;
        }
    }

    public sealed class SandboxBattle
    {
        private readonly List<SandboxUnitState> units;
        private readonly Dictionary<HexCoord, SandboxTerrain> terrain;
        private readonly List<string> turnOrderIds = new List<string>();
        private int currentTurnIndex = -1;

        public int Width { get; }
        public int Height { get; }
        public int Round { get; private set; }
        public SandboxBattlePhase Phase { get; private set; } = SandboxBattlePhase.Preparing;
        public IReadOnlyList<SandboxUnitState> Units => units;
        public IReadOnlyList<string> TurnOrderIds => turnOrderIds;
        public int CurrentTurnIndex => currentTurnIndex;

        public SandboxUnitState CurrentUnit
        {
            get
            {
                if (Phase != SandboxBattlePhase.InProgress ||
                    currentTurnIndex < 0 || currentTurnIndex >= turnOrderIds.Count)
                {
                    return null;
                }

                return GetUnit(turnOrderIds[currentTurnIndex]);
            }
        }

        public SandboxBattle(
            int width,
            int height,
            IEnumerable<SandboxUnitState> units,
            IDictionary<HexCoord, SandboxTerrain> terrain = null)
        {
            if (width < 2 || height < 2)
                throw new ArgumentOutOfRangeException(nameof(width));

            Width = width;
            Height = height;
            this.units = units != null
                ? new List<SandboxUnitState>(units)
                : throw new ArgumentNullException(nameof(units));
            this.terrain = terrain != null
                ? new Dictionary<HexCoord, SandboxTerrain>(terrain)
                : new Dictionary<HexCoord, SandboxTerrain>();

            ValidateInitialState();
        }

        public void Start()
        {
            if (Phase != SandboxBattlePhase.Preparing)
                return;

            Phase = SandboxBattlePhase.InProgress;
            Round = 1;
            BuildRoundOrder();
            currentTurnIndex = 0;
            BeginCurrentActivationOrAdvance();
        }

        public SandboxUnitState GetUnit(string unitId)
        {
            return units.FirstOrDefault(unit => unit.Id == unitId);
        }

        public SandboxUnitState GetUnitAt(HexCoord position)
        {
            return units.FirstOrDefault(unit => !unit.IsDefeated && unit.Position == position);
        }

        public SandboxTerrain GetTerrain(HexCoord position)
        {
            SandboxTerrain value;
            return terrain.TryGetValue(position, out value) ? value : SandboxTerrain.Normal;
        }

        public bool IsInside(HexCoord position)
        {
            return position.Q >= 0 && position.Q < Width &&
                   position.R >= 0 && position.R < Height;
        }

        public IReadOnlyDictionary<HexCoord, int> GetReachable(string unitId)
        {
            SandboxUnitState unit = GetUnit(unitId);
            Dictionary<HexCoord, int> result = new Dictionary<HexCoord, int>();
            if (unit == null || unit.IsDefeated ||
                unit.ActionPoints <= 0 || unit.RemainingMovement <= 0)
                return result;

            Dictionary<HexCoord, int> costs = new Dictionary<HexCoord, int>
            {
                { unit.Position, 0 }
            };
            List<HexCoord> frontier = new List<HexCoord> { unit.Position };

            while (frontier.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < frontier.Count; i++)
                {
                    if (costs[frontier[i]] < costs[frontier[bestIndex]])
                        bestIndex = i;
                }

                HexCoord current = frontier[bestIndex];
                frontier.RemoveAt(bestIndex);

                foreach (HexCoord next in current.Neighbors())
                {
                    if (!IsInside(next) || GetTerrain(next) == SandboxTerrain.Impassable)
                        continue;
                    if (GetUnitAt(next) != null && next != unit.Position)
                        continue;

                    int stepCost = GetTerrain(next) == SandboxTerrain.Difficult ? 2 : 1;
                    int newCost = costs[current] + stepCost;
                    if (newCost > unit.RemainingMovement)
                        continue;

                    int oldCost;
                    if (costs.TryGetValue(next, out oldCost) && oldCost <= newCost)
                        continue;

                    costs[next] = newCost;
                    frontier.Add(next);
                }
            }

            foreach (KeyValuePair<HexCoord, int> pair in costs)
            {
                if (pair.Key != unit.Position)
                    result[pair.Key] = pair.Value;
            }

            return result;
        }

        public bool TryMove(string unitId, HexCoord destination, out string message)
        {
            message = "Перемещение недоступно.";
            SandboxUnitState unit = CurrentUnit;
            if (Phase != SandboxBattlePhase.InProgress || unit == null || unit.Id != unitId)
                return false;
            if (unit.ActionPoints <= 0)
            {
                message = "У бойца не осталось действий.";
                return false;
            }
            if (unit.RemainingMovement <= 0)
            {
                message = "У бойца не осталось очков движения.";
                return false;
            }
            if (GetUnitAt(destination) != null)
            {
                message = "Гекс уже занят.";
                return false;
            }

            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unitId);
            if (!reachable.ContainsKey(destination))
            {
                message = "Гекс находится вне доступного маршрута.";
                return false;
            }

            HexCoord origin = unit.Position;
            int movementCost = reachable[destination];
            unit.Position = destination;
            unit.RemainingMovement = Math.Max(0, unit.RemainingMovement - movementCost);
            message = unit.DisplayLabel + " перемещается " + origin + " → " + destination +
                      ". Осталось движения: " + unit.RemainingMovement + ".";
            if (unit.RemainingMovement == 0)
            {
                message += " Движение исчерпано — активация завершена.";
                EndActivation();
            }
            return true;
        }

        public bool TryGetMeleeAttackPosition(
            string attackerId,
            string targetId,
            HexCoord requestedPosition,
            out int movementCost)
        {
            movementCost = 0;
            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (!CanPrepareAttack(attacker, target) || attacker.AttackRange != 1 ||
                requestedPosition.DistanceTo(target.Position) != 1 ||
                !IsInside(requestedPosition) ||
                GetTerrain(requestedPosition) == SandboxTerrain.Impassable)
            {
                return false;
            }

            SandboxUnitState occupant = GetUnitAt(requestedPosition);
            if (occupant != null && occupant.Id != attacker.Id)
                return false;

            int routeCost = 0;
            if (requestedPosition != attacker.Position)
            {
                IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(attackerId);
                if (!reachable.TryGetValue(requestedPosition, out routeCost))
                    return false;
            }

            int strikeStepCost = GetMovementCost(target.Position);
            if (strikeStepCost == int.MaxValue ||
                routeCost + strikeStepCost > attacker.RemainingMovement)
            {
                return false;
            }

            movementCost = routeCost;
            return true;
        }

        public bool TryFindAttackPosition(
            string attackerId,
            string targetId,
            out HexCoord position,
            out int movementCost)
        {
            position = default;
            movementCost = 0;

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (!CanPrepareAttack(attacker, target))
                return false;

            position = attacker.Position;
            if (attacker.AttackRange == 1)
            {
                bool meleeFound = false;
                int bestMeleeCost = int.MaxValue;
                HexCoord bestMeleePosition = attacker.Position;
                foreach (HexCoord neighbor in target.Position.Neighbors())
                {
                    int candidateCost;
                    if (!TryGetMeleeAttackPosition(
                            attackerId,
                            targetId,
                            neighbor,
                            out candidateCost))
                    {
                        continue;
                    }

                    if (!meleeFound || candidateCost < bestMeleeCost ||
                        (candidateCost == bestMeleeCost && neighbor.CompareTo(bestMeleePosition) < 0))
                    {
                        meleeFound = true;
                        bestMeleeCost = candidateCost;
                        bestMeleePosition = neighbor;
                    }
                }

                if (!meleeFound)
                    return false;

                position = bestMeleePosition;
                movementCost = bestMeleeCost;
                return true;
            }

            if (attacker.Position.DistanceTo(target.Position) <= attacker.AttackRange)
                return true;

            bool found = false;
            int bestCost = int.MaxValue;
            HexCoord best = attacker.Position;
            foreach (KeyValuePair<HexCoord, int> pair in GetReachable(attackerId))
            {
                if (pair.Value >= attacker.RemainingMovement ||
                    pair.Key.DistanceTo(target.Position) > attacker.AttackRange)
                    continue;

                if (!found || pair.Value < bestCost ||
                    (pair.Value == bestCost && pair.Key.CompareTo(best) < 0))
                {
                    found = true;
                    best = pair.Key;
                    bestCost = pair.Value;
                }
            }

            if (!found)
                return false;

            position = best;
            movementCost = bestCost;
            return true;
        }

        public SandboxAttackPreview PreviewReachableAttack(string attackerId, string targetId)
        {
            HexCoord attackPosition;
            int movementCost;
            if (!TryFindAttackPosition(
                    attackerId,
                    targetId,
                    out attackPosition,
                    out movementCost))
            {
                return SandboxAttackPreview.Invalid("Цель находится вне доступной зоны атаки.");
            }

            return BuildAttackPreview(GetUnit(attackerId), GetUnit(targetId));
        }

        public SandboxAttackPreview PreviewAttack(string attackerId, string targetId)
        {
            if (Phase != SandboxBattlePhase.InProgress)
                return SandboxAttackPreview.Invalid("Бой уже завершён.");

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (attacker == null || target == null || attacker.IsDefeated || target.IsDefeated)
                return SandboxAttackPreview.Invalid("Цель недоступна.");
            if (CurrentUnit == null || CurrentUnit.Id != attackerId)
                return SandboxAttackPreview.Invalid("Сейчас ход другого участника.");
            if (attacker.Team == target.Team)
                return SandboxAttackPreview.Invalid("Нельзя атаковать союзника.");
            if (attacker.ActionPoints <= 0)
                return SandboxAttackPreview.Invalid("Не осталось действий.");
            if (attacker.RemainingMovement <= 0)
                return SandboxAttackPreview.Invalid("Движение исчерпано, активация завершена.");
            if (attacker.HasAttacked)
                return SandboxAttackPreview.Invalid("Обычная атака уже использована.");
            if (attacker.Position.DistanceTo(target.Position) > attacker.AttackRange)
                return SandboxAttackPreview.Invalid("Цель вне дальности.");

            return BuildAttackPreview(attacker, target);
        }

        public bool TryAttack(string attackerId, string targetId, out string message)
        {
            SandboxAttackPreview preview = PreviewAttack(attackerId, targetId);
            if (!preview.IsValid)
            {
                message = preview.Reason;
                return false;
            }

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            target.ReceiveDamage(preview.Damage);
            attacker.ActionPoints--;
            attacker.RemainingMovement = 0;
            attacker.HasAttacked = true;

            message = attacker.DisplayLabel + " наносит " + target.DisplayLabel + " " +
                      preview.Damage + " урона" +
                      (target.IsDefeated ? " и выводит цель из строя." : ".");
            EvaluateBattleOutcome();
            return true;
        }

        public bool TryGuard(string unitId, out string message)
        {
            message = "Защитная стойка недоступна.";
            SandboxUnitState unit = CurrentUnit;
            if (Phase != SandboxBattlePhase.InProgress || unit == null || unit.Id != unitId)
                return false;
            if (unit.ActionPoints <= 0 || unit.IsGuarding)
                return false;

            unit.ActionPoints--;
            unit.RemainingMovement = 0;
            unit.IsGuarding = true;
            message = unit.DisplayLabel + " занимает защитную стойку: защита +2 до следующей активации.";
            return true;
        }

        public void EndActivation()
        {
            if (Phase != SandboxBattlePhase.InProgress || CurrentUnit == null)
                return;

            CurrentUnit.ActionPoints = 0;
            CurrentUnit.RemainingMovement = 0;
            AdvanceTurnIndex();
        }

        public SandboxUnitState FindClosestOpponent(SandboxUnitState unit)
        {
            if (unit == null)
                return null;

            return units
                .Where(candidate => !candidate.IsDefeated && candidate.Team != unit.Team)
                .OrderBy(candidate => unit.Position.DistanceTo(candidate.Position))
                .ThenBy(candidate => candidate.HitPoints)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public HexCoord FindBestMoveToward(SandboxUnitState unit, HexCoord target)
        {
            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unit.Id);
            HexCoord best = unit.Position;
            int bestDistance = best.DistanceTo(target);
            int bestCost = 0;

            foreach (KeyValuePair<HexCoord, int> pair in reachable)
            {
                int distance = pair.Key.DistanceTo(target);
                if (distance < bestDistance ||
                    (distance == bestDistance && pair.Value < bestCost) ||
                    (distance == bestDistance && pair.Value == bestCost && pair.Key.CompareTo(best) < 0))
                {
                    best = pair.Key;
                    bestDistance = distance;
                    bestCost = pair.Value;
                }
            }

            return best;
        }

        private void ValidateInitialState()
        {
            HashSet<string> ids = new HashSet<string>();
            HashSet<HexCoord> occupied = new HashSet<HexCoord>();
            foreach (SandboxUnitState unit in units)
            {
                if (!ids.Add(unit.Id))
                    throw new ArgumentException("ID участников боя должны быть уникальными.");
                if (!IsInside(unit.Position))
                    throw new ArgumentException("Участник размещён вне поля: " + unit.Id);
                if (GetTerrain(unit.Position) == SandboxTerrain.Impassable)
                    throw new ArgumentException("Участник размещён на непроходимом гексе: " + unit.Id);
                if (!occupied.Add(unit.Position))
                    throw new ArgumentException("Два участника занимают один стартовый гекс.");
            }

            if (!units.Any(unit => unit.Team == SandboxTeam.Player) ||
                !units.Any(unit => unit.Team == SandboxTeam.Enemy))
            {
                throw new ArgumentException("Для боя нужны обе стороны.");
            }
        }

        private void BuildRoundOrder()
        {
            turnOrderIds.Clear();
            turnOrderIds.AddRange(
                units
                    .Where(unit => !unit.IsDefeated)
                    .OrderByDescending(unit => unit.Initiative)
                    .ThenBy(unit => unit.Team)
                    .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                    .Select(unit => unit.Id));
        }

        private void AdvanceTurnIndex()
        {
            EvaluateBattleOutcome();
            if (Phase != SandboxBattlePhase.InProgress)
                return;

            currentTurnIndex++;
            BeginCurrentActivationOrAdvance();
        }

        private void BeginCurrentActivationOrAdvance()
        {
            while (Phase == SandboxBattlePhase.InProgress)
            {
                if (currentTurnIndex >= turnOrderIds.Count)
                {
                    Round++;
                    BuildRoundOrder();
                    currentTurnIndex = 0;
                }

                if (turnOrderIds.Count == 0)
                {
                    EvaluateBattleOutcome();
                    return;
                }

                SandboxUnitState candidate = GetUnit(turnOrderIds[currentTurnIndex]);
                if (candidate != null && !candidate.IsDefeated)
                {
                    candidate.BeginActivation();
                    return;
                }

                currentTurnIndex++;
            }
        }

        private void EvaluateBattleOutcome()
        {
            bool playersAlive = units.Any(unit => unit.Team == SandboxTeam.Player && !unit.IsDefeated);
            bool enemiesAlive = units.Any(unit => unit.Team == SandboxTeam.Enemy && !unit.IsDefeated);

            if (!enemiesAlive)
                Phase = SandboxBattlePhase.PlayerVictory;
            else if (!playersAlive)
                Phase = SandboxBattlePhase.EnemyVictory;
        }

        private static SandboxAttackPreview BuildAttackPreview(
            SandboxUnitState attacker,
            SandboxUnitState target)
        {
            int effectiveDefense = target.Defense + (target.IsGuarding ? 2 : 0);
            int statDifference = attacker.Attack - effectiveDefense;
            decimal damageMultiplier;

            if (statDifference > 0)
            {
                damageMultiplier = Math.Min(
                    5m,
                    1m + statDifference * 0.25m);
            }
            else if (statDifference < 0)
            {
                damageMultiplier = Math.Max(
                    0.3m,
                    1m - (-statDifference * 0.125m));
            }
            else
            {
                damageMultiplier = 1m;
            }

            int damage = Math.Max(
                1,
                (int)Math.Floor(attacker.Damage * damageMultiplier));
            if (attacker.Role == SandboxUnitRole.Spearman && target.Role == SandboxUnitRole.Beast)
                damage += 5;

            return new SandboxAttackPreview(
                true,
                string.Empty,
                damage,
                Math.Max(0, target.HitPoints - damage));
        }

        private bool CanPrepareAttack(
            SandboxUnitState attacker,
            SandboxUnitState target)
        {
            return Phase == SandboxBattlePhase.InProgress &&
                   attacker != null && target != null &&
                   !attacker.IsDefeated && !target.IsDefeated &&
                   CurrentUnit != null && CurrentUnit.Id == attacker.Id &&
                   attacker.Team != target.Team &&
                   attacker.ActionPoints > 0 && attacker.RemainingMovement > 0 &&
                   !attacker.HasAttacked;
        }

        private int GetMovementCost(HexCoord position)
        {
            SandboxTerrain value = GetTerrain(position);
            if (value == SandboxTerrain.Impassable)
                return int.MaxValue;
            return value == SandboxTerrain.Difficult ? 2 : 1;
        }
    }

    public static class SandboxEnemyPlanner
    {
        public static IReadOnlyList<string> TakeCurrentTurn(SandboxBattle battle)
        {
            List<string> events = new List<string>();
            SandboxUnitState enemy = battle != null ? battle.CurrentUnit : null;
            if (enemy == null || enemy.Team != SandboxTeam.Enemy)
                return events;

            SandboxUnitState target = SelectAttackTarget(battle, enemy);
            if (target == null)
            {
                SandboxUnitState closest = battle.FindClosestOpponent(enemy);
                if (closest != null)
                {
                    HexCoord destination = battle.FindBestMoveToward(enemy, closest.Position);
                    if (destination != enemy.Position)
                    {
                        string moveMessage;
                        if (battle.TryMove(enemy.Id, destination, out moveMessage))
                            events.Add(moveMessage);
                    }
                }
            }

            if (battle.Phase == SandboxBattlePhase.InProgress)
            {
                target = SelectAttackTarget(battle, enemy);
                if (target != null)
                {
                    string attackMessage;
                    if (battle.TryAttack(enemy.Id, target.Id, out attackMessage))
                        events.Add(attackMessage);
                }
            }

            if (battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.Id == enemy.Id &&
                enemy.ActionPoints > 0)
            {
                string guardMessage;
                if (battle.TryGuard(enemy.Id, out guardMessage))
                    events.Add(guardMessage);
            }

            if (battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.Id == enemy.Id)
            {
                battle.EndActivation();
            }

            return events;
        }

        private static SandboxUnitState SelectAttackTarget(
            SandboxBattle battle,
            SandboxUnitState attacker)
        {
            return battle.Units
                .Where(unit => !unit.IsDefeated && unit.Team != attacker.Team)
                .Select(unit => new
                {
                    Unit = unit,
                    Preview = battle.PreviewAttack(attacker.Id, unit.Id)
                })
                .Where(candidate => candidate.Preview.IsValid)
                .OrderBy(candidate => candidate.Unit.HitPoints)
                .ThenByDescending(candidate => candidate.Preview.Damage)
                .ThenBy(candidate => candidate.Unit.Id, StringComparer.Ordinal)
                .Select(candidate => candidate.Unit)
                .FirstOrDefault();
        }
    }
}
