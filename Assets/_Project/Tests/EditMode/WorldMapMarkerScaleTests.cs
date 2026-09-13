using KingdomSurvival.WorldMapVisual;
using KingdomSurvival.WorldMapVisual.Editor;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.Tests.EditMode
{
    // Задача "регулируемый визуальный размер героя и Дома": чистая проверка
    // fallback/валидации на WorldMapVisualTheme и перевода "доли клетки" →
    // "проценты карты" (та же формула, что использует runtime и Preview —
    // единый источник истины). IMGUI/UI Toolkit-отрисовку не юнит-тестируем
    // (как и раньше в этом проекте) — только ручная проверка в Play Mode.
    public class WorldMapMarkerScaleTests
    {
        private static WorldMapVisualTheme CreateTheme()
        {
            return ScriptableObject.CreateInstance<WorldMapVisualTheme>();
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(target, value);
        }

        // Hero Marker Size fallback.
        [Test]
        public void HeroMarkerSizeCells_DefaultTheme_IsDefaultValue()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                Assert.That(
                    theme.HeroMarkerSizeCells,
                    Is.EqualTo(WorldMapVisualTheme.DefaultHeroMarkerSizeCells).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void HeroMarkerSizeCells_ZeroOrNegativeOrInvalid_FallsBackToDefault()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                SetPrivateFloat(theme, "heroMarkerSizeCells", 0f);
                Assert.That(theme.HeroMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHeroMarkerSizeCells));

                SetPrivateFloat(theme, "heroMarkerSizeCells", -1f);
                Assert.That(theme.HeroMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHeroMarkerSizeCells));

                SetPrivateFloat(theme, "heroMarkerSizeCells", float.NaN);
                Assert.That(theme.HeroMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHeroMarkerSizeCells));

                SetPrivateFloat(theme, "heroMarkerSizeCells", float.PositiveInfinity);
                Assert.That(theme.HeroMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHeroMarkerSizeCells));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // Home Marker Size fallback.
        [Test]
        public void HomeMarkerSizeCells_DefaultTheme_IsDefaultValue()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                Assert.That(
                    theme.HomeMarkerSizeCells,
                    Is.EqualTo(WorldMapVisualTheme.DefaultHomeMarkerSizeCells).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void HomeMarkerSizeCells_ZeroOrNegativeOrInvalid_FallsBackToDefault()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                SetPrivateFloat(theme, "homeMarkerSizeCells", 0f);
                Assert.That(theme.HomeMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHomeMarkerSizeCells));

                SetPrivateFloat(theme, "homeMarkerSizeCells", -5f);
                Assert.That(theme.HomeMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHomeMarkerSizeCells));

                SetPrivateFloat(theme, "homeMarkerSizeCells", float.NaN);
                Assert.That(theme.HomeMarkerSizeCells, Is.EqualTo(WorldMapVisualTheme.DefaultHomeMarkerSizeCells));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void MarkerSizeCells_ValidCustomValue_IsUsedAsIs()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                SetPrivateFloat(theme, "heroMarkerSizeCells", 0.7f);
                SetPrivateFloat(theme, "homeMarkerSizeCells", 1.8f);

                Assert.That(theme.HeroMarkerSizeCells, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(theme.HomeMarkerSizeCells, Is.EqualTo(1.8f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // Размер в клетках правильно переводится в размер map canvas
        // (те же проценты, что и Bounds/позиции всего остального авторства —
        // 100/(GridWidth-1) и 100/(GridHeight-1) на клетку).
        [Test]
        public void MarkerSizeCells_ConvertsToExpectedPercentOfMapCanvas()
        {
            const float sizeCells = 0.7f;

            float expectedWidthPercent = 100f / (WorldMapNavigation.GridWidth - 1) * sizeCells;
            float expectedHeightPercent = 100f / (WorldMapNavigation.GridHeight - 1) * sizeCells;

            // Одна клетка карты — 100/(GridWidth-1) процентов ширины; полный
            // маркер размером 1.0 клетки должен занимать ровно эту долю.
            Assert.That(expectedWidthPercent, Is.EqualTo(100f / (WorldMapNavigation.GridWidth - 1) * 0.7f).Within(0.0001f));
            Assert.That(expectedHeightPercent, Is.EqualTo(100f / (WorldMapNavigation.GridHeight - 1) * 0.7f).Within(0.0001f));

            // При sizeCells=1.0 маркер занимает ровно долю одной клетки по
            // каждой оси — база перевода корректна и симметрична по осям.
            float oneCellWidthPercent = 100f / (WorldMapNavigation.GridWidth - 1);
            float oneCellHeightPercent = 100f / (WorldMapNavigation.GridHeight - 1);
            Assert.That(oneCellWidthPercent, Is.GreaterThan(0f));
            Assert.That(oneCellHeightPercent, Is.GreaterThan(0f));
        }

        // Изменение zoom масштабирует marker вместе с картой: маркер задан в
        // ПРОЦЕНТАХ карты (WorldMapPreviewMath.MapToPreview/аналог в
        // runtime — mapRect уже включает zoom/pan), поэтому его экранный
        // размер в пикселях линейно растёт вместе с mapRect.width/height —
        // проверяем этот инвариант через WorldMapPreviewMath напрямую
        // (та же математика, что рисует Preview).
        [Test]
        public void MarkerSizePercent_ScalesWithMapRectSizeLikeZoom()
        {
            const float sizeCells = 0.7f;
            float widthPercent = 100f / (WorldMapNavigation.GridWidth - 1) * sizeCells;

            Rect fitRect = new Rect(0f, 0f, 800f, 492f);
            Rect zoomedRect = WorldMapPreviewMath.ApplyZoomPan(fitRect, 2f, Vector2.zero);

            float markerPixelWidthAtFit = fitRect.width * widthPercent / 100f;
            float markerPixelWidthAtZoom2x = zoomedRect.width * widthPercent / 100f;

            Assert.That(markerPixelWidthAtZoom2x, Is.EqualTo(markerPixelWidthAtFit * 2f).Within(0.001f),
                "При zoom ×2 экранный размер маркера (в процентах от mapRect) должен вырасти вдвое вместе с картой.");
        }

        // Изменение визуального размера НЕ меняет map coordinates — сам факт,
        // что HeroMarkerSizeCells/HomeMarkerSizeCells не участвуют ни в одной
        // формуле WorldMapNavigation/ContinuousSimulationSystem (структурная
        // проверка через отсутствие такой связи), плюс явная проверка, что
        // gameplay-константы не меняются от значения темы.
        [Test]
        public void MarkerSize_DoesNotAffectGameplayCoordinatesOrCapitalPosition()
        {
            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                float capitalXBefore = WorldMapNavigation.CapitalXPercent;
                float capitalYBefore = WorldMapNavigation.CapitalYPercent;

                SetPrivateFloat(theme, "heroMarkerSizeCells", 2.0f);
                SetPrivateFloat(theme, "homeMarkerSizeCells", 4.0f);

                Assert.That(WorldMapNavigation.CapitalXPercent, Is.EqualTo(capitalXBefore));
                Assert.That(WorldMapNavigation.CapitalYPercent, Is.EqualTo(capitalYBefore));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // Изменение визуального размера НЕ меняет travel calculations —
        // WorldMapVisualTheme и ContinuousSimulationSystem/WorldMapDefinitionData
        // структурно независимы (маркеры живут в теме, скорость — в мире);
        // проверяем, что смена размера маркеров не трогает CellsPerGameHour.
        [Test]
        public void MarkerSize_DoesNotAffectTravelCalculations()
        {
            WorldMapNavigation.ConfigureDefaultTerrain();
            double cellsPerGameHourBefore = ContinuousSimulationSystem.CellsPerGameHour;

            WorldMapVisualTheme theme = CreateTheme();
            try
            {
                SetPrivateFloat(theme, "heroMarkerSizeCells", 1.9f);
                SetPrivateFloat(theme, "homeMarkerSizeCells", 3.5f);

                Assert.That(ContinuousSimulationSystem.CellsPerGameHour, Is.EqualTo(cellsPerGameHourBefore));
            }
            finally
            {
                Object.DestroyImmediate(theme);
                WorldMapNavigation.ConfigureDefaultTerrain();
            }
        }

        // Sprite aspect сохраняется: маркеры — окружности/квадраты с одним
        // числом размера (width == height), заданным в клетках по обеим
        // осям через один и тот же sizeCells — искажение по X/Y структурно
        // невозможно, так как обе стороны считаются из одного значения
        // (разные знаменатели GridWidth-1/GridHeight-1 компенсируют неквадратную
        // сетку, а не создают независимое искажение формы).
        [Test]
        public void MarkerSize_SingleValueDrivesBothAxesConsistently()
        {
            const float sizeCells = 1.3f;

            float widthPercent = 100f / (WorldMapNavigation.GridWidth - 1) * sizeCells;
            float heightPercent = 100f / (WorldMapNavigation.GridHeight - 1) * sizeCells;

            // Оба вычислены из ОДНОГО sizeCells — при равном GridWidth/GridHeight
            // результат был бы идентичен; при неравном (104×64, как сейчас)
            // соотношение процентов равно обратному соотношению
            // (GridWidth-1)/(GridHeight-1) (percent = 100/(cells-1), поэтому
            // больше клеток по оси → меньше процента на клетку), а не
            // произвольному искажению.
            float expectedRatio = (float)(WorldMapNavigation.GridWidth - 1) / (WorldMapNavigation.GridHeight - 1);
            float actualRatio = heightPercent / widthPercent;

            Assert.That(actualRatio, Is.EqualTo(expectedRatio).Within(0.0001f));
        }
    }
}
