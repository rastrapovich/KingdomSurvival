using System.Collections.Generic;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Рисованные скалы ревизии 06: раскладка кусков вдоль линии (без зазоров и
    // наложений, торцы на своих сторонах, класс по высоте, растворение концов без
    // торцов), сборка на карте без NaN и только из записей набора, импорт
    // cliff_module с привязками и bbox [x, y, ширина, высота].
    public sealed class MapLabCliffs06Tests
    {
        private static MapLabArtEntry Entry(string id, string role, string cls, int w, int h, float top, float bottom, string side = "")
        {
            return new MapLabArtEntry
            {
                Id = id, Category = MapLabArtCategory.CliffModule, Role = role, HeightClass = cls, Side = side,
                Width = w, Height = h, RockTopPx = top, BasePx = bottom, PxPerUnit = 100f, Texture = Texture2D.whiteTexture, Weight = 1f,
            };
        }

        private static MapLabArtSet KitSet()
        {
            MapLabArtSet set = ScriptableObject.CreateInstance<MapLabArtSet>();
            set.Entries.Add(Entry("Tall_A", "cliff_face", "tall", 2400, 1000, 100, 900));
            set.Entries.Add(Entry("Tall_B", "cliff_face", "tall", 2400, 1000, 100, 900));
            set.Entries.Add(Entry("Medium_A", "cliff_face", "medium", 2400, 700, 90, 590));
            set.Entries.Add(Entry("Low_A", "cliff_face", "low", 2400, 450, 80, 330));
            set.Entries.Add(Entry("EndL_Tall", "cliff_end", "tall", 1200, 1000, 100, 900, "left"));
            set.Entries.Add(Entry("EndR_Tall", "cliff_end", "tall", 1200, 1000, 100, 900, "right"));
            set.Entries.Add(Entry("EndL_Low", "cliff_end", "low", 800, 450, 80, 330, "left"));
            set.Entries.Add(Entry("EndR_Low", "cliff_end", "low", 800, 450, 80, 330, "right"));
            return set;
        }

        [Test]
        public void Layout_ContiguousPieces_EndsOnTheirSides_ClassByHeight()
        {
            MapLabArtSet set = KitSet();
            try
            {
                MapLabCliffs06.Kit kit = MapLabCliffs06.KitOf(set);
                Assert.AreEqual("tall", MapLabCliffs06.ClassFor(kit, 8.5f));
                Assert.AreEqual("medium", MapLabCliffs06.ClassFor(kit, 5f));
                Assert.AreEqual("low", MapLabCliffs06.ClassFor(kit, 2.2f));
                foreach ((string cls, float height, float length) in new[] { ("tall", 8f, 70f), ("tall", 7f, 14f), ("low", 2.5f, 41f), ("medium", 5f, 33f) })
                {
                    List<MapLabCliffs06.Piece> pieces = MapLabCliffs06.Layout(kit, cls, length, height, 17u, out float scale);
                    Assert.Greater(pieces.Count, 0);
                    Assert.AreEqual(0f, pieces[0].S0, 1e-4f, "Начало линии не покрыто");
                    Assert.AreEqual(length, pieces[pieces.Count - 1].S1, 1e-3f, "Конец линии не покрыт");
                    for (int i = 1; i < pieces.Count; i++)
                        Assert.AreEqual(pieces[i - 1].S1, pieces[i].S0, 1e-4f, "Зазор или наложение между кусками");
                    foreach (MapLabCliffs06.Piece p in pieces)
                    {
                        Assert.AreEqual(cls, p.Entry.HeightClass, "Кусок другого класса высоты");
                        float aspect = p.Entry.Width / (float)p.Entry.Height, world = p.S1 - p.S0;
                        float vertical = p.Entry.Height / 100f * scale;
                        Assert.That(world / vertical / aspect, Is.InRange(0.6f, 1.6f), "Плитка растянута по горизонтали");
                    }
                    if (cls == "medium")
                    {
                        // Торцов класса нет — концы растворяются.
                        Assert.IsTrue(pieces[0].FadeStart && pieces[pieces.Count - 1].FadeEnd);
                    }
                    else
                    {
                        Assert.AreEqual("left", pieces[0].Entry.Side, "Левый торец не в начале");
                        Assert.AreEqual("right", pieces[pieces.Count - 1].Entry.Side, "Правый торец не в конце");
                        for (int i = 1; i < pieces.Count - 1; i++)
                            Assert.AreEqual("cliff_face", pieces[i].Entry.Role);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void Build_OnGeneratedMap_NoNaN_FacesOutward()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.HillAndLedge);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            MapLabArtSet set = KitSet();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabCliffs06.Result r = MapLabCliffs06.Build(d, s, MapLabCliffs06.KitOf(set));
                Assert.Greater(r.Stats.Lines, 0, "Ни одной стены");
                foreach (KeyValuePair<MapLabArtEntry, MapLabCliffs06.MeshData> pair in r.Meshes)
                {
                    Assert.IsTrue(set.Entries.Contains(pair.Key), "Кусок не из набора");
                    MapLabCliffs06.MeshData m = pair.Value;
                    for (int i = 0; i < m.Positions.Count; i++)
                    {
                        Vector3 p = m.Positions[i];
                        Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), "NaN в стене");
                        Assert.AreEqual(1f, m.Outward[i].magnitude, 1e-3f, "Нормаль наружу не нормирована");
                        Assert.That(m.Uv[i].x, Is.InRange(-1e-4f, 1.0001f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void Import_Revision6_CliffModule_AnchorsAndSizeBbox()
        {
            const string folder = "Assets/Experiments/MapLab/Tests/TempImport06";
            string root = Path.Combine(Path.GetTempPath(), "MapLabCliffs06Import_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                MapLabArtImport.Profile profile = new MapLabArtImport.Profile
                {
                    Name = "тест 06", Folder = folder + "/Art", SetPath = folder + "/Set.asset", MarkerPath = folder + "/Markers.asset",
                    LastFolderKey = "KingdomSurvival.MapLab.Test06", MinRevision = 6, MaxRevision = 99,
                };
                AssetDatabase.CreateFolder("Assets/Experiments/MapLab/Tests", "TempImport06");
                Texture2D t = new Texture2D(48, 20, TextureFormat.RGBA32, false);
                Color32[] px = new Color32[48 * 20];
                for (int i = 0; i < px.Length; i++)
                    px[i] = i / 48 >= 2 && i / 48 < 18 ? new Color32(160, 148, 125, 255) : new Color32(0, 0, 0, 0);
                t.SetPixels32(px);
                byte[] png = t.EncodeToPNG();
                Object.DestroyImmediate(t);
                Directory.CreateDirectory(Path.Combine(root, "Assets", "Cliffs"));
                File.WriteAllBytes(Path.Combine(root, "Assets", "Cliffs", "Face.png"), png);
                string manifest = "{\"schema_version\":1,\"package_revision\":6,\"package_id\":\"T6\",\"category\":\"cliff_module\",\"coordinate_system\":\"top_left_pixels; bbox=[x,y,width,height]\",\"items\":[" +
                    $"{{\"id\":\"T_Face\",\"path\":\"Assets/Cliffs/Face.png\",\"category\":\"cliff_module\",\"width\":48,\"height\":20,\"sha256\":\"{MapLabArtImport.Sha256(png)}\",\"role\":\"cliff_face\",\"height_class\":\"low\",\"intended_pitch_down_degrees\":0,\"authoring_plane\":\"vertical_front\",\"px_per_unit\":100,\"rock_top_y_px\":4,\"base_y_px\":16,\"measured_bbox_alpha160_top_left\":[0,2,48,16],\"pivot_hint_unity_bottom_left\":[0.5,0.2],\"world_height_hint\":0.12}}" +
                    "]}";
                File.WriteAllText(Path.Combine(root, MapLabArtImport.ManifestName), manifest, Encoding.UTF8);
                MapLabArtImport.Report report = MapLabArtImport.Import(root, null, profile);
                Assert.IsTrue(report.Ok, report.ToString());
                MapLabArtSet set = AssetDatabase.LoadAssetAtPath<MapLabArtSet>(profile.SetPath);
                MapLabArtEntry e = set.Find("T_Face");
                Assert.AreEqual(MapLabArtCategory.CliffModule, e.Category);
                Assert.AreEqual("cliff_face", e.Role);
                Assert.AreEqual("low", e.HeightClass);
                Assert.AreEqual(4f, e.RockTopPx);
                Assert.AreEqual(16f, e.BasePx);
                Assert.AreEqual(100f, e.PxPerUnit);
                Assert.AreEqual(48, e.Width, "Полный холст (без обрезки по альфе)");
                Assert.IsTrue(MapLabCliffs06.KitOf(set).HasFaces);
                Assert.AreEqual(TextureWrapMode.Clamp, ((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(e.Texture))).wrapMode);
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
