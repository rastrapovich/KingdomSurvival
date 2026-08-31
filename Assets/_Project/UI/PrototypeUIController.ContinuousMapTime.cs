using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private void OnContinuousMapPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || isGameOver || gameState == null)
            return;

        VisualElement target = evt.target as VisualElement;
        VisualElement node = FindAncestorWithClass(target, "world-map-node");
        VisualElement capital = FindAncestorByName(
            target,
            "world-map-capital-button");

        if (capital != null)
            return;

        if (node != null)
        {
            string prefix = "world-map-node-";
            string id = node.name != null && node.name.StartsWith(prefix)
                ? node.name.Substring(prefix.Length)
                : null;
            LocationData location = gameState.FindLocation(id);

            if (location != null && location.IsVisibleOnMap && !location.IsWaypoint)
            {
                IssueContinuousMapOrder(
                    location.MapXPercent,
                    location.MapYPercent,
                    location.Id);
                evt.StopImmediatePropagation();
            }

            return;
        }

        if (target != worldMap &&
            target != worldMapTerrain &&
            target != worldMapRoutes &&
            target != worldMapMarkers &&
            !IsWorldMapGridElement(target))
        {
            return;
        }

        Vector2 local = worldMap.WorldToLocal(evt.position);
        float width = Math.Max(1f, worldMap.resolvedStyle.width);
        float height = Math.Max(1f, worldMap.resolvedStyle.height);
        float xPercent = WorldMapNavigation.ClampMapX(local.x / width * 100f);
        float yPercent = WorldMapNavigation.ClampMapY(local.y / height * 100f);

        IssueContinuousMapOrder(xPercent, yPercent, null);
        evt.StopImmediatePropagation();
    }

    private void IssueContinuousMapOrder(
        float targetXPercent,
        float targetYPercent,
        string locationId)
    {
        if (gameState == null || isGameOver)
            return;

        string ignoredMessage;
        bool changed;

        if (!gameState.HasActiveExpedition)
        {
            if (selectedFighterIds.Count == 0)
            {
                AddReport("Сначала перенесите бойцов в гарнизон командира.");
                return;
            }

            changed = gameState.TryStartExpeditionToMapPoint(
                targetXPercent,
                targetYPercent,
                locationId,
                false,
                GetSelectedFighterIdsInArmyOrder(),
                out ignoredMessage);

            if (changed)
            {
                ContinuousSimulationSystem.NotifyRouteChanged(gameState);
                AddReport(
                    "Приказ на экспедицию отдан. Армия начнёт движение " +
                    "сразу после снятия паузы.");
            }
            else
            {
                AddReport(NormalizeContinuousReportText(ignoredMessage));
            }
        }
        else
        {
            float exactStartX = gameState.ActiveExpedition.CurrentMapXPercent;
            float exactStartY = gameState.ActiveExpedition.CurrentMapYPercent;
            changed = gameState.TryChangeExpeditionRoute(
                targetXPercent,
                targetYPercent,
                locationId,
                out ignoredMessage);

            if (changed)
            {
                if (gameState.ActiveExpedition.Route != null &&
                    gameState.ActiveExpedition.Route.Count > 0)
                {
                    gameState.ActiveExpedition.Route[0].XPercent = exactStartX;
                    gameState.ActiveExpedition.Route[0].YPercent = exactStartY;
                    gameState.ActiveExpedition.CurrentMapXPercent = exactStartX;
                    gameState.ActiveExpedition.CurrentMapYPercent = exactStartY;
                }

                CommanderData commander = gameState.FindCommander(
                    gameState.ActiveExpedition.CommanderId);
                if (commander != null)
                    commander.State = gameState.ActiveExpedition.Phase;

                ContinuousSimulationSystem.NotifyRouteChanged(gameState);
                AddReport(
                    "Маршрут изменён. Новый путь построен от текущей позиции армии.");
            }
            else
            {
                AddReport(NormalizeContinuousReportText(ignoredMessage));
            }
        }

        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
    }

    private static VisualElement FindAncestorWithClass(
        VisualElement element,
        string className)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (current.ClassListContains(className))
                return current;
            current = current.parent;
        }
        return null;
    }

    private static VisualElement FindAncestorByName(
        VisualElement element,
        string name)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (current.name == name)
                return current;
            current = current.parent;
        }
        return null;
    }

    private static bool IsWorldMapGridElement(VisualElement element)
    {
        if (element == null)
            return false;

        VisualElement current = element;
        while (current != null)
        {
            if (current.name == "world-map-grid-overlay")
                return true;
            current = current.parent;
        }
        return false;
    }

}
