using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxTerrainRules
    {
        public const int HillMovementCost = 2;
        public const int HillDefenseBonus = 2;
        public const int HillRangedAttackRangeBonus = 1;

        private sealed class TerrainContext
        {
            public IReadOnlyDictionary<HexCoord, SandboxTerrain> Terrain { get; }

            public TerrainContext(IReadOnlyDictionary<HexCoord, SandboxTerrain> terrain)
            {
                Terrain = terrain;
            }
        }

        private static readonly ConditionalWeakTable<SandboxUnitState, TerrainContext> ContextByUnit =
            new ConditionalWeakTable<SandboxUnitState, TerrainContext>();

        public static void RegisterBattle(
            IEnumerable<SandboxUnitState> units,
            IDictionary<HexCoord, SandboxTerrain> terrain)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            Dictionary<HexCoord, SandboxTerrain> snapshot =
                new Dictionary<HexCoord, SandboxTerrain>(terrain);
            TerrainContext context = new TerrainContext(snapshot);

            foreach (SandboxUnitState unit in units)
            {
                if (unit == null)
                    continue;

                ContextByUnit.Remove(unit);
                ContextByUnit.Add(unit, context);
            }
        }

        public static int GetDefenseBonus(SandboxUnitState unit)
        {
            if (!IsOnHill(unit))
                return 0;

            return HillDefenseBonus;
        }

        public static int GetAttackRangeBonus(SandboxUnitState unit)
        {
            if (unit == null || !unit.HasTag(SandboxCombatTagRules.Ranged) || !IsOnHill(unit))
                return 0;

            return HillRangedAttackRangeBonus;
        }

        public static int GetEffectiveAttackRange(SandboxUnitState unit)
        {
            if (unit == null)
                return 0;

            return Math.Max(1, unit.Definition.AttackRange + GetAttackRangeBonus(unit));
        }

        public static bool IsOnHill(SandboxUnitState unit)
        {
            if (unit == null || !ContextByUnit.TryGetValue(unit, out TerrainContext context))
                return false;

            return context.Terrain.TryGetValue(unit.Position, out SandboxTerrain terrain) &&
                   terrain == SandboxTerrain.Difficult;
        }
    }
}
