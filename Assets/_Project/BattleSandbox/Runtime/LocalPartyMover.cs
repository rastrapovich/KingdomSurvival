using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-12К (канон v1.53 §28.3): движение отряда внутри исследуемого места.
    // Игрок ведёт только командира; спутники сами идут по его следу —
    // каждый к своей недавней клетке командира, поэтому никто не срезает
    // через стену и группа останавливается цепочкой рядом. Движение
    // непрерывное (доля шага), ходов и ОД нет; разметка — та же, что в бою.
    //
    // Правила [РАБОЧЕЕ]: два участника не стоят в одной клетке; спутник,
    // упёршийся в занятую клетку, ждёт и перестраивает путь, а после
    // секунды ожидания проходит сквозь своих (в узком проходе никто не
    // застревает навсегда); стоящего на пути спутника командир меняет
    // местами с собой; если кто-то отстал больше MaxLagSteps, командир
    // ждёт — игрок видит причину.
    public sealed class LocalPartyMover
    {
        public sealed class Member
        {
            public string Id;
            public HexCoord Cell;
            public HexCoord Next;
            public bool Stepping;
            // Доля текущего шага 0..1 от Cell к Next.
            public float Progress;
            // Номер направления шага (порядок соседей HexCoord, = HexFacing).
            public int Facing;
            internal readonly Queue<HexCoord> Path = new Queue<HexCoord>();
            internal float BlockedSeconds;

            // Клетка для расстановки к бою: дальше середины шага — уже следующая.
            public HexCoord SettledCell => Stepping && Progress >= 0.5f ? Next : Cell;
        }

        public const float FollowerPassThroughSeconds = 1f;
        public const int TrailLength = 48;

        private readonly Func<HexCoord, bool> passable;
        private readonly Func<HexCoord, int> stepCost;
        private readonly List<Member> members = new List<Member>();
        private readonly List<HexCoord> trail = new List<HexCoord>();

        public float CellsPerSecond = 2.5f;
        public int MaxLagSteps = 4;

        public IReadOnlyList<Member> Members => members;
        public Member Leader => members.Count > 0 ? members[0] : null;
        // Командир ждёт отставших.
        public bool IsWaitingForStragglers { get; private set; }

        public LocalPartyMover(
            Func<HexCoord, bool> passable,
            Func<HexCoord, int> stepCost,
            IReadOnlyList<KeyValuePair<string, HexCoord>> orderedMembers,
            IReadOnlyList<int> facings = null)
        {
            this.passable = passable ?? throw new ArgumentNullException(nameof(passable));
            this.stepCost = stepCost ?? (_ => 1);
            if (orderedMembers == null || orderedMembers.Count == 0)
                throw new ArgumentException("В месте должен быть хотя бы командир.", nameof(orderedMembers));
            for (int i = 0; i < orderedMembers.Count; i++)
            {
                members.Add(new Member
                {
                    Id = orderedMembers[i].Key,
                    Cell = orderedMembers[i].Value,
                    Facing = facings != null && i < facings.Count ? facings[i] : 0
                });
            }
            // След: сначала дальние спутники, последним — командир; каждый
            // спутник уже стоит на своей клетке следа.
            for (int i = members.Count - 1; i >= 0; i--)
                trail.Add(members[i].Cell);
        }

        public bool IsIdle
        {
            get
            {
                foreach (Member member in members)
                {
                    if (member.Stepping || member.Path.Count > 0)
                        return false;
                }
                return true;
            }
        }

        public bool LeaderHasOrder => Leader != null && (Leader.Stepping || Leader.Path.Count > 0);

        // Все спутники дошли до своих мест рядом с командиром.
        public bool IsGathered
        {
            get
            {
                List<HexCoord> targets = FollowerTargets();
                for (int i = 1; i < members.Count; i++)
                {
                    if (members[i].Stepping || members[i].Cell != targets[i])
                        return false;
                }
                return true;
            }
        }

        // Новый приказ заменяет старый от текущего положения: начатый шаг
        // доводится до клетки, дальше — новый путь. False — недостижимо.
        public bool MoveLeaderTo(HexCoord target)
        {
            Member leader = Leader;
            HexCoord origin = leader.Stepping ? leader.Next : leader.Cell;
            List<HexCoord> path = SandboxLocalNavigation.FindPath(origin, target, passable, stepCost);
            if (path.Count == 0)
                return false;
            leader.Path.Clear();
            for (int i = 1; i < path.Count; i++)
                leader.Path.Enqueue(path[i]);
            return true;
        }

        // Остановиться: начатые шаги доводятся до клеток, новых нет.
        public void Stop()
        {
            foreach (Member member in members)
                member.Path.Clear();
        }

        // Мгновенно встать в клетки (после боя, загрузки): без анимации.
        public void Place(IReadOnlyDictionary<string, HexCoord> cells)
        {
            foreach (Member member in members)
            {
                member.Path.Clear();
                member.Stepping = false;
                member.Progress = 0f;
                if (cells != null && cells.TryGetValue(member.Id, out HexCoord cell))
                    member.Cell = cell;
            }
            trail.Clear();
            for (int i = members.Count - 1; i >= 0; i--)
                trail.Add(members[i].Cell);
        }

        public void Remove(string memberId)
        {
            if (members.Count > 1 && members[0].Id != memberId)
                members.RemoveAll(member => member.Id == memberId);
        }

        // Шаг симуляции. Возвращает клетки, в которые командир вошёл за этот
        // шаг (для времени, зон угрозы и сохранения позиции).
        public List<HexCoord> Tick(float deltaSeconds)
        {
            List<HexCoord> entered = new List<HexCoord>();
            if (deltaSeconds <= 0f || members.Count == 0)
                return entered;

            Member leader = Leader;
            if (!leader.Stepping && leader.Path.Count > 0)
            {
                IsWaitingForStragglers = HasStraggler();
                if (!IsWaitingForStragglers)
                    StartLeaderStep(leader);
            }
            else if (!leader.Stepping)
            {
                IsWaitingForStragglers = false;
            }
            if (Advance(leader, deltaSeconds))
            {
                trail.Add(leader.Cell);
                if (trail.Count > TrailLength)
                    trail.RemoveRange(0, trail.Count - TrailLength);
                entered.Add(leader.Cell);
            }

            List<HexCoord> targets = FollowerTargets();
            for (int i = 1; i < members.Count; i++)
            {
                Member follower = members[i];
                if (!follower.Stepping)
                    StartFollowerStep(follower, targets[i], deltaSeconds);
                Advance(follower, deltaSeconds);
            }
            return entered;
        }

        private void StartLeaderStep(Member leader)
        {
            HexCoord next = leader.Path.Peek();
            if (!passable(next))
            {
                leader.Path.Clear();
                return;
            }
            leader.Path.Dequeue();
            // Спутник стоит на пути — меняемся местами.
            foreach (Member other in members)
            {
                if (other != leader && !other.Stepping && other.Cell == next)
                    BeginStep(other, leader.Cell);
            }
            BeginStep(leader, next);
        }

        private void StartFollowerStep(Member follower, HexCoord target, float deltaSeconds)
        {
            if (follower.Cell == target)
            {
                follower.BlockedSeconds = 0f;
                return;
            }
            bool passThrough = follower.BlockedSeconds >= FollowerPassThroughSeconds;
            List<HexCoord> path = SandboxLocalNavigation.FindPath(
                follower.Cell,
                target,
                cell => passable(cell) && (passThrough || cell == target || !IsReservedByOther(cell, follower)),
                stepCost);
            if (path.Count < 2 || (!passThrough && IsReservedByOther(path[1], follower)))
            {
                follower.BlockedSeconds += deltaSeconds;
                return;
            }
            follower.BlockedSeconds = 0f;
            BeginStep(follower, path[1]);
        }

        private static void BeginStep(Member member, HexCoord next)
        {
            int facing = member.Cell.GetNeighborIndex(next);
            if (facing >= 0)
                member.Facing = facing;
            member.Next = next;
            member.Progress = 0f;
            member.Stepping = true;
        }

        // True — шаг завершён в этом тике.
        private bool Advance(Member member, float deltaSeconds)
        {
            if (!member.Stepping)
                return false;
            member.Progress += deltaSeconds * CellsPerSecond / Math.Max(1, stepCost(member.Next));
            if (member.Progress < 1f)
                return false;
            member.Cell = member.Next;
            member.Stepping = false;
            member.Progress = 0f;
            return true;
        }

        private bool IsReservedByOther(HexCoord cell, Member self)
        {
            foreach (Member other in members)
            {
                if (other == self)
                    continue;
                if (other.Cell == cell || (other.Stepping && other.Next == cell))
                    return true;
            }
            return false;
        }

        // Место спутника i — i-я по давности различная клетка следа командира.
        private List<HexCoord> FollowerTargets()
        {
            List<HexCoord> targets = new List<HexCoord> { Leader.Cell };
            HashSet<HexCoord> used = new HashSet<HexCoord> { Leader.Cell };
            for (int index = trail.Count - 1; index >= 0 && targets.Count < members.Count; index--)
            {
                if (used.Add(trail[index]))
                    targets.Add(trail[index]);
            }
            while (targets.Count < members.Count)
                targets.Add(members[targets.Count].Cell);
            return targets;
        }

        private bool HasStraggler()
        {
            Member leader = Leader;
            for (int i = 1; i < members.Count; i++)
            {
                if (members[i].Cell.DistanceTo(leader.Cell) > i + MaxLagSteps)
                    return true;
            }
            return false;
        }
    }
}
