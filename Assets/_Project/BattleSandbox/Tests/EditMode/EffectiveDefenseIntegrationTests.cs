using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class EffectiveDefenseIntegrationTests
    {
        [Test]
        public void HillDefense_ReducesActualPreviewDamage()
        {
            SandboxUnitState attacker = CreateUnit(
                "attacker", SandboxTeam.Player, 4, 1, 10, new HexCoord(0, 0));
            SandboxUnitState normalTarget = CreateUnit(
                "normal", SandboxTeam.Enemy, 1, 2, 1, new HexCoord(1, 0));
            SandboxUnitState hillTarget = CreateUnit(
                "hill", SandboxTeam.Enemy, 1, 2, 1, new HexCoord(1, 0));

            SandboxBattle normalBattle = new SandboxBattle(
                4,
                4,
                new[] { attacker, normalTarget });
            SandboxTerrainRules.RegisterBattle(normalBattle.Units, new Dictionary<HexCoord, SandboxTerrain>());
            normalBattle.Start();

            SandboxUnitState hillAttacker = CreateUnit(
                "hill-attacker", SandboxTeam.Player, 4, 1, 10, new HexCoord(0, 0));
            Dictionary<HexCoord, SandboxTerrain> hillTerrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { hillTarget.Position, SandboxTerrain.Difficult }
            };
            SandboxBattle hillBattle = new SandboxBattle(
                4,
                4,
                new[] { hillAttacker, hillTarget },
                hillTerrain);
            SandboxTerrainRules.RegisterBattle(hillBattle.Units, hillTerrain);
            hillBattle.Start();

            SandboxAttackPreview normalPreview = normalBattle.PreviewAttack(attacker.Id, normalTarget.Id);
            SandboxAttackPreview hillPreview = hillBattle.PreviewAttack(hillAttacker.Id, hillTarget.Id);

            Assert.That(normalPreview.IsValid, Is.True);
            Assert.That(hillPreview.IsValid, Is.True);
            Assert.That(normalPreview.Damage, Is.EqualTo(15));
            Assert.That(hillPreview.Damage, Is.EqualTo(10));
        }

        [Test]
        public void DefenseSnapshot_ShowsArmorHillAndRoundedGuardFromSameCalculationOrder()
        {
            SandboxUnitState defender = CreateUnit(
                "guard", SandboxTeam.Player, 2, 4, 2, new HexCoord(1, 0),
                new[] { SandboxCombatTagRules.Armored });
            SandboxUnitState enemy = CreateUnit(
                "enemy", SandboxTeam.Enemy, 2, 1, 2, new HexCoord(3, 0));
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { defender.Position, SandboxTerrain.Difficult }
            };
            SandboxBattle battle = new SandboxBattle(5, 4, new[] { defender, enemy }, terrain);
            SandboxTerrainRules.RegisterBattle(battle.Units, terrain);
            battle.Start();

            Assert.That(battle.TryGuard(defender.Id, out _), Is.True);

            SandboxDefenseSnapshot snapshot = SandboxDefensePresentation.GetSnapshot(battle, defender);
            Assert.That(snapshot.BaseDefense, Is.EqualTo(4));
            Assert.That(snapshot.ArmorBonus, Is.EqualTo(2));
            Assert.That(snapshot.HillBonus, Is.EqualTo(2));
            Assert.That(snapshot.GuardBonus, Is.EqualTo(4));
            Assert.That(snapshot.EffectiveDefense, Is.EqualTo(12));
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(12m));
        }

        private static SandboxUnitState CreateUnit(
            string id,
            SandboxTeam team,
            int attack,
            int defense,
            int damage,
            HexCoord position,
            string[] tags = null)
        {
            SandboxUnitDefinition definition = new SandboxUnitDefinition(
                id + "-type",
                id,
                team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30,
                attack,
                defense,
                damage,
                4,
                team == SandboxTeam.Player ? 10 : 1,
                1,
                tags);
            return new SandboxUnitState(id, definition, team, position);
        }
    }
}
