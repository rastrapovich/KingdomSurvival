using System.Collections;
using System.IO;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.LocationRendering.Editor;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Окно «Генератор арен»: предпросмотр собирается той же сборкой, что в
    // бою; вкладка «Обработка кадра» строит панель прямой правки; галерея
    // собирает арены по одной. Кадры — в Logs/ArenaGenerator для осмотра.
    public sealed class ArenaGeneratorWindowTests
    {
        private const string ShotFolder = "Logs/ArenaGenerator";

        [UnityTest]
        public IEnumerator Window_BuildsPreview_FrameTab_AndGallery()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            string view = MapLabGameViews.ActiveId;
            EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);
            ArenaGeneratorWindow.Open();
            ArenaGeneratorWindow window = EditorWindow.GetWindow<ArenaGeneratorWindow>();
            try
            {
                double until = EditorApplication.timeSinceStartup + 90;
                while ((window.PreviewDriver == null || !window.PreviewDriver.Shown) && EditorApplication.timeSinceStartup < until)
                    yield return null;
                Assert.That(window.PreviewDriver, Is.Not.Null, window.StatusText);
                Assert.That(window.PreviewDriver.Shown, Is.True, "кадр арены показан: " + window.StatusText);
                for (int i = 0; i < 10; i++) yield return null;
                Assert.That(window.StatusText, Does.Contain("Арена собрана"), window.StatusText);
                Assert.That(window.ReportText, Is.Not.Empty, "отчёт генерации");
                Debug.Log("Генератор арен (окно): " + window.StatusText + "\n" + window.ReportText);
                Save(window.PreviewDriver.Arena, "window_preview.png");

                // Виды карты: каждый — свой кадр той же арены.
                foreach (string id in new[] { MapLabMapViews.ClassicId, MapLabMapViews.GloomId, MapLabMapViews.SepiaId })
                {
                    MapLabGameViews.Select(id);
                    window.PreviewDriver.Arena.ApplyActiveLook();
                    Save(window.PreviewDriver.Arena, "window_view_" + id + ".png");
                }
                MapLabGameViews.Select(view);

                ArenaGeneratorWindow.OpenFrame();
                yield return null;
                Assert.That(window.rootVisualElement.Query<Label>().ToList().Exists(label => label.text.StartsWith("Правится: ")),
                    Is.True, "панель обработки кадра арены построена");
                EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);

                window.StartGalleryForTest(false);
                Assert.That(window.GalleryCount, Is.EqualTo(9));
                Assert.That(window.PreviewDriver, Is.Null, "на время галереи предпросмотр освобождён");
                until = EditorApplication.timeSinceStartup + 240;
                while (window.GalleryRunning && EditorApplication.timeSinceStartup < until)
                    yield return null;
                Assert.That(window.GalleryRunning, Is.False, "галерея собрана: " + window.StatusText);
                Assert.That(window.StatusText, Does.Contain("Галерея готова"), window.StatusText);
            }
            finally
            {
                MapLabGameViews.Select(view);
                window.Close();
            }
        }

        // Земля и слой объектов одной картинкой (для осмотра).
        private static void Save(MapLabArena arena, string name)
        {
            RenderTexture ground = arena.Render(1280, 720);
            Texture2D a = Read(ground), b = Read(arena.OverlayTexture);
            if (b != null && b.width == a.width && b.height == a.height)
            {
                Color[] pa = a.GetPixels(), pb = b.GetPixels();
                for (int i = 0; i < pa.Length; i++) pa[i] = Color.Lerp(pa[i], new Color(pb[i].r, pb[i].g, pb[i].b, 1f), pb[i].a);
                a.SetPixels(pa);
                a.Apply();
            }
            Directory.CreateDirectory(ShotFolder);
            File.WriteAllBytes(Path.Combine(ShotFolder, name), a.EncodeToPNG());
            Object.DestroyImmediate(a);
            if (b != null) Object.DestroyImmediate(b);
        }

        private static Texture2D Read(RenderTexture texture)
        {
            if (texture == null) return null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            return copy;
        }
    }
}
