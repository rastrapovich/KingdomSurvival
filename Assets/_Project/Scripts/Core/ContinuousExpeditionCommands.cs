using System;
using System.Collections.Generic;

public static class ContinuousExpeditionCommands
{
    public static bool TryOrderReturn(GameState state, out string resultMessage)
    {
        resultMessage = "Не удалось отдать приказ о возвращении.";

        if (state == null || !state.HasActiveExpedition)
        {
            resultMessage = "Сейчас нет активной экспедиции.";
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;

        if (state.HasPendingExpeditionDecision)
        {
            resultMessage = "Сначала требуется принять обязательное решение.";
            return false;
        }

        if (expedition.IsLocationResearchInProgress)
        {
            resultMessage = "Нельзя возвращаться во время начатого исследования.";
            return false;
        }

        if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            resultMessage = "Экспедиция уже возвращается в столицу.";
            return false;
        }

        CommanderData commander = state.FindCommander(expedition.CommanderId);
        if (commander == null)
        {
            resultMessage = "Не удалось определить командира экспедиции.";
            return false;
        }

        WorldMapNavigation.ConfigureTerrain(state.WorldSeed);
        float exactStartX = expedition.CurrentMapXPercent;
        float exactStartY = expedition.CurrentMapYPercent;
        List<MapPointData> route = WorldMapNavigation.FindPath(
            exactStartX,
            exactStartY,
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent);

        if (route.Count > 0)
        {
            route[0].XPercent = exactStartX;
            route[0].YPercent = exactStartY;
        }

        if (route.Count < 2)
        {
            string delivered = state.CompleteExpeditionReturn();
            resultMessage = "Армия уже у столицы. " + delivered;
            ContinuousSimulationSystem.NotifyRouteChanged(state);
            return true;
        }

        expedition.Phase = CommanderState.ReturningToCastle;
        expedition.ActiveActivity = null;
        expedition.Route = route;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0;
        expedition.RemainingRouteCells = Math.Max(1, route.Count - 1);
        expedition.RouteLengthCells = expedition.RemainingRouteCells;
        expedition.TargetMapXPercent = WorldMapNavigation.CapitalXPercent;
        expedition.TargetMapYPercent = WorldMapNavigation.CapitalYPercent;
        expedition.HasInterruptedRoute = false;
        expedition.LastTravelPoints.Clear();
        commander.State = CommanderState.ReturningToCastle;

        ContinuousSimulationSystem.NotifyRouteChanged(state);
        double hours = ContinuousSimulationSystem.GetTravelHoursRemaining(state);
        resultMessage =
            commander.Name + " получил приказ возвращаться по прямому маршруту. " +
            "Расчётное время пути с учётом рельефа: " + FormatHours(hours) + ".";
        return true;
    }

    public static string FormatHours(double hours)
    {
        if (hours <= 0.01)
            return "меньше часа";

        if (hours < 24.0)
            return Math.Max(1, (int)Math.Ceiling(hours)) + " ч.";

        int days = (int)Math.Floor(hours / 24.0);
        int remainder = (int)Math.Ceiling(hours - days * 24.0);
        return remainder > 0
            ? days + " сут. " + remainder + " ч."
            : days + " сут.";
    }
}
