using System;
using System.Collections.Generic;

public static partial class ContinuousSimulationSystem
{
    private static void ResolveMidnight(
        GameState state,
        RuntimeState runtime,
        ContinuousSimulationBatch batch)
    {
        int finishedDay = state.Day;
        batch.ReportDay = finishedDay;
        batch.EventHour = 0.0;

        BuildingSystem.Synchronize(state);
        int dailyGoldIncome = BuildingSystem.GetDailyGoldIncome(state);
        int dailyFoodIncome = BuildingSystem.GetDailyFoodIncome(state);
        int dailyGoldUpkeep = BuildingSystem.GetDailyGoldUpkeep(state);
        // ПР-07Б: заработанный за сутки улов — ровно один раз, до расхода.
        int fishingCatch = HomeLife.TakeFishingCatch(state);

        state.Gold = Math.Max(
            0,
            state.Gold + dailyGoldIncome - dailyGoldUpkeep);
        state.Food += dailyFoodIncome + fishingCatch;

        // ПР-07Б: пока отряд в пути, будничный отчёт Дома не приходит —
        // это не сведения, которые герой может знать.
        if (!HomePeopleService.HasDeparted(state))
        {
            batch.Result.Messages.Add(
                "Полночь. В казну Дома поступило " + dailyGoldIncome + " золота" +
                (dailyGoldUpkeep > 0 ? ", на содержание ушло " + dailyGoldUpkeep : "") +
                ". В запасы Дома поступило " + dailyFoodIncome + " пищи" +
                (fishingCatch > 0 ? " и " + fishingCatch + " — улов Тихона" : "") + ".");
        }

        ResolveCityFoodAtMidnight(state, batch.Result);
        ResolveExpeditionSupplyAtMidnight(state, runtime, batch.Result);

        state.Day++;
        runtime.HourOfDay = 0.0;
        ScheduleDailyChecks(state, runtime, 0.0);
        batch.StateChanged = true;
    }

    private static void ResolveCityFoodAtMidnight(
        GameState state,
        StrategicSimulationResult result)
    {
        int requiredFood = state.DailyFoodConsumption;
        int availableFood = state.Food;

        if (availableFood >= requiredFood)
        {
            state.Food -= requiredFood;

            if (state.ConsecutiveFoodShortageDays > 0)
            {
                HomeKnowledge.Report(state, result.Messages, "Нехватка еды в Доме прекратилась.");
                result.HadNotableOccurrence |= !HomePeopleService.HasDeparted(state);
            }

            state.ConsecutiveFoodShortageDays = 0;
            if (!HomePeopleService.HasDeparted(state))
            {
                result.Messages.Add(
                    "Дом израсходовал " + requiredFood +
                    " пищи — по одной на каждого, кто сейчас дома.");
            }
            return;
        }

        int shortage = requiredFood - availableFood;
        state.Food = 0;
        state.ConsecutiveFoodShortageDays++;
        result.HadNotableOccurrence |= !HomePeopleService.HasDeparted(state);
        HomeKnowledge.Report(state, result.Messages,
            "Дому не хватило " + shortage +
            " пищи. Нехватка подряд: " +
            state.ConsecutiveFoodShortageDays + " сут.");

        // ПР-06А: голод не стирает безымянных жителей — у Дома конкретные
        // люди; смерть — только авторским событием. ПР-07А-1: настроения
        // больше нет — голод означает нехватку и остановку ухода
        // (HomeFunctionResolver) с той же полуночи, что и в сообщении.
        if (state.ConsecutiveFoodShortageDays >= HomeFunctionResolver.CareStopsAfterShortageDays)
        {
            HomeKnowledge.Report(state, result.Messages,
                "Голод затянулся: люди слабеют, уход за ранеными остановлен до появления еды.");
        }
    }

    private static void ResolveExpeditionSupplyAtMidnight(
        GameState state,
        RuntimeState runtime,
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

        int shortage = requiredSupply - availableSupply;
        state.ArmySupply = 0;
        state.ConsecutiveExpeditionSupplyShortageDays++;
        result.HadNotableOccurrence = true;

        if (state.ConsecutiveExpeditionSupplyShortageDays == 1)
        {
            result.Messages.Add(
                "Армии не хватило " + shortage +
                " припасов на суточный расход. Следующая такая полночь " +
                "сорвёт поход.");
            return;
        }

        float exactStartX = state.ActiveExpedition.CurrentMapXPercent;
        float exactStartY = state.ActiveExpedition.CurrentMapYPercent;
        string returnMessage;
        if (state.ForceReturnFromSupplyFailure(out returnMessage))
        {
            if (state.HasActiveExpedition &&
                state.ActiveExpedition.Route != null &&
                state.ActiveExpedition.Route.Count > 0)
            {
                state.ActiveExpedition.Route[0].XPercent = exactStartX;
                state.ActiveExpedition.Route[0].YPercent = exactStartY;
                state.ActiveExpedition.CurrentMapXPercent = exactStartX;
                state.ActiveExpedition.CurrentMapYPercent = exactStartY;
            }

            NotifyRouteChanged(state);
            result.Messages.Add(returnMessage);
        }
        else
        {
            result.Messages.Add(
                "Припасы снова закончились, но путь вынужденного возврата " +
                "не удалось построить.");
        }
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

    private static double GetRemainingCells(
        ExpeditionData expedition,
        RuntimeState runtime)
    {
        if (expedition == null || expedition.Route == null || expedition.Route.Count <= 1)
            return 0.0;

        int fullSegments = Math.Max(
            0,
            expedition.Route.Count - 1 - expedition.RouteIndex);
        return Math.Max(0.0, fullSegments - runtime.SegmentProgress);
    }

    private static void UpdateRemainingRouteCells(
        ExpeditionData expedition,
        RuntimeState runtime)
    {
        if (expedition == null)
            return;

        expedition.RemainingRouteCells =
            (int)Math.Ceiling(GetRemainingCells(expedition, runtime));
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

    private sealed class ExpeditionReturnSnapshotData
    {
        public string CommanderName;
        public int FighterCount;
        public int ArmyGold;
        public int ArmySupply;
    }

    private static ExpeditionReturnSnapshotData CaptureReturnSnapshot(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return null;

        ExpeditionData expedition = state.ActiveExpedition;
        CommanderData commander = state.FindCommander(expedition.CommanderId);
        return new ExpeditionReturnSnapshotData
        {
            CommanderName = commander != null ? commander.Name : "Командир",
            FighterCount = expedition.FighterIds.Count,
            ArmyGold = state.ArmyGold,
            ArmySupply = state.ArmySupply
        };
    }

    private static void AddReturnNoticeIfNeeded(
        GameState state,
        bool hadExpedition,
        ExpeditionReturnSnapshotData snapshot,
        ContinuousSimulationBatch batch)
    {
        if (!hadExpedition || snapshot == null || state.HasActiveExpedition)
            return;

        batch.Result.ExpeditionReturnNotice = new StrategicModalNotice
        {
            Title = "ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ",
            Description =
                snapshot.CommanderName + " и " + snapshot.FighterCount +
                " воинов вернулись в Дом.",
            Consequence =
                "В Дом передано: золото +" + snapshot.ArmyGold +
                ", пища +" + snapshot.ArmySupply + "."
        };
        batch.RequestAutoPause = true;
        batch.Result.HadNotableOccurrence = true;
    }
}
