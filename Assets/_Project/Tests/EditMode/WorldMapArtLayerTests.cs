using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "Map Art Layers" (раздел 26): чистая математика размещения PNG
    // (WorldMapPreviewMath.MapBoundsToRect), сортировка/фильтрация слоёв
    // (WorldMapArtLayerUtility — общий источник правды для Preview и
    // runtime) и Validation (CollectArtLayerIssues). IMGUI-отрисовку и
    // фактическое построение VisualElement в рантайме юнит-тестами не
    // покрываем — только ручная проверка в живом Editor/Play Mode.
    public class WorldMapArtLayerTests
    {
        private static Sprite CreateDummySprite()
        {
            Texture2D texture = new Texture2D(4, 4);
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }

        // A. Art Layer 0..100 → занимает всю карту.
        [Test]
        public void MapBoundsToRect_FullRangeBounds_OccupiesEntireMapRect()
        {
            Rect mapRect = new Rect(10f, 20f, 200f, 100f);
            Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(mapRect, 0f, 0f, 100f, 100f);

            Assert.AreEqual(mapRect.x, layerRect.x, 0.001f);
            Assert.AreEqual(mapRect.y, layerRect.y, 0.001f);
            Assert.AreEqual(mapRect.width, layerRect.width, 0.001f);
            Assert.AreEqual(mapRect.height, layerRect.height, 0.001f);
        }

        // B. Art Layer 10..30 / 20..40 → правильный участок.
        [Test]
        public void MapBoundsToRect_SubRegionBounds_OccupiesCorrectSubRect()
        {
            Rect mapRect = new Rect(0f, 0f, 1000f, 500f);
            Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(mapRect, 10f, 20f, 30f, 40f);

            Assert.AreEqual(100f, layerRect.x, 0.001f);
            Assert.AreEqual(100f, layerRect.y, 0.001f);
            Assert.AreEqual(200f, layerRect.width, 0.001f);
            Assert.AreEqual(100f, layerRect.height, 0.001f);
        }

        // C. Map-space center Layer → оказывается в центре mapRect.
        [Test]
        public void MapBoundsToRect_CenteredBounds_ResultingRectIsCenteredInMapRect()
        {
            Rect mapRect = new Rect(0f, 0f, 400f, 200f);
            Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(mapRect, 25f, 25f, 75f, 75f);

            Assert.AreEqual(mapRect.center.x, layerRect.center.x, 0.001f);
            Assert.AreEqual(mapRect.center.y, layerRect.center.y, 0.001f);
        }

        // D. Disabled Layer → не участвует.
        [Test]
        public void GetOrderedEnabledLayers_DisabledLayer_IsExcluded()
        {
            Sprite sprite = CreateDummySprite();
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry { Id = "on", Enabled = true, Sprite = sprite },
                new WorldMapArtLayerEntry { Id = "off", Enabled = false, Sprite = sprite }
            };

            List<WorldMapArtLayerEntry> result = WorldMapArtLayerUtility.GetOrderedEnabledLayers(layers);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("on", result[0].Id);
        }

        [Test]
        public void GetOrderedEnabledLayers_EnabledLayerWithoutSprite_IsExcluded()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry { Id = "no-sprite", Enabled = true, Sprite = null }
            };

            List<WorldMapArtLayerEntry> result = WorldMapArtLayerUtility.GetOrderedEnabledLayers(layers);

            Assert.AreEqual(0, result.Count);
        }

        // E. Order сортируется правильно (включая устойчивость при равном Order).
        [Test]
        public void GetOrderedEnabledLayers_SortsByOrderAscending()
        {
            Sprite sprite = CreateDummySprite();
            WorldMapArtLayerEntry layerHigh = new WorldMapArtLayerEntry { Id = "high", Enabled = true, Sprite = sprite, Order = 10 };
            WorldMapArtLayerEntry layerLow = new WorldMapArtLayerEntry { Id = "low", Enabled = true, Sprite = sprite, Order = -10 };
            WorldMapArtLayerEntry layerMid = new WorldMapArtLayerEntry { Id = "mid", Enabled = true, Sprite = sprite, Order = 0 };

            List<WorldMapArtLayerEntry> layers =
                new List<WorldMapArtLayerEntry> { layerHigh, layerLow, layerMid };

            List<WorldMapArtLayerEntry> result = WorldMapArtLayerUtility.GetOrderedEnabledLayers(layers);

            Assert.AreEqual(new[] { "low", "mid", "high" }, new[] { result[0].Id, result[1].Id, result[2].Id });
        }

        [Test]
        public void GetOrderedEnabledLayers_EqualOrder_PreservesOriginalListOrder()
        {
            Sprite sprite = CreateDummySprite();
            WorldMapArtLayerEntry first = new WorldMapArtLayerEntry { Id = "first", Enabled = true, Sprite = sprite, Order = 5 };
            WorldMapArtLayerEntry second = new WorldMapArtLayerEntry { Id = "second", Enabled = true, Sprite = sprite, Order = 5 };

            List<WorldMapArtLayerEntry> result =
                WorldMapArtLayerUtility.GetOrderedEnabledLayers(new List<WorldMapArtLayerEntry> { first, second });

            Assert.AreEqual("first", result[0].Id);
            Assert.AreEqual("second", result[1].Id);
        }

        // F. Opacity — Validation ловит Opacity <= 0.
        [Test]
        public void CollectArtLayerIssues_NonPositiveOpacity_ProducesWarning()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "invisible", Enabled = true, Sprite = CreateDummySprite(),
                    MinXPercent = 0f, MaxXPercent = 10f, MinYPercent = 0f, MaxYPercent = 10f,
                    Opacity = 0f
                }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("Opacity")));
        }

        // G. Invalid Bounds определяются Validation.
        [Test]
        public void CollectArtLayerIssues_MinGreaterThanOrEqualMax_ProducesError()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "bad", Enabled = true, Sprite = CreateDummySprite(),
                    MinXPercent = 50f, MaxXPercent = 30f,
                    MinYPercent = 10f, MaxYPercent = 40f
                }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("Min X >= Max X")));
        }

        [Test]
        public void CollectArtLayerIssues_EnabledLayerWithoutSprite_ProducesError()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "no-sprite", Enabled = true, Sprite = null,
                    MinXPercent = 0f, MaxXPercent = 10f, MinYPercent = 0f, MaxYPercent = 10f
                }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("без Sprite")));
        }

        [Test]
        public void CollectArtLayerIssues_DuplicateId_ProducesError()
        {
            Sprite sprite = CreateDummySprite();
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry { Id = "dup", Enabled = true, Sprite = sprite, MinXPercent = 0f, MaxXPercent = 10f, MinYPercent = 0f, MaxYPercent = 10f },
                new WorldMapArtLayerEntry { Id = "dup", Enabled = true, Sprite = sprite, MinXPercent = 20f, MaxXPercent = 30f, MinYPercent = 0f, MaxYPercent = 10f }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("Дублирующийся ID")));
        }

        [Test]
        public void CollectArtLayerIssues_LayerFullyOutsideMap_ProducesWarning()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "outside", Enabled = true, Sprite = CreateDummySprite(),
                    MinXPercent = 120f, MaxXPercent = 140f, MinYPercent = 0f, MaxYPercent = 10f
                }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("полностью вне карты")));
        }

        [Test]
        public void CollectArtLayerIssues_ValidLayer_ProducesNoIssues()
        {
            List<WorldMapArtLayerEntry> layers = new List<WorldMapArtLayerEntry>
            {
                new WorldMapArtLayerEntry
                {
                    Id = "home_region", DisplayName = "Дом", Enabled = true, Sprite = CreateDummySprite(),
                    MinXPercent = 10f, MaxXPercent = 35f, MinYPercent = 40f, MaxYPercent = 65f,
                    Order = 0, Opacity = 1f
                }
            };

            List<string> issues = new List<string>();
            WorldMapDatabaseWindow.CollectArtLayerIssues(layers, issues);

            Assert.AreEqual(0, issues.Count);
        }

        // H. Пустой ArtLayers → legacy Base Map behaviour (ничего не рисуется
        // поверх Base Map, но и не ломается).
        [Test]
        public void GetOrderedEnabledLayers_EmptyList_ReturnsEmpty()
        {
            List<WorldMapArtLayerEntry> result =
                WorldMapArtLayerUtility.GetOrderedEnabledLayers(new List<WorldMapArtLayerEntry>());

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetOrderedEnabledLayers_NullList_ReturnsEmptyWithoutThrowing()
        {
            List<WorldMapArtLayerEntry> result = WorldMapArtLayerUtility.GetOrderedEnabledLayers(null);

            Assert.AreEqual(0, result.Count);
        }

        // I. Map coordinate внутри Layer совпадает в Preview и runtime
        // conversion. Runtime использует проценты style.left/top/width/height
        // напрямую (см. ApplyWorldMapArtLayers) — математически идентично
        // MapToPreview/MapBoundsToRect: точка (map-space) внутри Bounds
        // должна попасть внутрь посчитанного Preview-прямоугольника слоя.
        [Test]
        public void MapBoundsToRect_PointInsideLayerBounds_MapsInsideComputedLayerRect()
        {
            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            float minX = 10f, maxX = 35f, minY = 40f, maxY = 65f;
            Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(mapRect, minX, minY, maxX, maxY);

            Vector2 pointInsideBounds = new Vector2(20f, 53f);
            Vector2 screenPoint = WorldMapPreviewMath.MapToPreview(mapRect, pointInsideBounds.x, pointInsideBounds.y);

            Assert.IsTrue(layerRect.Contains(screenPoint));
        }

        [Test]
        public void MapBoundsToRect_PointOutsideLayerBounds_MapsOutsideComputedLayerRect()
        {
            Rect mapRect = new Rect(0f, 0f, 1000f, 1000f);
            Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(mapRect, 10f, 40f, 35f, 65f);

            Vector2 pointOutsideBounds = new Vector2(80f, 5f);
            Vector2 screenPoint = WorldMapPreviewMath.MapToPreview(mapRect, pointOutsideBounds.x, pointOutsideBounds.y);

            Assert.IsFalse(layerRect.Contains(screenPoint));
        }

        // J. Resize Preview не меняет глобальный Bounds слоя — Bounds — это
        // просто данные (проценты), не зависящие от того, каким mapRect их
        // сейчас пересчитывают на экран.
        [Test]
        public void MapBoundsToRect_ResizingMapRect_DoesNotChangeSourceBoundsData()
        {
            WorldMapArtLayerEntry layer = new WorldMapArtLayerEntry
            {
                Id = "home_region", MinXPercent = 10f, MaxXPercent = 35f, MinYPercent = 40f, MaxYPercent = 65f
            };

            Rect smallMapRect = new Rect(0f, 0f, 100f, 100f);
            Rect largeMapRect = new Rect(0f, 0f, 800f, 800f);

            WorldMapPreviewMath.MapBoundsToRect(
                smallMapRect, layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);
            WorldMapPreviewMath.MapBoundsToRect(
                largeMapRect, layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);

            Assert.AreEqual(10f, layer.MinXPercent);
            Assert.AreEqual(35f, layer.MaxXPercent);
            Assert.AreEqual(40f, layer.MinYPercent);
            Assert.AreEqual(65f, layer.MaxYPercent);
        }
    }
}
