using System;
using System.Collections.Generic;
using NUnit.Framework;

// 22.11.2026: реку герой и группы мира переходят только по пролёту
// переправы — от берега до берега по оси моста, не наискось по воде и не
// вдоль русла. Без переправ (нет определения карты) путь прежний.
public class WorldMapBridgeRouteTests
{
    private static readonly int[] Seeds = { 3, 7, 11, 42, 20261030 };
    // Коридор вокруг оси пролёта, в клетках пути.
    private const double Corridor = 0.35;

    [SetUp]
    public void SetUp()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
        WorldMapNavigation.PreferProfitableRoutes = false;
        WorldMapObstacles.Clear();
        WorldMapBridges.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        WorldMapMovementRules.Current = WorldMapMovementRules.CreateDefault();
        WorldMapNavigation.ConfigureDefaultTerrain();
        WorldMapNavigation.PreferProfitableRoutes = false;
        WorldMapObstacles.Clear();
        WorldMapBridges.Clear();
    }

    [Test]
    public void CrossingTheRiver_GoesThroughBothSpanEnds_AndStaysInsideTheCorridor()
    {
        int checkedSpans = 0;
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = Configure(seed, out HashSet<int> river);
            WorldMapCrossings crossings = WorldMapNavigation.ActiveCrossings;
            if (crossings == null)
                continue; // на этом seed реки без переправ
            WorldMapHexGrid grid = WorldMapNavigation.Grid;

            for (int s = 0; s < crossings.Count; s++)
            {
                WorldMapCrossings.Span span = crossings.Spans[s];
                Axis(span, grid, out double ux, out double uy, out double length);
                foreach (bool profitable in new[] { false, true })
                {
                    List<MapPointData> path = PathAlongAxis(span, grid, ux, uy, 4.0, profitable);
                    string where = "seed " + seed + ", пролёт " + s + (profitable ? " (выгодный)" : " (прямой)");

                    Assert.That(HasPoint(path, grid, span.AX, span.AY), Is.True, where + ": путь проходит вход.");
                    Assert.That(HasPoint(path, grid, span.BX, span.BY), Is.True, where + ": путь проходит выход.");
                    Assert.That(CountOffCorridor(path, grid, river, crossings, span, ux, uy, length), Is.Zero,
                        where + ": в воде и на переправе — только по оси пролёта.");
                    checkedSpans++;
                }
            }
        }
        Assert.That(checkedSpans, Is.GreaterThan(0), "На проверочных seed есть переправы.");
    }

    [Test]
    public void WalkingAlongTheBank_DoesNotEnterCrossingCells()
    {
        int checkedPaths = 0;
        foreach (int seed in Seeds)
        {
            Configure(seed, out HashSet<int> river);
            WorldMapCrossings crossings = WorldMapNavigation.ActiveCrossings;
            if (crossings == null)
                continue; // на этом seed реки без переправ
            WorldMapHexGrid grid = WorldMapNavigation.Grid;
            WorldMapTerrainLayer layer = WorldMapNavigation.ActiveLayer;
            WorldMapMovementRules rules = WorldMapMovementRules.Current;

            for (int s = 0; s < crossings.Count; s++)
            {
                WorldMapCrossings.Span span = crossings.Spans[s];
                Axis(span, grid, out double ux, out double uy, out _);
                // Две точки на берегу входа по обе стороны от моста.
                double bx = span.AX - ux * 2.0 * grid.HexWidth, by = span.AY - uy * 2.0 * grid.HexWidth;
                double px = -uy * 3.0 * grid.HexWidth, py = ux * 3.0 * grid.HexWidth;
                if (!OnLand(layer, rules, river, grid, bx + px, by + py) || !OnLand(layer, rules, river, grid, bx - px, by - py))
                    continue;

                List<MapPointData> path = WorldMapNavigation.FindPath(
                    grid.PixelToPercentX(bx + px), grid.PixelToPercentY(by + py),
                    grid.PixelToPercentX(bx - px), grid.PixelToPercentY(by - py), false);
                Assert.That(path.Count, Is.GreaterThan(1), "seed " + seed + ": путь есть.");
                // Точки могут оказаться на разных берегах: тогда путь идёт по
                // какому-то мосту, но в клетки переправы — только по оси пролёта.
                foreach (MapPointData point in Sample(path, grid))
                {
                    if (!crossings.IsBlockedAtPercent(point.XPercent, point.YPercent))
                        continue;
                    Assert.That(OnAnySpanAxis(crossings, grid, point), Is.True,
                        "seed " + seed + ", пролёт " + s + ": путь не заходит в клетки переправы вне оси пролёта.");
                }
                checkedPaths++;
            }
        }
        Assert.That(checkedPaths, Is.GreaterThan(0), "Хотя бы один путь вдоль берега проверен.");
    }

    [Test]
    public void ExactBridgeFromTheDrawing_SetsTheDeckPoints()
    {
        int checkedSpans = 0;
        foreach (int seed in Seeds)
        {
            Configure(seed, out _);
            WorldMapCrossings crossings = WorldMapNavigation.ActiveCrossings;
            if (crossings == null)
                continue; // на этом seed реки без переправ
            WorldMapHexGrid grid = WorldMapNavigation.Grid;

            for (int s = 0; s < crossings.Count; s++)
            {
                WorldMapCrossings.Span span = crossings.Spans[s];
                Axis(span, grid, out double ux, out double uy, out _);
                // Настоящий мост чуть сдвинут в стороне: концы остаются в клетках берега.
                double sx = -uy * 0.2 * grid.HexWidth, sy = ux * 0.2 * grid.HexWidth;
                WorldMapBridgeSpan bridge = new WorldMapBridgeSpan(
                    grid.PixelToPercentX(span.BX + sx), grid.PixelToPercentY(span.BY + sy),
                    grid.PixelToPercentX(span.AX + sx), grid.PixelToPercentY(span.AY + sy),
                    1.2f); // концы заданы в обратном порядке
                WorldMapBridges.Set(new[] { bridge });
                Assert.That(WorldMapBridges.Count, Is.EqualTo(1));

                foreach (bool profitable in new[] { false, true })
                {
                    foreach (bool reverse in new[] { false, true })
                    {
                        List<MapPointData> path = PathAlongAxis(span, grid, ux, uy, 4.0, profitable, reverse);
                        string where = "seed " + seed + ", пролёт " + s + (profitable ? " (выгодный)" : " (прямой)") + (reverse ? " обратно" : "");
                        int a = IndexOfPoint(path, bridge.AXPercent, bridge.AYPercent);
                        int b = IndexOfPoint(path, bridge.BXPercent, bridge.BYPercent);
                        Assert.That(a, Is.GreaterThanOrEqualTo(0), where + ": есть конец моста A.");
                        Assert.That(b, Is.GreaterThanOrEqualTo(0), where + ": есть конец моста B.");
                        Assert.That(Math.Abs(a - b), Is.EqualTo(1), where + ": концы настила — соседние точки пути.");
                        // Конец, ближний к входу, идёт первым.
                        Assert.That(reverse ? b > a : a > b, Is.True, where + ": ближний конец моста первым.");
                    }
                }
                checkedSpans++;
            }
        }
        WorldMapBridges.Clear();
        Assert.That(WorldMapBridges.Count, Is.Zero);
        Assert.That(checkedSpans, Is.GreaterThan(0));
    }

    [Test]
    public void BridgeFarFromTheSpan_IsIgnored_PlanSpanEndsAreUsed()
    {
        Configure(7, out _);
        WorldMapCrossings crossings = WorldMapNavigation.ActiveCrossings;
        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        WorldMapCrossings.Span span = crossings.Spans[0];
        Axis(span, grid, out double ux, out double uy, out _);
        // Мост в десяти клетках от пролёта плана.
        double far = 10.0 * grid.HexWidth;
        WorldMapBridges.Set(new[]
        {
            new WorldMapBridgeSpan(
                grid.PixelToPercentX(span.AX + far), grid.PixelToPercentY(span.AY),
                grid.PixelToPercentX(span.BX + far), grid.PixelToPercentY(span.BY), 1f)
        });

        List<MapPointData> path = PathAlongAxis(span, grid, ux, uy, 4.0, false);

        Assert.That(HasPoint(path, grid, span.AX, span.AY), Is.True);
        Assert.That(HasPoint(path, grid, span.BX, span.BY), Is.True);
    }

    [Test]
    public void HeroStandingOnTheBridge_CanLeave()
    {
        Configure(7, out _);
        WorldMapCrossings crossings = WorldMapNavigation.ActiveCrossings;
        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        WorldMapCrossings.Span span = crossings.Spans[0];
        Axis(span, grid, out double ux, out double uy, out _);
        double mx = (span.AX + span.BX) * 0.5, my = (span.AY + span.BY) * 0.5;
        double tx = span.BX + ux * 4.0 * grid.HexWidth, ty = span.BY + uy * 4.0 * grid.HexWidth;

        List<MapPointData> path = WorldMapNavigation.FindPath(
            grid.PixelToPercentX(mx), grid.PixelToPercentY(my),
            grid.PixelToPercentX(tx), grid.PixelToPercentY(ty), false);

        Assert.That(path.Count, Is.GreaterThan(1), "Со стоянки на мосту путь есть.");
        MapPointData last = path[path.Count - 1];
        Assert.That(grid.DistanceHexes(last.XPercent, last.YPercent, grid.PixelToPercentX(tx), grid.PixelToPercentY(ty)),
            Is.LessThan(0.6), "Герой выходит с моста к цели.");
    }

    [Test]
    public void WithoutCrossings_PathIsTheSameAsBefore()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 7);
        WorldMapNavigation.ConfigureTerrainLayer(map.ToDefinitionData().CreateTerrainLayer());
        Assert.That(WorldMapNavigation.ActiveCrossings, Is.Null, "Без определения карты переправ нет.");

        WorldMapTerrainLayer layer = WorldMapNavigation.ActiveLayer;
        WorldMapHexGrid grid = layer.Grid;
        Random random = new Random(5);
        foreach (bool profitable in new[] { false, true })
        {
            for (int i = 0; i < 25; i++)
            {
                float sx = 5f + (float)random.NextDouble() * 90f, sy = 5f + (float)random.NextDouble() * 85f;
                float tx = 5f + (float)random.NextDouble() * 90f, ty = 5f + (float)random.NextDouble() * 85f;
                List<MapPointData> path = WorldMapNavigation.FindPath(sx, sy, tx, ty, profitable);
                List<WorldMapPathfinder.PathPoint> expected = WorldMapPathfinder.FindPath(
                    layer, WorldMapMovementRules.Current,
                    grid.PercentToPixelX(sx), grid.PercentToPixelY(sy),
                    grid.PercentToPixelX(tx), grid.PercentToPixelY(ty),
                    out _, profitable);

                Assert.That(path.Count, Is.EqualTo(expected.Count), "Путь " + i + ": та же ломаная.");
                for (int p = 1; p < path.Count; p++)
                {
                    Assert.That(grid.PercentToPixelX(path[p].XPercent), Is.EqualTo(expected[p].X).Within(0.5));
                    Assert.That(grid.PercentToPixelY(path[p].YPercent), Is.EqualTo(expected[p].Y).Within(0.5));
                }
            }
        }
    }

    [Test]
    public void BridgeRegistry_SetsAndClears()
    {
        WorldMapBridges.Set(new[] { new WorldMapBridgeSpan(10f, 20f, 12f, 22f, 1.5f) });
        Assert.That(WorldMapBridges.Count, Is.EqualTo(1));
        Assert.That(WorldMapBridges.Items[0].BYPercent, Is.EqualTo(22f));
        WorldMapBridges.Set(null);
        Assert.That(WorldMapBridges.Count, Is.Zero);
    }

    private static GeneratedWorldMap Configure(int seed, out HashSet<int> river)
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);
        WorldMapNavigation.ConfigureFromDefinition(map.ToDefinitionData());
        river = new HashSet<int>(map.RiverCells);
        return map;
    }

    private static void Axis(WorldMapCrossings.Span span, WorldMapHexGrid grid, out double ux, out double uy, out double length)
    {
        double dx = span.BX - span.AX, dy = span.BY - span.AY;
        length = Math.Sqrt(dx * dx + dy * dy);
        ux = dx / length;
        uy = dy / length;
    }

    // Путь от точки за cells клеток до входа к точке за cells клеток после выхода.
    private static List<MapPointData> PathAlongAxis(
        WorldMapCrossings.Span span, WorldMapHexGrid grid, double ux, double uy, double cells, bool profitable, bool reverse = false)
    {
        double reach = cells * grid.HexWidth;
        double sx = span.AX - ux * reach, sy = span.AY - uy * reach;
        double tx = span.BX + ux * reach, ty = span.BY + uy * reach;
        if (reverse)
        {
            double x = sx, y = sy;
            sx = tx;
            sy = ty;
            tx = x;
            ty = y;
        }
        return WorldMapNavigation.FindPath(
            grid.PixelToPercentX(sx), grid.PixelToPercentY(sy),
            grid.PixelToPercentX(tx), grid.PixelToPercentY(ty), profitable);
    }

    private static bool HasPoint(List<MapPointData> path, WorldMapHexGrid grid, double x, double y) =>
        IndexOfPoint(path, grid.PixelToPercentX(x), grid.PixelToPercentY(y)) >= 0;

    private static int IndexOfPoint(List<MapPointData> path, float xPercent, float yPercent)
    {
        for (int i = 0; i < path.Count; i++)
        {
            if (Math.Abs(path[i].XPercent - xPercent) < 0.002f && Math.Abs(path[i].YPercent - yPercent) < 0.002f)
                return i;
        }
        return -1;
    }

    // Точки пути с шагом 0,1 клетки.
    private static IEnumerable<MapPointData> Sample(List<MapPointData> path, WorldMapHexGrid grid)
    {
        for (int i = 1; i < path.Count; i++)
        {
            double length = grid.DistanceHexes(path[i - 1].XPercent, path[i - 1].YPercent, path[i].XPercent, path[i].YPercent);
            int steps = Math.Max(1, (int)Math.Ceiling(length / 0.1));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                yield return new MapPointData(
                    path[i - 1].XPercent + (path[i].XPercent - path[i - 1].XPercent) * t,
                    path[i - 1].YPercent + (path[i].YPercent - path[i - 1].YPercent) * t);
            }
        }
    }

    // Сколько точек пути лежит в клетке воды или переправы вне коридора оси пролёта.
    private static int CountOffCorridor(
        List<MapPointData> path, WorldMapHexGrid grid, HashSet<int> river, WorldMapCrossings crossings,
        WorldMapCrossings.Span span, double ux, double uy, double length)
    {
        int bad = 0;
        foreach (MapPointData point in Sample(path, grid))
        {
            double x = grid.PercentToPixelX(point.XPercent), y = grid.PercentToPixelY(point.YPercent);
            int cell = grid.IndexOf(grid.CellAtPixel(x, y));
            if (!river.Contains(cell) && !crossings.IsBlockedCell(cell))
                continue;
            double across = Math.Abs(-(x - span.AX) * uy + (y - span.AY) * ux) / grid.HexWidth;
            double along = (x - span.AX) * ux + (y - span.AY) * uy;
            if (across > Corridor || along < -1e-3 || along > length + 1e-3)
                bad++;
        }
        return bad;
    }

    private static bool OnAnySpanAxis(WorldMapCrossings crossings, WorldMapHexGrid grid, MapPointData point)
    {
        double x = grid.PercentToPixelX(point.XPercent), y = grid.PercentToPixelY(point.YPercent);
        foreach (WorldMapCrossings.Span span in crossings.Spans)
        {
            Axis(span, grid, out double ux, out double uy, out double length);
            double across = Math.Abs(-(x - span.AX) * uy + (y - span.AY) * ux) / grid.HexWidth;
            double along = (x - span.AX) * ux + (y - span.AY) * uy;
            if (across <= Corridor && along >= -1e-3 && along <= length + 1e-3)
                return true;
        }
        return false;
    }

    private static bool OnLand(
        WorldMapTerrainLayer layer, WorldMapMovementRules rules, HashSet<int> river, WorldMapHexGrid grid, double x, double y)
    {
        WorldMapHexCell cell = grid.CellAtPixel(x, y);
        return grid.IsInside(cell) && !river.Contains(grid.IndexOf(cell)) && rules.IsTraversable(layer.Get(cell));
    }
}
