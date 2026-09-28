namespace KingdomSurvival.BattleSandbox
{
    // 12Е-6: боевые правила особенностей, которые исполняет бой. Кампания
    // переводит особенности человека в эти ID (CombatPerkIds в ядре — те же
    // строки); полигон без кампании их не выдаёт. Каталог особенностей
    // [РАБОЧЕЕ]: числа не утверждены.
    public static class SandboxPerks
    {
        // Б-04 «Основная защита» I: первая атака по носителю за раунд −1 Атака.
        public const string BasicDefense = "perk.basic_defense";

        // Б-12 «Контрудар» I: два ответных удара за раунд.
        public const string Counterstrike = "perk.counterstrike";

        // Б-12 «Контрудар» II: первый ответный удар за раунд +1 Урон.
        public const string CounterstrikeDamage = "perk.counterstrike.damage";

        // Б-13 «Упреждающий удар»: в защитной стойке ответ на ближнюю атаку —
        // до удара противника.
        public const string FirstStrike = "perk.first_strike";

        // Б-18 «Холодный глаз» I: стрелок, не двигавшийся в этот ход,
        // игнорирует 1 Защиту цели.
        public const string ColdEye = "perk.cold_eye";

        // Б-08 «Ещё на ногах»: 1/бой урон, который вывел бы из строя,
        // оставляет 1 HP (после боя — тяжёлая рана, это решает кампания).
        public const string StillStanding = "perk.still_standing";
    }
}
