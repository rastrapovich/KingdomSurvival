using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    // ПР-12К: бой на месте — на гексах всей локации. Сетка покрывает весь
    // рисунок клетками размера кадра боя, стены — непроходимое и края; бой
    // на большом поле начинается из явной раскладки, противник обходит стену.
    public sealed class LocationBattleGridTests
    {
        private static LocalLocationDefinition Location(float width, float height) => new LocalLocationDefinition
        {
            Id = "grid_test", DisplayName = "Сетка", CanvasWidth = width, CanvasHeight = height, HexesAcross = 40, BattleFrameWidth = 1920,
            Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(100, 100) } }
        };

        [Test]
        public void GridCoversTheWholePicture_WithFrameSizedHexes()
        {
            LocalLocationGeometry geometry = new LocalLocationGeometry(Location(5169, 2140), null);
            LocationBattleGrid grid = geometry.BattleGrid;
            Assert.That(grid.HexSize, Is.EqualTo(geometry.ArenaHexSize).Within(1e-3), "Гекс — того же размера, что в кадре боя.");
            BattlefieldGridLayout layout = grid.Layout;
            Vector2 first = layout.GetCenter(0, 0), last = layout.GetCenter(grid.Columns - 1, grid.Rows - 1 - ((grid.Rows - 1) & 1));
            Assert.That(first.x, Is.LessThan(grid.HexSize * 2) .And.GreaterThan(0));
            Assert.That(first.y, Is.LessThan(grid.HexSize * 2).And.GreaterThan(0));
            Assert.That(last.x, Is.LessThanOrEqualTo(5169).And.GreaterThan(5169 - grid.HexSize * 2.5f), "Сетка доходит до правого края.");
            Assert.That(last.y, Is.LessThanOrEqualTo(2140).And.GreaterThan(2140 - grid.HexSize * 2.5f), "И до нижнего.");
            Assert.That(grid.CellCount, Is.GreaterThan(SandboxArenaShape.CellCount * 5), "Поле больше прежней арены 10×7.");
            Assert.That(grid.IsTooLarge, Is.False);
            // Клетка под центром клетки — она сама.
            foreach (HexCoord cell in new[] { new HexCoord(0, 0), new HexCoord(5, 3), new HexCoord(grid.Columns - 1, grid.Rows - 1) })
                Assert.That(grid.CellAt(grid.CellCenter(cell)), Is.EqualTo(cell));
        }

        [Test]
        public void BigBoardBattle_StartsFromLayout_AndEnemyWalksAroundTheWall()
        {
            const int width = 30, height = 14;
            SandboxBattleLayout layout = new SandboxBattleLayout { Width = width, Height = height };
            // Стена через всё поле с проходом внизу.
            for (int r = 0; r < height - 2; r++) layout.BlockedCells.Add(new HexCoord(15, r));
            SandboxUnitDefinition fighter = new SandboxUnitDefinition("fighter", "Боец", SandboxUnitRole.Guard, 20, 3, 1, 3, 4, 3, 1);
            SandboxUnitDefinition beast = new SandboxUnitDefinition("beast", "Зверь", SandboxUnitRole.Guard, 20, 3, 1, 3, 4, 5, 1);
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "player:a", Definition = fighter, Team = SandboxTeam.Player, Position = new HexCoord(10, 2) });
            layout.Units.Add(new SandboxLayoutUnit { InstanceId = "enemy:b", Definition = beast, Team = SandboxTeam.Enemy, Position = new HexCoord(20, 2) });
            Assert.That(layout.Validate(), Is.Empty);
            SandboxBattle battle = layout.CreateBattle();
            Assert.That(battle.Width, Is.EqualTo(width));
            Assert.That(battle.IsInside(new HexCoord(29, 13)), Is.True, "Прямоугольник целиком — не форма арены 10×7.");
            Assert.That(battle.IsInside(new HexCoord(15, 2)), Is.False, "Стена — не клетка поля.");
            SandboxUnitState enemy = battle.GetUnit("enemy:b");
            HexCoord step = battle.FindBestMoveToward(enemy, new HexCoord(10, 2));
            Assert.That(step.R, Is.GreaterThan(2), "Противник идёт к проходу внизу, а не упирается в стену.");
        }
    }
}
