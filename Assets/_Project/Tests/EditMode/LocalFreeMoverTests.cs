using System;
using System.Collections.Generic;
using NUnit.Framework;

// ПР-12К (канон v1.54 §28.3): движение по исследуемому месту — как по
// глобальной карте. Командир обходит стены по разметке, клик в стену ведёт
// к ближайшей доступной точке, время считается пройденным путём; спутники
// идут по следу, не срезают через стены и не стоят друг в друге.
public sealed class LocalFreeMoverTests
{
    private const float Width = 1920;
    private const float Height = 1080;

    // Стена поперёк рисунка с проходом внизу.
    private static WorldMapTerrainLayer WallWithGap()
    {
        WorldMapTerrainLayer layer = new WorldMapTerrainLayer(new WorldMapHexGrid(Width, Height, 60));
        for (int index = 0; index < layer.Grid.CellCount; index++)
        {
            WorldMapHexCell cell = layer.Grid.CellAt(index);
            layer.Grid.CellCenter(cell, out double x, out double y);
            if (x > 900 && x < 1020 && y < 800)
                layer.Set(cell, WorldMapGameplayTerrainType.Cliffs);
        }
        return layer;
    }

    private static LocalFreeMover Party(WorldMapTerrainLayer layer, WorldMapMovementRules rules, int count)
    {
        List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
        for (int i = 0; i < count; i++)
            members.Add(new KeyValuePair<string, LocalPointData>("m" + i, new LocalPointData(300 - i * 60, 400)));
        return new LocalFreeMover(layer, rules, members, 70);
    }

    private static WorldMapMovementRules Rules() => LocalLocationDefinition.DefaultMovement();

    private static void Run(LocalFreeMover mover, Action<LocalFreeMover> eachStep = null, int steps = 2000)
    {
        for (int i = 0; i < steps && !mover.IsIdle; i++)
        {
            mover.Tick(1 / 60.0, out _);
            eachStep?.Invoke(mover);
        }
    }

    [Test]
    public void Leader_WalksAroundTheWall_ToTheTarget()
    {
        WorldMapTerrainLayer layer = WallWithGap();
        LocalFreeMover mover = Party(layer, Rules(), 1);
        Assert.IsTrue(mover.MoveLeaderTo(1500, 400));
        Assert.IsTrue(mover.LastOrderReachesTarget);
        double lowest = 0;
        Run(mover, m =>
        {
            Assert.IsTrue(m.IsPassable(m.Leader.X, m.Leader.Y), "Командир не заходит в стену.");
            lowest = Math.Max(lowest, m.Leader.Y);
        });
        Assert.AreEqual(1500, mover.Leader.X, 1);
        Assert.AreEqual(400, mover.Leader.Y, 1);
        Assert.Greater(lowest, 790, "Путь ведёт в обход, через проход внизу.");
    }

    [Test]
    public void ClickIntoTheWall_GoesToTheNearestReachablePoint()
    {
        LocalFreeMover mover = Party(WallWithGap(), Rules(), 1);
        Assert.IsTrue(mover.MoveLeaderTo(925, 300));
        Assert.IsFalse(mover.LastOrderReachesTarget);
        Run(mover);
        Assert.IsTrue(mover.IsPassable(mover.Leader.X, mover.Leader.Y));
        Assert.Less(mover.Leader.X, 925, "Командир встал у стены со своей стороны.");
        Assert.Greater(mover.Leader.X, 820);
    }

    [Test]
    public void Time_IsTheWalkedDistance_LikeOnTheWorldMap()
    {
        WorldMapTerrainLayer layer = new WorldMapTerrainLayer(new WorldMapHexGrid(Width, Height, 60));
        WorldMapMovementRules rules = Rules();
        LocalFreeMover mover = Party(layer, rules, 1);
        Assert.IsTrue(mover.MoveLeaderTo(940, 400));
        double hours = 0, hexes = 0;
        for (int i = 0; i < 2000 && !mover.IsIdle; i++)
        {
            hexes += mover.Tick(1 / 60.0, out double step);
            hours += step;
        }
        double expectedHexes = 640 / layer.Grid.HexWidth;
        Assert.AreEqual(expectedHexes, hexes, 0.05);
        Assert.AreEqual(expectedHexes * rules.TravelHoursPerHex, hours, 1e-3);
    }

    [Test]
    public void Speed_ComesFromTheLocationRules()
    {
        WorldMapTerrainLayer layer = new WorldMapTerrainLayer(new WorldMapHexGrid(Width, Height, 60));
        WorldMapMovementRules rules = Rules();
        LocalFreeMover mover = Party(layer, rules, 1);
        mover.MoveLeaderTo(1800, 400);
        mover.Tick(1.0, out _);
        Assert.AreEqual(300 + rules.HeroRunSpeedHexesPerSecond * layer.Grid.HexWidth, mover.Leader.X, 1);
    }

    [Test]
    public void Followers_KeepDistance_AndNeverCutThroughWalls()
    {
        LocalFreeMover mover = Party(WallWithGap(), Rules(), 4);
        Assert.IsTrue(mover.MoveLeaderTo(1500, 400));
        Run(mover, m =>
        {
            foreach (LocalFreeMover.Member member in m.Members)
                Assert.IsTrue(m.IsPassable(member.X, member.Y), member.Id + " в стене.");
        });
        Assert.IsTrue(mover.IsGathered);
        for (int i = 0; i < mover.Members.Count; i++)
        {
            for (int j = i + 1; j < mover.Members.Count; j++)
            {
                double dx = mover.Members[i].X - mover.Members[j].X, dy = mover.Members[i].Y - mover.Members[j].Y;
                Assert.Greater(Math.Sqrt(dx * dx + dy * dy), mover.Spacing * 0.5, "Спутники не стоят друг в друге.");
            }
        }
    }

    // Длинный путь (след много раз укорачивается), затем короткий шаг:
    // спутники всё равно на своих местах позади, а не в одной точке.
    [Test]
    public void LongWalkThenShortStep_FollowersStayApart()
    {
        LocalFreeMover mover = Party(new WorldMapTerrainLayer(new WorldMapHexGrid(Width, Height, 60)), Rules(), 4);
        mover.MoveLeaderTo(1800, 400);
        Run(mover);
        mover.MoveLeaderTo(1800, 700);
        Run(mover);
        mover.MoveLeaderTo(1700, 650);
        Run(mover);
        Assert.IsTrue(mover.IsGathered);
        for (int i = 1; i < mover.Members.Count; i++)
        {
            double dx = mover.Members[i].X - mover.Members[i - 1].X, dy = mover.Members[i].Y - mover.Members[i - 1].Y;
            Assert.Greater(Math.Sqrt(dx * dx + dy * dy), mover.Spacing * 0.5, "Спутник " + i + " отстаёт на своё место.");
        }
    }

    [Test]
    public void NewOrderWhileWalking_StartsFromTheCurrentPoint()
    {
        LocalFreeMover mover = Party(new WorldMapTerrainLayer(new WorldMapHexGrid(Width, Height, 60)), Rules(), 2);
        mover.MoveLeaderTo(1500, 400);
        for (int i = 0; i < 20; i++) mover.Tick(1 / 60.0, out _);
        double x = mover.Leader.X;
        Assert.Greater(x, 300);
        // Зажатая кнопка: цель меняется каждый кадр — без рывков назад.
        mover.MoveLeaderTo(x + 200, 600);
        mover.Tick(1 / 60.0, out _);
        Assert.Greater(mover.Leader.X, x - 1);
        Assert.Greater(mover.Leader.Y, 400);
    }
}
