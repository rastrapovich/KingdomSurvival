using System;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxCombatTagRules
    {
        public const string Human = "species.human";
        public const string Beast = "species.beast";
        public const string Ranged = "combat.ranged";
        public const string Defender = "role.defender";
        public const string Armored = "trait.armored";
        public const string BeastSlayer = "trait.beast_slayer";
        public const string HumanSlayer = "trait.human_slayer";

        public const decimal GuardDefenseMultiplier = 1.50m;
        public const decimal DefenderGuardBonus = 0.25m;
        public const decimal SlayerAttackMultiplier = 1.50m;
        public const decimal RangedMeleeAttackMultiplier = 0.50m;
        public const int ArmoredDefenseBonus = 2;

        public static decimal GetEffectiveAttack(
            SandboxUnitState attacker,
            SandboxUnitState target,
            HexCoord attackPosition)
        {
            if (attacker == null)
                return 0m;

            decimal effectiveAttack = attacker.Attack;

            if (target != null &&
                attacker.HasTag(BeastSlayer) &&
                target.HasTag(Beast))
            {
                effectiveAttack *= SlayerAttackMultiplier;
            }

            if (target != null &&
                attacker.HasTag(HumanSlayer) &&
                target.HasTag(Human))
            {
                effectiveAttack *= SlayerAttackMultiplier;
            }

            if (target != null &&
                attacker.HasTag(Ranged) &&
                attackPosition.DistanceTo(target.Position) <= 1)
            {
                effectiveAttack *= RangedMeleeAttackMultiplier;
            }

            return Math.Max(0m, effectiveAttack);
        }

        public static decimal GetEffectiveDefense(SandboxUnitState target)
        {
            if (target == null)
                return 0m;

            decimal effectiveDefense = target.Defense;
            if (target.HasTag(Armored))
                effectiveDefense += ArmoredDefenseBonus;

            if (target.IsGuarding)
            {
                decimal multiplier = GuardDefenseMultiplier;
                if (target.HasTag(Defender))
                    multiplier += DefenderGuardBonus;
                effectiveDefense *= multiplier;
            }

            return Math.Max(0m, effectiveDefense);
        }

        public static int GetGuardDefensePercent(SandboxUnitState unit)
        {
            return unit != null && unit.HasTag(Defender) ? 75 : 50;
        }
    }
}
