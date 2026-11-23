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
                window.Close();
            }
        }

        // Обработка кадра арены: вкладки «Рисунки» (ветер), «Атмосфера» (облака,
        // лучи) и «Персонажи» есть; ветер качает деревья — два кадра с ветром
        // через долю секунды различаются, без ветра совпадают.
        [UnityTest]
        public IEnumerator FrameTab_HasWindAtmosphereCharacters_AndWindSwaysTrees()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);
            string tab = EditorPrefs.GetString("KS.FrameEffects.TabId", "Color");
            ArenaGeneratorWindow.Open();
            ArenaGeneratorWindow window = EditorWindow.GetWindow<ArenaGeneratorWindow>();
            try
            {
                double until = EditorApplication.timeSinceStartup + 90;
                while ((window.PreviewDriver == null || !window.PreviewDriver.Shown) && EditorApplication.timeSinceStartup < until)
                    yield return null;
                Assert.That(window.PreviewDriver, Is.Not.Null, window.StatusText);

                EditorPrefs.SetString("KS.FrameEffects.TabId", "Sprites");
                ArenaGeneratorWindow.OpenFrame();
                yield return null;
                System.Collections.Generic.List<string> buttons = window.rootVisualElement.Query<Button>().ToList().ConvertAll(button => button.text);
                foreach (string title in new[] { "Рисунки", "Атмосфера", "Персонажи" })
                    Assert.That(buttons, Does.Contain(title), "вкладка «" + title + "» на арене");
                Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Exists(block => block.text.Contains("Ветер")), Is.True, "блок «Ветер» на вкладке «Рисунки»");
                EditorPrefs.SetString("KS.FrameEffects.TabId", "Atmosphere");
                ArenaGeneratorWindow.OpenFrame();
                yield return null;
                Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Exists(block => block.text.Contains("Тени облаков")), Is.True, "тени облаков на вкладке «Атмосфера»");

                MapLabArena arena = window.PreviewDriver.Arena;
                KingdomSurvival.BattlefieldDatabase.LocationPostEffects keep = arena.Spec.Frame;
                KingdomSurvival.BattlefieldDatabase.LocationPostEffects windy = new KingdomSurvival.BattlefieldDatabase.LocationPostEffects { Enabled = true }.Repair();
                windy.Sprites.Wind = true;
                windy.Sprites.WindStrength = .4f;
                windy.Sprites.WindSpeed = 3f;
                windy.Sprites.WindTrees = 2f;
                windy.Sprites.WindPlants = 2f;
                windy.Sprites.WindDirection = 0f;
                try
                {
                    arena.Spec.Frame = windy;
                    Assert.That(arena.Windy, Is.True);
                    Texture2D first = Shot(arena, windy);
                    double wait = EditorApplication.timeSinceStartup + .4;
                    while (EditorApplication.timeSinceStartup < wait) yield return null;
                    Texture2D second = Shot(arena, windy);
                    float moved = Difference(first, second);
                    Save(arena, "wind.png");

                    windy.Sprites.Wind = false;
                    arena.Spec.Frame = windy;
                    Assert.That(arena.Windy, Is.False);
                    Texture2D calmA = Shot(arena, windy);
                    wait = EditorApplication.timeSinceStartup + .4;
                    while (EditorApplication.timeSinceStartup < wait) yield return null;
                    Texture2D calmB = Shot(arena, windy);
                    float calm = Difference(calmA, calmB);
                    Debug.Log("Ветер арены: доля изменившихся пикселей " + moved.ToString("0.0000") + ", без ветра " + calm.ToString("0.0000"));
                    Assert.That(moved, Is.GreaterThan(.002f), "с ветром деревья сдвинулись");
                    Assert.That(moved, Is.GreaterThan(calm * 4f + .001f), "без ветра кадр стоит");
                    foreach (Texture2D texture in new[] { first, second, calmA, calmB }) Object.DestroyImmediate(texture);
                }
                finally
                {
                    arena.Spec.Frame = keep;
                }
            }
            finally
            {
                EditorPrefs.SetString("KS.FrameEffects.TabId", tab);
                EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);
                window.Close();
            }
        }

        // Слой объектов кадра (деревья и кусты) — копией.
        private static Texture2D Shot(MapLabArena arena, KingdomSurvival.BattlefieldDatabase.LocationPostEffects frame)
        {
            arena.Spec.Frame = frame;
            arena.Render(1280, 720);
            return Read(arena.OverlayTexture);
        }

        // Доля пикселей, заметно различающихся по цвету или альфе.
        private static float Difference(Texture2D a, Texture2D b)
        {
            Assert.That(a, Is.Not.Null, "слой объектов");
            Assert.That(b, Is.Not.Null, "слой объектов");
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) + Mathf.Abs(pa[i].a - pb[i].a) > 24)
                    changed++;
            return changed / (float)pa.Length;
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
