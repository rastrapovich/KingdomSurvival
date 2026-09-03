using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class HillRangeBonusTests
    {
        [Test]
        public void RangedUnitOnHill_GetsPlusOneAttackRange()
        {
            SandboxUnitState archer = CreateUnit(
                "archer",
                SandboxTeam.Player,
                4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged },
                new HexCoord(1, 1));
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { archer.Position, SandboxTerrain.Difficult }
            };

            SandboxTerrainRules.RegisterBattle(new[] { archer }, terrain);
            SandboxRangeSnapshot range = SandboxCombatStatPresentation.GetRangeSnapshot(archer);

            Assert.That(archer.Definition.AttackRange, Is.EqualTo(4));
            Assert.That(archer.AttackRange, Is.EqualTo(5));
            Assert.That(range.BaseRange, Is.EqualTo(4));
            Assert.That(range.HillBonus, Is.EqualTo(1));
            Assert.That(range.EffectiveRange, Is.EqualTo(5));
        }

        [Test]
        public void NonRangedUnitOnHill_DoesNotGetRangeBonus()
        {
            SandboxUnitState guard = CreateUnit(
                "guard",
                SandboxTeam.Player,
                1,
                new[] { SandboxCombatTagRules.Human },
                new HexCoord(1, 1));
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { guard.Position, SandboxTerrain.Difficult }
            };

            SandboxTerrainRules.RegisterBattle(new[] { guard }, terrain);

            Assert.That(guard.AttackRange, Is.EqualTo(1));
            Assert.That(SandboxTerrainRules.GetAttackRangeBonus(guard), Is.EqualTo(0));
        }

        [Test]
        public void HillRangeBonus_ChangesRealAttackAvailability()
        {
            SandboxBattle hillBattle = CreateRangedBattle(true);
            SandboxBattle flatBattle = CreateRangedBattle(false);

            SandboxAttackPreview fromHill = hillBattle.PreviewAttack("player", "enemy");
            SandboxAttackPreview fromFlat = flatBattle.PreviewAttack("player", "enemy");

            Assert.That(fromHill.IsValid, Is.True);
            Assert.That(fromFlat.IsValid, Is.False);
            Assert.That(fromFlat.Reason, Is.EqualTo("Цель вне дальности."));
        }

        [Test]
        public void RangeBonus_DisappearsAfterLeavingHill()
        {
            SandboxUnitState archer = CreateUnit(
                "archer",
                SandboxTeam.Player,
                4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged },
                new HexCoord(0, 0));
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { new HexCoord(0, 0), SandboxTerrain.Difficult }
            };

            SandboxTerrainRules.RegisterBattle(new[] { archer }, terrain);
            Assert.That(archer.AttackRange, Is.EqualTo(5));

            archer.Position = new HexCoord(1, 0);
            Assert.That(archer.AttackRange, Is.EqualTo(4));
        }

        private static SandboxBattle CreateRangedBattle(bool playerOnHill)
        {
            SandboxUnitState player = CreateUnit(
                "player",
                SandboxTeam.Player,
                4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged },
                new HexCoord(0, 0),
                10);
            SandboxUnitState enemy = CreateUnit(
                "enemy",
                SandboxTeam.Enemy,
                1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(5, 0),
                1);

            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            if (playerOnHill)
                terrain[player.Position] = SandboxTerrain.Difficult;

            SandboxUnitState[] units = { player, enemy };
            SandboxTerrainRules.RegisterBattle(units, terrain);
            SandboxBattle battle = new SandboxBattle(8, 4, units, terrain);
            battle.Start();
            return battle;
        }

        private static SandboxUnitState CreateUnit(
            string id,
            SandboxTeam team,
            int attackRange,
            string[] tags,
            HexCoord position,
            int initiative = 5)
        {
            SandboxUnitDefinition definition = new SandboxUnitDefinition(
                id + "_type",
                id,
                team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Archer,
                20,
                3,
                2,
                3,
                6,
                initiative,
                attackRange,
                tags);
            return new SandboxUnitState(id, definition, team, position);
        }
    }
}
