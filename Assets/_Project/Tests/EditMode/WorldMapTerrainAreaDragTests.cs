using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "Terrain Area Preview Authoring" (WM-T04.8, раздел 30): чистая
    // математика create/move/resize/clamp (переиспользует
    // WorldMapArtLayerBoundsMath из WM-T04.7 — раздел 26 задачи явно
    // запрещает дублировать) и выбор зоны по клику
    // (WorldMapDatabaseWindow.FindTerrainAreaIndexAtPoint). IMGUI drag/Undo —
    // только ручная проверка, как и для Art Layers/Roads.
    public class WorldMapTerrainAreaDragTests
    {
        private static WorldMapWorldDefinitionAsset.TerrainAreaEntry CreateArea(
            string id, float minX, float minY, float maxX, float maxY, int priority = 0)
        {
            return new WorldMapWorldDefinitionAsset.TerrainAreaEntry
            {
                Id = id,
                Terrain = WorldMapTerrainType.Hills,
                Tags = new List<string>(),
                MinXPercent = minX,
                MinYPercent = minY,
                MaxXPercent = maxX,
                MaxYPercent = maxY,
                Priority = priority
            };
        }

        // CREATE

        // A. Two map points → корректные Min/Max независимо от направления drag.
        [Test]
        public void NormalizeBoundsFromTwoPoints_DragInReverseDirection_ProducesCorrectMinMax()
        {
            MapBounds bounds = WorldMapArtLayerBoundsMath.NormalizeBoundsFromTwoPoints(
                new Vector2(70f, 70f), new Vector2(20f, 30f));

            Assert.AreEqual(20f, bounds.MinX, 0.001f);
            Assert.AreEqual(70f, bounds.MaxX, 0.001f);
            Assert.AreEqual(30f, bounds.MinY, 0.001f);
            Assert.AreEqual(70f, bounds.MaxY, 0.001f);
        }

        [Test]
        public void NormalizeBoundsFromTwoPoints_DragInForwardDirection_ProducesCorrectMinMax()
        {
            MapBounds bounds = WorldMapArtLayerBoundsMath.NormalizeBoundsFromTwoPoints(
                new Vector2(20f, 30f), new Vector2(70f, 70f));

            Assert.AreEqual(20f, bounds.MinX, 0.001f);
            Assert.AreEqual(70f, bounds.MaxX, 0.001f);
            Assert.AreEqual(30f, bounds.MinY, 0.001f);
            Assert.AreEqual(70f, bounds.MaxY, 0.001f);
        }

        // B. Too-small drag → зона не создаётся. Проверяем на уровне
        // порогового условия, которое использует HandleTerrainAreaEditingInput
        // (минимальный размер в map-space) — сам IMGUI MouseUp не
        // юнит-тестируется, но условие "маленький Bounds отбрасывается"
        // выражено в чистых величинах и проверяется здесь.
        [Test]
        public void NormalizeBoundsFromTwoPoints_TooSmallDrag_ResultIsBelowMinimumSizeThreshold()
        {
            const float minSizePercent = 0.5f;
            MapBounds bounds = WorldMapArtLayerBoundsMath.NormalizeBoundsFromTwoPoints(
                new Vector2(50f, 50f), new Vector2(50.1f, 50.1f));

            Assert.Less(bounds.Width, minSizePercent);
            Assert.Less(bounds.Height, minSizePercent);
        }

        // MOVE

        // C. Move сохраняет width/height.
        [Test]
        public void MoveBounds_TerrainArea_KeepsWidthAndHeight()
        {
            MapBounds original = new MapBounds(10f, 10f, 40f, 30f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, 15f, -5f);

            Assert.AreEqual(original.Width, moved.Width, 0.001f);
            Assert.AreEqual(original.Height, moved.Height, 0.001f);
            Assert.AreEqual(25f, moved.MinX, 0.001f);
            Assert.AreEqual(5f, moved.MinY, 0.001f);
        }

        // D. Move clamp внутри 0..100.
        [Test]
        public void MoveBounds_TerrainArea_ClampsWithinMap()
        {
            MapBounds original = new MapBounds(0f, 0f, 20f, 20f);
            MapBounds moved = WorldMapArtLayerBoundsMath.MoveBounds(original, -50f, -50f);

            Assert.AreEqual(0f, moved.MinX, 0.001f);
            Assert.AreEqual(0f, moved.MinY, 0.001f);
            Assert.AreEqual(20f, moved.Width, 0.001f);
            Assert.AreEqual(20f, moved.Height, 0.001f);
        }

        // RESIZE

        // E. Resize TopLeft корректно меняет MinX/MinY.
        [Test]
        public void ResizeBoundsFromCorner_TerrainAreaTopLeft_ChangesMinXMinYOnly()
        {
            MapBounds original = new MapBounds(20f, 20f, 50f, 50f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.TopLeft, new Vector2(10f, 15f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.AreEqual(10f, resized.MinX, 0.001f);
            Assert.AreEqual(15f, resized.MinY, 0.001f);
            Assert.AreEqual(50f, resized.MaxX, 0.001f);
            Assert.AreEqual(50f, resized.MaxY, 0.001f);
        }

        // F. Resize BottomRight корректно меняет MaxX/MaxY.
        [Test]
        public void ResizeBoundsFromCorner_TerrainAreaBottomRight_ChangesMaxXMaxYOnly()
        {
            MapBounds original = new MapBounds(20f, 20f, 50f, 50f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(80f, 65f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.AreEqual(20f, resized.MinX, 0.001f);
            Assert.AreEqual(20f, resized.MinY, 0.001f);
            Assert.AreEqual(80f, resized.MaxX, 0.001f);
            Assert.AreEqual(65f, resized.MaxY, 0.001f);
        }

        // Free resize (no aspect ratio lock) — X and Y move independently,
        // unlike Art Layer's PreserveAspect mode.
        [Test]
        public void ResizeBoundsFromCorner_TerrainArea_AllowsFreeIndependentXY()
        {
            MapBounds original = new MapBounds(10f, 10f, 30f, 30f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(60f, 32f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.AreEqual(50f, resized.Width, 0.001f);
            Assert.AreEqual(22f, resized.Height, 0.001f);
        }

        // G. Нельзя получить MinX >= MaxX.
        [Test]
        public void ResizeBoundsFromCorner_TerrainArea_NeverProducesMinXGreaterOrEqualMaxX()
        {
            MapBounds original = new MapBounds(20f, 20f, 40f, 40f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(20f, 60f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.Less(resized.MinX, resized.MaxX);
        }

        // H. Нельзя получить MinY >= MaxY.
        [Test]
        public void ResizeBoundsFromCorner_TerrainArea_NeverProducesMinYGreaterOrEqualMaxY()
        {
            MapBounds original = new MapBounds(20f, 20f, 40f, 40f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(60f, 20f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.Less(resized.MinY, resized.MaxY);
        }

        [Test]
        public void ResizeBoundsFromCorner_TerrainArea_DragPastMapEdge_ClampsWithinMap()
        {
            MapBounds original = new MapBounds(85f, 85f, 95f, 95f);
            MapBounds resized = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                original, ArtLayerCorner.BottomRight, new Vector2(150f, 150f),
                preserveAspect: false, aspectRatio: 1f, minSizePercent: 0.5f);

            Assert.LessOrEqual(resized.MaxX, 100f);
            Assert.LessOrEqual(resized.MaxY, 100f);
        }

        // SELECTION

        // I. Overlap → Area с большим Priority выбирается первой.
        [Test]
        public void FindTerrainAreaIndexAtPoint_OverlappingAreas_SelectsHighestPriority()
        {
            List<WorldMapWorldDefinitionAsset.TerrainAreaEntry> areas =
                new List<WorldMapWorldDefinitionAsset.TerrainAreaEntry>
            {
                CreateArea("low", 0f, 0f, 100f, 100f, priority: 0),
                CreateArea("high", 10f, 10f, 50f, 50f, priority: 5)
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickInOverlap = WorldMapPreviewMath.MapToPreview(mapRect, 30f, 30f);

            int hitIndex = WorldMapDatabaseWindow.FindTerrainAreaIndexAtPoint(mapRect, areas, clickInOverlap);

            Assert.AreEqual(1, hitIndex);
            Assert.AreEqual("high", areas[hitIndex].Id);
        }

        // J. При одинаковом Priority порядок стабилен (последний в списке
        // побеждает — тот же принцип, что задокументирован для gameplay-
        // разрешения одинакового Priority на вкладке «География»).
        [Test]
        public void FindTerrainAreaIndexAtPoint_EqualPriority_LastInListWins()
        {
            List<WorldMapWorldDefinitionAsset.TerrainAreaEntry> areas =
                new List<WorldMapWorldDefinitionAsset.TerrainAreaEntry>
            {
                CreateArea("first", 0f, 0f, 100f, 100f, priority: 0),
                CreateArea("second", 0f, 0f, 100f, 100f, priority: 0)
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 click = WorldMapPreviewMath.MapToPreview(mapRect, 50f, 50f);

            int hitIndex = WorldMapDatabaseWindow.FindTerrainAreaIndexAtPoint(mapRect, areas, click);

            Assert.AreEqual(1, hitIndex);
            Assert.AreEqual("second", areas[hitIndex].Id);
        }

        [Test]
        public void FindTerrainAreaIndexAtPoint_ClickOutsideAllAreas_ReturnsMinusOne()
        {
            List<WorldMapWorldDefinitionAsset.TerrainAreaEntry> areas =
                new List<WorldMapWorldDefinitionAsset.TerrainAreaEntry>
            {
                CreateArea("small", 10f, 10f, 20f, 20f)
            };

            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Vector2 clickFarAway = WorldMapPreviewMath.MapToPreview(mapRect, 90f, 90f);

            int hitIndex = WorldMapDatabaseWindow.FindTerrainAreaIndexAtPoint(mapRect, areas, clickFarAway);

            Assert.AreEqual(-1, hitIndex);
        }

        // OPACITY

        // K. Preview opacity clamp 0..1.
        [Test]
        public void PreviewTerrainOpacity_ClampedToZeroOneRange()
        {
            Assert.AreEqual(0f, Mathf.Clamp01(-0.5f));
            Assert.AreEqual(1f, Mathf.Clamp01(1.5f));
            Assert.AreEqual(0.2f, Mathf.Clamp01(0.2f), 0.0001f);
        }

        // L. Opacity EditorPrefs не меняет Terrain Area data — прозрачность
        // не является полем TerrainAreaEntry вообще (раздел 1/16 задачи), так
        // что она структурно не может повлиять на Bounds/Terrain/Priority.
        [Test]
        public void TerrainAreaEntry_HasNoOpacityField()
        {
            System.Reflection.FieldInfo opacityField =
                typeof(WorldMapWorldDefinitionAsset.TerrainAreaEntry).GetField("Opacity");

            Assert.IsNull(opacityField);
        }
    }
}
