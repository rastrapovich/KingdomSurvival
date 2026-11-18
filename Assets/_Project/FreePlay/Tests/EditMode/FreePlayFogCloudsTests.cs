using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// Облака тумана войны (19.11.2026): раскладка стабильна по seed, без ровной
// сетки и покрывает всю карту; облако рассеяно только у исследованного —
// после сохранения и загрузки набор рассеянных облаков тот же; отрезок пути
// учитывается целиком; направление отлёта — от героя или стабильное.
public sealed class FreePlayFogCloudsTests
{
    private const int Seed = 31;

    [SetUp]
    public void SetUp()
    {
        FreePlayContent.Register();
    }

    [TearDown]
    public void TearDown()
    {
        SettlementCatalog.Reset();
        ItemCatalog.ResetToDefaults();
    }

    private static GameState NewGeneratedFreePlay()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), Seed);
        return new CampaignSetup().CreateCampaign(null, null, map);
    }

    private static HashSet<int> Cleared(GameState state, List<WorldMapFogCloudAnchor> anchors, double radius)
    {
        WorldMapHexGrid grid = WorldMapFog.Grid(state);
        return new HashSet<int>(anchors.Where(a => WorldMapFogClouds.IsCleared(state, grid, a.X, a.Y, radius)).Select(a => a.Id));
    }

    [Test]
    public void Layout_IsStableBySeed_IrregularAndCoversTheCanvas()
    {
        GameState state = NewGeneratedFreePlay();
        WorldMapHexGrid grid = WorldMapFog.Grid(state);
        List<WorldMapFogCloudAnchor> a = WorldMapFogClouds.Layout(grid, state.GeneratedMap.Seed, 4.5, 0.45);
        List<WorldMapFogCloudAnchor> b = WorldMapFogClouds.Layout(grid, state.GeneratedMap.Seed, 4.5, 0.45);
        List<WorldMapFogCloudAnchor> other = WorldMapFogClouds.Layout(grid, state.GeneratedMap.Seed + 1, 4.5, 0.45);

        Assert.Greater(a.Count, 20, "Облака на всю карту.");
        CollectionAssert.AreEqual(a.Select(x => (x.Id, x.X, x.Y, x.Hash)).ToList(), b.Select(x => (x.Id, x.X, x.Y, x.Hash)).ToList(), "Тот же seed — те же облака.");
        Assert.IsTrue(a.Zip(other, (p, q) => p.X != q.X || p.Y != q.Y).Any(d => d), "Другой seed — другая раскладка.");
        Assert.IsTrue(a.All(x => x.X >= 0 && x.Y >= 0 && x.X <= grid.CanvasWidth && x.Y <= grid.CanvasHeight));
        // Без «забора»: расстояния до соседа в ряду заметно различаются.
        double step = 4.5 * grid.HexWidth;
        List<double> gaps = new List<double>();
        for (int i = 1; i < a.Count; i++)
            if (a[i].Id == a[i - 1].Id + 1)
                gaps.Add(a[i].X - a[i - 1].X);
        Assert.Greater(gaps.Max() - gaps.Min(), step * 0.3, "Разброс шага облаков.");
        // Несколько рисунков и зеркал на карте.
        Assert.AreEqual(2, a.Select(x => x.Variant(2)).Distinct().Count());
        Assert.AreEqual(2, a.Select(x => x.Flip).Distinct().Count());
    }

    [Test]
    public void Cleared_OnlyNearExplored_GrowsWithReveal_AndSurvivesLoad()
    {
        GameState state = NewGeneratedFreePlay();
        WorldMapFog.EnsureInitialized(state);
        WorldMapHexGrid grid = WorldMapFog.Grid(state);
        List<WorldMapFogCloudAnchor> anchors = WorldMapFogClouds.Layout(grid, state.GeneratedMap.Seed, 4.5, 0.45);
        const double radius = 4.0;

        HashSet<int> start = Cleared(state, anchors, radius);
        Assert.Greater(start.Count, 0, "У открытой округи старта облака рассеяны.");
        Assert.Less(start.Count, anchors.Count / 2, "Остальная карта под облаками.");

        WorldMapFogCloudAnchor far = anchors
            .Where(x => !start.Contains(x.Id))
            .OrderByDescending(x => Mathf.Abs((float)(x.X - grid.CanvasWidth * 0.5)))
            .First();
        Assert.IsTrue(WorldMapFog.RevealAround(state, grid.PixelToPercentX(far.X), grid.PixelToPercentY(far.Y)));
        HashSet<int> after = Cleared(state, anchors, radius);
        Assert.IsTrue(after.Contains(far.Id), "Открытие у облака рассеивает его.");
        Assert.IsTrue(start.IsSubsetOf(after), "Рассеянные облака не возвращаются.");

        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        List<WorldMapFogCloudAnchor> reloaded = WorldMapFogClouds.Layout(WorldMapFog.Grid(loaded), loaded.GeneratedMap.Seed, 4.5, 0.45);
        CollectionAssert.AreEquivalent(after, Cleared(loaded, reloaded, radius), "После загрузки — те же рассеянные облака.");
    }

    [Test]
    public void Segment_CoversTheWholePath_AndDriftPointsAwayFromHero()
    {
        WorldMapFogClouds.ClosestOnSegment(5, 3, 0, 0, 10, 0, out double cx, out double cy);
        Assert.AreEqual(5, cx, 1e-9);
        Assert.AreEqual(0, cy, 1e-9);
        WorldMapFogClouds.ClosestOnSegment(-4, 1, 0, 0, 10, 0, out cx, out cy);
        Assert.AreEqual(0, cx, 1e-9, "До начала отрезка — его начало.");
        WorldMapFogClouds.ClosestOnSegment(3, 4, 2, 2, 2, 2, out cx, out cy);
        Assert.AreEqual(2, cx, 1e-9, "Стоящий герой — точка.");

        WorldMapFogCloudAnchor anchor = new WorldMapFogCloudAnchor(7, 10, 0, 12345u);
        WorldMapFogClouds.DriftDirection(anchor, 0, 0, 0.01, out double dx, out double dy);
        Assert.AreEqual(1, dx, 1e-9, "От героя к облаку.");
        Assert.AreEqual(0, dy, 1e-9);

        WorldMapFogClouds.DriftDirection(anchor, 10, 0, 0.01, out double ax, out double ay);
        WorldMapFogClouds.DriftDirection(anchor, 10, 0, 0.01, out double bx, out double by);
        Assert.AreEqual(1, ax * ax + ay * ay, 1e-9, "Герой на опоре — единичное направление.");
        Assert.AreEqual(ax, bx, 1e-12, "Стабильно, без случайных скачков.");
        Assert.AreEqual(ay, by, 1e-12);
    }
}
