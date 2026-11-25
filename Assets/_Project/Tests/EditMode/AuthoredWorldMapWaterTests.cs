using NUnit.Framework;

// 12Ф-2 «Вода и уступы»: реки линией с прорезкой русла и притоками, озеро до
// уровня, брод, уступ линией — и их сборка в карту партии.
public sealed class AuthoredWorldMapWaterTests
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

    // Вертикальная река по колонке column через всю карту.
    private static AuthoredRiver Vertical(AuthoredWorldMapData data, int column)
    {
        AuthoredRiver river = new AuthoredRiver { StartWidth = 1f, EndWidth = 1f };
        // Через всю карту, концы — за краем (клетки края тоже под рекой).
        AuthoredPoint top = At(data, column, 0);
        river.Points.Add(new AuthoredPoint(top.X, -data.Grid.RowStep));
        river.Points.Add(new AuthoredPoint(top.X, data.Grid.CanvasHeight + data.Grid.RowStep));
        data.Rivers.Add(river);
        return river;
    }

    [Test]
    public void River_BecomesWater_AndBedNeverRisesDownstream()
    {
        AuthoredWorldMapData data = Map();
        WorldMapHexGrid g = data.Grid;
        // Земля под рекой: низина посередине, подъём ниже по течению.
        for (int row = 0; row < g.Rows; row++)
            for (int c = 0; c < g.Columns; c++)
                data.Heights[g.IndexOf(new WorldMapHexCell(c, row))] = row < 15 ? 0.4f - row * 0.02f : 0.5f;
        Vertical(data, 20);

        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        Assert.Greater(layout.Rivers[0].Cells.Count, g.Rows - 2);
        float previous = float.MaxValue;
        foreach (int cell in layout.Rivers[0].Cells)
        {
            Assert.AreEqual((byte)WorldMapGameplayTerrainType.Water, layout.Terrain[cell]);
            if (g.CellAt(cell).Column == 20)
            {
                Assert.LessOrEqual(layout.Heights[cell], previous + 1e-4f, "Русло не поднимается вниз по течению.");
                previous = layout.Heights[cell];
            }
        }
        Assert.IsTrue(layout.Problems.Exists(p => p.Text.Contains("в гору")), "Подъём под рекой — предупреждение.");
        Assert.AreEqual(0.5f, data.HeightAt(g.IndexOf(new WorldMapHexCell(20, 25))), 1e-4f, "Рельеф автора не испорчен.");
    }

    [Test]
    public void Tributary_EndSnapsToRiver()
    {
        AuthoredWorldMapData data = Map();
        Vertical(data, 20);
        AuthoredPoint near = At(data, 21, 10);
        double x = near.X + 3, y = near.Y;
        Assert.IsTrue(AuthoredWorldMapLines.SnapToRiver(data, -1, ref x, ref y));
        AuthoredPoint onRiver = At(data, 20, 10);
        Assert.AreEqual(onRiver.X, x, data.Grid.HexWidth * 0.1);
    }

    [Test]
    public void Lake_FillsBasin_AndRefusesToFloodPlain()
    {
        AuthoredWorldMapData data = Map();
        WorldMapHexGrid g = data.Grid;
        for (int i = 0; i < g.CellCount; i++)
            data.Heights[i] = 0.5f;
        int center = g.IndexOf(new WorldMapHexCell(10, 10));
        foreach ((int index, float _) in AuthoredWorldMapEditing.CellsInBrush(data, At(data, 10, 10).X, At(data, 10, 10).Y, 2.5f, 0f))
            data.Heights[index] = 0.2f;
        int filled = AuthoredWorldMapLines.FillLake(data, center, 0.05f, 500);
        Assert.Greater(filled, 10);
        Assert.AreEqual(WorldMapGameplayTerrainType.Water, data.TerrainAt(center));
        Assert.AreEqual(WorldMapGameplayTerrainType.OpenGround, data.TerrainAt(g.IndexOf(new WorldMapHexCell(30, 20))), "Берег сухой.");

        int plain = g.IndexOf(new WorldMapHexCell(30, 20));
        Assert.AreEqual(-1, AuthoredWorldMapLines.FillLake(data, plain, 0.05f, 300), "Ровная равнина не заливается.");
        Assert.AreEqual(WorldMapGameplayTerrainType.OpenGround, data.TerrainAt(plain));

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 1);
        Assert.Contains(center, map.LakeCells);
        Assert.Greater(AuthoredWorldMapLines.Dry(data, center), 10);
        Assert.AreEqual(WorldMapGameplayTerrainType.OpenGround, data.TerrainAt(center));
    }

    [Test]
    public void Ford_ConnectsBanks_ForNavigationAndValidation()
    {
        AuthoredWorldMapData data = Map();
        Vertical(data, 20);
        data.StartColumn = 10;
        data.StartRow = 14;
        data.Villages.Add(new AuthoredVillage { Name = "За рекой", Column = 30, Row = 14, Size = 1 });
        WorldMapMovementRules rules = new WorldMapMovementRules();

        Assert.IsTrue(AuthoredWorldMapValidation.Check(data, rules).Exists(p => p.IsError && p.Text.Contains("За рекой")),
            "Без брода деревню за рекой не достать.");

        data.Fords.Add(At(data, 20, 14));
        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        Assert.IsTrue(layout.Fords[0].Ok);
        Assert.IsFalse(AuthoredWorldMapValidation.Check(data, rules).Exists(p => p.IsError), "С бродом дойти можно.");

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 3);
        Assert.AreEqual(2, map.BridgeSpans.Count, "Пролёт брода «берег → берег».");
        Assert.AreEqual(1, map.Rivers.Count);
        Assert.Greater(map.RiverCells.Count, 10);
        WorldMapCrossings crossings = new WorldMapCrossings(map.CreateTerrainLayer(), map.ToDefinitionData().BridgeSpans);
        Assert.AreEqual(1, crossings.Count, "Навигация видит переправу.");
    }

    [Test]
    public void Ledge_MakesCliffs_WithPassableEnds()
    {
        AuthoredWorldMapData data = Map();
        AuthoredLedge ledge = new AuthoredLedge { UpperLeft = false, Height = 1.4f };
        ledge.Points.Add(At(data, 5, 10));
        ledge.Points.Add(At(data, 25, 10));
        data.Ledges.Add(ledge);

        AuthoredWorldMapLayout layout = AuthoredWorldMapLayout.Compute(data);
        WorldMapHexGrid g = data.Grid;
        Assert.AreEqual((byte)WorldMapGameplayTerrainType.Cliffs, layout.Terrain[g.IndexOf(new WorldMapHexCell(15, 10))]);
        Assert.AreNotEqual((byte)WorldMapGameplayTerrainType.Cliffs, layout.Terrain[g.IndexOf(new WorldMapHexCell(5, 10))], "Конец — склон.");

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 3);
        Assert.AreEqual(1, map.Ledges.Count);
        Assert.AreEqual(-1f, map.Ledges[0].UpperSide);
        Assert.AreEqual(1.4f, map.Ledges[0].HeightScale, 1e-4f);
        Assert.Greater(map.Ledges[0].Cells.Count, 10);
    }

    [Test]
    public void Resize_MovesLinesWithTheMap()
    {
        AuthoredWorldMapData data = Map();
        AuthoredRiver river = Vertical(data, 20);
        data.Fords.Add(At(data, 20, 5));
        AuthoredWorldMapEditing.Resize(data, 4, 0, 2, 0);
        AuthoredPoint expected = At(data, 24, 2);
        Assert.AreEqual(expected.X, river.Points[0].X, 0.01f);
        Assert.AreEqual((float)data.Grid.RowStep, river.Points[0].Y, 0.01f, "Конец за верхним краем сдвинулся на два ряда.");
        Assert.AreEqual(At(data, 24, 7).Y, data.Fords[0].Y, 0.01f);
    }

    [Test]
    public void WaterMap_StartsCampaign_AndRiversSurviveSave()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(50, 30);
        AuthoredRiver river = new AuthoredRiver();
        river.Points.Add(At(data, 2, 2));
        river.Points.Add(At(data, 12, 12));
        river.Points.Add(At(data, 14, 28));
        data.Rivers.Add(river);

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 9);
        GameState state = new CampaignSetup { WorldSeed = 9, MapHexesAcross = map.HexesAcross }.CreateCampaign(generatedMap: map);
        Assert.AreEqual(1, state.GeneratedMap.Rivers.Count);
        GeneratedWorldMap loaded = UnityEngine.JsonUtility.FromJson<GeneratedWorldMap>(UnityEngine.JsonUtility.ToJson(map));
        Assert.AreEqual(map.RiverCells.Count, loaded.RiverCells.Count);
        Assert.Greater(loaded.Rivers[0].Points.Count, 3, "Линия сглажена.");
    }
}
