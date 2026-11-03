using System;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 05: камера до Zoom 3 и 35°, единое перо, живые края, модули оград и
    // мостов, деревня и новый пакет растровых ассетов. Только ручки этапа 05;
    // всё прочее — группы этапов 03 и 04 (тот же конвейер).
    [Serializable]
    public sealed class MapLabStage05Settings
    {
        [Header("Камера")]
        [Tooltip("Наибольшее ФАКТИЧЕСКОЕ приближение этапа 05 (2–4). Прежний предел рига 8 безопасно ограничивается этим значением: колесо, курсор, кнопки окна, плавная цель и экспорт читают один и тот же предел.")]
        [Range(2f, 4f)] public float ZoomMax = 3f;
        [Tooltip("Наклон при Zoom 1 (общий вид) и ровно на фактическом наибольшем приближении. Рисунки по наклону не переключаются.")]
        public Vector2 PitchRange = new Vector2(60f, 35f);

        [Header("Сглаживание")]
        [Tooltip("AlphaToMask на карточках и модулях (работает только при MSAA цели; без MSAA — обычный порог).")]
        public bool AlphaToCoverage = true;
        [Tooltip("Анизотропная фильтрация тайлов краски земли (полезна при низком наклоне).")]
        [Range(1, 16)] public int GroundAniso = 8;
        [Tooltip("Экспорт: суперсэмплинг (усреднение в линейном свете) и MSAA своей RenderTexture.")]
        [Range(1, 3)] public int ExportSupersample = 2;
        [Range(1, 8)] public int ExportMsaa = 4;

        [Header("Перо (единый профиль)")]
        [Tooltip("Тёплая тёмно-коричневая тушь (sRGB).")]
        public Color InkColor = new Color(0.28f, 0.2f, 0.12f, 1f);
        [Tooltip("Синяя тушь воды и кромки у воды (sRGB).")]
        public Color WaterInkColor = new Color(0.2f, 0.27f, 0.36f, 1f);
        [Tooltip("Толщина в мире, ед. Предел в пикселях поднят вместе с ней — иначе рост толщины съест верхний предел.")]
        [Range(0.02f, 0.5f)] public float InkWidth = 0.13f;
        [Range(0.5f, 5f)] public float InkMinPixels = 1.6f;
        [Range(2f, 16f)] public float InkMaxPixels = 6f;
        [Tooltip("Плотность пигмента (непрозрачность в середине линии).")]
        [Range(0f, 1f)] public float InkDensity = 0.88f;
        [Tooltip("Плавное изменение толщины вдоль линии: доля и длина волны, ед.")]
        [Range(0f, 0.8f)] public float InkThicknessDrift = 0.45f;
        [Range(2f, 30f)] public float InkDriftLength = 7f;
        [Tooltip("Короткие разрывы: длина участка между разрывами, ед., и доля разрыва.")]
        [Range(1f, 30f)] public float InkBreakLength = 9f;
        [Range(0f, 0.5f)] public float InkBreakShare = 0.1f;
        [Tooltip("Зерно края.")]
        [Range(0f, 1f)] public float InkGrain = 0.4f;
        [Tooltip("Доля длинных меж с пером (у дорог, на изломах склона, длинные отрезки).")]
        [Range(0f, 1f)] public float FieldInkShare = 0.45f;
        [Tooltip("Доля кромки берега с пером.")]
        [Range(0f, 1f)] public float ShoreInkShare = 0.55f;
        [Tooltip("Доля внешних контуров уступа с пером.")]
        [Range(0f, 1f)] public float CliffInkShare = 0.6f;

        [Header("Мазки")]
        [Tooltip("Мазки собираются в группы разного размера с просветами (без «забора»).")]
        [Range(0f, 1f)] public float StrokeGrouping = 0.65f;
        [Tooltip("Плотность мазков относительно этапа 04.")]
        [Range(0.5f, 2.5f)] public float StrokeDensity = 1.45f;
        [Tooltip("Ширина мягкого края мазка, текселей (узкая — чётче).")]
        [Range(0.2f, 2f)] public float StrokeEdge = 0.45f;

        [Header("Акварельные пятна (Wash PNG)")]
        [Tooltip("Сила пятна (0.04–0.12 по паспорту).")]
        [Range(0f, 0.2f)] public float WashStrength = 0.08f;
        [Tooltip("Размер пятна в мире, ед.")]
        [Range(6f, 60f)] public float WashScale = 22f;
        [Tooltip("Пятен на 10 000 кв. ед. луга.")]
        [Range(0f, 30f)] public float WashDensity = 9f;

        [Header("Трава, тростник, камни, сено (PNG)")]
        [Range(0f, 2f)] public float GrassDensity = 1f;
        [Range(0f, 2f)] public float ReedDensity = 1f;
        [Range(0f, 2f)] public float RockDensity = 1f;
        [Range(0f, 2f)] public float HayDensity = 1f;
        [Tooltip("Плавное исчезновение мелких спрайтов вдали: пикселей на единицу мира, ниже которых — полностью скрыты, и выше которых — полностью видны.")]
        public Vector2 DetailFadePixels = new Vector2(5f, 11f);

        [Header("Тень")]
        [Tooltip("Цвет тени — приглушённый оливково-серый (умножение).")]
        public Color ShadowColor = new Color(0.6f, 0.63f, 0.56f, 1f);
        [Tooltip("Доля внешнего края, теряющая плотность (живой край).")]
        [Range(0f, 1f)] public float ShadowEdgeLoss = 0.5f;
        [Tooltip("Размер зон живого края в мире, ед. (крупнее пиксельного шума).")]
        [Range(1f, 12f)] public float ShadowEdgeScale = 4.5f;

        [Header("Уступы")]
        [Tooltip("Ширина неравных плоскостей стены, ед.")]
        public Vector2 CliffPlaneWidth = new Vector2(1.6f, 5.2f);
        [Tooltip("Наибольший отступ грани вглубь, ед.")]
        [Range(0f, 2f)] public float CliffSetback = 0.7f;
        [Tooltip("Доля граней с полкой.")]
        [Range(0f, 1f)] public float CliffLedgeShare = 0.35f;
        [Tooltip("Доля оснований с осыпью.")]
        [Range(0f, 1f)] public float CliffScreeShare = 0.25f;
        [Tooltip("Тёплый серо-охристый камень.")]
        public Color CliffColor = new Color(0.63f, 0.58f, 0.49f, 1f);

        [Tooltip("Рисованные скалы пакета ревизии 06 вместо граней (если пакет подключён).")]
        public bool DrawnCliffs = true;

        [Header("Вода и берег")]
        [Tooltip("Спокойная серо-синяя основа.")]
        public Color WaterBase = new Color(0.4f, 0.48f, 0.53f, 1f);
        [Tooltip("Штрихи течения: река и озеро.")]
        [Range(0f, 1f)] public float RiverStrokes = 0.42f;
        [Range(0f, 1f)] public float LakeStrokes = 0.22f;
        [Tooltip("Проходов сглаживания общей границы берега.")]
        [Range(0, 8)] public int ShoreSmoothing = 3;
        [Tooltip("Охристая полоса у кромки (доля участков).")]
        [Range(0f, 1f)] public float ShoreOchreShare = 0.35f;

        [Header("Ограды и мосты")]
        [Tooltip("Наибольшее расстояние между столбами, ед.: длинный пролёт делится промежуточными узлами.")]
        [Range(1.2f, 4f)] public float FenceSpan = 2.6f;
        [Tooltip("Короче — остаток пролёта сливается с соседним.")]
        [Range(0.3f, 2f)] public float FenceMinSpan = 1.1f;
        [Tooltip("Ширина ворот (просвет между двумя узлами), ед.")]
        [Range(1f, 3f)] public float GateWidth = 1.8f;
        [Tooltip("Длина одной панели настила моста вдоль, ед.")]
        [Range(0.6f, 4f)] public float DeckPanelLength = 1.7f;
        [Tooltip("Шаг козел под мостом, ед. (только где они нужны).")]
        [Range(2f, 8f)] public float TrestleSpacing = 4f;
        public Color WoodColor = new Color(0.66f, 0.54f, 0.39f, 1f);

        [Header("Деревня")]
        [Range(3, 12)] public int YardsMin = 5;
        [Range(3, 12)] public int YardsMax = 9;
        [Tooltip("Расширение дороги у колодца, ед.")]
        [Range(0f, 8f)] public float WellWidening = 2.4f;

        // Набор этапа 05 для регистрации ракурсов построек при генерации
        // (назначает MapLabView перед генерацией; не сериализуется).
        [NonSerialized] public MapLabArtSet Art;
    }
}
