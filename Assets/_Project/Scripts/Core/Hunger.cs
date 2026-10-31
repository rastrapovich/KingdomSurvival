using System;
using System.Collections.Generic;

// Бриф правок (19 пунктов) п. 13: в свободной игре нулевые припасы не
// останавливают поход и не требуют «Понятно». Пока припасов нет, герой
// постепенно теряет HP — по игровым часам (не реальным секундам и не кадрам):
// на паузе урона нет, на ×2/×3 тот же урон за те же часы, переход суток и
// долгие действия идут теми же часами. С пополнением припасов урон
// прекращается. Спутники не голодают (в диктовке — о герое). Числа — рабочие
// (не утверждены); минимальный HP — 1: исход поражения не меняется.
public static class Hunger
{
    public const double DefaultHoursPerHitPoint = 6.0;
    public const int DefaultMinimumHitPoints = 1;

    public static double HoursPerHitPoint { get; set; } = DefaultHoursPerHitPoint;
    public static int MinimumHitPoints { get; set; } = DefaultMinimumHitPoints;

    public const string StarvingMessage = "Припасы кончились — отряд идёт впроголодь, герой слабеет.";

    public static bool Applies(GameState state) =>
        state != null && state.HasActiveExpedition;

    public static bool IsStarving(GameState state) => Applies(state) && state.ArmySupply <= 0;

    // Прошедшие игровые часы: накопленный голод → потеря HP героя.
    public static void Advance(GameState state, double gameHours, List<string> messages)
    {
        if (gameHours <= 0.0 || state == null)
            return;
        if (!IsStarving(state))
        {
            state.HungerHours = 0.0;
            return;
        }
        CommanderData commander = state.GetSelectedCommander();
        ResidentState hero = commander != null ? PeopleService.Find(state, commander.Id) : null;
        if (hero == null)
            return;
        PeopleService.EnsureCombatState(hero);

        state.HungerHours += gameHours;
        double period = Math.Max(0.1, HoursPerHitPoint);
        int lost = 0;
        while (state.HungerHours >= period)
        {
            state.HungerHours -= period;
            if (hero.CurrentHitPoints > MinimumHitPoints)
            {
                hero.CurrentHitPoints--;
                lost++;
            }
        }
        if (lost > 0 && messages != null)
            messages.Add("Голод: герой теряет силы (−" + lost + " HP, осталось " + hero.CurrentHitPoints + ").");
    }
}
