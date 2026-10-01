using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// 12И (канон v1.50 §9): прямое управление героем в духе King's Bounty:
// The Legend. Клик левой кнопкой — бег к точке; кнопка зажата — бег за
// курсором, путь пересчитывается, пока её держат. Клик по известному
// месту — бег к нему (прибытие откроет место); клик по Дому — возвращение.
public partial class PrototypeUIController
{
    private bool worldMapHoldActive;
    private int worldMapHoldPointerId = -1;
    private Vector2 worldMapHoldPanelPosition;
    private float worldMapHoldLastRepathTime;
    private float worldMapHoldLastTargetX;
    private float worldMapHoldLastTargetY;

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
                ShowWorldMapClickMarker(location.MapXPercent, location.MapYPercent);
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

        Vector2 point = WorldMapPanelToPercent(evt.position);
        ShowWorldMapClickMarker(point.x, point.y);
        IssueContinuousMapOrder(point.x, point.y, null);

        // Кнопка зажата — дальше герой бежит за курсором.
        worldMapHoldActive = gameState.HasActiveExpedition;
        if (worldMapHoldActive)
        {
            worldMapHoldPointerId = evt.pointerId;
            worldMapHoldPanelPosition = evt.position;
            worldMapHoldLastRepathTime = Time.realtimeSinceStartup;
            worldMapHoldLastTargetX = point.x;
            worldMapHoldLastTargetY = point.y;
            worldMap.CapturePointer(evt.pointerId);
        }

        evt.StopImmediatePropagation();
    }

    private void OnContinuousMapPointerMove(PointerMoveEvent evt)
    {
        if (!worldMapHoldActive || evt.pointerId != worldMapHoldPointerId)
            return;
        worldMapHoldPanelPosition = evt.position;
    }

    private void OnContinuousMapPointerUp(PointerUpEvent evt)
    {
        if (!worldMapHoldActive || evt.pointerId != worldMapHoldPointerId)
            return;
        EndWorldMapHold();
    }

    private void OnContinuousMapPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        if (worldMapHoldActive)
            EndWorldMapHold();
    }

    private void EndWorldMapHold()
    {
        int pointerId = worldMapHoldPointerId;
        worldMapHoldActive = false;
        worldMapHoldPointerId = -1;
        if (worldMap != null && pointerId >= 0 && worldMap.HasPointerCapture(pointerId))
            worldMap.ReleasePointer(pointerId);
    }

    // Пока кнопка зажата, точка под курсором меняется и от движения мыши, и
    // от движения камеры за героем — поэтому цель пересчитывается по таймеру.
    private void TickWorldMapHold(float now, WorldMapMovementSettingsAsset settings)
    {
        if (!worldMapHoldActive || worldMap == null || gameState == null)
            return;

        if (!gameState.HasActiveExpedition || isGameOver || HasBlockingModalWork())
        {
            EndWorldMapHold();
            return;
        }

        float interval = settings != null ? settings.HoldRepathIntervalSeconds : 0.1f;
        if (now - worldMapHoldLastRepathTime < interval)
            return;

        Vector2 point = WorldMapPanelToPercent(worldMapHoldPanelPosition);
        double shift = WorldMapNavigation.DistanceHexes(
            worldMapHoldLastTargetX, worldMapHoldLastTargetY, point.x, point.y);
        float minShift = settings != null ? settings.HoldRepathMinShiftHexes : 0.3f;
        if (shift < minShift)
            return;

        worldMapHoldLastRepathTime = now;
        worldMapHoldLastTargetX = point.x;
        worldMapHoldLastTargetY = point.y;
        IssueContinuousMapOrder(point.x, point.y, null, silent: true);
    }

    private Vector2 WorldMapPanelToPercent(Vector2 panelPosition)
    {
        Vector2 local = worldMap.WorldToLocal(panelPosition);
        float width = Math.Max(1f, worldMap.resolvedStyle.width);
        float height = Math.Max(1f, worldMap.resolvedStyle.height);
        return new Vector2(
            WorldMapNavigation.ClampMapX(local.x / width * 100f),
            WorldMapNavigation.ClampMapY(local.y / height * 100f));
    }

    // silent — пересчёт пути при зажатой кнопке: без донесений и без
    // полной перерисовки интерфейса (это десять раз в секунду).
    private void IssueContinuousMapOrder(
        float targetXPercent,
        float targetYPercent,
        string locationId,
        bool silent = false)
    {
        if (gameState == null || isGameOver)
            return;

        string resultMessage;
        bool changed;
        ResumeWorldMapCameraFollow();

        if (!gameState.HasActiveExpedition)
        {
            changed = gameState.TryStartExpeditionToMapPoint(
                targetXPercent,
                targetYPercent,
                locationId,
                false,
                GetSelectedFighterIdsInArmyOrder(),
                out resultMessage,
                ExpeditionPreparation.GetRetinueId(gameState));

            if (changed)
            {
                ContinuousSimulationSystem.NotifyRouteChanged(gameState);
                AddReport("Отряд вышел из Дома.");
            }
            else if (!silent)
            {
                AddReport(NormalizeContinuousReportText(resultMessage));
            }
        }
        else
        {
            changed = gameState.TryChangeExpeditionRoute(
                targetXPercent,
                targetYPercent,
                locationId,
                out resultMessage);

            if (changed)
            {
                CommanderData commander = gameState.FindCommander(
                    gameState.ActiveExpedition.CommanderId);
                if (commander != null && !gameState.CanCancelPreparedExpedition)
                    commander.State = gameState.ActiveExpedition.Phase;

                ContinuousSimulationSystem.NotifyRouteChanged(gameState);
            }
            else if (!silent && IsMeaningfulOrderRefusal())
            {
                AddReport(NormalizeContinuousReportText(resultMessage));
            }
        }

        if (silent)
        {
            if (changed)
                RefreshContinuousTimeUi(false);
            return;
        }

        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
    }

    // Клик под ноги героя — не ошибка, а «стой где стоишь»: без донесения.
    private bool IsMeaningfulOrderRefusal() =>
        gameState.HasPendingExpeditionDecision ||
        (gameState.HasActiveExpedition && gameState.ActiveExpedition.IsLocationResearchInProgress);

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
