using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    // Полный путь: тестовые кадры KS Sprite Renderer → разбор → проверка →
    // атласы в проекте → запись в набор. Файлы убираются после теста.
    public sealed class CreatureAnimationImportTests
    {
        private const string SetId = "zz_test_import_set";
        private string sourceRoot;
        private CreatureAnimationDatabaseAsset database;

        [SetUp]
        public void SetUp()
        {
            sourceRoot = Path.Combine(Path.GetTempPath(), "KS_AnimationImportTest_" + System.Guid.NewGuid().ToString("N")).Replace('\\', '/');
            database = ScriptableObject.CreateInstance<CreatureAnimationDatabaseAsset>();
            database.MigrateIfNeeded();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sourceRoot))
                Directory.Delete(sourceRoot, true);
            string folder = CreatureAnimationEditorData.ManagedFolder(SetId);
            if (AssetDatabase.IsValidFolder(folder))
                AssetDatabase.DeleteAsset(folder);
            if (database != null)
                Object.DestroyImmediate(database);
        }

        [Test]
        public void TestFrames_ImportIntoSet_WithAtlasesAndSourceNumbers()
        {
            int written = CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            Assert.IsFalse(package.HasErrors, string.Join("\n", package.Issues));
            Assert.AreEqual(written, package.FileCount);
            Assert.AreEqual(5, package.Groups.Count, "Ожидание, ходьба, атака, удар, смерть.");

            CreatureAnimationSetData set = database.AddSet(SetId, "Тест");
            bool applied = CreatureAnimationImporter.Apply(database, set, package, CreatureAnimationImportMode.Replace, out string report);

            Assert.IsTrue(applied, string.Join("\n", package.Issues));
            Assert.AreEqual(new Vector2Int(CreatureAnimationTestFrames.Width, CreatureAnimationTestFrames.Height), set.CanvasSize);
            Assert.AreEqual(CreatureAnimationSetStatus.Ready, set.Status);
            CreatureAnimationFrames walk = set.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.BackLeft);
            Assert.AreEqual(CreatureAnimationTestFrames.ExportNumbers(-1, 22, 3).Count, walk.FrameCount);
            Assert.AreEqual(-1, walk.SourceFrameNumbers[0], "Отрицательный старт сохранён.");
            Assert.AreEqual(22, walk.SourceFrameNumbers[walk.FrameCount - 1], "Последний кадр экспорта сохранён.");
            Assert.IsTrue(walk.Frames.All(sprite => sprite != null));
            Assert.IsTrue(walk.Frames.All(sprite => Mathf.RoundToInt(sprite.rect.width) == CreatureAnimationTestFrames.Width &&
                                                    Mathf.RoundToInt(sprite.rect.height) == CreatureAnimationTestFrames.Height),
                "Кадр атласа равен холсту: опора не прыгает.");
            string atlasPath = AssetDatabase.GetAssetPath(walk.Frames[0]);
            StringAssert.StartsWith(CreatureAnimationEditorData.ManagedFolder(SetId) + "/", atlasPath, "Кадры скопированы в проект, внешний путь не нужен.");
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsFalse(importer.mipmapEnabled);

            // Повторный импорт той же ячейки заменяет кадры, а не плодит наборы.
            int setsBefore = database.Sets.Count;
            CreatureAnimationImportPackage again = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(Path.Combine(sourceRoot, "Idle"), "*.png", SearchOption.AllDirectories));
            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, again, CreatureAnimationImportMode.Replace, out _),
                string.Join("\n", again.Issues));
            Assert.AreEqual(setsBefore, database.Sets.Count);
            Assert.AreEqual(CreatureAnimationTestFrames.ExportNumbers(1, 20, 3).Count,
                set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front).FrameCount);
        }

        [Test]
        public void BrokenFile_OrWrongCanvas_LeavesSetUntouched()
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            CreatureAnimationSetData set = database.AddSet(SetId, "Тест");
            CreatureAnimationImportPackage first = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(Path.Combine(sourceRoot, "Idle"), "*.png", SearchOption.AllDirectories));
            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, first, CreatureAnimationImportMode.Replace, out _));
            Sprite before = set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front).Frames[0];

            string broken = Path.Combine(sourceRoot, "Idle", "Front", "Idle_Front_0021.png");
            File.WriteAllText(broken, "это не картинка");
            CreatureAnimationImportPackage corrupted = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(Path.Combine(sourceRoot, "Idle"), "*.png", SearchOption.AllDirectories));
            Assert.IsFalse(CreatureAnimationImporter.Apply(database, set, corrupted, CreatureAnimationImportMode.Replace, out _));
            Assert.IsTrue(corrupted.HasErrors);
            Assert.AreSame(before, set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front).Frames[0],
                "Ошибочный пакет не оставляет частично заменённую анимацию.");
            File.Delete(broken);

            Texture2D wrong = new Texture2D(32, 32);
            File.WriteAllBytes(Path.Combine(sourceRoot, "Idle", "Back", "Idle_Back_0021.png"), wrong.EncodeToPNG());
            Object.DestroyImmediate(wrong);
            CreatureAnimationImportPackage mismatched = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(Path.Combine(sourceRoot, "Idle"), "*.png", SearchOption.AllDirectories));
            Assert.IsFalse(CreatureAnimationImporter.Apply(database, set, mismatched, CreatureAnimationImportMode.Replace, out _));
            Assert.IsTrue(mismatched.Issues.Any(issue => issue.Message.Contains("холстом")));
            Assert.AreSame(before, set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front).Frames[0]);
        }
    }
}
