using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Полный визуальный набор карты: местность + ссылка на библиотеку иконок.
    // Игровые данные (регионы/локации/слоты) сюда не входят — тема отвечает
    // только за внешний вид.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapTheme",
        menuName = "Kingdom Survival/Карта/Visual Theme")]
    public sealed class WorldMapVisualTheme : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapTheme";

        [SerializeField] private List<WorldMapTerrainVisualProfile> terrainProfiles =
            new List<WorldMapTerrainVisualProfile>();
        [SerializeField] private WorldMapIconLibrary iconLibrary;

        public IReadOnlyList<WorldMapTerrainVisualProfile> TerrainProfiles => terrainProfiles;
        public WorldMapIconLibrary IconLibrary => iconLibrary;

        public WorldMapTerrainVisualProfile FindTerrainProfile(WorldMapTerrainType terrain)
        {
            if (terrainProfiles != null)
            {
                for (int i = 0; i < terrainProfiles.Count; i++)
                {
                    WorldMapTerrainVisualProfile profile = terrainProfiles[i];
                    if (profile != null && profile.Terrain == terrain)
                        return profile;
                }
            }

            return null;
        }
    }
}
