using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Этап 05: ограничение прежнего Zoom 8 и 35° на фактическом пределе, один
    // ракурс дома и набор героя при любом приближении, граф оград (уникальные
    // узлы и рёбра, общие границы — один раз, ворота не на дорогах и домах),
    // сухие опоры мостов, общая граница берега без NaN и обрывов, импорт
    // ревизии 5 и повторный импорт, стабильность программного рисунка.
    public sealed class MapLabStage05Tests
    {
        private static MapLabSettings Settings(MapLabPresetKind preset = MapLabPresetKind.RiverValley)
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, preset);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            s.Stage03.PaintTexelsPerUnit = 6f;
            s.Stage05.Art = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(MapLabArtImport.Stage05SetPath);
            return s;
        }

        private sealed class Scene : System.IDisposable
        {
            public readonly MapLabView View;
            public readonly MapLabCameraRig Rig;
            private readonly GameObject cameraObject, mapObject;

            public Scene(MapLabSettings s)
            {
                cameraObject = new GameObject("MapLab Camera (test)");
                cameraObject.AddComponent<Camera>();
                Rig = cameraObject.AddComponent<MapLabCameraRig>();
                mapObject = new GameObject("MapLab (test)");
                mapObject.SetActive(false);
                View = mapObject.AddComponent<MapLabView>();
                View.GenerateOnLoad = false;
                View.Settings = s;
                View.CameraRig = Rig;
                mapObject.SetActive(true);
            }

            public void Dispose()
            {
                View.Clear();
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        // ---------- Пошаговая сборка (игра) ----------

        // 12У-G: пошаговая сборка (расчёты в фоне) даёт ту же карту, что
        // обычная: те же тайлы, перо, тени и число частей.
        [Test]
        public void StepwiseBuild_MatchesSynchronousBuild()
        {
            MapLabSettings s = Settings();
            try
            {
                using (Scene scene = new Scene(s))
                {
                    Assert.IsTrue(scene.View.Generate(null), scene.View.LastError);
                    int renderers = scene.View.Root.GetComponentsInChildren<MeshRenderer>(true).Length;
                    int tiles = scene.View.PaintTileCount, shapes = scene.View.ShadowShapes;
                    int ink = scene.View.InkStats.Vertices, field = scene.View.FieldInkStats.Vertices, shore = scene.View.ShoreInkStats.Vertices;
                    int objects = scene.View.Data.Objects.Count;
                    System.Collections.IEnumerator steps = scene.View.GenerateStepwise();
                    int frames = 0;
                    while (steps.MoveNext())
                    {
                        frames++;
                        System.Threading.Thread.Sleep(1);
                    }
                    Assert.Greater(frames, 5, "Сборка уступает кадры");
                    Assert.AreEqual(objects, scene.View.Data.Objects.Count, "Объекты данных");
                    Assert.AreEqual(tiles, scene.View.PaintTileCount, "Тайлы покраски");
                    Assert.AreEqual(shapes, scene.View.ShadowShapes, "Тени объектов");
                    Assert.AreEqual(ink, scene.View.InkStats.Vertices, "Перо дорог");
                    Assert.AreEqual(field, scene.View.FieldInkStats.Vertices, "Перо полей");
                    Assert.AreEqual(shore, scene.View.ShoreInkStats.Vertices, "Перо берега");
                    Assert.AreEqual(renderers, scene.View.Root.GetComponentsInChildren<MeshRenderer>(true).Length, "Части карты");
                    Assert.AreEqual(string.Empty, scene.View.BuildStage);
                }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // Составные детали мест: «ID*N» и части через «+».
        [Test]
        public void FeatureParts_ParseCountsAndParts()
        {
            List<(string Id, int Count)> one = MapLabArtBuilder.FeatureParts("Grave_Marker_A");
            Assert.AreEqual(1, one.Count);
            Assert.AreEqual(("Grave_Marker_A", 1), one[0]);
            List<(string Id, int Count)> cemetery = MapLabArtBuilder.FeatureParts("Grave_Marker_A*7");
            Assert.AreEqual(("Grave_Marker_A", 7), cemetery[0]);
            List<(string Id, int Count)> cave = MapLabArtBuilder.FeatureParts(" Cave_Entrance_A + Mushroom_Clump_A*2 ");
            Assert.AreEqual(2, cave.Count);
            Assert.AreEqual(("Cave_Entrance_A", 1), cave[0]);
            Assert.AreEqual(("Mushroom_Clump_A", 2), cave[1]);
            Assert.AreEqual(24, MapLabArtBuilder.FeatureParts("X*999")[0].Count, "Предел частей");
            Assert.IsEmpty(MapLabArtBuilder.FeatureParts(""));
        }

        // ---------- Камера ----------

        [Test]
        public void Camera_OldZoom8IsClamped_AndPitchIs50AtActualMax()
        {
            MapLabSettings s = Settings();
            try
            {
                using (Scene scene = new Scene(s))
                {
                    Assert.IsTrue(scene.View.Generate(null), scene.View.LastError);
                    MapLabCameraRig rig = scene.Rig;
                    Assert.AreEqual(8f, rig.ZoomLimits.y, "Сохранённый предел рига — прежний 8");
                    // 12У-C: Zoom считается от «всей карты», предел растёт со стороной
                    // мира — ближний кадр охватывает столько же мира, сколько у 256.
                    float Expected(float m) => Mathf.Clamp(m, 2f, 4f) * Mathf.Max(1f, s.WorldSide / 256f);
                    Assert.AreEqual(Expected(3f), rig.MaxZoom, 1e-4f, "Фактический предел этапа 05 — 3 × сторона/256");
                    rig.Zoom = 99f;
                    Assert.AreEqual(Expected(3f), rig.Zoom, 1e-4f, "Zoom сверх предела не ограничен");
                    Assert.AreEqual(35f, rig.Pitch, 0.01f, "На фактическом пределе — 35°");
                    rig.ZoomAround(99f, new Vector2(0.5f, 0.5f), false);
                    Assert.AreEqual(Expected(3f), rig.Zoom, 1e-4f, "Приближение к курсору выходит за предел");
                    rig.Zoom = 1f;
                    Assert.AreEqual(60f, rig.Pitch, 0.01f, "Общий вид — 60°");
                    float previous = 90f;
                    for (float z = 1f; z <= rig.MaxZoom + 1e-3f; z *= 1.05f)
                    {
                        rig.Zoom = z;
                        Assert.LessOrEqual(rig.Pitch, previous + 1e-4f);
                        Assert.GreaterOrEqual(rig.Pitch, 35f - 1e-3f);
                        previous = rig.Pitch;
                    }
                    // Предел настраивается в 2–4, наклон 35° — всегда на нём.
                    foreach (float max in new[] { 2f, 4f })
                    {
                        s.Stage05.ZoomMax = max;
                        rig.Zoom = 99f;
                        Assert.AreEqual(Expected(max), rig.Zoom, 1e-4f);
                        Assert.AreEqual(35f, rig.Pitch, 0.01f);
                    }
                    s.Stage05.ZoomMax = 9f;
                    Assert.AreEqual(Expected(4f), rig.MaxZoom, 1e-4f, "Вне диапазона 2–4");
                    // На этапе 04 предел прежний.
                    s.Stage03.Look = MapLabLook.Stage04;
                    Assert.AreEqual(8f, rig.MaxZoom, 1e-4f);
                }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void HouseArtAndHeroSet_DoNotChangeAcrossZoom()
        {
            MapLabSettings s = Settings();
            if (s.Stage05.Art == null || AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(MapLabArtImport.Stage05MarkerPath) == null)
            {
                Object.DestroyImmediate(s);
                Assert.Ignore("Набор этапа 05 не подключён локально (PNG не в git).");
            }
            try
            {
                using (Scene scene = new Scene(s))
                {
                    Assert.IsTrue(scene.View.Generate(null), scene.View.LastError);
                    string Houses()
                    {
                        List<string> ids = new List<string>();
                        foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in scene.View.ArtPlan.Cards)
                            if (pair.Key.Category == MapLabArtCategory.Building)
                                ids.Add(pair.Key.Id + "×" + pair.Value.Count);
                        ids.Sort(System.StringComparer.Ordinal);
                        return string.Join(",", ids);
                    }
                    string atStart = null, hero = null;
                    MapLabMarker marker = scene.View.Marker;
                    foreach (float z in new[] { 0.6f, 1f, 1.7f, 2.4f, 3f, 8f })
                    {
                        scene.Rig.Zoom = z;
                        scene.View.OnCameraChanged();
                        string houses = Houses();
                        Assert.IsNotEmpty(houses, "Нет нарисованных домов");
                        atStart = atStart ?? houses;
                        Assert.AreEqual(atStart, houses, $"Набор кадров домов сменился на Zoom {z}");
                        if (marker != null && marker.CurrentEntry != null)
                        {
                            Assert.AreEqual(35f, marker.PitchSetPitch, "Герой — только набор P35");
                            hero = hero ?? marker.CurrentEntry.Id;
                            Assert.AreEqual(hero, marker.CurrentEntry.Id, "Кадр героя сменился от приближения");
                        }
                    }
                    // Ракурс выбран по подходу: вход смотрит к дороге/воротам (положительная проекция).
                    MapLabData d = scene.View.Data;
                    int houses05 = 0;
                    foreach (MapLabPlacement o in d.Objects)
                    {
                        if (o.Kind != MapLabObjectKind.House || o.Yard <= 0)
                            continue;
                        houses05++;
                        MapLabYardRect yard = d.YardRects[o.Yard - 1];
                        Vector2 center = new Vector2(o.Position.x, o.Position.z);
                        Vector2 back = MapLabGeometry.LeftNormal(yard.AxisU);
                        Vector2 frontDir = Vector2.Dot(back, yard.Center - center) > 0f ? -back : back;
                        Assert.Greater(Vector2.Dot(o.Entrance, frontDir) + 0.5f, 0f, $"Вход двора {o.Yard} смотрит от подхода");
                    }
                    Assert.GreaterOrEqual(houses05, 3, "Деревня этапа 05 слишком мала");
                }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Ограды ----------

        [Test]
        public void FenceGraph_UniqueNodesAndEdges_SharedBoundaryOnce_GatesClear()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabFenceGraph g = d.FenceGraph;
                Assert.IsNotNull(g, "Нет графа оград");
                Assert.Greater(g.Edges.Count, 10);
                // R03: ограда — не у каждого дома: 1–3 просторных участка на деревню.
                HashSet<int> fencedYards = new HashSet<int>();
                foreach (MapLabFenceEdge e in g.Edges)
                    fencedYards.Add(e.Yard);
                int houses = d.Objects.FindAll(o => o.Kind == MapLabObjectKind.House && o.Yard > 0).Count;
                Assert.That(fencedYards.Count, Is.InRange(1, 3), "Число огороженных территорий вне бюджета");
                Assert.Less(fencedYards.Count * 2, houses + 1, "Большинство изб должно оставаться без ограды");
                HashSet<long> keys = new HashSet<long>();
                foreach (MapLabFenceEdge e in g.Edges)
                {
                    Assert.AreNotEqual(e.A, e.B);
                    long key = (long)Mathf.Min(e.A, e.B) * 100000 + Mathf.Max(e.A, e.B);
                    Assert.IsTrue(keys.Add(key), "Ребро повторяется");
                }
                for (int i = 0; i < g.Nodes.Count; i++)
                    for (int j = i + 1; j < g.Nodes.Count; j++)
                        Assert.Greater((g.Nodes[i] - g.Nodes[j]).sqrMagnitude, 0.3f * 0.3f, "Два столба в одном месте");
                List<MapLabPlacement> buildings = d.Objects.FindAll(o => o.Kind == MapLabObjectKind.House || o.Kind == MapLabObjectKind.Barn);
                foreach (MapLabFenceEdge e in g.Edges)
                {
                    Vector2 a = g.Nodes[e.A], b = g.Nodes[e.B];
                    for (int k = 0; k <= 8; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / 8f);
                        Assert.IsFalse(MapLabPlacer.InsideBuilding(buildings, p, 0f), $"{(e.Gate ? "Ворота" : "Ограда")} проходит через дом в {p}");
                        foreach (MapLabPolyline road in d.Roads)
                            if (road.Kind == MapLabRoadKind.Road)
                                Assert.Greater(MapLabGeometry.Nearest(road.Points, p, out _, out _, out _, out _), road.Width * 0.5f + 0.2f, $"{(e.Gate ? "Ворота" : "Ограда")} на дороге в {p}");
                    }
                    if (!e.Gate)
                        Assert.LessOrEqual(Vector2.Distance(a, b), s.Stage05.FenceSpan + 1e-3f, "Длинный пролёт без промежуточного узла");
                }
                // Две соседние площадки с общей стороной: общая сторона — один раз.
                MapLabData flat = new MapLabData(65, 64f);
                for (int i = 0; i < flat.WaterLevel.Length; i++) flat.WaterLevel[i] = float.NaN;
                flat.FenceGraph = new MapLabFenceGraph();
                List<(Vector2, Vector2, float, bool)> outlines = new List<(Vector2, Vector2, float, bool)>();
                void Rect(float x0, float z0, float x1, float z1, float gate)
                {
                    outlines.Add((new Vector2(x0, z0), new Vector2(x1, z0), gate, true));
                    outlines.Add((new Vector2(x1, z0), new Vector2(x1, z1), -1f, true));
                    outlines.Add((new Vector2(x1, z1), new Vector2(x0, z1), -1f, true));
                    outlines.Add((new Vector2(x0, z1), new Vector2(x0, z0), -1f, true));
                }
                Rect(10f, 10f, 20f, 20f, 3f);
                Rect(20f, 10f, 30f, 20f, 6f);
                MapLabPlacer.BuildFenceGraph(flat, s, outlines, new List<MapLabPlacement>());
                MapLabFenceGraph g2 = flat.FenceGraph;
                int onShared = 0;
                foreach (MapLabFenceEdge e in g2.Edges)
                {
                    Vector2 a = g2.Nodes[e.A], b = g2.Nodes[e.B];
                    if (Mathf.Abs(a.x - 20f) < 0.05f && Mathf.Abs(b.x - 20f) < 0.05f)
                        onShared++;
                }
                Assert.Greater(g2.SharedRemoved, 0, "Общая граница не распознана");
                Assert.AreEqual(Mathf.CeilToInt(10f / s.Stage05.FenceSpan), onShared, "Общая сторона построена дважды или с пропуском");
                int gates = g2.Edges.FindAll(e => e.Gate).Count;
                Assert.AreEqual(2, gates, "Ворота — явный проём с двумя узлами в каждом дворе");
                foreach (MapLabFenceEdge e in g2.Edges.FindAll(x => x.Gate))
                    Assert.AreEqual(s.Stage05.GateWidth, Vector2.Distance(g2.Nodes[e.A], g2.Nodes[e.B]), 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Мосты ----------

        // Синтетическая долина: русло шириной width поперёк направления angle.
        private static MapLabData Valley(float angleDeg, float width, out MapLabBridge bridge)
        {
            MapLabData d = new MapLabData(129, 128f);
            Vector2 dir = new Vector2(Mathf.Cos(angleDeg * Mathf.Deg2Rad), Mathf.Sin(angleDeg * Mathf.Deg2Rad));
            Vector2 across = new Vector2(-dir.y, dir.x);
            Vector2 c = new Vector2(64f, 64f);
            const float level = 1f;
            for (int z = 0; z < d.Resolution; z++)
                for (int x = 0; x < d.Resolution; x++)
                {
                    Vector2 p = d.GridToWorld(x, z);
                    float t = Mathf.Abs(Vector2.Dot(p - c, across));
                    float h = Mathf.Lerp(0f, 2.4f, Mathf.SmoothStep(0f, 1f, (t - width * 0.5f + 1.5f) / 4f));
                    int i = d.Index(x, z);
                    d.Heights[i] = h;
                    if (h < level + 0.4f)
                    {
                        d.WaterLevel[i] = level;
                        d.Water[i] = (byte)MapLabWaterKind.River;
                    }
                }
            // Концы моста генератора — у кромки воды (могут быть в воде).
            bridge = new MapLabBridge { EndA = c - across * (width * 0.5f - 0.5f), EndB = c + across * (width * 0.5f - 0.5f), DeckHeight = level + 0.7f, Width = 2.6f, WaterLevel = level };
            return d;
        }

        [Test]
        public void Bridge_LandsOnDryBanks_ContinuousDeck_ShortMediumLong()
        {
            MapLabSettings s = Settings();
            try
            {
                foreach ((float angle, float width) in new[] { (0f, 5f), (35f, 11f), (90f, 18f), (-60f, 24f) })
                {
                    MapLabData d = Valley(angle, width, out MapLabBridge bridge);
                    MapLabModules05.BridgeLayout l = MapLabModules05.Layout(d, s, bridge);
                    Assert.IsTrue(l.DryA && l.DryB, $"Опора моста в воде ({angle}°, {width})");
                    foreach (Vector2 p in new[] { l.LandA, l.LandB })
                    {
                        Assert.IsFalse(d.IsUnderWater(p.x, p.y, 0.05f), $"Опора в воде {p}");
                        Assert.Greater(d.SampleHeight(p.x, p.y), bridge.WaterLevel);
                    }
                    float length = Vector2.Distance(l.LandA, l.LandB);
                    Assert.Greater(length, width - 1.5f, "Настил короче русла");
                    Assert.AreEqual(length / s.Stage05.DeckPanelLength, l.Panels, 1f, "Настил не непрерывен (число панелей)");
                    for (int k = 0; k < l.TrestlePoints.Count; k++)
                    {
                        Assert.Greater(l.Deck - l.TrestleFloors[k], 0.6f, "Лишние козлы у берега");
                        Assert.AreEqual(d.SampleHeight(l.TrestlePoints[k].x, l.TrestlePoints[k].y), l.TrestleFloors[k], 1e-3f, "Ноги козел не до дна");
                    }
                    if (width > 15f)
                        Assert.Greater(l.Trestles, 0, "Длинный мост без опор");
                }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Берег ----------

        [Test]
        public void Shore_SharedBoundary_NoNaN_NoGridJumps_KeepsIslandAndChannel()
        {
            MapLabSettings s = Settings(MapLabPresetKind.ForestLake);
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabShore shore = new MapLabShore(d, s.Stage05.ShoreSmoothing);
                Assert.IsFalse(shore.HasInvalid(), "NaN в границе берега");
                int n = shore.N, jumps = 0, band = 0;
                for (int z = 1; z < n; z++)
                    for (int x = 1; x < n; x++)
                    {
                        float v = shore.Above[z * n + x];
                        if (Mathf.Abs(v) > 0.7f)
                            continue;
                        band++;
                        float l = shore.Above[z * n + x - 1], b = shore.Above[(z - 1) * n + x];
                        if (Mathf.Abs(l - v) > 1f || Mathf.Abs(b - v) > 1f)
                            jumps++;
                    }
                Assert.Greater(band, 100, "Нет береговой полосы");
                Assert.AreEqual(0, jumps, "Обрыв поля у кромки (источник «гармошки»)");
                MapLabInk.Stats stats;
                Mesh ink = MapLabInk.BuildShoreInk(d, s, shore, out stats);
                Assert.IsNotNull(ink, "Нет пера берега");
                foreach (Vector3 v in ink.vertices)
                    Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.y), "NaN в геометрии берега");
                Object.DestroyImmediate(ink);

                // Остров и узкая протока не исчезают от сглаживания.
                MapLabData lake = new MapLabData(65, 64f);
                for (int z = 0; z < lake.Resolution; z++)
                    for (int x = 0; x < lake.Resolution; x++)
                    {
                        Vector2 p = lake.GridToWorld(x, z);
                        float h = 0f;
                        if (Vector2.Distance(p, new Vector2(20f, 32f)) < 2f) h = 2f;          // остров
                        if (p.x > 40f && Mathf.Abs(p.y - 32f) > 0.8f) h = 2f;                  // суша с протокой 1.6
                        int i = lake.Index(x, z);
                        lake.Heights[i] = h;
                        if (h < 1f) { lake.WaterLevel[i] = 1f; lake.Water[i] = (byte)MapLabWaterKind.Lake; }
                    }
                MapLabShore small = new MapLabShore(lake, s.Stage05.ShoreSmoothing);
                Assert.Greater(small.Sample(20f, 32f), 0f, "Остров срезан");
                Assert.Less(small.Sample(52f, 32f), 0f, "Протока закрыта");
                Assert.Greater(small.Sample(52f, 36f), 0f);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Рисунок ----------

        [Test]
        public void Stage05Paint_SeamlessDeterministic_WashesStable()
        {
            MapLabSettings s = Settings();
            MapLabPalette palette = MapLabPalette.CreateFallback();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                // Пятно-источник: мягкий круг с градиентом альфы.
                Color32[] px = new Color32[64 * 48];
                for (int y = 0; y < 48; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float r = new Vector2((x - 32) / 32f, (y - 24) / 24f).magnitude;
                        px[y * 64 + x] = new Color32(150, 160, 110, (byte)(Mathf.Clamp01(1f - r) * 255));
                    }
                List<MapLabWash.Source> sources = new List<MapLabWash.Source> { MapLabWash.FromPixels("W", px, 64, 48, new Vector2(0.04f, 0.12f)) };
                s.Stage05.WashDensity = 25f;
                MapLabWash.Layer w1 = MapLabWash.Place(d, s, sources), w2 = MapLabWash.Place(d, s, sources);
                Assert.Greater(w1.Patches.Count, 0);
                Assert.AreEqual(w1.Patches.Count, w2.Patches.Count);
                for (int i = 0; i < w1.Patches.Count; i++)
                    Assert.AreEqual(w1.Patches[i].Center, w2.Patches[i].Center);
                foreach (MapLabWash.Patch p in w1.Patches)
                {
                    Assert.IsFalse(d.IsUnderWater(p.Center.x, p.Center.y, 0.5f), "Пятно в воде");
                    Assert.Less(d.SampleMask(d.Field, p.Center.x, p.Center.y), 0.2f, "Пятно на поле");
                }
                float tile = 32 * d.Cell;
                MapLabTiledPainter.Options options = new MapLabTiledPainter.Options { Washes = w1 };
                MapLabTiledPainter.Result a = MapLabTiledPainter.Paint(d, s, palette, tile, options);
                MapLabTiledPainter.Result b = MapLabTiledPainter.Paint(d, s, palette, tile, options);
                for (int i = 0; i < a.Tiles.Count; i++)
                    CollectionAssert.AreEqual(a.Tiles[i].Pixels, b.Tiles[i].Pixels, "Рисунок не детерминирован");
                int g = MapLabTiledPainter.Gutter, mismatched = 0;
                foreach (MapLabTiledPainter.Tile left in a.Tiles)
                {
                    MapLabTiledPainter.Tile right = a.Tiles.Find(t => t.X == left.X + 1 && t.Z == left.Z);
                    if (right == null)
                        continue;
                    for (int z = g; z < left.Size - g; z++)
                        for (int k = 0; k < g; k++)
                        {
                            Color32 p = left.Pixels[z * left.Size + left.Core + g + k], q = right.Pixels[z * right.Size + g + k];
                            if (Mathf.Abs(p.r - q.r) > 1 || Mathf.Abs(p.g - q.g) > 1 || Mathf.Abs(p.b - q.b) > 1)
                                mismatched++;
                        }
                }
                Assert.AreEqual(0, mismatched, "Мазки или пятна обрываются на стыке тайлов");
                // Светлый рисунок пятна не даёт белых заплат: краска не светлеет.
                Color meadow = new Color(0.5f, 0.55f, 0.3f);
                MapLabPaintFields.Sample sample = default;
                foreach (MapLabWash.Patch p in w1.Patches)
                {
                    Color c = MapLabWash.Apply(w1, p.Center, meadow, sample, s.Stage05.WashStrength);
                    Assert.LessOrEqual(c.grayscale, meadow.grayscale + 1e-3f, "Пятно осветляет луг");
                }
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Импорт ----------

        // Дом-силуэт: основание-параллелограмм F–L–R, стены, свесы за углами.
        private static byte[] HousePng(out Vector2 front, out Vector2 left, out Vector2 right)
        {
            const int w = 128, h = 100;
            front = new Vector2(60, 10);
            left = new Vector2(15, 25);
            right = new Vector2(110, 30);
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float bottom = -1f;
                if (x >= left.x && x <= front.x) bottom = Mathf.Lerp(left.y, front.y, (x - left.x) / (front.x - left.x));
                else if (x > front.x && x <= right.x) bottom = Mathf.Lerp(front.y, right.y, (x - front.x) / (right.x - front.x));
                else if (x >= left.x - 8 && x < left.x) bottom = left.y + 30;
                else if (x > right.x && x <= right.x + 8) bottom = right.y + 30;
                if (bottom < 0f)
                    continue;
                for (int y = Mathf.RoundToInt(bottom); y < 90; y++)
                    px[y * w + x] = new Color32(160, 120, 80, 255);
            }
            t.SetPixels32(px);
            byte[] png = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            return png;
        }

        [Test]
        public void Import_Revision5_Categories_Reuse_Registration_Reimport()
        {
            const string folder = "Assets/Experiments/MapLab/Tests/TempImport05";
            string root = Path.Combine(Path.GetTempPath(), "MapLabStage05Import_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                AssetDatabase.CreateFolder("Assets/Experiments/MapLab/Tests", "TempImport05");
                // Набор «прежнего этапа»: тот же файл героя уже подключён.
                Texture2D heroTex = new Texture2D(20, 30);
                AssetDatabase.CreateAsset(heroTex, folder + "/HeroTex.asset");
                Texture2D figure = new Texture2D(20, 30, TextureFormat.RGBA32, false);
                Color32[] fp = new Color32[20 * 30];
                for (int i = 0; i < fp.Length; i++)
                    fp[i] = (i % 20) > 4 && (i % 20) < 15 && i / 20 > 3 ? new Color32(150, 60, 50, 255) : new Color32(0, 0, 0, 0);
                figure.SetPixels32(fp);
                byte[] heroPng = figure.EncodeToPNG();
                Object.DestroyImmediate(figure);
                string heroSha = MapLabArtImport.Sha256(heroPng);
                MapLabArtSet reuse = ScriptableObject.CreateInstance<MapLabArtSet>();
                reuse.Entries.Add(new MapLabArtEntry { Id = "Old_Hero", Category = MapLabArtCategory.Figure, Sha256 = heroSha, Texture = heroTex, Direction = "Front", PitchDownDegrees = 35f, Pivot = new Vector2(0.4f, 0.15f), PivotEdited = true });
                AssetDatabase.CreateAsset(reuse, folder + "/Reuse.asset");
                MapLabArtImport.Profile profile = new MapLabArtImport.Profile
                {
                    Name = "тест 05", Folder = folder + "/Art", SetPath = folder + "/Set.asset", MarkerPath = folder + "/Markers.asset",
                    LastFolderKey = "KingdomSurvival.MapLab.Test05", MinRevision = 5, MaxRevision = 99, BootHintPivots = true,
                    ReuseSetPath = folder + "/Reuse.asset", RegisterBuildings = true,
                };
                byte[] house = HousePng(out Vector2 front, out Vector2 left, out Vector2 right);
                Texture2D decalTex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                Color32[] dp = new Color32[256];
                for (int i = 0; i < dp.Length; i++) dp[i] = new Color32(200, 210, 170, (byte)(i % 16 * 15));
                decalTex.SetPixels32(dp);
                byte[] decal = decalTex.EncodeToPNG();
                Object.DestroyImmediate(decalTex);
                Directory.CreateDirectory(Path.Combine(root, "Assets", "X"));
                File.WriteAllBytes(Path.Combine(root, "Assets", "X", "House.png"), house);
                File.WriteAllBytes(Path.Combine(root, "Assets", "X", "Decal.png"), decal);
                File.WriteAllBytes(Path.Combine(root, "Assets", "X", "Rail.png"), decal);
                File.WriteAllBytes(Path.Combine(root, "Assets", "X", "Hero.png"), heroPng);
                string Item(string id, string file, string category, int w, int hh, byte[] bytes, string extra) =>
                    $"{{\"id\":\"{id}\",\"path\":\"Assets/X/{file}\",\"category\":\"{category}\",\"width\":{w},\"height\":{hh},\"sha256\":\"{MapLabArtImport.Sha256(bytes)}\",\"pivot_hint_unity_bottom_left\":[0.5,0.1]{extra}}}";
                string manifest = "{\"schema_version\":1,\"package_revision\":5,\"art_revision\":2,\"package_id\":\"T5\",\"items\":[" +
                    Item("T_House_FrontLeft", "House.png", "building", 128, 100, house, ",\"kind\":\"House\",\"role\":\"house\",\"variant_group\":\"T_House\",\"view_tag\":\"FrontLeft\",\"entrance_location\":\"front_gable\",\"world_height_hint\":4,\"intended_pitch_down_degrees\":50") + "," +
                    Item("T_Wash", "Decal.png", "decal", 16, 16, decal, ",\"kind\":\"Wash\",\"role\":\"watercolor_patch\",\"opacity_start_range\":[0.05,0.1],\"intended_pitch_down_degrees\":null") + "," +
                    Item("T_Rail", "Rail.png", "fence_module", 16, 16, decal, ",\"kind\":\"Rails\",\"role\":\"fence_rail_panel\",\"authoring_plane\":\"vertical_front\",\"world_height_hint\":0.8,\"connection_hint\":{\"visual_bbox\":[0.1,0.2,0.9,0.7]}") + "," +
                    Item("T_Hero_Front", "Hero.png", "figure", 20, 30, heroPng, ",\"kind\":\"Hero\",\"direction\":\"Front\",\"intended_pitch_down_degrees\":35,\"world_height_hint\":2.8") +
                    "]}";
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), manifest, Encoding.UTF8);
                MapLabArtImport.Report first = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(first.Ok, first.ToString());
                MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                Assert.AreEqual(4, set.Entries.Count);
                MapLabArtEntry h = set.Find("T_House_FrontLeft"), dcl = set.Find("T_Wash"), rail = set.Find("T_Rail"), hero = set.Find("T_Hero_Front");
                Assert.AreEqual(MapLabArtCategory.Building, h.Category);
                Assert.AreEqual(MapLabArtCategory.Decal, dcl.Category);
                Assert.AreEqual(MapLabArtCategory.FenceModule, rail.Category);
                Assert.AreEqual("FrontLeft", h.ViewTag);
                Assert.AreEqual("front_gable", h.EntranceLocation);
                Assert.AreEqual("vertical_front", rail.AuthoringPlane);
                Assert.AreEqual(new Vector2(0.05f, 0.1f), dcl.OpacityRange);
                Assert.AreEqual(0.1f, rail.ModuleRect.xMin, 1e-4f);
                Assert.AreEqual(0.3f, rail.ModuleRect.yMin, 1e-4f, "visual_bbox (левый верх) → рамка (левый низ)");
                Assert.IsTrue(((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(dcl.Texture))).isReadable, "Декаль должна читаться для запекания");
                // Регистрация дома: опора — центр основания (L + R) / 2, не нижний пиксель.
                Assert.IsTrue(h.BaseRegistered, "Углы основания не найдены");
                Vector2 expected = new Vector2((left.x + right.x) * 0.5f / 128f, (left.y + right.y) * 0.5f / 100f);
                Assert.AreEqual(expected.x, h.Pivot.x, 0.03f);
                Assert.AreEqual(expected.y, h.Pivot.y, 0.04f);
                Assert.Greater(h.Pivot.y, h.SolidRect.yMin + 0.05f, "Опора широкого дома — по нижнему пикселю");
                Assert.Less(h.EntranceDirection.x, 0f, "FrontLeft: вход на видимом левом фронтоне");
                // Герой — из прежнего набора по SHA, файл не дублируется, калибровка сохранена.
                Assert.AreSame(heroTex, hero.Texture);
                Assert.AreEqual(profile.ReuseSetPath, hero.ReusedFrom);
                Assert.IsFalse(File.Exists(Path.GetFullPath(profile.Folder + "/X/Hero.png")), "Кадр героя скопирован повторно");
                Assert.AreEqual(new Vector2(0.4f, 0.15f), hero.Pivot);
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(h.Texture));
                h.Pivot = new Vector2(0.52f, 0.3f);
                h.PivotEdited = true;
                EditorUtility.SetDirty(set);
                MapLabArtImport.Report second = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(second.Ok, second.ToString());
                set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                Assert.AreEqual(4, set.Entries.Count, "Повторный импорт без дубликатов");
                Assert.AreEqual(new Vector2(0.52f, 0.3f), set.Find("T_House_FrontLeft").Pivot, "Повторный импорт сбросил калибровку");
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set.Find("T_House_FrontLeft").Texture)), "GUID картинки сменился");
                // Пакет другой ревизии в этот профиль не попадает.
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), manifest.Replace("\"package_revision\":5", "\"package_revision\":4"), Encoding.UTF8);
                Assert.IsFalse(MapLabArtImport.Import(root, null, profile).Ok);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
