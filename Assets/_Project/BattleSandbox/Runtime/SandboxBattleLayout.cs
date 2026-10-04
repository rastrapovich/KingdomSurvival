using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12К (канон v1.53 §28.3): бой из заранее подготовленной геометрии.
    // Позиции, стены и трудная местность задаёт вызывающий (исследуемое
    // место), а не генератор полигона: случайных камней и болот здесь нет,
    // и стена не исчезает, если кому-то не хватило места. Всё — HP, лидер,
    // прибавка первого раунда — ставится до Start(), то есть до построения
    // очереди первого раунда. Неверная раскладка — ошибка, а не подмена.
    public sealed class SandboxLayoutUnit
    {
        public string InstanceId;
        public SandboxUnitDefinition Definition;
        public SandboxTeam Team;
        public HexCoord Position;
        // 0 — полный запас шаблона.
        public int StartingHitPoints;
    }

    public sealed class SandboxBattleLayout
    {
        public readonly List<SandboxLayoutUnit> Units = new List<SandboxLayoutUnit>();
        // Стены и иные непроходимые клетки места (отключённые гексы поля).
        public readonly HashSet<HexCoord> BlockedCells = new HashSet<HexCoord>();
        public readonly HashSet<HexCoord> DifficultCells = new HashSet<HexCoord>();
        public int PlayerFirstRoundInitiativeBonus;
        public string LeaderUnitId;

        public bool IsPassable(HexCoord cell)
        {
            return SandboxArenaShape.Contains(cell) && !BlockedCells.Contains(cell);
        }

        // Пустой список — раскладка годится для боя.
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            foreach (HexCoord cell in BlockedCells)
            {
                if (!SandboxArenaShape.Contains(cell))
                    errors.Add("Стена вне поля: " + cell + ".");
            }
            foreach (HexCoord cell in DifficultCells)
            {
                if (!SandboxArenaShape.Contains(cell) || BlockedCells.Contains(cell))
                    errors.Add("Трудная клетка вне доступной области: " + cell + ".");
            }

            int players = 0;
            int enemies = 0;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<HexCoord> taken = new HashSet<HexCoord>();
            foreach (SandboxLayoutUnit unit in Units)
            {
                if (unit == null || unit.Definition == null)
                {
                    errors.Add("Участник без боевого шаблона.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(unit.InstanceId) || !ids.Add(unit.InstanceId))
                    errors.Add("Пустой или повторный ID участника: '" + unit.InstanceId + "'.");
                if (!IsPassable(unit.Position))
                    errors.Add("Участник '" + unit.InstanceId + "' стоит в стене или вне поля: " + unit.Position + ".");
                else if (!taken.Add(unit.Position))
                    errors.Add("Два участника на одной клетке: " + unit.Position + ".");
                if (unit.Team == SandboxTeam.Player)
                    players++;
                else
                    enemies++;
            }

            if (players == 0)
                errors.Add("В бою нет ни одного участника отряда.");
            if (enemies == 0)
                errors.Add("В бою нет ни одного противника.");
            if (enemies > SandboxRoster.MaxEnemies)
                errors.Add("Противников больше " + SandboxRoster.MaxEnemies + ".");
            if (!string.IsNullOrEmpty(LeaderUnitId) && !ids.Contains(LeaderUnitId))
                errors.Add("Лидер '" + LeaderUnitId + "' не участвует в бою.");

            // Все участники должны быть достижимы друг для друга: иначе
            // ближний бой не закончится никогда.
            if (errors.Count == 0)
            {
                HashSet<HexCoord> region = SandboxLocalNavigation.Region(Units[0].Position, IsPassable);
                foreach (SandboxLayoutUnit unit in Units)
                {
                    if (!region.Contains(unit.Position))
                        errors.Add("Участник '" + unit.InstanceId + "' отрезан стеной от остальных.");
                }
            }

            return errors;
        }

        public SandboxBattle CreateBattle()
        {
            List<string> errors = Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Бой не может начаться: " + string.Join(" ", errors));

            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            foreach (HexCoord inactive in SandboxArenaShape.InactiveCells())
                terrain[inactive] = SandboxTerrain.Impassable;
            foreach (HexCoord blocked in BlockedCells)
                terrain[blocked] = SandboxTerrain.Impassable;
            foreach (HexCoord difficult in DifficultCells)
                terrain[difficult] = SandboxTerrain.Difficult;

            List<SandboxUnitState> units = new List<SandboxUnitState>();
            foreach (SandboxLayoutUnit unit in Units)
            {
                SandboxUnitState state = new SandboxUnitState(unit.InstanceId, unit.Definition, unit.Team, unit.Position);
                if (unit.StartingHitPoints > 0)
                    state.SetStartingHitPoints(unit.StartingHitPoints);
                units.Add(state);
            }

            SandboxTerrainRules.RegisterBattle(units, terrain);
            SandboxBattle battle = new SandboxBattle(
                SandboxArenaShape.Width,
                SandboxArenaShape.Height,
                units,
                terrain,
                BlockedCells);
            battle.PlayerFirstRoundInitiativeBonus = PlayerFirstRoundInitiativeBonus;
            battle.LeaderUnitId = LeaderUnitId;
            battle.Start();
            return battle;
        }
    }

    // ПР-12К: скрытая гексовая разметка исследуемого места — та же, что в
    // бою. Путь не срезает через стену; расстановка к бою ищет ближайшую
    // свободную клетку по проходимой области, а не по прямой сквозь стену.
    public static class SandboxLocalNavigation
    {
        public static HashSet<HexCoord> Region(HexCoord start, Func<HexCoord, bool> passable)
        {
            HashSet<HexCoord> visited = new HashSet<HexCoord>();
            if (passable == null || !passable(start))
                return visited;
            Queue<HexCoord> frontier = new Queue<HexCoord>();
            visited.Add(start);
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();
                foreach (HexCoord next in current.Neighbors())
                {
                    if (visited.Contains(next) || !passable(next))
                        continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }
            return visited;
        }

        // Кратчайший путь с ценой клетки (трудная — 2). Пусто — недостижимо.
        // Путь начинается с from и заканчивается to.
        public static List<HexCoord> FindPath(
            HexCoord from,
            HexCoord to,
            Func<HexCoord, bool> passable,
            Func<HexCoord, int> stepCost = null)
        {
            List<HexCoord> path = new List<HexCoord>();
            if (passable == null || !passable(from) || !passable(to))
                return path;
            if (from == to)
            {
                path.Add(from);
                return path;
            }

            Dictionary<HexCoord, int> cost = new Dictionary<HexCoord, int> { [from] = 0 };
            Dictionary<HexCoord, HexCoord> previous = new Dictionary<HexCoord, HexCoord>();
            SortedSet<(int, HexCoord)> open = new SortedSet<(int, HexCoord)> { (0, from) };
            while (open.Count > 0)
            {
                (int currentCost, HexCoord current) = open.Min;
                open.Remove(open.Min);
                if (current == to)
                    break;
                if (currentCost > cost[current])
                    continue;
                foreach (HexCoord next in current.Neighbors().OrderBy(n => n))
                {
                    if (!passable(next))
                        continue;
                    int nextCost = currentCost + Math.Max(1, stepCost != null ? stepCost(next) : 1);
                    if (cost.TryGetValue(next, out int known) && known <= nextCost)
                        continue;
                    if (cost.ContainsKey(next))
                        open.Remove((known, next));
                    cost[next] = nextCost;
                    previous[next] = current;
                    open.Add((nextCost, next));
                }
            }

            if (!previous.ContainsKey(to))
                return path;
            HexCoord step = to;
            path.Add(step);
            while (step != from)
            {
                step = previous[step];
                path.Add(step);
            }
            path.Reverse();
            return path;
        }

        // Ближайшая свободная проходимая клетка, достижимая от preferred
        // (BFS по проходимым клеткам: со своей стороны стены). null — нет.
        public static HexCoord? NearestFree(
            HexCoord preferred,
            Func<HexCoord, bool> passable,
            ISet<HexCoord> taken)
        {
            if (passable == null || !passable(preferred))
                return null;
            Queue<HexCoord> frontier = new Queue<HexCoord>();
            HashSet<HexCoord> visited = new HashSet<HexCoord> { preferred };
            frontier.Enqueue(preferred);
            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();
                if (taken == null || !taken.Contains(current))
                    return current;
                foreach (HexCoord next in current.Neighbors().OrderBy(n => n))
                {
                    if (visited.Contains(next) || !passable(next))
                        continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }
            return null;
        }

        // Расстановка к бою: каждому — уникальная ближайшая допустимая клетка
        // в порядке списка (сначала тот, кто важнее, — герой). False — места
        // не хватило: бой с такими позициями начинать нельзя.
        public static bool TryAssignCells(
            IReadOnlyList<KeyValuePair<string, HexCoord>> preferred,
            Func<HexCoord, bool> passable,
            ISet<HexCoord> reserved,
            out Dictionary<string, HexCoord> assigned)
        {
            assigned = new Dictionary<string, HexCoord>(StringComparer.Ordinal);
            HashSet<HexCoord> taken = reserved != null ? new HashSet<HexCoord>(reserved) : new HashSet<HexCoord>();
            foreach (KeyValuePair<string, HexCoord> entry in preferred)
            {
                HexCoord? cell = NearestFree(entry.Value, passable, taken);
                if (cell == null)
                    return false;
                assigned[entry.Key] = cell.Value;
                taken.Add(cell.Value);
            }
            return true;
        }
    }
}
