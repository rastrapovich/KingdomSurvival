using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Просвечивание высоких предметов и тень от низа рисунка: дерево с
    // галочкой «Просвечивает» растворяется, пока за ним человек, и плотнеет,
    // когда он вышел вперёд; без галочки не меняется. Камень с опорой в
    // середине рисунка отбрасывает тень от нижнего края, а не от опоры.
    public sealed class LocationSeeThroughTests
    {
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
            foreach (Object item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        private ArtAssetDefinition Asset(string id, int width, int height, Vector2 pivot)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.3f, .5f, .2f, 1);
            texture.SetPixels(pixels);
            texture.Apply();
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot, 20);
            owned.Add(sprite);
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = id, Name = id, PixelsPerUnit = 20 };
            asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
            asset.Settings(ArtAssetView.Front).Pivot = pivot;
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            return asset;
        }

        private static LocalLocationDefinition Location() =>
            new LocalLocationDefinition { Id = "zz_see", DisplayName = "Лес", CanvasWidth = 1920, CanvasHeight = 1080 };

        private static LocationVisualObject Put(string id, Vector2 pixel) =>
            new LocationVisualObject { Id = id, Name = id, AssetId = id, Position = new Vector2(pixel.x / 1920, pixel.y / 1080) };

        private static LocationWorldRenderer.ActorFrame Actor(Vector2 pixel) =>
            new LocationWorldRenderer.ActorFrame { Id = "hero", Pixel = pixel };

        [Test]
        public void TallTree_SeesThrough_WhileSomeoneIsBehindIt()
        {
            ArtAssetDefinition tree = Asset("tree", 64, 256, new Vector2(.5f, 0));
            tree.SeeThrough = true;
            tree.SeeThroughAlpha = .4f;
            Asset("rock", 64, 256, new Vector2(.5f, 0));
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Objects.Add(Put("tree", new Vector2(700, 700)));
            visual.Objects.Add(Put("rock", new Vector2(1200, 700)));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                // За деревом: выше по рисунку, рисуется раньше.
                renderer.SetActors(new[] { Actor(new Vector2(700, 600)) }, 0);
                renderer.SetTime(13, 0);
                renderer.SetTime(13, .1f);
                float midway = renderer.ObjectSeeThroughLevel("tree");
                Assert.That(midway, Is.LessThan(1).And.GreaterThan(.4f), "Растворяется плавно, а не скачком.");
                renderer.SetTime(13, 1);
                Assert.That(renderer.ObjectSeeThroughLevel("tree"), Is.EqualTo(.4f).Within(1e-4f));
                Assert.That(renderer.ObjectImages("tree")[0].color.a, Is.EqualTo(.4f).Within(1e-4f));

                // Тот же человек за камнем без галочки — камень не меняется.
                renderer.SetActors(new[] { Actor(new Vector2(1200, 600)) }, 1);
                renderer.SetTime(13, 2);
                Assert.That(renderer.ObjectSeeThroughLevel("rock"), Is.EqualTo(1));
                Assert.That(renderer.ObjectSeeThroughLevel("tree"), Is.EqualTo(1), "Ушёл из-за дерева — оно снова плотное.");

                // Перед деревом — его не закрывает.
                renderer.SetActors(new[] { Actor(new Vector2(700, 760)) }, 2);
                renderer.SetTime(13, 3);
                Assert.That(renderer.ObjectSeeThroughLevel("tree"), Is.EqualTo(1));
            }
        }

        [Test]
        public void StoneWithPivotInTheMiddle_CastsShadowFromItsBottomEdge()
        {
            // Опора — на середине рисунка (как у камня: середина основания).
            Asset("stone", 100, 60, new Vector2(.5f, .5f));
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Objects.Add(Put("stone", new Vector2(960, 600)));
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetTime(13, 0);
                SpriteRenderer image = renderer.FindObject("stone");
                Transform shadow = image.transform.parent.Find("Тень от солнца");
                Assert.That(shadow, Is.Not.Null, "В полдень у камня есть тень от солнца.");
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                shadow.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
                float groundY = block.GetFloat("_GroundY");
                Assert.That(groundY, Is.EqualTo(image.bounds.min.y).Within(.05f), "Линия земли тени — низ рисунка, а не опора.");
                Assert.That(groundY, Is.LessThan(image.transform.parent.position.y - .5f));
            }
        }
    }
}
