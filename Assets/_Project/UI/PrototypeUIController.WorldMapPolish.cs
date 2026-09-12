using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapPolishInitialized;
    private List<string> cancelledExpeditionRosterSnapshot;
    private VisualElement worldMapGridOverlay;

    private const string WorldMapGridLineVerticalClass = "world-map-grid-line-vertical";
    private const string WorldMapGridLineHorizontalClass = "world-map-grid-line-horizontal";
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

        if (map == null || capitalButton == null || cancelButton == null)
        {
            ScheduleWorldMapPolishRetry();
            return;
        }

        HideLegacyWorldMapDecoration(map);
        EnsureWorldMapGrid(map);
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

    private void EnsureWorldMapGrid(VisualElement map)
    {
        VisualElement existing = map.Q<VisualElement>("world-map-grid-overlay");

        if (existing != null)
        {
            worldMapGridOverlay = existing;
            return;
        }

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

        // WM-13: линии — дочерние элементы .world-map, того же контейнера,
        // к которому WorldMapViewport применяет style.scale = zoom. Заданная
        // здесь толщина в px визуально умножается на zoom, поэтому реальная
        // толщина на экране пересчитывается в RefreshWorldMapZoomCompensatedVisuals
        // (1px / zoom) при каждом изменении масштаба — так линия остаётся
        // примерно 1 экранным пикселем на любом уровне приближения.
        for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.AddToClassList(WorldMapGridLineVerticalClass);
            line.style.position = Position.Absolute;
            line.style.left = new Length(
                x * 100f / (WorldMapNavigation.GridWidth - 1),
                LengthUnit.Percent);
            line.style.top = 0f;
            line.style.bottom = 0f;
            line.style.backgroundColor = gridColor;
            overlay.Add(line);
        }

        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        {
            VisualElement line = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };

            line.AddToClassList(WorldMapGridLineHorizontalClass);
            line.style.position = Position.Absolute;
            line.style.left = 0f;
            line.style.right = 0f;
            line.style.top = new Length(
                y * 100f / (WorldMapNavigation.GridHeight - 1),
                LengthUnit.Percent);
            line.style.backgroundColor = gridColor;
            overlay.Add(line);
        }

        map.Add(overlay);
        overlay.SendToBack();
        worldMapGridOverlay = overlay;
    }

    // WM-13: и линии сетки, и точки/штрихи маршрута заданы в px внутри
    // .world-map — контейнера, который WorldMapViewport масштабирует целиком
    // через style.scale. Чтобы их экранный размер не рос вместе с zoom,
    // здесь при каждом изменении zoom (см. вызов из
    // ApplyWorldMapViewportTransform) пересчитываем px как
    // "желаемый экранный размер / zoom" — после применения scale родителя
    // на экране снова получается желаемый размер.
    private void RefreshWorldMapZoomCompensatedVisuals()
    {
        float zoom = Mathf.Max(0.0001f, worldMapZoom);

        if (worldMapGridOverlay != null)
        {
            float lineThickness = 1f / zoom;

            worldMapGridOverlay
                .Query<VisualElement>(className: WorldMapGridLineVerticalClass)
                .ForEach(line => line.style.width = lineThickness);

            worldMapGridOverlay
                .Query<VisualElement>(className: WorldMapGridLineHorizontalClass)
                .ForEach(line => line.style.height = lineThickness);
        }

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
