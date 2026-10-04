using System;
using System.Collections.Generic;

// ПР-12К (канон v1.54 §28.3): движение отряда в исследуемом месте — то же,
// что движение героя по глобальной карте: путь по разметке местности с
// обходом и сглаживанием (WorldMapPathfinder), непрерывный бег со
// скоростью местности, новый приказ — от текущей точки, клик в
// непроходимое — к ближайшей доступной. Клеток и ходов нет.
//
// Спутники идут по следу командира: у каждого своё место на ломаной следа
// на расстоянии Spacing × номер позади. Они не срезают через стены (след —
// пройденный путь) и не стоят друг в друге (места на следе разнесены).
public sealed class LocalFreeMover
{
    public sealed class Member
    {
        public string Id;
        public double X;
        public double Y;
        // Направление последнего шага (экранное, Y вниз) — для ракурса.
        public double DirectionX = 1;
        public double DirectionY;
        public bool Walking;
        // Положение на следе (длина от начала ломаной).
        internal double TrailPosition;
    }

    private readonly WorldMapTerrainLayer layer;
    private readonly WorldMapMovementRules rules;
    private readonly List<Member> members = new List<Member>();
    // След командира: ломаная, последняя точка — командир.
    private readonly List<double> trailX = new List<double>();
    private readonly List<double> trailY = new List<double>();
    private readonly List<double> trailLength = new List<double>();
    private List<WorldMapPathfinder.PathPoint> path = new List<WorldMapPathfinder.PathPoint>();
    private int pathIndex;

    // Расстояние между участниками на следе (пиксели рисунка).
    public double Spacing;
    // Спутник догоняет своё место чуть быстрее командира.
    public double FollowerSpeedFactor = 1.3;

    public IReadOnlyList<Member> Members => members;
    public Member Leader => members.Count > 0 ? members[0] : null;
    public WorldMapTerrainLayer Layer => layer;
    public WorldMapMovementRules Rules => rules;
    public bool LeaderHasOrder => pathIndex < path.Count;
    // Последний приказ дошёл до самой точки (не к ближайшей доступной).
    public bool LastOrderReachesTarget { get; private set; } = true;

    public bool IsIdle
    {
        get
        {
            if (LeaderHasOrder)
                return false;
            foreach (Member member in members)
            {
                if (member.Walking)
                    return false;
            }
            return true;
        }
    }

    // Все спутники на своих местах позади командира.
    public bool IsGathered
    {
        get
        {
            for (int i = 1; i < members.Count; i++)
            {
                if (Math.Abs(members[i].TrailPosition - TargetTrailPosition(i)) > Spacing * 0.25)
                    return false;
            }
            return true;
        }
    }

    public LocalFreeMover(
        WorldMapTerrainLayer layer,
        WorldMapMovementRules rules,
        IReadOnlyList<KeyValuePair<string, LocalPointData>> orderedMembers,
        double spacing)
    {
        this.layer = layer ?? throw new ArgumentNullException(nameof(layer));
        this.rules = rules ?? WorldMapMovementRules.Current;
        if (orderedMembers == null || orderedMembers.Count == 0)
            throw new ArgumentException("В месте должен быть хотя бы командир.", nameof(orderedMembers));
        Spacing = Math.Max(1.0, spacing);
        foreach (KeyValuePair<string, LocalPointData> entry in orderedMembers)
            members.Add(new Member { Id = entry.Key, X = entry.Value.X, Y = entry.Value.Y });
        RebuildTrail();
    }

    // Пиксели одной клетки сетки проходимости (ширина шестиугольника).
    public double HexWidth => layer.Grid.HexWidth;

    public bool IsPassable(double x, double y)
    {
        if (x < 0 || y < 0 || x > layer.Grid.CanvasWidth || y > layer.Grid.CanvasHeight)
            return false;
        return rules.IsTraversable(layer.GetAtPixel(x, y));
    }

    // Бег к точке. False — путь не найден вовсе (окружён стенами).
    public bool MoveLeaderTo(double x, double y)
    {
        Member leader = Leader;
        List<WorldMapPathfinder.PathPoint> found = WorldMapPathfinder.FindPath(layer, rules, leader.X, leader.Y, x, y, out bool reached);
        LastOrderReachesTarget = reached;
        if (found.Count < 2)
        {
            path.Clear();
            pathIndex = 0;
            return reached && found.Count == 1 && Math.Abs(found[0].X - x) < 0.5 && Math.Abs(found[0].Y - y) < 0.5;
        }
        path = found;
        pathIndex = 1;
        return true;
    }

    public void Stop()
    {
        path.Clear();
        pathIndex = 0;
    }

    // Мгновенно встать (после боя, загрузки): след строится заново.
    public void Place(IReadOnlyDictionary<string, LocalPointData> points)
    {
        Stop();
        foreach (Member member in members)
        {
            if (points != null && points.TryGetValue(member.Id, out LocalPointData point) && point != null)
            {
                member.X = point.X;
                member.Y = point.Y;
            }
            member.Walking = false;
        }
        RebuildTrail();
    }

    public void Remove(string memberId)
    {
        if (members.Count > 1 && members[0].Id != memberId)
            members.RemoveAll(member => member.Id == memberId);
    }

