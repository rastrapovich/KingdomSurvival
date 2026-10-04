using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-12К (канон v1.53 §28.3): командир идёт по клику, спутники — по его
    // следу; никто не проходит сквозь стену, не встаёт в чужую клетку и не
    // теряется в узком проходе; новый приказ заменяет старый.
    public sealed class LocalPartyMoverTests
    {
        private static readonly HashSet<HexCoord> Walls = new HashSet<HexCoord>
        {
            new HexCoord(2, 0), new HexCoord(3, 0), new HexCoord(4, 0), new HexCoord(5, 0), new HexCoord(6, 0), new HexCoord(7, 0), new HexCoord(8, 0),
            new HexCoord(5, 1), new HexCoord(5, 2), new HexCoord(5, 5),
            new HexCoord(2, 6), new HexCoord(3, 6), new HexCoord(4, 6), new HexCoord(5, 6), new HexCoord(6, 6), new HexCoord(7, 6), new HexCoord(8, 6)
        };

        private static bool Passable(HexCoord cell) => SandboxArenaShape.Contains(cell) && !Walls.Contains(cell);

        private static LocalPartyMover NewParty(int size)
        {
            HexCoord[] start = { new HexCoord(1, 3), new HexCoord(1, 2), new HexCoord(1, 4), new HexCoord(2, 3), new HexCoord(2, 2) };
            List<KeyValuePair<string, HexCoord>> members = new List<KeyValuePair<string, HexCoord>>();
            for (int i = 0; i < size; i++)
                members.Add(new KeyValuePair<string, HexCoord>(i == 0 ? "hero" : "f" + i, start[i]));
            return new LocalPartyMover(Passable, _ => 1, members);
        }

        private static void Run(LocalPartyMover mover, float seconds, System.Action<LocalPartyMover> eachTick = null)
        {
            for (float t = 0f; t < seconds; t += 0.05f)
            {
                mover.Tick(0.05f);
                eachTick?.Invoke(mover);
            }
        }

        private static void AssertNoOverlapAtRest(LocalPartyMover mover)
        {
            List<HexCoord> cells = mover.Members.Select(member => member.Cell).ToList();
            Assert.AreEqual(cells.Count, cells.Distinct().Count(), "Два участника в одной клетке.");
        }

        [Test]
        public void FullParty_GoesThroughNarrowPassage_AndGathersBehindTheLeader()
        {
            LocalPartyMover mover = NewParty(5);
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(8, 3)));

            Run(mover, 20f, m => Assert.IsTrue(m.Members.All(member => Passable(member.Cell)), "Никто не стоит в стене."));

            Assert.AreEqual(new HexCoord(8, 3), mover.Leader.Cell);
            Assert.IsTrue(mover.IsIdle);
            Assert.IsTrue(mover.IsGathered, "Спутники дошли следом.");
            AssertNoOverlapAtRest(mover);
            Assert.IsTrue(mover.Members.All(member => member.Cell.DistanceTo(mover.Leader.Cell) <= 4),
                "Никто не потерялся по ту сторону прохода: цепочка рядом с командиром.");
        }

        [Test]
        public void Unreachable_Target_IsRefused_WithoutTeleport()
        {
            LocalPartyMover mover = NewParty(3);
            Assert.IsFalse(mover.MoveLeaderTo(new HexCoord(5, 2)), "В стену не пройти.");
            Run(mover, 1f);
            Assert.AreEqual(new HexCoord(1, 3), mover.Leader.Cell);
        }

        [Test]
        public void NewOrder_ReplacesOldOne_FromCurrentPosition()
        {
            LocalPartyMover mover = NewParty(3);
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(8, 3)));
            Run(mover, 1.0f);
            HexCoord midway = mover.Leader.SettledCell;
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(1, 1)));
            Run(mover, 15f);

            Assert.AreEqual(new HexCoord(1, 1), mover.Leader.Cell);
            Assert.AreNotEqual(new HexCoord(8, 3), midway, "Первый приказ не был доведён до конца.");
            AssertNoOverlapAtRest(mover);
        }

        [Test]
        public void LeaderTurningBack_IntoFollowers_IsNotBlockedForever()
        {
            LocalPartyMover mover = NewParty(5);
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(6, 3)));
            Run(mover, 10f);
            // Разворот назад сквозь цепочку в проходе.
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(1, 3)));
            Run(mover, 20f);

            Assert.AreEqual(new HexCoord(1, 3), mover.Leader.Cell);
            Assert.IsTrue(mover.IsIdle);
            AssertNoOverlapAtRest(mover);
        }

        [Test]
        public void LeaderEnteredCells_AreReportedOncePerStep()
        {
            LocalPartyMover mover = NewParty(1);
            Assert.IsTrue(mover.MoveLeaderTo(new HexCoord(4, 3)));
            List<HexCoord> entered = new List<HexCoord>();
            for (int i = 0; i < 200; i++)
                entered.AddRange(mover.Tick(0.05f));

            CollectionAssert.AreEqual(new[] { new HexCoord(2, 3), new HexCoord(3, 3), new HexCoord(4, 3) }, entered);
        }
    }
}
