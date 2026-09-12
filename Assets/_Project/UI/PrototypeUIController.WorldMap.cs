using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // WM-12: столица/армия занимали ½ клетки — по отзыву пользователя после
    // проверки в живой сессии это всё ещё выглядит слишком крупным
    // ("нужно в 10 раз меньше"), поэтому доля клетки уменьшена в 10 раз
    // (0.5 → 0.05). Единая константа — один параметр для подстройки, а не
    // магическое число в каждом месте.
    private const float MapMarkerCellFraction = 0.05f;

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
        DrawRiver();

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
        // объединяются в строке в один прямоугольник (run-length по X),
        // а не один VisualElement на клетку: на большой сетке (после
        // увеличения GridWidth/GridHeight ×4) поклеточная отрисовка создавала
        // тысячи элементов за один RefreshWorldMapPanel. Размер клетки считаем
        // из реального разрешения сетки, а не хардкодим — раньше было
        // 4.5%/6.8%, подобранные под старую сетку 26×16.
        float cellWidthPercent =
            100f / (WorldMapNavigation.GridWidth - 1) * 1.1f;
        float cellHeightPercent =
            100f / (WorldMapNavigation.GridHeight - 1) * 1.1f;

        for (int y = 0;
             y < WorldMapNavigation.GridHeight;
             y++)
        {
            int runStartX = -1;

            for (int x = 0;
                 x <= WorldMapNavigation.GridWidth;
                 x++)
            {
                bool matches =
                    x < WorldMapNavigation.GridWidth &&
                    WorldMapNavigation.GetTerrainAtGridCell(x, y) == terrain;

                if (matches && runStartX < 0)
                {
                    runStartX = x;
                }
                else if (!matches && runStartX >= 0)
                {
                    AddTerrainRunElement(
                        profile,
                        runStartX,
                        x - 1,
                        y,
                        cellWidthPercent,
                        cellHeightPercent);
                    runStartX = -1;
                }
            }
        }
    }

    private void AddTerrainRunElement(
        WorldMapTerrainVisualProfile profile,
        int startX,
        int endX,
        int y,
        float cellWidthPercent,
        float cellHeightPercent)
    {
        int runLength = endX - startX + 1;

        VisualElement cell = new VisualElement();

        cell.AddToClassList("world-map-terrain-cell");
        cell.style.backgroundColor = profile.CellColor;

        cell.style.width =
            new Length(cellWidthPercent * runLength, LengthUnit.Percent);
        cell.style.height =
            new Length(cellHeightPercent, LengthUnit.Percent);

        cell.style.left =
            new Length(GridXToPercent(startX), LengthUnit.Percent);
        cell.style.top =
            new Length(GridYToPercent(y), LengthUnit.Percent);

        worldMapTerrain.Add(cell);
    }

    private static float GridXToPercent(int x) =>
        x * 100f / (WorldMapNavigation.GridWidth - 1);

    private static float GridYToPercent(int y) =>
        y * 100f / (WorldMapNavigation.GridHeight - 1);

    // WM-13: желаемый экранный размер узла маршрута и декоративных штрихов
    // между узлами — герой (MapMarkerCellFraction) остаётся заметно крупнее.
    // Фактический px на каждом элементе выставляет
    // RefreshWorldMapZoomCompensatedVisuals (WorldMapPolish.cs) как
    // screenDiameter / zoom, чтобы размер не менялся визуально при zoom.
    private const float RouteNodeScreenDiameter = 5f;
    private const float RouteDashScreenDiameter = 2f;
    // WM-14: штрихов на один полный grid-cell длины сегмента — плотность
    // пунктира остаётся стабильной независимо от того, короткий это шаг
    // (обычная клетка) или длиннее (диагональ, под-точки холмов/гор в
    // FindPath). Считается от РЕАЛЬНОЙ длины сегмента (AddRouteDashes), а
    // не как фиксированное число штрихов на любой сегмент.
    private const float RouteDashesPerCell = 5f;
    private const int RouteDashesMinPerSegment = 1;
    private const int RouteDashesMaxPerSegment = 20;

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
            AddRouteMarker(
                route[i].XPercent,
                route[i].YPercent,
                extraClass,
                RouteNodeScreenDiameter);

            if (i + 1 < route.Count)
            {
                AddRouteDashes(
                    route[i],
                    route[i + 1]);
            }
        }
    }

    private void AddRouteDashes(
        MapPointData from,
        MapPointData to)
    {
        // WM-14: расстояние считаем в единицах grid-клетки (а не в процентах
        // от карты напрямую) — после введения единого квадратного размера
        // клетки (WorldMapViewport.ConfigureWorldMapCanvasSize) один шаг по X
        // и один шаг по Y физически равны, поэтому такое расстояние честно
        // отражает реальную длину сегмента на экране.
        float fromCellX = from.XPercent / 100f * (WorldMapNavigation.GridWidth - 1);
        float fromCellY = from.YPercent / 100f * (WorldMapNavigation.GridHeight - 1);
        float toCellX = to.XPercent / 100f * (WorldMapNavigation.GridWidth - 1);
        float toCellY = to.YPercent / 100f * (WorldMapNavigation.GridHeight - 1);

        float segmentLengthCells = Mathf.Sqrt(
            (toCellX - fromCellX) * (toCellX - fromCellX) +
            (toCellY - fromCellY) * (toCellY - fromCellY));

        int dashCount = Mathf.Clamp(
            Mathf.RoundToInt(segmentLengthCells * RouteDashesPerCell),
            RouteDashesMinPerSegment,
            RouteDashesMaxPerSegment);

        for (int d = 1; d <= dashCount; d++)
        {
            float t = d / (float)(dashCount + 1);

            AddRouteMarker(
                Mathf.Lerp(from.XPercent, to.XPercent, t),
                Mathf.Lerp(from.YPercent, to.YPercent, t),
                "world-map-route-dash",
                RouteDashScreenDiameter);
        }
    }

    private void AddRouteMarker(
        float xPercent,
        float yPercent,
        string extraClass,
        float screenDiameter)
    {
        VisualElement dot =
            new VisualElement();

        dot.AddToClassList("world-map-route-dot");
        dot.AddToClassList(WorldMapRouteMarkerClass);
        dot.AddToClassList(extraClass);
        // Читается в RefreshWorldMapZoomCompensatedVisuals, чтобы пересчитать
        // px при изменении zoom — сам этот вызов уже выставляет актуальный
        // размер для текущего zoom (см. конец метода).
        dot.userData = screenDiameter;

        dot.style.left =
            new Length(xPercent, LengthUnit.Percent);
        dot.style.top =
            new Length(yPercent, LengthUnit.Percent);

        worldMapRoutes.Add(dot);

        float size = screenDiameter / Mathf.Max(0.0001f, worldMapZoom);
        dot.style.width = size;
        dot.style.height = size;
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

        // WM-12: столицу можно пройти за полдня при правиле "1 клетка = 1
        // сутки". Размер и позиция считаются из реального разрешения сетки
        // и WorldMapNavigation.CapitalXPercent/YPercent — той же точки,
        // что используется путём и центрированием камеры при старте
        // (раньше CSS-позиция 37%/74% с ней не совпадала).
        float markerWidthPercent =
            100f / (WorldMapNavigation.GridWidth - 1) * MapMarkerCellFraction;
        float markerHeightPercent =
            100f / (WorldMapNavigation.GridHeight - 1) * MapMarkerCellFraction;

        worldMapCapitalButton.style.width =
            new Length(markerWidthPercent, LengthUnit.Percent);
        worldMapCapitalButton.style.height =
            new Length(markerHeightPercent, LengthUnit.Percent);
        // USS min-width/min-height:0 не побеждал встроенный min-size темы
        // Button (порядок применения стилшитов, не специфичность) — задаём
        // inline-стилем в коде, он гарантированно выше любого USS-правила.
        worldMapCapitalButton.style.minWidth = new Length(0, LengthUnit.Pixel);
        worldMapCapitalButton.style.minHeight = new Length(0, LengthUnit.Pixel);
        worldMapCapitalButton.style.left = new Length(
            WorldMapNavigation.CapitalXPercent - markerWidthPercent * 0.5f,
            LengthUnit.Percent);
        worldMapCapitalButton.style.top = new Length(
            WorldMapNavigation.CapitalYPercent - markerHeightPercent * 0.5f,
            LengthUnit.Percent);

        // При таком маленьком размере текст "СТОЛИЦА" физически не
        // поместится — название и статус уходят в tooltip.
        worldMapCapitalButton.text = string.Empty;
        worldMapCapitalButton.tooltip =
            active
                ? "Столица — нажмите, чтобы приказать армии возвращаться"
                : "Столица";

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

        // WM-12: та же доля клетки, что и у столицы (MapMarkerCellFraction).
        float markerWidthPercent =
            100f / (WorldMapNavigation.GridWidth - 1) * MapMarkerCellFraction;
        float markerHeightPercent =
            100f / (WorldMapNavigation.GridHeight - 1) * MapMarkerCellFraction;

        worldMapArmyMarker.style.width =
            new Length(markerWidthPercent, LengthUnit.Percent);
        worldMapArmyMarker.style.height =
            new Length(markerHeightPercent, LengthUnit.Percent);
        // См. комментарий в RefreshWorldMapCapital — inline min-width/
        // min-height:0 гарантированно побеждает встроенный min-size темы.
        worldMapArmyMarker.style.minWidth = new Length(0, LengthUnit.Pixel);
        worldMapArmyMarker.style.minHeight = new Length(0, LengthUnit.Pixel);

        worldMapArmyMarker.style.display =
            DisplayStyle.Flex;
        worldMapArmyMarker.style.left = new Length(
            expedition.CurrentMapXPercent - markerWidthPercent * 0.5f,
            LengthUnit.Percent);
        worldMapArmyMarker.style.top = new Length(
            expedition.CurrentMapYPercent - markerHeightPercent * 0.5f,
            LengthUnit.Percent);

        string armyStatusText =
            expedition.RemainingRouteCells > 0
                ? ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState))
                : "на месте";

        // Бейдж/подпись-Label скрыты статически в USS (текст физически не
        // помещается в маркер размером ½ клетки) — информация уходит в
        // tooltip самого маркера.
        worldMapArmyMarkerLabel.text = armyStatusText;
        worldMapArmyMarker.tooltip = "Отряд — " + armyStatusText;

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