    // Шаг симуляции. Возвращает пройденное командиром расстояние в клетках
    // сетки проходимости, взвешенное местностью: из него считаются игровые
    // часы (rules.HoursPerHex), как на глобальной карте.
    public double Tick(double deltaSeconds, out double travelledHours)
    {
        travelledHours = 0;
        if (deltaSeconds <= 0 || members.Count == 0)
            return 0;

        Member leader = Leader;
        double remaining = deltaSeconds;
        double moved = 0;
        while (remaining > 1e-6 && pathIndex < path.Count)
        {
            WorldMapGameplayTerrainType terrain = layer.GetAtPixel(leader.X, leader.Y);
            double speed = rules.SafeRunSpeedHexesPerSecond * rules.RunSpeedMultiplier(terrain) * HexWidth;
            WorldMapPathfinder.PathPoint target = path[pathIndex];
            double dx = target.X - leader.X;
            double dy = target.Y - leader.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double step = speed * remaining;
            if (distance <= step)
            {
                MoveLeader(target.X, target.Y, ref travelledHours, terrain);
                moved += distance;
                remaining -= distance / Math.Max(1e-6, speed);
                pathIndex++;
            }
            else
            {
                MoveLeader(leader.X + dx / distance * step, leader.Y + dy / distance * step, ref travelledHours, terrain);
                moved += step;
                remaining = 0;
            }
        }
        leader.Walking = moved > 1e-6;
        if (pathIndex >= path.Count && path.Count > 0)
            Stop();

        double followerSpeed = rules.SafeRunSpeedHexesPerSecond * HexWidth * FollowerSpeedFactor * deltaSeconds;
        for (int i = 1; i < members.Count; i++)
        {
            Member follower = members[i];
            double target = TargetTrailPosition(i);
            double delta = target - follower.TrailPosition;
            double before = follower.TrailPosition;
            if (Math.Abs(delta) <= followerSpeed)
                follower.TrailPosition = target;
            else
                follower.TrailPosition += Math.Sign(delta) * followerSpeed;
            PointOnTrail(follower.TrailPosition, out double x, out double y);
            double fx = x - follower.X;
            double fy = y - follower.Y;
            follower.Walking = Math.Abs(follower.TrailPosition - before) > 1e-4;
            if (follower.Walking && fx * fx + fy * fy > 1e-6)
            {
                double length = Math.Sqrt(fx * fx + fy * fy);
                follower.DirectionX = fx / length;
                follower.DirectionY = fy / length;
            }
            follower.X = x;
            follower.Y = y;
        }
        return moved / HexWidth;
    }

    private void MoveLeader(double x, double y, ref double hours, WorldMapGameplayTerrainType terrain)
    {
        Member leader = Leader;
        double dx = x - leader.X;
        double dy = y - leader.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1e-6)
            return;
        leader.DirectionX = dx / distance;
        leader.DirectionY = dy / distance;
        leader.X = x;
        leader.Y = y;
        hours += distance / HexWidth * rules.HoursPerHex(terrain);
        AppendTrail(x, y);
    }

    private double TargetTrailPosition(int index)
    {
        double leaderPosition = trailLength.Count > 0 ? trailLength[trailLength.Count - 1] : 0;
        return Math.Max(0, leaderPosition - Spacing * index);
    }

    // След: спутники от дальнего к ближнему, затем командир. Между точками
    // — путь по разметке, чтобы ломаная не проходила сквозь стены.
    private void RebuildTrail()
    {
        trailX.Clear();
        trailY.Clear();
        trailLength.Clear();
        for (int i = members.Count - 1; i >= 0; i--)
        {
            Member member = members[i];
            if (trailX.Count == 0)
            {
                AppendTrail(member.X, member.Y);
            }
            else
            {
                List<WorldMapPathfinder.PathPoint> link = WorldMapPathfinder.FindPath(
                    layer, rules, trailX[trailX.Count - 1], trailY[trailY.Count - 1], member.X, member.Y, out _);
                for (int p = 1; p < link.Count; p++)
                    AppendTrail(link[p].X, link[p].Y);
                AppendTrail(member.X, member.Y);
            }
            member.TrailPosition = trailLength[trailLength.Count - 1];
        }
    }

    private void AppendTrail(double x, double y)
    {
        if (trailX.Count == 0)
        {
            trailX.Add(x);
            trailY.Add(y);
            trailLength.Add(0);
            return;
        }
        double dx = x - trailX[trailX.Count - 1];
        double dy = y - trailY[trailY.Count - 1];
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 0.5)
            return;
        trailX.Add(x);
        trailY.Add(y);
        trailLength.Add(trailLength[trailLength.Count - 1] + distance);
        // Хвост, который никому уже не нужен, отбрасывается.
        double keep = Spacing * (members.Count + 2);
        while (trailLength.Count > 2 && trailLength[trailLength.Count - 1] - trailLength[1] > keep)
        {
            double cut = trailLength[1];
            trailX.RemoveAt(0);
            trailY.RemoveAt(0);
            trailLength.RemoveAt(0);
            for (int i = 0; i < trailLength.Count; i++)
                trailLength[i] -= cut;
            foreach (Member member in members)
                member.TrailPosition = Math.Max(0, member.TrailPosition - cut);
        }
    }

    private void PointOnTrail(double position, out double x, out double y)
    {
        if (trailLength.Count == 1 || position <= 0)
        {
            x = trailX[0];
            y = trailY[0];
            return;
        }
        for (int i = 1; i < trailLength.Count; i++)
        {
            if (position <= trailLength[i])
            {
                double t = (position - trailLength[i - 1]) / Math.Max(1e-6, trailLength[i] - trailLength[i - 1]);
                x = trailX[i - 1] + (trailX[i] - trailX[i - 1]) * t;
                y = trailY[i - 1] + (trailY[i] - trailY[i - 1]) * t;
                return;
            }
        }
        x = trailX[trailX.Count - 1];
        y = trailY[trailY.Count - 1];
    }
}
