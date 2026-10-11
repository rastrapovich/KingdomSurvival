using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    // ПР-12К: движение в исследуемом месте — строго по гексам боя места, как
    // на поле боя, с общей скоростью ходьбы. Командир идёт из центра клетки в
    // центр соседней и обходит стену, клик в стену ведёт к ближайшей
    // доступной клетке, новый приказ на ходу не срезает шаг; спутники идут
    // змейкой по покинутым клеткам и не стоят друг в друге.
    public sealed class LocalHexMoverTests
    {
        private const float Speed = 3f;

        private static LocalLocationDefinition Location() => new LocalLocationDefinition
        {
            Id = "hex_move_test", DisplayName = "Ходьба", CanvasWidth = 1920, CanvasHeight = 1080, HexesAcross = 40, BattleFrameWidth = 1920,
            Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(200, 400) } }
        };

        // Стена поперёк рисунка с проходом внизу.
        private static LocalLocationGeometry WallWithGap() =>
            new LocalLocationGeometry(Location(), null, new[] { new Rect(880, 0, 160, 800) });

        private static LocalHexMover Party(LocalLocationGeometry geometry, int count)
        {
            List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
            for (int i = 0; i < count; i++)
                members.Add(new KeyValuePair<string, LocalPointData>("m" + i, new LocalPointData(300 - i * 70, 400)));
            return new LocalHexMover(geometry, members, Speed);
        }

        private static double Run(LocalHexMover mover, Action<LocalHexMover> eachStep = null, int steps = 4000)
        {
            double seconds = 0;
            for (int i = 0; i < steps && !mover.IsIdle; i++)
            {
                mover.Tick(1 / 60.0, out _);
                seconds += 1 / 60.0;
                eachStep?.Invoke(mover);
            }
            return seconds;
        }

        // Точка лежит на отрезке между центром клетки и центром её соседа.
        private static void AssertOnHexStep(LocalHexMover mover, LocalHexMover.Member member)
        {
            Vector2 point = new Vector2((float)member.X, (float)member.Y);
            Vector2 from = mover.CellCenter(member.Cell);
            float best = Vector2.Distance(point, from);
            foreach (HexCoord neighbor in member.Cell.Neighbors())
            {
                Vector2 to = mover.CellCenter(neighbor);
                Vector2 line = to - from;
                float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / line.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(point, from + line * t));
            }
            Assert.That(best, Is.LessThan(.05f), member.Id + " сошёл с линии между центрами клеток: " + point);
        }

        [Test]
        public void Leader_WalksCellToCell_AroundTheWall_ToTheCellCenter()
        {
            LocalLocationGeometry geometry = WallWithGap();
            LocalHexMover mover = Party(geometry, 1);
            Vector2 target = new Vector2(1500, 400);
            Assert.IsTrue(mover.MoveLeaderTo(target.x, target.y));
            Assert.IsTrue(mover.LastOrderReachesTarget);
            HexCoord previous = mover.Leader.Cell;
            bool throughGap = false;
            Run(mover, m =>
            {
                LocalHexMover.Member leader = m.Leader;
                Assert.IsTrue(m.IsPassable(leader.Cell), "Командир не заходит в стену.");
                AssertOnHexStep(m, leader);
                if (leader.Cell != previous)
                {
                    Assert.AreEqual(1, previous.DistanceTo(leader.Cell), "Шаг — в соседнюю клетку.");
                    previous = leader.Cell;
                }
                if (leader.X > 880 && leader.X < 1040)
                    throughGap |= leader.Y > 800;
            });
            Assert.IsTrue(throughGap, "Стену обходят через проход.");
            Assert.AreEqual(mover.CellAt(target.x, target.y), mover.Leader.Cell);
            Vector2 center = mover.CellCenter(mover.Leader.Cell);
            Assert.AreEqual(center.x, mover.Leader.X, 1e-3);
            Assert.AreEqual(center.y, mover.Leader.Y, 1e-3);
            Assert.IsFalse(mover.Leader.Walking);
        }

        // Скорость — гексов в секунду, как у бойца на поле.
        [Test]
        public void Speed_IsHexesPerSecond_LikeOnTheBattlefield()
        {
            LocalLocationGeometry geometry = new LocalLocationGeometry(Location(), null);
            LocalHexMover mover = Party(geometry, 1);
            HexCoord start = mover.Leader.Cell;
            HexCoord goal = new HexCoord(start.Q + 6, start.R);
            Vector2 point = mover.CellCenter(goal);
            Assert.IsTrue(mover.MoveLeaderTo(point.x, point.y));
            double seconds = Run(mover);
            Assert.AreEqual(goal, mover.Leader.Cell);
            Assert.AreEqual(6 / Speed, seconds, 1.5 / 60, "Шесть клеток при трёх гексах в секунду — две секунды.");
        }

        // Общая скорость — из Базы полей боя (одна на бой и место).
        [Test]
        public void DefaultSpeed_IsTheSharedWalkSpeed()
        {
            LocalHexMover mover = new LocalHexMover(new LocalLocationGeometry(Location(), null),
                new List<KeyValuePair<string, LocalPointData>> { new KeyValuePair<string, LocalPointData>("hero", new LocalPointData(300, 400)) });
            Assert.AreEqual(BattlefieldDatabaseAsset.SharedWalkHexesPerSecond, mover.HexesPerSecond, 1e-5);
        }

        // Новый приказ на ходу: начатый шаг доводится до центра клетки.
        [Test]
        public void NewOrderMidStep_FinishesTheStepFirst()
        {
            LocalHexMover mover = new LocalHexMover(new LocalLocationGeometry(Location(), null),
                new List<KeyValuePair<string, LocalPointData>> { new KeyValuePair<string, LocalPointData>("hero", new LocalPointData(960, 540)) }, Speed);
            HexCoord start = mover.Leader.Cell;
            Vector2 east = mover.CellCenter(new HexCoord(start.Q + 5, start.R));
            Assert.IsTrue(mover.MoveLeaderTo(east.x, east.y));
            mover.Tick(.5 / Speed, out _);
            Vector2 west = mover.CellCenter(new HexCoord(start.Q - 4, start.R));
            Assert.IsTrue(mover.MoveLeaderTo(west.x, west.y));
            HexCoord previous = mover.Leader.Cell;
            Run(mover, m =>
            {
                AssertOnHexStep(m, m.Leader);
                if (m.Leader.Cell != previous)
                {
                    Assert.AreEqual(1, previous.DistanceTo(m.Leader.Cell));
                    previous = m.Leader.Cell;
                }
            });
            Assert.AreEqual(mover.CellAt(west.x, west.y), mover.Leader.Cell);
        }

        [Test]
        public void ClickIntoTheWall_GoesToTheNearestReachableCell()
        {
            LocalLocationGeometry geometry = WallWithGap();
            LocalHexMover mover = Party(geometry, 1);
            Assert.IsFalse(mover.IsPassable(960, 300));
            Assert.IsTrue(mover.MoveLeaderTo(960, 300));
            Assert.IsFalse(mover.LastOrderReachesTarget);
            Run(mover);
            Assert.IsTrue(mover.IsPassable(mover.Leader.Cell));
            Assert.That(Math.Abs(mover.Leader.X - 960), Is.LessThan(mover.CellRadius * 3), "Дошёл до стены, а не остался на месте.");
        }

        [Test]
        public void Followers_WalkAsASnake_InSeparateCells()
        {
            LocalLocationGeometry geometry = WallWithGap();
            LocalHexMover mover = Party(geometry, 4);
            Assert.AreEqual(4, mover.Members.Select(m => m.Cell).Distinct().Count(), "Каждый — в своей клетке.");
            Assert.IsTrue(mover.IsGathered);
            Assert.IsTrue(mover.MoveLeaderTo(1500, 900));
            Run(mover, m =>
            {
                foreach (LocalHexMover.Member member in m.Members)
                {
                    Assert.IsTrue(m.IsPassable(member.Cell), member.Id + " не заходит в стену.");
                    AssertOnHexStep(m, member);
                }
            }, 8000);
            Assert.IsTrue(mover.IsIdle);
            Assert.IsTrue(mover.IsGathered);
            List<HexCoord> cells = mover.Members.Select(m => m.Cell).ToList();
            Assert.AreEqual(cells.Count, cells.Distinct().Count(), "Никто не стоит в другом.");
            for (int i = 1; i < cells.Count; i++)
                Assert.AreEqual(1, cells[i].DistanceTo(cells[i - 1]), "Змейка: каждый — рядом с тем, кто впереди.");
        }
    }
}
