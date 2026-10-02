using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// 12И (канон v1.50 §9, §9.9): герой на глобальной карте — анимированная
// фигура из Базы анимаций (рабочее значение — ополченец), которая бежит под
// прямым управлением игрока. Траектория не рисуется; есть только короткая
// отметка места клика. Камера по умолчанию плавно держит героя в центре.
// Фигура и отметка — экранные элементы внутри viewport: их положение
// пересчитывается из процентов карты, pan и zoom каждый кадр, сразу после
// шага симуляции (Update), и задаётся через translate — без пересчёта
// раскладки и без округления до пикселя, иначе фигура дёргается.
public partial class PrototypeUIController
{
    private const float WorldMapHeroFallbackDiameter = 10f;
    // Смена ракурса — только при заметной смене направления: при пересчёте
    // пути под курсором фигура не мигает между двумя соседними ракурсами.
    private const float WorldMapHeroFacingHysteresis = 0.08f;
    // Высота фигуры в бою — 1,35 радиуса клетки (HexBoardElement): по ней
    // шаг ходьбы из Базы анимаций переводится в клетки карты.
    private const float BattleFigureHeightInHexRadii = 1.35f;

    private bool worldMapHeroInitialized;
    private Image worldMapHeroImage;
    private VisualElement worldMapHeroFallback;
    private VisualElement worldMapClickMarker;

    private CreatureAnimationDatabaseAsset worldMapHeroAnimationDatabase;
    private CreatureAnimationSetData worldMapHeroAnimationSet;
    private CreatureAnimationPlayer worldMapHeroPlayer;
    private string worldMapHeroAnimationSetId;
    private bool worldMapHeroAnimationSetResolved;
    private float worldMapHeroAnimationClock;
    private HexFacing worldMapHeroFacing = HexFacing.SouthEast;
    private Sprite worldMapHeroLastSprite;

    private Vector2 worldMapHeroAppliedSize = new Vector2(-1f, -1f);
    private bool worldMapCameraFollowSuspended;
    private const string WorldMapFollowPrefKey = "KingdomSurvival.WorldMap.FollowHero";

    // Плавный зум: колесо и кнопки задают цель, масштаб догоняет её за доли
    // секунды вокруг точки под курсором — без скачков по 15%.
    private const float WorldMapZoomSharpness = 14f;
    private bool worldMapZoomAnimating;
    private float worldMapZoomTarget = 1f;
    private Vector2 worldMapZoomPivot;

