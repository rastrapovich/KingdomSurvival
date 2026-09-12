using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // WM-03: единый скроллируемый/зумируемый холст (не набор экранов-регионов —
    // раздел 9.9 канона). Зум/панорама — чисто визуальный слой поверх world-map:
    // все проценто-позиционированные дети (местность, локации, маршрут, армия)
    // не меняются, координатное преобразование делает сам UI Toolkit через
    // worldMap.style.scale/left/top + WorldToLocal при клике.
    private const float WorldMapMinZoom = 0.75f;
    private const float WorldMapMaxZoom = 2.5f;
    private const float WorldMapZoomStep = 0.15f;

    private float worldMapZoom = 1f;
    private float worldMapPanOffsetX;
    private float worldMapPanOffsetY;
    private bool isPanningWorldMap;
    private Vector2 worldMapPanPointerStart;
    private Vector2 worldMapPanOffsetStart;

    private void InitializeWorldMapViewport()
    {
        if (worldMapViewport == null)
            return;

        worldMapViewport.RegisterCallback<GeometryChangedEvent>(
            OnWorldMapViewportGeometryChanged);

        ApplyWorldMapViewportTransform();
    }

    private void OnWorldMapViewportGeometryChanged(GeometryChangedEvent evt)
    {
        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
    }

    private void RegisterWorldMapViewportCallbacks()
    {
        if (worldMapViewport == null)
            return;

        worldMapViewport.RegisterCallback<WheelEvent>(OnWorldMapViewportWheel);
        worldMapViewport.RegisterCallback<PointerDownEvent>(OnWorldMapViewportPointerDown);
        worldMapViewport.RegisterCallback<PointerMoveEvent>(OnWorldMapViewportPointerMove);
        worldMapViewport.RegisterCallback<PointerUpEvent>(OnWorldMapViewportPointerUp);
        worldMapViewport.RegisterCallback<PointerCaptureOutEvent>(OnWorldMapViewportPointerCaptureOut);
    }

    private void UnregisterWorldMapViewportCallbacks()
    {
        if (worldMapViewport == null)
            return;

        worldMapViewport.UnregisterCallback<WheelEvent>(OnWorldMapViewportWheel);
        worldMapViewport.UnregisterCallback<PointerDownEvent>(OnWorldMapViewportPointerDown);
        worldMapViewport.UnregisterCallback<PointerMoveEvent>(OnWorldMapViewportPointerMove);
        worldMapViewport.UnregisterCallback<PointerUpEvent>(OnWorldMapViewportPointerUp);
        worldMapViewport.UnregisterCallback<PointerCaptureOutEvent>(OnWorldMapViewportPointerCaptureOut);

        isPanningWorldMap = false;
    }

    // Зум колесом мыши, привязанный к точке под курсором (точка под курсором
    // остаётся на месте на экране при изменении масштаба).
    private void OnWorldMapViewportWheel(WheelEvent evt)
    {
        if (worldMapViewport == null || worldMap == null)
            return;

        float direction = evt.delta.y > 0f ? -1f : 1f;
        float newZoom = Mathf.Clamp(
            worldMapZoom + direction * WorldMapZoomStep,
            WorldMapMinZoom,
            WorldMapMaxZoom);

        if (Mathf.Approximately(newZoom, worldMapZoom))
        {
            evt.StopPropagation();
            return;
        }

        Vector2 localPoint = worldMapViewport.WorldToLocal(evt.mousePosition);
        float canvasPointX = (localPoint.x - worldMapPanOffsetX) / worldMapZoom;
        float canvasPointY = (localPoint.y - worldMapPanOffsetY) / worldMapZoom;

        worldMapZoom = newZoom;
        worldMapPanOffsetX = localPoint.x - canvasPointX * worldMapZoom;
        worldMapPanOffsetY = localPoint.y - canvasPointY * worldMapZoom;

        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
        evt.StopPropagation();
    }

    // Панорамирование — средней кнопкой мыши, чтобы не конфликтовать с левым
    // кликом (сразу отдаёт приказ) и с ПКМ (осмотр локации на маркере, UI-M08).
    private void OnWorldMapViewportPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 2 || worldMapViewport == null)
            return;

        isPanningWorldMap = true;
        worldMapPanPointerStart = evt.position;
        worldMapPanOffsetStart = new Vector2(worldMapPanOffsetX, worldMapPanOffsetY);
        worldMapViewport.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnWorldMapViewportPointerMove(PointerMoveEvent evt)
    {
        if (!isPanningWorldMap)
            return;

        Vector2 delta = (Vector2)evt.position - worldMapPanPointerStart;
        worldMapPanOffsetX = worldMapPanOffsetStart.x + delta.x;
        worldMapPanOffsetY = worldMapPanOffsetStart.y + delta.y;

        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
        evt.StopPropagation();
    }

    private void OnWorldMapViewportPointerUp(PointerUpEvent evt)
    {
        if (!isPanningWorldMap || evt.button != 2)
            return;

        isPanningWorldMap = false;
        worldMapViewport.ReleasePointer(evt.pointerId);
    }

    private void OnWorldMapViewportPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        isPanningWorldMap = false;
    }

    // Не даёт карте (или пустому полю вокруг неё при zoom < 1) уехать за
    // пределы рамки просмотра; при zoom <= 1 карта центрируется в рамке.
    private void ClampWorldMapPan()
    {
        if (worldMapViewport == null)
            return;

        float viewportWidth = Mathf.Max(1f, worldMapViewport.resolvedStyle.width);
        float viewportHeight = Mathf.Max(1f, worldMapViewport.resolvedStyle.height);
        float canvasWidth = viewportWidth * worldMapZoom;
        float canvasHeight = viewportHeight * worldMapZoom;

        worldMapPanOffsetX = ClampWorldMapPanAxis(worldMapPanOffsetX, viewportWidth, canvasWidth);
        worldMapPanOffsetY = ClampWorldMapPanAxis(worldMapPanOffsetY, viewportHeight, canvasHeight);
    }

    private static float ClampWorldMapPanAxis(float offset, float viewportSize, float canvasSize)
    {
        if (canvasSize <= viewportSize)
            return (viewportSize - canvasSize) * 0.5f;

        float minOffset = viewportSize - canvasSize;
        return Mathf.Clamp(offset, minOffset, 0f);
    }

    private void ApplyWorldMapViewportTransform()
    {
        if (worldMap == null)
            return;

        worldMap.style.scale = new Scale(new Vector3(worldMapZoom, worldMapZoom, 1f));
        worldMap.style.left = new Length(worldMapPanOffsetX, LengthUnit.Pixel);
        worldMap.style.top = new Length(worldMapPanOffsetY, LengthUnit.Pixel);
    }
}
