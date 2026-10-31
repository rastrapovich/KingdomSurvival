using System;
using System.Collections.Generic;

public static partial class ContinuousSimulationSystem
{
    // Канон v1.59: Дома нет — в полночь только сутки похода: расход
    // припасов, смена дня и плата наёмникам.
    private static void ResolveMidnight(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        batch.ReportDay = state.Day;
        batch.EventHour = 0.0;

        ResolveExpeditionSupplyAtMidnight(state, batch.Result);
        state.Day++;
        // Бриф правок п. 10: плата наёмникам — раз за игровые сутки.
        HireService.PayDaily(state, batch.Result.Messages);
        runtime.HourOfDay = 0.0;
        ScheduleDailyChecks(state, runtime, 0.0);
        batch.StateChanged = true;
    }

    private static void ResolveExpeditionSupplyAtMidnight(
        GameState state,
        StrategicSimulationResult result)
    {
        if (!state.HasActiveExpedition)
            return;

        int requiredSupply = state.ExpeditionSupplyConsumption;
        int availableSupply = state.ArmySupply;

        if (availableSupply >= requiredSupply)
        {
            state.ArmySupply -= requiredSupply;
            state.ConsecutiveExpeditionSupplyShortageDays = 0;
            result.Messages.Add(
                "Экспедиция израсходовала суточные припасы: " +
                requiredSupply + ". Осталось: " + state.ArmySupply + ".");
            return;
        }

        // Бриф правок (19 пунктов) п. 13: голод не срывает поход и не
        // останавливает игру — одна запись за сутки, урон идёт по часам
        // (Hunger).
        state.ArmySupply = 0;
        state.ConsecutiveExpeditionSupplyShortageDays++;
        result.Messages.Add(Hunger.StarvingMessage);
    }

    private static void EnsureRouteTracking(GameState state, RuntimeState runtime)
    {
        ExpeditionData expedition =
            state != null && state.HasActiveExpedition
                ? state.ActiveExpedition
                : null;

        if (runtime.TrackedExpedition != expedition ||
            (expedition != null && runtime.TrackedRoute != expedition.Route) ||
            (expedition != null && runtime.TrackedRouteIndex != expedition.RouteIndex))
        {
            ResetRouteTracking(runtime, expedition);
        }
    }

    private static void ResetRouteTracking(
        RuntimeState runtime,
        ExpeditionData expedition)
    {
        runtime.TrackedExpedition = expedition;
        runtime.TrackedRoute = expedition != null ? expedition.Route : null;
        runtime.TrackedRouteIndex = expedition != null ? expedition.RouteIndex : 0;
        runtime.SegmentProgress = 0.0;
    }

    // 12И: остаток пути в клетках — непрерывная длина ломаной от героя.
    private static double GetRemainingCells(
        ExpeditionData expedition,
        RuntimeState runtime)
    {
        if (expedition == null || expedition.Route == null || expedition.Route.Count <= 1)
            return 0.0;

        int index = Math.Max(0, expedition.RouteIndex);
        if (index >= expedition.Route.Count - 1)
            return 0.0;

        double current = WorldMapNavigation.DistanceHexes(
            expedition.Route[index],
            expedition.Route[index + 1]) * (1.0 - runtime.SegmentProgress);
        return Math.Max(0.0, current + WorldMapNavigation.PathLengthHexes(expedition.Route, index + 1));
    }

    private static void UpdateRemainingRouteCells(
        ExpeditionData expedition,
        RuntimeState runtime)
    {
        if (expedition == null)
            return;

        double remaining = GetRemainingCells(expedition, runtime);
        expedition.RemainingRouteCells = remaining <= 0.0001
            ? 0
            : (int)Math.Ceiling(remaining - 0.0001);
    }

    private static float Lerp(float a, float b, float t) =>
        a + (b - a) * t;

    private static void PauseIfRequested(
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        if (batch.RequestAutoPause)
            runtime.IsPaused = true;
    }

}
