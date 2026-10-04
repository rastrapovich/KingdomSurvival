using System;

// ПР-12К (канон v1.53 §28.3): время внутри исследуемого места. Хозяин часов
// прежний — ContinuousSimulationSystem; отдельного таймера суток нет. Пока
// отряд в месте, мировые часы стоят (UI держит паузу), а время тратится
// явно: шаг командира по клетке и значимое действие — каждое своими
// игровыми часами (LocalLocationDefinition). Дом, полночь и уход
// пересчитываются от тех же прошедших часов. Дорожные случайные проверки
// внутри места не идут.
public static partial class ContinuousSimulationSystem
{
    public static ContinuousSimulationBatch AdvanceLocalHours(GameState state, double gameHours)
    {
        ContinuousSimulationBatch batch = new ContinuousSimulationBatch
        {
            ReportDay = state != null ? state.Day : 0
        };
        if (state == null || gameHours <= Epsilon)
            return batch;

        RuntimeState runtime = GetRuntime(state);
        double remainingGameHours = gameHours;
        while (remainingGameHours > Epsilon)
        {
            double hoursToMidnight = 24.0 - runtime.HourOfDay;
            double stepHours = Math.Min(Math.Min(remainingGameHours, MaxHomeSubstepHours), hoursToMidnight);
            if (stepHours <= Epsilon)
            {
                ResolveMidnight(state, runtime, batch);
                continue;
            }

            double advancedHours = AdvanceOngoingActivities(state, runtime, stepHours, batch);
            HomeLife.Advance(state, advancedHours, batch.Result.Messages);

            runtime.HourOfDay += advancedHours;
            remainingGameHours -= Math.Max(advancedHours, Epsilon);
            batch.StateChanged = true;
            batch.EventHour = runtime.HourOfDay;

            if (runtime.HourOfDay >= 24.0 - Epsilon)
                ResolveMidnight(state, runtime, batch);
        }

        return batch;
    }
}
