using System.Collections;
using System.Linq;
using System.Reflection;
using KingdomSurvival.MapLab.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Поле ассетов окна «Генератор карты»: ровная земля MapLab с рекой, все
    // ассеты каталога стоят на ней тем же конвейером, что детали мест карты.
    public sealed class MapLabAssetFieldTests
    {
        private const string PrefsKey = "KingdomSurvival.MapLab.AssetField";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        [Timeout(600000)]
        public void AssetField_EveryAssetStandsOnFlatGroundWithRiver_AndArrangeMovesThem()
        {
            if (!System.IO.File.Exists(MapLabAssets.ScenePath))
                Assert.Ignore("Сцены генератора нет.");
            string keep = EditorPrefs.GetString(PrefsKey, string.Empty);
            EditorPrefs.DeleteKey(PrefsKey);
            MapLabGeneratorWindow window = ScriptableObject.CreateInstance<MapLabGeneratorWindow>();
            MapLabSettings copy = null;
            MapLabSettings original = null;
            MapLabView view = null;
            try
            {
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                view = MapLabAssets.FindView();
                Assert.IsNotNull(view, "вид генератора в сцене");
                original = view.Settings;
                copy = Object.Instantiate(original);
                view.Settings = copy;
                typeof(MapLabGeneratorWindow).GetField("view", Any).SetValue(window, view);
                typeof(MapLabGeneratorWindow).GetMethod("RebuildCards", Any).Invoke(window, null);

                MapLabData mapBefore = view.Data;
                typeof(MapLabGeneratorWindow).GetMethod("BuildField", Any).Invoke(window, null);
                Assert.IsTrue((bool)typeof(MapLabGeneratorWindow).GetField("fieldBuilt", Any).GetValue(window), "поле построено");
                IList items = (IList)typeof(MapLabGeneratorWindow).GetField("fieldItems", Any).GetValue(window);
                Assert.Greater(items.Count, 20, "на поле — ассеты каталога");
                MapLabView fieldView = (MapLabView)typeof(MapLabGeneratorWindow).GetField("fieldView", Any).GetValue(window);
                Assert.IsNotNull(fieldView, "у поля свой вид");
                Assert.AreNotSame(view, fieldView, "поле не рисуется поверх карты окна");
                Assert.AreSame(mapBefore, view.Data, "карта окна не перестроена полем");
                MapLabData data = fieldView.Data;
                Assert.AreEqual(0, data.Objects.Count(o => o.Kind == MapLabObjectKind.Conifer || o.Kind == MapLabObjectKind.Deciduous || o.Kind == MapLabObjectKind.DeciduousGroup), "на поляне нет леса");
                // Фигура группы — один человечек, а не шесть ракурсов.
                int figureFrames = data.FeatureSprites.Count(f => f.Kind == "asset-field" && f.ArtId.StartsWith("Bandit_"));
                Assert.LessOrEqual(figureFrames, 1, "фигура ватаги — одна на поле");
                int drawn = data.FeatureSprites.Count(f => f.Kind == "asset-field");
                Debug.Log($"Поле ассетов: ассетов {items.Count}, нарисовано {drawn}, без рисунка: {string.Join(", ", MapLabArtBuilder.MissingFeatureArt)}");
                Assert.GreaterOrEqual(drawn, items.Count - MapLabArtBuilder.MissingFeatureArt.Count, "каждый ассет с рисунком стоит на поле");
                Assert.Greater(drawn, items.Count * 0.9f, "почти все ассеты нашли рисунок");
                Assert.Greater(data.Rivers.Count, 0, "река рядом");
                Assert.AreEqual(0, data.Objects.Count(o => o.Kind == MapLabObjectKind.House), "на поле нет деревень");

                // Ровная земля: разброс высот под ассетами мал.
                float min = float.MaxValue, max = float.MinValue;
                foreach (MapLabFeatureSprite f in data.FeatureSprites)
                {
                    min = Mathf.Min(min, f.Base.y);
                    max = Mathf.Max(max, f.Base.y);
                }
                Assert.Less(max - min, 1.5f, "земля под ассетами ровная");

                // «Квадрат»: позиции на одной сетке.
                typeof(MapLabGeneratorWindow).GetMethod("Arrange", Any).Invoke(window, new object[] { 0 });
                var spots = fieldView.Data.FeatureSprites.Where(f => f.Kind == "asset-field").Select(f => f.Point).Distinct().ToList();
                int columns = spots.Select(p => Mathf.Round(p.x * 10f)).Distinct().Count();
                int rows = spots.Select(p => Mathf.Round(p.y * 10f)).Distinct().Count();
                Debug.Log($"Квадрат: столбцов {columns}, рядов {rows}, мест {spots.Count}");
                Assert.LessOrEqual(Mathf.Abs(columns - rows), 2, "квадратная сетка");
                Assert.IsTrue(EditorPrefs.HasKey(PrefsKey), "расстановка сохранена");
                Snapshot(fieldView, "asset_field_square.png");
            }
            finally
            {
                if (view != null && original != null)
                    view.Settings = original;
                if (copy != null)
                    Object.DestroyImmediate(copy);
                Object.DestroyImmediate(window);
                if (string.IsNullOrEmpty(keep))
                    EditorPrefs.DeleteKey(PrefsKey);
                else
                    EditorPrefs.SetString(PrefsKey, keep);
            }
        }

        // Снимок поля для ручной проверки (только если задан KS_SCREENSHOT_DIR).
        private static void Snapshot(MapLabView view, string name)
        {
            string dir = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
            Camera cam = view.CameraRig != null ? view.CameraRig.Camera : null;
            if (string.IsNullOrEmpty(dir) || cam == null)
                return;
            RenderTexture rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            RenderTexture keepTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = keepTarget;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
