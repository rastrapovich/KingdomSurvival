using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Tests
{
    // Один вид без нормалей (09.11.2026): пакет отдельных PNG с каталогом —
    // каждый PNG отдельный ассет одного вида, русское название, назначение,
    // высота по силуэту, опора у дна силуэта; кайма очищается без изменения
    // альфы; повторный импорт сохраняет ID; нормали пропускаются.
    public sealed class ArtAssetPackImportTests
    {
        private string source;
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            source = Path.Combine(Path.GetTempPath(), "ks_art_pack_" + System.Guid.NewGuid().ToString("N").Substring(0, 8)).Replace('\\', '/');
            Directory.CreateDirectory(source + "/Assets/Trees");
            Directory.CreateDirectory(source + "/Assets/Vegetation");
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (ArtAssetDefinition asset in catalog.assets)
            {
                string folder = ArtAssetImporter.AssetFolder(asset);
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
            Object.DestroyImmediate(catalog);
            if (Directory.Exists(source)) Directory.Delete(source, true);
        }

        // Силуэт: столбец 20..40 × строки 10..90 (Y вверх) на холсте 60×100,
        // по краю — полупрозрачная красная кайма.
        private void Tree(string relative)
        {
            Texture2D texture = new Texture2D(60, 100, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[60 * 100];
            for (int y = 0; y < 100; y++)
            for (int x = 0; x < 60; x++)
            {
                bool inside = x >= 20 && x <= 40 && y >= 10 && y <= 90;
                bool edge = !inside && x >= 19 && x <= 41 && y >= 9 && y <= 91;
                pixels[y * 60 + x] = inside ? new Color32(90, 110, 60, 255) : edge ? new Color32(255, 0, 0, 120) : new Color32(0, 0, 0, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(source, relative), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private void Catalog()
        {
            File.WriteAllText(Path.Combine(source, "CATALOG_RU.csv"),
                "﻿ID;Название;Категория;Файл;Ширина px;Высота px;Предложенная высота дерева\n" +
                "Oak;Дуб;Trees;Assets/Trees/Oak.png;60;100;2\n" +
                "LowGrassModule;Модуль низкой травы;Vegetation;Assets/Vegetation/LowGrassModule.png;60;100;\n", new UTF8Encoding(false));
        }

        [Test]
        public void Pack_CreatesOneViewAssets_WithNamesPurposeHeightPivot_AndReimportKeepsIds()
        {
            Tree("Assets/Trees/Oak.png");
            Tree("Assets/Vegetation/LowGrassModule.png");
            Tree("Assets/Trees/Oak_normal.png");
            Catalog();

            string report = ArtAssetPackImport.Import(source, catalog, false);
            Assert.That(catalog.assets.Count, Is.EqualTo(2), report);
            ArtAssetDefinition oak = catalog.assets.Single(item => item.Name == "Дуб");
            ArtAssetDefinition grass = catalog.assets.Single(item => item.Name == "Модуль низкой травы");
            Assert.That(oak.Category, Is.EqualTo(ArtAssetCategory.Trees));
            Assert.That(oak.Purpose, Is.EqualTo(ArtAssetPurpose.Both));
            Assert.That(grass.Purpose, Is.EqualTo(ArtAssetPurpose.ArenaOnly));
            Assert.That(oak.UsableOnArena && oak.UsableOnMap && grass.UsableOnArena && !grass.UsableOnMap, Is.True);
            Assert.That(oak.ViewCount, Is.EqualTo(1), "Один вид.");
            Assert.That(oak.PrimaryView, Is.EqualTo(ArtAssetView.Front));
            Assert.That(oak.HasNormal(ArtAssetView.Front), Is.False, "Нормаль пакета не подключается.");
            // Видимый силуэт 81 строка → высота дерева 2 × 3 ед.
            Sprite sprite = oak.MainSprite(ArtAssetView.Front);
            Assert.That(81 / oak.PixelsPerUnit, Is.EqualTo(6f).Within(.05f));
            // Опора — дно силуэта по центру столбца.
            Vector2 pivot = oak.Settings(ArtAssetView.Front).Pivot;
            Assert.That(pivot.y, Is.EqualTo(10.5f / 100f).Within(.01f));
            Assert.That(pivot.x, Is.EqualTo(30.5f / 60f).Within(.02f));

            // Кайма: цвет края — от соседнего непрозрачного, альфа прежняя.
            Texture2D imported = new Texture2D(2, 2);
            imported.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture)));
            Color32 edge = imported.GetPixels32()[50 * 60 + 19];
            Assert.That(edge.a, Is.EqualTo(120));
            Assert.That(edge.r, Is.EqualTo(90), "Красная кайма очищена.");
            Assert.That(imported.GetPixels32().Count(p => p.a > 0), Is.EqualTo(23 * 83), "Силуэт не изменился.");
            Object.DestroyImmediate(imported);
            Assert.That(File.ReadAllBytes(source + "/Assets/Trees/Oak.png").Length, Is.GreaterThan(0), "Исходник на месте.");

            // Повторный импорт — те же записи и ID; правка назначения сохраняется.
            string oakId = oak.Id;
            oak.Purpose = ArtAssetPurpose.MapOnly;
            ArtAssetPackImport.Import(source, catalog, false);
            Assert.That(catalog.assets.Count, Is.EqualTo(2));
            Assert.That(catalog.assets.Any(item => item.Id == oakId && item.Purpose == ArtAssetPurpose.MapOnly), Is.True);
        }

        [Test]
        public void DropNormals_RemovesNormalsFromPlan_AndUnresolvedNormalFiles()
        {
            Tree("Assets/Trees/Oak.png");
            Tree("Assets/Trees/Oak_n.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { source + "/Assets/Trees" }, null, ArtAssetView.Front);
            int dropped = ArtAssetImportParser.DropNormals(plan);
            Assert.That(dropped, Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.Groups.SelectMany(g => g.Slots).All(slot => slot.NormalPath == null), Is.True);
            Assert.That(plan.Groups.Count, Is.EqualTo(1), "Нормаль не стала отдельным ассетом.");
        }
    }
}