    private float worldMapClickMarkerStartedAt = -1f;
    private float worldMapClickMarkerXPercent;
    private float worldMapClickMarkerYPercent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapHeroRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapHero)
            .ExecuteLater(30);
    }

    private void TryInitializeWorldMapHero()
    {
        if (worldMapHeroInitialized)
            return;

        if (worldMapViewport == null || worldMap == null || worldMapArmyMarker == null || gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeWorldMapHero)
                    .ExecuteLater(30);
            }
            return;
        }

        // Слой маршрута больше не используется: путь игроку не показывается.
        if (worldMapRoutes != null)
            worldMapRoutes.style.display = DisplayStyle.None;

        // Маркер внутри полотна остаётся носителем полоски дела и подсказки,
        // но сам ничего не рисует: героя показывает фигура ниже.
        worldMapArmyMarker.style.backgroundColor = Color.clear;
        if (worldMapArmyMarkerLabel != null)
            worldMapArmyMarkerLabel.style.display = DisplayStyle.None;

        EnsureWorldMapHeroElements();
        worldMapHeroInitialized = true;
        TickWorldMapHero(0f);
    }

    private void EnsureWorldMapHeroElements()
    {
        worldMapHeroFallback = worldMapViewport.Q<VisualElement>("world-map-hero-fallback");
        if (worldMapHeroFallback == null)
        {
            worldMapHeroFallback = new VisualElement
            {
                name = "world-map-hero-fallback",
                pickingMode = PickingMode.Ignore
            };
            worldMapHeroFallback.AddToClassList("world-map-army-marker");
            worldMapHeroFallback.style.position = Position.Absolute;
            worldMapHeroFallback.style.left = 0f;
            worldMapHeroFallback.style.top = 0f;
            worldMapHeroFallback.style.width = WorldMapHeroFallbackDiameter;
            worldMapHeroFallback.style.height = WorldMapHeroFallbackDiameter;
            worldMapHeroFallback.style.minWidth = new Length(0f, LengthUnit.Pixel);
            worldMapHeroFallback.style.minHeight = new Length(0f, LengthUnit.Pixel);
            worldMapHeroFallback.style.marginLeft = -WorldMapHeroFallbackDiameter * 0.5f;
            worldMapHeroFallback.style.marginTop = -WorldMapHeroFallbackDiameter * 0.5f;
            worldMapHeroFallback.style.display = DisplayStyle.None;
            worldMapViewport.Add(worldMapHeroFallback);
        }

        worldMapHeroImage = worldMapViewport.Q<Image>("world-map-hero-figure");
        if (worldMapHeroImage == null)
        {
            worldMapHeroImage = new Image
            {
                name = "world-map-hero-figure",
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.StretchToFill
            };
            worldMapHeroImage.style.position = Position.Absolute;
            worldMapHeroImage.style.left = 0f;
            worldMapHeroImage.style.top = 0f;
            worldMapHeroImage.style.display = DisplayStyle.None;
            worldMapViewport.Add(worldMapHeroImage);
        }

        worldMapClickMarker = worldMapViewport.Q<VisualElement>("world-map-click-marker");
        if (worldMapClickMarker == null)
        {
            worldMapClickMarker = new VisualElement
            {
                name = "world-map-click-marker",
                pickingMode = PickingMode.Ignore
            };
            worldMapClickMarker.style.position = Position.Absolute;
            worldMapClickMarker.style.left = 0f;
            worldMapClickMarker.style.top = 0f;
            worldMapClickMarker.style.display = DisplayStyle.None;
            worldMapViewport.Add(worldMapClickMarker);
        }

        KeepWorldMapInspectionCardOnTop();
    }

    private void KeepWorldMapInspectionCardOnTop()
    {
        if (worldMapViewport == null)
            return;

        VisualElement inspectionCard =
            worldMapViewport.Q<VisualElement>("world-map-location-inspection-card");
        if (inspectionCard != null)
            inspectionCard.BringToFront();
    }

    // Вызывается из Update каждый кадр сразу после шага симуляции — фигура,
    // камера и время сдвигаются в одном и том же кадре.
    private void TickWorldMapHero(float dt)
    {
        if (!worldMapHeroInitialized || gameState == null || worldMapViewport == null)
            return;

        float now = Time.realtimeSinceStartup;
        if (!IsWorldMapScreenGeometryReady())
            return;

        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        TickWorldMapHold(now, settings);
        TickWorldMapZoom(dt);
        TickWorldMapCameraFollow(dt, settings);
        TickWorldMapClickMarker(now, settings);
        TickWorldMapHeroFigure(dt, settings);
    }

    private bool IsWorldMapScreenGeometryReady()
    {
        if (worldMapCanvasWidth <= 0f || worldMapCanvasHeight <= 0f)
            return false;

        float width = worldMapViewport.resolvedStyle.width;
        float height = worldMapViewport.resolvedStyle.height;
        return !float.IsNaN(width) && !float.IsNaN(height) && width > 0f && height > 0f;
    }

    private Vector2 MapPercentToWorldMapViewport(float xPercent, float yPercent)
    {
        float zoom = Mathf.Max(0.0001f, worldMapZoom);
        return new Vector2(
            worldMapPanOffsetX + xPercent / 100f * worldMapCanvasWidth * zoom,
            worldMapPanOffsetY + yPercent / 100f * worldMapCanvasHeight * zoom);
    }

    private bool IsWorldMapScreenOpen =>
        openedScreen.HasValue && openedScreen.Value == MainScreen.Expeditions;

    // ------------------------------------------------------------------
    // Фигура героя
    // ------------------------------------------------------------------

    private void TickWorldMapHeroFigure(float dt, WorldMapMovementSettingsAsset settings)
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapHeroImage.style.display = DisplayStyle.None;
            worldMapHeroFallback.style.display = DisplayStyle.None;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        Vector2 ground = MapPercentToWorldMapViewport(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent);

        bool running = ContinuousSimulationSystem.IsExpeditionRunning(gameState) &&
                       !ContinuousSimulationSystem.IsPaused(gameState);
        if (running)
            UpdateWorldMapHeroFacing(expedition);

        CreatureAnimationSetData set = ResolveWorldMapHeroAnimationSet(settings);
        if (set == null)
        {
            ShowWorldMapHeroFallback(ground);
            return;
        }

        CreatureAnimationAction action = running ? CreatureAnimationAction.Walk : CreatureAnimationAction.Idle;
        if (worldMapHeroPlayer.Action != action)
            worldMapHeroPlayer.Play(action, worldMapHeroAnimationClock);
        worldMapHeroPlayer.SetDirection(ToWorldMapHeroDirection(worldMapHeroFacing));

        float hexRadius = (float)WorldMapNavigation.Grid.HexRadius;
        float heightInRadii = settings != null ? settings.HeroHeightInHexRadii : 1.4f;
        worldMapHeroAnimationClock += dt * WorldMapHeroCadence(running, set, heightInRadii, settings);

        Sprite sprite = worldMapHeroPlayer.Evaluate(worldMapHeroAnimationClock);
        if (sprite == null)
            sprite = worldMapHeroLastSprite != null ? worldMapHeroLastSprite : set.FindFirstFrame();
        if (sprite == null)
        {
            ShowWorldMapHeroFallback(ground);
            return;
        }

        worldMapHeroLastSprite = sprite;
        if (worldMapHeroImage.sprite != sprite)
            worldMapHeroImage.sprite = sprite;

        // Опора набора стоит в точке героя; размер — от клетки, а не от
        // границ кадра, поэтому ноги не прыгают при смене кадров (как в бою).
        Vector2 canvas = set.CanvasSize.x > 0 && set.CanvasSize.y > 0
            ? (Vector2)set.CanvasSize
            : sprite.rect.size;
        float height = hexRadius * heightInRadii * set.FieldScale * Mathf.Max(0.0001f, worldMapZoom);
        float width = height * canvas.x / Mathf.Max(1f, canvas.y);
        Vector2 cellOffset = worldMapHeroPlayer.Clip != null ? worldMapHeroPlayer.Clip.Offset : Vector2.zero;

        if (worldMapHeroFallback.style.display != DisplayStyle.None)
            worldMapHeroFallback.style.display = DisplayStyle.None;
        if (worldMapHeroImage.style.display != DisplayStyle.Flex)
            worldMapHeroImage.style.display = DisplayStyle.Flex;

        // Размер меняется только с зумом — раскладку не трогаем каждый кадр.
        if (Mathf.Abs(worldMapHeroAppliedSize.x - width) > 0.01f ||
            Mathf.Abs(worldMapHeroAppliedSize.y - height) > 0.01f)
        {
            worldMapHeroImage.style.width = width;
            worldMapHeroImage.style.height = height;
            worldMapHeroAppliedSize = new Vector2(width, height);
        }

        worldMapHeroImage.style.translate = new Translate(
            ground.x - set.Pivot.x * width + cellOffset.x * width,
            ground.y - (1f - set.Pivot.y) * height - cellOffset.y * height);
    }

    private void ShowWorldMapHeroFallback(Vector2 ground)
    {
        if (worldMapHeroImage.style.display != DisplayStyle.None)
            worldMapHeroImage.style.display = DisplayStyle.None;
        if (worldMapHeroFallback.style.display != DisplayStyle.Flex)
            worldMapHeroFallback.style.display = DisplayStyle.Flex;
        worldMapHeroFallback.style.translate = new Translate(ground.x, ground.y);
    }

    private CreatureAnimationSetData ResolveWorldMapHeroAnimationSet(WorldMapMovementSettingsAsset settings)
    {
        string setId = settings != null ? settings.HeroAnimationSetId : "militia";
        if (worldMapHeroAnimationSetResolved && worldMapHeroAnimationSetId == setId)
            return worldMapHeroAnimationSet;

        worldMapHeroAnimationSetResolved = true;
        worldMapHeroAnimationSetId = setId;
        worldMapHeroAnimationDatabase = Resources.Load<CreatureAnimationDatabaseAsset>(
            CreatureAnimationDatabaseAsset.ResourcesPath);
        worldMapHeroAnimationSet = worldMapHeroAnimationDatabase != null
            ? worldMapHeroAnimationDatabase.FindSet(setId)
            : null;
        if (worldMapHeroAnimationSet != null && !worldMapHeroAnimationSet.HasAnyFrames)
            worldMapHeroAnimationSet = null;

        worldMapHeroLastSprite = null;
        worldMapHeroPlayer = worldMapHeroAnimationSet != null
            ? new CreatureAnimationPlayer(worldMapHeroAnimationSet, ToWorldMapHeroDirection(worldMapHeroFacing))
            : null;
        worldMapHeroPlayer?.Play(CreatureAnimationAction.Idle, worldMapHeroAnimationClock);
        return worldMapHeroAnimationSet;
    }

    // Ракурс — по направлению к следующей точке пути на экране (карта
    // смотрит сверху, соседи клетки — шесть направлений по 60°).
    private void UpdateWorldMapHeroFacing(ExpeditionData expedition)
    {
        if (expedition.Route == null || expedition.RouteIndex + 1 >= expedition.Route.Count)
            return;

        MapPointData next = expedition.Route[expedition.RouteIndex + 1];
        Vector2 direction = new Vector2(
            (next.XPercent - expedition.CurrentMapXPercent) * worldMapCanvasWidth,
            (next.YPercent - expedition.CurrentMapYPercent) * worldMapCanvasHeight);
        if (direction.sqrMagnitude < 0.0001f)
            return;

        HexFacing nearest = HexFacingMath.Nearest(direction, WorldMapHexNeighborVectors, worldMapHeroFacing);
        if (nearest == worldMapHeroFacing)
            return;

        Vector2 normalized = direction.normalized;
        float currentDot = Vector2.Dot(normalized, WorldMapHexNeighborVectors[(int)worldMapHeroFacing]);
        float nearestDot = Vector2.Dot(normalized, WorldMapHexNeighborVectors[(int)nearest]);
        if (nearestDot - currentDot > WorldMapHeroFacingHysteresis)
            worldMapHeroFacing = nearest;
    }

    private static readonly Vector2[] WorldMapHexNeighborVectors =
    {
        new Vector2(1f, 0f),
        new Vector2(0.5f, -0.8660254f),
        new Vector2(-0.5f, -0.8660254f),
        new Vector2(-1f, 0f),
        new Vector2(-0.5f, 0.8660254f),
        new Vector2(0.5f, 0.8660254f)
    };

    private CreatureAnimationDirection ToWorldMapHeroDirection(HexFacing facing)
    {
        if (worldMapHeroAnimationDatabase != null)
            return worldMapHeroAnimationDatabase.GetDirection(facing);

        foreach (CreatureAnimationDirectionMapping entry in CreatureAnimationDatabaseAsset.DefaultDirectionMap())
        {
            if (entry.Facing == facing)
                return entry.Direction;
        }
        return CreatureAnimationDirection.Front;
    }

    // Темп кадров при беге: шаг набора (клетки боя за цикл) переводится в
    // клетки карты по размеру фигуры, чтобы ноги не скользили по земле.
    private float WorldMapHeroCadence(
        bool running,
        CreatureAnimationSetData set,
        float heightInRadii,
        WorldMapMovementSettingsAsset settings)
    {
        if (!running || settings == null || !settings.SyncWalkCadence || worldMapHeroPlayer == null)
            return 1f;

        CreatureAnimationClip clip = worldMapHeroPlayer.Clip;
        if (clip == null || clip.Duration <= 0.0001f)
            return 1f;

        WorldMapMovementRules rules = WorldMapMovementRules.Current;
        ExpeditionData expedition = gameState.ActiveExpedition;
        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent);
        double hexesPerSecond = rules.SafeRunSpeedHexesPerSecond * rules.RunSpeedMultiplier(terrain);
        double strideHexes = set.WalkHexesPerCycle * heightInRadii / BattleFigureHeightInHexRadii;
        if (strideHexes <= 0.0001)
            return 1f;

        double cyclesPerSecond = hexesPerSecond / strideHexes;
        return Mathf.Clamp((float)(cyclesPerSecond * clip.Duration), 0.25f, 4f);
    }

    // ------------------------------------------------------------------
    // Камера
    // ------------------------------------------------------------------

    private void TickWorldMapCameraFollow(float dt, WorldMapMovementSettingsAsset settings)
    {
        if (dt <= 0f ||
            !IsWorldMapScreenOpen ||
            !gameState.HasActiveExpedition ||
            isPanningWorldMap ||
            worldMapCameraFollowSuspended ||
            !IsWorldMapCameraFollowEnabled())
        {
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        float zoom = Mathf.Max(0.0001f, worldMapZoom);
        float targetX = worldMapViewport.resolvedStyle.width * 0.5f -
                        expedition.CurrentMapXPercent / 100f * worldMapCanvasWidth * zoom;
        float targetY = worldMapViewport.resolvedStyle.height * 0.5f -
                        expedition.CurrentMapYPercent / 100f * worldMapCanvasHeight * zoom;

        float sharpness = settings != null ? settings.CameraFollowSharpness : 6f;
        float blend = 1f - Mathf.Exp(-sharpness * dt);
        float newX = Mathf.Lerp(worldMapPanOffsetX, targetX, blend);
        float newY = Mathf.Lerp(worldMapPanOffsetY, targetY, blend);
        if (Mathf.Abs(newX - worldMapPanOffsetX) < 0.01f && Mathf.Abs(newY - worldMapPanOffsetY) < 0.01f)
            return;

        worldMapPanOffsetX = newX;
        worldMapPanOffsetY = newY;
        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
    }

    // Следование за героем — переключатель «За героем» на карте; пока игрок
    // его не трогал, действует значение из настроек перемещения.
    private static bool IsWorldMapCameraFollowEnabled()
    {
        if (PlayerPrefs.HasKey(WorldMapFollowPrefKey))
            return PlayerPrefs.GetInt(WorldMapFollowPrefKey) != 0;
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        return settings != null && settings.CameraFollowsHero;
    }

    private void OnWorldMapFollowToggleChanged(ChangeEvent<bool> evt)
    {
        PlayerPrefs.SetInt(WorldMapFollowPrefKey, evt.newValue ? 1 : 0);
        PlayerPrefs.Save();
        if (evt.newValue)
            ResumeWorldMapCameraFollow();
    }

    private void StartWorldMapZoom(float factor, Vector2 pivotScreenPoint)
    {
        float from = worldMapZoomAnimating ? worldMapZoomTarget : worldMapZoom;
        worldMapZoomTarget = Mathf.Clamp(from * factor, worldMapMinZoom, worldMapMaxZoom);
        worldMapZoomPivot = pivotScreenPoint;
        worldMapZoomAnimating = !Mathf.Approximately(worldMapZoomTarget, worldMapZoom);
    }

    private void CancelWorldMapZoomAnimation() => worldMapZoomAnimating = false;

    private void TickWorldMapZoom(float dt)
    {
        if (!worldMapZoomAnimating || dt <= 0f)
            return;

        // В логарифме масштаба: шаг ощущается одинаково на любом приближении.
        float blend = 1f - Mathf.Exp(-WorldMapZoomSharpness * dt);
        float next = Mathf.Exp(Mathf.Lerp(Mathf.Log(worldMapZoom), Mathf.Log(worldMapZoomTarget), blend));
        if (Mathf.Abs(next - worldMapZoomTarget) <= worldMapZoomTarget * 0.002f)
        {
            next = worldMapZoomTarget;
            worldMapZoomAnimating = false;
        }

        ZoomWorldMapAroundScreenPoint(next / Mathf.Max(0.0001f, worldMapZoom), worldMapZoomPivot);
    }

    // Ручная панорама отпускает камеру до следующего приказа или «К герою».
    private void SuspendWorldMapCameraFollow() => worldMapCameraFollowSuspended = true;

    private void ResumeWorldMapCameraFollow() => worldMapCameraFollowSuspended = false;

    // ------------------------------------------------------------------
    // Отметка клика
    // ------------------------------------------------------------------

    private void ShowWorldMapClickMarker(float xPercent, float yPercent)
    {
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        if (worldMapClickMarker == null || (settings != null && !settings.ShowClickMarker))
            return;

        worldMapClickMarkerXPercent = xPercent;
        worldMapClickMarkerYPercent = yPercent;
        worldMapClickMarkerStartedAt = Time.realtimeSinceStartup;
    }

    private void TickWorldMapClickMarker(float now, WorldMapMovementSettingsAsset settings)
    {
        float duration = settings != null ? settings.ClickMarkerSeconds : 0.45f;
        float elapsed = now - worldMapClickMarkerStartedAt;
        if (worldMapClickMarkerStartedAt < 0f || elapsed >= duration)
        {
            worldMapClickMarker.style.display = DisplayStyle.None;
            return;
        }

        float t = Mathf.Clamp01(elapsed / duration);
        const float size = 22f;
        Color color = settings != null ? settings.ClickMarkerColor : new Color(0.96f, 0.88f, 0.62f, 0.9f);
        color.a *= 1f - t;

        Vector2 position = MapPercentToWorldMapViewport(worldMapClickMarkerXPercent, worldMapClickMarkerYPercent);
        worldMapClickMarker.style.display = DisplayStyle.Flex;
        worldMapClickMarker.style.width = size;
        worldMapClickMarker.style.height = size * 0.5f;
        worldMapClickMarker.style.translate = new Translate(position.x - size * 0.5f, position.y - size * 0.25f);
        float grow = Mathf.Lerp(0.35f, 1f, t);
        worldMapClickMarker.style.scale = new Scale(new Vector3(grow, grow, 1f));
        worldMapClickMarker.style.borderTopLeftRadius = size * 0.5f;
        worldMapClickMarker.style.borderTopRightRadius = size * 0.5f;
        worldMapClickMarker.style.borderBottomLeftRadius = size * 0.5f;
        worldMapClickMarker.style.borderBottomRightRadius = size * 0.5f;
        worldMapClickMarker.style.borderTopWidth = 2f;
        worldMapClickMarker.style.borderBottomWidth = 2f;
        worldMapClickMarker.style.borderLeftWidth = 2f;
        worldMapClickMarker.style.borderRightWidth = 2f;
        worldMapClickMarker.style.borderTopColor = color;
        worldMapClickMarker.style.borderBottomColor = color;
        worldMapClickMarker.style.borderLeftColor = color;
        worldMapClickMarker.style.borderRightColor = color;
    }
}
