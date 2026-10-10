using System.Linq;
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
            Assert.That(window.rootVisualElement.Query<UnityEngine.UIElements.Toggle>().ToList().Any(item => item.label == "Основания"), Is.True, "Галочка оснований всех предметов.");
            window.Close();
        }

        // Предпросмотр окна рисуется слоями: гексы — между землёй и
        // предметами. Верхний слой (предметы и люди) в сцене предпросмотра —
        // на прозрачном фоне, шейдер склейки на месте; кадр — вся область.
        [Test]
        public void PreviewUpperLayer_IsTransparentAroundObjects()
        {
            Assert.That(Shader.Find("Hidden/KingdomSurvival/LocationTopLayer"), Is.Not.Null, "Шейдер верхнего слоя найден.");
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_layers", DisplayName = "Слои", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Objects.Add(new LocationVisualObject
            {
                Id = "crate", Name = "Ящик", Placeholder = LocationPlaceholder.Crate, Position = new Vector2(.5f, .5f),
                Height = 3, Pivot = new Vector2(.5f, .1f), Band = LocationVisualBand.World
            });
            LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null);
            renderer.Camera.enabled = false;
            PreviewRenderUtility preview = new PreviewRenderUtility(true);
            try
            {
                preview.AddSingleGO(renderer.Root);
                preview.camera.orthographic = true;
                UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(preview.camera).SetRenderer(0);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = Color.clear;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.orthographicSize = 1080 / LocationVisualGeometry.PixelsPerUnit / 2;
                renderer.SetTime(13, 0);
                renderer.SetLayersVisible(false, true);
                preview.BeginStaticPreview(new Rect(0, 0, 192, 108));
                preview.Render(true);
                RenderTexture target = preview.camera.targetTexture;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                Object.DestroyImmediate(preview.EndStaticPreview());
                renderer.SetLayersVisible(true, true);
                // Ящик: от опоры (середина) вверх на 3 единицы мира.
                float crate = image.GetPixelBilinear(.5f, .5f + 150f / 1080).a;
                float ground = image.GetPixelBilinear(.15f, .15f).a;
                Object.DestroyImmediate(image);
                Assert.That(crate, Is.GreaterThan(.9f), "Ящик — в верхнем слое.");
                Assert.That(ground, Is.LessThan(.05f), "Земля — под гексами, верхний слой вокруг прозрачен.");
            }
            finally
            {
                preview.Cleanup();
                renderer.Dispose();
            }
        }

        // Гексы боя на месте — тот же BattlefieldView, что в бою (поверх
        // рисунка), а настройки сетки и вида гекса — те же карточки, что в
        // Базе полей боя.
        [Test]
        public void ArenaHexesUseBattlefieldViewAndBattlefieldDatabaseCards()
        {
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            LocationDatabaseWindow window = EditorWindow.GetWindow<LocationDatabaseWindow>();
            try
            {
                window.CreateGUI();
                BattlefieldView view = window.rootVisualElement.Q<BattlefieldView>();
                Assert.That(view, Is.Not.Null);
                Assert.That(view.WorldUnderlay, Is.True, "кадр прозрачный — гексы поверх рисунка места, как в бою");
                Assert.That(view.pickingMode, Is.EqualTo(PickingMode.Ignore));

                ScrollView settings = (ScrollView)typeof(LocationDatabaseWindow).GetField("settings", flags).GetValue(window);
                System.Collections.Generic.List<string> texts = new System.Collections.Generic.List<string>();
                settings.Query<TextElement>().ForEach(item => texts.Add(item.text));
                foreach (string expected in new[] { "СЕТКА", "Сдвиг X", "Сдвиг Y", "Сбросить положение", "ВИД ГЕКСА",
                             "Свой вид у этого поля", "Цвета состояний в бою", "Показать состояния на предпросмотре", "Сбросить вид гекса" })
                    Assert.That(texts, Has.Some.EqualTo(expected), expected);
                Assert.That(texts, Has.None.EqualTo("Масштаб сетки (поле)"), "старые поля сетки заменены карточками");
            }
            finally
            {
                window.Close();
            }
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

        // Размер PNG по заголовку: по нему окно решает, похож ли брошенный
        // рисунок на фон места.
        [Test]
        public void PngSize_IsReadFromHeader()
        {
            string path = Path.Combine(Path.GetTempPath(), "ks_png_size_" + System.Guid.NewGuid().ToString("N") + ".png");
            Texture2D texture = new Texture2D(321, 123, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            try
            {
                Assert.That(LocationDatabaseWindow.PngSize(path), Is.EqualTo(new Vector2Int(321, 123)));
            }
            finally
            {
                File.Delete(path);
            }
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
