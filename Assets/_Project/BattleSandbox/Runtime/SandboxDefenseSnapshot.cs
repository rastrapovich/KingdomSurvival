using System;

namespace KingdomSurvival.BattleSandbox
{
    public readonly struct SandboxDefenseSnapshot
    {
        public int BaseDefense { get; }
        public int ArmorBonus { get; }
        public int HillBonus { get; }
        public int GuardBonus { get; }
        public int EffectiveDefense { get; }

        public SandboxDefenseSnapshot(
            int baseDefense,
            int armorBonus,
            int hillBonus,
            int guardBonus)
        {
            BaseDefense = Math.Max(0, baseDefense);
            ArmorBonus = Math.Max(0, armorBonus);
            HillBonus = Math.Max(0, hillBonus);
            GuardBonus = Math.Max(0, guardBonus);
            EffectiveDefense = BaseDefense + ArmorBonus + HillBonus + GuardBonus;
        }

        public string BuildCompactBreakdown()
        {
            string result = "База " + BaseDefense;
            if (ArmorBonus > 0)
                result += " · Броня +" + ArmorBonus;
            if (HillBonus > 0)
                result += " · Холм +" + HillBonus;
            if (GuardBonus > 0)
                result += " · Стойка +" + GuardBonus;
            return result;
        }
    }

    public static class SandboxDefensePresentation
    {
        public static SandboxDefenseSnapshot GetSnapshot(
            SandboxBattle battle,
            SandboxUnitState unit)
        {
            if (unit == null)
                return new SandboxDefenseSnapshot(0, 0, 0, 0);

            int armorBonus = unit.HasTag(SandboxCombatTagRules.Armored)
                ? SandboxCombatTagRules.ArmoredDefenseBonus
                : 0;
            int hillBonus = battle != null &&
                            battle.GetTerrain(unit.Position) == SandboxTerrain.Difficult
                ? SandboxTerrainRules.HillDefenseBonus
                : 0;
            int beforeGuard = unit.Defense + armorBonus + hillBonus;
            int guardBonus = unit.IsGuarding
                ? Math.Max(
                    SandboxCombatTagRules.MinimumGuardDefenseBonus,
                    (int)Math.Floor(beforeGuard * 0.50m))
                : 0;

            return new SandboxDefenseSnapshot(
                unit.Defense,
                armorBonus,
                hillBonus,
                guardBonus);
        }
    }
}
