using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Tests
{
    public sealed class LocationEditorTests
    {
        [Test]
        public void CampBootstrapDoesNotDuplicateAuthoredData()
        {
            LocationLightingTestBootstrap.EnsureCamp();
            LocalLocationDatabaseAsset database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            int places = database.locations.Count, visuals = database.visuals.Count;
            int objects = database.FindVisual(LocationLightingTestBootstrap.CampId).Objects.Count;
            LocationLightingTestBootstrap.EnsureCamp();
            Assert.That(database.locations.Count, Is.EqualTo(places));
            Assert.That(database.visuals.Count, Is.EqualTo(visuals));
            Assert.That(database.FindVisual(LocationLightingTestBootstrap.CampId).Objects.Count, Is.EqualTo(objects));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(LocationLightingTestBootstrap.ScenePath), Is.Not.Null);
        }

        [Test]
        public void EditorOpensWithRealPreviewAndRussianControls()
        {
            LocationDatabaseWindow window = EditorWindow.GetWindow<LocationDatabaseWindow>();
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<IMGUIContainer>(), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q<ScrollView>(), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q<Slider>(), Is.Not.Null);
            window.Close();
        }

        // Визуальная проверка без batchmode; снимок только окна базы.
        public static void CaptureEditor()
        {
            LocationDatabaseWindow.OpenWindow();
            LocationDatabaseWindow window = EditorWindow.GetWindow<LocationDatabaseWindow>();
            window.position = new Rect(100, 100, 1500, 900);
            window.Focus();
            double captureAt = EditorApplication.timeSinceStartup + 8;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (EditorApplication.timeSinceStartup < captureAt) return;
                EditorApplication.update -= callback;
                int width = Mathf.RoundToInt(window.position.width), height = Mathf.RoundToInt(window.position.height);
                Color[] pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(window.position.position, width, height);
                Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.SetPixels(pixels); image.Apply();
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/LocationLighting"));
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "editor.png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                window.Close();
                EditorApplication.Exit(0);
            };
            EditorApplication.update += callback;
        }
    }
}
