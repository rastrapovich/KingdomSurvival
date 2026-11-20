using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 4: то, что ИИ читает
    // о местности. Те же правила, что у пути и предпросмотра: цена шага
    // конкретного бойца (болотный ходок не платит за топь), опасность
    // клетки, линия огня и укрытие. Ничего не меняет в бою.
    public sealed partial class SandboxBattle
    {
        // Позиции бойца в начале двух последних активаций — против вечного
        // хождения между двумя укрытиями.
        private readonly Dictionary<string, List<HexCoord>> activationCells = new Dictionary<string, List<HexCoord>>();

        // На компактной арене с темой, объектами или закрытым основанием путь
        // считается по цене шага, а не по прямой.
        internal bool UsesTerrainPathing =>
            ArenaReport != null || TerrainObjects.Any() || Units.Any(u => u.HasTag(SandboxTerrainTags.Swimmer)) || HasBlockingGround();

        private bool HasBlockingGround()
        {
            foreach (HexCoord cell in GroundCells.Keys)
            {
                if (IsGroundBlocking(cell))
                    return true;
            }
            return false;
        }

        private void RememberActivationCell(SandboxUnitState unit)
        {
            if (unit == null)
                return;
            if (!activationCells.TryGetValue(unit.Id, out List<HexCoord> list))
                activationCells[unit.Id] = list = new List<HexCoord>();
            list.Add(unit.Position);
            if (list.Count > 3)
                list.RemoveAt(0);
        }

        // Клетка, где боец начинал предыдущую активацию (не текущую).
        public HexCoord? PreviousActivationCell(string unitId) =>
            activationCells.TryGetValue(unitId ?? string.Empty, out List<HexCoord> list) && list.Count >= 2
                ? list[list.Count - 2]
                : (HexCoord?)null;

        // Ожидаемый урон опасностей, если боец окажется в клетке (без учёта
        // уже сработавших видов: оценка для позиции на будущий ход).
        public int HazardAt(SandboxUnitState unit, HexCoord cell)
        {
            int total = 0;
            foreach (IGrouping<SandboxEffectKind, SandboxCellEffect> group in EffectsAt(cell)
                         .Where(e => SandboxHazardRules.IsHazard(e.Kind)).GroupBy(e => e.Kind))
            {
                if (group.Key == SandboxEffectKind.Electric && (HasBridgeAt(cell) || IsFrozen(cell)))
                    continue;
                total += group.Max(e => e.Damage);
            }
            return total;
        }

        // Цена пути от каждой клетки до target для этого бойца (без учёта
        // фигур; сама target — цель, вход в неё не считается).
        public Dictionary<HexCoord, int> TravelCosts(SandboxUnitState unit, HexCoord target)
        {
            Dictionary<HexCoord, int> cost = new Dictionary<HexCoord, int> { { target, 0 } };
            SortedSet<(int, HexCoord)> open = new SortedSet<(int, HexCoord)> { (0, target) };
            int guard = 0;
            while (open.Count > 0 && guard++ < 20000)
            {
                (int value, HexCoord current) = open.Min;
                open.Remove(open.Min);
                if (value > cost[current])
                    continue;
                // Шаг из next в current стоит цену входа в current (кроме цели).
                int enter = current == target ? 1 : Math.Max(1, GetStepCost(unit, current));
                foreach (HexCoord next in current.Neighbors())
                {
                    if (!IsInside(next) || !IsEnterableFor(unit, next))
                        continue;
                    int candidate = value + enter;
                    if (cost.TryGetValue(next, out int known))
                    {
                        if (known <= candidate)
                            continue;
                        open.Remove((known, next));
                    }
                    cost[next] = candidate;
                    open.Add((candidate, next));
                }
            }
            return cost;
        }

        // Лучшая клетка, откуда стрелок может выстрелить по цели после
        // перемещения: холм, укрытие от ближайшей угрозы, без опасности, не
        // назад в клетку прошлой активации. null — такой клетки нет.
        public HexCoord? BestShotCell(SandboxUnitState unit, SandboxUnitState target)
        {
            if (unit == null || target == null || unit.AttackRange <= 1)
                return null;
            HexCoord? previous = PreviousActivationCell(unit.Id);
            HexCoord? best = null;
            double bestValue = double.MinValue;
            IEnumerable<KeyValuePair<HexCoord, int>> options = GetReachable(unit.Id)
                .Where(pair => pair.Value < unit.RemainingMovement)
                .Concat(new[] { new KeyValuePair<HexCoord, int>(unit.Position, 0) });
            foreach (KeyValuePair<HexCoord, int> pair in options.OrderBy(pair => pair.Key))
            {
                HexCoord cell = pair.Key;
                if (cell.DistanceTo(target.Position) > unit.AttackRange || !CanShootFrom(cell, target.Position))
                    continue;
                double value = ShotCellValue(unit, cell) - pair.Value * 0.05;
                if (previous.HasValue && cell == previous.Value && cell != unit.Position)
                    value -= 0.7;
                if (value > bestValue + 1e-9)
                {
                    bestValue = value;
                    best = cell;
                }
            }
            return best;
        }

        // Ценность клетки для стрелка: холм, укрытие от ближайшей угрозы,
        // опасность поверхности.
        public double ShotCellValue(SandboxUnitState unit, HexCoord cell)
        {
            double value = 0;
            if (GetTerrain(cell) == SandboxTerrain.Difficult)
                value += 0.8;
            SandboxUnitState threat = Units
                .Where(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard)
                .OrderBy(other => other.Position.DistanceTo(cell)).ThenBy(other => other.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (threat != null && threat.AttackRange > 1)
                value += CoverAt(unit, cell, threat.Position) * 0.3;
            value -= HazardAt(unit, cell) * 1.5;
            return value;
        }

        // Укрытие клетки от выстрела из from (растительность или низкий
        // объект на линии) — для оценки позиции, без самого бойца в клетке.
        private int CoverAt(SandboxUnitState unit, HexCoord cell, HexCoord from)
        {
            SandboxSurfaceRules rules = SandboxSurfaceRules.Current;
            int cover = 0;
            SandboxVegetation vegetation = GetVegetation(cell);
            if (vegetation == SandboxVegetation.Forest)
                cover = rules.ForestCover;
            else if (vegetation == SandboxVegetation.Brush)
                cover = rules.BrushCover;
            if (from.DistanceTo(cell) > 1 && GetLineOfFire(from, cell).LowCover)
                cover = Math.Max(cover, rules.LowObjectCover);
            return cover;
        }

        // Ближний боец стоит в опасности и может ударить ту же цель из
        // безопасной соседней клетки — туда и встать (null — нет такой).
        public HexCoord? SaferMeleeCell(SandboxUnitState unit, SandboxUnitState target)
        {
            if (unit == null || target == null || HazardAt(unit, unit.Position) <= 0)
                return null;
            HexCoord? best = null;
            int bestCost = int.MaxValue;
            foreach (HexCoord cell in target.Position.Neighbors().OrderBy(c => c))
            {
                if (cell == unit.Position || HazardAt(unit, cell) > 0)
                    continue;
                if (unit.AttackRange == 1)
                {
                    if (!TryGetMeleeAttackPosition(unit.Id, target.Id, cell, out int cost))
                        continue;
                    if (cell != unit.Position && SandboxMovementPath.TryBuild(this, unit.Id, cell, out IReadOnlyList<HexCoord> path, out _) &&
                        PreviewHazardDamage(unit, path) > 0)
                        continue;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = cell;
                    }
                }
            }
            return best;
        }
    }
}
