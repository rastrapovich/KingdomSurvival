using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "Map Art Layers — Direct Manipulation" (WM-T04.7, раздел 27):
    // чистая математика move/resize/clamp (WorldMapArtLayerBoundsMath) и
    // выбор слоя по клику (WorldMapDatabaseWindow.FindArtLayerIndexAtPoint).
    // IMGUI drag/Undo — не юнит-тестируется (см. раздел 27, "Undo" — только
    // ручная проверка), только ручная проверка в живом Editor.
    public class WorldMapArtLayerDragTests
    {
        private static Sprite CreateDummySprite(int width = 4, int height = 4)
        {
            Texture2D texture = new Texture2D(width, height);
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f));
        }

        // MOVE

        // A. Move bounds +10 X → обе X-границы смещаются, ширина не меняется.
        [Test]
        public void MoveBounds_PositiveDeltaX_ShiftsBothXBordersKeepsWidth()
        {
            MapBounds original = new MapBounds(10f, 40f, 35f, 65f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, 10f, 0f);

            Assert.AreEqual(20f, moved.MinX, 0.001f);
            Assert.AreEqual(45f, moved.MaxX, 0.001f);
            Assert.AreEqual(original.Width, moved.Width, 0.001f);
        }

        // B. Move bounds +10 Y → обе Y-границы смещаются, высота не меняется.
        [Test]
        public void MoveBounds_PositiveDeltaY_ShiftsBothYBordersKeepsHeight()
        {
            MapBounds original = new MapBounds(10f, 40f, 35f, 65f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, 0f, 10f);

            Assert.AreEqual(50f, moved.MinY, 0.001f);
            Assert.AreEqual(75f, moved.MaxY, 0.001f);
            Assert.AreEqual(original.Height, moved.Height, 0.001f);
        }

        // C. Move у левого края → clamp к 0, ширина не меняется.
        [Test]
        public void MoveBounds_PastLeftEdge_ClampsToZeroKeepsWidth()
        {
            MapBounds original = new MapBounds(10f, 0f, 35f, 25f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, -50f, 0f);

            Assert.AreEqual(0f, moved.MinX, 0.001f);
            Assert.AreEqual(25f, moved.MaxX, 0.001f);
            Assert.AreEqual(original.Width, moved.Width, 0.001f);
        }

        // D. Move у правого края → clamp к 100, ширина не меняется.
        [Test]
        public void MoveBounds_PastRightEdge_ClampsToHundredKeepsWidth()
        {
            MapBounds original = new MapBounds(70f, 0f, 90f, 25f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, 50f, 0f);

            Assert.AreEqual(100f, moved.MaxX, 0.001f);
            Assert.AreEqual(80f, moved.MinX, 0.001f);
            Assert.AreEqual(original.Width, moved.Width, 0.001f);
        }

        [Test]
        public void MoveBounds_FromOriginalPlusTotalDelta_MatchesDirectComputation()
        {
            // Раздел 7 задачи: считать нужно как originalBounds + суммарная
            // дельта, а не накопительно за кадр — проверяем, что несколько
            // "кадров" с одним и тем же originalBounds дают тот же
            // результат, что и один прыжок сразу на финальную дельту.
            MapBounds original = new MapBounds(10f, 40f, 35f, 65f);

            MapBounds afterManySmallCalls = original;
            for (int i = 0; i < 5; i++)
                afterManySmallCalls = WorldMapArtLayerBoundsMath.MoveBounds(original, 2f * (i + 1), 0f);

            MapBounds directJump = WorldMapArtLayerBoundsMath.MoveBounds(original, 10f, 0f);

            Assert.AreEqual(directJump.MinX, afterManySmallCalls.MinX, 0.001f);
            Assert.AreEqual(directJump.MaxX, afterManySmallCalls.MaxX, 0.001f);
        }

        // RESIZE

        // E. BottomRight resize увеличивает MaxX/MaxY, MinX/MinY не меняются.
        [Test]
        public void ResizeBoundsFromCorner_BottomRight_IncreasesMaxXMaxY()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(50f, 50f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 1f);

            Assert.AreEqual(10f, resized.MinX, 0.001f);
            Assert.AreEqual(10f, resized.MinY, 0.001f);
            Assert.AreEqual(50f, resized.MaxX, 0.001f);
            Assert.AreEqual(50f, resized.MaxY, 0.001f);
        }

        // F. TopLeft resize уменьшает MinX/MinY, MaxX/MaxY не меняются.
        [Test]
        public void ResizeBoundsFromCorner_TopLeft_DecreasesMinXMinY()
        {
            MapBounds original = new MapBounds(20f, 20f, 40f, 40f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.TopLeft, new Vector2(5f, 5f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 1f);

            Assert.AreEqual(5f, resized.MinX, 0.001f);
            Assert.AreEqual(5f, resized.MinY, 0.001f);
            Assert.AreEqual(40f, resized.MaxX, 0.001f);
            Assert.AreEqual(40f, resized.MaxY, 0.001f);
        }

        // G. PreserveAspect: aspect ratio сохраняется.
        [Test]
        public void ResizeBoundsFromCorner_PreserveAspect_KeepsAspectRatio()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f); // квадрат 20x20
            float aspectRatio = 2f; // широкий спрайт (width/height = 2)

            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(50f, 90f),
                preserveAspect: true, aspectRatio: aspectRatio, minSizePercent: 1f);

            float resultAspect = resized.Width / resized.Height;
            Assert.AreEqual(aspectRatio, resultAspect, 0.001f);
            Assert.AreEqual(10f, resized.MinX, 0.001f);
            Assert.AreEqual(10f, resized.MinY, 0.001f);
        }

        // H. Stretch: X/Y можно менять независимо (не привязаны к aspect).
        [Test]
        public void ResizeBoundsFromCorner_Stretch_AllowsIndependentXY()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f);

            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(50f, 35f),
                preserveAspect: false, aspectRatio: 2f, minSizePercent: 1f);

            Assert.AreEqual(40f, resized.Width, 0.001f);
            Assert.AreEqual(25f, resized.Height, 0.001f);
        }

        // I. Resize не даёт width <= 0.
        [Test]
        public void ResizeBoundsFromCorner_CornerCollapsedOntoAnchor_WidthStaysAboveMinSize()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f);

            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(10f, 50f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 1f);

            Assert.GreaterOrEqual(resized.Width, 1f);
            Assert.Greater(resized.MaxX, resized.MinX);
        }

        // J. Resize не даёт height <= 0.
        [Test]
        public void ResizeBoundsFromCorner_CornerCollapsedOntoAnchor_HeightStaysAboveMinSize()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f);

            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(50f, 10f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 1f);

            Assert.GreaterOrEqual(resized.Height, 1f);
            Assert.Greater(resized.MaxY, resized.MinY);
        }

        // K. Resize clamp внутри карты.
        [Test]
        public void ResizeBoundsFromCorner_DragPastMapEdge_StaysWithinMapBounds()
        {
            MapBounds original = new MapBounds(80f, 80f, 90f, 90f);

            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(150f, 150f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 1f);

            Assert.LessOrEqual(resized.MaxX, 100f);
            Assert.LessOrEqual(resized.MaxY, 100f);
            Assert.GreaterOrEqual(resized.MinX, 0f);
            Assert.GreaterOrEqual(resized.MinY, 0f);
        }

        [Test]
        public void ClampBoundsToMap_AlreadyInsideMap_IsUnchanged()
        {
            MapBounds bounds = new MapBounds(10f, 20f, 30f, 40f);
            MapBounds clamped = WorldMapArtLayerBoundsMath.ClampBoundsToMap(bounds);

            Assert.AreEqual(bounds.MinX, clamped.MinX, 0.001f);
            Assert.AreEqual(bounds.MinY, clamped.MinY, 0.001f);
            Assert.AreEqual(bounds.MaxX, clamped.MaxX, 0.001f);
            Assert.AreEqual(bounds.MaxY, clamped.MaxY, 0.001f);
        }

        // SELECTION

        // L. Overlapping layers: верхний Order выбирается первым.
        [Test]
        public void FindArtLayerIndexAtPoint_OverlappingLayers_SelectsHighestOrder()
        {
            Sprite sprite = CreateDummySprite();
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "bottom", Enabled = true, Sprite = sprite, Order = 0,
                    MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
                },
                new WorldMapArtLayerEntry
                {
                    Id = "top", Enabled = true, Sprite = sprite, Order = 5,
                    MinXPercent = 10f, MaxXPercent = 50f, MinYPercent = 10f, MaxYPercent = 50f
                }
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickInsideOverlap = WorldMapPreviewMath.MapToPreview(mapRect, 30f, 30f);

            int hitIndex = WorldMapDatabaseWindow.FindArtLayerIndexAtPoint(mapRect, layers, clickInsideOverlap);

            Assert.AreEqual(1, hitIndex);
            Assert.AreEqual("top", layers[hitIndex].Id);
        }

        [Test]
        public void FindArtLayerIndexAtPoint_ClickOutsideOverlap_SelectsOnlyMatchingLayer()
        {
            Sprite sprite = CreateDummySprite();
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "bottom", Enabled = true, Sprite = sprite, Order = 0,
                    MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
                },
                new WorldMapArtLayerEntry
                {
                    Id = "top", Enabled = true, Sprite = sprite, Order = 5,
                    MinXPercent = 10f, MaxXPercent = 50f, MinYPercent = 10f, MaxYPercent = 50f
                }
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickOutsideTopLayer = WorldMapPreviewMath.MapToPreview(mapRect, 80f, 80f);

            int hitIndex = WorldMapDatabaseWindow.FindArtLayerIndexAtPoint(mapRect, layers, clickOutsideTopLayer);

            Assert.AreEqual(0, hitIndex);
        }

        // M. Disabled layer не выбирается.
        [Test]
        public void FindArtLayerIndexAtPoint_DisabledLayer_IsNotSelected()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "disabled", Enabled = false, Sprite = CreateDummySprite(), Order = 0,
                    MinXPercent = 0f, MaxXPercent = 100f, MinYPercent = 0f, MaxYPercent = 100f
                }
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickInside = WorldMapPreviewMath.MapToPreview(mapRect, 50f, 50f);

            int hitIndex = WorldMapDatabaseWindow.FindArtLayerIndexAtPoint(mapRect, layers, clickInside);

            Assert.AreEqual(-1, hitIndex);
        }

        [Test]
        public void FindArtLayerIndexAtPoint_ClickOutsideAllLayers_ReturnsMinusOne()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "small", Enabled = true, Sprite = CreateDummySprite(), Order = 0,
                    MinXPercent = 10f, MaxXPercent = 20f, MinYPercent = 10f, MaxYPercent = 20f
                }
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickFarAway = WorldMapPreviewMath.MapToPreview(mapRect, 90f, 90f);

            int hitIndex = WorldMapDatabaseWindow.FindArtLayerIndexAtPoint(mapRect, layers, clickFarAway);

            Assert.AreEqual(-1, hitIndex);
        }
    }
}
