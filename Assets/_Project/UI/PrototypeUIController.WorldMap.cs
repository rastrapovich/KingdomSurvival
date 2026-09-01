using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private VisualElement worldMap;
    private VisualElement worldMapTerrain;
    private VisualElement worldMapRoutes;
    private VisualElement worldMapMarkers;
    private Button worldMapCapitalButton;
    private VisualElement worldMapArmyMarker;
    private Label worldMapArmyMarkerLabel;
    private VisualElement worldMapArmyActivity;
    private VisualElement worldMapArmyActivityFill;
    private Label worldMapArmyActivityLabel;
    private VisualElement worldMapLocationActivity;
    private VisualElement worldMapLocationActivityFill;
    private Label worldMapLocationActivityLabel;
    private Label worldMapHintLabel;
    private VisualElement mapSelectionCard;
    private Label mapSelectionTitle;
    private Label mapSelectionDetails;
    private Button mapSendButton;

    // Эти поля оставлены для совместимости со старым прототипным UI.
    // Подтверждение цели больше не используется: клик сразу отдаёт приказ.
    private string selectedMapLocationId;
    private bool hasSelectedMapPoint;
    private float selectedMapXPercent;
    private float selectedMapYPercent;
    private readonly List<MapPointData> selectedMapRoute =
        new List<MapPointData>();

    private void FindWorldMapElements(VisualElement root)
    {
        worldMap = root.Q<VisualElement>("world-map");
        worldMapTerrain = root.Q<VisualElement>("world-map-terrain");
        worldMapRoutes = root.Q<VisualElement>("world-map-routes");
        worldMapMarkers = root.Q<VisualElement>("world-map-markers");
        worldMapCapitalButton = root.Q<Button>("world-map-capital-button");
        worldMapArmyMarker = root.Q<VisualElement>("world-map-army-marker");
        worldMapArmyMarkerLabel =
            root.Q<Label>("world-map-army-marker-label");
        worldMapHintLabel = root.Q<Label>("world-map-hint-label");
        mapSelectionCard = root.Q<VisualElement>("map-selection-card");
        mapSelectionTitle = root.Q<Label>("map-selection-title");
        mapSelectionDetails = root.Q<Label>("map-selection-details");
        mapSendButton = root.Q<Button>("map-send-button");

        if (worldMapTerrain != null)
            worldMapTerrain.pickingMode = PickingMode.Ignore;

        if (worldMapRoutes != null)
            worldMapRoutes.pickingMode = PickingMode.Ignore;

        if (worldMapArmyMarker != null)
            worldMapArmyMarker.pickingMode = PickingMode.Ignore;

        ConfigureWorldMapFullscreenLayout();
    }

    private void ConfigureWorldMapFullscreenLayout()
    {
        if (worldMap == null || expeditionsScreen == null)
            return;

        // Старый ScrollView содержал заголовки, статус, подсказку и карточку
        // "Разведка сектора". На экране карты они больше не нужны.
        ScrollView legacyScroll =
            expeditionsScreen.Q<ScrollView>();

        if (legacyScroll != null)
            legacyScroll.style.display = DisplayStyle.None;

        worldMap.RemoveFromHierarchy();
        expeditionsScreen.Add(worldMap);

        expeditionsScreen.style.flexGrow = 1f;
        expeditionsScreen.style.minHeight = 0f;
        expeditionsScreen.style.paddingLeft = 0f;
        expeditionsScreen.style.paddingRight = 0f;
        expeditionsScreen.style.paddingTop = 0f;
        expeditionsScreen.style.paddingBottom = 0f;

        worldMap.style.flexGrow = 1f;
        worldMap.style.flexShrink = 1f;
        worldMap.style.width = Length.Percent(100);
        worldMap.style.height = StyleKeyword.Auto;
        worldMap.style.minHeight = 0f;
        worldMap.style.marginLeft = 0f;
        worldMap.style.marginRight = 0f;
        worldMap.style.marginTop = 0f;
        worldMap.style.marginBottom = 0f;

        if (worldMapHintLabel != null)
            worldMapHintLabel.style.display = DisplayStyle.None;

        if (mapSelectionCard != null)
            mapSelectionCard.style.display = DisplayStyle.None;
    }

    private bool WorldMapElementsExist() =>
        worldMap != null &&
        worldMapTerrain != null &&
        worldMapRoutes != null &&
        worldMapMarkers != null &&
        worldMapCapitalButton != null &&
        worldMapArmyMarker != null &&
        worldMapArmyMarkerLabel != null;

    private void RegisterWorldMapCallbacks()
    {
        worldMap.RegisterCallback<PointerDownEvent>(
            OnWorldMapPointerDown);
        worldMapCapitalButton.clicked +=
            OnWorldMapCapitalClicked;
    }

    private void UnregisterWorldMapCallbacks()
    {
        worldMap.UnregisterCallback<PointerDownEvent>(
            OnWorldMapPointerDown);
        worldMapCapitalButton.clicked -=
            OnWorldMapCapitalClicked;
    }

    private void ResetWorldMapSelection()
    {
        selectedMapLocationId = null;
        hasSelectedMapPoint = false;
        selectedMapRoute.Clear();
    }

    private void OnWorldMapPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || isGameOver)
            return;

        VisualElement clicked =
            evt.target as VisualElement;

        // Кнопки столицы и найденных локаций обрабатывают собственный клик.
        if (clicked != worldMap &&
            clicked != worldMapTerrain &&
            clicked != worldMapRoutes &&
            clicked != worldMapMarkers)
        {
            return;
        }

        Vector2 local =
            worldMap.WorldToLocal(evt.position);
        float width =
            Math.Max(1f, worldMap.resolvedStyle.width);
        float height =
            Math.Max(1f, worldMap.resolvedStyle.height);

        float xPercent =
            WorldMapNavigation.ClampMapX(
                local.x / width * 100f);
        float yPercent =
            WorldMapNavigation.ClampMapY(
                local.y / height * 100f);

        IssueImmediateMapOrder(
            xPercent,
            yPercent,
            null);

        evt.StopPropagation();
    }

    private void SelectWorldMapLocation(string locationId)
    {
        LocationData location =
            gameState.FindLocation(locationId);

        if (location == null ||
            location.IsWaypoint ||
            !location.IsVisibleOnMap)
        {
            return;
        }

        IssueImmediateMapOrder(
            location.MapXPercent,
            location.MapYPercent,
            location.Id);
    }

    private void IssueImmediateMapOrder(
        float targetXPercent,
        float targetYPercent,
        string locationId)
    {
        if (gameState == null || isGameOver)
            return;

        string resultMessage;
        bool changed;

        if (!gameState.HasActiveExpedition)
        {
            if (selectedFighterIds.Count == 0)
            {
                AddReport(
                    "Сначала перенесите бойцов в гарнизон командира.");
                return;
            }

            changed =
                gameState.TryStartExpeditionToMapPoint(
                    targetXPercent,
                    targetYPercent,
                    locationId,
                    false,
                    new List<string>(selectedFighterIds),
                    out resultMessage);

            if (changed)
            {
                CommanderData commander =
                    gameState.FindCommander(
                        gameState.ActiveExpedition.CommanderId);

                // Приказ отдан, но мир ещё не сделал следующий ход.
                if (commander != null)
                    commander.State = CommanderState.InCastle;
            }
        }
        else
        {
            changed =
                gameState.TryChangeExpeditionRoute(
                    targetXPercent,
                    targetYPercent,
                    locationId,
                    out resultMessage);
        }

        AddReport(resultMessage);

        if (changed)
            ResetWorldMapSelection();

        RefreshStableUiAfterStateChange();
    }

    // Старый подтверждающий обработчик больше не используется.
    private void OnWorldMapSendClicked()
    {
    }

    private void OnWorldMapCapitalClicked()
    {
        if (!gameState.HasActiveExpedition ||
            gameState.HasPendingExpeditionDecision ||
            gameState.ActiveExpedition.IsLocationResearchInProgress)
        {
            return;
        }

        string resultMessage;

        if (gameState.CanCancelPreparedExpedition)
        {
            bool cancelled =
                gameState.TryCancelPreparedExpedition(
                    out resultMessage);

            if (cancelled)
                selectedFighterIds.Clear();
        }
        else
        {
            gameState.TryOrderReturn(
                out resultMessage);
        }

        AddReport(resultMessage);
        RefreshStableUiAfterStateChange();
    }

    private void RefreshWorldMapPanel()
    {
        if (gameState == null ||
            !WorldMapElementsExist())
        {
            return;
        }

        ConfigureWorldMapFullscreenLayout();

        worldMapTerrain.Clear();
        worldMapRoutes.Clear();
        worldMapMarkers.Clear();

        DrawBlockedTerrain();

        foreach (LocationData location in gameState.Locations)
        {
            if (location.IsWaypoint ||
                !location.IsVisibleOnMap)
            {
                continue;
            }

            CreateWorldMapNode(location);
        }

        if (gameState.HasActiveExpedition)
        {
            DrawRoute(
                gameState.ActiveExpedition.Route,
                "world-map-route-dot-active");
        }

        RefreshWorldMapCapital();
        RefreshWorldMapArmyMarker();
    }

    private void DrawBlockedTerrain()
    {
        for (int y = 0;
             y < WorldMapNavigation.GridHeight;
             y++)
        {
            for (int x = 0;
                 x < WorldMapNavigation.GridWidth;
                 x++)
            {
                float px =
                    x * 100f /
                    (WorldMapNavigation.GridWidth - 1);
                float py =
                    y * 100f /
                    (WorldMapNavigation.GridHeight - 1);

                if (!WorldMapNavigation.IsBlockedPercent(px, py))
                    continue;

                VisualElement cell =
                    new VisualElement();

                cell.AddToClassList(
                    "world-map-blocked-cell");

                cell.style.left =
                    new Length(
                        px,
                        LengthUnit.Percent);
                cell.style.top =
                    new Length(
                        py,
                        LengthUnit.Percent);

                worldMapTerrain.Add(cell);
            }
        }
    }

    private void DrawRoute(
        List<MapPointData> route,
        string extraClass)
    {
        if (route == null)
            return;

        int firstVisibleIndex = 1;

        if (gameState.HasActiveExpedition &&
            route == gameState.ActiveExpedition.Route)
        {
            firstVisibleIndex =
                Math.Max(
                    1,
                    gameState.ActiveExpedition.RouteIndex + 1);
        }

        for (int i = firstVisibleIndex;
             i < route.Count;
             i++)
        {
            VisualElement dot =
                new VisualElement();

            dot.AddToClassList(
                "world-map-route-dot");
            dot.AddToClassList(extraClass);

            dot.style.left =
                new Length(
                    route[i].XPercent,
                    LengthUnit.Percent);
            dot.style.top =
                new Length(
                    route[i].YPercent,
                    LengthUnit.Percent);

            worldMapRoutes.Add(dot);
        }
    }

    private void CreateWorldMapNode(
        LocationData location)
    {
        string id = location.Id;

        Button node =
            new Button(
                () => SelectWorldMapLocation(id));

        node.name =
            "world-map-node-" + id;
        node.text =
            location.IsExplored ? "✓" : "●";
        node.tooltip =
            location.Name + "\n" +
            location.RegionName + "\nУгроза: " +
            location.Threat;

        node.AddToClassList("world-map-node");
        node.AddToClassList(
            "world-map-node-known");

        node.style.left =
            new Length(
                location.MapXPercent,
                LengthUnit.Percent);
        node.style.top =
            new Length(
                location.MapYPercent,
                LengthUnit.Percent);

        if (location.IsExplored)
        {
            node.AddToClassList(
                "world-map-node-explored");
        }

        if (gameState.HasActiveExpedition &&
            location.Id ==
            gameState.ActiveExpedition.LocationId)
        {
            node.AddToClassList(
                "world-map-node-active");
        }

        bool canChangeRoute =
            !isGameOver &&
            (!gameState.HasActiveExpedition ||
             (!gameState.HasPendingExpeditionDecision &&
              !gameState.ActiveExpedition.IsLocationResearchInProgress));

        node.SetEnabled(canChangeRoute);
        worldMapMarkers.Add(node);
    }

    private void CreateDestinationMarker()
    {
        // Предварительной цели больше нет: клик сразу перестраивает маршрут.
    }

    private void RefreshWorldMapHint()
    {
        // Текстовые инструкции с экрана карты удалены.
    }

    private void RefreshWorldMapCapital()
    {
        bool active =
            gameState.HasActiveExpedition;

        worldMapCapitalButton.RemoveFromClassList(
            "world-map-capital-return");

        if (active)
        {
            worldMapCapitalButton.AddToClassList(
                "world-map-capital-return");
        }

        worldMapCapitalButton.text =
            active
                ? "СТОЛИЦА"
                : "СТОЛИЦА";

        bool canUseCapital =
            active &&
            !gameState.HasPendingExpeditionDecision &&
            !gameState.ActiveExpedition.IsLocationResearchInProgress &&
            gameState.ActiveExpedition.Phase !=
                CommanderState.ReturningToCastle;

        worldMapCapitalButton.SetEnabled(
            canUseCapital);
    }

    private void RefreshWorldMapArmyMarker()
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapArmyMarker.style.display =
                DisplayStyle.None;
            HideWorldMapActivityProgress();
            return;
        }

        ExpeditionData expedition =
            gameState.ActiveExpedition;

        worldMapArmyMarker.style.display =
            DisplayStyle.Flex;
        worldMapArmyMarker.style.left =
            new Length(
                expedition.CurrentMapXPercent,
                LengthUnit.Percent);
        worldMapArmyMarker.style.top =
            new Length(
                expedition.CurrentMapYPercent,
                LengthUnit.Percent);

        worldMapArmyMarkerLabel.text =
            expedition.RemainingRouteCells > 0
                ? ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState))
                : "на месте";

        RefreshWorldMapActivityProgress(expedition);
    }

    private void RefreshWorldMapActivityProgress(ExpeditionData expedition)
    {
        ExpeditionActivityData activity = expedition.ActiveActivity;

        if (activity == null)
        {
            HideWorldMapActivityProgress();
            return;
        }

        if (activity.Kind == ExpeditionActivityKind.RoadStop)
        {
            EnsureWorldMapArmyActivity();
            worldMapArmyActivity.style.display = DisplayStyle.Flex;
            UpdateWorldMapActivity(
                worldMapArmyActivityFill,
                worldMapArmyActivityLabel,
                activity);

            if (worldMapLocationActivity != null)
                worldMapLocationActivity.style.display = DisplayStyle.None;
            return;
        }

        LocationData location = gameState.FindLocation(activity.LocationId);
        if (location == null || worldMapMarkers == null)
        {
            HideWorldMapActivityProgress();
            return;
        }

        EnsureWorldMapLocationActivity();
        worldMapLocationActivity.style.display = DisplayStyle.Flex;
        worldMapLocationActivity.style.left = new Length(
            location.MapXPercent,
            LengthUnit.Percent);
        worldMapLocationActivity.style.top = new Length(
            location.MapYPercent,
            LengthUnit.Percent);
        UpdateWorldMapActivity(
            worldMapLocationActivityFill,
            worldMapLocationActivityLabel,
            activity);

        if (worldMapArmyActivity != null)
            worldMapArmyActivity.style.display = DisplayStyle.None;
    }

    private void EnsureWorldMapArmyActivity()
    {
        if (worldMapArmyActivity != null &&
            worldMapArmyActivity.parent == worldMapArmyMarker)
        {
            return;
        }

        CreateWorldMapActivityElements(
            out worldMapArmyActivity,
            out worldMapArmyActivityFill,
            out worldMapArmyActivityLabel);
        worldMapArmyActivity.AddToClassList("world-map-army-activity");
        worldMapArmyMarker.Add(worldMapArmyActivity);
    }

    private void EnsureWorldMapLocationActivity()
    {
        if (worldMapLocationActivity != null &&
            worldMapLocationActivity.parent == worldMapMarkers)
        {
            return;
        }

        CreateWorldMapActivityElements(
            out worldMapLocationActivity,
            out worldMapLocationActivityFill,
            out worldMapLocationActivityLabel);
        worldMapLocationActivity.AddToClassList("world-map-location-activity");
        worldMapMarkers.Add(worldMapLocationActivity);
    }

    private static void CreateWorldMapActivityElements(
        out VisualElement container,
        out VisualElement fill,
        out Label label)
    {
        container = new VisualElement();
        container.AddToClassList("world-map-activity");
        container.pickingMode = PickingMode.Ignore;

        label = new Label();
        label.AddToClassList("world-map-activity-label");
        label.pickingMode = PickingMode.Ignore;

        VisualElement track = new VisualElement();
        track.AddToClassList("world-map-activity-track");
        track.pickingMode = PickingMode.Ignore;

        fill = new VisualElement();
        fill.AddToClassList("world-map-activity-fill");
        fill.pickingMode = PickingMode.Ignore;

        track.Add(fill);
        container.Add(label);
        container.Add(track);
    }

    private static void UpdateWorldMapActivity(
        VisualElement fill,
        Label label,
        ExpeditionActivityData activity)
    {
        fill.style.width = new Length(
            (float)(activity.Progress01 * 100.0),
            LengthUnit.Percent);
        label.text =
            activity.DisplayName + " · " +
            ContinuousExpeditionCommands.FormatHours(activity.RemainingHours);
    }

    private void HideWorldMapActivityProgress()
    {
        if (worldMapArmyActivity != null)
            worldMapArmyActivity.style.display = DisplayStyle.None;
        if (worldMapLocationActivity != null)
            worldMapLocationActivity.style.display = DisplayStyle.None;
    }

    private void RefreshWorldMapSelectionCard()
    {
        if (mapSelectionCard != null)
            mapSelectionCard.style.display =
                DisplayStyle.None;
    }

    private static string GetWorldMapLocationStatus(
        LocationData location)
    {
        if (location.IsExplored)
            return "Состояние: исследована.";

        if (location.ExplorationHours > 0)
            return "Состояние: доступна для исследования.";

        return "Состояние: обнаружена.";
    }
}
