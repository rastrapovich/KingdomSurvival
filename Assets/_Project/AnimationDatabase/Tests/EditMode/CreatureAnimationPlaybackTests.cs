using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    public sealed class CreatureAnimationPlaybackTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }
            created.Clear();
        }

        private Sprite MakeSprite(string name)
        {
            Texture2D texture = new Texture2D(8, 8);
            created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0f));
            sprite.name = name;
            created.Add(sprite);
            return sprite;
        }

        private List<Sprite> MakeFrames(string prefix, int count)
        {
            return Enumerable.Range(0, count).Select(i => MakeSprite(prefix + i)).ToList();
        }

        private CreatureAnimationSetData MakeSet(params (CreatureAnimationAction action, CreatureAnimationDirection direction, int frames)[] cells)
        {
            CreatureAnimationSetData set = new CreatureAnimationSetData("test", "Тест");
            foreach ((CreatureAnimationAction action, CreatureAnimationDirection direction, int frames) in cells)
            {
                set.GetOrAddClip(action).GetOrAddDirection(direction)
                    .SetFrames(MakeFrames(action + "_" + direction + "_", frames), Enumerable.Range(1, frames));
            }
            return set;
        }

        [Test]
        public void Timing_DefaultFps_EqualFrames()
        {
            float[] durations = CreatureAnimationTiming.BuildFrameDurations(6, 12f, false, null, 24f);
            Assert.AreEqual(6, durations.Length);
            Assert.AreEqual(0.5f, CreatureAnimationTiming.Sum(durations), 0.0001f);
            Assert.AreEqual(0, CreatureAnimationTiming.GetFrameIndex(durations, 0f, CreatureAnimationPlayback.Loop));
            Assert.AreEqual(1, CreatureAnimationTiming.GetFrameIndex(durations, 0.09f, CreatureAnimationPlayback.Loop));
            Assert.AreEqual(0, CreatureAnimationTiming.GetFrameIndex(durations, 0.51f, CreatureAnimationPlayback.Loop), "Цикл начинается заново.");
            Assert.AreEqual(5, CreatureAnimationTiming.GetFrameIndex(durations, 3f, CreatureAnimationPlayback.Once), "Однократный клип стоит на последнем кадре.");
        }

        [Test]
        public void Timing_SourceMode_UsesNumberDifferences_AndKeepsLastFrame()
        {
            List<int> numbers = CreatureAnimationTestFrames.ExportNumbers(1, 20, 3); // 1,4,…,19,20
            float[] durations = CreatureAnimationTiming.BuildFrameDurations(numbers.Count, 12f, true, numbers, 24f);
            Assert.AreEqual(3f / 24f, durations[0], 0.0001f, "0001 → 0004 — три исходных кадра.");
            Assert.AreEqual(1f / 24f, durations[6], 0.0001f, "19 → 20 — короче обычного шага.");
            Assert.AreEqual(3f / 24f, durations[7], 0.0001f, "Последний кадр держится обычный шаг, а не пропадает.");
            Assert.AreEqual(3, CreatureAnimationTiming.MostCommonInterval(numbers));
        }

        [Test]
        public void Timing_SourceMode_WithoutNumbers_FallsBackToFps()
        {
            float[] durations = CreatureAnimationTiming.BuildFrameDurations(3, 10f, true, new[] { 5, 5, 6 }, 24f);
            Assert.AreEqual(0.1f, durations[0], 0.0001f);
        }

        [Test]
        public void Timing_OneFrameClip_IsValid()
        {
            float[] durations = CreatureAnimationTiming.BuildFrameDurations(1, 12f, false, null, 24f);
            Assert.AreEqual(0, CreatureAnimationTiming.GetFrameIndex(durations, 10f, CreatureAnimationPlayback.Loop));
        }

        [Test]
        public void Resolver_ExactDirection_ThenNearest()
        {
            CreatureAnimationSetData set = MakeSet(
                (CreatureAnimationAction.Attack, CreatureAnimationDirection.Front, 4),
                (CreatureAnimationAction.Attack, CreatureAnimationDirection.Back, 4));

            CreatureAnimationClip exact = CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Attack, CreatureAnimationDirection.Back);
            Assert.AreEqual(CreatureAnimationDirection.Back, exact.Direction);
            Assert.IsFalse(exact.IsDirectionSubstitute);

            CreatureAnimationClip near = CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Attack, CreatureAnimationDirection.FrontRight);
            Assert.AreEqual(CreatureAnimationDirection.Front, near.Direction, "Ближайший по кругу ракурс.");
            Assert.IsTrue(near.IsDirectionSubstitute);
        }

        [Test]
        public void Resolver_Fallbacks_AreGeneralAndPredictable()
        {
            CreatureAnimationSetData set = MakeSet(
                (CreatureAnimationAction.Idle, CreatureAnimationDirection.Front, 2),
                (CreatureAnimationAction.Attack, CreatureAnimationDirection.Front, 3));

            Assert.AreEqual(CreatureAnimationAction.Idle, CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Walk, CreatureAnimationDirection.Front).Action);
            Assert.AreEqual(CreatureAnimationAction.Idle, CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Hit, CreatureAnimationDirection.Front).Action);
            Assert.AreEqual(CreatureAnimationAction.Idle, CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Block, CreatureAnimationDirection.Front).Action);
            Assert.AreEqual(CreatureAnimationAction.Attack, CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Shoot, CreatureAnimationDirection.Front).Action,
                "Нет выстрела — временно атака.");
            Assert.IsNull(CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Death, CreatureAnimationDirection.Front),
                "Смерть не подменяется живым циклом.");
            Assert.IsNull(CreatureAnimationResolver.Resolve(null, CreatureAnimationAction.Idle, CreatureAnimationDirection.Front));
        }

        [Test]
        public void Resolver_MissingSprites_AreSkipped()
        {
            CreatureAnimationSetData set = new CreatureAnimationSetData("broken", "Битый");
            set.GetOrAddClip(CreatureAnimationAction.Idle).GetOrAddDirection(CreatureAnimationDirection.Front)
                .SetFrames(new Sprite[] { null, MakeSprite("a"), null }, new[] { 1, 2, 3 });
            CreatureAnimationClip clip = CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Idle, CreatureAnimationDirection.Front);
            Assert.AreEqual(1, clip.FrameCount);

            CreatureAnimationSetData empty = new CreatureAnimationSetData("empty", "Пустой");
            empty.GetOrAddClip(CreatureAnimationAction.Idle).GetOrAddDirection(CreatureAnimationDirection.Front)
                .SetFrames(new Sprite[] { null }, null);
            Assert.IsNull(CreatureAnimationResolver.Resolve(empty, CreatureAnimationAction.Idle, CreatureAnimationDirection.Front));
        }

        [Test]
        public void Player_OnceReturnsToIdle_DeathHoldsLastFrame_AndHasPriority()
        {
            CreatureAnimationSetData set = MakeSet(
                (CreatureAnimationAction.Idle, CreatureAnimationDirection.Front, 2),
                (CreatureAnimationAction.Hit, CreatureAnimationDirection.Front, 3),
                (CreatureAnimationAction.Death, CreatureAnimationDirection.Front, 4));
            CreatureAnimationPlayer player = new CreatureAnimationPlayer(set, CreatureAnimationDirection.Front);

            player.Play(CreatureAnimationAction.Hit, 0f);
            Assert.AreEqual("Hit_Front_0", player.Evaluate(0f).name);
            player.Evaluate(5f);
            Assert.AreEqual(CreatureAnimationAction.Idle, player.Action, "После удара — снова ожидание.");

            player.Play(CreatureAnimationAction.Death, 10f);
            Assert.AreEqual("Death_Front_3", player.Evaluate(100f).name, "Смерть держит последний кадр.");
            Assert.IsFalse(player.Play(CreatureAnimationAction.Idle, 101f), "Павший не возвращается к ожиданию.");
            Assert.AreEqual(CreatureAnimationAction.Death, player.Action);
        }

        [Test]
        public void Players_OfSameSet_AreIndependent()
        {
            CreatureAnimationSetData set = MakeSet(
                (CreatureAnimationAction.Idle, CreatureAnimationDirection.Front, 2),
                (CreatureAnimationAction.Hit, CreatureAnimationDirection.Front, 3));
            CreatureAnimationPlayer first = new CreatureAnimationPlayer(set, CreatureAnimationDirection.Front);
            CreatureAnimationPlayer second = new CreatureAnimationPlayer(set, CreatureAnimationDirection.Front);

            first.Play(CreatureAnimationAction.Hit, 0f);
            Assert.AreEqual(CreatureAnimationAction.Hit, first.Action);
            Assert.AreEqual(CreatureAnimationAction.Idle, second.Action, "Удар по одному волку не переключает других.");
            Assert.AreSame(first.Clip.Frames[0], CreatureAnimationResolver.Resolve(set, CreatureAnimationAction.Hit, CreatureAnimationDirection.Front).Frames[0],
                "Кадры общие, часы свои.");
        }

        [Test]
        public void Player_DirectionChange_DoesNotRestartWalk()
        {
            CreatureAnimationSetData set = MakeSet(
                (CreatureAnimationAction.Walk, CreatureAnimationDirection.Front, 4),
                (CreatureAnimationAction.Walk, CreatureAnimationDirection.Back, 4));
            CreatureAnimationPlayer player = new CreatureAnimationPlayer(set, CreatureAnimationDirection.Front);
            player.Play(CreatureAnimationAction.Walk, 0f);
            float started = player.StartedAt;
            player.SetDirection(CreatureAnimationDirection.Back);
            Assert.AreEqual(started, player.StartedAt);
            Assert.AreEqual("Walk_Back_2", player.Evaluate(2.5f / 12f).name);
        }

        [Test]
        public void Status_ReadyPartialEmpty()
        {
            CreatureAnimationSetData set = new CreatureAnimationSetData("s", "S");
            Assert.AreEqual(CreatureAnimationSetStatus.Empty, set.Status);
            foreach (CreatureAnimationAction action in new[] { CreatureAnimationAction.Idle, CreatureAnimationAction.Walk, CreatureAnimationAction.Death, CreatureAnimationAction.Attack })
            {
                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                    set.GetOrAddClip(action).GetOrAddDirection(direction).SetFrames(new[] { MakeSprite("x") }, null);
            }
            Assert.AreEqual(CreatureAnimationSetStatus.Ready, set.Status);
            set.GetOrAddClip(CreatureAnimationAction.Walk).RemoveDirection(CreatureAnimationDirection.Back);
            Assert.AreEqual(CreatureAnimationSetStatus.Partial, set.Status);
        }

        [Test]
        public void Database_MigratesAndKeepsDirectionMapBijective()
        {
            CreatureAnimationDatabaseAsset database = ScriptableObject.CreateInstance<CreatureAnimationDatabaseAsset>();
            created.Add(database);
            Assert.IsTrue(database.MigrateIfNeeded());
            Assert.IsFalse(database.MigrateIfNeeded(), "Миграция идемпотентна.");
            Assert.IsTrue(database.IsDirectionMapValid());

            database.AssignFacing(CreatureAnimationDirection.Front, HexFacing.East);
            Assert.IsTrue(database.IsDirectionMapValid(), "Обмен направлениями, а не дубль.");
            Assert.AreEqual(HexFacing.East, database.GetFacing(CreatureAnimationDirection.Front));
            Assert.AreEqual(CreatureAnimationDirection.Front, database.GetDirection(HexFacing.East));
            foreach (HexFacing facing in System.Enum.GetValues(typeof(HexFacing)))
                Assert.AreEqual(facing, database.GetFacing(database.GetDirection(facing)));
        }

        [Test]
        public void Database_SetsHaveStableUniqueIds()
        {
            CreatureAnimationDatabaseAsset database = ScriptableObject.CreateInstance<CreatureAnimationDatabaseAsset>();
            created.Add(database);
            database.MigrateIfNeeded();
            database.AddSet("wolf", "Волк");
            Assert.AreEqual("wolf_2", database.MakeUniqueSetId("wolf"));
            Assert.Throws<System.InvalidOperationException>(() => database.AddSet("wolf", "Другой"));
            database.FindSet("wolf").SetDisplayName("Серый");
            Assert.IsNotNull(database.FindSet("wolf"), "Переименование не ломает связь по ID.");
        }

        [Test]
        public void FacingMath_NeighborsAreExact_FarTargetsTakeNearestSector()
        {
            Vector2[] neighbors = System.Enum.GetValues(typeof(HexFacing)).Cast<HexFacing>()
                .Select(CreatureAnimationPreviewElement.FacingScreenVector)
                .ToArray();
            for (int i = 0; i < neighbors.Length; i++)
                Assert.AreEqual((HexFacing)i, HexFacingMath.Nearest(neighbors[i] * 3f, neighbors, HexFacing.East));
            Assert.AreEqual(HexFacing.East, HexFacingMath.Nearest(new Vector2(10f, 0.5f), neighbors, HexFacing.West));
            Assert.AreEqual(HexFacing.NorthWest, HexFacingMath.Nearest(new Vector2(-1f, -3f), neighbors, HexFacing.East));
            Assert.AreEqual(HexFacing.West, HexFacingMath.Nearest(Vector2.zero, neighbors, HexFacing.West), "Нулевой вектор — прежнее направление.");
        }

        [Test]
        public void AtlasLayout_UniformCells_SplitsPages()
        {
            List<CreatureAnimationAtlasPage> pages = CreatureAnimationAtlasBuilder.ComputeLayout(8, new Vector2Int(96, 128));
            Assert.AreEqual(1, pages.Count);
            Assert.AreEqual(8, pages[0].Cells.Count);
            Assert.IsTrue(pages[0].Cells.All(cell => cell.width == 96 && cell.height == 128), "Ячейка ровно с холст: опора не меняется.");
            Assert.AreEqual(0, pages[0].Width % 4);
            Assert.AreEqual(0, pages[0].Height % 4);
            Assert.IsTrue(pages[0].Cells.All(cell => cell.xMin >= 0 && cell.yMin >= 0 && cell.xMax <= pages[0].Width && cell.yMax <= pages[0].Height));

            List<CreatureAnimationAtlasPage> big = CreatureAnimationAtlasBuilder.ComputeLayout(40, new Vector2Int(1000, 1000), 2048);
            Assert.AreEqual(10, big.Count, "По 4 кадра 1000×1000 на страницу 2048.");
        }
    }
}
