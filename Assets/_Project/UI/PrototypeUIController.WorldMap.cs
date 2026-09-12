using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private VisualElement worldMapViewport;
    private VisualElement worldMap;
    private VisualElement worldMapBackground;
    private VisualElement worldMapWater;
    private VisualElement worldMapTerrain;
    private VisualElement worldMapRoads;
    private VisualElement worldMapDecoration;
    private VisualElement worldMapRoutes;
    private VisualElement worldMapMarkers;
    private VisualElement worldMapFog;
    private Button worldMapCapitalButton;
    private VisualElement worldMapArmyMarker;
    private Label worldMapArmyMarkerLabel;
    private VisualElement worldMapArmyActivity;
    private VisualElement worldMapArmyActivityFill;
    private Label worldMapArmyActivityLabel;
    private VisualElement worldMapLocationActivity;
    private VisualElement worldMapLocationActivityFill;
    private Label worldMapLocationActivityLabel;
    private List<MapPointData> renderedWorldMapRoute;
    private int renderedWorldMapRouteIndex = -1;
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
        worldMapViewport = root.Q<VisualElement>("world-map-viewport");
        worldMap = root.Q<VisualElement>("world-map");
        worldMapBackground = root.Q<VisualElement>("world-map-background");
        worldMapWater = root.Q<VisualElement>("world-map-water");
        worldMapTerrain = root.Q<VisualElement>("world-map-terrain");
        worldMapRoads = root.Q<VisualElement>("world-map-roads");
        worldMapDecoration = root.Q<VisualElement>("world-map-decoration");
        worldMapRoutes = root.Q<VisualElement>("world-map-routes");
        worldMapMarkers = root.Q<VisualElement>("world-map-markers");
        worldMapFog = root.Q<VisualElement>("world-map-fog");
        worldMapCapitalButton = root.Q<Button>("world-map-capital-button");
        worldMapArmyMarker = root.Q<VisualElement>("world-map-army-marker");
        worldMapArmyMarkerLabel =
            root.Q<Label>("world-map-army-marker-label");
        worldMapHintLabel = root.Q<Label>("world-map-hint-label");
        mapSelectionCard = root.Q<VisualElement>("map-selection-card");
        mapSelectionTitle = root.Q<Label>("map-selection-title");
        mapSelectionDetails = root.Q<Label>("map-selection-details");
        mapSendButton = root.Q<Button>("map-send-button");

        // Слои-заготовки (WM-02) пока ничего не рисуют, но не должны перехватывать
        // клики по карте — как и остальные декоративные/маршрутные слои.
        if (worldMapBackground != null)
            worldMapBackground.pickingMode = PickingMode.Ignore;

        if (worldMapWater != null)
            worldMapWater.pickingMode = PickingMode.Ignore;

        if (worldMapTerrain != null)
            worldMapTerrain.pickingMode = PickingMode.Ignore;

        if (worldMapRoads != null)
            worldMapRoads.pickingMode = PickingMode.Ignore;

        if (worldMapDecoration != null)
            worldMapDecoration.pickingMode = PickingMode.Ignore;

        if (worldMapRoutes != null)
            worldMapRoutes.pickingMode = PickingMode.Ignore;

        if (worldMapFog != null)
            worldMapFog.pickingMode = PickingMode.Ignore;

        if (worldMapArmyMarker != null)
            worldMapArmyMarker.pickingMode = PickingMode.Ignore;

        ConfigureWorldMapFullscreenLayout();
        InitializeWorldMapViewport();
    }

    private void ConfigureWorldMapFullscreenLayout()
    {
        if (worldMapViewport == null || expeditionsScreen == null)
            return;

        // Старый ScrollView содержал заголовки, статус, подсказку и карточку
        // "Разведка сектора". На экране карты они больше не нужны.
        ScrollView legacyScroll =
            expeditionsScreen.Q<ScrollView>();

        if (legacyScroll != null)
            legacyScroll.style.display = DisplayStyle.None;

        worldMapViewport.RemoveFromHierarchy();
        expeditionsScreen.Add(worldMapViewport);

        expeditionsScreen.style.flexGrow = 1f;
        expeditionsScreen.style.minHeight = 0f;
        expeditionsScreen.style.paddingLeft = 0f;
        expeditionsScreen.style.paddingRight = 0f;
        expeditionsScreen.style.paddingTop = 0f;
        expeditionsScreen.style.paddingBottom = 0f;

        worldMapViewport.style.flexGrow = 1f;
        worldMapViewport.style.flexShrink = 1f;
        worldMapViewport.style.width = Length.Percent(100);
        worldMapViewport.style.height = StyleKeyword.Auto;
        worldMapViewport.style.minHeight = 0f;
        worldMapViewport.style.marginLeft = 0f;
        worldMapViewport.style.marginRight = 0f;
        worldMapViewport.style.marginTop = 0f;
        worldMapViewport.style.marginBottom = 0f;

        if (worldMapHintLabel != null)
            worldMapHintLabel.style.display = DisplayStyle.None;

        if (mapSelectionCard != null)
            mapSelectionCard.style.display = DisplayStyle.None;
    }

    private bool WorldMapElementsExist() =>
        worldMapViewport != null &&
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
        RegisterWorldMapViewportCallbacks();
    }

    private void UnregisterWorldMapCallbacks()
    {
        worldMap.UnregisterCallback<PointerDownEvent>(
            OnWorldMapPointerDown);
        worldMapCapitalButton.clicked -=
            OnWorldMapCapitalClicked;
        UnregisterWorldMapViewportCallbacks();
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

        worldMapBackground?.Clear();
        worldMapWater?.Clear();
        worldMapTerrain.Clear();
        worldMapRoads?.Clear();
        worldMapDecoration?.Clear();
        worldMapRoutes.Clear();
        worldMapMarkers.Clear();
        worldMapFog?.Clear();
        renderedWorldMapRoute = null;
        renderedWorldMapRouteIndex = -1;

        DrawTerrainCells();

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
            renderedWorldMapRoute = gameState.ActiveExpedition.Route;
            renderedWorldMapRouteIndex = gameState.ActiveExpedition.RouteIndex;
        }

        RefreshWorldMapCapital();
        RefreshWorldMapArmyMarker();
    }

    private void DrawTerrainCells()
    {
        // Местность рисуется из WorldMapVisualTheme (World Map Database),
        // а не из захардкоженных цветов. Пока тема не назначена в Resources
        // (WorldMapDatabaseAsset.ResourcesPath), слой остаётся пустым — как
        // и раньше, до WM-01 карта не показывала местность вовсе.
        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();

        if (theme == null)
            return;

        DrawTerrainForType(theme, WorldMapTerrainType.Hills);
        DrawTerrainForType(theme, WorldMapTerrainType.Mountains);
    }

    private void DrawTerrainForType(
        WorldMapVisualTheme theme,
        WorldMapTerrainType terrain)
    {
        WorldMapTerrainVisualProfile profile =
            theme.FindTerrainProfile(terrain);

        if (profile == null)
            return;

        // WM-04: если художник уже дал варианты-массы для этого типа
        // местности — рисуем органичные пятна по кластерам клеток вместо
        // сетки квадратов. Пока вариантов нет (как сейчас, арта ещё нет),
        // используем прежнюю плоскую заливку по клетке — деградация без
        // регрессии, поведение как в WM-01.
        if (profile.MassVariants.Count > 0)
            DrawTerrainMassClusters(profile, terrain);
        else
            DrawTerrainFlatCells(profile, terrain);
    }

    private void DrawTerrainFlatCells(
        WorldMapTerrainVisualProfile profile,
        WorldMapTerrainType terrain)
    {
        if (profile.CellColor.a <= 0f)
            return;

        // Равнина не рисуется отдельными клетками — она фон карты. Клетки
        // добавляются только там, где местность реально отличается, чтобы
        // не создавать сотни VisualElement на пустом месте.
        for (int y = 0;
             y < WorldMapNavigation.GridHeight;
             y++)
        {
            for (int x = 0;
                 x < WorldMapNavigation.GridWidth;
                 x++)
            {
                if (WorldMapNavigation.GetTerrainAtGridCell(x, y) != terrain)
                    continue;

                VisualElement cell =
                    new VisualElement();

                cell.AddToClassList(
                    "world-map-terrain-cell");
                cell.style.backgroundColor =
                    profile.CellColor;

                cell.style.left =
                    new Length(
                        GridXToPercent(x),
                        LengthUnit.Percent);
                cell.style.top =
                    new Length(
                        GridYToPercent(y),
                        LengthUnit.Percent);

                worldMapTerrain.Add(cell);
            }
        }
    }

    private static float GridXToPercent(int x) =>
        x * 100f / (WorldMapNavigation.GridWidth - 1);

    private static float GridYToPercent(int y) =>
        y * 100f / (WorldMapNavigation.GridHeight - 1);

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

    private void RefreshWorldMapRouteProgress()
    {
        if (worldMapRoutes == null || gameState == null)
            return;

        if (!gameState.HasActiveExpedition ||
            gameState.ActiveExpedition.Route == null)
        {
            if (worldMapRoutes.childCount > 0)
                worldMapRoutes.Clear();

            renderedWorldMapRoute = null;
            renderedWorldMapRouteIndex = -1;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        bool routeChanged =
            !object.ReferenceEquals(renderedWorldMapRoute, expedition.Route) ||
            renderedWorldMapRouteIndex != expedition.RouteIndex;

        if (routeChanged)
        {
            worldMapRoutes.Clear();
            DrawRoute(expedition.Route, "world-map-route-dot-active");
            renderedWorldMapRoute = expedition.Route;
            renderedWorldMapRouteIndex = expedition.RouteIndex;
        }

        FadeNextRoutePoint(expedition);
    }

    private void FadeNextRoutePoint(ExpeditionData expedition)
    {
        if (worldMapRoutes.childCount == 0 ||
            expedition.RouteIndex < 0 ||
            expedition.RouteIndex >= expedition.Route.Count - 1)
        {
            return;
        }

        MapPointData from = expedition.Route[expedition.RouteIndex];
        MapPointData to = expedition.Route[expedition.RouteIndex + 1];
        float dx = to.XPercent - from.XPercent;
        float dy = to.YPercent - from.YPercent;
        float lengthSquared = dx * dx + dy * dy;
        float progress = 0f;

        if (lengthSquared > 0.0001f)
        {
            float currentDx = expedition.CurrentMapXPercent - from.XPercent;
            float currentDy = expedition.CurrentMapYPercent - from.YPercent;
            progress = Mathf.Clamp01(
                (currentDx * dx + currentDy * dy) / lengthSquared);
        }

        worldMapRoutes.ElementAt(0).style.opacity =
            Mathf.Lerp(0.96f, 0.08f, progress);
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

        ApplyWorldMapNodeIcon(node, location);

        bool canChangeRoute =
            !isGameOver &&
            (!gameState.HasActiveExpedition ||
             (!gameState.HasPendingExpeditionDecision &&
              !gameState.ActiveExpedition.IsLocationResearchInProgress));

        node.SetEnabled(canChangeRoute);
        worldMapMarkers.Add(node);
    }

    private static void ApplyWorldMapNodeIcon(
        Button node,
        LocationData location)
    {
        // Иконка берётся из WorldMapIconLibrary по LocationData.Id, а не из
        // самих игровых данных — художник меняет спрайт в теме, код не трогаем.
        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();
        Sprite icon =
            theme != null && theme.IconLibrary != null
                ? theme.IconLibrary.FindIconForLocation(location.Id)
                : null;

        if (icon == null)
            return;

        Image iconImage = new Image();
        iconImage.AddToClassList("world-map-node-icon");
        iconImage.sprite = icon;
        iconImage.pickingMode = PickingMode.Ignore;

        node.text = string.Empty;
        node.Add(iconImage);
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
