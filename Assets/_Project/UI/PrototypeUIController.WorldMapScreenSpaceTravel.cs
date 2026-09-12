using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // WM-17: герой и пунктир маршрута должны оставаться настоящими
    // screen-space элементами. Внутри масштабируемого world-map размеры вида
    // "2px / zoom" на максимальном приближении превращались в субпиксельные
    // VisualElement и UI Toolkit начинал мерцать/терять их при растрировании.
    private const float WorldMapScreenHeroDiameter = 8f;
    private const float WorldMapScreenRouteDotDiameter = 2f;
    private const float WorldMapScreenRouteDotSpacing = 8f;
    private const float WorldMapScreenRouteRebuildDistance = 2f;
    private const long WorldMapScreenTravelRefreshMs = 16;

    private bool worldMapScreenTravelInitialized;
    private bool worldMapScreenTravelRefreshScheduled;
    private VisualElement worldMapScreenRouteOverlay;
    private VisualElement worldMapScreenHeroMarker;

    private int worldMapScreenRouteSignature = int.MinValue;
    private int worldMapScreenRouteIndex = -1;
    private float worldMapScreenRouteZoom = float.NaN;
    private float worldMapScreenRoutePanX = float.NaN;
    private float worldMapScreenRoutePanY = float.NaN;
    private Vector2 worldMapScreenRouteOrigin;
    private bool worldMapScreenRouteOriginValid;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapScreenSpaceTravelRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapScreenSpaceTravel)
            .ExecuteLater(30);
    }

    private void TryInitializeWorldMapScreenSpaceTravel()
    {
        if (worldMapScreenTravelInitialized)
            return;

        if (worldMapViewport == null ||
            worldMapRoutes == null ||
            worldMapArmyMarker == null ||
            gameState == null)
        {
            ScheduleWorldMapScreenSpaceTravelRetry();
            return;
        }

        // Старый route-layer продолжает существовать как совместимый runtime-
        // слой для уже написанного кода (DrawRoute/FadeNextRoutePoint), но его
        // субпиксельная визуализация больше не показывается игроку.
        worldMapRoutes.style.display = DisplayStyle.None;

        // Старый army marker нужен существующей логике activity/tooltip, поэтому
        // не удаляем и не перепривязываем его. Убираем только фон самой точки:
        // видимую точку героя рисует стабильный screen-space marker ниже.
        worldMapArmyMarker.style.backgroundColor = Color.clear;

        EnsureWorldMapScreenRouteOverlay();
        EnsureWorldMapScreenHeroMarker();

        worldMapScreenTravelInitialized = true;
        ScheduleWorldMapScreenTravelRefresh();
        RefreshWorldMapScreenSpaceTravelVisuals();
    }

    private void ScheduleWorldMapScreenSpaceTravelRetry()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeWorldMapScreenSpaceTravel)
            .ExecuteLater(30);
    }

    private void EnsureWorldMapScreenRouteOverlay()
    {
        if (worldMapViewport == null)
            return;

        VisualElement existing =
            worldMapViewport.Q<VisualElement>("world-map-screen-route-overlay");

        if (existing != null)
        {
            worldMapScreenRouteOverlay = existing;
            return;
        }

        worldMapScreenRouteOverlay = new VisualElement
        {
            name = "world-map-screen-route-overlay",
            pickingMode = PickingMode.Ignore
        };

        worldMapScreenRouteOverlay.style.position = Position.Absolute;
        worldMapScreenRouteOverlay.style.left = 0f;
        worldMapScreenRouteOverlay.style.right = 0f;
        worldMapScreenRouteOverlay.style.top = 0f;
        worldMapScreenRouteOverlay.style.bottom = 0f;

        worldMapViewport.Add(worldMapScreenRouteOverlay);
        KeepWorldMapInspectionCardOnTop();
    }

    private void EnsureWorldMapScreenHeroMarker()
    {
        if (worldMapViewport == null)
            return;

        VisualElement existing =
            worldMapViewport.Q<VisualElement>("world-map-screen-hero-marker");

        if (existing != null)
        {
            worldMapScreenHeroMarker = existing;
            return;
        }

        worldMapScreenHeroMarker = new VisualElement
        {
            name = "world-map-screen-hero-marker",
            pickingMode = PickingMode.Ignore
        };

        // Переиспользуем только визуальный класс кружка. Все размеры и позиция
        // задаются inline в screen-space и никогда не делятся на zoom.
        worldMapScreenHeroMarker.AddToClassList("world-map-army-marker");
        worldMapScreenHeroMarker.style.width = WorldMapScreenHeroDiameter;
        worldMapScreenHeroMarker.style.height = WorldMapScreenHeroDiameter;
        worldMapScreenHeroMarker.style.minWidth =
            new Length(0f, LengthUnit.Pixel);
        worldMapScreenHeroMarker.style.minHeight =
            new Length(0f, LengthUnit.Pixel);
        worldMapScreenHeroMarker.style.marginLeft =
            -WorldMapScreenHeroDiameter * 0.5f;
        worldMapScreenHeroMarker.style.marginTop =
            -WorldMapScreenHeroDiameter * 0.5f;
        worldMapScreenHeroMarker.style.display = DisplayStyle.None;

        worldMapViewport.Add(worldMapScreenHeroMarker);
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

    private void ScheduleWorldMapScreenTravelRefresh()
    {
        if (worldMapScreenTravelRefreshScheduled || worldMapViewport == null)
            return;

        worldMapScreenTravelRefreshScheduled = true;
        worldMapViewport.schedule
            .Execute(RefreshWorldMapScreenSpaceTravelVisuals)
            .Every(WorldMapScreenTravelRefreshMs);
    }

    private void RefreshWorldMapScreenSpaceTravelVisuals()
    {
        if (!worldMapScreenTravelInitialized ||
            worldMapViewport == null ||
            worldMapScreenRouteOverlay == null ||
            worldMapScreenHeroMarker == null ||
            gameState == null)
        {
            return;
        }

        if (!IsWorldMapScreenGeometryReady())
            return;

        if (!gameState.HasActiveExpedition)
        {
            worldMapScreenHeroMarker.style.display = DisplayStyle.None;

            if (worldMapScreenRouteOverlay.childCount > 0)
                worldMapScreenRouteOverlay.Clear();

            ResetWorldMapScreenRouteCache();
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        Vector2 heroScreenPosition = MapPercentToWorldMapViewport(
            expedition.CurrentMapXPercent,
            expedition.CurrentMapYPercent);

        UpdateWorldMapScreenHero(heroScreenPosition);

        int routeSignature = ComputeWorldMapScreenRouteSignature(expedition);
        bool routeOriginMoved =
            !worldMapScreenRouteOriginValid ||
            Vector2.Distance(
                heroScreenPosition,
                worldMapScreenRouteOrigin) >= WorldMapScreenRouteRebuildDistance;

        bool transformChanged =
            !Mathf.Approximately(worldMapScreenRouteZoom, worldMapZoom) ||
            !Mathf.Approximately(worldMapScreenRoutePanX, worldMapPanOffsetX) ||
            !Mathf.Approximately(worldMapScreenRoutePanY, worldMapPanOffsetY);

        if (routeSignature != worldMapScreenRouteSignature ||
            expedition.RouteIndex != worldMapScreenRouteIndex ||
            transformChanged ||
            routeOriginMoved)
        {
            RebuildWorldMapScreenRoute(expedition, heroScreenPosition);
            worldMapScreenRouteSignature = routeSignature;
            worldMapScreenRouteIndex = expedition.RouteIndex;
            worldMapScreenRouteZoom = worldMapZoom;
            worldMapScreenRoutePanX = worldMapPanOffsetX;
            worldMapScreenRoutePanY = worldMapPanOffsetY;
            worldMapScreenRouteOrigin = heroScreenPosition;
            worldMapScreenRouteOriginValid = true;
        }
    }

    private bool IsWorldMapScreenGeometryReady()
    {
        if (worldMapCanvasWidth <= 0f || worldMapCanvasHeight <= 0f)
            return false;

        float width = worldMapViewport.resolvedStyle.width;
        float height = worldMapViewport.resolvedStyle.height;

        return !float.IsNaN(width) &&
               !float.IsNaN(height) &&
               width > 0f &&
               height > 0f;
    }

    private Vector2 MapPercentToWorldMapViewport(float xPercent, float yPercent)
    {
        float zoom = Mathf.Max(0.0001f, worldMapZoom);

        return new Vector2(
            worldMapPanOffsetX + xPercent / 100f * worldMapCanvasWidth * zoom,
            worldMapPanOffsetY + yPercent / 100f * worldMapCanvasHeight * zoom);
    }

    private void UpdateWorldMapScreenHero(Vector2 screenPosition)
    {
        // Округление позиции до реального экранного пикселя убирает остаточное
        // shimmer-антиалиасинг при медленном движении, но сохраняет контроль
        // внутри клетки: на max zoom одна клетка занимает сотни пикселей.
        worldMapScreenHeroMarker.style.left = Mathf.Round(screenPosition.x);
        worldMapScreenHeroMarker.style.top = Mathf.Round(screenPosition.y);
        worldMapScreenHeroMarker.style.width = WorldMapScreenHeroDiameter;
        worldMapScreenHeroMarker.style.height = WorldMapScreenHeroDiameter;
        worldMapScreenHeroMarker.style.marginLeft =
            -WorldMapScreenHeroDiameter * 0.5f;
        worldMapScreenHeroMarker.style.marginTop =
            -WorldMapScreenHeroDiameter * 0.5f;
        worldMapScreenHeroMarker.style.display = DisplayStyle.Flex;
    }

    private void RebuildWorldMapScreenRoute(
        ExpeditionData expedition,
        Vector2 heroScreenPosition)
    {
        worldMapScreenRouteOverlay.Clear();

        if (expedition.Route == null || expedition.Route.Count < 2)
            return;

        int firstSegmentIndex = Mathf.Clamp(
            expedition.RouteIndex,
            0,
            expedition.Route.Count - 2);

        float distanceUntilNextDash = WorldMapScreenRouteDotSpacing * 0.5f;
        Vector2 from = heroScreenPosition;

        for (int i = firstSegmentIndex;
             i < expedition.Route.Count - 1;
             i++)
        {
            MapPointData nextPoint = expedition.Route[i + 1];
            Vector2 to = MapPercentToWorldMapViewport(
                nextPoint.XPercent,
                nextPoint.YPercent);

            AddWorldMapScreenRouteSegment(
                from,
                to,
                ref distanceUntilNextDash);

            from = to;
        }
    }

    private void AddWorldMapScreenRouteSegment(
        Vector2 from,
        Vector2 to,
        ref float distanceUntilNextDash)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;

        if (length <= 0.001f)
            return;

        Vector2 direction = delta / length;

        while (distanceUntilNextDash < length)
        {
            Vector2 position = from + direction * distanceUntilNextDash;
            AddWorldMapScreenRouteDot(position);
            distanceUntilNextDash += WorldMapScreenRouteDotSpacing;
        }

        distanceUntilNextDash -= length;

        if (distanceUntilNextDash <= 0.001f)
            distanceUntilNextDash = WorldMapScreenRouteDotSpacing;
    }

    private void AddWorldMapScreenRouteDot(Vector2 screenPosition)
    {
        float viewportWidth = worldMapViewport.resolvedStyle.width;
        float viewportHeight = worldMapViewport.resolvedStyle.height;
        float padding = WorldMapScreenRouteDotDiameter;

        if (screenPosition.x < -padding ||
            screenPosition.y < -padding ||
            screenPosition.x > viewportWidth + padding ||
            screenPosition.y > viewportHeight + padding)
        {
            return;
        }

        VisualElement dot = new VisualElement
        {
            pickingMode = PickingMode.Ignore
        };

        dot.AddToClassList("world-map-route-dot");
        dot.AddToClassList("world-map-route-dot-active");
        dot.AddToClassList("world-map-route-dash");
        dot.style.width = WorldMapScreenRouteDotDiameter;
        dot.style.height = WorldMapScreenRouteDotDiameter;
        dot.style.left = Mathf.Round(screenPosition.x);
        dot.style.top = Mathf.Round(screenPosition.y);
        dot.style.marginLeft = -WorldMapScreenRouteDotDiameter * 0.5f;
        dot.style.marginTop = -WorldMapScreenRouteDotDiameter * 0.5f;

        worldMapScreenRouteOverlay.Add(dot);
    }

    private static int ComputeWorldMapScreenRouteSignature(ExpeditionData expedition)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + expedition.RouteIndex;

            if (expedition.Route == null)
                return hash;

            hash = hash * 31 + expedition.Route.Count;

            for (int i = 0; i < expedition.Route.Count; i++)
            {
                MapPointData point = expedition.Route[i];
                hash = hash * 31 + point.XPercent.GetHashCode();
                hash = hash * 31 + point.YPercent.GetHashCode();
            }

            return hash;
        }
    }

    private void ResetWorldMapScreenRouteCache()
    {
        worldMapScreenRouteSignature = int.MinValue;
        worldMapScreenRouteIndex = -1;
        worldMapScreenRouteZoom = float.NaN;
        worldMapScreenRoutePanX = float.NaN;
        worldMapScreenRoutePanY = float.NaN;
        worldMapScreenRouteOrigin = Vector2.zero;
        worldMapScreenRouteOriginValid = false;
    }
}
