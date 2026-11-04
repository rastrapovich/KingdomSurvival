using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Правки карты этапа 05 (04.11.2026): акварельные пятна разного размера и
    // рисунка, тропинки от ворот доходят до дороги.
    public sealed class MapLabStage05MapFixTests
    {
        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            return s;
        }

        private static MapLabWash.Source Blob(string id)
        {
            Color32[] px = new Color32[32 * 24];
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = new Vector2((x - 16f) / 16f, (y - 12f) / 12f).magnitude;
                    px[y * 32 + x] = new Color32(90, 110, 60, (byte)(Mathf.Clamp01(1f - d) * 255f));
                }
            return MapLabWash.FromPixels(id, px, 32, 24, new Vector2(0.04f, 0.12f));
        }

        [Test]
        public void Wash_SizesDiffer_NeighboursUseDifferentDrawings()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                List<MapLabWash.Source> sources = new List<MapLabWash.Source> { Blob("a"), Blob("b"), Blob("c"), Blob("d") };
                MapLabWash.Layer layer = MapLabWash.Place(d, s, sources);
                Assert.Greater(layer.Patches.Count, 8, "мало пятен для проверки");
                float min = float.MaxValue, max = 0f;
                foreach (MapLabWash.Patch p in layer.Patches)
                {
                    float size = Mathf.Max(p.HalfSize.x, p.HalfSize.y);
                    min = Mathf.Min(min, size);
                    max = Mathf.Max(max, size);
                    Assert.LessOrEqual(Mathf.Max(p.HalfSize.x, p.HalfSize.y) / Mathf.Min(p.HalfSize.x, p.HalfSize.y), 2.2f, "пятно сильно растянуто");
                }
                Assert.Greater(max / min, 2.2f, "размеры пятен почти одинаковые");
                // Соседи похожего размера не повторяют один рисунок (кроме нарочного наложения — другой рисунок).
                for (int i = 0; i < layer.Patches.Count; i++)
                    for (int j = i + 1; j < layer.Patches.Count; j++)
                    {
                        MapLabWash.Patch a = layer.Patches[i], b = layer.Patches[j];
                        if (a.Source != b.Source)
                            continue;
                        float reach = Mathf.Max(a.HalfSize.x, a.HalfSize.y) + Mathf.Max(b.HalfSize.x, b.HalfSize.y);
                        Assert.Greater((a.Center - b.Center).magnitude, reach, "рядом два пятна одного рисунка");
                    }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void YardGates_PathsReachRoad()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                int houses = 0;
                foreach (MapLabPlacement o in d.Objects)
                    if (o.Kind == MapLabObjectKind.House)
                        houses++;
                Assume.That(houses > 0, "в деревне нет домов");
                int reaching = 0;
                foreach (MapLabPolyline path in d.YardPaths)
                {
                    Vector2 end = path.Points[path.Points.Count - 1];
                    foreach (MapLabPolyline road in d.Roads)
                    {
                        if (road.Widening)
                            continue;
                        bool on = false;
                        for (int i = 0; i + 1 < road.Points.Count && !on; i++)
                            on = MapLabGeometry.DistanceToSegment(end, road.Points[i], road.Points[i + 1], out _) <= road.Width * 0.5f + 0.05f;
                        if (on)
                        {
                            reaching++;
                            break;
                        }
                    }
                }
                Assert.GreaterOrEqual(reaching, Mathf.CeilToInt(houses * 0.8f), "тропинки от ворот не доходят до дороги");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void ObjectShadows_ParallelBands_EqualSequential()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = new MapLabData(65, 64f);
                List<MapLabObjectShadows.Caster> casters = new List<MapLabObjectShadows.Caster>();
                System.Random rng = new System.Random(5);
                for (int i = 0; i < 400; i++)
                {
                    Vector3 p = new Vector3((float)rng.NextDouble() * 64f, 0f, (float)rng.NextDouble() * 64f);
                    if (i % 37 == 0)
                        casters.Add(new MapLabObjectShadows.Caster { Base = p, Building = true, Size = new Vector2(3.4f, 2.2f), Yaw = (float)rng.NextDouble() * 90f, Height = 3.4f });
                    else
                        casters.Add(new MapLabObjectShadows.Caster { Base = p, Radius = 0.6f + (float)rng.NextDouble() * 1.2f, CenterHeight = 2.5f, ContactRadius = 0.4f });
                }
                MapLabObjectShadows.Sequential = true;
                MapLabObjectShadows.Result a = MapLabObjectShadows.Compute(d, s, casters);
                MapLabObjectShadows.Sequential = false;
                MapLabObjectShadows.Result b = MapLabObjectShadows.Compute(d, s, casters);
                Assert.AreEqual(a.Cast.Length, b.Cast.Length);
                for (int i = 0; i < a.Cast.Length; i++)
                {
                    Assert.AreEqual(a.Cast[i], b.Cast[i], "падающая тень отличается в параллельной сборке");
                    Assert.AreEqual(a.Contact[i], b.Contact[i], "контактная тень отличается в параллельной сборке");
                }
            }
            finally
            {
                MapLabObjectShadows.Sequential = false;
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void WideFrame_PlayRectFieldsForestAndCameraFootprint()
        {
            MapLabSettings s = Settings();
            GameObject cameraObject = new GameObject("MapLab Camera (test)");
            GameObject mapObject = new GameObject("MapLab (test)");
            try
            {
                float frame = s.FrameWidth;
                Assert.Greater(frame, 40f, "окружение уже ближнего кадра");
                cameraObject.AddComponent<Camera>().aspect = 16f / 9f;
                MapLabCameraRig rig = cameraObject.AddComponent<MapLabCameraRig>();
                mapObject.SetActive(false);
                MapLabView view = mapObject.AddComponent<MapLabView>();
                view.GenerateOnLoad = false;
                view.Settings = s;
                view.CameraRig = rig;
                mapObject.SetActive(true);
                Assert.IsTrue(view.Generate(null), view.LastError);
                MapLabData d = view.Data;
                Assert.AreEqual(s.General.MapSize + 2f * frame, d.Size, 1e-3f, "сторона данных — игровая область и окружение");
                Assert.AreEqual(frame, d.PlayRect.xMin, 1e-3f);
                Assert.AreEqual(s.General.MapSize, d.PlayRect.width, 1e-3f, "игровая область не уменьшилась");
                // Поля — только в игровой области.
                int outsideFields = 0, cornerForest = 0, cornerSamples = 0;
                for (int z = 0; z < d.Resolution; z++)
                    for (int x = 0; x < d.Resolution; x++)
                    {
                        Vector2 p = d.GridToWorld(x, z);
                        int i = d.Index(x, z);
                        if (!d.PlayRect.Contains(p) && d.Field[i] > 0.5f)
                            outsideFields++;
                        bool corner = (p.x < frame * 0.5f || p.x > d.Size - frame * 0.5f) && (p.y < frame * 0.5f || p.y > d.Size - frame * 0.5f);
                        if (corner)
                        {
                            cornerSamples++;
                            if (d.Forest[i] > 0.5f)
                                cornerForest++;
                        }
                    }
                Assert.AreEqual(0, outsideFields, "поле за игровой областью");
                Assert.Greater(cornerForest, cornerSamples * 0.6f, "углы не закрыты лесом");
                // Отпечаток ближнего кадра у каждого угла — внутри данных карты.
                rig.Zoom = rig.MaxZoom;
                foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(d.Size, 0f), new Vector2(0f, d.Size), new Vector2(d.Size, d.Size) })
                {
                    rig.Target = new Vector3(corner.x, 0f, corner.y);
                    rig.ClampTarget();
                    rig.Apply();
                    Camera cam = rig.Camera;
                    float halfWidth = cam.orthographicSize * cam.aspect;
                    float halfDepth = cam.orthographicSize / Mathf.Sin(rig.Pitch * Mathf.Deg2Rad);
                    Assert.GreaterOrEqual(rig.Target.x - halfWidth, -1e-3f, "край данных в кадре слева");
                    Assert.LessOrEqual(rig.Target.x + halfWidth, d.Size + 1e-3f, "край данных в кадре справа");
                    Assert.GreaterOrEqual(rig.Target.z - halfDepth, -1e-3f, "край данных в кадре снизу");
                    Assert.LessOrEqual(rig.Target.z + halfDepth, d.Size + 1e-3f, "край данных в кадре сверху");
                }
                view.Clear();
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void PaintStreaming_DetailTileEqualsFullPaint()
        {
            MapLabSettings s = Settings();
            MapLabPalette palette = MapLabPalette.CreateFallback();
            try
            {
                s.General.MapSize = 96f;
                s.Stage05.WideFrame = false;
                s.Stage03.PaintTexelsPerUnit = 6f;
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabTiledPainter.Options options = new MapLabTiledPainter.Options();
                MapLabTiledPainter.Result full = MapLabTiledPainter.Paint(d, s, palette, 32f, options);
                MapLabTiledPainter.Session session = MapLabTiledPainter.Prepare(d, s, palette, 32f, options, full.Fields);
                foreach (int index in new[] { 0, full.Tiles.Count / 2, full.Tiles.Count - 1 })
                {
                    MapLabTiledPainter.Tile reference = full.Tiles[index];
                    MapLabTiledPainter.Tile tile = MapLabTiledPainter.MakeTile(session, reference.X, reference.Z, s.Stage03.PaintTexelsPerUnit);
                    Color32[] pixels = MapLabTiledPainter.PaintTile(session, tile);
                    Assert.AreEqual(reference.Rect, tile.Rect, "привязка тайла отличается");
                    Assert.AreEqual(reference.Pixels.Length, pixels.Length);
                    for (int i = 0; i < pixels.Length; i++)
                        Assert.AreEqual(reference.Pixels[i], pixels[i], "подгруженный тайл отличается от полной покраски");
                }
                // Облегчённый обзор — та же разметка тайлов, другая плотность.
                MapLabTiledPainter.Result low = MapLabTiledPainter.PaintAll(session, 2f);
                Assert.AreEqual(full.Tiles.Count, low.Tiles.Count);
                Assert.Less(low.Tiles[0].Core, full.Tiles[0].Core);
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void PaintFields_Blocks_EvictedAndRecomputed_SameValues()
        {
            MapLabSettings s = Settings();
            MapLabPalette palette = MapLabPalette.CreateFallback();
            int keep = MapLabPaintFields.BudgetMb;
            try
            {
                s.General.MapSize = 160f;
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabPaintFields eager = new MapLabPaintFields(d, s, palette);
                MapLabPaintFields.BudgetMb = 1;
                MapLabPaintFields lazy = new MapLabPaintFields(d, s, palette);
                Assert.Less(lazy.ResidentBlocks, lazy.TotalBlocks, "блоки не ленивые");
                System.Random rng = new System.Random(3);
                for (int k = 0; k < 200; k++)
                {
                    Vector2 p = new Vector2((float)rng.NextDouble() * d.Size, (float)rng.NextDouble() * d.Size);
                    eager.Get(p, out MapLabPaintFields.Sample a);
                    lazy.Get(p, out MapLabPaintFields.Sample b);
                    Assert.AreEqual(a.R, b.R);
                    Assert.AreEqual(a.FieldMask, b.FieldMask, "сглаженная маска поля зависит от блока");
                    Assert.AreEqual(a.Above, b.Above);
                    Assert.AreEqual(a.MainRoad, b.MainRoad);
                    Assert.AreEqual(a.Blot, b.Blot);
                }
                Assert.LessOrEqual(lazy.ResidentBlocks, 16, "бюджет блоков не соблюдён");
            }
            finally
            {
                MapLabPaintFields.BudgetMb = keep;
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(s);
            }
        }
    }
}
