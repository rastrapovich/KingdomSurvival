using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // Просвечивание высоких предметов и тень камня: в дереве с галочкой
    // «Просвечивает» плавно открывается круг вокруг человека за ним (сам
    // рисунок не прозрачен целиком) и закрывается, когда он вышел или стоит
    // впереди; без галочки ничего не меняется. Камень с опорой в середине
    // рисунка отбрасывает две проекции силуэта — от опоры и от низа рисунка.
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
                // Круг вокруг человека, а не весь предмет.
                Assert.That(renderer.ObjectImages("tree")[0].color.a, Is.EqualTo(1), "Сам рисунок не становится прозрачным целиком.");
                IReadOnlyList<Vector4> holes = renderer.ObjectSeeThroughHoles("tree");
                Assert.That(holes.Count, Is.EqualTo(1));
                Assert.That(holes[0].w, Is.EqualTo(1).Within(1e-4f));
                Assert.That(holes[0].z, Is.GreaterThan(0));
                Assert.That(renderer.ObjectImages("tree")[0].bounds.size.x, Is.GreaterThan(holes[0].z * 2), "Круг меньше дерева.");
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.ObjectImages("tree")[0].GetPropertyBlock(block);
                Assert.That(block.GetVectorArray("_KsHoles")[0], Is.EqualTo(holes[0]), "Круг передан шейдеру рисунка.");

                // Тот же человек за камнем без галочки — камень не меняется.
                renderer.SetActors(new[] { Actor(new Vector2(1200, 600)) }, 1);
                renderer.SetTime(13, 2);
                Assert.That(renderer.ObjectSeeThroughLevel("rock"), Is.EqualTo(1));
                Assert.That(renderer.ObjectSeeThroughLevel("tree"), Is.EqualTo(1), "Ушёл из-за дерева — оно снова плотное.");
                Assert.That(renderer.ObjectSeeThroughHoles("tree"), Is.Empty, "Круг закрылся.");

                // Перед деревом — его не закрывает.
                renderer.SetActors(new[] { Actor(new Vector2(700, 760)) }, 2);
                renderer.SetTime(13, 3);
                Assert.That(renderer.ObjectSeeThroughLevel("tree"), Is.EqualTo(1));
            }
        }

        [Test]
        public void StoneWithPivotInTheMiddle_CastsShadowFromPivotAndFromItsBottomEdge()
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
                float anchorY = image.transform.parent.position.y;
                // От опоры — верх камня в полдень (короткая тень вверх).
                Assert.That(GroundY(image, "Тень от солнца"), Is.EqualTo(anchorY).Within(1e-4f));
                // От низа рисунка — его нижняя половина, когда солнце под углом.
                Assert.That(GroundY(image, "Тень от солнца · от низа"), Is.EqualTo(image.bounds.min.y).Within(.05f));
            }
        }

        private static float GroundY(SpriteRenderer image, string name)
        {
            Transform shadow = image.transform.parent.Find(name);
            Assert.That(shadow, Is.Not.Null, "У камня есть «" + name + "».");
            SpriteRenderer renderer = shadow.GetComponent<SpriteRenderer>();
            Assert.That(renderer.enabled, Is.True, name);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetFloat("_GroundY");
        }
    }
}
