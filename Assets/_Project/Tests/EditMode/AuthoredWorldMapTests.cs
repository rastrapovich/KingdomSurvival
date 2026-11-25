using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;

// 12Ф-1 «Редактор карты»: авторская карта свободной игры — данные, кисти,
// изменение размера и сборка в карту партии.
public sealed class AuthoredWorldMapTests
{
    [Test]
    public void Grid_HasRequestedSize_AndHexSizeDoesNotDependOnIt()
    {
        AuthoredWorldMapData small = AuthoredWorldMapData.CreateDefault(30, 20);
        AuthoredWorldMapData big = AuthoredWorldMapData.CreateDefault(150, 90);
        Assert.AreEqual(30, small.Grid.HexesAcross);
        Assert.AreEqual(20, small.Grid.Rows, "Рядов — сколько задано.");
        Assert.AreEqual(90, big.Grid.Rows);
        Assert.AreEqual(small.Grid.HexRadius, big.Grid.HexRadius, 0.01f, "Клетка одного размера на любой карте.");
    }

    [Test]
    public void HeightBrush_RaisesInsideRadius_Only()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(40, 30);
        WorldMapHexGrid g = data.Grid;
        WorldMapHexCell center = new WorldMapHexCell(20, 14);
        g.CellCenter(center, out double x, out double y);
        for (int i = 0; i < 10; i++)
            AuthoredWorldMapEditing.ApplyHeight(data, AuthoredWorldMapEditing.HeightBrush.Raise, x, y, 3f, 1f, 0.5f);
        Assert.Greater(data.HeightAt(g.IndexOf(center)), 0.5f);
        Assert.AreEqual(0f, data.HeightAt(g.IndexOf(new WorldMapHexCell(2, 2))));

