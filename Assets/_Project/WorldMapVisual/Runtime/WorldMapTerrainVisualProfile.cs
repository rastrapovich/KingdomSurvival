using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Внешний вид одного типа местности (Plains/Hills/Mountains). Сам тип и его
    // геймплейный смысл (проходимость, множитель скорости) остаются в
    // WorldMapNavigation — этот класс отвечает только за то, как местность
    // выглядит на карте.
    [Serializable]
    public sealed class WorldMapTerrainVisualProfile
    {
        [SerializeField] private WorldMapTerrainType terrain = WorldMapTerrainType.Plains;
        [SerializeField] private Color cellColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private List<Sprite> massVariants = new List<Sprite>();

        public WorldMapTerrainType Terrain => terrain;
        public Color CellColor => cellColor;
        public IReadOnlyList<Sprite> MassVariants => massVariants;

        public Sprite PickVariant(System.Random random)
        {
            if (massVariants == null || massVariants.Count == 0)
                return null;

            int index = random != null ? random.Next(massVariants.Count) : 0;
            return massVariants[index];
        }
    }
}
