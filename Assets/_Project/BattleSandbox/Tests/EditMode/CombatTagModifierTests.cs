using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class CombatTagModifierTests
    {
        [Test]
        public void Guard_UsesFiftyPercentDefenseBonusRoundedDown()
        {
            SandboxUnitState defender = CreateState(
                "defender", SandboxTeam.Player, 2, 3, 2, 1,
                new[] { SandboxCombatTagRules.Human });
            SandboxUnitState enemy = CreateState(
                "enemy", SandboxTeam.Enemy, 2, 2, 1, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(3, 0));
            SandboxBattle battle = CreateBattle(defender, enemy);
            battle.Start();

            Assert.That(battle.TryGuard(defender.Id, out _), Is.True);
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(4m));
        }

        [Test]
        public void Guard_HasMinimumOneDefenseBonus()
        {
            SandboxUnitState defender = CreateState(
                "defender", SandboxTeam.Player, 2, 1, 2, 1,
                new[] { SandboxCombatTagRules.Human });
            SandboxUnitState enemy = CreateState(
                "enemy", SandboxTeam.Enemy, 2, 2, 1, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(3, 0));
            SandboxBattle battle = CreateBattle(defender, enemy);
            battle.Start();

            Assert.That(battle.TryGuard(defender.Id, out _), Is.True);
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(2m));
        }

        [Test]
        public void DefenderAndArmored_UseSameFiftyPercentGuardBonus()
        {
            SandboxUnitState defender = CreateState(
                "defender", SandboxTeam.Player, 2, 4, 2, 1,
                new[]
                {
                    SandboxCombatTagRules.Human,
                    SandboxCombatTagRules.Defender,
                    SandboxCombatTagRules.Armored
                });
            SandboxUnitState enemy = CreateState(
                "enemy", SandboxTeam.Enemy, 2, 2, 1, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(3, 0));
            SandboxBattle battle = CreateBattle(defender, enemy);
            battle.Start();

            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(6m));
            Assert.That(battle.TryGuard(defender.Id, out _), Is.True);
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(9m));
            Assert.That(SandboxCombatTagRules.GetGuardDefensePercent(defender), Is.EqualTo(50));
        }

        [Test]
        public void Hill_AddsTwoDefense()
        {
            HexCoord hill = new HexCoord(0, 0);
            SandboxUnitState defender = CreateState(
                "hill_defender", SandboxTeam.Player, 2, 4, 2, 1,
                new[] { SandboxCombatTagRules.Human },
                hill);

            SandboxTerrainRules.RegisterBattle(
                new[] { defender },
                new Dictionary<HexCoord, SandboxTerrain>
                {
                    { hill, SandboxTerrain.Difficult }
                });

            Assert.That(SandboxTerrainRules.GetDefenseBonus(defender), Is.EqualTo(2));
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(6m));
        }

        [Test]
        public void HillAndArmor_AreAppliedBeforeGuardBonus()
        {
            HexCoord hill = new HexCoord(0, 0);
            SandboxUnitState defender = CreateState(
                "hill_guard", SandboxTeam.Player, 2, 4, 2, 1,
                new[]
                {
                    SandboxCombatTagRules.Human,
                    SandboxCombatTagRules.Armored
                },
                hill);
            SandboxUnitState enemy = CreateState(
                "enemy", SandboxTeam.Enemy, 2, 2, 1, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(3, 0));

            SandboxTerrainRules.RegisterBattle(
                new[] { defender, enemy },
                new Dictionary<HexCoord, SandboxTerrain>
                {
                    { hill, SandboxTerrain.Difficult }
                });

            SandboxBattle battle = CreateBattle(defender, enemy);
            battle.Start();

            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(8m));
            Assert.That(battle.TryGuard(defender.Id, out _), Is.True);
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(defender), Is.EqualTo(12m));
        }

        [Test]
        public void BeastSlayer_AddsFiftyPercentAttackAgainstBeast()
        {
            SandboxUnitState attacker = CreateState(
                "spearman", SandboxTeam.Player, 3, 3, 3, 1,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.BeastSlayer });
            SandboxUnitState target = CreateState(
                "beast", SandboxTeam.Enemy, 2, 1, 3, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(1, 0));

            Assert.That(
                SandboxCombatTagRules.GetEffectiveAttack(attacker, target, attacker.Position),
                Is.EqualTo(4.5m));
        }

        [Test]
        public void HumanSlayer_AddsFiftyPercentAttackAgainstHuman()
        {
            SandboxUnitState attacker = CreateState(
                "alpha", SandboxTeam.Enemy, 4, 3, 5, 1,
                new[] { SandboxCombatTagRules.Beast, SandboxCombatTagRules.HumanSlayer });
            SandboxUnitState target = CreateState(
                "human", SandboxTeam.Player, 2, 2, 3, 1,
                new[] { SandboxCombatTagRules.Human },
                new HexCoord(1, 0));

            Assert.That(
                SandboxCombatTagRules.GetEffectiveAttack(attacker, target, attacker.Position),
                Is.EqualTo(6m));
        }

        [Test]
        public void RangedTag_HalvesAttackAtAdjacentHex()
        {
            SandboxUnitState attacker = CreateState(
                "archer", SandboxTeam.Player, 3, 1, 3, 4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged });
            SandboxUnitState target = CreateState(
                "target", SandboxTeam.Enemy, 2, 2, 3, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(1, 0));

            Assert.That(
                SandboxCombatTagRules.GetEffectiveAttack(attacker, target, attacker.Position),
                Is.EqualTo(1.5m));
        }

        [Test]
        public void RangedTag_KeepsFullAttackBeyondAdjacentHex()
        {
            SandboxUnitState attacker = CreateState(
                "archer", SandboxTeam.Player, 3, 1, 3, 4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged });
            SandboxUnitState target = CreateState(
                "target", SandboxTeam.Enemy, 2, 2, 3, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(3, 0));

            Assert.That(
                SandboxCombatTagRules.GetEffectiveAttack(attacker, target, attacker.Position),
                Is.EqualTo(3m));
        }

        [Test]
        public void PreviewAttack_UsesTagDrivenRangedPenalty()
        {
            SandboxUnitState archer = CreateState(
                "archer", SandboxTeam.Player, 3, 2, 4, 4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged });
            SandboxUnitState target = CreateState(
                "target", SandboxTeam.Enemy, 1, 2, 3, 1,
                new[] { SandboxCombatTagRules.Beast },
                new HexCoord(1, 0));
            SandboxBattle battle = CreateBattle(archer, target);
            battle.Start();

            SandboxAttackPreview preview = battle.PreviewAttack(archer.Id, target.Id);

            Assert.That(preview.IsValid, Is.True);
            Assert.That(preview.Damage, Is.EqualTo(3));
        }

        [Test]
        public void Retaliation_UsesHumanSlayerAttackModifier()
        {
            SandboxUnitState human = CreateState(
                "human", SandboxTeam.Player, 2, 2, 2, 1,
                new[] { SandboxCombatTagRules.Human });
            SandboxUnitState alpha = CreateState(
                "alpha", SandboxTeam.Enemy, 4, 3, 5, 1,
                new[]
                {
                    SandboxCombatTagRules.Beast,
                    SandboxCombatTagRules.HumanSlayer
                },
                new HexCoord(1, 0));
            SandboxBattle battle = CreateBattle(human, alpha);
            battle.Start();

            Assert.That(battle.TryAttack(human.Id, alpha.Id, out _), Is.True);
            SandboxAttackPreview retaliation = battle.PreviewPendingRetaliation();

            Assert.That(retaliation.IsValid, Is.True);
            Assert.That(retaliation.Damage, Is.EqualTo(10));
        }

        private static SandboxBattle CreateBattle(
            SandboxUnitState player,
            SandboxUnitState enemy)
        {
            return new SandboxBattle(8, 4, new[] { player, enemy });
        }

        private static SandboxUnitState CreateState(
            string id,
            SandboxTeam team,
            int attack,
            int defense,
            int damage,
            int attackRange,
            string[] tags,
            HexCoord? position = null)
        {
            SandboxUnitDefinition definition = new SandboxUnitDefinition(
                id + "_type",
                id,
                team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30,
                attack,
                defense,
                damage,
                3,
                team == SandboxTeam.Player ? 10 : 1,
                attackRange,
                tags);
            return new SandboxUnitState(
                id,
                definition,
                team,
                position ?? new HexCoord(0, 0));
        }
    }
}
