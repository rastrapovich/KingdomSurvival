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

        public const decimal SlayerAttackMultiplier = 1.50m;
        public const decimal RangedMeleeAttackMultiplier = 0.50m;
        public const int ArmoredDefenseBonus = 2;
        public const int GuardDefensePercent = 50;
        public const int MinimumGuardDefenseBonus = 1;

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

            int effectiveDefense = target.Defense;
            if (target.HasTag(Armored))
                effectiveDefense += ArmoredDefenseBonus;

            effectiveDefense += SandboxTerrainRules.GetDefenseBonus(target);

            if (target.IsGuarding)
            {
                int guardBonus = Math.Max(
                    MinimumGuardDefenseBonus,
                    (int)Math.Floor(effectiveDefense * 0.50m));
                effectiveDefense += guardBonus;
            }

            return Math.Max(0, effectiveDefense);
        }

        public static int GetGuardDefensePercent(SandboxUnitState unit)
        {
            return GuardDefensePercent;
        }
    }
}
