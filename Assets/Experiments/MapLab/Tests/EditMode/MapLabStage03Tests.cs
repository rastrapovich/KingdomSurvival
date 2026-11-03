using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Этап 03: отдельный импорт без перезаписи этапа 02 и без потери
    // калибровки, стабильность размещения при смене языка, пары ракурсов
    // домов, наклон 35° на фактическом пределе приближения, стыки тайлов
    // покраски, внешний периметр пера на развилке, устойчивость акцентов и
    // теней.
    public sealed class MapLabStage03Tests
    {
        private static MapLabSettings Settings(MapLabLook look)
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.General.PaintResolution = 512;
            s.Stage03.Look = look;
            s.Stage03.PaintTexelsPerUnit = 6f;
            return s;
        }

        // ---------- Импорт ----------

        [Test]
        public void Import_Stage03PackageRejectedByStage02Profile()
        {
            string root = Path.Combine(Path.GetTempPath(), "MapLabStage03Test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), "{\"schema_version\":1,\"package_revision\":3,\"package_id\":\"T3\",\"items\":[]}", Encoding.UTF8);
                MapLabArtImport.Report report = MapLabArtImport.Import(root, null, MapLabArtImport.Stage02);
                Assert.IsFalse(report.Ok);
                Assert.IsTrue(report.Errors[0].Contains("ревизии 3"), report.ToString());
                Assert.AreEqual(0, report.Imported.Count);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Import_ReimportKeepsCalibration_NoDuplicates_RejectsBadHash()
        {
            const string folder = "Assets/Experiments/MapLab/Tests/TempImport03";
            MapLabArtImport.Profile profile = new MapLabArtImport.Profile
            {
                Name = "тест 03", Folder = folder, SetPath = folder + "/Set.asset", MarkerPath = folder + "/Markers.asset",
                LastFolderKey = "KingdomSurvival.MapLab.Test03", MinRevision = 3, MaxRevision = 99,
            };
            string root = Path.Combine(Path.GetTempPath(), "MapLabStage03Import_" + System.Guid.NewGuid().ToString("N"));
            Texture2D image = new Texture2D(16, 24, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[16 * 24];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (i % 16) > 3 && (i % 16) < 12 && i / 16 > 2 ? new Color32(90, 110, 60, 255) : new Color32(0, 0, 0, 0);
                image.SetPixels32(pixels);
                byte[] png = image.EncodeToPNG();
                Directory.CreateDirectory(Path.Combine(root, "Assets", "Buildings"));
                File.WriteAllBytes(Path.Combine(root, "Assets", "Buildings", "H50.png"), png);
                File.WriteAllBytes(Path.Combine(root, "Assets", "Buildings", "H35.png"), png);
                string sha = MapLabArtImport.Sha256(png);
                string Item(string id, string file, string hash, int pitch) =>
                    $"{{\"id\":\"{id}\",\"path\":\"Assets/Buildings/{file}\",\"category\":\"building\",\"kind\":\"House\",\"role\":\"house\",\"width\":16,\"height\":24,\"sha256\":\"{hash}\"," +
                    $"\"variant_group\":\"House_T\",\"intended_pitch_down_degrees\":{pitch},\"pivot_hint_unity_bottom_left\":[0.5,0.1],\"world_height_hint\":4,\"provenance\":\"new_stage03_imagegen\",\"extra_new_field\":[1,2]}}";
                void Manifest(string hash35) => File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName),
                    "{\"schema_version\":1,\"package_revision\":3,\"package_id\":\"T3\",\"items\":[" + Item("H_P50", "H50.png", sha, 50) + "," + Item("H_P35", "H35.png", hash35, 35) + "]}", Encoding.UTF8);
                Manifest(sha);
                MapLabArtImport.Report first = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(first.Ok, first.ToString());
                MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                Assert.AreEqual(2, set.Entries.Count);
                MapLabArtEntry p50 = set.Find("H_P50");
                Assert.AreEqual("House_T", p50.VariantGroup);
                Assert.AreEqual(50f, p50.PitchDownDegrees);
                Assert.AreSame(p50, set.FindView("House_T", 47f));
                Assert.AreSame(set.Find("H_P35"), set.FindView("House_T", 36f));
                // Калибровка художника.
                p50.Pivot = new Vector2(0.42f, 0.3f);
                p50.PivotEdited = true;
                p50.WorldHeight = 5.5f;
                EditorUtility.SetDirty(set);

                // Повтор с испорченным хешем второго файла: он не подключается
                // заново, первая запись не теряет калибровку, дубликатов нет.
                Manifest(new string('0', 64));
                MapLabArtImport.Report second = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(second.Errors.Exists(e => e.Contains("SHA-256")), second.ToString());
                set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                Assert.AreEqual(2, set.Entries.Count);
                p50 = set.Find("H_P50");
                Assert.AreEqual(new Vector2(0.42f, 0.3f), p50.Pivot);
                Assert.AreEqual(5.5f, p50.WorldHeight);
                Assert.IsTrue(set.Find("H_P35").HasTexture, "Прежняя проверенная картинка остаётся");
            }
            finally
            {
                Object.DestroyImmediate(image);
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        // ---------- Размещение и пары ракурсов ----------

        [Test]
        public void Look_DoesNotMovePlacement_AndHousePairKeepsVariant()
        {
            MapLabSettings a = Settings(MapLabLook.Stage02);
            MapLabSettings b = Settings(MapLabLook.Stage03);
            List<Object> garbage = new List<Object> { a, b };
            try
            {
                MapLabData da = MapLabGenerator.Generate(a, null);
                MapLabData db = MapLabGenerator.Generate(b, null);
                Assert.AreEqual(da.Objects.Count, db.Objects.Count);
                for (int i = 0; i < da.Objects.Count; i++)
                {
                    Assert.AreEqual(da.Objects[i].Kind, db.Objects[i].Kind);
                    Assert.AreEqual(da.Objects[i].Position, db.Objects[i].Position);
                    Assert.AreEqual(da.Objects[i].Yaw, db.Objects[i].Yaw);
                }
                CollectionAssert.AreEqual(da.Heights, db.Heights);

                // Дома стоят под углом конька своего варианта (данные настроек).
                int houses = 0;
                foreach (MapLabPlacement o in db.Objects)
                {
                    if (o.Kind != MapLabObjectKind.House)
                        continue;
                    houses++;
                    float drawn = b.Settlement.DrawnRidgeYawVariants[o.Variant % 4];
                    float axis = Mathf.Abs(Mathf.DeltaAngle(o.Yaw, drawn));
                    Assert.IsTrue(axis < 0.01f || Mathf.Abs(axis - 180f) < 0.01f, $"Изба варианта {o.Variant}: {o.Yaw}° вместо {drawn}°");
                }
                Assert.GreaterOrEqual(houses, 3);

                MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
                garbage.Add(set);
                foreach (string group in new[] { "House_Thatch_A", "House_Thatch_B", "House_Shingle_A", "Barn_A" })
                    foreach (int pitch in new[] { 50, 35 })
                    {
                        Texture2D t = new Texture2D(4, 4);
                        garbage.Add(t);
                        set.Entries.Add(new MapLabArtEntry
                        {
                            Id = group + "_P" + pitch, Category = MapLabArtCategory.Building, Kind = group.StartsWith("Barn") ? MapLabObjectKind.Barn : MapLabObjectKind.House,
                            VariantGroup = group, PitchDownDegrees = pitch, Texture = t, Width = 4, Height = 4, SolidRect = new Rect(0, 0, 1, 0.8f),
                        });
                    }
                MapLabArtBuilder.Plan p50 = new MapLabArtBuilder.Plan(), p35 = new MapLabArtBuilder.Plan();
                MapLabArtBuilder.PlanHouses03(db, b, set, 50f, p50);
                MapLabArtBuilder.PlanHouses03(db, b, set, 35f, p35);
                Dictionary<int, string> groupAt50 = new Dictionary<int, string>();
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in p50.Cards)
                {
                    Assert.AreEqual(50f, pair.Key.PitchDownDegrees);
                    foreach (MapLabArtInstance inst in pair.Value)
                        groupAt50[inst.ObjectIndex] = pair.Key.VariantGroup;
                }
                int matched = 0;
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in p35.Cards)
                {
                    Assert.AreEqual(35f, pair.Key.PitchDownDegrees);
                    foreach (MapLabArtInstance inst in pair.Value)
                    {
                        Assert.AreEqual(groupAt50[inst.ObjectIndex], pair.Key.VariantGroup, "Смена наклона сменила тип дома");
                        Assert.AreEqual(db.Objects[inst.ObjectIndex].Position, inst.Base);
                        matched++;
                    }
                }
                Assert.AreEqual(groupAt50.Count, matched);
                Assert.IsTrue(p35.Replaced.Contains(MapLabObjectKind.House), "При наличии кадров нет скрытого возврата к геометрии");
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        // ---------- Камера ----------

        [Test]
        public void Camera_PitchReaches35AtActualMaxZoom_AndHouseViewHasHysteresis()
        {
            MapLabSettings s = Settings(MapLabLook.Stage03);
            Assert.IsTrue(s.Camera.PitchByZoom);
            GameObject cameraObject = new GameObject("MapLabCamera (test)");
            cameraObject.AddComponent<Camera>();
            MapLabCameraRig rig = cameraObject.AddComponent<MapLabCameraRig>();
            GameObject mapObject = new GameObject("MapLab (test)");
            mapObject.SetActive(false);
            MapLabView view = mapObject.AddComponent<MapLabView>();
            view.GenerateOnLoad = false;
            view.Settings = s;
            view.CameraRig = rig;
            mapObject.SetActive(true);
            try
            {
                Assert.IsTrue(view.Generate(null), view.LastError);
                rig.Zoom = rig.MaxZoom;
                Assert.AreEqual(35f, rig.Pitch, 0.01f, "На наибольшем фактическом приближении — 35°");
                rig.Zoom = 1f;
                Assert.AreEqual(60f, rig.Pitch, 0.01f);
                // Наклон монотонен на всём диапазоне рига.
                float previous = 90f;
                for (float z = 1f; z <= rig.MaxZoom; z *= 1.1f)
                {
                    rig.Zoom = z;
                    Assert.LessOrEqual(rig.Pitch, previous + 1e-4f);
                    previous = rig.Pitch;
                }

                // Кадр домов: порог 42.5°, зазор ±1°.
                s.Camera.PitchByZoom = false;
                s.Camera.Mode = MapLabCameraMode.Custom;
                int View(float pitch)
                {
                    s.Camera.CameraPitchDownDegrees = pitch;
                    view.OnCameraChanged();
                    return view.HouseView;
                }
                Assert.AreEqual(50, View(50f));
                Assert.AreEqual(50, View(42f), "Внутри зазора кадр не меняется");
                Assert.AreEqual(35, View(41f));
                Assert.AreEqual(35, View(43f), "Внутри зазора кадр не меняется");
                Assert.AreEqual(50, View(44f));

                // Фокус сохраняется при переходе в режим 35°.
                rig.SetMode(MapLabCameraMode.Volume50);
                Vector2 f = view.DemoFocus;
                rig.Target = rig.ToGround(new Vector3(f.x, view.Data.SampleHeight(f.x, f.y), f.y));
                rig.Apply();
                Vector3 focus = rig.Target;
                rig.SetMode(MapLabCameraMode.Low35);
                Assert.AreEqual(35f, rig.Pitch);
                Assert.Less(Vector3.Distance(focus, rig.Target), 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Покраска ----------

        [Test]
        public void TiledPaint_DeterministicAndSeamless()
        {
            MapLabSettings s = Settings(MapLabLook.Stage03);
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabPalette palette = MapLabPalette.CreateFallback();
                float tile = 32 * d.Cell;
                MapLabTiledPainter.Result a = MapLabTiledPainter.Paint(d, s, palette, tile, default);
                MapLabTiledPainter.Result b = MapLabTiledPainter.Paint(d, s, palette, tile, default);
                Assert.Greater(a.Tiles.Count, 1);
                Assert.Greater(a.Strokes, 1000);
                for (int i = 0; i < a.Tiles.Count; i++)
                    CollectionAssert.AreEqual(a.Tiles[i].Pixels, b.Tiles[i].Pixels, "Повтор того же seed даёт тот же рисунок");

                // Стык: правая полоса запаса тайла (x, z) совпадает с ядром тайла
                // (x + 1, z) — мазки соседей ложатся одинаково по обе стороны.
                int g = MapLabTiledPainter.Gutter;
                int mismatched = 0, compared = 0;
                foreach (MapLabTiledPainter.Tile left in a.Tiles)
                {
                    MapLabTiledPainter.Tile right = a.Tiles.Find(t => t.X == left.X + 1 && t.Z == left.Z);
                    if (right == null)
                        continue;
                    for (int z = g; z < left.Size - g; z++)
                    for (int k = 0; k < g; k++)
                    {
                        Color32 p = left.Pixels[z * left.Size + left.Core + g + k];
                        Color32 q = right.Pixels[z * right.Size + g + k];
                        compared++;
                        if (Mathf.Abs(p.r - q.r) > 1 || Mathf.Abs(p.g - q.g) > 1 || Mathf.Abs(p.b - q.b) > 1)
                            mismatched++;
                    }
                }
                Assert.Greater(compared, 100);
                Assert.AreEqual(0, mismatched, $"Шов тайлов: {mismatched} из {compared} текселей");
                Object.DestroyImmediate(palette);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Перо ----------

        [Test]
        public void Ink_JunctionIsOuterPerimeterWithoutInnerSeams()
        {
            const float step = 0.2f;
            int n = 201;
            float[] f = new float[n * n];
            float[] w = new float[n * n];
            Vector2 a0 = new Vector2(4f, 20f), a1 = new Vector2(36f, 20f);
            Vector2 b0 = new Vector2(20f, 20f), b1 = new Vector2(30f, 36f);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x * step, z * step);
                float da = MapLabGeometry.DistanceToSegment(p, a0, a1, out _) - 1.6f;
                float db = MapLabGeometry.DistanceToSegment(p, b0, b1, out _) - 1.1f;
                f[z * n + x] = Mathf.Min(da, db);
                w[z * n + x] = 1f;
            }
            List<MapLabInk.Contour> contours = MapLabInk.Trace(f, w, n, step);
            Assert.AreEqual(1, contours.Count, "Развилка — один общий периметр");
            Assert.IsTrue(contours[0].Closed);
            foreach (Vector2 p in contours[0].Points)
            {
                float da = MapLabGeometry.DistanceToSegment(p, a0, a1, out _) - 1.6f;
                float db = MapLabGeometry.DistanceToSegment(p, b0, b1, out _) - 1.1f;
                Assert.Greater(Mathf.Min(da, db), -0.05f, $"Точка {p} внутри полотна — внутренний шов");
                Assert.Less(Mathf.Min(da, db), 0.05f);
            }
        }

        // ---------- Лес и тени ----------

        [Test]
        public void ForestAccents_StableSelectiveChains_AndShadowsDoNotAccumulate()
        {
            MapLabSettings s = Settings(MapLabLook.Stage03);
            s.Vegetation.ForestDensity = 0.8f;
            List<Object> garbage = new List<Object> { s };
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
                garbage.Add(set);
                foreach (string id in new[] { "Tree_A", "Tree_B", "Tree_C" })
                {
                    Texture2D t = new Texture2D(4, 4);
                    garbage.Add(t);
                    set.Entries.Add(new MapLabArtEntry { Id = id, Category = MapLabArtCategory.Vegetation, Kind = MapLabObjectKind.Deciduous, Texture = t, Width = 4, Height = 4, WorldHeight = 4.5f, SolidRect = new Rect(0.1f, 0.1f, 0.8f, 0.8f), ScaleRange = new Vector2(0.9f, 1.1f), MaxSlope = 25f });
                }
                MapLabArtBuilder.Plan p1 = new MapLabArtBuilder.Plan(), p2 = new MapLabArtBuilder.Plan();
                MapLabArtBuilder.PlanVegetation(d, s, set, p1);
                MapLabArtBuilder.ForestStats stats = MapLabArtBuilder.LastForest;
                MapLabArtBuilder.PlanVegetation(d, s, set, p2);
                int accented = 0, total = 0, kinds = 0;
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in p1.Cards)
                {
                    kinds++;
                    List<MapLabArtInstance> other = p2.Cards[pair.Key];
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        Assert.AreEqual(pair.Value[i].Base, other[i].Base);
                        Assert.AreEqual(pair.Value[i].Accent, other[i].Accent, "Повтор seed — те же акценты");
                        Assert.AreEqual(pair.Value[i].Group, other[i].Group);
                        total++;
                        if (pair.Value[i].Accent > 0f)
                            accented++;
                    }
                }
                Assert.GreaterOrEqual(kinds, 2, "Лес смешан из нескольких отдельных рисунков по весам");
                Assert.Greater(stats.Chains, 0);
                Assert.Greater(accented, 0);
                Assert.Less(accented, total * 0.35f, "Акцент — выборочный, не обводка каждой кроны");

                // Тени: плотная группа крон не темнее одной кроны (объединение максимумом).
                List<MapLabObjectShadows.Caster> casters = new List<MapLabObjectShadows.Caster>();
                Vector2 c = new Vector2(d.Size * 0.5f, d.Size * 0.5f);
                for (int i = 0; i < 40; i++)
                {
                    Vector2 p = c + new Vector2((i % 7) * 0.6f, (i / 7) * 0.6f);
                    casters.Add(new MapLabObjectShadows.Caster { Base = new Vector3(p.x, d.SampleHeight(p.x, p.y), p.y), Radius = 1.5f, CenterHeight = 2.8f, ContactRadius = 0.7f });
                }
                MapLabObjectShadows.Result shadows = MapLabObjectShadows.Compute(d, s, casters);
                float max = 0f;
                foreach (float v in shadows.Cast)
                    max = Mathf.Max(max, v);
                Assert.LessOrEqual(max, 1f);
                Assert.Greater(max, 0.9f);
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void ShadowHit_FollowsActualTerrain()
        {
            MapLabData flat = new MapLabData(65, 64f);
            Vector3 light = new Vector3(-0.62f, 0.72f, 0.06f).normalized;
            Vector3 crown = new Vector3(30f, 3f, 30f);
            Vector2 hit = MapLabObjectShadows.Hit(flat, crown, light);
            Vector2 expected = new Vector2(crown.x, crown.z) - new Vector2(light.x, light.z) * (3f / light.y);
            Assert.Less(Vector2.Distance(hit, expected), 0.05f);
            // Уступ по свету вниз: тень уходит дальше, на нижнюю площадку.
            MapLabData step = new MapLabData(65, 64f);
            for (int z = 0; z < 65; z++)
            for (int x = 0; x < 65; x++)
                step.Heights[step.Index(x, z)] = x <= 31 ? 4f : 0f;
            Vector2 down = MapLabObjectShadows.Hit(step, new Vector3(30.5f, 7f, 30f), light);
            Assert.Greater(down.x, 32f, "Тень у края уступа не висит над нижней площадкой");
            Assert.Less(step.SampleHeight(down.x, down.y), 0.5f);
        }
    }
}
