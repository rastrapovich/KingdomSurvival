using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12Н: настоящий импорт папки из «Проводника» (временная папка вне
    // проекта): копии в управляемой папке, рисунок — спрайт, нормаль — Normal
    // map и вторая текстура _NormalMap рисунка; повторный импорт сохраняет
    // ID и GUID; замена одного ракурса не трогает остальные.
    public sealed class ArtAssetImportTests
    {
        private string source;
        private ArtAssetDatabaseAsset catalog;
        private readonly List<string> folders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            source = Path.Combine(Path.GetTempPath(), "ks_art_import_" + System.Guid.NewGuid().ToString("N").Substring(0, 8)).Replace('\\', '/');
            Directory.CreateDirectory(source);
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (ArtAssetDefinition asset in catalog.assets)
                folders.Add(ArtAssetImporter.AssetFolder(asset));
            foreach (string folder in folders.Distinct())
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            Object.DestroyImmediate(catalog);
            if (Directory.Exists(source)) Directory.Delete(source, true);
        }

        private void Png(string relative, int width, int height, Color color)
        {
            string path = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(Enumerable.Repeat(color, width * height).ToArray());
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static readonly Color Normal = new Color(.5f, .5f, 1, 1);

        private ArtAssetImportResult ImportCart(System.Func<ArtAssetImportGroup, ArtAssetDefinition> target = null)
        {
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { source + "/Телега" });
            return ArtAssetImporter.Import(catalog, plan, target, false);
        }

        [Test]
        public void FolderWithSixViews_ImportsOneRecord_WithAssignedNormals()
        {
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                Png("Телега/" + ArtAssetLabels.ViewFolder(view) + "/color.png", 64, 48, new Color(.6f, .4f, .2f, 1));
                Png("Телега/" + ArtAssetLabels.ViewFolder(view) + "/normal.png", 64, 48, Normal);
            }
            ArtAssetImportResult result = ImportCart();
            Assert.That(result.Errors, Is.Empty, string.Join("; ", result.Errors));
            Assert.That(catalog.assets.Count, Is.EqualTo(1));
            ArtAssetDefinition cart = catalog.assets[0];
            Assert.That(cart.Name, Is.EqualTo("Телега"));
            Assert.That(cart.ViewCount, Is.EqualTo(6));
            Assert.That(cart.NormalCount, Is.EqualTo(6));
            foreach (ArtAssetView view in ArtAssetLabels.Views)
            {
                ArtAssetPartView slot = cart.MainPart.FindView(view);
                string path = AssetDatabase.GetAssetPath(slot.Sprite);
                StringAssert.StartsWith(ArtAssetImporter.AssetFolder(cart) + "/", path, "Внешний арт копируется в управляемую папку.");
                Assert.That(SpriteNormalMaps.Find(slot.Sprite) == slot.NormalMap, Is.True, "Нормаль подключена к рисунку своего ракурса.");
                Assert.That(SpriteNormalMaps.IsNormalMapImport(slot.NormalMap), Is.True, "Нормаль — данные, не sRGB-картинка.");
                TextureImporter color = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(color.sRGBTexture, Is.True);
                Assert.That(color.textureType, Is.EqualTo(TextureImporterType.Sprite));
            }
            Assert.That(File.Exists(source + "/Телега/Front/color.png"), Is.True, "Источники пользователя не меняются.");
            Assert.That(ArtAssetValidator.Validate(catalog, cart).Where(issue => issue.Level != ArtAssetIssueLevel.Info), Is.Empty);
        }

        [Test]
        public void Reimport_KeepsIdAndGuids_AndReplacingOneViewKeepsOthers()
        {
            Png("Телега/Front/color.png", 32, 32, Color.red);
            Png("Телега/Front/normal.png", 32, 32, Normal);
            Png("Телега/Back/color.png", 32, 32, Color.green);
            ImportCart();
            ArtAssetDefinition cart = catalog.assets.Single();
            string id = cart.Id;
            string frontPath = AssetDatabase.GetAssetPath(cart.MainPart.FindView(ArtAssetView.Front).Sprite);
            string frontGuid = AssetDatabase.AssetPathToGUID(frontPath);
            Sprite back = cart.MainPart.FindView(ArtAssetView.Back).Sprite;

            Png("Телега/Front/color.png", 32, 32, Color.blue);
            ArtAssetImportResult result = ImportCart(group => catalog.assets[0]);
            Assert.That(result.Updated.Count, Is.EqualTo(1));
            Assert.That(catalog.assets.Count, Is.EqualTo(1), "Повторный импорт в ту же запись — не дубликат.");
            ArtAssetDefinition updated = catalog.assets.Single();
            Assert.That(updated.Id, Is.EqualTo(id));
            Sprite front = updated.MainPart.FindView(ArtAssetView.Front).Sprite;
            Assert.That(AssetDatabase.GetAssetPath(front), Is.EqualTo(frontPath));
            Assert.That(AssetDatabase.AssetPathToGUID(frontPath), Is.EqualTo(frontGuid), "GUID и .meta сохранены — ссылки мест не рвутся.");
            Assert.That(File.ReadAllBytes(frontPath), Is.EqualTo(File.ReadAllBytes(source + "/Телега/Front/color.png")), "Содержимое обновилось.");
            Assert.That(SpriteNormalMaps.Find(front), Is.Not.Null, "Пара рисунок–нормаль сохранилась.");

            // Один файл в ячейку «Сзади»: остальные ракурсы не сбрасываются.
            Png("одиночный.png", 40, 40, Color.white);
            ArtAssetImporter.AssignFile(catalog, updated, updated.MainPart, ArtAssetView.Back, source + "/одиночный.png", ArtAssetFileKind.Color);
            Assert.That(updated.MainPart.FindView(ArtAssetView.Back).Sprite.rect.width, Is.EqualTo(40), "Заменён рисунок «Сзади».");
            Assert.That(AssetDatabase.GetAssetPath(updated.MainPart.FindView(ArtAssetView.Back).Sprite), Is.EqualTo(AssetDatabase.GetAssetPath(back)));
            Assert.That(updated.MainPart.FindView(ArtAssetView.Front).Sprite == front, Is.True, "«Спереди» не тронут.");

            // Несовпадающая нормаль: понятное предупреждение.
            Png("кривая_нормаль.png", 16, 16, Normal);
            string warning = ArtAssetImporter.AssignFile(catalog, updated, updated.MainPart, ArtAssetView.Back, source + "/кривая_нормаль.png", ArtAssetFileKind.Normal);
            StringAssert.Contains("не совпадает", warning);
            Assert.That(ArtAssetValidator.Validate(catalog, updated).Any(issue => issue.Level == ArtAssetIssueLevel.Warning && issue.Text.Contains("размер нормали")), Is.True);

            // Снятие нормали снимает и вторую текстуру импорта.
            Sprite backSprite = updated.MainPart.FindView(ArtAssetView.Back).Sprite;
            ArtAssetImporter.ClearNormal(catalog, updated.MainPart, ArtAssetView.Back);
            Assert.That(updated.MainPart.FindView(ArtAssetView.Back).NormalMap, Is.Null);
            Assert.That(SpriteNormalMaps.Find(backSprite), Is.Null);
        }

        // Нормаль, записанная с гаммой sRGB (как из Blender не в Raw): «плоская»
        // (128,128,255) стала (188,188,255). Проверка это видит, «Исправить
        // гамму» возвращает длину векторов к 1. Нормаль, подключённая к рисунку,
        // но не записанная в карточке, подхватывается «Исправить подключения».
        [Test]
        public void GammaEncodedNormal_IsDetectedAndFixed_AndLostRecordIsRepaired()
        {
            Png("Телега/Front/color.png", 32, 32, Color.red);
            Png("Телега/Front/normal.png", 32, 32, new Color(188 / 255f, 188 / 255f, 1, 1));
            ImportCart();
            ArtAssetDefinition cart = catalog.assets.Single();
            ArtAssetPartView slot = cart.MainPart.FindView(ArtAssetView.Front);
            Assert.That(ArtAssetDrawing.NormalLength(slot.NormalMap), Is.GreaterThan(ArtAssetValidator.GammaLength));
            Assert.That(ArtAssetValidator.Validate(catalog, cart).Any(issue => issue.Text.Contains("гамма-коррекцией")), Is.True);

            Assert.That(ArtAssetValidator.FixNormalGamma(cart), Is.EqualTo(1));
            Assert.That(ArtAssetDrawing.NormalLength(slot.NormalMap), Is.EqualTo(1f).Within(.03f));
            Assert.That(ArtAssetValidator.Validate(catalog, cart).Any(issue => issue.Text.Contains("гамма-коррекцией")), Is.False);

            // Запись нормали потеряна (например, Undo импорта), а к рисунку она подключена.
            Texture2D normal = slot.NormalMap;
            slot.NormalMap = null;
            Assert.That(ArtAssetValidator.Validate(catalog, cart).Any(issue => issue.Text.Contains("не записана в карточке")), Is.True);
            Assert.That(ArtAssetValidator.RepairNormals(cart), Is.EqualTo(1));
            Assert.That(slot.NormalMap == normal, Is.True);
        }

        [Test]
        public void NameMatchAlone_DoesNotOverwrite()
        {
            Png("Телега/Front/color.png", 16, 16, Color.red);
            ImportCart();
            ImportCart();
            Assert.That(catalog.assets.Count, Is.EqualTo(2), "Без явного «обновить» совпадение имени создаёт новую запись.");
            Assert.That(catalog.assets[0].Id, Is.Not.EqualTo(catalog.assets[1].Id));
        }

        [Test]
        public void FailedObject_LeavesNoBrokenRecord()
        {
            Png("Пакет/Бочка/Front/color.png", 16, 16, Color.red);
            Png("Пакет/Телега/Front/color.png", 16, 16, Color.red);
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { source + "/Пакет" });
            // Файл одного объекта исчез между разбором и импортом.
            File.Delete(source + "/Пакет/Бочка/Front/color.png");
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan, null, false);
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(catalog.assets.Select(asset => asset.Name), Is.EquivalentTo(new[] { "Телега" }), "Ошибка одного объекта не портит остальные.");
        }

        // Ассет, загруженный прежним разбором (кадры в частях с именем самого
        // объекта, основа пуста), чинится новой загрузкой той же папки.
        [Test]
        public void ReimportIntoMisparsedAsset_MovesFramesToTheMainPart()
        {
            for (int i = 1; i <= 2; i++) Png("LowGrass/Front/LowGrass_000" + i + ".png", 32, 32, new Color(.2f, .6f, .2f, 1));
            ArtAssetDefinition broken = new ArtAssetDefinition { Name = "LowGrass" };
            broken.Parts.Add(new ArtAssetPart { Name = "Lowgrass" });
            broken.Parts.Add(new ArtAssetPart { Name = "LowGrass" });
            catalog.assets.Add(broken);
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { source + "/LowGrass" });
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan, _ => broken, false);
            Assert.That(result.Errors, Is.Empty, string.Join("; ", result.Errors));
            ArtAssetDefinition fixedAsset = catalog.assets.Single();
            Assert.That(fixedAsset.Parts.Count, Is.EqualTo(1), "Ошибочные части убраны.");
            Assert.That(fixedAsset.MainPart.FindView(ArtAssetView.Front).FrameCount, Is.EqualTo(2));
            Assert.That(fixedAsset.ViewCount, Is.EqualTo(1));
        }

        // Кадры анимации — листом, как в Базе анимаций: страница с кадрами и
        // страница нормалей той же раскладки (вторая текстура); места берут
        // анимацию из листа; снятые кадры — лист убран.
        [Test]
        public void AnimatedFrames_AreBuiltIntoASheet_WithNormals()
        {
            for (int i = 0; i < 4; i++)
            {
                Png("Трава/Front/" + i.ToString("000") + ".png", 40, 60, new Color(.1f, .3f + i * .15f, .1f, 1));
                Png("Трава/Front/" + i.ToString("000") + "_n.png", 40, 60, Normal);
            }
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { source + "/Трава" });
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan, null, false);
            Assert.That(result.Errors, Is.Empty, string.Join("; ", result.Errors));
            ArtAssetDefinition grass = catalog.assets.Single();
            ArtAssetPartView slot = grass.MainPart.FindView(ArtAssetView.Front);
            Assert.That(slot.FrameCount, Is.EqualTo(4));
            Assert.That(slot.HasSheet, Is.True, string.Join("; ", result.Warnings));
            Assert.That(slot.SheetFrames.Select(sprite => sprite.texture).Distinct().Count(), Is.EqualTo(1), "Четыре кадра — одна страница.");
            Assert.That(slot.SheetFrames.All(sprite => sprite.rect.size == new Vector2(40, 60)), Is.True, "Кадр в листе — своего размера.");
            Texture2D normalPage = slot.SheetNormals[0];
            Assert.That(normalPage, Is.Not.Null);
            Assert.That(SpriteNormalMaps.Find(slot.SheetFrames[0]), Is.EqualTo(normalPage), "Нормали — второй текстурой страницы.");
            Assert.That(slot.NormalOf(slot.SheetFrames[2]), Is.EqualTo(normalPage));
            Assert.That(ArtAssetSheets.IsStale(slot), Is.False);
            StringAssert.StartsWith(ArtAssetImporter.AssetFolder(grass) + "/" + ArtAssetSheets.FolderName + "/", AssetDatabase.GetAssetPath(normalPage));

            KingdomSurvival.BattlefieldDatabase.LocationResolvedVisual resolved = KingdomSurvival.BattlefieldDatabase.LocationVisualResolver.Resolve(
                new KingdomSurvival.BattlefieldDatabase.LocationVisualObject { Id = "g1", AssetId = grass.Id, Scale = 1 }, null, catalog);
            Assert.That(resolved.Main.Frames, Is.EqualTo(slot.SheetFrames.ToArray()), "Места показывают анимацию из листа.");
            Assert.That(resolved.Main.Sprite, Is.EqualTo(slot.SheetFrames[0]));

            ArtAssetImporter.ClearFrames(catalog, grass.MainPart, ArtAssetView.Front);
            Assert.That(slot.HasSheet, Is.False);
            string sheets = ArtAssetImporter.AssetFolder(grass) + "/" + ArtAssetSheets.FolderName;
            Assert.That(!AssetDatabase.IsValidFolder(sheets) || AssetDatabase.FindAssets(string.Empty, new[] { sheets }).Length == 0, Is.True,
                "Страницы снятого листа убраны.");
        }
    }
}
