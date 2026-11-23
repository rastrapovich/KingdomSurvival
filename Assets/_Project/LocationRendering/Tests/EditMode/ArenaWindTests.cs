using System.Collections;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Ветер арены: качание решает признак рисунка (пни, брёвна, коряги, камни стоят), а
    // граница высоты — доля роста самого рисунка, отдельно для деревьев и для кустов и травы.
    public sealed class ArenaWindTests
    {
        [Test]
        public void SwayFlag_FollowsRole_StumpsLogsAndRocksStand()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            System.Collections.Generic.List<MapLabDecorArt> decor = new System.Collections.Generic.List<MapLabDecorArt>();
            MapLabArtSet set = MapLabArtAssets.BuildSet(true, 1f, decor);
            Assert.That(set, Is.Not.Null);
            int stand = 0, trees = 0, plants = 0;
            foreach (MapLabDecorArt art in decor)
            {
                MapLabArtEntry entry = set.Find(art.Id);
                Assert.That(entry, Is.Not.Null, art.Id);
                switch (art.Role)
                {
                    case MapLabDecorRole.Tree:
                        Assert.That(entry.WindKind, Is.EqualTo(1f), "дерево качается: " + art.Id);
                        trees++;
                        break;
                    case MapLabDecorRole.TallPlant:
                    case MapLabDecorRole.LowGrass:
                    case MapLabDecorRole.TallGrass:
                        Assert.That(entry.WindKind, Is.EqualTo(2f), "куст и трава качаются: " + art.Id);
                        plants++;
                        break;
                    default:
                        Assert.That(entry.WindKind, Is.EqualTo(0f), "пень, бревно, коряга и камень стоят: " + art.Id + " (" + art.Role + ")");
                        stand++;
                        break;
                }
            }
            Debug.Log("Ветер арены: качаются деревьев " + trees + ", кустов и травы " + plants + "; стоят (пни, коряги арены, камни) " + stand);
            Assert.That(trees + plants, Is.GreaterThan(0));
            Object.DestroyImmediate(set);
        }

        // Граница высоты — доля роста самого рисунка: чем она выше, тем меньше качается верхняя часть
        // (деревья и растения — каждые по своей границе).
        [UnityTest]
        public IEnumerator HeightFloor_IsRelative_SeparateForTreesAndPlants()
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
                    float treesLow = 0f, treesHigh = 0f, plantsLow = 0f, plantsHigh = 0f, treesPlantFloor0 = 0f, treesPlantFloor9 = 0f;
                    // Деревья: граница 0 — качается весь рисунок, 0,9 — только макушка.
                    yield return Moved(arena, Frame(2f, 0f, 0f, .5f), value => treesLow = value);
                    yield return Moved(arena, Frame(2f, 0f, .9f, .5f), value => treesHigh = value);
                    // Растения — то же по своей границе.
                    yield return Moved(arena, Frame(0f, 2f, .5f, 0f), value => plantsLow = value);
                    yield return Moved(arena, Frame(0f, 2f, .5f, .9f), value => plantsHigh = value);
                    // Граница растений на деревья не влияет.
                    yield return Moved(arena, Frame(2f, 0f, .2f, 0f), value => treesPlantFloor0 = value);
                    yield return Moved(arena, Frame(2f, 0f, .2f, .9f), value => treesPlantFloor9 = value);
                    Debug.Log("Граница ветра: деревья 0 → " + treesLow.ToString("0.0000") + ", 0,9 → " + treesHigh.ToString("0.0000") +
                              "; растения 0 → " + plantsLow.ToString("0.0000") + ", 0,9 → " + plantsHigh.ToString("0.0000") +
                              "; деревья при границе растений 0 / 0,9: " + treesPlantFloor0.ToString("0.0000") + " / " + treesPlantFloor9.ToString("0.0000"));
                    Assert.That(treesLow, Is.GreaterThan(.0005f), "деревья качаются");
                    Assert.That(treesHigh, Is.LessThan(treesLow * .8f), "высокая граница деревьев уменьшает качание");
                    Assert.That(plantsLow, Is.GreaterThan(.0002f), "растения качаются");
                    Assert.That(plantsHigh, Is.LessThan(plantsLow * .8f), "высокая граница растений уменьшает качание");
                    Assert.That(treesPlantFloor9, Is.EqualTo(treesPlantFloor0).Within(treesPlantFloor0 * .2f + .0003f), "граница растений деревьев не касается");
                }
                finally
                {
                    arena.Spec.Frame = keep;
                }
            }
            finally
            {
                EditorPrefs.SetInt("KS.ArenaGenerator.Mode", 0);
                window.Close();
            }
        }

        private static LocationPostEffects Frame(float trees, float plants, float treeFloor, float plantFloor)
        {
            LocationPostEffects frame = new LocationPostEffects { Enabled = true }.Repair();
            frame.Sprites.Wind = true;
            frame.Sprites.WindStrength = .4f;
            frame.Sprites.WindSpeed = 3f;
            frame.Sprites.WindGusts = 0f;
            frame.Sprites.WindDirection = 0f;
            frame.Sprites.WindTrees = trees;
            frame.Sprites.WindPlants = plants;
            frame.Sprites.WindTreeFloor = treeFloor;
            frame.Sprites.WindPlantFloor = plantFloor;
            return frame;
        }

        // Доля пикселей слоя объектов, изменившихся между двумя кадрами ветра с паузой.
        private static IEnumerator Moved(MapLabArena arena, LocationPostEffects frame, System.Action<float> result)
        {
            arena.Spec.Frame = frame;
            arena.Render(1280, 720);
            Texture2D first = Read(arena.OverlayTexture);
            double wait = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < wait) yield return null;
            arena.Spec.Frame = frame;   // окно-предпросмотр за паузу подменило обработку кадра своей
            arena.Render(1280, 720);
            Texture2D second = Read(arena.OverlayTexture);
            Assert.That(first, Is.Not.Null, "слой объектов");
            Color32[] pa = first.GetPixels32(), pb = second.GetPixels32();
            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) + Mathf.Abs(pa[i].a - pb[i].a) > 24)
                    changed++;
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            result(changed / (float)pa.Length);
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
