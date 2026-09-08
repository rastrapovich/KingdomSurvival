using System;

// Преобразует одно качество героя (1-10) в боевой модификатор и применяет
// его к производной боевой характеристике командира — §16 производственной
// инструкции по качествам и проверкам.
//
// Базовые характеристики остаются в UnitDatabase (§16, шаг 1); это только
// шаги 2-6 построения командира. Шаг 7 (передача существующему адаптеру
// BattleSandbox) и предбоевое обнаружение сюда не входят: рабочего перехода
// "кампания → BattleSandbox → результат → кампания" в проекте ещё нет, а
// инструкция явно требует этот переход как предпосылку интеграции
// (§21: "Только после появления рабочего перехода").
//
// Качество нельзя прибавлять к характеристикам напрямую — разница
// Атаки/Защиты уже существенно меняет множитель урона (§16). Подключена
// ровно одна предварительная связь: Сноровка -> Инициатива. Остальные из
// §16 ("Сила — урон подходящего ближнего оружия", "Чутьё — дальняя атака
// и обнаружение засады") требуют системы снаряжения, которой в прототипе
// пока нет (снаряжение на экране героя — рабочая заглушка); Суждение и
// Характер намеренно не получают "искусственный +1 к Атаке" — для них нет
// реальной системы тактических способностей или боевых состояний.
public static class HeroCombatStatsBuilder
{
    public static int GetCombatModifier(int quality)
    {
        int clamped = NarrativeCheckMath.ClampQuality(quality);
        return ((clamped - 1) / 2) - 2;
    }

    public static UnitCombatStats BuildCommanderCombatStats(UnitCombatStats baseStats, HeroProfileData hero)
    {
        UnitCombatStats result = baseStats;
        if (hero == null)
            return result;

        int modifier = GetCombatModifier(hero.GetQuality(HeroQuality.Dexterity));
        result.Initiative = Math.Max(0, result.Initiative + modifier);
        return result;
    }
}
