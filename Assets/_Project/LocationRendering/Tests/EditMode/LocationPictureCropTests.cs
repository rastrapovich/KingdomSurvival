using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Кадрирование обычного рисунка места: обрезанная копия рисунка — ровно
    // та часть, что в рамке, исходный файл цел, раскладка остаётся на своих
    // местах рисунка, раскидка за рамкой убирается.
    public sealed class LocationPictureCropTests
    {
        private const string Folder = "Assets/__PictureCropTest";

        [TearDown]
        public void Clean()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void CropKeepsFramedPixelsAndMovesLayout()
        {
            Sprite picture = CreatePicture(200, 100);
            string original = AssetDatabase.GetAssetPath(picture.texture);
            byte[] originalBytes = File.ReadAllBytes(original);
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "crop_test", CanvasWidth = 200, CanvasHeight = 100, HexesAcross = 20 };
            location.Entrances.Add(new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(120, 60) });
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, Background = picture };
            LocationScatterLayer layer = new LocationScatterLayer();
            layer.Instances.Add(new LocationScatterInstance { Key = 1, Position = new Vector2(.5f, .5f) });   // (100, 50) — в рамке
            layer.Instances.Add(new LocationScatterInstance { Key = 2, Position = new Vector2(.05f, .05f) }); // (10, 5) — за рамкой
            visual.ScatterLayers.Add(layer);

            RectInt rect = new RectInt(50, 20, 100, 70);
            Assert.That(LocationPictureCrop.Problem(location, visual, new RectInt(0, 0, 200, 100)), Is.Not.Null, "весь рисунок — кадрировать нечего");
            Assert.That(LocationPictureCrop.Problem(location, visual, rect), Is.Null);
            Assert.That(LocationPictureCrop.CreateAssets(location, visual, rect, out Sprite sprite, out Texture2D normal, out string message), Is.True, message);
            Assert.That(normal, Is.Null);
            Assert.That(LocationPictureCrop.SourceSize(sprite), Is.EqualTo(new Vector2(100, 70)));
            Assert.That(File.ReadAllBytes(original), Is.EqualTo(originalBytes), "исходный файл не меняется");

            // Пиксель (x, y) места (Y вниз) хранит x и номер строки текстуры (Y вверх).
            Texture2D cropped = new Texture2D(2, 2);
            try
            {
                Assert.That(cropped.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture))), Is.True);
                Assert.That(cropped.width, Is.EqualTo(100));
                Assert.That(cropped.height, Is.EqualTo(70));
                Color32 topLeft = cropped.GetPixel(0, 69);
                Assert.That(topLeft.r, Is.EqualTo(50), "левый край рамки");
                Assert.That(topLeft.g, Is.EqualTo(100 - 1 - 20), "верхний край рамки");
                Color32 bottomRight = cropped.GetPixel(99, 0);
                Assert.That(bottomRight.r, Is.EqualTo(149));
                Assert.That(bottomRight.g, Is.EqualTo(100 - 90));
            }
            finally
            {
                Object.DestroyImmediate(cropped);
            }

            LocationRebase.Result moved = LocationRebase.Translate(location, visual, new Vector2(rect.x, rect.y), new Vector2(rect.width, rect.height));
            Assert.That(location.CanvasWidth, Is.EqualTo(100));
            Assert.That(location.CanvasHeight, Is.EqualTo(70));
            Assert.That(location.Entrances[0].Point.X, Is.EqualTo(70));
            Assert.That(location.Entrances[0].Point.Y, Is.EqualTo(40));
            Assert.That(moved.ScatterRemoved, Is.EqualTo(1));
            Assert.That(layer.Instances.Count, Is.EqualTo(1));
            Assert.That(LocationVisualGeometry.ToPixel(location, layer.Instances[0].Position).x, Is.EqualTo(50).Within(.01f));
            Assert.That(LocationVisualGeometry.ToPixel(location, layer.Instances[0].Position).y, Is.EqualTo(30).Within(.01f));
        }

        // Рисунок width×height: красный — x, зелёный — строка текстуры.
        private static Sprite CreatePicture(int width, int height)
        {
            Directory.CreateDirectory(Folder);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = new Color32((byte)x, (byte)y, 0, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = Folder + "/picture.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
