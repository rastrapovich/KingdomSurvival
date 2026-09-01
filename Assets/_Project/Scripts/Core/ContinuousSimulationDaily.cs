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

        state.Gold += state.DailyGoldIncome;
        state.Food += state.DailyFoodIncome;
        batch.Result.Messages.Add(
            "Полночь. Казна получила " + state.DailyGoldIncome +
            " золота, город получил " + state.DailyFoodIncome + " пищи.");

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
                result.Messages.Add("Нехватка городской пищи прекратилась.");
                result.HadNotableOccurrence = true;
            }

            state.ConsecutiveFoodShortageDays = 0;
            result.Messages.Add(
                "Город израсходовал " + requiredFood +
                " пищи для " + state.Population + " жителей.");
            return;
        }

        int shortage = requiredFood - availableFood;
        state.Food = 0;
        state.ConsecutiveFoodShortageDays++;
        result.HadNotableOccurrence = true;
        result.Messages.Add(
            "Городу не хватило " + shortage +
            " пищи. Нехватка подряд: " +
            state.ConsecutiveFoodShortageDays + " сут." );

        if (state.ConsecutiveFoodShortageDays <= MoodOnlyShortageDays)
        {
            state.Mood = Math.Max(0, state.Mood - MoodLossPerShortageDay);
            result.Messages.Add(
                "Настроение снизилось на " + MoodLossPerShortageDay + ".");
        }
        else
        {
            int before = state.Population;
            state.Population = Math.Max(
                0,
                state.Population - PopulationLossPerStarvationDay);
            result.Messages.Add(
                "Голод затянулся: население уменьшилось на " +
                (before - state.Population) + ".");
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
                "Экспедиция израсходовала суточное снабжение: " +
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
                " снабжения на суточный расход. Следующая такая полночь " +
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
                "Снабжение снова закончилось, но путь вынужденного возврата " +
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
                " воинов прибыли в столицу.",
            Consequence =
                "В столицу передано: золото +" + snapshot.ArmyGold +
                ", пища +" + snapshot.ArmySupply + "."
        };
        batch.RequestAutoPause = true;
        batch.Result.HadNotableOccurrence = true;
    }
}
