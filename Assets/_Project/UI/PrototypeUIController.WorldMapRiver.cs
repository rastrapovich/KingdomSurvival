using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapRiverGeometryCallbackRegistered;

    // WM-09: река рисуется лентой сегментов в UI Toolkit (не GameObject-based
    // Sprite Shape — карта целиком остаётся на UI Toolkit, решение WM-03) —
    // каждый сегмент соединяет две соседние точки пути (WorldMapNavigation.
    // GetRiverPath) и поворачивается по направлению. Только визуальная
    // география: на проходимость/скорость река здесь не влияет.
    private void DrawRiver()
    {
        if (worldMapWater == null)
            return;

        WorldMapVisualTheme theme = WorldMapVisualRuntime.LoadActiveTheme();

        if (theme == null)
            return;

        IReadOnlyList<(int X, int Y)> path = WorldMapNavigation.GetRiverPath();

        if (path == null || path.Count < 2)
            return;

        // Проценты X и Y считаются от разных осей контейнера (ширина/высота).
        // На не квадратном viewport (а он почти никогда не квадратный) прямое
        // sqrt(dx%^2+dy%^2)/atan2(dy%,dx%) даёт геометрически неверные угол и
        // длину — сегменты не совпадают в стыках. Поэтому переводим разницу
        // в реальные пиксели контейнера перед тем как считать угол/длину.
        float containerWidth = worldMapWater.resolvedStyle.width;
        float containerHeight = worldMapWater.resolvedStyle.height;

        // До первого layout-прохода (самый первый кадр) resolvedStyle может
        // быть NaN, а не 0 — Mathf.Max(1f, NaN) не спасает (сравнение с NaN
        // всегда false, значение "проваливается" как есть). Если размер ещё
        // не посчитан, тихо выходим и один раз подписываемся на
        // GeometryChangedEvent, чтобы перерисовать реку, как только реальный
        // размер появится — вместо NaN-сегментов на первом кадре.
        if (float.IsNaN(containerWidth) || float.IsNaN(containerHeight) ||
            containerWidth <= 1f || containerHeight <= 1f)
        {
            if (!worldMapRiverGeometryCallbackRegistered)
            {
                worldMapWater.RegisterCallback<GeometryChangedEvent>(
                    OnWorldMapWaterGeometryChanged);
                worldMapRiverGeometryCallbackRegistered = true;
            }

            return;
        }

        WorldMapWaterVisualProfile water = theme.Water;

        for (int i = 1; i < path.Count; i++)
        {
            float x1 = GridXToPercent(path[i - 1].X);
            float y1 = GridYToPercent(path[i - 1].Y);
            float x2 = GridXToPercent(path[i].X);
            float y2 = GridYToPercent(path[i].Y);

            float pixelDx = (x2 - x1) / 100f * containerWidth;
            float pixelDy = (y2 - y1) / 100f * containerHeight;
            float lengthPixels = Mathf.Sqrt(pixelDx * pixelDx + pixelDy * pixelDy);

            if (lengthPixels <= 0.1f)
                continue;

            float angleDegrees = Mathf.Atan2(pixelDy, pixelDx) * Mathf.Rad2Deg;

            VisualElement segment = new VisualElement();
            segment.AddToClassList("world-map-river-segment");
            segment.pickingMode = PickingMode.Ignore;

            if (water.SegmentSprite != null)
                segment.style.backgroundImage = new StyleBackground(water.SegmentSprite);
            else
                segment.style.backgroundColor = water.FallbackColor;

            segment.style.height = new Length(water.WidthPixels, LengthUnit.Pixel);
            segment.style.marginTop = new Length(-water.WidthPixels * 0.5f, LengthUnit.Pixel);
            segment.style.width = new Length(lengthPixels, LengthUnit.Pixel);
            segment.style.left = new Length(x1, LengthUnit.Percent);
            segment.style.top = new Length(y1, LengthUnit.Percent);
            segment.style.rotate = new Rotate(new Angle(angleDegrees));

            worldMapWater.Add(segment);
        }
    }

    private void OnWorldMapWaterGeometryChanged(GeometryChangedEvent evt)
    {
        worldMapWater.Clear();
        DrawRiver();
    }
}
