using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-12К (канон v1.53 §28.3): бой из явной геометрии места — позиции,
    // стены и трудная местность сохраняются до первого хода; случайной
    // местности нет; стена не снимается ради расстановки; неверная раскладка —
    // ошибка, а не подмена.
    public sealed class SandboxBattleLayoutTests
    {
        private static SandboxUnitDefinition Militia => SandboxRoster.PlayerRoster.First(unit => unit.Id == "militia");
        private static SandboxUnitDefinition Beast => SandboxRoster.EnemyRoster.First(unit => unit.Id == "forest_beast_1");

        private static SandboxBattleLayout MineLikeLayout()
        {
            SandboxBattleLayout layout = new SandboxBattleLayout();
            // Стенка в столбце 5 с проходом (5,3)/(5,4).
            foreach (HexCoord cell in new[] { new HexCoord(5, 0), new HexCoord(5, 1), new HexCoord(5, 2), new HexCoord(5, 5), new HexCoord(5, 6) })
                layout.BlockedCells.Add(cell);
            layout.DifficultCells.Add(new HexCoord(2, 4));
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "player:hero", Definition = Militia, Team = SandboxTeam.Player, Position = new HexCoord(4, 3), StartingHitPoints = 9 });
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "player:garrik", Definition = Militia, Team = SandboxTeam.Player, Position = new HexCoord(3, 3) });
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "enemy:mine.beast.1", Definition = Beast, Team = SandboxTeam.Enemy, Position = new HexCoord(8, 2), StartingHitPoints = 4 });
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "enemy:mine.beast.2", Definition = Beast, Team = SandboxTeam.Enemy, Position = new HexCoord(8, 4) });
            layout.LeaderUnitId = "player:hero";
            return layout;
        }

        [Test]
        public void ExplicitPositions_Walls_AndHitPoints_AreKept_BeforeFirstTurn()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            layout.PlayerFirstRoundInitiativeBonus = 2;
            CollectionAssert.IsEmpty(layout.Validate());

            SandboxBattle battle = layout.CreateBattle();

            Assert.AreEqual(SandboxBattlePhase.InProgress, battle.Phase);
            Assert.AreEqual(new HexCoord(4, 3), battle.GetUnit("player:hero").Position);
            Assert.AreEqual(new HexCoord(8, 2), battle.GetUnit("enemy:mine.beast.1").Position);
            Assert.AreEqual(9, battle.GetUnit("player:hero").HitPoints, "Текущее здоровье человека — до первого хода.");
            Assert.AreEqual(4, battle.GetUnit("enemy:mine.beast.1").HitPoints, "Раненый зверь не исцеляется молча.");
            Assert.AreEqual("player:hero", battle.LeaderUnitId);
            Assert.AreEqual(2, battle.PlayerFirstRoundInitiativeBonus);

            Assert.IsFalse(battle.IsInside(new HexCoord(5, 2)), "Стена места остаётся стеной.");
            Assert.AreEqual(SandboxTerrain.Difficult, battle.GetTerrain(new HexCoord(2, 4)));
            int randomObstacles = SandboxArenaShape.Cells().Count(cell =>
                !layout.BlockedCells.Contains(cell) && battle.GetTerrain(cell) == SandboxTerrain.Impassable);
            Assert.AreEqual(0, randomObstacles, "Случайных камней на месте прохода нет.");
            int difficult = SandboxArenaShape.Cells().Count(cell => battle.GetTerrain(cell) == SandboxTerrain.Difficult);
            Assert.AreEqual(1, difficult, "Случайного болота нет — только трудная клетка места.");
        }

        [Test]
        public void TwoUnitsOnOneCell_IsAnError_NotAShift()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            layout.Units[1].Position = layout.Units[0].Position;

            Assert.IsNotEmpty(layout.Validate());
            Assert.Throws<System.InvalidOperationException>(() => layout.CreateBattle());
        }

        [Test]
        public void UnitInWall_IsAnError_AndWallIsNotRemoved()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            layout.Units[1].Position = new HexCoord(5, 2);

            List<string> errors = layout.Validate();
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(layout.BlockedCells.Contains(new HexCoord(5, 2)), "Стена не исчезает ради расстановки.");
        }

        [Test]
        public void UnitCutOffByWalls_IsAnError()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            layout.BlockedCells.Add(new HexCoord(5, 3));
            layout.BlockedCells.Add(new HexCoord(5, 4));

            Assert.IsTrue(layout.Validate().Any(error => error.Contains("отрезан")));
        }

        [Test]
        public void Path_GoesThroughPassage_NotThroughWall()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            List<HexCoord> path = SandboxLocalNavigation.FindPath(new HexCoord(3, 1), new HexCoord(7, 1), layout.IsPassable);

            Assert.IsNotEmpty(path);
            Assert.AreEqual(new HexCoord(3, 1), path[0]);
            Assert.AreEqual(new HexCoord(7, 1), path[path.Count - 1]);
            Assert.IsTrue(path.All(layout.IsPassable), "Путь не срезает через стену.");
            Assert.IsTrue(path.Any(cell => cell.Q == 5), "Путь идёт через проход в столбце 5.");
            for (int i = 1; i < path.Count; i++)
                Assert.AreEqual(1, path[i - 1].DistanceTo(path[i]), "Путь — цепочка соседних клеток.");
        }

        [Test]
        public void Path_ToWall_IsEmpty()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            Assert.IsEmpty(SandboxLocalNavigation.FindPath(new HexCoord(3, 1), new HexCoord(5, 2), layout.IsPassable));
        }

        [Test]
        public void AssignCells_GivesUniqueCells_OnTheSameSideOfTheWall()
        {
            SandboxBattleLayout layout = MineLikeLayout();
            // Весь отряд «стоит» в одной клетке у стены слева — после
            // расстановки каждый на своей клетке и по ту же сторону стены.
            List<KeyValuePair<string, HexCoord>> preferred = new List<KeyValuePair<string, HexCoord>>();
            for (int i = 0; i < 5; i++)
                preferred.Add(new KeyValuePair<string, HexCoord>("p" + i, new HexCoord(4, 1)));
            HashSet<HexCoord> reserved = new HashSet<HexCoord> { new HexCoord(8, 2) };

            Assert.IsTrue(SandboxLocalNavigation.TryAssignCells(preferred, layout.IsPassable, reserved, out Dictionary<string, HexCoord> assigned));
            Assert.AreEqual(5, assigned.Values.Distinct().Count(), "Два участника не получают одну клетку.");
            Assert.IsTrue(assigned.Values.All(layout.IsPassable));
            Assert.IsFalse(assigned.Values.Contains(new HexCoord(8, 2)), "Занятая противником клетка не выдаётся.");
            Assert.IsTrue(assigned.Values.All(cell => cell.Q <= 5), "Никто не перенесён за стену в дальнюю штольню.");
        }
    }
}
