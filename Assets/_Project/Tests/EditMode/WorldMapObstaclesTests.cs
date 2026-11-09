using System.Collections.Generic;
using NUnit.Framework;

// 12У-H: путь героя и групп обходит основания построек и предметов мест.
public sealed class WorldMapObstaclesTests
{
    [TearDown]
    public void TearDown() => WorldMapObstacles.Clear();

    private static bool Crosses(List<MapPointData> route)
    {
        for (int i = 1; i < route.Count; i++)
            for (int s = 0; s <= 50; s++)
            {
                float t = s / 50f;
                float x = route[i - 1].XPercent + (route[i].XPercent - route[i - 1].XPercent) * t;
                float y = route[i - 1].YPercent + (route[i].YPercent - route[i - 1].YPercent) * t;
                if (WorldMapObstacles.At(x, y, 0f) >= 0)
                    return true;
            }
        return false;
    }

    [Test]
    public void Path_GoesAroundBuildings_AndStopsBesideOne()
    {
        WorldMapObstacles.Set(new[]
        {
            new WorldMapObstacle(10f, 10f, 12f, 11f),
            new WorldMapObstacle(12.05f, 10.2f, 13f, 11.5f),
            new WorldMapObstacle(20f, 5f, 20.4f, 5.3f),
        });
        List<MapPointData> straight = WorldMapObstacles.Avoid(new List<MapPointData> { new MapPointData(5f, 10.5f), new MapPointData(17f, 10.5f) }, (x, y) => true);
        Assert.IsFalse(Crosses(straight), "Путь сквозь ряд домов обходит их.");
        Assert.AreEqual(17f, straight[straight.Count - 1].XPercent, 1e-4f, "Цель прежняя.");

        List<MapPointData> diagonal = WorldMapObstacles.Avoid(new List<MapPointData> { new MapPointData(9f, 9f), new MapPointData(14f, 12.5f) }, (x, y) => true);
        Assert.IsFalse(Crosses(diagonal), "Наискось — тоже в обход.");

        List<MapPointData> toPost = WorldMapObstacles.Avoid(new List<MapPointData> { new MapPointData(15f, 5f), new MapPointData(20.2f, 5.15f) }, (x, y) => true);
        MapPointData end = toPost[toPost.Count - 1];
        Assert.Less(WorldMapObstacles.At(end.XPercent, end.YPercent, 0f), 0, "Цель на столбе — герой встаёт рядом, не на нём.");

        List<MapPointData> clear = new List<MapPointData> { new MapPointData(0f, 0f), new MapPointData(5f, 0f) };
        Assert.AreSame(clear, WorldMapObstacles.Avoid(clear, (x, y) => true), "Путь без препятствий не меняется.");
    }
}
