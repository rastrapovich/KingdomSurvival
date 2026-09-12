using System;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // WM-09: внешний вид реки. Геометрия (какие клетки — река) остаётся в
    // WorldMapNavigation — этот класс только про то, как её нарисовать.
    // Без назначенного SegmentSprite используется FallbackColor (та же
    // деградация без арта, что и у WorldMapTerrainVisualProfile).
    [Serializable]
    public sealed class WorldMapWaterVisualProfile
    {
        [SerializeField] private Sprite segmentSprite;
        [SerializeField] private Color fallbackColor = new Color(0.30f, 0.42f, 0.52f, 0.75f);
        [SerializeField] private float widthPixels = 6f;

        public Sprite SegmentSprite => segmentSprite;
        public Color FallbackColor => fallbackColor;
        public float WidthPixels => widthPixels;
    }
}
