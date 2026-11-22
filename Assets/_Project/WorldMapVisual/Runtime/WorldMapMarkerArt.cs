using System;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Рисунки меток карты мира («?» — неисследованное место, «!» — можно взять
    // задание): один набор символов. Это UI-маркеры, не ассеты окружения;
    // смысл и привязка метки от рисунка не зависят. Видимая часть символа
    // (alpha ≥ 128) — для размера без учёта прозрачных полей PNG.
    // Ассет создаёт и обновляет «Kingdom Survival → Карта → Метки карты: собрать».
    [CreateAssetMenu(fileName = "KingdomSurvivalWorldMapMarkers", menuName = "Kingdom Survival/Карта/Метки карты")]
    public sealed class WorldMapMarkerArt : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapMarkers";
        public const string AssetPath = "Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalWorldMapMarkers.asset";
        public const string ArtRoot = "Assets/_Project/Art/MapMarkers/classic";

        [Tooltip("Высота видимой части символа на экране, px.")]
        [Range(16f, 64f)] public float SymbolHeight = 32f;
        [Tooltip("Отступ над якорем места (верх рисунка или точка места), px.")]
        [Range(0f, 30f)] public float Gap = 8f;
        [Tooltip("Наименьшее расстояние между соседними метками, px (плотная деревня).")]
        [Range(0f, 40f)] public float MinSpacing = 26f;

        public WorldMapMarkerSymbol Question = new WorldMapMarkerSymbol();
        public WorldMapMarkerSymbol Exclamation = new WorldMapMarkerSymbol();

        public bool IsComplete => Question?.Texture != null && Exclamation?.Texture != null;

        private static WorldMapMarkerArt loaded;
        private static bool searched;

        public static WorldMapMarkerArt Load()
        {
            if (!searched || loaded == null)
            {
                loaded = Resources.Load<WorldMapMarkerArt>(ResourcesPath);
                searched = true;
            }
            return loaded;
        }
    }

    [Serializable]
    public sealed class WorldMapMarkerSymbol
    {
        public Texture2D Texture;
        // Видимая часть в долях холста, отсчёт от левого ВЕРХНЕГО угла.
        public Rect Visible = new Rect(0f, 0f, 1f, 1f);
    }
}
