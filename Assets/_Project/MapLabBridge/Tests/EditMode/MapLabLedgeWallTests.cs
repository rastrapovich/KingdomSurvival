using System.Collections.Generic;
using KingdomSurvival.MapLab;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Tests
{
    // Запрос 23.11.2026: «герой заходит в скалу» и «не может подойти к краю
    // скалы». Нарисованная стена обрыва и непроходимость совпадают: стена —
    // непроходима (ни низом, ни верхом), а к её подножию и к кромке площадки
    // можно дойти вплотную (клетка уступа проходима вне стены). Данные MapLab —
    // как в снимке карты партии (профиль MapLabGameAssets, сетка 257), без рендера.
    public sealed class MapLabLedgeWallTests
    {
        private const int UserSeed = 957485729;

        private static readonly (int Seed, WorldMapRegionKind Kind, int Hexes)[] Cases =
        {
            (UserSeed, WorldMapRegionKind.BySeed, 50),
            (3, WorldMapRegionKind.HillAndLedge, 50),
            (7, WorldMapRegionKind.HillAndLedge, 50),
            (11, WorldMapRegionKind.HillAndLedge, 50),
            (42, WorldMapRegionKind.HillAndLedge, 100),
        };

        private sealed class Built
        {
            public GeneratedWorldMap Plan;
            public MapLabSettings Settings;
            public MapLabData Data;
            public MapLabRegionPlan.Mapping Mapping;
            public WorldMapHexGrid Grid;
            public WorldMapTerrainLayer Layer;
        }

        private static Built Build(int seed, WorldMapRegionKind kind, int hexes)
        {
            WorldMapGeneratorAsset generator = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
            Assert.IsNotNull(generator, "ассет генератора карты");
            WorldMapGenerationSettings planSettings = generator.Settings.Clone();
            planSettings.RegionKind = kind;
            planSettings.HexesAcross = hexes;
            GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, seed);
            Assert.IsTrue(plan.IsValid, "план seed " + seed);
            MapLabGameAssets assets = MapLabGameAssets.Load();
            Assert.IsNotNull(assets?.Profile, "профиль рисунка игры");
            MapLabSettings settings = Object.Instantiate(assets.Profile);
            MapLabRegionPlan.Apply(plan, settings);
            settings.General.Quality = MapLabQuality.Samples257;
            return new Built
            {
                Plan = plan,
                Settings = settings,
                Data = MapLabGenerator.Generate(settings),
                Mapping = MapLabRegionPlan.MappingFor(plan, settings),
                Grid = plan.CreateGrid(),
                Layer = plan.CreateTerrainLayer(),
            };
        }

        private static void Register(Built b)
        {
            Rect play = new Rect(b.Mapping.Frame, b.Mapping.OffsetZ, b.Mapping.PlayWidth, b.Mapping.PlayHeight);
            WorldMapWalls.Set(MapLabGameLive.LedgeWalls(b.Data, play), MapLabGameLive.LedgeCells(b.Plan));
        }

        private static Vector2 ToPixel(Built b, Vector2 world)
        {
            Vector2 percent = b.Mapping.ToPercent(world);
            return new Vector2((float)b.Grid.PercentToPixelX(percent.x), (float)b.Grid.PercentToPixelY(percent.y));
        }

        private static Vector2 ToWorld(Built b, double px, double py) =>
            b.Mapping.ToWorld(b.Grid.PixelToPercentX(px), b.Grid.PixelToPercentY(py));

        private static bool WallAt(Built b, Vector2 world)
        {
            Vector2 percent = b.Mapping.ToPercent(world);
            return WorldMapWalls.Blocks(percent.x, percent.y);
        }

        // Точка суши, где можно стоять: проходимая клетка плана или клетка уступа вне стены.
        private static bool Standable(Built b, Vector2 world)
        {
            Vector2 percent = b.Mapping.ToPercent(world);
            if (percent.x < 1f || percent.y < 1f || percent.x > 99f || percent.y > 99f || WorldMapWalls.Blocks(percent.x, percent.y))
                return false;
            WorldMapHexCell cell = b.Grid.CellAtPercent(percent.x, percent.y);
            return WorldMapMovementRules.Current.IsTraversable(b.Layer.Get(cell)) || WorldMapWalls.IsLedgeCell(b.Grid.IndexOf(cell));
        }

        // Опора на крутом склоне обрыва (по данным MapLab, независимо от четырёхугольников стены):
        // рядом со столбом линии обрыва земля круче 1,5 (≈ 56°) поперёк стены. Склон перепада
        // круче 1,5 лишь в средней части (±0,8 ед. от линии), стена с запасом шире.
        private static bool OnWallFace(Built b, Vector2 p, out string why)
        {
            why = string.Empty;
            foreach (MapLabCliffLine line in b.Data.CliffLines)
                foreach (MapLabCliffColumn column in line.Columns)
                {
                    Vector2 along = column.End - column.Start;
                    float length2 = Mathf.Max(1e-6f, along.sqrMagnitude);
                    float t = Vector2.Dot(p - column.Start, along) / length2;
                    if (t <= 0.05f || t >= 0.95f)
                        continue;
                    Vector2 foot = column.Start + along * t;
                    if ((p - foot).magnitude > 3f)
                        continue;
                    Vector2 across = column.Outward;
                    Vector2 a = p - across * 0.4f, c = p + across * 0.4f;
                    float slope = Mathf.Abs(b.Data.SampleHeight(c.x, c.y) - b.Data.SampleHeight(a.x, a.y)) / 0.8f;
                    if (slope > 1.5f && Mathf.Abs(Vector2.Dot(p - foot, across)) < 2f)
                    {
                        Vector2 pc = b.Mapping.ToPercent(p);
                        why = $"уклон {slope:0.0}, столб ({column.Start.x:0.0}; {column.Start.y:0.0})-({column.End.x:0.0}; {column.End.y:0.0}), t {t:0.00}, поперёк {Vector2.Dot(p - foot, across):0.0}, на стене {WorldMapWalls.Blocks(pc.x, pc.y)}, стен {WorldMapWalls.Count}";
                        return true;
                    }
                }
            return false;
        }

        private static string Describe(Built b, List<WorldMapPathfinder.PathPoint> path)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (WorldMapPathfinder.PathPoint point in path)
            {
                Vector2 w = ToWorld(b, point.X, point.Y);
                WorldMapHexCell cell = b.Grid.CellAtPixel(point.X, point.Y);
                string mark = WorldMapWalls.IsLedgeCell(b.Grid.IndexOf(cell)) ? "/уступ" : "";
                text.Append($"[({w.x:0.0}; {w.y:0.0}) {b.Layer.Get(cell)}{mark}] ");
            }
            return text.ToString();
        }

        private static IEnumerable<(MapLabCliffColumn Column, Vector2 Upper)> Columns(Built b)
        {
            foreach (MapLabCliffLine line in b.Data.CliffLines)
                foreach (MapLabCliffColumn column in line.Columns)
                    yield return (column, -column.Outward);
        }

        [Test]
        public void PathsNeverStandOnTheDrawnWall_AtAnyHeight()
        {
            int pathsChecked = 0, samples = 0, cases = 0;
            foreach ((int seed, WorldMapRegionKind kind, int hexes) in Cases)
            {
                Built b = Build(seed, kind, hexes);
                if (b.Data.CliffLines.Count == 0)
                    continue;
                cases++;
                Register(b);
                try
                {
                    System.Random random = new System.Random(seed);
                    List<(MapLabCliffColumn Column, Vector2 Upper)> columns = new List<(MapLabCliffColumn, Vector2)>(Columns(b));
                    Assert.Greater(columns.Count, 0, "seed " + seed);
                    for (int n = 0; n < 160; n++)
                    {
                        // Конец пути — рядом со стеной (любая сторона), начало — рядом с соседним столбом.
                        (MapLabCliffColumn ca, Vector2 ua) = columns[random.Next(columns.Count)];
                        (MapLabCliffColumn cb, Vector2 ub) = columns[Mathf.Clamp(columns.IndexOf((ca, ua)) + random.Next(-6, 7), 0, columns.Count - 1)];
                        Vector2 a = (ca.Start + ca.End) * 0.5f + ua * (float)(random.NextDouble() * 14.0 - 7.0);
                        Vector2 c = (cb.Start + cb.End) * 0.5f + ub * (float)(random.NextDouble() * 14.0 - 7.0);
                        if (!Standable(b, a) || !Standable(b, c))
                            continue;
                        Vector2 pa = ToPixel(b, a), pc = ToPixel(b, c);
                        List<WorldMapPathfinder.PathPoint> path = WorldMapPathfinder.FindPath(
                            b.Layer, WorldMapMovementRules.Current, pa.x, pa.y, pc.x, pc.y, out _, false);
                        pathsChecked++;
                        for (int i = 0; i + 1 < path.Count; i++)
                        {
                            Vector2 from = ToWorld(b, path[i].X, path[i].Y), to = ToWorld(b, path[i + 1].X, path[i + 1].Y);
                            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 0.25f));
                            for (int s = 0; s <= steps; s++)
                            {
                                Vector2 p = Vector2.Lerp(from, to, s / (float)steps);
                                samples++;
                                Assert.IsFalse(WallAt(b, p), $"seed {seed}: путь заходит на стену обрыва в ({p.x:0.0}; {p.y:0.0}); звено {i} из {path.Count - 1}: {Describe(b, path)}; начало ({a.x:0.0}; {a.y:0.0}) конец ({c.x:0.0}; {c.y:0.0})");
                                Assert.IsFalse(OnWallFace(b, p, out string why), $"seed {seed}: опора героя на склоне обрыва ({p.x:0.0}; {p.y:0.0}): {why}; путь {Describe(b, path)}");
                            }
                        }
                    }
                }
                finally
                {
                    WorldMapWalls.Clear();
                    Object.DestroyImmediate(b.Settings);
                }
            }
            Assume.That(cases, Is.GreaterThan(0), "нет карт с обрывами");
            Assert.Greater(pathsChecked, 100, "проверено путей");
            Assert.Greater(samples, 1000);
        }

        [Test]
        public void EdgeOfTheCliffIsReachableUpToTheWall_FromBothSides()
        {
            int reachedHigh = 0, reachedLow = 0, inLedgeCells = 0, cases = 0;
            foreach ((int seed, WorldMapRegionKind kind, int hexes) in Cases)
            {
                Built b = Build(seed, kind, hexes);
                if (b.Data.CliffLines.Count == 0)
                    continue;
                cases++;
                Register(b);
                try
                {
                    HashSet<int> ledgeCells = new HashSet<int>(MapLabGameLive.LedgeCells(b.Plan));
                    MapLabGameLive.WallExtents(b.Data, out float wallLow, out float wallHigh);
                    float outLow = wallLow + 0.15f, outHigh = wallHigh + 0.15f;
                    int index = 0;
                    foreach ((MapLabCliffColumn column, Vector2 upper) in Columns(b))
                    {
                        if (index++ % 2 != 0)
                            continue;
                        Vector2 mid = (column.Start + column.End) * 0.5f;
                        for (int side = 0; side < 2; side++)
                        {
                            // Цель — сразу за краем стены; начало — подальше от стены на той же стороне.
                            Vector2 away = side == 0 ? upper : -upper;
                            Vector2 target = mid + away * (side == 0 ? outHigh : outLow);
                            if (!Standable(b, target))
                                continue;
                            Vector2 origin = Vector2.zero;
                            bool found = false;
                            foreach (float distance in new[] { 8f, 14f, 22f })
                            {
                                origin = target + away * distance;
                                if (Standable(b, origin) && Mathf.Abs(b.Data.SampleHeight(origin.x, origin.y) - b.Data.SampleHeight(target.x, target.y)) < 1.2f)
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                                continue;
                            Vector2 po = ToPixel(b, origin), pt = ToPixel(b, target);
                            List<WorldMapPathfinder.PathPoint> path = WorldMapPathfinder.FindPath(
                                b.Layer, WorldMapMovementRules.Current, po.x, po.y, pt.x, pt.y, out bool reached, false);
                            WorldMapPathfinder.PathPoint last = path[path.Count - 1];
                            Vector2 end = ToWorld(b, last.X, last.Y);
                            Assert.IsTrue(reached && Vector2.Distance(end, target) < 0.2f,
                                $"seed {seed}: не дойти вплотную к стене {(side == 0 ? "сверху" : "снизу")} ({target.x:0.0}; {target.y:0.0}), путь кончился в ({end.x:0.0}; {end.y:0.0})");
                            if (side == 0) reachedHigh++; else reachedLow++;
                            if (ledgeCells.Contains(b.Grid.IndexOf(b.Grid.CellAtPixel(pt.x, pt.y))))
                                inLedgeCells++;
                        }
                    }
                }
                finally
                {
                    WorldMapWalls.Clear();
                    Object.DestroyImmediate(b.Settings);
                }
            }
            Assume.That(cases, Is.GreaterThan(0), "нет карт с обрывами");
            Assert.Greater(reachedHigh, 5, "подходов к кромке сверху");
            Assert.Greater(reachedLow, 5, "подходов к подножию снизу");
            Assert.Greater(inLedgeCells, 8, "подходов к стене внутри клеток уступа (прежде эти клетки были непроходимы)");
        }

        [Test]
        public void ClickOnTheWall_PushesTheTargetOutsideIt()
        {
            Built b = Build(UserSeed, WorldMapRegionKind.BySeed, 50);
            Register(b);
            try
            {
                int checkedWalls = 0;
                foreach ((MapLabCliffColumn column, Vector2 upper) in Columns(b))
                {
                    Vector2 onWall = (column.Start + column.End) * 0.5f;
                    Vector2 percent = b.Mapping.ToPercent(onWall);
                    Assert.IsTrue(WorldMapWalls.Blocks(percent.x, percent.y), "середина столба — на стене");
                    float x = percent.x, y = percent.y;
                    WorldMapWalls.PushOut(ref x, ref y);
                    Assert.IsFalse(WorldMapWalls.Blocks(x, y), "точка вытолкнута со стены");
                    checkedWalls++;
                }
                Assume.That(checkedWalls, Is.GreaterThan(0));
            }
            finally
            {
                WorldMapWalls.Clear();
                Object.DestroyImmediate(b.Settings);
            }
        }

        [Test]
        public void Wall_ContainsPointsAndCrossesSegments()
        {
            try
            {
                WorldMapWalls.Set(new[] { new WorldMapWall(10f, 10f, 20f, 10f, 20f, 12f, 10f, 12f) }, new[] { 5 });
                Assert.IsTrue(WorldMapWalls.Blocks(15f, 11f));
                Assert.IsFalse(WorldMapWalls.Blocks(15f, 13f));
                Assert.IsTrue(WorldMapWalls.Crosses(15f, 8f, 15f, 14f), "отрезок поперёк стены");
                Assert.IsFalse(WorldMapWalls.Crosses(5f, 8f, 25f, 8f), "отрезок вдоль, не задевая");
                Assert.IsTrue(WorldMapWalls.IsLedgeCell(5));
                Assert.IsFalse(WorldMapWalls.IsLedgeCell(6));
                float x = 15f, y = 10.4f;
                WorldMapWalls.PushOut(ref x, ref y);
                Assert.IsFalse(WorldMapWalls.Blocks(x, y));
                Assert.Less(y, 10f, "ближайший край — нижний");
            }
            finally
            {
                WorldMapWalls.Clear();
            }
        }
    }
}
