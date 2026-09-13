using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "Preview + редактирование дорог поверх арта" (раздел 28):
    // тестируется только чистая математика WorldMapPreviewMath, не
    // IMGUI-обработка мыши/drag — это отдельный, нетестируемый юнит-тестами
    // слой, проверяемый вручную в живом редакторе.
    public class WorldMapPreviewMathTests
    {
        [Test]
        public void ComputeMapRect_WideSpriteInTallPreview_LettersboxesTopAndBottom()
        {
            Rect previewArea = new Rect(0f, 0f, 200f, 200f);
            Rect mapRect = WorldMapPreviewMath.ComputeMapRect(previewArea, 2f, true);

            Assert.AreEqual(200f, mapRect.width, 0.01f);
            Assert.AreEqual(100f, mapRect.height, 0.01f);
            Assert.AreEqual(0f, mapRect.x, 0.01f);
            Assert.AreEqual(50f, mapRect.y, 0.01f);
        }

        [Test]
        public void ComputeMapRect_TallSpriteInWidePreview_LettersboxesLeftAndRight()
        {
            Rect previewArea = new Rect(0f, 0f, 200f, 100f);
            Rect mapRect = WorldMapPreviewMath.ComputeMapRect(previewArea, 0.5f, true);

            Assert.AreEqual(100f, mapRect.height, 0.01f);
            Assert.AreEqual(50f, mapRect.width, 0.01f);
            Assert.AreEqual(75f, mapRect.x, 0.01f);
            Assert.AreEqual(0f, mapRect.y, 0.01f);
        }

        [Test]
        public void ComputeMapRect_ShowArtFalse_ReturnsFullPreviewArea()
        {
            Rect previewArea = new Rect(10f, 20f, 300f, 150f);
            Rect mapRect = WorldMapPreviewMath.ComputeMapRect(previewArea, 2f, false);

            Assert.AreEqual(previewArea, mapRect);
        }

        [Test]
        public void MapToPreview_TopLeftCorner_MapsToMapRectOrigin()
        {
            Rect mapRect = new Rect(10f, 20f, 100f, 50f);
            Vector2 point = WorldMapPreviewMath.MapToPreview(mapRect, 0f, 0f);

            Assert.AreEqual(new Vector2(10f, 20f), point);
        }

        [Test]
        public void MapToPreview_BottomRightCorner_MapsToOppositeCorner()
        {
            Rect mapRect = new Rect(10f, 20f, 100f, 50f);
            Vector2 point = WorldMapPreviewMath.MapToPreview(mapRect, 100f, 100f);

            Assert.AreEqual(new Vector2(110f, 70f), point);
        }

        [Test]
        public void MapToPreview_Center_MapsToMapRectCenter()
        {
            Rect mapRect = new Rect(10f, 20f, 100f, 50f);
            Vector2 point = WorldMapPreviewMath.MapToPreview(mapRect, 50f, 50f);

            Assert.AreEqual(new Vector2(60f, 45f), point);
        }

        [Test]
        public void MapToPreview_ThenPreviewToMap_RoundTripsToOriginalPercent()
        {
            Rect mapRect = new Rect(15f, 5f, 340f, 180f);
            Vector2 original = new Vector2(37.5f, 62.25f);

            Vector2 screen = WorldMapPreviewMath.MapToPreview(mapRect, original.x, original.y);
            Vector2 roundTripped = WorldMapPreviewMath.PreviewToMap(mapRect, screen);

            Assert.AreEqual(original.x, roundTripped.x, 0.001f);
            Assert.AreEqual(original.y, roundTripped.y, 0.001f);
        }

        [Test]
        public void PreviewToMapClamped_PointOutsideMapRect_ClampsToNearestEdge()
        {
            Rect mapRect = new Rect(0f, 0f, 100f, 100f);
            Vector2 farOutside = new Vector2(500f, -300f);

            Vector2 clamped = WorldMapPreviewMath.PreviewToMapClamped(mapRect, farOutside);

            Assert.AreEqual(100f, clamped.x, 0.001f);
            Assert.AreEqual(0f, clamped.y, 0.001f);
        }

        [Test]
        public void ComputeMapRect_ResizingPreviewArea_KeepsMapPercentToScreenMappingConsistentPerRect()
        {
            Rect smallArea = new Rect(0f, 0f, 100f, 100f);
            Rect largeArea = new Rect(0f, 0f, 400f, 400f);

            Rect smallMapRect = WorldMapPreviewMath.ComputeMapRect(smallArea, 1f, true);
            Rect largeMapRect = WorldMapPreviewMath.ComputeMapRect(largeArea, 1f, true);

            Vector2 smallCenter = WorldMapPreviewMath.MapToPreview(smallMapRect, 50f, 50f);
            Vector2 largeCenter = WorldMapPreviewMath.MapToPreview(largeMapRect, 50f, 50f);

            Assert.AreEqual(50f, smallCenter.x, 0.01f);
            Assert.AreEqual(200f, largeCenter.x, 0.01f);

            Vector2 backFromSmall = WorldMapPreviewMath.PreviewToMap(smallMapRect, smallCenter);
            Vector2 backFromLarge = WorldMapPreviewMath.PreviewToMap(largeMapRect, largeCenter);

            Assert.AreEqual(backFromSmall.x, backFromLarge.x, 0.01f);
            Assert.AreEqual(backFromSmall.y, backFromLarge.y, 0.01f);
        }
    }
}
