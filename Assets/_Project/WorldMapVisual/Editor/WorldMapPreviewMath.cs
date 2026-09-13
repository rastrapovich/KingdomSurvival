using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Задача "Preview + редактирование дорог поверх арта": единственное
    // место, где считается прямоугольник карты внутри Preview-панели и
    // преобразование координат карты (проценты 0..100, Y вниз — та же
    // ориентация, что уже использует Location/TerrainArea/SpawnSlot) в
    // экранные координаты Preview и обратно. Чистая математика, без
    // UnityEditor-типов — тестируется напрямую из EditMode-тестов.
    public static class WorldMapPreviewMath
    {
        // spriteAspect <= 0 или showArt == false — арт не показываем, весь
        // previewArea используется как карта (прежнее поведение до этой
        // задачи, без леттербоксинга).
        public static Rect ComputeMapRect(Rect previewArea, float spriteAspect, bool showArt)
        {
            if (!showArt || spriteAspect <= 0f ||
                previewArea.width <= 0f || previewArea.height <= 0f)
            {
                return previewArea;
            }

            float areaAspect = previewArea.width / previewArea.height;

            float width;
            float height;
            if (spriteAspect > areaAspect)
            {
                width = previewArea.width;
                height = width / spriteAspect;
            }
            else
            {
                height = previewArea.height;
                width = height * spriteAspect;
            }

            float x = previewArea.x + (previewArea.width - width) * 0.5f;
            float y = previewArea.y + (previewArea.height - height) * 0.5f;
            return new Rect(x, y, width, height);
        }

        public static Vector2 MapToPreview(Rect mapRect, float xPercent, float yPercent)
        {
            return new Vector2(
                mapRect.x + mapRect.width * xPercent / 100f,
                mapRect.y + mapRect.height * yPercent / 100f);
        }

        public static Vector2 PreviewToMap(Rect mapRect, Vector2 screenPoint)
        {
            if (mapRect.width <= 0f || mapRect.height <= 0f)
                return Vector2.zero;

            return new Vector2(
                (screenPoint.x - mapRect.x) / mapRect.width * 100f,
                (screenPoint.y - mapRect.y) / mapRect.height * 100f);
        }

        public static Vector2 PreviewToMapClamped(Rect mapRect, Vector2 screenPoint)
        {
            Vector2 point = PreviewToMap(mapRect, screenPoint);
            return new Vector2(
                Mathf.Clamp(point.x, 0f, 100f),
                Mathf.Clamp(point.y, 0f, 100f));
        }

        // Задача "Map Art Layers": прямоугольник, который в Preview занимает
        // один авторский Art Layer с данным Bounds (проценты карты) —
        // строится через те же две точки, что и MapToPreview, поэтому не
        // может разойтись с остальной математикой Preview. Не зависит от
        // порядка min/max — оба угла нормализуются в Rect.MinMaxRect.
        public static Rect MapBoundsToRect(
            Rect mapRect, float minXPercent, float minYPercent, float maxXPercent, float maxYPercent)
        {
            Vector2 a = MapToPreview(mapRect, minXPercent, minYPercent);
            Vector2 b = MapToPreview(mapRect, maxXPercent, maxYPercent);
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }

    // Задача "Map Art Layers" — Bounds одного слоя в координатах карты
    // (0..100%). Отдельная от Rect структура — Rect.x/y/width/height легко
    // спутать с min/max при работе с Bounds карты, а не экрана.
    public struct MapBounds
    {
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public MapBounds(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;
    }

    public enum ArtLayerCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    // Задача "Map Art Layers" (WM-T04.7), расширено в WM-T04.8 для Terrain
    // Areas: прямое перетаскивание/resize/создание прямоугольника в Preview.
    // Чистая математика, без UnityEditor-типов — тестируется напрямую.
    // Используется только Editor-стороной; runtime не меняется — после
    // drag меняются только сериализованные Bounds в ассете, существующий
    // рендерер их просто читает. Общая для Art Layers И Terrain Areas —
    // раздел 26 задачи WM-T04.8 явно требует переиспользовать, а не
    // дублировать эту математику.
    public static class WorldMapArtLayerBoundsMath
    {
        // Задача "Terrain Area Authoring" (WM-T04.8, раздел 7): создание
        // новой зоны протягиванием — две map-space точки (в любом порядке,
        // drag может идти в любую сторону) нормализуются в валидный
        // MapBounds. Не клампит и не проверяет минимальный размер сама —
        // это отдельная ответственность вызывающего кода (drag-threshold,
        // ClampBoundsToMap).
        public static MapBounds NormalizeBoundsFromTwoPoints(Vector2 pointA, Vector2 pointB)
        {
            return new MapBounds(
                Mathf.Min(pointA.x, pointB.x), Mathf.Min(pointA.y, pointB.y),
                Mathf.Max(pointA.x, pointB.x), Mathf.Max(pointA.y, pointB.y));
        }

        // Сдвигает прямоугольник целиком так, чтобы он остался внутри
        // 0..100 по обеим осям, не меняя Width/Height (раздел 8 задачи —
        // "не обрезать размер, а сдвинуть"). Если Width/Height сам больше
        // 100 (патологический случай), финальный Clamp может слегка урезать
        // размер — это единственный путь остаться в пределах карты.
        public static MapBounds ClampBoundsToMap(MapBounds bounds, float mapMin = 0f, float mapMax = 100f)
        {
            float minX = bounds.MinX, maxX = bounds.MaxX;
            if (maxX > mapMax) { minX -= maxX - mapMax; maxX = mapMax; }
            if (minX < mapMin) { maxX -= minX - mapMin; minX = mapMin; }
            minX = Mathf.Max(mapMin, minX);
            maxX = Mathf.Min(mapMax, maxX);

            float minY = bounds.MinY, maxY = bounds.MaxY;
            if (maxY > mapMax) { minY -= maxY - mapMax; maxY = mapMax; }
            if (minY < mapMin) { maxY -= minY - mapMin; minY = mapMin; }
            minY = Mathf.Max(mapMin, minY);
            maxY = Mathf.Min(mapMax, maxY);

            return new MapBounds(minX, minY, maxX, maxY);
        }

        // Раздел 6/7 задачи: считает от НЕИЗМЕННОГО originalBounds + суммарной
        // дельты от точки MouseDown, а не добавляет маленькую дельту каждый
        // кадр — исключает накопление плавающей ошибки за долгий drag.
        public static MapBounds MoveBounds(MapBounds originalBounds, float deltaX, float deltaY)
        {
            MapBounds moved = new MapBounds(
                originalBounds.MinX + deltaX, originalBounds.MinY + deltaY,
                originalBounds.MaxX + deltaX, originalBounds.MaxY + deltaY);
            return ClampBoundsToMap(moved);
        }

        // Раздел 9/10/12 задачи: тянем один угол, противоположный — anchor,
        // неподвижен. Ведущая ось — X (ширина, посчитанная из перемещения
        // угла); высота при preserveAspect выводится из ширины через
        // aspectRatio спрайта (Sprite.rect, не вся Texture — раздел 25).
        // newCornerMapPoint — АБСОЛЮТНАЯ map-точка курсора (originalCorner +
        // суммарная дельта от MouseDown), не дельта за кадр — та же защита
        // от дрифта, что и у MoveBounds.
        public static MapBounds ResizeBoundsFromCorner(
            MapBounds originalBounds,
            ArtLayerCorner corner,
            Vector2 newCornerMapPoint,
            bool preserveAspect,
            float aspectRatio,
            float minSizePercent)
        {
            float cornerX = Mathf.Clamp(newCornerMapPoint.x, 0f, 100f);
            float cornerY = Mathf.Clamp(newCornerMapPoint.y, 0f, 100f);

            bool movingIsLeft = corner == ArtLayerCorner.TopLeft || corner == ArtLayerCorner.BottomLeft;
            bool movingIsTop = corner == ArtLayerCorner.TopLeft || corner == ArtLayerCorner.TopRight;

            float anchorX = movingIsLeft ? originalBounds.MaxX : originalBounds.MinX;
            float anchorY = movingIsTop ? originalBounds.MaxY : originalBounds.MinY;

            float width = Mathf.Max(minSizePercent, Mathf.Abs(cornerX - anchorX));
            float height = preserveAspect && aspectRatio > 0f
                ? Mathf.Max(minSizePercent, width / aspectRatio)
                : Mathf.Max(minSizePercent, Mathf.Abs(cornerY - anchorY));

            float minX = movingIsLeft ? anchorX - width : anchorX;
            float maxX = movingIsLeft ? anchorX : anchorX + width;
            float minY = movingIsTop ? anchorY - height : anchorY;
            float maxY = movingIsTop ? anchorY : anchorY + height;

            return ClampBoundsToMap(new MapBounds(minX, minY, maxX, maxY));
        }
    }
}
