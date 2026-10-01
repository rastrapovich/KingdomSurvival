using System.Collections.Generic;
using System.IO;
using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Загрузка портрета и миниатюры с диска: копия в проект, Sprite, отказ
    // для битого файла без изменений. Созданные файлы удаляются после теста.
    public sealed class UnitArtImporterTests
    {
        private const string UnitId = "zz_test_art_unit";
        private readonly List<string> createdAssets = new List<string>();
        private string sourceFolder;

        [SetUp]
        public void SetUp()
        {
            sourceFolder = Path.Combine(Path.GetTempPath(), "KS_UnitArtTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sourceFolder);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string asset in createdAssets)
                AssetDatabase.DeleteAsset(asset);
            createdAssets.Clear();
            if (Directory.Exists(sourceFolder))
                Directory.Delete(sourceFolder, true);
        }

        private string WritePng(string name, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            string path = Path.Combine(sourceFolder, name);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return path;
        }

        [Test]
        public void ExternalPng_BecomesSpriteInManagedFolder()
        {
            string source = WritePng("portrait.png", 200, 280);
            Sprite portrait = UnitArtImporter.ImportExternal(source, UnitId, UnitArtKind.Portrait, out string error);
            Assert.IsNotNull(portrait, error);
            string path = AssetDatabase.GetAssetPath(portrait);
            createdAssets.Add(path);
            StringAssert.StartsWith(UnitArtImporter.PortraitFolder + "/" + UnitId + "__portrait__", path,
                "Файл скопирован в проект: внешняя папка после загрузки не нужна.");
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.AreEqual(200, Mathf.RoundToInt(portrait.rect.width), "Разрешение не уменьшено.");

            Sprite field = UnitArtImporter.ImportExternal(WritePng("field.png", 128, 128), UnitId, UnitArtKind.Battlefield, out error);
            Assert.IsNotNull(field, error);
            createdAssets.Add(AssetDatabase.GetAssetPath(field));
            StringAssert.StartsWith(UnitArtImporter.BattlefieldFolder + "/", AssetDatabase.GetAssetPath(field));

            List<string> unused = UnitArtImporter.FindUnused(ScriptableObject.CreateInstance<UnitDatabaseAsset>());
            CollectionAssert.Contains(unused, path, "Никому не нужная загрузка попадает в очистку.");
        }

        [Test]
        public void BrokenOrWrongFile_IsRejected_WithoutChanges()
        {
            string broken = Path.Combine(sourceFolder, "broken.png");
            File.WriteAllText(broken, "не картинка");
            Assert.IsNull(UnitArtImporter.ImportExternal(broken, UnitId, UnitArtKind.Portrait, out string error));
            StringAssert.Contains("broken.png", error);

            string text = Path.Combine(sourceFolder, "notes.txt");
            File.WriteAllText(text, "x");
            Assert.IsNull(UnitArtImporter.ImportExternal(text, UnitId, UnitArtKind.Portrait, out error));
            Assert.IsFalse(UnitArtImporter.IsImageFile(text));
        }
    }
}
