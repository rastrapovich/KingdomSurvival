using System;
using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    // Раздел «Глобальная карта» Базы анимаций: данные, таблица ракурсов карты,
    // импорт кадров в набор карты и пакетный импорт. Временные файлы и атласы
    // убираются после теста; боевая база не открывается на запись.
    public sealed class WorldMapAnimationDatabaseTests
    {
        private const string SetId = "zz_test_worldmap_set";
        private string sourceRoot;
        private WorldMapAnimationDatabaseAsset database;
        private bool artRootExisted;

        [SetUp]
        public void SetUp()
        {
            sourceRoot = Path.Combine(Path.GetTempPath(), "KS_WorldMapAnimationTest_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            database = ScriptableObject.CreateInstance<WorldMapAnimationDatabaseAsset>();
            database.MigrateIfNeeded();
            artRootExisted = AssetDatabase.IsValidFolder(WorldMapAnimationEditorData.ArtRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sourceRoot))
                Directory.Delete(sourceRoot, true);
            string folder = WorldMapAnimationEditorData.ManagedFolder(SetId);
            if (AssetDatabase.IsValidFolder(folder))
                AssetDatabase.DeleteAsset(folder);
            if (!artRootExisted && AssetDatabase.IsValidFolder(WorldMapAnimationEditorData.ArtRoot) &&
                AssetDatabase.FindAssets(string.Empty, new[] { WorldMapAnimationEditorData.ArtRoot }).Length == 0)
            {
                AssetDatabase.DeleteAsset(WorldMapAnimationEditorData.ArtRoot);
            }
            if (database != null)
                UnityEngine.Object.DestroyImmediate(database);
        }

        // ------------------------------------------------------------------
        // Таблица ракурсов карты и секторы
        // ------------------------------------------------------------------

        [Test]
        public void Migrate_FillsDefaultDirectionMap_OneToOne()
        {
            Assert.AreEqual(WorldMapAnimationDatabaseAsset.CurrentSchemaVersion, database.SchemaVersion);
            Assert.IsTrue(database.IsDirectionMapValid());
            Assert.AreEqual(CreatureAnimationLabels.DirectionCount, database.DirectionMap.Count);
            Assert.AreEqual(WorldMapFacing.Down, database.GetFacing(CreatureAnimationDirection.Front));
            Assert.AreEqual(WorldMapFacing.DownRight, database.GetFacing(CreatureAnimationDirection.FrontRight));
            Assert.AreEqual(WorldMapFacing.UpRight, database.GetFacing(CreatureAnimationDirection.BackRight));
            Assert.AreEqual(WorldMapFacing.Up, database.GetFacing(CreatureAnimationDirection.Back));
            Assert.AreEqual(WorldMapFacing.UpLeft, database.GetFacing(CreatureAnimationDirection.BackLeft));
            Assert.AreEqual(WorldMapFacing.DownLeft, database.GetFacing(CreatureAnimationDirection.FrontLeft));
            foreach (WorldMapFacing facing in Enum.GetValues(typeof(WorldMapFacing)))
                Assert.AreEqual(facing, database.GetFacing(database.GetDirection(facing)));
            Assert.IsFalse(database.MigrateIfNeeded(), "Повторная миграция ничего не меняет.");
        }

        [Test]
        public void Migrate_RepairsBrokenDirectionMap()
        {
            JsonUtility.FromJsonOverwrite("{\"directionMap\":[]}", database);
            Assert.IsFalse(database.IsDirectionMapValid());
            Assert.IsTrue(database.MigrateIfNeeded());
            Assert.IsTrue(database.IsDirectionMapValid());
            Assert.AreEqual(WorldMapFacing.Down, database.GetFacing(CreatureAnimationDirection.Front));
        }

        [Test]
        public void AssignFacing_SwapsWithPreviousOwner_AndStaysOneToOne()
        {
            database.AssignFacing(CreatureAnimationDirection.Front, WorldMapFacing.DownRight);
            Assert.AreEqual(WorldMapFacing.DownRight, database.GetFacing(CreatureAnimationDirection.Front));
            Assert.AreEqual(WorldMapFacing.Down, database.GetFacing(CreatureAnimationDirection.FrontRight), "Прежний владелец сектора получил освободившийся.");
            Assert.IsTrue(database.IsDirectionMapValid());
            Assert.AreEqual(CreatureAnimationDirection.Front, database.GetDirection(WorldMapFacing.DownRight));

            database.ResetDirectionMap();
            Assert.AreEqual(WorldMapFacing.Down, database.GetFacing(CreatureAnimationDirection.Front));
            Assert.AreEqual(WorldMapFacing.DownRight, database.GetFacing(CreatureAnimationDirection.FrontRight));
        }

        [Test]
        public void FromScreenDirection_PicksSectorByScreenDirection()
        {
            Assert.AreEqual(WorldMapFacing.Down, WorldMapFacingLabels.FromScreenDirection(0f, 1f));
            Assert.AreEqual(WorldMapFacing.DownRight, WorldMapFacingLabels.FromScreenDirection(0.866f, 0.5f));
            Assert.AreEqual(WorldMapFacing.UpRight, WorldMapFacingLabels.FromScreenDirection(0.866f, -0.5f));
            Assert.AreEqual(WorldMapFacing.Up, WorldMapFacingLabels.FromScreenDirection(0f, -1f));
            Assert.AreEqual(WorldMapFacing.UpLeft, WorldMapFacingLabels.FromScreenDirection(-0.866f, -0.5f));
            Assert.AreEqual(WorldMapFacing.DownLeft, WorldMapFacingLabels.FromScreenDirection(-0.866f, 0.5f));
        }

        // То же правило, что WorldMapActorMotion.DirectionIndex в Scripts/Core
        // (сборка Core из тестов модуля недоступна, поэтому формула повторена).
        [Test]
        public void FromScreenDirection_MatchesActorMotionRule_AtEveryAngle()
        {
            for (int degrees = 0; degrees < 360; degrees++)
            {
                float radians = degrees * Mathf.Deg2Rad;
                float dx = Mathf.Cos(radians);
                float dy = Mathf.Sin(radians);
                double facing = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
                int expected = (int)Math.Round((180.0 - facing) / 60.0);
                expected = ((expected % 6) + 6) % 6;
                Assert.AreEqual((WorldMapFacing)expected, WorldMapFacingLabels.FromScreenDirection(dx, dy), "Угол " + degrees);
            }
        }

        [Test]
        public void PreviewArrow_RoundTripsThroughSector()
        {
            foreach (WorldMapFacing facing in Enum.GetValues(typeof(WorldMapFacing)))
            {
                Vector2 vector = WorldMapAnimationPreviewElement.FacingScreenVector(facing);
                Assert.AreEqual(facing, WorldMapFacingLabels.FromScreenDirection(vector.x, vector.y));
            }
        }

        // ------------------------------------------------------------------
        // Набор: клампы, герой
        // ------------------------------------------------------------------

        [Test]
        public void SetData_ClampsHeightAndStride()
        {
            WorldMapAnimationSetData set = database.AddSet(SetId, "Тест");
            Assert.AreEqual(WorldMapAnimationSetData.DefaultHeightInHexes, set.HeightInHexes, 0.0001f);
            Assert.AreEqual(WorldMapAnimationSetData.DefaultStrideHexesPerCycle, set.StrideHexesPerCycle, 0.0001f);
            Assert.AreEqual(WorldMapAnimationSetData.DefaultPivot, set.Animation.Pivot);

            set.SetHeightInHexes(100f);
            set.SetStrideHexesPerCycle(100f);
            Assert.AreEqual(WorldMapAnimationSetData.MaxHeightInHexes, set.HeightInHexes, 0.0001f);
            Assert.AreEqual(WorldMapAnimationSetData.MaxStrideHexesPerCycle, set.StrideHexesPerCycle, 0.0001f);
            set.SetHeightInHexes(-1f);
            set.SetStrideHexesPerCycle(0f);
            Assert.AreEqual(WorldMapAnimationSetData.MinHeightInHexes, set.HeightInHexes, 0.0001f);
            Assert.AreEqual(WorldMapAnimationSetData.MinStrideHexesPerCycle, set.StrideHexesPerCycle, 0.0001f);
        }

        [Test]
        public void HeroSet_IsNullWithoutWalkFrames()
        {
            Assert.IsNull(database.HeroSet, "Набора героя нет.");
            database.AddSet(WorldMapAnimationDatabaseAsset.DefaultHeroSetId, "Герой");
            Assert.AreEqual(WorldMapAnimationDatabaseAsset.DefaultHeroSetId, database.HeroSetId);
            Assert.IsNull(database.HeroSet, "Набор без кадров ходьбы героем не считается.");
            Assert.AreEqual(WorldMapAnimationEditorData.SetState.Empty, WorldMapAnimationEditorData.GetState(database.FindSet("hero")));

            database.SetHeroSetId("нет_такого");
            Assert.IsNull(database.HeroSet);
            Assert.IsTrue(WorldMapAnimationEditorData.CollectDataErrors(database).Any(error => error.Contains("нет_такого")), "Битая ссылка на героя — ошибка данных.");
        }

        // ------------------------------------------------------------------
        // Импорт в набор карты
        // ------------------------------------------------------------------

        [Test]
        public void Import_GeneratedFrames_GoToWorldMapFolder_AndLeaveBattleDatabaseUntouched()
        {
            CreatureAnimationDatabaseAsset battleAsset =
                AssetDatabase.LoadAssetAtPath<CreatureAnimationDatabaseAsset>(CreatureAnimationDatabaseAsset.AssetPath);
            string battleBefore = battleAsset != null ? JsonUtility.ToJson(battleAsset) : null;
            bool battleDirtyBefore = battleAsset != null && EditorUtility.IsDirty(battleAsset);
            string battleFolder = CreatureAnimationEditorData.ManagedFolder(SetId);

            CreatureAnimationTestFrames.Generate(sourceRoot, null, false);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                sourceRoot, Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories));
            WorldMapAnimationSection.PreparePackage(package);
            Assert.IsFalse(package.HasErrors, string.Join("\n", package.Issues));
            Assert.AreEqual(2, package.ChosenGroups.Count(), "Только ходьба и ожидание.");
            Assert.IsTrue(package.Issues.Any(issue => issue.Message.Contains("пропущена")), "Атака, удар и смерть пропущены с замечанием.");

            WorldMapAnimationSetData set = database.AddSet(SetId, "Тест");
            bool applied = CreatureAnimationImporter.ApplyToWorldMap(database, set, package, CreatureAnimationImportMode.Replace, out string report);

            Assert.IsTrue(applied, string.Join("\n", package.Issues));
            Assert.IsNotNull(set.Animation.FindClip(CreatureAnimationAction.Walk));
            Assert.IsNotNull(set.Animation.FindClip(CreatureAnimationAction.Idle));
            Assert.IsNull(set.Animation.FindClip(CreatureAnimationAction.Attack));
            Assert.IsNull(set.Animation.FindClip(CreatureAnimationAction.Death));
            Assert.IsTrue(set.HasWalk);
            Assert.IsTrue(set.HasCompleteWalk);
            Assert.AreEqual(WorldMapAnimationEditorData.SetState.Ready, WorldMapAnimationEditorData.GetState(set));
            Assert.AreEqual(new Vector2Int(CreatureAnimationTestFrames.Width, CreatureAnimationTestFrames.Height), set.Animation.CanvasSize);

            CreatureAnimationClipData walk = set.Animation.FindClip(CreatureAnimationAction.Walk);
            Assert.AreEqual(12f, walk.FramesPerSecond, 0.0001f, "Новое действие карты — 12 к/с, а не настройки образца боя.");
            Assert.AreEqual(CreatureAnimationPlayback.Loop, walk.Playback);
            Assert.AreEqual(0, walk.SkipLastFrames);

            CreatureAnimationFrames cell = set.Animation.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.BackLeft);
            Assert.IsTrue(cell.Frames.All(sprite => sprite != null));
            string atlasPath = AssetDatabase.GetAssetPath(cell.Frames[0]);
            StringAssert.StartsWith(WorldMapAnimationEditorData.ManagedFolder(SetId) + "/", atlasPath, "Атласы карты — в отдельной папке WorldMap.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(battleFolder), "Боевая папка атласов не создаётся.");
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            Assert.IsTrue(importer.mipmapEnabled, "Фигура на карте мелкая: мипмапы включены.");
            Assert.IsTrue(importer.alphaIsTransparency);

            if (battleAsset != null)
            {
                Assert.AreEqual(battleBefore, JsonUtility.ToJson(battleAsset), "Боевая база не изменилась.");
                Assert.AreEqual(battleDirtyBefore, EditorUtility.IsDirty(battleAsset), "Боевая база не помечена изменённой.");
            }

            // Боевая «очистка атласов» не считает атласы карты своими, а карта — использованные.
            CreatureAnimationDatabaseAsset emptyBattle = ScriptableObject.CreateInstance<CreatureAnimationDatabaseAsset>();
            try
            {
                emptyBattle.MigrateIfNeeded();
                Assert.IsFalse(CreatureAnimationImporter.FindUnusedAtlases(emptyBattle)
                    .Any(path => path.StartsWith(WorldMapAnimationEditorData.ArtRoot + "/", StringComparison.Ordinal)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(emptyBattle);
            }
            Assert.IsFalse(WorldMapAnimationEditorData.FindUnusedAtlases(database).Contains(atlasPath));
            database.RemoveSet(SetId);
            Assert.IsTrue(WorldMapAnimationEditorData.FindUnusedAtlases(database).Contains(atlasPath), "Набор удалён — его атласы неиспользуемые.");
        }

        [Test]
        public void Import_ReplacingEverything_AllowsNewCanvas_ButPartialReplaceDoesNot()
        {
            WriteWalkPackage(sourceRoot + "/a", 32, 2, CreatureAnimationLabels.Directions);
            WorldMapAnimationSetData set = database.AddSet(SetId, "Тест");
            Assert.IsTrue(ImportWalk(set, sourceRoot + "/a"));
            Assert.AreEqual(new Vector2Int(32, 32), set.Animation.CanvasSize);

            WriteWalkPackage(sourceRoot + "/b", 64, 2, new[] { CreatureAnimationDirection.Front });
            CreatureAnimationImportPackage partial = Analyze(sourceRoot + "/b");
            Assert.IsFalse(CreatureAnimationImporter.ApplyToWorldMap(database, set, partial, CreatureAnimationImportMode.Replace, out _));
            Assert.IsTrue(partial.Issues.Any(issue => issue.Message.Contains("холстом")), "Одна ячейка другого размера не меняет холст набора.");
            Assert.AreEqual(new Vector2Int(32, 32), set.Animation.CanvasSize);

            WriteWalkPackage(sourceRoot + "/c", 64, 3, CreatureAnimationLabels.Directions);
            Assert.IsTrue(ImportWalk(set, sourceRoot + "/c"), "Замена всего содержимого задаёт новый холст.");
            Assert.AreEqual(new Vector2Int(64, 64), set.Animation.CanvasSize);
            Assert.AreEqual(3, set.Animation.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.Back).FrameCount);
        }

        [Test]
        public void Import_AppendMode_AddsFramesToCell()
        {
            WriteWalkPackage(sourceRoot + "/a", 32, 2, new[] { CreatureAnimationDirection.Front });
            WorldMapAnimationSetData set = database.AddSet(SetId, "Тест");
            Assert.IsTrue(ImportWalk(set, sourceRoot + "/a"));
            WriteWalkPackage(sourceRoot + "/b", 32, 3, new[] { CreatureAnimationDirection.Front });
            CreatureAnimationImportPackage again = Analyze(sourceRoot + "/b");
            Assert.IsTrue(CreatureAnimationImporter.ApplyToWorldMap(database, set, again, CreatureAnimationImportMode.Append, out _),
                string.Join("\n", again.Issues));
            Assert.AreEqual(5, set.Animation.FindFrames(CreatureAnimationAction.Walk, CreatureAnimationDirection.Front).FrameCount);
        }

        // ------------------------------------------------------------------
        // Пакетный импорт
        // ------------------------------------------------------------------

        [Test]
        public void Batch_ImportsHeroPackage_WithNumbers_AndAssignsHero()
        {
            // Структура пакета героя: <пакет>/Hero/Walk/<ракурс>/Walk_<ракурс>_0001.png.
            string packageRoot = sourceRoot + "/Pack";
            WriteWalkPackage(packageRoot + "/Hero", 32, 8, CreatureAnimationLabels.Directions);
            WorldMapAnimationSetData hero = database.AddSet(SetId, "Старое имя");

            WorldMapAnimationBatch.Options options = WorldMapAnimationBatch.ParseArguments(new[]
            {
                "Unity.exe", "-wmaFolder", packageRoot, "-wmaSet", SetId, "-wmaName", "Новый герой",
                "-wmaFps", "12", "-wmaPivotX", "0.5", "-wmaPivotY", "0.12109375", "-wmaHero", "1"
            }, out string error);
            Assert.IsNull(error);

            bool imported = WorldMapAnimationBatch.Import(database, options, out string report);

            Assert.IsTrue(imported, report);
            Assert.AreSame(hero, database.FindSet(SetId));
            Assert.AreEqual("Новый герой", hero.DisplayName);
            Assert.IsTrue(hero.HasCompleteWalk, "Walk во всех шести ракурсах.");
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                Assert.AreEqual(8, hero.Animation.FindFrames(CreatureAnimationAction.Walk, direction).FrameCount);
            CreatureAnimationClipData walk = hero.Animation.FindClip(CreatureAnimationAction.Walk);
            Assert.AreEqual(12f, walk.FramesPerSecond, 0.0001f);
            Assert.AreEqual(CreatureAnimationPlayback.Loop, walk.Playback);
            Assert.AreEqual(0, walk.SkipLastFrames);
            Assert.AreEqual(0.5f, hero.Animation.Pivot.x, 0.00001f);
            Assert.AreEqual(0.12109375f, hero.Animation.Pivot.y, 0.00001f);
            Assert.AreEqual(SetId, database.HeroSetId);
            Assert.AreSame(hero, database.HeroSet);
        }

        [Test]
        public void Batch_Fails_OnMissingFolderOrWalk()
        {
            WorldMapAnimationBatch.ParseArguments(new[] { "Unity.exe" }, out string error);
            Assert.IsNotNull(error, "Без -wmaFolder — ошибка аргументов.");

            WorldMapAnimationBatch.Options missing = WorldMapAnimationBatch.ParseArguments(
                new[] { "Unity.exe", "-wmaFolder", sourceRoot + "/нет" }, out error);
            Assert.IsNull(error);
            Assert.IsFalse(WorldMapAnimationBatch.Import(database, missing, out string report));
            StringAssert.Contains("не найдена", report);
            Assert.IsNull(database.FindSet(missing.SetId), "При ошибке набор не создаётся.");

            WorldMapAnimationBatch.ParseArguments(new[] { "Unity.exe", "-wmaFolder", "x", "-wmaFps", "быстро" }, out error);
            StringAssert.Contains("-wmaFps", error);
        }

        // ------------------------------------------------------------------
        // Помощники
        // ------------------------------------------------------------------

        private static CreatureAnimationImportPackage Analyze(string root)
        {
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(
                root, Directory.GetFiles(root, "*.*", SearchOption.AllDirectories));
            WorldMapAnimationSection.PreparePackage(package);
            return package;
        }

        private bool ImportWalk(WorldMapAnimationSetData set, string root)
        {
            CreatureAnimationImportPackage package = Analyze(root);
            bool applied = CreatureAnimationImporter.ApplyToWorldMap(database, set, package, CreatureAnimationImportMode.Replace, out _);
            if (!applied)
                Assert.Fail(string.Join("\n", package.Issues));
            return applied;
        }

        // <root>/Walk/<ракурс>/Walk_<ракурс>_0001.png — квадратные кадры size×size.
        private static void WriteWalkPackage(string root, int size, int frames, System.Collections.Generic.IEnumerable<CreatureAnimationDirection> directions)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(200, 120, 60, (byte)(i % 3 == 0 ? 0 : 255));
                texture.SetPixels32(pixels);
                texture.Apply(false);
                byte[] png = texture.EncodeToPNG();
                foreach (CreatureAnimationDirection direction in directions)
                {
                    string folderName = CreatureAnimationLabels.DirectionFolder(direction);
                    string folder = root + "/Walk/" + folderName;
                    Directory.CreateDirectory(folder);
                    for (int i = 1; i <= frames; i++)
                        File.WriteAllBytes(folder + "/Walk_" + folderName + "_" + i.ToString("0000") + ".png", png);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
