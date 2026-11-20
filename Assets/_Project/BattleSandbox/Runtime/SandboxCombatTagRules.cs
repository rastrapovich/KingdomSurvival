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
        // ПР-16: тег «Стая» (Волк) — правило, не кнопка.
        public const string Pack = "combat.pack";
        // Рабочая настройка брифа 19.11.2026, не окончательный баланс.
        public static int PackAttackBonus = SandboxBattle.PackBonusDefault;

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
                // status.130 (реестр пакета): прежняя формула floor(×0,5), минимум +1,
                // от Защиты до стойки; второй бонус состояния не добавляется.
                int guardBonus = Math.Max(
                    MinimumGuardDefenseBonus,
                    (int)Math.Floor(effectiveDefense * 0.50m));
                effectiveDefense += guardBonus;
            }

            // Пакет состояний: временные модификаторы Защиты (предел ±2).
            effectiveDefense += SandboxBattle.StatusDefenseMod(target);

            // Бриф «Поверхности арены»: топь — после положительных бонусов.
            effectiveDefense -= SandboxTerrainRules.GetSurfaceDefensePenalty(target);

            return Math.Max(0, effectiveDefense);
        }

        // Фактическая прибавка стойки (для подсказки status.130).
        public static int GetGuardBonus(SandboxUnitState unit)
        {
            if (unit == null)
                return 0;
            int before = unit.Defense + (unit.HasTag(Armored) ? ArmoredDefenseBonus : 0) + SandboxTerrainRules.GetDefenseBonus(unit);
            return Math.Max(MinimumGuardDefenseBonus, (int)Math.Floor(before * 0.50m));
        }

        public static int GetGuardDefensePercent(SandboxUnitState unit)
        {
            return GuardDefensePercent;
        }
    }
}
