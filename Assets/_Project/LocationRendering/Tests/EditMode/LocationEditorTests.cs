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

        // ПР-12Н: в окнах редактора (сцена предпросмотра) Unity не вызывает
        // LateUpdate у Light2D — местный свет отсекался, и в предпросмотре не
        // было ни огня, ни нормалей. Рендерер обновляет источники сам.
        [Test]
        public void EditorPreview_RendersLocalLights()
        {
            float Render(bool withLight)
            {
                LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_preview_light" };
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
                LocationVisualObject light = new LocationVisualObject { Id = "l", Name = "l", LightOnly = true, Position = new Vector2(.5f, .5f) };
                light.Light.Enabled = withLight; light.Light.Radius = 4; light.Light.Intensity = 2; light.Light.Color = Color.white;
                light.Light.Animation = LocationLightAnimation.None; light.Light.Offset = Vector2.zero;
                visual.Objects.Add(light);
                LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null);
                renderer.Camera.enabled = false;
                PreviewRenderUtility preview = new PreviewRenderUtility(true);
                try
                {
                    preview.AddSingleGO(renderer.Root);
                    preview.camera.orthographic = true;
                    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(preview.camera).SetRenderer(0);
                    preview.camera.clearFlags = CameraClearFlags.SolidColor;
                    preview.camera.backgroundColor = Color.black;
                    preview.camera.transform.position = new Vector3(0, 0, -10);
                    preview.camera.orthographicSize = 3;
                    renderer.SetTime(1, 0);
                    preview.BeginStaticPreview(new Rect(0, 0, 200, 150));
                    preview.Render(true);
                    Texture2D image = preview.EndStaticPreview();
                    float sum = 0;
                    foreach (Color color in image.GetPixels()) sum += color.grayscale;
                    Object.DestroyImmediate(image);
                    return sum / (200 * 150);
                }
                finally
                {
                    preview.Cleanup();
                    renderer.Dispose();
                }
            }
            float dark = Render(false), lit = Render(true);
            Assert.That(lit, Is.GreaterThan(dark + .05f), "Ночью источник в предпросмотре освещает место: " + dark + " → " + lit);
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
