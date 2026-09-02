using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class BattleSandboxTests
    {
        [Test]
        public void HexDistanceIsSymmetricAndCellHasSixNeighbors()
        {
            HexCoord origin = new HexCoord(0, 0);
            HexCoord target = new HexCoord(3, -2);

            Assert.That(origin.Neighbors().Distinct().Count(), Is.EqualTo(6));
            Assert.That(origin.DistanceTo(target), Is.EqualTo(3));
            Assert.That(target.DistanceTo(origin), Is.EqualTo(3));
        }

        [Test]
        public void ReachableHexesExcludeObstaclesAndOccupiedCells()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(2, 1),
                playerMovement: 3,
                terrain: new Dictionary<HexCoord, SandboxTerrain>
                {
                    { new HexCoord(1, 1), SandboxTerrain.Impassable }
                });

            IReadOnlyDictionary<HexCoord, int> reachable = battle.GetReachable("player");

            Assert.That(reachable.ContainsKey(new HexCoord(1, 1)), Is.False);
            Assert.That(reachable.ContainsKey(new HexCoord(2, 1)), Is.False);
            Assert.That(reachable.ContainsKey(new HexCoord(0, 2)), Is.True);
        }

        [Test]
        public void MovementCanBeSplitWithoutSpendingCombatAction()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(4, 1),
                playerMovement: 3);

            string message;
            Assert.That(battle.TryMove("player", new HexCoord(1, 1), out message), Is.True);
            Assert.That(battle.CurrentUnit.ActionPoints, Is.EqualTo(1));
            Assert.That(battle.CurrentUnit.RemainingMovement, Is.EqualTo(2));
            Assert.That(battle.TryMove("player", new HexCoord(2, 1), out message), Is.True);
            Assert.That(battle.CurrentUnit.ActionPoints, Is.EqualTo(1));
            Assert.That(battle.CurrentUnit.RemainingMovement, Is.EqualTo(1));
        }

        [Test]
        public void ExhaustingMovementEndsActivationAutomatically()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(4, 1),
                playerMovement: 1);

            string message;
            Assert.That(battle.TryMove("player", new HexCoord(1, 1), out message), Is.True);
            Assert.That(battle.GetUnit("player").RemainingMovement, Is.EqualTo(0));
            Assert.That(battle.GetUnit("player").ActionPoints, Is.EqualTo(0));
            Assert.That(battle.CurrentUnit.Id, Is.EqualTo("enemy"));
            Assert.That(message, Does.Contain("активация завершена"));
        }

        [Test]
        public void AttackPositionUsesRemainingMovementAndAttackRange()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(3, 1),
                playerMovement: 3);

            HexCoord attackPosition;
            int movementCost;
            Assert.That(
                battle.TryFindAttackPosition(
                    "player",
                    "enemy",
                    out attackPosition,
                    out movementCost),
                Is.True);
            Assert.That(attackPosition.DistanceTo(new HexCoord(3, 1)), Is.EqualTo(1));
            Assert.That(movementCost, Is.EqualTo(2));
            Assert.That(
                battle.TryGetMeleeAttackPosition(
                    "player",
                    "enemy",
                    attackPosition,
                    out movementCost),
                Is.True);
            Assert.That(
                battle.TryGetMeleeAttackPosition(
                    "player",
                    "enemy",
                    new HexCoord(4, 1),
                    out movementCost),
                Is.False);
            Assert.That(battle.PreviewReachableAttack("player", "enemy").IsValid, Is.True);
            Assert.That(battle.PreviewReachableAttack("player", "enemy").Damage, Is.EqualTo(15));

            string message;
            Assert.That(battle.TryMove("player", attackPosition, out message), Is.True);
            Assert.That(battle.PreviewAttack("player", "enemy").IsValid, Is.True);

            SandboxBattle outsideMovement = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(3, 1),
                playerMovement: 2);
            Assert.That(
                outsideMovement.TryFindAttackPosition(
                    "player",
                    "enemy",
                    out attackPosition,
                    out movementCost),
                Is.False);
        }

        [Test]
        public void AttackPreviewMatchesDamageAndAttackCanOnlyBeUsedOnce()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(1, 1),
                playerAttack: 5,
                enemyDefense: 3,
                enemyHitPoints: 100);

            SandboxAttackPreview preview = battle.PreviewAttack("player", "enemy");
            string message;

            Assert.That(preview.IsValid, Is.True);
            Assert.That(preview.Damage, Is.EqualTo(25));
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);
            Assert.That(battle.GetUnit("enemy").HitPoints, Is.EqualTo(75));
            Assert.That(battle.GetUnit("player").ActionPoints, Is.EqualTo(0));
            Assert.That(battle.GetUnit("player").RemainingMovement, Is.EqualTo(0));
            Assert.That(battle.PreviewAttack("player", "enemy").IsValid, Is.False);
        }

        [Test]
        public void DamageStateAlwaysMatchesCurrentHitPoints()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(1, 1),
                playerAttack: 5,
                enemyDefense: 3,
                enemyHitPoints: 100);

            string message;
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);

            SandboxUnitState enemy = battle.GetUnit("enemy");
            Assert.That(enemy.HitPoints, Is.EqualTo(75));
            Assert.That(enemy.DamageTaken, Is.EqualTo(25));
            Assert.That(enemy.IsDamaged, Is.True);
        }

        [Test]
        public void BattlePresentationUsesUnitTypeLabels()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(4, 1));

            Assert.That(battle.CurrentUnit.DisplayLabel, Is.EqualTo("Тест"));

            string message;
            Assert.That(battle.TryMove("player", new HexCoord(1, 1), out message), Is.True);
            Assert.That(message, Does.StartWith("Тест перемещается"));
        }

        [Test]
        public void GuardAddsTwoDefenseUntilNextActivation()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(1, 1),
                playerDefense: 3,
                enemyAttack: 5,
                playerInitiative: 10,
                enemyInitiative: 5);

            string message;
            Assert.That(battle.TryGuard("player", out message), Is.True);
            battle.EndActivation();

            SandboxAttackPreview preview = battle.PreviewAttack("enemy", "player");
            Assert.That(preview.IsValid, Is.True);
            Assert.That(preview.Damage, Is.EqualTo(15));
        }

        [Test]
        public void DefeatingLastEnemyEndsBattleImmediately()
        {
            SandboxBattle battle = CreateBattle(
                playerPosition: new HexCoord(0, 1),
                enemyPosition: new HexCoord(1, 1),
                playerAttack: 10,
                enemyDefense: 0,
                enemyHitPoints: 10);

            string message;
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);
            Assert.That(battle.Phase, Is.EqualTo(SandboxBattlePhase.PlayerVictory));
            Assert.That(battle.CurrentUnit, Is.Null);
        }

        [Test]
        public void DefaultBattleUsesSelectedFightersAndBlackForestEnemies()
        {
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(
                new[] { "guard", "archer", "scout" });

            Assert.That(battle.Width, Is.EqualTo(9));
            Assert.That(battle.Height, Is.EqualTo(7));
            Assert.That(battle.Units.Count(unit => unit.Team == SandboxTeam.Player), Is.EqualTo(3));
            Assert.That(battle.Units.Count(unit => unit.Team == SandboxTeam.Enemy), Is.EqualTo(4));
            Assert.That(battle.Phase, Is.EqualTo(SandboxBattlePhase.InProgress));
        }

        private static SandboxBattle CreateBattle(
            HexCoord playerPosition,
            HexCoord enemyPosition,
            int playerMovement = 3,
            int playerAttack = 3,
            int playerDefense = 3,
            int enemyAttack = 3,
            int enemyDefense = 3,
            int enemyHitPoints = 100,
            int playerInitiative = 10,
            int enemyInitiative = 5,
            IDictionary<HexCoord, SandboxTerrain> terrain = null)
        {
            SandboxUnitDefinition player = new SandboxUnitDefinition(
                "player",
                "Тест",
                SandboxUnitRole.Militia,
                100,
                playerAttack,
                playerDefense,
                playerMovement,
                playerInitiative,
                1);
            SandboxUnitDefinition enemy = new SandboxUnitDefinition(
                "enemy",
                "Тест",
                SandboxUnitRole.Beast,
                enemyHitPoints,
                enemyAttack,
                enemyDefense,
                3,
                enemyInitiative,
                1);

            SandboxBattle battle = new SandboxBattle(
                5,
                4,
                new[]
                {
                    new SandboxUnitState(player, SandboxTeam.Player, playerPosition),
                    new SandboxUnitState(enemy, SandboxTeam.Enemy, enemyPosition)
                },
                terrain);
            battle.Start();
            return battle;
        }
    }
}
