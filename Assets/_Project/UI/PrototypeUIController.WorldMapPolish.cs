using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapPolishInitialized;
    private List<string> cancelledExpeditionRosterSnapshot;
    private VisualElement worldMapGridOverlay;
    private readonly List<VisualElement> worldMapGridVerticalLines =
        new List<VisualElement>();
    private readonly List<VisualElement> worldMapGridHorizontalLines =
        new List<VisualElement>();

    // Общий класс для ЛЮБОЙ точки/штриха маршрута (узел или декоративный
    // штрих между узлами) — по нему RefreshWorldMapZoomCompensatedVisuals
    // находит все элементы, которым нужна компенсация zoom, независимо от
    // их конкретного визуального варианта (world-map-route-dot-active и т.п.).
    private const string WorldMapRouteMarkerClass = "world-map-route-marker";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapPolish)
            .ExecuteLater(20);
    }

    private void TryInitializeWorldMapPolish()
    {
        if (worldMapPolishInitialized)
            return;

        if (interfaceRoot == null || gameState == null)
        {
            ScheduleWorldMapPolishRetry();
            return;
        }

        VisualElement map =
            interfaceRoot.Q<VisualElement>("world-map");
        Button capitalButton =
            interfaceRoot.Q<Button>("world-map-capital-button");
        Button cancelButton =
            interfaceRoot.Q<Button>("return-expedition-button");

        if (map == null ||
            worldMapViewport == null ||
            capitalButton == null ||
            cancelButton == null)
        {
            ScheduleWorldMapPolishRetry();
            return;
        }

        HideLegacyWorldMapDecoration(map);
        EnsureWorldMapGrid();
        RegisterCancelledRosterPreservation(cancelButton, capitalButton);

        worldMapPolishInitialized = true;
    }

    private void ScheduleWorldMapPolishRetry()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeWorldMapPolish)
            .ExecuteLater(20);
    }

    private static void HideLegacyWorldMapDecoration(VisualElement map)
    {
        map.Query<VisualElement>(className: "world-map-land")
            .ForEach(element =>
                element.style.display = DisplayStyle.None);

        Label compass =
            map.Q<Label>(className: "world-map-compass");

        if (compass != null)
            compass.style.display = DisplayStyle.None;
    }

    private void EnsureWorldMapGrid()
    {
        if (worldMapViewport == null)
            return;

        // WM-15: старая сетка была дочерним элементом масштабируемого
        // world-map. На больших zoom её толщина компенсировалась как 1/zoom,
        // из-за чего UI Toolkit получал линии 0.5/0.2/0.1 px и на части
        // масштабов растрировал их нестабильно: исчезала одна ось или вся
        // сетка. Удаляем возможный старый экземпляр и создаём сетку как
        // screen-space overlay непосредственно внутри viewport.
        VisualElement existing =
            interfaceRoot != null
                ? interfaceRoot.Q<VisualElement>("world-map-grid-overlay")
                : null;

        if (existing != null)
            existing.RemoveFromHierarchy();

        worldMapGridVerticalLines.Clear();
        worldMapGridHorizontalLines.Clear();

        VisualElement overlay = new VisualElement
        {
            name = "world-map-grid-overlay",
            pickingMode = PickingMode.Ignore
        };

        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;

        Color gridColor = new Color32(95, 99, 92, 72);

        for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.style.position = Position.Absolute;
            line.style.width = 1f;
            line.style.backgroundColor = gridColor;
            line.style.display = DisplayStyle.None;

            overlay.Add(line);
            worldMapGridVerticalLines.Add(line);
        }

        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.style.position = Position.Absolute;
            line.style.height = 1f;
            line.style.backgroundColor = gridColor;
            line.style.display = DisplayStyle.None;

            overlay.Add(line);
            worldMapGridHorizontalLines.Add(line);
        }

        worldMapViewport.Add(overlay);
        // Сетка остаётся под содержимым карты: terrain/route/markers рисуются
        // поверх неё, но сама сетка уже не наследует scale world-map.
        overlay.SendToBack();
        worldMapGridOverlay = overlay;

        RefreshWorldMapGridOverlay();
    }

    // WM-15: сетка рисуется в координатах viewport и всегда имеет настоящую
    // толщину 1px. Позиция каждой линии вычисляется из pan + zoom карты, а не
    // через масштабирование самих line-элементов. Поэтому линии не становятся
    // субпиксельными и не исчезают на отдельных уровнях zoom.
    private void RefreshWorldMapGridOverlay()
    {
        if (worldMapGridOverlay == null ||
            worldMapViewport == null ||
            worldMapCanvasWidth <= 0f ||
            worldMapCanvasHeight <= 0f)
        {
            return;
        }

        float viewportWidth = worldMapViewport.resolvedStyle.width;
        float viewportHeight = worldMapViewport.resolvedStyle.height;

        if (float.IsNaN(viewportWidth) ||
            float.IsNaN(viewportHeight) ||
            viewportWidth <= 0f ||
            viewportHeight <= 0f)
        {
            return;
        }

        float zoom = Mathf.Max(0.0001f, worldMapZoom);
        float cellScreenSize = WorldMapBaseCellSizePx * zoom;
        float mapLeft = worldMapPanOffsetX;
        float mapTop = worldMapPanOffsetY;
        float mapRight = mapLeft + worldMapCanvasWidth * zoom;
        float mapBottom = mapTop + worldMapCanvasHeight * zoom;

        float visibleLeft = Mathf.Max(0f, mapLeft);
        float visibleTop = Mathf.Max(0f, mapTop);
        float visibleRight = Mathf.Min(viewportWidth, mapRight);
        float visibleBottom = Mathf.Min(viewportHeight, mapBottom);

        if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
        {
            worldMapGridOverlay.style.display = DisplayStyle.None;
            return;
        }

        worldMapGridOverlay.style.display = DisplayStyle.Flex;

        float snappedLeft = Mathf.Round(visibleLeft);
        float snappedTop = Mathf.Round(visibleTop);
        float snappedRight = Mathf.Round(visibleRight);
        float snappedBottom = Mathf.Round(visibleBottom);
        float verticalHeight = Mathf.Max(1f, snappedBottom - snappedTop);
        float horizontalWidth = Mathf.Max(1f, snappedRight - snappedLeft);
        float maxVisibleX = Mathf.Max(0f, viewportWidth - 1f);
        float maxVisibleY = Mathf.Max(0f, viewportHeight - 1f);

        for (int x = 0; x < worldMapGridVerticalLines.Count; x++)
        {
            VisualElement line = worldMapGridVerticalLines[x];
            float screenX = mapLeft + x * cellScreenSize;

            if (screenX < visibleLeft - 0.5f ||
                screenX > visibleRight + 0.5f)
            {
                line.style.display = DisplayStyle.None;
                continue;
            }

            line.style.display = DisplayStyle.Flex;
            line.style.left = Mathf.Clamp(Mathf.Round(screenX), 0f, maxVisibleX);
            line.style.top = snappedTop;
            line.style.width = 1f;
            line.style.height = verticalHeight;
        }

        for (int y = 0; y < worldMapGridHorizontalLines.Count; y++)
        {
            VisualElement line = worldMapGridHorizontalLines[y];
            float screenY = mapTop + y * cellScreenSize;

            if (screenY < visibleTop - 0.5f ||
                screenY > visibleBottom + 0.5f)
            {
                line.style.display = DisplayStyle.None;
                continue;
            }

            line.style.display = DisplayStyle.Flex;
            line.style.left = snappedLeft;
            line.style.top = Mathf.Clamp(Mathf.Round(screenY), 0f, maxVisibleY);
            line.style.width = horizontalWidth;
            line.style.height = 1f;
        }
    }

    // Route-маркеры и точка героя остаются внутри world-map, поэтому их
    // физический размер компенсируется обратно пропорционально zoom. Сетка
    // вынесена в screen-space overlay и обновляется отдельно.
    private void RefreshWorldMapZoomCompensatedVisuals()
    {
        RefreshWorldMapGridOverlay();

        float zoom = Mathf.Max(0.0001f, worldMapZoom);

        if (worldMapRoutes != null)
        {
            worldMapRoutes.Query<VisualElement>(
                className: WorldMapRouteMarkerClass).ForEach(dot =>
            {
                if (dot.userData is float screenDiameter)
                {
                    float size = screenDiameter / zoom;
                    dot.style.width = size;
                    dot.style.height = size;
                }
            });
        }

        RefreshWorldMapArmyMarkerScreenSize();
    }

    private void RegisterCancelledRosterPreservation(
        Button cancelButton,
        Button capitalButton)
    {
        cancelButton.RegisterCallback<PointerDownEvent>(
            CaptureRosterBeforePotentialCancellation,
            TrickleDown.TrickleDown);
        capitalButton.RegisterCallback<PointerDownEvent>(
            CaptureRosterBeforePotentialCancellation,
            TrickleDown.TrickleDown);

        cancelButton.clicked += ScheduleCancelledRosterRestore;
        capitalButton.clicked += ScheduleCancelledRosterRestore;
    }

    private void CaptureRosterBeforePotentialCancellation(
        PointerDownEvent pointerEvent)
    {
        cancelledExpeditionRosterSnapshot = null;

        if (pointerEvent.button != 0 ||
            gameState == null ||
            !gameState.HasActiveExpedition ||
            !gameState.CanCancelPreparedExpedition)
        {
            return;
        }

        cancelledExpeditionRosterSnapshot =
            new List<string>(gameState.ActiveExpedition.FighterIds);
    }

    private void ScheduleCancelledRosterRestore()
    {
        if (cancelledExpeditionRosterSnapshot == null ||
            cancelledExpeditionRosterSnapshot.Count == 0 ||
            interfaceRoot == null)
        {
            return;
        }

        List<string> rosterToRestore =
            new List<string>(cancelledExpeditionRosterSnapshot);
        cancelledExpeditionRosterSnapshot = null;

        interfaceRoot.schedule
            .Execute(() => RestoreCancelledRoster(rosterToRestore))
            .ExecuteLater(1);
    }

    private void RestoreCancelledRoster(List<string> rosterToRestore)
    {
        if (gameState == null || gameState.HasActiveExpedition)
            return;

        selectedFighterIds.Clear();

        foreach (string fighterId in rosterToRestore)
        {
            if (gameState.FindFighter(fighterId) != null)
                selectedFighterIds.Add(fighterId);
        }

        RefreshStableUiAfterStateChange();
    }
}
