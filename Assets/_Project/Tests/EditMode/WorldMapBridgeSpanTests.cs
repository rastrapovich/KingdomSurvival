using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Бриф правок (22 пункта) п. 13: каждая переправа дороги через реку — один
// пролёт моста от берега до берега: концы на суше, середина в русле, мост не
// идёт вдоль русла; пролёты переживают сохранение (JsonUtility).
public sealed class WorldMapBridgeSpanTests
{
    private static readonly int[] Seeds = { 3, 7, 11, 42, 20261030 };

    [Test]
    public void EveryCrossing_IsOneSpanFromBankToBank_AcrossTheRiver()
    {
        int spans = 0;
        foreach (int seed in Seeds)
        {
            GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), seed);
            WorldMapHexGrid grid = map.CreateGrid();
            HashSet<int> river = new HashSet<int>(map.RiverCells);
            Assert.That(map.BridgeSpans.Count % 2, Is.Zero, "Пролёт — пара точек.");
            if (map.Bridges.Count > 0)
                Assert.That(map.BridgeSpans.Count, Is.GreaterThan(0), "seed " + seed + ": переправы есть — есть и пролёты.");
            for (int i = 0; i + 1 < map.BridgeSpans.Count; i += 2)
            {
                MapPointData a = map.BridgeSpans[i];
                MapPointData b = map.BridgeSpans[i + 1];
                int aCell = grid.IndexOf(grid.CellAtPercent(a.XPercent, a.YPercent));
                int bCell = grid.IndexOf(grid.CellAtPercent(b.XPercent, b.YPercent));
                string where = "seed " + seed + ", пролёт " + (i / 2);
                Assert.That(river.Contains(aCell), Is.False, where + ": начало — на берегу.");
                Assert.That(river.Contains(bCell), Is.False, where + ": конец — на берегу.");
                MapPointData middle = new MapPointData((a.XPercent + b.XPercent) * 0.5f, (a.YPercent + b.YPercent) * 0.5f);
                Assert.That(river.Contains(grid.IndexOf(grid.CellAtPercent(middle.XPercent, middle.YPercent))), Is.True,
                    where + ": середина — над водой.");

                // Поперёк русла: с направлением ближайшего отрезка реки — не
                // вдоль (угол больше 30°).
                Vector2 span = new Vector2(
                    (float)(grid.PercentToPixelX(b.XPercent) - grid.PercentToPixelX(a.XPercent)),
                    (float)(grid.PercentToPixelY(b.YPercent) - grid.PercentToPixelY(a.YPercent))).normalized;
                Vector2 riverDirection = NearestRiverDirection(map, grid, middle);
                Assert.That(Mathf.Abs(Vector2.Dot(span, riverDirection)), Is.LessThan(Mathf.Cos(30f * Mathf.Deg2Rad)),
                    where + ": мост не вдоль русла.");
                spans++;
            }
        }
        Assert.That(spans, Is.GreaterThan(0), "На проверочных seed есть переправы.");
    }

    [Test]
    public void Spans_SurviveSaveFormat()
    {
        GeneratedWorldMap map = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 7);
        GeneratedWorldMap loaded = JsonUtility.FromJson<GeneratedWorldMap>(JsonUtility.ToJson(map));
        Assert.That(loaded.BridgeSpans.Select(p => p.XPercent), Is.EqualTo(map.BridgeSpans.Select(p => p.XPercent)));
        Assert.That(loaded.BridgeSpans.Select(p => p.YPercent), Is.EqualTo(map.BridgeSpans.Select(p => p.YPercent)));
    }

    private static Vector2 NearestRiverDirection(GeneratedWorldMap map, WorldMapHexGrid grid, MapPointData point)
    {
        Vector2 p = new Vector2((float)grid.PercentToPixelX(point.XPercent), (float)grid.PercentToPixelY(point.YPercent));
        Vector2 best = Vector2.right;
        float bestDistance = float.MaxValue;
        foreach (GeneratedMapLine line in map.Rivers)
        {
            for (int i = 0; line?.Points != null && i + 1 < line.Points.Count; i++)
            {
                Vector2 a = new Vector2((float)grid.PercentToPixelX(line.Points[i].XPercent), (float)grid.PercentToPixelY(line.Points[i].YPercent));
                Vector2 b = new Vector2((float)grid.PercentToPixelX(line.Points[i + 1].XPercent), (float)grid.PercentToPixelY(line.Points[i + 1].YPercent));
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float distance = Vector2.Distance(p, a + ab * t);
                if (distance < bestDistance && ab.sqrMagnitude > 0.0001f)
                {
                    bestDistance = distance;
                    best = ab.normalized;
                }
            }
        }
        return best;
    }
}
