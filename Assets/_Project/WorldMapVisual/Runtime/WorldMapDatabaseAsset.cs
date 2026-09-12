using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Фасад над картой: единственная точка входа, из которой рантайм берёт
    // активную визуальную тему. В следующих этапах (WM-06/WM-07) сюда же
    // добавятся ссылки на регионы/слоты — сам этот класс данные не хранит,
    // только связывает их.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapDatabase",
        menuName = "Kingdom Survival/Карта/World Map Database")]
    public sealed class WorldMapDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapDatabase";

        [SerializeField] private WorldMapVisualTheme activeTheme;

        public WorldMapVisualTheme ActiveTheme => activeTheme;
    }
}
