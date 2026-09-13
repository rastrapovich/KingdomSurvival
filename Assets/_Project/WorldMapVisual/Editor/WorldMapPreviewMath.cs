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
}
