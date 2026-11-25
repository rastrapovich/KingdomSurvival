using NUnit.Framework;

// 12Ф-3 «Дороги»: опорные точки → путь по местности, мост через реку,
// прилипание к деревням и дорогам, «Соединить деревни».
public sealed class AuthoredWorldMapRoadsTests
{
    private static AuthoredWorldMapData Map(int columns = 40, int rows = 30)
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(columns, rows);
        data.Villages.Clear();
        return data;
    }

    private static AuthoredPoint At(AuthoredWorldMapData data, int column, int row)
    {
        data.Grid.CellCenter(new WorldMapHexCell(column, row), out double x, out double y);
        return new AuthoredPoint(x, y);
    }

    private static AuthoredRoad Road(AuthoredWorldMapData data, params AuthoredPoint[] points)
    {
        AuthoredRoad road = new AuthoredRoad();
        road.Points.AddRange(points);
        data.Roads.Add(road);
        return road;
    }

    private static void Paint(AuthoredWorldMapData data, int column, int row, WorldMapGameplayTerrainType type) =>
        data.Terrain[data.Grid.IndexOf(new WorldMapHexCell(column, row))] = (byte)type;

    [Test]
    public void Road_GoesThroughGap_AndMarksRoadCells()
    {
        AuthoredWorldMapData data = Map();
        // Стена скал по колонке 20 с проходом в рядах 22–24.
        for (int row = 0; row < data.Grid.Rows; row++)
            if (row < 22 || row > 24)
                Paint(data, 20, row, WorldMapGameplayTerrainType.Cliffs);
        Road(data, At(data, 10, 10), At(data, 30, 10));

        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        AuthoredWorldMapLayout.RoadInfo info = layout.Roads[0];
        Assert.IsFalse(info.Broken);
        WorldMapHexGrid g = data.Grid;
        Assert.AreEqual(g.IndexOf(new WorldMapHexCell(10, 10)), info.Cells[0]);
        Assert.AreEqual(g.IndexOf(new WorldMapHexCell(30, 10)), info.Cells[info.Cells.Count - 1]);
        bool throughGap = false;
        foreach (int cell in info.Cells)
        {
            Assert.AreNotEqual((byte)WorldMapGameplayTerrainType.Cliffs, data.Terrain[cell], "Не через скалы.");
            Assert.AreEqual((byte)WorldMapGameplayTerrainType.Road, layout.Terrain[cell]);
            WorldMapHexCell c = g.CellAt(cell);
            throughGap |= c.Column == 20 && c.Row >= 22 && c.Row <= 24;
        }
        Assert.IsTrue(throughGap, "Через проход.");
    }

    [Test]
    public void Straightness_DecidesWhetherToCutThroughForest()
    {
        AuthoredWorldMapData data = Map();
        AuthoredPoint center = At(data, 20, 14);
        foreach ((int index, float _) in AuthoredWorldMapEditing.CellsInBrush(data, center.X, center.Y, 3f, 0f))
            data.Terrain[index] = (byte)WorldMapGameplayTerrainType.Swamp;
        AuthoredRoad road = Road(data, At(data, 12, 14), At(data, 28, 14));

        road.Straightness = 0f;
        int curvy = CountSwamp(data);
        road.Straightness = 1f;
        int straight = CountSwamp(data);
        Assert.AreEqual(0, curvy, "По рельефу — в обход болота.");
        Assert.Greater(straight, 2, "Прямо — через болото.");
    }

    private static int CountSwamp(AuthoredWorldMapData data)
    {
        int n = 0;
        foreach (int cell in AuthoredWorldMapLayout.Compute(data).Roads[0].Cells)
            if (data.Terrain[cell] == (byte)WorldMapGameplayTerrainType.Swamp)
                n++;
        return n;
    }

    [Test]
    public void RiverCrossing_BuildsBridge_OrDetoursToFord()
    {
        AuthoredWorldMapData data = Map();
        AuthoredPoint top = At(data, 20, 0);
        AuthoredRiver river = new AuthoredRiver { StartWidth = 1f, EndWidth = 1f };
        river.Points.Add(new AuthoredPoint(top.X, -data.Grid.RowStep));
        river.Points.Add(new AuthoredPoint(top.X, data.Grid.CanvasHeight + data.Grid.RowStep));
        data.Rivers.Add(river);
        AuthoredRoad road = Road(data, At(data, 10, 10), At(data, 30, 10));

        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        Assert.AreEqual(1, layout.Roads[0].Crossings.Count, "Мост сам.");
        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 1);
        Assert.AreEqual(2, map.BridgeSpans.Count);
        Assert.Greater(map.Bridges.Count, 0);
        Assert.AreEqual(1, map.Roads.Count);
        WorldMapCrossings crossings = new WorldMapCrossings(map.CreateTerrainLayer(), map.ToDefinitionData().BridgeSpans);
        Assert.AreEqual(1, crossings.Count, "Навигация видит мост.");

        road.AvoidWater = true;
        layout = AuthoredWorldMapLayout.Compute(data);
        Assert.IsTrue(layout.Roads[0].Broken, "Без моста и брода — пути нет.");
        Assert.IsTrue(layout.Problems.Exists(p => p.IsError && p.Text.Contains("без воды")));

        data.Fords.Add(At(data, 20, 20));
        layout = AuthoredWorldMapLayout.Compute(data);
        Assert.IsFalse(layout.Roads[0].Broken, "Через брод.");
        Assert.AreEqual(0, layout.Roads[0].Crossings.Count);
    }

    [Test]
    public void Snap_PrefersVillage_ThenOtherRoad()
    {
        AuthoredWorldMapData data = Map();
        data.Villages.Add(new AuthoredVillage { Column = 15, Row = 10 });
        AuthoredPoint v = At(data, 15, 10);
        double x = v.X + data.Grid.HexWidth * 1.2, y = v.Y;
        Assert.IsTrue(AuthoredWorldMapRoads.Snap(data, null, -1, 2f, ref x, ref y));
        Assert.AreEqual(v.X, x, 0.01);

        Road(data, At(data, 5, 20), At(data, 35, 20));
        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        AuthoredPoint near = At(data, 25, 21);
        x = near.X;
        y = near.Y + 3;
        Assert.IsTrue(AuthoredWorldMapRoads.Snap(data, layout, -1, 2f, ref x, ref y));
        int cell = data.Grid.IndexOf(data.Grid.CellAtPixel(x, y));
        Assert.IsTrue(layout.RoadSet.Contains(cell), "Прилипла к клетке дороги — перекрёсток.");
    }

    [Test]
    public void ConnectVillages_LinksCampAndVillages_Once()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(50, 34);
        data.Villages.Add(new AuthoredVillage { Name = "Третья", Column = 40, Row = 28 });
        WorldMapMovementRules rules = new WorldMapMovementRules();

        int added = AuthoredWorldMapRoads.ConnectVillages(data, 0f, 0, false, 0.3f);
        Assert.AreEqual(3, added, "Лагерь и три деревни — три дороги остова.");
        Assert.IsFalse(AuthoredWorldMapValidation.Check(data, rules).Exists(p => p.Text.Contains("не ведёт дорога")));
        Assert.AreEqual(0, AuthoredWorldMapRoads.ConnectVillages(data, 0f, 0, false, 0.3f), "Повторно — ничего.");

        Assert.AreEqual(2, AuthoredWorldMapRoads.ConnectVillages(data, 0f, 2, false, 0.3f), "Два выхода к краю.");
        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        bool left = false;
        foreach (int cell in layout.RoadSet)
            left |= data.Grid.CellAt(cell).Column == 0;
        Assert.IsTrue(left, "Дорога выходит к левому краю.");
    }

    [Test]
    public void RoadMap_StartsCampaign_TrailKindKept()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(50, 30);
        AuthoredWorldMapRoads.ConnectVillages(data, 0.5f, 1, false, 0.3f);
        AuthoredRoad trail = Road(data, At(data, 5, 5), At(data, 15, 8));
        trail.Trail = true;

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 4);
        Assert.IsTrue(map.Roads.Exists(r => r.Kind == GeneratedMapLine.TrailKind));
        Assert.IsTrue(map.Roads.Exists(r => r.Kind == GeneratedMapLine.RoadKind));
        GameState state = new CampaignSetup { WorldSeed = 4, MapHexesAcross = map.HexesAcross }.CreateCampaign(generatedMap: map);
        Assert.AreEqual(map.Roads.Count, state.GeneratedMap.Roads.Count);
        Assert.Greater(map.ToDefinitionData().RoadLines.Count, 0, "Дороги — в определении карты для навигации.");
    }
}
