using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // База полей боя: гексы, отключённые на конкретном поле, выпадают из боя.
    public sealed class SandboxDisabledCellsTests
    {
        // Заняты стартовые места обеих сторон и полоса «горы» в центре.
        private static readonly HexCoord[] Disabled =
        {
            new HexCoord(2, 0), new HexCoord(1, 1), new HexCoord(8, 1), new HexCoord(9, 2),
            new HexCoord(4, 2), new HexCoord(5, 2), new HexCoord(5, 3)
        };

        private static SandboxUnitDefinition Sheshka()
        {
            return new SandboxUnitDefinition("sheshka", "Шешка", SandboxUnitRole.Beast, 8, 2, 0, 2, 5, 8, 1, new string[0]);
        }

        [Test]
        public void DisabledCells_AreOutsideBattle_AndSpawnsMoveAway()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                SandboxBattle battle = SandboxRoster.CreateBattle(
                    SandboxRoster.PlayerRoster.Take(4).ToList(),
                    Enumerable.Repeat(Sheshka(), 6),
                    seed,
                    disabledCells: Disabled);

                foreach (HexCoord cell in Disabled)
                {
                    Assert.IsFalse(battle.IsInside(cell), "seed " + seed + " " + cell);
                    Assert.AreEqual(SandboxTerrain.Impassable, battle.GetTerrain(cell), "seed " + seed);
                }

                Assert.IsTrue(battle.Units.All(unit => !Disabled.Contains(unit.Position)), "seed " + seed);
                Assert.AreEqual(battle.Units.Count, battle.Units.Select(unit => unit.Position).Distinct().Count());
                Assert.IsTrue(battle.Units.All(unit => battle.IsInside(unit.Position)));

                SandboxUnitState player = battle.Units.First(unit => unit.Team == SandboxTeam.Player);
                IReadOnlyDictionary<HexCoord, int> reachable = battle.GetReachable(player.Id);
                Assert.IsTrue(reachable.Keys.All(cell => !Disabled.Contains(cell)), "seed " + seed);
            }
        }

        [Test]
        public void NoDisabledCells_KeepsStandardSpawns()
        {
            SandboxBattle plain = SandboxRoster.CreateBattle(
                SandboxRoster.PlayerRoster.Take(2).ToList(), new[] { Sheshka() }, 5);
            SandboxBattle empty = SandboxRoster.CreateBattle(
                SandboxRoster.PlayerRoster.Take(2).ToList(), new[] { Sheshka() }, 5, disabledCells: new HexCoord[0]);

            CollectionAssert.AreEqual(
                plain.Units.Select(unit => unit.Position).ToList(),
                empty.Units.Select(unit => unit.Position).ToList());
            Assert.AreEqual(new HexCoord(2, 0), plain.Units[0].Position);
        }

        [Test]
        public void TooManyDisabledCells_AreIgnored()
        {
            List<HexCoord> almostAll = SandboxArenaShape.Cells().Skip(5).ToList();
            SandboxBattle battle = SandboxRoster.CreateBattle(
                SandboxRoster.PlayerRoster.Take(2).ToList(), new[] { Sheshka() }, 3, disabledCells: almostAll);

            Assert.IsTrue(SandboxArenaShape.Cells().All(battle.IsInside));
        }
    }
}
