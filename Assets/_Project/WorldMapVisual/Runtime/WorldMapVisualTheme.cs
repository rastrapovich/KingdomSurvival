using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // AM-07.5 (канон v1.35, §9.9): визуальный набор карты сведён к фону и
    // библиотеке иконок локаций. Профили местности (Plains/Hills/Mountains
    // цвет/масса) удалены — код больше не рисует рельеф ни клетками, ни
    // спрайтами-массами; вся видимая география (включая холмы, лес, поля)
    // рисуется художником прямо в baseMapSprite.
    //
    // Задача "Map Art Layers": Base Map (baseMapSprite) остаётся
    // необязательным фоном на всю глобальную карту (0..100×0..100), а
    // artLayers — отдельные PNG-фрагменты, каждый занимает только свой
    // Bounds в ТОЙ ЖЕ системе координат карты, что Roads/Locations/Spawn
    // Slots/герой — позволяет дорисовывать глобальную карту постепенно,
    // не растягивая один PNG на всю площадь.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapTheme",
        menuName = "Kingdom Survival/Карта/Visual Theme")]
    public sealed class WorldMapVisualTheme : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapTheme";

        [SerializeField] private Sprite baseMapSprite;
        [SerializeField] private Color baseMapColor = new Color(0.16f, 0.17f, 0.15f, 1f);
        [SerializeField] private Color baseMapTint = Color.white;
        [SerializeField] private WorldMapIconLibrary iconLibrary;
        [SerializeField] private List<WorldMapArtLayerEntry> artLayers = new List<WorldMapArtLayerEntry>();

        public Sprite BaseMapSprite => baseMapSprite;
        public Color BaseMapColor => baseMapColor;
        public Color BaseMapTint => baseMapTint;
        public WorldMapIconLibrary IconLibrary => iconLibrary;
        public IReadOnlyList<WorldMapArtLayerEntry> ArtLayers => artLayers;
    }

    public enum WorldMapArtLayerFitMode
    {
        PreserveAspect,
        Stretch
    }

    // Один авторский PNG-фрагмент глобальной карты. Bounds — проценты
    // 0..100 по обеим осям, та же система координат, что у Roads/Locations/
    // Spawn Slots/Terrain Areas/героя — НЕ пиксели PNG и НЕ экранные
    // координаты Preview/runtime.
    [System.Serializable]
    public sealed class WorldMapArtLayerEntry
    {
        public string Id;
        public string DisplayName;
        public bool Enabled = true;
        public Sprite Sprite;

        public float MinXPercent;
        public float MaxXPercent = 100f;
        public float MinYPercent;
        public float MaxYPercent = 100f;

        // Меньший Order — ниже (рисуется раньше, другие слои могут лечь
        // поверх). Base Map всегда ниже всех Art Layers.
        public int Order;

        [Range(0f, 1f)] public float Opacity = 1f;

        public WorldMapArtLayerFitMode FitMode = WorldMapArtLayerFitMode.PreserveAspect;
    }

    // Единственный источник правды для порядка/фильтрации слоёв — используется
    // и Preview (Editor), и runtime-рендером карты, чтобы визуал никогда не
    // разошёлся между режимами (раздел 25 задачи "Map Art Layers").
    public static class WorldMapArtLayerUtility
    {
        public static List<WorldMapArtLayerEntry> GetOrderedEnabledLayers(
            IReadOnlyList<WorldMapArtLayerEntry> layers)
        {
            List<WorldMapArtLayerEntry> result = new List<WorldMapArtLayerEntry>();
            if (layers == null)
                return result;

            foreach (WorldMapArtLayerEntry layer in layers)
            {
                if (layer != null && layer.Enabled && layer.Sprite != null)
                    result.Add(layer);
            }

            // List<T>.Sort — не стабильна; сортируем индексами, чтобы при
            // равном Order сохранялся исходный порядок списка (раздел 17 —
            // overlap разрешён, порядок среди равных Order предсказуем).
            List<int> indices = new List<int>(result.Count);
            for (int i = 0; i < result.Count; i++)
                indices.Add(i);

            indices.Sort((a, b) =>
            {
                int cmp = result[a].Order.CompareTo(result[b].Order);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            List<WorldMapArtLayerEntry> sorted = new List<WorldMapArtLayerEntry>(result.Count);
            foreach (int index in indices)
                sorted.Add(result[index]);

            return sorted;
        }
    }
}
