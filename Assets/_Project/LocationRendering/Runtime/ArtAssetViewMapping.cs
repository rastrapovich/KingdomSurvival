using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.ArtAssets;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12Н: ракурсы Базы ассетов и Базы анимаций — один контракт шести
    // направлений. Модули не зависят друг от друга; соответствие — здесь,
    // явно, без опоры на числовые значения enum.
    public static class ArtAssetViewMapping
    {
        public static CreatureAnimationDirection ToDirection(ArtAssetView view)
        {
            switch (view)
            {
                case ArtAssetView.FrontRight: return CreatureAnimationDirection.FrontRight;
                case ArtAssetView.BackRight: return CreatureAnimationDirection.BackRight;
                case ArtAssetView.Back: return CreatureAnimationDirection.Back;
                case ArtAssetView.BackLeft: return CreatureAnimationDirection.BackLeft;
                case ArtAssetView.FrontLeft: return CreatureAnimationDirection.FrontLeft;
                default: return CreatureAnimationDirection.Front;
            }
        }

        public static ArtAssetView FromDirection(CreatureAnimationDirection direction)
        {
            switch (direction)
            {
                case CreatureAnimationDirection.FrontRight: return ArtAssetView.FrontRight;
                case CreatureAnimationDirection.BackRight: return ArtAssetView.BackRight;
                case CreatureAnimationDirection.Back: return ArtAssetView.Back;
                case CreatureAnimationDirection.BackLeft: return ArtAssetView.BackLeft;
                case CreatureAnimationDirection.FrontLeft: return ArtAssetView.FrontLeft;
                default: return ArtAssetView.Front;
            }
        }
    }
}
