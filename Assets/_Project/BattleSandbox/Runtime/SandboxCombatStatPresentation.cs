using System;
using System.Globalization;

namespace KingdomSurvival.BattleSandbox
{
    public readonly struct SandboxRangeSnapshot
    {
        public int BaseRange { get; }
        public int HillBonus { get; }
        public int EffectiveRange => BaseRange + HillBonus;
        public bool HasBonus => EffectiveRange > BaseRange;

        public SandboxRangeSnapshot(int baseRange, int hillBonus)
        {
            BaseRange = Math.Max(1, baseRange);
            HillBonus = Math.Max(0, hillBonus);
        }

        public string BuildBreakdown()
        {
            string result = BaseRange + "  база";
            if (HillBonus > 0)
                result += "\n+" + HillBonus + "  расположение: холм";
            result += "\n=" + EffectiveRange + "  итог";
            return result;
        }
    }

    public readonly struct SandboxAttackSnapshot
    {
        public decimal BaseAttack { get; }
        public decimal SlayerBonus { get; }
        public decimal MeleePenalty { get; }
        public decimal EffectiveAttack { get; }
        public string SlayerLabel { get; }
        public bool HasPositiveBonus => EffectiveAttack > BaseAttack;

        public SandboxAttackSnapshot(
            decimal baseAttack,
            decimal slayerBonus,
            decimal meleePenalty,
            string slayerLabel)
        {
            BaseAttack = Math.Max(0m, baseAttack);
            SlayerBonus = Math.Max(0m, slayerBonus);
            MeleePenalty = Math.Min(0m, meleePenalty);
            SlayerLabel = slayerLabel ?? string.Empty;
            EffectiveAttack = Math.Max(0m, BaseAttack + SlayerBonus + MeleePenalty);
        }

        public string BuildBreakdown()
        {
            string result = Format(BaseAttack) + "  база";
            if (SlayerBonus > 0m)
                result += "\n+" + Format(SlayerBonus) + "  " + SlayerLabel;
            if (MeleePenalty < 0m)
                result += "\n−" + Format(-MeleePenalty) + "  ближняя дистанция";
            result += "\n=" + Format(EffectiveAttack) + "  итог";
            return result;
        }

        private static string Format(decimal value)
        {
            return value.ToString(value % 1m == 0m ? "0" : "0.##", CultureInfo.InvariantCulture)
                .Replace('.', ',');
        }
    }

    public static class SandboxCombatStatPresentation
    {
        public static SandboxRangeSnapshot GetRangeSnapshot(SandboxUnitState unit)
        {
            if (unit == null)
                return new SandboxRangeSnapshot(1, 0);

            return new SandboxRangeSnapshot(
                unit.Definition.AttackRange,
                SandboxTerrainRules.GetAttackRangeBonus(unit));
        }

        public static SandboxAttackSnapshot GetAttackSnapshot(
            SandboxUnitState attacker,
            SandboxUnitState target)
        {
            if (attacker == null)
                return new SandboxAttackSnapshot(0m, 0m, 0m, string.Empty);

            decimal baseAttack = attacker.Attack;
            decimal afterSlayer = baseAttack;
            string slayerLabel = string.Empty;

            if (target != null && attacker.HasTag(SandboxCombatTagRules.BeastSlayer) &&
                target.HasTag(SandboxCombatTagRules.Beast))
            {
                afterSlayer *= SandboxCombatTagRules.SlayerAttackMultiplier;
                slayerLabel = "гроза зверей";
            }
            else if (target != null && attacker.HasTag(SandboxCombatTagRules.HumanSlayer) &&
                     target.HasTag(SandboxCombatTagRules.Human))
            {
                afterSlayer *= SandboxCombatTagRules.SlayerAttackMultiplier;
                slayerLabel = "гроза людей";
            }

            decimal slayerBonus = afterSlayer - baseAttack;
            decimal afterRange = afterSlayer;
            if (target != null && attacker.HasTag(SandboxCombatTagRules.Ranged) &&
                attacker.Position.DistanceTo(target.Position) <= 1)
            {
                afterRange *= SandboxCombatTagRules.RangedMeleeAttackMultiplier;
            }

            decimal meleePenalty = afterRange - afterSlayer;
            return new SandboxAttackSnapshot(
                baseAttack,
                slayerBonus,
                meleePenalty,
                slayerLabel);
        }
    }
}
