using System.Linq;
using KingdomSurvival.MapLab;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Tests
{
    // 12У-D/F: план региона игры → композиция MapLab.
    public sealed class MapLabRegionPlanTests
    {
        [Test]
        public void Plan_ToComposition_VillagesMappingAndGrids()
        {
            WorldMapGenerationSettings planSettings = WorldMapGenerationSettings.CreateDefault();
            planSettings.HexesAcross = 40;
            GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, 11);
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            try
            {
                MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
                s.Stage03.Look = MapLabLook.Stage05;
                MapLabComposition c = MapLabRegionPlan.Apply(plan, s);
                Assert.AreSame(c, s.RegionPlan);
                Assert.IsTrue(c.FromPlan);
                Assert.AreEqual(plan.Villages.Count(v => !v.IsStart), c.Settlements.Count, "каждая деревня плана, кроме лагеря старта, — поселение MapLab");
                Assert.AreEqual(MapLabRegionPlan.PlayWidth(plan), s.General.MapSize, 1e-3f);
                Assert.AreEqual(plan.CanvasHeight / plan.CanvasWidth, c.PlayRect.height / c.PlayRect.width, 1e-3f, "пропорции холста");
                Assert.LessOrEqual(c.PlayRect.yMax, s.WorldSide + 1e-3f, "игровая область внутри карты");
                MapLabRegionPlan.Mapping m = MapLabRegionPlan.MappingFor(plan, s);
                foreach (GeneratedMapSite v in plan.Villages)
                {
                    Vector2 w = m.ToWorld(v.XPercent, v.YPercent);
                    Vector2 back = m.ToPercent(w);
                    Assert.AreEqual(v.XPercent, back.x, 1e-3f);
                    Assert.AreEqual(v.YPercent, back.y, 1e-3f);
                    Assert.IsTrue(c.PlayRect.Contains(w), "деревня вне игровой области");
                }
                Assert.AreEqual(c.PlanCols * c.PlanRows, c.PlanForest.Length);
                // Сетка леса совпадает с клетками плана.
                WorldMapHexGrid grid = plan.CreateGrid();
                WorldMapTerrainLayer layer = plan.CreateTerrainLayer();
                int checkedCells = 0, agree = 0;
                for (int z = 1; z < c.PlanRows - 1; z += 3)
                    for (int x = 1; x < c.PlanCols - 1; x += 3)
                    {
                        float xp = x / (float)(c.PlanCols - 1) * 100f, yp = (1f - z / (float)(c.PlanRows - 1)) * 100f;
                        bool forest = layer.GetAtPixel(grid.PercentToPixelX(xp), grid.PercentToPixelY(yp)) == WorldMapGameplayTerrainType.Forest;
                        Vector2 w = m.ToWorld(xp, yp);
                        bool drawn = c.SamplePlan(c.PlanForest, w) > 0.5f;
                        checkedCells++;
                        if (forest == drawn || layer.GetAtPixel(grid.PercentToPixelX(xp), grid.PercentToPixelY(yp)) == WorldMapGameplayTerrainType.Swamp)
                            agree++;
                    }
                Assert.AreEqual(checkedCells, agree, "сетка леса расходится с клетками плана");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
