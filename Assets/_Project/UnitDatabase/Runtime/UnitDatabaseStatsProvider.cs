using UnityEngine;

namespace KingdomSurvival.UnitDatabase
{
    // Реализация IUnitStatsProvider (KingdomSurvival.Core) поверх
    // UnitDatabaseAsset. Основная игра назначает экземпляр в
    // GameState.UnitStatsProvider при старте, чтобы боевые агрегаты
    // кампании читали настоящие характеристики вместо legacy-полей.
    // Бриф «Живое население» (19.11.2026): тот же поставщик отвечает
    // генератору населения, годится ли существо для новых групп (запись есть,
    // не исключено из новых встреч, есть рисунок поля или портрет).
    public sealed class UnitDatabaseStatsProvider : IUnitStatsProvider, IUnitCatalogAvailability
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

        public bool IsAvailableForNewGroups(string unitTypeId, out string reason)
        {
            reason = string.Empty;
            UnitDefinitionData unit = database != null ? database.FindById(unitTypeId) : null;
            if (unit == null)
            {
                reason = database == null ? "База существ не загружена" : "нет записи в Базе существ";
                return false;
            }
            if (unit.ExcludedFromNewEncounters)
            {
                reason = "исключено из новых встреч" + (string.IsNullOrEmpty(unit.ExclusionNote) ? "" : " (" + unit.ExclusionNote + ")");
                return false;
            }
            if (unit.BattlefieldSprite == null && unit.Portrait == null)
            {
                reason = "нет рисунка поля и портрета";
                return false;
            }
            return true;
        }

        // Название типа для подписей карты.
        public string Label(string unitTypeId)
        {
            UnitDefinitionData unit = database != null ? database.FindById(unitTypeId) : null;
            return unit != null && !string.IsNullOrWhiteSpace(unit.DisplayLabel) ? unit.DisplayLabel.ToLowerInvariant() : unitTypeId;
        }

        // Рисунок группы без набора ракурсов: миниатюра поля (или портрет)
        // активного вида карты.
        public Sprite MapSprite(string unitTypeId)
        {
            UnitDefinitionData unit = database != null ? database.FindById(unitTypeId) : null;
            if (unit == null)
                return null;
            return unit.ActiveBattlefieldSprite != null ? unit.ActiveBattlefieldSprite : unit.ActivePortrait;
        }
    }
}
