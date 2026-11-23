using System.Collections;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Слои «впереди бойца» из общего слоя (ключ глубины) совпадают с прежними отдельными проходами камеры.
    public sealed class ArenaFrontRowsTests
    {
        [UnityTest]
        public IEnumerator SharedSourceRows_MatchPerRowPasses()
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
                MapLabArena arena = window.PreviewDriver.Arena;
                LocationPostEffects keep = arena.Spec.Frame;
                try
                {
                    foreach (bool graded in new[] { true, false })
                    {
                        LocationPostEffects frame = new LocationPostEffects { Enabled = graded }.Repair();
                        // Без ветра: два кадра в разное время при ветре различались бы сами по себе.
                        frame.Sprites.Wind = false;
                        arena.Spec.Frame = frame;
                        const int rows = 5;
                        // Прежний путь: отдельная пара проходов на строку.
                        arena.Render(1280, 720);
                        arena.PrepareFronts(0);
                        Texture2D[] old = new Texture2D[rows];
                        for (int r = 0; r < rows; r++) old[r] = Read(arena.RenderFront(r, arena.GroundZ(0.3f + 0.1f * r)));
                        // Новый путь: общий слой, строки выбираются по ключу.
                        arena.Render(1280, 720);
                        arena.PrepareFronts(rows);
                        float worst = 0f;
                        for (int r = 0; r < rows; r++)
                        {
                            Texture2D fresh = Read(arena.RenderFront(r, arena.GroundZ(0.3f + 0.1f * r)));
                            Color32[] a = old[r].GetPixels32(), b = fresh.GetPixels32();
                            int diff = 0, opaque = 0;
                            for (int i = 0; i < a.Length; i++)
                            {
                                if (a[i].a > 8) opaque++;
                                if (Mathf.Abs(a[i].a - b[i].a) > 40 || (a[i].a > 200 && b[i].a > 200 && Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 60)) diff++;
                            }
                            float share = diff / (float)Mathf.Max(1, opaque);
                            worst = Mathf.Max(worst, share);
                            Debug.Log("Строка " + r + (graded ? " (с обработкой)" : " (без обработки)") + ": пикселей объектов " + opaque + ", различий " + diff + " (" + share.ToString("0.0000") + ")");
                            if (r == 2)
                            {
                                Directory.CreateDirectory("Logs/ArenaFrontRows");
                                File.WriteAllBytes("Logs/ArenaFrontRows/old_" + (graded ? "graded" : "plain") + ".png", old[r].EncodeToPNG());
                                File.WriteAllBytes("Logs/ArenaFrontRows/new_" + (graded ? "graded" : "plain") + ".png", fresh.EncodeToPNG());
                            }
                            Object.DestroyImmediate(fresh);
                        }
                        foreach (Texture2D t in old) Object.DestroyImmediate(t);
                        Assert.That(worst, Is.LessThan(.03f), "слои строк из общего слоя совпадают с отдельными проходами");
                    }
                }
                finally { arena.Spec.Frame = keep; }
            }
            finally
            {
                EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);
                window.Close();
            }
        }

        private static Texture2D Read(RenderTexture texture)
        {
            Assert.That(texture, Is.Not.Null, "слой строки");
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
