using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12М: рисунок подробнее мест (PPU заметно выше 108) — предупреждение
    // проверки; «Уменьшить рисунок» уменьшает все PNG папки ассета, делит
    // PPU (высота в мире прежняя) и пересобирает лист кадров.
    public sealed class ArtAssetDensityTests
    {
        private string source, backup;
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            string id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            source = Path.Combine(Path.GetTempPath(), "ks_art_density_" + id).Replace('\\', '/');
            backup = Path.Combine(Path.GetTempPath(), "ks_art_density_backup_" + id).Replace('\\', '/');
            Directory.CreateDirectory(source);
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (ArtAssetDefinition asset in catalog.assets)
                if (AssetDatabase.IsValidFolder(ArtAssetImporter.AssetFolder(asset))) AssetDatabase.DeleteAsset(ArtAssetImporter.AssetFolder(asset));
            Object.DestroyImmediate(catalog);
            foreach (string folder in new[] { source, backup })
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        private static void Png(string path, int width, int height, params Color[] pixels)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels.Length == 1 ? Enumerable.Repeat(pixels[0], width * height).ToArray() : pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Color Read(string path, out Vector2Int size)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            size = new Vector2Int(texture.width, texture.height);
            Color color = texture.GetPixel(0, 0);
            Object.DestroyImmediate(texture);
            return color;
        }

        private ArtAssetDefinition ImportGrass(int width, int height)
        {
            for (int i = 0; i < 3; i++)
            {
                Png(source + "/Трава/Front/" + i.ToString("000") + ".png", width, height, new Color(.1f, .3f + i * .2f, .1f, 1));
                Png(source + "/Трава/Front/" + i.ToString("000") + "_n.png", width, height, new Color(.5f, .5f, 1, 1));
            }
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, ArtAssetImportParser.ParsePaths(new[] { source + "/Трава" }), null, false);
            Assert.That(result.Errors, Is.Empty, string.Join("; ", result.Errors));
            return catalog.assets.Single();
        }

        [Test]
        public void Reduce_HalvesFiles_KeepsWorldHeight_AndRebuildsSheet()
        {
            ArtAssetDefinition grass = ImportGrass(40, 60);
            grass.PixelsPerUnit = 240;
            ArtAssetPartView slot = grass.MainPart.FindView(ArtAssetView.Front);
            Assert.That(slot.HasSheet, Is.True);
            float height = grass.Height;
            Assert.That(ArtAssetValidator.Validate(catalog, grass).Any(issue => issue.Text.Contains("подробнее, чем видно в игре")), Is.True,
                "PPU 240 при 108 у мест — предупреждение.");
            Assert.That(ArtAssetDensity.UsefulFactors(grass), Is.EqualTo(new[] { 2 }), "Вчетверо (60) — уже грубее места.");

            ArtAssetReduceResult result = ArtAssetDensity.Reduce(catalog, grass, 2, backup);
            Assert.That(result.Done, Is.True, result.Error);
            Assert.That(result.Files, Is.EqualTo(6), "Три кадра и три нормали.");
            Assert.That(grass.PixelsPerUnit, Is.EqualTo(120).Within(1e-3f));
            Assert.That(grass.Height, Is.EqualTo(height).Within(1e-4f), "Высота в мире прежняя.");
            Assert.That(slot.Sprite.rect.size, Is.EqualTo(new Vector2(20, 30)), "Рисунок вдвое меньше.");
            Assert.That(SpriteNormalMaps.SourceSize(slot.NormalMap), Is.EqualTo(new Vector2Int(20, 30)), "Нормаль — тоже.");
            Assert.That(slot.HasSheet, Is.True, string.Join("; ", result.Warnings));
            Assert.That(slot.SheetFrames.All(sprite => sprite.rect.size == new Vector2(20, 30)), Is.True, "Лист пересобран из уменьшенных кадров.");
            Assert.That(ArtAssetSheets.IsStale(slot), Is.False);
            Assert.That(ArtAssetValidator.Validate(catalog, grass).Any(issue => issue.Text.Contains("подробнее, чем видно в игре")), Is.False);

            Assert.That(result.BackupFolder, Is.Not.Null);
            List<string> copies = Directory.GetFiles(result.BackupFolder, "*.png").ToList();
            Assert.That(copies.Count, Is.EqualTo(6), "Копия исходников — до перезаписи.");
            Read(copies[0], out Vector2Int original);
            Assert.That(original, Is.EqualTo(new Vector2Int(40, 60)));
        }

        [Test]
        public void Reduce_OddSize_IsRejected_FilesUntouched()
        {
            ArtAssetDefinition grass = ImportGrass(41, 60);
            grass.PixelsPerUnit = 240;
            ArtAssetReduceResult result = ArtAssetDensity.Reduce(catalog, grass, 2, null);
            Assert.That(result.Done, Is.False);
            StringAssert.Contains("не делится на 2", result.Error);
            Assert.That(grass.PixelsPerUnit, Is.EqualTo(240));
            Assert.That(grass.MainPart.FindView(ArtAssetView.Front).Sprite.rect.width, Is.EqualTo(41));
        }

        // Цвет усредняется с премультипликацией альфы: прозрачный зелёный не
        // окрашивает край красного. Нормаль — средний вектор единичной длины.
        [Test]
        public void Downscale_PremultipliesColor_AndNormalizesNormals()
        {
            string color = source + "/color.png", normal = source + "/normal.png";
            Color red = new Color(1, 0, 0, 1), clearGreen = new Color(0, 1, 0, 0);
            Png(color, 2, 2, red, clearGreen, red, clearGreen);
            Assert.That(ArtAssetDensity.Downscale(color, 2, false), Is.True);
            Color c = Read(color, out Vector2Int size);
            Assert.That(size, Is.EqualTo(Vector2Int.one));
            Assert.That(c.r, Is.EqualTo(1).Within(.01f));
            Assert.That(c.g, Is.EqualTo(0).Within(.01f), "Нет каймы от цвета прозрачных пикселей.");
            Assert.That(c.a, Is.EqualTo(.5f).Within(.01f));

            Color right = new Color(1, .5f, .5f, 1), up = new Color(.5f, .5f, 1, 1);
            Png(normal, 2, 2, right, up, right, up);
            Assert.That(ArtAssetDensity.Downscale(normal, 2, true), Is.True);
            Color n = Read(normal, out _);
            Vector3 vector = new Vector3(n.r * 2 - 1, n.g * 2 - 1, n.b * 2 - 1);
            Assert.That(vector.magnitude, Is.EqualTo(1).Within(.02f), "Вектор нормализован.");
            Assert.That(vector.x, Is.EqualTo(.707f).Within(.02f));
            Assert.That(vector.z, Is.EqualTo(.707f).Within(.02f));
            Assert.That(n.a, Is.EqualTo(1).Within(.01f), "Альфа нормали не премультиплицируется.");
        }
    }
}
