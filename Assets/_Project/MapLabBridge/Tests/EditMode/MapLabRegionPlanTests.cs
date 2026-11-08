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

        // Окно «Генератор карты» строит ту же карту, что игра: размеры как на
        // экране «Новая игра», деревням назначаются услуги из профилей поселений.
        [Test]
        public void GeneratorPreview_UsesGameSizesAndVillageServices()
        {
            Assert.AreEqual(CampaignStartOptions.SmallMapHexesAcross, global::KingdomSurvival.MapLabBridge.Editor.MapLabPlanHook.HexesFor(MapLabWorldSize.Small));
            Assert.AreEqual(CampaignStartOptions.MediumMapHexesAcross, global::KingdomSurvival.MapLabBridge.Editor.MapLabPlanHook.HexesFor(MapLabWorldSize.Medium));
            Assert.AreEqual(CampaignStartOptions.HugeMapHexesAcross, global::KingdomSurvival.MapLabBridge.Editor.MapLabPlanHook.HexesFor(MapLabWorldSize.Huge));
            GeneratedWorldMap plan = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 41);
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> services = global::KingdomSurvival.MapLabBridge.Editor.MapLabPlanHook.PreviewServices(plan);
            Assert.IsNotEmpty(services, "Деревням предпросмотра назначены услуги.");
            Assert.IsTrue(services.Values.Any(r => r.Contains("merchant")), "Есть лавка.");
            Assert.IsTrue(services.Keys.All(k => k.StartsWith(GeneratedMapPlaces.IdPrefix)), "ID — как у мест игры.");
        }

        // Путь окна «Генератор карты»: профиль пресета A → план игры → рисунок
        // MapLab. У каждой деревни плана — дома (не один колодец), на всех
        // размерах карты.
        [Test]
        [Timeout(600000)]
        public void GeneratorWindowPath_EveryVillageHasHouses()
        {
            MapLabSettings source = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabSettings>("Assets/Experiments/MapLab/Presets/MapLab_A_HillAndLedge.asset");
            Assert.IsNotNull(source, "пресет A");
            GameObject host = new GameObject("MapLabWindowPathTest");
            MapLabSettings s = Object.Instantiate(source);
            try
            {
                MapLabView view = host.AddComponent<MapLabView>();
                foreach (MapLabWorldSize size in new[] { MapLabWorldSize.Small })
                    foreach (int seed in new[] { 104, 7 })
                    {
                        s.Author.UseGamePlan = true;
                        s.Author.WorldSize = size;
                        s.General.Seed = seed;
                        MapLabAuthor.ApplyStructure(s);
                        MapLabAuthor.ApplyLook(s);
                        global::KingdomSurvival.MapLabBridge.Editor.MapLabPlanHook.Build(s);
                        Assert.IsNotNull(s.RegionPlan, "план региона назначен");
                        view.Settings = s;
                        Assert.IsTrue(view.Generate(null), "генерация");
                        MapLabData data = view.Data;
                        int settlements = s.RegionPlan.Settlements.Count;
                        int houses = data.Objects.Count(o => o.Kind == MapLabObjectKind.House);
                        Debug.Log($"Окно генератора {size}/{seed}: поселений {settlements}, домов {houses}, объектов {data.Objects.Count}");
                        Assert.Greater(settlements, 0, $"{size}/{seed}: в плане есть деревни");
                        Assert.GreaterOrEqual(houses, settlements * 2, $"{size}/{seed}: у деревень есть дома");
                        // Мосты никогда не стоят друг на друге.
                        for (int i = 0; i < data.Bridges.Count; i++)
                            for (int j = i + 1; j < data.Bridges.Count; j++)
                            {
                                Vector2 ci = (data.Bridges[i].EndA + data.Bridges[i].EndB) * 0.5f, cj = (data.Bridges[j].EndA + data.Bridges[j].EndB) * 0.5f;
                                float reach = (Vector2.Distance(data.Bridges[i].EndA, data.Bridges[i].EndB) + Vector2.Distance(data.Bridges[j].EndA, data.Bridges[j].EndB)) * 0.5f;
                                Assert.Greater(Vector2.Distance(ci, cj), reach, $"{size}/{seed}: мосты {i} и {j} накладываются");
                            }
                        foreach (MapLabComposition.Settlement st in s.RegionPlan.Settlements)
                        {
                            int near = data.Objects.Count(o => o.Kind == MapLabObjectKind.House && Vector2.Distance(new Vector2(o.Position.x, o.Position.z), st.Center) < st.Radius * 2f);
                            Assert.GreaterOrEqual(near, 1, $"{size}/{seed}: у деревни {st.Id} нет ни одного дома");
                        }
                    }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(s);
            }
        }
    }
}
