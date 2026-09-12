using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Переиспользуемый набор иконок локаций. Отдельный ассет от Visual Theme,
    // чтобы иконки можно было переиспользовать между темами оформления.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapIcons",
        menuName = "Kingdom Survival/Карта/Icon Library")]
    public sealed class WorldMapIconLibrary : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapIcons";

        [SerializeField] private Sprite defaultLocationIcon;
        [SerializeField] private List<WorldMapLocationIconEntry> locationIcons =
            new List<WorldMapLocationIconEntry>();

        public Sprite DefaultLocationIcon => defaultLocationIcon;
        public IReadOnlyList<WorldMapLocationIconEntry> LocationIcons => locationIcons;

        public Sprite FindIconForLocation(string locationId)
        {
            if (!string.IsNullOrEmpty(locationId) && locationIcons != null)
            {
                for (int i = 0; i < locationIcons.Count; i++)
                {
                    WorldMapLocationIconEntry entry = locationIcons[i];
                    if (entry != null && entry.LocationId == locationId && entry.Icon != null)
                        return entry.Icon;
                }
            }

            return defaultLocationIcon;
        }
    }
}
