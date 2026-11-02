using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Этап 04: единая тень с непрерывным градиентом контакта, примыкание тропы
    // без внутреннего шва, мировой детерминизм и стыки покраски с пятнами,
    // трава только в разрешённых местах, выбор пары «наклон + направление»
    // героя без перезаписи, гистерезис и fallback, сохранение опор героя.
    public sealed class MapLabStage04Tests
    {
        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage04;
            s.Stage03.PaintTexelsPerUnit = 6f;
            return s;
        }

        // ---------- Тень ----------

        [Test]
        public void Shadow_ContactIsContinuousGradientInsideSharpCast()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData flat = new MapLabData(65, 64f);
                List<MapLabObjectShadows.Caster> casters = new List<MapLabObjectShadows.Caster>
                {
                    new MapLabObjectShadows.Caster { Base = new Vector3(24f, 0f, 30f), Radius = 1.6f, CenterHeight = 2.8f, ContactRadius = 0.7f },
                };
                MapLabObjectShadows.Result r = MapLabObjectShadows.Compute(flat, s, casters);
                Assert.IsTrue(r.Gradient);
                int n = r.Resolution;
                float texel = flat.Size / n;
                // Внешний край падающей тени собранный: по линии света переход
                // от полной тени к нулю занимает не больше 3 текселей.
                int row = Mathf.RoundToInt(30f / texel - 0.5f);
                int soft = 0, inside = 0;
                for (int x = Mathf.RoundToInt(26f / texel); x < n; x++)
                {
                    float v = r.Cast[row * n + x];
                    if (v > 0.02f && v < 0.98f) soft++;
                    if (v >= 0.98f) inside++;
                }
                Assert.Greater(inside, 3);
                Assert.LessOrEqual(soft, 3, "Край тени размыт");
                // Контакт — плавное поле: нет скачка между соседними текселями
                // (жёсткий диск дал бы перепад ≈ 1), максимум — у опоры.
                float maxJump = 0f, atFoot = 0f, far = 0f;
                for (int z = 1; z < n; z++)
                for (int x = 1; x < n; x++)
                {
                    float v = r.Contact[z * n + x];
                    maxJump = Mathf.Max(maxJump, Mathf.Abs(v - r.Contact[z * n + x - 1]), Mathf.Abs(v - r.Contact[(z - 1) * n + x]));
                }
                atFoot = r.Contact[Mathf.RoundToInt(30f / texel) * n + Mathf.RoundToInt(24.4f / texel)];
                far = r.Contact[Mathf.RoundToInt(30f / texel) * n + Mathf.RoundToInt(27.5f / texel)];
                Assert.Less(maxJump, 0.45f, "В контакте есть жёсткая граница (второй диск)");
                Assert.Greater(atFoot, 0.6f);
                Assert.Less(far, atFoot * 0.5f);
                // Контакт не выходит отдельным ореолом за падающую форму: вне
                // формы шейдер его не показывает (тон = маска × плотность).
                for (int i = 0; i < r.Contact.Length; i++)
                    if (r.Cast[i] < 0.02f && r.Contact[i] > 0.3f)
                        Assert.Fail("Плотный контакт вне падающей тени");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Узел тропы ----------

        [Test]
        public void Junction_NoInnerSeam_AndPathBlendsIntoRoad()
        {
            MapLabSettings s = Settings();
            MapLabPalette palette = MapLabPalette.CreateFallback();
            try
            {
                MapLabData d = new MapLabData(65, 64f);
                MapLabPolyline road = new MapLabPolyline { Width = 3.2f, Kind = MapLabRoadKind.Road };
                road.Points.Add(new Vector2(4f, 30f));
                road.Points.Add(new Vector2(60f, 30f));
                MapLabPolyline path = new MapLabPolyline { Width = 1.4f, Kind = MapLabRoadKind.Path };
                path.Points.Add(new Vector2(30f, 52f));
                path.Points.Add(new Vector2(31f, 30f));
                d.Roads.Add(road);
                d.Roads.Add(path);
                MapLabPaintFields fields = new MapLabPaintFields(d, s, palette);
                MapLabRoadJunction junction = MapLabRoadJunction.From(s);
                Assert.Greater(junction.Length, 0f);
                float[] f = MapLabInk.RoadField(d, fields, s.Stage03.InkStyle.Weights, out int n, out float[] weights, junction);
                List<MapLabInk.Contour> contours = MapLabInk.Trace(f, weights, n, MapLabInk.GridStep);
                Assert.AreEqual(1, contours.Count, "Тропа и дорога — один общий периметр");
                foreach (Vector2 p in contours[0].Points)
                {
                    float dRoad = MapLabGeometry.DistanceToSegment(p, road.Points[0], road.Points[1], out _) - road.Width * 0.5f;
                    // Кромка гуляет на ±0.4 ед. (неровность), внутренний шов лежал бы на 1.6 ед. внутри.
                    Assert.Greater(dRoad, -0.7f, $"Линия пера внутри полотна дороги у {p} — внутренний шов");
                }
                // Вес пера вдоль тропы к узлу меняется плавно, у дороги — как у дороги.
                float previous = -1f, maxStep = 0f;
                for (float z = 50f; z >= 31.8f; z -= MapLabInk.GridStep)
                {
                    int gx = Mathf.RoundToInt(30.95f / MapLabInk.GridStep), gz = Mathf.RoundToInt(z / MapLabInk.GridStep);
                    float w = weights[gz * n + gx];
                    if (previous >= 0f)
                        maxStep = Mathf.Max(maxStep, Mathf.Abs(w - previous));
                    previous = w;
                }
                Assert.Less(maxStep, 0.05f, "Поперечный стык веса пера на тропе");
                Assert.AreEqual(s.Stage03.InkStyle.Weights.x, previous, 0.05f);
                // Вне узла тропа остаётся тропой.
                Assert.AreEqual(s.Stage03.InkStyle.Weights.y, weights[Mathf.RoundToInt(48f / MapLabInk.GridStep) * n + Mathf.RoundToInt(30.1f / MapLabInk.GridStep)], 0.01f);
                // Этап 03 — без перехода (нейтрально).
                Assert.AreEqual(0f, MapLabRoadJunction.From(null).Length);
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Покраска с пятнами и трава ----------

        [Test]
        public void Stage04Paint_SeamlessAndDeterministic_GrassOnlyInAllowedPlaces()
        {
            MapLabSettings s = Settings();
            MapLabPalette palette = MapLabPalette.CreateFallback();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                float tile = 32 * d.Cell;
                MapLabTiledPainter.Result a = MapLabTiledPainter.Paint(d, s, palette, tile, default);
                MapLabTiledPainter.Result b = MapLabTiledPainter.Paint(d, s, palette, tile, default);
                for (int i = 0; i < a.Tiles.Count; i++)
                    CollectionAssert.AreEqual(a.Tiles[i].Pixels, b.Tiles[i].Pixels);
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
                Assert.AreEqual(0, mismatched, "Пятна или мазки обрываются на стыке тайлов");

                List<MapLabGrass.Tuft> tufts = MapLabGrass.Place(d, s);
                List<MapLabGrass.Tuft> again = MapLabGrass.Place(d, s);
                Assert.Greater(tufts.Count, 10);
                Assert.AreEqual(tufts.Count, again.Count);
                foreach (MapLabGrass.Tuft t in tufts)
                {
                    Vector2 p = new Vector2(t.Base.x, t.Base.z);
                    Assert.IsTrue(MapLabGrass.Allowed(d, p), $"Трава в запрещённом месте {p}");
                    Assert.IsFalse(d.IsUnderWater(p.x, p.y), "Трава в воде");
                    foreach (MapLabYardRect yard in d.YardRects)
                        Assert.IsFalse(yard.Contains(p, 0f), "Трава во дворе");
                    foreach (MapLabPolyline road in d.Roads)
                        for (int k = 0; k < road.Points.Count - 1; k++)
                            Assert.GreaterOrEqual(MapLabGeometry.DistanceToSegment(p, road.Points[k], road.Points[k + 1], out _), road.Width * 0.5f, "Трава на полотне дороги");
                    Assert.AreEqual(d.SampleHeight(p.x, p.y), t.Base.y, 1e-4f, "Пучок висит над землёй");
                }
                s.Stage04.Grass = 0f;
                Assert.AreEqual(0, MapLabGrass.Place(d, s).Count);
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(s);
            }
        }

        // ---------- Герой ----------

        private static MapLabMarkerSet HeroSet(List<Object> garbage, bool missingBack35)
        {
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            MapLabMarkerSet markers = ScriptableObject.CreateInstance<MapLabMarkerSet>();
            garbage.Add(set);
            garbage.Add(markers);
            markers.ArtSet = set;
            foreach (int pitch in new[] { 50, 35 })
                for (int i = 0; i < 6; i++)
                {
                    string id = $"Hero_{MapLabMarkerSet.DirectionNames[i]}_P{pitch}";
                    Texture2D t = missingBack35 && pitch == 35 && i == 3 ? null : new Texture2D(2, 2);
                    if (t != null)
                        garbage.Add(t);
                    set.Entries.Add(new MapLabArtEntry { Id = id, Category = MapLabArtCategory.Figure, Direction = MapLabMarkerSet.DirectionNames[i], PitchDownDegrees = pitch, Texture = t });
                }
            Dictionary<string, object> manifest = MapLabJson.AsObject(MapLabJson.Parse(
                "{\"marker_pitch_sets\":[" +
                "{\"pitch_down_degrees\":50,\"view_ids\":[\"Hero_Front_P50\",\"Hero_Front_Right_P50\",\"Hero_Back_Right_P50\",\"Hero_Back_P50\",\"Hero_Back_Left_P50\",\"Hero_Front_Left_P50\"]}," +
                "{\"pitch_down_degrees\":35,\"view_ids\":[\"Hero_Front_P35\",\"Hero_Front_Right_P35\",\"Hero_Back_Right_P35\",\"Hero_Back_P35\",\"Hero_Back_Left_P35\",\"Hero_Front_Left_P35\"]}]}"));
            MapLabArtImport.Report report = new MapLabArtImport.Report();
            MapLabArtImport.ReadPitchSets(markers, set, manifest, report);
            return markers;
        }

        [Test]
        public void Hero_EachPitchDirectionPairResolves_WithoutOverwrite()
        {
            List<Object> garbage = new List<Object>();
            try
            {
                MapLabMarkerSet markers = HeroSet(garbage, false);
                Assert.AreEqual(2, markers.PitchSets.Count);
                Assert.IsTrue(markers.Complete);
                // Прежний ViewIds — набор 50°, а не последние записанные кадры 35°.
                Assert.AreEqual("Hero_Front_P50", markers.ViewIds[0]);
                Assert.AreEqual(50f, markers.FullSetPitch);
                for (int s = 0; s < 2; s++)
                {
                    float pitch = markers.PitchSets[s].Pitch;
                    for (int i = 0; i < 6; i++)
                    {
                        MapLabArtEntry e = markers.ResolveSet((MapLabMarkerDirection)i, pitch, s, out bool approx);
                        Assert.AreEqual($"Hero_{MapLabMarkerSet.DirectionNames[i]}_P{pitch:0}", e.Id);
                        Assert.IsFalse(approx);
                    }
                }
                // 60°: ближайший новый набор 50° с пометкой «приближённо».
                int at60 = markers.ChooseSet(60f, -1, 42.5f, 2f);
                Assert.AreEqual(50f, markers.PitchSets[at60].Pitch);
                markers.ResolveSet(MapLabMarkerDirection.Front, 60f, at60, out bool approx60);
                Assert.IsTrue(approx60);
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Hero_SetSwitchHasHysteresis_AndMissingFrameFallsBack()
        {
            List<Object> garbage = new List<Object>();
            try
            {
                MapLabMarkerSet markers = HeroSet(garbage, true);
                int set = markers.ChooseSet(50f, -1, 42.5f, 2f);
                float Pitch(float p)
                {
                    set = markers.ChooseSet(p, set, 42.5f, 2f);
                    return markers.PitchSets[set].Pitch;
                }
                Assert.AreEqual(50f, Pitch(42f), "Внутри зазора набор не меняется");
                Assert.AreEqual(35f, Pitch(41f));
                Assert.AreEqual(35f, Pitch(43f), "Внутри зазора набор не меняется");
                Assert.AreEqual(50f, Pitch(44f));
                // Нет кадра Back при 35° — тот же Direction из 50° (приближённо), не другой ракурс.
                MapLabArtEntry back = markers.ResolveSet(MapLabMarkerDirection.Back, 35f, 1, out bool approx);
                Assert.AreEqual("Hero_Back_P50", back.Id);
                Assert.IsTrue(approx);
                Assert.IsFalse(markers.Complete);
            }
            finally
            {
                foreach (Object o in garbage)
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Hero_ReimportKeepsEditedPivot_AndUsesBootHints()
        {
            const string folder = "Assets/Experiments/MapLab/Tests/TempImport04";
            MapLabArtImport.Profile profile = new MapLabArtImport.Profile
            {
                Name = "тест 04", Folder = folder, SetPath = folder + "/Set.asset", MarkerPath = folder + "/Markers.asset",
                LastFolderKey = "KingdomSurvival.MapLab.Test04", MinRevision = 4, MaxRevision = 99, BootHintPivots = true,
            };
            string root = Path.Combine(Path.GetTempPath(), "MapLabStage04Import_" + System.Guid.NewGuid().ToString("N"));
            Texture2D image = new Texture2D(20, 30, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[20 * 30];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (i % 20) > 4 && (i % 20) < 15 && i / 20 > 3 ? new Color32(150, 60, 50, 255) : new Color32(0, 0, 0, 0);
                image.SetPixels32(pixels);
                byte[] png = image.EncodeToPNG();
                string sha = MapLabArtImport.Sha256(png);
                StringBuilder items = new StringBuilder();
                StringBuilder ids50 = new StringBuilder(), ids35 = new StringBuilder();
                Directory.CreateDirectory(Path.Combine(root, "Assets", "Hero"));
                foreach (int pitch in new[] { 50, 35 })
                    for (int i = 0; i < 6; i++)
                    {
                        string dir = MapLabMarkerSet.DirectionNames[i];
                        string id = $"H_{dir}_P{pitch}";
                        File.WriteAllBytes(Path.Combine(root, "Assets", "Hero", id + ".png"), png);
                        if (items.Length > 0) items.Append(',');
                        items.Append($"{{\"id\":\"{id}\",\"path\":\"Assets/Hero/{id}.png\",\"category\":\"figure\",\"kind\":null,\"width\":20,\"height\":30,\"sha256\":\"{sha}\",\"direction\":\"{dir}\",\"intended_pitch_down_degrees\":{pitch},\"pivot_hint_unity_bottom_left\":[0.47,0.12],\"world_height_hint\":2.8}}");
                        StringBuilder ids = pitch == 50 ? ids50 : ids35;
                        if (ids.Length > 0) ids.Append(',');
                        ids.Append($"\"{id}\"");
                    }
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName),
                    "{\"schema_version\":1,\"package_revision\":4,\"package_id\":\"T4\",\"items\":[" + items + "],\"marker_pitch_sets\":[{\"pitch_down_degrees\":50,\"view_ids\":[" + ids50 + "]},{\"pitch_down_degrees\":35,\"view_ids\":[" + ids35 + "]}]}", Encoding.UTF8);
                MapLabArtImport.Report first = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(first.Ok, first.ToString());
                MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                MapLabMarkerSet markers = AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(profile.MarkerPath);
                Assert.AreEqual(12, set.Entries.Count);
                Assert.AreEqual(2, markers.PitchSets.Count);
                MapLabArtEntry front = set.Find("H_Front_P35");
                Assert.AreEqual(0.47f, front.Pivot.x, 1e-4f, "Опора героя — по подсказке сапог");
                front.Pivot = new Vector2(0.51f, 0.2f);
                front.PivotEdited = true;
                EditorUtility.SetDirty(set);
                MapLabArtImport.Report second = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(second.Ok, second.ToString());
                set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                Assert.AreEqual(12, set.Entries.Count, "Повторный импорт без дубликатов");
                Assert.AreEqual(new Vector2(0.51f, 0.2f), set.Find("H_Front_P35").Pivot, "Повторный импорт сбросил калибровку");
                Assert.AreEqual("H_Front_P35", AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>(profile.MarkerPath).PitchSets[1].ViewIds[0]);
            }
            finally
            {
                Object.DestroyImmediate(image);
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
