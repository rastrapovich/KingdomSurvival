using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class RetaliationTests
    {
        [Test]
        public void MeleeAttackCreatesAndResolvesRetaliation()
        {
            SandboxBattle battle = CreateDuel(
                attackerRange: 1,
                attackerDamage: 10,
                defenderDamage: 8,
                defenderRange: 4,
                attackerHitPoints: 40,
                defenderHitPoints: 40);

            string attackMessage;
            Assert.That(battle.TryAttack("player", "enemy", out attackMessage), Is.True);
            Assert.That(battle.GetUnit("enemy").HitPoints, Is.EqualTo(30));
            Assert.That(battle.HasPendingRetaliation, Is.True);
            Assert.That(battle.PendingRetaliationDefenderId, Is.EqualTo("enemy"));
            Assert.That(battle.PendingRetaliationAttackerId, Is.EqualTo("player"));

            SandboxAttackPreview retaliation = battle.PreviewPendingRetaliation();
            Assert.That(retaliation.IsValid, Is.True);
            Assert.That(retaliation.Damage, Is.EqualTo(8));

            string retaliationMessage;
            Assert.That(battle.TryResolvePendingRetaliation(out retaliationMessage), Is.True);
            Assert.That(battle.GetUnit("player").HitPoints, Is.EqualTo(32));
            Assert.That(battle.GetUnit("enemy").HasRetaliatedThisRound, Is.True);
            Assert.That(battle.HasPendingRetaliation, Is.False);
        }

        [Test]
        public void RangedAttackDoesNotTriggerRetaliation()
        {
            SandboxBattle battle = CreateDuel(
                attackerRange: 3,
                attackerDamage: 10,
                defenderDamage: 8,
                attackerPosition: new HexCoord(0, 1),
                defenderPosition: new HexCoord(2, 1));

            string message;
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);
            Assert.That(battle.GetUnit("enemy").IsDefeated, Is.False);
            Assert.That(battle.HasPendingRetaliation, Is.False);
            Assert.That(battle.GetUnit("enemy").HasRetaliatedThisRound, Is.False);
        }

        [Test]
        public void DefeatedTargetCannotRetaliate()
        {
            SandboxBattle battle = CreateDuel(
                attackerRange: 1,
                attackerDamage: 50,
                defenderDamage: 8,
                defenderHitPoints: 10);

            string message;
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);
            Assert.That(battle.GetUnit("enemy").IsDefeated, Is.True);
            Assert.That(battle.HasPendingRetaliation, Is.False);
            Assert.That(battle.Phase, Is.EqualTo(SandboxBattlePhase.PlayerVictory));
        }

        [Test]
        public void DefenderRetaliatesOnlyOncePerRound()
        {
            SandboxUnitDefinition first = CreateDefinition("first", 100, 10, 1, 10);
            SandboxUnitDefinition second = CreateDefinition("second", 100, 10, 1, 9);
            SandboxUnitDefinition enemy = CreateDefinition("enemy", 100, 8, 1, 1);
            SandboxBattle battle = new SandboxBattle(
                4,
                4,
                new[]
                {
                    new SandboxUnitState("first", first, SandboxTeam.Player, new HexCoord(0, 1)),
                    new SandboxUnitState("second", second, SandboxTeam.Player, new HexCoord(1, 2)),
                    new SandboxUnitState("enemy", enemy, SandboxTeam.Enemy, new HexCoord(1, 1))
                });
            battle.Start();

            string message;
            Assert.That(battle.TryAttack("first", "enemy", out message), Is.True);
            Assert.That(battle.TryResolvePendingRetaliation(out message), Is.True);
            Assert.That(battle.GetUnit("enemy").HasRetaliatedThisRound, Is.True);

            battle.EndActivation();
            Assert.That(battle.CurrentUnit.Id, Is.EqualTo("second"));
            Assert.That(battle.TryAttack("second", "enemy", out message), Is.True);
            Assert.That(battle.HasPendingRetaliation, Is.False);
        }

        [Test]
        public void RetaliationCostsNoActionAndReturnsAtNextRound()
        {
            SandboxBattle battle = CreateDuel(
                attackerRange: 1,
                attackerDamage: 10,
                defenderDamage: 8);

            string message;
            Assert.That(battle.TryAttack("player", "enemy", out message), Is.True);
            Assert.That(battle.TryResolvePendingRetaliation(out message), Is.True);
            Assert.That(battle.GetUnit("enemy").HasRetaliatedThisRound, Is.True);

            battle.EndActivation();
            SandboxUnitState enemy = battle.CurrentUnit;
            Assert.That(enemy.Id, Is.EqualTo("enemy"));
            Assert.That(enemy.ActionPoints, Is.EqualTo(SandboxUnitState.ActionsPerActivation));
            Assert.That(enemy.RemainingMovement, Is.EqualTo(enemy.Movement));

            battle.EndActivation();
            Assert.That(battle.Round, Is.EqualTo(2));
            Assert.That(battle.GetUnit("enemy").HasRetaliatedThisRound, Is.False);
            Assert.That(battle.GetUnit("enemy").CanRetaliate, Is.True);
        }

        [Test]
        public void CompactArenaTreatsHiddenCornersAsOutsideBattlefield()
        {
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(new[] { "guard" });

            Assert.That(battle.IsInside(new HexCoord(0, 0)), Is.False);
            Assert.That(battle.IsInside(new HexCoord(1, 0)), Is.True);
            Assert.That(battle.IsInside(new HexCoord(7, 0)), Is.False);
            Assert.That(battle.IsInside(new HexCoord(6, 1)), Is.True);
            Assert.That(battle.IsInside(new HexCoord(7, 1)), Is.False);
            Assert.That(battle.IsInside(new HexCoord(0, 6)), Is.False);
            Assert.That(battle.IsInside(new HexCoord(1, 6)), Is.True);

            int activeCells = Enumerable.Range(0, battle.Height)
                .SelectMany(row => Enumerable.Range(0, battle.Width)
                    .Select(column => new HexCoord(column, row)))
                .Count(battle.IsInside);
            Assert.That(activeCells, Is.EqualTo(SandboxArenaShape.CellCount));
        }

        private static SandboxBattle CreateDuel(
            int attackerRange,
            int attackerDamage,
            int defenderDamage,
            int defenderRange = 1,
            int attackerHitPoints = 100,
            int defenderHitPoints = 100,
            HexCoord? attackerPosition = null,
            HexCoord? defenderPosition = null)
        {
            SandboxUnitDefinition attacker = CreateDefinition(
                "player",
                attackerHitPoints,
                attackerDamage,
                attackerRange,
                10);
            SandboxUnitDefinition defender = CreateDefinition(
                "enemy",
                defenderHitPoints,
                defenderDamage,
                defenderRange,
                5);

            SandboxBattle battle = new SandboxBattle(
                4,
                4,
                new[]
                {
                    new SandboxUnitState(
                        "player",
                        attacker,
                        SandboxTeam.Player,
                        attackerPosition ?? new HexCoord(0, 1)),
                    new SandboxUnitState(
                        "enemy",
                        defender,
                        SandboxTeam.Enemy,
                        defenderPosition ?? new HexCoord(1, 1))
                });
            battle.Start();
            return battle;
        }

        private static SandboxUnitDefinition CreateDefinition(
            string id,
            int hitPoints,
            int damage,
            int range,
            int initiative)
        {
            return new SandboxUnitDefinition(
                id,
                id,
                SandboxUnitRole.Militia,
                hitPoints,
                3,
                3,
                damage,
                3,
                initiative,
                range);
        }
    }
}
