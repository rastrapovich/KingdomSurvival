using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // WM-03: единый скроллируемый/зумируемый холст (не набор экранов-регионов —
    // раздел 9.9 канона). Зум/панорама — чисто визуальный слой поверх world-map:
    // все проценто-позиционированные дети (местность, локации, маршрут, армия)
    // не меняются, координатное преобразование делает сам UI Toolkit через
    // worldMap.style.scale/left/top + WorldToLocal при клике.
    //
    // AM-06 (раздел 9 инструкции по миграции): нижняя граница зума раньше была
    // фиксированной константой 0.75, которая не гарантирует, что вся карта
    // помещается в viewport на большом полотне — RecalculateWorldMapMinZoom
    // считает её от реального размера viewport ("вся карта помещается").
    // Запасное значение до первого GeometryChangedEvent.
    private const float WorldMapMinZoomFallback = 0.75f;
    // WM-13: раньше фиксированное число (10×), подобранное на глаз под одно
    // разрешение окна. Теперь maxZoom считается от реального размера
    // viewport в RecalculateWorldMapMaxZoom — критерий "одна клетка занимает
    // ~85-90% меньшей стороны экрана" не зависит от того, каким было окно
    // при подборе константы. Это значение — только запасной вариант до
    // первого GeometryChangedEvent.
    private const float WorldMapMaxZoomFallback = 10f;
    // Доля меньшей стороны viewport, которую должна занимать одна клетка на
    // максимальном приближении — середина требуемого диапазона 85-90%.
    private const float WorldMapMaxZoomCellFraction = 0.875f;
    // Мультипликативный шаг (±15% за нотч), а не аддитивный — даёт
    // равномерное ощущение зума независимо от того, насколько широк
    // фактический диапазон [WorldMapMinZoom; worldMapMaxZoom].
    private const float WorldMapZoomStepFactor = 1.15f;
    // WM-14: единый визуальный размер логической клетки в пикселях при
    // zoom=1. Конкретное число не имеет смысла само по себе (пропорции
    // компенсируются zoom/maxZoom) — важно только то, что ОДНО и то же
    // значение используется для ширины и высоты канваса, поэтому клетка
    // сетки всегда квадратная независимо от формы viewport.
    private const float WorldMapBaseCellSizePx = 32f;

    private float worldMapMinZoom = WorldMapMinZoomFallback;
    private float worldMapMaxZoom = WorldMapMaxZoomFallback;
    // AM-06: старт со средним масштабом вокруг Дома, а не с максимальным
    // приближением — игрок сразу видит географическое окружение Дома, а не
    // только его саму клетку (раздел 9 инструкции: "Нынешний принудительный
    // старт на максимальном приближении заменить"). Настоящее значение
    // считается в RecalculateWorldMapMinMaxZoom как среднее геометрическое
    // между min и max — до первого GeometryChangedEvent используется
    // запасной уровень между запасными min/max.
    private float worldMapZoom =
        (WorldMapMinZoomFallback + WorldMapMaxZoomFallback) * 0.5f;
    private float worldMapPanOffsetX;
    private float worldMapPanOffsetY;
    private bool isPanningWorldMap;
    private Vector2 worldMapPanPointerStart;
    private Vector2 worldMapPanOffsetStart;
    private bool worldMapInitialFocusApplied;
    // WM-14: собственный (не растянутый под viewport) размер .world-map при
    // zoom=1 — (GridWidth-1)/(GridHeight-1) клеток по WorldMapBaseCellSizePx.
    // Единственный источник истины для canvasWidth/canvasHeight, которые
    // раньше ошибочно принимались равными размеру viewport.
    private float worldMapCanvasWidth;
    private float worldMapCanvasHeight;

    private void InitializeWorldMapViewport()
    {
        if (worldMapViewport == null)
            return;

        ConfigureWorldMapCanvasSize();

        worldMapViewport.RegisterCallback<GeometryChangedEvent>(
            OnWorldMapViewportGeometryChanged);

        ApplyWorldMapViewportTransform();
    }

    // WM-14: canvasWidth/canvasHeight зависят только от размера сетки и
    // WorldMapBaseCellSizePx — не от viewport, поэтому это можно и нужно
    // выставить сразу, до первого GeometryChangedEvent (в отличие от
    // maxZoom/pan, которым реальный размер viewport обязателен).
    private void ConfigureWorldMapCanvasSize()
    {
        if (worldMap == null)
            return;

        worldMapCanvasWidth =
            (WorldMapNavigation.GridWidth - 1) * WorldMapBaseCellSizePx;
        worldMapCanvasHeight =
            (WorldMapNavigation.GridHeight - 1) * WorldMapBaseCellSizePx;

        worldMap.style.width = worldMapCanvasWidth;
        worldMap.style.height = worldMapCanvasHeight;
    }

    private void OnWorldMapViewportGeometryChanged(GeometryChangedEvent evt)
    {
        // WM-14/AM-06: порядок принципиален — сначала пересчитать min/max от
        // актуального размера viewport, и только потом (при первом входе)
        // ставить стартовый zoom и центрировать на нём.
        RecalculateWorldMapMinZoom();
        RecalculateWorldMapMaxZoom();

        // AM-06: старт со средним масштабом (геометрическое среднее min/max)
        // вокруг Дома — игрок сразу видит географическое окружение, а не
        // только клетку Дома (раздел 9 инструкции). При zoom > 1 канвас
        // крупнее viewport, и panOffset=(0,0) по умолчанию показывает левый
        // верхний угол карты — центрируем явно.
        if (!worldMapInitialFocusApplied)
        {
            worldMapZoom = Mathf.Sqrt(worldMapMinZoom * worldMapMaxZoom);
            CenterWorldMapOn(GetWorldMapHomeXPercent(), GetWorldMapHomeYPercent());
            worldMapInitialFocusApplied = true;
        }
        else
        {
            // Окно могло измениться (например, изменение размера панели) —
            // min/max могли сместиться, не даём текущему zoom выйти за
            // новые пределы.
            worldMapZoom = Mathf.Clamp(worldMapZoom, worldMapMinZoom, worldMapMaxZoom);
        }

        RefreshWorldMapZoomIndicator();

        // WM-12: на самом первом layout-проходе (до этого события) Button
        // измеряет свой текст/минимальный контент по исходной UXML-разметке
        // ("СТОЛИЦА" и т.п.), и Unity кэширует это измерение — просто задать
        // маленькие width/height/minWidth/minHeight в коде оказывается
        // недостаточно, пока не случится настоящий пересчёт geometry (тот же
        // класс проблемы, что и NaN-баг реки в WM-09). Пересобираем
        // столицу/армию здесь же, как только реальный размер известен.
        if (gameState != null && WorldMapElementsExist())
        {
            RefreshWorldMapCapital();
            RefreshWorldMapArmyMarker();
        }

        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
    }

    // AM-06 (раздел 9 инструкции): "вся карта помещается в viewport" —
    // computed вместо фиксированной константы 0.75, которая на большом
    // полотне (WM-11: карта в 16 раз больше по площади) не гарантирует
    // видимость всей карты целиком по кнопке "Вся карта".
    private void RecalculateWorldMapMinZoom()
    {
        if (worldMapViewport == null || worldMapCanvasWidth <= 0f || worldMapCanvasHeight <= 0f)
            return;

        float viewportWidth = Mathf.Max(1f, worldMapViewport.resolvedStyle.width);
        float viewportHeight = Mathf.Max(1f, worldMapViewport.resolvedStyle.height);

        worldMapMinZoom = Mathf.Min(
            viewportWidth / worldMapCanvasWidth,
            viewportHeight / worldMapCanvasHeight);
    }

    // AM-06: единственный источник координат Дома для новых элементов
    // управления камерой ("К Дому", стартовый фокус). Не заменяет
    // WorldMapNavigation.CapitalXPercent/YPercent там, где эти константы уже
    // читают существующие 16 файлов (см. AM-01) — только новый код здесь.
    private float GetWorldMapHomeXPercent()
    {
        WorldMapDatabaseAsset database = WorldMapVisualRuntime.LoadDatabase();
        return database != null && database.ActiveWorld != null
            ? database.ActiveWorld.HomeXPercent
            : WorldMapNavigation.CapitalXPercent;
    }

    private float GetWorldMapHomeYPercent()
    {
        WorldMapDatabaseAsset database = WorldMapVisualRuntime.LoadDatabase();
        return database != null && database.ActiveWorld != null
            ? database.ActiveWorld.HomeYPercent
            : WorldMapNavigation.CapitalYPercent;
    }

    // WM-14: цель — одна (теперь всегда квадратная) клетка занимает
    // ~85-90% МЕНЬШЕЙ стороны viewport на максимальном приближении.
    // baseCellSizePx — фактический пиксельный размер клетки при zoom=1,
    // выведенный из canvasWidth (а не повторно из константы), чтобы формула
    // оставалась верной, даже если ConfigureWorldMapCanvasSize когда-нибудь
    // станет считать канвас иначе.
    private void RecalculateWorldMapMaxZoom()
    {
        if (worldMapViewport == null || worldMapCanvasWidth <= 0f)
            return;

        float viewportWidth = Mathf.Max(1f, worldMapViewport.resolvedStyle.width);
        float viewportHeight = Mathf.Max(1f, worldMapViewport.resolvedStyle.height);

        float baseCellSizePx =
            worldMapCanvasWidth / (WorldMapNavigation.GridWidth - 1);
        float targetVisibleCellPx =
            WorldMapMaxZoomCellFraction * Mathf.Min(viewportWidth, viewportHeight);

        worldMapMaxZoom = Mathf.Max(
            worldMapMinZoom,
            targetVisibleCellPx / baseCellSizePx);
    }

    // WM-14: центрируем по РЕАЛЬНОМУ размеру канваса (canvasWidth/Height), а
    // не по размеру viewport — до этой правки формула молча предполагала
    // canvasSize == viewportSize, что было правдой только пока .world-map
    // был растянут на 100%/100% viewport (и именно это растяжение и
    // деформировало клетки — WM-14 отменяет его).
    private void CenterWorldMapOn(float xPercent, float yPercent)
    {
        if (worldMapViewport == null || worldMapCanvasWidth <= 0f)
            return;

        float viewportWidth = Mathf.Max(1f, worldMapViewport.resolvedStyle.width);
        float viewportHeight = Mathf.Max(1f, worldMapViewport.resolvedStyle.height);

        float targetPixelX = xPercent / 100f * worldMapCanvasWidth * worldMapZoom;
        float targetPixelY = yPercent / 100f * worldMapCanvasHeight * worldMapZoom;

        worldMapPanOffsetX = viewportWidth * 0.5f - targetPixelX;
        worldMapPanOffsetY = viewportHeight * 0.5f - targetPixelY;
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

        float factor = evt.delta.y > 0f
            ? 1f / WorldMapZoomStepFactor
            : WorldMapZoomStepFactor;

        Vector2 localPoint = worldMapViewport.WorldToLocal(evt.mousePosition);
        if (ZoomWorldMapAroundScreenPoint(factor, localPoint))
            evt.StopPropagation();
    }

    // AM-06: общая точка входа для колеса мыши и кнопок +/- ("Зум — до 10×,
    // мультипликативный шаг" остаётся неизменным правилом). pivotScreenPoint
    // — точка (в координатах viewport), которая должна остаться на месте на
    // экране; кнопки передают центр viewport.
    private bool ZoomWorldMapAroundScreenPoint(float factor, Vector2 pivotScreenPoint)
    {
        if (worldMapViewport == null || worldMap == null)
            return false;

        float newZoom = Mathf.Clamp(
            worldMapZoom * factor,
            worldMapMinZoom,
            worldMapMaxZoom);

        if (Mathf.Approximately(newZoom, worldMapZoom))
            return false;

        float canvasPointX = (pivotScreenPoint.x - worldMapPanOffsetX) / worldMapZoom;
        float canvasPointY = (pivotScreenPoint.y - worldMapPanOffsetY) / worldMapZoom;

        worldMapZoom = newZoom;
        worldMapPanOffsetX = pivotScreenPoint.x - canvasPointX * worldMapZoom;
        worldMapPanOffsetY = pivotScreenPoint.y - canvasPointY * worldMapZoom;

        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
        RefreshWorldMapZoomIndicator();
        return true;
    }

    private void OnWorldMapZoomInButtonClicked() => ZoomWorldMapAroundViewportCenter(WorldMapZoomStepFactor);

    private void OnWorldMapZoomOutButtonClicked() =>
        ZoomWorldMapAroundViewportCenter(1f / WorldMapZoomStepFactor);

    private void ZoomWorldMapAroundViewportCenter(float factor)
    {
        if (worldMapViewport == null)
            return;

        Vector2 center = new Vector2(
            worldMapViewport.resolvedStyle.width * 0.5f,
            worldMapViewport.resolvedStyle.height * 0.5f);
        ZoomWorldMapAroundScreenPoint(factor, center);
    }

    // "Вся карта" — раздел 7 инструкции: минимальный зум, при котором вся
    // карта помещается в viewport, с центрированием.
    private void OnWorldMapFitButtonClicked()
    {
        worldMapZoom = worldMapMinZoom;
        CenterWorldMapOn(50f, 50f);
        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
        RefreshWorldMapZoomIndicator();
    }

    private void OnWorldMapFocusHomeButtonClicked()
    {
        CenterWorldMapOn(GetWorldMapHomeXPercent(), GetWorldMapHomeYPercent());
        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
    }

    private void OnWorldMapFocusHeroButtonClicked()
    {
        if (gameState == null)
            return;

        float heroX = GetWorldMapHomeXPercent();
        float heroY = GetWorldMapHomeYPercent();

        if (gameState.HasActiveExpedition)
        {
            heroX = gameState.ActiveExpedition.CurrentMapXPercent;
            heroY = gameState.ActiveExpedition.CurrentMapYPercent;
        }

        CenterWorldMapOn(heroX, heroY);
        ClampWorldMapPan();
        ApplyWorldMapViewportTransform();
    }

    private void RefreshWorldMapZoomIndicator()
    {
        if (worldMapZoomIndicatorLabel == null || worldMapMaxZoom <= 0f)
            return;

        // Компактный индикатор — доля от максимального приближения, не
        // "голое" число zoom (которое само по себе ничего не говорит
        // игроку, см. §6 инструкции: "компактный индикатор масштаба").
        float percent = Mathf.Clamp01(worldMapZoom / worldMapMaxZoom) * 100f;
        worldMapZoomIndicatorLabel.text = Mathf.RoundToInt(percent) + "%";
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
    // WM-14: масштабируем реальный размер канваса (worldMapCanvasWidth/
    // Height), а не размер viewport — раньше формула молча предполагала их
    // равенство.
    private void ClampWorldMapPan()
    {
        if (worldMapViewport == null || worldMapCanvasWidth <= 0f)
            return;

        float viewportWidth = Mathf.Max(1f, worldMapViewport.resolvedStyle.width);
        float viewportHeight = Mathf.Max(1f, worldMapViewport.resolvedStyle.height);
        float scaledCanvasWidth = worldMapCanvasWidth * worldMapZoom;
        float scaledCanvasHeight = worldMapCanvasHeight * worldMapZoom;

        worldMapPanOffsetX = ClampWorldMapPanAxis(worldMapPanOffsetX, viewportWidth, scaledCanvasWidth);
        worldMapPanOffsetY = ClampWorldMapPanAxis(worldMapPanOffsetY, viewportHeight, scaledCanvasHeight);
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

        RefreshWorldMapZoomCompensatedVisuals();
    }
}
