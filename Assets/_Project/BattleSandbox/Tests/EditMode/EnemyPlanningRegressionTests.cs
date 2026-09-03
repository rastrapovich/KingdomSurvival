using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class EnemyPlanningRegressionTests
    {
        [Test]
        public void EnemyPlanner_RangedUnitMovesOnlyEnoughToShootAndAttacks()
        {
            SandboxUnitDefinition archer = new SandboxUnitDefinition(
                "enemy_archer",
                "Вражеский лучник",
                SandboxUnitRole.Archer,
                12,
                3,
                1,
                3,
                3,
                10,
                4);
            SandboxUnitDefinition guard = new SandboxUnitDefinition(
                "player_guard",
                "Гвардеец",
                SandboxUnitRole.Guard,
                30,
                2,
                2,
                2,
                3,
                1,
                1);

            SandboxUnitState enemy = new SandboxUnitState(
                "enemy",
                archer,
                SandboxTeam.Enemy,
                new HexCoord(0, 1));
            SandboxUnitState player = new SandboxUnitState(
                "player",
                guard,
                SandboxTeam.Player,
                new HexCoord(6, 1));
            SandboxBattle battle = new SandboxBattle(
                10,
                4,
                new[] { enemy, player });
            battle.Start();

            int hitPointsBefore = player.HitPoints;
            var events = SandboxEnemyPlanner.TakeCurrentTurn(battle);

            Assert.That(events.Any(entry => entry.Contains("перемещается")), Is.True);
            Assert.That(events.Any(entry => entry.Contains("наносит")), Is.True);
            Assert.That(player.HitPoints, Is.LessThan(hitPointsBefore));
            Assert.That(enemy.Position.DistanceTo(player.Position), Is.EqualTo(4));
        }

        [Test]
        public void EnemyPlanner_MeleeUnitMovesAndAttacksInSameTurn()
        {
            SandboxUnitDefinition beast = new SandboxUnitDefinition(
                "enemy_beast",
                "Зверь",
                SandboxUnitRole.Beast,
                14,
                3,
                2,
                4,
                3,
                10,
                1,
                new[] { SandboxUnitTags.Beast });
            SandboxUnitDefinition guard = new SandboxUnitDefinition(
                "player_guard",
                "Гвардеец",
                SandboxUnitRole.Guard,
                30,
                2,
                2,
                2,
                3,
                1,
                1);

            SandboxUnitState enemy = new SandboxUnitState(
                "enemy",
                beast,
                SandboxTeam.Enemy,
                new HexCoord(0, 1));
            SandboxUnitState player = new SandboxUnitState(
                "player",
                guard,
                SandboxTeam.Player,
                new HexCoord(3, 1));
            SandboxBattle battle = new SandboxBattle(
                8,
                4,
                new[] { enemy, player });
            battle.Start();

            int hitPointsBefore = player.HitPoints;
            var events = SandboxEnemyPlanner.TakeCurrentTurn(battle);

            Assert.That(events.Any(entry => entry.Contains("перемещается")), Is.True);
            Assert.That(events.Any(entry => entry.Contains("наносит")), Is.True);
            Assert.That(player.HitPoints, Is.LessThan(hitPointsBefore));
            Assert.That(enemy.Position.DistanceTo(player.Position), Is.EqualTo(1));
        }
    }
}
