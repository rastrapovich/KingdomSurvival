using System.Collections;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12Р: раскидка в кадре. Один и тот же несимметричный рисунок с
    // поворотом, растяжением и отражением объектом и ковром ложится
    // одинаково (опора, сторона поворота, отражение); сдвиг тона красного на
    // 120° даёт зелёный у обоих; кадры ковра меняются по времени места.
    public sealed class LocationScatterPlayModeTests
    {
        private const int Width = 640, Height = 360;
        private readonly List<Object> owned = new List<Object>();
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            owned.Add(catalog);
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (Object item in owned) if (item != null) Object.Destroy(item);
            owned.Clear();
        }

        private Sprite Sprite(int width, int height, System.Func<float, float, Color> pixel)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color[] colors = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel((x + .5f) / width, (y + .5f) / height);
            texture.SetPixels(colors);
            texture.Apply();
            owned.Add(texture);
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100);
            owned.Add(sprite);
            return sprite;
        }

        // «Флажок»: древко слева, полотнище вверху справа — видно поворот и отражение.
        private static Color Flag(float x, float y, Color cloth)
        {
            if (x < .2f) return Color.white;
            if (y > .6f) return cloth;
            return Color.clear;
        }

        private ArtAssetDefinition Asset(string id, params Sprite[] frames)
        {
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = id, Name = id, PixelsPerUnit = 64, FramesPerSecond = 4, RandomPhase = false };
            ArtAssetPartView slot = asset.MainPart.View(ArtAssetView.Front);
            slot.Sprite = frames[0];
            for (int i = 1; i < frames.Length; i++) slot.Frames.Add(new ArtAssetFrame { Sprite = frames[i] });
            asset.Settings(ArtAssetView.Front).Pivot = new Vector2(.1f, .05f);
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            return asset;
        }

        private static LocalLocationDefinition Location() => new LocalLocationDefinition { Id = "zz_scatter", DisplayName = "Раскидка", CanvasWidth = 1920, CanvasHeight = 1080 };

        private static LocationScatterLayer Layer(LocationScatterMode mode, string assetId, LocationScatterInstance instance)
        {
            LocationScatterLayer layer = new LocationScatterLayer { Name = "Тест", Mode = mode, Band = LocationVisualBand.GroundDetail };
            layer.Assets.Add(new LocationScatterEntry { AssetId = assetId });
            instance.Key = layer.NextKey++;
            instance.AssetId = assetId;
            layer.Instances.Add(instance);
            return layer;
        }

        private static Texture2D Shot(LocationWorldRenderer renderer, string name)
        {
            RenderTexture target = new RenderTexture(Width, Height, 24);
            target.Create();
            renderer.Camera.targetTexture = target;
            renderer.Camera.aspect = Width / (float)Height;
            renderer.SetView(new Vector2(960, 540), 540);
            renderer.SetTime(13, 0);
            renderer.Camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            renderer.Camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Scatter"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            return texture;
        }

        private static LocationScatterInstance Look() => new LocationScatterInstance
        {
            Position = new Vector2(.5f, .55f), Scale = 1, Stretch = 1.5f, Rotation = 30, FlipX = true
        };

        // Маска «рисунок светлее фона».
        private static bool[] Mask(Texture2D shot, Texture2D empty, out Vector2 centroid, out int count)
        {
            Color[] a = shot.GetPixels(), b = empty.GetPixels();
            bool[] mask = new bool[a.Length];
            centroid = Vector2.zero;
            count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                mask[i] = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > .15f;
                if (!mask[i]) continue;
                centroid += new Vector2(i % Width, i / Width);
                count++;
            }
            if (count > 0) centroid /= count;
            return mask;
        }

        [UnityTest]
        public IEnumerator CarpetParticle_LiesLikeObject_RotationStretchFlipPivot()
        {
            Sprite flag = Sprite(32, 64, (x, y) => Flag(x, y, Color.red));
            ArtAssetDefinition asset = Asset("zz_flag", flag);
            Texture2D empty, objectShot, carpetShot;
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), new LocationVisualDefinition { LocationId = "zz_scatter", UseWorldLighting = false }, null))
                empty = Shot(renderer, "empty");
            yield return null;
            LocationVisualDefinition objects = new LocationVisualDefinition { LocationId = "zz_scatter", UseWorldLighting = false };
            objects.ScatterLayers.Add(Layer(LocationScatterMode.Objects, asset.Id, Look()));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), objects, null))
            {
                Assert.That(renderer.ScatterObjectCount(objects.ScatterLayers[0].Id), Is.EqualTo(1));
                objectShot = Shot(renderer, "flag-objects");
            }
            yield return null;
            LocationVisualDefinition carpet = new LocationVisualDefinition { LocationId = "zz_scatter", UseWorldLighting = false };
            carpet.ScatterLayers.Add(Layer(LocationScatterMode.Carpet, asset.Id, Look()));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), carpet, null))
            {
                Assert.That(renderer.ScatterCarpetCount(carpet.ScatterLayers[0].Id), Is.EqualTo(1));
                carpetShot = Shot(renderer, "flag-carpet");
            }
            bool[] a = Mask(objectShot, empty, out Vector2 objectCenter, out int objectCount);
            bool[] b = Mask(carpetShot, empty, out Vector2 carpetCenter, out int carpetCount);
            Assert.That(objectCount, Is.GreaterThan(200), "Объект виден.");
            Assert.That(carpetCount, Is.GreaterThan(200), "Ковёр виден.");
            int both = 0, any = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] && b[i]) both++;
                if (a[i] || b[i]) any++;
            }
            Assert.That(both / (float)any, Is.GreaterThan(.8f),
                "Силуэты объекта и ковра совпадают (центры " + objectCenter + " и " + carpetCenter + ").");
            Object.Destroy(empty); Object.Destroy(objectShot); Object.Destroy(carpetShot);
        }

        [UnityTest]
        public IEnumerator HueShift_TurnsRedIntoGreen_ForObjectAndCarpet()
        {
            Sprite square = Sprite(32, 32, (x, y) => new Color(.9f, .1f, .1f));
            ArtAssetDefinition asset = Asset("zz_red", square);
            foreach (LocationScatterMode mode in new[] { LocationScatterMode.Objects, LocationScatterMode.Carpet })
            {
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_scatter", UseWorldLighting = false };
                visual.ScatterLayers.Add(Layer(mode, asset.Id, new LocationScatterInstance { Position = new Vector2(.5f, .55f), Hue = 120 }));
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
                {
                    Texture2D shot = Shot(renderer, "hue-" + mode);
                    // Внутри квадрата: точка рисунка (980, 570) в кадре 540 пикселей высотой.
                    Color color = shot.GetPixel(Mathf.RoundToInt(Width / 2f + 20 / 540f * Height), Mathf.RoundToInt(Height / 2f - 30 / 540f * Height));
                    Assert.That(color.g, Is.GreaterThan(color.r * 2), mode + ": тон сдвинут к зелёному, получено " + color);
                    Object.Destroy(shot);
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CarpetFrames_FollowLocationTime()
        {
            Sprite first = Sprite(16, 16, (x, y) => Color.red), second = Sprite(16, 16, (x, y) => Color.blue);
            ArtAssetDefinition asset = Asset("zz_blink", first, second);
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_scatter", UseWorldLighting = false };
            visual.ScatterLayers.Add(Layer(LocationScatterMode.Carpet, asset.Id, new LocationScatterInstance { Position = new Vector2(.5f, .5f) }));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                LocationScatterCarpet carpet = renderer.ScatterCarpet(visual.ScatterLayers[0].Id);
                Assert.That(carpet.GroupCount, Is.EqualTo(1));
                renderer.SetTime(13, .1f);
                Assert.That(carpet.ShownSprite(0), Is.SameAs(first));
                renderer.SetTime(13, .3f);
                Assert.That(carpet.ShownSprite(0), Is.SameAs(second), "4 кадра/с: через четверть секунды — второй кадр.");
            }
            yield return null;
        }
    }
}
