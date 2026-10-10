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

        // «Сдвиг X / Y» сетки поля двигает гексы всей локации: сдвиг на
        // полпериода — центры сместились, сетка по-прежнему покрывает рисунок;
        // сдвиг на целый период — та же сетка.
        [Test]
        public void FieldGridOffset_ShiftsLocationHexes_AndStillCoversThePicture()
        {
            Vector2 canvas = new Vector2(3000, 1800);
            const float size = 60;
            LocationBattleGrid plain = new LocationBattleGrid(canvas, size);
            Vector2 period = LocationBattleGrid.Period(size);
            LocationBattleGrid same = new LocationBattleGrid(canvas, size, period * 3);
            Assert.That(same.Shift, Is.EqualTo(Vector2.zero), "Сдвиг на целый период — та же сетка.");
            Assert.That(same.Columns, Is.EqualTo(plain.Columns));

            LocationBattleGrid shifted = new LocationBattleGrid(canvas, size, period * .5f);
            Assert.That(shifted.Shift.x, Is.InRange(-period.x, 0f));
            Assert.That(shifted.Shift.y, Is.InRange(-period.y, 0f));
            Vector2 moved = shifted.Layout.GetCenter(1, 2) - plain.Layout.GetCenter(1, 2);
            Assert.That(Mathf.Abs(Mathf.Repeat(moved.x, period.x) - period.x * .5f), Is.LessThan(.01f), "Гексы сдвинуты на полстолбца.");
            Assert.That(Mathf.Abs(Mathf.Repeat(moved.y, period.y) - period.y * .5f), Is.LessThan(.01f), "И на полпериода по высоте.");
            // Каждая точка рисунка — в своём гексе: ближайший центр не дальше радиуса.
            for (float x = 0; x <= canvas.x; x += 97)
                for (float y = 0; y <= canvas.y; y += 89)
                {
                    Vector2 point = new Vector2(x, y);
                    Vector2 delta = point - shifted.CellCenter(shifted.CellAt(point));
                    delta.y /= BattlefieldFrame.VerticalScale;
                    Assert.That(delta.magnitude, Is.LessThanOrEqualTo(size * 1.01f), "Точка " + point + " покрыта сеткой.");
                }

            // Место берёт сдвиг поля (доли кадра боя → пиксели рисунка).
            BattlefieldDefinitionData field = new BattlefieldDefinitionData();
            typeof(BattlefieldDefinitionData).GetField("gridOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(field, new Vector2(.01f, .02f));
            LocalLocationGeometry geometry = new LocalLocationGeometry(Location(5169, 2140), field);
            LocalLocationGeometry still = new LocalLocationGeometry(Location(5169, 2140), null);
            Assert.That(geometry.GridShift, Is.EqualTo(new Vector2(.01f * geometry.FrameWidth, .02f * geometry.FrameHeight)));
            Assert.That(geometry.BattleGrid.Layout.Origin, Is.Not.EqualTo(still.BattleGrid.Layout.Origin), "Сдвиг сетки поля двигает гексы места.");
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
