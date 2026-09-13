using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Единая авторская база карты: активная тема и стартовый каталог
    // локаций. Координаты по-прежнему определяются один раз из WorldSeed и
    // Spawn Slots при создании партии, а эта база отвечает за то, какие
    // локации существуют и как выглядят их маркеры.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapDatabase",
        menuName = "Kingdom Survival/Карта/World Map Database")]
    public sealed class WorldMapDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapDatabase";

        [SerializeField] private WorldMapVisualTheme activeTheme;
        [SerializeField] private List<WorldMapLocationDefinition> locations =
            new List<WorldMapLocationDefinition>();

        public WorldMapVisualTheme ActiveTheme => activeTheme;
        public IReadOnlyList<WorldMapLocationDefinition> Locations => locations;

        public IReadOnlyList<WorldMapLocationTemplateData> BuildRuntimeLocationTemplates()
        {
            List<WorldMapLocationTemplateData> result =
                new List<WorldMapLocationTemplateData>();

            if (locations == null)
                return result;

            foreach (WorldMapLocationDefinition location in locations)
            {
                if (location != null && !string.IsNullOrWhiteSpace(location.Id))
                    result.Add(location.ToRuntimeTemplate());
            }

            return result;
        }

        public WorldMapLocationDefinition FindLocation(string locationId)
        {
            if (string.IsNullOrEmpty(locationId) || locations == null)
                return null;

            foreach (WorldMapLocationDefinition location in locations)
            {
                if (location != null && location.Id == locationId)
                    return location;
            }

            return null;
        }
    }
}
