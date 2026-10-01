using KingdomSurvival.WorldMapVisual;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapPolishInitialized;
    private List<string> cancelledExpeditionRosterSnapshot;
    private VisualElement worldMapGridOverlay;
    // AM-06 (раздел 9 инструкции): "на обычном игровом масштабе сетка
    // выключена" — по умолчанию false, включается явно переключателем
    // world-map-grid-toggle, а не всегда рисуется при видимости оверлея.
    private bool worldMapGridEnabled;
    private static readonly Color WorldMapGridLineColor = new Color32(240, 236, 220, 70);
    private const float WorldMapGridTerrainAlpha = 0.32f;

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

    // 12И (канон v1.50 §9.9): служебный показ шестиугольной сетки и её
    // разметки — выключен по умолчанию, включается переключателем сетки.
    // Рисуется в координатах viewport (линии всегда 1px, без субпикселей
    // при zoom, как требовал WM-15) и только для видимых клеток.
    private void EnsureWorldMapGrid()
    {
        if (worldMapViewport == null)
            return;

        VisualElement existing =
            interfaceRoot != null
                ? interfaceRoot.Q<VisualElement>("world-map-grid-overlay")
                : null;

        if (existing != null)
            existing.RemoveFromHierarchy();

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
        overlay.style.display = DisplayStyle.None;
        overlay.generateVisualContent += DrawWorldMapHexGrid;

        worldMapViewport.Add(overlay);
        // Поверх рисунка карты, но под героем, маркерами и карточками.
        if (worldMap != null && worldMap.parent == worldMapViewport)
            overlay.PlaceInFront(worldMap);
        worldMapGridOverlay = overlay;

        RefreshWorldMapGridOverlay();
    }

    private void OnWorldMapGridToggleChanged(ChangeEvent<bool> evt)
    {
        worldMapGridEnabled = evt.newValue;
        RefreshWorldMapGridOverlay();
    }

    private void RefreshWorldMapGridOverlay()
    {
        if (worldMapGridOverlay == null)
            return;

        worldMapGridOverlay.style.display = worldMapGridEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        if (worldMapGridEnabled)
            worldMapGridOverlay.MarkDirtyRepaint();
    }

    private void DrawWorldMapHexGrid(MeshGenerationContext context)
    {
        if (!worldMapGridEnabled ||
            worldMapViewport == null ||
            worldMapCanvasWidth <= 0f ||
            worldMapCanvasHeight <= 0f)
        {
            return;
        }

        float viewportWidth = worldMapViewport.resolvedStyle.width;
        float viewportHeight = worldMapViewport.resolvedStyle.height;
        if (float.IsNaN(viewportWidth) || float.IsNaN(viewportHeight) || viewportWidth <= 0f || viewportHeight <= 0f)
            return;

        WorldMapTerrainLayer layer = WorldMapNavigation.ActiveLayer;
        WorldMapHexGrid grid = layer.Grid;
        float zoom = Mathf.Max(0.0001f, worldMapZoom);
        // Пиксели полотна сетки → пиксели canvas интерфейса → экран.
        float scaleX = worldMapCanvasWidth / grid.CanvasWidth * zoom;
        float scaleY = worldMapCanvasHeight / grid.CanvasHeight * zoom;
        float radiusScreen = grid.HexRadius * Mathf.Min(scaleX, scaleY);
        if (radiusScreen < 2f)
            return;

        // Видимый прямоугольник в пикселях полотна сетки (с запасом в клетку).
        double minX = (0f - worldMapPanOffsetX) / scaleX - grid.HexWidth;
        double maxX = (viewportWidth - worldMapPanOffsetX) / scaleX + grid.HexWidth;
        double minY = (0f - worldMapPanOffsetY) / scaleY - grid.RowStep;
        double maxY = (viewportHeight - worldMapPanOffsetY) / scaleY + grid.RowStep;
        int firstRow = Mathf.Max(0, (int)System.Math.Floor(minY / grid.RowStep));
        int lastRow = Mathf.Min(grid.Rows - 1, (int)System.Math.Ceiling(maxY / grid.RowStep));
        int firstColumn = Mathf.Max(0, (int)System.Math.Floor(minX / grid.HexWidth) - 1);
        int lastColumn = Mathf.Min(grid.Columns - 1, (int)System.Math.Ceiling(maxX / grid.HexWidth));

        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        Painter2D painter = context.painter2D;
        painter.lineWidth = 1f;
        painter.strokeColor = WorldMapGridLineColor;

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                WorldMapHexCell cell = new WorldMapHexCell(column, row);
                grid.CellCenter(cell, out double cx, out double cy);
                Vector2 center = new Vector2(
                    worldMapPanOffsetX + (float)cx * scaleX,
                    worldMapPanOffsetY + (float)cy * scaleY);

                WorldMapGameplayTerrainType terrain = layer.Get(cell);
                BuildWorldMapHexPath(painter, center, radiusScreen);
                if (terrain != WorldMapGameplayTerrainType.OpenGround)
                {
                    Color fill = settings != null
                        ? settings.GetTerrainColor(terrain)
                        : WorldMapMovementSettingsAsset.DefaultColor(terrain);
                    fill.a = WorldMapGridTerrainAlpha;
                    painter.fillColor = fill;
                    painter.Fill();
                }
                painter.Stroke();
            }
        }
    }

    // Острые вершины сверху, как у поля боя.
    private static void BuildWorldMapHexPath(Painter2D painter, Vector2 center, float radius)
    {
        painter.BeginPath();
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (60f * i - 90f);
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }
        painter.ClosePath();
    }

    private void RefreshWorldMapZoomCompensatedVisuals()
    {
        RefreshWorldMapGridOverlay();
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
