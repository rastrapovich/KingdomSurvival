using System;

// Мост между кампанией (KingdomSurvival.Core, без Unity-зависимостей) и
// реальными боевыми характеристиками из UnitDatabase/BattleSandbox.
// Core знает только эту нейтральную структуру и интерфейс поставщика;
// сам UnitDatabaseAsset подключается снаружи через GameState.UnitStatsProvider.

[Serializable]
public struct UnitCombatStats
{
    public int MaxHitPoints;
    public int Attack;
    public int Defense;
    public int Damage;
    public int Movement;
    public int Initiative;
    public int AttackRange;
}

public interface IUnitStatsProvider
{
    bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats);
}
