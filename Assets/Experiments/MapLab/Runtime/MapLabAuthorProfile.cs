using System;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public enum MapLabWorldSize { Small = 0, Medium = 1, Large = 2, Huge = 3 }
    public enum MapLabDisplayQuality { Fast = 0, Standard = 1, High = 2 }
    public enum MapLabDiscoveryRate { Rare = 0, Normal = 1, Rich = 2 }
    public enum MapLabVillageSize { Auto = 0, Small = 1, Medium = 2, Large = 3 }

    // Профиль автора для окна «Генератор карты» (мастер-бриф перехода, §5):
    // десять основных настроек, семь полей внешнего вида и восемь полей точной
    // настройки. Это не вторая копия чисел: значения пересчитываются в уже
    // работающие поля MapLabSettings (MapLabAuthor.Apply) от снимка рабочих
    // значений (база). Нейтральные положения (0.5) дают ровно базу; точная
    // правка — переопределение со своим флагом, макропараметр её не стирает.
    [Serializable]
    public sealed class MapLabAuthorProfile
    {
        [Tooltip("База снята: макропараметры отсчитываются от снимка рабочих значений.")]
        public bool Captured;

        [Header("Основные (структура мира)")]
        [Tooltip("12У-D: регион строит генератор игры (план), MapLab его рисует. Выключено — пресеты MapLab для предпросмотра.")]
        public bool UseGamePlan = true;
        [Tooltip("Тип региона плана: 0 по seed, 1 речная долина, 2 лесное озеро, 3 холм и уступ, 4 обжитая земля.")]
        public int PlanRegion;
        public MapLabWorldSize WorldSize = MapLabWorldSize.Medium;
        [Range(0f, 1f)] public float Relief = 0.5f;
        [Range(0f, 1f)] public float Water = 0.5f;
        [Range(0f, 1f)] public float Forest = 0.5f;
        [Range(0f, 1f)] public float Settlement = 0.5f;
        public MapLabDiscoveryRate Discoveries = MapLabDiscoveryRate.Normal;
        [Range(0f, 1f)] public float Connectivity = 0.5f;
        public MapLabDisplayQuality Quality = MapLabDisplayQuality.Standard;

        [Header("Внешний вид")]
        [Range(0f, 1f)] public float StrokeStrength = 0.5f;
        [Range(0f, 1f)] public float StrokeSize = 0.5f;
        [Range(0f, 1f)] public float InkWeight = 0.5f;
        [Range(0f, 1f)] public float ShadowStrength = 0.5f;
        [Range(0f, 1f)] public float Details = 0.5f;

        [Header("Точная настройка (переопределения)")]
        public bool RiverWidthSet; public float RiverWidth = 11f;
        public bool RoadWidthSet; public float RoadWidth = 3.2f;
        public bool TreeSizeSet; public float TreeSize = 4.6f;
        public bool ConiferShareSet; public float ConiferShare = 0.18f;
        public MapLabVillageSize VillageSize = MapLabVillageSize.Auto;
        public bool FencedShareSet; public float FencedShare = 0.2f;
        public bool FieldDensitySet; public float FieldDensity = 0.7f;
        public bool CliffHeightSet; public float CliffHeight = 6f;

        [Header("База (снимок рабочих значений)")]
        public float BaseHeightRange = 12f, BaseHillHeight = 1f, BaseCliffHeight = 6f;
        public float BaseLakeScale = 1f, BaseRiverWidth = 11f, BaseLevelOffset;
        public float BaseForestDensity = 0.45f;
        public float BaseFieldDensity = 0.7f, BaseFieldRadius = 95f;
        public int BaseYardsMin = 5, BaseYardsMax = 9;
        public float BaseFencedShare = 0.2f;
        public float BaseRoadWidth = 3.2f, BasePathWidth = 1.6f;
        public float BaseTreeSize = 4.6f, BaseConiferShare = 0.18f;
        public float BaseBrushStrength = 0.55f, BaseBrushScale = 1.6f;
        public float BaseInkDensity = 0.88f, BaseInkWidth = 0.13f;
        public float BaseShadowStrength = 0.4f, BaseContactStrength = 0.25f;
        public float BaseGrass = 1f, BaseReed = 1f, BaseRock = 1f, BaseHay = 1f, BaseInkGrass = 1f;
    }

    public static class MapLabAuthor
    {
        // Стороны мира по размерам-пресетам. Выше 512 — после подгрузки (этап C);
        // до тех пор «Очень большой» — проверочный размер, не для игры.
        public static float SideOf(MapLabWorldSize size)
        {
            switch (size)
            {
                case MapLabWorldSize.Small: return 192f;
                case MapLabWorldSize.Large: return 384f;
                case MapLabWorldSize.Huge: return 512f;
                default: return 256f;
            }
        }

        public static bool Verified(MapLabWorldSize size) => size != MapLabWorldSize.Huge;

        public static MapLabWorldSize SizeOf(float side)
        {
            if (side <= 224f) return MapLabWorldSize.Small;
            if (side <= 320f) return MapLabWorldSize.Medium;
            if (side <= 448f) return MapLabWorldSize.Large;
            return MapLabWorldSize.Huge;
        }

        // Множитель вокруг базы: 0 → low, 0.5 → 1, 1 → high.
        public static float Factor(float m, float low, float high)
        {
            m = Mathf.Clamp01(m);
            return m < 0.5f ? Mathf.Lerp(low, 1f, m * 2f) : Mathf.Lerp(1f, high, (m - 0.5f) * 2f);
        }

        // Доля 0..1 вокруг базы: вниз — к base × low, вверх — к максимуму.
        public static float Share(float m, float baseValue, float low, float max)
        {
            m = Mathf.Clamp01(m);
            return m < 0.5f ? Mathf.Lerp(baseValue * low, baseValue, m * 2f) : Mathf.Lerp(baseValue, Mathf.Max(baseValue, max), (m - 0.5f) * 2f);
        }

        // Снимок рабочих значений как базы макропараметров.
        public static void Capture(MapLabSettings s)
        {
            MapLabAuthorProfile a = s.Author;
            a.BaseHeightRange = s.Relief.HeightRange;
            a.BaseHillHeight = s.Relief.HillHeight;
            a.BaseCliffHeight = s.Cliffs.CliffHeight;
            a.BaseLakeScale = s.Water.LakeScale;
            a.BaseRiverWidth = s.Water.RiverWidth;
            a.BaseLevelOffset = s.Water.LevelOffset;
            a.BaseForestDensity = s.Vegetation.ForestDensity;
            a.BaseFieldDensity = s.RoadsAndFields.FieldDensity;
            a.BaseFieldRadius = s.RoadsAndFields.FieldRadius;
            a.BaseYardsMin = s.Stage05.YardsMin;
            a.BaseYardsMax = s.Stage05.YardsMax;
            a.BaseFencedShare = s.Stage05.FencedYardShare;
            a.BaseRoadWidth = s.RoadsAndFields.RoadWidth;
            a.BasePathWidth = s.RoadsAndFields.PathWidth;
            a.BaseTreeSize = s.Vegetation.TreeSize;
            a.BaseConiferShare = s.Vegetation.ConiferShare;
            a.BaseBrushStrength = s.PainterlyStyle.BrushStrength;
            a.BaseBrushScale = s.PainterlyStyle.BrushScale;
            a.BaseInkDensity = s.Stage05.InkDensity;
            a.BaseInkWidth = s.Stage05.InkWidth;
            a.BaseShadowStrength = s.Stage03.ShadowStrength;
            a.BaseContactStrength = s.Stage03.ContactStrength;
            a.BaseGrass = s.Stage05.GrassDensity;
            a.BaseReed = s.Stage05.ReedDensity;
            a.BaseRock = s.Stage05.RockDensity;
            a.BaseHay = s.Stage05.HayDensity;
            a.BaseInkGrass = s.Stage05.InkGrassDensity;
            a.WorldSize = SizeOf(s.General.MapSize);
            a.Quality = s.General.Quality == MapLabQuality.Samples129 ? MapLabDisplayQuality.Fast : MapLabDisplayQuality.Standard;
            a.RiverWidth = a.BaseRiverWidth;
            a.RoadWidth = a.BaseRoadWidth;
            a.TreeSize = a.BaseTreeSize;
            a.ConiferShare = a.BaseConiferShare;
            a.FencedShare = a.BaseFencedShare;
            a.FieldDensity = a.BaseFieldDensity;
            a.CliffHeight = a.BaseCliffHeight;
            a.Captured = true;
        }

        // Эффективные значения в рабочие поля. Структурные и художественные
        // части разделены: окно применяет их разными командами.
        public static void ApplyStructure(MapLabSettings s)
        {
            MapLabAuthorProfile a = s.Author;
            if (!a.Captured)
                Capture(s);
            s.General.MapSize = SideOf(a.WorldSize);
            float relief = Factor(a.Relief, 0.45f, 1.8f);
            s.Relief.HeightRange = Mathf.Clamp(a.BaseHeightRange * relief, 2f, 40f);
            s.Relief.HillHeight = Mathf.Clamp(a.BaseHillHeight * Factor(a.Relief, 0.5f, 1.45f), 0f, 1.5f);
            s.Cliffs.CliffHeight = a.CliffHeightSet ? Mathf.Clamp(a.CliffHeight, 0f, 20f) : Mathf.Clamp(a.BaseCliffHeight * Factor(a.Relief, 0.4f, 1.7f), 0f, 20f);
            s.Water.LakeScale = Mathf.Clamp(a.BaseLakeScale * Factor(a.Water, 0.7f, 1.35f), 0.5f, 1.6f);
            s.Water.LevelOffset = Mathf.Clamp(a.BaseLevelOffset + (a.Water - 0.5f) * 1.6f, -3f, 3f);
            s.Water.RiverWidth = a.RiverWidthSet ? Mathf.Clamp(a.RiverWidth, 3f, 30f) : Mathf.Clamp(a.BaseRiverWidth * Factor(a.Water, 0.75f, 1.3f), 3f, 30f);
            s.Vegetation.ForestDensity = Mathf.Clamp01(Share(a.Forest, a.BaseForestDensity, 0.25f, 0.95f));
            s.Vegetation.TreeSize = a.TreeSizeSet ? Mathf.Clamp(a.TreeSize, 1.5f, 10f) : a.BaseTreeSize;
            s.Vegetation.ConiferShare = a.ConiferShareSet ? Mathf.Clamp01(a.ConiferShare) : a.BaseConiferShare;
            s.RoadsAndFields.FieldDensity = a.FieldDensitySet ? Mathf.Clamp01(a.FieldDensity) : Mathf.Clamp01(Share(a.Settlement, a.BaseFieldDensity, 0.2f, 1f));
            s.RoadsAndFields.FieldRadius = Mathf.Clamp(a.BaseFieldRadius * Factor(a.Settlement, 0.7f, 1.35f), 20f, 200f);
            float roadWidth = a.RoadWidthSet ? Mathf.Clamp(a.RoadWidth, 1f, 8f) : a.BaseRoadWidth;
            s.RoadsAndFields.RoadWidth = roadWidth;
            s.RoadsAndFields.PathWidth = Mathf.Clamp(a.BasePathWidth * roadWidth / Mathf.Max(0.1f, a.BaseRoadWidth), 0.5f, 5f);
            Vector2Int yards = Yards(a);
            s.Stage05.YardsMin = yards.x;
            s.Stage05.YardsMax = yards.y;
            s.Stage05.FencedYardShare = a.FencedShareSet ? Mathf.Clamp(a.FencedShare, 0f, 0.5f) : a.BaseFencedShare;
            switch (a.Quality)
            {
                case MapLabDisplayQuality.Fast:
                    s.General.Quality = MapLabQuality.Samples129;
                    s.Stage03.PaintTexelsPerUnit = 12f;
                    break;
                case MapLabDisplayQuality.High:
                    s.General.Quality = MapLabQuality.Samples257;
                    s.Stage03.PaintTexelsPerUnit = 20f;
                    break;
                default:
                    s.General.Quality = MapLabQuality.Samples257;
                    s.Stage03.PaintTexelsPerUnit = 16f;
                    break;
            }
        }

        // Размер деревни: авто — от освоенности вокруг базы; явный — диапазон
        // дворов. Предел текущего Stage05 — 12 дворов (крупные — этап E).
        public static Vector2Int Yards(MapLabAuthorProfile a)
        {
            switch (a.VillageSize)
            {
                case MapLabVillageSize.Small: return new Vector2Int(3, 6);
                case MapLabVillageSize.Medium: return new Vector2Int(7, 12);
                case MapLabVillageSize.Large: return new Vector2Int(12, 18);
            }
            float f = Factor(a.Settlement, 0.6f, 1.4f);
            int min = Mathf.Clamp(Mathf.RoundToInt(a.BaseYardsMin * f), 3, 12);
            int max = Mathf.Clamp(Mathf.RoundToInt(a.BaseYardsMax * f), min, 18);
            return new Vector2Int(min, max);
        }

        public static void ApplyLook(MapLabSettings s)
        {
            MapLabAuthorProfile a = s.Author;
            if (!a.Captured)
                Capture(s);
            s.PainterlyStyle.BrushStrength = Mathf.Clamp01(a.BaseBrushStrength * Factor(a.StrokeStrength, 0.3f, 1.7f));
            s.PainterlyStyle.BrushScale = Mathf.Clamp(a.BaseBrushScale * Factor(a.StrokeSize, 0.6f, 1.6f), 0.3f, 6f);
            s.Stage05.InkDensity = Mathf.Clamp01(a.BaseInkDensity * Factor(a.InkWeight, 0.45f, 1.14f));
            s.Stage05.InkWidth = Mathf.Clamp(a.BaseInkWidth * Factor(a.InkWeight, 0.7f, 1.4f), 0.02f, 0.5f);
            s.Stage03.ShadowStrength = Mathf.Clamp01(a.BaseShadowStrength * Factor(a.ShadowStrength, 0f, 1.8f));
            s.Stage03.ContactStrength = Mathf.Clamp01(a.BaseContactStrength * Factor(a.ShadowStrength, 0f, 1.8f));
            float details = Factor(a.Details, 0f, 2f);
            s.Stage05.GrassDensity = Mathf.Clamp(a.BaseGrass * details, 0f, 2f);
            s.Stage05.ReedDensity = Mathf.Clamp(a.BaseReed * details, 0f, 2f);
            s.Stage05.RockDensity = Mathf.Clamp(a.BaseRock * details, 0f, 2f);
            s.Stage05.HayDensity = Mathf.Clamp(a.BaseHay * details, 0f, 2f);
            s.Stage05.InkGrassDensity = Mathf.Clamp(a.BaseInkGrass * details, 0f, 2f);
        }
    }
}
