using System.Linq;
using KingdomSurvival.MapLab;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Tests
{
    // 12С/12У: уступы плана региона → обрывы MapLab. Клетки Cliffs непроходимы
    // и должны быть видны: на линии уступа — обрыв, прочие — горой.
    public sealed class MapLabLedgePlanTests
    {
        private static GeneratedWorldMap Plan(int seed)
        {
            WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
            return WorldMapGenerator.Generate(settings, seed);
        }

        [Test]
        public void EveryLedge_BecomesACliff_WithThePlateauOnTheHigherSide()
        {
            int checkedLedges = 0;
            foreach (int seed in new[] { 3, 7, 11, 42 })
            {
                GeneratedWorldMap plan = Plan(seed);
                MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
                try
                {
                    MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
                    MapLabComposition c = MapLabRegionPlan.Build(plan, s, false);
                    GeneratedMapLedge[] ledges = plan.Ledges.Where(l => l.Points.Count >= 2).ToArray();
                    Assert.AreEqual(ledges.Length, c.Cliffs.Count, "seed " + seed + ": обрыв на каждый уступ плана");
                    Assert.Greater(s.Cliffs.CliffHeight, 0.01f, "обрыв должен рисоваться");

                    MapLabRegionPlan.Mapping m = MapLabRegionPlan.MappingFor(plan, s);
                    WorldMapHexGrid grid = plan.CreateGrid();
                    float hexWorld = m.PlayWidth / plan.HexesAcross;
                    for (int i = 0; i < ledges.Length; i++)
                    {
                        MapLabComposition.Cliff cliff = c.Cliffs[i];
                        Assert.AreEqual(ledges[i].Points.Count, cliff.Control.Count);
                        Assert.AreEqual(1f, cliff.UpperSide, "плато слева по ходу линии");
                        float high = 0f, low = 0f;
                        for (int j = 0; j + 1 < cliff.Control.Count; j++)
                        {
                            Vector2 a = cliff.Control[j], b = cliff.Control[j + 1];
                            Vector2 direction = (b - a).normalized;
                            Vector2 left = new Vector2(-direction.y, direction.x); // слева по ходу в мире MapLab (x вправо, z вглубь)
                            Vector2 mid = (a + b) * 0.5f;
                            high += ElevationAt(plan, grid, m, mid + left * hexWorld * 1.5f);
                            low += ElevationAt(plan, grid, m, mid - left * hexWorld * 1.5f);
                        }
                        Assert.Greater(high, low, "seed " + seed + ": сторона плато совпадает со стороной большей высоты плана");
                        checkedLedges++;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(s);
                }
            }
            Assert.Greater(checkedLedges, 0);
        }

        private static float ElevationAt(GeneratedWorldMap plan, WorldMapHexGrid grid, MapLabRegionPlan.Mapping m, Vector2 world)
        {
            Vector2 percent = m.ToPercent(world);
            return plan.Elevation[grid.IndexOf(grid.CellAtPercent(percent.x, percent.y))];
        }

        [Test]
        public void CliffCellsWithoutALedge_AreDrawnAsMountains()
        {
            GeneratedWorldMap plan = Plan(7);
            Assume.That(plan.Ledges.Count, Is.GreaterThan(0));
            int cliffCells = plan.Ledges.Sum(l => l.Cells.Count);
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            try
            {
                MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
                int withLedges = MapLabRegionPlan.Build(plan, s, false).Hills.Count;
                // Карта, сохранённая до уступов: Cliffs есть, списка уступов нет.
                plan.Ledges.Clear();
                MapLabComposition old = MapLabRegionPlan.Build(plan, s, false);
                Assert.AreEqual(0, old.Cliffs.Count);
                Assert.AreEqual(withLedges + cliffCells, old.Hills.Count, "каждая скала старой карты видна горой");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
