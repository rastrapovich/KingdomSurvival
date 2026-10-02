using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // Задача "регулируемый визуальный размер героя и Дома": размеры теперь
    // читаются из WorldMapVisualTheme.HeroMarkerSizeCells/HomeMarkerSizeCells
    // (доли логической клетки, единый источник истины с Preview) — заменяет
    // прежние захардкоженные CapitalMarkerCellFraction/ArmyMarkerScreenDiameter.
    // Сознательный отказ от прежней WM-16 модели "герой — постоянный
    // экранный диаметр, не зависит от zoom": по явному требованию этой
    // задачи маркер героя теперь масштабируется вместе с картой, как и Дом.

    // AM-07.5 (канон v1.35, §9.9): единственное место в UI-слое, где решается,
    // какая география активна — авторский мир (если подключён) или безопасная
    // сплошная Plains. Никогда не вызывает старую процедурную генерацию (её
    // больше не существует) — заменяет разрозненные ConfigureTerrain(seed)
    // по всему UI, каждый из которых был потенциальным скрытым откатом к ней.
    private static void EnsureWorldMapGeographyConfigured()
    {
        // 12И: правила перемещения (темп, местность) — из ассета настроек.
        WorldMapVisualRuntime.ApplyMovementRules();
        WorldMapDatabaseAsset database = WorldMapVisualRuntime.LoadDatabase();
        if (database != null && database.ActiveWorld != null)
            WorldMapNavigation.ConfigureFromDefinition(database.ActiveWorld.ToData());
        else
            WorldMapNavigation.ConfigureDefaultTerrain();
    }

    private VisualElement worldMapViewport;
    private VisualElement worldMap;
    private VisualElement worldMapBackground;
    private VisualElement worldMapArtLayers;
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
    private Label worldMapHintLabel;
    private VisualElement mapSelectionCard;
    private Label mapSelectionTitle;
    private Label mapSelectionDetails;
    private Button mapSendButton;

    // AM-06: навигация/зум карты — "К герою"/"К Дому"/"Вся карта", +/-,
    // компактный индикатор масштаба, переключатель вспомогательной сетки.
    private Button worldMapFocusHeroButton;
    private Button worldMapFocusHomeButton;
    private Button worldMapFitButton;
    private Button worldMapZoomInButton;
    private Button worldMapZoomOutButton;
    private Label worldMapZoomIndicatorLabel;
    private Toggle worldMapGridToggle;
    private Toggle worldMapFollowToggle;

    // Эти поля оставлены для совместимости со старым прототипным UI.
    // Подтверждение цели больше не используется: клик сразу отдаёт приказ.
    private string selectedMapLocationId;
    private bool hasSelectedMapPoint;
    private float selectedMapXPercent;
    private float selectedMapYPercent;

    private void FindWorldMapElements(VisualElement root)
    {
        worldMapViewport = root.Q<VisualElement>("world-map-viewport");
        worldMap = root.Q<VisualElement>("world-map");
        worldMapBackground = root.Q<VisualElement>("world-map-background");
        worldMapArtLayers = root.Q<VisualElement>("world-map-art-layers");
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
        worldMapFocusHeroButton = root.Q<Button>("world-map-focus-hero-button");
        worldMapFocusHomeButton = root.Q<Button>("world-map-focus-home-button");
        worldMapFitButton = root.Q<Button>("world-map-fit-button");
        worldMapZoomInButton = root.Q<Button>("world-map-zoom-in-button");
        worldMapZoomOutButton = root.Q<Button>("world-map-zoom-out-button");
        worldMapZoomIndicatorLabel = root.Q<Label>("world-map-zoom-indicator");
        worldMapGridToggle = root.Q<Toggle>("world-map-grid-toggle");
        worldMapFollowToggle = root.Q<Toggle>("world-map-follow-toggle");

        // Слои-заготовки (WM-02) пока ничего не рисуют, но не должны перехватывать
        // клики по карте — как и остальные декоративные/маршрутные слои.
        if (worldMapBackground != null)
            worldMapBackground.pickingMode = PickingMode.Ignore;

        if (worldMapArtLayers != null)
            worldMapArtLayers.pickingMode = PickingMode.Ignore;

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
        worldMapCapitalButton.clicked +=
            OnWorldMapCapitalClicked;
        RegisterWorldMapViewportCallbacks();
        RegisterWorldMapNavControlCallbacks();
    }

    private void UnregisterWorldMapCallbacks()
    {
        worldMapCapitalButton.clicked -=
            OnWorldMapCapitalClicked;
        UnregisterWorldMapViewportCallbacks();
        UnregisterWorldMapNavControlCallbacks();
    }

    private void RegisterWorldMapNavControlCallbacks()
    {
        if (worldMapFocusHeroButton != null)
            worldMapFocusHeroButton.clicked += OnWorldMapFocusHeroButtonClicked;
        if (worldMapFocusHomeButton != null)
            worldMapFocusHomeButton.clicked += OnWorldMapFocusHomeButtonClicked;
        if (worldMapFitButton != null)
            worldMapFitButton.clicked += OnWorldMapFitButtonClicked;
        if (worldMapZoomInButton != null)
            worldMapZoomInButton.clicked += OnWorldMapZoomInButtonClicked;
        if (worldMapZoomOutButton != null)
            worldMapZoomOutButton.clicked += OnWorldMapZoomOutButtonClicked;
        if (worldMapGridToggle != null)
            worldMapGridToggle.RegisterValueChangedCallback(OnWorldMapGridToggleChanged);
        if (worldMapFollowToggle != null)
        {
            worldMapFollowToggle.SetValueWithoutNotify(IsWorldMapCameraFollowEnabled());
            worldMapFollowToggle.RegisterValueChangedCallback(OnWorldMapFollowToggleChanged);
        }
    }

    private void UnregisterWorldMapNavControlCallbacks()
    {
        if (worldMapFocusHeroButton != null)
            worldMapFocusHeroButton.clicked -= OnWorldMapFocusHeroButtonClicked;
        if (worldMapFocusHomeButton != null)
            worldMapFocusHomeButton.clicked -= OnWorldMapFocusHomeButtonClicked;
        if (worldMapFitButton != null)
            worldMapFitButton.clicked -= OnWorldMapFitButtonClicked;
        if (worldMapZoomInButton != null)
            worldMapZoomInButton.clicked -= OnWorldMapZoomInButtonClicked;
        if (worldMapZoomOutButton != null)
            worldMapZoomOutButton.clicked -= OnWorldMapZoomOutButtonClicked;
        if (worldMapGridToggle != null)
            worldMapGridToggle.UnregisterValueChangedCallback(OnWorldMapGridToggleChanged);
        if (worldMapFollowToggle != null)
            worldMapFollowToggle.UnregisterValueChangedCallback(OnWorldMapFollowToggleChanged);
    }

    private void ResetWorldMapSelection()
    {
        selectedMapLocationId = null;
        hasSelectedMapPoint = false;
    }

    // 12И: клик по маркеру места — бег к нему (тот же приказ, что у карты).
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

        IssueContinuousMapOrder(
            location.MapXPercent,
            location.MapYPercent,
            location.Id);
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
            gameState.TryCancelPreparedExpedition(
                out resultMessage);
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
        worldMapArtLayers?.Clear();
        worldMapWater?.Clear();
        worldMapTerrain.Clear();
        worldMapRoads?.Clear();
        worldMapDecoration?.Clear();
        worldMapRoutes.Clear();
        worldMapMarkers.Clear();
        worldMapFog?.Clear();

        ApplyWorldMapBackground();
        ApplyWorldMapArtLayers();

        foreach (LocationData location in gameState.Locations)
        {
            if (location.IsWaypoint ||
                !location.IsVisibleOnMap)
            {
                continue;
            }

            CreateWorldMapNode(location);
        }

        RefreshWorldMapCapital();
        RefreshWorldMapArmyMarker();
    }

    private void ApplyWorldMapBackground()
    {
        if (worldMapBackground == null)
            return;

        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();
        if (theme == null)
            return;

        worldMapBackground.style.backgroundColor = theme.BaseMapColor;
        worldMapBackground.style.unityBackgroundImageTintColor = theme.BaseMapTint;
        if (theme.BaseMapSprite != null)
            worldMapBackground.style.backgroundImage = new StyleBackground(theme.BaseMapSprite);
        else
            worldMapBackground.style.backgroundImage = StyleKeyword.None;
    }

    // Задача "Map Art Layers": каждый Art Layer занимает только свой Bounds
    // внутри world-map (0..100%, та же система координат, что у Roads/
    // Locations/героя). Проценты style.left/top/width/height относительно
    // world-map — это ровно тот же линейный перевод координат, что и
    // WorldMapPreviewMath в Editor Preview (OnWorldMapPointerDown уже делает
    // обратное преобразование той же формулой: local/resolvedStyle*100).
    // Порядок/фильтрация — через общий WorldMapArtLayerUtility, чтобы Preview
    // и runtime никогда не разошлись.
    private void ApplyWorldMapArtLayers()
    {
        if (worldMapArtLayers == null)
            return;

        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();
        if (theme == null)
            return;

        List<WorldMapArtLayerEntry> ordered =
            WorldMapArtLayerUtility.GetOrderedEnabledLayers(theme.ArtLayers);

        foreach (WorldMapArtLayerEntry layer in ordered)
        {
            VisualElement element = new VisualElement();
            element.pickingMode = PickingMode.Ignore;
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(layer.MinXPercent);
            element.style.top = Length.Percent(layer.MinYPercent);
            element.style.width = Length.Percent(layer.MaxXPercent - layer.MinXPercent);
            element.style.height = Length.Percent(layer.MaxYPercent - layer.MinYPercent);
            element.style.opacity = Mathf.Clamp01(layer.Opacity);
            element.style.backgroundImage = new StyleBackground(layer.Sprite);
            element.style.unityBackgroundScaleMode =
                layer.FitMode == WorldMapArtLayerFitMode.PreserveAspect
                    ? ScaleMode.ScaleToFit
                    : ScaleMode.StretchToFill;

            worldMapArtLayers.Add(element);
        }
    }

    // AM-07.5 (канон v1.35, §9.9): рельеф больше не рисуется кодом.
    // Художественный источник истины — baseMapSprite; шестиугольная сетка
    // WorldMapNavigation невидима. 12И (канон v1.50): путь героя тоже не
    // рисуется — ни пунктиром, ни точками.

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
        // Персональная иконка/оттенок/масштаб берутся из записи
        // локации в World Map Database. Старая Icon Library остаётся
        // совместимым fallback для уже настроенных ассетов.
        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();
        WorldMapLocationDefinition definition =
            WorldMapVisualRuntime.FindLocation(location.Id);
        Sprite icon = definition != null && definition.Icon != null
            ? definition.Icon
            : theme != null && theme.IconLibrary != null
                ? theme.IconLibrary.FindIconForLocation(location.Id)
                : null;

        if (icon == null)
            return;

        Image iconImage = new Image();
        iconImage.AddToClassList("world-map-node-icon");
        iconImage.sprite = icon;
        iconImage.tintColor = definition != null
            ? definition.IconTint
            : Color.white;
        float iconScale = definition != null
            ? Mathf.Clamp(definition.IconScale, 0.25f, 3f)
            : 1f;
        iconImage.style.scale = new Scale(
            new Vector3(iconScale, iconScale, 1f));
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

        // Размер и позиция считаются из реального разрешения сетки,
        // WorldMapVisualTheme.HomeMarkerSizeCells (доли клетки) и
        // канонической точки столицы.
        float homeMarkerSizeCells = GetActiveMarkerTheme()?.HomeMarkerSizeCells
            ?? WorldMapVisualTheme.DefaultHomeMarkerSizeCells;
        Vector2 markerSizePercent = GetMarkerSizePercent(homeMarkerSizeCells);
        float markerWidthPercent = markerSizePercent.x;
        float markerHeightPercent = markerSizePercent.y;

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

        worldMapCapitalButton.text = string.Empty;
        worldMapCapitalButton.tooltip =
            active
                ? "Дом — нажмите, чтобы приказать отряду возвращаться"
                : "Дом";

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

        // Позиция и размер оба в процентах мира — маркер героя теперь
        // масштабируется вместе с картой при zoom (child элемента world-map,
        // который сам получает transform:scale, поэтому процентный размер
        // автоматически масштабируется вместе с ним без ручной компенсации).
        RefreshWorldMapArmyMarkerSize();
        worldMapArmyMarker.style.minWidth = new Length(0, LengthUnit.Pixel);
        worldMapArmyMarker.style.minHeight = new Length(0, LengthUnit.Pixel);

        worldMapArmyMarker.style.display =
            DisplayStyle.Flex;

        float heroMarkerSizeCells = GetActiveMarkerTheme()?.HeroMarkerSizeCells
            ?? WorldMapVisualTheme.DefaultHeroMarkerSizeCells;
        Vector2 heroMarkerSizePercent = GetMarkerSizePercent(heroMarkerSizeCells);

        worldMapArmyMarker.style.left = new Length(
            expedition.CurrentMapXPercent - heroMarkerSizePercent.x * 0.5f,
            LengthUnit.Percent);
        worldMapArmyMarker.style.top = new Length(
            expedition.CurrentMapYPercent - heroMarkerSizePercent.y * 0.5f,
            LengthUnit.Percent);

        string armyStatusText =
            expedition.RemainingRouteCells > 0
                ? ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState))
                : "на месте";

        // 12И: надпись времени у героя не показывается — только подсказка.
        worldMapArmyMarkerLabel.text = armyStatusText;
        worldMapArmyMarkerLabel.style.display = DisplayStyle.None;
        worldMapArmyMarker.tooltip = "Отряд — " + armyStatusText;

        RefreshWorldMapActivityProgress(expedition);
    }

    private void RefreshWorldMapArmyMarkerSize()
    {
        if (worldMapArmyMarker == null)
            return;

        float heroMarkerSizeCells = GetActiveMarkerTheme()?.HeroMarkerSizeCells
            ?? WorldMapVisualTheme.DefaultHeroMarkerSizeCells;
        Vector2 sizePercent = GetMarkerSizePercent(heroMarkerSizeCells);

        // Процентный размер, не px — маркер масштабируется вместе с картой
        // при zoom (см. комментарий в RefreshWorldMapArmyMarker). margin
        // больше не нужен для центрирования — left/top уже сдвинуты на
        // половину ширины/высоты в RefreshWorldMapArmyMarker.
        worldMapArmyMarker.style.width = new Length(sizePercent.x, LengthUnit.Percent);
        worldMapArmyMarker.style.height = new Length(sizePercent.y, LengthUnit.Percent);
        worldMapArmyMarker.style.marginLeft = 0;
        worldMapArmyMarker.style.marginTop = 0;
    }

    // Задача "регулируемый визуальный размер героя и Дома": единый перевод
    // "доли клетки" → "проценты карты" для обеих осей. 12И: клетка —
    // шестиугольник активного мира, её ширина — шаг между центрами соседей.
    private static Vector2 GetMarkerSizePercent(float sizeCells)
    {
        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        float cellPixels = (float)grid.HexWidth * sizeCells;
        return new Vector2(
            cellPixels / grid.CanvasWidth * 100f,
            cellPixels / grid.CanvasHeight * 100f);
    }

    private static WorldMapVisualTheme GetActiveMarkerTheme() =>
        WorldMapVisualRuntime.LoadActiveTheme();

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
