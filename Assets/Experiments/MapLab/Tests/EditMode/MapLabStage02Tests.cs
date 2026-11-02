using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Этап 02: проверка пакета арта, независимость географии от оформления,
    // сектора фишки, режимы камеры, фокус и якорь приближения, JSON экспорта.
    public sealed class MapLabStage02Tests
    {
        // ---------- Пакет и manifest ----------

        [TestCase("Assets/Vegetation/Tree_Oak_01.png", true)]
        [TestCase("../Tree.png", false)]
        [TestCase("Assets/../../secret.png", false)]
        [TestCase("C:/Windows/x.png", false)]
        [TestCase("/etc/x.png", false)]
        [TestCase("Assets//x.png", false)]
        [TestCase("Assets/x.exe", false)]
        public void RelativePath_OnlyInsidePackage(string path, bool ok)
        {
            Assert.AreEqual(ok, MapLabArtImport.ValidateRelativePath(path) == null, path);
        }

        [Test]
        public void Json_ToleratesNullsAndUnknownFields()
        {
            Dictionary<string, object> obj = MapLabJson.AsObject(MapLabJson.Parse("{\"a\": null, \"b\": [1, 2.5], \"s\": \"\\u0416\", \"x\": {\"y\": true}}"));
            Assert.IsNull(obj["a"]);
            Assert.AreEqual(new[] { 1.0, 2.5 }, MapLabJson.GetNumbers(obj, "b"));
            Assert.AreEqual("Ж", MapLabJson.GetString(obj, "s"));
            Assert.AreEqual(7.0, MapLabJson.GetNumber(obj, "missing", 7.0));
            Assert.Throws<System.FormatException>(() => MapLabJson.Parse("{\"a\": }"));
        }

        [Test]
        public void Manifest_ChecksSchemaHashSizeAndMissingFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "MapLabStage02Test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                byte[] good = FakePng(4, 3);
                File.WriteAllBytes(Path.Combine(root, "Assets", "Good.png"), good);
                File.WriteAllBytes(Path.Combine(root, "Assets", "Bad.png"), FakePng(4, 3, 9));
                File.WriteAllBytes(Path.Combine(root, "Assets", "Size.png"), FakePng(5, 3));
                string hash = MapLabArtImport.Sha256(good);
                string Item(string id, string file, string sha, int w = 4, int h = 3) =>
                    $"{{\"id\":\"{id}\",\"path\":\"Assets/{file}\",\"category\":\"vegetation\",\"kind\":\"Deciduous\",\"width\":{w},\"height\":{h},\"sha256\":\"{sha}\",\"pivot_hint_unity_bottom_left\":[0.5,0.1],\"world_height_hint\":null,\"future_field\":{{\"x\":1}}}}";
                string manifest = "{\"schema_version\":1,\"package_id\":\"T\",\"asset_count\":5,\"items\":[" +
                                  Item("Good", "Good.png", hash) + "," +
                                  Item("BadHash", "Bad.png", hash) + "," +
                                  Item("WrongSize", "Size.png", MapLabArtImport.Sha256(File.ReadAllBytes(Path.Combine(root, "Assets", "Size.png")))) + "," +
                                  Item("Missing", "Missing.png", hash) + "," +
                                  Item("Escape", "../Good.png", hash) + "]}";
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), manifest, Encoding.UTF8);

                MapLabArtImport.Report report = new MapLabArtImport.Report();
                List<MapLabArtImport.Item> items = MapLabArtImport.ReadManifest(root, report, out _, out _);
                Assert.IsNotNull(items);
                Assert.AreEqual(4, items.Count, "Путь с «..» должен быть отклонён на чтении manifest");
                Assert.IsTrue(report.Errors.Exists(e => e.Contains("Escape")));
                Dictionary<string, bool> verified = new Dictionary<string, bool>();
                foreach (MapLabArtImport.Item item in items)
                    verified[item.Id] = MapLabArtImport.VerifyFile(item, report);
                Assert.IsTrue(verified["Good"]);
                Assert.IsFalse(verified["BadHash"]);
                Assert.IsFalse(verified["WrongSize"]);
                Assert.IsFalse(verified["Missing"]);
                Assert.IsTrue(report.Errors.Exists(e => e.Contains("SHA-256")));
                Assert.IsTrue(report.Errors.Exists(e => e.Contains("не найден")));

                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), "{\"schema_version\":2,\"items\":[]}");
                MapLabArtImport.Report future = new MapLabArtImport.Report();
                Assert.IsNull(MapLabArtImport.ReadManifest(root, future, out _, out _));
                Assert.IsTrue(future.Errors[0].Contains("schema_version"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        // Минимальная «PNG»: сигнатура и IHDR с размерами (для проверки размеров и хеша).
        private static byte[] FakePng(int width, int height, byte salt = 0)
        {
            byte[] b = new byte[33];
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            sig.CopyTo(b, 0);
            b[11] = 13;
            b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
            b[19] = (byte)width;
            b[23] = (byte)height;
            b[32] = salt;
            return b;
        }

        // ---------- Оформление не меняет географию ----------

        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.General.PaintResolution = 512;
            // Тесты этапа 02: прежний язык и углы домов этапа 02.
            s.Stage03.Look = MapLabLook.Stage02;
            s.Settlement.UseVariantYaw = false;
            return s;
        }

        private static MapLabArtSet FakeSet(List<Object> garbage)
        {
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            garbage.Add(set);
            void Add(string id, MapLabArtCategory category, MapLabObjectKind kind)
            {
                Texture2D t = new Texture2D(8, 8);
                garbage.Add(t);
                set.Entries.Add(new MapLabArtEntry { Id = id, Category = category, Kind = kind, Texture = t, Width = 8, Height = 8, WorldHeight = 4f, SolidRect = new Rect(0f, 0f, 1f, 0.9f) });
            }
            Add("Tree_Oak_01", MapLabArtCategory.Vegetation, MapLabObjectKind.Deciduous);
            Add("Tree_Birch_01", MapLabArtCategory.Vegetation, MapLabObjectKind.Deciduous);
            Add("Tree_Spruce_01", MapLabArtCategory.Vegetation, MapLabObjectKind.Conifer);
            Add("House_Thatch_01", MapLabArtCategory.Building, MapLabObjectKind.House);
            Add("Boulder_01", MapLabArtCategory.Rock, MapLabObjectKind.Boulder);
            return set;
        }

        [Test]
        public void ArtMode_DoesNotMoveGeography_AndPickIsDeterministic()
        {
            List<Object> garbage = new List<Object>();
            MapLabSettings procedural = Settings();
            MapLabSettings art = Settings();
            garbage.Add(procedural);
            garbage.Add(art);
            try
            {
                procedural.Art.Vegetation = MapLabArtMode.Procedural;
                procedural.Art.Buildings = MapLabBuildingDisplay.Geometry;
                procedural.Art.CliffDecor = false;
                art.Art.Vegetation = MapLabArtMode.Art;
                art.Art.Buildings = MapLabBuildingDisplay.SpriteDecorative;
                art.Art.CliffDecor = true;
                MapLabData a = MapLabGenerator.Generate(procedural, null);
                MapLabData b = MapLabGenerator.Generate(art, null);
                Assert.AreEqual(a.Objects.Count, b.Objects.Count);
                for (int i = 0; i < a.Objects.Count; i++)
                {
                    Assert.AreEqual(a.Objects[i].Kind, b.Objects[i].Kind);
                    Assert.AreEqual(a.Objects[i].Position, b.Objects[i].Position);
                    Assert.AreEqual(a.Objects[i].Yaw, b.Objects[i].Yaw);
                }
                Assert.AreEqual(a.Roads.Count, b.Roads.Count);
                for (int r = 0; r < a.Roads.Count; r++)
                    CollectionAssert.AreEqual(a.Roads[r].Points, b.Roads[r].Points);
                CollectionAssert.AreEqual(a.Heights, b.Heights);

                MapLabArtSet set = FakeSet(garbage);
                MapLabArtBuilder.Plan p1 = new MapLabArtBuilder.Plan();
                MapLabArtBuilder.Plan p2 = new MapLabArtBuilder.Plan();
                MapLabArtBuilder.PlanVegetation(b, art, set, p1);
                MapLabArtBuilder.PlanVegetation(b, art, set, p2);
                Assert.Greater(p1.Count, 100);
                Assert.AreEqual(p1.Count, p2.Count);
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in p1.Cards)
                {
                    List<MapLabArtInstance> other = p2.Cards[pair.Key];
                    Assert.AreEqual(pair.Value.Count, other.Count);
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        MapLabArtInstance inst = pair.Value[i];
                        Assert.AreEqual(inst.Base, other[i].Base);
                        MapLabPlacement o = b.Objects[inst.ObjectIndex];
                        // Одиночное дерево стоит ровно в точке объекта; группа крон — рядом.
                        if (o.Kind != MapLabObjectKind.DeciduousGroup)
                            Assert.AreEqual(o.Position, inst.Base);
                        else
                            Assert.Less(Vector2.Distance(new Vector2(o.Position.x, o.Position.z), new Vector2(inst.Base.x, inst.Base.z)), o.Scale);
                    }
                }
                // Смешанный лиственный лес: больше одного рисунка среди лиственных.
                int deciduousKinds = 0;
                foreach (MapLabArtEntry e in p1.Cards.Keys)
                    if (e.Kind == MapLabObjectKind.Deciduous)
                        deciduousKinds++;
                Assert.GreaterOrEqual(deciduousKinds, 2);
                Assert.IsTrue(p1.Replaced.Contains(MapLabObjectKind.Deciduous));
                Assert.IsFalse(p1.Replaced.Contains(MapLabObjectKind.Bush), "Без PNG куста — процедурный fallback");
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Houses_FollowDrawnOrientation_AndYardsHaveGateAndPath()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                int houses = 0;
                foreach (MapLabPlacement o in d.Objects)
                {
                    if (o.Kind != MapLabObjectKind.House)
                        continue;
                    houses++;
                    float drawn = o.Variant % 4 < 2 ? s.Settlement.DrawnRidgeYaw.x : s.Settlement.DrawnRidgeYaw.y;
                    float axis = Mathf.Abs(Mathf.DeltaAngle(o.Yaw, drawn));
                    Assert.IsTrue(axis < 0.01f || Mathf.Abs(axis - 180f) < 0.01f, $"Изба повёрнута на {o.Yaw}°, рисунок — {drawn}°");
                }
                Assert.GreaterOrEqual(houses, 3);
                Assert.Greater(d.Fences.Count, 0);
                Assert.Greater(d.YardPaths.Count, 0, "От ворот нет ни одной тропинки");
                foreach (MapLabPlacement o in d.Objects)
                {
                    if (o.Kind != MapLabObjectKind.Deciduous && o.Kind != MapLabObjectKind.Conifer && o.Kind != MapLabObjectKind.DeciduousGroup && o.Kind != MapLabObjectKind.Bush)
                        continue;
                    foreach (MapLabYardRect yard in d.YardRects)
                        Assert.IsFalse(yard.Contains(new Vector2(o.Position.x, o.Position.z), 0f), $"{o.Kind} внутри двора {yard.Center}");
                }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Фишка ----------

        [TestCase(180f, 0f, MapLabMarkerDirection.Front)]
        [TestCase(120f, 0f, MapLabMarkerDirection.FrontRight)]
        [TestCase(60f, 0f, MapLabMarkerDirection.BackRight)]
        [TestCase(0f, 0f, MapLabMarkerDirection.Back)]
        [TestCase(-60f, 0f, MapLabMarkerDirection.BackLeft)]
        [TestCase(-120f, 0f, MapLabMarkerDirection.FrontLeft)]
        [TestCase(150f, 0f, MapLabMarkerDirection.Front)]
        [TestCase(-150f, 0f, MapLabMarkerDirection.Front)]
        [TestCase(210f, 30f, MapLabMarkerDirection.Front)]
        [TestCase(90f, 30f, MapLabMarkerDirection.BackRight)]
        public void Marker_SectorRelativeToCamera(float facing, float azimuth, MapLabMarkerDirection expected)
        {
            Assert.AreEqual(expected, MapLabMarkerSet.Sector(facing, azimuth));
        }

        [Test]
        public void Marker_CalibrationOnlyForFrontAtItsPitch()
        {
            List<Object> garbage = new List<Object>();
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            MapLabMarkerSet markers = ScriptableObject.CreateInstance<MapLabMarkerSet>();
            garbage.Add(set);
            garbage.Add(markers);
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    Texture2D t = new Texture2D(2, 2);
                    garbage.Add(t);
                    set.Entries.Add(new MapLabArtEntry { Id = "V" + i, Category = MapLabArtCategory.Figure, Texture = t });
                    markers.ViewIds[i] = "V" + i;
                }
                foreach (string id in new[] { "F40", "F60" })
                {
                    Texture2D t = new Texture2D(2, 2);
                    garbage.Add(t);
                    set.Entries.Add(new MapLabArtEntry { Id = id, Category = MapLabArtCategory.FigureCalibration, Texture = t });
                }
                markers.ArtSet = set;
                markers.Front40Id = "F40";
                markers.Front60Id = "F60";
                Assert.IsTrue(markers.Complete);
                Assert.AreEqual("V0", markers.Resolve(MapLabMarkerDirection.Front, 50f, out bool approx50).Id);
                Assert.IsFalse(approx50);
                Assert.AreEqual("F40", markers.Resolve(MapLabMarkerDirection.Front, 40f, out bool approx40).Id);
                Assert.IsFalse(approx40);
                Assert.AreEqual("F60", markers.Resolve(MapLabMarkerDirection.Front, 60f, out _).Id);
                // Других направлений под 40°/60° нет: основной кадр с пометкой «приближённый».
                Assert.AreEqual("V1", markers.Resolve(MapLabMarkerDirection.FrontRight, 40f, out bool approxSide).Id);
                Assert.IsTrue(approxSide);
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        // ---------- Камера ----------

        [Test]
        public void CameraModes_PitchValues_AndPitchByZoomIsMonotonic()
        {
            MapLabCameraSettings c = new MapLabCameraSettings();
            c.PitchByZoom = false;
            c.Mode = MapLabCameraMode.Atlas60;
            Assert.AreEqual(60f, c.PitchFor(3f));
            c.Mode = MapLabCameraMode.Volume50;
            Assert.AreEqual(50f, c.PitchFor(1f));
            c.Mode = MapLabCameraMode.Diorama40;
            Assert.AreEqual(40f, c.PitchFor(1f));
            c.PitchByZoom = true;
            c.PitchByZoomRange = new Vector2(60f, 40f);
            Assert.AreEqual(60f, c.PitchFor(1f, 4f), 0.01f);
            Assert.AreEqual(40f, c.PitchFor(4f, 4f), 0.01f);
            float previous = 61f;
            for (float z = 1f; z <= 6f; z += 0.25f)
            {
                float p = c.PitchFor(z);
                Assert.LessOrEqual(p, previous + 1e-4f);
                previous = p;
            }
        }

        [Test]
        public void Camera_ModeKeepsFocus_ZoomKeepsCursorPoint_ExportInfoReadsRig()
        {
            MapLabSettings s = Settings();
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
                rig.SetMode(MapLabCameraMode.Volume50);
                Vector2 f = view.DemoFocus;
                rig.Target = rig.ToGround(new Vector3(f.x, view.Data.SampleHeight(f.x, f.y), f.y));
                rig.Apply();
                Vector3 focus = rig.Target;
                foreach (MapLabCameraMode mode in new[] { MapLabCameraMode.Diorama40, MapLabCameraMode.Atlas60, MapLabCameraMode.Volume50 })
                {
                    rig.SetMode(mode);
                    Assert.Less(Vector3.Distance(focus, rig.Target), 0.05f, "Смена наклона сдвинула фокус");
                    Vector3 vp = rig.Camera.WorldToViewportPoint(focus);
                    Assert.AreEqual(0.5f, vp.x, 0.002f);
                    Assert.AreEqual(0.5f, vp.y, 0.002f);
                }
                Vector2 cursor = new Vector2(0.3f, 0.65f);
                Vector3 ground = rig.PickGround(cursor);
                rig.ZoomAround(2.5f, cursor, false);
                Assert.AreEqual(2.5f, rig.Zoom, 1e-4f);
                Vector3 after = rig.Camera.WorldToViewportPoint(ground);
                Assert.AreEqual(cursor.x, after.x, 0.01f, "Точка под курсором уехала по X");
                Assert.AreEqual(cursor.y, after.y, 0.01f, "Точка под курсором уехала по Y");
                Assert.AreEqual(ground.y, view.Data.SampleHeight(ground.x, ground.z), 0.05f, "Якорь — не точка рельефа");

                s.Camera.Zoom = 1f;
                MapLabExportInfo info = MapLabExportInfo.From(view, 640, 480);
                Assert.AreEqual(2.5f, info.Zoom, 1e-4f, "JSON должен писать фактический Zoom камеры");
                Assert.AreEqual(1f, info.SettingsZoom);
                Assert.AreEqual(rig.Pitch, info.CameraPitchDownDegrees);
                Assert.AreEqual(rig.Target, info.Target);
                Assert.AreEqual("Orthographic", info.Projection);

                // Экспорт не меняет приближение, фокус и режим пользователя.
                Vector3 target = rig.Target;
                Texture2D image = MapLabExport.Render(rig, 320, 160, 1);
                Object.DestroyImmediate(image);
                Assert.AreEqual(2.5f, rig.Zoom, 1e-4f);
                Assert.AreEqual(target, rig.Target);
                Assert.AreEqual(MapLabCameraMode.Volume50, s.Camera.Mode);
            }
            finally
            {
                Object.DestroyImmediate(mapObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(s);
            }
        }
    }
}
