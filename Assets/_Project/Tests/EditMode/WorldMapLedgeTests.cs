using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12С: уступы генератора карты свободной игры — дуги скального обрыва.
// Снизу не зайти, сверху не спрыгнуть; обход — у концов и в просвете.
public sealed class WorldMapLedgeTests
{
    private static readonly int[] Seeds =
    {
        3, 7, 11, 42, 101, 202, 303, 404, 505, 606, 707, 808, 909, 1001, 1102, 1203, 1304, 1405, 1506, 20261030
    };

    private static GeneratedWorldMap Generate(int seed) =>
        WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);

    private static WorldMapGameplayTerrainType TerrainAt(GeneratedWorldMap map, int cell) =>
        map.CreateTerrainLayer().Get(map.CreateGrid().CellAt(cell));

    [Test]
    public void MostSeeds_HaveALedge_AndGenerationIsDeterministic()
    {
        int withLedge = 0;
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap a = Generate(seed);
            GeneratedWorldMap b = Generate(seed);
            Assert.That(b.TerrainCells, Is.EqualTo(a.TerrainCells), "seed " + seed + ": разметка детерминирована.");
            Assert.That(b.Ledges.Count, Is.EqualTo(a.Ledges.Count));
            for (int i = 0; i < a.Ledges.Count; i++)
                Assert.That(b.Ledges[i].Cells, Is.EqualTo(a.Ledges[i].Cells), "seed " + seed + ": клетки уступа.");
            if (a.Ledges.Count > 0)
                withLedge++;
        }
        Assert.That(withLedge, Is.GreaterThanOrEqualTo((int)Math.Ceiling(Seeds.Length * 0.8)),
            "Уступ есть не меньше чем на 80 % seed: " + withLedge + " из " + Seeds.Length);
    }

    [Test]
    public void LedgeCells_AreAnUnbrokenChainOfCliffs_AndNoStrayCliffsExist()
    {
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Generate(seed);
            WorldMapHexGrid grid = map.CreateGrid();
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            HashSet<int> ledgeCells = new HashSet<int>(map.Ledges.SelectMany(l => l.Cells));
            for (int i = 0; i < grid.CellCount; i++)
            {
                bool cliff = layer.Get(grid.CellAt(i)) == WorldMapGameplayTerrainType.Cliffs;
                Assert.That(cliff, Is.EqualTo(ledgeCells.Contains(i)), "seed " + seed + ", клетка " + i + ": скалы — только на уступах.");
            }
            foreach (GeneratedMapLedge ledge in map.Ledges)
            {
                Assert.That(ledge.Cells.Count, Is.GreaterThanOrEqualTo(3));
                for (int k = 1; k < ledge.Cells.Count; k++)
                    Assert.That(grid.CellAt(ledge.Cells[k]).DistanceTo(grid.CellAt(ledge.Cells[k - 1])), Is.EqualTo(1),
                        "seed " + seed + ": соседние клетки цепочки соседние (путь не проскочит по диагонали).");
            }
        }
    }

    [Test]
    public void LedgeEnds_ArePassable()
    {
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Generate(seed);
            WorldMapHexGrid grid = map.CreateGrid();
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            foreach (GeneratedMapLedge ledge in map.Ledges)
            {
                foreach (MapPointData end in new[] { ledge.Points[0], ledge.Points[ledge.Points.Count - 1] })
                {
                    WorldMapHexCell cell = grid.CellAtPercent(end.XPercent, end.YPercent);
                    Assert.That(layer.Get(cell), Is.Not.EqualTo(WorldMapGameplayTerrainType.Cliffs), "seed " + seed + ": конец уступа — склон.");
                    foreach (int cliff in ledge.Cells)
                        Assert.That(grid.CellAt(cliff).DistanceTo(cell), Is.GreaterThanOrEqualTo(1));
                }
            }
        }
    }

    [Test]
    public void AllLandReachableWithoutLedges_StaysReachableWithThem()
    {
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Generate(seed);
            WorldMapHexGrid grid = map.CreateGrid();
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            int start = grid.IndexOf(grid.CellAtPercent(map.StartXPercent, map.StartYPercent));
            HashSet<int> withLedges = Flood(grid, layer, start, true);
            HashSet<int> withoutLedges = Flood(grid, layer, start, false);
            Assert.That(withLedges.Count, Is.EqualTo(withoutLedges.Count - map.Ledges.Sum(l => l.Cells.Count)),
                "seed " + seed + ": уступ ничего не отрезал.");
        }
    }

    private static HashSet<int> Flood(WorldMapHexGrid grid, WorldMapTerrainLayer layer, int start, bool cliffsBlock)
    {
        HashSet<int> seen = new HashSet<int> { start };
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            WorldMapHexCell cell = grid.CellAt(queue.Dequeue());
            for (int d = 0; d < 6; d++)
            {
                WorldMapHexCell next = cell.Neighbor(d);
                if (!grid.IsInside(next))
                    continue;
                int index = grid.IndexOf(next);
                WorldMapGameplayTerrainType t = layer.Get(next);
                if (seen.Contains(index) || t == WorldMapGameplayTerrainType.Water || (cliffsBlock && t == WorldMapGameplayTerrainType.Cliffs))
                    continue;
                seen.Add(index);
                queue.Enqueue(index);
            }
        }
        return seen;
    }

    [Test]
    public void PlateauSide_IsTheHigherSide_AndFacesTheCamera()
    {
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Generate(seed);
            WorldMapHexGrid grid = map.CreateGrid();
            foreach (GeneratedMapLedge ledge in map.Ledges)
            {
                Assert.That(ledge.UpperSide, Is.GreaterThan(0f));
                double high = 0, low = 0, northness = 0;
                int n = 0;
                for (int k = 0; k + 1 < ledge.Points.Count; k++)
                {
                    Normal(grid, ledge.Points[k], ledge.Points[k + 1], out double mx, out double my, out double nx, out double ny);
                    high += map.Elevation[grid.IndexOf(grid.CellAtPixel(mx + nx * 1.5 * grid.HexWidth, my + ny * 1.5 * grid.HexWidth))];
                    low += map.Elevation[grid.IndexOf(grid.CellAtPixel(mx - nx * 1.5 * grid.HexWidth, my - ny * 1.5 * grid.HexWidth))];
                    northness += -ny;
                    n++;
                }
                Assert.That(high / n, Is.GreaterThan(low / n), "seed " + seed + ": плато выше.");
                Assert.That(northness / n, Is.GreaterThan(0.25), "seed " + seed + ": плато к северу — стенка к камере.");
            }
        }
    }

    // Нормаль слева по ходу отрезка (пиксели, y вниз) и середина отрезка.
    private static void Normal(WorldMapHexGrid grid, MapPointData a, MapPointData b, out double mx, out double my, out double nx, out double ny)
    {
        double ax = grid.PercentToPixelX(a.XPercent), ay = grid.PercentToPixelY(a.YPercent);
        double bx = grid.PercentToPixelX(b.XPercent), by = grid.PercentToPixelY(b.YPercent);
        double tx = bx - ax, ty = by - ay, length = Math.Sqrt(tx * tx + ty * ty);
        mx = (ax + bx) * 0.5;
        my = (ay + by) * 0.5;
        nx = ty / length;
        ny = -tx / length;
    }

    [Test]
    public void PathAcrossTheMiddleOfALedge_GoesAround_InBothDirections_WithoutTouchingCliffs()
    {
        int tested = 0;
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Generate(seed);
            WorldMapHexGrid grid = map.CreateGrid();
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            double hw = grid.HexWidth;
            foreach (GeneratedMapLedge ledge in map.Ledges)
            {
                if (ledge.Cells.Count < 6)
                    continue;
                int k = ledge.Cells.Count / 2;
                grid.CellCenter(grid.CellAt(ledge.Cells[k]), out double cx, out double cy);
                grid.CellCenter(grid.CellAt(ledge.Cells[k - 1]), out double px, out double py);
                grid.CellCenter(grid.CellAt(ledge.Cells[k + 1]), out double qx, out double qy);
                double tx = qx - px, ty = qy - py, tl = Math.Sqrt(tx * tx + ty * ty);
                double nx = ty / tl, ny = -tx / tl; // к плато

                const double offset = 2.5;
                double lx = cx - nx * offset * hw, ly = cy - ny * offset * hw; // внизу
                double ux = cx + nx * offset * hw, uy = cy + ny * offset * hw; // на плато
                if (!Walkable(grid, layer, lx, ly) || !Walkable(grid, layer, ux, uy))
                    continue;

                foreach (bool reverse in new[] { false, true })
                {
                    double sx = reverse ? ux : lx, sy = reverse ? uy : ly;
                    double ex = reverse ? lx : ux, ey = reverse ? ly : uy;
                    List<WorldMapPathfinder.PathPoint> path = WorldMapPathfinder.FindPath(layer, null, sx, sy, ex, ey, out bool reached);
                    string where = "seed " + seed + (reverse ? ", сверху вниз" : ", снизу вверх");
                    Assert.That(reached, Is.True, where + ": путь вокруг уступа есть.");
                    double length = 0;
                    for (int i = 1; i < path.Count; i++)
                    {
                        double ax = path[i - 1].X, ay = path[i - 1].Y, bx = path[i].X, by = path[i].Y;
                        double segment = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                        length += segment;
                        int steps = Math.Max(1, (int)Math.Ceiling(segment / (0.2 * hw)));
                        for (int s = 0; s <= steps; s++)
                        {
                            double f = s / (double)steps;
                            double sampleX = ax + (bx - ax) * f, sampleY = ay + (by - ay) * f;
                            WorldMapHexCell cell = grid.CellAtPixel(sampleX, sampleY);
                            if (layer.Get(cell) != WorldMapGameplayTerrainType.Cliffs)
                                continue;
                            // Сглаживание пути может лишь задеть край клетки-скалы
                            // (вписанный радиус клетки — 0,5 её ширины); вглубь — нельзя.
                            grid.CellCenter(cell, out double ccx, out double ccy);
                            double inside = Math.Sqrt((sampleX - ccx) * (sampleX - ccx) + (sampleY - ccy) * (sampleY - ccy)) / hw;
                            Assert.That(inside, Is.GreaterThan(0.4), where + ": путь не заходит вглубь скалы.");
                        }
                    }
                    double straight = 2.0 * offset * hw;
                    Assert.That(length, Is.GreaterThan(straight * 1.15), where + ": обход длиннее прямой.");
                    tested++;
                }
            }
        }
        Assert.That(tested, Is.GreaterThanOrEqualTo(6), "Проверено путей: " + tested);
    }

    private static bool Walkable(WorldMapHexGrid grid, WorldMapTerrainLayer layer, double x, double y)
    {
        if (x < 0 || y < 0 || x > grid.CanvasWidth || y > grid.CanvasHeight)
            return false;
        WorldMapGameplayTerrainType t = layer.Get(grid.CellAtPixel(x, y));
        return t != WorldMapGameplayTerrainType.Water && t != WorldMapGameplayTerrainType.Cliffs;
    }

    [Test]
    public void Ledges_SurviveSaveFormat()
    {
        GeneratedWorldMap map = Generate(7);
        Assert.That(map.Ledges.Count, Is.GreaterThan(0));
        GeneratedWorldMap loaded = JsonUtility.FromJson<GeneratedWorldMap>(JsonUtility.ToJson(map));
        Assert.That(loaded.Ledges.Count, Is.EqualTo(map.Ledges.Count));
        for (int i = 0; i < map.Ledges.Count; i++)
        {
            Assert.That(loaded.Ledges[i].Cells, Is.EqualTo(map.Ledges[i].Cells));
            Assert.That(loaded.Ledges[i].Points.Select(p => p.XPercent), Is.EqualTo(map.Ledges[i].Points.Select(p => p.XPercent)));
            Assert.That(loaded.Ledges[i].Points.Select(p => p.YPercent), Is.EqualTo(map.Ledges[i].Points.Select(p => p.YPercent)));
            Assert.That(loaded.Ledges[i].UpperSide, Is.EqualTo(map.Ledges[i].UpperSide));
        }
    }

    [Test]
    public void ZeroLedgeCount_MakesNoLedgesAndNoCliffs()
    {
        WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        settings.LedgeCount = 0;
        settings.RegionKind = WorldMapRegionKind.ForestLake;
        GeneratedWorldMap map = WorldMapGenerator.Generate(settings, 7);
        Assert.That(map.Ledges, Is.Empty);
        WorldMapHexGrid grid = map.CreateGrid();
        WorldMapTerrainLayer layer = map.CreateTerrainLayer();
        for (int i = 0; i < grid.CellCount; i++)
            Assert.That(layer.Get(grid.CellAt(i)), Is.Not.EqualTo(WorldMapGameplayTerrainType.Cliffs));
    }
}
