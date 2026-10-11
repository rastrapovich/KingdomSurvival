using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К: движение отряда в исследуемом месте — строго по гексам боя
    // места (LocationBattleGrid), как на поле боя: из центра клетки в центр
    // соседней, с обходом стен (клетки на непроходимом и за краем рисунка) и
    // с той же скоростью, что у бойцов, — одна общая скорость ходьбы по
    // гексам (BattlefieldDatabaseAsset.WalkHexesPerSecond); трудная клетка
    // проходится медленнее и в пути дороже. Новый приказ на ходу выполняется
    // после шага, начатого раньше: командир всегда приходит в центр клетки.
    // Клик в стену — к ближайшей доступной клетке.
    //
    // Спутники идут змейкой: спутник с номером i встаёт в клетку, которую
    // командир покинул i шагов назад; к ней — своим путём по гексам и чуть
    // быстрее командира, чтобы не отставать.
    public sealed class LocalHexMover
    {
        public sealed class Member
        {
            public string Id;
            // Точка на рисунке места (пиксели, Y вниз).
            public double X;
            public double Y;
            // Направление последнего шага (экранное, Y вниз) — для ракурса.
            public double DirectionX = 1;
            public double DirectionY;
            public bool Walking;
            // Клетка, где участник стоит или из которой шагает.
            public HexCoord Cell { get; internal set; }
            // Клетки впереди; первая — та, в которую идёт шаг.
            internal readonly List<HexCoord> Route = new List<HexCoord>();
            // Доля текущего шага (0..1).
            internal double Progress;
        }

        // Трудная клетка в пути — как в бою: дороже при выборе пути.
        private const int DifficultStepCost = 2;

        private readonly LocalLocationGeometry geometry;
        private readonly LocationBattleGrid grid;
        private readonly HashSet<HexCoord> blocked;
        private readonly HashSet<HexCoord> difficult;
        private readonly List<Member> members = new List<Member>();
        // Клетки, которые покинул командир: первая — последняя покинутая.
        private readonly List<HexCoord> history = new List<HexCoord>();

        // Гексов в секунду (без трудной местности).
        public float HexesPerSecond;
        // Спутник догоняет своё место чуть быстрее командира.
        public float FollowerSpeedFactor = 1.3f;

        public IReadOnlyList<Member> Members => members;
        public Member Leader => members.Count > 0 ? members[0] : null;
        public LocationBattleGrid Grid => grid;
        public bool LeaderHasOrder => Leader != null && Leader.Route.Count > 0;
        // Последний приказ дошёл до самой клетки (не к ближайшей доступной).
        public bool LastOrderReachesTarget { get; private set; } = true;
        // Радиус клетки (пиксели рисунка): на столько точка цели может быть
        // дальше от центра клетки, в которую пришёл командир.
        public float CellRadius => grid.HexSize;

        public bool IsIdle => members.TrueForAll(member => member.Route.Count == 0);

        // Все спутники в своих клетках позади командира.
        public bool IsGathered
        {
            get
            {
                List<HexCoord> targets = FollowerTargets();
                for (int i = 1; i < members.Count; i++)
                    if (members[i].Route.Count > 0 || members[i].Cell != targets[i])
                        return false;
                return true;
            }
        }

        public LocalHexMover(LocalLocationGeometry geometry, IReadOnlyList<KeyValuePair<string, LocalPointData>> orderedMembers, float hexesPerSecond = 0)
        {
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (orderedMembers == null || orderedMembers.Count == 0)
                throw new ArgumentException("В месте должен быть хотя бы командир.", nameof(orderedMembers));
            grid = geometry.BattleGrid;
            blocked = geometry.GridBlockedCells();
            difficult = geometry.GridDifficultCells();
            HexesPerSecond = hexesPerSecond > 0 ? hexesPerSecond : BattlefieldDatabaseAsset.SharedWalkHexesPerSecond;
            foreach (KeyValuePair<string, LocalPointData> entry in orderedMembers)
                members.Add(new Member { Id = entry.Key });
            Place(orderedMembers.ToDictionary(entry => entry.Key, entry => entry.Value));
        }

        public bool IsPassable(HexCoord cell) => grid.Contains(cell) && !blocked.Contains(cell);

        // Точка рисунка проходима, если проходима её клетка.
        public bool IsPassable(double x, double y)
        {
            if (x < 0 || y < 0 || x > grid.Canvas.x || y > grid.Canvas.y)
                return false;
            return IsPassable(grid.CellAt(new Vector2((float)x, (float)y)));
        }

        public HexCoord CellAt(double x, double y) => grid.CellAt(new Vector2((float)x, (float)y));
        public Vector2 CellCenter(HexCoord cell) => grid.CellCenter(cell);

        // Командир дошёл до точки: она в его клетке или в радиусе radius от него.
        public bool LeaderReached(double x, double y, double radius)
        {
            Member leader = Leader;
            double dx = x - leader.X, dy = y - leader.Y;
            return dx * dx + dy * dy <= Math.Pow(radius + CellRadius, 2) || CellAt(x, y) == leader.Cell;
        }

        // Путь к точке. False — путь не найден вовсе (окружён стенами).
        public bool MoveLeaderTo(double x, double y)
        {
            Member leader = Leader;
            HexCoord start = leader.Route.Count > 0 ? leader.Route[0] : leader.Cell;
            Vector2 point = new Vector2((float)x, (float)y);
            HexCoord target = grid.CellAt(point);
            bool reached = IsPassable(x, y);
            List<HexCoord> path = reached ? FindPath(start, target) : new List<HexCoord>();
            if (path.Count == 0)
            {
                // Стена или клетка за стеной — к ближайшей доступной.
                reached = false;
                if (NearestReachable(start, point, out target)) path = FindPath(start, target);
            }
            if (path.Count == 0)
            {
                LastOrderReachesTarget = false;
                return false;
            }
            LastOrderReachesTarget = reached;
            SetRoute(leader, path);
            return true;
        }

        // Остановиться: шаг, начатый раньше, доводится до центра клетки.
        public void Stop()
        {
            foreach (Member member in members)
                if (member.Route.Count > 1)
                    member.Route.RemoveRange(1, member.Route.Count - 1);
        }

        // Мгновенно встать (вход, после боя, загрузка): каждому — своя
        // свободная проходимая клетка у его точки; спутники — в очереди
        // за командиром.
        public void Place(IReadOnlyDictionary<string, LocalPointData> points)
        {
            HashSet<HexCoord> taken = new HashSet<HexCoord>();
            foreach (Member member in members)
            {
                LocalPointData point = points != null && points.TryGetValue(member.Id, out LocalPointData found) && found != null
                    ? found : new LocalPointData((float)member.X, (float)member.Y);
                HexCoord cell = FreeCellNear(new Vector2(point.X, point.Y), taken);
                taken.Add(cell);
                member.Cell = cell;
                member.Route.Clear();
                member.Progress = 0;
                member.Walking = false;
                Vector2 center = grid.CellCenter(cell);
                member.X = center.x;
                member.Y = center.y;
            }
            history.Clear();
            for (int i = 1; i < members.Count; i++)
                history.Add(members[i].Cell);
        }

        public void Remove(string memberId)
        {
            if (members.Count > 1 && members[0].Id != memberId)
                members.RemoveAll(member => member.Id == memberId);
        }

        // Шаг симуляции. Возвращает пройденное командиром расстояние в
        // клетках сетки проходимости, взвешенное местностью: из него
        // считаются игровые часы (Rules.HoursPerHex), как на глобальной карте.
        public double Tick(double deltaSeconds, out double travelledHours)
        {
            travelledHours = 0;
            if (deltaSeconds <= 0 || members.Count == 0)
                return 0;
            double moved = Advance(Leader, deltaSeconds, HexesPerSecond, true, ref travelledHours);
            List<HexCoord> targets = FollowerTargets();
            for (int i = 1; i < members.Count; i++)
            {
                Member follower = members[i];
                HexCoord target = targets[i];
                HexCoord end = follower.Route.Count > 0 ? follower.Route[follower.Route.Count - 1] : follower.Cell;
                if (end != target)
                {
                    HexCoord start = follower.Route.Count > 0 ? follower.Route[0] : follower.Cell;
                    List<HexCoord> path = FindPath(start, target);
                    if (path.Count > 0) SetRoute(follower, path);
                }
                double ignored = 0;
                Advance(follower, deltaSeconds, HexesPerSecond * FollowerSpeedFactor, false, ref ignored);
            }
            return moved / geometry.Layer.Grid.HexWidth;
        }

        private double Advance(Member member, double seconds, double hexesPerSecond, bool leader, ref double hours)
        {
            double moved = 0;
            while (seconds > 1e-9 && member.Route.Count > 0)
            {
                HexCoord next = member.Route[0];
                Vector2 from = grid.CellCenter(member.Cell), to = grid.CellCenter(next);
                double duration = 1.0 / Math.Max(.01, hexesPerSecond) * (difficult.Contains(next) ? SandboxMovementTimeline.DifficultTerrainSlowdown : 1);
                double step = Math.Min(seconds, (1 - member.Progress) * duration);
                double before = member.Progress;
                member.Progress += step / duration;
                seconds -= step;
                double length = Vector2.Distance(from, to);
                double distance = (member.Progress - before) * length;
                moved += distance;
                if (leader)
                    hours += distance / geometry.Layer.Grid.HexWidth * geometry.Rules.HoursPerHex(geometry.Layer.GetAtPixel(member.X, member.Y));
                if (length > 1e-4)
                {
                    member.DirectionX = (to.x - from.x) / length;
                    member.DirectionY = (to.y - from.y) / length;
                }
                if (member.Progress >= 1 - 1e-9)
                {
                    if (leader)
                    {
                        history.Insert(0, member.Cell);
                        if (history.Count > HistoryLength) history.RemoveRange(HistoryLength, history.Count - HistoryLength);
                    }
                    member.Cell = next;
                    member.Route.RemoveAt(0);
                    member.Progress = 0;
                }
            }
            Vector2 position = member.Route.Count > 0
                ? Vector2.Lerp(grid.CellCenter(member.Cell), grid.CellCenter(member.Route[0]), (float)member.Progress)
                : grid.CellCenter(member.Cell);
            member.X = position.x;
            member.Y = position.y;
            member.Walking = member.Route.Count > 0;
            return moved;
        }

        // Шаг, начатый раньше, остаётся первым: новый путь — от его клетки.
        private static void SetRoute(Member member, List<HexCoord> path)
        {
            bool stepping = member.Route.Count > 0;
            HexCoord current = stepping ? member.Route[0] : member.Cell;
            member.Route.Clear();
            if (stepping) member.Route.Add(current);
            for (int i = 1; i < path.Count; i++)
                member.Route.Add(path[i]);
        }

        // Сколько покинутых клеток помнить: командир может ходить туда-обратно.
        private int HistoryLength => members.Count * 4;

        // Клетки спутников (индекс — номер участника; у командира — его
        // клетка): по порядку покинутых командиром клеток, без повторов и без
        // клеток, куда идёт или где стоит командир. Не хватило — своя клетка.
        private List<HexCoord> FollowerTargets()
        {
            Member leader = Leader;
            List<HexCoord> targets = new List<HexCoord> { leader.Cell };
            HashSet<HexCoord> used = new HashSet<HexCoord> { leader.Cell };
            foreach (HexCoord cell in leader.Route) used.Add(cell);
            int next = 0;
            for (int i = 1; i < members.Count; i++)
            {
                while (next < history.Count && used.Contains(history[next])) next++;
                HexCoord target = next < history.Count ? history[next++] : members[i].Cell;
                used.Add(target);
                targets.Add(target);
            }
            return targets;
        }

        private List<HexCoord> FindPath(HexCoord from, HexCoord to) =>
            SandboxLocalNavigation.FindPath(from, to, IsPassable, cell => difficult.Contains(cell) ? DifficultStepCost : 1);

        // Ближайшая к точке клетка, до которой можно дойти от start.
        private bool NearestReachable(HexCoord start, Vector2 point, out HexCoord cell)
        {
            cell = start;
            HashSet<HexCoord> region = SandboxLocalNavigation.Region(start, IsPassable);
            if (region.Count == 0) return false;
            float best = float.MaxValue;
            foreach (HexCoord candidate in region)
            {
                float distance = (grid.CellCenter(candidate) - point).sqrMagnitude;
                if (distance < best || (Mathf.Approximately(distance, best) && candidate.CompareTo(cell) < 0))
                {
                    best = distance;
                    cell = candidate;
                }
            }
            return true;
        }

        // Свободная проходимая клетка у точки: своя клетка, иначе ближайшая.
        private HexCoord FreeCellNear(Vector2 point, ISet<HexCoord> taken)
        {
            HexCoord cell = grid.CellAt(point);
            if (IsPassable(cell) && !taken.Contains(cell)) return cell;
            if (IsPassable(cell))
            {
                HexCoord? free = SandboxLocalNavigation.NearestFree(cell, IsPassable, taken);
                if (free.HasValue) return free.Value;
            }
            HexCoord best = cell;
            float bestDistance = float.MaxValue;
            foreach (HexCoord candidate in grid.Cells())
            {
                if (!IsPassable(candidate) || taken.Contains(candidate)) continue;
                float distance = (grid.CellCenter(candidate) - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }
    }
}
