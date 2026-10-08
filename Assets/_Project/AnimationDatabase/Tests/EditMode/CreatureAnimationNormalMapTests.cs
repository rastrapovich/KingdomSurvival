using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    // ПР-12М: карты нормалей кадров. Рядом с кадром лежит «имя_n.png» (рендер
    // нормалей из Blender) → в атласе появляется страница нормалей с той же
    // раскладкой, подключённая к спрайтам как `_NormalMap`. Частичные нормали
    // не подключаются, лишние — предупреждение.
    public sealed class CreatureAnimationNormalMapTests
    {
        private const string SetId = "zz_test_normal_set";
        private string sourceRoot;
        private CreatureAnimationDatabaseAsset database;

        [SetUp]
        public void SetUp()
        {
            sourceRoot = Path.Combine(Path.GetTempPath(), "KS_AnimationNormalTest_" + System.Guid.NewGuid().ToString("N")).Replace('\\', '/');
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

        // Нормали как из Blender: (0.5·n + 0.5) в RGB, полусфера к зрителю.
        private static void WriteNormals(string root) => WriteNormals(root, null, 1);

        // target — отдельная папка той же структуры с теми же именами, что у
        // кадров (без «_n»); scale — другое разрешение рендера.
        private static void WriteNormals(string root, string target, int scale)
        {
            foreach (string color in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            {
                if (CreatureAnimationImportParser.IsNormalFile(color))
                    continue;
                Texture2D texture = new Texture2D(CreatureAnimationTestFrames.Width * scale, CreatureAnimationTestFrames.Height * scale, TextureFormat.RGBA32, false, true);
                Color32[] pixels = new Color32[texture.width * texture.height];
                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        float nx = (x + .5f) / texture.width * 2 - 1, ny = (y + .5f) / texture.height * 2 - 1;
                        float nz = Mathf.Sqrt(Mathf.Max(0, 1 - nx * nx - ny * ny));
                        pixels[y * texture.width + x] = new Color32((byte)((nx * .5f + .5f) * 255), (byte)((ny * .5f + .5f) * 255), (byte)((nz * .5f + .5f) * 255), 255);
                    }
                }
                texture.SetPixels32(pixels);
                string path = target == null
                    ? Path.Combine(Path.GetDirectoryName(color), Path.GetFileNameWithoutExtension(color) + "_n.png")
                    : Path.Combine(target, color.Substring(root.Length).TrimStart('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
        }

        private CreatureAnimationSetData ImportFramesWithoutNormals(out CreatureAnimationImportPackage package)
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            package = CreatureAnimationImportParser.Analyze(sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            CreatureAnimationSetData set = database.AddSet(SetId, "Тест нормалей");
            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, package, CreatureAnimationImportMode.Replace, out _), string.Join("\n", package.Issues));
            return set;
        }

        // Нормали без «_normal» — отдельной папкой с теми же именами, что у
        // кадров (проход Normals в другую папку): подключаются и к ячейке, и
        // всей папкой; другое разрешение подгоняется под кадр.
        [Test]
        public void NormalsWithoutSuffix_InSeparateFolder_AttachToCellAndSet()
        {
            CreatureAnimationSetData set = ImportFramesWithoutNormals(out CreatureAnimationImportPackage package);
            string normalsRoot = sourceRoot + "_normals";
            try
            {
                WriteNormals(sourceRoot, normalsRoot, 2);
                string[] normals = Directory.GetFiles(normalsRoot, "*.png", SearchOption.AllDirectories);
                Assert.IsFalse(normals.Any(CreatureAnimationImportParser.IsNormalFile), "У файлов нет суффикса нормали.");
                Assert.IsTrue(CreatureAnimationNormals.LooksLikeNormalMap(normals[0]), "Рендер нормалей узнаётся по содержимому.");
                string colorFrame = package.Groups.First().Cells.Values.First().Frames[0].Path;
                Assert.IsFalse(CreatureAnimationNormals.LooksLikeNormalMap(colorFrame), "Цветной кадр — не нормаль.");

                // Ячейка: файлы ячейки в любом порядке, рендер вдвое крупнее.
                CreatureAnimationFrames idle = set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front);
                string cellFolder = Path.GetDirectoryName(package.Groups.First(group => group.ChosenAction == CreatureAnimationAction.Idle)
                    .Cells[CreatureAnimationDirection.Front].Frames[0].Path).Replace('\\', '/');
                string cellNormals = normalsRoot + cellFolder.Substring(sourceRoot.Length);
                List<string> files = Directory.GetFiles(cellNormals, "*.png").Reverse().ToList();
                Assert.IsTrue(CreatureAnimationNormals.Match(idle, files, out List<string> ordered, out string problem), problem);
                Assert.IsTrue(CreatureAnimationNormals.Attach(idle.Frames, ordered, out string message), message);
                StringAssert.Contains("Размер подогнан", message);
                Assert.AreEqual(idle.FrameCount, CreatureAnimationNormals.CountWithNormals(idle));

                // Весь набор — папкой той же структуры.
                string report = CreatureAnimationNormals.AttachFolder(set, normalsRoot, normals, out int cells, out int failures);
                Assert.AreEqual(0, failures, report);
                foreach (CreatureAnimationClipData clip in set.Clips)
                    foreach (CreatureAnimationFrames cell in clip.Directions)
                        Assert.AreEqual(cell.FrameCount, CreatureAnimationNormals.CountWithNormals(cell), clip.Action + " → " + cell.Direction + "\n" + report);
            }
            finally
            {
                if (Directory.Exists(normalsRoot))
                    Directory.Delete(normalsRoot, true);
            }
        }

        // Номера нормалей другие (рендер с иного кадра), но файлов ровно по
        // кадру — сопоставление по порядку, с пояснением.
        [Test]
        public void CellNormals_WithOtherNumbers_MatchByOrder()
        {
            CreatureAnimationSetData set = ImportFramesWithoutNormals(out _);
            CreatureAnimationFrames idle = set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front);
            List<string> files = Enumerable.Range(0, idle.FrameCount).Select(i => "normal_" + (500 + i).ToString("0000") + ".png").Reverse().ToList();
            Assert.IsTrue(CreatureAnimationNormals.Match(idle, files, out List<string> ordered, out string problem, out string note), problem);
            Assert.IsNotNull(note);
            Assert.AreEqual("normal_0500.png", ordered[0]);
            Assert.AreEqual(idle.FrameCount, ordered.Count);
        }

        [Test]
        public void NormalFiles_AreNotFrames_AndAttachToEveryFrame()
        {
            int written = CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            WriteNormals(sourceRoot);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));

            Assert.IsFalse(package.HasErrors, string.Join("\n", package.Issues));
            Assert.AreEqual(written, package.FileCount, "Файлы нормалей не считаются кадрами.");
            Assert.IsTrue(package.Groups.SelectMany(group => group.Cells.Values).SelectMany(cell => cell.Frames)
                .All(frame => frame.NormalPath.EndsWith("_n.png")));
        }

        [Test]
        public void PartialNormals_AreNotAttached_WithWarning()
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            WriteNormals(sourceRoot);
            string removed = Directory.GetFiles(sourceRoot, "*_n.png", SearchOption.AllDirectories).First();
            File.Delete(removed);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));

            Assert.IsTrue(package.Issues.Any(issue => issue.ToString().Contains("нормали есть не у всех кадров")), string.Join("\n", package.Issues));
            string folder = Path.GetDirectoryName(removed).Replace('\\', '/');
            Assert.IsTrue(package.Groups.SelectMany(group => group.Cells.Values).SelectMany(cell => cell.Frames)
                .Where(frame => Path.GetDirectoryName(frame.Path).Replace('\\', '/') == folder)
                .All(frame => frame.NormalPath.Length == 0));
        }

        [Test]
        public void Import_BuildsNormalPage_AndConnectsItToSprites()
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            WriteNormals(sourceRoot);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            CreatureAnimationSetData set = database.AddSet(SetId, "Тест нормалей");

            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, package, CreatureAnimationImportMode.Replace, out string report),
                string.Join("\n", package.Issues));
            StringAssert.Contains("С картами нормалей", report);

            CreatureAnimationFrames walk = set.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.BackLeft);
            Sprite frame = walk.Frames[0];
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(frame));
            SecondarySpriteTexture normal = importer.secondarySpriteTextures.Single(item => item.name == CreatureAnimationAtlasBuilder.NormalMapName);
            Assert.IsNotNull(normal.texture);
            Assert.AreEqual(frame.texture.width, normal.texture.width, "Страница нормалей — той же раскладки.");
            TextureImporter normalImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal.texture));
            Assert.AreEqual(TextureImporterType.NormalMap, normalImporter.textureType);
            Assert.IsFalse(CreatureAnimationImporter.FindUnusedAtlases(database).Contains(AssetDatabase.GetAssetPath(normal.texture)),
                "Страница нормалей используемого атласа не считается лишней.");
        }

        // ПР-12Н: нормали, отрендеренные позже, подключаются к уже загруженным
        // кадрам папкой той же структуры — без перезагрузки кадров и правки базы.
        [Test]
        public void LaterNormals_AttachToLoadedFrames_WithoutReimport()
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            CreatureAnimationSetData set = database.AddSet(SetId, "Тест нормалей");
            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, package, CreatureAnimationImportMode.Replace, out _), string.Join("\n", package.Issues));
            CreatureAnimationFrames walk = set.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.BackLeft);
            Sprite[] before = walk.Frames.ToArray();
            Assert.AreEqual(0, CreatureAnimationNormals.CountWithNormals(walk), "Кадры загружены без нормалей.");

            WriteNormals(sourceRoot);
            string[] normals = Directory.GetFiles(sourceRoot, "*_n.png", SearchOption.AllDirectories);
            string report = CreatureAnimationNormals.AttachFolder(set, sourceRoot, normals, out int cells, out int failures);

            Assert.AreEqual(0, failures, report);
            Assert.Greater(cells, 0, report);
            foreach (CreatureAnimationClipData clip in set.Clips)
                foreach (CreatureAnimationFrames cell in clip.Directions)
                    Assert.AreEqual(cell.FrameCount, CreatureAnimationNormals.CountWithNormals(cell), clip.Action + " → " + cell.Direction + "\n" + report);
            CollectionAssert.AreEqual(before, walk.Frames.ToArray(), "Кадры не перезагружены.");

            Texture2D page = CreatureAnimationNormals.FindPageNormal(walk.Frames[0].texture);
            Assert.AreEqual(TextureImporterType.NormalMap, ((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(page))).textureType);
            // В прямоугольнике кадра на странице — нормаль кадра (центр полусферы смотрит на зрителя).
            Texture2D pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            pixels.LoadImage(File.ReadAllBytes(CreatureAnimationAtlasBuilder.ToAbsolute(AssetDatabase.GetAssetPath(page))));
            Rect rect = walk.Frames[0].rect;
            Color32 middle = pixels.GetPixel(Mathf.RoundToInt(rect.center.x), Mathf.RoundToInt(rect.center.y));
            Object.DestroyImmediate(pixels);
            Assert.Greater(middle.b, 240, "Синий канал — нормаль к зрителю.");
            Assert.AreEqual(128f, middle.r, 3f);

            Assert.Greater(CreatureAnimationNormals.Remove(walk.Frames), 0);
            Assert.AreEqual(0, CreatureAnimationNormals.CountWithNormals(walk), "Нормали ячейки сняты.");
        }

        [Test]
        public void CellNormals_MatchByFrameNumbers_AndRejectMissingOnes()
        {
            CreatureAnimationTestFrames.Generate(sourceRoot, database, false);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            CreatureAnimationSetData set = database.AddSet(SetId, "Тест нормалей");
            Assert.IsTrue(CreatureAnimationImporter.Apply(database, set, package, CreatureAnimationImportMode.Replace, out _));
            WriteNormals(sourceRoot);
            CreatureAnimationFrames idle = set.FindFrames(CreatureAnimationAction.Idle, CreatureAnimationDirection.Front);
            string folder = Path.GetDirectoryName(package.Groups.First(group => group.ChosenAction == CreatureAnimationAction.Idle)
                .Cells[CreatureAnimationDirection.Front].Frames[0].Path);
            List<string> normals = Directory.GetFiles(folder, "*_n.png").ToList();

            // Порядок файлов не важен: сопоставление по номерам кадров.
            normals.Reverse();
            Assert.IsTrue(CreatureAnimationNormals.Match(idle, normals, out List<string> ordered, out string problem), problem);
            for (int i = 0; i < ordered.Count; i++)
            {
                CreatureAnimationImportParser.TryParseFrameNumber(CreatureAnimationNormals.BaseName(ordered[i]), out int number);
                Assert.AreEqual(idle.SourceFrameNumbers[i], number);
            }
            normals.RemoveAt(0);
            Assert.IsFalse(CreatureAnimationNormals.Match(idle, normals, out _, out problem));
            StringAssert.Contains("нет нормалей для кадров", problem);
        }
    }
}
