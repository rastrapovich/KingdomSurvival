using UnityEngine;

namespace KingdomSurvival.UnitDatabase
{
    // Реализация IUnitStatsProvider (KingdomSurvival.Core) поверх
    // UnitDatabaseAsset. Основная игра назначает экземпляр в
    // GameState.UnitStatsProvider при старте, чтобы боевые агрегаты
    // кампании читали настоящие характеристики вместо legacy-полей.
    public sealed class UnitDatabaseStatsProvider : IUnitStatsProvider
    {
        private readonly UnitDatabaseAsset database;

        public UnitDatabaseStatsProvider()
        {
            database = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
        }

        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = default;

            UnitDefinitionData unit = database != null ? database.FindById(unitTypeId) : null;
            if (unit == null)
                return false;

            stats = new UnitCombatStats
            {
                MaxHitPoints = unit.MaxHitPoints,
                Attack = unit.Attack,
                Defense = unit.Defense,
                Damage = unit.Damage,
                Movement = unit.Movement,
                Initiative = unit.Initiative,
                AttackRange = unit.AttackRange
            };
            return true;
        }
    }
}
