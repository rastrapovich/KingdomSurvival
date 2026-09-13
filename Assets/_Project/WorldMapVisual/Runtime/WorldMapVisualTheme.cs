using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // AM-07.5 (канон v1.35, §9.9): визуальный набор карты сведён к фону и
    // библиотеке иконок локаций. Профили местности (Plains/Hills/Mountains
    // цвет/масса) удалены — код больше не рисует рельеф ни клетками, ни
    // спрайтами-массами; вся видимая география (включая холмы, лес, поля)
    // рисуется художником прямо в baseMapSprite.
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

        public Sprite BaseMapSprite => baseMapSprite;
        public Color BaseMapColor => baseMapColor;
        public Color BaseMapTint => baseMapTint;
        public WorldMapIconLibrary IconLibrary => iconLibrary;
    }
}
