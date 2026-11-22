using System.Collections.Generic;
using System.Linq;
using System.Text;
using KingdomSurvival.MapLab;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Tests
{
    // Запрос 23.11.2026: «дорога уходит в озеро без моста, группа мира стоит в
    // воде». Нарисованная вода (озёра; реки — кроме пролёта моста) не накрывает
    // ни дорожных и тропиночных линий, ни центров проходимых клеток, по которым
    // ходят герой и группы мира. План строится по настройкам игры
    // (KingdomSurvivalWorldMapGenerator), данные MapLab — как в снимке карты партии
    // (профиль MapLabGameAssets, сетка 257), без рендера.
    public sealed class MapLabWaterClearanceTests
    {
        // Партия пользователя 23.11.2026: дорога у озера, обоз «в воде».
        private const int UserSeed = 957485729;
        private const float LineStepHexes = 0.25f;
        // Мель у кромки: вода мельче этого шейдер красит берегом (MapLabWater: shoal, 0…0,35), а
        // отсчёты сетки на кромке дают лишние доли единицы — глубже считается водой.
        private const float ShoalDepth = 0.35f;
        // Подход к мосту: дорога выводится на настил прямо, ещё несколько единиц.
        private const float BridgeApproach = 4f;

        private static readonly (int Seed, WorldMapRegionKind Kind, int Hexes)[] Cases =
        {
            (UserSeed, WorldMapRegionKind.BySeed, 50),
            (3, WorldMapRegionKind.ForestLake, 50),
            (11, WorldMapRegionKind.ForestLake, 50),
            (29, WorldMapRegionKind.ForestLake, 100),
            (5, WorldMapRegionKind.RiverValley, 50),
            (17, WorldMapRegionKind.RiverValley, 50),
            (8, WorldMapRegionKind.Mixed, 50),
            (23, WorldMapRegionKind.Mixed, 100),
        };

        private sealed class Built
        {
            public GeneratedWorldMap Plan;
            public MapLabSettings Settings;
            public MapLabData Data;
            public MapLabRegionPlan.Mapping Mapping;
        }

        private static Built Build(int seed, WorldMapRegionKind kind, int hexes)
        {
            WorldMapGeneratorAsset generator = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
            Assert.IsNotNull(generator, "ассет генератора карты");
            WorldMapGenerationSettings planSettings = generator.Settings.Clone();
            planSettings.RegionKind = kind;
            planSettings.HexesAcross = hexes; // в игре — 50 или 100 клеток по ширине
            GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, seed);
            Assert.IsTrue(plan.IsValid, "план seed " + seed);

            MapLabGameAssets assets = MapLabGameAssets.Load();
            Assert.IsNotNull(assets, "MapLabGameAssets");
            Assert.IsNotNull(assets.Profile, "профиль рисунка игры");
            MapLabSettings settings = Object.Instantiate(assets.Profile);
            MapLabRegionPlan.Apply(plan, settings);
            settings.General.Quality = MapLabQuality.Samples257; // как в MapLabGameMap.Bake
            return new Built
            {
                Plan = plan,
                Settings = settings,
                Data = MapLabGenerator.Generate(settings),
                Mapping = MapLabRegionPlan.MappingFor(plan, settings),
            };
        }

        // Вода в точке: озеро (поле воды) или глубже мели под любым уровнем воды.
        // lakesOnly — только озёро (линии плана реки пересекают по мосту MapLab).
        private static bool InWater(MapLabData data, Vector2 p, bool lakesOnly = false)
        {
            if (!data.Contains(p.x, p.y))
                return false;
            if (data.Bilinear(data.LakeField, p.x, p.y) > 0f)
                return true;
            if (lakesOnly)
                return false;
            float level = data.SampleWaterLevel(p.x, p.y);
            return !float.IsNaN(level) && level - data.SampleHeight(p.x, p.y) > ShoalDepth;
        }

        // Чем именно накрыта точка: поле озера, расстояние до оси реки (ед.), уровень воды и земли.
        private static string WaterInfo(MapLabData data, Vector2 p)
        {
            float river = float.MaxValue;
            foreach (MapLabRiver r in data.Rivers)
                if (r.Points.Count > 1)
                    river = Mathf.Min(river, MapLabGeometry.Nearest(r.Points, p, out _, out _, out _, out _));
            return $"озеро {data.Bilinear(data.LakeField, p.x, p.y):0.00}, до реки {(river == float.MaxValue ? -1f : river):0.0}, вода {data.SampleWaterLevel(p.x, p.y):0.0}, земля {data.SampleHeight(p.x, p.y):0.0}";
        }

        // Точка на пролёте моста (настил над водой) — вода под ней допустима.
        private static bool OnBridge(MapLabData data, Vector2 p)
        {
            foreach (var bridge in data.Bridges)
            {
                float d = MapLabGeometry.DistanceToSegment(p, bridge.EndA, bridge.EndB, out _);
                if (d < bridge.Width * 0.5f + 1f + BridgeApproach)
                    return true;
            }
            return false;
        }

        [Test]
        [Timeout(900000)]
        public void DrawnWater_NeverCoversRoads_OrWalkableCells()
        {
            StringBuilder failures = new StringBuilder();
            int checkedRoadPoints = 0, checkedCells = 0, lakeMaps = 0;
            foreach ((int seed, WorldMapRegionKind kind, int hexes) in Cases)
            {
                Built b = Build(seed, kind, hexes);
                try
                {
                    if (b.Plan.LakeCells.Count > 0)
                        lakeMaps++;
                    string name = $"seed {seed} ({kind}, озёр {b.Plan.LakeCells.Count} клеток)";
                    int roadBad = 0, planLineBad = 0, cellBad = 0;
                    string firstRoad = "", firstPlan = "", firstCell = "";

                    // 1) Нарисованные линии дорог и троп (после стыковки и огибания).
                    foreach (MapLabPolyline road in b.Data.Roads)
                        foreach (Vector2 p in road.Points)
                        {
                            checkedRoadPoints++;
                            if (!OnBridge(b.Data, p) && InWater(b.Data, p) && roadBad++ == 0)
                                firstRoad = p.ToString("0.0") + " " + WaterInfo(b.Data, p);
                        }

                    // 2) Линии плана с шагом ≤ 0,25 клетки (по ним ходят дозоры и обозы): не в озере.
                    WorldMapHexGrid grid = b.Plan.CreateGrid();
                    float hexWorld = MapLabRegionPlan.UnitsPerHex;
                    foreach (GeneratedMapLine line in b.Plan.Roads)
                    {
                        for (int i = 0; i + 1 < line.Points.Count; i++)
                        {
                            Vector2 a = b.Mapping.ToWorld(line.Points[i].XPercent, line.Points[i].YPercent);
                            Vector2 c = b.Mapping.ToWorld(line.Points[i + 1].XPercent, line.Points[i + 1].YPercent);
                            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, c) / (hexWorld * LineStepHexes)));
                            for (int s = 0; s <= steps; s++)
                            {
                                Vector2 p = Vector2.Lerp(a, c, s / (float)steps);
                                checkedRoadPoints++;
                                if (InWater(b.Data, p, lakesOnly: true) && planLineBad++ == 0)
                                    firstPlan = $"{line.Kind} {p:0.0} {WaterInfo(b.Data, p)}";
                            }
                        }
                    }

                    // 3) Центры проходимых клеток (не переправа): герой и группы стоят в них.
                    WorldMapTerrainLayer layer = b.Plan.CreateTerrainLayer();
                    WorldMapCrossings crossings = new WorldMapCrossings(layer, b.Plan.BridgeSpans);
                    WorldMapMovementRules rules = WorldMapMovementRules.CreateDefault();
                    for (int i = 0; i < grid.CellCount; i++)
                    {
                        WorldMapHexCell cell = grid.CellAt(i);
                        if (!rules.IsTraversable(layer.Get(cell)) || crossings.IsBlockedCell(i))
                            continue;
                        grid.CellCenter(cell, out double px, out double py);
                        float xp = grid.PixelToPercentX(px), yp = grid.PixelToPercentY(py);
                        if (xp < 0f || xp > 100f || yp < 0f || yp > 100f)
                            continue;
                        Vector2 p = b.Mapping.ToWorld(xp, yp);
                        checkedCells++;
                        if (!OnBridge(b.Data, p) && InWater(b.Data, p) && cellBad++ == 0)
                            firstCell = $"клетка {cell} ({xp:0.#}%, {yp:0.#}%) = {layer.Get(cell)}, {WaterInfo(b.Data, p)}";
                    }

                    if (roadBad + planLineBad + cellBad > 0)
                        failures.AppendLine($"{name}: в воде — точек нарисованных дорог {roadBad} (первая {firstRoad}), точек линий плана {planLineBad} (первая {firstPlan}), центров проходимых клеток {cellBad} (первая {firstCell})");
                }
                finally
                {
                    Object.DestroyImmediate(b.Settings);
                }
            }
            Debug.Log($"Проверено точек дорог {checkedRoadPoints}, центров клеток {checkedCells}, карт с озёрами {lakeMaps}");
            Assert.Greater(lakeMaps, 0, "в наборе есть карты с озёрами");
            Assert.IsEmpty(failures.ToString(), "нарисованная вода накрывает проходимое:\n" + failures);
        }

        [Test]
        public void Validator_RejectsRoadThroughALakeCell()
        {
            GeneratedWorldMap plan = WorldMapGenerator.Generate(WorldMapGenerationSettings.CreateDefault(), 11);
            Assume.That(plan.LakeCells.Count, Is.GreaterThan(0), "на seed 11 нет озера");
            Assert.IsFalse(WorldMapRegionValidator.Validate(plan).Any(i => i.Code == "H11"), "исправная карта проходит H11");
            // Дорога, заведённая в клетку озера, — ошибка H11.
            WorldMapHexGrid grid = plan.CreateGrid();
            grid.CellCenter(grid.CellAt(plan.LakeCells[0]), out double x, out double y);
            plan.Roads.Add(new GeneratedMapLine
            {
                Kind = GeneratedMapLine.RoadKind,
                Points = new List<MapPointData> { new MapPointData(grid.PixelToPercentX(x), grid.PixelToPercentY(y)) },
            });
            Assert.IsTrue(WorldMapRegionValidator.Validate(plan).Any(i => i.Code == "H11" && i.Error), "дорога в клетке озера отмечена");
        }
    }
}
