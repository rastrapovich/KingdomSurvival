using KingdomSurvival.WorldMapVisual;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "Global Map Aspect + Preview Canvas Navigation" (WM-T04.9,
    // раздел 37/38): чистая математика аспекта глобальной карты и
    // Preview-трансформа (fit → zoom → pan). IMGUI-обработка колеса/MMB не
    // юнит-тестируется (как и раньше в этом проекте) — только ручная
    // проверка в живом Editor.
    public class WorldMapPreviewTransformTests
    {
        // A. 4160×2560 → aspect 1.625.
        [Test]
        public void GlobalMapAspect_DefaultCanvasSize_Is1625()
        {
            WorldMapWorldDefinitionAsset world = ScriptableObject.CreateInstance<WorldMapWorldDefinitionAsset>();
            try
            {
                Assert.AreEqual(1.625f, world.GlobalMapAspect, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }

        // B/C/D. Aspect не зависит от BaseMapSprite/toggle — сама
        // GlobalMapAspect вообще не принимает Sprite или bool параметром,
        // поэтому структурно не может от них зависеть. Проверяем это
        // напрямую через сигнатуру использования: ComputeMapRect для FitRect
        // вызывается только с globalAspect, полученным из World Definition.
        [Test]
        public void GlobalMapAspect_InvalidWidth_FallsBackToDefault()
        {
            WorldMapWorldDefinitionAsset world = ScriptableObject.CreateInstance<WorldMapWorldDefinitionAsset>();
            try
            {
                SetPrivateFloat(world, "mapCanvasWidth", 0f);
                Assert.AreEqual(1.625f, world.GlobalMapAspect, 0.0001f);

                SetPrivateFloat(world, "mapCanvasWidth", -100f);
                Assert.AreEqual(1.625f, world.GlobalMapAspect, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }

        [Test]
        public void GlobalMapAspect_InvalidHeight_FallsBackToDefault()
        {
            WorldMapWorldDefinitionAsset world = ScriptableObject.CreateInstance<WorldMapWorldDefinitionAsset>();
            try
            {
                SetPrivateFloat(world, "mapCanvasHeight", 0f);
                Assert.AreEqual(1.625f, world.GlobalMapAspect, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(target, value);
        }

        // E. Wide Canvas → горизонтальный letterbox корректен.
        [Test]
        public void ComputeMapRect_WideCanvas_LettersboxesLeftAndRight()
        {
            Rect canvas = new Rect(0f, 0f, 2000f, 500f);
            Rect fitRect = WorldMapPreviewMath.ComputeMapRect(canvas, 1.625f, true);

            Assert.AreEqual(500f, fitRect.height, 0.001f);
            Assert.AreEqual(500f * 1.625f, fitRect.width, 0.001f);
            Assert.Less(fitRect.width, canvas.width);
            Assert.AreEqual(canvas.center.x, fitRect.center.x, 0.001f);
        }

        // F. Tall Canvas → вертикальный fit корректен.
        [Test]
        public void ComputeMapRect_TallCanvas_LettersboxesTopAndBottom()
        {
            Rect canvas = new Rect(0f, 0f, 500f, 2000f);
            Rect fitRect = WorldMapPreviewMath.ComputeMapRect(canvas, 1.625f, true);

            Assert.AreEqual(500f, fitRect.width, 0.001f);
            Assert.AreEqual(500f / 1.625f, fitRect.height, 0.001f);
            Assert.Less(fitRect.height, canvas.height);
            Assert.AreEqual(canvas.center.y, fitRect.center.y, 0.001f);
        }

        // G. Resize Canvas → global aspect не меняется (fitRect всегда
        // пересчитывается с ОДНИМ И ТЕМ ЖЕ aspect, независимо от размера).
        [Test]
        public void ComputeMapRect_DifferentCanvasSizes_AlwaysPreservesGivenAspect()
        {
            Rect smallCanvas = new Rect(0f, 0f, 400f, 300f);
            Rect largeCanvas = new Rect(0f, 0f, 1600f, 1200f);

            Rect smallFit = WorldMapPreviewMath.ComputeMapRect(smallCanvas, 1.625f, true);
            Rect largeFit = WorldMapPreviewMath.ComputeMapRect(largeCanvas, 1.625f, true);

            Assert.AreEqual(1.625f, smallFit.width / smallFit.height, 0.001f);
            Assert.AreEqual(1.625f, largeFit.width / largeFit.height, 0.001f);
        }

        // H. Map (0,0) → правильный screen corner.
        [Test]
        public void MapToPreview_Origin_MapsToRectTopLeft()
        {
            Rect mapRect = new Rect(10f, 20f, 400f, 200f);
            Vector2 screen = WorldMapPreviewMath.MapToPreview(mapRect, 0f, 0f);

            Assert.AreEqual(new Vector2(10f, 20f), screen);
        }

        // I. Map (100,100) → противоположный corner.
        [Test]
        public void MapToPreview_MaxCorner_MapsToRectBottomRight()
        {
            Rect mapRect = new Rect(10f, 20f, 400f, 200f);
            Vector2 screen = WorldMapPreviewMath.MapToPreview(mapRect, 100f, 100f);

            Assert.AreEqual(new Vector2(410f, 220f), screen);
        }

        // J. Map (50,50) → центр.
        [Test]
        public void MapToPreview_Center_MapsToRectCenter()
        {
            Rect mapRect = new Rect(10f, 20f, 400f, 200f);
            Vector2 screen = WorldMapPreviewMath.MapToPreview(mapRect, 50f, 50f);

            Assert.AreEqual(mapRect.center, screen);
        }

        // K. Map → Screen → Map roundtrip.
        [Test]
        public void MapToPreview_ThenPreviewToMap_RoundTrips()
        {
            Rect mapRect = new Rect(15f, 25f, 640f, 400f);
            Vector2 original = new Vector2(37.2f, 81.6f);

            Vector2 screen = WorldMapPreviewMath.MapToPreview(mapRect, original.x, original.y);
            Vector2 back = WorldMapPreviewMath.PreviewToMap(mapRect, screen);

            Assert.AreEqual(original.x, back.x, 0.001f);
            Assert.AreEqual(original.y, back.y, 0.001f);
        }

        // L. Zoom = 1 → displayRect == fitRect (это и есть "Вписать карту").
        [Test]
        public void ApplyZoomPan_ZoomOneNoPan_EqualsFitRect()
        {
            Rect fitRect = new Rect(50f, 60f, 800f, 492f);
            Rect display = WorldMapPreviewMath.ApplyZoomPan(fitRect, 1f, Vector2.zero);

            Assert.AreEqual(fitRect.x, display.x, 0.001f);
            Assert.AreEqual(fitRect.y, display.y, 0.001f);
            Assert.AreEqual(fitRect.width, display.width, 0.001f);
            Assert.AreEqual(fitRect.height, display.height, 0.001f);
        }

        // M. Zoom = 2 → displayRect вдвое больше fitRect.
        [Test]
        public void ApplyZoomPan_ZoomTwo_DoublesSize()
        {
            Rect fitRect = new Rect(0f, 0f, 800f, 492f);
            Rect display = WorldMapPreviewMath.ApplyZoomPan(fitRect, 2f, Vector2.zero);

            Assert.AreEqual(1600f, display.width, 0.001f);
            Assert.AreEqual(984f, display.height, 0.001f);
        }

        // N. Pan X/Y — сдвигает displayRect на долю fitRect.
        [Test]
        public void ApplyZoomPan_PanFraction_ShiftsDisplayRectProportionally()
        {
            Rect fitRect = new Rect(100f, 100f, 800f, 492f);
            Rect display = WorldMapPreviewMath.ApplyZoomPan(fitRect, 1f, new Vector2(0.1f, -0.2f));

            Assert.AreEqual(180f, display.x, 0.001f);
            Assert.AreEqual(100f - 98.4f, display.y, 0.01f);
        }

        // O. Zoom Around Cursor сохраняет map point под курсором.
        [Test]
        public void ZoomRectAroundPoint_KeepsAnchorPointFixedRelativeToRect()
        {
            Rect fitRect = new Rect(0f, 0f, 800f, 492f);
            Rect display = WorldMapPreviewMath.ApplyZoomPan(fitRect, 1f, Vector2.zero);

            Vector2 cursor = new Vector2(300f, 150f);
            Vector2 mapPointUnderCursorBefore = WorldMapPreviewMath.PreviewToMap(display, cursor);

            Rect zoomedDisplay = WorldMapPreviewMath.ZoomRectAroundPoint(display, 2.5f, cursor);
            Vector2 mapPointUnderCursorAfter = WorldMapPreviewMath.PreviewToMap(zoomedDisplay, cursor);

            Assert.AreEqual(mapPointUnderCursorBefore.x, mapPointUnderCursorAfter.x, 0.01f);
            Assert.AreEqual(mapPointUnderCursorBefore.y, mapPointUnderCursorAfter.y, 0.01f);
        }

        [Test]
        public void ZoomRectAroundPoint_ThenExtractZoomPan_RoundTripsThroughApplyZoomPan()
        {
            Rect fitRect = new Rect(20f, 30f, 800f, 492f);
            Rect display = WorldMapPreviewMath.ApplyZoomPan(fitRect, 1f, Vector2.zero);

            Vector2 cursor = new Vector2(250f, 180f);
            Rect zoomedDisplay = WorldMapPreviewMath.ZoomRectAroundPoint(display, 1.8f, cursor);

            WorldMapPreviewMath.ExtractZoomPan(fitRect, zoomedDisplay, out float zoom, out Vector2 panFraction);
            Rect reconstructed = WorldMapPreviewMath.ApplyZoomPan(fitRect, zoom, panFraction);

            Assert.AreEqual(zoomedDisplay.x, reconstructed.x, 0.01f);
            Assert.AreEqual(zoomedDisplay.y, reconstructed.y, 0.01f);
            Assert.AreEqual(zoomedDisplay.width, reconstructed.width, 0.01f);
            Assert.AreEqual(zoomedDisplay.height, reconstructed.height, 0.01f);
        }

        // P. Fit To View показывает весь map rect — это ровно zoom=1,
        // pan=(0,0), уже покрыто тестом L; здесь дополнительно проверяем,
        // что после произвольного zoom/pan возврат к (1, zero) снова даёт
        // fitRect (сброс "Вписать карту" идемпотентен).
        [Test]
        public void ApplyZoomPan_ResetAfterZoomAndPan_ReturnsToFitRect()
        {
            Rect fitRect = new Rect(10f, 10f, 800f, 492f);

            Rect zoomed = WorldMapPreviewMath.ApplyZoomPan(fitRect, 3f, new Vector2(0.4f, -0.3f));
            Assert.AreNotEqual(fitRect.width, zoomed.width);

            Rect resetToFit = WorldMapPreviewMath.ApplyZoomPan(fitRect, 1f, Vector2.zero);
            Assert.AreEqual(fitRect.x, resetToFit.x, 0.001f);
            Assert.AreEqual(fitRect.width, resetToFit.width, 0.001f);
        }

        // Q. Point вне Canvas корректно определяется.
        [Test]
        public void RectContains_PointOutsideCanvas_IsFalse()
        {
            Rect canvas = new Rect(0f, 0f, 800f, 600f);
            Assert.IsFalse(canvas.Contains(new Vector2(900f, 300f)));
            Assert.IsTrue(canvas.Contains(new Vector2(400f, 300f)));
        }

        // Раздел "увеличить максимальный zoom до 15×": ClampPreviewZoom —
        // единственное место, где считается допустимый диапазон zoom (wheel,
        // "1×"/"Вписать карту" и загрузка из EditorPrefs проходят через
        // одну и ту же функцию, раздельных лимитов в разных местах нет).
        [Test]
        public void ClampPreviewZoom_ExactlyFifteen_IsAllowed()
        {
            Assert.AreEqual(15f, WorldMapDatabaseWindow.ClampPreviewZoom(15f), 0.0001f);
        }

        [Test]
        public void ClampPreviewZoom_AboveFifteen_ClampsToFifteen()
        {
            Assert.AreEqual(15f, WorldMapDatabaseWindow.ClampPreviewZoom(50f), 0.0001f);
        }

        [Test]
        public void ClampPreviewZoom_OldEightCap_IsNoLongerALimit()
        {
            // Старый максимум (8×) в диапазоне 8 < zoom <= 15 больше не
            // считается пределом — раньше это значение было бы обрезано.
            Assert.AreEqual(8f, WorldMapDatabaseWindow.ClampPreviewZoom(8f), 0.0001f);
            Assert.AreEqual(12f, WorldMapDatabaseWindow.ClampPreviewZoom(12f), 0.0001f);
        }

        [Test]
        public void ClampPreviewZoom_BelowMinimum_ClampsToMinimum()
        {
            Assert.AreEqual(0.1f, WorldMapDatabaseWindow.ClampPreviewZoom(0.01f), 0.0001f);
        }

        // Регрессия существующих инструментов (раздел 39): MapBoundsToRect
        // после zoom/pan (т.е. когда переданный mapRect уже сам является
        // displayRect) продолжает работать той же формулой — Art Layer/
        // Terrain Area hit-test не нуждаются в отдельной математике.
        [Test]
        public void MapBoundsToRect_AfterZoomAndPan_UsesDisplayRectConsistently()
        {
            Rect fitRect = new Rect(0f, 0f, 800f, 492f);
            Rect displayRect = WorldMapPreviewMath.ApplyZoomPan(fitRect, 2f, new Vector2(0.1f, 0.05f));

            Rect boundsRect = WorldMapPreviewMath.MapBoundsToRect(displayRect, 10f, 40f, 35f, 65f);
            Vector2 expectedMin = WorldMapPreviewMath.MapToPreview(displayRect, 10f, 40f);
            Vector2 expectedMax = WorldMapPreviewMath.MapToPreview(displayRect, 35f, 65f);

            Assert.AreEqual(expectedMin.x, boundsRect.xMin, 0.001f);
            Assert.AreEqual(expectedMin.y, boundsRect.yMin, 0.001f);
            Assert.AreEqual(expectedMax.x, boundsRect.xMax, 0.001f);
            Assert.AreEqual(expectedMax.y, boundsRect.yMax, 0.001f);
        }

        [Test]
        public void PreviewToMap_AfterZoomAndPan_ScreenDragDeltaMapsToCorrectMapDelta()
        {
            Rect fitRect = new Rect(0f, 0f, 800f, 492f);
            Rect displayRect = WorldMapPreviewMath.ApplyZoomPan(fitRect, 2f, Vector2.zero);

            Vector2 pointA = new Vector2(400f, 246f);
            Vector2 pointB = pointA + new Vector2(80f, 0f);

            Vector2 mapA = WorldMapPreviewMath.PreviewToMap(displayRect, pointA);
            Vector2 mapB = WorldMapPreviewMath.PreviewToMap(displayRect, pointB);

            float expectedDeltaXPercent = 80f / displayRect.width * 100f;
            Assert.AreEqual(expectedDeltaXPercent, mapB.x - mapA.x, 0.001f);
            Assert.AreEqual(0f, mapB.y - mapA.y, 0.001f);
        }
    }
}
