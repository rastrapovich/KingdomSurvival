using NUnit.Framework;

namespace KingdomSurvival.MapLabBridge
{
    // 12И/12У: клик по экрану → точка земли с учётом сдвига рисунка по высоте.
    public sealed class MapLabGroundPickTests
    {
        [Test]
        public void ConstantShiftIsInverted()
        {
            float y = MapLabGameLive.SolveGroundY(g => g - 5f, 50f, 12f, 0.25f);
            Assert.That(y, Is.EqualTo(55f).Within(0.01f));
        }

        [Test]
        public void SlopedShiftIsInverted()
        {
            // Экранный y = g + 0.1·(g − 40): подставляем найденный корень обратно.
            float y = MapLabGameLive.SolveGroundY(g => g + 0.1f * (g - 40f), 52f, 12f, 0.25f);
            Assert.That(y + 0.1f * (y - 40f), Is.EqualTo(52f).Within(0.01f));
        }

        [Test]
        public void ClosestToCameraRootIsChosenAtLedge()
        {
            // Уступ: за g = 50 земля поднимается и рисуется на 4 выше. Цель 48 видна
            // и до уступа (g = 48 → 48), и на верхней площадке (g = 52 → 48).
            float y = MapLabGameLive.SolveGroundY(g => g < 50f ? g : g - 4f, 48f, 12f, 0.25f);
            Assert.That(y, Is.EqualTo(52f).Within(0.01f));
        }

        [Test]
        public void NoRootFallsBackWithoutThrowing()
        {
            float y = MapLabGameLive.SolveGroundY(g => g < 50f ? g : g + 20f, 55f, 12f, 0.25f);
            Assert.That(y, Is.InRange(43f, 67f));
        }
    }
}