        // Выровнять к нулю — центр опускается.
        AuthoredWorldMapEditing.ApplyHeight(data, AuthoredWorldMapEditing.HeightBrush.Flatten, x, y, 3f, 1f, 0f, 0f);
        Assert.Less(data.HeightAt(g.IndexOf(center)), 0.1f);
    }

    [Test]
    public void Commit_RoundTripsHeightsAndTerrain()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(40, 30);
        data.Heights[123] = 0.5f;
        data.Terrain[200] = (byte)WorldMapGameplayTerrainType.Forest;
        data.Commit();
        data.Invalidate();
        Assert.AreEqual(0.5f, data.HeightAt(123), 0.001f);
        Assert.AreEqual(WorldMapGameplayTerrainType.Forest, data.TerrainAt(200));
    }

    [Test]
    public void Fill_ChangesConnectedAreaOnly()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(20, 12);
        WorldMapHexGrid g = data.Grid;
        // Стена воды по колонке 10 делит карту надвое.
        for (int row = 0; row < g.Rows; row++)
            data.Terrain[g.IndexOf(new WorldMapHexCell(10, row))] = (byte)WorldMapGameplayTerrainType.Water;
        AuthoredWorldMapEditing.Fill(data, g.IndexOf(new WorldMapHexCell(2, 5)), WorldMapGameplayTerrainType.Forest);
        Assert.AreEqual(WorldMapGameplayTerrainType.Forest, data.TerrainAt(g.IndexOf(new WorldMapHexCell(5, 3))));
        Assert.AreEqual(WorldMapGameplayTerrainType.OpenGround, data.TerrainAt(g.IndexOf(new WorldMapHexCell(15, 3))));
    }

    [Test]
    public void Resize_KeepsPaintedCells_AndStartAndVillages()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(30, 20);
        WorldMapHexGrid g = data.Grid;
        int marked = g.IndexOf(new WorldMapHexCell(5, 6));
        data.Terrain[marked] = (byte)WorldMapGameplayTerrainType.Swamp;
        data.Heights[marked] = 0.7f;
        data.Commit();
        int startColumn = data.StartColumn, startRow = data.StartRow;
        int villageColumn = data.Villages[0].Column;

        AuthoredWorldMapEditing.Resize(data, 10, 3, 3, 2);
        Assert.AreEqual(43, data.Columns);
        Assert.AreEqual(26, data.Rows, "Сверху ряды парами: 3 → 4.");
        int moved = data.Grid.IndexOf(new WorldMapHexCell(15, 10));
        Assert.AreEqual(WorldMapGameplayTerrainType.Swamp, data.TerrainAt(moved));
        Assert.AreEqual(0.7f, data.HeightAt(moved), 0.001f);
        Assert.AreEqual(startColumn + 10, data.StartColumn);
        Assert.AreEqual(startRow + 4, data.StartRow);
        Assert.AreEqual(villageColumn + 10, data.Villages[0].Column);
    }

    [Test]
    public void AutoRelief_TurnsOpenGroundIntoHillsAndMountains()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(20, 12);
        data.Heights[10] = data.HillsFrom + 0.01f;
        data.Heights[11] = data.MountainsFrom + 0.01f;
        data.Heights[12] = data.MountainsFrom + 0.01f;
        data.Terrain[12] = (byte)WorldMapGameplayTerrainType.Forest;
        Assert.AreEqual(WorldMapGameplayTerrainType.Hills, data.EffectiveTerrain(10));
        Assert.AreEqual(WorldMapGameplayTerrainType.Mountains, data.EffectiveTerrain(11));
        Assert.AreEqual(WorldMapGameplayTerrainType.Forest, data.EffectiveTerrain(12), "Нарисованное автором не меняется.");
        data.AutoReliefTerrain = false;
        Assert.AreEqual(WorldMapGameplayTerrainType.OpenGround, data.EffectiveTerrain(11));
    }

    [Test]
    public void Build_GivesPlayableCampaign_WithAuthorVillages()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(50, 30);
        data.Villages[0].Name = "Заречье";
        WorldMapHexGrid g = data.Grid;
        int lake = g.IndexOf(new WorldMapHexCell(3, 3));
        data.Terrain[lake] = (byte)WorldMapGameplayTerrainType.Water;
        data.Heights[lake + 1] = 0.9f;
        data.Commit();

        GeneratedWorldMap map = AuthoredWorldMapBuilder.Build(data, 77);
        Assert.IsTrue(map.IsValid);
        Assert.IsTrue(map.Authored);
        Assert.AreEqual(50, map.HexesAcross);
        Assert.Contains(lake, map.LakeCells);
        Assert.AreEqual(WorldMapGameplayTerrainType.Mountains, map.CreateTerrainLayer().GetAtIndex(lake + 1));
        Assert.IsTrue(map.Villages[0].IsStart, "Первая — стоянка отряда.");
        Assert.AreEqual(3, map.Villages.Count);

        GameState state = new CampaignSetup { WorldSeed = 77, MapHexesAcross = map.HexesAcross }.CreateCampaign(generatedMap: map);
        Assert.AreEqual(50, state.GeneratedMap.HexesAcross);
        Assert.IsTrue(state.Locations.Exists(l => l.IsSettlement && l.Name == "Заречье"), "Деревня автора — поселение с его названием.");

        // Высоты переживают сохранение партии (JsonUtility, как сохранение).
        string json = UnityEngine.JsonUtility.ToJson(map);
        GeneratedWorldMap loaded = UnityEngine.JsonUtility.FromJson<GeneratedWorldMap>(json);
        Assert.AreEqual(0.9f, loaded.AuthoredElevation()[lake + 1], 0.001f);
    }

    [Test]
    public void Validation_ReportsUnreachableVillage()
    {
        AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(30, 20);
        AuthoredVillage v = data.Villages[0];
        v.Name = "Остров";
        WorldMapHexGrid g = data.Grid;
        WorldMapHexCell c = new WorldMapHexCell(v.Column, v.Row);
        for (int dir = 0; dir < 6; dir++)
            data.Terrain[g.IndexOf(c.Neighbor(dir))] = (byte)WorldMapGameplayTerrainType.Water;
        List<AuthoredWorldMapValidation.Problem> problems = AuthoredWorldMapValidation.Check(data, new WorldMapMovementRules());
        Assert.IsTrue(problems.Exists(p => p.IsError && p.Text.Contains("Остров")));
    }

    [Test]
    public void FreePlayMap_ComesFromAuthoredAsset()
    {
        AuthoredWorldMapAsset asset = WorldMapVisualRuntime.LoadAuthoredMap();
        Assert.IsNotNull(asset, "Ассет авторской карты лежит в Resources.");
        GeneratedWorldMap map = WorldMapVisualRuntime.BuildFreePlayMap(5);
        Assert.IsTrue(map.IsValid);
        Assert.IsTrue(map.Authored, "Новая партия — на авторской карте, не на сгенерированной.");
        Assert.AreEqual(asset.Data.Columns, map.HexesAcross);
    }
}
