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

            double stepStartHour = GetAbsoluteHours(state);
            HeroMapPosition(state, out float heroFromX, out float heroFromY);
            double advancedHours = AdvanceOngoingActivities(state, runtime, stepHours, batch);
            Hunger.Advance(state, advancedHours, batch.Result.Messages);

            runtime.HourOfDay += advancedHours;
            remainingGameHours -= Math.Max(advancedHours, Epsilon);
            batch.StateChanged = true;
            batch.EventHour = runtime.HourOfDay;
            // Группы мира живут и пока отряд в месте (без контакта с ним).
            AdvancePopulation(state, stepStartHour, heroFromX, heroFromY, batch);

            if (runtime.HourOfDay >= 24.0 - Epsilon)
                ResolveMidnight(state, runtime, batch);
        }

        return batch;
    }

    private static void HeroMapPosition(GameState state, out float x, out float y)
    {
        ExpeditionData expedition = state.ActiveExpedition;
        x = expedition != null ? expedition.CurrentMapXPercent : WorldMapNavigation.CapitalXPercent;
        y = expedition != null ? expedition.CurrentMapYPercent : WorldMapNavigation.CapitalYPercent;
    }

    // Бриф «Живое население»: жизнь групп мира на тех же часах. Обязательный
    // контакт останавливает время (штатная пауза до решения игрока).
    private static void AdvancePopulation(GameState state, double stepStartHour, float heroFromX, float heroFromY, ContinuousSimulationBatch batch)
    {
        if (!state.HasGeneratedMap)
            return;
        HeroMapPosition(state, out float heroToX, out float heroToY);
        int before = batch.Result.Messages.Count;
        bool raised = WorldPopulationService.Advance(state, GetAbsoluteHours(state), heroFromX, heroFromY, heroToX, heroToY,
            stepStartHour, batch.Result.Messages);
        if (raised)
        {
            batch.RequestAutoPause = true;
            batch.Result.HadNotableOccurrence = true;
            batch.ReportDay = state.Day;
        }
        else if (batch.Result.Messages.Count > before)
            batch.ReportDay = state.Day;
    }
}
